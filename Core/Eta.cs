using System.Diagnostics;

namespace MediaSorter.Core;

/// <summary>
/// Estimates time remaining from the average rate since the work started.
/// "Units" can be files (scanning) or bytes (moving/copying), whatever tracks the real cost.
/// </summary>
public sealed class Eta
{
    // Don't guess until there's enough history; early rates swing wildly.
    public static readonly TimeSpan WarmUp = TimeSpan.FromSeconds(2);

    private readonly Func<TimeSpan> _clock;

    public Eta() : this(StopwatchClock()) { }
    public Eta(Func<TimeSpan> clock) => _clock = clock;

    public TimeSpan Elapsed => _clock();

    /// <summary>Remaining time, or null while there isn't enough data yet.</summary>
    public TimeSpan? Remaining(double doneUnits, double totalUnits)
    {
        var elapsed = _clock();
        if (doneUnits <= 0 || totalUnits <= 0 || elapsed < WarmUp) return null;
        if (doneUnits >= totalUnits) return TimeSpan.Zero;
        var rate = doneUnits / elapsed.TotalSeconds;
        return TimeSpan.FromSeconds((totalUnits - doneUnits) / rate);
    }

    /// <summary>Friendly wording: "about 3 min left", "about 1 hr 20 min left", "a few seconds left".</summary>
    public static string Format(TimeSpan? remaining)
    {
        if (remaining is not { } r) return "estimating time…";
        if (r.TotalSeconds < 10) return "a few seconds left";
        if (r.TotalSeconds < 60) return $"about {RoundTo(r.TotalSeconds, 5)} sec left";
        if (r.TotalMinutes < 60) return $"about {Math.Max(1, (int)Math.Round(r.TotalMinutes, MidpointRounding.AwayFromZero))} min left";
        var hours = (int)r.TotalHours;
        var mins = (int)Math.Round((r.TotalMinutes - hours * 60) / 5, MidpointRounding.AwayFromZero) * 5;
        if (mins == 60) { hours++; mins = 0; }
        return mins == 0 ? $"about {hours} hr left" : $"about {hours} hr {mins} min left";
    }

    /// <summary>Compact elapsed time for the finished message: "8s", "3m 12s", "1h 05m".</summary>
    public static string FormatElapsed(TimeSpan t) =>
        t.TotalHours >= 1 ? $"{(int)t.TotalHours}h {t.Minutes:00}m"
        : t.TotalMinutes >= 1 ? $"{(int)t.TotalMinutes}m {t.Seconds:00}s"
        : $"{Math.Max(0, (int)Math.Round(t.TotalSeconds, MidpointRounding.AwayFromZero))}s";

    private static int RoundTo(double value, int step) => Math.Max(step, (int)Math.Round(value / step, MidpointRounding.AwayFromZero) * step);

    private static Func<TimeSpan> StopwatchClock()
    {
        var sw = Stopwatch.StartNew();
        return () => sw.Elapsed;
    }
}
