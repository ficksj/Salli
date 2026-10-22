using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Win32;
using ColorConverter = System.Windows.Media.ColorConverter;
using Color = System.Windows.Media.Color;

namespace Salli;

/// <summary>Palette + accent colour, swapped live between dark and light.</summary>
public static class Theme
{
    public static event Action? Changed;

    public static bool IsLight { get; private set; }
    public static Color Accent { get; private set; } = Color.FromRgb(0x0A, 0x84, 0xFF);

    public static SolidColorBrush AccentBrush { get; private set; } =
        Freeze(Color.FromRgb(0x0A, 0x84, 0xFF));

    public static void Apply(AppSettings s)
    {
        IsLight = string.Equals(s.Theme, "light", StringComparison.OrdinalIgnoreCase);

        var preset = (s.ThemePreset ?? "graphite").ToLowerInvariant();

        var presetAccent = preset switch
        {
            "midnight" => Color.FromRgb(0x4C, 0x8D, 0xFF),
            "nord"     => Color.FromRgb(0x88, 0xC0, 0xD0),
            "mono"     => Color.FromRgb(0xE6, 0xE6, 0xE9),
            "sand"     => Color.FromRgb(0xE0, 0xA4, 0x58),
            _          => Color.FromRgb(0x0A, 0x84, 0xFF)
        };

        Accent = string.Equals(s.AccentMode, "preset", StringComparison.OrdinalIgnoreCase)
            ? presetAccent
            : string.Equals(s.AccentMode, "wallpaper", StringComparison.OrdinalIgnoreCase)
                ? WallpaperAccent() ?? Parse(s.AccentColor, presetAccent)
                : Parse(s.AccentColor, presetAccent);

        AccentBrush = Freeze(Accent);

        // glass = translucent surface; the window itself stays layered anyway
        var surface = preset switch
        {
            "midnight" => Color.FromRgb(0x0B, 0x12, 0x20),
            "nord"     => Color.FromRgb(0x1B, 0x20, 0x27),
            "mono"     => Color.FromRgb(0x11, 0x11, 0x13),
            "sand"     => Color.FromRgb(0x20, 0x1A, 0x14),
            _          => Color.FromRgb(0x14, 0x14, 0x18)
        };

        var surfaceAlpha = s.Glass ? (byte)0xE2 : (byte)0xFF;

        if (IsLight)
        {
            Set("SurfaceBrush",      Color.FromArgb(surfaceAlpha, 0xF2, 0xF2, 0xF7));
            Set("SurfaceSolidBrush", Color.FromRgb(0xF2, 0xF2, 0xF7));
            Set("CardBrush",         Color.FromArgb(0x12, 0x00, 0x00, 0x00));
            Set("CardSoftBrush",     Color.FromArgb(0x0A, 0x00, 0x00, 0x00));
            Set("TrackBrush",        Color.FromArgb(0x26, 0x00, 0x00, 0x00));
            Set("TextBrush",         Color.FromRgb(0x1C, 0x1C, 0x1E));
            Set("SubBrush",          Color.FromRgb(0x6E, 0x6E, 0x73));
            Set("MutedBrush",        Color.FromRgb(0x48, 0x48, 0x4A));
            Set("ChipOnBrush",       Color.FromRgb(0x1C, 0x1C, 0x1E));
            Set("ChipOnInkBrush",    Color.FromRgb(0xFF, 0xFF, 0xFF));
            Set("ChipOnSubBrush",    Color.FromRgb(0xC7, 0xC7, 0xCC));
            Set("DividerBrush",      Color.FromArgb(0x1F, 0x00, 0x00, 0x00));
            Set("GoodBrush",         Color.FromRgb(0x24, 0x8A, 0x3D));
            Set("WarnBrush",         Color.FromRgb(0xC9, 0x34, 0x00));
            Set("BadBrush",          Color.FromRgb(0xD7, 0x00, 0x15));
        }
        else
        {
            Set("SurfaceBrush",      Color.FromArgb(surfaceAlpha, surface.R, surface.G, surface.B));
            Set("SurfaceSolidBrush", surface);
            Set("CardBrush",         Color.FromArgb(0x1F, 0xFF, 0xFF, 0xFF));
            Set("CardSoftBrush",     Color.FromArgb(0x14, 0xFF, 0xFF, 0xFF));
            Set("TrackBrush",        Color.FromArgb(0x2E, 0xFF, 0xFF, 0xFF));
            Set("TextBrush",         Color.FromRgb(0xF5, 0xF5, 0xF7));
            Set("SubBrush",          Color.FromRgb(0x8E, 0x8E, 0x93));
            Set("MutedBrush",        Color.FromRgb(0xAE, 0xAE, 0xB2));
            Set("ChipOnBrush",       Color.FromArgb(0xF2, 0xF5, 0xF5, 0xF7));
            Set("ChipOnInkBrush",    surface);
            Set("ChipOnSubBrush",    Color.FromRgb(0x4A, 0x4A, 0x4E));
            Set("DividerBrush",      Color.FromArgb(0x1A, 0xFF, 0xFF, 0xFF));
            Set("GoodBrush",         Color.FromRgb(0x30, 0xD1, 0x58));
            Set("WarnBrush",         Color.FromRgb(0xFF, 0x9F, 0x0A));
            Set("BadBrush",          Color.FromRgb(0xFF, 0x45, 0x3A));
        }

        Set("AccentBrush",      Accent);
        Set("AccentSoftBrush",  Color.FromArgb(0x33, Accent.R, Accent.G, Accent.B));
        Set("AccentInkBrush",   IsLight ? Colors.White : Color.FromRgb(0x14, 0x14, 0x18));

        Changed?.Invoke();
    }

    private static void Set(string key, Color c)
    {
        if (Application.Current is null) return;
        var brush = Freeze(c);
        Application.Current.Resources[key] = brush;
    }

    public static SolidColorBrush Brush(string key, Color fallback)
    {
        if (Application.Current?.Resources[key] is SolidColorBrush b) return b;
        return Freeze(fallback);
    }

    private static SolidColorBrush Freeze(Color c)
    {
        var b = new SolidColorBrush(c);
        b.Freeze();
        return b;
    }

    private static Color Parse(string hex, Color fallback)
    {
        try
        {
            return (Color)ColorConverter.ConvertFromString(hex);
        }
        catch
        {
            return fallback;
        }
    }

    /// <summary>Most vivid dominant colour of the desktop wallpaper.</summary>
    private static Color? WallpaperAccent()
    {
        try
        {
            var path = Registry.GetValue(
                @"HKEY_CURRENT_USER\Control Panel\Desktop", "WallPaper", null) as string;

            if (string.IsNullOrWhiteSpace(path) || !System.IO.File.Exists(path)) return null;

            var bmp = new BitmapImage();
            bmp.BeginInit();
            bmp.CacheOption   = BitmapCacheOption.OnLoad;
            bmp.CreateOptions = BitmapCreateOptions.IgnoreImageCache;
            bmp.UriSource     = new Uri(path);
            bmp.DecodePixelWidth = 48;
            bmp.EndInit();

            var w = bmp.PixelWidth;
            var h = bmp.PixelHeight;
            if (w <= 0 || h <= 0) return null;

            var stride = w * 4;
            var pixels = new byte[stride * h];
            bmp.CopyPixels(pixels, stride, 0);

            var buckets = new Dictionary<int, (int Count, double S, double V, int R, int G, int B)>();

            for (var y = 0; y < h; y += 2)
            for (var x = 0; x < w; x += 2)
            {
                var i = y * stride + x * 4;
                byte b = pixels[i], g = pixels[i + 1], r = pixels[i + 2];

                RgbToHsv(r, g, b, out var hue, out var sat, out var val);
                if (sat < 0.28 || val < 0.18 || val > 0.97) continue;

                var key = (int)(hue / 12);
                buckets.TryGetValue(key, out var cur);
                buckets[key] = (cur.Count + 1, cur.S + sat, cur.V + val, cur.R + r, cur.G + g, cur.B + b);
            }

            if (buckets.Count == 0) return null;

            var best = buckets.OrderByDescending(kv => kv.Value.Count).First().Value;
            var n = Math.Max(1, best.Count);

            var color = Color.FromRgb((byte)(best.R / n), (byte)(best.G / n), (byte)(best.B / n));

            // lift saturation a little so it reads as an accent
            RgbToHsv(color.R, color.G, color.B, out var hh, out var ss, out var vv);
            return Hsv(hh, Math.Min(1, ss + 0.15), Math.Clamp(vv + 0.15, 0.45, 0.95));
        }
        catch
        {
            return null;
        }
    }

    private static Color Hsv(double h, double s, double v)
    {
        var c = v * s;
        var x = c * (1 - Math.Abs(h / 60 % 2 - 1));
        var m = v - c;

        (double r, double g, double b) = h switch
        {
            < 60  => (c, x, 0.0),
            < 120 => (x, c, 0.0),
            < 180 => (0.0, c, x),
            < 240 => (0.0, x, c),
            < 300 => (x, 0.0, c),
            _     => (c, 0.0, x)
        };

        return Color.FromRgb(
            (byte)Math.Round((r + m) * 255),
            (byte)Math.Round((g + m) * 255),
            (byte)Math.Round((b + m) * 255));
    }

    private static void RgbToHsv(byte r, byte g, byte b, out double h, out double s, out double v)
    {
        double rr = r / 255.0, gg = g / 255.0, bb = b / 255.0;
        var max = Math.Max(rr, Math.Max(gg, bb));
        var min = Math.Min(rr, Math.Min(gg, bb));
        var d = max - min;

        v = max;
        s = max <= 0 ? 0 : d / max;

        if (d <= 0) { h = 0; return; }

        if (max == rr) h = 60 * ((gg - bb) / d % 6);
        else if (max == gg) h = 60 * ((bb - rr) / d + 2);
        else h = 60 * ((rr - gg) / d + 4);

        if (h < 0) h += 360;
    }
}


