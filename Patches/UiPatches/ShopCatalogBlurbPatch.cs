using HarmonyLib;
using Il2CppMonomiPark.SlimeRancher.Shop;
using Il2CppMonomiPark.SlimeRancher.UI.Shop;
using Il2CppMonomiPark.SlimeRancher.UI.Shop.Pages;
using SlimeRancher2AP.Archipelago;

namespace SlimeRancher2AP.Patches.UiPatches;

/// <summary>
/// The "N more locked" line for shop categories — <see cref="ShopCatalogListPatch"/> hides
/// locked items, so this says they exist.
/// </summary>
internal static class ShopCatalogNotice
{
    /// <summary>
    /// Locked checks per category, recorded by the filter. The seed-wide count also includes
    /// zones not yet reached, which no catalog can reveal.
    /// </summary>
    private static readonly Dictionary<string, int> _lockedHere = new();

    // Labels are written on SetCategory, before the filter runs — cached so it can refresh them.
    private static UnityEngine.MonoBehaviour? _headerLabel;
    private static IShopCategoryDescription?  _headerDesc;
    private static UnityEngine.MonoBehaviour? _blurbLabel;
    private static IShopCategoryDescription?  _blurbDesc;

    internal static void RecordLockedHere(string? category, int count)
    {
        if (string.IsNullOrEmpty(category)) return;
        _lockedHere[category!] = count;
    }

    internal static void Refresh()
    {
        if (_headerLabel != null) Apply(_headerLabel, _headerDesc);
        if (_blurbLabel  != null) Apply(_blurbLabel,  _blurbDesc);
    }

    /// <summary>On disconnect.</summary>
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

        // Not filtered yet — use the seed-wide count.
        if (category == null || !_lockedHere.TryGetValue(category, out int here))
            return $"{total} more locked — requires Shop Catalog {wave}";

        if (here >= total)
            return $"{here} more locked — requires Shop Catalog {wave}";

        // Explain the remainder, or a catalog that reveals fewer than promised looks broken.
        return $"{here} more locked here — requires Shop Catalog {wave}" +
               $"  ({total - here} more in zones you haven't reached)";
    }

    /// <summary>Appends the line to the category description.</summary>
    internal static void Apply(UnityEngine.MonoBehaviour? label, IShopCategoryDescription? desc)
    {
        var line = Line(desc);
        if (line == null)
        {
            // Nothing hidden — restore localization, dropping any stale count.
            ShopUiHelper.OverrideText(label, null, freeze: false);
            return;
        }

        string original = "";
        try { original = desc?.Description?.GetLocalizedString() ?? ""; }
        catch { /* optional */ }

        ShopUiHelper.OverrideText(
            label,
            string.IsNullOrEmpty(original) ? line : $"{original}\n\n{line}",
            freeze: true);
    }

    internal static void CacheHeader(UnityEngine.MonoBehaviour? label, IShopCategoryDescription? desc)
    {
        _headerLabel = label; _headerDesc = desc;
    }

    internal static void CacheBlurb(UnityEngine.MonoBehaviour? label, IShopCategoryDescription? desc)
    {
        _blurbLabel = label; _blurbDesc = desc;
    }
}

/// <summary>The locked-items line in the category header — the main surface, as it stays on screen.</summary>
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
/// The line in the fading entry blurb too, at vanilla timing. (A fake shop row would mean
/// fabricating a ShopRuntimeItem — far more invasive than reusing existing labels.)
/// </summary>
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
