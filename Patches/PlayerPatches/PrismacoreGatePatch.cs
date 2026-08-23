using HarmonyLib;
using Il2CppMonomiPark.SlimeRancher.Dialogue.CommStation;
using Il2CppMonomiPark.SlimeRancher.UI.CommStation;
using SlimeRancher2AP.Archipelago;

namespace SlimeRancher2AP.Patches.PlayerPatches;

/// <summary>
/// Blocks the Prismacore boss encounter until enough Prisma Shards have been collected
/// (<c>prismacore_hunt</c> goal only).
///
/// <para>
/// <b>Why this hook.</b> The obvious targets — <c>BossFightController.TryToStartFight</c>,
/// <c>StartPhases</c>, <c>CompleteFight</c> — are all CallerCount(0) and crash the trampoline
/// since the 5/13/2026 update. But the fight is started FROM a conversation
/// (<c>TryToStartFight(IConversation)</c>), and <c>ConversationViewHolder.ShowConversation</c> is
/// CallerCount(2) and already carries a Postfix elsewhere in this mod. Refusing to show the
/// conversation prevents the fight from ever being offered.
/// </para>
///
/// <para>
/// Gigi is the only way to start or re-enter the encounter, so covering her two start
/// conversations covers every route in.
/// </para>
/// </summary>
[HarmonyPatch(typeof(ConversationViewHolder), nameof(ConversationViewHolder.ShowConversation))]
internal static class PrismacoreGatePatch
{
    /// <summary>
    /// Conversations that begin the boss fight. Both must be blocked: the game picks between
    /// them depending on whether this is a first attempt or a retry.
    /// </summary>
    private static readonly HashSet<string> FightStarters = new()
    {
        "GigiCore_StartFight",
        "GigiCore_StartFightAlt",
    };

    private static bool Prefix(IConversation conversation)
    {
        try
        {
            if (conversation == null) return true;
            if (!Plugin.Instance.ModEnabled) return true;
            if (!PrismaShardHandler.IsHuntGoal) return true;
            if (PrismaShardHandler.IsEncounterUnlocked) return true;

            var name = conversation.TryCast<FixedConversation>()?.GetDebugName();
            if (name == null || !FightStarters.Contains(name)) return true;

            Logger.Info(
                $"[AP] Prismacore encounter blocked — '{name}' refused, " +
                $"Prisma Shards {PrismaShardHandler.Progress}");

            UI.ApPopup.ShowThrottled(
                "prismacore-gate", "The Prismacore is sealed",
                "Archipelago", $"Prisma Shards: {PrismaShardHandler.Progress}");

            return false;   // do not show the conversation — the fight is never offered
        }
        catch (System.Exception ex)
        {
            // Never let a gate failure swallow a conversation the player needs.
            Logger.Warning($"[AP] PrismacoreGatePatch threw: {ex.Message}");
            return true;
        }
    }
}
