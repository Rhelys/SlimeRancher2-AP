using HarmonyLib;
using Il2CppMonomiPark.SlimeRancher;
using SlimeRancher2AP.Data;
using SlimeRancher2AP.Utils;

namespace SlimeRancher2AP.Patches.PlayerPatches;

/// <summary>
/// Postfix on PuzzleSlotLockable.ActivateOnUnlock — fires via the managed caller path when
/// a Shadow Plort door (Grey Labyrinth) opens. Sends the AP location check.
///
/// Shadow plort doors have a managed caller that reaches ActivateOnUnlock, so this patch
/// fires reliably for them. Other plort doors (PuzzleDoor type) and the PB region gate are
/// called from native code and bypass this patch — those are handled by PlortDoorPoller
/// (ShouldUnlock() polling from the Update loop).
///
/// Uses posKey (sceneName_X_Y_Z) for identification because multiple doors share the same
/// objectName (e.g. "objLabyrinthPlortDoor01Small").
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
            Plugin.Instance.ApClient.SendCheck(locInfo.Id);
            Logger.Info($"[AP] Shadow Plort Door check: '{locInfo.Name}' (id={locInfo.Id}) posKey='{posKey}'");
        }

        return true;
    }
}

// NOTE (2026-05-15, v0.4.4): PuzzleDoorLockPatch, PuzzleGateActivatorPatch,
// PuzzleSlotLockableNotifyPatch, and PuzzleSlotLockableSendAnalyticsPatch were removed.
// These classes (PuzzleDoorLock, PuzzleGateActivator, PuzzleSlotLockable) moved to the
// root namespace in the 5/13/2026 game update and their native method prologues changed,
// causing HarmonyX trampoline crashes. The labyrinth_open goal is fully detected by
// InvisibleSwitchPatch (WorldStateInvisibleSwitch.SetStateForAll) in RegionGatePatch.cs.
