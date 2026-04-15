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
