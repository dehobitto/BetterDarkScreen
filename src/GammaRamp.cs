using Microsoft.Win32;

namespace BetterDarkScreen;

/// <summary>
/// Hardware gamma ramp dimming. Works in exclusive-fullscreen games where overlays can't draw.
/// Windows limits how far a ramp may deviate unless GdiIcmGammaRange=256 is set (see Unlock).
/// </summary>
internal static class GammaRamp
{
    private static readonly Dictionary<string, double> _minFactor = new(StringComparer.OrdinalIgnoreCase);
    private static readonly Dictionary<string, double> _applied = new(StringComparer.OrdinalIgnoreCase);
    private static readonly Dictionary<string, ushort[]> _base = new(StringComparer.OrdinalIgnoreCase);
    private static readonly ushort[] _ramp = new ushort[768];
    private static string _cur = "";

    private const string IcmKey = @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\ICM";

    /// <summary>brightness 1.0 = normal, 0.1 = very dark. Returns factor actually applied.</summary>
    public static double Apply(string gdiDevice, double brightness, bool force = false)
    {
        brightness = Math.Clamp(brightness, 0.05, 1.0);
        if (_minFactor.TryGetValue(gdiDevice, out var min))
        {
            if (min >= 0.999) return 1.0;          // device refuses any ramp
            if (brightness < min) brightness = min;
        }
        if (!force && _applied.TryGetValue(gdiDevice, out var cur) && Math.Abs(cur - brightness) < 0.001) return brightness;

        IntPtr dc = Native.CreateDC(null, gdiDevice, null, IntPtr.Zero);
        if (dc == IntPtr.Zero) return 1.0;
        try
        {
            _cur = gdiDevice;
            CaptureBase(dc, gdiDevice);
            if (Set(dc, brightness)) { _applied[gdiDevice] = brightness; return brightness; }

            // Driver/OS refused: binary-search the darkest accepted value and cache it.
            double lo = brightness, hi = 1.0;
            for (int i = 0; i < 7; i++)
            {
                double mid = (lo + hi) / 2;
                if (Set(dc, mid)) hi = mid; else lo = mid;
            }
            Set(dc, hi);
            _minFactor[gdiDevice] = hi;
            _applied[gdiDevice] = hi;
            return hi;
        }
        finally { Native.DeleteDC(dc); }
    }

    /// <summary>Remember the current ramp (e.g. a color calibration) so we scale it instead of replacing it.</summary>
    private static void CaptureBase(IntPtr dc, string dev)
    {
        if (_base.ContainsKey(dev)) return;
        var b = new ushort[768];
        bool ok = Native.GetDeviceGammaRamp(dc, b);
        // Reject missing or already-dimmed (stale after crash) ramps; use linear instead.
        if (!ok || b[255] < 60000 || b[511] < 60000 || b[767] < 60000)
            for (int i = 0; i < 256; i++) b[i] = b[i + 256] = b[i + 512] = (ushort)(i * 257);
        _base[dev] = b;
    }

    private static bool Set(IntPtr dc, double f)
    {
        _base.TryGetValue(_cur, out var b);
        for (int i = 0; i < 768; i++)
        {
            int baseV = b != null ? b[i] : (i % 256) * 257;
            _ramp[i] = (ushort)Math.Clamp((int)Math.Round(baseV * f), 0, 65535);
        }
        return Native.SetDeviceGammaRamp(dc, _ramp);
    }

    public static void Reset(string gdiDevice)
    {
        if (!_applied.ContainsKey(gdiDevice)) return;
        IntPtr dc = Native.CreateDC(null, gdiDevice, null, IntPtr.Zero);
        if (dc != IntPtr.Zero) { _cur = gdiDevice; Set(dc, 1.0); Native.DeleteDC(dc); }
        _applied.Remove(gdiDevice);
    }

    public static void ResetAll()
    {
        foreach (var k in _applied.Keys.ToList()) Reset(k);
    }

    /// <summary>Windows' default cap (no registry unlock): ramps darker than about half brightness are refused.</summary>
    public const double DefaultFloor = 0.5;

    /// <summary>Darkest brightness factor this device accepted, if we've hit its limit.</summary>
    public static double? KnownFloor(string gdiDevice) => _minFactor.TryGetValue(gdiDevice, out var v) ? v : null;

    /// <summary>Can this display take a gamma ramp at all? Re-sets the current ramp (invisible).</summary>
    public static bool Probe(string gdiDevice)
    {
        IntPtr dc = Native.CreateDC(null, gdiDevice, null, IntPtr.Zero);
        if (dc == IntPtr.Zero) return false;
        try
        {
            var cur = new ushort[768];
            return Native.GetDeviceGammaRamp(dc, cur) && Native.SetDeviceGammaRamp(dc, cur);
        }
        finally { Native.DeleteDC(dc); }
    }

    public static bool IsApplied(string gdiDevice) => _applied.ContainsKey(gdiDevice);

    /// <summary>Forget cached limits (after unlocking the range or display changes).</summary>
    public static void ClearLimits() { _minFactor.Clear(); }

    /// <summary>Restore original ramps and forget everything (display topology changed).</summary>
    public static void Invalidate() { ResetAll(); _base.Clear(); _minFactor.Clear(); }

    public static bool IsRangeUnlocked()
    {
        try
        {
            using var k = Registry.LocalMachine.OpenSubKey(IcmKey);
            return k?.GetValue("GdiIcmGammaRange") is int v && v >= 256;
        }
        catch { return false; }
    }

    /// <summary>Asks UAC to set GdiIcmGammaRange=256. Takes effect after sign-out/reboot.</summary>
    public static bool UnlockRange()
    {
        try
        {
            var psi = new System.Diagnostics.ProcessStartInfo("reg.exe",
                $"add \"HKLM\\{IcmKey}\" /v GdiIcmGammaRange /t REG_DWORD /d 256 /f")
            {
                UseShellExecute = true, Verb = "runas",
                WindowStyle = System.Diagnostics.ProcessWindowStyle.Hidden,
            };
            using var p = System.Diagnostics.Process.Start(psi);
            p?.WaitForExit(15000);
            return p?.ExitCode == 0;
        }
        catch { return false; } // user cancelled UAC
    }
}
