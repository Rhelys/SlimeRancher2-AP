using HarmonyLib;
using Il2CppMonomiPark.SlimeRancher;
using SlimeRancher2AP.Archipelago;
using SlimeRancher2AP.Data;
using SlimeRancher2AP.Utils;
using UnityEngine;

namespace SlimeRancher2AP.Patches.PlayerPatches;

/// <summary>
/// Prefix on PuzzleSlotLockable.ActivateOnUnlock — the single point where any plort door
/// transitions to its open state. Handles two cases:
///
///   1. Powderfall Bluffs region gate (posKey = "zoneGorge_Area3_-645_34_681"):
///      • Sends the AP location check for RegionGate_PowderfallBluffs (idempotent).
///      • Blocks the open if "Powderfall Bluffs Access" has not been received yet.
///      • When the access item arrives, ApplyRegionAccess calls ActivateOnUnlock directly,
///        which passes through here with IsRegionUnlocked = true.
///
///   2. Other puzzle doors (PuzzleDoor entries in LocationTable):
///      • Sends the AP location check if RandomizePuzzleDoors is enabled.
///      • Always allows the door to open (no blocking).
///
/// Uses posKey (sceneName_X_Y_Z) rather than objectName for identification, because
/// multiple doors share the same objectName (e.g. "objLabyrinthPlortDoor01Small").
/// </summary>
[HarmonyPatch(typeof(PuzzleSlotLockable), "ActivateOnUnlock")]
internal static class PuzzleSlotLockableActivatePatch
{
    private static bool Prefix(PuzzleSlotLockable __instance)
    {
        if (!Plugin.Instance.ModEnabled) return true;
        if (SceneContext.Instance?.PlayerState?._model == null) return true;

        string posKey;
        try { posKey = WorldUtils.PositionKey(__instance.gameObject!); }
        catch { return true; }

        // ── Case 1: Powderfall Bluffs region gate ────────────────────────────────
        if (posKey == RegionTable.PBGatePosKey)
        {
            var mode = Plugin.Instance.ApClient.SlotData?.RegionAccessMode ?? "vanilla";
            if (mode == "vanilla") return true;

            Plugin.Instance.ApClient.SendCheck(LocationConstants.RegionGate_PowderfallBluffs);

            bool unlocked = Plugin.Instance.SaveManager.IsRegionUnlocked(RegionTable.PBRegionItemName);
            if (!unlocked)
            {
                Logger.Info(
                    $"[AP] Blocked PB Slime Door — '{RegionTable.PBRegionItemName}' not yet received.");
                return false;
            }

            Logger.Info("[AP] PB Slime Door — access confirmed, opening.");
            return true;
        }

        // ── Case 2: Other puzzle doors ────────────────────────────────────────────
        if (Plugin.Instance.ApClient.SlotData?.RandomizePuzzleDoors == true)
        {
            if (LocationTable.TryGetByObjectName(posKey, out var locInfo)
                && locInfo!.Type == LocationType.PuzzleDoor)
            {
                Plugin.Instance.ApClient.SendCheck(locInfo.Id);
                Logger.Info(
                    $"[AP] Puzzle Door check: '{locInfo.Name}' (id={locInfo.Id}) posKey='{posKey}'");
            }
        }

        return true;
    }
}

// NOTE (2026-05-15, v0.4.4): PuzzleDoorLockPatch, PuzzleGateActivatorPatch,
// PuzzleSlotLockableNotifyPatch, and PuzzleSlotLockableSendAnalyticsPatch were removed.
// These classes (PuzzleDoorLock, PuzzleGateActivator, PuzzleSlotLockable) moved to the
// root namespace in the 5/13/2026 game update and their native method prologues changed,
// causing HarmonyX trampoline crashes. The labyrinth_open goal is fully detected by
// InvisibleSwitchPatch (WorldStateInvisibleSwitch.SetStateForAll) in RegionGatePatch.cs.
