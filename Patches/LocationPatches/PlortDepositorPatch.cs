namespace SlimeRancher2AP.Patches.LocationPatches;

// NOTE (2026-05-15, v0.4.4): PlortDepositorPatch removed.
//
// Previous hook history for Shadow Plort door detection:
//   1. ActivateOnFill()        — stack overflow: HarmonyX il2cpp_runtime_invoke re-enters the
//                                patched managed bridge wrapper, causing infinite recursion.
//   2. PlortDepositorModel.Push — crashes on main menu load: Push is invoked from native code
//                                 during scene initialisation (CallerCount=7 counts only managed
//                                 callers; native callers are not counted).  The HarmonyX trampoline
//                                 fails in the same way OnTriggerEnter did.
//   3. PlortDepositor.OnTriggerEnter — REMOVED here: PlortDepositor moved to root namespace in
//                                      the 5/13/2026 game update. OnTriggerEnter is a Unity physics
//                                      callback (CallerCount=0 managed callers) whose prologue
//                                      changed, crashing the HarmonyX trampoline immediately on
//                                      scene load as the Conservatory's refinery PlortDepositors
//                                      fire their physics triggers during initialization.
//
// Shadow Plort door detection is handled in PuzzleSlotLockableActivatePatch
// (PuzzleSlotLockable.ActivateOnUnlock) in PuzzleDoorLockPatch.cs.
//
// Powderfall Bluffs region gate detection: the PB gate IS a PuzzleSlotLockable
// (posKey confirmed via PlortDoorPoller debug dump 2026-06-21 — see RegionTable),
// but it is opened from native code, so the ActivateOnUnlock patch never fires
// for it. Detection is handled via ShouldUnlock() polling in PlortDoorPoller.
