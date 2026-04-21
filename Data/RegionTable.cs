namespace SlimeRancher2AP.Data;

/// <summary>
/// Maps Archipelago region access item names to the in-game gate object names.
/// EV and SS use WorldStatePrimarySwitch; PB uses a PuzzleSlotLockable (plort door).
/// </summary>
public static class RegionTable
{
    private static readonly Dictionary<string, string> Map = new()
    {
        // Key: WorldStatePrimarySwitch GameObject.name — confirmed via in-game log dump (2026-04-19).
        // Both switches live in the zoneFields scene (Rainbow Fields).
        ["Ember Valley Access"]      = "ruinSwitch",
        ["Starlight Strand Access"]  = "ruinSwitch (2)",
        // PB uses a PuzzleSlotLockable — NOT a WorldStatePrimarySwitch; excluded from Map.
    };

    // Reverse of Map: switch name → region item name (for vanilla-mode teleporter grants)
    private static readonly Dictionary<string, string> ReverseMap =
        Map.ToDictionary(kv => kv.Value, kv => kv.Key);

    // Switch name → AP location ID (for locations/bundled mode check sending from RegionGatePatch)
    private static readonly Dictionary<string, long> SwitchToLocationId = new()
    {
        ["ruinSwitch"]      = LocationConstants.RegionGate_EmberValley,
        ["ruinSwitch (2)"]  = LocationConstants.RegionGate_StarlightStrand,
        // PB handled separately — it uses a PuzzleSlotLockable, not WorldStatePrimarySwitch.
    };

    // Region access item name → AP location ID.
    // Used by ApplyRegionAccess to send the check directly, independent of SetStateForAll.
    private static readonly Dictionary<string, long> ItemToLocationId = new()
    {
        ["Ember Valley Access"]      = LocationConstants.RegionGate_EmberValley,
        ["Starlight Strand Access"]  = LocationConstants.RegionGate_StarlightStrand,
        ["Powderfall Bluffs Access"] = LocationConstants.RegionGate_PowderfallBluffs,
    };

    // -------------------------------------------------------------------------
    // Powderfall Bluffs Slime Door (PuzzleSlotLockable, zoneGorge_Area3)
    // Confirmed via in-game log 2026-04-20:
    //   name='objLabyrinthPlortDoor01Small'  scene='zoneGorge_Area3'
    //   posKey='zoneGorge_Area3_-645_34_681'
    // -------------------------------------------------------------------------
    public const string PBDoorObjectName = "objLabyrinthPlortDoor01Small";
    public const string PBGatePosKey     = "zoneGorge_Area3_-645_34_681";  // confirmed via AP-Dump 2026-04-20
    public const string PBRegionItemName = "Powderfall Bluffs Access";

    /// <summary>Returns the WorldStatePrimarySwitch name for the given region access item name.</summary>
    public static bool TryGetSwitch(string itemName, out string switchName)
        => Map.TryGetValue(itemName, out switchName!);

    /// <summary>Returns the region access item name for the given switch GameObject name.</summary>
    public static bool TryGetRegionForSwitch(string switchName, out string regionName)
        => ReverseMap.TryGetValue(switchName, out regionName!);

    /// <summary>
    /// Returns the AP location ID for the given gate switch name.
    /// Used by <c>RegionGatePatch</c> when the player physically presses the gate button.
    /// </summary>
    public static bool TryGetLocationId(string switchName, out long locationId)
        => SwitchToLocationId.TryGetValue(switchName, out locationId);

    /// <summary>
    /// Returns the AP location ID for the given region access item name (e.g. "Ember Valley Access").
    /// Used by <c>ItemHandler.ApplyRegionAccess</c> to send the check directly when the item
    /// arrives, independent of whether the gate button is ever pressed again.
    /// </summary>
    public static bool TryGetLocationIdForRegion(string regionItemName, out long locationId)
        => ItemToLocationId.TryGetValue(regionItemName, out locationId);
}
