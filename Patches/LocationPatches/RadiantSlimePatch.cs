using System;
using HarmonyLib;
using Il2CppMonomiPark.SlimeRancher.SceneManagement;
using Il2CppMonomiPark.SlimeRancher.Slime;
using UnityEngine;

namespace SlimeRancher2AP.Patches.LocationPatches;

/// <summary>Debug panel "Force Radiant Spawn": every draw comes up radiant.</summary>
internal static class RadiantDebugFlags
{
    internal static bool ForceRadiantSpawn = false;
}

/// <summary>
/// Makes every draw radiant for the debug flag or <c>AllRadiantSlimes</c> (the director's
/// own DEBUG_ForceRadiantSpawn is ignored by shipping code). Debug builds also log the bag
/// after each draw — <c>RadiantShuffleBag.Draw()</c> is native-to-native and can't be patched.
/// </summary>
[HarmonyPatch(typeof(RadiantSlimeDirector), "DrawFromRadiantShuffleBag")]
internal static class ForceRadiantSpawnPatch
{
    private static void Postfix(ref bool __result, IdentifiableType id,
        SceneGroup sceneGroup, RadiantSlimeDirector __instance)
    {
#if DEBUG
        try
        {
            var bags = __instance._radiantSlimesModel?.RadiantShuffleBags; // direct field — property is CallerCount(0)
            RadiantShuffleBag? bag = null;
            if (bags != null && id != null)
                foreach (var kvp in bags)
                    if (kvp.Key?.name == id.name) { bag = kvp.Value; break; }

            if (bag != null)
                Logger.Info(
                    $"[AP-Bag] POST Draw: slime='{id!.name}' " +
                    $"idx={bag.CurrentIndex} radiantAt={bag.RadiantSpawnIndex} size={bag.Size} " +
                    $"→ radiant={__result}");
            else
                Logger.Info(
                    $"[AP-Bag] POST Draw: slime='{id?.name ?? "null"}' bag=null → radiant={__result}");
        }
        catch (Exception ex)
        {
            Logger.Warning($"[AP-Bag] bag state read threw: {ex.Message}");
        }
#endif
        if (RadiantDebugFlags.ForceRadiantSpawn)
        {
            __result = true;
            return;
        }

        if (Plugin.Instance.ModEnabled &&
            (Plugin.Instance.ApClient?.SlotData?.AllRadiantSlimes ?? false))
        {
            __result = true;
        }
    }
}

/// <summary>
/// <c>radiant_spawn_rate_multiplier</c>: divides each slime's radiant shuffle-bag size (a radiant
/// once per bag) in the persistent <c>RadiantSlimeConfig</c>, floored at 2. Vanilla sizes are
/// cached so a reconnect never divides twice, and restored on disconnect.
/// </summary>
internal static class RadiantSlimeSpawnRatePatch
{
    private static bool _bagSizesScaled = false;

    // Separate stage: needs a scene's director, which may not exist yet.
    private static bool _livebagsSeeded = false;

    // Vanilla sizes, by index in _radiantShuffleBagSizes.
    private static int[]? _origBagSizes = null;

    // A zone change means a new director whose bags need seeding.
    private static string? _lastZoneRef = null;

    private const  float RetrySeconds = 1f;
    private static float _nextRetry   = 0f;

    /// <summary>
    /// Per-frame, two stages: (1) scale the config's bag sizes; (2) seed live bags for every
    /// slime without one, so even the first encounter uses the scaled size. Stage 2 needs the
    /// scene's director and redoes on every zone change.
    /// </summary>
    internal static void TryApplyIfNeeded()
    {
        if (!Plugin.Instance.ModEnabled) return;

        // Zone changes stand in for RadiantSlimeDirector.Start, which can't be patched since 5/13.
        string? currentZone = SlimeRancher2AP.Archipelago.TrapHandler.CurrentZoneRef;

        if (currentZone != _lastZoneRef)
        {
            _lastZoneRef = currentZone;
            if (_bagSizesScaled && _livebagsSeeded)
                _livebagsSeeded = false;
        }

        if (_bagSizesScaled && _livebagsSeeded) return;

        // Throttled — everything below scans Resources.
        float nowT = Time.unscaledTime;
        if (nowT < _nextRetry) return;
        _nextRetry = nowT + RetrySeconds;

        var slotData = Plugin.Instance.ApClient?.SlotData;
        if (slotData == null) return;

        var multiplier = slotData.RadiantSpawnRateMultiplier;
        if (multiplier <= 1)
        {
            // Undo a previous session's scaling.
            RestoreOriginalBagSizes();
            _bagSizesScaled = true;
            _livebagsSeeded = true;
            return;
        }

        // ── Stage 1: scale config bag sizes ───────────────────────────────────
        if (!_bagSizesScaled)
        {
            var configs = Resources.FindObjectsOfTypeAll<RadiantSlimeConfig>();
            if (configs.Length == 0) return;

            bool appliedAny = false;
            for (int c = 0; c < configs.Length; c++)
            {
                var config = configs[c];
                if (config == null) continue;

                var bags = config._radiantShuffleBagSizes;
                if (bags == null || bags.Length == 0) continue;

                if (_origBagSizes == null)
                {
                    _origBagSizes = new int[bags.Length];
                    for (int i = 0; i < bags.Length; i++)
                        _origBagSizes[i] = bags[i]?.BagSize ?? 0;
                }

                int scaled = 0;
                for (int i = 0; i < bags.Length; i++)
                {
                    var entry = bags[i];
                    if (entry == null) continue;
                    int origSize = (i < _origBagSizes.Length) ? _origBagSizes[i] : entry.BagSize;
                    if (origSize <= 0) continue;
                    entry.BagSize = System.Math.Max(2, origSize / multiplier);
                    scaled++;
                }

                Logger.Info(
                    $"[AP-Radiant] Scaled {scaled} bag config(s): size ÷{multiplier} " +
                    $"→ radiant spawns {multiplier}× more frequent");
                appliedAny = true;
            }

            if (appliedAny) _bagSizesScaled = true;
            else return;
        }

        // ── Stage 2: pre-seed RadiantShuffleBags for all configured slimes ────
        if (!_livebagsSeeded)
        {
            var directors = Resources.FindObjectsOfTypeAll<RadiantSlimeDirector>();
            if (directors.Count == 0) return; // scene not loaded yet

            var director = directors[0];
            if (director == null) return;

            // The field, never the CallerCount(0) property, which can hard-crash the process.
            var model = director._radiantSlimesModel;
            if (model == null) return;

            var liveBags   = model.RadiantShuffleBags;
            var gConfig    = director.GlobalSpawnConfig;
            if (liveBags == null || gConfig == null) return;

            var configEntries = gConfig._radiantShuffleBagSizes;
            if (configEntries == null) return;

            int seeded = 0;
            for (int i = 0; i < configEntries.Length; i++)
            {
                var entry = configEntries[i];
                if (entry == null || entry.Slime == null) continue;

                // Bags loaded from the save are kept.
                bool alreadyHasBag = false;
                foreach (var kvp in liveBags)
                    if (kvp.Key?.name == entry.Slime.name) { alreadyHasBag = true; break; }

                if (alreadyHasBag) continue;

                // The ctor picks the radiant index.
                var newBag = new RadiantShuffleBag(entry.BagSize);
                liveBags[entry.Slime] = newBag;
                seeded++;
            }

            Logger.Info(
                $"[AP-Radiant] Pre-seeded {seeded} slime bag(s) into RadiantShuffleBags " +
                $"(already had {liveBags.Count - seeded})");
            _livebagsSeeded = true;
        }
    }

    /// <summary>
    /// On disconnect (main thread): restores vanilla sizes. Existing live bags keep theirs
    /// until drained.
    /// </summary>
    internal static void OnDisconnected()
    {
        RestoreOriginalBagSizes();
        _bagSizesScaled = false;
        _livebagsSeeded = false;
        _lastZoneRef = null;
    }

    private static void RestoreOriginalBagSizes()
    {
        if (_origBagSizes == null) return; // never scaled

        var configs = Resources.FindObjectsOfTypeAll<RadiantSlimeConfig>();
        int restored = 0;
        for (int c = 0; c < configs.Length; c++)
        {
            var config = configs[c];
            if (config == null) continue;
            var bags = config._radiantShuffleBagSizes;
            if (bags == null) continue;

            for (int i = 0; i < bags.Length && i < _origBagSizes.Length; i++)
            {
                var entry = bags[i];
                if (entry == null || _origBagSizes[i] <= 0) continue;
                entry.BagSize = _origBagSizes[i];
                restored++;
            }
        }

        if (restored > 0)
            Logger.Info($"[AP-Radiant] Restored vanilla bag sizes on {restored} config entry(ies)");
    }
}
