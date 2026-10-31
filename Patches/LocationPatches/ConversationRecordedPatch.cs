using HarmonyLib;
using Il2CppMonomiPark.SlimeRancher.Dialogue.CommStation;
using SlimeRancher2AP.Data;

namespace SlimeRancher2AP.Patches.LocationPatches;

/// <summary>
/// A conversation's first completion (<c>FixedConversation.RecordPlayed</c>) is its check, per
/// <see cref="ConversationCheckMode"/> — once per conversation, however many gift pages it has.
/// </summary>
[HarmonyPatch(typeof(FixedConversation), nameof(FixedConversation.RecordPlayed))]
internal static class ConversationRecordedPatch
{
    private static void Prefix(FixedConversation __instance, out bool __state)
    {
#if DEBUG
        SlimeRancher2AP.Utils.DebugTrace.Once("ConversationRecordedPatch.Prefix — first entry");
#endif
        // Before RecordPlayed marks it. A throw (half-initialised during restore) = played.
        try { __state = __instance.HasBeenPlayed(); }
        catch { __state = true; }
    }

    private static void Postfix(FixedConversation __instance, bool __state)
    {
        if (!Plugin.Instance.ModEnabled) return;

        // Before the UI tears down.
        ConversationActiveTrackerPatch.KillConversationTweens("RecordPlayed");

        var debugName = __instance.GetDebugName() ?? "";
        Logger.Info(
            $"[AP-Conv] RecordPlayed: debug='{debugName}'  hasBeenPlayed(before)={__state}");

        if (__state) return; // already played — re-read, not a first completion

        var mode = Plugin.Instance.ApClient?.SlotData?.ConversationChecks
                   ?? ConversationCheckMode.Off;
        if (mode == ConversationCheckMode.Off) return;

        if (string.IsNullOrEmpty(debugName)) return;

        if (!LocationTable.TryGetByConversation(debugName, out var loc) || loc is null)
        {
            // Excluded on purpose, or a new conversation not yet mapped.
            Logger.Info(
                $"[AP-Conv] Unmapped conversation completed: '{debugName}'");
            return;
        }

        if (!LocationTable.IsConversationIncluded(loc.Type, mode))
        {
            Logger.Info(
                $"[AP-Conv] Conversation excluded by mode={mode}: '{debugName}' (type={loc.Type})");
            return;
        }

        Logger.Info(
            $"[AP-Conv] Check: '{loc.Name}' (id={loc.Id}  debug='{debugName}'  mode={mode})");

        Plugin.Instance.ApClient?.SendCheck(loc.Id);

        UI.StatusHUD.Instance?.ShowNotification($"AP ✓  {loc.Name}");
    }
}
