using Il2CppMonomiPark.SlimeRancher.World.ResearchDrone;
using SlimeRancher2AP.Data;
using SlimeRancher2AP.Utils;
using UnityEngine;

namespace SlimeRancher2AP.Patches.LocationPatches;

/// <summary>
/// Fires location checks when Research Drones are activated by the player.
///
/// <para>
/// Polling approach (no Harmony patch): every <see cref="PollInterval"/> frames we call
/// <c>Resources.FindObjectsOfTypeAll&lt;ResearchDroneController&gt;()</c> and check each
/// controller's <c>_state</c> field. When a drone is <c>ACTIVATED</c> we call
/// <see cref="ArchipelagoClient.SendCheck"/>, which is idempotent — already-sent checks
/// are silently skipped by <c>SaveManager.IsChecked()</c>.
/// </para>
///
/// <para>
/// Why polling: the natural Harmony targets in the activation chain are all unsafe.
/// <c>ResearchDroneActivator.OnInteract()</c> and <c>ResearchDroneController.ActivateDrone()</c>
/// are both CallerCount(0) — their native prologues changed in the 5/13/2026 update, causing
/// HarmonyX trampoline crashes. <c>WakeDrone()</c> (CallerCount(1)) fires on proximity approach,
/// not on player interaction, so its Postfix alone cannot detect activation.
/// </para>
///
/// <para>
/// Identity: <c>_researchDroneEntry.name</c> — the ResearchDroneEntry ScriptableObject
/// asset has a stable name used as <see cref="LocationInfo.GameObjectName"/> in
/// <see cref="LocationTable"/>.
/// </para>
/// </summary>
internal static class ResearchDronePatch
{
    // Poll once per second at 60 fps — low overhead, acceptable latency.
    private const int PollInterval = 60;
    private static int _pollCounter = 0;

    /// <summary>Called every Update frame from <c>ApUpdateBehaviour</c>.</summary>
    internal static void Tick()
    {
        if (!Plugin.Instance.ModEnabled || !Plugin.Instance.SaveManager.HasActiveSession) return;
        if (++_pollCounter < PollInterval) return;
        _pollCounter = 0;

        var controllers = Resources.FindObjectsOfTypeAll<ResearchDroneController>();
        for (int i = 0; i < controllers.Length; i++)
        {
            var ctrl = controllers[i];
            if (ctrl == null) continue;
            if (ctrl._state != ResearchDroneController.DroneState.ACTIVATED) continue;

            var entry = ctrl._researchDroneEntry;
            if (entry == null) continue;

            var entryName = entry.name;
            if (!LocationTable.TryGetByEntryName(entryName, out var info) || info == null)
            {
                Logger.Warning(
                    $"[AP] Unknown ResearchDroneEntry '{entryName}' — run AP-Dump and add to LocationTable");
                continue;
            }

            Plugin.Instance.ApClient.SendCheck(info.Id);
        }
    }
}
