using HarmonyLib;
using Il2CppMonomiPark.SlimeRancher;
using Il2CppMonomiPark.World;
using SlimeRancher2AP.Archipelago;

namespace SlimeRancher2AP.Patches.PlayerPatches;

// ─────────────────────────────────────────────────────────────────────────────
// AccessDoorOpenPatch — diagnostic only
//
// Logs AccessDoor.ForceUpdate calls so we can observe state transitions if
// there are any. Not used for goal detection.
//
// AccessDoor.ForceUpdate() is called during scene state restoration to visually
// restore previously-opened access doors (gordo rewards, etc.).  Accessing
// __instance._model without a guard can cause a native IL2CPP crash if the
// AccessDoor object is partially initialised at that point.
// ─────────────────────────────────────────────────────────────────────────────
[HarmonyPatch(typeof(AccessDoor), nameof(AccessDoor.ForceUpdate))]
internal static class AccessDoorOpenPatch
{
    private static readonly System.Collections.Generic.HashSet<int> _reportedOpenDoors = new();

    private static void Postfix(AccessDoor __instance)
    {
#if DEBUG
        SlimeRancher2AP.Utils.DebugTrace.Once("AccessDoorOpenPatch.Postfix — first entry");
#endif
        if (!Plugin.Instance.ModEnabled) return;

        // Guard: skip during scene state restoration — same pattern used by all location patches.
        // AccessDoor.ForceUpdate() fires during loading; _model on a partially-initialised
        // AccessDoor may hold a non-null garbage native pointer, causing a native crash when
        // dereferenced.  PlayerState._model becomes non-null only after save data is fully applied.
        if (SceneContext.Instance?.PlayerState?._model == null) return;

        AccessDoor.State? doorState;
        try { doorState = __instance._model?.state; }
        catch { return; }
        if (doorState != AccessDoor.State.OPEN) return;

        int id;
        try { id = __instance.GetInstanceID(); }
        catch { return; }
        if (!_reportedOpenDoors.Add(id)) return;

        string doorName, sceneName;
        try
        {
            doorName  = __instance.gameObject.name;
            var scene = __instance.gameObject.scene;
            sceneName = scene.IsValid() ? (scene.name ?? "") : "";
        }
        catch { return; }

        Plugin.Instance.Log.LogInfo(
            $"[AP-Gate] AccessDoor OPEN: name='{doorName}'  scene='{sceneName}'");
    }

    internal static void Reset() => _reportedOpenDoors.Clear();
}

// EnergyBeamReceiverPatch is intentionally removed.
// The gate mechanism is fully handled by InvisibleSwitchPatch in RegionGatePatch.cs,
// which patches WorldStateInvisibleSwitch.SetStateForAll(DOWN).
