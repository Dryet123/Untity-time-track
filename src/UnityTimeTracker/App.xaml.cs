using System.Globalization;
using System.Windows;
using System.Windows.Threading;
using UnityTimeTracker.Core;
using UnityTimeTracker.UI;

namespace UnityTimeTracker;

public partial class App : Application
{
    const string InstanceName = @"Local\UnityTimeTracker.Instance";
    const string ShowSignalName = @"Local\UnityTimeTracker.Show";
    const string ExitSignalName = @"Local\UnityTimeTracker.Exit";

    public static Database Db { get; private set; } = null!;
    public static AppSettings Settings { get; private set; } = null!;
    public static Tracker Tracker { get; private set; } = null!;

    Mutex? instance;
    EventWaitHandle? showSignal, exitSignal;
    TrayIcon? tray;
    MainWindow? window;
    readonly DispatcherTimer trayTimer = new() { Interval = TimeSpan.FromSeconds(30) };
    bool exiting;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // "--exit" asks a running copy to quit cleanly (used by publish.ps1).
        bool exitRequest = e.Args.Contains("--exit", StringComparer.OrdinalIgnoreCase);
        instance = new Mutex(true, InstanceName, out bool first);
        if (!first || exitRequest)
        {
            Signal(exitRequest ? ExitSignalName : ShowSignalName);
            Shutdown();
            return;
        }

        var culture = CultureInfo.GetCultureInfo("en-GB");
        CultureInfo.DefaultThreadCurrentCulture = culture;
        CultureInfo.DefaultThreadCurrentUICulture = culture;
        Thread.CurrentThread.CurrentCulture = culture;
        Thread.CurrentThread.CurrentUICulture = culture;
        DispatcherUnhandledException += (_, a) =>
        {
            Log.Error(a.Exception);
            a.Handled = true;
        };

        Settings = AppSettings.Load();
        Clock.Offset = Settings.DayStartHour;
        Db = new Database(AppPaths.Database);
        try { UnityHub.Import(Db); }
        catch (Exception ex) { Log.Error(ex); }
        SetupAutostart();

        Tracker = new Tracker(Db, Settings);
        Tracker.StatusChanged += _ => Dispatcher.BeginInvoke(UpdateTray);
        Tracker.Start();

        tray = new TrayIcon();
        tray.OpenRequested += ShowMain;
        tray.PauseToggled += () => SetPaused(!Settings.Paused);
        tray.ExitRequested += ExitApp;
        trayTimer.Tick += (_, _) => UpdateTray();
        trayTimer.Start();
        UpdateTray();

        showSignal = Listen(ShowSignalName, ShowMain);
        exitSignal = Listen(ExitSignalName, ExitApp);

        if (!e.Args.Contains("--tray", StringComparer.OrdinalIgnoreCase)) ShowMain();
    }

    static void Signal(string name)
    {
        try
        {
            using var ev = EventWaitHandle.OpenExisting(name);
            ev.Set();
        }
        catch (WaitHandleCannotBeOpenedException) { }
    }

    EventWaitHandle Listen(string name, Action action)
    {
        var ev = new EventWaitHandle(false, EventResetMode.AutoReset, name);
        ThreadPool.RegisterWaitForSingleObject(ev, (_, _) => Dispatcher.BeginInvoke(action), null, Timeout.Infinite, false);
        return ev;
    }

    static void SetupAutostart()
    {
#if !DEBUG
        try
        {
            if (!Settings.AutostartRegistered)
            {
                Autostart.SetEnabled(true);
                Settings.AutostartRegistered = true;
                Settings.Save();
            }
            else
            {
                Autostart.SyncPath();
            }
        }
        catch (Exception ex) { Log.Error(ex); }
#endif
    }

    public void ShowMain()
    {
        if (exiting) return;
        window ??= new MainWindow();
        window.Show();
        if (window.WindowState == WindowState.Minimized) window.WindowState = WindowState.Normal;
        window.Activate();
    }

    public void OnWindowHidden()
    {
        if (Settings.TrayHintShown) return;
        Settings.TrayHintShown = true;
        Settings.Save();
        tray?.Notify("Still tracking", "Unity Time Tracker keeps running in the tray. Right-click the icon to exit.");
    }

    public void SetPaused(bool paused)
    {
        Settings.Paused = paused;
        Settings.Save();
        UpdateTray();
    }

    void UpdateTray()
    {
        if (tray == null) return;
        var today = TimeSpan.Zero;
        try { today = Db.WorkBetween(Clock.Start(Clock.Today).ToUniversalTime(), DateTime.UtcNow); }
        catch (Exception ex) { Log.Error(ex); }
        tray.Update(Tracker.Status, today, Settings.Paused);
    }

    public void ExitApp()
    {
        if (exiting) return;
        exiting = true;
        trayTimer.Stop();
        window?.SaveSize();
        Tracker.Dispose();
        Settings.Save();
        tray?.Dispose();
        Shutdown();
    }

    protected override void OnSessionEnding(SessionEndingCancelEventArgs e)
    {
        Tracker?.Dispose();
        Settings?.Save();
        base.OnSessionEnding(e);
    }

    protected override void OnExit(ExitEventArgs e)
    {
        tray?.Dispose();
        showSignal?.Dispose();
        exitSignal?.Dispose();
        base.OnExit(e);
    }
}
