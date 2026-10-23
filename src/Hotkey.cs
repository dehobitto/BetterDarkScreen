namespace BetterDarkScreen;

/// <summary>Global hotkey as text, e.g. "Ctrl+Alt+PageDown". Empty = not set.</summary>
internal readonly record struct Hotkey(uint Mods, Keys Key)
{
    public bool IsEmpty => Key == Keys.None;

    public static Hotkey Parse(string? s)
    {
        if (string.IsNullOrWhiteSpace(s)) return default;
        uint mods = 0; Keys key = Keys.None;
        foreach (var raw in s.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            switch (raw.ToLowerInvariant())
            {
                case "ctrl": case "control": mods |= Native.MOD_CONTROL; break;
                case "alt": mods |= Native.MOD_ALT; break;
                case "shift": mods |= Native.MOD_SHIFT; break;
                case "win": mods |= Native.MOD_WIN; break;
                default: if (Enum.TryParse(raw, true, out Keys k)) key = k; break;
            }
        }
        return new Hotkey(mods, key);
    }

    /// <summary>Storage form.</summary>
    public override string ToString()
    {
        if (IsEmpty) return "";
        var parts = new List<string>();
        if ((Mods & Native.MOD_CONTROL) != 0) parts.Add("Ctrl");
        if ((Mods & Native.MOD_ALT) != 0) parts.Add("Alt");
        if ((Mods & Native.MOD_SHIFT) != 0) parts.Add("Shift");
        if ((Mods & Native.MOD_WIN) != 0) parts.Add("Win");
        parts.Add(Key.ToString());
        return string.Join("+", parts);
    }

    /// <summary>Human form: "Ctrl + Alt + PgDn".</summary>
    public string Display()
    {
        if (IsEmpty) return "";
        var parts = ToString().Split('+').ToList();
        parts[^1] = KeyName(Key);
        return string.Join(" + ", parts);
    }

    public static string KeyName(Keys k) => k switch
    {
        Keys.PageUp => "PgUp",
        Keys.PageDown => "PgDn",
        Keys.Insert => "Ins",
        Keys.Delete => "Del",
        Keys.Up => "↑", Keys.Down => "↓", Keys.Left => "←", Keys.Right => "→",
        >= Keys.D0 and <= Keys.D9 => ((char)('0' + (k - Keys.D0))).ToString(),
        >= Keys.NumPad0 and <= Keys.NumPad9 => "Num " + (k - Keys.NumPad0),
        Keys.Oemplus => "=", Keys.OemMinus => "-", Keys.Oemcomma => ",", Keys.OemPeriod => ".",
        Keys.OemOpenBrackets => "[", Keys.OemCloseBrackets => "]", Keys.OemSemicolon => ";",
        Keys.OemQuotes => "'", Keys.OemQuestion => "/", Keys.OemPipe => "\\", Keys.Oemtilde => "`",
        Keys.Add => "Num +", Keys.Subtract => "Num -", Keys.Multiply => "Num *", Keys.Divide => "Num /",
        Keys.Scroll => "Scroll Lock", Keys.Pause => "Pause", Keys.Snapshot => "PrtSc",
        _ => k.ToString(),
    };

    /// <summary>A global hotkey needs a modifier, except F-keys and a few special keys.</summary>
    public bool IsValid => !IsEmpty && (Mods != 0 || Key is >= Keys.F1 and <= Keys.F24 or Keys.Pause or Keys.Scroll);
}

/// <summary>Actions that can have a hotkey.</summary>
internal enum HotkeyAction { Darker = 1, Brighter = 2, Toggle = 3, Pause = 4, Panel = 5 }

internal static class HotkeyActions
{
    public static readonly HotkeyAction[] All = { HotkeyAction.Darker, HotkeyAction.Brighter, HotkeyAction.Toggle, HotkeyAction.Pause, HotkeyAction.Panel };

    public static string Title(HotkeyAction a) => a switch
    {
        HotkeyAction.Darker => "Dim more",
        HotkeyAction.Brighter => "Dim less",
        HotkeyAction.Toggle => "Turn dimming on / off",
        HotkeyAction.Pause => "Pause 1 hour / resume",
        _ => "Open quick panel",
    };

    public static string Get(Settings s, HotkeyAction a) => a switch
    {
        HotkeyAction.Darker => s.HkDarker,
        HotkeyAction.Brighter => s.HkBrighter,
        HotkeyAction.Toggle => s.HkToggle,
        HotkeyAction.Pause => s.HkPause,
        _ => s.HkPanel,
    };

    public static void Set(Settings s, HotkeyAction a, string v)
    {
        switch (a)
        {
            case HotkeyAction.Darker: s.HkDarker = v; break;
            case HotkeyAction.Brighter: s.HkBrighter = v; break;
            case HotkeyAction.Toggle: s.HkToggle = v; break;
            case HotkeyAction.Pause: s.HkPause = v; break;
            default: s.HkPanel = v; break;
        }
    }
}
