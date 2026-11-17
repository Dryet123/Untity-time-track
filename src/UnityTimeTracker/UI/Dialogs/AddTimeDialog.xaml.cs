using System.Globalization;
using System.Windows;
using UnityTimeTracker.Core;

namespace UnityTimeTracker.UI.Dialogs;

// For time the tracker missed: work on another machine, the app not running, and so on.
public partial class AddTimeDialog : DarkWindow
{
    static readonly string[] DateFormats = ["yyyy-MM-dd", "yyyy-M-d", "d.M.yyyy", "dd.MM.yyyy"];
    static readonly string[] TimeFormats = [@"h\:mm", @"hh\:mm", "hhmm"];

    readonly DataSnapshot snap;
    bool overlapConfirmed;

    public long ProjectId { get; private set; }
    public SpanKind Kind { get; private set; }
    public DateTime Start { get; private set; }
    public DateTime End { get; private set; }

    public AddTimeDialog(DataSnapshot snap, DateOnly day, long? projectId)
    {
        InitializeComponent();
        this.snap = snap;
        var projects = snap.Projects.Where(p => !p.Hidden || p.Id == projectId).OrderBy(p => p.Title, StringComparer.OrdinalIgnoreCase).ToList();
        Projects.ItemsSource = projects;
        Projects.SelectedItem = projects.FirstOrDefault(p => p.Id == projectId) ?? projects.FirstOrDefault();
        DateBox.Text = day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        foreach (var box in new[] { DateBox, FromBox, ToBox }) box.TextChanged += (_, _) => overlapConfirmed = false;
    }

    void OnAdd(object sender, RoutedEventArgs e)
    {
        if (Projects.SelectedItem is not Project project) { Warn("Pick a project."); return; }
        if (!DateOnly.TryParseExact(DateBox.Text.Trim(), DateFormats, CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)) { Warn("The date should look like 2026-11-17."); return; }
        if (!TryTime(FromBox.Text, out var from) || !TryTime(ToBox.Text, out var to)) { Warn("Times should look like 14:30."); return; }

        var start = date.ToDateTime(TimeOnly.MinValue) + from;
        var end = date.ToDateTime(TimeOnly.MinValue) + to;
        if (end <= start) end = end.AddDays(1);
        if (end - start > TimeSpan.FromHours(16)) { Warn("That's more than 16 hours. Split it into separate entries."); return; }
        if (end > DateTime.Now.AddMinutes(1)) { Warn("That time hasn't happened yet."); return; }

        var range = new TimeRange(start, end);
        var overlap = snap.Spans.Where(s => s.Kind != SpanKind.Open).Aggregate(TimeSpan.Zero, (a, s) => a + range.Overlap(s.Start, s.End));
        if (overlap >= TimeSpan.FromMinutes(1) && !overlapConfirmed)
        {
            overlapConfirmed = true;
            Warn($"{Fmt.Dur(overlap)} of this range is already tracked, so it would be counted twice. Press Add again to keep it anyway.");
            return;
        }

        ProjectId = project.Id;
        Kind = KindIde.IsChecked == true ? SpanKind.Ide : SpanKind.Editor;
        Start = start;
        End = end;
        DialogResult = true;
    }

    static bool TryTime(string text, out TimeSpan t) =>
        TimeSpan.TryParseExact(text.Trim(), TimeFormats, CultureInfo.InvariantCulture, out t) && t < TimeSpan.FromDays(1);

    void Warn(string text)
    {
        Message.Text = text;
        Message.Visibility = Visibility.Visible;
    }
}
