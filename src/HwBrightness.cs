namespace BetterDarkScreen;

/// <summary>
/// Monitor's own backlight ("hardware" / natural dimming):
/// DDC/CI for external monitors, the LCD brightness IOCTL for built-in panels.
/// DDC is slow (~50 ms per call) so everything runs on one background thread; requests are coalesced.
/// </summary>
internal sealed class HwBrightness : IDisposable
{
    public sealed record DisplaySpec(string Id, string GdiName, bool Internal, int SavedNormal, bool SavedDimmed);

    private sealed class Dev
    {
        public required string Id;
        public required string Gdi;
        public bool Internal;
        public IntPtr Phys;                 // DDC handle (one per display; first physical monitor)
        public Native.PHYSICAL_MONITOR[]? PhysArr;
        public byte[]? LcdLevels;           // built-in panel levels
        public bool? Supported;
        public uint Max = 100;
        public int Normal = -1;             // brightness to return to
        public bool Dimmed;                 // we currently hold it lowered
        public double WantDim;              // 0..0.95
        public int LastSet = -1;
    }

    private readonly object _lock = new();
    private readonly Dictionary<string, Dev> _devs = new(StringComparer.OrdinalIgnoreCase);
    private List<DisplaySpec>? _pendingSpecs;
    private readonly AutoResetEvent _wake = new(false);
    private readonly Thread _thread;
    private readonly SynchronizationContext _ui;
    private volatile bool _quit;

    /// <summary>(id, supported) after probing. Raised on UI thread.</summary>
    public event Action<string, bool>? Probed;
    /// <summary>(id, normal, dimmed) state to persist for crash recovery. Raised on UI thread.</summary>
    public event Action<string, int, bool>? StateChanged;

    public HwBrightness()
    {
        _ui = SynchronizationContext.Current ?? new SynchronizationContext();
        _thread = new Thread(Run) { IsBackground = true, Name = "HwBrightness", Priority = ThreadPriority.BelowNormal };
        _thread.Start();
    }

    public bool? IsSupported(string id) { lock (_lock) return _devs.TryGetValue(id, out var d) ? d.Supported : null; }

    /// <summary>Replace display set (after topology change). Re-probes; keeps normal/dimmed state per id.</summary>
    public void SetDisplays(IEnumerable<DisplaySpec> specs)
    {
        lock (_lock) _pendingSpecs = specs.ToList();
        _wake.Set();
    }

    public void SetDim(string id, double dim)
    {
        lock (_lock)
        {
            if (!_devs.TryGetValue(id, out var d)) return;
            if (Math.Abs(d.WantDim - dim) < 0.0001) return;
            d.WantDim = dim;
        }
        _wake.Set();
    }

    // ------------------------------------------------------------ worker

    private void Run()
    {
        while (!_quit)
        {
            _wake.WaitOne();
            List<DisplaySpec>? specs;
            lock (_lock) { specs = _pendingSpecs; _pendingSpecs = null; }
            if (specs != null) Reprobe(specs);

            // Apply until nothing changes (coalesces fast slider moves).
            bool again = true;
            while (again && !_quit)
            {
                again = false;
                List<Dev> list;
                lock (_lock) list = _devs.Values.ToList();
                foreach (var d in list)
                    if (Step(d)) { again = true; Thread.Sleep(60); }
            }
        }
        RestoreAll();
    }

    private void Reprobe(List<DisplaySpec> specs)
    {
        Dictionary<string, Dev> old;
        lock (_lock) { old = new(_devs, StringComparer.OrdinalIgnoreCase); _devs.Clear(); }

        foreach (var sp in specs)
        {
            old.TryGetValue(sp.Id, out var prev);
            var d = new Dev { Id = sp.Id, Gdi = sp.GdiName, Internal = sp.Internal };
            if (prev != null) { d.Normal = prev.Normal; d.Dimmed = prev.Dimmed; d.WantDim = prev.WantDim; }
            else if (sp.SavedDimmed && sp.SavedNormal >= 0) { d.Normal = sp.SavedNormal; d.Dimmed = true; } // crash recovery

            d.Supported = Open(d);
            lock (_lock) _devs[d.Id] = d;
            bool sup = d.Supported == true;
            _ui.Post(_ => Probed?.Invoke(sp.Id, sup), null);
        }
        foreach (var o in old.Values) Close(o);
    }

    private static bool Open(Dev d)
    {
        // Built-in laptop panel
        if (d.Internal && OpenLcd(d)) return true;

        IntPtr hmon = HMonitorOf(d.Gdi);
        if (hmon == IntPtr.Zero) return false;
        try
        {
            if (!Native.GetNumberOfPhysicalMonitorsFromHMONITOR(hmon, out uint n) || n == 0) return false;
            var arr = new Native.PHYSICAL_MONITOR[n];
            if (!Native.GetPhysicalMonitorsFromHMONITOR(hmon, n, arr)) return false;
            d.PhysArr = arr;
            d.Phys = arr[0].hPhysicalMonitor;
            for (int i = 0; i < 3; i++) // DDC is flaky; retry
            {
                if (Native.GetVCPFeatureAndVCPFeatureReply(d.Phys, Native.VCP_BRIGHTNESS, IntPtr.Zero, out uint cur, out uint max) && max > 0)
                {
                    d.Max = max;
                    if (!d.Dimmed) d.Normal = (int)cur;
                    return true;
                }
                Thread.Sleep(40);
            }
        }
        catch { }
        return false;
    }

    private static bool OpenLcd(Dev d)
    {
        IntPtr h = Native.CreateFile(@"\\.\LCD", Native.GENERIC_READ | Native.GENERIC_WRITE, Native.FILE_SHARE_RW, IntPtr.Zero, Native.OPEN_EXISTING, 0, IntPtr.Zero);
        if (h == Native.INVALID_HANDLE || h == IntPtr.Zero) return false;
        try
        {
            var levels = new byte[256];
            if (!Native.DeviceIoControl(h, Native.IOCTL_VIDEO_QUERY_SUPPORTED_BRIGHTNESS, null, 0, levels, levels.Length, out int n, IntPtr.Zero) || n == 0)
                return false;
            d.LcdLevels = levels.Take(n).Distinct().OrderBy(b => b).ToArray();
            var cur = new byte[3];
            if (Native.DeviceIoControl(h, Native.IOCTL_VIDEO_QUERY_DISPLAY_BRIGHTNESS, null, 0, cur, 3, out _, IntPtr.Zero))
            {
                if (!d.Dimmed) d.Normal = SystemInformation.PowerStatus.PowerLineStatus == PowerLineStatus.Offline ? cur[2] : cur[1];
            }
            d.Max = 100;
            return true;
        }
        finally { Native.CloseHandle(h); }
    }

    private static IntPtr HMonitorOf(string gdi)
    {
        foreach (var s in Screen.AllScreens)
            if (string.Equals(s.DeviceName, gdi, StringComparison.OrdinalIgnoreCase))
                return Native.MonitorFromPoint(new System.Drawing.Point(s.Bounds.X + s.Bounds.Width / 2, s.Bounds.Y + s.Bounds.Height / 2), Native.MONITOR_DEFAULTTONEAREST);
        return IntPtr.Zero;
    }

    /// <summary>Moves one device toward its wanted level. Returns true if it wrote something.</summary>
    private bool Step(Dev d)
    {
        if (d.Supported != true) return false;
        double dim;
        lock (_lock) dim = d.WantDim;

        if (dim <= 0.0005)
        {
            if (!d.Dimmed) return false;
            if (!Write(d, d.Normal)) return false;
            d.Dimmed = false;
            Report(d);
            return true;
        }

        if (!d.Dimmed)
        {
            // Capture the user's current brightness right before lowering it.
            int cur = Read(d);
            if (cur >= 0) d.Normal = cur;
            if (d.Normal < 0) d.Normal = (int)d.Max;
            d.Dimmed = true;
            Report(d);
        }
        int target = (int)Math.Round(d.Normal * (1 - dim));
        target = Math.Clamp(target, 0, (int)d.Max);
        if (target == d.LastSet) return false;
        return Write(d, target);
    }

    private void Report(Dev d)
    {
        string id = d.Id; int n = d.Normal; bool dm = d.Dimmed;
        _ui.Post(_ => StateChanged?.Invoke(id, n, dm), null);
    }

    private static int Read(Dev d)
    {
        if (d.LcdLevels != null)
        {
            IntPtr h = Native.CreateFile(@"\\.\LCD", Native.GENERIC_READ | Native.GENERIC_WRITE, Native.FILE_SHARE_RW, IntPtr.Zero, Native.OPEN_EXISTING, 0, IntPtr.Zero);
            if (h == Native.INVALID_HANDLE) return -1;
            try
            {
                var cur = new byte[3];
                return Native.DeviceIoControl(h, Native.IOCTL_VIDEO_QUERY_DISPLAY_BRIGHTNESS, null, 0, cur, 3, out _, IntPtr.Zero)
                    ? (SystemInformation.PowerStatus.PowerLineStatus == PowerLineStatus.Offline ? cur[2] : cur[1]) : -1;
            }
            finally { Native.CloseHandle(h); }
        }
        for (int i = 0; i < 2; i++)
        {
            if (Native.GetVCPFeatureAndVCPFeatureReply(d.Phys, Native.VCP_BRIGHTNESS, IntPtr.Zero, out uint cur, out _)) return (int)cur;
            Thread.Sleep(40);
        }
        return -1;
    }

    private static bool Write(Dev d, int value)
    {
        if (value < 0) return false;
        bool ok;
        if (d.LcdLevels != null)
        {
            // Snap to a supported level.
            byte v = d.LcdLevels.OrderBy(l => Math.Abs(l - value)).First();
            IntPtr h = Native.CreateFile(@"\\.\LCD", Native.GENERIC_READ | Native.GENERIC_WRITE, Native.FILE_SHARE_RW, IntPtr.Zero, Native.OPEN_EXISTING, 0, IntPtr.Zero);
            if (h == Native.INVALID_HANDLE) return false;
            try
            {
                var buf = new byte[] { 3, v, v }; // DISPLAYPOLICY_BOTH, AC, DC
                ok = Native.DeviceIoControl(h, Native.IOCTL_VIDEO_SET_DISPLAY_BRIGHTNESS, buf, 3, null, 0, out _, IntPtr.Zero);
            }
            finally { Native.CloseHandle(h); }
        }
        else
        {
            ok = Native.SetVCPFeature(d.Phys, Native.VCP_BRIGHTNESS, (uint)value);
            if (!ok) { Thread.Sleep(50); ok = Native.SetVCPFeature(d.Phys, Native.VCP_BRIGHTNESS, (uint)value); }
        }
        if (ok) d.LastSet = value;
        return ok;
    }

    private void RestoreAll()
    {
        List<Dev> list;
        lock (_lock) list = _devs.Values.ToList();
        foreach (var d in list)
        {
            if (d.Supported == true && d.Dimmed && Write(d, d.Normal)) d.Dimmed = false;
            Close(d);
        }
    }

    private static void Close(Dev d)
    {
        if (d.PhysArr != null) { try { Native.DestroyPhysicalMonitors((uint)d.PhysArr.Length, d.PhysArr); } catch { } }
        d.PhysArr = null; d.Phys = IntPtr.Zero;
    }

    /// <summary>Restores normal brightness on every monitor and stops the thread (blocks up to 3 s).</summary>
    public void Dispose()
    {
        _quit = true;
        _wake.Set();
        _thread.Join(3000);
        _wake.Dispose();
    }
}
