using HarmonyLib;
using Il2CppMonomiPark.SlimeRancher.Economy;
using UnityEngine;

namespace SlimeRancher2AP.Patches.EconomyPatches;

/// <summary>
/// Implements the <c>plort_market_mode</c> slot data option.
///
/// <para>
/// When enabled ("5_items" or "10_items"), the market starts fully saturated (minimum
/// prices for all plort types) and natural saturation recovery is zeroed so prices only
/// improve when the player receives Market Recovery items from the AP pool.
/// </para>
///
/// <para>
/// <b>Saturation is declarative, not event-driven:</b> the target saturation is always
/// computed as <c>FullSaturation × (1 − fraction × recoveryItemsReceived)</c>, where the
/// count comes from the session's <c>AllItemsReceived</c> snapshot. This makes the state
/// idempotent across reconnects — Market Recovery items are one-shot ephemerals that never
/// re-apply on replay, so an event-driven "subtract on receipt" design would permanently
/// wipe recovery progress the first time the player reconnected (the original stashed
/// implementation had exactly that bug).
/// </para>
/// </summary>
internal static class PlortMarketModePatch
{
    private static float  _origSaturationRecovery = -1f;
    // Daily price noise amplitudes, zeroed while the mode is active. ResetPrices computes
    // price = f(saturation) + noise(day seed) — with the amplitudes untouched, fully
    // saturated prices sat a random offset above the floor and re-rolled every midnight
    // even though saturation never moved. Nullable: cached the first time we modify them.
    private static float? _origMarketNoise;
    private static float? _origIndivNoise;
    private static bool   _appliedThisSession     = false;

    /// <summary>Called from the main-thread post-connect callback after SlotData is available.</summary>
    internal static void OnSlotDataReceived()
    {
        _appliedThisSession = false;
        TryApplyIfNeeded();
    }

    /// <summary>Fraction of FullSaturation each Market Recovery item restores, from the mode key.</summary>
    private static float RecoveryFraction(string mode) => mode switch
    {
        "5_items"  => 0.20f,
        "10_items" => 0.10f,
        _          => 0f,
    };

    /// <summary>
    /// Counts Market Recovery items in the session's received-items snapshot.
    /// Recomputed on demand so saturation state survives reconnects and replays.
    /// </summary>
    private static int CountRecoveryItemsReceived()
    {
        var items = Plugin.Instance.ApClient.Session?.Items?.AllItemsReceived;
        if (items == null) return 0;
        int n = 0;
        for (int i = 0; i < items.Count; i++)
        {
            var id = items[i].ItemId;
            if (id == Data.ItemTable.MarketRecovery20 || id == Data.ItemTable.MarketRecovery10) n++;
        }
        return n;
    }

    /// <summary>
    /// Called every Update frame. No-ops once applied for this session or if the option is
    /// disabled. Waits for the Player object so the world model's saved saturations have been
    /// restored — writing earlier would be overwritten by the save-load restore.
    /// </summary>
    internal static void TryApplyIfNeeded()
    {
        if (_appliedThisSession) return;
        if (!Plugin.Instance.ModEnabled) return;

        var slotData = Plugin.Instance.ApClient?.SlotData;
        if (slotData == null) return; // not connected yet — retry next frame

        var mode = slotData.PlortMarketMode;
        if (mode == "disabled")
        {
            // Mode off for THIS session — but a previous session in the same process may have
            // zeroed recovery/noise on the persistent settings asset. Put them back.
            RestoreMarketSettings();
            _appliedThisSession = true;
            return;
        }

        // Wait for the scene to be fully interactive (save restore complete).
        if (SceneContext.Instance?.Player == null) return;

        var director = SceneContext.Instance?.PlortEconomyDirector;
        if (director == null) return;

        var worldModel = director._worldModel;
        if (worldModel == null) return;

        var settings = director._settings;
        if (settings == null) return;

        // Cache originals so they can be restored on disconnect / mode change.
        if (_origSaturationRecovery < 0f)
            _origSaturationRecovery = settings.SaturationRecovery;
        _origMarketNoise ??= settings.MarketNoiseAmplitude;
        _origIndivNoise  ??= settings.IndivNoiseAmplitude;

        // Zero natural recovery — prices only improve via Market Recovery items.
        settings.SaturationRecovery = 0f;
        // Zero the daily price noise — prices become a pure deterministic function of
        // saturation: they start at the true floor and only move on Market Recovery items,
        // with no random offset at generation and no midnight re-roll drift.
        settings.MarketNoiseAmplitude = 0f;
        settings.IndivNoiseAmplitude  = 0f;

        if (!ApplySaturationFromItemCount(mode))
            return; // plort table unavailable — retry next frame

        _appliedThisSession = true;
        // Prices are otherwise only recomputed from saturation at the game's midnight tick —
        // on a fresh file the market would display unsaturated prices all of day one.
        ForcePriceRefresh("initial saturation");
    }

    /// <summary>
    /// Sets every plort's saturation to the declarative target derived from the number of
    /// Market Recovery items received. Returns false when the market objects aren't ready.
    /// </summary>
    private static bool ApplySaturationFromItemCount(string mode, bool quiet = false)
    {
        var director   = SceneContext.Instance?.PlortEconomyDirector;
        var worldModel = director?._worldModel;
        var plorts     = director?._settings?.PlortsTable.Plorts;
        if (director == null || worldModel == null || plorts == null) return false;

        int   received  = CountRecoveryItemsReceived();
        float remaining = Mathf.Clamp01(1f - RecoveryFraction(mode) * received);

        var satMap = worldModel.marketSaturation;
        int applied = 0;
        for (int i = 0; i < plorts.Count; i++)
        {
            var config = plorts[i];
            if (config?.Type == null) continue;
            satMap[config.Type] = config.FullSaturation * remaining;
            applied++;
        }

        if (!quiet)
            Logger.Info(
                $"[AP-Market] Saturation set to {remaining * 100f:F0}% of cap on {applied} plort(s) " +
                $"({received} Market Recovery item(s) received, mode={mode}).");
        return true;
    }

    /// <summary>
    /// Called when a Market Recovery item arrives through the item pipeline. Recomputes the
    /// declarative saturation target (the snapshot already includes the new item) and
    /// refreshes prices immediately — the player should see the improvement on receipt,
    /// not at the next in-game midnight.
    /// Returns false when the market isn't ready yet — caller requeues the item.
    /// </summary>
    internal static bool ApplyRecovery()
    {
        var mode = Plugin.Instance.ApClient?.SlotData?.PlortMarketMode ?? "disabled";
        if (mode == "disabled")
        {
            // Items exist but the mode is off (mismatched seed/mod state) — acknowledge only.
            Logger.Info("[AP-Market] Market Recovery received but plort_market_mode is disabled — no effect.");
            return true;
        }
        if (!ApplySaturationFromItemCount(mode)) return false;
        ForcePriceRefresh("market recovery item");
        return true;
    }

    /// <summary>
    /// Re-asserts the mod-governed saturation after a plort sale. While the mode is active
    /// the mod is the SOLE authority over market saturation — the native RegisterSold body
    /// has just increased the sold plort's saturation (selling normally depresses prices),
    /// so the declarative target is rewritten and prices refreshed. The player still gets
    /// paid at the pre-sale price; the sale simply no longer moves the market.
    /// </summary>
    internal static void ReassertSaturationAfterSale()
    {
        var mode = Plugin.Instance.ApClient?.SlotData?.PlortMarketMode ?? "disabled";
        if (mode == "disabled") return;
        if (!_appliedThisSession) return; // initial apply pending — it will set everything anyway

        if (ApplySaturationFromItemCount(mode, quiet: true))
            ForcePriceRefresh("sale override", quiet: true);
    }

    /// <summary>
    /// Forces the game to recompute displayed plort prices from the current saturation NOW,
    /// instead of waiting for the midnight tick. Uses the game's own
    /// <c>PlortEconomyDirector.ResetPrices</c> (price ← saturation + day-seeded noise), so
    /// the resulting floor/values are exactly what heavy selling would produce in vanilla.
    /// Called several times because the recompute smooths from the previous value
    /// (PrevValue/CurrValue) — with a fixed day seed the target is stable, so repeated calls
    /// converge and become no-ops once settled.
    /// </summary>
    private static void ForcePriceRefresh(string reason, bool quiet = false)
    {
        var director   = SceneContext.Instance?.PlortEconomyDirector;
        var worldModel = director?._worldModel;
        if (director == null || worldModel == null) return;

        int day = 0;
        try { day = SceneContext.Instance?.TimeDirector?._lastDay ?? 0; } catch { /* default seed */ }

        try
        {
            for (int i = 0; i < 6; i++)
                director.ResetPrices(worldModel, day);
            if (!quiet)
                Logger.Info($"[AP-Market] Prices recomputed immediately ({reason}, day={day}).");
        }
        catch (System.Exception ex)
        {
            Logger.Warning($"[AP-Market] Immediate price refresh failed ({reason}): {ex.Message}");
        }
    }

    /// <summary>
    /// Called on disconnect. Restores the vanilla saturation recovery rate and price noise
    /// amplitudes on the persistent settings ScriptableObject — located via Resources so this
    /// works even when disconnecting from the main menu (where
    /// SceneContext.PlortEconomyDirector is null; the original implementation skipped the
    /// restore there and then discarded the cached value, leaving the modified values in
    /// place for the rest of the process).
    /// </summary>
    internal static void OnDisconnected()
    {
        RestoreMarketSettings();
        _appliedThisSession = false;
    }

    private static void RestoreMarketSettings()
    {
        if (_origSaturationRecovery < 0f && _origMarketNoise == null && _origIndivNoise == null)
            return; // never modified

        var allSettings = Resources.FindObjectsOfTypeAll<PlortEconomySettings>();
        int restored = 0;
        for (int i = 0; i < allSettings.Length; i++)
        {
            var s = allSettings[i];
            if (s == null) continue;
            if (_origSaturationRecovery >= 0f)   s.SaturationRecovery    = _origSaturationRecovery;
            if (_origMarketNoise.HasValue)       s.MarketNoiseAmplitude  = _origMarketNoise.Value;
            if (_origIndivNoise.HasValue)        s.IndivNoiseAmplitude   = _origIndivNoise.Value;
            restored++;
        }

        if (restored > 0)
        {
            Logger.Info(
                $"[AP-Market] Vanilla market settings restored on {restored} settings asset(s) " +
                $"(recovery={_origSaturationRecovery:F4}, marketNoise={_origMarketNoise ?? -1f:F4}, " +
                $"indivNoise={_origIndivNoise ?? -1f:F4}).");
            _origSaturationRecovery = -1f;
            _origMarketNoise        = null;
            _origIndivNoise         = null;
        }
        // If nothing was found (asset unloaded?), keep the cached originals so a later call can retry.
    }

#if DEBUG
    /// <summary>
    /// Debug helper — saturates all plorts to their FullSaturation cap regardless of session
    /// state. Safe to call from the debug panel without an active AP session.
    /// </summary>
    internal static bool DebugForceSaturate()
    {
        var director   = SceneContext.Instance?.PlortEconomyDirector;
        var worldModel = director?._worldModel;
        var plorts     = director?._settings?.PlortsTable.Plorts;
        if (director == null || worldModel == null || plorts == null) return false;

        var satMap = worldModel.marketSaturation;
        for (int i = 0; i < plorts.Count; i++)
        {
            var config = plorts[i];
            if (config?.Type == null) continue;
            satMap[config.Type] = config.FullSaturation;
        }

        Logger.Info("[AP-Market] DEBUG: Market fully saturated (all plorts at max saturation).");
        ForcePriceRefresh("debug saturate");
        return true;
    }

    /// <summary>
    /// Debug helper — manually subtracts one recovery step (fraction × FullSaturation) from
    /// every plort's current saturation, clamped to 0. Event-driven on purpose: it exercises
    /// the price response without needing an AP session or received items.
    /// </summary>
    internal static bool DebugApplyRecoveryStep(float fraction)
    {
        var director   = SceneContext.Instance?.PlortEconomyDirector;
        var worldModel = director?._worldModel;
        var plorts     = director?._settings?.PlortsTable.Plorts;
        if (director == null || worldModel == null || plorts == null) return false;

        var satMap = worldModel.marketSaturation;
        for (int i = 0; i < plorts.Count; i++)
        {
            var config = plorts[i];
            if (config?.Type == null) continue;
            float curr = satMap.ContainsKey(config.Type) ? satMap[config.Type] : config.FullSaturation;
            satMap[config.Type] = Mathf.Max(0f, curr - fraction * config.FullSaturation);
        }

        Logger.Info($"[AP-Market] DEBUG: applied {fraction * 100f:F0}% recovery step to all plort prices.");
        ForcePriceRefresh("debug recovery step");
        return true;
    }
#endif
}

/// <summary>
/// While <c>plort_market_mode</c> is active the mod is the sole authority over market
/// saturation. The native <c>RegisterSold</c> body increases the sold plort's saturation
/// (selling normally depresses its price) — this Postfix rewrites the declarative saturation
/// target immediately afterwards so player sales never move the market. Coexists with the
/// first-sale location check Postfix in <c>LocationPatches.PlortMarketPatch</c>.
/// </summary>
[HarmonyPatch(typeof(PlortEconomyDirector), nameof(PlortEconomyDirector.RegisterSold))]
internal static class PlortMarketModeSaleOverridePatch
{
    private static void Postfix() => PlortMarketModePatch.ReassertSaturationAfterSale();
}
