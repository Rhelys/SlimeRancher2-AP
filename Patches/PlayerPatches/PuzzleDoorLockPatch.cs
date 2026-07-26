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
            SuppressVanillaReward(__instance);
            Plugin.Instance.ApClient.SendCheck(locInfo.Id);
            Logger.Info($"[AP] Shadow Plort Door check: '{locInfo.Name}' (id={locInfo.Id}) posKey='{posKey}'");
        }

        return true;
    }

    /// <summary>
    /// Shadow plort doors carry a <c>TreasurePodRewarder</c> (<c>_rewardOnUnlock</c>) that
    /// dispenses a vanilla reward (blueprint / upgrade component / spawned items) when the
    /// door opens. The door IS the AP check, so — exactly like treasure pods
    /// (TreasurePodPatch) — the vanilla grant is suppressed by nulling the reward fields
    /// before the original ActivateOnUnlock runs; the open animation and FX still play,
    /// and the AP server delivers the randomized item instead. (Player-reported: doors
    /// were double-dipping, granting both the check and the vanilla reward.)
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

// NOTE (2026-05-15, v0.4.4): PuzzleDoorLockPatch, PuzzleGateActivatorPatch,
// PuzzleSlotLockableNotifyPatch, and PuzzleSlotLockableSendAnalyticsPatch were removed.
// These classes (PuzzleDoorLock, PuzzleGateActivator, PuzzleSlotLockable) moved to the
// root namespace in the 5/13/2026 game update and their native method prologues changed,
// causing HarmonyX trampoline crashes. The labyrinth_open goal is fully detected by
// InvisibleSwitchPatch (WorldStateInvisibleSwitch.SetStateForAll) in RegionGatePatch.cs.
