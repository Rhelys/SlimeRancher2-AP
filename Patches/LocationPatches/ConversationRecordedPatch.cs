using HarmonyLib;
using Il2CppMonomiPark.SlimeRancher.Dialogue.CommStation;
using SlimeRancher2AP.Data;

namespace SlimeRancher2AP.Patches.LocationPatches;

/// <summary>
/// Detects the first-ever completion of any CommStation conversation and converts it
/// into an Archipelago location check, subject to the <see cref="ConversationCheckMode"/>
/// configured in slot data.
///
/// Hook point: <c>FixedConversation.RecordPlayed()</c> — called exactly once when the player
/// finishes reading a conversation (clicks through all pages). <c>HasBeenPlayed()</c> is captured
/// in a Prefix so that re-reads of already-played conversations are silently ignored.
///
/// This single patch covers all three modes:
///   <list type="bullet">
///     <item><see cref="ConversationCheckMode.Off"/>         — all checks skipped.</item>
///     <item><see cref="ConversationCheckMode.Conditional"/> — only the 8 curated conversations with zone/chain access requirements.</item>
///     <item><see cref="ConversationCheckMode.All"/>         — every conversation incl. story/deflect.</item>
///   </list>
///
/// Note: <c>ConversationGiftPatch.cs</c> continues to log gift details for debugging, but
/// the actual AP check is sent here — not from the gift patch — so that multi-gift
/// conversations (e.g. <c>ViktorGift_GadgetIntro</c> with 3 blueprint pages) produce
/// exactly one check per conversation regardless of page count.
/// </summary>
[HarmonyPatch(typeof(FixedConversation), nameof(FixedConversation.RecordPlayed))]
internal static class ConversationRecordedPatch
{
    private static void Prefix(FixedConversation __instance, out bool __state)
    {
#if DEBUG
        SlimeRancher2AP.Utils.DebugTrace.Once("ConversationRecordedPatch.Prefix — first entry");
#endif
        // Capture whether this conversation has already been played BEFORE RecordPlayed marks it.
        // After the original runs, HasBeenPlayed() would return true even on first play.
        // Wrapped in try/catch: HasBeenPlayed() can crash on partially-initialised IL2CPP
        // objects during scene state restoration.  Treat a throw as "already played" so the
        // Postfix skips the check (same effect as returning false from Unlock).
        try { __state = __instance.HasBeenPlayed(); }
        catch { __state = true; }
    }

    private static void Postfix(FixedConversation __instance, bool __state)
    {
        if (!Plugin.Instance.ModEnabled) return;

        // Always log at Info so we can see the conversation name and hasBeenPlayed state.
        var debugName = __instance.GetDebugName() ?? "";
        Plugin.Instance.Log.LogInfo(
            $"[AP-Conv] RecordPlayed: debug='{debugName}'  hasBeenPlayed(before)={__state}");

        if (__state) return; // already played — re-read, not a first completion

        var mode = Plugin.Instance.ApClient?.SlotData?.ConversationChecks
                   ?? ConversationCheckMode.Off;
        if (mode == ConversationCheckMode.Off) return;

        if (string.IsNullOrEmpty(debugName)) return;

        if (!LocationTable.TryGetByConversation(debugName, out var loc) || loc is null)
        {
            // Conversation not in the table — either intentionally excluded (GiftPrefab,
            // ViktorNoName, etc.) or a newly added conversation not yet mapped.
            Plugin.Instance.Log.LogInfo(
                $"[AP-Conv] Unmapped conversation completed: '{debugName}'");
            return;
        }

        if (!LocationTable.IsConversationIncluded(loc.Type, mode))
        {
            // In the location table but excluded by the current mode setting.
            Plugin.Instance.Log.LogInfo(
                $"[AP-Conv] Conversation excluded by mode={mode}: '{debugName}' (type={loc.Type})");
            return;
        }

        Plugin.Instance.Log.LogInfo(
            $"[AP-Conv] Check: '{loc.Name}' (id={loc.Id}  debug='{debugName}'  mode={mode})");

        Plugin.Instance.ApClient?.SendCheck(loc.Id);

        // Show a HUD notification so the player knows the blueprint/gadget was sent to AP
        // rather than granted directly. Message is kept short to fit the notification bar.
        UI.StatusHUD.Instance?.ShowNotification($"AP ✓  {loc.Name}");
    }
}
