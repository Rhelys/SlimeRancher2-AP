using HarmonyLib;
using Il2CppMonomiPark.SlimeRancher.Input;
using Il2CppMonomiPark.SlimeRancher.UI.MainMenu;
using SlimeRancher2AP.UI;

namespace SlimeRancher2AP.Patches.UiPatches;

/// <summary>
/// Blocks New Game from starting when the Archipelago mod is enabled but the client
/// is not yet connected to a server.
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
/// InputEventData so the full new-game pipeline runs exactly as normal.
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

        // Already connected → fine to start a new game
        if (Plugin.Instance.ApClient.IsConnected) return true;

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
}
