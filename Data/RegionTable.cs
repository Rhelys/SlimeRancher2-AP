namespace SlimeRancher2AP.Data;

/// <summary>
/// Maps Archipelago region access item names to the in-game WorldStatePrimarySwitch
/// object names that gate those regions.
///
/// To add a region: add a row to the Map below.
/// SwitchName must match the GameObject.name of the WorldStatePrimarySwitch exactly —
/// verify via ILSpy on the BepInEx interop DLLs or by logging switch names at runtime.
/// </summary>
public static class RegionTable
{
    private static readonly Dictionary<string, string> Map = new()
    {
        // Item name (must match ItemTable)     Switch GameObject.name (confirm via ILSpy)
        ["Ember Valley Access"]      = "EmberValleyGate",
        ["Starlight Strand Access"]  = "StarlightStrandGate",
        ["Powderfall Bluffs Access"] = "PowderfallBluffsGate",
    };

    // Reverse of Map: switch name → region item name (for auto-mode teleporter grants)
    private static readonly Dictionary<string, string> ReverseMap =
        Map.ToDictionary(kv => kv.Value, kv => kv.Key);

    /// <summary>Returns the WorldStatePrimarySwitch name for the given region access item name.</summary>
    public static bool TryGetSwitch(string itemName, out string switchName)
        => Map.TryGetValue(itemName, out switchName!);

    /// <summary>Returns the region access item name for the given switch GameObject name.</summary>
    public static bool TryGetRegionForSwitch(string switchName, out string regionName)
        => ReverseMap.TryGetValue(switchName, out regionName!);
}
