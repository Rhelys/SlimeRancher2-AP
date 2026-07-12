using HarmonyLib;
using Il2CppMonomiPark.SlimeRancher.Input;
using Il2CppMonomiPark.SlimeRancher.UI.MainMenu;
using SlimeRancher2AP.Archipelago;
using SlimeRancher2AP.UI;

namespace SlimeRancher2AP.Patches.UiPatches;

/// <summary>
/// Blocks New Game from starting in two risky situations:
/// (1) the mod is enabled but the client is not yet connected to a server, and
/// (2) the client IS connected but the AP slot is already associated with a
///     different SR2 save (see <see cref="SaveGuard"/>) — a new save would start
///     with item delivery and checks paused, which surprises players.
/// </summary>
/// <remarks>
/// <para>
/// Patched method: <c>NewGameOptionsUIRoot.OnSubmit(InputEventData data)</c>
/// This fires when the player confirms their new-game options and the game would
/// begin creating the save. We intercept here (rather than at slot selection) so
/// that the user can still browse the options screen — only the final confirmation
/// is blocked.
/// </para>
/// <para>
/// When blocked, a modal is shown with "Go Back" and "Create Anyways" options.
/// "Create Anyways" sets the bypass flag and re-invokes OnSubmit with the original
/// InputEventData so the full new-game pipeline runs exactly as normal. In the
/// already-associated case it additionally clears the slot's save association, so
/// the fresh save auto-associates on first load (SaveGuard) and the item replay
/// requested by <c>NewGamePatch</c> actually lands in it.
/// </para>
/// </remarks>
[HarmonyPatch(typeof(NewGameOptionsUIRoot), "OnSubmit")]
internal static class NewGameBlockPatch
{
    private static bool _bypass;

    private static bool Prefix(NewGameOptionsUIRoot __instance, InputEventData data)
    {
        if (_bypass) { _bypass = false; return true; }

        // Mod disabled → vanilla play; never block
        if (!Plugin.Instance.ModEnabled) return true;

        if (!Plugin.Instance.ApClient.IsConnected)
        {
            // Blocked: show a warning dialog with an option to proceed anyway
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

        // Connected — but is this AP slot already bound to another save? Creating a new
        // save now would leave it PAUSED by SaveGuard (wrong-save protection), which is
        // confusing when discovered in-game. Warn up front instead, and make proceeding
        // move the run: clear the association so the fresh save auto-associates on load.
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
