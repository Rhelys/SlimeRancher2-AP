using System.Collections.Generic;
using SlimeRancher2AP.Data;
using SlimeRancher2AP.Utils;
using UnityEngine;

namespace SlimeRancher2AP.Archipelago;

/// <summary>
/// Polls PuzzleSlotLockable objects each Update tick to detect when plort doors open.
/// Handles:
///   1. Powderfall Bluffs region gate (when RegionAccessMode != "vanilla")
///   2. The 28 other plort doors (when RandomizePlortDoors == "locations")
///
/// Harmony patches on PuzzleSlotLockable.ActivateOnUnlock only fire for managed callers.
/// These doors are opened from native code, so the patch is bypassed. Polling
/// ShouldUnlock() is the only reliable approach.
///
/// Shadow plort doors (Grey Labyrinth, 819200-819224) are NOT tracked here — they have
/// a managed caller path and are handled by PuzzleSlotLockableActivatePatch.
///
/// Any door whose ShouldUnlock() is already true when polled (e.g. Boom Gordo door after
/// defeat, or a door opened in a prior session) immediately triggers a check if not yet
/// recorded in the save manager.
/// </summary>
public static class PlortDoorPoller
{
    // A door may map to more than one AP location (e.g. the PB gate is both a plort door
    // check and a region gate check when both settings are active).
    private record DoorEntry(PuzzleSlotLockable Psl, List<long> LocationIds);

    // posKey → tracked door entry. Built up across scans; not cleared on scene change
    // (scenes load additively so doors from previous sub-scenes stay valid).
    private static readonly Dictionary<string, DoorEntry> _tracked = new();
    // posKeys that have been fully processed (check sent or already saved). Scan skips
    // these so processed doors are never re-added after Poll removes them.
    private static readonly HashSet<string> _done = new();

    private static float _nextScan = 0f;
    private static float _nextPoll = 0f;

    private const float ScanInterval = 5f;
    private const float PollInterval = 1f;

    public static void Reset()
    {
        _tracked.Clear();
        _done.Clear();
        _nextScan = 0f;
        _nextPoll = 0f;
    }

    public static void Tick()
    {
        if (!Plugin.Instance.ModEnabled) return;
        float now = Time.time;

        if (now >= _nextScan)
        {
            _nextScan = now + ScanInterval;
            Scan();
        }

        if (!Plugin.Instance.ApClient.IsConnected) return;
        if (now < _nextPoll) return;
        _nextPoll = now + PollInterval;
        Poll();
    }

    private static void Scan()
    {
        var all = Resources.FindObjectsOfTypeAll<PuzzleSlotLockable>();
        if (all.Count == 0) return;

        var slotData       = Plugin.Instance.ApClient.SlotData;
        bool regionEnabled = (slotData?.RegionAccessMode ?? "vanilla") != "vanilla";
        bool plortEnabled  = (slotData?.RandomizePlortDoors ?? "vanilla") != "vanilla";

        foreach (var psl in all)
        {
            string posKey;
            try { posKey = WorldUtils.PositionKey(psl.gameObject); }
            catch { continue; }

            if (_done.Contains(posKey) || _tracked.ContainsKey(posKey)) continue;

            var ids = new List<long>();

            if (posKey == RegionTable.PBGatePosKey && regionEnabled)
                ids.Add(LocationConstants.RegionGate_PowderfallBluffs);

            if (plortEnabled
                && LocationTable.TryGetByObjectName(posKey, out var info)
                && info != null
                && info.Type == LocationType.PuzzleDoor)
                ids.Add(info.Id);

            if (ids.Count == 0) continue;

            // Already fully checked in a prior session — mark done immediately.
            if (ids.TrueForAll(id => Plugin.Instance.SaveManager.IsChecked(id)))
            {
                _done.Add(posKey);
                continue;
            }

            _tracked[posKey] = new DoorEntry(psl, ids);
#if DEBUG
            Logger.Info($"[AP-PlortDoorPoller] Tracking posKey='{posKey}' ids=[{string.Join(",", ids)}]");
#endif
        }
    }

    private static void Poll()
    {
        var completed = new List<string>();  // checks sent / already checked → never re-track
        var stale     = new List<string>();  // instance threw (collected on zone unload) → re-track on next Scan

        foreach (var (posKey, door) in _tracked)
        {
            if (door.LocationIds.TrueForAll(id => Plugin.Instance.SaveManager.IsChecked(id)))
            {
                completed.Add(posKey);
                continue;
            }

            bool shouldUnlock;
            try { shouldUnlock = door.Psl.ShouldUnlock(); }
            catch
            {
                // The tracked IL2CPP object was destroyed (typical after a zone unload).
                // The check has NOT been sent — drop only the stale reference so Scan()
                // re-tracks a live instance when the player returns to the zone.
                // Marking it _done here would make the check silently unobtainable
                // for the rest of the session.
                stale.Add(posKey);
                continue;
            }

            if (!shouldUnlock) continue;

            foreach (var id in door.LocationIds)
            {
                if (!Plugin.Instance.SaveManager.IsChecked(id))
                {
                    Plugin.Instance.ApClient.SendCheck(id);
                    Logger.Info($"[AP] Plort door check sent: posKey='{posKey}' id={id}");
                }
            }
            completed.Add(posKey);
        }

        foreach (var k in completed)
        {
            _tracked.Remove(k);
            _done.Add(k);
        }
        foreach (var k in stale)
            _tracked.Remove(k);
    }
}
