namespace SlimeRancher2AP.Archipelago;

/// <summary>
/// Tracks Progressive Shop Catalog copies and answers the one question the shop needs: is a
/// given shop location's wave open yet?
///
/// <para>
/// The apworld splits the seed's Polestar checks into waves — wave 0 is on sale from the start,
/// wave N needs N catalog copies. Without this the shop contributes its entire check count to
/// sphere 1, because the shop opens at minute one and Newbucks are farmable throughout.
/// </para>
///
/// <para>
/// <b>Why the count is a max of two sources.</b> <see cref="PrismaShardHandler"/> reads the
/// server snapshot alone, which is correct for a gate only consulted while connected. This one
/// is consulted by the shop UI, which the player can open offline — and a snapshot read offline
/// returns 0, which would hide every wave the player has already earned. So the persisted
/// per-save count is used as a floor. The snapshot stays authoritative on top of it, so a
/// catalog received while the mod was not running is still picked up on the next connect.
/// </para>
/// </summary>
public static class ShopCatalogHandler
{
    private static int  _cachedSnapshot;
    private static bool _dirty = true;

    /// <summary>Invalidates the cached snapshot count. Called when a catalog copy is applied.</summary>
    public static void MarkDirty() => _dirty = true;

    /// <summary>Clears all state. Called on disconnect.</summary>
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

    /// <summary>
    /// True when the shop location may be purchased. Locations with no wave entry — anything
    /// not selected as a check in this seed — are always allowed, so unselected shop items stay
    /// fully vanilla.
    /// </summary>
    public static bool IsUnlocked(long locationId)
    {
        var slot = Plugin.Instance.ApClient.SlotData;
        if (slot == null || !slot.ShopCatalogActive) return true;
        if (!slot.ShopCatalogWaves.TryGetValue(locationId, out int wave)) return true;
        return wave <= Held;
    }

    /// <summary>
    /// Number of this seed's shop checks still locked, for the "N more items" row.
    /// </summary>
    public static int LockedCount()
    {
        var slot = Plugin.Instance.ApClient.SlotData;
        if (slot == null || !slot.ShopCatalogActive) return 0;

        int held = Held, locked = 0;
        foreach (var kv in slot.ShopCatalogWaves)
        {
            // A check already sent is not "still to come" — counting it would leave the row
            // stuck above zero for a player who bought everything their catalogs opened.
            if (kv.Value > held && !Plugin.Instance.SaveManager.IsChecked(kv.Key)) locked++;
        }
        return locked;
    }

    /// <summary>
    /// The next wave that would open, or 0 when nothing is locked. Used for the shop row text.
    /// </summary>
    public static int NextWave()
    {
        var slot = Plugin.Instance.ApClient.SlotData;
        if (slot == null || !slot.ShopCatalogActive) return 0;

        int held = Held, next = int.MaxValue;
        foreach (var kv in slot.ShopCatalogWaves)
            if (kv.Value > held && kv.Value < next) next = kv.Value;

        return next == int.MaxValue ? 0 : next;
    }

    /// <summary>
    /// How many checks the copy just received released, for the pickup notification.
    /// </summary>
    /// <remarks>
    /// Counted against the wave the copy opened rather than everything now unlocked, so the
    /// number matches what actually appeared in the shop.
    /// </remarks>
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
