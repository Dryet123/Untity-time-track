using System.Windows;
using System.Windows.Controls;
using UnityTimeTracker.Core;

namespace UnityTimeTracker.UI.Controls;

public partial class PeriodPicker : UserControl
{
    Period period = new(PeriodKind.Week, Clock.Today);
    DateOnly seenToday = Clock.Today;
    bool syncing;

    public event EventHandler? PeriodChanged;

    public PeriodPicker()
    {
        InitializeComponent();
        Sync();
    }

    public Period Period
    {
        get => period;
        set
        {
            period = value;
            seenToday = Clock.Today;
            Sync();
            PeriodChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    bool IsCurrent(DateOnly today)
    {
        var (from, to) = period.Days(today);
        return today >= from && today < to;
    }

    // The app runs for days from autostart; a period showing "now" should move on with the calendar.
    public void FollowToday()
    {
        var today = Clock.Today;
        if (today == seenToday) return;
        bool wasCurrent = period.Kind != PeriodKind.All && IsCurrent(seenToday);
        seenToday = today;
        if (wasCurrent) period = period with { Anchor = today };
        Sync();
    }

    public void Shift(int n)
    {
        if (period.Kind != PeriodKind.All) Period = period.Shift(n);
    }

    public void GoToCurrent() => Period = period with { Anchor = Clock.Today };

    void Sync()
    {
        syncing = true;
        KindDay.IsChecked = period.Kind == PeriodKind.Day;
        KindWeek.IsChecked = period.Kind == PeriodKind.Week;
        KindMonth.IsChecked = period.Kind == PeriodKind.Month;
        KindYear.IsChecked = period.Kind == PeriodKind.Year;
        KindAll.IsChecked = period.Kind == PeriodKind.All;
        syncing = false;

        LabelText.Text = period.Label;
        bool movable = period.Kind != PeriodKind.All;
        PrevButton.IsEnabled = NextButton.IsEnabled = movable;
        CurrentButton.IsEnabled = movable && !IsCurrent(Clock.Today);
    }

    void OnKind(object sender, RoutedEventArgs e)
    {
        if (syncing) return;
        var kind = sender == KindDay ? PeriodKind.Day
            : sender == KindWeek ? PeriodKind.Week
            : sender == KindMonth ? PeriodKind.Month
            : sender == KindYear ? PeriodKind.Year
            : PeriodKind.All;
        Period = period with { Kind = kind };
    }

    void OnPrev(object sender, RoutedEventArgs e) => Shift(-1);
    void OnNext(object sender, RoutedEventArgs e) => Shift(1);
    void OnCurrent(object sender, RoutedEventArgs e) => GoToCurrent();
}
