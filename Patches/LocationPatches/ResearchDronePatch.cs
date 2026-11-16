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
/// With <c>drone_access_key</c> the poll skips every drone's state: the state proves nothing
/// for a keyed drone, whose check <see cref="ResearchDroneInteractPatch"/> sends when it is
/// opened with the key. The same poll keeps locked drones uninteractable
/// (<see cref="ResearchDroneLock.ApplyLocks"/>).
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

        if (_scan.Due())
        {
            Rescan();
            _nextPoll = 0f; // lock newly streamed drones now, not up to a second later
        }

        float now = UnityEngine.Time.unscaledTime;
        if (now < _nextPoll) return;
        _nextPoll = now + PollSeconds;

        ResearchDroneLock.ApplyLocks();

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
        ResearchDroneLock.Rescan();
    }
}

/// <summary>
/// The Drone Access Key lock. Only drones the option keys and that are not yet checked are
/// affected.
/// </summary>
/// <remarks>
/// <para>
/// <b>Primary lock: a locked drone's interaction colliders are disabled</b> — those on a layer in
/// the player's <c>UIDetector.interactionLayerMask</c> — so the detector's cast never finds it,
/// and re-enabled once the key arrives. Disabling the activator component is not enough:
/// confirmed in game, the detector still found and opened a disabled one. It has to stop
/// the interaction before the drone's own <c>ResearchDroneActivator.OnInteract</c> runs: that
/// method marks the drone read (<c>ResearchDroneModel.read</c>), wakes it and raises its game
/// event before calling the base method, and it can't be patched (it crashes the trampoline).
/// </para>
/// <para>
/// <b>Never hook <c>BaseUIInteractable.OnInteractInput</c> for this</b> (0.17.0 did). Installing
/// any Harmony patch on it — even one whose code never runs for the object in question —
/// crashed the game a few frames after pressing a comm station, with a fatal
/// "Unhandled exception. System.NullReferenceException" and no stack trace. Confirmed by
/// bisection: identical repro, no crash with the hook not installed. Most likely its tiny native
/// body is shared with unrelated methods, as with <c>ShouldShowCallout</c>.
/// </para>
/// </remarks>
internal static class ResearchDroneLock
{
    internal enum Verdict { NotKeyed, Allowed, Locked }

    // Within this range of a locked drone the "locked" popup shows, once per approach; it
    // clears past the larger radius.
    private const float PopupNearSqr = 4f * 4f;
    private const float PopupFarSqr  = 7f * 7f;

    private sealed class Tracked
    {
        public ResearchDroneActivator Activator = null!;
        public LocationInfo           Info      = null!;
        public List<Collider>?        DisabledColliders; // non-null while locked by us
        public bool                   PlayerNear;
    }

    private static int _interactionMask; // 0 until the player's UIDetector is found

    private static readonly List<Tracked> _activators = new();

    /// <summary>Collects the loaded drones' activators. Called with the controller scan.</summary>
    internal static void Rescan()
    {
        // Activators from the previous scan that are still loaded keep their state, so a
        // drone we disabled is re-enabled when its key arrives.
        var previous = new Dictionary<System.IntPtr, Tracked>();
        foreach (var t in _activators)
            if (t.Activator != null) previous[t.Activator.Pointer] = t;
        _activators.Clear();

        var all = Resources.FindObjectsOfTypeAll<ResearchDroneActivator>();
        for (int i = 0; i < all.Length; i++)
        {
            var act = all[i];
            if (act == null || !act.gameObject.scene.IsValid()) continue;
            if (previous.TryGetValue(act.Pointer, out var known)) { _activators.Add(known); continue; }

            string? entryName = null;
            try { entryName = act._researchDroneController?._researchDroneEntry?.name; } catch { }
            if (string.IsNullOrEmpty(entryName)) continue;
            if (!LocationTable.TryGetByEntryName(entryName, out var info) || info == null) continue;

            _activators.Add(new Tracked { Activator = act, Info = info });
        }
    }

    /// <summary>
    /// Disables each locked drone's activator and re-enables those we disabled once unlocked.
    /// Also shows the "locked" popup as the player approaches one.
    /// </summary>
    internal static void ApplyLocks()
    {
        if (_activators.Count == 0) return;

        Vector3? playerPos = null;
        try
        {
            var player = SceneContext.Instance?.Player;
            if (player != null) playerPos = player.transform.position;
        }
        catch { /* scene tearing down */ }

        for (int i = _activators.Count - 1; i >= 0; i--)
        {
            var t = _activators[i];
            if (t.Activator == null) { _activators.RemoveAt(i); continue; } // unloaded with its zone

            bool locked = Evaluate(t.Info, out long keyId) == Verdict.Locked;

            if (locked && t.DisabledColliders == null)
            {
                Lock(t);
            }
            else if (!locked && t.DisabledColliders != null)
            {
                foreach (var c in t.DisabledColliders)
                    if (c != null) c.enabled = true;
                t.DisabledColliders = null;
                Logger.Info($"[AP] Research drone unlocked: {t.Info.Name}");
            }

            if (!locked || playerPos == null) { t.PlayerNear = false; continue; }

            float distSqr = (t.Activator.transform.position - playerPos.Value).sqrMagnitude;
            if (!t.PlayerNear && distSqr <= PopupNearSqr)
            {
                t.PlayerNear = true;
                ShowLocked(t.Info, keyId);
            }
            else if (t.PlayerNear && distSqr > PopupFarSqr)
            {
                t.PlayerNear = false;
            }
        }
    }

    /// <summary>
    /// Disables the drone's colliders that the interaction detector can hit. Logs every collider
    /// it considered, so a drone that still opens shows why.
    /// </summary>
    private static void Lock(Tracked t)
    {
        if (_interactionMask == 0 && !FindInteractionMask()) return; // no player yet — retry next poll

        var disabled = new List<Collider>();
        var report   = new System.Text.StringBuilder();

        // The activator's own subtree first; if nothing there is hittable, the whole drone.
        foreach (var root in new[] { t.Activator.gameObject, t.Activator._researchDroneController?.gameObject })
        {
            if (root == null) continue;
            var colliders = root.GetComponentsInChildren<Collider>(true);
            for (int i = 0; i < colliders.Length; i++)
            {
                var c = colliders[i];
                if (c == null || disabled.Contains(c)) continue;
                bool inMask = (_interactionMask & (1 << c.gameObject.layer)) != 0;
                report.Append($"\n      '{c.gameObject.name}' layer={LayerMask.LayerToName(c.gameObject.layer)}" +
                              $" trigger={c.isTrigger} enabled={c.enabled} inMask={inMask}");
                if (inMask && c.enabled)
                {
                    c.enabled = false;
                    disabled.Add(c);
                }
            }
            if (disabled.Count > 0) break;
        }

        t.DisabledColliders = disabled;
        if (disabled.Count == 0)
            Logger.Warning($"[AP] Research drone '{t.Info.Name}': no interaction collider found to " +
                           $"disable — it can still be opened. Colliders seen:{report}");
        else
            Logger.Info($"[AP] Research drone locked ({disabled.Count} interaction collider(s) " +
                        $"disabled): {t.Info.Name}{report}");
    }

    private static bool FindInteractionMask()
    {
        var detectors = Resources.FindObjectsOfTypeAll<UIDetector>();
        for (int i = 0; i < detectors.Length; i++)
        {
            var d = detectors[i];
            if (d == null || !d.gameObject.scene.IsValid()) continue;
            _interactionMask = d.interactionLayerMask.value;
            if (_interactionMask == 0) continue;
            Logger.Info($"[AP] UIDetector interaction mask: 0x{_interactionMask:X}");
            return true;
        }
        return false;
    }

    /// <summary>Lock state of an interactable, if it is a research drone.</summary>
    internal static Verdict Evaluate(BaseUIInteractable interactable, out LocationInfo? info, out long keyId)
    {
        info  = null;
        keyId = 0;
        if (!IsActive()) return Verdict.NotKeyed;

        var activator = interactable.TryCast<ResearchDroneActivator>();
        if (activator == null) return Verdict.NotKeyed;

        string? entryName = null;
        try { entryName = activator._researchDroneController?._researchDroneEntry?.name; } catch { }
        if (string.IsNullOrEmpty(entryName)) return Verdict.NotKeyed;
        if (!LocationTable.TryGetByEntryName(entryName, out info) || info == null) return Verdict.NotKeyed;

        return Evaluate(info, out keyId);
    }

    private static Verdict Evaluate(LocationInfo info, out long keyId)
    {
        keyId = 0;
        if (!IsActive()) return Verdict.NotKeyed;
        if (Plugin.Instance.SaveManager.IsChecked(info.Id)) return Verdict.NotKeyed;

        if (Archipelago.LocationKeys.RequiredKey(info) is not long key) return Verdict.NotKeyed;
        keyId = key;
        return Archipelago.LocationKeys.HasKey(key) ? Verdict.Allowed : Verdict.Locked;
    }

    private static bool IsActive()
        => Plugin.Instance.ModEnabled
           && Plugin.Instance.SaveManager.IsSaveBound
           && Plugin.Instance.ApClient.SlotData?.DroneAccessKey == true;

    internal static void ShowLocked(LocationInfo info, long keyId)
    {
        var keyName = ItemTable.Get(keyId)?.Name ?? "Drone Access Key";
        Logger.Info($"[AP] Research drone locked: {info.Name} — '{keyName}' not yet received");
        UI.ApPopup.ShowThrottled($"drone-key-{keyId}", "Research Drone is locked",
                                 "Archipelago", $"Requires: {keyName}");
    }
}

/// <summary>
/// Sends a keyed drone's check when it is opened with the key — the poll ignores keyed drones.
/// Hooks the base <c>UIActivator.OnInteract</c> the drone's own OnInteract calls (already patched
/// safely for ranch plots). Also a fallback lock, if a locked drone is ever interacted with
/// despite its disabled colliders; by then the drone is already marked read, so it warns.
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
                Logger.Warning($"[AP] Research drone '{info!.Name}' reached the fallback lock — " +
                               "disabling its interaction colliders did not stop it, so the game " +
                               "has already marked it read");
                ResearchDroneLock.ShowLocked(info, keyId);
                return false;
            default:
                return true;
        }
    }
}
