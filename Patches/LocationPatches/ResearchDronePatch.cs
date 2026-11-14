using System.Collections.Generic;
using HarmonyLib;
using Il2CppMonomiPark.SlimeRancher.UI;
using Il2CppMonomiPark.SlimeRancher.World.ResearchDrone;
using SlimeRancher2AP.Data;
using SlimeRancher2AP.Utils;
using UnityEngine;

namespace SlimeRancher2AP.Patches.LocationPatches;

/// <summary>
/// Research drone checks, by polling: any controller in state ACTIVATED sends its check
/// (idempotent), keyed by <c>_researchDroneEntry.name</c>. Polled because OnInteract and
/// ActivateDrone crash the trampoline since 5/13, and WakeDrone fires on approach.
///
/// The controllers are found by a <c>FindObjectsOfTypeAll</c> scan (~20 ms) that runs only after
/// zones stream in (<see cref="SceneScanSchedule"/>); the 1 s poll reads the cached list.
/// With <c>drone_access_key</c> the poll skips every drone: a refused interaction still leaves
/// the drone ACTIVATED (the drone's own OnInteract sets it before calling the base method that
/// <see cref="ResearchDroneInteractPatch"/> blocks), so the state proves nothing. That patch
/// sends the check instead, when it lets a keyed drone through.
/// </summary>
internal static class ResearchDronePatch
{
    // By time, not frames, so the poll rate doesn't scale with frame rate.
    private const float  PollSeconds = 1f;
    private static float _nextPoll   = 0f;

    private static readonly SceneScanSchedule _scan = new(safetySeconds: 30f);
    private static readonly List<ResearchDroneController> _controllers = new();

    internal static void Tick()
    {
        if (!Plugin.Instance.ModEnabled || !Plugin.Instance.SaveManager.IsSaveBound) return;

        if (_scan.Due()) Rescan();

        float now = UnityEngine.Time.unscaledTime;
        if (now < _nextPoll) return;
        _nextPoll = now + PollSeconds;

        for (int i = _controllers.Count - 1; i >= 0; i--)
        {
            var ctrl = _controllers[i];
            if (ctrl == null) { _controllers.RemoveAt(i); continue; } // unloaded with its zone
            if (ctrl._state != ResearchDroneController.DroneState.ACTIVATED) continue;

            var entry = ctrl._researchDroneEntry;
            if (entry == null) continue;

            var entryName = entry.name;
            if (!LocationTable.TryGetByEntryName(entryName, out var info) || info == null)
            {
                Logger.Warning(
                    $"[AP] Unknown ResearchDroneEntry '{entryName}' — run AP-Dump and add to LocationTable");
                _controllers.RemoveAt(i); // warn once per scan, not every second
                continue;
            }

            // Keyed drones are sent by ResearchDroneInteractPatch, never from the state.
            if (Archipelago.LocationKeys.RequiredKey(info) != null) continue;

            Plugin.Instance.ApClient.SendCheck(info.Id);
        }
    }

    private static void Rescan()
    {
        _controllers.Clear();
        var all = Resources.FindObjectsOfTypeAll<ResearchDroneController>();
        for (int i = 0; i < all.Length; i++)
        {
            var ctrl = all[i];
            if (ctrl == null) continue;
            if (!ctrl.gameObject.scene.IsValid()) continue; // prefab, never activates
            _controllers.Add(ctrl);
        }
    }
}

/// <summary>
/// The Drone Access Key lock, shared by the two interaction hooks below. Only drones the
/// option keys and that are not yet checked are affected.
/// </summary>
internal static class ResearchDroneLock
{
    internal enum Verdict { NotKeyed, Allowed, Locked }

    internal static Verdict Evaluate(BaseUIInteractable interactable, out LocationInfo? info, out long keyId)
    {
        info = null;
        keyId = 0;
        if (!Plugin.Instance.ModEnabled || !Plugin.Instance.SaveManager.IsSaveBound) return Verdict.NotKeyed;
        if (Plugin.Instance.ApClient.SlotData?.DroneAccessKey != true) return Verdict.NotKeyed;

        var activator = interactable.TryCast<ResearchDroneActivator>();
        if (activator == null) return Verdict.NotKeyed;

        string? entryName = null;
        try { entryName = activator._researchDroneController?._researchDroneEntry?.name; } catch { }
        if (string.IsNullOrEmpty(entryName)) return Verdict.NotKeyed;
        if (!LocationTable.TryGetByEntryName(entryName, out info) || info == null) return Verdict.NotKeyed;
        if (Plugin.Instance.SaveManager.IsChecked(info.Id)) return Verdict.NotKeyed;

        if (Archipelago.LocationKeys.RequiredKey(info) is not long key) return Verdict.NotKeyed;
        keyId = key;
        return Archipelago.LocationKeys.HasKey(key) ? Verdict.Allowed : Verdict.Locked;
    }

    internal static void ShowLocked(LocationInfo info, long keyId)
    {
        var keyName = ItemTable.Get(keyId)?.Name ?? "Drone Access Key";
        Logger.Info($"[AP] Research drone locked: {info.Name} — '{keyName}' not yet received");
        UI.ApPopup.ShowThrottled($"drone-key-{keyId}", "Research Drone is locked",
                                 "Archipelago", $"Requires: {keyName}");
    }
}

/// <summary>
/// Primary lock: refuses a locked drone at the input handler, before the drone's own
/// <c>ResearchDroneActivator.OnInteract</c> runs. That method marks the drone read
/// (<c>ResearchDroneModel.read</c>), wakes it and raises its game event before it calls the base
/// method, so blocking any later leaves the drone counted as read by the game. The method itself
/// can't be patched (it crashes the trampoline, see above).
/// </summary>
[HarmonyPatch(typeof(BaseUIInteractable), nameof(BaseUIInteractable.OnInteractInput))]
internal static class ResearchDroneInputPatch
{
    private static bool Prefix(BaseUIInteractable __instance)
    {
        if (ResearchDroneLock.Evaluate(__instance, out var info, out var keyId) != ResearchDroneLock.Verdict.Locked)
            return true;
        ResearchDroneLock.ShowLocked(info!, keyId);
        return false; // the drone's own OnInteract never runs
    }
}

/// <summary>
/// Sends a keyed drone's check when it is opened with the key — the poll ignores keyed drones.
/// Hooks the base <c>UIActivator.OnInteract</c> the drone's own OnInteract calls (already patched
/// safely for ranch plots). Also a fallback lock, if an interaction ever bypasses the input
/// handler; by then the drone is already marked read, so it warns.
/// </summary>
[HarmonyPatch(typeof(UIActivator), nameof(UIActivator.OnInteract))]
internal static class ResearchDroneInteractPatch
{
    private static bool Prefix(UIActivator __instance)
    {
        switch (ResearchDroneLock.Evaluate(__instance, out var info, out var keyId))
        {
            case ResearchDroneLock.Verdict.Allowed:
                Plugin.Instance.ApClient.SendCheck(info!.Id);
                return true;
            case ResearchDroneLock.Verdict.Locked:
                Logger.Warning($"[AP] Research drone '{info!.Name}' reached the fallback lock — the " +
                               "input-handler lock was bypassed, so the game has already marked it read");
                ResearchDroneLock.ShowLocked(info, keyId);
                return false;
            default:
                return true;
        }
    }
}
