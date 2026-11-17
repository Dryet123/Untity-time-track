using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using UnityTimeTracker.Core;

namespace UnityTimeTracker.UI.Controls;

public sealed class BarChart : FrameworkElement
{
    // Value is hours, or a plain count when CountUnit is set.
    public sealed record Segment(double Value, Color Color, string Name);

    public sealed record Group(string Label, string Title, IReadOnlyList<Segment> Segments, IReadOnlyList<TipLine>? Notes = null)
    {
        public double Total => Segments.Sum(s => s.Value);
    }

    const double PadLeft = 48, PadRight = 6, PadTop = 10, PadBottom = 20;
    static readonly double[] StepsMinutes = [5, 10, 15, 30, 60, 120, 180, 240, 360, 480, 720, 1200, 1800, 3000, 6000, 12000, 30000, 60000];

    readonly HoverTip tip;
    IReadOnlyList<Group> groups = [];
    int hover = -1;
    Rect plot;

    public event Action<int>? GroupClicked;

    // e.g. ("session", "sessions") switches the chart from hours to counts.
    public (string One, string Many)? CountUnit { get; set; }

    public BarChart()
    {
        tip = new HoverTip(this);
        MinHeight = 80;
    }

    public void SetData(IReadOnlyList<Group> data)
    {
        groups = data;
        tip.Invalidate();
        if (IsMouseOver) UpdateHover(Mouse.GetPosition(this));
        else hover = -1;
        InvalidateVisual();
    }

    string FormatValue(double v) => CountUnit is var (one, many) ? Fmt.Plural((int)Math.Round(v), one, many) : Fmt.Hours(v);

    protected override void OnRender(DrawingContext dc)
    {
        dc.DrawRectangle(Brushes.Transparent, null, new Rect(RenderSize));
        plot = new Rect(PadLeft, PadTop, Math.Max(0, RenderSize.Width - PadLeft - PadRight), Math.Max(0, RenderSize.Height - PadTop - PadBottom));
        if (plot.Width < 10 || plot.Height < 10) return;

        double max = groups.Count == 0 ? 0 : groups.Max(g => g.Total);
        var (step, top) = CountUnit != null ? CountScale(max) : TimeScale(max * 60);
        double scale = CountUnit != null ? 1 : 60;

        for (double v = 0; v <= top + 0.001; v += step)
        {
            double y = Math.Round(plot.Bottom - v / top * plot.Height) + 0.5;
            dc.DrawLine(Palette.GridPen, new Point(plot.Left, y), new Point(plot.Right, y));
            var label = Palette.Label(this, CountUnit != null ? $"{v:0}" : AxisLabel(v), 11, Palette.Dim);
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
                if (s.Value <= 0) continue;
                double h = s.Value * scale / top * plot.Height;
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

        if (max <= 0)
        {
            var empty = Palette.Label(this, "No activity in this period", 12, Palette.Dim);
            dc.DrawText(empty, new Point(plot.Left + (plot.Width - empty.Width) / 2, plot.Top + (plot.Height - empty.Height) / 2));
        }
    }

    static (double Step, double Top) TimeScale(double maxMinutes)
    {
        if (maxMinutes <= 0) return (15, 60);
        foreach (var s in StepsMinutes)
            if (maxMinutes / s <= 4.5) return (s, Math.Ceiling(maxMinutes / s) * s);
        var last = StepsMinutes[^1];
        return (last, Math.Ceiling(maxMinutes / last) * last);
    }

    static (double Step, double Top) CountScale(double max)
    {
        if (max <= 0) return (1, 4);
        double magnitude = Math.Pow(10, Math.Floor(Math.Log10(max)));
        foreach (var m in new[] { 0.1, 0.2, 0.5, 1, 2, 5, 10 })
        {
            double step = Math.Max(1, m * magnitude);
            if (max / step <= 4.5) return (step, Math.Ceiling(max / step) * step);
        }
        return (magnitude * 10, Math.Ceiling(max / (magnitude * 10)) * magnitude * 10);
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

    void UpdateHover(Point p)
    {
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
        var lines = g.Segments.Where(s => s.Value > 0).OrderByDescending(s => s.Value)
            .Select(s => new TipLine(s.Color, s.Name, FormatValue(s.Value))).ToList();
        if (lines.Count == 0) lines.Add(new TipLine(null, CountUnit != null ? "None" : "No activity", ""));
        else if (lines.Count > 1) lines.Add(new TipLine(null, "Total", FormatValue(g.Total), true));
        if (g.Notes != null) lines.AddRange(g.Notes);
        tip.Show(p, i, g.Title, lines);
        Cursor = GroupClicked != null ? Cursors.Hand : null;
    }

    protected override void OnMouseMove(MouseEventArgs e) => UpdateHover(e.GetPosition(this));

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
