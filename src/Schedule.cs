namespace BetterDarkScreen;

/// <summary>Computes how strongly to dim (0..1 of configured level) at a given time.</summary>
internal static class Schedule
{
    public static bool TryParseTime(string s, out TimeSpan t)
    {
        t = default;
        var parts = s.Trim().Split(':', '.');
        if (parts.Length != 2 || !int.TryParse(parts[0], out int h) || !int.TryParse(parts[1], out int m)) return false;
        if (h < 0 || h > 23 || m < 0 || m > 59) return false;
        t = new TimeSpan(h, m, 0);
        return true;
    }

    /// <summary>Start/end of the dim window that begins on <paramref name="day"/>.</summary>
    public static (DateTime start, DateTime end)? Window(Settings s, DateTime day)
    {
        day = day.Date;
        switch (s.Schedule)
        {
            case ScheduleKind.Fixed:
            {
                if (!TryParseTime(s.FixedStart, out var a) || !TryParseTime(s.FixedEnd, out var b)) return null;
                var start = day + a;
                var end = day + b;
                if (end <= start) end = end.AddDays(1);
                return (start, end);
            }
            case ScheduleKind.Sun:
            {
                var set = SunCalc.Sunset(day, s.Latitude, s.Longitude);
                var rise = SunCalc.Sunrise(day.AddDays(1), s.Latitude, s.Longitude);
                if (set is null || rise is null)
                {
                    // Polar day/night: dim whole day if sun never rises.
                    return SunCalc.IsPolarNight(day, s.Latitude) ? (day, day.AddDays(1)) : null;
                }
                var start = set.Value.AddMinutes(s.SunsetOffsetMin);
                var end = rise.Value.AddMinutes(s.SunriseOffsetMin);
                if (end <= start) return null;
                return (start, end);
            }
            default:
                return null;
        }
    }

    /// <summary>0 = off, 1 = full configured level. Fades in after start and out before end.</summary>
    public static double Factor(Settings s, DateTime now)
    {
        if (s.Schedule == ScheduleKind.Always) return 1.0;
        double fade = Math.Max(0, s.FadeMinutes);
        for (int d = -1; d <= 0; d++)
        {
            var w = Window(s, now.AddDays(d));
            if (w is null) continue;
            var (start, end) = w.Value;
            if (now < start || now >= end) continue;
            if (fade <= 0) return 1.0;
            double span = (end - start).TotalMinutes;
            double f = Math.Min(fade, span / 2);
            double inF = (now - start).TotalMinutes / f;
            double outF = (end - now).TotalMinutes / f;
            return Math.Clamp(Math.Min(inF, outF), 0, 1);
        }
        return 0.0;
    }

    /// <summary>Is a fade in progress (needs faster ticks)?</summary>
    public static bool IsFading(Settings s, DateTime now)
    {
        double f = Factor(s, now);
        return f > 0 && f < 1;
    }

    /// <summary>Short human text: "Dimming 21:00 – 07:00" etc.</summary>
    public static string Describe(Settings s, DateTime now)
    {
        if (s.Schedule == ScheduleKind.Always) return "Always on";
        var cur = Window(s, now.AddDays(-1)) is { } y && now < y.end ? y : Window(s, now);
        if (cur is null) return s.Schedule == ScheduleKind.Sun ? "No sunset today" : "Invalid time";
        var (a, b) = cur.Value;
        string prefix = s.Schedule == ScheduleKind.Sun ? "Sunset" : "Scheduled";
        return $"{prefix} {a:HH:mm} – {b:HH:mm}";
    }
}

/// <summary>NOAA sunrise/sunset approximation (±1 min), no network needed.</summary>
internal static class SunCalc
{
    private const double Zenith = 90.833; // official, incl. refraction

    public static DateTime? Sunrise(DateTime date, double lat, double lon) => Calc(date, lat, lon, true);
    public static DateTime? Sunset(DateTime date, double lat, double lon) => Calc(date, lat, lon, false);

    public static bool IsPolarNight(DateTime date, double lat)
    {
        double decl = Declination(date.DayOfYear);
        double cosH = (Cos(Zenith) - Sin(lat) * Sin(decl)) / (Cos(lat) * Cos(decl));
        return cosH > 1;
    }

    private static double Declination(int n)
    {
        double g = 2 * Math.PI / 365.0 * (n - 1);
        return Rad2Deg(0.006918 - 0.399912 * Math.Cos(g) + 0.070257 * Math.Sin(g) - 0.006758 * Math.Cos(2 * g)
               + 0.000907 * Math.Sin(2 * g) - 0.002697 * Math.Cos(3 * g) + 0.00148 * Math.Sin(3 * g));
    }

    private static DateTime? Calc(DateTime date, double lat, double lon, bool rise)
    {
        int n = date.DayOfYear;
        double g = 2 * Math.PI / 365.0 * (n - 1 + 0.5);
        double eqTime = 229.18 * (0.000075 + 0.001868 * Math.Cos(g) - 0.032077 * Math.Sin(g)
                        - 0.014615 * Math.Cos(2 * g) - 0.040849 * Math.Sin(2 * g));
        double decl = Declination(n);
        double cosH = (Cos(Zenith) - Sin(lat) * Sin(decl)) / (Cos(lat) * Cos(decl));
        if (cosH > 1 || cosH < -1) return null;
        double ha = Rad2Deg(Math.Acos(cosH));
        double utcMin = 720 - 4 * (lon + (rise ? ha : -ha)) - eqTime;
        var utc = new DateTime(date.Year, date.Month, date.Day, 0, 0, 0, DateTimeKind.Utc).AddMinutes(utcMin);
        return utc.ToLocalTime();
    }

    private static double Sin(double d) => Math.Sin(d * Math.PI / 180);
    private static double Cos(double d) => Math.Cos(d * Math.PI / 180);
    private static double Rad2Deg(double r) => r * 180 / Math.PI;
}
