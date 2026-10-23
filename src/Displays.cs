using System.Runtime.InteropServices;

namespace BetterDarkScreen;

/// <summary>One physical display.</summary>
internal sealed record DisplayInfo(string Id, string GdiName, string FriendlyName, Rectangle Bounds, bool Primary, int Index, bool Internal)
{
    public string Label => string.IsNullOrWhiteSpace(FriendlyName) ? $"Display {Index}" : FriendlyName;
}

internal static class Displays
{
    /// <summary>Enumerates active displays. Id is stable (device path) when available.</summary>
    public static List<DisplayInfo> Enumerate()
    {
        var names = QueryFriendlyNames();
        var list = new List<DisplayInfo>();
        int i = 1;
        foreach (var s in Screen.AllScreens)
        {
            names.TryGetValue(s.DeviceName, out var info);
            string id = string.IsNullOrEmpty(info.path) ? s.DeviceName : info.path;
            list.Add(new DisplayInfo(id, s.DeviceName, info.name ?? "", s.Bounds, s.Primary, i++, info.isInternal));
        }
        // Keep primary first, then left-to-right.
        list.Sort((a, b) => a.Primary != b.Primary ? (a.Primary ? -1 : 1) : a.Bounds.X.CompareTo(b.Bounds.X));
        for (int k = 0; k < list.Count; k++) list[k] = list[k] with { Index = k + 1 };

        // Disambiguate identical model names.
        foreach (var g in list.GroupBy(d => d.Label).Where(g => g.Count() > 1))
            foreach (var d in g.ToList())
            {
                int idx = list.IndexOf(d);
                list[idx] = d with { FriendlyName = $"{d.Label} ({d.Index})" };
            }
        return list;
    }

    private static Dictionary<string, (string? name, string? path, bool isInternal)> QueryFriendlyNames()
    {
        var map = new Dictionary<string, (string?, string?, bool)>(StringComparer.OrdinalIgnoreCase);
        try
        {
            if (Native.GetDisplayConfigBufferSizes(Native.QDC_ONLY_ACTIVE_PATHS, out uint np, out uint nm) != 0) return map;
            var paths = new Native.DISPLAYCONFIG_PATH_INFO[np];
            var modes = new Native.DISPLAYCONFIG_MODE_INFO[nm];
            if (Native.QueryDisplayConfig(Native.QDC_ONLY_ACTIVE_PATHS, ref np, paths, ref nm, modes, IntPtr.Zero) != 0) return map;

            for (int i = 0; i < np; i++)
            {
                var p = paths[i];
                var src = new Native.DISPLAYCONFIG_SOURCE_DEVICE_NAME
                {
                    header = new() { type = 1, size = (uint)Marshal.SizeOf<Native.DISPLAYCONFIG_SOURCE_DEVICE_NAME>(), adapterId = p.sourceInfo.adapterId, id = p.sourceInfo.id }
                };
                if (Native.DisplayConfigGetDeviceInfo(ref src) != 0) continue;

                var tgt = new Native.DISPLAYCONFIG_TARGET_DEVICE_NAME
                {
                    header = new() { type = 2, size = (uint)Marshal.SizeOf<Native.DISPLAYCONFIG_TARGET_DEVICE_NAME>(), adapterId = p.targetInfo.adapterId, id = p.targetInfo.id }
                };
                if (Native.DisplayConfigGetDeviceInfo(ref tgt) != 0) continue;

                // INTERNAL, DISPLAYPORT_EMBEDDED, UDI_EMBEDDED
                bool isInternal = tgt.outputTechnology is 0x80000000 or 11 or 13;
                string? name = tgt.monitorFriendlyDeviceName;
                if (string.IsNullOrWhiteSpace(name) && isInternal) name = "Built-in display";
                map.TryAdd(src.viewGdiDeviceName, (name, tgt.monitorDevicePath, isInternal));
            }
        }
        catch { /* fall back to GDI names */ }
        return map;
    }
}
