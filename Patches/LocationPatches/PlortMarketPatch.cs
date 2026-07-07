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
    // IdentifiableType.name values for plorts the apworld removes from the location pool
    // under exclude_rng_slimes / exclude_weather_checks (mirrors _RNG_SLIMES_EXCLUDED /
    // _WEATHER_CHECKS_EXCLUDED in the apworld's __init__.py). Sending a check for one of
    // these while the option is on would target a location ID that doesn't exist in the
    // seed — the same class of bug fixed for Gordo/MapNode checks.
    private static readonly HashSet<string> RngExcludedPlorts =
        new() { "GoldPlort", "YolkyPlort" };

    private static readonly HashSet<string> WeatherExcludedPlorts =
        new() { "DervishPlort", "TanglePlort" };

    private static void Postfix(IdentifiableType id, int count, int price)
    {
        if (!Plugin.Instance.ModEnabled || !Plugin.Instance.SaveManager.HasActiveSession) return;
        var slotData = Plugin.Instance.ApClient.SlotData;
        if (!(slotData?.RandomizePlortMarket ?? false)) return;

        var plortName = id?.name;
        if (string.IsNullOrEmpty(plortName)) return;

        if (slotData.ExcludeRngSlimes && RngExcludedPlorts.Contains(plortName)) return;
        if (slotData.ExcludeWeatherChecks && WeatherExcludedPlorts.Contains(plortName)) return;

        if (!LocationTable.TryGetPlortMarketByPlortName(plortName, out var info) || info == null) return;
        if (Plugin.Instance.SaveManager.IsChecked(info.Id)) return;

        Logger.Info($"[AP] Plort Market: first sale of '{plortName}' → check {info.Id} ({info.Name})");
        Plugin.Instance.ApClient.SendCheck(info.Id);
    }
}
