using HarmonyLib;
using Il2CppMonomiPark.SlimeRancher;
using SlimeRancher2AP.Archipelago;

namespace SlimeRancher2AP.Patches.PlayerPatches;

/// <summary>Forwards Prismacore room states to GoalHandler (POST_FIGHT wins).</summary>
[HarmonyPatch(typeof(CoreRoomController), nameof(CoreRoomController.UpdateState))]
internal static class CoreRoomControllerPatch
{
    private static void Postfix(CoreRoomController.CoreRoomState state)
    {
#if DEBUG
        SlimeRancher2AP.Utils.DebugTrace.Once("CoreRoomControllerPatch.Postfix — first entry");
#endif
        if (!Plugin.Instance.ModEnabled) return;
        GoalHandler.OnCoreRoomStateChanged(state);
    }
}
