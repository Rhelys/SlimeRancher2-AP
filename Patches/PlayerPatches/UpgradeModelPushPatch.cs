using HarmonyLib;
using Il2CppMonomiPark.SlimeRancher.DataModel;
using SlimeRancher2AP.Archipelago;

namespace SlimeRancher2AP.Patches.PlayerPatches;

/// <summary>
/// Signals that save restore (<c>UpgradeModel.Push</c>, which raises no per-upgrade events) has
/// filled the model, and re-runs reconciliation — before this the model is empty and would be
/// "repaired" wholesale. CallerCount(1), safe.
/// </summary>
[HarmonyPatch(typeof(UpgradeModel), nameof(UpgradeModel.Push))]
internal static class UpgradeModelPushPatch
{
    private static void Postfix()
    {
        try
        {
            ItemHandler.OnUpgradesRestored();
            Logger.Info("[AP] Upgrade model restored from save — scheduling watermark reconciliation.");

            // Even if validation already ran — it saw the pre-restore model.
            if (Plugin.Instance.ApClient?.IsConnected == true)
                Plugin.Instance.ApClient.ScheduleUpgradeValidation();
        }
        catch (System.Exception ex)
        {
            Logger.Warning($"[AP] UpgradeModelPushPatch threw: {ex.Message}");
        }
    }
}
