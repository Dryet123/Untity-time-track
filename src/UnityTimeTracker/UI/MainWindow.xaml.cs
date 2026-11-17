using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
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
    readonly LogView log = new();
    readonly SettingsView settings = new();
    readonly DispatcherTimer timer = new() { Interval = TimeSpan.FromSeconds(10) };
    readonly RadioButton[] tabs;
    IView? current;
    string? shownSignature;

    public MainWindow()
    {
        InitializeComponent();
        tabs = [TabOverview, TabCalendar, TabProjects, TabLog, TabSettings];
        Width = App.Settings.WindowWidth;
        Height = App.Settings.WindowHeight;
        if (App.Settings.WindowMaximized) WindowState = WindowState.Maximized;

        overview.OpenProject += ShowProject;
        foreach (var view in new IView[] { overview, calendar, projects, log, settings })
            view.DataChanged += () => RefreshCurrent(force: true);

        timer.Tick += (_, _) => RefreshCurrent();
        IsVisibleChanged += (_, _) =>
        {
            if (IsVisible)
            {
                RefreshCurrent(force: true);
                timer.Start();
            }
            else timer.Stop();
        };

        App.Tracker.StatusChanged += _ => Dispatcher.BeginInvoke(() => ShowStatus());
        App.Tracker.ProjectsChanged += () => Dispatcher.BeginInvoke(() => RefreshCurrent(force: true));
        ((App)Application.Current).PausedChanged += () => RefreshCurrent(force: true);

        var last = tabs.FirstOrDefault(t => (string)t.Content == App.Settings.LastTab) ?? TabOverview;
        last.IsChecked = true;
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
            : sender == TabLog ? log
            : sender == TabSettings ? settings
            : overview;
        App.Settings.LastTab = (string)((RadioButton)sender).Content;
        Host.Content = current;
        RefreshCurrent(force: true);
    }

    void ShowProject(long id)
    {
        projects.Select(id);
        TabProjects.IsChecked = true;
    }

    DataSnapshot? lastSnap;

    // Timer refreshes skip redrawing when the data and the date haven't changed.
    public void RefreshCurrent(bool force = false)
    {
        if (!IsVisible) return;
        try
        {
            App.Tracker.Flush();
            var snap = DataSnapshot.Load(App.Db);
            var signature = $"{snap.Signature}|{Clock.Today}|{Clock.Offset}|{App.Settings.DailyGoalMinutes}";
            if (force || signature != shownSignature) current?.Refresh(snap);
            shownSignature = signature;
            lastSnap = snap;
            ShowStatus();
        }
        catch (Exception ex) { Log.Error(ex); }
    }

    void ShowStatus()
    {
        var s = App.Tracker.Status;
        var text = s.Describe();
        if (lastSnap != null && s.ProjectId is long pid && s.State is TrackerState.Active or TrackerState.Ide)
        {
            var session = lastSnap.WorkSessions(pid).LastOrDefault();
            if (session != null && DateTime.Now - session.End < Stats.SessionGap)
                text += $"   Session {Fmt.Dur(session.Work)}, since {Fmt.Time(session.Start)}";
        }
        StatusText.Text = text;
        StatusDot.Fill = Palette.Brush(s.State switch
        {
            TrackerState.Active or TrackerState.Ide => Palette.AccentColor,
            TrackerState.Paused => Palette.C(0xC48434),
            TrackerState.Open => Palette.C(0x8A8A8A),
            _ => Palette.C(0x555555),
        });

        if (lastSnap == null) return;
        var today = Stats.Sum(lastSnap.Of(null), Clock.Range(Clock.Today, Clock.Today.AddDays(1))).Work;
        var goal = App.Settings.DailyGoal;
        TodayText.Text = goal > TimeSpan.Zero
            ? $"Today {Fmt.Dur(today)} of {Fmt.Dur(goal)} ({Fmt.Pct(today / goal)})"
            : $"Today {Fmt.Dur(today)}";
        GoalBar.Visibility = goal > TimeSpan.Zero ? Visibility.Visible : Visibility.Collapsed;
        GoalBar.Fraction = goal > TimeSpan.Zero ? today / goal : 0;
    }

    // Ctrl+1..5 switch tabs, Ctrl+Left/Right move the period, Ctrl+Home jumps to the current one, F5 reloads.
    protected override void OnPreviewKeyDown(KeyEventArgs e)
    {
        base.OnPreviewKeyDown(e);
        if (e.Key == Key.F5)
        {
            RefreshCurrent(force: true);
            e.Handled = true;
            return;
        }
        if (Keyboard.Modifiers != ModifierKeys.Control || Keyboard.FocusedElement is TextBoxBase) return;

        int digit = e.Key - Key.D1;
        if (digit is >= 0 and < 5)
        {
            tabs[digit].IsChecked = true;
            e.Handled = true;
        }
        else if (current is IPeriodView pv && e.Key is Key.Left or Key.Right or Key.Home)
        {
            if (e.Key == Key.Home) pv.GoToCurrent();
            else pv.Shift(e.Key == Key.Left ? -1 : 1);
            e.Handled = true;
        }
    }

    public void SaveSize()
    {
        App.Settings.WindowMaximized = WindowState == WindowState.Maximized;
        if (WindowState != WindowState.Normal) return;
        App.Settings.WindowWidth = Width;
        App.Settings.WindowHeight = Height;
    }

    // The close button hides to the tray; exiting is done from the tray menu.
    protected override void OnClosing(CancelEventArgs e)
    {
        SaveSize();
        App.Settings.Save();
        e.Cancel = true;
        Hide();
        ((App)Application.Current).OnWindowHidden();
        base.OnClosing(e);
    }
}
