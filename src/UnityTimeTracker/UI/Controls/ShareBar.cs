using System.Windows;
using System.Windows.Media;

namespace UnityTimeTracker.UI.Controls;

public sealed class ShareBar : FrameworkElement
{
    public static readonly DependencyProperty FractionProperty = DependencyProperty.Register(
        nameof(Fraction), typeof(double), typeof(ShareBar), new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty FillProperty = DependencyProperty.Register(
        nameof(Fill), typeof(Brush), typeof(ShareBar), new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    public double Fraction
    {
        get => (double)GetValue(FractionProperty);
        set => SetValue(FractionProperty, value);
    }

    public Brush? Fill
    {
        get => (Brush?)GetValue(FillProperty);
        set => SetValue(FillProperty, value);
    }

    protected override void OnRender(DrawingContext dc)
    {
        var r = new Rect(RenderSize);
        dc.DrawRectangle(Palette.Empty, null, r);
        var f = Math.Clamp(Fraction, 0, 1);
        if (f > 0 && Fill != null)
            dc.DrawRectangle(Fill, null, new Rect(0, 0, Math.Max(1, r.Width * f), r.Height));
    }
}
