using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using UnityTimeTracker.Core;

namespace UnityTimeTracker.UI.Controls;

// GitHub-style year grid: columns are weeks, rows are Mon..Sun.
public sealed class YearHeatmap : FrameworkElement
{
    const double Gap = 3, Left = 30, Top = 18, Legend = 26;
    const int Columns = 54;
    static readonly string[] RowLabels = ["Mon", "", "Wed", "", "Fri", "", ""];

    readonly HoverTip tip;
    int year = DateTime.Today.Year;
    DataSnapshot? snap;
    Dictionary<DateOnly, Dictionary<long, TimeSpan>> data = [];
    DateOnly? selected;
    DateOnly? hover;
    double cell = 12;

    public event Action<DateOnly>? DayClicked;

    public YearHeatmap()
    {
        tip = new HoverTip(this);
        Cursor = Cursors.Hand;
    }

    public void SetData(int year, DataSnapshot snap, Dictionary<DateOnly, Dictionary<long, TimeSpan>> data, DateOnly? selected)
    {
        this.year = year;
        this.snap = snap;
        this.data = data;
        this.selected = selected;
        tip.Invalidate();
        InvalidateVisual();
    }

    DateOnly First => new(year, 1, 1);
    int Lead => Clock.Weekday(First);

    protected override Size MeasureOverride(Size available)
    {
        double width = double.IsInfinity(available.Width) ? Left + Columns * 15 : available.Width;
        cell = Math.Clamp((width - Left) / Columns - Gap, 8, 15);
        return new Size(Left + Columns * (cell + Gap), Top + 7 * (cell + Gap) + Legend);
    }

    Rect CellRect(DateOnly d)
    {
        int index = d.DayNumber - First.DayNumber + Lead;
        return new Rect(Left + index / 7 * (cell + Gap), Top + index % 7 * (cell + Gap), cell, cell);
    }

    protected override void OnRender(DrawingContext dc)
    {
        dc.DrawRectangle(Brushes.Transparent, null, new Rect(RenderSize));
        var today = Clock.Today;

        for (int m = 1; m <= 12; m++)
        {
            var first = new DateOnly(year, m, 1);
            var label = Palette.Label(this, Fmt.Format(first, "MMM"), 11, Palette.Dim);
            dc.DrawText(label, new Point(CellRect(first).X, 0));
        }
        for (int r = 0; r < 7; r++)
        {
            if (RowLabels[r].Length == 0) continue;
            var label = Palette.Label(this, RowLabels[r], 10, Palette.Dim);
            dc.DrawText(label, new Point(Left - 6 - label.Width, Top + r * (cell + Gap) + (cell - label.Height) / 2));
        }

        for (var d = First; d.Year == year; d = d.AddDays(1))
        {
            var rect = CellRect(d);
            var color = d > today ? Palette.C(0x333333) : Palette.HeatOf(Stats.Total(data.GetValueOrDefault(d)));
            dc.DrawRectangle(Palette.Brush(color), null, rect);
            if (d == hover) dc.DrawRectangle(null, Palette.TodayPen, Inset(rect, 0.5));
            if (d == today) dc.DrawRectangle(null, Palette.TodayPen, Inset(rect, 0.5));
            if (d == selected) dc.DrawRectangle(null, Palette.SelectPen, Inset(rect, 1));
        }

        double y = Top + 7 * (cell + Gap) + 8;
        var more = Palette.Label(this, "More", 11, Palette.Dim);
        double x = Left + Columns * (cell + Gap) - Gap - more.Width;
        dc.DrawText(more, new Point(x, y + (cell - more.Height) / 2));
        for (int i = Palette.Heat.Length - 1; i >= 0; i--)
        {
            x -= cell + Gap;
            dc.DrawRectangle(Palette.Brush(Palette.Heat[i]), null, new Rect(x, y, cell, cell));
        }
        var less = Palette.Label(this, "Less", 11, Palette.Dim);
        dc.DrawText(less, new Point(x - 6 - less.Width, y + (cell - less.Height) / 2));
    }

    static Rect Inset(Rect r, double by) => new(r.X + by, r.Y + by, Math.Max(0, r.Width - 2 * by), Math.Max(0, r.Height - 2 * by));

    DateOnly? DayAt(Point p)
    {
        if (p.X < Left || p.Y < Top) return null;
        int col = (int)((p.X - Left) / (cell + Gap)), row = (int)((p.Y - Top) / (cell + Gap));
        if (row > 6 || col >= Columns) return null;
        var d = First.AddDays(col * 7 + row - Lead);
        return d.Year == year ? d : null;
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
        tip.Show(p, day, Fmt.Day(day), HoverTip.DayLines(snap, day, data.GetValueOrDefault(day)));
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
