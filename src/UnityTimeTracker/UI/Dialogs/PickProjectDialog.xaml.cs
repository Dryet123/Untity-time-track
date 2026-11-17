using System.Windows;
using System.Windows.Input;
using UnityTimeTracker.Core;

namespace UnityTimeTracker.UI.Dialogs;

public partial class PickProjectDialog : DarkWindow
{
    public Project? Picked { get; private set; }

    public PickProjectDialog(string title, string prompt, string ok, IEnumerable<Project> projects)
    {
        InitializeComponent();
        Title = title;
        Prompt.Text = prompt;
        OkButton.Content = ok;
        Projects.ItemsSource = projects.OrderBy(p => p.Title, StringComparer.OrdinalIgnoreCase).ToList();
    }

    void OnOk(object sender, RoutedEventArgs e)
    {
        if (Projects.SelectedItem is not Project p) return;
        Picked = p;
        DialogResult = true;
    }

    void OnDoubleClick(object sender, MouseButtonEventArgs e) => OnOk(sender, e);
}
