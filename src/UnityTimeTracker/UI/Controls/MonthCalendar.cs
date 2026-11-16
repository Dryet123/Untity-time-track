using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using UnityTimeTracker.Core;

namespace UnityTimeTracker.UI.Controls;

public sealed class MonthCalendar : FrameworkElement
{
    const double HeadHeight = 22;
    static readonly string[] Names = ["Mon", "Tue", "Wed", "Thu", "Fri", "Sat", "Sun"];

    readonly HoverTip tip;
    DateOnly month = new(DateTime.Today.Year, DateTime.Today.Month, 1);
    DataSnapshot? snap;
    Dictionary<DateOnly, Dictionary<long, TimeSpan>> data = [];
    DateOnly? selected;
    DateOnly? hover;

    public event Action<DateOnly>? DayClicked;

    public MonthCalendar()
    {
        tip = new HoverTip(this);
        Cursor = Cursors.Hand;
    }

    public void SetData(DateOnly month, DataSnapshot snap, Dictionary<DateOnly, Dictionary<long, TimeSpan>> data, DateOnly? selected)
    {
        this.month = new DateOnly(month.Year, month.Month, 1);
        this.snap = snap;
        this.data = data;
        this.selected = selected;
        tip.Hide();
        InvalidateVisual();
    }

    DateOnly GridStart => Clock.Monday(month);
    double CellWidth => RenderSize.Width / 7;
    double CellHeight => (RenderSize.Height - HeadHeight) / 6;

    protected override void OnRender(DrawingContext dc)
    {
        dc.DrawRectangle(Brushes.Transparent, null, new Rect(RenderSize));
        double cw = CellWidth, ch = CellHeight;
        if (cw <= 4 || ch <= 4) return;

        for (int i = 0; i < 7; i++)
        {
            var label = Palette.Label(this, Names[i], 11, Palette.Dim);
            dc.DrawText(label, new Point(i * cw + 6, (HeadHeight - label.Height) / 2 - 2));
        }

        var today = Clock.Today;
        var max = data.Count > 0 ? data.Values.Max(Stats.Total) : TimeSpan.Zero;

        for (int i = 0; i < 42; i++)
        {
            var d = GridStart.AddDays(i);
            bool inMonth = d.Month == month.Month;
            var r = new Rect(i % 7 * cw + 1, HeadHeight + i / 7 * ch + 1, cw - 2, ch - 2);
            var bg = d == hover ? 0x474747 : inMonth ? 0x3F3F3F : 0x333333;
            dc.DrawRectangle(Palette.Brush(Palette.C(bg)), null, r);

            var number = Palette.Label(this, d.Day.ToString(), 12, d == today ? Palette.Accent : inMonth ? Palette.Text : Palette.Dim, d == today);
            dc.DrawText(number, new Point(r.X + 6, r.Y + 5));

            var map = data.GetValueOrDefault(d);
            var total = Stats.Total(map);
            if (total > TimeSpan.Zero && map != null)
            {
                var label = Palette.Label(this, Fmt.Dur(total), 12, inMonth ? Palette.Strong : Palette.Dim);
                dc.DrawText(label, new Point(r.X + 6, r.Bottom - 12 - label.Height));

                double width = (r.Width - 12) * (total / max);
                double x = r.X + 6;
                foreach (var (id, t) in map.OrderByDescending(kv => kv.Value))
                {
                    double w = width * (t / total);
                    dc.DrawRectangle(Palette.Brush(Palette.Of(snap?.Get(id))), null, new Rect(x, r.Bottom - 8, Math.Max(1, w), 4));
                    x += w;
                }
            }

            if (d == selected) dc.DrawRectangle(null, Palette.SelectPen, new Rect(r.X + 1, r.Y + 1, r.Width - 2, r.Height - 2));
        }
    }

    DateOnly? DayAt(Point p)
    {
        if (p.Y < HeadHeight || CellWidth <= 0) return null;
        int col = (int)(p.X / CellWidth), row = (int)((p.Y - HeadHeight) / CellHeight);
        if (col is < 0 or > 6 || row is < 0 or > 5) return null;
        return GridStart.AddDays(row * 7 + col);
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        var p = e.GetPosition(this);
        var d = DayAt(p);
        if (d != hover)
        {
            hover = d;
            InvalidateVisual();
        }
        if (d is not DateOnly day || snap == null)
        {
            tip.Hide();
            return;
        }
        tip.Show(p, day, Fmt.Day(day), HoverTip.Breakdown(snap, data.GetValueOrDefault(day)));
    }

    protected override void OnMouseLeave(MouseEventArgs e)
    {
        hover = null;
        InvalidateVisual();
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        if (DayAt(e.GetPosition(this)) is DateOnly d) DayClicked?.Invoke(d);
    }
}
