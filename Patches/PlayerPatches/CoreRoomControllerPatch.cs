using HarmonyLib;
using Il2CppMonomiPark.SlimeRancher;
using SlimeRancher2AP.Archipelago;

namespace SlimeRancher2AP.Patches.PlayerPatches;

/// <summary>
/// Detects Prismacore room state transitions and notifies GoalHandler.
/// Drives the "prismacore" goal: it fires on POST_FIGHT (boss complete, core stabilized).
/// PRE_FIGHT fires on scene load and is ignored by GoalHandler.
/// </summary>
[HarmonyPatch(typeof(CoreRoomController), nameof(CoreRoomController.UpdateState))]
internal static class CoreRoomControllerPatch
{
    // The Harmony-generated parameter name for the first argument of UpdateState is "state"
    // and the second is "immediate" (bool). Only state matters for goal detection.
    private static void Postfix(CoreRoomController.CoreRoomState state)
    {
#if DEBUG
        SlimeRancher2AP.Utils.DebugTrace.Once("CoreRoomControllerPatch.Postfix — first entry");
#endif
        if (!Plugin.Instance.ModEnabled) return;
        GoalHandler.OnCoreRoomStateChanged(state);
    }
}
