using System.Globalization;
using System.Net.Http;
using System.Text.Json;

namespace Salli.Services;

public sealed record LyricLine(TimeSpan At, string Text);

/// <summary>
/// Fetches time-synced lyrics from LRCLIB (free, no key required) and caches them per track.
/// </summary>
public static class LyricsService
{
    private static readonly HttpClient Http = CreateClient();
    private static readonly Dictionary<string, IReadOnlyList<LyricLine>?> Cache = new();

    private static HttpClient CreateClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromSeconds(7) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("Salli/1.0 (Windows Dynamic Island)");
        return client;
    }

    public static async Task<IReadOnlyList<LyricLine>?> GetAsync(string artist, string title, double duration)
    {
        if (string.IsNullOrWhiteSpace(title)) return null;

        var key = $"{artist}\u0001{title}".ToLowerInvariant();
        if (Cache.TryGetValue(key, out var cached)) return cached;

        IReadOnlyList<LyricLine>? lines = null;
        try
        {
            var query = $"track_name={Uri.EscapeDataString(title)}";
            if (!string.IsNullOrWhiteSpace(artist))
                query += $"&artist_name={Uri.EscapeDataString(artist)}";
            if (duration > 1)
                query += $"&duration={Math.Round(duration)}";

            using var response = await Http.GetAsync($"https://lrclib.net/api/get?{query}");
            if (response.IsSuccessStatusCode)
            {
                using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
                if (doc.RootElement.TryGetProperty("syncedLyrics", out var synced) &&
                    synced.ValueKind == JsonValueKind.String)
                {
                    lines = Parse(synced.GetString() ?? "");
                    if (lines.Count == 0) lines = null;
                }
            }
        }
        catch
        {
            // offline / rate limited / no match — the island simply shows no lyrics
        }

        Cache[key] = lines;
        return lines;
    }

    private static List<LyricLine> Parse(string lrc)
    {
        var lines = new List<LyricLine>();

        foreach (var raw in lrc.Split('\n'))
        {
            var line = raw.Trim();
            if (line.Length == 0) continue;

            var rest = line;
            var stamps = new List<TimeSpan>();

            while (rest.StartsWith('['))
            {
                var end = rest.IndexOf(']');
                if (end < 0) break;

                var tag = rest[1..end];
                if (TryParseTime(tag, out var at)) stamps.Add(at);
                rest = rest[(end + 1)..].TrimStart();
            }

            if (stamps.Count == 0 || rest.Length == 0) continue;
            foreach (var at in stamps) lines.Add(new LyricLine(at, rest));
        }

        lines.Sort((a, b) => a.At.CompareTo(b.At));
        return lines;
    }

    private static bool TryParseTime(string tag, out TimeSpan at)
    {
        at = default;
        var parts = tag.Split(':');
        if (parts.Length != 2) return false;

        if (!int.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out var minutes))
            return false;

        if (!double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var seconds))
            return false;

        at = TimeSpan.FromSeconds(minutes * 60 + seconds);
        return true;
    }
}
