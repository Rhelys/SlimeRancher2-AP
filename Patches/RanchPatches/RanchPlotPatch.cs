using HarmonyLib;
using Il2CppMonomiPark.SlimeRancher.Economy;
using Il2CppMonomiPark.SlimeRancher.UI;
using Il2CppMonomiPark.SlimeRancher.UI.Plot;
using SlimeRancher2AP.Archipelago;
using SlimeRancher2AP.UI;
using UnityEngine;

namespace SlimeRancher2AP.Patches.RanchPatches;

// ─────────────────────────────────────────────────────────────────────────────
// Ranch plot randomization enforcement (see RanchPlotHandler for the item model).
//
// Three patches, all on methods with managed callers (safe post-5/13/2026):
//   • UIActivator.OnInteract (CC1)        — tier 1 front door: blocks opening the build
//     menu of an EMPTY plot when the area has no free plot unlocks. Built plots always
//     open normally (upgrade/demolish stay accessible).
//   • LandPlotLocation.Replace (CC1)      — belt-and-braces for tiers 1+2: the single
//     managed funnel where an empty plot becomes a building. Should never fire if the
//     UI gates hold; blocks with a HUD message if something slips through.
//   • LandPlotUIRoot.Upgrade (CC1)        — tier 3 enforcement: resolves the upgrade
//     enum + the plot's building type to the asset-keyed AP item and blocks the
//     purchase when locked. The menu entry is already greyed out by the availability
//     wrap in RanchPlotHandler.Tick; this stops a purchase racing the ~10-frame
//     re-wrap window.
// ─────────────────────────────────────────────────────────────────────────────

[HarmonyPatch(typeof(UIActivator), nameof(UIActivator.OnInteract))]
internal static class RanchPlotInteractPatch
{
    private static bool Prefix(UIActivator __instance)
    {
        if (!RanchPlotHandler.PlotsEnabled) return true;

        var activator = __instance.TryCast<LandPlotUIActivator>();
        if (activator == null) return true;

        LandPlot? plot = null;
        try { plot = activator.landPlot; } catch { }
        if (plot == null) return true;
        if (plot.TypeId != LandPlot.Id.EMPTY) return true;

        if (RanchPlotHandler.CanBuildOnPlot(plot, out var blockMessage)) return true;

        Logger.Info($"[AP] Plot interaction blocked — {blockMessage}");
        StatusHUD.Instance?.ShowNotification(blockMessage);
        return false; // do not open the plot purchase UI
    }
}

[HarmonyPatch(typeof(LandPlotLocation), nameof(LandPlotLocation.Replace))]
internal static class RanchPlotReplacePatch
{
    private static bool Prefix(LandPlotLocation __instance, LandPlot oldLandPlot,
                               GameObject replacementPrefab, ref GameObject? __result)
    {
        LandPlot.Id newType = LandPlot.Id.NONE;
        try { newType = replacementPrefab?.GetComponent<LandPlot>()?.TypeId ?? LandPlot.Id.NONE; }
        catch { }

        // Tier 2 — building type must be unlocked.
        if (RanchPlotHandler.BuildingsEnabled)
        {
            var buildingItem = RanchPlotHandler.BuildingItemFor(newType);
            if (buildingItem.HasValue && !RanchPlotHandler.HasItem(buildingItem.Value))
            {
                Logger.Warning(
                    $"[AP] Blocked plot build: {newType} — its Plans item has not been received " +
                    "(menu filter was bypassed)");
                StatusHUD.Instance?.ShowNotification($"AP: {newType} locked - Plans item not received yet");
                // The game already charged the purchase before Replace — give it back.
                RanchPlotHandler.RefundNewbucks(
                    RanchPlotHandler.VanillaBuildCost(newType), $"blocked locked {newType} build");
                __result = null;
                return false;
            }
        }

        // Tier 1 — replacing an EMPTY plot consumes a plot slot for its area.
        if (RanchPlotHandler.PlotsEnabled
            && oldLandPlot != null && oldLandPlot.TypeId == LandPlot.Id.EMPTY
            && !RanchPlotHandler.CanBuildOnPlot(oldLandPlot, out var blockMessage))
        {
            Logger.Warning($"[AP] Blocked plot build at Replace — {blockMessage}");
            StatusHUD.Instance?.ShowNotification(blockMessage);
            RanchPlotHandler.RefundNewbucks(
                RanchPlotHandler.VanillaBuildCost(newType), "blocked build (no free plot slot)");
            __result = null;
            return false;
        }

        return true;
    }
}

[HarmonyPatch(typeof(LandPlotUIRoot), nameof(LandPlotUIRoot.Upgrade))]
internal static class RanchPlotUpgradePatch
{
    private static bool Prefix(LandPlotUIRoot __instance, LandPlot.Upgrade upgrade,
                               PurchaseCost purchaseCost, ref bool __result)
    {
        if (!RanchPlotHandler.UpgradesEnabled) return true;

        LandPlot.Id building = LandPlot.Id.NONE;
        try { building = __instance.Activator?.TypeId ?? LandPlot.Id.NONE; } catch { }

        var upgradeItem = RanchPlotHandler.UpgradeItemFor(upgrade, building);
        if (upgradeItem == null || RanchPlotHandler.HasItem(upgradeItem.Value)) return true;

        Logger.Info($"[AP] Blocked plot upgrade: {upgrade} on {building} — item not received yet");
        StatusHUD.Instance?.ShowNotification("AP: upgrade locked - its item has not been received yet");
        __result = false;
        return false;
    }
}
