using HarmonyLib;
using Il2CppMonomiPark.SlimeRancher.Shop.Runtime;
using SlimeRancher2AP.Archipelago;
using SlimeRancher2AP.Patches.LocationPatches;

namespace SlimeRancher2AP.Patches.UiPatches;

/// <summary>
/// Hides shop checks whose Progressive Shop Catalog wave is not open yet.
/// </summary>
/// <remarks>
/// Filters the data source — items have no availability flag of their own, and wrapping UI
/// delegates corrupts pooled rows. Hooks <c>GetRuntimeItems</c>, what every reader
/// uses, so the filter re-applies on each read; the cached list it trims is rebuilt by the game.
/// A new catalog copy therefore shows on the next shop open. Only this seed's unchecked shop
/// checks are hidden; sold ones stay as sold out.
/// </remarks>
[HarmonyPatch(typeof(ShopCategoryRuntime), nameof(ShopCategoryRuntime.GetRuntimeItems))]
internal static class ShopCatalogListPatch
{
    /// <summary>Locked checks hidden by the last pass.</summary>
    internal static int LastHiddenCount { get; private set; }

    private static void Postfix(ShopCategoryRuntime __instance)
    {
        try
        {
            if (!Plugin.Instance.ModEnabled) return;
            if (!ShopCatalogHandler.IsActive) return;

            // Edit the backing list by index — enumerating __result races an async load and
            // throws. The lazy sequence is read after this returns, so it still sees the edit.
            var list = __instance._cachedItemList;
            if (list == null || list.Count == 0) return;

            int before = list.Count, hidden = 0;

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

            // Zero too — "none here" differs from "no data yet".
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
            // Non-fatal: ShopTryPurchasePatch still refuses locked items.
            Logger.Warning($"[AP] ShopCatalogListPatch threw: {ex.Message}");
        }
    }
}
