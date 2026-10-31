using Il2CppMonomiPark.SlimeRancher.DataModel;
using Il2CppMonomiPark.SlimeRancher.World.Teleportation;
using SlimeRancher2AP.Data;
using UnityEngine;

namespace SlimeRancher2AP.Archipelago;

/// <summary>
/// Enforces region gates against teleporter bypasses (locations/bundled modes only). A zone
/// teleporter from another player could reach EV/SS/PB without the gate's check ever being
/// sent, blocking the multiworld. On each zone change this refuses entry to a zone whose access
/// item is missing, and rescues a player whose way home is sealed behind a shut gate — both by
/// resetting them to Rainbow Fields spawn.
/// </summary>
public static class GateReturnEnforcer
{
    // Zone whose teleporter node sits behind the gates — the only destination the exit rescue covers.
    private const string RainbowFieldsZone = "SceneGroup.ConservatoryFields";

    // Gated zone → its gate's location ID. (The Labyrinth has no gate check.)
    private static readonly Dictionary<string, long> ZoneGateLocations = new()
    {
        ["SceneGroup.RumblingGorge"]    = LocationConstants.RegionGate_EmberValley,
        ["SceneGroup.LuminousStrand"]   = LocationConstants.RegionGate_StarlightStrand,
        ["SceneGroup.PowderfallBluffs"] = LocationConstants.RegionGate_PowderfallBluffs,
    };

    /// <summary>Region names for refusal popups — mid-transition, "this region" is ambiguous.</summary>
    private static readonly Dictionary<string, string> ZoneDisplayNames = new()
    {
        ["SceneGroup.RumblingGorge"]    = "Ember Valley",
        ["SceneGroup.LuminousStrand"]   = "Starlight Strand",
        ["SceneGroup.PowderfallBluffs"] = "Powderfall Bluffs",
        ["SceneGroup.ConservatoryFields"] = "Rainbow Fields",
        ["SceneGroup.Labyrinth"]        = "Grey Labyrinth",
    };

    /// <summary>Gated zone → the access item entry requires.</summary>
    private static readonly Dictionary<string, string> ZoneAccessItems = new()
    {
        ["SceneGroup.RumblingGorge"]    = "Ember Valley Access",
        ["SceneGroup.LuminousStrand"]   = "Starlight Strand Access",
        ["SceneGroup.PowderfallBluffs"] = RegionTable.PBRegionItemName,
    };

    private static string ZoneName(string? zoneRef)
        => zoneRef != null && ZoneDisplayNames.TryGetValue(zoneRef, out var n) ? n : "This region";

#if DEBUG
    /// <summary>
    /// Debug: log what enforcement would do, then cancel, to roam freely. Off by default so dev
    /// builds enforce like release. Toggled from F9 → Misc.
    /// </summary>
    public static bool SuppressReturn = false;
#endif

    // -------------------------------------------------------------------------
    // Pending return state
    // -------------------------------------------------------------------------

    private static long    _returnLocId    = -1;
    private static float   _returnAt       = -1f;
    // Entry blocks only: the access item that cancels the return. (The gate's check alone
    // doesn't — pressing a gate sends it without granting access.)
    private static string? _requiredItemName = null;
    // Use Teleport_ResetPlayer, not the last safe position (which may be inside the zone being
    // left, or in another scene).
    private static bool    _forceSpawnReset = false;

    // Position recorded while nothing is pending.
    private static Vector3 _lastSafePosition;
    private static bool    _hasSafePosition = false;

    // ── Post-return verification ──────────────────────────────────────────────
    // A zone transition can take 15-20 s, and the game's arrival placement can overwrite a
    // return made mid-transition. So keep retrying while the player is still in the blocked
    // zone — bounded, so an inescapable destination can't loop forever.
    private const  float VerifyWindowSeconds = 20f;
    private const  float VerifyRetrySeconds  = 2f;
    private const  int   MaxReturnAttempts   = 5;
    private static string? _blockedZone;      // zone the player must not remain in
    private static string? _exitZone;         // gated zone an exit block was raised for
    private static float   _verifyUntil;
    private static float   _nextAttemptAt;
    private static int     _attempts;

    /// <summary>
    /// Delay before returning: lets the scene finish loading, and cancels if the condition is
    /// satisfied meanwhile.
    /// </summary>
    private const float ReturnDelay = 2f;

    // -------------------------------------------------------------------------
    // Public API
    // -------------------------------------------------------------------------

    /// <summary>Called on every zone transition; schedules a return if it is unauthorized.</summary>
    /// <param name="newZone">SceneGroup.ReferenceId the player just arrived in.</param>
    /// <param name="previousZone">SceneGroup.ReferenceId the player just left.</param>
    /// <returns>True if enforcement was triggered (zone transition is unauthorized).</returns>
    public static bool OnZoneChanged(string? newZone, string? previousZone)
    {
        if (newZone == null) return false;

        // Every skip logs why.
        if (previousZone == null)
        {
            Logger.Info($"[AP] GateReturnEnforcer: first zone seen this session — '{newZone}' (no transition to enforce)");
            return false;
        }
        if (!Plugin.Instance.ModEnabled)
        {
            Logger.Info($"[AP] GateReturnEnforcer: '{previousZone}' → '{newZone}' — not enforced (mod disabled)");
            return false;
        }
        // Session, not socket: enforcement survives a brief disconnect.
        if (!Plugin.Instance.SaveManager.HasActiveSession)
        {
            Logger.Info($"[AP] GateReturnEnforcer: '{previousZone}' → '{newZone}' — not enforced (no active AP session)");
            return false;
        }

        var mode = Plugin.Instance.ApClient.SlotData?.RegionAccessMode ?? "vanilla";
        if (mode == "vanilla")
        {
            Logger.Info($"[AP] GateReturnEnforcer: '{previousZone}' → '{newZone}' — not enforced (region_access_mode=vanilla)");
            return false;
        }

        Logger.Info($"[AP] GateReturnEnforcer: zone transition '{previousZone}' → '{newZone}' (mode={mode})");

        // ── EV entry from PB ───────────────────────────
        // Bundled mode's PB teleporter lets a player walk into EV without its gate check sent.
        if (newZone == "SceneGroup.RumblingGorge"
            && previousZone == "SceneGroup.PowderfallBluffs"
            && !Plugin.Instance.SaveManager.IsChecked(LocationConstants.RegionGate_EmberValley))
        {
            _returnLocId     = LocationConstants.RegionGate_EmberValley;
            _returnAt        = Time.time + ReturnDelay;
            _requiredItemName = null;     // this path is about the check, not the item
            _forceSpawnReset  = true;
            _blockedZone      = newZone;  // must not remain in EV
            Logger.Info(
                $"[AP] GateReturnEnforcer: entered EV from PB without EV gate check " +
                $"— resetting in {ReturnDelay}s");
            UI.ApPopup.ShowThrottled("gate-ev", "Ember Valley is locked",
                                     "Archipelago", "Press its gate in Rainbow Fields first");
            return true;
        }

        // ── Entry enforcement: any gated zone, any route ─────────────────────
        // You may only be in a zone whose access item you hold.
        if (ZoneAccessItems.TryGetValue(newZone, out var accessItem)
            && !Plugin.Instance.SaveManager.IsRegionUnlocked(accessItem))
        {
            ZoneGateLocations.TryGetValue(newZone, out var entryLocId);
            _returnLocId      = entryLocId;
            _returnAt         = Time.time + ReturnDelay;
            _requiredItemName = accessItem;
            _blockedZone      = newZone;
            // The safe position belongs to the previous scene.
            _forceSpawnReset  = true;
            Logger.Info(
                $"[AP] GateReturnEnforcer: entered '{newZone}' without '{accessItem}' " +
                $"— resetting to Rainbow Fields in {ReturnDelay}s");
            UI.ApPopup.ShowThrottled($"gate-entry-{newZone}", $"{ZoneName(newZone)} is locked",
                                     "Archipelago", $"Requires: {accessItem}");
            return true;
        }

        // ── Exit rescue ───────────
        // Riding the teleporter home lands at the RF-side node, which sits behind the gate; with
        // the gate shut the player is sealed in. Resetting to spawn is a rescue, not a penalty.
        if (!ZoneGateLocations.TryGetValue(previousZone, out var locId))
        {
            Logger.Info($"[AP] GateReturnEnforcer: '{previousZone}' is not a gated zone — nothing to enforce on exit");
            return false;
        }

        // Only toward Rainbow Fields — other destinations don't touch that node.
        if (newZone != RainbowFieldsZone)
        {
            Logger.Info(
                $"[AP] GateReturnEnforcer: '{previousZone}' → '{newZone}' is not a return to " +
                "Rainbow Fields — the sealed-teleporter rescue does not apply");
            return false;
        }
        // The gate must be physically open. Neither proxy works: a blocked press still sends
        // the check, and receiving the item doesn't press the button.
        if (IsGateOpen(previousZone))
        {
            Logger.Info($"[AP] GateReturnEnforcer: left '{previousZone}' safely — its Rainbow Fields gate is open");
            return false;
        }

        // Gate shut — send them to RF spawn, where the gate button is.
        _returnLocId      = locId;
        _returnAt         = Time.time + ReturnDelay;
        _requiredItemName = null;
        _exitZone         = previousZone;
        _forceSpawnReset  = true;

        Logger.Info(
            $"[AP] GateReturnEnforcer: '{previousZone}' → '{newZone}' with the " +
            $"{ZoneName(previousZone)} gate still shut (location {locId}) — the arrival teleporter " +
            $"is sealed off, resetting to Rainbow Fields spawn in {ReturnDelay}s");

        // No popup: the move is granted, just rerouted toward the gate button.
        return true;
    }


    /// <summary>
    /// True only when the zone's Rainbow Fields gate is confirmed open.
    /// </summary>
    /// <remarks>
    /// Reads the save-persistent switch registry (<c>GameModel.GetSwitchState</c>), which works
    /// with Rainbow Fields unloaded. A scene scan is only the fallback, and must match the scene
    /// too — another <c>ruinSwitch</c> exists in <c>zoneStrand_Area4</c>. Unknown counts as
    /// closed: a needless reset is an annoyance, a sealed gate a soft-lock.
    /// </remarks>
    private static bool IsGateOpen(string zoneRef)
    {
        if (!ZoneAccessItems.TryGetValue(zoneRef, out var itemName)) return false;

        // Fast path: the gate was seen opening this session.
        if (TrapHandler.IsRegionOpenThisSession(itemName)) return true;

        // Primary: the persistent registry, independent of what is loaded.
        if (RegionTable.TryGetSwitchDefinitionId(itemName, out var definitionId))
        {
            try
            {
                var gameModel = SceneContext.Instance?.GameModel;
                if (gameModel != null)
                    return gameModel.GetSwitchState(definitionId) == SwitchHandler.State.DOWN;
            }
            catch (System.Exception ex)
            {
                Logger.Warning(
                    $"[AP] GateReturnEnforcer: switch registry read failed for '{itemName}' " +
                    $"({ex.Message}) — falling back to a scene scan");
            }
        }

        // Fallback: no GameModel or unmapped region.
        if (!RegionTable.TryGetSwitchSceneAndName(itemName, out var sceneName, out var switchName))
            return false;

        try
        {
            foreach (var sw in Resources.FindObjectsOfTypeAll<WorldStatePrimarySwitch>())
            {
                if (sw == null) continue;
                var go = sw.gameObject;
                if (go == null || go.name != switchName) continue;
                if (!go.scene.IsValid() || go.scene.name != sceneName) continue;

                var model = sw._model;
                if (model == null) continue;              // not bound yet
                if (model.state == SwitchHandler.State.DOWN) return true;
            }
        }
        catch { /* treat as closed */ }

        return false;
    }

    public static void Tick()
    {
        if (_returnLocId < 0)
        {
            var p = SceneContext.Instance?.Player;
            if (p != null) { _lastSafePosition = p.transform.position; _hasSafePosition = true; }
        }

        if (_verifyUntil > 0f)
        {
            if (Time.time > _verifyUntil)
            {
                Logger.Info("[AP] GateReturnEnforcer: verification window closed — return held.");
                ClearPending();
                return;
            }
            if (TrapHandler.CurrentZoneRef != _blockedZone)
            {
                Logger.Info($"[AP] GateReturnEnforcer: player left '{_blockedZone}' — return confirmed.");
                ClearPending();
                return;
            }
            if (Time.time < _nextAttemptAt) return;

            Logger.Info(
                $"[AP] GateReturnEnforcer: still in '{_blockedZone}' after the return " +
                $"(attempt {_attempts}) — retrying.");
            _verifyUntil = 0f;              // fall through and act again this tick
            _returnAt    = Time.time;
        }

        if (_returnLocId < 0 || _returnAt < 0f) return;
        if (Time.time < _returnAt) return;

        // Satisfied during the delay? Entry: item arrived. Exit: gate open.
        bool satisfied = _requiredItemName != null
            ? Plugin.Instance.SaveManager.IsRegionUnlocked(_requiredItemName)
            : (_exitZone != null && IsGateOpen(_exitZone));
        if (satisfied)
        {
            Logger.Info("[AP] GateReturnEnforcer: condition satisfied during delay — cancelling reset");
            ClearPending();
            return;
        }

        var playerGo = SceneContext.Instance?.Player;
        if (playerGo == null)
        {
            _returnAt = Time.time + 1f;
            return;
        }

#if DEBUG
        if (SuppressReturn)
        {
            Logger.Info(
                $"[AP] GateReturnEnforcer: DEBUG — enforcement suppressed by debug panel toggle " +
                $"(forceSpawn={_forceSpawnReset}, would return to " +
                (_hasSafePosition && !_forceSpawnReset ? $"{_lastSafePosition}" : "spawn fallback") + ")");
            ClearPending();
            return;
        }
#endif
        // Back to the last safe position via the motor, unless forced to spawn.
        if (_hasSafePosition && !_forceSpawnReset)
        {
            var motor = playerGo
                .GetComponent<Il2CppMonomiPark.SlimeRancher.Player.CharacterController.SRCharacterController>()
                ?._motor;
            if (motor != null)
            {
                motor.SetPosition(_lastSafePosition);
                Logger.Info($"[AP] GateReturnEnforcer: returned player to last safe position {_lastSafePosition}");
                BeginVerify();
                return;
            }
        }

        var teleportable = playerGo.GetComponent<TeleportablePlayer>();
        var network      = UnityEngine.Object.FindObjectOfType<TeleportNetwork>();

        if (teleportable == null || network == null)
        {
            Logger.Warning("[AP] GateReturnEnforcer: TeleportablePlayer or TeleportNetwork not found — cancelling");
            ClearPending();
            return;
        }

        network.Teleport_ResetPlayer(teleportable);
        Logger.Info("[AP] GateReturnEnforcer: fallback reset to Rainbow Fields spawn");
        BeginVerify();
    }

    /// <summary>Arms post-return verification, or clears when there is no blocked zone (exit rescue).</summary>
    private static void BeginVerify()
    {
        _attempts++;
        if (_blockedZone == null || _attempts >= MaxReturnAttempts)
        {
            if (_attempts >= MaxReturnAttempts)
                Logger.Warning(
                    $"[AP] GateReturnEnforcer: gave up after {_attempts} attempts — player is still " +
                    $"in '{_blockedZone}'. Report this with the surrounding log.");
            ClearPending();
            return;
        }

        // Keep _returnLocId / _blockedZone armed; Tick re-checks the zone below.
        _verifyUntil   = Time.time + VerifyWindowSeconds;
        _nextAttemptAt = Time.time + VerifyRetrySeconds;
    }

    /// <summary>Clears any pending return (on disconnect).</summary>
    public static void Clear() => ClearPending();

    private static void ClearPending()
    {
        _returnLocId      = -1;
        _returnAt         = -1f;
        _requiredItemName = null;
        _forceSpawnReset  = false;
        _blockedZone     = null;
        _exitZone        = null;
        _verifyUntil     = 0f;
        _nextAttemptAt   = 0f;
        _attempts        = 0;
    }
}
