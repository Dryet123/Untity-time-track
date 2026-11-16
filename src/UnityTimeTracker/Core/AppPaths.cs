namespace UnityTimeTracker.Core;

public static class AppPaths
{
    public static string DataDir { get; } = Init();
    public static string Database => Path.Combine(DataDir, "tracker.db");
    public static string Settings => Path.Combine(DataDir, "settings.json");
    public static string Icons => Path.Combine(DataDir, "icons");

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
