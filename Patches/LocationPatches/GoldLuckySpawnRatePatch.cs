using HarmonyLib;
using System;
using System.Collections.Generic;
using UnityEngine;

namespace SlimeRancher2AP.Patches.LocationPatches;

/// <summary>
/// Scales Gold and Lucky slime spawn frequency based on the
/// <c>gold_lucky_spawn_rate_multiplier</c> slot data option.
///
/// <para>
/// SR2 uses a weight-based system for rare slime spawns: each <c>DirectedActorSpawner</c>
/// contains <c>SpawnConstraint</c> entries, each of which has a <c>SlimeSet</c> with
/// <c>Members</c>. Each member has a <c>Weight</c> value — the probability of that slime
/// spawning relative to the total weight of all members in the set.
/// Gold and Lucky slimes have very small weights (typically 1–5) compared to common slimes
/// (50–200+), making them rare.
/// </para>
///
/// <para>
/// The multiplier scales all Gold and Lucky member weights upward:
/// <list type="bullet">
///   <item><term>1 (default)</term><description>Vanilla weights — no change.</description></item>
///   <item><term>2</term><description>Gold/Lucky weights doubled — roughly 2× more frequent.</description></item>
///   <item><term>10</term><description>Gold/Lucky weights ×10 — significantly more common.</description></item>
///   <item><term>50</term><description>Gold/Lucky weights ×50 — very high chance on any eligible spawner.</description></item>
/// </list>
/// Original weights are cached on first application so disconnect–reconnect with a different
/// multiplier always scales from the unmodified vanilla baseline.
/// </para>
///
/// <para>
/// <b>Timing:</b> <c>DirectedActorSpawner</c> instances are scene-scoped.
/// <c>TryApplyIfNeeded()</c> is called every Update frame and scales all unscaled spawners
/// the first frame they are findable after connection.
/// The <c>Start()</c> Postfix handles new spawners that appear during zone transitions
/// after the connection is already established.
/// </para>
/// </summary>
internal static class GoldLuckySpawnRatePatch
{
    private static bool _applied = false;

    // Original weights keyed by (instanceId, constraintIndex, memberIndex).
    // Cached on first scaling so we always multiply from vanilla values,
    // never from an already-scaled value (prevents double-scaling on reconnect).
    private static readonly Dictionary<(int, int, int), float> _origWeights = new();

    private static readonly string[] TargetKeywords = { "Gold", "Lucky" };

    /// <summary>
    /// Called every Update frame from <see cref="Plugin"/>.
    /// Finds all loaded <c>DirectedActorSpawner</c> instances and applies the multiplier
    /// to any Gold or Lucky <c>SlimeSet.Member</c> that has not yet been scaled.
    /// Sets <c>_applied = true</c> once all spawners have been processed for this session.
    /// </summary>
    internal static void TryApplyIfNeeded()
    {
        if (_applied) return;
        if (!Plugin.Instance.ModEnabled) return;

        var slotData = Plugin.Instance.ApClient?.SlotData;
        if (slotData == null) return; // not connected yet

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
                        _origWeights[key] = member.Weight; // cache vanilla value

                    member.Weight = _origWeights[key] * multiplier;
                    scaled++;
                }
            }
        }

        if (scaled > 0)
        {
            Plugin.Instance.Log.LogInfo(
                $"[AP-GoldLucky] Applied {multiplier}× weight multiplier to {scaled} " +
                $"Gold/Lucky member(s) across {spawners.Count} spawner(s)");
            _applied = true;
        }
        // If scaled == 0 (no relevant zone loaded yet), leave _applied false and retry next frame.
    }

    /// <summary>
    /// Called on disconnect so a reconnect with a different multiplier re-applies correctly.
    /// Restores original weights for any still-alive spawner instances before clearing state.
    /// </summary>
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
    }

    /// <summary>
    /// Secondary trigger: fires when a <c>DirectedActorSpawner</c> initializes (e.g. on zone
    /// transition after connection is already established). Resets <c>_applied</c> so the next
    /// <c>TryApplyIfNeeded()</c> call in Update scans and scales the new spawner.
    /// </summary>
    [HarmonyPatch(typeof(DirectedActorSpawner), "Start")]
    [HarmonyPostfix]
    private static void StartPostfix(DirectedActorSpawner __instance)
    {
        _applied = false;
        TryApplyIfNeeded();
    }
}
