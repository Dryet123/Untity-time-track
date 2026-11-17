using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using UnityTimeTracker.Core;
using UnityTimeTracker.UI.Controls;

namespace UnityTimeTracker.UI.Views;

public partial class CalendarView : UserControl, IView, IPeriodView
{
    readonly DispatcherTimer noteSave = new() { Interval = TimeSpan.FromMilliseconds(800) };
    DataSnapshot? snap;
    int year = Clock.Today.Year;
    DateOnly month = new(Clock.Today.Year, Clock.Today.Month, 1);
    DateOnly day = Clock.Today;
    DateOnly seenToday = Clock.Today;
    DateOnly? noteDay;
    bool loadingNote;

    public event Action? DataChanged;

    public CalendarView()
    {
        InitializeComponent();
        Heatmap.DayClicked += Select;
        Month.DayClicked += Select;
        noteSave.Tick += (_, _) => SaveNote();
    }

    public void Refresh(DataSnapshot d)
    {
        snap = d;
        if (Clock.Today != seenToday)
        {
            if (day == seenToday) SetDay(Clock.Today);
            seenToday = Clock.Today;
        }
        Render();
    }

    public void Select(DateOnly d)
    {
        SaveNote();
        SetDay(d);
        if (snap != null) Render();
    }

    void SetDay(DateOnly d)
    {
        day = d;
        month = new DateOnly(d.Year, d.Month, 1);
        year = d.Year;
    }

    public void Shift(int n) => ShiftMonth(n);
    public void GoToCurrent() => Select(Clock.Today);

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
        var goal = App.Settings.DailyGoal;
        var hit = goal > TimeSpan.Zero ? $", goal met on {days.Count(kv => Stats.Total(kv.Value) >= goal)}" : "";
        return $"{Fmt.Dur(total)} in {Fmt.Plural(days.Count, "day", "days")}{hit}";
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

        if (!NoteBox.IsKeyboardFocused || noteDay != day)
        {
            loadingNote = true;
            NoteBox.Text = d.Notes.GetValueOrDefault(day) ?? "";
            noteDay = day;
            loadingNote = false;
        }

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

    void OnNoteChanged(object sender, TextChangedEventArgs e)
    {
        if (loadingNote) return;
        noteSave.Stop();
        noteSave.Start();
    }

    void OnNoteLostFocus(object sender, KeyboardFocusChangedEventArgs e) => SaveNote();

    void SaveNote()
    {
        if (!noteSave.IsEnabled || noteDay is not DateOnly d) return;
        noteSave.Stop();
        try { App.Db.SaveNote(d, NoteBox.Text); }
        catch (Exception ex) { Log.Error(ex); }
    }

    void OnAddTime(object sender, RoutedEventArgs e)
    {
        if (snap != null && Actions.AddTime(this, snap, day, null)) DataChanged?.Invoke();
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
