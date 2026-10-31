using HarmonyLib;
using Il2CppMonomiPark.SlimeRancher.Dialogue.CommStation;
using Il2CppMonomiPark.SlimeRancher.World;
using SlimeRancher2AP.Archipelago;

namespace SlimeRancher2AP.Patches.PlayerPatches;

/// <summary>
/// <c>prisma_shard_hunt</c>: the Prismacore fight won't start without the shards. Gates
/// <c>TryToStartFight</c>, which every route in shares — never the conversation: refusing it
/// opens an empty dialogue box that softlocks the game.
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
            // Fail open.
            Logger.Warning($"[AP] PrismacoreGatePatch threw: {ex.Message}");
            return true;
        }
    }
}
