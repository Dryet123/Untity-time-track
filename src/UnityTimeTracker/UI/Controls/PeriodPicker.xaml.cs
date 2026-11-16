using System.Windows;
using System.Windows.Controls;
using UnityTimeTracker.Core;

namespace UnityTimeTracker.UI.Controls;

public partial class PeriodPicker : UserControl
{
    Period period = new(PeriodKind.Week, Clock.Today);
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
            Sync();
            PeriodChanged?.Invoke(this, EventArgs.Empty);
        }
    }

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
        var current = period with { Anchor = Clock.Today };
        CurrentButton.IsEnabled = movable && period.Days(Clock.Today) != current.Days(Clock.Today);
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

    void OnPrev(object sender, RoutedEventArgs e) => Period = period.Shift(-1);
    void OnNext(object sender, RoutedEventArgs e) => Period = period.Shift(1);
    void OnCurrent(object sender, RoutedEventArgs e) => Period = period with { Anchor = Clock.Today };
}
