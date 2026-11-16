using System.Diagnostics;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using UnityTimeTracker.Core;

namespace UnityTimeTracker.UI.Views;

public partial class SettingsView : UserControl, IView
{
    static readonly int[] IdleChoices = [1, 2, 5, 10, 15, 30];
    static readonly int[] DayStartChoices = [0, 2, 3, 4, 5, 6];

    DataSnapshot? snap;
    bool loading;

    public event Action? DataChanged;

    public SettingsView()
    {
        InitializeComponent();
        AddOptions(IdleOptions, IdleChoices, m => $"{m} min", OnIdle);
        AddOptions(DayStartOptions, DayStartChoices, h => $"{h:00}:00", OnDayStart);
        DataPath.Text = $"Everything is stored in {AppPaths.DataDir}";
    }

    void AddOptions(StackPanel host, int[] values, Func<int, string> label, RoutedEventHandler handler)
    {
        for (int i = 0; i < values.Length; i++)
        {
            var b = new RadioButton { Style = (Style)FindResource("SegButton"), Content = label(values[i]), Tag = values[i], MinWidth = 54 };
            if (i > 0) b.Margin = new Thickness(-1, 0, 0, 0);
            b.Checked += handler;
            host.Children.Add(b);
        }
    }

    public void Refresh(DataSnapshot d)
    {
        snap = d;
        loading = true;
        AutostartBox.IsChecked = Autostart.IsEnabled;
        PauseBox.IsChecked = App.Settings.Paused;
        IdeBox.IsChecked = App.Settings.TrackIde;
        foreach (RadioButton b in IdleOptions.Children) b.IsChecked = (int)b.Tag == App.Settings.IdleMinutes;
        foreach (RadioButton b in DayStartOptions.Children) b.IsChecked = (int)b.Tag == App.Settings.DayStartHour;
        loading = false;
        ShowAutostartInfo();
    }

    void ShowAutostartInfo()
    {
        var state = !Autostart.IsRegistered ? "Not registered yet."
            : Autostart.DisabledInTaskManager ? "Registered, currently turned off."
            : "Registered and turned on.";
        AutostartInfo.Text = $"{state} The same entry is listed in Task Manager under Startup apps as Unity Time Tracker, " +
                             "and can be switched on or off there too.\nCommand: " + (Autostart.RegisteredCommand ?? Autostart.Command);
    }

    void OnAutostart(object sender, RoutedEventArgs e)
    {
        try
        {
            Autostart.SetEnabled(AutostartBox.IsChecked == true);
            App.Settings.AutostartRegistered = true;
            App.Settings.Save();
        }
        catch (Exception ex)
        {
            Log.Error(ex);
            MessageBox.Show(Window.GetWindow(this), ex.Message, "Autostart", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        ShowAutostartInfo();
    }

    void OnPause(object sender, RoutedEventArgs e) => ((App)Application.Current).SetPaused(PauseBox.IsChecked == true);

    void OnIde(object sender, RoutedEventArgs e)
    {
        App.Settings.TrackIde = IdeBox.IsChecked == true;
        App.Settings.Save();
    }

    void OnIdle(object sender, RoutedEventArgs e)
    {
        if (loading) return;
        App.Settings.IdleMinutes = (int)((RadioButton)sender).Tag;
        App.Settings.Save();
    }

    void OnDayStart(object sender, RoutedEventArgs e)
    {
        if (loading) return;
        App.Settings.DayStartHour = (int)((RadioButton)sender).Tag;
        Clock.Offset = App.Settings.DayStartHour;
        App.Settings.Save();
        DataChanged?.Invoke();
    }

    void OnOpenData(object sender, RoutedEventArgs e) =>
        Process.Start(new ProcessStartInfo("explorer.exe", $"\"{AppPaths.DataDir}\"") { UseShellExecute = true });

    void OnImportHub(object sender, RoutedEventArgs e)
    {
        try
        {
            int n = UnityHub.Import(App.Db);
            DataStatus.Text = n == 0 ? "Unity Hub has no projects listed." : $"Imported {Fmt.Plural(n, "project", "projects")} from Unity Hub.";
            DataChanged?.Invoke();
        }
        catch (Exception ex)
        {
            Log.Error(ex);
            DataStatus.Text = "Couldn't read the Unity Hub project list.";
        }
    }

    void OnExportRaw(object sender, RoutedEventArgs e)
    {
        if (snap == null) return;
        var dialog = new SaveFileDialog { Title = "Export raw time", Filter = "CSV file|*.csv", FileName = "unity-time-raw.csv" };
        if (dialog.ShowDialog(Window.GetWindow(this)) != true) return;

        var sb = new StringBuilder("project,kind,scene,start,end,seconds\n");
        foreach (var s in snap.Spans)
        {
            var kind = s.Kind switch { SpanKind.Editor => "unity", SpanKind.Ide => "ide", _ => "open" };
            sb.AppendJoin(',', Csv.Escape(snap.Get(s.ProjectId)?.Title ?? "Unknown"), kind, Csv.Escape(s.Scene ?? ""),
                Fmt.Format(s.Start, "yyyy-MM-dd HH:mm:ss"), Fmt.Format(s.End, "yyyy-MM-dd HH:mm:ss"), ((long)s.Duration.TotalSeconds).ToString());
            sb.Append('\n');
        }
        try
        {
            File.WriteAllText(dialog.FileName, sb.ToString(), new UTF8Encoding(true));
            DataStatus.Text = $"Exported {Fmt.Num(snap.Spans.Count)} rows.";
        }
        catch (IOException ex) { DataStatus.Text = ex.Message; }
    }
}
