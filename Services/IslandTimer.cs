using System;

namespace Salli.Services;

public enum IslandTimerMode { Stopwatch, Countdown, Pomodoro }

/// <summary>
/// Stopwatch / countdown / pomodoro state machine. Runs entirely on the UI
/// thread (ticks are passed in), so it stays trivially bindable and testable.
/// </summary>
public sealed class IslandTimer
{
    public IslandTimerMode Mode { get; private set; } = IslandTimerMode.Countdown;
    public TimeSpan Duration { get; private set; } = TimeSpan.FromMinutes(25);
    public TimeSpan Elapsed  { get; private set; }
    public bool Running      { get; private set; }

    public bool IsStopwatch => Mode == IslandTimerMode.Stopwatch;
    public bool IsPomodoro  => Mode == IslandTimerMode.Pomodoro;

    /// <summary>Work (or countdown) remainder.</summary>
    public TimeSpan Remaining => IsStopwatch
        ? TimeSpan.Zero
        : Duration - Elapsed;

    public double Fraction => IsStopwatch
        ? 0
        : Duration.TotalSeconds <= 0 ? 0 : Math.Clamp(Elapsed / Duration, 0, 1);

    /// <summary>"работа" / "перерыв" for pomodoro, otherwise empty.</summary>
    public string Phase { get; private set; } = "";
    public int Cycle { get; private set; } = 1;

    public bool Finished => Running && !IsStopwatch && Elapsed >= Duration;

    public void SetMode(IslandTimerMode mode, TimeSpan? duration = null)
    {
        Mode     = mode;
        Running  = false;
        Elapsed  = TimeSpan.Zero;
        Phase    = mode == IslandTimerMode.Pomodoro ? "work" : "";
        Cycle    = 1;
        Duration = mode switch
        {
            IslandTimerMode.Stopwatch => TimeSpan.Zero,
            IslandTimerMode.Pomodoro  => TimeSpan.FromMinutes(25),
            _                         => duration ?? Duration
        };
    }

    public void SetDuration(TimeSpan duration)
    {
        if (IsStopwatch) return;
        Duration = duration < TimeSpan.FromSeconds(1) ? TimeSpan.FromSeconds(1) : duration;
        Elapsed  = TimeSpan.Zero;
        Running  = false;
    }

    public void SetCountdown(TimeSpan duration)
    {
        Mode     = IslandTimerMode.Countdown;
        Duration = duration < TimeSpan.FromSeconds(1) ? TimeSpan.FromSeconds(1) : duration;
        Elapsed  = TimeSpan.Zero;
        Running  = false;
        Phase    = "";
        Cycle    = 1;
    }

    public void Add(TimeSpan delta)
    {
        Duration += delta;
        if (Duration < TimeSpan.FromMinutes(1)) Duration = TimeSpan.FromMinutes(1);
        if (Elapsed > Duration) Elapsed = Duration;
    }

    public void Toggle()
    {
        if (IsStopwatch)
        {
            Running = !Running;
            return;
        }

        if (Running) { Running = false; return; }

        // restart a finished countdown instead of sitting at zero
        if (Elapsed >= Duration) Elapsed = TimeSpan.Zero;
        Running = true;
    }

    public void Reset()
    {
        Running = false;
        Elapsed = TimeSpan.Zero;
        if (IsPomodoro) { Phase = "work"; Cycle = 1; Duration = TimeSpan.FromMinutes(25); }
    }

    /// <summary>Advances the clock. True when a countdown just hit zero.</summary>
    public bool Tick(TimeSpan delta)
    {
        if (!Running || delta <= TimeSpan.Zero) return false;

        Elapsed += delta;
        if (IsStopwatch || Elapsed < Duration) return false;

        if (!IsPomodoro)
        {
            Elapsed = Duration;
            Running = false;
            return true;
        }

        // pomodoro: work 25 → break 5, long break 15 after four cycles
        var wasWork = Phase != "break";
        if (wasWork)
        {
            var longBreak = Cycle % 4 == 0;
            Phase    = "break";
            Duration = TimeSpan.FromMinutes(longBreak ? 15 : 5);
        }
        else
        {
            Cycle++;
            Phase    = "work";
            Duration = TimeSpan.FromMinutes(25);
        }

        Elapsed = TimeSpan.Zero;
        return true;
    }

    public static string Format(TimeSpan t)
    {
        if (t < TimeSpan.Zero) t = TimeSpan.Zero;
        return t.TotalHours >= 1
            ? $"{(int)t.TotalHours}:{t.Minutes:00}:{t.Seconds:00}"
            : $"{t.Minutes:00}:{t.Seconds:00}";
    }

    /// <summary>Value shown in the island: elapsed for a stopwatch, remaining otherwise.</summary>
    public TimeSpan Display => IsStopwatch ? Elapsed : Remaining;
}
