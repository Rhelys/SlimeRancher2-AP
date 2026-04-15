using HarmonyLib;
using Il2CppMonomiPark.SlimeRancher.UI.Fabricator;
using SlimeRancher2AP.Data;

namespace SlimeRancher2AP.Patches.LocationPatches;

/// <summary>
/// Fires location checks when the player crafts a Vacpack upgrade at the Fabricator.
/// </summary>
/// <remarks>
/// <para>
/// Patched method: <c>PlayerUpgradeFabricatableItem.FabricateAndSpendCost(int count)</c>
/// (IL2CPP interop name used because it is an explicit interface implementation).
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
    private static void Postfix(PlayerUpgradeFabricatableItem __instance,
                                int count,
                                FabricationBlockedReason __result)
    {
        // Only fire on a successful craft
        if (__result != FabricationBlockedReason.NONE) return;
        if (!Plugin.Instance.ModEnabled || !Plugin.Instance.SaveManager.HasActiveSession) return;

        var upgradeName = __instance.UpgradeDefinition?.name;
        if (string.IsNullOrEmpty(upgradeName)) return;

        var crafts = LocationTable.GetFabricatorCrafts(upgradeName);
        if (crafts.Count == 0)
        {
            Plugin.Instance.Log.LogWarning($"[AP] Fabricator: unknown upgrade '{upgradeName}' — add to LocationTable");
            return;
        }

        // Send one check per unit crafted (count is almost always 1, but handle bulk)
        for (int i = 0; i < count; i++)
        {
            var next = crafts.FirstOrDefault(l => !Plugin.Instance.SaveManager.IsChecked(l.Id));
            if (next == null)
            {
                // All levels of this upgrade have already been checked — nothing to send
                Plugin.Instance.Log.LogInfo($"[AP] Fabricator: all {crafts.Count} checks for '{upgradeName}' already sent");
                break;
            }
            Plugin.Instance.ApClient.SendCheck(next.Id);
        }
    }
}
