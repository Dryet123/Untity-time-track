using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using UnityTimeTracker.Core;

namespace UnityTimeTracker.UI.Dialogs;

// Base for small modal dialogs in the same look as the main window.
public class DarkWindow : Window
{
    public DarkWindow()
    {
        Background = Palette.B(Palette.C(0x383838));
        Foreground = Palette.Text;
        FontFamily = new FontFamily("Arial");
        FontSize = 12;
        TextOptions.SetTextFormattingMode(this, TextFormattingMode.Display);
        UseLayoutRounding = true;
        SnapsToDevicePixels = true;
        ShowInTaskbar = false;
        ResizeMode = ResizeMode.NoResize;
        SizeToContent = SizeToContent.WidthAndHeight;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        Native.UseDarkTitleBar(new WindowInteropHelper(this).Handle);
    }
}
