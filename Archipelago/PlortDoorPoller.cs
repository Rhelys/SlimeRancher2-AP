using System.Collections.Generic;
using SlimeRancher2AP.Data;
using SlimeRancher2AP.Utils;
using UnityEngine;

namespace SlimeRancher2AP.Archipelago;

/// <summary>
/// Polls <c>ShouldUnlock()</c> on the plort doors that open from native code, past the
/// ActivateOnUnlock patch: the PB region gate (non-vanilla modes) and the 28 plort doors
/// (<c>randomize_plort_doors</c>). A door already open when seen sends any unsent check.
/// Shadow doors are PuzzleSlotLockableActivatePatch's.
/// </summary>
public static class PlortDoorPoller
{
    // A door can be several locations (the PB gate: plort door and region gate).
    private record DoorEntry(PuzzleSlotLockable Psl, List<long> LocationIds);

    // posKey → door. Kept across scene changes (scenes load additively).
    private static readonly Dictionary<string, DoorEntry> _tracked = new();
    // Fully processed doors, never re-tracked.
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
                // Destroyed with its zone, check unsent: drop the reference so Scan re-tracks
                // it — never mark it done.
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
