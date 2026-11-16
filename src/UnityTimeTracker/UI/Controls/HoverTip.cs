using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Media;
using UnityTimeTracker.Core;
using Rectangle = System.Windows.Shapes.Rectangle;

namespace UnityTimeTracker.UI.Controls;

public readonly record struct TipLine(Color? Swatch, string Name, string Value, bool Strong = false);

// Lightweight tooltip that follows the mouse over custom-drawn charts.
public sealed class HoverTip
{
    readonly Popup popup;
    readonly StackPanel body = new();
    object? key;

    public HoverTip(FrameworkElement owner)
    {
        var border = new Border
        {
            Background = Palette.B(Palette.C(0x1F1F1F)),
            BorderBrush = Palette.B(Palette.C(0x0F0F0F)),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(9, 7, 9, 7),
            Child = body,
        };
        TextElement.SetFontFamily(border, new FontFamily("Arial"));
        TextElement.SetFontSize(border, 12);
        TextElement.SetForeground(border, Palette.Text);
        TextOptions.SetTextFormattingMode(border, TextFormattingMode.Display);

        popup = new Popup
        {
            Child = border,
            PlacementTarget = owner,
            Placement = PlacementMode.Relative,
            AllowsTransparency = true,
            IsHitTestVisible = false,
        };
        owner.MouseLeave += (_, _) => Hide();
    }

    public void Show(Point at, object key, string header, IEnumerable<TipLine> lines)
    {
        if (!Equals(key, this.key))
        {
            Build(header, lines);
            this.key = key;
        }
        popup.HorizontalOffset = at.X + 16;
        popup.VerticalOffset = at.Y + 18;
        popup.IsOpen = true;
    }

    public void Hide()
    {
        popup.IsOpen = false;
        key = null;
    }

    void Build(string header, IEnumerable<TipLine> lines)
    {
        body.Children.Clear();
        body.Children.Add(new TextBlock { Text = header, FontWeight = FontWeights.Bold, Foreground = Palette.Strong, Margin = new Thickness(0, 0, 0, 5) });
        foreach (var line in lines)
        {
            var row = new Grid { Margin = new Thickness(0, 1, 0, 1) };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            if (line.Swatch is Color c)
                row.Children.Add(new Rectangle { Width = 9, Height = 9, Fill = Palette.Brush(c), Margin = new Thickness(0, 0, 7, 0), VerticalAlignment = VerticalAlignment.Center });

            var name = new TextBlock { Text = line.Name, Margin = new Thickness(0, 0, 18, 0) };
            var value = new TextBlock { Text = line.Value, HorizontalAlignment = HorizontalAlignment.Right };
            if (line.Strong)
            {
                name.FontWeight = value.FontWeight = FontWeights.Bold;
                row.Margin = new Thickness(0, 4, 0, 0);
            }
            Grid.SetColumn(name, 1);
            Grid.SetColumn(value, 2);
            row.Children.Add(name);
            row.Children.Add(value);
            body.Children.Add(row);
        }
    }

    public static List<TipLine> Breakdown(DataSnapshot snap, Dictionary<long, TimeSpan>? perProject)
    {
        if (perProject == null || perProject.Count == 0) return [new TipLine(null, "No activity", "")];
        var lines = perProject.OrderByDescending(kv => kv.Value)
            .Select(kv => new TipLine(Palette.Of(snap.Get(kv.Key)), snap.Get(kv.Key)?.Title ?? "Unknown", Fmt.Dur(kv.Value)))
            .ToList();
        if (lines.Count > 1) lines.Add(new TipLine(null, "Total", Fmt.Dur(Stats.Total(perProject)), true));
        return lines;
    }
}
