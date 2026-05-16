namespace SlimeRancher2AP.Patches.LocationPatches;

// NOTE (2026-05-15, v0.4.4): PlortDepositorPatch removed.
//
// Previous hook history for Shadow Plort door detection:
//   1. ActivateOnFill()        — stack overflow: HarmonyX il2cpp_runtime_invoke re-enters the
//                                patched managed bridge wrapper, causing infinite recursion.
//   2. PlortDepositorModel.Push — AccessViolationException on game load: Push fires during save
//                                 restoration before SetGameObject is called on the model.
//   3. PlortDepositor.OnTriggerEnter — REMOVED here: PlortDepositor moved to root namespace in
//                                      the 5/13/2026 game update. OnTriggerEnter is a Unity physics
//                                      callback (CallerCount=0 managed callers) whose prologue
//                                      changed, crashing the HarmonyX trampoline immediately on
//                                      scene load as the Conservatory's refinery PlortDepositors
//                                      fire their physics triggers during initialization.
//
// Shadow Plort door detection is now handled in PuzzleSlotLockableActivatePatch
// (PuzzleSlotLockable.ActivateOnUnlock, CallerCount=1, already patched for the PB gate).
// ActivateOnUnlock fires as the final step of the PuzzleSlotLockable unlock chain — after
// the plort is accepted (OnTriggerEnter) → ActivateOnFill → NotifySlotChanged → ActivateOnUnlock.
// Detection uses posKey via WorldUtils.PositionKey() exactly as the old OnTriggerEnter patch did.
