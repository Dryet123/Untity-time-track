using System.Windows.Controls;
using UnityTimeTracker.Core;

namespace UnityTimeTracker.UI.Views;

public interface IView
{
    void Refresh(DataSnapshot snap);

    // Raised when the view changed data and every view needs fresh numbers.
    event Action? DataChanged;
}

public interface IPeriodView
{
    void Shift(int n);
    void GoToCurrent();
}

public partial class OverviewView : UserControl, IView, IPeriodView
{
    public event Action<long>? OpenProject;
    public event Action? DataChanged { add { } remove { } }

    public OverviewView()
    {
        InitializeComponent();
        Panel.Period = new Period(App.Settings.OverviewPeriod, Clock.Today);
        Panel.PeriodChanged += (_, _) => App.Settings.OverviewPeriod = Panel.Period.Kind;
        Panel.ProjectClicked += id => OpenProject?.Invoke(id);
    }

    public void Refresh(DataSnapshot snap) => Panel.Refresh(snap);
    public void Shift(int n) => Panel.Shift(n);
    public void GoToCurrent() => Panel.GoToCurrent();
}
