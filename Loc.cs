using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace Salli;

/// <summary>
/// Tiny two-language dictionary. The Russian string is the key, so a missing
/// translation simply falls back to Russian instead of showing a token.
/// </summary>
public static class L
{
    public static string Lang { get; private set; } = "ru";
    public static bool IsEn => Lang == "en";

    public static void Set(string? lang) => Lang = lang == "en" ? "en" : "ru";

    private static readonly Dictionary<string, string> En = new(StringComparer.Ordinal)
    {
        // tabs
        ["Медиа"] = "Media",
        ["Система"] = "System",
        ["Сети"] = "Network",
        ["Буфер"] = "Clips",
        ["Поиск"] = "Search",
        ["Таймер"] = "Timer",
        ["Настройки"] = "Settings",

        // bar / header
        ["Ничего не играет"] = "Nothing playing",
        ["Закрепить панель"] = "Pin panel",
        ["Закреплено (нажми, чтобы открепить)"] = "Pinned (click to unpin)",
        ["Свернуть"] = "Collapse",
        ["Закрыть Salli"] = "Quit Salli",
        ["Закрыть"] = "Close",
        ["Микрофон используется"] = "Microphone in use",
        ["Пауза / играть"] = "Play / pause",
        ["Предыдущий"] = "Previous",
        ["Следующий"] = "Next",
        ["В избранное"] = "Add to favourites",
        ["Убрать из избранного"] = "Remove from favourites",
        ["Заглушить"] = "Mute",
        ["Обновить"] = "Refresh",
        ["Параметры Bluetooth"] = "Bluetooth settings",
        ["Ночной"] = "Night light",
        ["Ночной вкл"] = "Night light on",
        ["Не беспокоить"] = "Do not disturb",

        // sections
        ["НАГРУЗКА"] = "LOAD",
        ["процессор / память"] = "cpu / memory",
        ["БУФЕР ОБМЕНА"] = "CLIPBOARD",
        ["ЗАМЕТКА"] = "NOTE",
        ["Быстрая заметка…"] = "Quick note…",
        ["ЗВУК"] = "SOUND",
        ["ПРИЛОЖЕНИЯ"] = "APPS",
        ["ТЕМА И РАЗМЕР"] = "THEME & SIZE",
        ["ВНЕШНИЙ ВИД"] = "APPEARANCE",
        ["ПОВЕДЕНИЕ"] = "BEHAVIOUR",
        ["ЧТО ПОКАЗЫВАТЬ"] = "WHAT TO SHOW",
        ["СИСТЕМА"] = "SYSTEM",
        ["Сдвиг слов"] = "Lyrics offset",
        ["Диск"] = "Disk",
        ["Сеть"] = "Network",

        // settings window
        ["  настройки"] = "  settings",
        ["Тема"] = "Theme",
        ["Тёмная"] = "Dark",
        ["Светлая"] = "Light",
        ["Акцент"] = "Accent",
        ["Из обоев"] = "From wallpaper",
        ["Пресет"] = "Preset",
        ["Размер островка"] = "Island size",
        ["Мини"] = "Small",
        ["Обычный"] = "Normal",
        ["Крупный"] = "Large",
        ["Раскрывать при наведении"] = "Expand on hover",
        ["без клика, через 0.4 сек"] = "no click, after 0.4 s",
        ["Тоньше при простое"] = "Thin out when idle",
        ["через 30 секунд без действий"] = "after 30 seconds idle",
        ["Громкость колесом"] = "Volume with wheel",
        ["крутить мышь над островком"] = "scroll over the island",
        ["Свайп вниз — свернуть"] = "Swipe down to collapse",
        ["потянуть островок вниз мышью"] = "drag the island downwards",
        ["Звук открытия"] = "Open sound",
        ["тихий «поп» при раскрытии"] = "soft pop when expanding",
        ["Режим стримера"] = "Streamer mode",
        ["скрывает SSID и название трека"] = "hides SSID and track title",
        ["Погода в баре"] = "Weather in the bar",
        ["Индикатор микрофона"] = "Microphone indicator",
        ["Скорость сети"] = "Network speed",
        ["Эквалайзер"] = "Equalizer",
        ["Город для погоды"] = "Weather city",
        ["Автозапуск с Windows"] = "Start with Windows",
        ["Ночной свет"] = "Night light",
        ["тёплый фильтр экрана"] = "warm screen filter",
        ["Сбросить островок по центру"] = "Re-centre the island",
        ["Готово"] = "Done",
        ["Все настройки…"] = "All settings…",
        ["сохранено"] = "saved",
        ["Очистить историю"] = "Clear history",
        ["пусто — скопируй что-нибудь"] = "empty — copy something",
        ["Скопировано"] = "Copied",
        ["Изображение"] = "Image",
        ["Закрепить"] = "Pin",
        ["Открепить"] = "Unpin",
        ["Закреплённые"] = "Pinned",

        // new settings
        ["Автоскрытие в полноэкранных приложениях"] = "Hide in fullscreen apps",
        ["игры, видео, презентации"] = "games, video, presentations",
        ["Скрывать в полном экране"] = "Hide when fullscreen",
        ["игры и видео — островок уезжает"] = "games and video — the island hides",
        ["что рисует эквалайзер"] = "what feeds the equalizer",
        ["Микрофон"] = "Microphone",
        ["скриншоты сохраняются с превью"] = "screenshots are kept as thumbnails",
        ["Удалить"] = "Delete",
        ["Русский"] = "Russian",
        ["Ctrl+Alt+Space — поиск · Ctrl+Alt+M — mute"] = "Ctrl+Alt+Space — search · Ctrl+Alt+M — mute",
        ["Язык"] = "Language",
        ["Палитра"] = "Palette",
        ["Стекло"] = "Glass",
        ["полупрозрачная поверхность"] = "translucent surface",
        ["Визуализатор"] = "Visualizer",
        ["выход"] = "output",
        ["микрофон"] = "microphone",
        ["авто"] = "auto",
        ["Картинки в буфере"] = "Images in clipboard",
        ["хранить до 8 скриншотов"] = "keep up to 8 screenshots",
        ["Горячие клавиши"] = "Hotkeys",
        ["Ctrl+Alt+Space — поиск, Ctrl+Alt+M — mute"] = "Ctrl+Alt+Space — search, Ctrl+Alt+M — mute",

        // search
        ["Поиск и запуск"] = "Search & launch",
        ["Начни печатать: приложение, 2+2, 100 usd в rub"] = "Start typing: app, 2+2, 100 usd in rub",
        ["Показать"] = "Show",
        ["Настройки…"] = "Settings…",
        ["Выход"] = "Quit",
        ["приложение"] = "application",
        ["искать в интернете"] = "search the web",
        ["Ничего не найдено"] = "Nothing found",
        ["Enter — запустить · ↑↓ — выбрать · Esc — очистить"] = "Enter — launch · ↑↓ — choose · Esc — clear",
        ["Калькулятор"] = "Calculator",
        ["Параметры"] = "Settings",
        ["Проводник"] = "File Explorer",
        ["Блокнот"] = "Notepad",
        ["Терминал"] = "Terminal",
        ["Диспетчер задач"] = "Task Manager",
        ["Курс загружается…"] = "Loading rates…",

        // timer
        ["Секундомер"] = "Stopwatch",
        ["Таймер"] = "Timer",
        ["Помодоро"] = "Pomodoro",
        ["Старт"] = "Start",
        ["Пауза"] = "Pause",
        ["Сброс"] = "Reset",
        ["+1 мин"] = "+1 min",
        ["работа"] = "focus",
        ["перерыв"] = "break",
        ["Время вышло"] = "Time is up",
        ["Пора отдохнуть"] = "Time for a break",
        ["Пора работать"] = "Time to focus",
        ["цикл"] = "cycle",
        ["Фокус 25 минут, перерыв 5"] = "25 minutes focus, 5 minute break",
        ["5 мин"] = "5 min",
        ["10 мин"] = "10 min",
        ["15 мин"] = "15 min",
        ["25 мин"] = "25 min",
        ["45 мин"] = "45 min",

        // mixer
        ["Громкость приложений"] = "App volume",
        ["нет активных приложений"] = "no active apps",

        // misc
        ["нет данных"] = "no data",
        ["подключено"] = "connected",
        ["выключено"] = "off",
        ["не подключено"] = "disconnected",
        ["по умолчанию"] = "default",
        ["сети не найдены"] = "no networks found",
        ["Wi-Fi выключен"] = "Wi-Fi is off",
        ["нет сопряжённых"] = "no paired devices",
        ["Скрыто"] = "Hidden",
        ["режим стримера"] = "streamer mode",
        ["Сейчас играет"] = "Now playing",
        ["текст не найден"] = "lyrics not found",
        ["Батарея"] = "Battery",
        ["Зарядка подключена"] = "Charger connected",
        ["Питание от батареи"] = "On battery",
        ["Низкий заряд"] = "Low battery",
        ["подключите зарядку"] = "plug in the charger",
        ["Громкость"] = "Volume",
        ["Звук"] = "Sound",
        ["Вывод"] = "Output",
        ["В избранном"] = "Added to favourites",
        ["Убрано из избранного"] = "Removed from favourites",
        ["Избранное"] = "Favourites",
        ["Список скопирован"] = "List copied",
        ["Скопировать"] = "Copy"
    };

    private static readonly Dictionary<string, string> Ru = BuildReverse();

    private static Dictionary<string, string> BuildReverse()
    {
        var map = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var kv in En)
        {
            if (!map.ContainsKey(kv.Value)) map[kv.Value] = kv.Key;
        }
        return map;
    }

    public static string T(string text)
    {
        if (string.IsNullOrEmpty(text)) return text;

        var map = IsEn ? En : Ru;
        return map.TryGetValue(text, out var value) ? value : text;
    }

    public static string T(string text, params object?[] args)
        => string.Format(T(text), args);
}

/// <summary>
/// Applies <see cref="L"/> to every static string in a visual tree. Dynamic
/// strings are translated at their call sites instead.
/// </summary>
public static class Localizer
{
    public static void Apply(DependencyObject? root)
    {
        if (root is null) return;
        Walk(root);
    }
    private static void Walk(DependencyObject d)
    {
        switch (d)
        {
            case TextBlock tb when !string.IsNullOrEmpty(tb.Text):
            {
                var t = L.T(tb.Text);
                if (t != tb.Text) tb.Text = t;
                break;
            }
            case ContentControl cc when cc.Content is string s && s.Length > 0:
            {
                var t = L.T(s);
                if (t != s) cc.Content = t;
                break;
            }
        }

        if (d is FrameworkElement fe && fe.ToolTip is string tip && tip.Length > 0)
        {
            var t = L.T(tip);
            if (t != tip) fe.ToolTip = t;
        }

        if (d is Window w && !string.IsNullOrEmpty(w.Title))
        {
            var t = L.T(w.Title);
            if (t != w.Title) w.Title = t;
        }

        // visual children first; before a window is shown only the logical tree exists
        if (d is Visual || d is System.Windows.Media.Media3D.Visual3D)
        {
            var count = VisualTreeHelper.GetChildrenCount(d);
            if (count > 0)
            {
                for (var i = 0; i < count; i++)
                    Walk(VisualTreeHelper.GetChild(d, i));
                return;
            }
        }

        foreach (var child in LogicalTreeHelper.GetChildren(d))
        {
            if (child is DependencyObject dep && !ReferenceEquals(dep, d))
                Walk(dep);
        }
    }
}
