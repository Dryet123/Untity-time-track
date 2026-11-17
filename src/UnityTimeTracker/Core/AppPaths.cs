namespace UnityTimeTracker.Core;

public static class AppPaths
{
    public static string DataDir { get; } = Init();
    public static string Database => Path.Combine(DataDir, "tracker.db");
    public static string Settings => Path.Combine(DataDir, "settings.json");
    public static string Icons => Path.Combine(DataDir, "icons");
    public static bool IsCustom => !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("UTT_DATA_DIR"));

    // One app instance per data folder, so a test copy with UTT_DATA_DIR can run next to the real one.
    public static string SyncName(string kind) => $@"Local\UnityTimeTracker.{StableHash(DataDir.ToLowerInvariant()):x8}.{kind}";

    // string.GetHashCode is randomized per process, so names shared between processes need their own hash.
    static uint StableHash(string s)
    {
        uint h = 2166136261;
        foreach (char c in s) h = (h ^ c) * 16777619;
        return h;
    }

    public static void Signal(string kind)
    {
        try
        {
            using var ev = EventWaitHandle.OpenExisting(SyncName(kind));
            ev.Set();
        }
        catch (WaitHandleCannotBeOpenedException) { }
    }

    static string Init()
    {
        var dir = Environment.GetEnvironmentVariable("UTT_DATA_DIR");
        if (string.IsNullOrWhiteSpace(dir))
            dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "UnityTimeTracker");
        Directory.CreateDirectory(Path.Combine(dir, "icons"));
        return dir;
    }
}

public static class Log
{
    static readonly object gate = new();

    public static void Error(Exception ex)
    {
        try
        {
            lock (gate)
                File.AppendAllText(Path.Combine(AppPaths.DataDir, "errors.log"), $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} {ex}\n\n");
        }
        catch { }
    }
}
