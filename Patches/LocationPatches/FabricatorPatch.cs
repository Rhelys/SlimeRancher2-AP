using HarmonyLib;
using Il2CppMonomiPark.SlimeRancher.DataModel;
using Il2CppMonomiPark.SlimeRancher.UI.Fabricator;
using SlimeRancher2AP.Archipelago;
using SlimeRancher2AP.Data;
using SlimeRancher2AP.Patches.PlayerPatches;

namespace SlimeRancher2AP.Patches.LocationPatches;

/// <summary>
/// A Fabricator craft sends the upgrade's next unsent craft check; the cost is spent but the
/// vanilla grant is blocked (<c>FabricatorUpgradeBlockPatch</c>) — the upgrade arrives via AP.
/// Patched under its interop name, being an explicit interface implementation.
/// </summary>
[HarmonyPatch(typeof(PlayerUpgradeFabricatableItem),
    "MonomiPark_SlimeRancher_UI_Fabricator_IFabricatableItem_FabricateAndSpendCost")]
internal static class FabricatorPatch
{
    /// <summary>
    /// The switch every fabricator patch consults. Never gate on <c>randomize_fabricator</c> —
    /// crafts are checks in both modes, so gating on it would make them unsendable.
    /// </summary>
    internal static bool IsEnabled =>
        Plugin.Instance.ModEnabled
        && Plugin.Instance.SaveManager.IsSaveBound;

    internal static bool IsCrafting { get; private set; }

    /// <summary>
    /// The upgrade being crafted, cleared only after its check is sent — so the level patches
    /// can count the in-flight craft and the display updates the same frame.
    /// </summary>
    internal static string? CraftingUpgradeName { get; private set; }

    private static void Prefix(PlayerUpgradeFabricatableItem __instance)
    {
        if (IsEnabled)
        {
            IsCrafting = true;
            CraftingUpgradeName = __instance.UpgradeDefinition?.name;
            FabricatorUpgradeBlockPatch.WasCraftBlocked = false;
        }
    }

    private static void Postfix(PlayerUpgradeFabricatableItem __instance,
                                int count,
                                FabricationBlockedReason __result)
    {
        IsCrafting = false;

        if (__result != FabricationBlockedReason.NONE)
        {
            // Log it — otherwise the slot just looks unresponsive.
            if (IsEnabled)
                Logger.Info(
                    $"[AP] Fabricator: craft of '{__instance.UpgradeDefinition?.name ?? "?"}' " +
                    $"refused by the game ({__result}) — no cost spent, no check sent");
            CraftingUpgradeName = null;
            return;
        }
        if (!IsEnabled) { CraftingUpgradeName = null; return; }

        var upgradeName = __instance.UpgradeDefinition?.name;
        if (string.IsNullOrEmpty(upgradeName)) { CraftingUpgradeName = null; return; }

        var crafts = LocationTable.GetFabricatorCrafts(upgradeName);
        if (crafts.Count == 0)
        {
            Logger.Warning($"[AP] Fabricator: unknown upgrade '{upgradeName}' — add to LocationTable");
            CraftingUpgradeName = null;
            return;
        }

        // SendCheck marks as it goes, so each iteration takes the next unsent craft.
        for (int i = 0; i < count; i++)
        {
            var next = crafts.FirstOrDefault(l => !Plugin.Instance.SaveManager.IsChecked(l.Id));
            if (next == null)
            {
                Logger.Info($"[AP] Fabricator: all {crafts.Count} checks for '{upgradeName}' already sent");
                break;
            }
            Plugin.Instance.ApClient.SendCheck(next.Id);
        }

        // Only after SendCheck, for synchronous display refreshes.
        CraftingUpgradeName = null;
    }
}

/// <summary>
/// The grid's progress dots show crafts sent, not the model level — a craft whose item went to
/// another player never advances the model. Non-virtual, safe.
/// </summary>
[HarmonyPatch(typeof(PlayerUpgradeFabricatableItem), "get_CurrentUpgradeLevel")]
internal static class FabricatorCurrentLevelPatch
{
    private static bool Prefix(PlayerUpgradeFabricatableItem __instance, ref int __result)
    {
        if (!FabricatorPatch.IsEnabled)
            return true;

        var upgradeName = __instance.UpgradeDefinition?.name;
        if (string.IsNullOrEmpty(upgradeName)) return true;

        var crafts = LocationTable.GetFabricatorCrafts(upgradeName);
        if (crafts.Count == 0) return true; // not an AP upgrade

        // Sent checks plus the in-flight craft: the display refreshes inside the craft, before
        // our Postfix sends its check.
        int checkedCount = crafts.Count(l => Plugin.Instance.SaveManager.IsChecked(l.Id));
        if (FabricatorPatch.CraftingUpgradeName == upgradeName)
            checkedCount++;
        __result = checkedCount - 1;
        return false;
    }
}

/// <summary>
/// The detail panel's next tier, counted as in <see cref="FabricatorCurrentLevelPatch"/>. (Its
/// cost and recipe getters read the level natively — see <c>UpgradeModelGetLevelPatch</c>.)
/// </summary>
[HarmonyPatch(typeof(PlayerUpgradeFabricatableItem), "get_NextUpgradeLevelDefinition")]
internal static class FabricatorNextLevelDefinitionPatch
{
    private static bool Prefix(PlayerUpgradeFabricatableItem __instance,
                                ref UpgradeDefinition.UpgradeLevelDefinition __result)
    {
        if (!FabricatorPatch.IsEnabled)
            return true;

        var upgradeDef = __instance.UpgradeDefinition;
        if (upgradeDef == null) return true;

        var upgradeName = upgradeDef.name;
        if (string.IsNullOrEmpty(upgradeName)) return true;

        var crafts = LocationTable.GetFabricatorCrafts(upgradeName);
        if (crafts.Count == 0) return true;

        int checkedCount = crafts.Count(l => Plugin.Instance.SaveManager.IsChecked(l.Id));
        if (FabricatorPatch.CraftingUpgradeName == upgradeName)
            checkedCount++;

        // = index of the next tier
        if (checkedCount >= upgradeDef.LevelCount) return true; // all done

        __result = upgradeDef.GetUpgradeLevel(checkedCount);
        return false;
    }
}
