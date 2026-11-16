using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using UnityTimeTracker.Core;

namespace UnityTimeTracker.UI.Controls;

public sealed class BarChart : FrameworkElement
{
    public sealed record Segment(double Hours, Color Color, string Name);

    public sealed record Group(string Label, string Title, IReadOnlyList<Segment> Segments, IReadOnlyList<TipLine>? Notes = null)
    {
        public double Total => Segments.Sum(s => s.Hours);
    }

    const double PadLeft = 48, PadRight = 6, PadTop = 10, PadBottom = 20;
    static readonly double[] StepsMinutes = [5, 10, 15, 30, 60, 120, 180, 240, 360, 480, 720, 1200, 1800, 3000, 6000, 12000, 30000, 60000];

    readonly HoverTip tip;
    IReadOnlyList<Group> groups = [];
    int hover = -1;
    Rect plot;

    public event Action<int>? GroupClicked;

    public BarChart()
    {
        tip = new HoverTip(this);
        MinHeight = 80;
    }

    public void SetData(IReadOnlyList<Group> data)
    {
        groups = data;
        hover = -1;
        tip.Hide();
        InvalidateVisual();
    }

    protected override void OnRender(DrawingContext dc)
    {
        dc.DrawRectangle(Brushes.Transparent, null, new Rect(RenderSize));
        plot = new Rect(PadLeft, PadTop, Math.Max(0, RenderSize.Width - PadLeft - PadRight), Math.Max(0, RenderSize.Height - PadTop - PadBottom));
        if (plot.Width < 10 || plot.Height < 10) return;

        double maxHours = groups.Count == 0 ? 0 : groups.Max(g => g.Total);
        var (step, top) = Scale(maxHours * 60);

        for (double m = 0; m <= top + 0.001; m += step)
        {
            double y = Math.Round(plot.Bottom - m / top * plot.Height) + 0.5;
            dc.DrawLine(Palette.GridPen, new Point(plot.Left, y), new Point(plot.Right, y));
            var label = Palette.Label(this, AxisLabel(m), 11, Palette.Dim);
            dc.DrawText(label, new Point(plot.Left - 7 - label.Width, y - label.Height / 2));
        }

        int n = groups.Count;
        if (n == 0) return;
        double slot = plot.Width / n;
        double barWidth = Math.Max(1, Math.Min(slot * 0.72, 56));
        if (hover >= 0 && hover < n)
            dc.DrawRectangle(Palette.Hover, null, new Rect(plot.Left + slot * hover, plot.Top, slot, plot.Height));

        for (int i = 0; i < n; i++)
        {
            double x = plot.Left + slot * i + (slot - barWidth) / 2;
            double y = plot.Bottom;
            foreach (var s in groups[i].Segments)
            {
                if (s.Hours <= 0) continue;
                double h = s.Hours * 60 / top * plot.Height;
                dc.DrawRectangle(Palette.Brush(s.Color), null, new Rect(x, y - h, barWidth, h));
                y -= h;
            }
        }

        double widest = groups.Max(g => Palette.Label(this, g.Label, 11, Palette.Dim).Width) + 8;
        int every = Math.Max(1, (int)Math.Ceiling(widest / slot));
        for (int i = 0; i < n; i += every)
        {
            var label = Palette.Label(this, groups[i].Label, 11, i == hover ? Palette.Strong : Palette.Dim);
            dc.DrawText(label, new Point(plot.Left + slot * i + slot / 2 - label.Width / 2, plot.Bottom + 5));
        }

        if (maxHours <= 0)
        {
            var empty = Palette.Label(this, "No activity in this period", 12, Palette.Dim);
            dc.DrawText(empty, new Point(plot.Left + (plot.Width - empty.Width) / 2, plot.Top + (plot.Height - empty.Height) / 2));
        }
    }

    static (double Step, double Top) Scale(double maxMinutes)
    {
        if (maxMinutes <= 0) return (15, 60);
        foreach (var s in StepsMinutes)
            if (maxMinutes / s <= 4.5) return (s, Math.Ceiling(maxMinutes / s) * s);
        var last = StepsMinutes[^1];
        return (last, Math.Ceiling(maxMinutes / last) * last);
    }

    static string AxisLabel(double minutes)
    {
        int m = (int)Math.Round(minutes);
        if (m < 60) return $"{m}m";
        return m % 60 == 0 ? $"{m / 60}h" : $"{m / 60}h{m % 60:00}";
    }

    int IndexAt(Point p)
    {
        int n = groups.Count;
        if (n == 0 || plot.Width <= 0 || p.X < plot.Left || p.X >= plot.Right || p.Y < plot.Top || p.Y > plot.Bottom + PadBottom) return -1;
        return Math.Min(n - 1, (int)((p.X - plot.Left) / (plot.Width / n)));
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        var p = e.GetPosition(this);
        int i = IndexAt(p);
        if (i != hover)
        {
            hover = i;
            InvalidateVisual();
        }
        if (i < 0)
        {
            tip.Hide();
            return;
        }

        var g = groups[i];
        var lines = g.Segments.Where(s => s.Hours > 0).OrderByDescending(s => s.Hours)
            .Select(s => new TipLine(s.Color, s.Name, Fmt.Hours(s.Hours))).ToList();
        if (lines.Count == 0) lines.Add(new TipLine(null, "No activity", ""));
        else if (lines.Count > 1) lines.Add(new TipLine(null, "Total", Fmt.Hours(g.Total), true));
        if (g.Notes != null) lines.AddRange(g.Notes);
        tip.Show(p, i, g.Title, lines);
        Cursor = GroupClicked != null ? Cursors.Hand : null;
    }

    protected override void OnMouseLeave(MouseEventArgs e)
    {
        hover = -1;
        InvalidateVisual();
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        int i = IndexAt(e.GetPosition(this));
        if (i >= 0) GroupClicked?.Invoke(i);
    }
}
