using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using UnityTimeTracker.Core;

namespace UnityTimeTracker.UI.Controls;

// One day on a 24h axis with three lanes: Unity open, in Unity, in IDE.
public sealed class DayTimeline : FrameworkElement
{
    const double LabelWidth = 76, LaneHeight = 16, LaneGap = 6, AxisHeight = 18;
    static readonly (SpanKind Kind, string Name)[] Lanes = [(SpanKind.Open, "Unity open"), (SpanKind.Editor, "In Unity"), (SpanKind.Ide, "In IDE")];

    readonly HoverTip tip;
    TimeRange range;
    List<SpanRec> spans = [];
    DataSnapshot? snap;

    public DayTimeline()
    {
        tip = new HoverTip(this);
        Height = Lanes.Length * (LaneHeight + LaneGap) + AxisHeight;
    }

    public void SetData(TimeRange range, IEnumerable<SpanRec> spans, DataSnapshot snap)
    {
        this.range = range;
        this.spans = spans.Where(s => s.End > range.From && s.Start < range.To).ToList();
        this.snap = snap;
        tip.Hide();
        InvalidateVisual();
    }

    double Width0 => Math.Max(1, RenderSize.Width - LabelWidth);

    double X(DateTime t) => LabelWidth + Math.Clamp((t - range.From) / (range.To - range.From), 0, 1) * Width0;

    protected override void OnRender(DrawingContext dc)
    {
        dc.DrawRectangle(Brushes.Transparent, null, new Rect(RenderSize));
        if (range.To <= range.From) return;

        for (int lane = 0; lane < Lanes.Length; lane++)
        {
            double y = lane * (LaneHeight + LaneGap);
            var label = Palette.Label(this, Lanes[lane].Name, 11, Palette.Dim);
            dc.DrawText(label, new Point(0, y + (LaneHeight - label.Height) / 2));
            dc.DrawRectangle(Palette.Empty, null, new Rect(LabelWidth, y, Width0, LaneHeight));

            foreach (var s in spans.Where(s => s.Kind == Lanes[lane].Kind))
            {
                var color = Palette.Of(snap?.Get(s.ProjectId));
                if (s.Kind == SpanKind.Open) color = Palette.Alpha(color, 0x80);
                double x1 = X(s.Start), x2 = X(s.End);
                dc.DrawRectangle(Palette.Brush(color), null, new Rect(x1, y, Math.Max(1, x2 - x1), LaneHeight));
            }
        }

        double axisY = Lanes.Length * (LaneHeight + LaneGap);
        int hours = (int)Math.Round((range.To - range.From).TotalHours);
        int step = Width0 / Math.Max(1, hours) < 26 ? 3 : 2;
        for (int h = 0; h <= hours; h += step)
        {
            var t = range.From.AddHours(h);
            double x = Math.Round(X(t)) + 0.5;
            dc.DrawLine(Palette.GridPen, new Point(x, axisY - LaneGap), new Point(x, axisY + 3));
            if (h == hours) continue;
            var label = Palette.Label(this, $"{t.Hour:00}", 10, Palette.Dim);
            dc.DrawText(label, new Point(x + 2, axisY + 2));
        }

        var now = DateTime.Now;
        if (range.Contains(now))
        {
            double x = Math.Round(X(now)) + 0.5;
            dc.DrawLine(Palette.NowPen, new Point(x, 0), new Point(x, axisY - LaneGap));
        }
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        var p = e.GetPosition(this);
        int lane = (int)(p.Y / (LaneHeight + LaneGap));
        SpanRec? hit = null;
        if (lane is >= 0 and < 3 && p.X >= LabelWidth && p.Y - lane * (LaneHeight + LaneGap) <= LaneHeight)
            hit = spans.Where(s => s.Kind == Lanes[lane].Kind).LastOrDefault(s => X(s.Start) - 2 <= p.X && p.X <= X(s.End) + 2);

        if (hit == null || snap == null)
        {
            tip.Hide();
            return;
        }
        var project = snap.Get(hit.ProjectId);
        var lines = new List<TipLine> { new(Palette.Of(project), Lanes[lane].Name, $"{Fmt.Time(hit.Start)} - {Fmt.Time(hit.End)}") };
        if (hit.Scene != null) lines.Add(new TipLine(null, "Scene", hit.Scene));
        lines.Add(new TipLine(null, "Duration", Fmt.Dur(hit.Duration), true));
        tip.Show(p, hit, project?.Title ?? "Unknown", lines);
    }
}
