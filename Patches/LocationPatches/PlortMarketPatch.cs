using HarmonyLib;
using Il2CppMonomiPark.SlimeRancher.DataModel;
using Il2CppMonomiPark.SlimeRancher.Economy;
using SlimeRancher2AP.Archipelago;
using SlimeRancher2AP.Data;

namespace SlimeRancher2AP.Patches.LocationPatches;

/// <summary>
/// Fires a location check the first time each plort type is sold at the Plort Market.
/// Patched method: <c>PlortEconomyDirector.RegisterSold(IdentifiableType id, int count, int price)</c>
/// CallerCount=2 — called by the market sale pipeline each time plorts are deposited.
/// The <c>IsChecked</c> guard ensures only the very first sale of each type sends a check.
/// </summary>
[HarmonyPatch(typeof(PlortEconomyDirector), nameof(PlortEconomyDirector.RegisterSold))]
internal static class PlortMarketPatch
{
    private static void Postfix(IdentifiableType id, int count, int price)
    {
        if (!Plugin.Instance.ModEnabled || !Plugin.Instance.SaveManager.HasActiveSession) return;
        if (!(Plugin.Instance.ApClient.SlotData?.RandomizePlortMarket ?? false)) return;

        var plortName = id?.name;
        if (string.IsNullOrEmpty(plortName)) return;

        if (!LocationTable.TryGetPlortMarketByPlortName(plortName, out var info) || info == null) return;
        if (Plugin.Instance.SaveManager.IsChecked(info.Id)) return;

        Logger.Info($"[AP] Plort Market: first sale of '{plortName}' → check {info.Id} ({info.Name})");
        Plugin.Instance.ApClient.SendCheck(info.Id);
    }
}
