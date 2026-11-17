using Microsoft.Win32;

namespace UnityTimeTracker.Core;

// Uses the same registry entries as Task Manager's "Startup apps", so it can be toggled from either place.
public static class Autostart
{
    const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    const string ApprovedKey = @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run";
    const string ValueName = "UnityTimeTracker";

    public static string Command => $"\"{Environment.ProcessPath}\" --tray";

    public static string? RegisteredCommand
    {
        get
        {
            using var run = Registry.CurrentUser.OpenSubKey(RunKey);
            return run?.GetValue(ValueName) as string;
        }
    }

    public static bool IsRegistered => RegisteredCommand != null;

    // Task Manager writes 02 (enabled) or 03 (disabled) followed by a timestamp.
    public static bool DisabledInTaskManager
    {
        get
        {
            using var approved = Registry.CurrentUser.OpenSubKey(ApprovedKey);
            return approved?.GetValue(ValueName) is byte[] { Length: > 0 } data && (data[0] & 1) == 1;
        }
    }

    public static bool IsEnabled => IsRegistered && !DisabledInTaskManager;

    public static void SetEnabled(bool enabled)
    {
        using (var run = Registry.CurrentUser.CreateSubKey(RunKey))
            run.SetValue(ValueName, Command);

        var data = new byte[12];
        data[0] = enabled ? (byte)2 : (byte)3;
        if (!enabled) BitConverter.GetBytes(DateTime.UtcNow.ToFileTimeUtc()).CopyTo(data, 4);
        using var approved = Registry.CurrentUser.CreateSubKey(ApprovedKey);
        approved.SetValue(ValueName, data, RegistryValueKind.Binary);
    }

    public static void Remove()
    {
        using (var run = Registry.CurrentUser.OpenSubKey(RunKey, writable: true))
            run?.DeleteValue(ValueName, throwOnMissingValue: false);
        using var approved = Registry.CurrentUser.OpenSubKey(ApprovedKey, writable: true);
        approved?.DeleteValue(ValueName, throwOnMissingValue: false);
    }

    // Keeps the entry pointing at this exe if the app was moved; leaves the on/off state alone.
    public static void SyncPath()
    {
        using var run = Registry.CurrentUser.OpenSubKey(RunKey, writable: true);
        if (run?.GetValue(ValueName) is string current && current != Command) run.SetValue(ValueName, Command);
    }
}
