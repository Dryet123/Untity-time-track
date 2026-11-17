using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Microsoft.Win32;
using UnityTimeTracker.Core;
using UnityTimeTracker.UI.Controls;

namespace UnityTimeTracker.UI.Views;

public sealed class ProjectRow(long id) : INotifyPropertyChanged
{
    Project project = null!;
    string subtitle = "";

    public long Id { get; } = id;

    public Project Project
    {
        get => project;
        set
        {
            project = value;
            Changed(nameof(Project));
            Changed(nameof(Title));
            Changed(nameof(Opacity));
        }
    }

    public string Title => project.Title;
    public double Opacity => project.Hidden ? 0.5 : 1;

    public string Subtitle
    {
        get => subtitle;
        set
        {
            if (subtitle == value) return;
            subtitle = value;
            Changed(nameof(Subtitle));
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    void Changed(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

public partial class ProjectsView : UserControl, IView, IPeriodView
{
    readonly ObservableCollection<ProjectRow> rows = [];
    DataSnapshot? snap;
    long? selectedId, shownId;
    ProjectDetails? details;
    CancellationTokenSource? inspecting;
    bool syncing;

    public event Action? DataChanged;

    public ProjectsView()
    {
        InitializeComponent();
        List.ItemsSource = rows;
        syncing = true;
        SortRecent.IsChecked = true;
        syncing = false;

        ProjectStats.ProjectId = 0;
        ProjectStats.Period = new Period(App.Settings.ProjectPeriod, Clock.Today);
        ProjectStats.PeriodChanged += (_, _) =>
        {
            App.Settings.ProjectPeriod = ProjectStats.Period.Kind;
            RenderHeatmap();
        };
        Heatmap.DayClicked += d => ProjectStats.Period = new Period(PeriodKind.Day, d);

        foreach (var hex in ProjectColors.All)
        {
            var swatch = new RadioButton { Style = (Style)FindResource("Swatch"), Background = Palette.Brush(Palette.Parse(hex)), Tag = hex, ToolTip = hex };
            swatch.Checked += OnSwatch;
            Swatches.Children.Add(swatch);
        }
    }

    Project? Current => selectedId is long id ? snap?.Get(id) : null;

    public void Refresh(DataSnapshot d)
    {
        snap = d;
        SyncList();
        RenderDetail(false);
    }

    public void Select(long id)
    {
        selectedId = id;
        if (snap == null) return;
        SyncList();
        RenderDetail(true);
    }

    void SyncList()
    {
        var d = snap!;
        var work = d.Spans.Where(s => s.Kind != SpanKind.Open)
            .GroupBy(s => s.ProjectId)
            .ToDictionary(g => g.Key, g => (Total: g.Aggregate(TimeSpan.Zero, (a, s) => a + s.Duration), Last: g.Max(s => s.End)));

        var visible = d.Projects.Where(p => ShowHidden.IsChecked == true || !p.Hidden || p.Id == selectedId);
        var sorted = (SortTime.IsChecked == true ? visible.OrderByDescending(p => work.GetValueOrDefault(p.Id).Total)
                : SortName.IsChecked == true ? visible.OrderBy(p => p.Title, StringComparer.OrdinalIgnoreCase)
                : visible.OrderByDescending(p => work.TryGetValue(p.Id, out var w) ? w.Last : p.LastOpened ?? DateTime.MinValue))
            .ThenBy(p => p.Title, StringComparer.OrdinalIgnoreCase)
            .ToList();

        syncing = true;
        var byId = rows.ToDictionary(r => r.Id);
        for (int i = 0; i < sorted.Count; i++)
        {
            var p = sorted[i];
            if (!byId.TryGetValue(p.Id, out var row))
            {
                row = new ProjectRow(p.Id);
                rows.Insert(i, row);
            }
            else if (rows.IndexOf(row) is var at && at != i)
            {
                rows.Move(at, i);
            }
            row.Project = p;
            row.Subtitle = work.TryGetValue(p.Id, out var w)
                ? $"{Fmt.Dur(w.Total)}, {Fmt.Ago(w.Last)}"
                : "Not tracked yet";
            if (p.Hidden) row.Subtitle += ", hidden";
        }
        while (rows.Count > sorted.Count) rows.RemoveAt(rows.Count - 1);

        if (selectedId is not long sel || sorted.All(p => p.Id != sel)) selectedId = sorted.FirstOrDefault()?.Id;
        List.SelectedItem = rows.FirstOrDefault(r => r.Id == selectedId);
        syncing = false;

        bool any = sorted.Count > 0;
        EmptyText.Visibility = any ? Visibility.Collapsed : Visibility.Visible;
        DetailScroll.Visibility = any ? Visibility.Visible : Visibility.Collapsed;
    }

    void OnSelect(object sender, SelectionChangedEventArgs e)
    {
        if (syncing || List.SelectedItem is not ProjectRow row || row.Id == selectedId) return;
        selectedId = row.Id;
        RenderDetail(true);
    }

    void RenderDetail(bool selectionChanged)
    {
        var p = Current;
        if (p == null) return;
        selectionChanged |= p.Id != shownId;
        shownId = p.Id;

        BigIcon.Source = p;
        if (!NameBox.IsKeyboardFocused) NameBox.Text = p.Title;
        PathText.Text = p.HasFolder ? p.Folder : "Folder unknown, detected by window title";
        ResetIconButton.IsEnabled = p.Icon != null;
        OpenFolderButton.IsEnabled = OpenUnityButton.IsEnabled = p.HasFolder && Directory.Exists(p.Folder);
        HideButton.Content = p.Hidden ? "Unhide" : "Hide";

        syncing = true;
        foreach (RadioButton s in Swatches.Children) s.IsChecked = string.Equals((string)s.Tag, p.Color, StringComparison.OrdinalIgnoreCase);
        syncing = false;

        if (selectionChanged)
        {
            ProjectStats.ProjectId = p.Id;
            DetailScroll.ScrollToTop();
            Inspect(p);
        }
        RenderInfo();
        ProjectStats.Refresh(snap!);
        RenderHeatmap();
    }

    void RenderHeatmap()
    {
        var p = Current;
        if (p == null || snap == null) return;
        int year = ProjectStats.Period.Kind == PeriodKind.All ? Clock.Today.Year : ProjectStats.Period.Anchor.Year;
        var y0 = new DateOnly(year, 1, 1);
        HeatSection.Header = $"Activity in {year}";
        var selected = ProjectStats.Period.Kind == PeriodKind.Day ? ProjectStats.Period.Anchor : (DateOnly?)null;
        Heatmap.SetData(year, snap, Stats.PerDay(snap.Work(p.Id), y0, y0.AddYears(1)), selected);
    }

    async void Inspect(Project p)
    {
        inspecting?.Cancel();
        details = null;
        if (!p.HasFolder) return;
        var cts = inspecting = new CancellationTokenSource();
        try
        {
            var result = await Task.Run(() => ProjectInspector.Inspect(p.Folder, cts.Token), cts.Token);
            if (cts.IsCancellationRequested || selectedId != p.Id) return;
            details = result;
            RenderInfo();
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { Log.Error(ex); }
    }

    void RenderInfo()
    {
        var p = Current;
        if (p == null || snap == null) return;
        var d = details;
        string loading = p.HasFolder ? "reading..." : "-";

        var spans = snap.Of(p.Id).ToList();
        var editorSessions = snap.EditorSessions(p.Id).ToList();
        var launches = editorSessions.Select(s => s.Pid).Distinct().Count();
        var versions = editorSessions.Where(s => s.Version != null)
            .GroupBy(s => s.Version!)
            .Select(g => (Version: g.Key, Time: g.Aggregate(TimeSpan.Zero, (a, s) => a + (s.End - s.Start))))
            .OrderByDescending(v => v.Time)
            .ToList();
        var versionTotal = versions.Aggregate(TimeSpan.Zero, (a, v) => a + v.Time);
        var versionText = versions.Count == 0 ? "-"
            : string.Join(", ", versions.Select(v => versions.Count == 1 || versionTotal <= TimeSpan.Zero ? v.Version : $"{v.Version} ({Fmt.Pct(v.Time / versionTotal)})"));
        var info = new List<(string, string)>
        {
            ("Unity version", d?.UnityVersion ?? p.UnityVersion ?? "-"),
            ("Render pipeline", d?.Pipeline ?? p.Pipeline ?? loading),
            ("Product", d == null ? loading : JoinNonEmpty(d.ProductName, d.Company == null ? null : $"by {d.Company}", d.AppVersion == null ? null : $"v{d.AppVersion}")),
            ("Build target", p.BuildTarget ?? "-"),
            ("Git", d == null ? (p.Repo ?? loading) : JoinNonEmpty(d.GitBranch, d.GitRemote ?? p.Repo)),
            ("Packages", d == null ? loading : d.Packages.ToString()),
            ("Scripts", d == null ? loading : $"{Fmt.Num(d.Scripts)} files, {Fmt.Num(d.ScriptLines)} lines"),
            ("Scenes and prefabs", d == null ? loading : $"{Fmt.Num(d.Scenes)} scenes, {Fmt.Num(d.Prefabs)} prefabs"),
            ("Assets folder", d == null ? loading : d.Exists ? Fmt.Bytes(d.AssetsBytes) : "folder not found"),
            ("Tracked since", spans.Count > 0 ? Fmt.Day(Clock.DayOf(spans[0].Start)) : "not tracked yet"),
            ("Last active", spans.Count > 0 ? Fmt.Ago(spans.Max(s => s.End)) : p.LastOpened is DateTime lo ? Fmt.Ago(lo) : "-"),
            ("Editor launches", launches.ToString()),
            ("Editors used", versionText),
            ("Manual entries", Fmt.Plural(spans.Count(s => s.Manual && s.Kind != SpanKind.Open), "entry", "entries")),
        };

        InfoGrid.Children.Clear();
        InfoGrid.RowDefinitions.Clear();
        InfoGrid.ColumnDefinitions.Clear();
        foreach (var w in new[] { 130.0, -1, 130.0, -1 })
            InfoGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = w < 0 ? new GridLength(1, GridUnitType.Star) : new GridLength(w) });

        for (int i = 0; i < info.Count; i++)
        {
            int row = i / 2, col = i % 2 * 2;
            if (col == 0) InfoGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(24) });
            var key = new TextBlock { Text = info[i].Item1, Foreground = Palette.Dim, VerticalAlignment = VerticalAlignment.Center };
            var value = new TextBlock { Text = info[i].Item2, Foreground = Palette.Strong, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Thickness(0, 0, 16, 0), ToolTip = info[i].Item2 };
            Grid.SetRow(key, row);
            Grid.SetColumn(key, col);
            Grid.SetRow(value, row);
            Grid.SetColumn(value, col + 1);
            InfoGrid.Children.Add(key);
            InfoGrid.Children.Add(value);
        }
    }

    static string JoinNonEmpty(params string?[] parts)
    {
        var s = string.Join(", ", parts.Where(x => !string.IsNullOrWhiteSpace(x)));
        return s.Length > 0 ? s : "-";
    }

    void Save(Project p)
    {
        App.Db.UpdateProject(p);
        App.Tracker.ReloadProjects();
        DataChanged?.Invoke();
    }

    void OnSort(object sender, RoutedEventArgs e)
    {
        if (!syncing && snap != null) SyncList();
    }

    void OnShowHidden(object sender, RoutedEventArgs e)
    {
        if (snap != null) SyncList();
    }

    void OnImportHub(object sender, RoutedEventArgs e)
    {
        try { UnityHub.Import(App.Db); }
        catch (Exception ex) { Log.Error(ex); }
        DataChanged?.Invoke();
    }

    void OnNameKey(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            CommitName();
            Keyboard.ClearFocus();
        }
        else if (e.Key == Key.Escape)
        {
            NameBox.Text = Current?.Title ?? "";
            Keyboard.ClearFocus();
        }
    }

    void OnNameLostFocus(object sender, KeyboardFocusChangedEventArgs e) => CommitName();

    void CommitName()
    {
        var p = Current;
        if (p == null) return;
        var text = NameBox.Text.Trim();
        var display = text.Length == 0 || text == p.Name ? null : text;
        if (display == p.DisplayName) return;
        p.DisplayName = display;
        NameBox.Text = p.Title;
        Save(p);
    }

    void OnSwatch(object sender, RoutedEventArgs e)
    {
        if (syncing || Current is not Project p || sender is not RadioButton { Tag: string hex }) return;
        p.Color = hex;
        Save(p);
    }

    void OnChangeIcon(object sender, RoutedEventArgs e) => PickIcon();
    void OnIconBoxClick(object sender, MouseButtonEventArgs e) => PickIcon();

    void PickIcon()
    {
        var dialog = new OpenFileDialog
        {
            Title = "Choose a project icon",
            Filter = "Images|*.png;*.jpg;*.jpeg;*.bmp;*.gif;*.ico;*.tif;*.tiff|All files|*.*",
        };
        if (dialog.ShowDialog(Window.GetWindow(this)) == true) SetIcon(dialog.FileName);
    }

    void OnIconDragOver(object sender, DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    void OnIconDrop(object sender, DragEventArgs e)
    {
        if (e.Data.GetData(DataFormats.FileDrop) is string[] { Length: > 0 } files) SetIcon(files[0]);
    }

    void SetIcon(string file)
    {
        var p = Current;
        if (p == null) return;
        try
        {
            var old = p.Icon;
            p.Icon = IconStore.Import(file, p.Id);
            Save(p);
            IconStore.Delete(old);
        }
        catch (Exception ex)
        {
            Log.Error(ex);
            MessageBox.Show(Window.GetWindow(this), $"Couldn't use this file as an icon.\n\n{ex.Message}", "Unity Time Tracker", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    void OnResetIcon(object sender, RoutedEventArgs e)
    {
        var p = Current;
        if (p?.Icon == null) return;
        var old = p.Icon;
        p.Icon = null;
        Save(p);
        IconStore.Delete(old);
    }

    void OnOpenFolder(object sender, RoutedEventArgs e)
    {
        if (Current is { HasFolder: true } p && Directory.Exists(p.Folder))
            Process.Start(new ProcessStartInfo("explorer.exe", $"\"{p.Folder}\"") { UseShellExecute = true });
    }

    void OnOpenUnity(object sender, RoutedEventArgs e)
    {
        var p = Current;
        if (p == null || !p.HasFolder) return;
        var version = details?.UnityVersion ?? p.UnityVersion;
        var editor = UnityHub.FindEditor(version);
        if (editor == null)
        {
            MessageBox.Show(Window.GetWindow(this), $"Unity {version ?? "(unknown version)"} is not installed in the Unity Hub editors folder.",
                "Unity Time Tracker", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        var start = new ProcessStartInfo(editor) { UseShellExecute = false };
        start.ArgumentList.Add("-projectPath");
        start.ArgumentList.Add(p.Folder);
        Process.Start(start);
    }

    void OnHide(object sender, RoutedEventArgs e)
    {
        var p = Current;
        if (p == null) return;
        p.Hidden = !p.Hidden;
        Save(p);
    }

    public void Shift(int n) => ProjectStats.Shift(n);
    public void GoToCurrent() => ProjectStats.GoToCurrent();

    void OnReport(object sender, RoutedEventArgs e)
    {
        if (snap != null && Current is Project p) Actions.SaveReport(this, snap, ProjectStats.Period, p.Id);
    }

    void OnMerge(object sender, RoutedEventArgs e)
    {
        if (snap == null || Current is not Project p || !Actions.Merge(this, snap, p)) return;
        selectedId = null;
        DataChanged?.Invoke();
    }

    void OnDelete(object sender, RoutedEventArgs e)
    {
        var p = Current;
        if (p == null) return;
        var answer = MessageBox.Show(Window.GetWindow(this),
            $"Delete all tracked time for \"{p.Title}\"?\n\nThis can't be undone. The project comes back (with no time) if Unity Hub still lists it or you open it again.",
            "Delete project data", MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No);
        if (answer != MessageBoxResult.Yes) return;

        App.Tracker.Forget(p.Id);
        App.Db.DeleteProject(p.Id);
        IconStore.Delete(p.Icon);
        selectedId = null;
        DataChanged?.Invoke();
    }
}
