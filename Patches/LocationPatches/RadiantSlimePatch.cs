using HarmonyLib;
using Il2CppMonomiPark.SlimeRancher.Slime;

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
/// </summary>
[HarmonyPatch(typeof(RadiantSlimeDirector), "DrawFromRadiantShuffleBag")]
internal static class ForceRadiantSpawnPatch
{
    private static void Postfix(ref bool __result)
    {
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
/// a configured <c>BagSize</c> (e.g. 50–200). The player "draws" from the bag on each slime
/// encounter; when the bag empties, a radiant spawn is guaranteed and the bag resets.
/// A smaller bag means more frequent radiant spawns.
/// </para>
///
/// <para>
/// The multiplier divides all bag sizes on scene start:
/// <list type="bullet">
///   <item><term>1 (default)</term><description>Vanilla bag sizes — no change.</description></item>
///   <item><term>2</term><description>Bags halved — radiant slimes appear ~2× as often.</description></item>
///   <item><term>5</term><description>Bags ÷5 — radiant slimes appear ~5× as often.</description></item>
///   <item><term>10</term><description>Bags ÷10 — radiant slimes appear ~10× as often.</description></item>
///   <item><term>50</term><description>Bags ÷50 — near-guaranteed on every other encounter (bag floor is 2).</description></item>
/// </list>
/// Bag sizes are floored at 1 to prevent division-to-zero.
/// </para>
///
/// <para>
/// Patch target: <c>RadiantSlimeDirector.Start()</c> Postfix — runs once per scene load after
/// the director is initialised, before any slimes spawn.
/// </para>
/// </summary>
[HarmonyPatch(typeof(RadiantSlimeDirector), "Start")]
internal static class RadiantSlimeSpawnRatePatch
{
    private static void Postfix(RadiantSlimeDirector __instance)
    {
        if (!Plugin.Instance.ModEnabled) return;

        var multiplier = Plugin.Instance.ApClient?.SlotData?.RadiantSpawnRateMultiplier ?? 1;
        if (multiplier <= 1) return;

        var config = __instance.GlobalSpawnConfig;
        if (config == null)
        {
            Plugin.Instance.Log.LogWarning("[AP-Radiant] GlobalSpawnConfig is null — cannot scale bag sizes");
            return;
        }

        var bags = config._radiantShuffleBagSizes;
        if (bags == null || bags.Length == 0) return;

        int scaled = 0;
        for (int i = 0; i < bags.Length; i++)
        {
            var entry = bags[i];
            if (entry == null) continue;
            int newSize = System.Math.Max(2, entry.BagSize / multiplier);
            entry.BagSize = newSize;
            scaled++;
        }

        Plugin.Instance.Log.LogInfo(
            $"[AP-Radiant] Scaled {scaled} shuffle bag(s): size ÷{multiplier} → radiant spawns {multiplier}× more frequent");
    }
}
