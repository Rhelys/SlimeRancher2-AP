using Il2CppMonomiPark.SlimeRancher.DataModel;
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
    // EV and SS: catches zone-teleporter bypasses (gadget received without access item).
    // PB: catches PB→RF exit via gadget bypass (PB→EV walk is handled separately above).
    // Grey Labyrinth has no AP gate check; omitted.
    /// <summary>
    /// Zone holding the Rainbow Fields teleporter nodes, and the only destination for which the
    /// exit rescue below is meaningful.
    /// </summary>
    private const string RainbowFieldsZone = "SceneGroup.ConservatoryFields";

    private static readonly Dictionary<string, long> ZoneGateLocations = new()
    {
        ["SceneGroup.RumblingGorge"]    = LocationConstants.RegionGate_EmberValley,
        ["SceneGroup.LuminousStrand"]   = LocationConstants.RegionGate_StarlightStrand,
        ["SceneGroup.PowderfallBluffs"] = LocationConstants.RegionGate_PowderfallBluffs,
    };

    /// <summary>
    /// Player-facing region names. Refusal popups must name the region the player is actually
    /// being turned away from — "this region" is meaningless when the message appears mid
    /// zone-transition and the player cannot tell which side of it the mod is objecting to.
    /// </summary>
    private static readonly Dictionary<string, string> ZoneDisplayNames = new()
    {
        ["SceneGroup.RumblingGorge"]    = "Ember Valley",
        ["SceneGroup.LuminousStrand"]   = "Starlight Strand",
        ["SceneGroup.PowderfallBluffs"] = "Powderfall Bluffs",
        ["SceneGroup.ConservatoryFields"] = "Rainbow Fields",
        ["SceneGroup.Labyrinth"]        = "Grey Labyrinth",
    };

    /// <summary>
    /// Gated zone -> the AP item that grants access to it. Entry to any of these is refused
    /// unless the item has been received.
    /// </summary>
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
    /// Debug builds only: when true, a triggered enforcement logs what it WOULD have done and
    /// then cancels, so a developer can roam gated zones freely.  Defaults to <c>false</c> —
    /// dev builds enforce exactly like release builds.  Previously debug builds suppressed the
    /// return unconditionally, which made zone protection untestable outside a release build.
    /// Toggle from the debug panel (F9 → Misc).
    /// </summary>
    public static bool SuppressReturn = false;
#endif

    // -------------------------------------------------------------------------
    // Pending return state
    // -------------------------------------------------------------------------

    private static long    _returnLocId    = -1;
    private static float   _returnAt       = -1f;
    // Set when the pending return was triggered by ENTERING a gated zone without its access
    // item. The cancel condition is then IsRegionUnlocked(item) rather than IsChecked(location),
    // because pressing a gate or filling the PB plort door sends the check without granting
    // access. Null for exit enforcement, which really is about the check having been sent.
    private static string? _requiredItemName = null;
    // When true, skip the last-safe-position path and always use Teleport_ResetPlayer.
    // Set for EV-from-PB enforcement: last safe position is inside PB, so returning there
    // would put the player right back where they can re-enter EV — an infinite loop.
    private static bool    _forceSpawnReset = false;

    // Last player position recorded while no enforcement was pending.
    // Used to return the player to where they came from rather than to spawn.
    private static Vector3 _lastSafePosition;
    private static bool    _hasSafePosition = false;

    // ── Post-return verification ──────────────────────────────────────────────
    // A zone transition in this game can take 15-20 seconds, and the return fires partway
    // through it. The reposition then executes successfully and is immediately overwritten by
    // the game's own teleport-arrival placement, so the log claimed success while the player
    // stayed exactly where they were (observed on PB entry).
    //
    // After acting, keep watching: while the player is still standing in the zone they were
    // supposed to be evicted from, try again. Bounded so a destination we cannot escape does
    // not trap the player in a reposition loop.
    private const  float VerifyWindowSeconds = 20f;
    private const  float VerifyRetrySeconds  = 2f;
    private const  int   MaxReturnAttempts   = 5;
    private static string? _blockedZone;      // zone the player must not remain in
    private static string? _exitZone;         // gated zone an exit block was raised for
    private static float   _verifyUntil;
    private static float   _nextAttemptAt;
    private static int     _attempts;

    /// <summary>
    /// Seconds after detecting the bypass before firing the return teleport.
    /// Gives the destination scene time to finish loading so Player/TeleportNetwork
    /// are valid.  Also a grace window: if the gate check / access item arrives during
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
    /// <returns>True if enforcement was triggered (zone transition is unauthorized).</returns>
    public static bool OnZoneChanged(string? newZone, string? previousZone)
    {
        if (newZone == null) return false;

        // Always log the transition and, when enforcement is skipped, WHY.  Previously every
        // guard returned silently, so a zone change that should have been blocked left no trace
        // at all in the log and there was no way to tell which condition let it through.
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
        // Session-based, not socket-based: enforcement must survive a temporary disconnect,
        // otherwise a briefly-offline player can slip through gated zones unenforced.
        if (!Plugin.Instance.SaveManager.HasActiveSession)
        {
            Logger.Info($"[AP] GateReturnEnforcer: '{previousZone}' → '{newZone}' — not enforced (no active AP session)");
            return false;
        }

        // Only enforce when gate checks are actual AP locations.
        var mode = Plugin.Instance.ApClient.SlotData?.RegionAccessMode ?? "vanilla";
        if (mode == "vanilla")
        {
            Logger.Info($"[AP] GateReturnEnforcer: '{previousZone}' → '{newZone}' — not enforced (region_access_mode=vanilla)");
            return false;
        }

        Logger.Info($"[AP] GateReturnEnforcer: zone transition '{previousZone}' → '{newZone}' (mode={mode})");

        // ── EV entry from PB (bundled-mode bypass) ───────────────────────────
        // In bundled mode the PB zone teleporter is granted with "Powderfall Bluffs Access",
        // so a player can reach PB without ever pressing the EV gate.  From there they can
        // walk into Ember Valley through the Gorge portal — entering EV without the EV gate
        // check being sent.  This also blocks EV from being added to the teleport trap pool.
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
        // The rule is simply "you may only be in a zone whose access item you hold".
        //
        // This used to guard PB only, on the assumption that EV and SS could be entered only by
        // pressing their gate button (which RegionGatePatch blocks), leaving the exit rule below
        // as the safety net. A zone teleporter gadget defeats that: teleporting Rainbow Fields ->
        // Ember Valley has a non-gated previousZone, so the exit rule declined to act and the
        // player roamed EV freely until they happened to leave. Entry is now checked directly.
        //
        // Cancel condition is IsRegionUnlocked (item received), NOT IsChecked — pressing the gate
        // or filling the PB plort door sends the check but does not itself grant access.
        if (ZoneAccessItems.TryGetValue(newZone, out var accessItem)
            && !Plugin.Instance.SaveManager.IsRegionUnlocked(accessItem))
        {
            ZoneGateLocations.TryGetValue(newZone, out var entryLocId);
            _returnLocId      = entryLocId;
            _returnAt         = Time.time + ReturnDelay;
            _requiredItemName = accessItem;
            _blockedZone      = newZone;
            // Force the teleport-network reset rather than the last safe position: gated zones are
            // entered through a full scene transition, so the recorded position belongs to the zone
            // the player came FROM and is meaningless in the scene now loaded — writing it into the
            // motor either does nothing or drops the player out of the world.
            _forceSpawnReset  = true;
            Logger.Info(
                $"[AP] GateReturnEnforcer: entered '{newZone}' without '{accessItem}' " +
                $"— resetting to Rainbow Fields in {ReturnDelay}s");
            UI.ApPopup.ShowThrottled($"gate-entry-{newZone}", $"{ZoneName(newZone)} is locked",
                                     "Archipelago", $"Requires: {accessItem}");
            return true;
        }

        // ── EV / SS exit enforcement (and PB exit / gadget bypass) ───────────
        // Legacy safety net. Entry enforcement above now prevents being in a gated zone without
        // its access item at all, so this only catches states that predate that check — a save
        // loaded while already standing in a gated zone, for instance, where previousZone is null
        // on the first frame and no entry transition is ever observed.
        if (!ZoneGateLocations.TryGetValue(previousZone, out var locId))
        {
            Logger.Info($"[AP] GateReturnEnforcer: '{previousZone}' is not a gated zone — nothing to enforce on exit");
            return false;
        }

        // Only when the player is actually heading for Rainbow Fields.
        //
        // The whole premise below is that the player rode the normal teleporter home and will
        // arrive at the RF-side node, which sits behind the gate. Any other destination — walking
        // Starlight Strand -> Grey Labyrinth, or Ember Valley -> Grey Labyrinth — does not touch
        // that node, so there is nothing to be sealed behind and nothing to rescue them from.
        //
        // Without this the rule fired on every Labyrinth entry and threw the player back to
        // spawn, because IsGateOpen reads the switch out of the loaded scene and Rainbow Fields
        // is not loaded when the destination is the Labyrinth: the scan finds nothing and
        // "unknown" is deliberately treated as closed. The same player leaving the same zone
        // toward Rainbow Fields was cleared a minute earlier, with RF loading and the switch
        // readable — reported as being teleported out of the Labyrinth from both entrances with
        // every access item held and every gate check sent.
        if (newZone != RainbowFieldsZone)
        {
            Logger.Info(
                $"[AP] GateReturnEnforcer: '{previousZone}' → '{newZone}' is not a return to " +
                "Rainbow Fields — the sealed-teleporter rescue does not apply");
            return false;
        }
        // The pass condition is whether the Rainbow Fields gate is PHYSICALLY OPEN — not whether
        // the check was sent and not whether the access item is held.
        //
        // This rule exists to prevent a soft-lock: the RF-side teleporter node sits behind that
        // gate, so a player who reaches a zone by other means (a teleporter gadget) and rides the
        // normal teleporter home arrives inside a sealed area with no way out. Resetting them to
        // spawn is a rescue, not a penalty.
        //
        // Neither proxy is sound. RegionGatePatch sends the check even when it BLOCKS the press,
        // so a sent check does not imply an open gate; and receiving the access item does not
        // press the button, so a held item does not either. A player in either state would be
        // waved through and sealed in.
        if (IsGateOpen(previousZone))
        {
            Logger.Info($"[AP] GateReturnEnforcer: left '{previousZone}' safely — its Rainbow Fields gate is open");
            return false;
        }

        // The Rainbow Fields gate for this zone is shut, so the teleporter node the player is
        // arriving at is sealed off — send them to RF spawn instead, where they can reach the
        // gate button. _forceSpawnReset: the last safe position is inside the zone they are
        // leaving, so returning there would re-trigger enforcement in a loop.
        _returnLocId      = locId;
        _returnAt         = Time.time + ReturnDelay;
        _requiredItemName = null;
        _exitZone         = previousZone;
        _forceSpawnReset  = true;

        Logger.Info(
            $"[AP] GateReturnEnforcer: '{previousZone}' → '{newZone}' with the " +
            $"{ZoneName(previousZone)} gate still shut (location {locId}) — the arrival teleporter " +
            $"is sealed off, resetting to Rainbow Fields spawn in {ReturnDelay}s");

        // No popup here, deliberately. This fires as the player is already leaving the zone, and
        // the reset sends them to Rainbow Fields — where the gate button is. Announcing "region
        // locked" while moving them toward the thing that unlocks it explains nothing they are not
        // about to see. Refusals are worth a popup when an action is denied; this one is granted.
        return true;
    }


    /// <summary>
    /// True only when the Rainbow Fields gate for <paramref name="zoneRef"/> is confirmed open.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Reads <c>GameModel.GetSwitchState</c> — the game's save-persistent switch registry, keyed
    /// by <c>WorldSwitchDefinition.ID</c>. Two reasons that beats scanning loaded objects:
    /// </para>
    ///
    /// <para>
    /// <b>It answers while the scene is unloaded.</b> The old scan read the switch out of
    /// Rainbow Fields, so with RF unloaded an open gate read as shut. That is what threw a player
    /// out of the Grey Labyrinth from both entrances with every access item held and both gate
    /// checks sent. Confirmed by dump: standing in Starlight Strand, both gates were still in the
    /// registry with <c>obj='(not loaded)'</c> and their true states.
    /// </para>
    ///
    /// <para>
    /// <b>It cannot read the wrong switch.</b> The scan matched on GameObject name alone, and the
    /// same dump found a different switch also named <c>ruinSwitch</c> in
    /// <c>zoneStrand_Area4</c> — so standing in Starlight Strand, that object could answer for
    /// the Ember Valley gate. RegionTable has always keyed switches by scene for exactly this
    /// reason; the scan was the one place that dropped the scene.
    /// </para>
    ///
    /// <para>
    /// Unknown still counts as closed: being sent to spawn unnecessarily is an annoyance, being
    /// sealed behind a shut gate is a soft-lock.
    /// </para>
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

        // Fallback: scan loaded objects, scene-qualified. Only reached with no GameModel (main
        // menu) or an unmapped region, where the answer is closed anyway.
        if (!RegionTable.TryGetSwitchSceneAndName(itemName, out var sceneName, out var switchName))
            return false;

        try
        {
            foreach (var sw in Resources.FindObjectsOfTypeAll<WorldStatePrimarySwitch>())
            {
                if (sw == null) continue;
                var go = sw.gameObject;
                // Scene must match: name alone collides across scenes.
                if (go == null || go.name != switchName) continue;
                if (!go.scene.IsValid() || go.scene.name != sceneName) continue;

                var model = sw._model;
                if (model == null) continue;              // not yet bound — treat as closed
                if (model.state == SwitchHandler.State.DOWN) return true;
            }
        }
        catch { /* Rainbow Fields not loaded — treat as closed */ }

        return false;
    }

    /// <summary>Called every frame from <c>ApUpdateBehaviour.Update()</c>.</summary>
    public static void Tick()
    {
        // Continuously record the player's position while no enforcement is pending,
        // so we have a "where they came from" position ready when a violation fires.
        if (_returnLocId < 0)
        {
            var p = SceneContext.Instance?.Player;
            if (p != null) { _lastSafePosition = p.transform.position; _hasSafePosition = true; }
        }

        // Post-return verification: the reposition may have been undone by the game's own
        // teleport-arrival placement if it landed mid scene-load.
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

        // Re-check: gate condition may have been satisfied during the delay window.
        // Entry blocks clear when the access item arrives. Exit blocks clear only when the gate
        // is actually open — by the time this runs, Rainbow Fields has usually finished loading,
        // so the switch is readable even if it was not at transition time.
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
            // Scene still loading — extend the wait rather than giving up.
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
        // Return the player to where they came from via the KCC motor.
        // Skipped when _forceSpawnReset is set: the safe position is inside a zone the player
        // isn't allowed to be in (e.g. PB for the EV-from-PB path), so sending them back there
        // would restart the same illegal transition.
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

        // Fallback: no safe position captured yet (e.g. enforcement fired immediately on load).
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

    /// <summary>
    /// Arms the post-return check, or clears outright when there is nothing to verify (the exit
    /// enforcement path has no "must not be here" zone — it sends the player to spawn from a
    /// zone they are allowed to be in).
    /// </summary>
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

    /// <summary>
    /// Clears any pending return. Called on disconnect so a pending reset scheduled
    /// just before a session ends does not fire on the next load.
    /// </summary>
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
