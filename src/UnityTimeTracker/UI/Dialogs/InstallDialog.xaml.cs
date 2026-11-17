using System.Windows;

namespace UnityTimeTracker.UI.Dialogs;

public enum InstallChoice { Cancel, Primary, Secondary }

public partial class InstallDialog : DarkWindow
{
    public InstallChoice Choice { get; private set; }

    public InstallDialog(string heading, string body, string where, string primary, string secondary)
    {
        InitializeComponent();
        Heading.Text = heading;
        Body.Text = body;
        Where.Text = where;
        PrimaryButton.Content = primary;
        SecondaryButton.Content = secondary;
    }

    void OnPrimary(object sender, RoutedEventArgs e)
    {
        Choice = InstallChoice.Primary;
        Close();
    }

    void OnSecondary(object sender, RoutedEventArgs e)
    {
        Choice = InstallChoice.Secondary;
        Close();
    }
}
