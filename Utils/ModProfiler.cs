using System.Collections.Generic;
using System.Diagnostics;

namespace SlimeRancher2AP.Utils;

/// <summary>
/// The mod's main-thread cost per named section, so a frame-rate regression can be attributed.
/// One Stopwatch pair per section per frame. Report via F9 → "Log Mod Frame Cost".
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

    /// <summary>Off by default; on in Debug builds or via <c>Diagnostics/PerfLogSeconds</c>.</summary>
    public static bool Enabled = false;

    /// <summary>When &gt; 0, report and reset on this interval (how Release builds report).</summary>
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

    /// <summary>Times <paramref name="body"/>; pass a cached delegate to avoid allocating.</summary>
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

    public static void Reset()
    {
        _entries.Clear();
        _frames = 0;
    }

    /// <summary>
    /// Logs sections by total cost. Compare summed "avg/frame" with the frame budget (6.9 ms at
    /// 144 fps).
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
