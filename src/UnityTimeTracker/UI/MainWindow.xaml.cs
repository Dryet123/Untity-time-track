using System.ComponentModel;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;
using UnityTimeTracker.Core;
using UnityTimeTracker.UI.Views;

namespace UnityTimeTracker.UI;

public partial class MainWindow : Window
{
    readonly OverviewView overview = new();
    readonly CalendarView calendar = new();
    readonly ProjectsView projects = new();
    readonly DispatcherTimer timer = new() { Interval = TimeSpan.FromSeconds(10) };
    IView? current;

    public MainWindow()
    {
        InitializeComponent();
        Width = App.Settings.WindowWidth;
        Height = App.Settings.WindowHeight;

        overview.OpenProject += ShowProject;
        projects.DataChanged += RefreshCurrent;

        timer.Tick += (_, _) => RefreshCurrent();
        IsVisibleChanged += (_, _) =>
        {
            if (IsVisible)
            {
                RefreshCurrent();
                timer.Start();
            }
            else timer.Stop();
        };

        App.Tracker.StatusChanged += s => Dispatcher.BeginInvoke(() => ShowStatus(s));
        App.Tracker.ProjectsChanged += () => Dispatcher.BeginInvoke(RefreshCurrent);
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
        current = sender == TabCalendar ? calendar
            : sender == TabProjects ? projects
            : overview;
        Host.Content = current;
        RefreshCurrent();
    }

    void ShowProject(long id)
    {
        projects.Select(id);
        TabProjects.IsChecked = true;
    }

    public void RefreshCurrent()
    {
        if (!IsVisible) return;
        App.Tracker.Flush();
        var snap = DataSnapshot.Load(App.Db);
        current?.Refresh(snap);
        var today = Stats.Sum(snap.Of(null), Clock.Range(Clock.Today, Clock.Today.AddDays(1)));
        TodayText.Text = $"Today {Fmt.Dur(today.Work)}";
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

    protected override void OnClosing(CancelEventArgs e)
    {
        if (WindowState == WindowState.Normal)
        {
            App.Settings.WindowWidth = Width;
            App.Settings.WindowHeight = Height;
        }
        base.OnClosing(e);
    }
}
