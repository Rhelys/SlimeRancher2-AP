using Il2CppMonomiPark.SlimeRancher.Drone;
using System.Collections.Generic;
using UnityEngine;

namespace SlimeRancher2AP.Patches.LocationPatches;

// NOTE (2026-05-16, v0.4.4): Harmony patch on ComponentAcqDroneSpawner.HandleSpawnQueryComplete removed.
//
// HandleSpawnQueryComplete(bool) has CallerCount(0). Its native prologue changed in the
// 5/13/2026 game update, causing a HarmonyX trampoline crash on zone load.
//
// REPLACEMENT (2026-06-13): ApUpdateBehaviour retries ForceSpawnAll() every 3 seconds for
// up to 60 seconds after each scene change, because SR2's additive sub-scenes (zoneConservatory,
// zoneFields_Area1, etc.) finish loading several seconds after SceneContext.Player becomes available.
// SpawnDrone() has CallerCount(4) and is safe to invoke, but has no built-in dedup guard —
// calling it twice on the same spawner creates a duplicate drone. We track instance IDs in
// _alreadySpawned to ensure each spawner is called exactly once per scene load.

/// <summary>
/// Forces all uncollected ghostly drone spawners in the current scene to spawn their drones
/// immediately, bypassing the vanilla spawn query (which waits for a WorldSwitch to be DOWN —
/// a condition that AP region-gate blocking prevents from ever being met).
/// </summary>
internal static class ComponentAcqDroneSpawnerFix
{
    // Instance IDs of spawners we have already called SpawnDrone() on this scene load.
    // Cleared by ApUpdateBehaviour on every scene change.
    private static readonly HashSet<int> _alreadySpawned = new();

    internal static void ClearSpawnedSet() => _alreadySpawned.Clear();

    internal static void ForceSpawnAll()
    {
        if (!Plugin.Instance.ModEnabled) return;
        if (!Plugin.Instance.SaveManager.HasActiveSession) return;
        if (Plugin.Instance.ApClient.SlotData?.RandomizeGhostlyDrones != true) return;
        ForceSpawnAllUnchecked();
    }

#if DEBUG
    /// <summary>Guard-free version for the debug panel — works without an active AP session.</summary>
    internal static void ForceSpawnAllDebug() => ForceSpawnAllUnchecked();
#endif

    private static void ForceSpawnAllUnchecked()
    {
        var spawners = Resources.FindObjectsOfTypeAll<ComponentAcqDroneSpawner>();
        int count = 0;
        foreach (var spawner in spawners)
        {
            try
            {
                if (spawner == null) continue;
                var pod = spawner._treasurePod;
                if (pod == null) continue;
                if (pod.CurrState != TreasurePod.State.LOCKED) continue; // already collected

                int id = spawner.gameObject.GetInstanceID();
                if (_alreadySpawned.Contains(id)) continue; // already spawned this session

                spawner.SpawnDrone();
                _alreadySpawned.Add(id);
                count++;
            }
            catch (Exception ex)
            {
                Logger.Warning($"[AP] GhostDroneSpawn: exception on spawner: {ex.Message}");
            }
        }

        if (count > 0)
            Logger.Info($"[AP] GhostDroneSpawn: called SpawnDrone() on {count} new uncollected ghost drone spawner(s) (total this scene: {_alreadySpawned.Count}).");
    }
}
