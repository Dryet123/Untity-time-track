using System.Globalization;
using System.Windows;
using UnityTimeTracker.Core;
using UnityTimeTracker.UI;

namespace UnityTimeTracker;

public partial class App : Application
{
    public static Database Db { get; private set; } = null!;
    public static AppSettings Settings { get; private set; } = null!;
    public static Tracker Tracker { get; private set; } = null!;

    MainWindow? window;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        var culture = CultureInfo.GetCultureInfo("en-GB");
        CultureInfo.DefaultThreadCurrentCulture = culture;
        CultureInfo.DefaultThreadCurrentUICulture = culture;
        Thread.CurrentThread.CurrentCulture = culture;
        Thread.CurrentThread.CurrentUICulture = culture;
        DispatcherUnhandledException += (_, a) => { Log.Error(a.Exception); a.Handled = true; };

        Settings = AppSettings.Load();
        Clock.Offset = Settings.DayStartHour;
        Db = new Database(AppPaths.Database);
        try { UnityHub.Import(Db); }
        catch (Exception ex) { Log.Error(ex); }

        Tracker = new Tracker(Db, Settings);
        Tracker.Start();

        window = new MainWindow();
        window.Closed += (_, _) => ExitApp();
        window.Show();
    }

    void ExitApp()
    {
        Tracker.Dispose();
        Settings.Save();
        Shutdown();
    }

    protected override void OnSessionEnding(SessionEndingCancelEventArgs e)
    {
        Tracker?.Dispose();
        Settings?.Save();
        base.OnSessionEnding(e);
    }
}
