using System.Windows;
using System.Windows.Media;
using UnityTimeTracker.Core;

namespace UnityTimeTracker.UI.Controls;

public sealed class ProjectIcon : FrameworkElement
{
    public static readonly DependencyProperty SourceProperty = DependencyProperty.Register(
        nameof(Source), typeof(Project), typeof(ProjectIcon), new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    public Project? Source
    {
        get => (Project?)GetValue(SourceProperty);
        set => SetValue(SourceProperty, value);
    }

    public ProjectIcon()
    {
        RenderOptions.SetBitmapScalingMode(this, BitmapScalingMode.HighQuality);
    }

    protected override void OnRender(DrawingContext dc)
    {
        var p = Source;
        var r = new Rect(RenderSize);
        if (p == null || r.Width <= 0 || r.Height <= 0) return;

        var img = IconStore.Get(p.Icon);
        if (img != null)
        {
            double s = Math.Min(r.Width / img.Width, r.Height / img.Height);
            double w = img.Width * s, h = img.Height * s;
            dc.DrawImage(img, new Rect((r.Width - w) / 2, (r.Height - h) / 2, w, h));
            return;
        }

        dc.DrawRectangle(Palette.Brush(Palette.Of(p)), null, r);
        var text = Palette.Label(this, Initials(p.Title), Math.Max(7, r.Height * 0.42), Brushes.White, bold: true);
        dc.DrawText(text, new Point((r.Width - text.Width) / 2, (r.Height - text.Height) / 2));
    }

    static string Initials(string title)
    {
        var words = title.Split([' ', '-', '_', '.'], StringSplitOptions.RemoveEmptyEntries);
        if (words.Length == 0) return "?";
        var s = words.Length == 1 ? words[0][..1] : $"{words[0][0]}{words[1][0]}";
        return s.ToUpperInvariant();
    }
}
