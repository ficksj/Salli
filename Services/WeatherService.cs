using System.Globalization;
using System.Net.Http;
using System.Text.Json;

namespace Salli.Services;

public sealed record WeatherInfo(double Temp, string Description, string Glyph, string City)
{
    public string TempText => $"{(int)Math.Round(Temp)}°";
}

/// <summary>Open-Meteo (no API key) + IP based geolocation, cached for 15 minutes.</summary>
public static class WeatherService
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(7) };
    private static WeatherInfo? _cached;
    private static DateTime _cachedAt = DateTime.MinValue;
    private static double? _lat, _lon;
    private static string _city = "";
    private static string _cityQuery = "";

    public static Task<WeatherInfo?> GetAsync(bool force = false) => GetAsync("", force);

    /// <summary>Pass a non-empty <paramref name="city"/> to override the IP-based location.</summary>
    public static async Task<WeatherInfo?> GetAsync(string city, bool force = false)
    {
        city ??= "";

        if (!string.Equals(city, _cityQuery, StringComparison.OrdinalIgnoreCase))
        {
            _cityQuery = city;
            _cached    = null;
            _lat = _lon = null;
            _city = "";
        }

        if (!force && _cached is not null && (DateTime.Now - _cachedAt).TotalMinutes < 15)
            return _cached;

        try
        {
            if (_lat is null || _lon is null)
            {
                if (city.Length > 0) await GeocodeAsync(city);
                if (_lat is null || _lon is null) await LocateAsync();
            }

            if (_lat is null || _lon is null) return _cached;

            var lat = _lat.Value.ToString("0.####", CultureInfo.InvariantCulture);
            var lon = _lon.Value.ToString("0.####", CultureInfo.InvariantCulture);
            var url = "https://api.open-meteo.com/v1/forecast" +
                      $"?latitude={lat}&longitude={lon}" +
                      "&current=temperature_2m,weather_code&timezone=auto";

            using var doc = JsonDocument.Parse(await Http.GetStringAsync(url));
            if (!doc.RootElement.TryGetProperty("current", out var cur)) return _cached;

            var temp = cur.GetProperty("temperature_2m").GetDouble();
            var code = cur.GetProperty("weather_code").GetInt32();
            var (description, glyph) = Describe(code);

            _cached   = new WeatherInfo(temp, description, glyph, _city);
            _cachedAt = DateTime.Now;
        }
        catch
        {
            // offline or service hiccup — keep the last good value
        }

        return _cached;
    }

    private static async Task LocateAsync()
    {
        // ipwho.is first, ipapi.co as a fallback; both are key-less.
        if (await TryLocate("https://ipwho.is/", "latitude", "longitude", "city")) return;
        await TryLocate("https://ipapi.co/json/", "latitude", "longitude", "city");
    }

    private static async Task GeocodeAsync(string city)
    {
        try
        {
            var url = "https://geocoding-api.open-meteo.com/v1/search?count=1&language=ru&format=json&name="
                      + Uri.EscapeDataString(city);

            using var doc = JsonDocument.Parse(await Http.GetStringAsync(url));
            if (!doc.RootElement.TryGetProperty("results", out var results) ||
                results.ValueKind != JsonValueKind.Array ||
                results.GetArrayLength() == 0)
                return;

            var first = results[0];
            if (!first.TryGetProperty("latitude", out var la) || la.ValueKind != JsonValueKind.Number) return;
            if (!first.TryGetProperty("longitude", out var lo) || lo.ValueKind != JsonValueKind.Number) return;

            _lat = la.GetDouble();
            _lon = lo.GetDouble();
            _city = first.TryGetProperty("name", out var n) ? (n.GetString() ?? city) : city;
        }
        catch
        {
            // fall back to IP geolocation
        }
    }

    private static async Task<bool> TryLocate(string url, string latKey, string lonKey, string cityKey)
    {
        try
        {
            using var doc = JsonDocument.Parse(await Http.GetStringAsync(url));
            var root = doc.RootElement;

            if (root.TryGetProperty("success", out var ok) && ok.ValueKind == JsonValueKind.False)
                return false;
            if (root.TryGetProperty("error", out var err) && err.ValueKind == JsonValueKind.True)
                return false;

            if (!root.TryGetProperty(latKey, out var la) || la.ValueKind != JsonValueKind.Number) return false;
            if (!root.TryGetProperty(lonKey, out var lo) || lo.ValueKind != JsonValueKind.Number) return false;

            _lat = la.GetDouble();
            _lon = lo.GetDouble();
            _city = root.TryGetProperty(cityKey, out var c) ? (c.GetString() ?? "") : "";
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static (string Description, string Glyph) Describe(int code) => code switch
    {
        0                => ("Ясно", "\uE706"),
        1                => ("Ясно", "\uE706"),
        2                => ("Облачно", "\uE753"),
        3                => ("Пасмурно", "\uE753"),
        45 or 48         => ("Туман", "\uE753"),
        51 or 53 or 55   => ("Морось", "\uE753"),
        56 or 57         => ("Морось", "\uE753"),
        61 or 63 or 65   => ("Дождь", "\uE753"),
        66 or 67         => ("Дождь", "\uE753"),
        71 or 73 or 75   => ("Снег", "\uE753"),
        77               => ("Снег", "\uE753"),
        80 or 81 or 82   => ("Ливень", "\uE753"),
        85 or 86         => ("Снег", "\uE753"),
        95               => ("Гроза", "\uE945"),
        96 or 99         => ("Гроза", "\uE945"),
        _                => ("—", "\uE753")
    };
}
