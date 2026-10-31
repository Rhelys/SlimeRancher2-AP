using SlimeRancher2AP.Archipelago;
using System.Collections.Generic;
using UnityEngine;
using ApItemInfo = Archipelago.MultiClient.Net.Models.ItemInfo;
using ItemFlags = Archipelago.MultiClient.Net.Enums.ItemFlags;

namespace SlimeRancher2AP.UI;

/// <summary>
/// How loudly each received item is announced (<c>item_notifications</c>), and the pacing.
/// Major popups (<see cref="ApPopup"/>) for progression/useful; corner text for the rest under
/// "all". Rate-limited, and silent after the goal — which releases everything left at once.
/// </summary>
public static class ItemNotifier
{
    /// <summary>How loudly a single item should be announced.</summary>
    private enum Tier { None, Minor, Major }

    // Longer than one popup's full display, so they don't interrupt each other.
    private const float MajorIntervalSeconds = 3f;

    // Overflow is counted and summarised in one popup.
    private const int MaxQueued = 20;

    private static readonly Queue<string> _majorQueue = new();
    private static float _nextMajorAt;
    private static int   _overflowCount;

    /// <summary>On scene change / disconnect.</summary>
    public static void Reset()
    {
        _majorQueue.Clear();
        _overflowCount = 0;
    }

    // -------------------------------------------------------------------------
    // Entry point
    // -------------------------------------------------------------------------

    /// <summary>Called once an item has actually been granted.</summary>
    public static void OnItemApplied(Data.ItemInfo item, ApItemInfo? apItem)
    {
        try
        {
            var tier = TierFor(item, apItem);
            if (tier == Tier.None) return;

            if (GoalHandler.IsGoalComplete) return;

            string label = apItem?.ItemName ?? item.Name;

            if (tier == Tier.Minor) { ShowMinor(label); return; }

            if (_majorQueue.Count >= MaxQueued) { _overflowCount++; return; }
            _majorQueue.Enqueue(label);
        }
        catch (System.Exception ex)
        {
            Logger.Warning($"[AP-Notify] OnItemApplied threw: {ex.Message}");
        }
    }

    /// <summary>
    /// The tier from the option and the item's AP flags (what the generator decided). Debug grants
    /// have no flags and fall back to <c>ItemType</c>.
    /// </summary>
    private static Tier TierFor(Data.ItemInfo item, ApItemInfo? apItem)
    {
        var mode = Plugin.Instance.ApClient.SlotData?.ItemNotifications ?? "progression_useful";
        if (mode == "none") return Tier.None;

        bool progression, useful;
        if (apItem != null)
        {
            progression = apItem.Flags.HasFlag(ItemFlags.Advancement);
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

        return mode == "all" ? Tier.Minor : Tier.None;
    }

    // -------------------------------------------------------------------------
    // Per-frame pump
    // -------------------------------------------------------------------------

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

        int dropped = _overflowCount;
        _overflowCount = 0;
        ApPopup.Show($"+{dropped} more item{(dropped == 1 ? "" : "s")}", "Archipelago", "Item received");
    }

#if DEBUG
    /// <summary>Debug: queues a major popup, bypassing the option check.</summary>
    public static void DebugShowMajor(string label) => _majorQueue.Enqueue(label);
#endif

    // -------------------------------------------------------------------------
    // Minor notification
    // -------------------------------------------------------------------------

    // Corner text, not the vanilla side list: feeding that needs an INotificationProvider,
    // i.e. an IL2CPP type registration.
    private static void ShowMinor(string label)
        => StatusHUD.Instance?.ShowNotification($"Received: {label}");
}
