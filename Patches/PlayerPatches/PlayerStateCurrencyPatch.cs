using HarmonyLib;
using Il2CppMonomiPark.SlimeRancher.DataModel;
using Il2CppMonomiPark.SlimeRancher.Economy;
using SlimeRancher2AP.Archipelago;

namespace SlimeRancher2AP.Patches.PlayerPatches;

/// <summary>
/// Shared rules for Newbucks the game pays the player: what is scaled by
/// <c>newbucks_multiplier</c>, and what counts toward the <c>newbucks</c> goal.
/// </summary>
/// <remarks>
/// <para>
/// <b>Two levels.</b> Every payout goes through <c>PlayerState.AddCurrency</c>, which writes the
/// model via <c>PlayerModel.AddCurrency</c>. The Quantum Cloud's "Send to Market" passes a
/// <b>null</b> currency, which the original resolves to the default (Newbucks) — so at the
/// PlayerState level a cloud sale cannot be recognised, and was neither scaled nor counted
/// (confirmed by trace: PlayerState saw <c>type=null</c>, the model then received Newbucks 12).
/// Each payout is handled exactly once: the PlayerState patches own every payout whose currency
/// they can see, and announce the model write that follows
/// (<see cref="ConsumeExpectedModelWrite"/>); <see cref="PlayerModelAddCurrencyPatch"/> handles
/// the rest, where the currency has been resolved. Scaling at the PlayerState level where
/// possible keeps the on-screen "+N" in agreement with the wallet.
/// </para>
///
/// <para>
/// <b>What is deliberately excluded</b>, via <c>ItemHandler.IsGrantingCurrency</c>, which stays
/// set across the inner model call:
/// <list type="bullet">
///   <item>Archipelago Newbucks filler (250/500/1000) — balanced as item-pool rewards, so
///   neither scaled nor counted as earned.</item>
///   <item><c>RanchPlotHandler.RefundNewbucks</c> — a scaled refund would return more than the
///   plot cost, an unlimited money loop.</item>
/// </list>
/// Negative adjustments are never touched: scaling a debit would charge more, not pay more.
/// </para>
/// </remarks>
internal static class NewbucksEarnings
{
    // The model write PlayerState.AddCurrency is about to make: the amount, already scaled, and
    // the frame. The model-level hook skips exactly that write, once.
    //
    // An exact, announced handoff rather than "a PlayerState call is in progress". A scope guard
    // also covers the null-currency cloud call, which PlayerState cannot handle, and so swallowed
    // every cloud payout. Only calls with a visible currency are announced (see the Prefix).
    private static int _expectedAdjust = int.MinValue;
    private static int _expectedFrame  = -1;

    internal static void ExpectModelWrite(int adjust)
    {
        _expectedAdjust = adjust;
        _expectedFrame  = UnityEngine.Time.frameCount;
    }

    internal static void ClearExpectedModelWrite() => _expectedFrame = -1;

    /// <summary>
    /// True — and clears the expectation — when this model write is the one PlayerState announced.
    /// </summary>
    internal static bool ConsumeExpectedModelWrite(int adjust)
    {
        if (_expectedFrame != UnityEngine.Time.frameCount || _expectedAdjust != adjust) return false;
        _expectedFrame = -1;
        return true;
    }

    /// <summary>True when this currency is Newbucks rather than energy (rad) or keys.</summary>
    /// <remarks>
    /// <para>
    /// Compared by <c>ICurrency.PersistenceId</c> — an interface member, so it reads correctly
    /// from any implementation — against the Newbucks definition, which is found by name.
    /// </para>
    /// <para>
    /// The Newbucks id is resolved here on demand. Matching on a PersistenceId cached by the
    /// newbucks goal was tried once, and failed on every other goal because only that goal ever
    /// filled the cache. A null currency (the Quantum Cloud's) returns false — see the class
    /// remarks for how that payout is still handled.
    /// </para>
    /// </remarks>
    internal static bool IsNewbucks(ICurrency? currency)
    {
        if (currency == null) return false;
        try
        {
            int id = NewbucksId();
            return id != int.MinValue && currency.PersistenceId == id;
        }
        catch { return false; }
    }

    private static int _newbucksId = int.MinValue;
    private static int _newbucksLookups;

    /// <summary>PersistenceId of the Newbucks definition, or int.MinValue if not loaded yet.</summary>
    /// <remarks>
    /// Bounded retries: the definition may not be loaded during early startup, so one miss must
    /// not be final, but an unbounded retry would scan every asset on every currency change.
    /// </remarks>
    private static int NewbucksId()
    {
        if (_newbucksId != int.MinValue || _newbucksLookups >= 20) return _newbucksId;
        _newbucksLookups++;
        foreach (var def in UnityEngine.Resources.FindObjectsOfTypeAll<CurrencyDefinition>())
        {
            if (def != null && def.name.IndexOf("Newbucks", System.StringComparison.OrdinalIgnoreCase) >= 0)
            {
                _newbucksId = def.PersistenceId;
                break;
            }
        }
        return _newbucksId;
    }

    /// <summary>
    /// <paramref name="adjust"/> scaled by <c>newbucks_multiplier</c>, or unchanged when the
    /// payout is excluded or the multiplier is 100%.
    /// </summary>
    internal static int Scale(ICurrency? currency, int adjust)
    {
        try
        {
            if (adjust <= 0) return adjust;
            if (!Plugin.Instance.ModEnabled) return adjust;
            if (ItemHandler.IsGrantingCurrency) return adjust;

            var pct = Plugin.Instance.ApClient.SlotData?.NewbucksMultiplier ?? 100;
            if (pct == 100) return adjust;
            if (!IsNewbucks(currency)) return adjust;

            // Floor of 1: a payment worth something before scaling must stay worth something,
            // or a low multiplier silently turns small sales into nothing.
            return System.Math.Max(1, (int)System.Math.Round(adjust * pct / 100.0,
                                                             System.MidpointRounding.AwayFromZero));
        }
        catch (System.Exception ex)
        {
            // Never let a scaling failure cost the player a payment.
            Logger.Warning($"[AP] Newbucks scaling threw: {ex.Message}");
            return adjust;
        }
    }

    /// <summary>Adds a payout to the persisted <c>newbucks</c> goal counter if it qualifies.</summary>
    /// <remarks>
    /// Counts whenever the save is AP-bound and trusted, connected or not — the rule every goal
    /// uses (see GoalHandler). It used to require a live connection, so sales made offline never
    /// counted toward the goal at all.
    /// </remarks>
    internal static void Count(ICurrency? currency, int adjust)
    {
        try
        {
            if (adjust <= 0) return;
            if (ItemHandler.IsGrantingCurrency) return;   // AP filler and plot refunds are not earned
            if (!Plugin.Instance.ModEnabled || !Plugin.Instance.SaveManager.IsSaveBound) return;
            if (Plugin.Instance.ApClient.SlotData?.Goal != "newbucks") return;
            if (!IsNewbucks(currency)) return;
            if (!SaveGuard.IsSaveTrusted()) return;

            Plugin.Instance.SaveManager.AccumulateNewbucks(adjust);
        }
        catch (System.Exception ex)
        {
            Logger.Warning($"[AP] Newbucks goal counting threw: {ex.Message}");
        }
    }
}

/// <summary>Scales Newbucks paid through <c>PlayerState.AddCurrency</c>.</summary>
/// <remarks>
/// A Prefix, so the scaled value is what reaches the model, the wallet and the "+N"
/// notification. <see cref="PlayerStateAddCurrencyPatch"/> then counts the scaled amount, so the
/// goal counter never disagrees with the player's money. It also announces the model write that
/// follows, so <see cref="PlayerModelAddCurrencyPatch"/> does not handle it a second time.
/// <c>AddCurrency</c> is CallerCount(6).
/// </remarks>
[HarmonyPatch(typeof(PlayerState), nameof(PlayerState.AddCurrency))]
internal static class PlayerStateAddCurrencyScalePatch
{
    private static void Prefix(ICurrency currencyDefinition, ref int adjust)
    {
        adjust = NewbucksEarnings.Scale(currencyDefinition, adjust);

        // A null currency is resolved to the default (Newbucks) inside the original, so this
        // level cannot tell what it is. Leave that write to PlayerModelAddCurrencyPatch, which
        // sees the resolved definition, rather than announcing it as handled here.
        if (currencyDefinition != null) NewbucksEarnings.ExpectModelWrite(adjust);
    }

    private static void Postfix() => NewbucksEarnings.ClearExpectedModelWrite();
}

/// <summary>
/// Counts Newbucks paid through <c>PlayerState.AddCurrency</c> toward the <c>newbucks</c> goal.
/// </summary>
/// <remarks>
/// <c>PlayerModel.AmountEverCollected</c> is never updated by any code path in SR2 (vestigial),
/// so the mod keeps its own counter in <see cref="SaveData.ApSaveManager.NewbucksEarned"/>.
/// </remarks>
[HarmonyPatch(typeof(PlayerState), nameof(PlayerState.AddCurrency))]
internal static class PlayerStateAddCurrencyPatch
{
    private static void Postfix(ICurrency currencyDefinition, int adjust)
        => NewbucksEarnings.Count(currencyDefinition, adjust);
}

/// <summary>
/// Scales and counts Newbucks payouts that PlayerState could not identify — the Quantum
/// Cloud's "Send to Market", which passes a null currency that is only resolved to Newbucks
/// inside <c>PlayerState.AddCurrency</c>.
/// </summary>
/// <remarks>
/// Payouts PlayerState announced (<see cref="NewbucksEarnings.ConsumeExpectedModelWrite"/>) were
/// already handled there, so they pass untouched. Patched method:
/// <c>PlayerModel.AddCurrency(ICurrency, int)</c> — CallerCount(1).
/// </remarks>
[HarmonyPatch(typeof(PlayerModel), nameof(PlayerModel.AddCurrency))]
internal static class PlayerModelAddCurrencyPatch
{
    private static void Prefix(ICurrency currencyDefinition, ref int adjust, out int __state)
    {
        __state = 0;
        if (NewbucksEarnings.ConsumeExpectedModelWrite(adjust)) return;   // handled at PlayerState
        if (adjust <= 0 || !NewbucksEarnings.IsNewbucks(currencyDefinition)) return;

        int scaled = NewbucksEarnings.Scale(currencyDefinition, adjust);
        if (scaled != adjust)
            Logger.Info($"[AP] Newbucks paid with no currency named (Quantum Cloud sale): {adjust} → {scaled}");
        adjust = scaled;
        __state = scaled;
    }

    private static void Postfix(ICurrency currencyDefinition, int __state)
    {
        if (__state > 0) NewbucksEarnings.Count(currencyDefinition, __state);
    }
}
