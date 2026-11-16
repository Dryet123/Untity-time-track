using System.Windows;
using System.Windows.Controls;
using UnityTimeTracker.Core;
using UnityTimeTracker.UI.Controls;

namespace UnityTimeTracker.UI.Views;

public partial class CalendarView : UserControl, IView
{
    DataSnapshot? snap;
    int year = Clock.Today.Year;
    DateOnly month = new(Clock.Today.Year, Clock.Today.Month, 1);
    DateOnly day = Clock.Today;

    public CalendarView()
    {
        InitializeComponent();
        Heatmap.DayClicked += Select;
        Month.DayClicked += Select;
    }

    public void Refresh(DataSnapshot d)
    {
        snap = d;
        Render();
    }

    public void Select(DateOnly d)
    {
        day = d;
        month = new DateOnly(d.Year, d.Month, 1);
        year = d.Year;
        if (snap != null) Render();
    }

    void Render()
    {
        var d = snap!;
        var work = d.Work(null).ToList();

        var y0 = new DateOnly(year, 1, 1);
        var yearData = Stats.PerDay(work, y0, y0.AddYears(1));
        YearLabel.Text = year.ToString();
        Heatmap.SetData(year, d, yearData, day);
        YearSummary.Text = Summary(yearData);

        var gridStart = Clock.Monday(month);
        var monthData = Stats.PerDay(work, gridStart, gridStart.AddDays(42));
        MonthLabel.Text = Fmt.Month(month);
        Month.SetData(month, d, monthData, day);
        MonthSummary.Text = Summary(monthData.Where(kv => kv.Key.Month == month.Month && kv.Key.Year == month.Year).ToDictionary());

        RenderDay(d);
    }

    static string Summary(Dictionary<DateOnly, Dictionary<long, TimeSpan>> perDay)
    {
        var days = perDay.Where(kv => Stats.Total(kv.Value) >= TimeSpan.FromMinutes(1)).ToList();
        if (days.Count == 0) return "No activity";
        var total = days.Aggregate(TimeSpan.Zero, (a, kv) => a + Stats.Total(kv.Value));
        return $"{Fmt.Dur(total)} in {Fmt.Plural(days.Count, "day", "days")}";
    }

    void RenderDay(DataSnapshot d)
    {
        var range = Clock.Range(day, day.AddDays(1));
        var all = d.Of(null).ToList();
        var totals = Stats.Sum(all, range);
        DayHeader.Text = Fmt.DayLong(day);
        DayStats.Set(
            new Stat("Total", Fmt.Dur(totals.Work)),
            new Stat("In Unity", Fmt.Dur(totals.Editor)),
            new Stat("In IDE", Fmt.Dur(totals.Ide)),
            new Stat("Unity open", Fmt.Dur(totals.Open)));
        Timeline.SetData(range, all, d);

        var per = Stats.PerProject(all, range).Where(kv => kv.Value.Work > TimeSpan.Zero).OrderByDescending(kv => kv.Value.Work).ToList();
        var max = per.Count > 0 ? per[0].Value.Work : TimeSpan.Zero;
        DayProjects.ItemsSource = per.Select(kv => new ShareRow
        {
            ProjectId = kv.Key,
            Project = d.Get(kv.Key),
            Name = d.Get(kv.Key)?.Title ?? "Unknown",
            Fraction = kv.Value.Work / max,
            Fill = Palette.Brush(Palette.Of(d.Get(kv.Key))),
            Time = Fmt.Dur(kv.Value.Work),
            Percent = Fmt.Pct(kv.Value.Work / totals.Work),
        }).ToList();
        DayProjectsLabel.Visibility = DayProjects.Visibility = per.Count > 0 ? Visibility.Visible : Visibility.Collapsed;

        var sessions = d.WorkSessions(null).Where(s => s.End > range.From && s.Start < range.To).Select(s => SessionRow.From(s, d)).ToList();
        DaySessions.ItemsSource = sessions;
        DayEmpty.Visibility = sessions.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    void OnPrevYear(object sender, RoutedEventArgs e) => ShiftYear(-1);
    void OnNextYear(object sender, RoutedEventArgs e) => ShiftYear(1);
    void OnPrevMonth(object sender, RoutedEventArgs e) => ShiftMonth(-1);
    void OnNextMonth(object sender, RoutedEventArgs e) => ShiftMonth(1);
    void OnToday(object sender, RoutedEventArgs e) => Select(Clock.Today);

    void ShiftYear(int n)
    {
        year += n;
        if (snap != null) Render();
    }

    void ShiftMonth(int n)
    {
        month = month.AddMonths(n);
        year = month.Year;
        if (snap != null) Render();
    }
}
