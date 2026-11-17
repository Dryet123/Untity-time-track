namespace UnityTimeTracker.Core;

// One database copy a day in DataDir\backups, keeping the newest two weeks.
public static class Backup
{
    const int Keep = 14;

    public static string Folder => Path.Combine(AppPaths.DataDir, "backups");

    public static void RunIfDue(Database db, AppSettings settings)
    {
        if (DateTime.Now - settings.LastBackup < TimeSpan.FromDays(1)) return;
        try
        {
            Directory.CreateDirectory(Folder);
            var file = Path.Combine(Folder, $"tracker-{DateTime.Now:yyyy-MM-dd-HHmm}.db");
            if (!File.Exists(file)) db.BackupTo(file);
            settings.LastBackup = DateTime.Now;
            settings.Save();

            foreach (var old in new DirectoryInfo(Folder).GetFiles("tracker-*.db").OrderByDescending(f => f.Name).Skip(Keep))
                old.Delete();
        }
        catch (Exception ex) { Log.Error(ex); }
    }

    public static (int Count, DateTime? Newest) Info()
    {
        if (!Directory.Exists(Folder)) return (0, null);
        var files = new DirectoryInfo(Folder).GetFiles("tracker-*.db");
        return (files.Length, files.Length > 0 ? files.Max(f => f.LastWriteTime) : null);
    }
}
