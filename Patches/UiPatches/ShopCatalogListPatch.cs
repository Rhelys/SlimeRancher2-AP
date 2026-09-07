using HarmonyLib;
using Il2CppMonomiPark.SlimeRancher.Shop.Runtime;
using SlimeRancher2AP.Archipelago;
using SlimeRancher2AP.Patches.LocationPatches;

namespace SlimeRancher2AP.Patches.UiPatches;

/// <summary>
/// Hides shop checks whose Progressive Shop Catalog wave is not open yet.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why the data source and not a flag.</b> <c>ShopRuntimeItem</c> carries no availability of
/// its own — the game decides that upstream, when a category is populated from its rule sets, so
/// there is nothing per-item to switch off. That leaves filtering the source list, which is also
/// the approach this codebase already settled on: reassigning a UI model's
/// <c>IsAvailable</c>/<c>IsHidden</c> delegate while its menu is open corrupts the pooled list
/// views (rows render another item's title and cost). See the note in CLAUDE.md.
/// </para>
///
/// <para>
/// <b>Why this hook.</b> The first version filtered <c>_cachedItemList</c> from a
/// <c>SortItems</c> Postfix and hid an inconsistent subset — that list is a cache the category
/// rebuilds (<c>CacheDataOnLoadComplete</c>, <c>TryRepopulateCategoryProvider</c>), and any
/// rebuild after the pass silently restored the removed rows. Hooking <c>GetRuntimeItems</c>
/// instead — CallerCount(3), what the grid, filter bar and counts all actually read — means the
/// filter re-applies on every read, so a later repopulate only survives until the next one.
/// </para>
///
/// <para>
/// A catalog copy arriving mid-session still only takes effect the next time the category is
/// read, which in practice means reopening the shop; filtering rows while they are bound is the
/// corruption case above. <c>ItemHandler</c> fires a notification on receipt so the player knows
/// to go back rather than assuming nothing happened.
/// </para>
///
/// <para>
/// Only locations this seed selected as checks are touched. <c>GetActiveLocation</c> returns null
/// for anything else, so unselected shop items stay fully vanilla and purchasable throughout.
/// Already-checked locations are left in place as well — they show as sold out, and yanking a row
/// the player just bought would read as a glitch.
/// </para>
/// </remarks>
[HarmonyPatch(typeof(ShopCategoryRuntime), nameof(ShopCategoryRuntime.GetRuntimeItems))]
internal static class ShopCatalogListPatch
{
    /// <summary>Locked checks removed by the last pass, for the "N more" line and logging.</summary>
    internal static int LastHiddenCount { get; private set; }

    private static void Postfix(ShopCategoryRuntime __instance)
    {
        try
        {
            if (!Plugin.Instance.ModEnabled) return;
            if (!ShopCatalogHandler.IsActive) return;

            // Mutate the backing list BY INDEX rather than enumerating the returned sequence.
            //
            // Enumerating __result threw "Collection was modified; enumeration operation may not
            // execute" on every shop open: the sequence walks _cachedItemList, and an async
            // category load completing (CacheDataOnLoadComplete) adds to that list while the walk
            // is in progress. Indexing takes no enumerator, so it cannot lose that race.
            //
            // Removing here still filters the read, because the returned sequence is lazy — the
            // consumer enumerates it after this Postfix returns, by which point the locked rows
            // are gone. The game's own availability filtering runs on top, so zone-gated items
            // stay gated.
            var list = __instance._cachedItemList;
            if (list == null || list.Count == 0) return;

            int before = list.Count, hidden = 0;

            // Backwards: RemoveAt shifts everything after the index down.
            for (int i = list.Count - 1; i >= 0; i--)
            {
                ShopRuntimeItem? item;
                try { item = list[i]; } catch { continue; }   // list shrank under us
                if (item == null) continue;

                var info = ShopPatchState.GetActiveLocation(item);
                if (info == null) continue;                             // vanilla item
                if (ShopPatchState.IsChecked(info)) continue;           // already bought
                if (ShopCatalogHandler.IsUnlocked(info.Id)) continue;   // wave is open

                try { list.RemoveAt(i); hidden++; } catch { /* raced a rebuild — next read retries */ }
            }

            // Record even when nothing was hidden: the notice needs to know this category holds
            // zero locked checks, not merely that we have no data for it yet.
            ShopCatalogNotice.RecordLockedHere(__instance.AnalyticsName, hidden);
            ShopCatalogNotice.Refresh();

            if (hidden == 0) return;

            LastHiddenCount = hidden;
            Logger.Info(
                $"[AP-Shop] Catalog {ShopCatalogHandler.Held}/{ShopCatalogHandler.Total}: " +
                $"hid {hidden} of {before} item(s) in '{__instance.AnalyticsName}' " +
                $"({list.Count} left)");
        }
        catch (System.Exception ex)
        {
            // A filter failure must not take the shop down with it — worst case the player sees
            // items they cannot buy, which ShopTryPurchasePatch still refuses.
            Logger.Warning($"[AP] ShopCatalogListPatch threw: {ex.Message}");
        }
    }
}
