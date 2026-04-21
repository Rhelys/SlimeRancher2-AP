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
/// <list type="bullet">
///   <item><term>locations / bundled modes</term><description>
///     Prefix blocks gate opens until <c>SaveManager.IsRegionUnlocked</c> returns true.
///     Teleporter is never auto-granted here (handled by <c>ItemHandler.ApplyRegionAccess</c>
///     in bundled mode).
///   </description></item>
///   <item><term>vanilla mode</term><description>
///     Regions are not AP items — Prefix never blocks. Postfix grants the matching zone
///     teleporter the first time each gate opens (idempotent via <c>IsBlueprintUnlocked</c>).
///   </description></item>
/// </list>
/// <para>
/// <c>__state</c> (bool) is Harmony's mechanism for passing data from Prefix to Postfix.
/// It is set to <c>true</c> only in vanilla mode, signalling the Postfix that it should
/// attempt a teleporter grant for this call.
/// </para>
/// </remarks>
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

        // Vanilla mode: regions are not AP items — never block, and let Postfix grant teleporter
        if (teleMode == "vanilla")
        {
            __state = true;
            return true;
        }

        // Locations / bundled mode: gate only the tracked AP region gates.
        // Any switch NOT in RegionTable (e.g. vanilla puzzle gates, save-state restores) passes through.
        // gameObject.name can crash on partially-initialised IL2CPP objects during scene load — guard it.
        string switchName;
        try { switchName = __instance.gameObject.name; }
        catch { return true; }   // can't identify — never block

        // Always log the raw switch name + scene in locations/bundled mode so we can confirm
        // the GameObject names in RegionTable are correct.
        string dbgScene;
        try { var sc = __instance.gameObject.scene; dbgScene = sc.IsValid() ? (sc.name ?? "") : "?"; }
        catch { dbgScene = "?"; }
        Plugin.Instance.Log.LogInfo(
            $"[AP-Gate] {teleMode} mode — DOWN switch: '{switchName}' scene='{dbgScene}' " +
            $"tracked={RegionTable.TryGetRegionForSwitch(switchName, out _)}");

        if (!RegionTable.TryGetRegionForSwitch(switchName, out var regionName))
            return true;   // not a tracked region gate — never block

        // Always send the location check on player interaction — idempotent, safe to call
        // whether the gate is about to open or be blocked.  This ensures the check is sent
        // even when the access item arrived before the player ever touched the button.
        if (RegionTable.TryGetLocationId(switchName, out var locationId))
            Plugin.Instance.ApClient.SendCheck(locationId);

        // Allow the gate to open only if the access item has been received.
        if (Plugin.Instance.SaveManager.IsRegionUnlocked(regionName)) return true;

        Plugin.Instance.Log.LogInfo($"[AP] Blocked gate open: {switchName} (region '{regionName}' not yet received)");
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
        // Works for all modes — harmless in item/bundled (save already has it), essential in auto.
        if (RegionTable.TryGetRegionForSwitch(switchName, out var regionItemName))
            TrapHandler.MarkRegionOpen(regionItemName);

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
