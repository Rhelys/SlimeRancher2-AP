using HarmonyLib;
using Il2CppMonomiPark.SlimeRancher.Dialogue.ResearchDrone;
using Il2CppMonomiPark.SlimeRancher.World.ResearchDrone;
using SlimeRancher2AP.Data;
using UnityEngine;

namespace SlimeRancher2AP.Patches.LocationPatches;

/// <summary>
/// Detects the first-ever activation of a Research Drone (Gigi's journal drones) and
/// sends an Archipelago location check.
///
/// Hook point: <c>ResearchDroneActivator.OnInteract()</c> — called when the player
/// approaches and interacts with the drone world object.
///
/// Dedup: <see cref="ApSaveManager.IsChecked"/> is the sole guard. It returns true once
/// the AP server has acknowledged the check, preventing re-sends on every subsequent
/// approach and on session reconnect.
///
/// Identity: <c>ResearchDroneController.ResearchDroneEntry.name</c> is a stable asset
/// name (e.g. "ResearchDroneGully") stored as <see cref="LocationInfo.EntryName"/> in
/// <see cref="LocationTable"/>.
/// </summary>
[HarmonyPatch(typeof(ResearchDroneActivator), nameof(ResearchDroneActivator.OnInteract))]
internal static class ResearchDronePatch
{
    private static bool _archiveScanDone;

    /// <summary>
    /// One-shot scan: finds every ResearchDroneEntry asset in memory and logs which ones
    /// have an archivedEntry. Run once on first interaction so the full list is in the log
    /// without visiting every drone manually.
    /// </summary>
    private static void LogAllArchiveEntries()
    {
        if (_archiveScanDone) return;
        _archiveScanDone = true;

        var allEntries = Resources.FindObjectsOfTypeAll<ResearchDroneEntry>();
        Logger.Info(
            $"[AP-Drone] Archive scan: found {allEntries.Count} ResearchDroneEntry assets");

        foreach (var e in allEntries)
        {
            var archiveName = e.archivedEntry?.name;
            if (!string.IsNullOrEmpty(archiveName))
                Logger.Info(
                    $"[AP-Drone]   HAS archive: '{e.name}' → '{archiveName}'");
            else
                Logger.Debug(
                    $"[AP-Drone]   no archive:  '{e.name}'");
        }
    }

    private static void Postfix(ResearchDroneActivator __instance)
    {
        if (!Plugin.Instance.ModEnabled || !Plugin.Instance.SaveManager.HasActiveSession)
            return;

        if (Plugin.Instance.ApClient?.SlotData?.RandomizeResearchDrones != true)
            return;

        var controller = __instance._researchDroneController;
        if (controller == null)
        {
            Logger.Warning("[AP] ResearchDrone: _researchDroneController is null");
            return;
        }

        var entry = controller.ResearchDroneEntry;
        if (entry == null)
        {
            Logger.Warning("[AP] ResearchDrone: ResearchDroneEntry is null");
            return;
        }

        var entryName = entry.name;
        if (string.IsNullOrEmpty(entryName))
        {
            Logger.Warning("[AP] ResearchDrone: ResearchDroneEntry.name is null/empty");
            return;
        }

        // Always log the entry name + archive name for in-game validation.
        var archiveEntryName = entry.archivedEntry?.name ?? "(none)";
        Logger.Info(
            $"[AP-Drone] OnInteract: entry='{entryName}'  archive='{archiveEntryName}'");

        // Run the one-shot full scan so all entries are visible in the log.
        LogAllArchiveEntries();

        if (!LocationTable.TryGetByEntryName(entryName, out var info) || info is null)
        {
            Logger.Warning(
                $"[AP] Unknown ResearchDrone entry '{entryName}' — add to LocationTable");
            return;
        }

        var alreadyChecked = Plugin.Instance.SaveManager.IsChecked(info.Id);
        Logger.Info(
            $"[AP-Drone] Lookup ok: '{info.Name}' (id={info.Id}  entry='{entryName}'  alreadyChecked={alreadyChecked})");

        if (alreadyChecked)
            return;

        Logger.Info(
            $"[AP] Research Drone check: '{info.Name}' (id={info.Id}  entry='{entryName}')");

        Plugin.Instance.ApClient?.SendCheck(info.Id);
    }
}
