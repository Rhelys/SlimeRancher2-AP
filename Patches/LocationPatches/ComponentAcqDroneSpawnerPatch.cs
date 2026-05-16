namespace SlimeRancher2AP.Patches.LocationPatches;

// NOTE (2026-05-16, v0.4.4): ComponentAcqDroneSpawnerPatch removed.
//
// ComponentAcqDroneSpawner.HandleSpawnQueryComplete(bool) has CallerCount(0).
// Its native prologue changed in the 5/13/2026 game update, causing a HarmonyX trampoline
// crash on zone load (fires during scene initialization when spawn queries resolve).
//
// Effect of removal: Ghostly drone pod nodes will only appear when their vanilla story
// conditions are met, rather than being forced visible from session start.
//
// A safe replacement is needed to restore forced visibility without Harmony:
//   • Direct field manipulation on ComponentAcqDroneSpawner to mark the query satisfied.
//   • Update-loop polling to call a higher-CallerCount method that triggers the spawn.
//   • Investigate ComponentAcqDroneSpawner fields/properties for a direct spawn flag.
