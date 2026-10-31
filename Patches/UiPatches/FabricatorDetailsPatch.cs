using HarmonyLib;
using Il2CppMonomiPark.SlimeRancher.UI.Fabricator;
using SlimeRancher2AP.Data;
using TMPro;

namespace SlimeRancher2AP.Patches.UiPatches;

/// <summary>Refreshes the details panel after a craft, so it moves on to the next check.</summary>
[HarmonyPatch(typeof(FabricatorItemDetails), nameof(FabricatorItemDetails.ItemCrafted))]
internal static class FabricatorItemCraftedPatch
{
    private static void Postfix(FabricatorItemDetails __instance, IFabricatableItem item)
        => FabricatorDetailsPatch.UpdateDisplay(__instance, item);
}

/// <summary>
/// The Fabricator details panel shows the next unsent craft check's name and its scouted item,
/// via the freeze-localization technique (see <c>ShopUiHelper</c>). Unfrozen for non-upgrades.
/// </summary>
[HarmonyPatch(typeof(FabricatorItemDetails), nameof(FabricatorItemDetails.SetItem))]
internal static class FabricatorDetailsPatch
{
    private static void Postfix(FabricatorItemDetails __instance, IFabricatableItem item)
        => UpdateDisplay(__instance, item);

    internal static void UpdateDisplay(FabricatorItemDetails __instance, IFabricatableItem item)
    {
        // As MonoBehaviour — avoids referencing Unity.Localization.
        var nameEvent = (UnityEngine.MonoBehaviour?)__instance._name;
        var descEvent = (UnityEngine.MonoBehaviour?)__instance._description;

        var upgradeItem = item?.TryCast<PlayerUpgradeFabricatableItem>();

        if (upgradeItem == null)
        {
            if (nameEvent != null) nameEvent.enabled = true;
            if (descEvent != null) descEvent.enabled = true;
            return;
        }

        // Connected only. Correct in both fabricator modes — crafts are always checks.
        if (!Plugin.Instance.ApClient.IsConnected)
        {
            if (nameEvent != null) nameEvent.enabled = true;
            if (descEvent != null) descEvent.enabled = true;
            return;
        }

        var upgradeName = upgradeItem.UpgradeDefinition?.name;
        if (string.IsNullOrEmpty(upgradeName)) return;

        // In level order; upgrades with no AP crafts keep the vanilla display.
        var crafts = LocationTable.GetFabricatorCrafts(upgradeName);
        if (crafts.Count == 0)
        {
            if (nameEvent != null) nameEvent.enabled = true;
            if (descEvent != null) descEvent.enabled = true;
            return;
        }

        // By sent checks, not upgrade level — a craft for another world never raises it.
        var next = crafts.FirstOrDefault(l => !Plugin.Instance.SaveManager.IsChecked(l.Id));

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

        if (nameEvent != null) nameEvent.enabled = false;
        if (descEvent != null) descEvent.enabled = false;

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
