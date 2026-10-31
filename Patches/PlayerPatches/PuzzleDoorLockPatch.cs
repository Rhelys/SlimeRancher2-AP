using HarmonyLib;
using Il2CppMonomiPark.SlimeRancher;
using SlimeRancher2AP.Data;
using SlimeRancher2AP.Utils;

namespace SlimeRancher2AP.Patches.PlayerPatches;

/// <summary>
/// Shadow plort doors (Grey Labyrinth) send their check on <c>ActivateOnUnlock</c>, which they
/// reach through a managed caller. Other plort doors open from native code — see PlortDoorPoller.
/// Identified by posKey; door names repeat.
/// </summary>
[HarmonyPatch(typeof(PuzzleSlotLockable), "ActivateOnUnlock")]
internal static class PuzzleSlotLockableActivatePatch
{
    private static bool Prefix(PuzzleSlotLockable __instance)
    {
        if (!Plugin.Instance.ModEnabled) return true;
        if (SceneContext.Instance?.PlayerState?._model == null) return true;

        string posKey;
        try { posKey = WorldUtils.PositionKey(__instance.gameObject!); }
        catch { return true; }

#if DEBUG
        Logger.Info($"[AP-PuzzleDoor] ActivateOnUnlock: name='{__instance.gameObject?.name ?? "?"}'  posKey='{posKey}'");
#endif

        if (LocationTable.TryGetByObjectName(posKey, out var locInfo) && locInfo != null
            && locInfo.Type == LocationType.ShadowPlortDoor)
        {
            // Unconditional — randomize_shadow_doors gates only the check (see SlotData).
            SuppressVanillaReward(__instance);

            bool isCheck = string.Equals(
                Plugin.Instance.ApClient.SlotData?.RandomizeShadowDoors, "locations",
                System.StringComparison.OrdinalIgnoreCase);

            if (isCheck)
            {
                Plugin.Instance.ApClient.SendCheck(locInfo.Id);
                Logger.Info($"[AP] Shadow Plort Door check: '{locInfo.Name}' (id={locInfo.Id}) posKey='{posKey}'");
            }
            else
            {
                Logger.Info($"[AP] Shadow Plort Door opened (not a check): '{locInfo.Name}' posKey='{posKey}'");
            }
        }

        return true;
    }

    /// <summary>
    /// Nulls the door's <c>_rewardOnUnlock</c> rewarder fields, as for treasure pods; the door
    /// still opens.
    /// </summary>
    private static void SuppressVanillaReward(PuzzleSlotLockable door)
    {
        try
        {
            var rewarder = door._rewardOnUnlock;
            if (rewarder == null) return;
            rewarder.Blueprint                          = null;
            rewarder.UpgradeComponent                   = null;
            rewarder.SpawnObjs                          = null;
            rewarder.UnlockedSlimeAppearance            = null;
            rewarder.UnlockedSlimeAppearanceDefinition  = null;
            Logger.Info("[AP] Shadow Plort Door vanilla reward suppressed");
        }
        catch (System.Exception ex)
        {
            Logger.Warning($"[AP] Shadow Plort Door reward suppression failed: {ex.Message}");
        }
    }
}

// Don't patch PuzzleDoorLock, PuzzleGateActivator, or PuzzleSlotLockable's Notify /
// SendAnalytics: they crashed the trampoline after the 5/13/2026 update. Nor PlortDepositor:
//   • ActivateOnFill — infinite recursion through the managed bridge.
//   • PlortDepositorModel.Push — called natively during scene init; the trampoline fails.
//   • OnTriggerEnter — trampoline crash on scene load since the 5/13/2026 update.
// Plort doors other than the shadow doors, and the PB gate, are PlortDoorPoller's.
