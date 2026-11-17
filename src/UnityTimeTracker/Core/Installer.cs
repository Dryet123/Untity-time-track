using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using Microsoft.Win32;

namespace UnityTimeTracker.Core;

// Per-user install without admin rights: the exe copies itself to %LOCALAPPDATA%\Programs,
// adds a Start menu shortcut and an entry in Settings > Apps so it can be removed like any app.
public static class Installer
{
    const string UninstallKey = @"Software\Microsoft\Windows\CurrentVersion\Uninstall\UnityTimeTracker";
    const string AppName = "Unity Time Tracker";

    public static string InstallDir => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "UnityTimeTracker");
    public static string InstalledExe => Path.Combine(InstallDir, "UnityTimeTracker.exe");
    static string StartMenuLink => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs), AppName + ".lnk");

    public static Version CurrentVersion => Assembly.GetEntryAssembly()?.GetName().Version ?? new Version(0, 0);

    public static bool IsInstalledCopy =>
        string.Equals(Path.GetFullPath(Environment.ProcessPath ?? ""), InstalledExe, StringComparison.OrdinalIgnoreCase);

    public static Version? InstalledVersion
    {
        get
        {
            if (!File.Exists(InstalledExe)) return null;
            var text = FileVersionInfo.GetVersionInfo(InstalledExe).FileVersion;
            return Version.TryParse(text, out var v) ? v : new Version(0, 0);
        }
    }

    public static string Short(Version v) => v.ToString(3);

    public static bool IsSameAsInstalled()
    {
        var mine = new FileInfo(Environment.ProcessPath!);
        var installed = new FileInfo(InstalledExe);
        if (!installed.Exists || mine.Length != installed.Length) return false;
        using var a = mine.OpenRead();
        using var b = installed.OpenRead();
        return SHA256.HashData(a).AsSpan().SequenceEqual(SHA256.HashData(b));
    }

    public static void Install()
    {
        Directory.CreateDirectory(InstallDir);
        StopInstalledCopy();
        File.Copy(Environment.ProcessPath!, InstalledExe, overwrite: true);
        Register();
    }

    // Asks the running installed copy to quit and waits for it, so its exe can be replaced or removed.
    public static void StopInstalledCopy()
    {
        AppPaths.Signal("Exit");
        var deadline = DateTime.Now.AddSeconds(15);
        while (DateTime.Now < deadline && RunningInstalled().Any()) Thread.Sleep(200);
        foreach (var p in RunningInstalled())
        {
            try { p.Kill(); p.WaitForExit(3000); }
            catch (Exception ex) { Log.Error(ex); }
        }
    }

    static IEnumerable<Process> RunningInstalled()
    {
        int self = Environment.ProcessId;
        foreach (var p in Process.GetProcessesByName("UnityTimeTracker"))
        {
            string? path = null;
            try { path = p.MainModule?.FileName; }
            catch (Exception) { }
            if (p.Id != self && string.Equals(path, InstalledExe, StringComparison.OrdinalIgnoreCase)) yield return p;
            else p.Dispose();
        }
    }

    public static void Register()
    {
        using (var key = Registry.CurrentUser.CreateSubKey(UninstallKey))
        {
            key.SetValue("DisplayName", AppName);
            key.SetValue("DisplayVersion", Short(CurrentVersion));
            key.SetValue("DisplayIcon", InstalledExe);
            key.SetValue("InstallLocation", InstallDir);
            key.SetValue("UninstallString", $"\"{InstalledExe}\" --uninstall");
            key.SetValue("NoModify", 1, RegistryValueKind.DWord);
            key.SetValue("NoRepair", 1, RegistryValueKind.DWord);
            if (File.Exists(InstalledExe))
                key.SetValue("EstimatedSize", (int)(new FileInfo(InstalledExe).Length / 1024), RegistryValueKind.DWord);
        }
        CreateShortcut(StartMenuLink, InstalledExe);
    }

    // Repairs the shortcut and Apps entry when the app was put in place some other way (publish.ps1).
    public static void EnsureRegistered()
    {
        using var key = Registry.CurrentUser.OpenSubKey(UninstallKey);
        if (key?.GetValue("DisplayVersion") as string != Short(CurrentVersion) || !File.Exists(StartMenuLink)) Register();
    }

    public static void Unregister()
    {
        Registry.CurrentUser.DeleteSubKeyTree(UninstallKey, throwOnMissingSubKey: false);
        Autostart.Remove();
        try { File.Delete(StartMenuLink); }
        catch (IOException) { }
    }

    // The running exe can't delete itself, so a hidden PowerShell waits for this process to end and removes the folders.
    public static void ScheduleRemoval(bool includeData)
    {
        static string Quote(string s) => "'" + s.Replace("'", "''") + "'";
        var folders = includeData ? $"{Quote(InstallDir)}, {Quote(AppPaths.DataDir)}" : Quote(InstallDir);
        var script = $"Wait-Process -Id {Environment.ProcessId} -ErrorAction SilentlyContinue; Start-Sleep -Seconds 1; " +
                     $"foreach ($f in @({folders})) {{ Remove-Item -LiteralPath $f -Recurse -Force -ErrorAction SilentlyContinue }}";
        var encoded = Convert.ToBase64String(System.Text.Encoding.Unicode.GetBytes(script));
        Process.Start(new ProcessStartInfo("powershell.exe", $"-NoProfile -NonInteractive -WindowStyle Hidden -EncodedCommand {encoded}")
        {
            CreateNoWindow = true,
            UseShellExecute = false,
            WorkingDirectory = Path.GetTempPath(),
        });
    }

    static void CreateShortcut(string link, string target)
    {
        var type = Type.GetTypeFromProgID("WScript.Shell");
        if (type == null) return;
        object? shell = null, shortcut = null;
        try
        {
            shell = Activator.CreateInstance(type);
            shortcut = ((dynamic)shell!).CreateShortcut(link);
            dynamic s = shortcut!;
            s.TargetPath = target;
            s.WorkingDirectory = Path.GetDirectoryName(target);
            s.IconLocation = target + ",0";
            s.Description = "Tracks time spent in the Unity editor";
            s.Save();
        }
        catch (Exception ex) { Log.Error(ex); }
        finally
        {
            if (shortcut != null) Marshal.FinalReleaseComObject(shortcut);
            if (shell != null) Marshal.FinalReleaseComObject(shell);
        }
    }
}
