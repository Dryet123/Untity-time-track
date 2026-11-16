using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using UnityTimeTracker.Core;

namespace UnityTimeTracker.UI.Controls;

// Period picker plus every chart and metric; used by Overview (all projects) and by the Projects tab (one project).
public partial class StatsPanel : UserControl
{
    static readonly string[] WeekdayNames = ["Mon", "Tue", "Wed", "Thu", "Fri", "Sat", "Sun"];
    static readonly string[] WeekdayPlural = ["Mondays", "Tuesdays", "Wednesdays", "Thursdays", "Fridays", "Saturdays", "Sundays"];

    DataSnapshot? snap;
    long? projectId;
    List<Period?> drill = [];

    public event Action<long>? ProjectClicked;
    public event EventHandler? PeriodChanged;

    public StatsPanel()
    {
        InitializeComponent();
        TimeChart.GroupClicked += i =>
        {
            if (i < drill.Count && drill[i] is Period p) Picker.Period = p;
        };
    }

    public Period Period
    {
        get => Picker.Period;
        set => Picker.Period = value;
    }

    public long? ProjectId
    {
        get => projectId;
        set
        {
            projectId = value;
            bool single = value != null;
            ProjectsSection.Visibility = single ? Visibility.Collapsed : Visibility.Visible;
            Grid.SetColumn(ScenesSection, single ? 0 : 2);
            Grid.SetColumnSpan(ScenesSection, single ? 3 : 1);
        }
    }

    public void Refresh(DataSnapshot d)
    {
        snap = d;
        Render();
    }

    void OnPeriodChanged(object? sender, EventArgs e)
    {
        if (snap != null) Render();
        PeriodChanged?.Invoke(this, EventArgs.Empty);
    }

    void Render()
    {
        var d = snap!;
        var period = Picker.Period;
        var (from, to) = period.Days(d.FirstDay);
        var range = Clock.Range(from, to);
        var all = d.Of(projectId).ToList();
        var work = all.Where(s => s.Kind != SpanKind.Open).ToList();
        var totals = Stats.Sum(all, range);
        var perDay = Stats.PerDay(work, from, to);
        var lastDay = to < Clock.Today.AddDays(1) ? to : Clock.Today.AddDays(1);
        int daysInRange = Math.Max(0, lastDay.DayNumber - from.DayNumber);

        RenderSummary(d, range, work, totals, perDay, daysInRange);
        RenderTimeChart(d, period, from, to, work);

        var hours = Stats.HourOfDay(work, range);
        HourChart.SetData(Enumerable.Range(0, 24)
            .Select(h => new BarChart.Group(h.ToString(), $"{h:00}:00 - {(h + 1) % 24:00}:00", [new BarChart.Segment(hours[h], Palette.AccentColor, "Total")]))
            .ToList());

        var weekday = new double[7];
        var weekdayCount = new int[7];
        foreach (var (day, map) in perDay) weekday[Clock.Weekday(day)] += Stats.Total(map).TotalHours;
        for (var x = from; x < lastDay; x = x.AddDays(1)) weekdayCount[Clock.Weekday(x)]++;
        WeekdayChart.SetData(Enumerable.Range(0, 7)
            .Select(i => new BarChart.Group(WeekdayNames[i], WeekdayPlural[i], [new BarChart.Segment(weekday[i], Palette.AccentColor, "Total")],
                weekdayCount[i] > 1 ? [new TipLine(null, "Average", Fmt.Hours(weekday[i] / weekdayCount[i]))] : null))
            .ToList());

        RenderProjects(d, all, range, totals);
        RenderScenes(d, all, range, totals);
    }

    void RenderSummary(DataSnapshot d, TimeRange range, List<SpanRec> work, Totals totals,
        Dictionary<DateOnly, Dictionary<long, TimeSpan>> perDay, int daysInRange)
    {
        var sessions = d.WorkSessions(projectId).Where(s => range.Contains(s.Start)).ToList();
        var longest = sessions.MaxBy(s => s.Work);
        var launches = d.EditorSessions(projectId).Where(s => range.Contains(s.Start)).Select(s => (s.ProjectId, s.Pid)).Distinct().Count();
        int worked = perDay.Count(kv => Stats.Total(kv.Value) >= TimeSpan.FromMinutes(1));
        var best = perDay.Select(kv => (Day: kv.Key, Time: Stats.Total(kv.Value))).OrderByDescending(x => x.Time).FirstOrDefault();
        var (streak, bestStreak) = Stats.Streaks(work);
        var usual = Stats.UsualHours(work, range);
        var hours = Stats.HourOfDay(work, range);
        int peakHour = Array.IndexOf(hours, hours.Max());

        Summary.Set(
            new Stat("Total", Fmt.Dur(totals.Work), "Unity and IDE"),
            new Stat("In Unity", Fmt.Dur(totals.Editor), totals.Open > TimeSpan.Zero ? $"{Fmt.Pct(totals.Editor / totals.Open)} of open time" : "focused and not idle"),
            new Stat("In IDE", Fmt.Dur(totals.Ide), App.Settings.TrackIde ? "Rider, VS, VS Code" : "turned off"),
            new Stat("Unity open", Fmt.Dur(totals.Open), Fmt.Plural(launches, "launch", "launches")),
            new Stat("Sessions", sessions.Count.ToString(), sessions.Count > 0 ? $"avg {Fmt.Dur(totals.Work / sessions.Count)}" : "split by 10 min breaks"),
            new Stat("Longest session", longest != null ? Fmt.Dur(longest.Work) : "-", longest != null ? Fmt.Day(Clock.DayOf(longest.Start)) : null),
            new Stat("Days worked", worked.ToString(), $"of {daysInRange}"),
            new Stat("Daily average", worked > 0 ? Fmt.Dur(totals.Work / worked) : "-", "on days worked"),
            new Stat("Best day", best.Time > TimeSpan.Zero ? Fmt.Dur(best.Time) : "-", best.Time > TimeSpan.Zero ? Fmt.Day(best.Day) : null),
            new Stat("Streak", Fmt.Plural(streak, "day", "days"), $"best {Fmt.Plural(bestStreak, "day", "days")}"),
            new Stat("Usual hours", usual is { } u ? $"{Fmt.ClockTime(u.Start)} - {Fmt.ClockTime(u.End)}" : "-", "median start and end"),
            new Stat("Peak hour", hours[peakHour] > 0 ? $"{peakHour:00}:00" : "-", hours[peakHour] > 0 ? $"{Fmt.Hours(hours[peakHour])} in total" : null));
    }

    void RenderTimeChart(DataSnapshot d, Period p, DateOnly from, DateOnly to, List<SpanRec> work)
    {
        var edges = new List<DateTime>();
        var labels = new List<(string Label, string Title, Period? Drill)>();

        switch (p.Kind)
        {
            case PeriodKind.Day:
                var start = Clock.Start(from);
                for (int h = 0; h < 24; h++)
                {
                    var t = start.AddHours(h);
                    edges.Add(t);
                    labels.Add((t.Hour.ToString(), $"{Fmt.Time(t)} - {Fmt.Time(t.AddHours(1))}", null));
                }
                edges.Add(start.AddHours(24));
                TimeSection.Header = "Time per hour";
                break;

            case PeriodKind.Week or PeriodKind.Month:
                for (var x = from; x < to; x = x.AddDays(1))
                {
                    edges.Add(Clock.Start(x));
                    labels.Add((p.Kind == PeriodKind.Week ? Fmt.Format(x, "ddd d") : x.Day.ToString(), Fmt.Day(x), new Period(PeriodKind.Day, x)));
                }
                edges.Add(Clock.Start(to));
                TimeSection.Header = "Time per day";
                break;

            default:
                var month = new DateOnly(from.Year, from.Month, 1);
                var lastIncluded = to.AddDays(-1);
                int months = (lastIncluded.Year - month.Year) * 12 + lastIncluded.Month - month.Month + 1;
                if (p.Kind == PeriodKind.All && months > 36)
                {
                    for (int y = from.Year; y <= lastIncluded.Year; y++)
                    {
                        var first = new DateOnly(y, 1, 1);
                        edges.Add(Clock.Start(first));
                        labels.Add((y.ToString(), y.ToString(), new Period(PeriodKind.Year, first)));
                    }
                    edges.Add(Clock.Start(new DateOnly(lastIncluded.Year + 1, 1, 1)));
                    TimeSection.Header = "Time per year";
                }
                else
                {
                    for (int i = 0; i < months; i++, month = month.AddMonths(1))
                    {
                        edges.Add(Clock.Start(month));
                        labels.Add((Fmt.Format(month, p.Kind == PeriodKind.Year ? "MMM" : "MMM yy"), Fmt.Month(month), new Period(PeriodKind.Month, month)));
                    }
                    edges.Add(Clock.Start(month));
                    TimeSection.Header = "Time per month";
                }
                break;
        }

        var buckets = Stats.Buckets(work, edges);
        var order = buckets.SelectMany(b => b)
            .GroupBy(kv => kv.Key)
            .OrderByDescending(g => g.Sum(kv => kv.Value.Ticks))
            .Select(g => g.Key)
            .ToList();

        var groups = new List<BarChart.Group>();
        for (int i = 0; i < buckets.Length; i++)
        {
            var segments = order.Where(buckets[i].ContainsKey)
                .Select(id => new BarChart.Segment(buckets[i][id].TotalHours, Palette.Of(d.Get(id)), d.Get(id)?.Title ?? "Unknown"))
                .ToList();
            groups.Add(new BarChart.Group(labels[i].Label, labels[i].Title, segments));
        }
        drill = labels.Select(l => l.Drill).ToList();
        TimeChart.SetData(groups);
    }

    void RenderProjects(DataSnapshot d, List<SpanRec> all, TimeRange range, Totals totals)
    {
        if (projectId != null) return;
        var per = Stats.PerProject(all, range).Where(kv => kv.Value.Work > TimeSpan.Zero).OrderByDescending(kv => kv.Value.Work).ToList();
        var max = per.Count > 0 ? per[0].Value.Work : TimeSpan.Zero;
        ProjectList.ItemsSource = per.Select(kv =>
        {
            var p = d.Get(kv.Key);
            return new ShareRow
            {
                ProjectId = kv.Key,
                Project = p,
                Name = p?.Title ?? "Unknown",
                Fraction = kv.Value.Work / max,
                Fill = Palette.Brush(Palette.Of(p)),
                Time = Fmt.Dur(kv.Value.Work),
                Percent = Fmt.Pct(kv.Value.Work / totals.Work),
            };
        }).ToList();
        ProjectsEmpty.Visibility = per.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    void RenderScenes(DataSnapshot d, List<SpanRec> all, TimeRange range, Totals totals)
    {
        var scenes = Stats.Scenes(all, range).Take(projectId == null ? 8 : 15).ToList();
        var max = scenes.Count > 0 ? scenes[0].Time : TimeSpan.Zero;
        SceneList.ItemsSource = scenes.Select(s =>
        {
            var p = d.Get(s.ProjectId);
            return new ShareRow
            {
                ProjectId = s.ProjectId,
                Project = projectId == null ? p : null,
                Name = s.Scene,
                Sub = projectId == null ? $"   {p?.Title}" : null,
                Fraction = s.Time / max,
                Fill = Palette.Brush(Palette.Of(p)),
                Time = Fmt.Dur(s.Time),
                Percent = totals.Editor > TimeSpan.Zero ? Fmt.Pct(s.Time / totals.Editor) : "",
            };
        }).ToList();
        ScenesEmpty.Visibility = scenes.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    void OnProjectRowClick(object sender, MouseButtonEventArgs e)
    {
        if ((e.OriginalSource as FrameworkElement)?.DataContext is ShareRow row) ProjectClicked?.Invoke(row.ProjectId);
    }
}
