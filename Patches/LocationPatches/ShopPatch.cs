using HarmonyLib;
using Il2CppMonomiPark.SlimeRancher.Economy;
using Il2CppMonomiPark.SlimeRancher.Shop.Runtime;
using SlimeRancher2AP.Data;

namespace SlimeRancher2AP.Patches.LocationPatches;

/// <summary>
/// Shop check state and lookups (<c>randomize_shop</c>). Only the seed's subset of shop
/// locations is active; everything else stays vanilla. Keyed by <c>AssetGuid</c>, not the
/// unstable <c>ShopItemId</c>.
/// </summary>
internal static class ShopPatchState
{
    internal static bool IsEnabled =>
        Plugin.Instance.ModEnabled
        && Plugin.Instance.SaveManager.IsSaveBound
        && (Plugin.Instance.ApClient.SlotData?.RandomizeShop ?? false);

    /// <summary>
    /// The AP location being purchased inside <c>TryPurchase</c> (synchronous), so
    /// <see cref="AcquisitionSuppressPatch"/> skips its vanilla reward.
    /// </summary>
    internal static LocationInfo? InFlightCheck;

    /// <summary>The item's AP location, or null if off, unknown, or not in this seed.</summary>
    internal static LocationInfo? GetActiveLocation(ShopRuntimeItem? item)
    {
        if (item == null || !IsEnabled) return null;

        string? guid = null;
        try { guid = item.AssetGuid; } catch { /* item mid-load */ }
        if (string.IsNullOrEmpty(guid)) return null;

        if (!LocationTable.TryGetShopByAssetGuid(guid!, out var info) || info == null) return null;
        return Plugin.Instance.ApClient.IsLocationInSeed(info.Id) ? info : null;
    }

    internal static bool IsChecked(LocationInfo info)
        => Plugin.Instance.SaveManager.IsChecked(info.Id);

    // Asset name → location, for the full-art patch, whose source may be the asset (no AssetGuid).
    private static Dictionary<string, LocationInfo>? _byAssetName;

    /// <summary>As <see cref="GetActiveLocation"/>, by asset name.</summary>
    internal static LocationInfo? GetActiveLocationByAssetName(string? assetName)
    {
        if (string.IsNullOrEmpty(assetName) || !IsEnabled) return null;

        _byAssetName ??= LocationTable.All
            .Where(l => l.Type == LocationType.PolestarShop && l.EntryName != null)
            .ToDictionary(l => l.EntryName!);

        if (!_byAssetName.TryGetValue(assetName!, out var info)) return null;
        return Plugin.Instance.ApClient.IsLocationInSeed(info.Id) ? info : null;
    }
}

/// <summary>
/// The first purchase of an AP shop item sends its check: the vanilla purchase runs (cost
/// spent, purchase recorded) but its reward is suppressed. Later purchases are refused.
/// </summary>
[HarmonyPatch(typeof(ShopRuntimeItem), nameof(ShopRuntimeItem.TryPurchase))]
internal static class ShopTryPurchasePatch
{
    private static bool Prefix(ShopRuntimeItem __instance, ref bool __result)
    {
        var info = ShopPatchState.GetActiveLocation(__instance);
        if (info == null) return true;

        if (ShopPatchState.IsChecked(info))
        {
            // Already sent — fail before any cost is spent.
            Logger.Info($"[AP] Shop: '{info.Name}' already checked — purchase blocked");
            __result = false;
            return false;
        }

        // Catalog-locked. Normally hidden by ShopCatalogListPatch, but refuse anyway — sending
        // an unreachable check is worse than an odd failed purchase.
        if (!Archipelago.ShopCatalogHandler.IsUnlocked(info.Id))
        {
            Logger.Info(
                $"[AP] Shop: '{info.Name}' is locked behind Shop Catalog " +
                $"{Plugin.Instance.ApClient.SlotData?.ShopCatalogWaves[info.Id]} " +
                $"(holding {Archipelago.ShopCatalogHandler.Held}) — purchase blocked");
            __result = false;
            return false;
        }

        ShopPatchState.InFlightCheck = info;
        return true;
    }

    private static void Postfix(ShopRuntimeItem __instance, bool __result)
    {
        var info = ShopPatchState.InFlightCheck;
        ShopPatchState.InFlightCheck = null;
        if (info == null || !__result) return;

        Logger.Info($"[AP] Shop: first purchase of '{info.EntryName}' → check {info.Id} ({info.Name})");
        Plugin.Instance.ApClient.SendCheck(info.Id);

        try { __instance._isSoldOut = true; } catch { /* cosmetic only */ }

        // The vanilla refresh ran before the check existed.
        UiPatches.ShopUiHelper.RefreshAfterPurchase(__instance);
    }

    // On a throw the Postfix never runs — clear the flag, or every later reward stays suppressed.
    private static System.Exception? Finalizer(System.Exception? __exception)
    {
        if (__exception != null) ShopPatchState.InFlightCheck = null;
        return __exception;
    }
}

/// <summary>
/// Suppresses the reward of an in-flight AP purchase. <c>Acquire_SpawnOrStore</c> is the single
/// funnel for every shop reward type.
/// </summary>
[HarmonyPatch(typeof(AcquisitionUtility), nameof(AcquisitionUtility.Acquire_SpawnOrStore))]
internal static class AcquisitionSuppressPatch
{
    private static bool Prefix(ref bool __result)
    {
        var info = ShopPatchState.InFlightCheck;
        if (info == null) return true;

        // Report success so TryPurchase completes.
        Logger.Info($"[AP] Shop: vanilla reward suppressed for '{info.EntryName}' (AP check)");
        __result = true;
        return false;
    }
}
