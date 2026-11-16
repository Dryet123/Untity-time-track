using System.Globalization;

namespace UnityTimeTracker.Core;

public static class Fmt
{
    static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    public static string Dur(TimeSpan t)
    {
        if (t <= TimeSpan.Zero) return "0m";
        if (t < TimeSpan.FromMinutes(1)) return "<1m";
        int h = (int)t.TotalHours, m = t.Minutes;
        return h == 0 ? $"{m}m" : $"{h}h {m:00}m";
    }

    public static string Hours(double hours) => Dur(TimeSpan.FromHours(hours));

    public static string Pct(double fraction) => double.IsFinite(fraction) ? $"{fraction * 100:0}%" : "-";

    public static string Day(DateOnly d) => d.ToString("ddd, d MMM yyyy", Inv);
    public static string DayShort(DateOnly d) => d.ToString("d MMM", Inv);
    public static string DayLong(DateOnly d) => d.ToString("dddd, d MMMM yyyy", Inv);
    public static string Month(DateOnly d) => d.ToString("MMMM yyyy", Inv);
    public static string Time(DateTime t) => t.ToString("HH:mm", Inv);
    public static string Format(DateOnly d, string format) => d.ToString(format, Inv);
    public static string Format(DateTime t, string format) => t.ToString(format, Inv);

    public static string ClockTime(TimeSpan sinceMidnight)
    {
        var t = TimeSpan.FromMinutes(Math.Round(sinceMidnight.TotalMinutes) % (24 * 60));
        return $"{(int)t.TotalHours:00}:{t.Minutes:00}";
    }

    public static string Ago(DateTime t)
    {
        var d = DateTime.Now - t;
        if (d < TimeSpan.FromMinutes(1)) return "just now";
        if (d < TimeSpan.FromHours(1)) return $"{(int)d.TotalMinutes} min ago";
        int days = (DateTime.Today - t.Date).Days;
        if (days == 0) return $"{(int)d.TotalHours} h ago";
        if (days == 1) return "yesterday";
        if (days < 30) return $"{days} days ago";
        return t.ToString("d MMM yyyy", Inv);
    }

    public static string Bytes(long b) => b switch
    {
        < 1024 => $"{b} B",
        < 1024 * 1024 => $"{b / 1024.0:0.#} KB",
        < 1024L * 1024 * 1024 => $"{b / 1048576.0:0.#} MB",
        _ => $"{b / 1073741824.0:0.##} GB",
    };

    public static string Num(long n) => n.ToString("#,0", Inv);

    public static string Plural(int n, string one, string many) => n == 1 ? $"1 {one}" : $"{n} {many}";
}
