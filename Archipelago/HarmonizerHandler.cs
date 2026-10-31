using System.Collections.Generic;
using Il2CppMonomiPark.SlimeRancher.World;
using SlimeRancher2AP.Data;
using UnityEngine;
using ApItemInfo = Archipelago.MultiClient.Net.Models.ItemInfo;

namespace SlimeRancher2AP.Archipelago;

/// <summary>
/// <c>randomize_harmonizers</c>: the five Grey Labyrinth bells are checks, and each rings only
/// when its "Harmonizer - &lt;subzone&gt;" item arrives.
/// </summary>
/// <remarks>
/// <para>
/// Naming is inverted from what the assets suggest: the in-game Harmonizers are the
/// <c>labySwitchBell</c> world switches, while the gadget asset named <c>Harmonizer</c> is the
/// Nullifier. See CLAUDE.md, "Prismacore encounter prerequisites".
/// </para>
/// <para>
/// <b>Ringing a bell for an item.</b> A loaded bell is rung with
/// <c>SetStateForAll(DOWN, immediate:false)</c>, the path a real ring takes, so its effects
/// play. A bell whose scene is not loaded is written straight into the save's switch registry
/// (<c>GameModel.AllSwitches()</c>), which the bell reads when its scene loads; an entry that does
/// not exist yet is created with <c>InitializeSwitchModel</c>. <c>SetStateForAll</c> does not go
/// through <c>Activate</c>, so <c>HarmonizerBellPatch</c> never blocks the mod's own rings.
/// </para>
/// </remarks>
public static class HarmonizerHandler
{
    /// <summary>True when bells are checks for the bound save.</summary>
    public static bool IsActive
        => Plugin.Instance.ModEnabled
        && Plugin.Instance.SaveManager.IsSaveBound
        && (Plugin.Instance.ApClient.SlotData?.RandomizeHarmonizers ?? false);

    private enum RingResult { Rung, AlreadyRung, WrittenToSave, NotReady, Failed }

    // -------------------------------------------------------------------------
    // Item pipeline
    // -------------------------------------------------------------------------

    /// <summary>
    /// Rings the bell for a Harmonizer item. Returns false (and requeues) when the game model is
    /// not ready yet, so the watermark does not advance past an unapplied item.
    /// </summary>
    internal static bool Apply(ItemInfo item, ApItemInfo? apItem, int itemIndex)
    {
        var bell = BellForItem(item.Id);
        if (bell == null)
        {
            Logger.Warning($"[AP-Harmonizer] No bell for item {item.Name} ({item.Id})");
            return true;   // a data error retrying cannot fix
        }

        var result = RingBell(bell.GameObjectName, bell.SceneName);
        switch (result)
        {
            case RingResult.NotReady:
                if (apItem != null) Plugin.Instance.ApClient.RequeueItem(apItem, itemIndex);
                return false;
            case RingResult.Failed:
                // Logged in RingBell. Not requeued: it would fail every frame. Reconcile retries
                // whenever the bell is loaded.
                MarkReceivedDirty();
                return true;
            default:
                Logger.Info($"[AP-Harmonizer] {item.Name}: {Describe(result)}");
                MarkReceivedDirty();
                return true;
        }
    }

    private static string Describe(RingResult r) => r switch
    {
        RingResult.Rung          => "bell rung",
        RingResult.AlreadyRung   => "bell already rung",
        RingResult.WrittenToSave => "bell's scene not loaded — marked rung in the save",
        _                        => r.ToString(),
    };

    private static LocationInfo? BellForItem(long itemId)
    {
        string key = itemId.ToString();
        foreach (var loc in LocationTable.Harmonizers)
            if (loc.EntryName == key) return loc;
        return null;
    }

    /// <summary>The Harmonizer item name for a bell location ("Harmonizer - Dreamland").</summary>
    internal static string ItemNameFor(LocationInfo bell)
        => long.TryParse(bell.EntryName, out var id) && ItemTable.Get(id) is { } item
            ? item.Name
            : bell.Name.Replace("Ring ", "");

    // -------------------------------------------------------------------------
    // Ringing
    // -------------------------------------------------------------------------

    private static RingResult RingBell(string switchId, string sceneName)
    {
        var gameModel = SceneContext.Instance?.GameModel;
        if (gameModel == null) return RingResult.NotReady;

        try
        {
            var loaded = FindLoadedBell(switchId);
            if (loaded != null)
            {
                if (loaded._model != null && loaded._model.state == SwitchHandler.State.DOWN)
                    return RingResult.AlreadyRung;
                loaded.SetStateForAll(SwitchHandler.State.DOWN, false);
                return RingResult.Rung;
            }

            var all = gameModel.AllSwitches();
            if (all != null && all.TryGetValue(switchId, out var model) && model != null)
            {
                if (model.state == SwitchHandler.State.DOWN) return RingResult.AlreadyRung;
                model.state = SwitchHandler.State.DOWN;
                return RingResult.WrittenToSave;
            }

            // Never loaded on this save, so the registry has no entry yet.
            var created = gameModel.InitializeSwitchModel(switchId);
            if (created == null)
            {
                Logger.Warning($"[AP-Harmonizer] Could not create a switch entry for '{switchId}' ({sceneName})");
                return RingResult.Failed;
            }
            created.state = SwitchHandler.State.DOWN;
            return RingResult.WrittenToSave;
        }
        catch (System.Exception ex)
        {
            Logger.Warning($"[AP-Harmonizer] Ringing '{switchId}' ({sceneName}) failed: {ex.Message}");
            return RingResult.Failed;
        }
    }

    /// <summary>The scene instance of the bell with this switch ID, or null if not loaded.</summary>
    internal static WorldStateInvisibleSwitch? FindLoadedBell(string switchId)
    {
        foreach (var sw in Resources.FindObjectsOfTypeAll<WorldStateInvisibleSwitch>())
        {
            if (sw == null) continue;
            try
            {
                if (!sw.gameObject.scene.IsValid()) continue;   // prefab
                if (sw.SwitchDefinition?.ID == switchId) return sw;
            }
            catch { /* half-initialised during scene load */ }
        }
        return null;
    }

    // -------------------------------------------------------------------------
    // Reconcile
    // -------------------------------------------------------------------------

    private const float ReconcileSeconds = 5f;
    private static float _nextReconcile;

    private static HashSet<long>? _received;
    private static bool _receivedDirty = true;

    internal static void MarkReceivedDirty() => _receivedDirty = true;

    /// <summary>Clears cached state. Called on disconnect.</summary>
    public static void Reset()
    {
        _received      = null;
        _receivedDirty = true;
        _nextReconcile = 0f;
    }

    /// <summary>
    /// Every few seconds, rings any loaded bell whose item has been received but which still
    /// reads unrung — a safety net for a registry write the bell did not pick up, or an item
    /// applied while its ring failed. Needs the server snapshot, so it only runs while connected;
    /// offline the save already holds what was rung.
    /// </summary>
    public static void Tick()
    {
        if (!IsActive) return;
        float now = Time.unscaledTime;
        if (now < _nextReconcile) return;
        _nextReconcile = now + ReconcileSeconds;

        var received = ReceivedHarmonizers();
        if (received == null || received.Count == 0) return;

        foreach (var bell in LocationTable.Harmonizers)
        {
            if (!long.TryParse(bell.EntryName, out var itemId) || !received.Contains(itemId)) continue;

            var sw = FindLoadedBell(bell.GameObjectName);
            if (sw == null) continue;
            try
            {
                if (sw._model != null && sw._model.state == SwitchHandler.State.DOWN) continue;
                sw.SetStateForAll(SwitchHandler.State.DOWN, false);
                Logger.Info($"[AP-Harmonizer] Reconcile: rang {bell.Name.Replace("Ring ", "")} (item held, bell was silent)");
            }
            catch (System.Exception ex)
            {
                Logger.Warning($"[AP-Harmonizer] Reconcile of '{bell.GameObjectName}' failed: {ex.Message}");
            }
        }
    }

    private static HashSet<long>? ReceivedHarmonizers()
    {
        if (!_receivedDirty && _received != null) return _received;

        var snapshot = Plugin.Instance.ApClient.Session?.Items?.AllItemsReceived;
        if (snapshot == null) return null;   // offline — stay dirty, retry once connected

        var set = new HashSet<long>();
        for (int i = 0; i < snapshot.Count; i++)
        {
            var info = ItemTable.Get(snapshot[i].ItemId);
            if (info != null && info.Type == ItemType.Harmonizer) set.Add(info.Id);
        }
        _received      = set;
        _receivedDirty = false;
        return set;
    }
}
