using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Salli;

public sealed class ClipItem
{
    public string Kind { get; set; } = "text";      // text | image
    public string Text { get; set; } = "";
    public string File { get; set; } = "";          // PNG path for images
    public string Hash { get; set; } = "";          // dedupe
    public bool   Pinned { get; set; }
    public DateTime At { get; set; } = DateTime.Now;

    [JsonIgnore] public bool IsImage => Kind == "image";
}

public sealed class AppSettings
{
    private static readonly string _path = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "Salli", "settings.json");

    public static string Folder => Path.GetDirectoryName(_path)!;

    // window / tray
    public double Left      { get; set; } = -1;
    public double Top       { get; set; } = 10;
    public bool   Pinned    { get; set; }
    public bool   Autostart { get; set; }

    // look
    public string Theme      { get; set; } = "dark";      // dark | light
    public string AccentMode { get; set; } = "wallpaper"; // wallpaper | manual | preset
    public string AccentColor{ get; set; } = "#0A84FF";
    public string Size       { get; set; } = "normal";    // compact | normal | large
    public string ThemePreset{ get; set; } = "graphite";  // graphite | midnight | nord | mono | sand
    public bool   Glass      { get; set; }

    // behaviour
    public bool HoverExpand  { get; set; }
    public bool IdleCollapse { get; set; } = true;
    public bool WheelVolume  { get; set; } = true;
    public bool PopSound     { get; set; } = true;
    public bool SwipeCollapse{ get; set; } = true;
    public bool StreamerMode { get; set; }
    public bool AutoHideFullscreen { get; set; } = true;
    public bool Hotkeys      { get; set; } = true;

    // bar contents
    public bool ShowWeather  { get; set; } = true;
    public bool ShowMic      { get; set; } = true;
    public bool ShowNetwork  { get; set; } = true;
    public bool ShowEqualizer{ get; set; } = true;
    public string VisualizerSource { get; set; } = "output"; // output | mic | auto

    // language
    public string Language   { get; set; } = "ru";        // ru | en

    // extras
    public string WeatherCity   { get; set; } = "";
    public double LyricsOffset  { get; set; }         // seconds
    public string Note          { get; set; } = "";
    public List<string> Favorites { get; set; } = new();

    // clipboard
    public bool ClipboardImages { get; set; } = true;
    public List<ClipItem> Clips { get; set; } = new();
    public List<string> ClipboardHistory { get; set; } = new();   // legacy, migrated into Clips

    // timer
    public string TimerMode { get; set; } = "countdown";
    public int    TimerMinutes { get; set; } = 25;

    [JsonIgnore] public string LyricsOffsetText => LyricsOffset.ToString("+0.0;-0.0;0.0") + " с";

    public static AppSettings Load()
    {
        AppSettings s;

        try
        {
            if (File.Exists(_path))
            {
                var json = File.ReadAllText(_path);
                s = JsonSerializer.Deserialize<AppSettings>(json) ?? new AppSettings();
            }
            else s = new AppSettings();
        }
        catch
        {
            s = new AppSettings();
        }

        s.Migrate();
        return s;
    }

    private void Migrate()
    {
        if (Clips.Count == 0 && ClipboardHistory.Count > 0)
        {
            foreach (var text in ClipboardHistory.Take(20))
            {
                if (string.IsNullOrWhiteSpace(text)) continue;
                Clips.Add(new ClipItem { Kind = "text", Text = text });
            }
        }

        // drop dangling image files
        Clips.RemoveAll(c => c.IsImage && (string.IsNullOrEmpty(c.File) || !File.Exists(c.File)));

        while (Clips.Count(c => !c.Pinned) > 20)
            Clips.Remove(Clips.Last(c => !c.Pinned));
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(Folder);
            File.WriteAllText(_path, JsonSerializer.Serialize(this,
                new JsonSerializerOptions { WriteIndented = true }));
        }
        catch { }
    }
}
