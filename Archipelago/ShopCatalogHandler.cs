namespace SlimeRancher2AP.Archipelago;

/// <summary>
/// Progressive Shop Catalog: is a shop location's wave open? Wave N needs N copies (wave 0 is on
/// sale from the start) — otherwise the whole shop lands in sphere 1.
/// </summary>
/// <remarks>
/// The count is the larger of the server snapshot and the persisted count: the shop opens
/// offline, where the snapshot reads 0.
/// </remarks>
public static class ShopCatalogHandler
{
    private static int  _cachedSnapshot;
    private static bool _dirty = true;

    public static void MarkDirty() => _dirty = true;

    public static void Reset()
    {
        _cachedSnapshot = 0;
        _dirty          = true;
    }

    /// <summary>True when this seed gates shop checks behind catalog items.</summary>
    public static bool IsActive
        => Plugin.Instance.ApClient.SlotData?.ShopCatalogActive ?? false;

    /// <summary>Catalog copies the player holds.</summary>
    public static int Held
    {
        get
        {
            if (_dirty)
            {
                var snapshot = Plugin.Instance.ApClient.Session?.Items?.AllItemsReceived;
                if (snapshot != null)   // else stay dirty — retry once a session exists
                {
                    int count = 0;
                    for (int i = 0; i < snapshot.Count; i++)
                        if (snapshot[i].ItemId == Data.ItemTable.ProgressiveShopCatalog) count++;

                    _cachedSnapshot = count;
                    _dirty          = false;
                }
            }

            int persisted = Plugin.Instance.SaveManager.ShopCatalogsHeld;
            return _cachedSnapshot > persisted ? _cachedSnapshot : persisted;
        }
    }

    /// <summary>Total catalog copies in this seed's pool.</summary>
    public static int Total => Plugin.Instance.ApClient.SlotData?.ShopCatalogItems ?? 0;

    /// <summary>True when purchasable; locations with no wave (not checks) always are.</summary>
    public static bool IsUnlocked(long locationId)
    {
        var slot = Plugin.Instance.ApClient.SlotData;
        if (slot == null || !slot.ShopCatalogActive) return true;
        if (!slot.ShopCatalogWaves.TryGetValue(locationId, out int wave)) return true;
        return wave <= Held;
    }

    /// <summary>This seed's shop checks still locked.</summary>
    public static int LockedCount()
    {
        var slot = Plugin.Instance.ApClient.SlotData;
        if (slot == null || !slot.ShopCatalogActive) return 0;

        int held = Held, locked = 0;
        foreach (var kv in slot.ShopCatalogWaves)
        {
            // Sent checks don't count.
            if (kv.Value > held && !Plugin.Instance.SaveManager.IsChecked(kv.Key)) locked++;
        }
        return locked;
    }

    /// <summary>The next wave to open, or 0 when nothing is locked.</summary>
    public static int NextWave()
    {
        var slot = Plugin.Instance.ApClient.SlotData;
        if (slot == null || !slot.ShopCatalogActive) return 0;

        int held = Held, next = int.MaxValue;
        foreach (var kv in slot.ShopCatalogWaves)
            if (kv.Value > held && kv.Value < next) next = kv.Value;

        return next == int.MaxValue ? 0 : next;
    }

    /// <summary>Checks released by the copy that opened this wave, for the pickup notification.</summary>
    public static int CountInWave(int wave)
    {
        var slot = Plugin.Instance.ApClient.SlotData;
        if (slot == null || !slot.ShopCatalogActive) return 0;

        int n = 0;
        foreach (var kv in slot.ShopCatalogWaves)
            if (kv.Value == wave) n++;
        return n;
    }
}
