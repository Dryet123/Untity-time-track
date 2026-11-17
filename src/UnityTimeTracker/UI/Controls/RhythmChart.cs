using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using UnityTimeTracker.Core;

namespace UnityTimeTracker.UI.Controls;

// Weekday x hour grid showing when work usually happens.
public sealed class RhythmChart : FrameworkElement
{
    const double Left = 34, Top = 4, Bottom = 18, Gap = 2;
    static readonly string[] Days = ["Mon", "Tue", "Wed", "Thu", "Fri", "Sat", "Sun"];
    static readonly string[] Plural = ["Mondays", "Tuesdays", "Wednesdays", "Thursdays", "Fridays", "Saturdays", "Sundays"];

    readonly HoverTip tip;
    double[,] grid = new double[7, 24];
    double max;
    (int Day, int Hour)? hover;

    public RhythmChart()
    {
        tip = new HoverTip(this);
        Height = 7 * 18 + Top + Bottom;
    }

    public void SetData(double[,] data)
    {
        grid = data;
        max = 0;
        foreach (var v in data) max = Math.Max(max, v);
        tip.Invalidate();
        InvalidateVisual();
    }

    double CellW => Math.Max(1, (RenderSize.Width - Left) / 24);
    double CellH => Math.Max(1, (RenderSize.Height - Top - Bottom) / 7);

    protected override void OnRender(DrawingContext dc)
    {
        dc.DrawRectangle(Brushes.Transparent, null, new Rect(RenderSize));
        double cw = CellW, ch = CellH;
        for (int d = 0; d < 7; d++)
        {
            var label = Palette.Label(this, Days[d], 10, Palette.Dim);
            dc.DrawText(label, new Point(Left - 6 - label.Width, Top + d * ch + (ch - label.Height) / 2));
            for (int h = 0; h < 24; h++)
            {
                var r = new Rect(Left + h * cw, Top + d * ch, Math.Max(1, cw - Gap), Math.Max(1, ch - Gap));
                double f = max > 0 ? grid[d, h] / max : 0;
                var color = f <= 0 ? Palette.Heat[0] : Palette.Heat[Math.Clamp(1 + (int)(f * 4.999), 1, 5)];
                dc.DrawRectangle(Palette.Brush(color), null, r);
                if (hover == (d, h)) dc.DrawRectangle(null, Palette.TodayPen, new Rect(r.X + 0.5, r.Y + 0.5, r.Width - 1, r.Height - 1));
            }
        }
        for (int h = 0; h < 24; h += cw < 22 ? 3 : 2)
        {
            var label = Palette.Label(this, $"{h:00}", 10, Palette.Dim);
            dc.DrawText(label, new Point(Left + h * cw, Top + 7 * ch + 3));
        }
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        var p = e.GetPosition(this);
        int h = (int)((p.X - Left) / CellW), d = (int)((p.Y - Top) / CellH);
        (int, int)? at = p.X >= Left && h is >= 0 and < 24 && d is >= 0 and < 7 ? (d, h) : null;
        if (at != hover)
        {
            hover = at;
            InvalidateVisual();
        }
        if (at is not var (day, hour))
        {
            tip.Hide();
            return;
        }
        tip.Show(p, (day, hour), $"{Plural[day]}, {hour:00}:00 - {(hour + 1) % 24:00}:00",
            [new TipLine(null, "Total", Fmt.Hours(grid[day, hour]))]);
    }

    protected override void OnMouseLeave(MouseEventArgs e)
    {
        hover = null;
        InvalidateVisual();
    }
}
