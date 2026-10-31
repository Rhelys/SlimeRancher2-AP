using Il2CppMonomiPark.SlimeRancher.Drone;
using System.Collections.Generic;
using UnityEngine;

namespace SlimeRancher2AP.Patches.LocationPatches;

/// <summary>
/// Spawns uncollected ghostly drones directly. Their vanilla spawn query waits on a WorldSwitch
/// that AP gate blocking can keep UP forever. (HandleSpawnQueryComplete can't be patched since
/// 5/13.) Retried from ApUpdateBehaviour after each scene change.
/// </summary>
internal static class ComponentAcqDroneSpawnerFix
{
    // SpawnDrone has no dedup — calling it twice spawns a duplicate. Cleared per scene.
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
    /// <summary>Debug panel: no session needed.</summary>
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
