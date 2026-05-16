namespace SlimeRancher2AP.Patches.PlayerPatches;

// NOTE (2026-05-15, v0.4.4): SlimeGateActivatorPatch and AccessDoorOpenPatch were removed.
//
// SlimeGateActivator.Activate() and AccessDoor.ForceUpdate() both had their native method
// prologues changed in the 5/13/2026 game update, causing HarmonyX trampoline crashes.
// SlimeGateActivatorPatch was diagnostic-only (logging for future PB-gate posKey discovery).
// AccessDoorOpenPatch was also diagnostic-only (logging AccessDoor state restoration).
// Neither was wired up to send any AP location checks.
//
// AccessDoor.ForceUpdate in particular crashes immediately on scene load, making it the
// highest-priority patch to remove.
//
// The Powderfall Bluffs gate check is fully handled by PuzzleSlotLockableActivatePatch
// in PuzzleDoorLockPatch.cs (patches PuzzleSlotLockable.ActivateOnUnlock — unchanged).
