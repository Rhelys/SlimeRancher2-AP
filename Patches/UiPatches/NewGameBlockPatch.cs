using HarmonyLib;
using Il2CppMonomiPark.SlimeRancher.Input;
using Il2CppMonomiPark.SlimeRancher.UI.MainMenu;
using SlimeRancher2AP.Archipelago;
using SlimeRancher2AP.UI;

namespace SlimeRancher2AP.Patches.UiPatches;

/// <summary>
/// Confirming a new game asks first when the mod is on but not connected, or when the slot is
/// already bound to another save (<see cref="SaveGuard"/> would pause the new one).
/// "Create Anyways" re-submits — and in the second case clears the binding so the new save
/// takes it.
/// </summary>
[HarmonyPatch(typeof(NewGameOptionsUIRoot), "OnSubmit")]
internal static class NewGameBlockPatch
{
    private static bool _bypass;

    private static bool Prefix(NewGameOptionsUIRoot __instance, InputEventData data)
    {
        if (_bypass) { _bypass = false; return true; }

        if (!Plugin.Instance.ModEnabled) return true;

        if (!Plugin.Instance.ApClient.IsConnected)
        {
            Logger.Warning(
                "[AP] New game blocked: not connected to Archipelago. " +
                "Connect first so the server can provide your randomized world data.");

            StatusHUD.Instance?.ShowWarningModal(
                "Not connected to Archipelago.\n\n" +
                "You must connect before starting a new game so the server can " +
                "provide your randomized world data.\n\n" +
                "Go to Options > Archipelago to connect.",
                () => { _bypass = true; __instance.OnSubmit(data); });

            return false; // skip original OnSubmit
        }

        // Already bound to another save? Warn now rather than leave the new save paused.
        var saveManager = Plugin.Instance.SaveManager;
        var associated  = saveManager.HasActiveSession ? saveManager.AssociatedSaveName : "";
        if (string.IsNullOrEmpty(associated)) return true; // fresh AP slot → fine

        Logger.Warning(
            $"[AP] New game warning: this AP slot is already associated with save " +
            $"'{associated}'. Creating a new save moves the Archipelago run to it.");

        StatusHUD.Instance?.ShowWarningModal(
            "This Archipelago slot already has a save in progress.\n\n" +
            "Creating a new save will move your Archipelago run to it: all received " +
            "items will replay into the new save, and the previous save will no " +
            "longer receive items or send checks.\n\n" +
            "To continue your existing run, go back and load that save instead.",
            () =>
            {
                saveManager.AssociateSave("");
                SaveGuard.Reset();
                _bypass = true;
                __instance.OnSubmit(data);
            });

        return false; // skip original OnSubmit
    }
}
