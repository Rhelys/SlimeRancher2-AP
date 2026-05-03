using HarmonyLib;
using Il2CppMonomiPark.SlimeRancher.DataModel;
using Il2CppMonomiPark.SlimeRancher.UI.Fabricator;
using SlimeRancher2AP.Archipelago;
using SlimeRancher2AP.Data;
using SlimeRancher2AP.Patches.PlayerPatches;

namespace SlimeRancher2AP.Patches.LocationPatches;

/// <summary>
/// Fires location checks when the player crafts a Vacpack upgrade at the Fabricator,
/// and suppresses the direct upgrade grant so the item arrives via Archipelago instead.
/// </summary>
/// <remarks>
/// <para>
/// Patched method: <c>PlayerUpgradeFabricatableItem.FabricateAndSpendCost(int count)</c>
/// (IL2CPP interop name used because it is an explicit interface implementation).
/// </para>
/// <para>
/// The Prefix sets <see cref="IsCrafting"/> before the original runs. This signals
/// <c>FabricatorUpgradeBlockPatch</c> (in ActorUpgradeHandlerPatch.cs) to skip
/// <c>UpgradeModel.IncrementUpgradeLevel</c> — the method the Fabricator actually calls to
/// apply the upgrade. The crafting cost is still spent; the upgrade arrives via Archipelago.
/// </para>
/// <para>
/// Multiple sequential location IDs exist for the same upgrade type (e.g. four HealthCapacity
/// crafts). We use <c>LocationTable.GetFabricatorCrafts(upgradeName)</c> — which returns them
/// in ascending ID order — and send the first ID not yet in the save manager's checked set.
/// Because <c>SendCheck</c> calls <c>MarkChecked</c> internally, a loop for bulk crafts
/// (count &gt; 1) naturally advances to the next unchecked entry each iteration.
/// </para>
/// </remarks>
[HarmonyPatch(typeof(PlayerUpgradeFabricatableItem),
    "MonomiPark_SlimeRancher_UI_Fabricator_IFabricatableItem_FabricateAndSpendCost")]
internal static class FabricatorPatch
{
    /// <summary>
    /// True while <c>FabricateAndSpendCost</c> is executing with AP mode active.
    /// </summary>
    internal static bool IsCrafting { get; private set; }

    /// <summary>
    /// The <c>UpgradeDefinition.name</c> of the upgrade currently being fabricated, or
    /// <c>null</c> when no craft is in progress. Set in <see cref="Prefix"/> and cleared at
    /// the end of <see cref="Postfix"/> (after <c>SendCheck</c> marks the location as checked).
    /// Read by <see cref="FabricatorCurrentLevelPatch"/> to count the in-flight craft
    /// optimistically, so the display updates within the same frame as the craft.
    /// </summary>
    internal static string? CraftingUpgradeName { get; private set; }

    private static void Prefix(PlayerUpgradeFabricatableItem __instance)
    {
        if (Plugin.Instance.ModEnabled && Plugin.Instance.SaveManager.HasActiveSession)
        {
            IsCrafting = true;
            CraftingUpgradeName = __instance.UpgradeDefinition?.name;
            FabricatorUpgradeBlockPatch.WasCraftBlocked = false; // reset for this new craft
        }
    }

    private static void Postfix(PlayerUpgradeFabricatableItem __instance,
                                int count,
                                FabricationBlockedReason __result)
    {
        IsCrafting = false;  // always reset first, before any early return

        if (__result != FabricationBlockedReason.NONE) { CraftingUpgradeName = null; return; }
        if (!Plugin.Instance.ModEnabled || !Plugin.Instance.SaveManager.HasActiveSession) { CraftingUpgradeName = null; return; }

        var upgradeName = __instance.UpgradeDefinition?.name;
        if (string.IsNullOrEmpty(upgradeName)) { CraftingUpgradeName = null; return; }

        var crafts = LocationTable.GetFabricatorCrafts(upgradeName);
        if (crafts.Count == 0)
        {
            Plugin.Instance.Log.LogWarning($"[AP] Fabricator: unknown upgrade '{upgradeName}' — add to LocationTable");
            CraftingUpgradeName = null;
            return;
        }

        // Send one check per unit crafted (count is almost always 1, but handle bulk)
        for (int i = 0; i < count; i++)
        {
            var next = crafts.FirstOrDefault(l => !Plugin.Instance.SaveManager.IsChecked(l.Id));
            if (next == null)
            {
                Plugin.Instance.Log.LogInfo($"[AP] Fabricator: all {crafts.Count} checks for '{upgradeName}' already sent");
                break;
            }
            Plugin.Instance.ApClient.SendCheck(next.Id);
        }

        // Clear AFTER SendCheck so that any synchronous display refresh that fires during or
        // immediately after SendCheck still sees CraftingUpgradeName as set and the checked
        // count as already incremented.
        CraftingUpgradeName = null;
    }
}

/// <summary>
/// Overrides <c>PlayerUpgradeFabricatableItem.CurrentUpgradeLevel</c> in AP mode to return the
/// number of AP checks sent for this upgrade type rather than the actual <c>UpgradeModel</c>
/// level.
/// </summary>
/// <remarks>
/// <para>
/// <c>CurrentUpgradeLevel</c> drives the filled-dot progress indicator in the Fabricator grid
/// (left panel). The right-side detail panel (cost and recipe) goes through
/// <c>NextUpgradeLevelDefinition</c> instead — patched separately in
/// <see cref="FabricatorNextLevelDefinitionPatch"/>.
/// </para>
/// <para>
/// In AP mode the player may send checks at the Fabricator for items that go to other players,
/// so the <c>UpgradeModel</c> level never advances for those upgrades.  Without this patch the
/// Fabricator always shows tier-0 cost (level −1 + 1 = 0) even when tier 1 or higher is next
/// to craft, breaking the material-progression gate.
/// </para>
/// <para>
/// <c>CurrentUpgradeLevel</c> is non-virtual, so this patch is safe on IL2CPP.
/// </para>
/// </remarks>
[HarmonyPatch(typeof(PlayerUpgradeFabricatableItem), "get_CurrentUpgradeLevel")]
internal static class FabricatorCurrentLevelPatch
{
    private static bool Prefix(PlayerUpgradeFabricatableItem __instance, ref int __result)
    {
        if (!Plugin.Instance.ModEnabled || !Plugin.Instance.SaveManager.HasActiveSession)
            return true;

        var upgradeName = __instance.UpgradeDefinition?.name;
        if (string.IsNullOrEmpty(upgradeName)) return true;

        var crafts = LocationTable.GetFabricatorCrafts(upgradeName);
        if (crafts.Count == 0) return true; // not tracked — vanilla behaviour

        // Number of checks sent (persisted) plus any in-flight craft.
        // The Fabricator refreshes its display synchronously inside FabricateAndSpendCost,
        // before our Postfix has called SendCheck/MarkChecked. We compensate by counting the
        // current craft optimistically: CraftingUpgradeName is set in the Prefix and cleared
        // only after SendCheck, so the display always sees the updated level.
        //
        //   0 sent, 0 in-flight → CurrentUpgradeLevel = -1 → NextLevel = 0 → tier-0 cost ✓
        //   0 sent, 1 in-flight → CurrentUpgradeLevel =  0 → NextLevel = 1 → tier-1 cost ✓
        //   1 sent, 0 in-flight → CurrentUpgradeLevel =  0 → NextLevel = 1 → tier-1 cost ✓
        int checkedCount = crafts.Count(l => Plugin.Instance.SaveManager.IsChecked(l.Id));
        if (FabricatorPatch.CraftingUpgradeName == upgradeName)
            checkedCount++;   // count the craft that is currently in-flight
        __result = checkedCount - 1;
        return false;
    }
}

/// <summary>
/// Overrides <c>PlayerUpgradeFabricatableItem.NextUpgradeLevelDefinition</c> in AP mode to
/// return the <c>UpgradeLevelDefinition</c> for the tier that corresponds to the number of
/// AP checks already sent (plus any in-flight craft).
/// </summary>
/// <remarks>
/// <para>
/// The right-side detail panel in the Fabricator (crafting requirements and material cost)
/// calls <c>NextUpgradeLevelDefinition</c> directly rather than reading
/// <c>CurrentUpgradeLevel</c>. Because the native <c>PurchaseCost</c> and <c>Recipe</c>
/// getters are virtual and invoke the native IL2CPP method body, they bypass any patch on
/// <c>get_CurrentUpgradeLevel</c> entirely.
/// </para>
/// <para>
/// <c>NextUpgradeLevelDefinition</c> is non-virtual, so Harmony can place a detour safely.
/// The override returns <c>UpgradeDefinition.GetUpgradeLevel(checkedCount)</c> where
/// <c>checkedCount</c> is computed identically to <see cref="FabricatorCurrentLevelPatch"/>
/// (persisted checks + 1 if a craft is currently in-flight).
/// </para>
/// </remarks>
[HarmonyPatch(typeof(PlayerUpgradeFabricatableItem), "get_NextUpgradeLevelDefinition")]
internal static class FabricatorNextLevelDefinitionPatch
{
    private static bool Prefix(PlayerUpgradeFabricatableItem __instance,
                                ref UpgradeDefinition.UpgradeLevelDefinition __result)
    {
        if (!Plugin.Instance.ModEnabled || !Plugin.Instance.SaveManager.HasActiveSession)
            return true;

        var upgradeDef = __instance.UpgradeDefinition;
        if (upgradeDef == null) return true;

        var upgradeName = upgradeDef.name;
        if (string.IsNullOrEmpty(upgradeName)) return true;

        var crafts = LocationTable.GetFabricatorCrafts(upgradeName);
        if (crafts.Count == 0) return true; // not tracked — vanilla behaviour

        int checkedCount = crafts.Count(l => Plugin.Instance.SaveManager.IsChecked(l.Id));
        if (FabricatorPatch.CraftingUpgradeName == upgradeName)
            checkedCount++; // count the craft that is currently in-flight

        // checkedCount == index of the NEXT tier to craft (0-based)
        if (checkedCount >= upgradeDef.LevelCount) return true; // all tiers done — let vanilla handle

        __result = upgradeDef.GetUpgradeLevel(checkedCount);
        return false;
    }
}
