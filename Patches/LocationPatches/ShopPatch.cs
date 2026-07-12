using HarmonyLib;
using Il2CppMonomiPark.SlimeRancher.Economy;
using Il2CppMonomiPark.SlimeRancher.Shop.Runtime;
using SlimeRancher2AP.Data;

namespace SlimeRancher2AP.Patches.LocationPatches;

/// <summary>
/// Shared state and lookups for the Polestar Provisions shop checks
/// (<c>randomize_shop</c> apworld option).
/// </summary>
/// <remarks>
/// <para>
/// The apworld defines all 84 shop locations but only creates a per-seed random subset —
/// <see cref="GetActiveLocation"/> therefore requires the location to exist in the connected
/// seed (<c>ArchipelagoClient.IsLocationInSeed</c>). Purchases of items outside the subset
/// behave fully vanilla.
/// </para>
/// <para>
/// Identity: <c>ShopRuntimeItem.AssetGuid</c> (stable addressables GUID). The game's own
/// <c>ShopItemId</c> is NOT usable — it degrades to a GUID while the item's asset is
/// unloaded, and some entries use hand-authored GUIDs even when loaded.
/// </para>
/// </remarks>
internal static class ShopPatchState
{
    internal static bool IsEnabled =>
        Plugin.Instance.ModEnabled
        && Plugin.Instance.SaveManager.HasActiveSession
        && (Plugin.Instance.ApClient.SlotData?.RandomizeShop ?? false);

    /// <summary>
    /// The AP shop location whose purchase is currently executing inside
    /// <c>ShopRuntimeItem.TryPurchase</c>, or null. Set by <see cref="ShopTryPurchasePatch"/>'s
    /// Prefix and cleared in its Postfix; consumed by <see cref="AcquisitionSuppressPatch"/>
    /// to skip the vanilla reward grant. The purchase pipeline is synchronous on the main
    /// thread, so plain static state is safe.
    /// </summary>
    internal static LocationInfo? InFlightCheck;

    /// <summary>
    /// Maps a shop item to its AP location, or null when the option is off, the item is not
    /// in the table, or the location does not exist in this seed.
    /// </summary>
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

    // Secondary identity: GadgetDefinition asset name → location. Used by the full-art
    // patch, whose IFullArtAndIconSource may be the underlying asset rather than the
    // ShopRuntimeItem (only the latter exposes AssetGuid).
    private static Dictionary<string, LocationInfo>? _byAssetName;

    /// <summary>
    /// Maps a shop item's asset name (LocationTable <c>EntryName</c>) to its AP location,
    /// with the same option/seed gating as <see cref="GetActiveLocation"/>.
    /// </summary>
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
/// Turns the first purchase of an AP-selected shop item into a location check.
/// Patched method: <c>ShopRuntimeItem.TryPurchase(int count, string spawnHandlerId,
/// Guid uiSessionId)</c> — CallerCount(1), called by the purchase popup confirm.
/// </summary>
/// <remarks>
/// The vanilla purchase is allowed to run (Newbucks are spent and the purchase is recorded
/// in the persisted <c>ShopDataPurchaseSet</c>) but the reward grant is suppressed by
/// <see cref="AcquisitionSuppressPatch"/> while <see cref="ShopPatchState.InFlightCheck"/>
/// is set. Once the location is checked, further purchases of that slot are blocked
/// entirely (single-purchase rule) and the slot displays as sold out
/// (<c>ShopUiPatch</c> in UiPatches).
/// </remarks>
[HarmonyPatch(typeof(ShopRuntimeItem), nameof(ShopRuntimeItem.TryPurchase))]
internal static class ShopTryPurchasePatch
{
    private static bool Prefix(ShopRuntimeItem __instance, ref bool __result)
    {
        var info = ShopPatchState.GetActiveLocation(__instance);
        if (info == null) return true; // vanilla item or option off

        if (ShopPatchState.IsChecked(info))
        {
            // Check already sent — enforce single purchase by failing the transaction
            // before any cost is spent.
            Logger.Info($"[AP] Shop: '{info.Name}' already checked — purchase blocked");
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

        // Mark the runtime slot sold out so the vanilla UI refresh (ItemPurchased event)
        // immediately picks up the state; ShopUiPatch re-asserts it on every rebind.
        try { __instance._isSoldOut = true; } catch { /* cosmetic only */ }
    }

    // If the original throws, the Postfix never runs — clear the flag here or every
    // later Acquire_SpawnOrStore call (treasure pods, other shop items) stays suppressed.
    private static System.Exception? Finalizer(System.Exception? __exception)
    {
        if (__exception != null) ShopPatchState.InFlightCheck = null;
        return __exception;
    }
}

/// <summary>
/// Suppresses the vanilla reward while an AP shop check purchase is in flight.
/// Patched method: <c>AcquisitionUtility.Acquire_SpawnOrStore(IAcquirable item,
/// AcquireParameters parameters, SpawnItemDelegate spawnItemFunc)</c> — CallerCount(5),
/// the single funnel for all shop reward types (gadget blueprints, variants, spawned
/// items, currency, bundles).
/// </summary>
[HarmonyPatch(typeof(AcquisitionUtility), nameof(AcquisitionUtility.Acquire_SpawnOrStore))]
internal static class AcquisitionSuppressPatch
{
    private static bool Prefix(ref bool __result)
    {
        var info = ShopPatchState.InFlightCheck;
        if (info == null) return true;

        // Report success so TryPurchase completes (cost spent, purchase recorded) —
        // the AP item at this location arrives via the multiworld instead.
        Logger.Info($"[AP] Shop: vanilla reward suppressed for '{info.EntryName}' (AP check)");
        __result = true;
        return false;
    }
}
