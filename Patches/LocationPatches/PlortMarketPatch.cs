using HarmonyLib;
using Il2CppMonomiPark.SlimeRancher.DataModel;
using Il2CppMonomiPark.SlimeRancher.Economy;
using SlimeRancher2AP.Archipelago;
using SlimeRancher2AP.Data;

namespace SlimeRancher2AP.Patches.LocationPatches;

/// <summary>
/// The first sale of each plort type sends its check; every sale feeds the plort_seller counter.
/// </summary>
[HarmonyPatch(typeof(PlortEconomyDirector), nameof(PlortEconomyDirector.RegisterSold))]
internal static class PlortMarketPatch
{
    // Plorts the apworld drops from the pool under exclude_rng_slimes / exclude_weather_checks
    // (its _RNG_SLIMES_EXCLUDED / _WEATHER_CHECKS_EXCLUDED) — their IDs aren't in the seed.
    private static readonly HashSet<string> RngExcludedPlorts =
        new() { "GoldPlort", "YolkyPlort" };

    private static readonly HashSet<string> WeatherExcludedPlorts =
        new() { "DervishPlort", "TanglePlort" };

    private static void Postfix(IdentifiableType id, int count, int price)
    {
        if (!Plugin.Instance.ModEnabled || !Plugin.Instance.SaveManager.IsSaveBound) return;
        var slotData = Plugin.Instance.ApClient.SlotData;
        if (slotData == null) return;

        var plortName = id?.name;
        if (string.IsNullOrEmpty(plortName)) return;

        // Counted whether or not market checks are on — independent features.
        if (slotData.Goal == "plort_seller" && count > 0)
            Plugin.Instance.SaveManager.AccumulatePlortSold(plortName!, count);

        if (!slotData.RandomizePlortMarket) return;

        if (slotData.ExcludeRngSlimes && RngExcludedPlorts.Contains(plortName)) return;
        if (slotData.ExcludeWeatherChecks && WeatherExcludedPlorts.Contains(plortName)) return;

        if (!LocationTable.TryGetPlortMarketByPlortName(plortName, out var info) || info == null) return;
        if (Plugin.Instance.SaveManager.IsChecked(info.Id)) return;

        Logger.Info($"[AP] Plort Market: first sale of '{plortName}' → check {info.Id} ({info.Name})");
        Plugin.Instance.ApClient.SendCheck(info.Id);
    }

    /// <summary>Shared with GoalHandler's plort_seller scope.</summary>
    internal static bool IsRngExcludedPlort(string plortName)     => RngExcludedPlorts.Contains(plortName);
    internal static bool IsWeatherExcludedPlort(string plortName) => WeatherExcludedPlorts.Contains(plortName);
}
