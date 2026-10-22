using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace Salli.Services;

public enum LaunchKind { App, Web, Calc, Currency }

public sealed record LaunchResult(string Glyph, string Title, string Hint, LaunchKind Kind, string Payload);

/// <summary>
/// Start-menu launcher with inline calculator and currency conversion.
/// The app index is built once in the background; queries are synchronous.
/// </summary>
public static class Launcher
{
    private static readonly List<(string Name, string Path)> _apps = new();
    private static bool _loaded;

    private static readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(6) };
    private static Dictionary<string, double>? _rates;
    private static DateTime _ratesAt;

    public static string Language { get; set; } = "ru";

    public static async Task LoadAsync()
    {
        if (_loaded) return;
        _loaded = true;

        await Task.Run(() =>
        {
            var roots = new[]
            {
                Environment.GetFolderPath(Environment.SpecialFolder.CommonStartMenu)  + @"\Programs",
                Environment.GetFolderPath(Environment.SpecialFolder.StartMenu)         + @"\Programs"
            };

            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var found = new List<(string, string)>();

            foreach (var root in roots)
            {
                if (!Directory.Exists(root)) continue;

                IEnumerable<string> files;
                try { files = Directory.EnumerateFiles(root, "*.lnk", SearchOption.AllDirectories); }
                catch { continue; }

                foreach (var f in files)
                {
                    var name = Path.GetFileNameWithoutExtension(f);
                    if (string.IsNullOrWhiteSpace(name)) continue;
                    if (BadName(name)) continue;
                    if (!seen.Add(name)) continue;

                    found.Add((name, f));
                }
            }

            found.Sort((a, b) => string.Compare(a.Item1, b.Item1, StringComparison.CurrentCultureIgnoreCase));
            _apps.AddRange(found);
        });
    }

    private static bool BadName(string name)
    {
        string[] bad = { "uninstall", "readme", "read me", "help", "website", "документац", "справка",
                         "удалить", "деинстал", "license", "changelog", "release notes", "обновление" };
        var lower = name.ToLowerInvariant();
        return bad.Any(b => lower.Contains(b));
    }

    public static IReadOnlyList<LaunchResult> Query(string text, int max = 7)
    {
        var results = new List<LaunchResult>();
        text = text.Trim();
        if (text.Length == 0) return Builtins(max);

        if (TryCurrency(text, out var currency))
        {
            results.Add(currency!);
            return results;
        }

        if (TryCalculate(text, out var calc))
        {
            results.Add(new LaunchResult("\uE8EF", calc!, text + " =", LaunchKind.Calc, calc!));
        }

        var lower = text.ToLowerInvariant();

        // exact / prefix matches first, then substring matches
        var ranked = _apps
            .Select(a => (a.Name, a.Path, Score: Score(a.Name, lower)))
            .Where(x => x.Score > 0)
            .OrderByDescending(x => x.Score)
            .ThenBy(x => x.Name.Length)
            .Take(max)
            .ToList();

        foreach (var a in ranked)
            results.Add(new LaunchResult("\uE737", a.Name, "приложение", LaunchKind.App, a.Path));

        if (results.Count < max)
        {
            foreach (var b in Builtins(max))
            {
                if (results.Any(r => r.Title.Equals(b.Title, StringComparison.OrdinalIgnoreCase))) continue;
                if (!b.Title.ToLowerInvariant().Contains(lower)) continue;
                results.Add(b);
            }
        }

        var web = new LaunchResult("\uE774", text, Language == "en" ? "search the web" : "искать в интернете",
            LaunchKind.Web, text);
        results.Add(web);

        return results.Take(max).ToList();
    }

    private static int Score(string name, string query)
    {
        var n = name.ToLowerInvariant();
        if (n == query) return 100;
        if (n.StartsWith(query, StringComparison.Ordinal)) return 80;
        if (n.Contains(" " + query, StringComparison.Ordinal)) return 60;
        if (n.Contains(query, StringComparison.Ordinal)) return 40;

        // simple subsequence match ("vsc" → "Visual Studio Code")
        var i = 0;
        foreach (var c in n)
        {
            if (i < query.Length && c == query[i]) i++;
        }
        return i == query.Length && query.Length >= 2 ? 20 : 0;
    }

    private static List<LaunchResult> Builtins(int max)
    {
        var list = new List<LaunchResult>
        {
            new("\uE8EF", "Калькулятор", "приложение", LaunchKind.App, "calc.exe"),
            new("\uE7F4", "Параметры",   "приложение", LaunchKind.App, "ms-settings:"),
            new("\uE8B7", "Проводник",   "приложение", LaunchKind.App, "explorer.exe"),
            new("\uE70F", "Блокнот",     "приложение", LaunchKind.App, "notepad.exe"),
            new("\uE756", "Терминал",    "приложение", LaunchKind.App, "wt.exe"),
            new("\uE7EF", "Диспетчер задач", "приложение", LaunchKind.App, "taskmgr.exe")
        };

        return list.Take(max).ToList();
    }

    public static void Run(LaunchResult item)
    {
        try
        {
            switch (item.Kind)
            {
                case LaunchKind.App:
                    Process.Start(new ProcessStartInfo(item.Payload) { UseShellExecute = true });
                    break;

                case LaunchKind.Web:
                    var url = Language == "en"
                        ? "https://www.google.com/search?q=" + Uri.EscapeDataString(item.Payload)
                        : "https://yandex.ru/search/?text=" + Uri.EscapeDataString(item.Payload);
                    Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
                    break;

                case LaunchKind.Calc:
                case LaunchKind.Currency:
                    System.Windows.Clipboard.SetText(item.Payload);
                    break;
            }
        }
        catch { }
    }

    // ── calculator ────────────────────────────────────────────────────────
    public static bool TryCalculate(string input, out string? result)
    {
        result = null;

        var expr = input.Trim()
            .Replace(',', '.')
            .Replace('×', '*')
            .Replace('÷', '/')
            .Replace('−', '-')
            .Replace(" ", "");

        if (expr.Length == 0) return false;
        if (!expr.Any(c => "+-*/^".Contains(c))) return false;
        if (!Regex.IsMatch(expr, @"^[0-9+\-*/^().]+$")) return false;

        try
        {
            var value = new Parser(expr).Parse();
            if (double.IsNaN(value) || double.IsInfinity(value)) return false;

            var text = Math.Abs(value) >= 1e12 || (Math.Abs(value) < 1e-6 && value != 0)
                ? value.ToString("0.######e+0")
                : Math.Round(value, 6).ToString("0.######");

            result = text;
            return true;
        }
        catch
        {
            return false;
        }
    }

    private sealed class Parser
    {
        private readonly string _s;
        private int _i;

        public Parser(string s) => _s = s;

        public double Parse()
        {
            var v = Expression();
            if (_i < _s.Length) throw new FormatException();
            return v;
        }

        private double Expression()
        {
            var v = Term();
            while (_i < _s.Length && (_s[_i] == '+' || _s[_i] == '-'))
            {
                var op = _s[_i++];
                var r = Term();
                v = op == '+' ? v + r : v - r;
            }
            return v;
        }

        private double Term()
        {
            var v = Power();
            while (_i < _s.Length && (_s[_i] == '*' || _s[_i] == '/'))
            {
                var op = _s[_i++];
                var r = Power();
                if (op == '/' && r == 0) throw new DivideByZeroException();
                v = op == '*' ? v * r : v / r;
            }
            return v;
        }

        private double Power()
        {
            var v = Unary();
            if (_i < _s.Length && _s[_i] == '^')
            {
                _i++;
                return Math.Pow(v, Power());
            }
            return v;
        }

        private double Unary()
        {
            if (_i < _s.Length && _s[_i] == '-') { _i++; return -Unary(); }
            if (_i < _s.Length && _s[_i] == '+') { _i++; return Unary(); }
            return Primary();
        }

        private double Primary()
        {
            if (_i < _s.Length && _s[_i] == '(')
            {
                _i++;
                var v = Expression();
                if (_i >= _s.Length || _s[_i] != ')') throw new FormatException();
                _i++;
                return v;
            }

            var start = _i;
            while (_i < _s.Length && (char.IsDigit(_s[_i]) || _s[_i] == '.')) _i++;
            if (start == _i) throw new FormatException();

            return double.Parse(_s[start.._i], System.Globalization.CultureInfo.InvariantCulture);
        }
    }

    // ── currency ──────────────────────────────────────────────────────────
    private static readonly Dictionary<string, string> CurrencyWords = new(StringComparer.OrdinalIgnoreCase)
    {
        ["$"] = "USD", ["usd"] = "USD", ["доллар"] = "USD", ["долларов"] = "USD", ["доллара"] = "USD",
        ["€"] = "EUR", ["eur"] = "EUR", ["евро"] = "EUR",
        ["₽"] = "RUB", ["rub"] = "RUB", ["рубль"] = "RUB", ["рублей"] = "RUB", ["рубля"] = "RUB", ["руб"] = "RUB",
        ["₴"] = "UAH", ["uah"] = "UAH", ["гривна"] = "UAH", ["гривен"] = "UAH", ["гривны"] = "UAH",
        ["₸"] = "KZT", ["kzt"] = "KZT", ["тенге"] = "KZT",
        ["£"] = "GBP", ["gbp"] = "GBP", ["фунт"] = "GBP", ["фунтов"] = "GBP",
        ["¥"] = "JPY", ["jpy"] = "JPY", ["йен"] = "JPY", ["иен"] = "JPY",
        ["cny"] = "CNY", ["юань"] = "CNY", ["юаней"] = "CNY",
        ["try"] = "TRY", ["лир"] = "TRY",
        ["byn"] = "BYN", ["белорусских"] = "BYN"
    };

    private static readonly Regex CurrencyRx = new(
        @"^\s*(?<amt>\d+(?:[.,]\d+)?)\s*(?<from>[a-zA-Zа-яА-ЯёЁ$€₽₴₸£¥]{1,12})\s*(?:в|in|to|→|->)\s*(?<to>[a-zA-Zа-яА-ЯёЁ$€₽₴₸£¥]{1,12})\s*$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static bool TryCurrency(string input, out LaunchResult? result)
    {
        result = null;

        var m = CurrencyRx.Match(input);
        if (!m.Success) return false;

        var from = Normalize(m.Groups["from"].Value);
        var to   = Normalize(m.Groups["to"].Value);
        if (from is null || to is null || from == to) return false;

        var amount = double.Parse(m.Groups["amt"].Value.Replace(',', '.'),
            System.Globalization.CultureInfo.InvariantCulture);

        if (_rates is null || (DateTime.Now - _ratesAt).TotalMinutes > 60)
        {
            result = new LaunchResult("\uE8C7", "Курс загружается…",
                $"{amount:0.##} {from} → {to}", LaunchKind.Currency, "");
            _ = FetchRatesAsync(amount, from, to);
            return true;
        }

        if (!_rates.TryGetValue(from, out var rf) || !_rates.TryGetValue(to, out var rt) ||
            rf <= 0) return false;

        var value = amount / rf * rt;
        var text = value >= 100 ? value.ToString("0.00") : value.ToString("0.####");
        result = new LaunchResult("\uE8C7", $"{text} {to}",
            $"{amount:0.##} {from} · {DateTime.Now:HH:mm}", LaunchKind.Currency, text);
        return true;
    }

    private static string? Normalize(string word)
    {
        word = word.Trim().ToLowerInvariant();
        if (CurrencyWords.TryGetValue(word, out var code)) return code;

        foreach (var kv in CurrencyWords)
            if (word.StartsWith(kv.Key, StringComparison.Ordinal) && kv.Key.Length >= 3)
                return kv.Value;

        return null;
    }

    private static async Task FetchRatesAsync(double amount, string from, string to)
    {
        try
        {
            var json = await _http.GetStringAsync("https://open.er-api.com/v6/latest/USD");
            using var doc = JsonDocument.Parse(json);

            if (!doc.RootElement.TryGetProperty("rates", out var rates)) return;

            var map = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
            foreach (var p in rates.EnumerateObject())
                if (p.Value.TryGetDouble(out var v)) map[p.Name] = v;

            _rates   = map;
            _ratesAt = DateTime.Now;
        }
        catch { }
    }
}
