using HarmonyLib;
using Il2CppMonomiPark.SlimeRancher.Economy;
using UnityEngine;

namespace SlimeRancher2AP.Patches.EconomyPatches;

/// <summary>
/// <c>plort_market_mode</c>: the market starts fully saturated, natural recovery is off, and
/// prices improve only with Market Recovery items.
/// </summary>
/// <remarks>
/// Declarative: saturation is always <c>FullSaturation × (1 − fraction × received)</c> from the
/// server snapshot. Recovery items are ephemeral and never replay, so subtracting on receipt
/// would lose progress on reconnect.
/// </remarks>
internal static class PlortMarketModePatch
{
    private static float  _origSaturationRecovery = -1f;
    // Daily price noise, zeroed while active so prices are a pure function of saturation.
    // Null until first modified.
    private static float? _origMarketNoise;
    private static float? _origIndivNoise;
    private static bool   _appliedThisSession     = false;

    /// <summary>After connect, main thread.</summary>
    internal static void OnSlotDataReceived()
    {
        _appliedThisSession = false;
        TryApplyIfNeeded();
    }

    /// <summary>Fraction of FullSaturation each recovery item restores.</summary>
    private static float RecoveryFraction(string mode) => mode switch
    {
        "5_items"  => 0.20f,
        "10_items" => 0.10f,
        _          => 0f,
    };

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

    /// <summary>Per-frame until applied. Waits for the Player, or the save restore overwrites it.</summary>
    internal static void TryApplyIfNeeded()
    {
        if (_appliedThisSession) return;
        if (!Plugin.Instance.ModEnabled) return;

        var slotData = Plugin.Instance.ApClient?.SlotData;
        if (slotData == null) return;

        var mode = slotData.PlortMarketMode;
        if (mode == "disabled")
        {
            // Undo a previous session's changes to the persistent settings.
            RestoreMarketSettings();
            _appliedThisSession = true;
            return;
        }

        if (SceneContext.Instance?.Player == null) return;

        var director = SceneContext.Instance?.PlortEconomyDirector;
        if (director == null) return;

        var worldModel = director._worldModel;
        if (worldModel == null) return;

        var settings = director._settings;
        if (settings == null) return;

        if (_origSaturationRecovery < 0f)
            _origSaturationRecovery = settings.SaturationRecovery;
        _origMarketNoise ??= settings.MarketNoiseAmplitude;
        _origIndivNoise  ??= settings.IndivNoiseAmplitude;

        settings.SaturationRecovery = 0f;
        settings.MarketNoiseAmplitude = 0f;
        settings.IndivNoiseAmplitude  = 0f;

        if (!ApplySaturationFromItemCount(mode))
            return; // retry

        _appliedThisSession = true;
        // Otherwise prices only update at midnight.
        ForcePriceRefresh("initial saturation");
    }

    /// <summary>Sets every plort to the target saturation; false if the market isn't ready.</summary>
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
    /// On a recovery item: re-applies the target and refreshes prices now. False = requeue.
    /// </summary>
    internal static bool ApplyRecovery()
    {
        var mode = Plugin.Instance.ApClient?.SlotData?.PlortMarketMode ?? "disabled";
        if (mode == "disabled")
        {
            Logger.Info("[AP-Market] Market Recovery received but plort_market_mode is disabled — no effect.");
            return true;
        }
        if (!ApplySaturationFromItemCount(mode)) return false;
        ForcePriceRefresh("market recovery item");
        return true;
    }

    /// <summary>After a sale, undoes the saturation it added — sales never move the market here.</summary>
    internal static void ReassertSaturationAfterSale()
    {
        var mode = Plugin.Instance.ApClient?.SlotData?.PlortMarketMode ?? "disabled";
        if (mode == "disabled") return;
        if (!_appliedThisSession) return; // the initial apply will cover it

        if (ApplySaturationFromItemCount(mode, quiet: true))
            ForcePriceRefresh("sale override", quiet: true);
    }

    /// <summary>
    /// Recomputes prices now via the game's own <c>ResetPrices</c>. Repeated because each call
    /// only smooths toward the target; extra calls converge to no-ops.
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
    /// On disconnect: restores the persistent settings, found via Resources so it works from the
    /// main menu too.
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
        // Nothing found: keep the originals for a later retry.
    }

#if DEBUG
    /// <summary>Debug: fully saturates the market, no session needed.</summary>
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

    /// <summary>Debug: subtracts one recovery step from every plort, no session needed.</summary>
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

/// <summary>See <see cref="PlortMarketModePatch.ReassertSaturationAfterSale"/>.</summary>
[HarmonyPatch(typeof(PlortEconomyDirector), nameof(PlortEconomyDirector.RegisterSold))]
internal static class PlortMarketModeSaleOverridePatch
{
    private static void Postfix() => PlortMarketModePatch.ReassertSaturationAfterSale();
}
