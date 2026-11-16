using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;

namespace UnityTimeTracker.UI.Controls;

public sealed record Stat(string Label, string Value, string? Sub = null);

public sealed class StatStrip : UniformGrid
{
    public StatStrip()
    {
        Columns = 6;
    }

    public void Set(params Stat[] stats)
    {
        Children.Clear();
        foreach (var s in stats)
        {
            var cell = new StackPanel { Margin = new Thickness(0, 2, 14, 10) };
            cell.Children.Add(new TextBlock { Text = s.Label, Foreground = Palette.Dim, FontSize = 11, TextTrimming = TextTrimming.CharacterEllipsis });
            cell.Children.Add(new TextBlock { Text = s.Value, Foreground = Palette.Strong, FontSize = 18, Margin = new Thickness(0, 4, 0, 0), TextTrimming = TextTrimming.CharacterEllipsis });
            if (s.Sub != null)
                cell.Children.Add(new TextBlock { Text = s.Sub, Foreground = Palette.Dim, FontSize = 11, Margin = new Thickness(0, 3, 0, 0), TextTrimming = TextTrimming.CharacterEllipsis });
            Children.Add(cell);
        }
    }
}
