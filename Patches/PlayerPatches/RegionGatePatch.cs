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
// ─────────────────────────────────────────────────────────────────────────────
[HarmonyPatch(typeof(WorldStatePrimarySwitch), "Activate")]
internal static class RegionGateActivatePatch
{
    private static bool Prefix(WorldStatePrimarySwitch __instance)
    {
#if DEBUG
        SlimeRancher2AP.Utils.DebugTrace.Once("RegionGateActivatePatch.Prefix — first entry");
#endif
        if (!Plugin.Instance.ApClient.IsConnected) return true;

        var teleMode = Plugin.Instance.ApClient.SlotData?.RegionAccessMode ?? "vanilla";
        if (teleMode == "vanilla") return true;   // vanilla: never block at this level

        string switchName;
        try { switchName = __instance.gameObject.name; }
        catch { return true; }

        if (!RegionTable.TryGetRegionForSwitch(switchName, out var regionName)) return true;

        // Region already unlocked — let Activate() run. SetStateForAll will fire, pass
        // through the Prefix there, and the Postfix will send the location check.
        if (Plugin.Instance.SaveManager.IsRegionUnlocked(regionName)) return true;

        // Region locked: send the check and block Activate() entirely.
        // Because Activate() never runs, _switchEnabled is not cleared and the button
        // stays live so the player can press it again after the access item arrives.
        if (RegionTable.TryGetLocationId(switchName, out var locationId))
            Plugin.Instance.ApClient.SendCheck(locationId);

        Plugin.Instance.Log.LogInfo(
            $"[AP] Blocked gate Activate: '{switchName}' ('{regionName}' not yet received) — button left enabled.");
        return false;
    }
}

// ─────────────────────────────────────────────────────────────────────────────
// RegionGatePatch — guards SetStateForAll for all callers (Activate second press,
// scene-state restoration, etc.) and handles post-open side effects.
// ─────────────────────────────────────────────────────────────────────────────
[HarmonyPatch(typeof(WorldStatePrimarySwitch), nameof(WorldStatePrimarySwitch.SetStateForAll))]
internal static class RegionGatePatch
{
    private static bool Prefix(WorldStatePrimarySwitch __instance, SwitchHandler.State state,
                                out bool __state)
    {
#if DEBUG
        SlimeRancher2AP.Utils.DebugTrace.Once("RegionGatePatch.Prefix — first entry");
#endif
        __state = false; // Postfix: don't auto-grant unless we set this true below

        // Allow vanilla behaviour when not connected to Archipelago
        if (!Plugin.Instance.ApClient.IsConnected) return true;

        // Only intercept opening (DOWN) — always allow closing
        if (state != SwitchHandler.State.DOWN) return true;

        var teleMode = Plugin.Instance.ApClient.SlotData?.RegionAccessMode ?? "vanilla";

        // Vanilla mode: regions are not AP items — never block, let Postfix grant teleporter
        if (teleMode == "vanilla")
        {
            __state = true;
            return true;
        }

        // Locations / bundled mode: gate only the tracked AP region gates.
        // Any switch NOT in RegionTable (e.g. vanilla puzzle gates) passes through.
        // gameObject.name can crash on partially-initialised IL2CPP objects — guard it.
        string switchName;
        try { switchName = __instance.gameObject.name; }
        catch { return true; }

        string dbgScene;
        try { var sc = __instance.gameObject.scene; dbgScene = sc.IsValid() ? (sc.name ?? "") : "?"; }
        catch { dbgScene = "?"; }
        Plugin.Instance.Log.LogInfo(
            $"[AP-Gate] {teleMode} mode — DOWN switch: '{switchName}' scene='{dbgScene}' " +
            $"tracked={RegionTable.TryGetRegionForSwitch(switchName, out _)}");

        if (!RegionTable.TryGetRegionForSwitch(switchName, out var regionName))
            return true;   // not a tracked region gate — never block

        // Allow the gate to open only if the access item has been received.
        if (Plugin.Instance.SaveManager.IsRegionUnlocked(regionName)) return true;

        Plugin.Instance.Log.LogInfo($"[AP] Blocked gate SetStateForAll: '{switchName}' ('{regionName}' not yet received)");
        return false;
    }

    private static void Postfix(WorldStatePrimarySwitch __instance, SwitchHandler.State state,
                                 bool __state)
    {
#if DEBUG
        SlimeRancher2AP.Utils.DebugTrace.Once("RegionGatePatch.Postfix — first entry");
#endif
        if (state != SwitchHandler.State.DOWN) return;

        string switchName, sceneName;
        try
        {
            switchName = __instance.gameObject.name;
            var scene  = __instance.gameObject.scene;
            sceneName  = scene.IsValid() ? (scene.name ?? "") : "";
        }
        catch { return; }

        // Track gate as open for this session (used by teleport trap eligibility check).
        if (RegionTable.TryGetRegionForSwitch(switchName, out var regionItemName))
            TrapHandler.MarkRegionOpen(regionItemName);

        // Send the location check now that the gate has actually opened.
        // Idempotent — SendCheck guards with IsChecked(). Only for locations/bundled mode.
        var teleMode = Plugin.Instance.ApClient.IsConnected
            ? (Plugin.Instance.ApClient.SlotData?.RegionAccessMode ?? "vanilla")
            : "vanilla";
        if (teleMode != "vanilla" && RegionTable.TryGetLocationId(switchName, out var locId))
            Plugin.Instance.ApClient.SendCheck(locId);

        // Vanilla mode: grant the zone teleporter for this region
        if (__state)
            ItemHandler.TryGrantRegionTeleporterForSwitch(switchName);

        // Goal detection: notify GoalHandler of every switch opening (mode-independent)
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

        Plugin.Instance.Log.LogInfo(
            $"[AP-Gate] InvisibleSwitch DOWN: name='{switchName}'  scene='{sceneName}'");

        GoalHandler.OnSwitchOpened(switchName, sceneName);
    }
}
