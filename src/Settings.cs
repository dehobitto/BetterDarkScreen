using System.Text.Json;
using System.Text.Json.Serialization;

namespace BetterDarkScreen;

/// <summary>Global method. Custom = each monitor uses its own <see cref="MonitorMethod"/>.</summary>
public enum DimMode { Auto, Overlay, Gamma, Hardware, Custom }
/// <summary>Per-monitor method (used when global mode is Custom).</summary>
public enum MonitorMethod { Auto, Overlay, Gamma, Hardware }
public enum ScheduleKind { Always, Fixed, Sun }

public sealed class MonitorSettings
{
    public bool Enabled { get; set; } = true;
    /// <summary>Dim strength 0..95 (%).</summary>
    public int Level { get; set; } = 40;
    public MonitorMethod Method { get; set; } = MonitorMethod.Auto;
    /// <summary>Monitor's own brightness before we lowered it (hardware mode), -1 = unknown.</summary>
    public int HwNormal { get; set; } = -1;
    /// <summary>True while we hold the hardware brightness lowered (crash recovery).</summary>
    public bool HwDimmed { get; set; }
}

public sealed class Settings
{
    public bool Enabled { get; set; } = true;
    public DimMode Mode { get; set; } = DimMode.Auto;
    public Dictionary<string, MonitorSettings> Monitors { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public bool LinkMonitors { get; set; } = false;

    public ScheduleKind Schedule { get; set; } = ScheduleKind.Always;
    public string FixedStart { get; set; } = "21:00";
    public string FixedEnd { get; set; } = "07:00";
    public int FadeMinutes { get; set; } = 20;
    public double Latitude { get; set; } = 50.45;   // Kyiv default
    public double Longitude { get; set; } = 30.52;
    public int SunsetOffsetMin { get; set; } = 0;
    public int SunriseOffsetMin { get; set; } = 0;

    public bool HideFromCapture { get; set; } = true;
    public bool Hotkeys { get; set; } = true;
    public int HotkeyStep { get; set; } = 5;
    public string HkDarker { get; set; } = "Ctrl+Alt+PageDown";
    public string HkBrighter { get; set; } = "Ctrl+Alt+PageUp";
    public string HkToggle { get; set; } = "Ctrl+Alt+End";
    public string HkPause { get; set; } = "";
    public string HkPanel { get; set; } = "";
    public bool StartWithWindows { get; set; } = false;
    public bool FirstRun { get; set; } = true;

    public MonitorSettings For(string id)
    {
        if (!Monitors.TryGetValue(id, out var m)) Monitors[id] = m = new MonitorSettings();
        return m;
    }

    // ---- persistence ----
    public static string Dir => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "BetterDarkScreen");
    private static string FilePath => Path.Combine(Dir, "settings.json");

    public static Settings Load()
    {
        try
        {
            if (File.Exists(FilePath))
            {
                var s = JsonSerializer.Deserialize(File.ReadAllText(FilePath), SettingsJson.Default.Settings);
                if (s != null)
                {
                    s.Monitors = new(s.Monitors ?? new(), StringComparer.OrdinalIgnoreCase);
                    return s;
                }
            }
        }
        catch { /* corrupt file -> defaults */ }
        return new Settings();
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(Dir);
            string tmp = FilePath + ".tmp";
            File.WriteAllText(tmp, JsonSerializer.Serialize(this, SettingsJson.Default.Settings));
            File.Move(tmp, FilePath, true);
        }
        catch { }
    }
}

[JsonSourceGenerationOptions(WriteIndented = true, UseStringEnumConverter = true)]
[JsonSerializable(typeof(Settings))]
internal partial class SettingsJson : JsonSerializerContext { }
