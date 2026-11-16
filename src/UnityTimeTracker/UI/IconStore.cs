using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using UnityTimeTracker.Core;

namespace UnityTimeTracker.UI;

public static class IconStore
{
    const int MaxSize = 128;
    static readonly Dictionary<string, BitmapSource?> cache = [];

    public static BitmapSource? Get(string? name)
    {
        if (string.IsNullOrEmpty(name)) return null;
        if (cache.TryGetValue(name, out var cached)) return cached;
        BitmapSource? image = null;
        try
        {
            var bi = new BitmapImage();
            bi.BeginInit();
            bi.CacheOption = BitmapCacheOption.OnLoad;
            bi.UriSource = new Uri(Path.Combine(AppPaths.Icons, name));
            bi.EndInit();
            bi.Freeze();
            image = bi;
        }
        catch (Exception ex) { Log.Error(ex); }
        cache[name] = image;
        return image;
    }

    // Copies the image into the app folder as a PNG no larger than 128px; returns the stored file name.
    public static string Import(string file, long projectId)
    {
        BitmapSource src;
        using (var fs = File.OpenRead(file))
        {
            var decoder = BitmapDecoder.Create(fs, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
            src = decoder.Frames.OrderByDescending(f => f.PixelWidth * f.PixelHeight).First();
        }

        double scale = Math.Min(1.0, (double)MaxSize / Math.Max(src.PixelWidth, src.PixelHeight));
        int w = Math.Max(1, (int)Math.Round(src.PixelWidth * scale));
        int h = Math.Max(1, (int)Math.Round(src.PixelHeight * scale));

        var visual = new DrawingVisual();
        RenderOptions.SetBitmapScalingMode(visual, BitmapScalingMode.HighQuality);
        using (var dc = visual.RenderOpen())
            dc.DrawImage(src, new Rect(0, 0, w, h));
        var target = new RenderTargetBitmap(w, h, 96, 96, PixelFormats.Pbgra32);
        target.Render(visual);

        var name = $"{projectId}-{DateTime.UtcNow.Ticks}.png";
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(target));
        using var output = File.Create(Path.Combine(AppPaths.Icons, name));
        encoder.Save(output);
        return name;
    }

    public static void Delete(string? name)
    {
        if (string.IsNullOrEmpty(name)) return;
        cache.Remove(name);
        try { File.Delete(Path.Combine(AppPaths.Icons, name)); }
        catch (IOException) { }
    }
}
