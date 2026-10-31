using HarmonyLib;
using Il2CppMonomiPark.SlimeRancher.Input;
using Il2CppMonomiPark.SlimeRancher.UI.ResearchDrone;
using SlimeRancher2AP.Data;

namespace SlimeRancher2AP.Patches.LocationPatches;

/// <summary>
/// Opening a drone's archive page (needs the Drone Archive Key) sends its check, keyed by
/// <c>mainEntry.archivedEntry.name</c>. Hooks <c>ToggleArchive</c> and checks
/// <c>isInArchive</c> after — <c>ShowArchiveEntry</c> is only called natively. Any toggle also
/// sends the drone's own check, keyed by <c>mainEntry.name</c>.
/// </summary>
[HarmonyPatch(typeof(ResearchDroneUI), "ToggleArchive")]
internal static class ResearchDroneArchivePatch
{
    private static void Postfix(ResearchDroneUI __instance)
    {
        var mainEntry    = __instance.mainEntry;
        var archiveEntry = mainEntry?.archivedEntry;

        var mainName    = mainEntry?.name    ?? "(null main)";
        var archiveName = archiveEntry?.name ?? "(no archive)";

        Logger.Info(
            $"[AP-Drone] ToggleArchive: isInArchive={__instance.isInArchive}" +
            $"  main='{mainName}'  archive='{archiveName}'");

        if (!Plugin.Instance.ModEnabled || !Plugin.Instance.SaveManager.IsSaveBound)
            return;

        if (Plugin.Instance.ApClient?.SlotData?.RandomizeResearchDrones != true)
            return;

        // The archive is only reachable from the drone's own page, so this drone has been
        // read. Sending its check here backs up the poll in ResearchDronePatch, which a quick
        // switch to the archive was reported to slip past.
        if (mainEntry != null
            && LocationTable.TryGetByEntryName(mainName, out var mainInfo) && mainInfo is not null
            && !Plugin.Instance.SaveManager.IsChecked(mainInfo.Id))
        {
            Logger.Info($"[AP] Research Drone check via archive toggle: '{mainInfo.Name}' (id={mainInfo.Id})");
            Plugin.Instance.ApClient?.SendCheck(mainInfo.Id);
        }

        if (!__instance.isInArchive) return;

        if (archiveEntry == null)
            return; // This drone has no archive content — nothing to check.

        // Every archive has a row, so this means a game update renamed or added one — warn
        // loudly. F9 → "Dump Research Drone Archives" shows which.
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
