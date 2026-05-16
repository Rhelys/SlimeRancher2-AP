using HarmonyLib;
using Il2CppMonomiPark.SlimeRancher.World;
using SlimeRancher2AP.Archipelago;
using SlimeRancher2AP.Data;

namespace SlimeRancher2AP.Patches.PlayerPatches;

/// <summary>
/// Manages region gate behaviour and zone teleporter grants based on the slot-data
/// <c>region_access_mode</c> setting.
/// </summary>
/// <remarks>
/// Two patch points work together to give the player a re-pressable gate button:
///
/// <list type="bullet">
///   <item><term>RegionGateActivatePatch (Activate Prefix)</term><description>
///     Intercepts the player's button press BEFORE <c>Activate()</c> disables the
///     interactable. If the region is locked, sends the location check and returns false —
///     <c>Activate()</c> never runs, so <c>_switchEnabled</c> is never cleared and the
///     button remains pressable for the next attempt.
///   </description></item>
///   <item><term>RegionGatePatch (SetStateForAll Prefix + Postfix)</term><description>
///     Prefix blocks the gate from actually opening until <c>IsRegionUnlocked</c> is true.
///     This catches both Activate-originated calls (second press, after item received) and
///     any other callers such as scene-state restoration.
///     Postfix fires only when the gate actually opens; sends the location check at that
///     point and handles vanilla teleporter grants and goal detection.
///   </description></item>
/// </list>
///
/// <c>__state</c> (bool) is Harmony's mechanism for passing data from Prefix to Postfix.
/// It is set to <c>true</c> only in vanilla mode, signalling the Postfix that it should
/// attempt a teleporter grant for this call.
/// </remarks>

// ─────────────────────────────────────────────────────────────────────────────
// RegionGateActivatePatch — intercepts the button press at Activate() level so
// the interactable is NOT disabled when the region is still locked.
//
// SR2's WorldStatePrimarySwitch.Activate() sets _switchEnabled = false before
// calling SetStateForAll. Blocking at SetStateForAll (too late) leaves the button
// dead. Blocking here keeps _switchEnabled untouched — the button stays pressable.
//
// NOTE (2026-05-15, v0.4.4): RegionGatePatch (WorldStatePrimarySwitch.SetStateForAll)
// was removed because SetStateForAll's native prologue changed in the 5/13/2026 game
// update, causing a HarmonyX trampoline crash. The Postfix logic (send check, grant
// teleporter, GoalHandler) now lives here in RegionGateActivatePatch, which patches
// Activate() — a method whose prologue is unchanged. __state passes a signal from
// Prefix to Postfix indicating the gate is actually opening this call.
// ─────────────────────────────────────────────────────────────────────────────
[HarmonyPatch(typeof(WorldStatePrimarySwitch), "Activate")]
internal static class RegionGateActivatePatch
{
    private static bool Prefix(WorldStatePrimarySwitch __instance, out bool __state)
    {
#if DEBUG
        SlimeRancher2AP.Utils.DebugTrace.Once("RegionGateActivatePatch.Prefix — first entry");
#endif
        __state = false;

        if (!Plugin.Instance.ApClient.IsConnected) return true;

        var teleMode = Plugin.Instance.ApClient.SlotData?.RegionAccessMode ?? "vanilla";

        if (teleMode == "vanilla")
        {
            // Vanilla: all gates open freely; Postfix will grant the zone teleporter.
            __state = true;
            return true;
        }

        string switchName;
        try { switchName = __instance.gameObject.name; }
        catch { return true; }

        if (!RegionTable.TryGetRegionForSwitch(switchName, out var regionName))
            return true; // untracked switch — let it through, no side-effects needed

        if (Plugin.Instance.SaveManager.IsRegionUnlocked(regionName))
        {
            // Region unlocked — gate is opening; signal Postfix to send check + GoalHandler.
            __state = true;
            return true;
        }

        // Region locked: send the check and block Activate() entirely so the button stays live.
        // Because Activate() never runs, _switchEnabled is not cleared and the player can press
        // the button again after the access item arrives.
        if (RegionTable.TryGetLocationId(switchName, out var locationId))
            Plugin.Instance.ApClient.SendCheck(locationId);

        Logger.Info(
            $"[AP] Blocked gate Activate: '{switchName}' ('{regionName}' not yet received) — button left enabled.");
        return false;
    }

    private static void Postfix(WorldStatePrimarySwitch __instance, bool __state)
    {
#if DEBUG
        SlimeRancher2AP.Utils.DebugTrace.Once("RegionGateActivatePatch.Postfix — first entry");
#endif
        if (!__state) return;

        string switchName, sceneName;
        try
        {
            switchName = __instance.gameObject.name;
            var scene  = __instance.gameObject.scene;
            sceneName  = scene.IsValid() ? (scene.name ?? "") : "";
        }
        catch { return; }

        // Track gate as open for teleport trap eligibility.
        if (RegionTable.TryGetRegionForSwitch(switchName, out var regionItemName))
            TrapHandler.MarkRegionOpen(regionItemName);

        // Send the location check now that the gate has actually opened.
        // Idempotent — SendCheck guards with IsChecked(). Only for locations/bundled mode.
        var teleMode = Plugin.Instance.ApClient.IsConnected
            ? (Plugin.Instance.ApClient.SlotData?.RegionAccessMode ?? "vanilla")
            : "vanilla";
        if (teleMode != "vanilla" && RegionTable.TryGetLocationId(switchName, out var locId))
            Plugin.Instance.ApClient.SendCheck(locId);

        // Vanilla mode: grant the zone teleporter for this region.
        if (teleMode == "vanilla")
            ItemHandler.TryGrantRegionTeleporterForSwitch(switchName);

        // Goal detection (mode-independent).
        GoalHandler.OnSwitchOpened(switchName, sceneName);
    }
}

/// <summary>
/// Detects WorldStateInvisibleSwitch state changes — the same mechanism as WorldStatePrimarySwitch
/// but used for puzzle-triggered gates like the Valley Labyrinth entrance (via EnergyBeamReceiver
/// techActivator). Fires repeatedly while a beam puzzle is active, so we log every DOWN transition
/// but GoalHandler deduplicates via _openedLabyrinthSwitches.
/// </summary>
[HarmonyPatch(typeof(WorldStateInvisibleSwitch), nameof(WorldStateInvisibleSwitch.SetStateForAll))]
internal static class InvisibleSwitchPatch
{
    private static void Postfix(WorldStateInvisibleSwitch __instance, SwitchHandler.State state)
    {
#if DEBUG
        SlimeRancher2AP.Utils.DebugTrace.Once("InvisibleSwitchPatch.Postfix — first entry");
#endif
        if (state != SwitchHandler.State.DOWN) return;

        string switchName, sceneName;
        try
        {
            switchName = __instance.gameObject.name;
            var scene  = __instance.gameObject.scene;
            sceneName  = scene.IsValid() ? (scene.name ?? "") : "";
        }
        catch { return; }  // guard against partially-initialized objects during scene load

        Logger.Info(
            $"[AP-Gate] InvisibleSwitch DOWN: name='{switchName}'  scene='{sceneName}'");

        GoalHandler.OnSwitchOpened(switchName, sceneName);
    }
}
