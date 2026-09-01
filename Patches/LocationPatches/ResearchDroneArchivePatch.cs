using HarmonyLib;
using Il2CppMonomiPark.SlimeRancher.Input;
using Il2CppMonomiPark.SlimeRancher.UI.ResearchDrone;
using SlimeRancher2AP.Data;

namespace SlimeRancher2AP.Patches.LocationPatches;

/// <summary>
/// Detects the first-ever viewing of a Research Drone's archive page (unlocked by the
/// Drone Archive Key upgrade) and sends an Archipelago location check.
///
/// Hook point: <c>ResearchDroneUI.ToggleArchive(InputEventData)</c> — called by the UI
/// input system when the player presses the archive button. After the original runs,
/// <c>isInArchive</c> is true when the player just entered the archive view.
///
/// Note: <c>ShowArchiveEntry()</c> has CallerCount=0 and is called exclusively from native
/// code inside <c>ToggleArchive</c>'s AOT-compiled body, so patching it directly does not
/// fire; <c>ToggleArchive</c> is used instead with an <c>isInArchive</c> post-toggle check.
///
/// Identity: <c>mainEntry.archivedEntry.name</c> is the stable asset name of the archive
/// <c>ResearchDroneEntry</c>. Stored as <see cref="LocationInfo.EntryName"/> in the table.
///
/// Dedup: <see cref="ApSaveManager.IsChecked"/> is the sole guard.
/// </summary>
[HarmonyPatch(typeof(ResearchDroneUI), "ToggleArchive")]
internal static class ResearchDroneArchivePatch
{
    private static void Postfix(ResearchDroneUI __instance)
    {
        // Always log so we can discover which drones have archive entries.
        // This fires on both directions of the toggle; isInArchive tells us which.
        var mainEntry    = __instance.mainEntry;
        var archiveEntry = mainEntry?.archivedEntry;

        var mainName    = mainEntry?.name    ?? "(null main)";
        var archiveName = archiveEntry?.name ?? "(no archive)";

        Logger.Info(
            $"[AP-Drone] ToggleArchive: isInArchive={__instance.isInArchive}" +
            $"  main='{mainName}'  archive='{archiveName}'");

        // Only send a check when entering archive view (not when toggling back).
        if (!__instance.isInArchive) return;

        if (!Plugin.Instance.ModEnabled || !Plugin.Instance.SaveManager.IsSaveBound)
            return;

        if (Plugin.Instance.ApClient?.SlotData?.RandomizeResearchDrones != true)
            return;

        if (archiveEntry == null)
            return; // This drone has no archive content — nothing to check.

        // All 23 drones now have an archive row, so this branch means either a game update
        // renamed an asset or a new drone was added. Either way the archive is a dead location
        // for the seed, so warn loudly rather than failing silently — that is how the original
        // 10 missing rows were found. F9 → Dumps → "Dump Research Drone Archives" lists every
        // loaded entry and flags the ones with no table row.
        if (!LocationTable.TryGetByEntryName(archiveName, out var info) || info is null)
        {
            Logger.Warning(
                $"[AP-Drone] Unknown archive entry '{archiveName}' (main='{mainName}') " +
                $"— no AP location is mapped to it, so no check was sent");
            return;
        }

        var alreadyChecked = Plugin.Instance.SaveManager.IsChecked(info.Id);
        Logger.Info(
            $"[AP-Drone] Archive lookup ok: '{info.Name}' " +
            $"(id={info.Id}  archive='{archiveName}'  alreadyChecked={alreadyChecked})");

        if (alreadyChecked)
            return;

        Logger.Info(
            $"[AP] Research Drone Archive check: '{info.Name}' " +
            $"(id={info.Id}  archive='{archiveName}'  main='{mainName}')");

        Plugin.Instance.ApClient?.SendCheck(info.Id);
    }
}
