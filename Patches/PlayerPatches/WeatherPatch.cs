using HarmonyLib;
using Il2CppMonomiPark.SlimeRancher.Weather;
using Il2CppMonomiPark.SlimeRancher.World;
using UnityEngine;

namespace SlimeRancher2AP.Patches.PlayerPatches;

/// <summary>
/// Applies weather-related slot data options:
/// <list type="bullet">
///   <item><term>WeatherFrequencyMultiplier</term>
///     <description>
///       Divides <c>WeatherRegistry.ForecastHourIntervalLow/High</c> by the multiplier,
///       compressing the gap between scheduled weather events.
///       At 2× the vanilla [0.25, 1.0] hr gap becomes [0.125, 0.5] hr; at 4× it becomes
///       [0.0625, 0.25] hr.
///       <para>
///       <b>Timing note:</b> <c>WeatherRegistry</c> lives on the persistent
///       <c>SceneContext</c> singleton — its <c>Awake()</c> fires once at game start,
///       before any AP connection. The <c>Awake</c> Postfix handles the rare case where
///       a new registry instance initialises after connection (e.g. a scene reload that
///       recreates the object). The normal case — where Awake fired while SlotData was
///       still null — is handled by <see cref="OnSlotDataReceived"/>, which is called
///       from the main-thread post-connect callback and applies the multiplier to any
///       already-awake registry.
///       </para>
///       <para>
///       Original interval values are cached the first time a registry is seen so that
///       repeated calls (e.g. disconnect + reconnect with a different multiplier) always
///       compute from the unmodified vanilla baseline.
///       </para>
///     </description>
///   </item>
///   <item><term>ForceHeavyWeather</term>
///     <description>
///       Postfix on <c>WeatherRegistry.RunPatternState</c>. When the newly started state has
///       MapTier 1 (Light) or 2 (Medium), calls <c>StopPatternState</c> for that state and
///       then <c>RunPatternState</c> for the corresponding Heavy state (<c>immediate=true</c>),
///       so the player never has to wait out the Light/Medium phase. Running both simultaneously
///       without stopping first caused the Light/Medium state to dominate.
///       Flat single-state patterns (Slime Rain, Snow — MapTier 0) are unaffected.
///       The second RunPatternState re-enters this Postfix, but MapTier 3 returns immediately.
///     </description>
///   </item>
/// </list>
/// </summary>
internal static class WeatherPatch
{
    // ─────────────────────────────────────────────────────────────────────────
    // Weather Frequency Multiplier
    // ─────────────────────────────────────────────────────────────────────────

    // Vanilla interval values, captured the first time we see a WeatherRegistry.
    // Always computing from these originals means disconnect-reconnect with a different
    // multiplier produces a correct result rather than double-dividing.
    private static float _origIntervalLow  = -1f;
    private static float _origIntervalHigh = -1f;

    // Set to true once we successfully apply the multiplier to a live registry,
    // or when multiplier is 1 (nothing to do). Reset on disconnect so a reconnect
    // with a different multiplier re-applies from the cached originals.
    private static bool _multiplierApplied = false;

    /// <summary>
    /// Called from the main-thread post-connect callback after SlotData is available.
    /// Attempts to find and modify the live <c>WeatherRegistry</c>.  If the registry
    /// is not yet findable (scene still loading), <see cref="_multiplierApplied"/> stays
    /// false and <see cref="TryApplyIfNeeded"/> will retry every Update frame until it
    /// succeeds.
    /// </summary>
    internal static void OnSlotDataReceived()
    {
        _multiplierApplied     = false; // reset so TryApplyIfNeeded will run
        _forceHeavyRemapDone   = false;
        TryApplyIfNeeded();
    }

    /// <summary>
    /// Called every Update frame from <c>ApUpdateBehaviour</c>.
    /// Runs the two independent weather appliers; each no-ops once applied.
    /// </summary>
    internal static void TryApplyIfNeeded()
    {
        if (!Plugin.Instance.ModEnabled) return;

        var slotData = Plugin.Instance.ApClient?.SlotData;
        if (slotData == null) return;

        TryApplyFrequencyMultiplier(slotData.WeatherFrequencyMultiplier);
        TryApplyForceHeavyRemap(slotData.ForceHeavyWeather);
    }

    private static void TryApplyFrequencyMultiplier(int multiplier)
    {
        if (_multiplierApplied) return;

        if (multiplier <= 1)
        {
            // Multiplier 1 for THIS session — but a previous session in the same process may
            // have divided the intervals (the registry is a persistent singleton). Put the
            // vanilla values back rather than leaving the old session's scaling in place.
            RestoreOriginals();
            _multiplierApplied = true;
            return;
        }

        var registries = Resources.FindObjectsOfTypeAll<WeatherRegistry>();
        if (registries.Length == 0) return; // not ready yet — retry next frame

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
            // The intervals only influence NEWLY generated forecast entries, and the game
            // pre-schedules weather up to DaysToForecast in-game days ahead (persisted in the
            // save). Without a reschedule the player would keep the old vanilla spacing for
            // days of play time before the multiplier had any visible effect.
            RescheduleForecasts();
        }
    }

    /// <summary>
    /// Called on disconnect. Restores the vanilla forecast intervals — the WeatherRegistry is
    /// a persistent singleton, so without this the divided values would carry into vanilla
    /// play or the next AP slot for the rest of the process. Also resets the applied flag so
    /// a reconnect with a different multiplier re-applies from the cached originals.
    /// Must be called on the main thread (Disconnect is only invoked from main-thread paths).
    /// </summary>
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
        if (_origIntervalLow <= 0f) return; // originals never cached → nothing was ever modified

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
            // Symmetric to the apply path: drop the densely-packed future schedule so vanilla
            // play doesn't inherit days of multiplied weather from the previous session.
            RescheduleForecasts();
        }
    }

    /// <summary>
    /// Drops every not-yet-started future forecast entry and resets the registry's forecast
    /// horizon to the current world time. The game's own <c>CheckForForecast</c> (driven from
    /// the registry's Update) then regenerates the schedule using the CURRENT
    /// <c>ForecastHourIntervalLow/High</c> values. Currently running or already-started
    /// entries are left alone so in-progress weather is not cut short.
    /// No-op when the scene/time director isn't available (e.g. disconnect from the main
    /// menu) — in that case the next world load generates fresh forecasts anyway.
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
        catch { return; } // scene tearing down — nothing to reschedule

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

            // Pull the horizon back to "now" so the game's next forecast check refills the
            // whole DaysToForecast window with the current interval values.
            reg._forecastedTime = now;

            Logger.Info(
                $"[AP-Weather] Forecast reschedule: culled {culled} future entr(ies); " +
                $"horizon reset — schedule will regenerate with current intervals.");
        }
    }

    private static void CacheOriginalsIfNeeded(WeatherRegistry reg)
    {
        if (_origIntervalLow > 0f) return; // already cached
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

    // NOTE (2026-05-16, v0.4.4): WeatherRegistry.Awake Postfix was removed.
    // Awake() is CallerCount(0) — Unity calls it natively. Its prologue changed in the
    // 5/13/2026 game update, causing a HarmonyX trampoline crash on scene load, identical
    // to the pattern seen with DirectedActorSpawner.Start, RadiantSlimeDirector.Start, etc.
    // TryApplyIfNeeded() polls every Update frame and already covers the same use case
    // (applies the multiplier the first frame a live WeatherRegistry is findable after connect).
    // The WeatherRegistry is a persistent SceneContext singleton that is not recreated on scene
    // reloads, so the "new registry after connection" edge case does not actually occur.

    // ─────────────────────────────────────────────────────────────────────────
    // Force Heavy Weather
    //
    // Two complementary mechanisms:
    //
    //  1. DATA REMAP (primary, TryApplyForceHeavyRemap): every Light/Medium (MapTier 1/2)
    //     ToState in every pattern's transition graph is redirected to that pattern's Heavy
    //     (MapTier 3) state. This covers ALL code paths — pattern starts, mid-pattern
    //     escalations chosen by ChooseTransition, forecast-driven starts — because the graph
    //     simply no longer contains Light/Medium destinations. Weather is Heavy from its
    //     first frame; the Heavy state's own wind-down/end transitions are untouched, so
    //     event duration and ending stay natural.
    //
    //  2. RunPatternState Postfix (safety net, below): catches any state started through the
    //     registry that the remap didn't cover (e.g. a state referenced outside the pattern
    //     graphs). With the remap active this is normally a no-op.
    //
    // The remap edits WeatherPatternDefinition ScriptableObject assets, which persist for
    // the process lifetime — originals are cached and restored on disconnect, like the
    // frequency multiplier above.
    // ─────────────────────────────────────────────────────────────────────────

    // WeatherStateDefinition assets are ScriptableObjects that persist for the
    // lifetime of the session. Cached on first use — never needs clearing.
    private static WeatherStateDefinition[]? _stateDefCache;

    // Set once the transition graphs have been remapped (or confirmed unnecessary) this session.
    private static bool _forceHeavyRemapDone = false;

    // Original ToState per remapped Transition, for restore on disconnect.
    private static readonly System.Collections.Generic.List<(WeatherPatternDefinition.Transition transition, WeatherStateDefinition original)>
        _remapOriginals = new();

    private static void TryApplyForceHeavyRemap(bool forceHeavy)
    {
        if (_forceHeavyRemapDone) return;

        if (!forceHeavy)
        {
            // Option off THIS session — undo any remap left over from a previous session
            // in the same process (pattern definitions are persistent assets).
            RestoreForceHeavyRemap();
            _forceHeavyRemapDone = true;
            return;
        }

        // Enumerate patterns through the registry's zone config list — this covers every
        // zone's patterns regardless of which scenes are loaded, unlike a Resources scan.
        var registries = Resources.FindObjectsOfTypeAll<WeatherRegistry>();
        if (registries.Length == 0) return; // not ready yet — retry next frame

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

        if (visitedPatterns.Count == 0) return; // config lists empty — retry next frame

        Logger.Info(
            $"[AP-Weather] ForceHeavy remap: {remapped} Light/Medium transition(s) redirected to Heavy " +
            $"across {patternsTouched} pattern(s) ({visitedPatterns.Count} pattern(s) scanned).");
        _forceHeavyRemapDone = true;
    }

    /// <summary>
    /// Redirects every MapTier 1/2 ToState in <paramref name="pattern"/>'s STARTING
    /// transitions to the pattern's MapTier 3 (Heavy) state, so weather begins at Heavy on
    /// its first frame. Returns the number of transitions changed; 0 when the pattern is
    /// flat (Slime Rain, Snow — no Heavy tier).
    ///
    /// <para>
    /// RUNNING transitions are deliberately NOT remapped. They contain both the escalation
    /// chain (Light→Medium→Heavy — unreachable once the entry is Heavy, so remapping is
    /// pointless) and the WIND-DOWN chain (Heavy→Medium→Light→end). Remapping the wind-down
    /// turned Heavy into a self-loop with no path to the pattern's end state, making weather
    /// events last for multiple in-game days (player-reported). Leaving them vanilla means
    /// the event holds Heavy for its normal severe phase, then decays and ends on vanilla
    /// pacing — the brief Medium/Light tail during wind-down is the trade for correct
    /// event durations.
    /// </para>
    /// </summary>
    private static int RemapPatternToHeavy(WeatherPatternDefinition pattern)
    {
        var heavy = FindHeavyStateInPattern(pattern);
        if (heavy == null) return 0; // flat pattern — nothing to force

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
        if (to.MapTier != 1 && to.MapTier != 2) return 0; // only Light/Medium get redirected

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

        // Cast to concrete type to read MapTier
        var stateDef = state?.TryCast<WeatherStateDefinition>();
        if (stateDef == null) return;

        int tier = stateDef.MapTier;
        // MapTier 0 = flat patterns (Slime Rain, Snow) — already maximum, skip.
        // MapTier 3 = Heavy — already correct, skip. Also prevents infinite recursion
        // when this Postfix fires for the replacement RunPatternState call below.
        if (tier == 0 || tier >= 3) return;

        // Derive the Heavy state name by replacing the tier word in StateName.
        // SR2 state names follow the convention "Wind Light", "Wind Medium", "Wind Heavy".
        var fromName = stateDef.StateName ?? stateDef.name ?? "";
        var toName   = fromName
            .Replace(" Light",  " Heavy")
            .Replace(" Medium", " Heavy");

        if (toName == fromName) return; // no substitution matched — unexpected state name

        Logger.Info($"[AP-Weather] ForceHeavy: intercepted tier={tier} '{fromName}' on zone='{zone?.name}' → looking for '{toName}'");

        // Lazy-init the state cache
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

        // Stop the Light/Medium state that just started, then start Heavy immediately.
        // Without the Stop call, both states run simultaneously and Light/Medium can dominate.
        // immediate=true bypasses MinDurationHours so Heavy takes effect this frame.
        // The second RunPatternState call re-enters this Postfix, but heavyDef.MapTier == 3 returns immediately.
        __instance.StopPatternState(zone, pattern, state);
        __instance.RunPatternState(zone, pattern, heavyDef.Cast<IWeatherState>(), true);
    }
}
