using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Microsoft.Win32;
using Salli.Services;
using WinForms = System.Windows.Forms;
using Point = System.Windows.Point;
using Size = System.Windows.Size;
using MouseEventArgs = System.Windows.Input.MouseEventArgs;
using Brush = System.Windows.Media.Brush;
using Brushes = System.Windows.Media.Brushes;
using Color = System.Windows.Media.Color;
using FontFamily = System.Windows.Media.FontFamily;

namespace Salli;

public sealed record IslandNotification(string Glyph, string Title, string Text);

public partial class MainWindow : Window
{
    private enum Mode { Collapsed, Expanded, Notification }

    // ── timers ────────────────────────────────────────────────────────────
    private readonly DispatcherTimer _clock        = new() { Interval = TimeSpan.FromSeconds(1) };
    private readonly DispatcherTimer _poll         = new() { Interval = TimeSpan.FromSeconds(4) };
    private readonly DispatcherTimer _weatherTimer = new() { Interval = TimeSpan.FromMinutes(10) };
    private readonly DispatcherTimer _eqTimer      = new() { Interval = TimeSpan.FromMilliseconds(90) };
    private readonly DispatcherTimer _idleTimer    = new() { Interval = TimeSpan.FromSeconds(5) };
    private readonly DispatcherTimer _hoverTimer   = new() { Interval = TimeSpan.FromMilliseconds(420) };
    private readonly DispatcherTimer _noteTimer    = new() { Interval = TimeSpan.FromMilliseconds(700) };
    private readonly DispatcherTimer _layoutTimer  = new() { Interval = TimeSpan.FromMilliseconds(40) };
    private readonly DispatcherTimer _shadowTimer  = new() { Interval = TimeSpan.FromMilliseconds(500) };

    // ── state ─────────────────────────────────────────────────────────────
    private Mode _mode = Mode.Collapsed;
    private Size _collapsedSize = new(420, 48);
    private Size _expandedSize  = new(520, 410);
    private Size _notifySize    = new(420, 48);
    private DateTime _morphUntil = DateTime.MinValue;
    private bool _ready;
    private bool _brightnessAvailable = true;
    private bool _brightnessDdc;
    private bool _pinned;
    private bool _autostart;
    private int  _ignoreSlider;
    private bool _syncUi;

    private NowPlaying _now  = new();
    private RadioInfo  _wifi = new() { Name = "Wi-Fi" };
    private RadioInfo  _bt   = new() { Name = "Bluetooth" };

    private string _artKey = "";
    private double _progress;
    private double _position;
    private double _duration;
    private DateTime _progressStamp = DateTime.Now;
    private int _cpu, _ram;

    private WeatherInfo? _weather;
    private bool _micOn;
    private string _micApps = "";
    private bool _seeking;
    private DateTime _seekUntil = DateTime.MinValue;

    private IReadOnlyList<LyricLine>? _lyrics;
    private string _lyricsKey = "";
    private int _lyricIndex = int.MinValue;
    private readonly List<Run> _karaokeRuns = new();
    private int _karaokeWord = -1;

    private readonly Queue<IslandNotification> _notes = new();
    private bool _noteShowing;
    private bool _firstApply = true;
    private bool _lastWifiOn, _lastCharging;
    private string _lastTrack = "";
    private bool _batteryWarned;

    private readonly AppSettings         _settings = AppSettings.Load();
    private readonly WinForms.NotifyIcon _tray;
    private SettingsWindow? _settingsWindow;

    private string _tab = "media";
    private bool _idle;
    private DateTime _lastInteraction = DateTime.Now;

    private Point _swipeStart;
    private bool _swipeTracking;
    private DateTime _modeChangedAt = DateTime.MinValue;
    private double _down, _up;
    private double _level;
    private int  _eqTick;
    private IntPtr _hwnd;

    private string _lastClipboard = "";
    private string _appliedLang   = "ru";
    private bool _loadingNote;
    private bool _refreshing;

    // timer
    private readonly IslandTimer _timer      = new();
    private readonly DispatcherTimer _tickTimer = new() { Interval = TimeSpan.FromMilliseconds(100) };
    private readonly DispatcherTimer _fsTimer   = new() { Interval = TimeSpan.FromMilliseconds(1400) };
    private string _timerText = "";
    private bool _hiddenFs;

    // search
    private readonly List<LaunchResult> _results = new();
    private int _resultIndex;

    // mixer
    private DateTime _mixerAt = DateTime.MinValue;
    private bool _mixerSync;

    // clipboard
    private bool _clipBusy;

    // hotkeys
    private const int HotkeySearchId = 0x5A11;
    private const int HotkeyMuteId   = 0x5A12;
    private bool _hotkeysRegistered;

    private const string AutostartKey = "Salli";

    public MainWindow()
    {
        _appliedLang = _settings.Language;
        L.Set(_settings.Language);
        Launcher.Language = _settings.Language;

        Theme.Apply(_settings);

        InitializeComponent();

        ApplyTimerSettings();

        _tray = new WinForms.NotifyIcon
        {
            Icon    = BuildTrayIcon(),
            Text    = "Salli — Dynamic Island",
            Visible = true
        };
        BuildTrayMenu();
        _tray.DoubleClick += (_, _) => ShowIsland();

        _pinned    = _settings.Pinned;
        _autostart = IsAutostartEnabled();

        SourceInitialized += (_, _) =>
        {
            Place();
            if (PresentationSource.FromVisual(this) is HwndSource src)
            {
                _hwnd = src.Handle;
                src.AddHook(WindowProc);
                AddClipboardFormatListener(_hwnd);
                RegisterHotkeys();
            }
        };
        Loaded += async (_, _) =>
        {
            Place();
            ApplySizePreset();
            TickClock();
            UpdatePinButton();
            UpdateSettingsTabState();
            UpdateLikeButton();
            RenderClipboard();
            SelectTab("media");

            _loadingNote = true;
            NoteBox.Text = _settings.Note ?? "";
            _loadingNote = false;
            NotePlaceholder.Visibility = NoteBox.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
            OffsetText.Text = _settings.LyricsOffset.ToString("+0.0;-0.0;0.0", System.Globalization.CultureInfo.InvariantCulture) + (L.IsEn ? " s" : " с");

            _lastClipboard = _settings.ClipboardHistory.FirstOrDefault() ?? "";
            _ready = true;

            Localizer.Apply(this);
            UpdatePaletteDots();
            UpdateTimerUi();
            RenderSearch();
            _ = Launcher.LoadAsync();

            await RefreshAsync();
            _ = UpdateWeatherAsync(true);

            ApplyCommandLine();
        };
        Closed      += (_, _) =>
        {
            UnregisterHotkeys();
            if (_hwnd != IntPtr.Zero) RemoveClipboardFormatListener(_hwnd);
            _tray.Visible = false;
            _tray.Dispose();
            _settings.Save();
        };
        Deactivated += (_, _) =>
        {
            if (_mode == Mode.Expanded && !_pinned && _settingsWindow is not { IsVisible: true })
                SetMode(Mode.Collapsed);
        };

        _clock.Tick += (_, _) => TickClock();
        _clock.Start();
        _poll.Tick += async (_, _) => await RefreshAsync();
        _poll.Start();
        _weatherTimer.Tick += async (_, _) => await UpdateWeatherAsync();
        _weatherTimer.Start();

        _eqTimer.Tick += (_, _) => TickEqualizer();
        _eqTimer.Start();

        _idleTimer.Tick += (_, _) => CheckIdle();
        _idleTimer.Start();

        _hoverTimer.Tick += (_, _) =>
        {
            _hoverTimer.Stop();
            if (_settings.HoverExpand && _mode == Mode.Collapsed && Island.IsMouseOver)
                SetMode(Mode.Expanded);
        };

        _noteTimer.Tick += (_, _) =>
        {
            _noteTimer.Stop();
            _settings.Note = NoteBox.Text;
            _settings.Save();
            FlashSaved();
        };

        _layoutTimer.Tick += (_, _) =>
        {
            _layoutTimer.Stop();
            RefreshExpandedLayout();
        };

        _shadowTimer.Tick += (_, _) =>
        {
            _shadowTimer.Stop();

            Island.Effect = IslandShadow;
            IslandShadow.BeginAnimation(DropShadowEffect.OpacityProperty,
                new DoubleAnimation(0, 0.42, TimeSpan.FromMilliseconds(240))
                { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
        };

        _tickTimer.Tick += (_, _) => TickTimer();
        _tickTimer.Start();

        _fsTimer.Tick += (_, _) => CheckFullscreen();
        _fsTimer.Start();
    }

    // ── click-through outside the island + clipboard listener ─────────────
    private IntPtr WindowProc(IntPtr hwnd, int msg, IntPtr w, IntPtr l, ref bool handled)
    {
        const int WM_NCHITTEST       = 0x0084;
        const int WM_CLIPBOARDUPDATE = 0x031D;
        const int WM_MOUSEACTIVATE   = 0x0021;
        const int WM_HOTKEY          = 0x0312;

        if (msg == WM_HOTKEY)
        {
            var id = w.ToInt32();
            if (id == HotkeySearchId)
            {
                ShowIsland();
                SetMode(Mode.Expanded);
                SelectTab("search");
                Dispatcher.BeginInvoke(DispatcherPriority.Input, new Action(() => SearchBox.Focus()));
            }
            else if (id == HotkeyMuteId)
            {
                AudioService.ToggleMute();
                _ = RefreshAsync();
            }

            handled = true;
            return IntPtr.Zero;
        }

        if (msg == WM_CLIPBOARDUPDATE)
        {
            OnClipboardUpdate();
            return IntPtr.Zero;
        }

        // never steal focus: the island must react to the very first click
        if (msg == WM_MOUSEACTIVATE)
        {
            handled = true;
            return new IntPtr(3); // MA_NOACTIVATE
        }

        if (msg != WM_NCHITTEST) return IntPtr.Zero;

        var raw = l.ToInt64();
        var sx  = (short)(raw & 0xFFFF);
        var sy  = (short)((raw >> 16) & 0xFFFF);

        if (!IslandRect().Contains(PointFromScreen(new Point(sx, sy))))
        {
            handled = true;
            return new IntPtr(-1); // HTTRANSPARENT
        }
        return IntPtr.Zero;
    }

    private Rect IslandRect()
    {
        var origin = Island.TranslatePoint(new Point(0, 0), this);
        return new Rect(origin.X, origin.Y, Island.ActualWidth, Island.ActualHeight);
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool AddClipboardFormatListener(IntPtr hwnd);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool RemoveClipboardFormatListener(IntPtr hwnd);

    // ── tray icon ─────────────────────────────────────────────────────────
    private static System.Drawing.Icon BuildTrayIcon()
    {
        using var bmp = new System.Drawing.Bitmap(32, 32);
        using var g   = System.Drawing.Graphics.FromImage(bmp);
        g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        using var brush = new System.Drawing.SolidBrush(System.Drawing.Color.White);
        g.FillEllipse(brush, 3, 11, 12, 12);
        g.FillEllipse(brush, 17, 11, 12, 12);
        g.FillRectangle(brush, 9, 11, 14, 12);
        return System.Drawing.Icon.FromHandle(bmp.GetHicon());
    }

    // ── placement ─────────────────────────────────────────────────────────
    private void Place()
    {
        var area = SystemParameters.WorkArea;
        Width  = 640;
        Height = 520;
        Left   = area.Left + Math.Round((area.Width - Width) / 2);
        Top    = area.Top;
    }

    // ── clock + live progress ─────────────────────────────────────────────
    private void TickClock()
    {
        var now = DateTime.Now;
        var t   = now.ToString("HH:mm");

        var culture = System.Globalization.CultureInfo.GetCultureInfo(L.IsEn ? "en-US" : "ru-RU");

        CompactTime.Text = t;
        CompactDate.Text = now.ToString("d MMM", culture);
        BigTime.Text     = t;
        BigDate.Text     = now.ToString("d MMMM", culture);
        BigWeekday.Text  = Cap(now.ToString("dddd", culture));

        if (_now.IsPlaying && _now.HasSession && !_seeking)
        {
            var elapsed = (now - _progressStamp).TotalSeconds;
            if (elapsed > 0 && elapsed < 10)
            {
                _position += elapsed;
                if (_duration > 1) _position = Math.Clamp(_position, 0, _duration);
                _progress = _duration > 1
                    ? Math.Clamp(_position / _duration, 0, 1)
                    : Math.Clamp(_progress + elapsed / 180.0, 0, 1);
                UpdateLyrics();
            }
            _progressStamp = now;
        }

        DrawProgress();
    }

    private void DrawProgress()
    {
        var width = ProgressHost.ActualWidth;
        var fraction = Math.Clamp(_progress, 0, 1);

        if (_seeking)
        {
            ProgressScale.BeginAnimation(ScaleTransform.ScaleXProperty, null);
            ProgressScale.ScaleX = fraction;
        }
        else
        {
            var from = ProgressScale.ScaleX;
            ProgressScale.BeginAnimation(ScaleTransform.ScaleXProperty,
                new DoubleAnimation(from, fraction, TimeSpan.FromMilliseconds(380))
                { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
        }

        ProgressThumb.Margin = new Thickness(Math.Max(0, width * fraction) - 5, 0, 0, 0);
        PosText.Text = FormatTime(_position);
        DurText.Text = FormatTime(_duration);
    }

    private static string FormatTime(double seconds)
    {
        if (seconds <= 0 || double.IsNaN(seconds) || double.IsInfinity(seconds)) return "0:00";
        var t = TimeSpan.FromSeconds(seconds);
        return $"{(int)t.TotalMinutes}:{t.Seconds:00}";
    }

    private static string Cap(string s) => s.Length == 0 ? s : char.ToUpper(s[0]) + s[1..];

    // ── refresh ───────────────────────────────────────────────────────────
    private sealed record Snapshot(
        AudioEndpoint Audio, int? Brightness, bool BrightnessDdc, string? Battery, string? BatteryTime,
        string? Ssid, (int Cpu, int Ram) Stats, (bool InUse, string Apps) Mic,
        (double Down, double Up) Net, DiskService.DiskInfo Disk, int? Temp,
        NowPlaying Now, RadioInfo Wifi, RadioInfo Bt);

    /// <summary>
    /// Samples every service on a worker thread. WMI, DDC/CI and WinRT calls
    /// are slow; running them on the UI thread used to stall animations for
    /// well over 100 ms every poll tick.
    /// </summary>
    private async Task<Snapshot> GatherAsync()
    {
        var wT = RadioService.GetWifiAsync();
        var bT = RadioService.GetBluetoothAsync();
        var mT = MediaService.GetAsync();
        var tT = Task.Run(TemperatureService.Cpu);
        await Task.WhenAll(wT, bT, mT);

        var audio  = AudioService.Get();
        var bright = BrightnessService.Get();

        var ddc = false;
        if (bright is null)
        {
            var d = DdcService.Get(_hwnd);
            if (d is { } v && v.Max > v.Min)
            {
                bright = (int)Math.Round(100.0 * (v.Cur - v.Min) / (v.Max - v.Min));
                ddc = true;
            }
        }

        var batt  = PowerService.BatteryText();
        var battT = PowerService.RemainingText();
        var ssid  = wT.Result.IsOn ? NetworkService.WifiName() : null;
        var stats = SystemStatsService.Sample();
        var mic   = MicService.Check();
        var net   = NetworkSpeedService.Sample();
        var disk  = DiskService.System();
        var temp  = tT.Result;

        return new Snapshot(audio, bright, ddc, batt, battT, ssid, stats, mic, net, disk, temp,
                            mT.Result, wT.Result, bT.Result);
    }

    private async Task RefreshAsync()
    {
        if (_refreshing) return;
        _refreshing = true;

        try
        {
            var snap = await Task.Run(GatherAsync);

            await Dispatcher.InvokeAsync(() =>
            {
                _wifi = snap.Wifi;
                _bt   = snap.Bt;
                _now  = snap.Now;
                _brightnessDdc = snap.BrightnessDdc && snap.Brightness is not null;

                Apply(snap.Audio, snap.Brightness, snap.Battery, snap.BatteryTime,
                      snap.Ssid, snap.Stats, snap.Mic, snap.Net, snap.Disk, snap.Temp);

                // the track/lyrics may have changed the natural height of the panel
                ScheduleExpandedLayout(80);
            });
        }
        finally
        {
            _refreshing = false;
        }
    }

    /// <summary>Poll burst a moment after expanding, never mid-morph.</summary>
    private async Task DelayedRefreshAsync(int delayMs = 620)
    {
        await Task.Delay(delayMs);
        if (_mode == Mode.Expanded) await RefreshAsync();
    }

    private void Apply(AudioEndpoint audio, int? brightness, string? battery, string? batteryTime,
                       string? ssid, (int Cpu, int Ram) stats, (bool InUse, string Apps) mic,
                       (double Down, double Up) net, DiskService.DiskInfo disk, int? temp)
    {
        _cpu = stats.Cpu;
        _ram = stats.Ram;
        _down = net.Down;
        _up   = net.Up;

        _ignoreSlider++;
        VolumeSlider.Value = audio.Volume;
        VolumeValue.Text   = audio.Muted ? "выкл" : $"{audio.Volume}%";
        VolIcon.Text       = audio.Muted ? Icons.VolumeMute : Icons.Volume;

        if (brightness is int b)
        {
            _brightnessAvailable   = true;
            BrightSlider.IsEnabled = true;
            BrightSlider.Value     = b;
            BrightValue.Text       = $"{b}%";
        }
        else
        {
            _brightnessAvailable   = false;
            BrightSlider.IsEnabled = false;
            BrightValue.Text       = "—";
        }
        _ignoreSlider--;

        CompactWifi.Text       = Icons.Wifi;
        CompactWifi.Foreground = Theme.Brush(_wifi.IsOn ? "TextBrush" : "SubBrush", Colors.Gray);
        CompactBt.Text         = Icons.Bluetooth;
        CompactBt.Foreground   = Theme.Brush(_bt.IsOn ? "TextBrush" : "SubBrush", Colors.Gray);
        CompactVol.Text        = audio.Muted ? Icons.VolumeMute : Icons.Volume;

        var battText = battery ?? "";
        CompactBattery.Text = battText;

        var charging = PowerService.IsCharging;
        CompactBolt.Visibility = charging ? Visibility.Visible : Visibility.Collapsed;

        BatteryText2.Text = $"Батарея {battText}".Trim() +
                            (string.IsNullOrEmpty(batteryTime) ? "" : $" · {batteryTime}");
        BatteryGlyph.Text = charging ? Icons.Bolt : Icons.Battery;

        CpuValue.Text  = temp is int t ? $"{_cpu}% · {t}°" : $"{_cpu}%";
        RamValue.Text  = $"{_ram}%";
        DiskValue.Text = $"{disk.UsedPercent}%";
        NetValue.Text  = $"↓ {NetworkSpeedService.Format(_down)}";
        NetSub.Text    = $"↑ {NetworkSpeedService.Format(_up)}";

        SetBarScale(CpuScale, _cpu);
        SetBarScale(RamScale, _ram);
        SetBarScale(DiskScale, disk.UsedPercent);

        BarCpu.Text = _settings.ShowNetwork
            ? $"↓ {NetworkSpeedService.Format(_down)}"
            : $"CPU {_cpu}%";

        UpdateMic(mic);

        // media
        if (_now.HasSession && !string.IsNullOrWhiteSpace(_now.Title))
        {
            MediaCard.Visibility = Visibility.Visible;
            EmptyMedia.Visibility = Visibility.Collapsed;

            var hidden = _settings.StreamerMode;

            TrackTitle.Text  = hidden ? "Скрыто" : _now.Title;
            TrackArtist.Text = hidden
                ? "режим стримера"
                : string.IsNullOrWhiteSpace(_now.Artist) ? "Сейчас играет" : _now.Artist;

            PlayGlyph.Text = _now.IsPlaying ? Icons.Pause : Icons.Play;

            CompactTitle.Text  = TrackTitle.Text;
            CompactArtist.Text = TrackArtist.Text;

            var (sourceName, sourceGlyph) = SourceService.Describe(_now.SourceApp);
            SourcePill.Visibility = string.IsNullOrWhiteSpace(sourceName) ? Visibility.Collapsed : Visibility.Visible;
            SourceName.Text       = sourceName;
            SourceGlyph.Text      = sourceGlyph;

            _duration = _now.Duration;
            if (!_seeking && DateTime.Now >= _seekUntil)
            {
                _position      = _now.Progress * Math.Max(_now.Duration, 0);
                _progress      = _now.Progress;
                _progressStamp = DateTime.Now;
            }
            DrawProgress();

            var key = _now.Title + "|" + _now.Artist;
            if (key != _artKey)
            {
                _artKey = key;
                ApplyArt(ToImage(_now.Art));
                UpdateLikeButton();
            }

            if (key != _lyricsKey)
            {
                _lyricsKey  = key;
                _lyrics     = null;
                _lyricIndex = int.MinValue;
                _karaokeRuns.Clear();
                LyricKaraoke.Inlines.Clear();
                _ = LoadLyricsAsync(_now.Artist, _now.Title, _now.Duration, key);
            }
        }
        else
        {
            MediaCard.Visibility  = Visibility.Collapsed;
            EmptyMedia.Visibility = Visibility.Visible;
            LyricsBox.Visibility  = Visibility.Collapsed;
            FavoritesButton.Visibility = Visibility.Collapsed;

            CompactTitle.Text  = "Salli";
            CompactArtist.Text = "Dynamic Island";

            _artKey    = "";
            _lyricsKey = "";
            _lyrics    = null;
            ApplyArt(null);
        }

        UpdateFavoritesButton();

        var percent = ParsePercent(battText);
        NotifyChanges(percent, charging);

        if (_mode == Mode.Collapsed) ResizeToContent();

        UpdateLyrics();

        if (_tab == "system" && _mode == Mode.Expanded) UpdateProcesses();
    }

    private void ResizeToContent()
    {
        if (_mode != Mode.Collapsed) return;

        var size = MeasurePanel(CollapsedPanel);
        if (Math.Abs(size.Width - _collapsedSize.Width) > 6 ||
            Math.Abs(size.Height - _collapsedSize.Height) > 0.5)
        {
            _collapsedSize = size;

            // The very first measurement happens while the window is still
            // settling; snap instead of animating from the XAML placeholder.
            if (!_ready)
            {
                Island.BeginAnimation(WidthProperty, null);
                Island.BeginAnimation(HeightProperty, null);
                Island.Width  = size.Width;
                Island.Height = size.Height;
                return;
            }

            Morph(size, Mode.Collapsed);
        }
    }

    private static int ParsePercent(string? text)
    {
        if (string.IsNullOrEmpty(text)) return -1;
        var digits = new string(text.TakeWhile(char.IsDigit).ToArray());
        return int.TryParse(digits, out var v) ? v : -1;
    }

    // ── island notifications ──────────────────────────────────────────────
    private void NotifyChanges(int percent, bool charging)
    {
        var track = _now.HasSession ? _now.Title + "|" + _now.Artist : "";

        if (_firstApply)
        {
            _firstApply   = false;
            _lastTrack    = track;
            _lastWifiOn   = _wifi.IsOn;
            _lastCharging = charging;
            return;
        }

        if (track.Length > 0 && _lastTrack.Length > 0 && track != _lastTrack)
            Notify(Icons.Music,
                   _settings.StreamerMode ? "Скрыто" : _now.Title,
                   _settings.StreamerMode ? "режим стримера"
                       : string.IsNullOrWhiteSpace(_now.Artist) ? "Сейчас играет" : _now.Artist);

        if (_wifi.Available && _wifi.IsOn != _lastWifiOn)
            Notify(Icons.Wifi, "Wi-Fi", _wifi.IsOn ? "подключено" : "выключено");

        if (charging != _lastCharging)
            Notify(Icons.Bolt, charging ? "Зарядка подключена" : "Питание от батареи",
                   percent >= 0 ? $"{percent}%" : "");

        if (!charging && percent is >= 0 and <= 20 && !_batteryWarned)
        {
            _batteryWarned = true;
            Notify(Icons.Battery, "Низкий заряд", $"{percent}% — подключите зарядку");
        }
        if (percent > 25) _batteryWarned = false;

        _lastTrack    = track;
        _lastWifiOn   = _wifi.IsOn;
        _lastCharging = charging;
    }

    private void Notify(string glyph, string title, string text)
    {
        if (_mode == Mode.Expanded) return;
        _notes.Enqueue(new IslandNotification(glyph, title, text));
        _ = PumpNotificationsAsync();
    }

    private async Task PumpNotificationsAsync()
    {
        if (_noteShowing) return;
        _noteShowing = true;
        try
        {
            while (_notes.Count > 0)
            {
                var note = _notes.Dequeue();
                NoteGlyph.Text = note.Glyph;
                NoteTitle.Text = note.Title;
                NoteText.Text  = note.Text;

                SetMode(Mode.Notification);
                await Task.Delay(3800);
                if (_mode == Mode.Notification) SetMode(Mode.Collapsed);
                await Task.Delay(450);
            }
        }
        finally
        {
            _noteShowing = false;
        }
    }

    // ── lyrics + karaoke ──────────────────────────────────────────────────
    private async Task LoadLyricsAsync(string artist, string title, double duration, string key)
    {
        var lines = await LyricsService.GetAsync(artist, title, duration);
        if (_lyricsKey != key) return;

        await Dispatcher.InvokeAsync(() =>
        {
            _lyrics     = lines;
            _lyricIndex = int.MinValue;
            UpdateLyrics();
        });
    }

    private void UpdateLyrics()
    {
        if (!_now.HasSession)
        {
            LyricsBox.Visibility = Visibility.Collapsed;
            return;
        }

        LyricsBox.Visibility = Visibility.Visible;

        if (_lyrics is null || _lyrics.Count == 0)
        {
            if (_lyricIndex == -2) return;
            _lyricIndex = -2;
            _karaokeRuns.Clear();
            LyricKaraoke.Inlines.Clear();
            LyricKaraoke.Inlines.Add(new Run("♪")
            {
                Foreground = Theme.Brush("SubBrush", Colors.Gray)
            });
            LyricPrev.Text = "";
            LyricNext.Text = "текст не найден";
            return;
        }

        var shift = _settings.LyricsOffset;
        var time  = TimeSpan.FromSeconds(_position - shift);

        var index = -1;
        for (var i = 0; i < _lyrics.Count; i++)
        {
            if (_lyrics[i].At <= time) index = i;
            else break;
        }

        if (index == _lyricIndex) return;
        _lyricIndex = index;

        LyricsBox.Visibility = Visibility.Visible;

        LyricPrev.Text = index > 0 ? _lyrics[index - 1].Text : "";

        if (index < 0)
        {
            LyricKaraoke.Inlines.Clear();
            _karaokeRuns.Clear();
            LyricKaraoke.Inlines.Add(new Run("♪")
            {
                Foreground = Theme.Brush("SubBrush", Colors.Gray)
            });
            LyricNext.Text = _lyrics[0].Text;
            return;
        }

        LyricNext.Text = index + 1 < _lyrics.Count ? _lyrics[index + 1].Text : "";
        BuildKaraokeLine(_lyrics[index].Text);
        UpdateKaraokeProgress();
    }

    private void BuildKaraokeLine(string text)
    {
        LyricKaraoke.Inlines.Clear();
        _karaokeRuns.Clear();
        _karaokeWord = -1;

        var words = text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (words.Length == 0)
        {
            LyricKaraoke.Inlines.Add(new Run("…") { Foreground = Theme.Brush("SubBrush", Colors.Gray) });
            return;
        }

        for (var i = 0; i < words.Length; i++)
        {
            var run = new Run(words[i] + (i < words.Length - 1 ? " " : ""))
            {
                Foreground = Theme.Brush("SubBrush", Colors.Gray)
            };
            _karaokeRuns.Add(run);
            LyricKaraoke.Inlines.Add(run);
        }
    }

    private void UpdateKaraokeProgress()
    {
        if (_karaokeRuns.Count == 0 || _lyrics is null) return;
        if (_lyricIndex < 0 || _lyricIndex >= _lyrics.Count) return;

        var line = _lyrics[_lyricIndex];
        var next = _lyricIndex + 1 < _lyrics.Count
            ? _lyrics[_lyricIndex + 1].At
            : line.At + TimeSpan.FromSeconds(6);

        var start = line.At.TotalSeconds + _settings.LyricsOffset;
        var end   = next.TotalSeconds    + _settings.LyricsOffset;

        var fraction = Math.Clamp((_position - start) / Math.Max(0.4, end - start), 0, 1);

        var word = (int)(fraction * _karaokeRuns.Count);
        if (word == _karaokeWord) return;
        _karaokeWord = word;

        var sung = Theme.Brush("TextBrush", Colors.White);
        var rest = Theme.Brush("SubBrush", Colors.Gray);

        for (var i = 0; i < _karaokeRuns.Count; i++)
            _karaokeRuns[i].Foreground = i < word ? sung : rest;
    }

    private static void FadeIn(UIElement el, int ms)
    {
        el.BeginAnimation(OpacityProperty,
            new DoubleAnimation(0.35, 1, new Duration(TimeSpan.FromMilliseconds(ms)))
            { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
    }

    // ── mode switching + morph ────────────────────────────────────────────
    private UIElement PanelFor(Mode mode) => mode switch
    {
        Mode.Expanded     => ExpandedPanel,
        Mode.Notification => NotificationPanel,
        _                 => CollapsedPanel
    };

    private void SetMode(Mode mode)
    {
        if (_mode == mode)
        {
            if (mode == Mode.Expanded) _ = RefreshAsync();
            return;
        }

        var prev = _mode;
        _mode = mode;
        _modeChangedAt = DateTime.Now;

        if (mode == Mode.Expanded && prev != Mode.Expanded && _settings.PopSound)
            SoundService.Pop(true);
        else if (mode == Mode.Collapsed && prev == Mode.Expanded && _settings.PopSound)
            SoundService.Pop(false);

        Size target;
        switch (mode)
        {
            case Mode.Expanded:
                _expandedSize = MeasurePanel(ExpandedPanel);
                ExpandedPanel.Height = _expandedSize.Height;  // freeze layout while the island morphs
                target = _expandedSize;
                break;
            case Mode.Notification:
                _notifySize = MeasurePanel(NotificationPanel);
                NotificationPanel.Height = _notifySize.Height;
                target = _notifySize;
                break;
            default:
                _collapsedSize = MeasurePanel(CollapsedPanel);
                target = _collapsedSize;
                break;
        }

        if (mode == Mode.Expanded)
        {
            _ = DelayedRefreshAsync();
            if (_tab == "nets")   _ = LoadNetworksAsync();
            if (_tab == "buffer") RenderClipboard();
            if (_tab == "system") UpdateProcesses();
            if (_tab == "search") RenderSearch();
            if (_tab == "timer")  UpdateTimerUi();
        }

        var show = PanelFor(mode);
        var hide = PanelFor(prev);

        if (!ReferenceEquals(show, hide))
        {
            if (mode == Mode.Expanded)
            {
                // Lay the panel out (and paint it once, invisibly) before the
                // clock starts; the morph itself then runs without hitches.
                show.Visibility = Visibility.Visible;
                show.Opacity    = 0.01;
                hide.Opacity    = 0;
                Island.UpdateLayout();

                Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(() =>
                {
                    if (_mode != mode) return;

                    show.Opacity = 0;
                    hide.Visibility = Visibility.Collapsed;
                    hide.Opacity = 1;

                    AnimateIn(show);
                    Morph(target, mode);

                    if (_tab == "search")
                        SearchBox.Focus();
                }));
                return;
            }

            AnimateIn(show);
            AnimateOut(hide);
        }

        Morph(target, mode);
    }

    private static void AnimateIn(UIElement el)    {
        var ease = new QuinticEase { EasingMode = EasingMode.EaseOut };

        el.Visibility = Visibility.Visible;
        el.BeginAnimation(OpacityProperty, null);
        el.Opacity = 0;
        el.BeginAnimation(OpacityProperty,
            new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(300))
            {
                BeginTime      = TimeSpan.FromMilliseconds(70),
                EasingFunction = ease
            });

        if (el.RenderTransform is TranslateTransform shift)
        {
            shift.BeginAnimation(TranslateTransform.YProperty, null);
            shift.Y = 0;
            shift.BeginAnimation(TranslateTransform.YProperty,
                new DoubleAnimation(10, 0, TimeSpan.FromMilliseconds(440))
                {
                    BeginTime      = TimeSpan.FromMilliseconds(40),
                    EasingFunction = ease
                });
        }
    }

    private void AnimateOut(UIElement el)
    {
        var ease = new QuinticEase { EasingMode = EasingMode.EaseOut };

        var fade = new DoubleAnimation(0, TimeSpan.FromMilliseconds(170)) { EasingFunction = ease };

        fade.Completed += (_, _) =>
        {
            if (ReferenceEquals(PanelFor(_mode), el)) return;

            el.Visibility = Visibility.Collapsed;
            el.BeginAnimation(OpacityProperty, null);
            el.Opacity = 1;

            if (el.RenderTransform is TranslateTransform s)
            {
                s.BeginAnimation(TranslateTransform.YProperty, null);
                s.Y = 0;
            }
        };

        el.BeginAnimation(OpacityProperty, fade);

        if (el.RenderTransform is TranslateTransform shift)
        {
            shift.BeginAnimation(TranslateTransform.YProperty,
                new DoubleAnimation(-6, TimeSpan.FromMilliseconds(170)) { EasingFunction = ease });
        }
    }

    private void Morph(Size target, Mode mode, int? msOverride = null)
    {
        var fromW = Island.ActualWidth;
        var fromH = Island.ActualHeight;
        if (double.IsNaN(fromW) || fromW <= 0) fromW = target.Width;
        if (double.IsNaN(fromH) || fromH <= 0) fromH = target.Height;

        Island.BeginAnimation(WidthProperty, null);
        Island.BeginAnimation(HeightProperty, null);
        Island.Width  = target.Width;
        Island.Height = target.Height;

        if (Math.Abs(fromW - target.Width) < 0.5 && Math.Abs(fromH - target.Height) < 0.5) return;

        // Growing gets a slightly longer, softer curve than shrinking.
        var expanding = target.Height > fromH + 2;
        var ms   = msOverride ?? (expanding ? 440 : 300);
        if (ms <= 0)
        {
            _morphUntil = DateTime.MinValue;
            return;
        }

        var ease = new QuinticEase { EasingMode = EasingMode.EaseOut };
        var dur  = new Duration(TimeSpan.FromMilliseconds(ms));

        // A blur effect is rasterised afresh on every frame while the island
        // resizes, which costs ~40 ms per morph on layered windows. Drop it
        // for the duration of the animation and ease it back in.
        Island.Effect = null;
        _shadowTimer.Stop();

        Island.BeginAnimation(WidthProperty,
            new DoubleAnimation(fromW, target.Width, dur) { EasingFunction = ease });
        Island.BeginAnimation(HeightProperty,
            new DoubleAnimation(fromH, target.Height, dur) { EasingFunction = ease });

        _shadowTimer.Interval = TimeSpan.FromMilliseconds(ms + 20);
        _shadowTimer.Start();

        _morphUntil = DateTime.Now.AddMilliseconds(ms + 40);
    }

    /// <summary>
    /// Measures a panel at its natural size. Clearing the explicit height is
    /// important: while the island animates, panels keep a frozen height so
    /// their whole subtree is not re-measured on every frame.
    /// </summary>
    private Size MeasurePanel(FrameworkElement panel)
    {
        var vis = panel.Visibility;
        var h   = panel.Height;

        panel.Visibility = Visibility.Visible;
        panel.Height     = double.NaN;

        var available = Math.Max(240, Width - 40);
        panel.Measure(new Size(available, double.PositiveInfinity));
        var width = Math.Ceiling(panel.DesiredSize.Width);

        panel.Measure(new Size(width, double.PositiveInfinity));
        var size = new Size(width, Math.Ceiling(panel.DesiredSize.Height));

        panel.Height     = h;
        panel.Visibility = vis;
        panel.InvalidateMeasure();
        return size;
    }

    private void ScheduleExpandedLayout(int delayMs = 40)
    {
        if (_mode != Mode.Expanded) return;
        _layoutTimer.Stop();
        _layoutTimer.Interval = TimeSpan.FromMilliseconds(Math.Max(1, delayMs));
        _layoutTimer.Start();
    }

    private void RefreshExpandedLayout()
    {
        if (_mode != Mode.Expanded) return;

        var wait = (_morphUntil - DateTime.Now).TotalMilliseconds;
        if (wait > 0)
        {
            ScheduleExpandedLayout((int)wait + 20);
            return;
        }

        var size = MeasurePanel(ExpandedPanel);
        if (Math.Abs(size.Height - _expandedSize.Height) > 1 || Math.Abs(size.Width - _expandedSize.Width) > 1)
        {
            _expandedSize = size;
            ExpandedPanel.Height = size.Height;
            Morph(size, Mode.Expanded);
        }
    }

    private static void SetBarScale(ScaleTransform scale, int percent)
    {
        var target = Math.Clamp(percent, 0, 100) / 100.0;
        var from   = scale.ScaleX;

        scale.BeginAnimation(ScaleTransform.ScaleXProperty,
            new DoubleAnimation(from, target, TimeSpan.FromMilliseconds(600))
            { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
    }

    private void ApplyArt(BitmapImage? img)
    {
        if (img == null)
        {
            ArtBox.Background  = Theme.Brush("TrackBrush", Color.FromArgb(0x2E, 0xFF, 0xFF, 0xFF));
            MiniArt.Background = Theme.Brush("TrackBrush", Color.FromArgb(0x2E, 0xFF, 0xFF, 0xFF));
            return;
        }

        var brush = new ImageBrush(img) { Stretch = Stretch.UniformToFill };
        brush.Freeze();
        ArtBox.Background  = brush;
        MiniArt.Background = brush;
    }

    private static BitmapImage? ToImage(byte[]? bytes)
    {
        if (bytes == null || bytes.Length == 0) return null;
        try
        {
            var img = new BitmapImage();
            img.BeginInit();
            img.CacheOption  = BitmapCacheOption.OnLoad;
            img.StreamSource = new MemoryStream(bytes);
            img.EndInit();
            img.Freeze();
            return img;
        }
        catch
        {
            return null;
        }
    }

    private void ShowIsland()
    {
        Dispatcher.Invoke(() =>
        {
            if (Visibility != Visibility.Visible) Show();
            Activate();
        });
    }

    /// <summary>Tray menu, rebuilt whenever the language changes.</summary>
    private void BuildTrayMenu()
    {
        var menu = new WinForms.ContextMenuStrip();

        menu.Items.Add(L.T("Показать"), null, (_, _) => ShowIsland());
        menu.Items.Add(L.T("Свернуть"), null, (_, _) => Dispatcher.Invoke(() => SetMode(Mode.Collapsed)));
        menu.Items.Add(L.T("Настройки…"), null, (_, _) => Dispatcher.Invoke(OpenSettings));
        menu.Items.Add("-");
        menu.Items.Add(L.T("Автозапуск с Windows"), null, (_, _) => Dispatcher.Invoke(() => SetAutostart(!_autostart)));
        menu.Items.Add(L.T("Режим стримера"), null, (_, _) => Dispatcher.Invoke(() =>
        {
            _settings.StreamerMode = !_settings.StreamerMode;
            _settings.Save();
            ApplySettings();
        }));
        menu.Items.Add(L.T("Ночной свет"), null, (_, _) => Dispatcher.Invoke(() =>
        {
            NightLightService.Set(!NightLightService.Enabled);
            UpdateNightButton();
        }));
        menu.Items.Add("-");
        menu.Items.Add(L.T("Выход"), null, (_, _) => Dispatcher.Invoke(Close));

        var old = _tray.ContextMenuStrip;
        _tray.ContextMenuStrip = menu;
        old?.Dispose();
    }

    /// <summary>Optional command line helpers: <c>--tab=system</c>, <c>--settings</c>.</summary>
    private void ApplyCommandLine()
    {
        var args = Environment.GetCommandLineArgs();

        var tab = args.FirstOrDefault(a => a.StartsWith("--tab=", StringComparison.OrdinalIgnoreCase));
        if (tab is not null)
        {
            SelectTab(tab.Substring("--tab=".Length).ToLowerInvariant());
            SetMode(Mode.Expanded);
        }

        if (args.Any(a => a.Equals("--settings", StringComparison.OrdinalIgnoreCase)))
            OpenSettings();
    }

    // ── autostart ─────────────────────────────────────────────────────────
    private static bool IsAutostartEnabled()
    {
        using var key = Registry.CurrentUser.OpenSubKey(
            @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run", false);
        return key?.GetValue(AutostartKey) != null;
    }

    private void SetAutostart(bool enable)
    {
        using var key = Registry.CurrentUser.OpenSubKey(
            @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run", true);
        if (key != null)
        {
            if (enable)
            {
                var exe = Environment.ProcessPath ?? "";
                if (exe.Length > 0) key.SetValue(AutostartKey, $"\"{exe}\"");
            }
            else
            {
                key.DeleteValue(AutostartKey, throwOnMissingValue: false);
            }
        }

        _autostart          = enable;
        _settings.Autostart = enable;
        _settings.Save();
    }

    private void UpdatePinButton()
    {
        PinGlyph.Text       = _pinned ? Icons.Unpin : Icons.Pin;
        PinBtn.Background   = _pinned
            ? Theme.Brush("ChipOnBrush", Colors.White)
            : Theme.Brush("CardBrush", Color.FromArgb(0x1F, 0xFF, 0xFF, 0xFF));
        PinGlyph.Foreground = _pinned
            ? Theme.Brush("ChipOnInkBrush", Colors.Black)
            : Theme.Brush("TextBrush", Colors.White);
        PinBtn.ToolTip      = _pinned ? "Закреплено (нажми, чтобы открепить)" : "Закрепить панель";
    }

    // ── size presets / idle / hover ───────────────────────────────────────
    private void ApplySizePreset(bool animate = true)
    {
        var scale = _settings.Size switch
        {
            "compact" => 0.90,
            "large"   => 1.12,
            _         => 1.00
        };

        var barScale = _idle && _settings.IdleCollapse ? scale * 0.90 : scale;

        var oldBar   = BarScale.ScaleX;
        var oldPanel = PanelScale.ScaleX;

        // Measure the geometry we are morphing to, then put the visuals back
        // so the change itself can be animated.
        BarScale.ScaleX   = BarScale.ScaleY   = barScale;
        PanelScale.ScaleX = PanelScale.ScaleY = scale;
        NoteScale.ScaleX  = NoteScale.ScaleY  = scale;

        _collapsedSize = MeasurePanel(CollapsedPanel);
        _expandedSize  = MeasurePanel(ExpandedPanel);

        BarScale.ScaleX   = BarScale.ScaleY   = oldBar;
        PanelScale.ScaleX = PanelScale.ScaleY = oldPanel;
        NoteScale.ScaleX  = NoteScale.ScaleY  = oldPanel;

        if (!animate)
        {
            SetScale(BarScale, barScale);
            SetScale(PanelScale, scale);
            SetScale(NoteScale, scale);
            Morph(_mode == Mode.Expanded ? _expandedSize : _collapsedSize, _mode, 0);
            return;
        }

        AnimateScale(BarScale, barScale, 380);
        AnimateScale(PanelScale, scale, 380);
        AnimateScale(NoteScale, scale, 380);

        if (_mode == Mode.Collapsed)
        {
            Morph(_collapsedSize, Mode.Collapsed, 380);
        }
        else if (_mode == Mode.Expanded)
        {
            ExpandedPanel.Height = _expandedSize.Height;
            Morph(_expandedSize, Mode.Expanded, 380);
        }
    }

    private static void SetScale(ScaleTransform t, double value)
    {
        t.BeginAnimation(ScaleTransform.ScaleXProperty, null);
        t.BeginAnimation(ScaleTransform.ScaleYProperty, null);
        t.ScaleX = t.ScaleY = value;
    }

    private static void AnimateScale(ScaleTransform t, double target, int ms)
    {
        var from = t.ScaleX;
        if (Math.Abs(from - target) < 0.002)
        {
            SetScale(t, target);
            return;
        }

        var dur  = new Duration(TimeSpan.FromMilliseconds(ms));
        var ease = new QuinticEase { EasingMode = EasingMode.EaseOut };

        t.BeginAnimation(ScaleTransform.ScaleXProperty, null);
        t.BeginAnimation(ScaleTransform.ScaleYProperty, null);
        t.ScaleX = t.ScaleY = from;

        t.BeginAnimation(ScaleTransform.ScaleXProperty,
            new DoubleAnimation(from, target, dur) { EasingFunction = ease });
        t.BeginAnimation(ScaleTransform.ScaleYProperty,
            new DoubleAnimation(from, target, dur) { EasingFunction = ease });
    }

    private void CheckIdle()
    {
        if (Island.IsMouseOver) _lastInteraction = DateTime.Now;

        var shouldIdle = _settings.IdleCollapse &&
                         (DateTime.Now - _lastInteraction).TotalSeconds > 30;

        if (shouldIdle != _idle)
        {
            _idle = shouldIdle;
            ApplySizePreset();
        }

        if (shouldIdle && _mode == Mode.Expanded && !_pinned && _settingsWindow is not { IsVisible: true })
            SetMode(Mode.Collapsed);
    }

    // ── pointer: click to expand / collapse, swipe, wheel ─────────────────
    private void Island_MouseLeftButtonUp(object s, MouseButtonEventArgs e)
    {
        if (IsInteractive(e.OriginalSource as DependencyObject)) return;

        _lastInteraction = DateTime.Now;

        // hover may have expanded the island a moment ago — don't undo it
        if ((DateTime.Now - _modeChangedAt).TotalMilliseconds < 450) return;

        if (_mode == Mode.Collapsed) SetMode(Mode.Expanded);
        else if (_mode == Mode.Expanded) SetMode(Mode.Collapsed);
        else if (_mode == Mode.Notification) SetMode(Mode.Collapsed);
    }

    private void Island_MouseEnter(object s, MouseEventArgs e)
    {
        _lastInteraction = DateTime.Now;

        if (_settings.HoverExpand && _mode == Mode.Collapsed)
        {
            _hoverTimer.Stop();
            _hoverTimer.Start();
        }
    }

    private void Island_MouseLeave(object s, MouseEventArgs e)
    {
        _hoverTimer.Stop();
        _lastInteraction = DateTime.Now;
    }

    private void Island_PreviewMouseDown(object s, MouseButtonEventArgs e)
    {
        _lastInteraction = DateTime.Now;
        _swipeStart      = e.GetPosition(Island);
        _swipeTracking   = true;
    }

    private void Island_PreviewMouseMove(object s, MouseEventArgs e)
    {
        if (!_swipeTracking || e.LeftButton != MouseButtonState.Pressed) return;
        if (!_settings.SwipeCollapse || _mode != Mode.Expanded) return;

        var p = e.GetPosition(Island);
        if (p.Y - _swipeStart.Y < 42) return;

        _swipeTracking = false;
        SetMode(Mode.Collapsed);
        e.Handled = true;
    }

    private void Island_PreviewMouseUp(object s, MouseButtonEventArgs e) => _swipeTracking = false;

    private void Island_PreviewMouseWheel(object s, MouseWheelEventArgs e)
    {
        if (!_settings.WheelVolume) return;

        e.Handled = true;

        var delta = e.Delta > 0 ? 2 : -2;
        var value = Math.Clamp((int)Math.Round(VolumeSlider.Value) + delta, 0, 100);

        AudioService.SetVolume(value);

        _ignoreSlider++;
        VolumeSlider.Value = value;
        VolumeValue.Text   = $"{value}%";
        _ignoreSlider--;

        if (_mode == Mode.Collapsed)
            Notify(Icons.Volume, "Громкость", $"{value}%");
    }

    private static bool IsInteractive(DependencyObject? d)    {
        while (d != null)
        {
            if (d is System.Windows.Controls.Primitives.ButtonBase ||
                d is Slider ||
                d is System.Windows.Controls.Primitives.Thumb ||
                d is TextBox)
                return true;
            d = VisualTreeHelper.GetParent(d);
        }
        return false;
    }

    // ── handlers ──────────────────────────────────────────────────────────
    private void Collapse_Click(object s, RoutedEventArgs e) => SetMode(Mode.Collapsed);
    private void Quit_Click(object s, RoutedEventArgs e)     => Close();

    private void Pin_Click(object s, RoutedEventArgs e)
    {
        _pinned          = !_pinned;
        _settings.Pinned = _pinned;
        _settings.Save();
        UpdatePinButton();
    }

    private void VolIcon_Click(object s, MouseButtonEventArgs e)
    {
        AudioService.ToggleMute();
        _ = RefreshAsync();
    }

    private async void Play_Click(object s, RoutedEventArgs e)
    {
        await MediaService.ToggleAsync(_now);
        await Task.Delay(250);
        await RefreshAsync();
    }

    private async void Next_Click(object s, RoutedEventArgs e)
    {
        await MediaService.NextAsync(_now);
        await Task.Delay(300);
        await RefreshAsync();
    }

    private async void Prev_Click(object s, RoutedEventArgs e)
    {
        await MediaService.PreviousAsync(_now);
        await Task.Delay(300);
        await RefreshAsync();
    }

    private void VolumeSlider_ValueChanged(object s, RoutedPropertyChangedEventArgs<double> e)
    {
        if (!_ready || _ignoreSlider > 0) return;
        var v = (int)Math.Round(e.NewValue);
        AudioService.SetVolume(v);
        VolumeValue.Text = $"{v}%";
    }

    private void BrightSlider_ValueChanged(object s, RoutedPropertyChangedEventArgs<double> e)
    {
        if (!_ready || _ignoreSlider > 0 || !_brightnessAvailable) return;

        var v = (int)Math.Round(e.NewValue);

        if (_brightnessDdc ? DdcService.Set(this, v) : BrightnessService.Set(v))
            BrightValue.Text = $"{v}%";
        else if (!_brightnessDdc && DdcService.Set(this, v))
            BrightValue.Text = $"{v}%";
    }

    // ── seek ──────────────────────────────────────────────────────────────
    private void Progress_MouseDown(object s, MouseButtonEventArgs e)
    {
        if (!_now.HasSession || _duration <= 0) return;

        _seeking = true;
        ProgressHost.CaptureMouse();
        ShowThumb(true);
        SeekTo(e.GetPosition(ProgressHost).X / Math.Max(1, ProgressHost.ActualWidth));
        e.Handled = true;
    }

    private void Progress_MouseMove(object s, MouseEventArgs e)
    {
        if (!_seeking) return;
        SeekTo(e.GetPosition(ProgressHost).X / Math.Max(1, ProgressHost.ActualWidth));
    }

    private void Progress_MouseUp(object s, MouseButtonEventArgs e)
    {
        if (!_seeking) return;

        _seeking = false;
        ProgressHost.ReleaseMouseCapture();
        ShowThumb(ProgressHost.IsMouseOver);

        _seekUntil = DateTime.Now.AddMilliseconds(900);
        _ = CommitSeekAsync(_progress);
        e.Handled = true;
    }

    private void Progress_MouseEnter(object s, MouseEventArgs e) => ShowThumb(true);

    private void Progress_MouseLeave(object s, MouseEventArgs e)
    {
        if (!_seeking) ShowThumb(false);
    }

    private void ShowThumb(bool show)
    {
        ProgressThumb.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
        if (!show) return;

        ProgressThumb.BeginAnimation(OpacityProperty,
            new DoubleAnimation(0.2, 1, TimeSpan.FromMilliseconds(150))
            { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
    }

    private void SeekTo(double fraction)
    {
        fraction = Math.Clamp(fraction, 0, 1);

        _progress = fraction;
        if (_duration > 1) _position = fraction * _duration;

        _progressStamp = DateTime.Now;
        _lyricIndex    = int.MinValue;

        DrawProgress();
        UpdateLyrics();
    }

    private async Task CommitSeekAsync(double fraction)
    {
        await MediaService.SeekAsync(_now, fraction);
        await Task.Delay(400);
        await RefreshAsync();
    }

    // ── weather ───────────────────────────────────────────────────────────
    private async Task UpdateWeatherAsync(bool force = false)
    {
        var w = await WeatherService.GetAsync(_settings.WeatherCity, force);
        if (w is null) return;

        await Dispatcher.InvokeAsync(() =>
        {
            _weather = w;
            ApplyWeather();
            ResizeToContent();
        });
    }

    private void ApplyWeather()
    {
        if (_weather is null) return;

        BarWeatherBox.Visibility = _settings.ShowWeather ? Visibility.Visible : Visibility.Collapsed;
        BarWeatherGlyph.Text     = _weather.Glyph;
        BarWeatherTemp.Text      = _weather.TempText;
    }

    // ── microphone indicator ──────────────────────────────────────────────
    private void UpdateMic((bool InUse, string Apps) mic)
    {
        var changed = mic.InUse != _micOn;
        _micOn   = mic.InUse;
        _micApps = mic.Apps;

        CompactMic.Visibility = _micOn && _settings.ShowMic ? Visibility.Visible : Visibility.Collapsed;

        if (!_micOn)
        {
            if (changed)
            {
                CompactMic.BeginAnimation(OpacityProperty, null);
                CompactMic.Opacity = 1;
            }
            return;
        }

        if (changed)
        {
            CompactMic.BeginAnimation(OpacityProperty,
                new DoubleAnimation(1, 0.3, TimeSpan.FromMilliseconds(900))
                {
                    AutoReverse = true,
                    RepeatBehavior = RepeatBehavior.Forever,
                    EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut }
                });
        }
    }

    // ── tabs ──────────────────────────────────────────────────────────────
    private void Tab_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button b && b.Tag is string tag)
            SelectTab(tag);
    }

    private void SelectTab(string tab)
    {
        var changed = tab != _tab;
        _tab = tab;

        MediaTab.Visibility    = tab == "media"    ? Visibility.Visible : Visibility.Collapsed;
        SystemTab.Visibility   = tab == "system"   ? Visibility.Visible : Visibility.Collapsed;
        NetsTab.Visibility     = tab == "nets"     ? Visibility.Visible : Visibility.Collapsed;
        BufferTab.Visibility   = tab == "buffer"   ? Visibility.Visible : Visibility.Collapsed;
        SearchTab.Visibility   = tab == "search"   ? Visibility.Visible : Visibility.Collapsed;
        TimerTab.Visibility    = tab == "timer"    ? Visibility.Visible : Visibility.Collapsed;
        SettingsTab.Visibility = tab == "settings" ? Visibility.Visible : Visibility.Collapsed;

        foreach (var (button, key) in new[]
                 {
                     (TabMedia, "media"), (TabSystem, "system"), (TabNets, "nets"),
                     (TabBuffer, "buffer"), (TabSearch, "search"), (TabTimer, "timer"),
                     (TabSettings, "settings")
                 })
        {
            var on = key == tab;
            button.Background = on ? Theme.Brush("CardBrush", Colors.Transparent) : Brushes.Transparent;
            button.Foreground = on ? Theme.Brush("TextBrush", Colors.White) : Theme.Brush("SubBrush", Colors.Gray);
        }

        if (tab == "nets")     _ = LoadNetworksAsync();
        if (tab == "nets")     _ = LoadMixerAsync();
        if (tab == "buffer")   RenderClipboard();
        if (tab == "system")   UpdateProcesses();
        if (tab == "settings") UpdateSettingsTabState();
        if (tab == "search")   RenderSearch();
        if (tab == "timer")    UpdateTimerUi();

        if (tab == "search" && _mode == Mode.Expanded)
            Dispatcher.BeginInvoke(DispatcherPriority.Input, new Action(() => SearchBox.Focus()));

        if (changed && _mode == Mode.Expanded)
        {
            // A soft cross-fade instead of an instant content swap.
            TabContent.BeginAnimation(OpacityProperty, null);
            TabContent.Opacity = 0;
            TabContent.BeginAnimation(OpacityProperty,
                new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(240))
                { EasingFunction = new QuinticEase { EasingMode = EasingMode.EaseOut } });

            TabShift.BeginAnimation(TranslateTransform.YProperty, null);
            TabShift.Y = 8;
            TabShift.BeginAnimation(TranslateTransform.YProperty,
                new DoubleAnimation(8, 0, TimeSpan.FromMilliseconds(340))
                { EasingFunction = new QuinticEase { EasingMode = EasingMode.EaseOut } });
        }

        ScheduleExpandedLayout();
    }

    // ── system tab ────────────────────────────────────────────────────────
    private void UpdateProcesses()
    {
        var list = ProcessService.Top(3);

        ProcessList.Children.Clear();

        if (list.Count == 0)
        {
            ProcessList.Children.Add(new TextBlock
            {
                Text = "нет данных",
                FontSize = 10,
                Foreground = Theme.Brush("SubBrush", Colors.Gray),
                Margin = new Thickness(4, 4, 0, 0)
            });
            return;
        }

        foreach (var p in list)
        {
            var grid = new Grid { Height = 26 };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            grid.Children.Add(new TextBlock
            {
                Text = p.Name,
                FontSize = 11,
                VerticalAlignment = VerticalAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis,
                Foreground = Theme.Brush("TextBrush", Colors.White)
            });

            var value = new TextBlock
            {
                Text = $"{p.Cpu:0}% · {p.RamMb:0} МБ",
                FontSize = 10,
                VerticalAlignment = VerticalAlignment.Center,
                Foreground = Theme.Brush("SubBrush", Colors.Gray)
            };
            Grid.SetColumn(value, 1);
            grid.Children.Add(value);

            ProcessList.Children.Add(grid);
        }
    }

    private void Night_Click(object sender, RoutedEventArgs e)
    {
        NightLightService.Set(!NightLightService.Enabled);
        UpdateNightButton();
    }

    private void UpdateNightButton()
    {
        var on = NightLightService.Enabled;
        NightBtn.Background = on ? Theme.Brush("AccentBrush", Colors.DodgerBlue) : Theme.Brush("CardBrush", Colors.Transparent);
        NightBtn.Foreground = on ? Theme.Brush("AccentInkBrush", Colors.White) : Theme.Brush("TextBrush", Colors.White);
        NightBtn.Content    = on ? "Ночной вкл" : "Ночной";
    }

    private void Focus_Click(object sender, RoutedEventArgs e) => QuickSettings.Open(QuickSettings.Notifications);

    // ── networks tab ──────────────────────────────────────────────────────
    private async Task LoadNetworksAsync()
    {
        List<WifiNetwork> wifi;
        List<BtDevice> bt;
        List<AudioDevice> audio;

        try
        {
            wifi  = await Task.Run(DevicesService.WifiScan);
            bt    = await DevicesService.BluetoothAsync();
            audio = await Task.Run(AudioDeviceService.List);
        }
        catch
        {
            return;
        }

        if (!IsLoaded) return;

        var hide = _settings.StreamerMode;

        WifiList.Children.Clear();
        if (wifi.Count == 0)
            WifiList.Children.Add(EmptyHint(_wifi.IsOn ? "сети не найдены" : "Wi-Fi выключен"));

        foreach (var n in wifi)
        {
            var name = hide ? "Скрыто" : n.Ssid;
            var sub  = n.Connected ? "подключено" : $"{n.Signal}%";
            var row  = ListRow(Icons.Wifi, name, sub, n.Connected, () =>
            {
                if (n.Connected) return;

                if (string.IsNullOrWhiteSpace(n.Profile))
                {
                    QuickSettings.Open(QuickSettings.Wifi);
                    return;
                }

                if (DevicesService.WifiConnect(n.Profile))
                {
                    Notify(Icons.Wifi, "Wi-Fi", $"Подключение к {name}…");
                    _ = Task.Delay(2500).ContinueWith(_ => Dispatcher.Invoke(() => _ = LoadNetworksAsync()));
                }
                else
                {
                    QuickSettings.Open(QuickSettings.Wifi);
                }
            });
            WifiList.Children.Add(row);
        }

        BtList.Children.Clear();
        if (bt.Count == 0)
            BtList.Children.Add(EmptyHint("нет сопряжённых"));

        foreach (var d in bt)
        {
            var name = hide ? "Скрыто" : d.Name;
            BtList.Children.Add(ListRow(Icons.Bluetooth, name, d.Connected ? "подключено" : "не подключено",
                d.Connected, () => QuickSettings.Open(QuickSettings.Bluetooth)));
        }

        AudioList.Children.Clear();
        foreach (var d in audio)
        {
            var name = d.Name;
            AudioList.Children.Add(ListRow(Icons.Volume, name, d.IsDefault ? "по умолчанию" : "", d.IsDefault, () =>
            {
                if (d.IsDefault) return;
                if (AudioDeviceService.SetDefault(d.Id))
                {
                    Notify(Icons.Volume, "Звук", $"Вывод: {name}");
                    _ = LoadNetworksAsync();
                }
            }));
        }
    }

    private void RefreshNets_Click(object sender, RoutedEventArgs e) => _ = LoadNetworksAsync();

    private void BtSettings_Click(object sender, RoutedEventArgs e) => QuickSettings.Open(QuickSettings.Bluetooth);

    private static TextBlock EmptyHint(string text) => new()
    {
        Text = text,
        FontSize = 10,
        Margin = new Thickness(4, 4, 0, 0),
        Foreground = Theme.Brush("SubBrush", Colors.Gray),
        TextWrapping = TextWrapping.Wrap
    };

    private Button ListRow(string glyph, string title, string subtitle, bool active, Action click)
    {
        var button = new Button
        {
            Style = (Style)FindResource("RowButton"),
            Height = 32,
            ToolTip = title
        };

        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(18) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        grid.Children.Add(new TextBlock
        {
            Text = glyph,
            FontFamily = new FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets"),
            FontSize = 11,
            VerticalAlignment = VerticalAlignment.Center,
            Foreground = active ? Theme.Brush("AccentBrush", Colors.DodgerBlue) : Theme.Brush("SubBrush", Colors.Gray)
        });

        var label = new TextBlock
        {
            Text = title,
            FontSize = 10.5,
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis,
            Margin = new Thickness(2, 0, 4, 0),
            Foreground = Theme.Brush("TextBrush", Colors.White)
        };
        Grid.SetColumn(label, 1);
        grid.Children.Add(label);

        if (!string.IsNullOrEmpty(subtitle))
        {
            var sub = new TextBlock
            {
                Text = subtitle,
                FontSize = 8.5,
                VerticalAlignment = VerticalAlignment.Center,
                Foreground = active ? Theme.Brush("AccentBrush", Colors.DodgerBlue) : Theme.Brush("SubBrush", Colors.Gray)
            };
            Grid.SetColumn(sub, 2);
            grid.Children.Add(sub);
        }

        button.Content = grid;
        button.Click += (_, _) => click();
        return button;
    }

    // ── clipboard + note ──────────────────────────────────────────────────
    private void OnClipboardUpdate()
    {
        if (_clipBusy) return;

        _clipBusy = true;
        try
        {
            var hasImage = Clipboard.ContainsImage();
            if (hasImage && !_settings.ClipboardImages) return;

            var item = ClipStore.CaptureFromClipboard();
            if (item is null) return;

            // identical content bubbles to the top instead of duplicating
            var existing = _settings.Clips.FirstOrDefault(c => c.Hash == item.Hash);
            if (existing is not null)
            {
                if (existing.Pinned) return;
                _settings.Clips.Remove(existing);
                item.At = DateTime.Now;
            }

            _settings.Clips.Insert(0, item);
            ClipStore.Trim(_settings.Clips);
            _settings.Save();

            _lastClipboard = item.IsImage ? item.File : item.Text;
            RenderClipboard();
        }
        catch
        {
            // clipboard busy
        }
        finally
        {
            _clipBusy = false;
        }
    }

    private void RenderClipboard()
    {
        if (ClipboardList is null) return;

        ClipboardList.Children.Clear();

        if (_settings.Clips.Count == 0)
        {
            ClipboardList.Children.Add(EmptyHint(L.T("пусто — скопируй что-нибудь")));
            return;
        }

        var ordered = _settings.Clips
            .OrderByDescending(c => c.Pinned)
            .ThenByDescending(c => c.At)
            .ToList();

        foreach (var item in ordered)
            ClipboardList.Children.Add(ClipRow(item));
    }

    private UIElement ClipRow(ClipItem item)
    {
        var root = new Button
        {
            Style = (Style)FindResource("RowButton"),
            Height = 36
        };

        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(40) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(26) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(22) });

        FrameworkElement preview;

        if (item.IsImage)
        {
            var thumb = ClipStore.Thumbnail(item, 72);
            preview = new Border
            {
                Width = 36, Height = 26, CornerRadius = new CornerRadius(6),
                Background = Theme.Brush("TrackBrush", Colors.DimGray),
                ClipToBounds = true,
                Child = thumb is null
                    ? new TextBlock
                    {
                        Text = Icons.Image,
                        FontFamily = new FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets"),
                        FontSize = 11,
                        HorizontalAlignment = HorizontalAlignment.Center,
                        VerticalAlignment = VerticalAlignment.Center,
                        Foreground = Theme.Brush("SubBrush", Colors.Gray)
                    }
                    : new System.Windows.Controls.Image { Source = thumb, Stretch = Stretch.UniformToFill },
                VerticalAlignment = VerticalAlignment.Center
            };
        }
        else
        {
            preview = new TextBlock
            {
                Text = Icons.Clipboard,
                FontFamily = new FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets"),
                FontSize = 12,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(4, 0, 0, 0),
                Foreground = Theme.Brush("SubBrush", Colors.Gray)
            };
        }

        grid.Children.Add(preview);

        var text = item.IsImage
            ? $"{L.T("Изображение")} · {item.At:HH:mm}"
            : item.Text.Replace("\r", " ").Replace("\n", " ");

        if (text.Length > 64) text = text[..64] + "…";

        var label = new TextBlock
        {
            Text = text,
            FontSize = 10.5,
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis,
            Margin = new Thickness(2, 0, 4, 0),
            Foreground = Theme.Brush("TextBrush", Colors.White)
        };
        Grid.SetColumn(label, 1);
        grid.Children.Add(label);

        var pin = new Button
        {
            Style = (Style)FindResource("Round"),
            Width = 24, Height = 24,
            Background = Brushes.Transparent,
            ToolTip = item.Pinned ? L.T("Открепить") : L.T("Закрепить"),
            Content = new TextBlock
            {
                Text = item.Pinned ? Icons.PinFilled : Icons.Pin,
                FontFamily = new FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets"),
                FontSize = 10,
                Foreground = item.Pinned
                    ? Theme.Brush("AccentBrush", Colors.DodgerBlue)
                    : Theme.Brush("MutedBrush", Colors.Gray)
            }
        };
        pin.Click += (_, e) =>
        {
            item.Pinned = !item.Pinned;
            _settings.Save();
            RenderClipboard();
            e.Handled = true;
        };
        Grid.SetColumn(pin, 2);
        grid.Children.Add(pin);

        var del = new Button
        {
            Style = (Style)FindResource("Round"),
            Width = 18, Height = 18,
            Background = Brushes.Transparent,
            ToolTip = L.T("Удалить"),
            Content = new TextBlock
            {
                Text = "✕",
                FontSize = 8,
                Foreground = Theme.Brush("MutedBrush", Colors.Gray)
            }
        };
        del.Click += (_, e) =>
        {
            _settings.Clips.Remove(item);
            ClipStore.DeleteFiles(item);
            _settings.Save();
            RenderClipboard();
            e.Handled = true;
        };
        Grid.SetColumn(del, 3);
        grid.Children.Add(del);

        root.Content = grid;
        root.Click += (_, e) =>
        {
            ClipStore.CopyToClipboard(item);
            _lastClipboard = item.IsImage ? item.File : item.Text;

            _ = FlashRow(root);

            Notify(Icons.Clipboard, L.T("Скопировано"),
                item.IsImage ? L.T("Изображение") : (item.Text.Length > 40 ? item.Text[..40] + "…" : item.Text));

            e.Handled = true;
        };

        return root;
    }

    private static async Task FlashRow(Button row)
    {
        var brush = new SolidColorBrush(Theme.Accent) { Opacity = 0.22 };
        row.Background = brush;

        await Task.Delay(240);

        var fade = new DoubleAnimation(0.22, 0, TimeSpan.FromMilliseconds(320))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        };
        brush.BeginAnimation(Brush.OpacityProperty, fade);

        await Task.Delay(360);
        row.Background = Brushes.Transparent;
    }

    private void ClearClipboard_Click(object sender, RoutedEventArgs e)
    {
        foreach (var item in _settings.Clips.Where(c => !c.Pinned).ToList())
            ClipStore.DeleteFiles(item);

        _settings.Clips.RemoveAll(c => !c.Pinned);
        _settings.ClipboardHistory.Clear();
        _settings.Save();
        RenderClipboard();
    }

    private void NoteBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        NotePlaceholder.Visibility = NoteBox.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;

        if (_loadingNote || !_ready) return;
        _noteTimer.Stop();
        _noteTimer.Start();
    }

    private void FlashSaved()
    {
        NoteSaved.BeginAnimation(OpacityProperty,
            new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(1400))
            {
                BeginTime = TimeSpan.FromMilliseconds(250),
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn }
            });
    }

    // ── favourites ────────────────────────────────────────────────────────
    private string TrackKey => $"{_now.Artist}|{_now.Title}".ToLowerInvariant();

    private void Like_Click(object sender, RoutedEventArgs e)
    {
        if (!_now.HasSession) return;

        var key = TrackKey;
        if (_settings.Favorites.RemoveAll(k => k == key) > 0)
        {
            Notify(Icons.Music, "Убрано из избранного", _now.Title);
        }
        else
        {
            _settings.Favorites.Add(key);
            Notify(Icons.Music, "В избранном", _now.Title);
        }

        _settings.Save();
        UpdateLikeButton();
        UpdateFavoritesButton();
    }

    private void UpdateLikeButton()
    {
        var liked = _now.HasSession && _settings.Favorites.Contains(TrackKey);
        LikeGlyph.Text       = liked ? "\uEB52" : "\uEB51";
        LikeGlyph.Foreground = liked
            ? Theme.Brush("BadBrush", Colors.IndianRed)
            : Theme.Brush("SubBrush", Colors.Gray);
        LikeBtn.ToolTip = liked ? "Убрать из избранного" : "В избранное";
    }

    private void UpdateFavoritesButton()
    {
        var count = _settings.Favorites.Count;
        FavoritesButton.Visibility = count > 0 ? Visibility.Visible : Visibility.Collapsed;
        FavoritesText.Text = $"♥ Избранное: {count}   ·   нажми, чтобы скопировать список";
    }

    private void Favorites_Click(object sender, RoutedEventArgs e)
    {
        if (_settings.Favorites.Count == 0) return;

        var text = string.Join(Environment.NewLine, _settings.Favorites.Select(f =>
        {
            var parts = f.Split('|');
            return parts.Length == 2 ? $"{parts[0]} — {parts[1]}" : f;
        }));

        try
        {
            Clipboard.SetText(text);
            Notify(Icons.Music, "Избранное", "Список скопирован");
        }
        catch { }
    }

    // ── equalizer ─────────────────────────────────────────────────────────
    private void TickEqualizer()
    {
        // the mixer list refreshes itself while its tab is open
        if (_mode == Mode.Expanded && _tab == "nets" && !MixerList.IsMouseOver &&
            DateTime.Now > _mixerAt)
        {
            _mixerAt = DateTime.Now.AddSeconds(3.5);
            _ = LoadMixerAsync();
        }

        if (_mode != Mode.Expanded || _tab != "media") return;

        UpdateKaraokeProgress();

        var useMic = _settings.VisualizerSource == "mic";

        if (!_settings.ShowEqualizer || (!_now.IsPlaying && !useMic))
        {
            _level *= 0.85;
        }
        else
        {
            if (++_eqTick % 3 == 0)
            {
                var peak = useMic ? AudioDeviceService.MicPeak() : AudioDeviceService.Peak();
                _level = Math.Max(peak, _level * 0.80);
            }
        }

        var height = 4 + _level * 22;
        var jitter = new[] { 0.55, 0.85, 1.0, 0.75, 0.45 };
        var bars   = new[] { Eq1, Eq2, Eq3, Eq4, Eq5 };

        for (var i = 0; i < bars.Length; i++)
        {
            var value = 4 + height * jitter[i] * (0.85 + Random.Shared.NextDouble() * 0.3);
            bars[i].Height = Math.Clamp(value, 3, 26);
        }
    }

    // ── settings tab + window ─────────────────────────────────────────────
    private void Theme_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button b || b.Tag is not string theme) return;

        _settings.Theme = theme;
        _settings.Save();
        ApplySettings();
    }

    private void Size_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button b || b.Tag is not string size) return;

        _settings.Size = size;
        _settings.Save();
        ApplySettings();
    }

    private void Hover_Changed(object sender, RoutedEventArgs e)
    {
        if (_syncUi) return;
        _settings.HoverExpand = HoverSwitch.IsChecked == true;
        _settings.Save();
    }

    private void Idle_Changed(object sender, RoutedEventArgs e)
    {
        if (_syncUi) return;
        _settings.IdleCollapse = IdleSwitch.IsChecked == true;
        _settings.Save();
        if (!_settings.IdleCollapse && _idle)
        {
            _idle = false;
            ApplySizePreset();
        }
    }

    private void Wheel_Changed(object sender, RoutedEventArgs e)
    {
        if (_syncUi) return;
        _settings.WheelVolume = WheelSwitch.IsChecked == true;
        _settings.Save();
    }

    private void Eq_Changed(object sender, RoutedEventArgs e)
    {
        if (_syncUi) return;
        _settings.ShowEqualizer = EqSwitch.IsChecked == true;
        _settings.Save();
        EqualizerBox.Visibility = _settings.ShowEqualizer ? Visibility.Visible : Visibility.Collapsed;
    }

    private void Stream_Changed(object sender, RoutedEventArgs e)
    {
        if (_syncUi) return;
        _settings.StreamerMode = StreamSwitch.IsChecked == true;
        _settings.Save();
        ApplySettings();
    }

    private void UpdateSettingsTabState()
    {
        _syncUi = true;
        try
        {
            HoverSwitch.IsChecked  = _settings.HoverExpand;
            IdleSwitch.IsChecked   = _settings.IdleCollapse;
            WheelSwitch.IsChecked  = _settings.WheelVolume;
            EqSwitch.IsChecked     = _settings.ShowEqualizer;
            StreamSwitch.IsChecked = _settings.StreamerMode;

            var light = _settings.Theme == "light";
            Highlight(DarkBtn, !light);
            Highlight(LightBtn, light);

            Highlight(SizeCompact, _settings.Size == "compact");
            Highlight(SizeNormal,  _settings.Size is not ("compact" or "large"));
            Highlight(SizeLarge,   _settings.Size == "large");
        }
        finally
        {
            _syncUi = false;
        }
    }

    private static void Highlight(Button button, bool on)
    {
        button.Background = on ? Theme.Brush("AccentBrush", Colors.DodgerBlue) : Theme.Brush("CardBrush", Colors.Transparent);
        button.Foreground = on ? Theme.Brush("AccentInkBrush", Colors.White) : Theme.Brush("TextBrush", Colors.White);
    }

    private void Settings_Click(object sender, RoutedEventArgs e) => OpenSettings();

    private void OpenSettings()
    {
        if (_settingsWindow is { IsLoaded: true })
        {
            _settingsWindow.Activate();
            return;
        }

        _settingsWindow = new SettingsWindow(_settings);
        _settingsWindow.Changed += () =>
        {
            ApplySettings();
            _ = RefreshAsync();
        };
        _settingsWindow.ResetRequested += () =>
        {
            _settings.Left = -1;
            _settings.Save();
            Place();
        };
        _settingsWindow.Closed += (_, _) => _settingsWindow = null;
        _settingsWindow.Show();
    }

    private void ApplySettings()
    {
        Theme.Apply(_settings);

        if (_appliedLang != _settings.Language)
        {
            _appliedLang = _settings.Language;
            L.Set(_appliedLang);
            Launcher.Language = _appliedLang;
            Localizer.Apply(this);
            if (_settingsWindow is { IsVisible: true }) Localizer.Apply(_settingsWindow);
        }

        if (_autostart != _settings.Autostart) SetAutostart(_settings.Autostart);

        if (_settings.Hotkeys) RegisterHotkeys();
        else UnregisterHotkeys();

        ApplySizePreset();
        UpdatePinButton();
        UpdateLikeButton();
        UpdateFavoritesButton();
        UpdateNightButton();
        UpdatePaletteDots();
        UpdateSettingsTabState();
        UpdateBarVisibility();
        UpdateTimerUi();
        ApplyWeather();
        RenderClipboard();
        RenderSearch();
        _ = UpdateWeatherAsync(true);
        _ = RefreshAsync();
    }

    private void UpdateBarVisibility()
    {
        BarWeatherBox.Visibility = _settings.ShowWeather && _weather is not null
            ? Visibility.Visible
            : Visibility.Collapsed;

        CompactMic.Visibility = _settings.ShowMic && _micOn ? Visibility.Visible : Visibility.Collapsed;
        EqualizerBox.Visibility = _settings.ShowEqualizer ? Visibility.Visible : Visibility.Collapsed;

        if (!_settings.ShowWeather) BarWeatherBox.Visibility = Visibility.Collapsed;
    }

    // ── offsets ───────────────────────────────────────────────────────────
    private void OffsetDown_Click(object sender, RoutedEventArgs e) => ShiftLyrics(-0.5);
    private void OffsetUp_Click(object sender, RoutedEventArgs e)   => ShiftLyrics(+0.5);

    private void ShiftLyrics(double delta)
    {
        _settings.LyricsOffset = Math.Clamp(Math.Round(_settings.LyricsOffset + delta, 1), -5, 5);
        _settings.Save();
        OffsetText.Text = _settings.LyricsOffset.ToString("+0.0;-0.0;0.0", System.Globalization.CultureInfo.InvariantCulture) + (L.IsEn ? " s" : " с");
        _lyricIndex = int.MinValue;
        UpdateLyrics();
    }

    // ══ timer ═════════════════════════════════════════════════════════════
    private void ApplyTimerSettings()
    {
        var mode = _settings.TimerMode switch
        {
            "stopwatch" => IslandTimerMode.Stopwatch,
            "pomodoro"  => IslandTimerMode.Pomodoro,
            _           => IslandTimerMode.Countdown
        };

        _timer.SetMode(mode, TimeSpan.FromMinutes(Math.Clamp(_settings.TimerMinutes, 1, 240)));
        UpdateTimerUi();
    }

    private void TickTimer()
    {
        if (!_timer.Running) return;

        var finished = _timer.Tick(TimeSpan.FromMilliseconds(100));
        UpdateTimerUi();

        if (!finished) return;

        SoundService.Alarm();
        FlashTimerRing();

        var title = _timer.IsPomodoro
            ? _timer.Phase == "break" ? L.T("Пора отдохнуть") : L.T("Пора работать")
            : L.T("Время вышло");

        if (_timer.IsPomodoro)
            Notify(Icons.Timer, title, $"{L.T("цикл")} {_timer.Cycle} · {IslandTimer.Format(_timer.Duration)}");
        else
            Notify(Icons.Timer, title, IslandTimer.Format(_timer.Duration));
    }

    private void UpdateTimerUi()
    {
        if (TimerTime is null) return;

        var display  = _timer.Display;
        var text     = IslandTimer.Format(display);
        var fraction = _timer.IsStopwatch
            ? (_timer.Elapsed.TotalSeconds % 60) / 60.0
            : Math.Clamp(1 - _timer.Fraction, 0, 1);

        var visible = _timer.Running || _timer.Elapsed > TimeSpan.Zero;
        BarTimer.Visibility  = visible ? Visibility.Visible : Visibility.Collapsed;
        BarTimerText.Text    = text;

        if (visible) BarRing.Data = ArcGeometry(22, fraction);
        TimerRing.Data = ArcGeometry(122, fraction);

        TimerTime.Text = text;
        TimerPhase.Text = _timer.IsPomodoro
            ? (_timer.Phase == "break"
                ? $"{L.T("перерыв")} · {L.T("цикл")} {_timer.Cycle}"
                : L.T("работа"))
            : "";

        TimerStartBtn.Content = _timer.Running ? L.T("Пауза") : L.T("Старт");
        TimerPresets.Visibility = _timer.IsStopwatch ? Visibility.Collapsed : Visibility.Visible;

        Highlight(TmStopwatch, _timer.IsStopwatch);
        Highlight(TmCountdown, _timer.Mode == IslandTimerMode.Countdown);
        Highlight(TmPomodoro,  _timer.IsPomodoro);

        if (text != _timerText)
        {
            _timerText = text;
            if (_mode == Mode.Collapsed && _ready) ResizeToContent();
        }
    }

    /// <summary>Arc for a ring of <paramref name="size"/> px, starting at 12 o'clock.</summary>
    private static Geometry? ArcGeometry(double size, double fraction)
    {
        fraction = Math.Clamp(fraction, 0, 1);
        if (fraction <= 0.0005) return null;

        var r = size / 2 - 3;
        var c = new Point(size / 2, size / 2);

        if (fraction >= 0.9995) return new EllipseGeometry(c, r, r);

        var angle = fraction * 360 - 90;
        var rad   = angle * Math.PI / 180;
        var end   = new Point(c.X + r * Math.Cos(rad), c.Y + r * Math.Sin(rad));

        var figure = new PathFigure
        {
            StartPoint = new Point(c.X, c.Y - r),
            IsClosed   = false,
            IsFilled   = false
        };
        figure.Segments.Add(new ArcSegment(end, new Size(r, r), 0, fraction > 0.5,
            SweepDirection.Clockwise, true));

        var geometry = new PathGeometry();
        geometry.Figures.Add(figure);
        geometry.Freeze();
        return geometry;
    }

    private void FlashTimerRing()
    {
        var pulse = new DoubleAnimation(1, 0.25, TimeSpan.FromMilliseconds(220))
        {
            AutoReverse    = true,
            RepeatBehavior = new RepeatBehavior(3),
            EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut }
        };

        TimerRing.BeginAnimation(OpacityProperty, pulse);
        BarRing.BeginAnimation(OpacityProperty, pulse);
    }

    private void TimerMode_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button b || b.Tag is not string mode) return;

        var m = mode switch
        {
            "stopwatch" => IslandTimerMode.Stopwatch,
            "pomodoro"  => IslandTimerMode.Pomodoro,
            _           => IslandTimerMode.Countdown
        };

        _timer.SetMode(m, TimeSpan.FromMinutes(Math.Clamp(_settings.TimerMinutes, 1, 240)));
        _settings.TimerMode = mode;
        _settings.Save();
        UpdateTimerUi();
    }

    private void TimerStart_Click(object sender, RoutedEventArgs e)
    {
        _timer.Toggle();
        UpdateTimerUi();
    }

    private void TimerReset_Click(object sender, RoutedEventArgs e)
    {
        _timer.Reset();
        UpdateTimerUi();
    }

    private void TimerPlus_Click(object sender, RoutedEventArgs e)
    {
        _timer.Add(TimeSpan.FromMinutes(1));
        UpdateTimerUi();
    }

    private void TimerPreset_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button b || b.Tag is not string raw || !int.TryParse(raw, out var minutes)) return;

        _timer.SetMode(IslandTimerMode.Countdown);
        _timer.SetDuration(TimeSpan.FromMinutes(minutes));

        _settings.TimerMode    = "countdown";
        _settings.TimerMinutes = minutes;
        _settings.Save();
        UpdateTimerUi();
    }

    // ══ search ════════════════════════════════════════════════════════════
    private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        _resultIndex = 0;
        RenderSearch();
    }

    private void SearchBox_KeyDown(object sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Down:
                if (_results.Count > 0)
                {
                    _resultIndex = Math.Min(_results.Count - 1, _resultIndex + 1);
                    RenderSearch();
                }
                e.Handled = true;
                break;

            case Key.Up:
                if (_results.Count > 0)
                {
                    _resultIndex = Math.Max(0, _resultIndex - 1);
                    RenderSearch();
                }
                e.Handled = true;
                break;

            case Key.Enter:
                RunResult(_resultIndex);
                e.Handled = true;
                break;

            case Key.Escape:
                SearchBox.Text = "";
                e.Handled = true;
                break;
        }
    }

    private void RenderSearch()
    {
        if (SearchList is null) return;

        SearchList.Children.Clear();
        _results.Clear();

        var query = SearchBox.Text ?? "";
        _results.AddRange(Launcher.Query(query, 7));
        _resultIndex = Math.Clamp(_resultIndex, 0, Math.Max(0, _results.Count - 1));

        if (_results.Count == 0)
        {
            SearchList.Children.Add(EmptyHint(L.T("Ничего не найдено")));
            return;
        }

        for (var i = 0; i < _results.Count; i++)
        {
            var item  = _results[i];
            var index = i;

            var row = new Button
            {
                Style  = (Style)FindResource("RowButton"),
                Height = 32,
                Background = i == _resultIndex ? Theme.Brush("CardBrush", Colors.Transparent) : Brushes.Transparent
            };

            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(24) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            grid.Children.Add(new TextBlock
            {
                Text = item.Glyph,
                FontFamily = new FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets"),
                FontSize = 12,
                VerticalAlignment = VerticalAlignment.Center,
                Foreground = i == _resultIndex
                    ? Theme.Brush("AccentBrush", Colors.DodgerBlue)
                    : Theme.Brush("SubBrush", Colors.Gray)
            });

            var label = new TextBlock
            {
                Text = item.Kind == LaunchKind.App ? L.T(item.Title) : item.Title,
                FontSize = 11.5,
                VerticalAlignment = VerticalAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis,
                Foreground = Theme.Brush("TextBrush", Colors.White)
            };
            Grid.SetColumn(label, 1);
            grid.Children.Add(label);

            var hint = new TextBlock
            {
                Text = L.T(item.Hint),
                FontSize = 9.5,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(8, 0, 2, 0),
                Foreground = Theme.Brush("SubBrush", Colors.Gray)
            };
            Grid.SetColumn(hint, 2);
            grid.Children.Add(hint);

            row.Content = grid;
            row.Click += (_, _) => RunResult(index);
            SearchList.Children.Add(row);
        }
    }

    private void RunResult(int index)
    {
        if (index < 0 || index >= _results.Count) return;

        var item = _results[index];
        Launcher.Run(item);

        switch (item.Kind)
        {
            case LaunchKind.App:
            case LaunchKind.Web:
                SearchBox.Text = "";
                SetMode(Mode.Collapsed);
                break;

            case LaunchKind.Calc:
            case LaunchKind.Currency when !string.IsNullOrEmpty(item.Payload):
                Notify(Icons.Clipboard, L.T("Скопировано"), item.Payload);
                break;
        }
    }

    // ══ app mixer ═════════════════════════════════════════════════════════
    private async void RefreshMixer_Click(object sender, RoutedEventArgs e) => await LoadMixerAsync();

    private async Task LoadMixerAsync()
    {
        if (MixerList is null) return;

        List<MixerSession> sessions;
        try
        {
            sessions = await Task.Run(AppMixer.List);
        }
        catch
        {
            return;
        }

        if (!IsLoaded) return;

        MixerList.Children.Clear();

        if (sessions.Count == 0)
        {
            MixerList.Children.Add(EmptyHint(L.T("нет активных приложений")));
            return;
        }

        _mixerSync = true;
        try
        {
            foreach (var session in sessions)
                MixerList.Children.Add(MixerRow(session));
        }
        finally
        {
            _mixerSync = false;
        }
    }

    private UIElement MixerRow(MixerSession session)
    {
        var grid = new Grid { Height = 30, Margin = new Thickness(3, 0, 0, 0) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(88) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(28) });

        var name = new TextBlock
        {
            Text = string.IsNullOrWhiteSpace(session.Title) ? session.Name : session.Title,
            FontSize = 10.5,
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis,
            Margin = new Thickness(0, 0, 6, 0),
            Foreground = Theme.Brush("TextBrush", Colors.White)
        };
        grid.Children.Add(name);

        var slider = new Slider
        {
            Style = (Style)FindResource("Slim"),
            Minimum = 0,
            Maximum = 100,
            Value = session.Volume,
            IsMoveToPointEnabled = true,
            VerticalAlignment = VerticalAlignment.Center
        };
        slider.ValueChanged += (_, e) =>
        {
            if (_mixerSync) return;
            AppMixer.SetVolume(session.Pids, (int)e.NewValue);
        };
        Grid.SetColumn(slider, 1);
        grid.Children.Add(slider);

        var mute = new Button
        {
            Style = (Style)FindResource("Round"),
            Width = 24, Height = 24,
            Background = Brushes.Transparent,
            ToolTip = L.T("Заглушить"),
            Content = new TextBlock
            {
                Text = session.Muted ? Icons.VolumeMute : Icons.Volume,
                FontFamily = new FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets"),
                FontSize = 11,
                Foreground = session.Muted
                    ? Theme.Brush("BadBrush", Colors.IndianRed)
                    : Theme.Brush("SubBrush", Colors.Gray)
            }
        };
        mute.Click += (_, _) =>
        {
            AppMixer.SetMute(session.Pids, !session.Muted);
            _mixerAt = DateTime.MinValue;
        };
        Grid.SetColumn(mute, 2);
        grid.Children.Add(mute);

        return grid;
    }

    // ══ palette + language ════════════════════════════════════════════════
    private void Palette_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button b || b.Tag is not string preset) return;

        _settings.ThemePreset = preset;
        _settings.AccentMode  = "preset";
        _settings.Save();

        Theme.Apply(_settings);
        ApplySizePreset();
        UpdatePaletteDots();
        UpdateSettingsTabState();
    }

    private void UpdatePaletteDots()
    {
        if (PalGraphite is null) return;

        var preset = _settings.ThemePreset ?? "graphite";

        foreach (var (button, key) in new[]
                 {
                     (PalGraphite, "graphite"), (PalMidnight, "midnight"), (PalNord, "nord"),
                     (PalMono, "mono"), (PalSand, "sand")
                 })
        {
            button.BorderBrush = key == preset
                ? Theme.Brush("TextBrush", Colors.White)
                : Brushes.Transparent;
        }
    }

    private void Lang_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button b || b.Tag is not string lang) return;
        SetLanguage(lang);
    }

    private void SetLanguage(string lang)
    {
        _settings.Language = lang == "en" ? "en" : "ru";
        _settings.Save();

        _appliedLang = _settings.Language;
        L.Set(_settings.Language);
        Launcher.Language = _settings.Language;

        Localizer.Apply(this);
        if (_settingsWindow is { IsVisible: true }) Localizer.Apply(_settingsWindow);

        BuildTrayMenu();
        UpdatePaletteDots();
        UpdateTimerUi();
        UpdateNightButton();
        UpdateLikeButton();
        UpdateSettingsTabState();
        RenderClipboard();
        RenderSearch();
        ResizeToContent();
    }

    // ══ fullscreen auto-hide ══════════════════════════════════════════════
    private void CheckFullscreen()
    {
        if (!_settings.AutoHideFullscreen)
        {
            if (_hiddenFs) RestoreFromFullscreen();
            return;
        }

        var settingsHwnd = _settingsWindow is { IsVisible: true }
            ? new WindowInteropHelper(_settingsWindow).Handle
            : IntPtr.Zero;

        bool full;
        try
        {
            full = FullscreenWatcher.IsFullscreen(_hwnd, settingsHwnd);
        }
        catch
        {
            return;
        }

        // never yank the island away while the user is using it
        if (full && DateTime.Now - _lastInteraction < TimeSpan.FromSeconds(3)) return;

        if (full && !_hiddenFs) HideForFullscreen();
        else if (!full && _hiddenFs) RestoreFromFullscreen();
    }

    private void HideForFullscreen()
    {
        _hiddenFs = true;
        _poll.Stop();
        _eqTimer.Stop();
        Hide();
    }

    private void RestoreFromFullscreen()
    {
        _hiddenFs = false;
        Show();
        Place();
        Topmost = true;
        _poll.Start();
        _eqTimer.Start();
        SetMode(Mode.Collapsed);
        _ = RefreshAsync();
    }

    // ══ hotkeys ═══════════════════════════════════════════════════════════
    private const uint MOD_ALT     = 0x0001;
    private const uint MOD_CONTROL = 0x0002;

    [DllImport("user32.dll")]
    private static extern bool RegisterHotKey(IntPtr hwnd, int id, uint modifiers, uint key);

    [DllImport("user32.dll")]
    private static extern bool UnregisterHotKey(IntPtr hwnd, int id);

    private void RegisterHotkeys()
    {
        if (!_settings.Hotkeys || _hwnd == IntPtr.Zero || _hotkeysRegistered) return;

        _hotkeysRegistered = RegisterHotKey(_hwnd, HotkeySearchId, MOD_CONTROL | MOD_ALT, 0x20 /*space*/);
        RegisterHotKey(_hwnd, HotkeyMuteId, MOD_CONTROL | MOD_ALT, 0x4D /*M*/);
    }

    private void UnregisterHotkeys()
    {
        if (_hwnd == IntPtr.Zero || !_hotkeysRegistered) return;

        UnregisterHotKey(_hwnd, HotkeySearchId);
        UnregisterHotKey(_hwnd, HotkeyMuteId);
        _hotkeysRegistered = false;
    }
}

