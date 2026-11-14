using SlimeRancher2AP.Data;

namespace SlimeRancher2AP.Archipelago;

/// <summary>
/// Location keys: a category of checks is held back until its key item is received — one key
/// for the whole category in "global" mode, one per zone in "zones" mode. Each keyed category is
/// a row in <see cref="Gates"/>; the patch for that category asks <see cref="MissingKey"/>.
/// </summary>
/// <remarks>
/// Received keys are persisted by item name in the save's unlocked-access set, beside the
/// Region Access items, so they hold offline.
/// </remarks>
internal static class LocationKeys
{
    internal enum Zone { RainbowFields, EmberValley, StarlightStrand, PowderfallBluffs }

    /// <summary>
    /// One keyed category. <paramref name="Mode"/> reads its option from slot data: "vanilla",
    /// "global" or "zones".
    /// </summary>
    private sealed record Gate(
        LocationType                    Type,
        Func<SlotData, string>          Mode,
        long                            GlobalKey,
        IReadOnlyDictionary<Zone, long> ZoneKeys);

    private static readonly IReadOnlyDictionary<Zone, long> NoZoneKeys = new Dictionary<Zone, long>();

    private static readonly Gate[] Gates =
    {
        // treasure_pod_keys
        new(LocationType.TreasurePod,
            sd => sd.RandomizePods ? sd.TreasurePodKeys : "vanilla",
            ItemTable.TreasurePodKey,
            new Dictionary<Zone, long>
            {
                [Zone.RainbowFields]    = ItemTable.TreasurePodKeyRainbowFields,
                [Zone.EmberValley]      = ItemTable.TreasurePodKeyEmberValley,
                [Zone.StarlightStrand]  = ItemTable.TreasurePodKeyStarlightStrand,
                [Zone.PowderfallBluffs] = ItemTable.TreasurePodKeyPowderfallBluffs,
            }),

        // drone_access_key — global only; an archive page needs it as well as the Drone Archive Key
        new(LocationType.ResearchDrone,
            sd => sd.RandomizeResearchDrones && sd.DroneAccessKey ? "global" : "vanilla",
            ItemTable.DroneAccessKey,
            NoZoneKeys),
        new(LocationType.ResearchDroneArchive,
            sd => sd.RandomizeResearchDrones && sd.DroneAccessKey ? "global" : "vanilla",
            ItemTable.DroneAccessKey,
            NoZoneKeys),
    };

    /// <summary>The key a location needs and has not received, or null if it is open.</summary>
    internal static long? MissingKey(LocationInfo loc)
        => RequiredKey(loc) is long key && !HasKey(key) ? key : null;

    /// <summary>The key a location needs, or null if it needs none.</summary>
    internal static long? RequiredKey(LocationInfo loc)
    {
        var sd = Plugin.Instance.ApClient.SlotData;
        if (sd == null) return null;

        var gate = Array.Find(Gates, g => g.Type == loc.Type);
        if (gate == null) return null;

        switch (gate.Mode(sd))
        {
            case "global":
                return gate.GlobalKey;
            case "zones":
                if (ZoneOf(loc.SceneName) is Zone zone && gate.ZoneKeys.TryGetValue(zone, out var key))
                    return key;
                Logger.Warning($"[AP] Keyed location '{loc.Name}' in unmapped scene '{loc.SceneName}' — left unlocked");
                return null;
            default:
                return null;
        }
    }

    /// <summary>The zone a scene belongs to. The Conservatory counts as Rainbow Fields.</summary>
    internal static Zone? ZoneOf(string scene)
    {
        if (scene.StartsWith("zoneConservatory") || scene.StartsWith("zoneFields"))
            return Zone.RainbowFields;
        if (scene.StartsWith("zoneGorge"))  return Zone.EmberValley;
        if (scene.StartsWith("zoneStrand")) return Zone.StarlightStrand;
        if (scene.StartsWith("zoneBluffs")) return Zone.PowderfallBluffs;
        return null;
    }

    internal static bool HasKey(long keyId)
    {
        var item = ItemTable.Get(keyId);
        return item != null && Plugin.Instance.SaveManager.IsRegionUnlocked(item.Name);
    }

    /// <summary>Records a received key. Always succeeds.</summary>
    internal static void Grant(ItemInfo item) => Plugin.Instance.SaveManager.UnlockRegion(item.Name);
}
