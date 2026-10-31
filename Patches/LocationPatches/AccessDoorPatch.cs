using HarmonyLib;
using Il2CppMonomiPark.SlimeRancher.UI.AccessDoor;
using Il2CppMonomiPark.SlimeRancher.UI.Framework.Displays;
using Il2CppMonomiPark.SlimeRancher.UI.Purchase;
using Il2CppMonomiPark.World;
using SlimeRancher2AP.Data;
using TMPro;
using UnityEngine;

namespace SlimeRancher2AP.Patches.LocationPatches;

// Don't patch AccessDoor.ForceUpdate (crashes on every scene load) or
// SlimeGateActivator.Activate — both broke the trampoline after the 5/13/2026 update.

/// <summary>
/// The expansion terminal's purchase UI shows the AP location and scouted item
/// (<c>randomize_conservatory_expansions</c>).
/// </summary>
[HarmonyPatch(typeof(AccessDoorUI),
    "MonomiPark_SlimeRancher_UI_Framework_Displays_IDisplayShowDataHandler_MonomiPark_SlimeRancher_UI_Purchase_MenuPurchaseItemModel__UpdateShowData")]
internal static class AccessDoorUITextPatch
{
    private static readonly System.Collections.Generic.HashSet<string> _knownDoorIds = new()
    {
        "door1733849867", // The Gully
        "door0129604684", // The Tidepools
        "door0749608168", // The Archway
        "door0010140679", // The Den
        "door1356553442", // The Digsite
    };

    // Re-applied for a few frames from Tick — localization overwrites it asynchronously.
    private static TMP_Text? _pendingTitle;
    private static TMP_Text? _pendingDesc;
    private static string    _pendingTitleText = "";
    private static string    _pendingDescText  = "";
    private static int       _pendingFrames    = 0;

    internal static void Tick()
    {
        if (_pendingFrames <= 0) return;
        if (_pendingTitle != null) _pendingTitle.text = _pendingTitleText;
        if (_pendingDesc  != null) _pendingDesc.text  = _pendingDescText;
        _pendingFrames--;
    }

    private static void Postfix(AccessDoorUI __instance, MenuPurchaseItemModel data)
    {
        if (!Plugin.Instance.ApClient.IsConnected) return;
        if (!(Plugin.Instance.ApClient.SlotData?.RandomizeConservatoryExpansions ?? false)) return;

        // All five activators are loaded; the pressed one is the nearest.
        var playerGo = SceneContext.Instance?.Player;
        if (playerGo == null) return;
        var playerPos = playerGo.transform.position;

        AccessDoorUIActivator? activator = null;
        float minDist = float.MaxValue;
        foreach (var a in Resources.FindObjectsOfTypeAll<AccessDoorUIActivator>())
        {
            if (a._accessDoor == null || !_knownDoorIds.Contains(a._accessDoor._id ?? "")) continue;
            float d = (a.transform.position - playerPos).sqrMagnitude;
            if (d < minDist) { minDist = d; activator = a; }
        }

        var doorId = activator?._accessDoor?._id;
        if (doorId == null)
        {
            Logger.Info("[AP] AccessDoorUITextPatch: no matching expansion activator found");
            return;
        }

        if (!LocationTable.TryGetByObjectName(doorId, out var locInfo) || locInfo == null)
        {
            Logger.Warning($"[AP] AccessDoorUITextPatch: door '{doorId}' not in LocationTable");
            return;
        }

        bool alreadyChecked = Plugin.Instance.SaveManager.IsChecked(locInfo.Id);
        // The full name is too long for the panel.
        const string prefix = "Conservatory Expansion: ";
        string title = locInfo.Name.StartsWith(prefix, System.StringComparison.Ordinal)
            ? locInfo.Name[prefix.Length..]
            : locInfo.Name;
        string description;

        var scouted = Plugin.Instance.ApClient.GetScoutedItem(locInfo.Id);
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
            description = alreadyChecked ? $"{itemLine}\n(Check already sent)" : itemLine;
        }
        else
        {
            description = alreadyChecked ? "Check already sent." : "Archipelago check";
        }

        var titleEvent = (MonoBehaviour?)__instance._titleLabel;
        var descEvent  = (MonoBehaviour?)__instance._descriptionLabel;
        if (titleEvent != null) titleEvent.enabled = false;
        if (descEvent  != null) descEvent.enabled  = false;

        var titleTmp = titleEvent?.GetComponent<TMP_Text>()
                    ?? titleEvent?.gameObject.GetComponentInChildren<TMP_Text>();
        var descTmp  = descEvent?.GetComponent<TMP_Text>()
                    ?? descEvent?.gameObject.GetComponentInChildren<TMP_Text>();

        if (titleTmp == null || descTmp == null)
        {
            var allTexts = __instance.GetComponentsInChildren<TMP_Text>(true);
            if (titleTmp == null)
                titleTmp = allTexts.FirstOrDefault(t =>
                    t.gameObject.name.Contains("Title", System.StringComparison.OrdinalIgnoreCase) ||
                    t.gameObject.name.Contains("Header", System.StringComparison.OrdinalIgnoreCase) ||
                    t.gameObject.name.Contains("Name",  System.StringComparison.OrdinalIgnoreCase));
            if (descTmp == null)
                descTmp = allTexts.FirstOrDefault(t =>
                    t.gameObject.name.Contains("Desc",  System.StringComparison.OrdinalIgnoreCase) ||
                    t.gameObject.name.Contains("Body",  System.StringComparison.OrdinalIgnoreCase) ||
                    t.gameObject.name.Contains("Main",  System.StringComparison.OrdinalIgnoreCase));
        }

        _pendingTitle     = titleTmp;
        _pendingDesc      = descTmp;
        _pendingTitleText = title;
        _pendingDescText  = description;
        _pendingFrames    = 10;

        if (titleTmp != null) titleTmp.text = title;
        if (descTmp  != null) descTmp.text  = description;

        Logger.Info($"[AP] AccessDoorUITextPatch: set '{title}' on {titleTmp?.gameObject.name ?? "NULL"}");
    }
}

/// <summary>Once its check is sent, a terminal no longer opens the purchase dialog.</summary>
[HarmonyPatch(typeof(AccessDoorUIActivator), "OnInteract")]
internal static class AccessDoorInteractPatch
{
    private static readonly System.Collections.Generic.HashSet<string> _knownDoorIds = new()
    {
        "door1733849867", "door0129604684", "door0749608168", "door0010140679", "door1356553442",
    };

    private static bool Prefix(AccessDoorUIActivator __instance)
    {
        if (!(Plugin.Instance.ApClient.SlotData?.RandomizeConservatoryExpansions ?? false)) return true;

        var doorId = __instance._accessDoor?._id;
        if (doorId == null || !_knownDoorIds.Contains(doorId)) return true;

        if (!LocationTable.TryGetByObjectName(doorId, out var locInfo) || locInfo == null) return true;

        if (Plugin.Instance.SaveManager.IsChecked(locInfo.Id))
        {
            Logger.Info($"[AP] Blocking terminal re-interaction (check already sent): {locInfo.Name}");
            return false;
        }

        return true;
    }
}

/// <summary>
/// Confirming a terminal sends its check instead of the purchase — no Newbucks spent, door
/// stays locked until its item. Cancelling runs the original.
/// </summary>
[HarmonyPatch(typeof(AccessDoorUIActivator), "OnPurchaseMenuResult")]
internal static class AccessDoorPurchasePatch
{
    private static bool Prefix(AccessDoorUIActivator __instance, UIRuntimeDisplay display, bool result)
    {
        if (!result) return true; // cancelled — let original close the UI normally
        if (!(Plugin.Instance.ApClient.SlotData?.RandomizeConservatoryExpansions ?? false)) return true;

        var doorId = __instance._accessDoor?._id;
        if (doorId == null) return true;

        if (!LocationTable.TryGetByObjectName(doorId, out var locInfo) || locInfo == null) return true;

        if (Plugin.Instance.SaveManager.IsChecked(locInfo.Id))
        {
            Logger.Info($"[AP] Expansion already checked, blocking re-purchase: {locInfo.Name}");
            // Already sent, still shut — say so, or the terminal reads as broken on re-press.
            UI.ApPopup.ShowThrottled($"expansion-{doorId}", "Expansion Access required",
                                     "Archipelago", "Expansion locked");
            return false;
        }

        Plugin.Instance.ApClient.SendCheck(locInfo.Id);
        Logger.Info($"[AP] Conservatory expansion check sent: {locInfo.Name} (door='{doorId}')");
        // Its item may already be waiting.
        Archipelago.ItemHandler.TriggerPendingDoorUnlock(doorId);

        // Explain why the door stays shut.
        UI.ApPopup.ShowThrottled($"expansion-{doorId}", "Expansion Access required",
                                 "Archipelago", "Expansion locked");

        return false; // block original — no Newbucks spent, door stays locked until AP item received
    }
}
