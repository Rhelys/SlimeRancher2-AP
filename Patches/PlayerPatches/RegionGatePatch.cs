using HarmonyLib;
using Il2CppMonomiPark.SlimeRancher.World;
using SlimeRancher2AP.Archipelago;
using SlimeRancher2AP.Data;

namespace SlimeRancher2AP.Patches.PlayerPatches;

/// <summary>
/// Region gate buttons (<c>region_access_mode</c>). A press on a locked gate sends its check
/// and is refused before <c>Activate()</c> runs — Activate clears <c>_switchEnabled</c>, which
/// would leave the button dead. <c>__state</c> tells the Postfix the gate is really opening.
/// (Not SetStateForAll: patching it crashed the trampoline after the 5/13/2026 update.)
/// </summary>
[HarmonyPatch(typeof(WorldStatePrimarySwitch), "Activate")]
internal static class RegionGateActivatePatch
{
    private static bool Prefix(WorldStatePrimarySwitch __instance, out bool __state)
    {
#if DEBUG
        SlimeRancher2AP.Utils.DebugTrace.Once("RegionGateActivatePatch.Prefix — first entry");
#endif
        __state = false;

        // Session, not socket — a brief disconnect must not open gates for free.
        if (!Plugin.Instance.ModEnabled || !Plugin.Instance.SaveManager.HasActiveSession)
            return true;

        var teleMode = Plugin.Instance.ApClient.SlotData?.RegionAccessMode ?? "vanilla";

        if (teleMode == "vanilla")
        {
            __state = true;   // the Postfix grants the zone teleporter
            return true;
        }

        string switchName, sceneName;
        try
        {
            switchName = __instance.gameObject.name;
            var scene  = __instance.gameObject.scene;
            sceneName  = scene.IsValid() ? (scene.name ?? "") : "";
        }
        catch { return true; }

        if (!RegionTable.TryGetRegionForSwitch(switchName, sceneName, out var regionName))
            return true; // not a gate

        if (Plugin.Instance.SaveManager.IsRegionUnlocked(regionName))
        {
            __state = true;
            return true;
        }

        // Locked: send the check, block Activate, and leave the button live.
        if (RegionTable.TryGetLocationId(switchName, sceneName, out var locationId))
            Plugin.Instance.ApClient.SendCheck(locationId);

        Logger.Info(
            $"[AP] Blocked gate Activate: '{switchName}' ('{regionName}' not yet received) — button left enabled.");

        // Explain the dead button, naming the region and its item ("Ember Valley Access").
        var displayName = regionName.EndsWith(" Access")
            ? regionName.Substring(0, regionName.Length - " Access".Length)
            : regionName;
        SlimeRancher2AP.UI.ApPopup.ShowThrottled(
            $"gate-press-{regionName}", $"{displayName} is locked",
            "Archipelago", $"Requires: {regionName}");

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

        if (RegionTable.TryGetRegionForSwitch(switchName, sceneName, out var regionItemName))
            TrapHandler.MarkRegionOpen(regionItemName);

        // Idempotent; covers a gate opened some other way than the locked press.
        var teleMode = Plugin.Instance.SaveManager.HasActiveSession
            ? (Plugin.Instance.ApClient.SlotData?.RegionAccessMode ?? "vanilla")
            : "vanilla";
        if (teleMode != "vanilla" && RegionTable.TryGetLocationId(switchName, sceneName, out var locId))
            Plugin.Instance.ApClient.SendCheck(locId);

        if (teleMode == "vanilla")
            ItemHandler.TryGrantRegionTeleporterForSwitch(switchName, sceneName);

        GoalHandler.OnSwitchOpened(switchName, sceneName);
    }
}

/// <summary>
/// Puzzle-driven switches (the Labyrinth beam gates, the Harmonizer bells). Safe to patch; fires
/// repeatedly while a beam is lit.
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
        catch { return; }  // half-initialised during scene load

        Logger.Info(
            $"[AP-Gate] InvisibleSwitch DOWN: name='{switchName}'  scene='{sceneName}'");

        GoalHandler.OnSwitchOpened(switchName, sceneName);
    }
}
