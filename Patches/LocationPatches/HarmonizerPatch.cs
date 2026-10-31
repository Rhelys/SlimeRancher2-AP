using HarmonyLib;
using Il2CppMonomiPark.SlimeRancher.Prestige;
using Il2CppMonomiPark.SlimeRancher.World;
using SlimeRancher2AP.Archipelago;
using SlimeRancher2AP.Data;

namespace SlimeRancher2AP.Patches.LocationPatches;

/// <summary>
/// Hitting a Harmonizer bell sends its check and leaves the bell silent; it rings only when its
/// item arrives (<see cref="HarmonizerHandler"/>). <c>randomize_harmonizers</c> only.
/// </summary>
/// <remarks>
/// A hit runs <c>BellTechActivator</c> → <c>WorldStateInvisibleSwitch.Activate</c> →
/// <c>SetStateForAll(DOWN)</c> (confirmed by trace, 2026-10-31). Blocking <c>Activate</c> stops the
/// ring; the mod's own rings call <c>SetStateForAll</c> directly and are unaffected.
/// Patched method: <c>WorldStateInvisibleSwitch.Activate()</c> — CallerCount(2), virtual. Only
/// bells with a Harmonizer location are touched; beam receivers and other puzzles pass through.
/// </remarks>
[HarmonyPatch(typeof(WorldStateInvisibleSwitch), nameof(WorldStateInvisibleSwitch.Activate))]
internal static class HarmonizerBellPatch
{
    private static bool Prefix(WorldStateInvisibleSwitch __instance)
    {
        try
        {
            if (!HarmonizerHandler.IsActive) return true;

            var switchId = __instance.SwitchDefinition?.ID;
            if (string.IsNullOrEmpty(switchId)) return true;
            if (!LocationTable.TryGetHarmonizerBySwitchId(switchId!, out var bell) || bell == null) return true;

            Plugin.Instance.ApClient.SendCheck(bell.Id);

            // Already rung by its item — a repeat hit needs no explanation.
            bool rung = __instance._model != null && __instance._model.state == SwitchHandler.State.DOWN;
            if (!rung)
                UI.ApPopup.ShowThrottled($"harmonizer-{switchId}", $"{HarmonizerHandler.ItemNameFor(bell)} not found",
                                         "Archipelago", "This Harmonizer rings when you receive its item");
            return false;
        }
        catch (System.Exception ex)
        {
            // Never break the bell: worst case it rings without its item.
            Logger.Warning($"[AP-Harmonizer] Bell patch threw: {ex.Message}");
            return true;
        }
    }
}

/// <summary>
/// Turns claiming the Nullifier at Gigi's kiosk into a check whenever the Grey Labyrinth is in
/// scope; the Nullifier itself then arrives as an AP item.
/// </summary>
/// <remarks>
/// The kiosk is a reward track (<c>RewardsKiosk</c>, <c>HighTierEndGameRewards</c>), not a shop.
/// Each reward entry is dispensed through <c>DispenseRewards(rewardTrackId, rewards)</c> with its
/// stable <c>UniqueId</c>; the Nullifier's entry holds only the Nullifier blueprint, so skipping
/// the dispense withholds exactly that and nothing else. Other entries (Rainbow Coins, the Tank
/// Booster and Drone Station components) pass through.
/// Gated by slot data rather than <c>IsLocationInSeed</c>, which is empty offline — the purchase
/// must not hand out the real Nullifier just because the player is disconnected.
/// Patched method: <c>RewardsKiosk.DispenseRewards</c> — CallerCount(3).
/// </remarks>
[HarmonyPatch(typeof(RewardsKiosk), nameof(RewardsKiosk.DispenseRewards))]
internal static class KioskRewardPatch
{
    private static bool Prefix(string rewardTrackId)
    {
        try
        {
            if (!Plugin.Instance.ModEnabled || !Plugin.Instance.SaveManager.IsSaveBound) return true;
            if (!(Plugin.Instance.ApClient.SlotData?.GreyLabyrinthInScope ?? false)) return true;
            if (!LocationTable.TryGetKioskReward(rewardTrackId, out var loc) || loc == null) return true;

            Logger.Info($"[AP] Gigi's kiosk: '{loc.EntryName}' reward withheld → check {loc.Id} ({loc.Name})");
            Plugin.Instance.ApClient.SendCheck(loc.Id);
            UI.StatusHUD.Instance?.ShowNotification($"AP ✓  {loc.Name}");
            return false;
        }
        catch (System.Exception ex)
        {
            Logger.Warning($"[AP] Kiosk reward patch threw: {ex.Message}");
            return true;
        }
    }
}
