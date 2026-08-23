namespace SlimeRancher2AP.Archipelago;

/// <summary>
/// Tracks Prisma Shards for the <c>prismacore_hunt</c> goal and answers the one question the
/// gate needs: may the player start the Prismacore encounter yet?
///
/// <para>
/// The hunt is the ordinary <c>prismacore</c> goal with an entry requirement bolted on. Victory
/// is still detected by <c>CoreRoomControllerPatch</c> on <c>POST_FIGHT</c> — collecting shards
/// does not win, it only unlocks the fight.
/// </para>
///
/// <para>
/// Counts come from the AP server snapshot (<c>Session.Items.AllItemsReceived</c>), never from
/// replay/apply state, matching <see cref="RanchPlotHandler"/>. The count is cached and
/// invalidated on receipt rather than recomputed per frame, because the gate is consulted from
/// a Harmony prefix on conversation display.
/// </para>
/// </summary>
public static class PrismaShardHandler
{
    private static int  _cached;
    private static bool _dirty = true;

    /// <summary>Invalidates the cached count. Called when a shard is applied.</summary>
    public static void MarkDirty() => _dirty = true;

    /// <summary>Clears all state. Called on disconnect.</summary>
    public static void Reset()
    {
        _cached = 0;
        _dirty  = true;
    }

    /// <summary>True when the active goal is the shard hunt.</summary>
    public static bool IsHuntGoal
        => Plugin.Instance.ApClient.SlotData?.Goal == "prismacore_hunt";

    /// <summary>Shards required to unlock the encounter, from slot data.</summary>
    public static int Required
        => Plugin.Instance.ApClient.SlotData?.PrismaShardsRequired ?? 0;

    /// <summary>Shards received so far.</summary>
    public static int Collected
    {
        get
        {
            if (!_dirty) return _cached;

            var snapshot = Plugin.Instance.ApClient.Session?.Items?.AllItemsReceived;
            if (snapshot == null) return _cached;   // stay dirty — retry once a session exists

            int count = 0;
            for (int i = 0; i < snapshot.Count; i++)
                if (snapshot[i].ItemId == Data.ItemTable.PrismaShard) count++;

            _cached = count;
            _dirty  = false;
            return _cached;
        }
    }

    /// <summary>
    /// True when the Prismacore encounter may be started. Always true unless the shard hunt is
    /// the active goal — every other goal leaves the encounter alone.
    /// </summary>
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
