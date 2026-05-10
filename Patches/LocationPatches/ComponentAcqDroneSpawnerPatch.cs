using HarmonyLib;
using Il2CppMonomiPark.SlimeRancher.Drone;

namespace SlimeRancher2AP.Patches.LocationPatches;

/// <summary>
/// Forces all <see cref="ComponentAcqDroneSpawner"/> instances to spawn their attached
/// treasure pod regardless of whether the scene's spawn query is satisfied.
///
/// <para>
/// In vanilla SR2 the spawner evaluates a <c>CompositeQueryComponent</c> (which may check
/// story conversations, game-event flags, etc.) before showing the ghostly drone node and
/// its linked <c>TreasurePod</c>. If the query has not yet been satisfied the pod never
/// appears in the world, making that Archipelago location unreachable.
/// </para>
///
/// <para>
/// For the randomiser all 10 ghostly-drone locations must be accessible from the moment the
/// player enters the zone, irrespective of story progression. Forcing <c>isSatisfied = true</c>
/// causes the spawner's normal spawn path to run exactly as it would if the player had already
/// completed the gating event, so no other state is corrupted.
/// </para>
///
/// <para>
/// This patch is only active when the mod is enabled and a session is live with ghostly-drone
/// randomisation turned on; vanilla behaviour is preserved otherwise.
/// </para>
/// </summary>
[HarmonyPatch(typeof(ComponentAcqDroneSpawner), nameof(ComponentAcqDroneSpawner.HandleSpawnQueryComplete))]
internal static class ComponentAcqDroneSpawnerPatch
{
    // Harmony passes primitive parameters by reference when the patch parameter is declared
    // with 'ref', allowing us to rewrite the value before the original method sees it.
    private static void Prefix(ref bool isSatisfied)
    {
        if (!Plugin.Instance.ModEnabled)
            return;
        if (!Plugin.Instance.SaveManager.HasActiveSession)
            return;
        if (Plugin.Instance.ApClient?.SlotData?.RandomizeGhostlyDrones != true)
            return;

        if (!isSatisfied)
        {
            Logger.Info("[AP] ComponentAcqDroneSpawner: overriding isSatisfied=false → true (randomiser forces spawn)");
            isSatisfied = true;
        }
    }
}
