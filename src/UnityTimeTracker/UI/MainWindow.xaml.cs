using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using UnityTimeTracker.Core;

namespace UnityTimeTracker.UI;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        App.Tracker.StatusChanged += s => Dispatcher.BeginInvoke(() => ShowStatus(s));
        ShowStatus(App.Tracker.Status);
        TabOverview.IsChecked = true;
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        Native.UseDarkTitleBar(new WindowInteropHelper(this).Handle);
    }

    void OnTab(object sender, RoutedEventArgs e)
    {
    }

    void ShowStatus(TrackerStatus s)
    {
        StatusText.Text = s.Describe();
        StatusDot.Fill = Palette.Brush(s.State switch
        {
            TrackerState.Active or TrackerState.Ide => Palette.AccentColor,
            TrackerState.Paused => Palette.C(0xC48434),
            TrackerState.Open => Palette.C(0x8A8A8A),
            _ => Palette.C(0x555555),
        });
    }
}
