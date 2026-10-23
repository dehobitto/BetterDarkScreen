using System.Runtime.InteropServices;

namespace BetterDarkScreen;

internal static class Native
{
    // ---- window styles ----
    public const int WS_POPUP = unchecked((int)0x80000000);
    public const int WS_EX_TOPMOST = 0x00000008;
    public const int WS_EX_TRANSPARENT = 0x00000020;
    public const int WS_EX_TOOLWINDOW = 0x00000080;
    public const int WS_EX_LAYERED = 0x00080000;
    public const int WS_EX_NOACTIVATE = 0x08000000;
    public const int CS_DROPSHADOW = 0x00020000;

    public const int WM_ERASEBKGND = 0x0014;
    public const int WM_PAINT = 0x000F;
    public const int WM_NCHITTEST = 0x0084;
    public const int WM_MOUSEACTIVATE = 0x0021;
    public const int WM_DPICHANGED = 0x02E0;
    public const int WM_HOTKEY = 0x0312;
    public const int WM_DISPLAYCHANGE = 0x007E;
    public const int WM_POWERBROADCAST = 0x0218;
    public const int WM_WTSSESSION_CHANGE = 0x02B1;
    public const int WM_SETTINGCHANGE = 0x001A;
    public const int HTTRANSPARENT = -1;
    public const int MA_NOACTIVATE = 3;

    public const int LWA_ALPHA = 0x2;
    public const int SW_HIDE = 0;
    public const int SW_SHOWNOACTIVATE = 4;

    public static readonly IntPtr HWND_TOPMOST = new(-1);
    public const uint SWP_NOSIZE = 0x0001;
    public const uint SWP_NOMOVE = 0x0002;
    public const uint SWP_NOACTIVATE = 0x0010;
    public const uint SWP_SHOWWINDOW = 0x0040;
    public const uint SWP_NOOWNERZORDER = 0x0200;
    public const uint SWP_NOSENDCHANGING = 0x0400;

    public const uint WDA_NONE = 0x0;
    public const uint WDA_EXCLUDEFROMCAPTURE = 0x11;

    [DllImport("user32.dll")] public static extern bool SetLayeredWindowAttributes(IntPtr hwnd, uint crKey, byte bAlpha, uint dwFlags);
    [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);
    [DllImport("user32.dll")] public static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);
    [DllImport("user32.dll")] public static extern bool RedrawWindow(IntPtr hWnd, IntPtr rect, IntPtr rgn, uint flags);
    public const int WM_SETREDRAW = 0x000B;
    public const int WS_EX_COMPOSITED = 0x02000000;
    public const uint RDW_INVALIDATE = 0x1, RDW_ERASE = 0x4, RDW_ALLCHILDREN = 0x80, RDW_UPDATENOW = 0x100, RDW_FRAME = 0x400;
    [DllImport("user32.dll")] public static extern bool SetWindowPos(IntPtr hWnd, IntPtr after, int x, int y, int cx, int cy, uint flags);
    [DllImport("user32.dll")] public static extern bool SetWindowDisplayAffinity(IntPtr hWnd, uint affinity);
    [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr hWnd, out RECT rect);
    [DllImport("user32.dll")] public static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint flags);
    [DllImport("user32.dll")] public static extern IntPtr GetShellWindow();
    [DllImport("user32.dll")] public static extern IntPtr GetDesktopWindow();
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern int GetClassName(IntPtr hWnd, System.Text.StringBuilder sb, int max);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern bool GetMonitorInfo(IntPtr hMonitor, ref MONITORINFOEX info);
    [DllImport("user32.dll")] public static extern IntPtr BeginPaint(IntPtr hwnd, out PAINTSTRUCT ps);
    [DllImport("user32.dll")] public static extern bool EndPaint(IntPtr hwnd, ref PAINTSTRUCT ps);
    [DllImport("user32.dll")] public static extern int FillRect(IntPtr hdc, ref RECT rc, IntPtr brush);
    [DllImport("gdi32.dll")] public static extern IntPtr GetStockObject(int obj);
    public const int BLACK_BRUSH = 4;
    public const uint MONITOR_DEFAULTTONEAREST = 2;

    [DllImport("user32.dll")] public static extern bool RegisterHotKey(IntPtr hWnd, int id, uint mods, uint vk);
    [DllImport("user32.dll")] public static extern bool UnregisterHotKey(IntPtr hWnd, int id);
    public const uint MOD_ALT = 1, MOD_CONTROL = 2, MOD_SHIFT = 4, MOD_WIN = 8, MOD_NOREPEAT = 0x4000;

    [DllImport("user32.dll")] public static extern bool DestroyIcon(IntPtr h);

    // ---- WinEvent hook (foreground changes => keep overlay on top, detect fullscreen) ----
    public delegate void WinEventDelegate(IntPtr hook, uint evt, IntPtr hwnd, int idObject, int idChild, uint thread, uint time);
    [DllImport("user32.dll")] public static extern IntPtr SetWinEventHook(uint min, uint max, IntPtr hmod, WinEventDelegate proc, uint pid, uint tid, uint flags);
    [DllImport("user32.dll")] public static extern bool UnhookWinEvent(IntPtr hook);
    public const uint EVENT_SYSTEM_FOREGROUND = 0x0003;
    public const uint EVENT_SYSTEM_MINIMIZEEND = 0x0017;
    public const uint WINEVENT_OUTOFCONTEXT = 0;
    public const uint WINEVENT_SKIPOWNPROCESS = 2;

    // ---- shell ----
    [DllImport("shell32.dll")] public static extern int SHQueryUserNotificationState(out int state);
    public const int QUNS_BUSY = 2, QUNS_RUNNING_D3D_FULL_SCREEN = 3, QUNS_PRESENTATION_MODE = 4;

    [DllImport("wtsapi32.dll")] public static extern bool WTSRegisterSessionNotification(IntPtr hWnd, int flags);
    [DllImport("wtsapi32.dll")] public static extern bool WTSUnRegisterSessionNotification(IntPtr hWnd);

    // ---- gamma ----
    [DllImport("gdi32.dll", CharSet = CharSet.Unicode)] public static extern IntPtr CreateDC(string? driver, string device, string? output, IntPtr init);
    [DllImport("gdi32.dll")] public static extern bool DeleteDC(IntPtr hdc);
    [DllImport("gdi32.dll")] public static extern bool SetDeviceGammaRamp(IntPtr hdc, ushort[] ramp);
    [DllImport("gdi32.dll")] public static extern bool GetDeviceGammaRamp(IntPtr hdc, ushort[] ramp);

    // ---- DWM ----
    [DllImport("dwmapi.dll")] public static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);
    public const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;
    public const int DWMWA_WINDOW_CORNER_PREFERENCE = 33;
    public const int DWMWA_BORDER_COLOR = 34;
    public const int DWMWA_CAPTION_COLOR = 35;
    public const int DWMWCP_ROUND = 2;
    public const int DWMWCP_ROUNDSMALL = 3;

    [DllImport("dwmapi.dll")] public static extern int DwmExtendFrameIntoClientArea(IntPtr hwnd, ref MARGINS m);
    public const int DWMWA_NCRENDERING_POLICY = 2;
    [StructLayout(LayoutKind.Sequential)] public struct MARGINS { public int Left, Right, Top, Bottom; }
    public static bool IsWin11 => Environment.OSVersion.Version.Build >= 22000;

    [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr hWnd);
    [DllImport("user32.dll")] public static extern short GetKeyState(int vk);
    [DllImport("user32.dll")] public static extern IntPtr MonitorFromPoint(System.Drawing.Point pt, uint flags);

    // ---- DDC/CI (external monitors) ----
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    public struct PHYSICAL_MONITOR
    {
        public IntPtr hPhysicalMonitor;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string szPhysicalMonitorDescription;
    }
    [DllImport("dxva2.dll", SetLastError = true)] public static extern bool GetNumberOfPhysicalMonitorsFromHMONITOR(IntPtr hMon, out uint count);
    [DllImport("dxva2.dll", SetLastError = true)] public static extern bool GetPhysicalMonitorsFromHMONITOR(IntPtr hMon, uint count, [Out] PHYSICAL_MONITOR[] arr);
    [DllImport("dxva2.dll", SetLastError = true)] public static extern bool DestroyPhysicalMonitors(uint count, PHYSICAL_MONITOR[] arr);
    [DllImport("dxva2.dll", SetLastError = true)] public static extern bool GetVCPFeatureAndVCPFeatureReply(IntPtr h, byte code, IntPtr type, out uint cur, out uint max);
    [DllImport("dxva2.dll", SetLastError = true)] public static extern bool SetVCPFeature(IntPtr h, byte code, uint value);
    public const byte VCP_BRIGHTNESS = 0x10;

    // ---- built-in panel brightness ----
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    public static extern IntPtr CreateFile(string name, uint access, uint share, IntPtr sa, uint disp, uint flags, IntPtr tmpl);
    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern bool DeviceIoControl(IntPtr dev, uint code, byte[]? inBuf, int inSize, byte[]? outBuf, int outSize, out int returned, IntPtr ov);
    [DllImport("kernel32.dll")] public static extern bool CloseHandle(IntPtr h);
    public const uint GENERIC_READ = 0x80000000, GENERIC_WRITE = 0x40000000, FILE_SHARE_RW = 3, OPEN_EXISTING = 3;
    public static readonly IntPtr INVALID_HANDLE = new(-1);
    // CTL_CODE(FILE_DEVICE_VIDEO, 0x125..0x127, METHOD_BUFFERED, FILE_ANY_ACCESS)
    public const uint IOCTL_VIDEO_QUERY_SUPPORTED_BRIGHTNESS = 0x00230494;
    public const uint IOCTL_VIDEO_QUERY_DISPLAY_BRIGHTNESS = 0x00230498;
    public const uint IOCTL_VIDEO_SET_DISPLAY_BRIGHTNESS = 0x0023049C;

    // ---- memory ----
    [DllImport("kernel32.dll")] public static extern bool SetProcessWorkingSetSize(IntPtr proc, IntPtr min, IntPtr max);
    [DllImport("kernel32.dll")] public static extern IntPtr GetCurrentProcess();

    public static void TrimWorkingSet()
    {
        GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, true, true);
        GC.WaitForPendingFinalizers();
        SetProcessWorkingSetSize(GetCurrentProcess(), -1, -1);
    }

    // ---- display config (friendly monitor names) ----
    [DllImport("user32.dll")] public static extern int GetDisplayConfigBufferSizes(uint flags, out uint numPaths, out uint numModes);
    [DllImport("user32.dll")] public static extern int QueryDisplayConfig(uint flags, ref uint numPaths, [Out] DISPLAYCONFIG_PATH_INFO[] paths, ref uint numModes, [Out] DISPLAYCONFIG_MODE_INFO[] modes, IntPtr topology);
    [DllImport("user32.dll")] public static extern int DisplayConfigGetDeviceInfo(ref DISPLAYCONFIG_TARGET_DEVICE_NAME req);
    [DllImport("user32.dll")] public static extern int DisplayConfigGetDeviceInfo(ref DISPLAYCONFIG_SOURCE_DEVICE_NAME req);
    public const uint QDC_ONLY_ACTIVE_PATHS = 2;

    [StructLayout(LayoutKind.Sequential)]
    public struct RECT { public int Left, Top, Right, Bottom; }

    [StructLayout(LayoutKind.Sequential)]
    public struct PAINTSTRUCT
    {
        public IntPtr hdc; public int fErase; public RECT rcPaint; public int fRestore; public int fIncUpdate;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 32)] public byte[] rgbReserved;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    public struct MONITORINFOEX
    {
        public int cbSize; public RECT rcMonitor; public RECT rcWork; public uint dwFlags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string szDevice;
    }

    [StructLayout(LayoutKind.Sequential)] public struct LUID { public uint Low; public int High; }

    [StructLayout(LayoutKind.Sequential)]
    public struct DISPLAYCONFIG_PATH_SOURCE_INFO { public LUID adapterId; public uint id; public uint modeInfoIdx; public uint statusFlags; }

    [StructLayout(LayoutKind.Sequential)]
    public struct DISPLAYCONFIG_RATIONAL { public uint Numerator, Denominator; }

    [StructLayout(LayoutKind.Sequential)]
    public struct DISPLAYCONFIG_PATH_TARGET_INFO
    {
        public LUID adapterId; public uint id; public uint modeInfoIdx; public uint outputTechnology; public uint rotation;
        public uint scaling; public DISPLAYCONFIG_RATIONAL refreshRate; public uint scanLineOrdering; public int targetAvailable; public uint statusFlags;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct DISPLAYCONFIG_PATH_INFO { public DISPLAYCONFIG_PATH_SOURCE_INFO sourceInfo; public DISPLAYCONFIG_PATH_TARGET_INFO targetInfo; public uint flags; }

    // mode info is a 64-byte struct (with union); we never read it.
    [StructLayout(LayoutKind.Sequential, Size = 64)]
    public struct DISPLAYCONFIG_MODE_INFO { public uint infoType; public uint id; public LUID adapterId; }

    [StructLayout(LayoutKind.Sequential)]
    public struct DISPLAYCONFIG_DEVICE_INFO_HEADER { public uint type; public uint size; public LUID adapterId; public uint id; }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    public struct DISPLAYCONFIG_TARGET_DEVICE_NAME
    {
        public DISPLAYCONFIG_DEVICE_INFO_HEADER header;
        public uint flags; public uint outputTechnology; public ushort edidManufactureId; public ushort edidProductCodeId; public uint connectorInstance;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)] public string monitorFriendlyDeviceName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string monitorDevicePath;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    public struct DISPLAYCONFIG_SOURCE_DEVICE_NAME
    {
        public DISPLAYCONFIG_DEVICE_INFO_HEADER header;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string viewGdiDeviceName;
    }
}
