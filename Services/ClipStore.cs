using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Windows;
using System.Windows.Media.Imaging;

namespace Salli.Services;

/// <summary>Clipboard capture for text and images. Images live as PNGs on disk
/// so the settings JSON stays small; identical images are deduplicated by hash.</summary>
public static class ClipStore
{
    public static string Folder => Path.Combine(AppSettings.Folder, "clips");

    public static ClipItem? CaptureFromClipboard()
    {
        try
        {
            if (Clipboard.ContainsImage())
            {
                var source = Clipboard.GetImage();
                if (source is null) return null;

                var bytes = EncodePng(source);
                if (bytes.Length < 64) return null;

                var hash = Hash(bytes);
                Directory.CreateDirectory(Folder);
                var file = Path.Combine(Folder, $"clip-{DateTime.Now:yyyyMMdd-HHmmss-fff}-{hash[..6]}.png");

                if (!File.Exists(file)) File.WriteAllBytes(file, bytes);

                return new ClipItem { Kind = "image", File = file, Hash = hash };
            }

            if (Clipboard.ContainsText())
            {
                var text = Clipboard.GetText()?.Trim();
                if (string.IsNullOrWhiteSpace(text) || text.Length > 4000) return null;

                return new ClipItem { Kind = "text", Text = text, Hash = Hash(text) };
            }
        }
        catch
        {
            // clipboard is busy or locked by another app
        }

        return null;
    }

    public static void CopyToClipboard(ClipItem item)
    {
        try
        {
            if (item.IsImage)
            {
                if (!File.Exists(item.File)) return;

                var bmp = new BitmapImage();
                bmp.BeginInit();
                bmp.CacheOption   = BitmapCacheOption.OnLoad;
                bmp.CreateOptions = BitmapCreateOptions.IgnoreImageCache;
                bmp.UriSource     = new Uri(item.File);
                bmp.EndInit();
                bmp.Freeze();

                Clipboard.SetImage(bmp);
            }
            else
            {
                Clipboard.SetText(item.Text);
            }
        }
        catch { }
    }

    public static BitmapImage? Thumbnail(ClipItem item, int width = 64)
    {
        if (!item.IsImage || !File.Exists(item.File)) return null;

        try
        {
            var bmp = new BitmapImage();
            bmp.BeginInit();
            bmp.CacheOption      = BitmapCacheOption.OnLoad;
            bmp.CreateOptions    = BitmapCreateOptions.IgnoreImageCache;
            bmp.DecodePixelWidth = width;
            bmp.UriSource        = new Uri(item.File);
            bmp.EndInit();
            bmp.Freeze();
            return bmp;
        }
        catch
        {
            return null;
        }
    }

    public static void DeleteFiles(ClipItem item)
    {
        try
        {
            if (item.IsImage && File.Exists(item.File)) File.Delete(item.File);
        }
        catch { }
    }

    public static int CountImages() => Directory.Exists(Folder)
        ? Directory.GetFiles(Folder, "*.png").Length
        : 0;

    private static byte[] EncodePng(BitmapSource source)
    {
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(source));

        using var ms = new MemoryStream();
        encoder.Save(ms);
        return ms.ToArray();
    }

    public static string Hash(byte[] bytes)
    {
        var hash = SHA1.HashData(bytes);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    public static string Hash(string text)
    {
        var hash = SHA1.HashData(System.Text.Encoding.UTF8.GetBytes(text));
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    /// <summary>Keeps 20 items (8 images) overall, but never touches pinned ones.</summary>
    public static void Trim(List<ClipItem> clips)
    {
        while (clips.Count(c => c.IsImage && !c.Pinned) > 8)
        {
            var victim = clips.Last(c => c.IsImage && !c.Pinned);
            clips.Remove(victim);
            DeleteFiles(victim);
        }

        while (clips.Count(c => !c.Pinned) > 20)
        {
            var victim = clips.Last(c => !c.Pinned);
            clips.Remove(victim);
            DeleteFiles(victim);
        }
    }
}
