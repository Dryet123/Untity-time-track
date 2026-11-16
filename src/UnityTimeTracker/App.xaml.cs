using System.Globalization;
using System.Windows;
using UnityTimeTracker.Core;
using UnityTimeTracker.UI;

namespace UnityTimeTracker;

public partial class App : Application
{
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

        window = new MainWindow();
        window.Closed += (_, _) => Shutdown();
        window.Show();
    }
}
