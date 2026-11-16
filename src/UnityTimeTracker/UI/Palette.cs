using System.Globalization;
using System.Windows;
using System.Windows.Media;
using UnityTimeTracker.Core;

namespace UnityTimeTracker.UI;

public static class Palette
{
    public static readonly Color TextColor = C(0xC8C8C8);
    public static readonly Color StrongColor = C(0xE2E2E2);
    public static readonly Color DimColor = C(0x8F8F8F);
    public static readonly Color AccentColor = C(0x3A79BB);
    public static readonly Color EmptyColor = C(0x2F2F2F);

    public static readonly SolidColorBrush Text = B(TextColor);
    public static readonly SolidColorBrush Strong = B(StrongColor);
    public static readonly SolidColorBrush Dim = B(DimColor);
    public static readonly SolidColorBrush Accent = B(AccentColor);
    public static readonly SolidColorBrush Empty = B(EmptyColor);
    public static readonly SolidColorBrush Hover = B(Color.FromArgb(0x16, 0xFF, 0xFF, 0xFF));

    public static readonly Pen GridPen = P(C(0x444444), 1);
    public static readonly Pen SelectPen = P(C(0x7AB4F0), 2);
    public static readonly Pen TodayPen = P(C(0xA8A8A8), 1);
    public static readonly Pen NowPen = P(C(0xE0A040), 1);

    // Heatmap levels: none, <30m, <1h, <2h, <4h, 4h+
    public static readonly Color[] Heat = [C(0x2F2F2F), C(0x1F3B58), C(0x255381), C(0x2D6BA8), C(0x3A86CC), C(0x66A8EA)];

    static readonly FontFamily Font = new("Arial");
    static readonly Typeface Regular = new(Font, FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);
    static readonly Typeface BoldFace = new(Font, FontStyles.Normal, FontWeights.Bold, FontStretches.Normal);
    static readonly Dictionary<Color, SolidColorBrush> brushes = [];

    public static Color C(int rgb) => Color.FromRgb((byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb);

    public static SolidColorBrush B(Color c)
    {
        var b = new SolidColorBrush(c);
        b.Freeze();
        return b;
    }

    static Pen P(Color c, double width)
    {
        var p = new Pen(B(c), width);
        p.Freeze();
        return p;
    }

    public static SolidColorBrush Brush(Color c)
    {
        if (!brushes.TryGetValue(c, out var b)) brushes[c] = b = B(c);
        return b;
    }

    public static Color Parse(string? hex)
    {
        try
        {
            if (hex != null && ColorConverter.ConvertFromString(hex) is Color c) return c;
        }
        catch (FormatException) { }
        return AccentColor;
    }

    public static Color Of(Project? p) => p == null ? AccentColor : Parse(p.Color);

    public static Color Alpha(Color c, byte a) => Color.FromArgb(a, c.R, c.G, c.B);

    public static Color HeatOf(TimeSpan t)
    {
        int i = t <= TimeSpan.Zero ? 0
            : t < TimeSpan.FromMinutes(30) ? 1
            : t < TimeSpan.FromHours(1) ? 2
            : t < TimeSpan.FromHours(2) ? 3
            : t < TimeSpan.FromHours(4) ? 4 : 5;
        return Heat[i];
    }

    public static FormattedText Label(Visual v, string s, double size, Brush brush, bool bold = false) =>
        new(s, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, bold ? BoldFace : Regular, size, brush,
            VisualTreeHelper.GetDpi(v).PixelsPerDip);
}
