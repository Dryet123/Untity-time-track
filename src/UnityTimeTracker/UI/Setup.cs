using System.Diagnostics;
using System.Windows;
using UnityTimeTracker.Core;
using UnityTimeTracker.UI.Dialogs;

namespace UnityTimeTracker.UI;

// First-run install, updates and uninstall, driven from App startup and the Settings tab.
public static class Setup
{
    const string Title = "Unity Time Tracker";

    public enum Outcome { Continue, Exit }

    // Called when the exe runs from anywhere but the install folder, e.g. straight from Downloads.
    public static Outcome OfferInstall()
    {
        var mine = Installer.CurrentVersion;
        var installed = Installer.InstalledVersion;

        if (installed != null && installed == mine && Installer.IsSameAsInstalled())
        {
            Process.Start(new ProcessStartInfo(Installer.InstalledExe) { UseShellExecute = true });
            return Outcome.Exit;
        }

        var where = $"Location: {Installer.InstallDir}";
        InstallDialog dialog = installed == null
            ? new InstallDialog("Install Unity Time Tracker?",
                "It will be set up for your Windows user, no admin rights needed: added to the Start menu, started with Windows " +
                "in the tray, and listed in Settings > Apps so it can be removed like any other app.",
                where, "Install", "Just run it")
            : installed <= mine
                ? new InstallDialog(installed < mine ? $"Update to {Installer.Short(mine)}?" : "Replace the installed copy?",
                    $"Version {Installer.Short(installed)} is installed. Updating keeps all your tracked time and settings.",
                    where, "Update", "Just run it")
                : new InstallDialog("A newer version is installed",
                    $"Version {Installer.Short(installed)} is already installed, this file is {Installer.Short(mine)}.",
                    where, "Open installed", "Run this one");
        dialog.ShowDialog();

        switch (dialog.Choice)
        {
            case InstallChoice.Primary when installed != null && installed > mine:
                Process.Start(new ProcessStartInfo(Installer.InstalledExe) { UseShellExecute = true });
                return Outcome.Exit;
            case InstallChoice.Primary:
                try
                {
                    Installer.Install();
                    Process.Start(new ProcessStartInfo(Installer.InstalledExe) { UseShellExecute = true });
                }
                catch (Exception ex)
                {
                    Log.Error(ex);
                    MessageBox.Show($"Couldn't install.\n\n{ex.Message}", Title, MessageBoxButton.OK, MessageBoxImage.Warning);
                }
                return Outcome.Exit;
            case InstallChoice.Secondary:
                return Outcome.Continue;
            default:
                return Outcome.Exit;
        }
    }

    // Returns true when the app was removed and the caller should quit.
    public static bool Uninstall(Window? owner, bool fromRunningApp)
    {
        var confirm = Ask(owner, "Uninstall Unity Time Tracker?\n\nThis removes the app, its Start menu shortcut and autostart.",
            MessageBoxButton.YesNo, MessageBoxResult.No);
        if (confirm != MessageBoxResult.Yes) return false;

        var deleteData = Ask(owner, $"Also delete your tracked time, notes and settings?\n\nChoose No to keep them in {AppPaths.DataDir} " +
            "in case you install it again.", MessageBoxButton.YesNo, MessageBoxResult.No) == MessageBoxResult.Yes;

        try
        {
            if (!fromRunningApp) Installer.StopInstalledCopy();
            Installer.Unregister();
            if (!deleteData)
            {
                // A later reinstall should register autostart again.
                var settings = fromRunningApp ? App.Settings : AppSettings.Load();
                settings.AutostartRegistered = false;
                settings.Save();
            }
            Installer.ScheduleRemoval(deleteData);
        }
        catch (Exception ex)
        {
            Log.Error(ex);
            Ask(owner, $"Uninstall failed.\n\n{ex.Message}", MessageBoxButton.OK, MessageBoxResult.OK);
            return false;
        }

        if (!fromRunningApp) Ask(owner, "Unity Time Tracker was removed.", MessageBoxButton.OK, MessageBoxResult.OK);
        return true;
    }

    static MessageBoxResult Ask(Window? owner, string text, MessageBoxButton buttons, MessageBoxResult fallback) =>
        owner != null
            ? MessageBox.Show(owner, text, Title, buttons, MessageBoxImage.None, fallback)
            : MessageBox.Show(text, Title, buttons, MessageBoxImage.None, fallback);
}
