using HarmonyLib;
using SlimeRancher2AP.Archipelago;

namespace SlimeRancher2AP.Patches.PlayerPatches;

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
