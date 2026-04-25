using System;
using HarmonyLib;
using Il2CppMonomiPark.SlimeRancher.Slime;
using UnityEngine;

namespace SlimeRancher2AP.Patches.LocationPatches;

/// <summary>
/// Debug-only flag set by the debug panel "Force Radiant Spawn ON" button.
/// When <c>true</c>, <see cref="ForceRadiantSpawnPatch"/> overrides every
/// <c>DrawFromRadiantShuffleBag</c> return value to <c>true</c>, making every
/// eligible slime encounter radiant regardless of bag state.
/// </summary>
internal static class RadiantDebugFlags
{
    internal static bool ForceRadiantSpawn = false;
}

/// <summary>
/// Postfix on <c>RadiantSlimeDirector.DrawFromRadiantShuffleBag</c>.
/// Forces the return value to <c>true</c> (every slime encounter is radiant) when either:
/// <list type="bullet">
///   <item><see cref="RadiantDebugFlags.ForceRadiantSpawn"/> is set (debug panel toggle), or</item>
///   <item><c>SlotData.AllRadiantSlimes</c> is <c>true</c> (apworld option).</item>
/// </list>
/// <para>
/// This is a reliable alternative to <c>DEBUG_ForceRadiantSpawn</c> on the director,
/// which appears to be an editor-only field that is not read by the shipping native code.
/// </para>
/// <para>
/// In DEBUG builds also logs post-draw bag state (CurrentIndex, RadiantSpawnIndex, Size)
/// by looking up the slime's bag in <c>RadiantSlimesModel.RadiantShuffleBags</c>.
/// Note: <c>RadiantShuffleBag.Draw()</c> is called natively inside <c>GameAssembly.dll</c>
/// and cannot be intercepted by Harmony — bag state must be observed here instead.
/// </para>
/// </summary>
[HarmonyPatch(typeof(RadiantSlimeDirector), "DrawFromRadiantShuffleBag")]
internal static class ForceRadiantSpawnPatch
{
    private static void Postfix(ref bool __result, IdentifiableType id,
        RadiantSlimeDirector __instance)
    {
#if DEBUG
        // Log bag state by looking up the slime in RadiantShuffleBags.
        // RadiantShuffleBag.Draw() is a native-to-native call inside GameAssembly.dll —
        // a Harmony patch on the managed wrapper never fires. We observe post-draw state here.
        try
        {
            var bags = __instance.RadiantSlimesModel?.RadiantShuffleBags;
            RadiantShuffleBag? bag = null;
            if (bags != null && id != null)
                foreach (var kvp in bags)
                    if (kvp.Key?.name == id.name) { bag = kvp.Value; break; }

            if (bag != null)
                Plugin.Instance.Log.LogInfo(
                    $"[AP-Bag] POST Draw: slime='{id!.name}' " +
                    $"idx={bag.CurrentIndex} radiantAt={bag.RadiantSpawnIndex} size={bag.Size} " +
                    $"→ radiant={__result}");
            else
                Plugin.Instance.Log.LogInfo(
                    $"[AP-Bag] POST Draw: slime='{id?.name ?? "null"}' bag=null → radiant={__result}");
        }
        catch (Exception ex)
        {
            Plugin.Instance.Log.LogWarning($"[AP-Bag] bag state read threw: {ex.Message}");
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
/// Scales radiant slime spawn frequency based on the <c>radiant_spawn_rate_multiplier</c>
/// slot data option.
///
/// <para>
/// SR2 uses a <b>shuffle-bag</b> algorithm for radiant spawns: each eligible slime type has
/// a configured <c>BagSize</c> in the shared <c>RadiantSlimeConfig</c> ScriptableObject asset.
/// The director reads this size when creating a new live bag for a slime type on first
/// encounter. A smaller bag means more frequent radiant spawns.
/// </para>
///
/// <para>
/// The multiplier divides all bag sizes:
/// <list type="bullet">
///   <item><term>1 (default)</term><description>Vanilla bag sizes — no change.</description></item>
///   <item><term>2</term><description>Bags halved — radiant slimes appear ~2× as often.</description></item>
///   <item><term>5</term><description>Bags ÷5 — radiant slimes appear ~5× as often.</description></item>
///   <item><term>10</term><description>Bags ÷10 — radiant slimes appear ~10× as often.</description></item>
///   <item><term>50</term><description>
///     Bags ÷50 — for slimes with vanilla bag sizes below 100 (most common types),
///     bags floor to 2, giving a radiant on the 1st or 2nd encounter of each type.
///     Pink slimes (bag=2000) reach size 40 at this multiplier.
///   </description></item>
/// </list>
/// Bag sizes are floored at 2 so a radiant is never guaranteed on every single spawn.
/// </para>
///
/// <para>
/// <b>Timing:</b> <c>RadiantSlimeDirector.Start()</c> fires during scene load, before the
/// AP connection completes. <c>TryApplyIfNeeded()</c> is called every Update frame and
/// applies the multiplier the first frame a director becomes findable after connection,
/// using cached original bag sizes so disconnect-reconnect with a different multiplier
/// always computes from the unmodified vanilla baseline.
/// The <c>Start()</c> Postfix is kept as a secondary trigger for scene reloads that happen
/// after connection is already established.
/// </para>
/// </summary>
internal static class RadiantSlimeSpawnRatePatch
{
    // Set to true once the bag config has been scaled for the current session.
    // Reset on disconnect so a reconnect with a different multiplier re-applies.
    private static bool _bagSizesScaled = false;

    // Set to true once RadiantShuffleBags has been seeded with bags for all configured slimes.
    // Separate from _bagSizesScaled because the director (and therefore the model) may not be
    // available when config scaling first runs. Retried every Update until it succeeds.
    // Reset on disconnect alongside _bagSizesScaled.
    private static bool _livebagsSeeded = false;

    // Vanilla bag sizes indexed by position in _radiantShuffleBagSizes.
    // Cached on first successful application so we always divide from the original,
    // never from an already-scaled value (prevents double-scaling on reconnect).
    private static int[]? _origBagSizes = null;

    /// <summary>
    /// Called every Update frame from <c>Plugin.cs</c>.
    /// Runs in two stages that may succeed on different frames:
    /// <list type="bullet">
    ///   <item>Stage 1 (<c>_bagSizesScaled</c>): Scale <c>RadiantSlimeConfig</c> bag sizes —
    ///   succeeds as soon as the ScriptableObject asset is in memory (immediately after AP
    ///   connection, before any scene is loaded).</item>
    ///   <item>Stage 2 (<c>_livebagsSeeded</c>): Pre-seed <c>RadiantSlimesModel.RadiantShuffleBags</c>
    ///   with bags for every configured slime that does not yet have one — guarantees the
    ///   native code picks up the multiplied size on the very first encounter rather than
    ///   using an unobservable transient bag. Requires a live <c>RadiantSlimeDirector</c>
    ///   (scene-scoped), so it retries each frame until one is found.</item>
    /// </list>
    /// </summary>
    internal static void TryApplyIfNeeded()
    {
        if (_bagSizesScaled && _livebagsSeeded) return;
        if (!Plugin.Instance.ModEnabled) return;

        var slotData = Plugin.Instance.ApClient?.SlotData;
        if (slotData == null) return; // not connected yet — retry next frame

        var multiplier = slotData.RadiantSpawnRateMultiplier;
        if (multiplier <= 1) { _bagSizesScaled = true; _livebagsSeeded = true; return; }

        // ── Stage 1: scale config bag sizes ───────────────────────────────────
        if (!_bagSizesScaled)
        {
            // Target RadiantSlimeConfig directly — it is a ScriptableObject asset that is
            // always resident in memory regardless of which scene is loaded.
            var configs = Resources.FindObjectsOfTypeAll<RadiantSlimeConfig>();
            if (configs.Length == 0) return; // unexpected — retry next frame

            bool appliedAny = false;
            for (int c = 0; c < configs.Length; c++)
            {
                var config = configs[c];
                if (config == null) continue;

                var bags = config._radiantShuffleBagSizes;
                if (bags == null || bags.Length == 0) continue;

                // Cache originals on first successful access so disconnect-reconnect with a
                // different multiplier always divides from the unmodified vanilla values.
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

                Plugin.Instance.Log.LogInfo(
                    $"[AP-Radiant] Scaled {scaled} bag config(s): size ÷{multiplier} " +
                    $"→ radiant spawns {multiplier}× more frequent");
                appliedAny = true;
            }

            if (appliedAny) _bagSizesScaled = true;
            else return; // couldn't find config — retry
        }

        // ── Stage 2: pre-seed RadiantShuffleBags for all configured slimes ────
        if (!_livebagsSeeded)
        {
            var directors = Resources.FindObjectsOfTypeAll<RadiantSlimeDirector>();
            if (directors.Count == 0) return; // scene not loaded yet — retry next frame

            var director = directors[0];
            if (director == null) return;

            var model = director.RadiantSlimesModel;
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

                // Check if this slime already has a bag (loaded from save)
                bool alreadyHasBag = false;
                foreach (var kvp in liveBags)
                    if (kvp.Key?.name == entry.Slime.name) { alreadyHasBag = true; break; }

                if (alreadyHasBag) continue;

                // Create a fresh bag with the patched size and insert it into the live dict.
                // CurrentIndex=0, RadiantSpawnIndex chosen randomly by the native ctor.
                var newBag = new RadiantShuffleBag(entry.BagSize);
                liveBags[entry.Slime] = newBag;
                seeded++;
            }

            Plugin.Instance.Log.LogInfo(
                $"[AP-Radiant] Pre-seeded {seeded} slime bag(s) into RadiantShuffleBags " +
                $"(already had {liveBags.Count - seeded})");
            _livebagsSeeded = true;
        }
    }

    /// <summary>Called on disconnect so a reconnect with a different multiplier re-applies.</summary>
    internal static void OnDisconnected()
    {
        _bagSizesScaled = false;
        _livebagsSeeded = false;
    }

    // Secondary trigger: fires when a director wakes after connection is already established
    // (e.g. a late scene load or zone transition that creates a new director instance).
    // Re-seeds live bags for the new director without re-scaling the already-patched config.
    [HarmonyPatch(typeof(RadiantSlimeDirector), "Start")]
    [HarmonyPostfix]
    private static void StartPostfix(RadiantSlimeDirector __instance)
    {
        // Config scaling is already done (_bagSizesScaled=true). Reset only the live-bag
        // seed flag so the new director's model gets seeded on the next Update frame.
        if (_bagSizesScaled && _livebagsSeeded)
            _livebagsSeeded = false;
        TryApplyIfNeeded();
    }
}
