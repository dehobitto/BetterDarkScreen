namespace BetterDarkScreen;

/// <summary>
/// Click-through, topmost, layered black window covering one monitor.
/// DWM composites it with a single alpha value: near zero CPU/GPU cost.
/// </summary>
internal sealed class OverlayWindow : NativeWindow, IDisposable
{
    private Rectangle _bounds;
    private byte _alpha;
    private bool _visible;
    private bool _excludeFromCapture;

    public OverlayWindow(Rectangle bounds, bool excludeFromCapture)
    {
        _bounds = bounds;
        _excludeFromCapture = excludeFromCapture;
        var cp = new CreateParams
        {
            Caption = "BetterDarkScreen Overlay",
            X = bounds.X, Y = bounds.Y, Width = bounds.Width, Height = bounds.Height,
            Style = Native.WS_POPUP,
            ExStyle = Native.WS_EX_LAYERED | Native.WS_EX_TRANSPARENT | Native.WS_EX_TOPMOST
                    | Native.WS_EX_TOOLWINDOW | Native.WS_EX_NOACTIVATE,
        };
        CreateHandle(cp);
        Native.SetLayeredWindowAttributes(Handle, 0, 0, Native.LWA_ALPHA);
        ApplyCaptureAffinity();
    }

    public Rectangle Bounds => _bounds;
    public byte Alpha => _alpha;

    public bool ExcludeFromCapture
    {
        get => _excludeFromCapture;
        set { if (_excludeFromCapture != value) { _excludeFromCapture = value; ApplyCaptureAffinity(); } }
    }

    private void ApplyCaptureAffinity()
    {
        // Windows 10 2004+. Silently ignored on older builds.
        Native.SetWindowDisplayAffinity(Handle, _excludeFromCapture ? Native.WDA_EXCLUDEFROMCAPTURE : Native.WDA_NONE);
    }

    public void SetBounds(Rectangle b)
    {
        _bounds = b;
        Native.SetWindowPos(Handle, Native.HWND_TOPMOST, b.X, b.Y, b.Width, b.Height,
            Native.SWP_NOACTIVATE | Native.SWP_NOOWNERZORDER | Native.SWP_NOSENDCHANGING);
    }

    public void SetAlpha(byte alpha)
    {
        if (alpha == _alpha && _visible == alpha > 0) return;
        _alpha = alpha;
        if (alpha == 0)
        {
            if (_visible) { Native.ShowWindow(Handle, Native.SW_HIDE); _visible = false; }
            return;
        }
        Native.SetLayeredWindowAttributes(Handle, 0, alpha, Native.LWA_ALPHA);
        if (!_visible)
        {
            Native.SetWindowPos(Handle, Native.HWND_TOPMOST, _bounds.X, _bounds.Y, _bounds.Width, _bounds.Height,
                Native.SWP_NOACTIVATE | Native.SWP_SHOWWINDOW | Native.SWP_NOOWNERZORDER);
            _visible = true;
        }
    }

    /// <summary>Re-assert topmost z-order after another topmost window popped up.</summary>
    public void BringToTop()
    {
        if (!_visible) return;
        Native.SetWindowPos(Handle, Native.HWND_TOPMOST, 0, 0, 0, 0,
            Native.SWP_NOMOVE | Native.SWP_NOSIZE | Native.SWP_NOACTIVATE | Native.SWP_NOOWNERZORDER | Native.SWP_NOSENDCHANGING);
    }

    protected override void WndProc(ref Message m)
    {
        switch (m.Msg)
        {
            case Native.WM_ERASEBKGND:
                m.Result = 1;
                return;
            case Native.WM_PAINT:
                {
                    IntPtr hdc = Native.BeginPaint(m.HWnd, out var ps);
                    Native.FillRect(hdc, ref ps.rcPaint, Native.GetStockObject(Native.BLACK_BRUSH));
                    Native.EndPaint(m.HWnd, ref ps);
                    m.Result = IntPtr.Zero;
                    return;
                }
            case Native.WM_NCHITTEST:
                m.Result = Native.HTTRANSPARENT;
                return;
            case Native.WM_MOUSEACTIVATE:
                m.Result = Native.MA_NOACTIVATE;
                return;
            case Native.WM_DPICHANGED:
                // Ignore suggested rect; we always size to the monitor in physical pixels.
                m.Result = IntPtr.Zero;
                SetBounds(_bounds);
                return;
        }
        base.WndProc(ref m);
    }

    public void Dispose()
    {
        if (Handle != IntPtr.Zero) DestroyHandle();
    }
}
