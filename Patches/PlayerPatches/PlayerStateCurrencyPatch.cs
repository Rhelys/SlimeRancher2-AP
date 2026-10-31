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
/// <b>Two levels.</b> Payouts go through <c>PlayerState.AddCurrency</c> and then
/// <c>PlayerModel.AddCurrency</c>. A Quantum Cloud sale passes a null currency, resolved to
/// Newbucks only inside the original, so PlayerState can't recognise it. Each payout is handled
/// once: PlayerState handles what it can see (keeping the "+N" in step with the wallet) and
/// announces the model write that follows (<see cref="ConsumeExpectedModelWrite"/>);
/// <see cref="PlayerModelAddCurrencyPatch"/> takes the rest.
/// </para>
/// <para>
/// <b>Excluded</b> via <c>ItemHandler.IsGrantingCurrency</c>: AP Newbucks filler, and plot refunds
/// (a scaled refund would be a money loop). Debits are never scaled.
/// </para>
/// </remarks>
internal static class NewbucksEarnings
{
    // The (scaled) model write PlayerState announced, and its frame; the model hook skips exactly
    // that write once. Not a "PlayerState is running" scope — that also swallowed cloud payouts.
    private static int _expectedAdjust = int.MinValue;
    private static int _expectedFrame  = -1;

    internal static void ExpectModelWrite(int adjust)
    {
        _expectedAdjust = adjust;
        _expectedFrame  = UnityEngine.Time.frameCount;
    }

    internal static void ClearExpectedModelWrite() => _expectedFrame = -1;

    /// <summary>True (and clears it) when this is the model write PlayerState announced.</summary>
    internal static bool ConsumeExpectedModelWrite(int adjust)
    {
        if (_expectedFrame != UnityEngine.Time.frameCount || _expectedAdjust != adjust) return false;
        _expectedFrame = -1;
        return true;
    }

    /// <summary>
    /// True for Newbucks (not energy or keys), by <c>PersistenceId</c>. A null currency is false.
    /// </summary>
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

    /// <summary>
    /// The Newbucks PersistenceId, or int.MinValue. Retries are bounded: early on it may not be
    /// loaded, but each try scans every asset.
    /// </summary>
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

            // Floor of 1, so small sales never round to nothing.
            return System.Math.Max(1, (int)System.Math.Round(adjust * pct / 100.0,
                                                             System.MidpointRounding.AwayFromZero));
        }
        catch (System.Exception ex)
        {
            Logger.Warning($"[AP] Newbucks scaling threw: {ex.Message}");
            return adjust;
        }
    }

    /// <summary>Counts a qualifying payout toward the <c>newbucks</c> goal — offline too.</summary>
    internal static void Count(ICurrency? currency, int adjust)
    {
        try
        {
            if (adjust <= 0) return;
            if (ItemHandler.IsGrantingCurrency) return;
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

/// <summary>
/// Scales Newbucks in a Prefix, so the wallet, the "+N" and the goal counter all see the same
/// amount, and announces the model write that follows.
/// </summary>
[HarmonyPatch(typeof(PlayerState), nameof(PlayerState.AddCurrency))]
internal static class PlayerStateAddCurrencyScalePatch
{
    private static void Prefix(ICurrency currencyDefinition, ref int adjust)
    {
        adjust = NewbucksEarnings.Scale(currencyDefinition, adjust);

        // A null currency is left to PlayerModelAddCurrencyPatch.
        if (currencyDefinition != null) NewbucksEarnings.ExpectModelWrite(adjust);
    }

    private static void Postfix() => NewbucksEarnings.ClearExpectedModelWrite();
}

/// <summary>
/// Counts Newbucks toward the <c>newbucks</c> goal — the game never updates its own
/// <c>AmountEverCollected</c>.
/// </summary>
[HarmonyPatch(typeof(PlayerState), nameof(PlayerState.AddCurrency))]
internal static class PlayerStateAddCurrencyPatch
{
    private static void Postfix(ICurrency currencyDefinition, int adjust)
        => NewbucksEarnings.Count(currencyDefinition, adjust);
}

/// <summary>
/// Scales and counts the payouts PlayerState couldn't identify (Quantum Cloud sales); announced
/// writes pass untouched. CallerCount(1).
/// </summary>
[HarmonyPatch(typeof(PlayerModel), nameof(PlayerModel.AddCurrency))]
internal static class PlayerModelAddCurrencyPatch
{
    private static void Prefix(ICurrency currencyDefinition, ref int adjust, out int __state)
    {
        __state = 0;
        if (NewbucksEarnings.ConsumeExpectedModelWrite(adjust)) return;
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
