namespace SlimeRancher2AP.Data;

/// <summary>
/// Region access items → their gates: EV/SS are WorldStatePrimarySwitches, PB a plort door.
/// Switches are keyed "scene:name" — "ruinSwitch" exists in more than one scene.
/// </summary>
public static class RegionTable
{
    private static string Key(string scene, string name) => $"{scene}:{name}";

    // Item → "scene:switch". (PB is a plort door, so absent from the switch maps.)
    private static readonly Dictionary<string, string> ItemToSwitchKey = new()
    {
        ["Ember Valley Access"]      = Key("zoneFields", "ruinSwitch"),
        ["Starlight Strand Access"]  = Key("zoneFields", "ruinSwitch (2)"),
    };

    /// <summary>
    /// Item → <c>WorldSwitchDefinition.ID</c> of its gate: the key into the save-persistent
    /// switch registry, readable while Rainbow Fields is unloaded and immune to name collisions.
    /// From F9 → "Dump World Switches".
    /// </summary>
    private static readonly Dictionary<string, string> ItemToSwitchDefinitionId = new()
    {
        ["Ember Valley Access"]     = "switch0365756481",   // zoneFields:ruinSwitch
        ["Starlight Strand Access"] = "switch0876920914",   // zoneFields:ruinSwitch (2)
    };

    private static readonly Dictionary<string, string> KeyToRegion =
        ItemToSwitchKey.ToDictionary(kv => kv.Value, kv => kv.Key);

    private static readonly Dictionary<string, long> KeyToLocationId = new()
    {
        [Key("zoneFields", "ruinSwitch")]      = LocationConstants.RegionGate_EmberValley,
        [Key("zoneFields", "ruinSwitch (2)")]  = LocationConstants.RegionGate_StarlightStrand,
    };

    private static readonly Dictionary<string, long> ItemToLocationId = new()
    {
        ["Ember Valley Access"]      = LocationConstants.RegionGate_EmberValley,
        ["Starlight Strand Access"]  = LocationConstants.RegionGate_StarlightStrand,
        ["Powderfall Bluffs Access"] = LocationConstants.RegionGate_PowderfallBluffs,
    };

    // -------------------------------------------------------------------------
    // Powderfall Bluffs gate: a plort door in zoneGorge_Area3. (The Boom Gordo door,
    // zoneGorge_Area4_-892_12_508, is not a location.)
    // -------------------------------------------------------------------------
    public const string PBDoorObjectName = "objLabyrinthPlortDoor01Small";
    public const string PBGatePosKey     = "zoneGorge_Area3_-645_34_681";
    public const string PBRegionItemName = "Powderfall Bluffs Access";

    /// <summary>The gate switch's bare name, for logs only — it can't identify a switch.</summary>
    public static bool TryGetSwitch(string itemName, out string switchName)
    {
        if (!ItemToSwitchKey.TryGetValue(itemName, out var compositeKey))
        {
            switchName = "";
            return false;
        }
        var colon = compositeKey.IndexOf(':');
        switchName = colon >= 0 ? compositeKey[(colon + 1)..] : compositeKey;
        return true;
    }

    /// <summary>The gate's key for <c>GameModel.GetSwitchState</c>.</summary>
    public static bool TryGetSwitchDefinitionId(string itemName, out string definitionId)
        => ItemToSwitchDefinitionId.TryGetValue(itemName, out definitionId!);

    /// <summary>The gate switch's scene and name — both are needed to identify it.</summary>
    public static bool TryGetSwitchSceneAndName(string itemName, out string sceneName, out string switchName)
    {
        sceneName = switchName = "";
        if (!ItemToSwitchKey.TryGetValue(itemName, out var compositeKey)) return false;

        var colon = compositeKey.IndexOf(':');
        if (colon < 0) { switchName = compositeKey; return true; }
        sceneName  = compositeKey[..colon];
        switchName = compositeKey[(colon + 1)..];
        return true;
    }

    public static bool TryGetRegionForSwitch(string switchName, string sceneName, out string regionName)
        => KeyToRegion.TryGetValue(Key(sceneName, switchName), out regionName!);

    public static bool TryGetLocationId(string switchName, string sceneName, out long locationId)
        => KeyToLocationId.TryGetValue(Key(sceneName, switchName), out locationId);

    public static bool TryGetLocationIdForRegion(string regionItemName, out long locationId)
        => ItemToLocationId.TryGetValue(regionItemName, out locationId);
}
