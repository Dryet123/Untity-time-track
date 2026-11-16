using System.Windows.Controls;
using UnityTimeTracker.Core;

namespace UnityTimeTracker.UI.Views;

public interface IView
{
    void Refresh(DataSnapshot snap);
}

public partial class OverviewView : UserControl, IView
{
    public event Action<long>? OpenProject;

    public OverviewView()
    {
        InitializeComponent();
        Panel.ProjectClicked += id => OpenProject?.Invoke(id);
    }

    public void Refresh(DataSnapshot snap) => Panel.Refresh(snap);
}
