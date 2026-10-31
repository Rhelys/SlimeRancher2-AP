using HarmonyLib;
using System;
using System.Collections.Generic;
using UnityEngine;

namespace SlimeRancher2AP.Patches.LocationPatches;

/// <summary>
/// <c>gold_lucky_spawn_rate_multiplier</c>: multiplies Gold/Lucky <c>SlimeSet.Member.Weight</c>
/// (relative within the set; vanilla ~1–5 against 50–200+ for commons) on each zone's spawners.
/// Vanilla weights are cached, so a reconnect never multiplies twice.
/// </summary>
internal static class GoldLuckySpawnRatePatch
{
    private static bool _applied = false;

    // Vanilla weights by (instanceId, constraintIndex, memberIndex).
    private static readonly Dictionary<(int, int, int), float> _origWeights = new();

    // Zone changes stand in for DirectedActorSpawner.Start, which can't be patched since 5/13.
    private static string? _lastZoneRef = null;

    // Bounded retry window after each zone change.
    private const  float RetrySeconds       = 1f;
    private const  float RetryWindowSeconds = 60f;
    private static float _nextRetry  = 0f;
    private static float _retryUntil = 0f;

    private static readonly string[] TargetKeywords = { "Gold", "Lucky" };

    /// <summary>Per-frame; scales each new zone's Gold/Lucky members.</summary>
    internal static void TryApplyIfNeeded()
    {
        if (!Plugin.Instance.ModEnabled) return;

        string? currentZone = SlimeRancher2AP.Archipelago.TrapHandler.CurrentZoneRef;

        if (currentZone != _lastZoneRef)
        {
            _lastZoneRef  = currentZone;
            _applied      = false;
            _retryUntil   = Time.unscaledTime + RetryWindowSeconds;
            _nextRetry    = 0f;
        }

        if (_applied) return;

        // Spawners stream in after the zone, so retry once a second — but only for a bounded
        // window: many zones (the ranch) have nothing to scale, and an unbounded scan was the
        // mod's most expensive per-frame cost.
        float now = Time.unscaledTime;
        if (now > _retryUntil) return;
        if (now < _nextRetry) return;
        _nextRetry = now + RetrySeconds;

        var slotData = Plugin.Instance.ApClient?.SlotData;
        if (slotData == null) return;

        int multiplier = slotData.GoldLuckySpawnRateMultiplier;
        if (multiplier <= 1) { _applied = true; return; }

        var spawners = Resources.FindObjectsOfTypeAll<DirectedActorSpawner>();
        if (spawners.Count == 0) return;

        int scaled = 0;
        foreach (var spawner in spawners)
        {
            if (spawner == null) continue;
            int id = spawner.GetInstanceID();
            var constraints = spawner.Constraints;
            if (constraints == null) continue;

            for (int ci = 0; ci < constraints.Length; ci++)
            {
                var constraint = constraints[ci];
                if (constraint?.Slimeset?.Members == null) continue;
                var members = constraint.Slimeset.Members;

                for (int mi = 0; mi < members.Length; mi++)
                {
                    var member = members[mi];
                    if (member == null) continue;

                    string identName = member.IdentType?.name ?? "";
                    bool isTarget = false;
                    foreach (var kw in TargetKeywords)
                        if (identName.IndexOf(kw, StringComparison.OrdinalIgnoreCase) >= 0)
                        { isTarget = true; break; }
                    if (!isTarget) continue;

                    var key = (id, ci, mi);
                    if (!_origWeights.ContainsKey(key))
                        _origWeights[key] = member.Weight;

                    member.Weight = _origWeights[key] * multiplier;
                    scaled++;
                }
            }
        }

        if (scaled > 0)
        {
            Logger.Info(
                $"[AP-GoldLucky] Applied {multiplier}× weight multiplier to {scaled} " +
                $"Gold/Lucky member(s) across {spawners.Count} spawner(s)");
            _applied = true;
        }
        // Nothing scaled: keep retrying until the window closes.
    }

    /// <summary>On disconnect: restores vanilla weights on live spawners.</summary>
    internal static void OnDisconnected()
    {
        if (_origWeights.Count > 0)
        {
            var spawners = Resources.FindObjectsOfTypeAll<DirectedActorSpawner>();
            foreach (var spawner in spawners)
            {
                if (spawner == null) continue;
                int id = spawner.GetInstanceID();
                var constraints = spawner.Constraints;
                if (constraints == null) continue;

                for (int ci = 0; ci < constraints.Length; ci++)
                {
                    var constraint = constraints[ci];
                    if (constraint?.Slimeset?.Members == null) continue;
                    var members = constraint.Slimeset.Members;

                    for (int mi = 0; mi < members.Length; mi++)
                    {
                        var member = members[mi];
                        if (member == null) continue;
                        var key = (id, ci, mi);
                        if (_origWeights.TryGetValue(key, out float orig))
                            member.Weight = orig;
                    }
                }
            }
        }

        _origWeights.Clear();
        _applied = false;
        _lastZoneRef = null;
    }
}
