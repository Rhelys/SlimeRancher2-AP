using HarmonyLib;
using Il2CppMonomiPark.SlimeRancher;
using Il2CppMonomiPark.World;
using SlimeRancher2AP.Archipelago;
using SlimeRancher2AP.Data;
using SlimeRancher2AP.Utils;

namespace SlimeRancher2AP.Patches.PlayerPatches;

// ─────────────────────────────────────────────────────────────────────────────
// SlimeGateActivatorPatch
//
// Patches SlimeGateActivator.Activate() — fires when the player completes the
// purchase interaction at a Slime Gate (e.g. the Powderfall Bluffs access door).
// Used to:
//   1. Log gate identity (name / scene / posKey) to identify which AccessDoor
//      corresponds to the PB gate.
//   2. (Future) Block the gate open and send the AP location check.
//
// SlimeGateActivator.GateDoor is the AccessDoor that actually opens.
// ─────────────────────────────────────────────────────────────────────────────
[HarmonyPatch(typeof(SlimeGateActivator), nameof(SlimeGateActivator.Activate))]
internal static class SlimeGateActivatorPatch
{
    private static void Postfix(SlimeGateActivator __instance)
    {
#if DEBUG
        DebugTrace.Once("SlimeGateActivatorPatch.Postfix — first entry");
#endif
        if (!Plugin.Instance.ModEnabled) return;

        string activatorName, gateDoorName, sceneName, posKey;
        try
        {
            activatorName = __instance.gameObject?.name ?? "null";
            gateDoorName  = __instance.GateDoor?.gameObject?.name ?? "null";
            var scene     = __instance.gameObject!.scene;
            sceneName     = scene.IsValid() ? (scene.name ?? "") : "?";
            posKey        = WorldUtils.PositionKey(__instance.gameObject);
        }
        catch { return; }

        Logger.Info(
            $"[AP-SlimeGate] Activate: activator='{activatorName}' gateDoor='{gateDoorName}' " +
            $"scene='{sceneName}' posKey='{posKey}'");

        // Guard: only process during an active session with save loaded.
        if (!Plugin.Instance.SaveManager.HasActiveSession) return;
        if (SceneContext.Instance?.PlayerState?._model == null) return;

        // TODO: when posKey matches the PB gate, send the AP check.
        // Example (fill in the actual posKey after first in-game log run):
        //   if (posKey == "<PB_GATE_POSKEY>")
        //       Plugin.Instance.ApClient.SendCheck(LocationConstants.RegionGate_PowderfallBluffs);
    }
}

// ─────────────────────────────────────────────────────────────────────────────
// AccessDoorOpenPatch — diagnostic only
//
// Logs AccessDoor.ForceUpdate calls so we can observe state transitions during
// scene state restoration. Not used for real-time gate detection.
//
// AccessDoor.ForceUpdate() fires during loading to visually restore previously-
// opened access doors (gordo rewards, etc.).  Accessing __instance._model without
// a guard can cause a native IL2CPP crash if the object is partially initialised.
// ─────────────────────────────────────────────────────────────────────────────
[HarmonyPatch(typeof(AccessDoor), nameof(AccessDoor.ForceUpdate),
    new System.Type[] { typeof(bool) })]
internal static class AccessDoorOpenPatch
{
    private static readonly System.Collections.Generic.HashSet<int> _reportedOpenDoors = new();

    private static void Postfix(AccessDoor __instance, bool immediate)
    {
#if DEBUG
        DebugTrace.Once("AccessDoorOpenPatch.Postfix — first entry");
#endif
        if (!Plugin.Instance.ModEnabled) return;

        // Guard: skip during scene state restoration.
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

        Logger.Info(
            $"[AP-Gate] AccessDoor OPEN (restore): name='{doorName}'  scene='{sceneName}'");
    }

    internal static void Reset() => _reportedOpenDoors.Clear();
}

// EnergyBeamReceiverPatch is intentionally removed.
// The gate mechanism is fully handled by InvisibleSwitchPatch in RegionGatePatch.cs,
// which patches WorldStateInvisibleSwitch.SetStateForAll(DOWN).
