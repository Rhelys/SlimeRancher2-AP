using HarmonyLib;
using Il2CppMonomiPark.SlimeRancher.Economy;
using Il2CppMonomiPark.SlimeRancher.UI;
using Il2CppMonomiPark.SlimeRancher.UI.Plot;
using SlimeRancher2AP.Archipelago;
using SlimeRancher2AP.UI;
using UnityEngine;

namespace SlimeRancher2AP.Patches.RanchPatches;

// ─────────────────────────────────────────────────────────────────────────────
// Ranch plot enforcement (see RanchPlotHandler), on safely patchable methods:
//   • UIActivator.OnInteract   — an empty plot's build menu won't open without a free slot.
//     Built plots open normally.
//   • LandPlotLocation.Replace — backstop for tiers 1–2, where an empty plot becomes a building.
//   • LandPlotUIRoot.Upgrade   — backstop for tier 3; the menu already hides locked upgrades.
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
        ApPopup.ShowThrottled("plot-locked", blockMessage, "Archipelago", "Plot locked");
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

        // Tier 2 — building type
        if (RanchPlotHandler.BuildingsEnabled)
        {
            var buildingItem = RanchPlotHandler.BuildingItemFor(newType);
            if (buildingItem.HasValue && !RanchPlotHandler.HasItem(buildingItem.Value))
            {
                Logger.Warning(
                    $"[AP] Blocked plot build: {newType} — its Plans item has not been received " +
                    "(menu filter was bypassed)");
                ApPopup.ShowThrottled("plot-plans", $"{newType} Plans not received",
                                      "Archipelago", "Building locked");
                // The game already charged the purchase before Replace — give it back.
                RanchPlotHandler.RefundNewbucks(
                    RanchPlotHandler.VanillaBuildCost(newType), $"blocked locked {newType} build");
                __result = null;
                return false;
            }
        }

        // Tier 1 — a free slot in the area
        if (RanchPlotHandler.PlotsEnabled
            && oldLandPlot != null && oldLandPlot.TypeId == LandPlot.Id.EMPTY
            && !RanchPlotHandler.CanBuildOnPlot(oldLandPlot, out var blockMessage))
        {
            Logger.Warning($"[AP] Blocked plot build at Replace — {blockMessage}");
            ApPopup.ShowThrottled("plot-slot", blockMessage, "Archipelago", "Plot locked");
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
        ApPopup.ShowThrottled("plot-upgrade", "Upgrade not received yet",
                              "Archipelago", "Upgrade locked");
        __result = false;
        return false;
    }
}
