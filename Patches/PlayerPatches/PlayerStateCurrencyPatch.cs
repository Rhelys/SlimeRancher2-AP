using HarmonyLib;
using Il2CppMonomiPark.SlimeRancher.Economy;
using SlimeRancher2AP.Archipelago;

namespace SlimeRancher2AP.Patches.PlayerPatches;

/// <summary>
/// Tracks cumulative newbucks earnings for the "newbucks" AP goal.
///
/// <para>
/// <c>PlayerModel.AmountEverCollected</c> is never updated by any code path in SR2
/// (not by plort selling, not by AddCurrency — it appears to be vestigial).
/// We therefore maintain our own counter in <see cref="SaveData.ApSaveManager.NewbucksEarned"/>
/// that persists across sessions in the per-seed BepInEx config file.
/// </para>
///
/// <para>
/// Only accumulates when:
/// <list type="bullet">
///   <item>The AP goal is "newbucks"</item>
///   <item>The player is connected to the AP server</item>
///   <item>The currency is Newbucks (matched by <c>PersistenceId</c>)</item>
///   <item><c>adjust</c> is positive (spending goes through <c>SpendCurrency</c>, not here)</item>
/// </list>
/// </para>
/// </summary>
[HarmonyPatch(typeof(PlayerState), nameof(PlayerState.AddCurrency))]
internal static class PlayerStateAddCurrencyPatch
{
    private static void Postfix(ICurrency currencyDefinition, int adjust)
    {
        if (adjust <= 0) return;
        // Newbucks granted BY the AP item pipeline (Newbucks filler) are not "earned" —
        // counting them would give free goal progress for receiving your own filler items.
        if (ItemHandler.IsGrantingCurrency) return;
        if (!Plugin.Instance.ApClient.IsConnected) return;
        if (Plugin.Instance.ApClient.SlotData?.Goal != "newbucks") return;

        // Filter to Newbucks only — the game also uses AddCurrency for energy (rad) and keys.
        var persistenceId = GoalHandler.NewbucksPersistenceId;
        if (persistenceId < 0) return;

        var def = currencyDefinition?.TryCast<CurrencyDefinition>();
        if (def == null || def.PersistenceId != persistenceId) return;

        Plugin.Instance.SaveManager.AccumulateNewbucks(adjust);
    }
}

/// <summary>
/// Scales Newbucks the game pays the player by the <c>newbucks_multiplier</c> slot data option.
/// </summary>
/// <remarks>
/// <para>
/// Runs as a Prefix so the scaled value is what reaches the model, the wallet, and the on-screen
/// "+N" notification — all three then agree. <see cref="PlayerStateAddCurrencyPatch"/> is a
/// Postfix on the same method, so the <c>newbucks</c> goal counts the scaled amount too, which is
/// the intended behaviour: the goal counter never disagrees with the player's money.
/// </para>
///
/// <para>
/// <b>What is deliberately NOT scaled.</b> Both exclusions go through
/// <c>ItemHandler.IsGrantingCurrency</c>:
/// <list type="bullet">
///   <item>
///     Archipelago Newbucks filler (250/500/1000). Those are balanced as item-pool rewards, so
///     scaling them would make a check's value depend on an economy setting.
///   </item>
///   <item>
///     <c>RanchPlotHandler.RefundNewbucks</c>. A scaled refund would return more than the plot
///     cost, making buy-then-refund an unlimited money loop.
///   </item>
/// </list>
/// </para>
///
/// <para>
/// Negative adjustments are left alone. Spending goes through <c>SpendCurrency</c> rather than
/// here, but if anything ever routes a debit through this method, scaling it would make a high
/// multiplier charge the player more rather than pay them more.
/// </para>
///
/// <para>
/// <c>AddCurrency</c> is CallerCount(6) and already carries a Postfix in shipping builds.
/// </para>
/// </remarks>
[HarmonyPatch(typeof(PlayerState), nameof(PlayerState.AddCurrency))]
internal static class PlayerStateAddCurrencyScalePatch
{
    /// <summary>True when this currency is Newbucks rather than energy or keys.</summary>
    private static bool IsNewbucks(ICurrency? currency)
    {
        try
        {
            var def = currency?.TryCast<CurrencyDefinition>();
            return def != null
                && def.name.IndexOf("Newbucks", System.StringComparison.OrdinalIgnoreCase) >= 0;
        }
        catch { return false; }
    }

    private static void Prefix(ICurrency currencyDefinition, ref int adjust)
    {
        try
        {
            if (adjust <= 0) return;                      // debits and no-ops are untouched
            if (!Plugin.Instance.ModEnabled) return;
            if (ItemHandler.IsGrantingCurrency) return;   // AP filler and plot refunds

            var pct = Plugin.Instance.ApClient.SlotData?.NewbucksMultiplier ?? 100;
            if (pct == 100) return;

            // Newbucks only — the game also routes energy (rad) and keys through AddCurrency.
            //
            // Identified from the definition in hand rather than by PersistenceId. The id comes
            // from a cache that only the newbucks goal used to populate, so on any other goal it
            // read -1 and this guard rejected every genuine payment — which is exactly how this
            // shipped broken the first time. Name matching needs no shared state and no lookup,
            // and it is the same test TryCacheNewbucksDef uses to find the definition anyway.
            if (!IsNewbucks(currencyDefinition)) return;

            // Floor of 1: a payment that was worth something before scaling must stay worth
            // something, or a low multiplier silently turns small sales into nothing.
            int scaled = System.Math.Max(1, (int)System.Math.Round(adjust * pct / 100.0,
                                                                   System.MidpointRounding.AwayFromZero));
            if (scaled == adjust) return;

            adjust = scaled;
        }
        catch (System.Exception ex)
        {
            // Never let a scaling failure cost the player a payment — leave adjust untouched.
            Logger.Warning($"[AP] PlayerStateAddCurrencyScalePatch threw: {ex.Message}");
        }
    }
}
