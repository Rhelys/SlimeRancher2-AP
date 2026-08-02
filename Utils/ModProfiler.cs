using System.Collections.Generic;
using System.Diagnostics;

namespace SlimeRancher2AP.Utils;

/// <summary>
/// Measures how much main-thread time the mod itself costs, per named section, so frame-rate
/// regressions can be attributed instead of guessed at.
///
/// <para>
/// Motivated by a report of ~20 fps lost against a 144 fps baseline — about 1.1 ms per frame.
/// At that scale the culprit is invisible by inspection: it is not one obviously slow call but
/// whichever poll happens to run <c>Resources.FindObjectsOfTypeAll</c> most often, and frame-
/// counted intervals make that rate scale with the frame rate itself.
/// </para>
///
/// <para>
/// Costs one <see cref="Stopwatch"/> timestamp pair per section per frame. Report from the debug
/// panel (F9 → Misc → "Log Mod Frame Cost"); totals are cumulative until reset.
/// </para>
/// </summary>
public static class ModProfiler
{
    private sealed class Entry
    {
        public long  Ticks;
        public long  Calls;
        public long  WorstTicks;
    }

    private static readonly Dictionary<string, Entry> _entries = new();
    private static readonly Stopwatch _sw = Stopwatch.StartNew();
    private static long _frames;

    /// <summary>
    /// Set false to remove all measurement overhead. Defaults off; Debug builds and the
    /// <c>Diagnostics/PerfLogSeconds</c> config entry turn it on.
    /// </summary>
    public static bool Enabled = false;

    /// <summary>
    /// When &gt; 0, <see cref="BeginFrame"/> writes a report on this interval and resets the
    /// counters, so each report covers one window rather than all time since launch. This is
    /// the only way to get a report out of a Release build (no debug panel).
    /// </summary>
    public static float LogIntervalSeconds = 0f;

    private static float _nextAutoLog = 0f;

    /// <summary>Call once per frame, before any <see cref="Time"/> sections.</summary>
    public static void BeginFrame()
    {
        if (!Enabled) return;
        _frames++;

        if (LogIntervalSeconds <= 0f) return;
        float now = UnityEngine.Time.unscaledTime;
        if (_nextAutoLog == 0f) { _nextAutoLog = now + LogIntervalSeconds; return; }
        if (now < _nextAutoLog) return;
        _nextAutoLog = now + LogIntervalSeconds;
        Report();
        Reset();
    }

    /// <summary>
    /// Times <paramref name="body"/> under <paramref name="name"/>. The delegate is invoked
    /// directly (no allocation per call when a cached static lambda is passed).
    /// </summary>
    public static void Time(string name, System.Action body)
    {
        if (!Enabled) { body(); return; }

        long start = _sw.ElapsedTicks;
        try { body(); }
        finally
        {
            long elapsed = _sw.ElapsedTicks - start;
            if (!_entries.TryGetValue(name, out var e))
                _entries[name] = e = new Entry();
            e.Ticks += elapsed;
            e.Calls++;
            if (elapsed > e.WorstTicks) e.WorstTicks = elapsed;
        }
    }

    /// <summary>Clears all accumulated measurements.</summary>
    public static void Reset()
    {
        _entries.Clear();
        _frames = 0;
    }

    /// <summary>
    /// Writes a per-section breakdown to the BepInEx log, sorted by total cost.
    /// "avg/frame" is the number that matters: sum it and compare against the frame budget
    /// (6.9 ms at 144 fps, 16.7 ms at 60 fps).
    /// </summary>
    public static void Report()
    {
        if (_frames == 0)
        {
            Logger.Info("[AP-Perf] No frames measured yet.");
            return;
        }

        double ticksToMs = 1000.0 / Stopwatch.Frequency;

        Logger.Info($"[AP-Perf] ===== MOD FRAME COST over {_frames} frames =====");

        var rows = new List<KeyValuePair<string, Entry>>(_entries);
        rows.Sort((a, b) => b.Value.Ticks.CompareTo(a.Value.Ticks));

        double totalMs = 0;
        foreach (var kv in rows)
        {
            double ms         = kv.Value.Ticks * ticksToMs;
            double perFrame   = ms / _frames;
            double worstMs    = kv.Value.WorstTicks * ticksToMs;
            totalMs += ms;
            Logger.Info(
                $"[AP-Perf] {kv.Key,-28} avg/frame {perFrame,7:F3} ms   " +
                $"worst {worstMs,7:F3} ms   calls {kv.Value.Calls}");
        }

        Logger.Info(
            $"[AP-Perf] TOTAL avg/frame {totalMs / _frames:F3} ms " +
            $"(budget: 6.94 ms @144fps, 16.67 ms @60fps)");
        Logger.Info("[AP-Perf] ===== END MOD FRAME COST =====");
    }
}
