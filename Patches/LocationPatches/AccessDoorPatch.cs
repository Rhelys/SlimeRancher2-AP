using HarmonyLib;
using Il2CppMonomiPark.SlimeRancher.UI.AccessDoor;
using Il2CppMonomiPark.SlimeRancher.UI.Framework.Displays;
using Il2CppMonomiPark.SlimeRancher.UI.Purchase;
using Il2CppMonomiPark.World;
using SlimeRancher2AP.Data;
using TMPro;
using UnityEngine;

namespace SlimeRancher2AP.Patches.LocationPatches;

/// <summary>
/// Overrides the AccessDoor purchase UI text to display Archipelago location/item
/// information instead of the game's default expansion name and description.
/// Only active when <c>randomize_conservatory_expansions</c> is enabled.
/// </summary>
[HarmonyPatch(typeof(AccessDoorUI),
    "MonomiPark_SlimeRancher_UI_Framework_Displays_IDisplayShowDataHandler_MonomiPark_SlimeRancher_UI_Purchase_MenuPurchaseItemModel__UpdateShowData")]
internal static class AccessDoorUITextPatch
{
    // Known expansion door IDs — used to identify which activator belongs to an AP expansion terminal.
    private static readonly System.Collections.Generic.HashSet<string> _knownDoorIds = new()
    {
        "door1733849867", // The Gully
        "door0129604684", // The Tidepools
        "door0749608168", // The Archway
        "door0010140679", // The Den
        "door1356553442", // The Digsite
    };

    // Deferred text override — Unity Localization fires its async OnUpdateString callback one
    // or more frames after UpdateShowData, overwriting our text. We re-apply for several
    // frames from ApUpdateBehaviour.Update() to outlast the localization cycle.
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

        // All 5 expansion activators are loaded simultaneously, so we can't use FirstOrDefault.
        // Instead find the one closest to the player — UpdateShowData fires synchronously on
        // interact, so the player is still standing at the terminal they just pressed.
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
        // Strip the "Conservatory Expansion: " prefix — the full name is too long for the UI panel
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

        // Freeze LocalizeStringEvent so it doesn't keep re-triggering localization loads
        var titleEvent = (MonoBehaviour?)__instance._titleLabel;
        var descEvent  = (MonoBehaviour?)__instance._descriptionLabel;
        if (titleEvent != null) titleEvent.enabled = false;
        if (descEvent  != null) descEvent.enabled  = false;

        // HeaderLabel and MainDescription are the confirmed component names on this prefab
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

        // Set text immediately, then arm the deferred updater for 10 frames to outlast
        // Unity Localization's async OnUpdateString callback, which fires 1–2 frames later.
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

/// <summary>
/// Blocks the interaction with an expansion terminal when the check has already been sent.
/// Prevents the Newbucks confirm dialog from opening again while the door is still locked
/// waiting for the AP item to arrive.
/// </summary>
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
/// Intercepts the conservatory expansion purchase confirmation.
/// When <c>randomize_conservatory_expansions</c> is active and the player confirms:
///   • Blocks re-purchase if the check was already sent.
///   • Sends the AP check for this terminal (first time only).
///   • Returns false to block the original — no Newbucks spent, door stays locked.
/// When cancelled (<paramref name="result"/> = false), the original runs normally to close the UI.
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
            return false;
        }

        Plugin.Instance.ApClient.SendCheck(locInfo.Id);
        Logger.Info($"[AP] Conservatory expansion check sent: {locInfo.Name} (door='{doorId}')");
        return false; // block original — no Newbucks spent, door stays locked until AP item received
    }
}
