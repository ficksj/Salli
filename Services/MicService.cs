using Microsoft.Win32;

namespace Salli.Services;

/// <summary>
/// Windows keeps per-app microphone usage timestamps in the capability consent
/// store: LastUsedTimeStop == 0 means the microphone is still open.
/// </summary>
public static class MicService
{
    private const string Root =
        @"SOFTWARE\Microsoft\Windows\CurrentVersion\CapabilityAccessManager\ConsentStore\microphone";

    public static (bool InUse, string Apps) Check()
    {
        var names = new List<string>();

        // Desktop apps live under NonPackaged\<exe path>; Store apps use their
        // package family name directly under the root key.
        Scan(Root + @"\NonPackaged", names, packaged: false);
        Scan(Root, names, packaged: true);

        var app = string.Join(", ", names
            .Where(n => n.Length > 0 && !n.Contains("Salli", StringComparison.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(3));

        var inUse = names.Count > 0;
        return (inUse, app);
    }

    private static void Scan(string path, List<string> names, bool packaged)
    {
        try
        {
            using var root = Registry.CurrentUser.OpenSubKey(path);
            if (root is null) return;

            foreach (var keyName in root.GetSubKeyNames())
            {
                try
                {
                    using var sub = root.OpenSubKey(keyName);
                    if (sub is null) continue;

                    var start = sub.GetValue("LastUsedTimeStart");
                    var stop  = sub.GetValue("LastUsedTimeStop");

                    if (start is not long started || started == 0) continue;
                    if (stop is not long stopped) continue;
                    if (stopped != 0) continue;   // still open

                    var friendly = Friendly(keyName, packaged);
                    if (friendly.Length > 0) names.Add(friendly);
                }
                catch
                {
                    // a key can vanish between enumeration and open
                }
            }
        }
        catch
        {
            // no consent store on this build
        }
    }

    private static string Friendly(string keyName, bool packaged)
    {
        if (!packaged)
        {
            // "C:#Program Files#App#app.exe" → "app"
            var cut = keyName.LastIndexOfAny(new[] { '#', '\\', '/' });
            var file = cut >= 0 ? keyName[(cut + 1)..] : keyName;
            return Strip(file);
        }

        // "Microsoft.WindowsSoundRecorder_8wekyb3d8bbwe" → "WindowsSoundRecorder"
        var name = keyName;
        var underscore = name.IndexOf('_');
        if (underscore > 0) name = name[..underscore];

        var dot = name.LastIndexOf('.');
        if (dot > 0) name = name[(dot + 1)..];

        return Strip(name);
    }

    private static string Strip(string s)
    {
        s = s.Trim();
        if (s.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) s = s[..^4];
        return s;
    }
}
