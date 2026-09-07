using HarmonyLib;
using Il2CppMonomiPark.SlimeRancher.Shop;
using Il2CppMonomiPark.SlimeRancher.UI.Shop;
using Il2CppMonomiPark.SlimeRancher.UI.Shop.Pages;
using SlimeRancher2AP.Archipelago;

namespace SlimeRancher2AP.Patches.UiPatches;

/// <summary>
/// Shared text for the "still locked" line, written to every surface that will hold it.
/// </summary>
/// <remarks>
/// <see cref="ShopCatalogListPatch"/> removes locked items outright, which leaves no trace that
/// anything is missing — a player would see a short shop and have no way to tell a small seed
/// from a gated one. This is the counterpart: hidden, but counted.
/// </remarks>
internal static class ShopCatalogNotice
{
    /// <summary>Locked checks the shop is currently offering, per category analytics name.</summary>
    /// <remarks>
    /// Recorded by <see cref="ShopCatalogListPatch"/> as it filters, because only the filter knows
    /// which locked checks the category actually holds right now. The seed-wide count from
    /// <c>ShopCatalogHandler.LockedCount</c> includes checks in zones the player has not reached,
    /// which the shop cannot show at any catalog level — reporting only that number makes a
    /// catalog look broken when it reveals fewer items than promised.
    /// </remarks>
    private static readonly Dictionary<string, int> _lockedHere = new();

    // Cached so the filter can refresh a label that was written before it ran. SetCategory fires
    // when the category is entered; the item list is read after, so the first visit would
    // otherwise show a stale count.
    private static UnityEngine.MonoBehaviour? _headerLabel;
    private static IShopCategoryDescription?  _headerDesc;
    private static UnityEngine.MonoBehaviour? _blurbLabel;
    private static IShopCategoryDescription?  _blurbDesc;

    /// <summary>Records how many locked checks a category is holding back.</summary>
    internal static void RecordLockedHere(string? category, int count)
    {
        if (string.IsNullOrEmpty(category)) return;
        _lockedHere[category!] = count;
    }

    /// <summary>Re-applies the line to whichever labels are currently bound.</summary>
    internal static void Refresh()
    {
        if (_headerLabel != null) Apply(_headerLabel, _headerDesc);
        if (_blurbLabel  != null) Apply(_blurbLabel,  _blurbDesc);
    }

    /// <summary>Clears cached labels and per-category counts. Called on disconnect.</summary>
    internal static void Reset()
    {
        _lockedHere.Clear();
        _headerLabel = null; _headerDesc = null;
        _blurbLabel  = null; _blurbDesc  = null;
    }

    /// <summary>The locked-items line for a category, or null when nothing is hidden.</summary>
    internal static string? Line(IShopCategoryDescription? desc)
    {
        if (!Plugin.Instance.ModEnabled) return null;
        if (!ShopCatalogHandler.IsActive) return null;

        int total = ShopCatalogHandler.LockedCount();
        if (total <= 0) return null;

        int wave = ShopCatalogHandler.NextWave();

        string? category = null;
        try { category = desc?.AnalyticsName; } catch { /* fall through to seed-wide */ }

        // No recorded count for this category yet (first visit, before the list was filtered) —
        // report the seed-wide number rather than inventing a split.
        if (category == null || !_lockedHere.TryGetValue(category, out int here))
            return $"{total} more locked — requires Shop Catalog {wave}";

        if (here >= total)
            return $"{here} more locked — requires Shop Catalog {wave}";

        // Spell out the remainder. This is the case that reads as a bug otherwise: the player
        // spends a catalog, fewer items appear than the count promised, and nothing on screen
        // says the difference is waiting behind zone access.
        return $"{here} more locked here — requires Shop Catalog {wave}" +
               $"  ({total - here} more in zones you haven't reached)";
    }

    /// <summary>Appends the line to a category description, preserving the original.</summary>
    internal static void Apply(UnityEngine.MonoBehaviour? label, IShopCategoryDescription? desc)
    {
        var line = Line(desc);
        if (line == null)
        {
            // Nothing hidden — hand the label back to localization so a category the player has
            // fully opened does not keep a stale count from an earlier visit.
            ShopUiHelper.OverrideText(label, null, freeze: false);
            return;
        }

        string original = "";
        try { original = desc?.Description?.GetLocalizedString() ?? ""; }
        catch { /* description is optional — the count line stands on its own */ }

        ShopUiHelper.OverrideText(
            label,
            string.IsNullOrEmpty(original) ? line : $"{original}\n\n{line}",
            freeze: true);
    }

    /// <summary>Remembers the header label so <see cref="Refresh"/> can rewrite it.</summary>
    internal static void CacheHeader(UnityEngine.MonoBehaviour? label, IShopCategoryDescription? desc)
    {
        _headerLabel = label; _headerDesc = desc;
    }

    /// <summary>Remembers the blurb label so <see cref="Refresh"/> can rewrite it.</summary>
    internal static void CacheBlurb(UnityEngine.MonoBehaviour? label, IShopCategoryDescription? desc)
    {
        _blurbLabel = label; _blurbDesc = desc;
    }
}

/// <summary>
/// Writes the locked-items line into the shop's persistent category header.
/// </summary>
/// <remarks>
/// The header stays on screen for as long as the player is browsing the category, unlike
/// <see cref="ShopCategoryBlurbPatch"/>, which fades. This is the surface the count is meant to
/// live on; the blurb is a bonus on entry.
///
/// Patched method: <c>ShopHeaderContent.SetCategory(IShopCategoryDescription, ShopStyle)</c> —
/// CallerCount(5).
/// </remarks>
[HarmonyPatch(typeof(ShopHeaderContent), nameof(ShopHeaderContent.SetCategory))]
internal static class ShopCatalogHeaderPatch
{
    private static void Postfix(ShopHeaderContent __instance, IShopCategoryDescription category)
    {
        try
        {
            ShopCatalogNotice.CacheHeader(__instance._descriptionLabel, category);
            ShopCatalogNotice.Apply(__instance._descriptionLabel, category);
        }
        catch (System.Exception ex)
        {
            Logger.Warning($"[AP] ShopCatalogHeaderPatch threw: {ex.Message}");
        }
    }
}

/// <summary>
/// Writes the locked-items line into the category blurb shown on entering a shop category.
/// </summary>
/// <remarks>
/// <para>
/// Secondary to <see cref="ShopCatalogHeaderPatch"/>, which is the surface the count actually
/// lives on — the header stays at the top of the shop for as long as the player is browsing.
/// The blurb repeats it on entry and then fades, which is fine: it is the game's own way of
/// introducing a category.
/// </para>
///
/// <para>
/// An earlier version raised <c>_fadeOutDelaySeconds</c> to hold the blurb up long enough to
/// read, because the blurb was then the only place the count appeared. That is no longer true,
/// and holding a full-width overlay across the shop for eight seconds on every visit costs more
/// than it gives, so the fade is left at vanilla timing.
/// </para>
///
/// <para>
/// <b>Why not a shop row.</b> Adding a row means constructing a <c>ShopRuntimeItem</c>, which
/// needs a <c>ShopCategoryContext</c>, an <c>IShopItemEntry</c> and an
/// <c>IShopCategoryUpdater</c> — fabricating one to hold a label is far more invasive than
/// writing to labels the game already draws.
/// </para>
///
/// <para>
/// Patched method: <c>ShopCategoryBlurb.SetCategory(IShopCategoryDescription, ShopStyle)</c> —
/// CallerCount(1).
/// </para>
/// </remarks>
[HarmonyPatch(typeof(ShopCategoryBlurb), nameof(ShopCategoryBlurb.SetCategory))]
internal static class ShopCategoryBlurbPatch
{
    private static void Postfix(ShopCategoryBlurb __instance, IShopCategoryDescription desc)
    {
        try
        {
            ShopCatalogNotice.CacheBlurb(__instance._descriptionLabel, desc);
            ShopCatalogNotice.Apply(__instance._descriptionLabel, desc);
        }
        catch (System.Exception ex)
        {
            Logger.Warning($"[AP] ShopCategoryBlurbPatch threw: {ex.Message}");
        }
    }
}
