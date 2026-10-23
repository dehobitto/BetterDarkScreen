namespace BetterDarkScreen.Ui;

/// <summary>Tray icon: the brand monitor mark, monochrome to match the taskbar (no image files needed).</summary>
internal static class TrayIcons
{
    public enum State { Active, Idle, Off }

    public static Icon Create(State state)
    {
        int size = SystemInformation.SmallIconSize.Width;
        bool darkBar = Theme.TaskbarDark;
        var fg = darkBar ? Color.White : Color.FromArgb(28, 28, 28);
        return state switch
        {
            State.Active => Brand.MarkIcon(size, fg),
            // Waiting for schedule / paused: tertiary text color.
            State.Idle => Brand.MarkIcon(size, darkBar ? Color.FromArgb(0x9D, 0x9D, 0x9D) : Color.FromArgb(0x70, 0x70, 0x70)),
            _ => Brand.MarkIcon(size, Color.FromArgb(150, fg), slash: true),
        };
    }
}
