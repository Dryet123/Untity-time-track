using System.Windows;
using System.Windows.Interop;
using UnityTimeTracker.Core;

namespace UnityTimeTracker.UI;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        TabOverview.IsChecked = true;
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        Native.UseDarkTitleBar(new WindowInteropHelper(this).Handle);
    }

    void OnTab(object sender, RoutedEventArgs e)
    {
    }
}
