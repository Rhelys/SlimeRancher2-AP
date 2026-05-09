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
///       MapTier 1 (Light) or 2 (Medium), immediately re-calls <c>RunPatternState</c> with
///       the corresponding Heavy state (<c>immediate=true</c>) so the player never has to
///       wait out the 3-hour Light/Medium phase. Flat single-state patterns (Slime Rain,
///       Snow — MapTier 0) are unaffected.
///       The second call enters this Postfix again, but MapTier 3 returns immediately,
///       preventing infinite recursion.
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
        _multiplierApplied = false; // reset so TryApplyIfNeeded will run
        TryApplyIfNeeded();
    }

    /// <summary>
    /// Called every Update frame from <c>ApUpdateBehaviour</c>.
    /// No-ops immediately once the multiplier has been applied or is not needed.
    /// </summary>
    internal static void TryApplyIfNeeded()
    {
        if (_multiplierApplied) return;
        if (!Plugin.Instance.ModEnabled) return;

        var slotData = Plugin.Instance.ApClient?.SlotData;
        if (slotData == null) return;

        int multiplier = slotData.WeatherFrequencyMultiplier;
        if (multiplier <= 1) { _multiplierApplied = true; return; } // nothing to do

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

        if (appliedAny) _multiplierApplied = true;
    }

    /// <summary>Called on disconnect so a reconnect with a different multiplier re-applies.</summary>
    internal static void OnDisconnected() => _multiplierApplied = false;

    [HarmonyPostfix]
    [HarmonyPatch(typeof(WeatherRegistry), nameof(WeatherRegistry.Awake))]
    static void RegistryAwakePostfix(WeatherRegistry __instance)
    {
        if (!Plugin.Instance.ModEnabled) return;

        // Always refresh originals from a freshly-awoken instance.
        _origIntervalLow  = __instance.ForecastHourIntervalLow;
        _origIntervalHigh = __instance.ForecastHourIntervalHigh;

        var slotData = Plugin.Instance.ApClient?.SlotData;
        if (slotData == null) return; // normal on first game start — OnSlotDataReceived handles it

        ApplyMultiplierToRegistry(__instance, slotData.WeatherFrequencyMultiplier);
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

    // ─────────────────────────────────────────────────────────────────────────
    // Force Heavy Weather
    // ─────────────────────────────────────────────────────────────────────────

    // WeatherStateDefinition assets are ScriptableObjects that persist for the
    // lifetime of the session. Cached on first use — never needs clearing.
    private static WeatherStateDefinition[]? _stateDefCache;

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

        // Jump the running pattern to Heavy immediately.
        // immediate=true bypasses MinDurationHours so the state takes effect this frame.
        // This triggers our Postfix again, but heavyDef.MapTier == 3 causes early return.
        __instance.RunPatternState(zone, pattern, heavyDef.Cast<IWeatherState>(), true);
    }
}
