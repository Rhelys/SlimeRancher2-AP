using HarmonyLib;
using Il2CppMonomiPark.SlimeRancher.UI.Fabricator;
using SlimeRancher2AP.Data;
using TMPro;

namespace SlimeRancher2AP.Patches.UiPatches;

/// <summary>
/// Overrides the Fabricator item detail panel text to display Archipelago location/item
/// information instead of the game's default upgrade name and description.
/// </summary>
/// <remarks>
/// <para>
/// Patched method: <c>FabricatorItemDetails.SetItem(IFabricatableItem)</c> — called whenever
/// the player selects a different item in the Fabricator grid. Non-virtual; safe to Postfix.
/// </para>
/// <para>
/// Strategy: the original method populates two <c>LocalizeStringEvent</c> components
/// (<c>_name</c> and <c>_description</c>) which drive their child TMP_Text objects via Unity
/// Localization. We disable those components (freezing their output) and write our own text
/// directly to the TMP_Text. Components are re-enabled when a non-upgrade item (e.g. gadgets)
/// is selected so their names display correctly.
/// </para>
/// <para>
/// Title displayed: the AP location name for the <em>next uncrafted</em> level of this upgrade
/// (determined by <c>CurrentUpgradeLevel</c>, which tracks crafted count, not AP-granted count).
/// Description: the AP item name at that location (from scout cache), or "Archipelago check"
/// if scout data is not yet available.
/// </para>
/// </remarks>
/// <summary>
/// Re-runs the display override after a craft completes so the panel advances to the
/// next uncrafted check rather than staying stale. <c>ItemCrafted</c> fires on the
/// same instance and with the same item that was just crafted.
/// </summary>
[HarmonyPatch(typeof(FabricatorItemDetails), nameof(FabricatorItemDetails.ItemCrafted))]
internal static class FabricatorItemCraftedPatch
{
    private static void Postfix(FabricatorItemDetails __instance, IFabricatableItem item)
        => FabricatorDetailsPatch.UpdateDisplay(__instance, item);
}

[HarmonyPatch(typeof(FabricatorItemDetails), nameof(FabricatorItemDetails.SetItem))]
internal static class FabricatorDetailsPatch
{
    private static void Postfix(FabricatorItemDetails __instance, IFabricatableItem item)
        => UpdateDisplay(__instance, item);

    internal static void UpdateDisplay(FabricatorItemDetails __instance, IFabricatableItem item)
    {
        // Cast to MonoBehaviour so we can toggle .enabled and call GetComponent
        // without requiring a direct reference to the Unity.Localization assembly.
        var nameEvent = (UnityEngine.MonoBehaviour?)__instance._name;
        var descEvent = (UnityEngine.MonoBehaviour?)__instance._description;

        // Try to cast to a player upgrade — null for gadgets and other non-upgrade fabricatables
        var upgradeItem = item?.TryCast<PlayerUpgradeFabricatableItem>();

        if (upgradeItem == null)
        {
            // Not an upgrade slot — re-enable localization so gadget names display correctly
            if (nameEvent != null) nameEvent.enabled = true;
            if (descEvent != null) descEvent.enabled = true;
            return;
        }

        // Only override when connected; otherwise preserve normal display
        if (!Plugin.Instance.ApClient.IsConnected)
        {
            if (nameEvent != null) nameEvent.enabled = true;
            if (descEvent != null) descEvent.enabled = true;
            return;
        }

        var upgradeName = upgradeItem.UpgradeDefinition?.name;
        if (string.IsNullOrEmpty(upgradeName)) return;

        // All checks for this upgrade type in ascending ID order (= ascending level order).
        // Some UpgradeDefinitions (e.g. ResourceNodeHarvester, AmmoCapacity) are vanilla-only
        // and intentionally absent from our AP location pool — silently fall back to normal display.
        var crafts = LocationTable.GetFabricatorCrafts(upgradeName);
        if (crafts.Count == 0)
        {
            if (nameEvent != null) nameEvent.enabled = true;
            if (descEvent != null) descEvent.enabled = true;
            return;
        }

        // Use IsChecked (same logic as FabricatorPatch) rather than CurrentUpgradeLevel.
        // In a multiworld the player crafts an upgrade for someone else's world, so their
        // own upgrade level never increments — CurrentUpgradeLevel would stay stuck at 0.
        var next = crafts.FirstOrDefault(l => !Plugin.Instance.SaveManager.IsChecked(l.Id));

        // Resolve the location to display: next unchecked, or last entry if all are done.
        var display    = next ?? crafts[^1];
        bool allSent   = next == null;

        string title       = display.Name;
        string description;

        var scouted = Plugin.Instance.ApClient.GetScoutedItem(display.Id);
        if (scouted != null)
        {
            var session  = Plugin.Instance.ApClient.Session!;
            bool isSelf  = session.ConnectionInfo.Slot == scouted.Player;
            string owner = isSelf
                ? "your game"
                : (session.Players.GetPlayerAlias(scouted.Player) ?? "Unknown");
            string itemLine = isSelf
                ? $"Contains: {scouted.ItemName}"
                : $"Contains: {scouted.ItemName}\nFor: {owner}";
            description = allSent ? $"{itemLine}\n(All checks sent)" : itemLine;
        }
        else
        {
            description = allSent ? "All upgrade checks sent." : "Archipelago check";
        }

        // Freeze localization components so they don't overwrite us on the next localization tick
        if (nameEvent != null) nameEvent.enabled = false;
        if (descEvent != null) descEvent.enabled = false;

        // Get the TMP_Text driven by each LocalizeStringEvent (on the same GameObject)
        var nameTmp = nameEvent?.GetComponent<TMP_Text>();
        var descTmp = descEvent?.GetComponent<TMP_Text>();

        if (nameTmp == null && nameEvent != null)
            nameTmp = nameEvent.gameObject.GetComponentInChildren<TMP_Text>();
        if (descTmp == null && descEvent != null)
            descTmp = descEvent.gameObject.GetComponentInChildren<TMP_Text>();

        if (nameTmp != null) nameTmp.text = title;
        if (descTmp != null) descTmp.text = description;
    }
}
