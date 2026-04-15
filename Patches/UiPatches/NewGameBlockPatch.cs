using HarmonyLib;
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
/// When blocked, the mod shows a notification and opens the connection UI.
/// If the player has toggled the mod off (vanilla mode) via the StatusHUD button,
/// this patch is a no-op and the new game proceeds normally.
/// </para>
/// </remarks>
[HarmonyPatch(typeof(NewGameOptionsUIRoot), "OnSubmit")]
internal static class NewGameBlockPatch
{
    private static bool Prefix()
    {
#if DEBUG
        // Debug builds skip the connection requirement for easier local testing
        return true;
#else
        // Mod disabled → vanilla play; never block
        if (!Plugin.Instance.ModEnabled) return true;

        // Already connected → fine to start a new game
        if (Plugin.Instance.ApClient.IsConnected) return true;

        // Blocked: show guidance and open connection dialog
        Plugin.Instance.Log.LogWarning(
            "[AP] New game blocked: not connected to Archipelago. " +
            "Connect first so the server can provide your randomized world data.");

        StatusHUD.Instance?.ShowNotification(
            "Connect to Archipelago before starting a new game!");

        Plugin.Instance.ConnectionUi?.Show();

        return false; // skip original OnSubmit
#endif
    }
}
