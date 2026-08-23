using SlimeRancher2AP.Archipelago;
using System.Collections.Generic;
using UnityEngine;
using ApItemInfo = Archipelago.MultiClient.Net.Models.ItemInfo;
using ItemFlags = Archipelago.MultiClient.Net.Enums.ItemFlags;

namespace SlimeRancher2AP.UI;

/// <summary>
/// Decides how loudly each received Archipelago item is announced, and paces the announcements.
///
/// <para>
/// Policy only — the drawing is <see cref="ApPopup"/>'s job. Progression and useful items get the
/// game's major popup (the one shown for a new Slimepedia entry); filler and traps get the quiet
/// corner text, and only when the player opted into "all". Driven by the apworld's
/// <c>item_notifications</c> option via <see cref="SlotData.ItemNotifications"/>.
/// </para>
///
/// <para>
/// <b>Volume control.</b> Two independent guards: a rate limit, so a burst of incoming items
/// drips out instead of thrashing the popup stack; and full suppression once
/// <see cref="GoalHandler.IsGoalComplete"/> — finishing your own goal releases everything you
/// have left, which is hundreds of items and never worth announcing.
/// </para>
/// </summary>
public static class ItemNotifier
{
    /// <summary>How loudly a single item should be announced.</summary>
    private enum Tier { None, Minor, Major }

    // The vanilla popup animates in, holds, and animates out; anything faster than this just
    // interrupts itself. 3s is comfortably longer than the stack's own display duration.
    private const float MajorIntervalSeconds = 3f;

    // Beyond this the player is not reading them anyway. Overflow is counted, not dropped
    // silently, and reported in one summary popup once the backlog clears.
    private const int MaxQueued = 20;

    private static readonly Queue<string> _majorQueue = new();
    private static float _nextMajorAt;
    private static int   _overflowCount;

    /// <summary>Clears any pending announcements. Call on scene change / disconnect.</summary>
    public static void Reset()
    {
        _majorQueue.Clear();
        _overflowCount = 0;
    }

    // -------------------------------------------------------------------------
    // Entry point
    // -------------------------------------------------------------------------

    /// <summary>
    /// Called from <c>ItemHandler.Apply</c> once an item has actually been granted.
    /// Decides the tier from the player's option plus the item's AP flags and either shows the
    /// notification or queues it.
    /// </summary>
    public static void OnItemApplied(Data.ItemInfo item, ApItemInfo? apItem)
    {
        try
        {
            var tier = TierFor(item, apItem);
            if (tier == Tier.None) return;

            // Suppress once our own goal is done — the server releases every remaining item at
            // that moment. Mirrors how TrapHandler already suppresses disruptive traps.
            if (GoalHandler.IsGoalComplete) return;

            string label = apItem?.ItemName ?? item.Name;

            if (tier == Tier.Minor) { ShowMinor(label); return; }

            if (_majorQueue.Count >= MaxQueued) { _overflowCount++; return; }
            _majorQueue.Enqueue(label);
        }
        catch (System.Exception ex)
        {
            // A notification must never break item application.
            Logger.Warning($"[AP-Notify] OnItemApplied threw: {ex.Message}");
        }
    }

    /// <summary>
    /// Maps the player's <c>item_notifications</c> option and the item's AP flags to a tier.
    ///
    /// AP flags are authoritative because they reflect what the generator actually decided for
    /// this item in this seed. <paramref name="apItem"/> is null for debug-panel grants, where
    /// the local table's <c>ItemType</c> is the only classification available.
    /// </summary>
    private static Tier TierFor(Data.ItemInfo item, ApItemInfo? apItem)
    {
        var mode = Plugin.Instance.ApClient.SlotData?.ItemNotifications ?? "progression_useful";
        if (mode == "none") return Tier.None;

        bool progression, useful;
        if (apItem != null)
        {
            progression = apItem.Flags.HasFlag(ItemFlags.Advancement);
            // AP models "useful" as NeverExclude.
            useful      = apItem.Flags.HasFlag(ItemFlags.NeverExclude);
        }
        else
        {
            progression = item.Type is not Data.ItemType.Filler
                                   and not Data.ItemType.Trap
                                   and not Data.ItemType.Useful;
            useful      = item.Type is Data.ItemType.Useful;
        }

        if (progression) return Tier.Major;
        if (useful)      return mode == "progression" ? Tier.None : Tier.Major;

        // Filler and traps: announced quietly, and only in "all".
        return mode == "all" ? Tier.Minor : Tier.None;
    }

    // -------------------------------------------------------------------------
    // Per-frame pump
    // -------------------------------------------------------------------------

    /// <summary>Called every frame from <c>ApUpdateBehaviour.Update</c>.</summary>
    public static void Tick()
    {
        if (_majorQueue.Count == 0 && _overflowCount == 0) return;

        float now = Time.unscaledTime;
        if (now < _nextMajorAt) return;
        _nextMajorAt = now + MajorIntervalSeconds;

        if (_majorQueue.Count > 0)
        {
            ApPopup.Show(_majorQueue.Dequeue(), "Archipelago", "Item received");
            return;
        }

        // Backlog drained but items were dropped — say so once rather than losing them silently.
        int dropped = _overflowCount;
        _overflowCount = 0;
        ApPopup.Show($"+{dropped} more item{(dropped == 1 ? "" : "s")}", "Archipelago", "Item received");
    }

#if DEBUG
    /// <summary>
    /// Debug panel only: queues a major popup directly, skipping the option/flag check so the
    /// visuals and the rate limit can be exercised without a connected multiworld.
    /// </summary>
    public static void DebugShowMajor(string label) => _majorQueue.Enqueue(label);
#endif

    // -------------------------------------------------------------------------
    // Minor notification
    // -------------------------------------------------------------------------

    // The vanilla side-notification list is driven by INotificationProvider implementations,
    // which cannot be implemented from managed code without registering an IL2CPP type. Rather
    // than inject a provider, filler/trap announcements use the mod's existing corner text —
    // it is already the quiet channel, and this keeps the risky interop confined to the major
    // popup path where the visual payoff justifies it.
    private static void ShowMinor(string label)
        => StatusHUD.Instance?.ShowNotification($"Received: {label}");
}
