using HarmonyLib;
using Il2CppMonomiPark.SlimeRancher.Weather;
using Il2CppMonomiPark.SlimeRancher.World;
using UnityEngine;

namespace SlimeRancher2AP.Patches.PlayerPatches;

/// <summary>
/// Weather slot data options: <c>WeatherFrequencyMultiplier</c> (divides the forecast
/// interval) and <c>ForceHeavyWeather</c>. The registry and pattern assets persist for the
/// process, so both cache the vanilla values and restore them on disconnect.
/// </summary>
internal static class WeatherPatch
{
    // ─────────────────────────────────────────────────────────────────────────
    // Weather Frequency Multiplier
    // ─────────────────────────────────────────────────────────────────────────

    // Vanilla intervals, so a reconnect never divides twice.
    private static float _origIntervalLow  = -1f;
    private static float _origIntervalHigh = -1f;

    private static bool _multiplierApplied = false;

    /// <summary>After connect (main thread); <see cref="TryApplyIfNeeded"/> retries until the registry loads.</summary>
    internal static void OnSlotDataReceived()
    {
        _multiplierApplied     = false; // reset so TryApplyIfNeeded will run
        _forceHeavyRemapDone   = false;
        TryApplyIfNeeded();
    }

    // Each attempt is a FindObjectsOfTypeAll; once a second is plenty for a one-time apply.
    private const  float RetrySeconds = 1f;
    private static float _nextRetry   = 0f;

    /// <summary>Per-frame; each applier no-ops once applied.</summary>
    internal static void TryApplyIfNeeded()
    {
        if (!Plugin.Instance.ModEnabled) return;
        if (_multiplierApplied && _forceHeavyRemapDone) return;

        var slotData = Plugin.Instance.ApClient?.SlotData;
        if (slotData == null) return;

        float now = Time.unscaledTime;
        if (now < _nextRetry) return;
        _nextRetry = now + RetrySeconds;

        TryApplyFrequencyMultiplier(slotData.WeatherFrequencyMultiplier);
        TryApplyForceHeavyRemap(slotData.ForceHeavyWeather);
    }

    private static void TryApplyFrequencyMultiplier(int multiplier)
    {
        if (_multiplierApplied) return;

        if (multiplier <= 1)
        {
            // Undo a previous session's scaling.
            RestoreOriginals();
            _multiplierApplied = true;
            return;
        }

        var registries = Resources.FindObjectsOfTypeAll<WeatherRegistry>();
        if (registries.Length == 0) return; // not ready yet

        bool appliedAny = false;
        for (int i = 0; i < registries.Length; i++)
        {
            var reg = registries[i];
            if (reg == null) continue;
            CacheOriginalsIfNeeded(reg);
            ApplyMultiplierToRegistry(reg, multiplier);
            appliedAny = true;
        }

        if (appliedAny)
        {
            _multiplierApplied = true;
            // Intervals only affect new forecast entries, and days are pre-scheduled.
            RescheduleForecasts();
        }
    }

    /// <summary>On disconnect (main thread): restores vanilla weather for the rest of the process.</summary>
    internal static void OnDisconnected()
    {
        RestoreOriginals();
        RestoreForceHeavyRemap();
        _multiplierApplied   = false;
        _forceHeavyRemapDone = false;
    }

    /// <summary>Writes the cached vanilla intervals back to every live registry. No-op if never scaled.</summary>
    private static void RestoreOriginals()
    {
        if (_origIntervalLow <= 0f) return; // never modified

        var registries = Resources.FindObjectsOfTypeAll<WeatherRegistry>();
        int restored = 0;
        for (int i = 0; i < registries.Length; i++)
        {
            var reg = registries[i];
            if (reg == null) continue;
            reg.ForecastHourIntervalLow  = _origIntervalLow;
            reg.ForecastHourIntervalHigh = _origIntervalHigh;
            restored++;
        }

        if (restored > 0)
        {
            Logger.Info(
                $"[AP-Weather] Restored vanilla forecast interval " +
                $"[{_origIntervalLow:F4}, {_origIntervalHigh:F4}] game-hrs on {restored} registry(ies)");
            RescheduleForecasts();
        }
    }

    /// <summary>
    /// Drops unstarted future forecasts and pulls the horizon back to now, so the game
    /// regenerates them with the current intervals. Started weather is left alone.
    /// </summary>
    private static void RescheduleForecasts()
    {
        double now;
        try
        {
            var timeDir = SceneContext.Instance?.TimeDirector;
            if (timeDir == null) return;
            now = timeDir.WorldTime();
        }
        catch { return; } // scene tearing down

        var registries = Resources.FindObjectsOfTypeAll<WeatherRegistry>();
        for (int r = 0; r < registries.Length; r++)
        {
            var reg = registries[r];
            if (reg == null) continue;

            int culled = 0;
            var zones = reg._zones;
            if (zones != null)
            {
                foreach (var kvp in zones)
                {
                    var forecast = kvp.Value?.Forecast;
                    if (forecast == null) continue;
                    for (int i = forecast.Count - 1; i >= 0; i--)
                    {
                        var entry = forecast[i];
                        if (entry == null) continue;
                        if (!entry.Started && entry.StartTime > now)
                        {
                            forecast.RemoveAt(i);
                            culled++;
                        }
                    }
                }
            }

            reg._forecastedTime = now;

            Logger.Info(
                $"[AP-Weather] Forecast reschedule: culled {culled} future entr(ies); " +
                $"horizon reset — schedule will regenerate with current intervals.");
        }
    }

    private static void CacheOriginalsIfNeeded(WeatherRegistry reg)
    {
        if (_origIntervalLow > 0f) return;
        _origIntervalLow  = reg.ForecastHourIntervalLow;
        _origIntervalHigh = reg.ForecastHourIntervalHigh;
    }

    private static void ApplyMultiplierToRegistry(WeatherRegistry reg, int multiplier)
    {
        if (multiplier <= 1) return;

        float factor = 1f / multiplier;
        reg.ForecastHourIntervalLow  = _origIntervalLow  * factor;
        reg.ForecastHourIntervalHigh = _origIntervalHigh * factor;

        Logger.Info(
            $"[AP-Weather] Frequency ×{multiplier}: forecast interval now " +
            $"[{reg.ForecastHourIntervalLow:F4}, {reg.ForecastHourIntervalHigh:F4}] game-hrs");
    }

    // Don't patch WeatherRegistry.Awake (CallerCount(0) — crashed the trampoline after the
    // 5/13/2026 update). The registry is a persistent singleton, so polling covers it.

    // ─────────────────────────────────────────────────────────────────────────
    // Force Heavy Weather
    //
    //  1. Remap (primary): each pattern's STARTING transitions into Light/Medium are pointed
    //     at its Heavy state, so weather begins Heavy.
    //  2. RunPatternState Postfix (safety net): swaps any Light/Medium state that still starts.
    // ─────────────────────────────────────────────────────────────────────────

    private static WeatherStateDefinition[]? _stateDefCache;   // assets persist; never cleared

    private static bool _forceHeavyRemapDone = false;

    // Original ToState per remapped Transition, for restore on disconnect.
    private static readonly System.Collections.Generic.List<(WeatherPatternDefinition.Transition transition, WeatherStateDefinition original)>
        _remapOriginals = new();

    private static void TryApplyForceHeavyRemap(bool forceHeavy)
    {
        if (_forceHeavyRemapDone) return;

        if (!forceHeavy)
        {
            // Undo a previous session's remap.
            RestoreForceHeavyRemap();
            _forceHeavyRemapDone = true;
            return;
        }

        // Via the zone configs, which cover every zone whether loaded or not.
        var registries = Resources.FindObjectsOfTypeAll<WeatherRegistry>();
        if (registries.Length == 0) return; // not ready yet

        var visitedPatterns = new System.Collections.Generic.HashSet<System.IntPtr>();
        int remapped = 0, patternsTouched = 0;

        for (int r = 0; r < registries.Length; r++)
        {
            var configs = registries[r]?.ZoneConfigList;
            if (configs == null) continue;

            for (int ci = 0; ci < configs.Count; ci++)
            {
                var patterns = configs[ci]?.Patterns;
                if (patterns == null) continue;

                for (int pi = 0; pi < patterns.Count; pi++)
                {
                    var pattern = patterns[pi];
                    if (pattern == null || !visitedPatterns.Add(pattern.Pointer)) continue;

                    int n = RemapPatternToHeavy(pattern);
                    if (n > 0) { remapped += n; patternsTouched++; }
                }
            }
        }

        if (visitedPatterns.Count == 0) return; // config lists empty — retry

        Logger.Info(
            $"[AP-Weather] ForceHeavy remap: {remapped} Light/Medium transition(s) redirected to Heavy " +
            $"across {patternsTouched} pattern(s) ({visitedPatterns.Count} pattern(s) scanned).");
        _forceHeavyRemapDone = true;
    }

    /// <summary>
    /// Points the pattern's Light/Medium STARTING transitions at its Heavy state; returns the
    /// count changed (0 for flat patterns). Never the RUNNING transitions: they hold the
    /// wind-down, and remapping it made Heavy loop for days.
    /// </summary>
    private static int RemapPatternToHeavy(WeatherPatternDefinition pattern)
    {
        var heavy = FindHeavyStateInPattern(pattern);
        if (heavy == null) return 0; // flat pattern

        int changed = 0;

        var startTrans = pattern.StartingTransitions;
        if (startTrans != null)
            for (int i = 0; i < startTrans.Count; i++)
                changed += RemapTransition(startTrans[i], heavy);

        return changed;
    }

    private static int RemapTransition(WeatherPatternDefinition.Transition? t, WeatherStateDefinition heavy)
    {
        var to = t?.ToState;
        if (t == null || to == null) return 0;
        if (to.MapTier != 1 && to.MapTier != 2) return 0;

        _remapOriginals.Add((t, to));
        t.ToState = heavy;
        return 1;
    }

    /// <summary>Finds the MapTier 3 state referenced anywhere in the pattern's transition graph.</summary>
    private static WeatherStateDefinition? FindHeavyStateInPattern(WeatherPatternDefinition pattern)
    {
        var startTrans = pattern.StartingTransitions;
        if (startTrans != null)
            for (int i = 0; i < startTrans.Count; i++)
            {
                var to = startTrans[i]?.ToState;
                if (to != null && to.MapTier == 3) return to;
            }

        var runTrans = pattern.RunningTransitions;
        if (runTrans == null) return null;
        for (int i = 0; i < runTrans.Count; i++)
        {
            var tl = runTrans[i];
            if (tl == null) continue;
            var from = tl.FromState;
            if (from != null && from.MapTier == 3) return from;
            var list = tl.Transitions;
            if (list == null) continue;
            for (int j = 0; j < list.Count; j++)
            {
                var to = list[j]?.ToState;
                if (to != null && to.MapTier == 3) return to;
            }
        }
        return null;
    }

    /// <summary>Restores every remapped transition's original ToState. No-op if never remapped.</summary>
    private static void RestoreForceHeavyRemap()
    {
        if (_remapOriginals.Count == 0) return;

        int restored = 0;
        for (int i = 0; i < _remapOriginals.Count; i++)
        {
            var (transition, original) = _remapOriginals[i];
            if (transition == null) continue;
            transition.ToState = original;
            restored++;
        }
        _remapOriginals.Clear();

        Logger.Info($"[AP-Weather] ForceHeavy remap restored on {restored} transition(s).");
    }

    [HarmonyPostfix]
    [HarmonyPatch(typeof(WeatherRegistry), nameof(WeatherRegistry.RunPatternState))]
    static void RunPatternStatePostfix(
        WeatherRegistry __instance,
        ZoneDefinition zone, IWeatherPattern pattern, IWeatherState state)
    {
        if (!Plugin.Instance.ModEnabled) return;

        var slotData = Plugin.Instance.ApClient?.SlotData;
        if (slotData == null || !slotData.ForceHeavyWeather) return;

        var stateDef = state?.TryCast<WeatherStateDefinition>();
        if (stateDef == null) return;

        int tier = stateDef.MapTier;
        // 0 = flat pattern; 3 = Heavy (also stops recursion from the call below).
        if (tier == 0 || tier >= 3) return;

        // "Wind Light" / "Wind Medium" → "Wind Heavy"
        var fromName = stateDef.StateName ?? stateDef.name ?? "";
        var toName   = fromName
            .Replace(" Light",  " Heavy")
            .Replace(" Medium", " Heavy");

        if (toName == fromName) return; // unexpected name

        Logger.Info($"[AP-Weather] ForceHeavy: intercepted tier={tier} '{fromName}' on zone='{zone?.name}' → looking for '{toName}'");

        _stateDefCache ??= Resources.FindObjectsOfTypeAll<WeatherStateDefinition>();

        WeatherStateDefinition? heavyDef = null;
        for (int i = 0; i < _stateDefCache.Length; i++)
        {
            var s = _stateDefCache[i];
            if (s == null) continue;
            var sName = s.StateName ?? s.name ?? "";
            if (sName == toName && s.MapTier == 3) { heavyDef = s; break; }
        }

        if (heavyDef == null)
        {
            Logger.Warning(
                $"[AP-Weather] ForceHeavy: no Heavy state found for '{toName}' (from '{fromName}')");
            return;
        }

        Logger.Info($"[AP-Weather] ForceHeavy: substituting '{fromName}' → '{heavyDef.StateName ?? heavyDef.name}'");

        // Stop first, or both run and Light/Medium dominates. immediate skips MinDurationHours.
        __instance.StopPatternState(zone, pattern, state);
        __instance.RunPatternState(zone, pattern, heavyDef.Cast<IWeatherState>(), true);
    }
}
