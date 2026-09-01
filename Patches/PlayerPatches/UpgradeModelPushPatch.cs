using HarmonyLib;
using Il2CppMonomiPark.SlimeRancher.DataModel;
using SlimeRancher2AP.Archipelago;

namespace SlimeRancher2AP.Patches.PlayerPatches;

/// <summary>
/// Signals that the SR2 save's upgrade levels have been restored into the model, and triggers a
/// fresh reconciliation against the AP watermark.
/// </summary>
/// <remarks>
/// <para>
/// Save restore goes through <c>UpgradeModel.Push(Dictionary&lt;int,int&gt;)</c>, not
/// <c>IncrementUpgradeLevel</c> — which is why <c>FabricatorUpgradeBlockPatch</c> never
/// interferes with it, and why the restore is invisible to everything else in the mod.
/// </para>
///
/// <para>
/// Ordering matters because auto-connect completes before the upgrade handler is even
/// constructed. Without this signal, <c>ValidateAndRepairUpgrades</c> could run against an empty
/// model, see every upgrade at level -1, and "repair" the whole set from the AP snapshot —
/// quietly discarding any level the watermark does not account for. Reconciling after Push makes
/// the watermark the last word over real save state rather than over an empty model.
/// </para>
///
/// <para>
/// <c>Push</c> is CallerCount(1) — a managed caller exists, making it one of the safest patch
/// targets in the game.
/// </para>
/// </remarks>
[HarmonyPatch(typeof(UpgradeModel), nameof(UpgradeModel.Push))]
internal static class UpgradeModelPushPatch
{
    private static void Postfix()
    {
        try
        {
            ItemHandler.OnUpgradesRestored();
            Logger.Info("[AP] Upgrade model restored from save — scheduling watermark reconciliation.");

            // Re-run validation even if it already ran this connect: the levels it compared
            // against were pre-restore and therefore meaningless.
            if (Plugin.Instance.ApClient?.IsConnected == true)
                Plugin.Instance.ApClient.ScheduleUpgradeValidation();
        }
        catch (System.Exception ex)
        {
            Logger.Warning($"[AP] UpgradeModelPushPatch threw: {ex.Message}");
        }
    }
}
