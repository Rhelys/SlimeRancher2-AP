using HarmonyLib;
using Il2CppMonomiPark.SlimeRancher.Dialogue.CommStation;
using Il2CppMonomiPark.SlimeRancher.UI.CommStation;
using SlimeRancher2AP.Data;
using UnityEngine;

namespace SlimeRancher2AP.Patches.LocationPatches;

// ---------------------------------------------------------------------------
// Active conversation tracker
// ---------------------------------------------------------------------------

/// <summary>
/// Tracks which <see cref="FixedConversation"/> is currently being displayed so that
/// the gift-page patches below know whether to suppress the in-game grant.
///
/// Hook: <c>ConversationViewHolder.ShowConversation(IConversation, bool)</c> fires
/// whenever a new conversation begins playing. The debug name is extracted and stored
/// in <see cref="ActiveConversationDebugName"/>. All conversation UI and page callbacks
/// run on the main thread, so this static field is safe to read without locking.
/// </summary>
[HarmonyPatch(typeof(ConversationViewHolder), nameof(ConversationViewHolder.ShowConversation))]
internal static class ConversationActiveTrackerPatch
{
    /// <summary>
    /// Debug name (<c>AbstractConversation.GetDebugName()</c>) of the conversation
    /// currently open in the CommStation UI, or <see langword="null"/> if none.
    /// Updated each time <c>ShowConversation</c> fires.
    /// </summary>
    internal static string? ActiveConversationDebugName { get; private set; }

    private static void Postfix(IConversation conversation)
    {
        if (conversation == null)
        {
            ActiveConversationDebugName = null;
            return;
        }

        var fixedConv = conversation.TryCast<FixedConversation>();
        ActiveConversationDebugName = fixedConv?.GetDebugName();

        Logger.Info(
            $"[AP-Conv] Conversation started: '{ActiveConversationDebugName}'" +
            $"  (cast {(fixedConv == null ? "FAILED — not FixedConversation" : "ok")}");
    }

    /// <summary>
    /// Returns <see langword="true"/> when the currently-active conversation is an AP
    /// location check under the current <see cref="ConversationCheckMode"/>.
    /// When <see langword="true"/>, gift-page <c>ApplyChanges()</c> should be suppressed
    /// so the AP server delivers the randomized item instead of the hardcoded in-game gift.
    /// </summary>
    internal static bool ShouldSuppressGift()
    {
        if (Plugin.Instance == null || !Plugin.Instance.ModEnabled) return false;

        var mode = Plugin.Instance.ApClient?.SlotData?.ConversationChecks
                   ?? ConversationCheckMode.Off;
        if (mode == ConversationCheckMode.Off) return false;

        var debugName = ActiveConversationDebugName;
        if (string.IsNullOrEmpty(debugName)) return false;

        if (!LocationTable.TryGetByConversation(debugName, out var loc) || loc is null)
            return false;

        return LocationTable.IsConversationIncluded(loc.Type, mode);
    }
}

// ---------------------------------------------------------------------------
// Gift blueprint suppression
// ---------------------------------------------------------------------------

/// <summary>
/// Intercepts CommStation conversations that gift a gadget blueprint.
///
/// <list type="bullet">
///   <item>When the active conversation is an AP location:
///     <c>Prefix</c> returns <see langword="false"/>, blocking <c>ApplyChanges()</c>
///     so the blueprint is NOT granted. The AP server delivers the randomized item
///     via <see cref="SlimeRancher2AP.Archipelago.ItemHandler"/> instead.</item>
///   <item>Otherwise the original runs normally and <c>Postfix</c> logs the grant.</item>
/// </list>
///
/// Note: <c>ConversationRecordedPatch</c> is responsible for sending the actual AP check —
/// this patch only suppresses the duplicate in-game gift. Multi-gift conversations
/// (e.g. <c>ViktorGift_GadgetIntro</c> with three blueprint pages) are suppressed on
/// every page, but only one AP check is sent.
/// </summary>
[HarmonyPatch(typeof(ConversationPageGiftBlueprint), nameof(ConversationPageGiftBlueprint.ApplyChanges))]
internal static class ConversationPageGiftBlueprintPatch
{
    private static bool Prefix(ConversationPageGiftBlueprint __instance, out bool __state)
    {
        // __state must be set before any early return so Harmony can pass it to Postfix.
        var director = SceneContext.Instance?.GadgetDirector;
        __state = director != null && __instance.gadget != null
                  && director.IsBlueprintUnlocked(__instance.gadget);

        if (ConversationActiveTrackerPatch.ShouldSuppressGift())
        {
            Logger.Info(
                $"[AP-Conv] GiftBlueprint suppressed (AP will deliver item): " +
                $"gadget='{__instance.gadget?.name}'  " +
                $"conv='{ConversationActiveTrackerPatch.ActiveConversationDebugName}'");
            return false; // skip ApplyChanges
        }

        return true;
    }

    private static void Postfix(ConversationPageGiftBlueprint __instance, bool __state)
    {
        if (!Plugin.Instance.ModEnabled) return;
        if (__instance.gadget == null) return;
        if (ConversationActiveTrackerPatch.ShouldSuppressGift()) return; // already logged in Prefix

        var gadgetName = __instance.gadget.name;
        var eventName  = __instance.noticeEvent?.name ?? "(null)";
        var debugStr   = __instance.ToDebugString() ?? "(null)";

        if (__state)
        {
            Logger.Debug(
                $"[AP-Conv] GiftBlueprint (already unlocked, skipping): " +
                $"gadget='{gadgetName}'  event='{eventName}'  debug='{debugStr}'");
            return;
        }

        Logger.Info(
            $"[AP-Conv] GiftBlueprint (granted in-game): " +
            $"gadget='{gadgetName}'  event='{eventName}'  debug='{debugStr}'");
    }
}

// ---------------------------------------------------------------------------
// Gift gadget (unit) suppression
// ---------------------------------------------------------------------------

/// <summary>
/// Intercepts conversations that gift a gadget unit directly (not a blueprint).
/// Suppresses the in-game grant when the conversation is an active AP location check.
///
/// Note: Most GiftGadget conversations are the GiftPrefab variants (e.g. a pre-built
/// Warp Depot) that are NOT in the LocationTable, so suppression will rarely fire here.
/// The guard is present for correctness in case a GiftGadget conversation is ever
/// added to the location pool.
/// </summary>
[HarmonyPatch(typeof(ConversationPageGiftGadget), nameof(ConversationPageGiftGadget.ApplyChanges))]
internal static class ConversationPageGiftGadgetPatch
{
    private static bool Prefix(ConversationPageGiftGadget __instance)
    {
        if (ConversationActiveTrackerPatch.ShouldSuppressGift())
        {
            Logger.Info(
                $"[AP-Conv] GiftGadget suppressed (AP will deliver item): " +
                $"gadget='{__instance.gadget?.name}'  " +
                $"conv='{ConversationActiveTrackerPatch.ActiveConversationDebugName}'");
            return false;
        }

        return true;
    }

    private static void Postfix(ConversationPageGiftGadget __instance)
    {
        if (!Plugin.Instance.ModEnabled) return;
        if (__instance.gadget == null) return;
        if (ConversationActiveTrackerPatch.ShouldSuppressGift()) return;

        Logger.Info(
            $"[AP-Conv] GiftGadget (granted in-game): gadget='{__instance.gadget.name}'");
    }
}

// ---------------------------------------------------------------------------
// Gift upgrade component suppression
// ---------------------------------------------------------------------------

/// <summary>
/// Intercepts conversations that gift an UpgradeComponent
/// (e.g. Mochi's <c>Conv_Mochi_ArchiveKeyComponent</c>).
/// Suppresses the in-game grant when the conversation is an active AP location check.
/// </summary>
[HarmonyPatch(typeof(ConversationPageGiftUpgradeComponent), nameof(ConversationPageGiftUpgradeComponent.ApplyChanges))]
internal static class ConversationPageGiftUpgradeComponentPatch
{
    private static bool Prefix(ConversationPageGiftUpgradeComponent __instance)
    {
        if (ConversationActiveTrackerPatch.ShouldSuppressGift())
        {
            Logger.Info(
                $"[AP-Conv] GiftUpgradeComponent suppressed (AP will deliver item): " +
                $"component='{__instance.upgradeComponent?.name}'  " +
                $"conv='{ConversationActiveTrackerPatch.ActiveConversationDebugName}'");
            return false;
        }

        return true;
    }

    private static void Postfix(ConversationPageGiftUpgradeComponent __instance)
    {
        if (!Plugin.Instance.ModEnabled) return;
        if (ConversationActiveTrackerPatch.ShouldSuppressGift()) return;

        var compName = __instance.upgradeComponent?.name ?? "(null)";
        Logger.Info(
            $"[AP-Conv] GiftUpgradeComponent (granted in-game): component='{compName}'");
    }
}

// ---------------------------------------------------------------------------
// Suppress callout animation on suppressed gift pages (fixes DOTween null-ref)
// ---------------------------------------------------------------------------

/// <summary>
/// When a <see cref="ConversationPageGiftBlueprint"/> is part of an AP location check,
/// its vanilla grant is blocked by <see cref="ConversationPageGiftBlueprintPatch"/>.
/// The <c>ConversationPageNoticeViewHolder</c> still renders the page and starts a
/// <c>DOLocalJump</c> animation on <c>_calloutHolder</c> — which fails with a null-ref
/// because the blueprint/gadget object was never instantiated.
///
/// Returning <see langword="false"/> from <c>ShouldShowCallout()</c> causes the view holder
/// to deactivate <c>_calloutHolder</c> before any animation starts, eliminating the error.
/// The conversation page still displays its text body normally.
/// </summary>
[HarmonyPatch(typeof(ConversationPageGiftBlueprint),
    nameof(ConversationPageGiftBlueprint.ShouldShowCallout))]
internal static class ConversationPageGiftBlueprintCalloutPatch
{
    // Always skip the native original — the native ShouldShowCallout body can crash
    // (AccessViolationException / null unbox) when invoked on objects that are not yet
    // fully initialised during IL2CPP static setup.
    //
    // Return false (hide callout) only when we are suppressing the gift for an AP location
    // check. Otherwise return true — the game only invokes ShouldShowCallout on pages that
    // have been properly configured, so there is no need to re-inspect __instance here.
    // Do NOT access __instance.gadget in this Prefix: the field pointer can be garbage
    // during early IL2CPP initialisation, causing an unrecoverable AccessViolationException.
    private static bool Prefix(ref bool __result)
    {
        __result = !ConversationActiveTrackerPatch.ShouldSuppressGift();
        return false; // always skip original
    }
}
