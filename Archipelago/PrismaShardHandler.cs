namespace SlimeRancher2AP.Archipelago;

/// <summary>
/// Prisma Shards for <c>prisma_shard_hunt</c>: may the Prismacore fight start yet? (Shards only
/// unlock the fight; winning it is the goal.) Counted from the server snapshot, cached and
/// invalidated on receipt.
/// </summary>
public static class PrismaShardHandler
{
    private static int  _cached;
    private static bool _dirty = true;

#if DEBUG
    /// <summary>Debug-panel shards, added on top — a local grant can't add to the snapshot.</summary>
    private static int _debugGranted;

    public static void DebugGrant()
    {
        _debugGranted++;
        MarkDirty();
    }
#endif

    public static void MarkDirty() => _dirty = true;

    public static void Reset()
    {
        _cached = 0;
        _dirty  = true;
#if DEBUG
        _debugGranted = 0;
#endif
    }

    public static bool IsHuntGoal
        => Plugin.Instance.ApClient.SlotData?.Goal == "prisma_shard_hunt";

    public static int Required
        => Plugin.Instance.ApClient.SlotData?.PrismaShardsRequired ?? 0;

    public static int Collected
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
                        if (snapshot[i].ItemId == Data.ItemTable.PrismaShard) count++;

                    _cached = count;
                    _dirty  = false;
                }
            }
#if DEBUG
            return _cached + _debugGranted;
#else
            return _cached;
#endif
        }
    }

    /// <summary>True when the fight may start — always, outside the shard hunt.</summary>
    public static bool IsEncounterUnlocked
    {
        get
        {
            if (!Plugin.Instance.ModEnabled) return true;
            if (!IsHuntGoal) return true;
            int need = Required;
            return need <= 0 || Collected >= need;
        }
    }

    /// <summary>Progress string for the pause menu and refusal popup, e.g. "3/8".</summary>
    public static string Progress => $"{Collected}/{Required}";
}
