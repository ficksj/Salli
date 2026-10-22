using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Salli.Services;
using Brush = System.Windows.Media.Brush;

namespace Salli;

public partial class SettingsWindow : Window
{
    private readonly AppSettings _settings;
    private bool _loading = true;

    /// <summary>Raised whenever something in the settings changed.</summary>
    public event Action? Changed;

    /// <summary>Raised when the user wants the island re-centred.</summary>
    public event Action? ResetRequested;

    public SettingsWindow(AppSettings settings)
    {
        InitializeComponent();

        _settings = settings;

        L.Set(settings.Language);

        Load();
        _loading = false;

        // the visual tree only exists once the window is shown
        Loaded += (_, _) => Localizer.Apply(this);

        Theme.Changed += Refresh;

        Closed += (_, _) => Theme.Changed -= Refresh;
    }

    public void Load()
    {
        _loading = true;

        HoverSwitch.IsChecked  = _settings.HoverExpand;
        IdleSwitch.IsChecked   = _settings.IdleCollapse;
        WheelSwitch.IsChecked  = _settings.WheelVolume;
        SwipeSwitch.IsChecked  = _settings.SwipeCollapse;
        SoundSwitch.IsChecked  = _settings.PopSound;
        StreamSwitch.IsChecked = _settings.StreamerMode;

        WeatherSwitch.IsChecked = _settings.ShowWeather;
        MicSwitch.IsChecked     = _settings.ShowMic;
        NetSwitch.IsChecked     = _settings.ShowNetwork;
        EqSwitch.IsChecked      = _settings.ShowEqualizer;

        CityBox.Text = _settings.WeatherCity;

        AutoSwitch.IsChecked  = _settings.Autostart;
        NightSwitch.IsChecked = NightLightService.Enabled;

        GlassSwitch.IsChecked    = _settings.Glass;
        FsSwitch.IsChecked       = _settings.AutoHideFullscreen;
        HotkeySwitch.IsChecked   = _settings.Hotkeys;
        ClipImgSwitch.IsChecked  = _settings.ClipboardImages;

        Highlight(LangRu, _settings.Language != "en");
        Highlight(LangEn, _settings.Language == "en");
        Highlight(VisOutput, _settings.VisualizerSource != "mic");
        Highlight(VisMic, _settings.VisualizerSource == "mic");
        UpdatePaletteDots();

        Highlight(DarkBtn,  _settings.Theme != "light");
        Highlight(LightBtn, _settings.Theme == "light");

        Highlight(SizeCompact, _settings.Size == "compact");
        Highlight(SizeNormal,  _settings.Size is not ("compact" or "large"));
        Highlight(SizeLarge,   _settings.Size == "large");

        Highlight(AutoAccent, _settings.AccentMode == "wallpaper");
        Highlight(BlueAccent, _settings.AccentMode == "manual");

        _loading = false;
    }

    public void Refresh()
    {
        var wasLoading = _loading;
        _loading = true;
        Highlight(DarkBtn,  _settings.Theme != "light");
        Highlight(LightBtn, _settings.Theme == "light");
        Highlight(AutoAccent, _settings.AccentMode == "wallpaper");
        UpdatePaletteDots();
        _loading = wasLoading;
    }

    private void UpdatePaletteDots()
    {
        var preset = _settings.ThemePreset ?? "graphite";

        foreach (var (button, key) in new[]
                 {
                     (PalGraphite, "graphite"), (PalMidnight, "midnight"), (PalNord, "nord"),
                     (PalMono, "mono"), (PalSand, "sand")
                 })
        {
            button.BorderBrush = key == preset
                ? (Brush)FindResource("TextBrush")
                : System.Windows.Media.Brushes.Transparent;
        }
    }

    private void Highlight(Button button, bool on)
    {
        if (button is null) return;

        button.Background = on
            ? (Brush)FindResource("AccentBrush")
            : (Brush)FindResource("CardBrush");

        button.Foreground = on
            ? (Brush)FindResource("AccentInkBrush")
            : (Brush)FindResource("TextBrush");
    }

    private void Save()
    {
        if (_loading) return;
        _settings.Save();
        Changed?.Invoke();
    }

    // ── appearance ────────────────────────────────────────────────────────
    private void Theme_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button b || b.Tag is not string theme) return;

        _settings.Theme = theme;
        Theme.Apply(_settings);
        Load();
        Save();
    }

    private void AccentAuto_Click(object sender, RoutedEventArgs e)
    {
        _settings.AccentMode = "wallpaper";
        Theme.Apply(_settings);
        Load();
        Save();
    }

    // ── palette / glass / language ────────────────────────────────────────
    private void Palette_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button b || b.Tag is not string preset) return;

        _settings.ThemePreset = preset;
        _settings.AccentMode  = "preset";

        Theme.Apply(_settings);
        UpdatePaletteDots();
        Save();
    }

    private void Glass_Changed(object sender, RoutedEventArgs e)
    {
        if (_loading) return;
        _settings.Glass = GlassSwitch.IsChecked == true;
        Theme.Apply(_settings);
        Save();
    }

    private void Lang_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button b || b.Tag is not string lang) return;

        _settings.Language = lang == "en" ? "en" : "ru";
        L.Set(_settings.Language);

        Localizer.Apply(this);
        Load();
        Save();
    }

    // ── behaviour ─────────────────────────────────────────────────────────
    private void Fs_Changed(object sender, RoutedEventArgs e)
    {
        if (_loading) return;
        _settings.AutoHideFullscreen = FsSwitch.IsChecked == true;
        Save();
    }

    private void Hotkey_Changed(object sender, RoutedEventArgs e)
    {
        if (_loading) return;
        _settings.Hotkeys = HotkeySwitch.IsChecked == true;
        Save();
    }

    // ── what to show ──────────────────────────────────────────────────────
    private void Vis_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button b || b.Tag is not string source) return;

        _settings.VisualizerSource = source == "mic" ? "mic" : "output";
        Highlight(VisOutput, _settings.VisualizerSource != "mic");
        Highlight(VisMic, _settings.VisualizerSource == "mic");
        Save();
    }

    private void ClipImg_Changed(object sender, RoutedEventArgs e)
    {
        if (_loading) return;
        _settings.ClipboardImages = ClipImgSwitch.IsChecked == true;
        Save();
    }

    private void AccentColor_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button b || b.Tag is not string hex) return;

        _settings.AccentMode  = "manual";
        _settings.AccentColor = hex;
        Theme.Apply(_settings);
        Load();
        Save();
    }

    private void Size_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button b || b.Tag is not string size) return;

        _settings.Size = size;
        Load();
        Save();
    }

    // ── behaviour ─────────────────────────────────────────────────────────
    private void Hover_Changed(object sender, RoutedEventArgs e)
    {
        if (_loading) return;
        _settings.HoverExpand = HoverSwitch.IsChecked == true;
        Save();
    }

    private void Idle_Changed(object sender, RoutedEventArgs e)
    {
        if (_loading) return;
        _settings.IdleCollapse = IdleSwitch.IsChecked == true;
        Save();
    }

    private void Wheel_Changed(object sender, RoutedEventArgs e)
    {
        if (_loading) return;
        _settings.WheelVolume = WheelSwitch.IsChecked == true;
        Save();
    }

    private void Swipe_Changed(object sender, RoutedEventArgs e)
    {
        if (_loading) return;
        _settings.SwipeCollapse = SwipeSwitch.IsChecked == true;
        Save();
    }

    private void Sound_Changed(object sender, RoutedEventArgs e)
    {
        if (_loading) return;
        _settings.PopSound = SoundSwitch.IsChecked == true;
        Save();
    }

    private void Stream_Changed(object sender, RoutedEventArgs e)
    {
        if (_loading) return;
        _settings.StreamerMode = StreamSwitch.IsChecked == true;
        Save();
    }

    // ── what to show ──────────────────────────────────────────────────────
    private void Weather_Changed(object sender, RoutedEventArgs e)
    {
        if (_loading) return;
        _settings.ShowWeather = WeatherSwitch.IsChecked == true;
        Save();
    }

    private void Mic_Changed(object sender, RoutedEventArgs e)
    {
        if (_loading) return;
        _settings.ShowMic = MicSwitch.IsChecked == true;
        Save();
    }

    private void Net_Changed(object sender, RoutedEventArgs e)
    {
        if (_loading) return;
        _settings.ShowNetwork = NetSwitch.IsChecked == true;
        Save();
    }

    private void Eq_Changed(object sender, RoutedEventArgs e)
    {
        if (_loading) return;
        _settings.ShowEqualizer = EqSwitch.IsChecked == true;
        Save();
    }

    private void City_Changed(object sender, TextChangedEventArgs e)
    {
        if (_loading) return;
        _settings.WeatherCity = CityBox.Text.Trim();
        Save();
    }

    // ── system ────────────────────────────────────────────────────────────
    private void Auto_Changed(object sender, RoutedEventArgs e)
    {
        if (_loading) return;
        _settings.Autostart = AutoSwitch.IsChecked == true;
        Save();
    }

    private void Night_Changed(object sender, RoutedEventArgs e)
    {
        if (_loading) return;
        NightLightService.Set(NightSwitch.IsChecked == true);
        Save();
    }

    private void Reset_Click(object sender, RoutedEventArgs e) => ResetRequested?.Invoke();

    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}
