using System.ComponentModel;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using UnityTimeTracker.Core;
using UnityTimeTracker.UI.Controls;

namespace UnityTimeTracker.UI.Views;

public partial class LogView : UserControl, IView
{
    DataSnapshot? snap;
    List<SessionRow> rows = [];
    (int Count, DateTime Last, TimeSpan Work) shown;

    public LogView()
    {
        InitializeComponent();
        Picker.Period = new Period(PeriodKind.Month, Clock.Today);
    }

    public void Refresh(DataSnapshot d)
    {
        snap = d;
        Render(false);
    }

    void OnPeriodChanged(object? sender, EventArgs e)
    {
        if (snap != null) Render(true);
    }

    void Render(bool force)
    {
        var d = snap!;
        var (from, to) = Picker.Period.Days(d.FirstDay);
        var range = Clock.Range(from, to);
        var fresh = d.WorkSessions(null).Where(s => range.Contains(s.Start)).Select(s => SessionRow.From(s, d)).ToList();
        var total = fresh.Aggregate(TimeSpan.Zero, (a, r) => a + r.Work);

        // Reassigning ItemsSource drops the user's sort and scroll, so only do it when something changed.
        var signature = (fresh.Count, fresh.Count > 0 ? fresh.Max(r => r.End) : default, total);
        if (force || signature != shown)
        {
            shown = signature;
            rows = fresh;
            var sorts = Table.Items.SortDescriptions.ToList();
            Table.ItemsSource = rows;
            if (sorts.Count == 0) sorts.Add(new SortDescription(nameof(SessionRow.Start), ListSortDirection.Descending));
            Table.Items.SortDescriptions.Clear();
            foreach (var s in sorts) Table.Items.SortDescriptions.Add(s);
            foreach (var col in Table.Columns)
            {
                var match = sorts.FirstOrDefault(s => s.PropertyName == col.SortMemberPath);
                col.SortDirection = match.PropertyName == null ? null : match.Direction;
            }
        }

        Empty.Visibility = rows.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        var days = rows.Select(r => Clock.DayOf(r.Start)).Distinct().Count();
        Footer.Text = rows.Count == 0
            ? Picker.Period.Label
            : $"{Fmt.Plural(rows.Count, "session", "sessions")} on {Fmt.Plural(days, "day", "days")}, {Fmt.Dur(total)} in total. A new session starts after a break longer than 10 minutes.";
    }

    void OnExport(object sender, RoutedEventArgs e)
    {
        if (snap == null) return;
        var (from, to) = Picker.Period.Days(snap.FirstDay);
        var dialog = new SaveFileDialog
        {
            Title = "Export sessions",
            Filter = "CSV file|*.csv",
            FileName = $"unity-time-{Fmt.Format(from, "yyyy-MM-dd")}-{Fmt.Format(to.AddDays(-1), "yyyy-MM-dd")}.csv",
        };
        if (dialog.ShowDialog(Window.GetWindow(this)) != true) return;

        var sb = new StringBuilder("date,project,start,end,total_minutes,unity_minutes,ide_minutes,scenes\n");
        foreach (var r in rows.OrderBy(r => r.Start))
        {
            sb.AppendJoin(',',
                Fmt.Format(Clock.DayOf(r.Start), "yyyy-MM-dd"),
                Csv.Escape(r.Name),
                Fmt.Format(r.Start, "yyyy-MM-dd HH:mm"),
                Fmt.Format(r.End, "yyyy-MM-dd HH:mm"),
                Csv.Minutes(r.Work), Csv.Minutes(r.Editor), Csv.Minutes(r.Ide),
                Csv.Escape(r.Scenes));
            sb.Append('\n');
        }
        try { File.WriteAllText(dialog.FileName, sb.ToString(), new UTF8Encoding(true)); }
        catch (IOException ex) { MessageBox.Show(Window.GetWindow(this), ex.Message, "Export failed", MessageBoxButton.OK, MessageBoxImage.Warning); }
    }
}

public static class Csv
{
    public static string Escape(string s) =>
        s.IndexOfAny([',', '"', '\n', '\r']) >= 0 ? $"\"{s.Replace("\"", "\"\"")}\"" : s;

    public static string Minutes(TimeSpan t) => Math.Round(t.TotalMinutes, 1).ToString(System.Globalization.CultureInfo.InvariantCulture);
}
