using HarmonyLib;
using Il2CppMonomiPark.SlimeRancher;
using SlimeRancher2AP.Archipelago;
using SlimeRancher2AP.Data;
using SlimeRancher2AP.Utils;
using UnityEngine;

namespace SlimeRancher2AP.Patches.PlayerPatches;

/// <summary>
/// Prefix on PuzzleSlotLockable.ActivateOnUnlock — the single point where any plort door
/// transitions to its open state. Handles two cases:
///
///   1. Powderfall Bluffs region gate (posKey = "zoneGorge_Area3_-645_34_681"):
///      • Sends the AP location check for RegionGate_PowderfallBluffs (idempotent).
///      • Blocks the open if "Powderfall Bluffs Access" has not been received yet.
///      • When the access item arrives, ApplyRegionAccess calls ActivateOnUnlock directly,
///        which passes through here with IsRegionUnlocked = true.
///
///   2. Other puzzle doors (PuzzleDoor entries in LocationTable):
///      • Sends the AP location check if RandomizePuzzleDoors is enabled.
///      • Always allows the door to open (no blocking).
///
/// Uses posKey (sceneName_X_Y_Z) rather than objectName for identification, because
/// multiple doors share the same objectName (e.g. "objLabyrinthPlortDoor01Small").
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

        // ── Case 1: Powderfall Bluffs region gate ────────────────────────────────
        if (posKey == RegionTable.PBGatePosKey)
        {
            var mode = Plugin.Instance.ApClient.SlotData?.RegionAccessMode ?? "vanilla";
            if (mode == "vanilla") return true;

            Plugin.Instance.ApClient.SendCheck(LocationConstants.RegionGate_PowderfallBluffs);

            bool unlocked = Plugin.Instance.SaveManager.IsRegionUnlocked(RegionTable.PBRegionItemName);
            if (!unlocked)
            {
                Plugin.Instance.Log.LogInfo(
                    $"[AP] Blocked PB Slime Door — '{RegionTable.PBRegionItemName}' not yet received.");
                return false;
            }

            Plugin.Instance.Log.LogInfo("[AP] PB Slime Door — access confirmed, opening.");
            return true;
        }

        // ── Case 2: Other puzzle doors ────────────────────────────────────────────
        if (Plugin.Instance.ApClient.SlotData?.RandomizePuzzleDoors == true)
        {
            if (LocationTable.TryGetByObjectName(posKey, out var locInfo)
                && locInfo!.Type == LocationType.PuzzleDoor)
            {
                Plugin.Instance.ApClient.SendCheck(locInfo.Id);
                Plugin.Instance.Log.LogInfo(
                    $"[AP] Puzzle Door check: '{locInfo.Name}' (id={locInfo.Id}) posKey='{posKey}'");
            }
        }

        return true;
    }
}

/// <summary>
/// Forwards PuzzleDoorLock unlock events to GoalHandler for labyrinth_open detection.
/// Accesses scene/object names defensively to avoid IL2CPP crashes during scene loading.
/// </summary>
[HarmonyPatch(typeof(PuzzleDoorLock), nameof(PuzzleDoorLock.NotifySlotChanged))]
internal static class PuzzleDoorLockPatch
{
    private static void Postfix(PuzzleDoorLock __instance)
    {
#if DEBUG
        SlimeRancher2AP.Utils.DebugTrace.Once("PuzzleDoorLockPatch.Postfix — first entry");
#endif
        if (!Plugin.Instance.ModEnabled) return;

        bool shouldUnlock = false;
        try { shouldUnlock = __instance.ShouldUnlock(); } catch { return; }
        if (!shouldUnlock) return;

        string objectName, sceneName;
        try
        {
            objectName = __instance.gameObject.name;
            var scene  = __instance.gameObject.scene;
            sceneName  = scene.IsValid() ? (scene.name ?? "") : "";
        }
        catch { return; }

        Plugin.Instance.Log.LogInfo(
            $"[AP-Gate] PuzzleDoorLock unlocked: name='{objectName}'  scene='{sceneName}'");

        GoalHandler.OnSwitchOpened(objectName, sceneName);
    }
}

/// <summary>
/// Forwards PuzzleGateActivator activation events to GoalHandler for labyrinth_open detection.
/// Accesses scene/object names defensively to avoid IL2CPP crashes during scene loading.
/// </summary>
[HarmonyPatch(typeof(PuzzleGateActivator), nameof(PuzzleGateActivator.TryToActivate))]
internal static class PuzzleGateActivatorPatch
{
    private static void Postfix(PuzzleGateActivator __instance)
    {
#if DEBUG
        SlimeRancher2AP.Utils.DebugTrace.Once("PuzzleGateActivatorPatch.Postfix — first entry");
#endif
        if (!Plugin.Instance.ModEnabled) return;

        string objectName, sceneName;
        try
        {
            objectName = __instance.gameObject.name;
            var scene  = __instance.gameObject.scene;
            sceneName  = scene.IsValid() ? (scene.name ?? "") : "";
        }
        catch { return; }

        Plugin.Instance.Log.LogInfo(
            $"[AP-Gate] PuzzleGateActivator activated: name='{objectName}'  scene='{sceneName}'");

        GoalHandler.OnSwitchOpened(objectName, sceneName);
    }
}

/// <summary>
/// Catches PuzzleSlotLockable (base class) NotifySlotChanged — covers puzzle doors that are
/// plain PuzzleSlotLockable instances rather than PuzzleDoorLock subclass instances
/// (e.g. the Powderfall Bluffs slime door). Same ShouldUnlock() guard as PuzzleDoorLockPatch.
/// </summary>
[HarmonyPatch(typeof(PuzzleSlotLockable), nameof(PuzzleSlotLockable.NotifySlotChanged))]
internal static class PuzzleSlotLockableNotifyPatch
{
    private static void Postfix(PuzzleSlotLockable __instance)
    {
#if DEBUG
        DebugTrace.Once("PuzzleSlotLockableNotifyPatch.Postfix — first entry");
#endif
        if (!Plugin.Instance.ModEnabled) return;

        bool shouldUnlock = false;
        try { shouldUnlock = __instance.ShouldUnlock(); } catch { return; }
        if (!shouldUnlock) return;

        string objectName, sceneName, posKey;
        try
        {
            objectName = __instance.gameObject?.name ?? "null";
            var scene  = __instance.gameObject!.scene;
            sceneName  = scene.IsValid() ? (scene.name ?? "") : "";
            posKey     = WorldUtils.PositionKey(__instance.gameObject!);
        }
        catch { return; }

        Plugin.Instance.Log.LogInfo(
            $"[AP-PuzzleDoor] NotifySlotChanged (unlocked): name='{objectName}' " +
            $"scene='{sceneName}' posKey='{posKey}'");

        GoalHandler.OnSwitchOpened(objectName, sceneName);
    }
}

/// <summary>
/// Backstop: catches the analytics event that fires whenever any PuzzleSlotLockable
/// fully unlocks (PuzzleLockOpened_V1). Logs lockTag + posKey for identification.
/// This fires even if ActivateOnUnlock / NotifySlotChanged patches miss the door.
/// </summary>
[HarmonyPatch(typeof(PuzzleSlotLockable), "SendAnalyticsEvents")]
internal static class PuzzleSlotLockableSendAnalyticsPatch
{
    private static void Postfix(PuzzleSlotLockable __instance, string lockTag)
    {
#if DEBUG
        DebugTrace.Once("PuzzleSlotLockableSendAnalyticsPatch.Postfix — first entry");
#endif
        if (!Plugin.Instance.ModEnabled) return;

        string objectName, sceneName, posKey;
        try
        {
            objectName = __instance.gameObject?.name ?? "null";
            var scene  = __instance.gameObject!.scene;
            sceneName  = scene.IsValid() ? (scene.name ?? "") : "";
            posKey     = WorldUtils.PositionKey(__instance.gameObject!);
        }
        catch { return; }

        Plugin.Instance.Log.LogInfo(
            $"[AP-PuzzleDoor] SendAnalytics (CONFIRMED UNLOCK): lockTag='{lockTag}' " +
            $"name='{objectName}' scene='{sceneName}' posKey='{posKey}'");
    }
}
