using HarmonyLib;
using Il2CppMonomiPark.SlimeRancher.World;
using SlimeRancher2AP.Archipelago;
using SlimeRancher2AP.Data;

namespace SlimeRancher2AP.Patches.PlayerPatches;

/// <summary>
/// Manages region gate behaviour and zone teleporter grants based on the slot-data
/// <c>zone_teleporter_mode</c> setting.
/// </summary>
/// <remarks>
/// <list type="bullet">
///   <item><term>item / bundled modes</term><description>
///     Prefix blocks gate opens until <c>SaveManager.IsRegionUnlocked</c> returns true.
///     Teleporter is never auto-granted here (handled by <c>ItemHandler.ApplyRegionAccess</c>
///     in bundled mode).
///   </description></item>
///   <item><term>auto mode</term><description>
///     Regions are not AP items — Prefix never blocks. Postfix grants the matching zone
///     teleporter the first time each gate opens (idempotent via <c>IsBlueprintUnlocked</c>).
///   </description></item>
/// </list>
/// <para>
/// <c>__state</c> (bool) is Harmony's mechanism for passing data from Prefix to Postfix.
/// It is set to <c>true</c> only in auto mode, signalling the Postfix that it should
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

        var teleMode = Plugin.Instance.ApClient.SlotData?.ZoneTeleporterMode ?? "item";

        // Auto mode: regions are not AP items — never block, and let Postfix grant teleporter
        if (teleMode == "auto")
        {
            __state = true;
            return true;
        }

        // Bundled / item mode: gate only the three known AP region gates.
        // Any switch NOT in RegionTable (e.g. vanilla puzzle gates, save-state restores) passes through.
        // gameObject.name can crash on partially-initialised IL2CPP objects during scene load — guard it.
        string switchName;
        try { switchName = __instance.gameObject.name; }
        catch { return true; }   // can't identify — never block

        if (!RegionTable.TryGetRegionForSwitch(switchName, out var regionName))
            return true;   // not a tracked region gate — never block

        // Check by region item name (what UnlockRegion stores), not by switch name.
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

        // Auto mode: grant the zone teleporter for this region
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
