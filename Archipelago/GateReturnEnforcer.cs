using Il2CppMonomiPark.SlimeRancher.World.Teleportation;
using SlimeRancher2AP.Data;
using UnityEngine;

namespace SlimeRancher2AP.Archipelago;

/// <summary>
/// Prevents players from bypassing region gate location checks by using zone teleporter gadgets.
///
/// In "locations" or "bundled" <c>region_access_mode</c>, pressing the gate button in Rainbow
/// Fields is a location check.  A player who receives a zone teleporter gadget from another
/// AP player can travel to EV/SS/PB without ever pressing the button — the check is never sent
/// and other players in the multiworld are permanently blocked behind it.
///
/// This class detects every zone transition away from a gated zone and checks whether the
/// corresponding gate location has been sent.  If not, it teleports the player back to the
/// Rainbow Fields spawn point via <c>Teleport_ResetPlayer</c> — the same reset used by the
/// teleport trap.  This avoids any risk of the player becoming stranded in a zone they cannot
/// legally exit.
///
/// Not active in vanilla mode (no gate checks exist there).
/// </summary>
public static class GateReturnEnforcer
{
    // -------------------------------------------------------------------------
    // Zone → gate location ID mapping
    // -------------------------------------------------------------------------
    // Key: SceneGroup.ReferenceId of the zone the player is LEAVING.
    // Value: AP location ID that must be checked before the player may leave.
    //
    // EV and SS connect directly back to Rainbow Fields.
    // PB connects back to Ember Valley — checked on the PB→Gorge transition.
    // Grey Labyrinth has no AP gate check; omitted.
    private static readonly Dictionary<string, long> ZoneGateLocations = new()
    {
        ["SceneGroup.RumblingGorge"]    = LocationConstants.RegionGate_EmberValley,
        ["SceneGroup.LuminousStrand"]   = LocationConstants.RegionGate_StarlightStrand,
        ["SceneGroup.PowderfallBluffs"] = LocationConstants.RegionGate_PowderfallBluffs,
    };

    // -------------------------------------------------------------------------
    // Pending return state
    // -------------------------------------------------------------------------

    private static long  _returnLocId = -1;
    private static float _returnAt    = -1f;

    /// <summary>
    /// Seconds after detecting the bypass before firing the return teleport.
    /// Gives the destination scene time to finish loading so Player/TeleportNetwork
    /// are valid.  Also a grace window: if the player presses the gate button during
    /// this time the return is cancelled (rechecked in Tick before executing).
    /// </summary>
    private const float ReturnDelay = 2f;

    // -------------------------------------------------------------------------
    // Public API
    // -------------------------------------------------------------------------

    /// <summary>
    /// Called by <c>TrapHandler.TrackCurrentZone</c> on every zone transition.
    /// Schedules a Rainbow Fields reset if the player left a gated zone without having
    /// sent that gate's location check.
    /// </summary>
    /// <param name="newZone">SceneGroup.ReferenceId the player just arrived in.</param>
    /// <param name="previousZone">SceneGroup.ReferenceId the player just left.</param>
    public static void OnZoneChanged(string? newZone, string? previousZone)
    {
        if (newZone == null || previousZone == null) return;
        if (!Plugin.Instance.ModEnabled) return;
        if (!Plugin.Instance.ApClient.IsConnected) return;

        // Only enforce when gate checks are actual AP locations.
        var mode = Plugin.Instance.ApClient.SlotData?.RegionAccessMode ?? "vanilla";
        if (mode == "vanilla") return;

        if (!ZoneGateLocations.TryGetValue(previousZone, out var locId)) return;
        if (Plugin.Instance.SaveManager.IsChecked(locId)) return;

        // Gate check not sent — schedule the reset to Rainbow Fields spawn.
        _returnLocId = locId;
        _returnAt    = Time.time + ReturnDelay;

        Logger.Info(
            $"[AP] GateReturnEnforcer: '{previousZone}' → '{newZone}' " +
            $"without gate check {locId} — resetting to Rainbow Fields in {ReturnDelay}s");

        UI.StatusHUD.Instance?.ShowNotification("Use the gate button to open the region first!");
    }

    /// <summary>Called every frame from <c>ApUpdateBehaviour.Update()</c>.</summary>
    public static void Tick()
    {
        if (_returnLocId < 0 || _returnAt < 0f) return;
        if (Time.time < _returnAt) return;

        // Re-check: gate button may have been pressed during the delay window.
        if (Plugin.Instance.SaveManager.IsChecked(_returnLocId))
        {
            Logger.Info(
                "[AP] GateReturnEnforcer: gate check sent during delay — cancelling reset");
            ClearPending();
            return;
        }

        var playerGo = SceneContext.Instance?.Player;
        if (playerGo == null)
        {
            // Scene still loading — extend the wait rather than giving up.
            _returnAt = Time.time + 1f;
            return;
        }

        var teleportable = playerGo.GetComponent<TeleportablePlayer>();
        var network      = UnityEngine.Object.FindObjectOfType<TeleportNetwork>();

        if (teleportable == null || network == null)
        {
            Logger.Warning(
                "[AP] GateReturnEnforcer: TeleportablePlayer or TeleportNetwork not found — cancelling");
            ClearPending();
            return;
        }

        network.Teleport_ResetPlayer(teleportable);

        Logger.Info(
            "[AP] GateReturnEnforcer: reset player to Rainbow Fields spawn");
        ClearPending();
    }

    /// <summary>
    /// Clears any pending return. Called on disconnect so a pending reset scheduled
    /// just before a session ends does not fire on the next load.
    /// </summary>
    public static void Clear() => ClearPending();

    private static void ClearPending()
    {
        _returnLocId = -1;
        _returnAt    = -1f;
    }
}
