namespace BetterDarkScreen.Ui;

/// <summary>
/// Borderless popup that still gets the real DWM shadow and Win11 rounded corners:
/// it keeps a resize frame (so DWM treats it as a normal window) but removes the frame area.
/// </summary>
internal abstract class PopupForm : Form
{
    private const int WS_THICKFRAME = 0x00040000;
    private const int WM_NCCALCSIZE = 0x0083;
    private const int WM_NCHITTEST = 0x0084;
    private const int HTCLIENT = 1;

    protected PopupForm()
    {
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        TopMost = true;
        StartPosition = FormStartPosition.Manual;
        AutoScaleMode = AutoScaleMode.None;
        KeyPreview = true;
    }

    protected override CreateParams CreateParams
    {
        get
        {
            var cp = base.CreateParams;
            cp.ExStyle |= Native.WS_EX_TOOLWINDOW;
            if (Native.IsWin11) cp.Style |= WS_THICKFRAME;   // DWM shadow + rounded corners
            else cp.ClassStyle |= Native.CS_DROPSHADOW;      // Win10: classic shadow, square corners
            return cp;
        }
    }

    protected override void WndProc(ref Message m)
    {
        if (Native.IsWin11)
        {
            if (m.Msg == WM_NCCALCSIZE && m.WParam != IntPtr.Zero) { m.Result = IntPtr.Zero; return; } // no frame
            if (m.Msg == WM_NCHITTEST) { m.Result = HTCLIENT; return; }                                  // no resizing
        }
        base.WndProc(ref m);
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        Theme.ApplyPopupChrome(Handle, Theme.Current);
    }
}
