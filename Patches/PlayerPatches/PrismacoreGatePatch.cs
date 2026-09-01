using HarmonyLib;
using Il2CppMonomiPark.SlimeRancher.Dialogue.CommStation;
using Il2CppMonomiPark.SlimeRancher.World;
using SlimeRancher2AP.Archipelago;

namespace SlimeRancher2AP.Patches.PlayerPatches;

/// <summary>
/// Blocks the Prismacore boss encounter until enough Prisma Shards have been collected
/// (<c>prismacore_hunt</c> goal only).
///
/// <para>
/// <b>Why this hook.</b> An earlier version refused the fight conversation instead, via a Prefix
/// on <c>ConversationViewHolder.ShowConversation</c>. That softlocked the game: returning false
/// skipped populating the dialogue view, but the caller had already opened the dialogue UI, so
/// the player was left staring at an empty box (with a stale nameplate from the pooled view) and
/// no way to close it. Observed directly —
/// <c>"encounter blocked — 'GigiCore_StartFight' refused"</c> followed 20 ms later by
/// <c>"Conversation started: 'GigiCore_StartFight'"</c>. Suppressing a conversation mid-open is
/// not something this UI survives.
/// </para>
///
/// <para>
/// Blocking the fight itself is both safer and better-targeted: the conversation plays normally
/// so the UI stays healthy, and every route into the encounter funnels through
/// <c>TryToStartFight</c> regardless of which conversation triggered it. That also removes the
/// old name list — <c>GigiCore_StartFight</c>, <c>_StartFightAlt</c> and the never-verified
/// <c>_RetryFight</c> are all covered without having to enumerate them.
/// </para>
///
/// <para>
/// <b>Patch safety.</b> <c>TryToStartFight</c> is CallerCount(0), the same profile as
/// <c>GordoEat.Awake</c> and <c>PlortDepositor.Awake</c>, both of which this mod patches
/// successfully in shipping builds.
/// </para>
/// </summary>
[HarmonyPatch(typeof(BossFightController), nameof(BossFightController.TryToStartFight))]
internal static class PrismacoreGatePatch
{
    private static bool Prefix(IConversation conversation)
    {
        try
        {
            if (!Plugin.Instance.ModEnabled) return true;
            if (!PrismaShardHandler.IsHuntGoal) return true;
            if (PrismaShardHandler.IsEncounterUnlocked) return true;

            string name = "?";
            try { name = conversation?.TryCast<FixedConversation>()?.GetDebugName() ?? "?"; }
            catch { /* name is for logging only */ }

            Logger.Info(
                $"[AP] Prismacore fight blocked — started from '{name}', " +
                $"Prisma Shards {PrismaShardHandler.Progress}");

            UI.ApPopup.ShowThrottled(
                "prismacore-gate", "The Prismacore is sealed",
                "Archipelago", $"Prisma Shards: {PrismaShardHandler.Progress}");

            return false;   // the conversation still plays; the fight simply does not begin
        }
        catch (System.Exception ex)
        {
            // Never let a gate failure block a fight the player is entitled to.
            Logger.Warning($"[AP] PrismacoreGatePatch threw: {ex.Message}");
            return true;
        }
    }
}
