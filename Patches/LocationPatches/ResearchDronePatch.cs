using Il2CppMonomiPark.SlimeRancher.World.ResearchDrone;
using SlimeRancher2AP.Data;
using SlimeRancher2AP.Utils;
using UnityEngine;

namespace SlimeRancher2AP.Patches.LocationPatches;

/// <summary>
/// Research drone checks, by polling: any controller in state ACTIVATED sends its check
/// (idempotent), keyed by <c>_researchDroneEntry.name</c>. Polled because OnInteract and
/// ActivateDrone crash the trampoline since 5/13, and WakeDrone fires on approach.
/// </summary>
internal static class ResearchDronePatch
{
    // By time, not frames, so the scan rate doesn't scale with frame rate.
    private const float  PollSeconds = 1f;
    private static float _nextPoll   = 0f;

    internal static void Tick()
    {
        if (!Plugin.Instance.ModEnabled || !Plugin.Instance.SaveManager.IsSaveBound) return;
        float now = UnityEngine.Time.unscaledTime;
        if (now < _nextPoll) return;
        _nextPoll = now + PollSeconds;

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
