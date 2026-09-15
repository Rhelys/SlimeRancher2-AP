namespace SlimeRancher2AP.Data;

/// <summary>
/// Maps Archipelago region access item names to the in-game gate object names.
/// EV and SS use WorldStatePrimarySwitch; PB uses a PuzzleSlotLockable (plort door).
///
/// Switch lookups are keyed by "sceneName:switchName" to avoid conflicts with
/// duplicate switch names across different scenes (e.g. Grey Labyrinth also has
/// a GameObject named "ruinSwitch" in a different scene from the EV gate).
/// </summary>
public static class RegionTable
{
    private static string Key(string scene, string name) => $"{scene}:{name}";

    // Item name → scene-qualified switch key (for TryGetSwitch logging helper)
    private static readonly Dictionary<string, string> ItemToSwitchKey = new()
    {
        // Confirmed: both switches live in the zoneFields scene (Rainbow Fields) — 2026-04-19.
        ["Ember Valley Access"]      = Key("zoneFields", "ruinSwitch"),
        ["Starlight Strand Access"]  = Key("zoneFields", "ruinSwitch (2)"),
        // PB uses a PuzzleSlotLockable — NOT a WorldStatePrimarySwitch; excluded from Map.
    };

    /// <summary>
    /// Region access item name → <c>WorldSwitchDefinition.ID</c> of its Rainbow Fields gate.
    /// </summary>
    /// <remarks>
    /// <para>
    /// These are the keys of <c>GameModel.switches</c>, the game's save-persistent switch
    /// registry. Unlike a scene scan, that registry answers correctly while Rainbow Fields is
    /// unloaded — confirmed by dump: standing in Starlight Strand, both gates were still listed
    /// with <c>obj='(not loaded)'</c> and their true states (SS DOWN after opening, EV UP).
    /// </para>
    ///
    /// <para>
    /// IDs are also immune to the GameObject name collision that makes scanning unsafe: the same
    /// dump found a DIFFERENT switch named <c>ruinSwitch</c> in <c>zoneStrand_Area4</c>
    /// (<c>switch1251612338</c>), so a name-only match can read the wrong gate entirely.
    /// </para>
    ///
    /// <para>Confirmed via F9 → Dumps → "Dump World Switches" on 2026-09-14.</para>
    /// </remarks>
    private static readonly Dictionary<string, string> ItemToSwitchDefinitionId = new()
    {
        ["Ember Valley Access"]     = "switch0365756481",   // zoneFields:ruinSwitch
        ["Starlight Strand Access"] = "switch0876920914",   // zoneFields:ruinSwitch (2)
        // PB uses a PuzzleSlotLockable, which is not in the world-switch registry.
    };

    // Scene-qualified switch key → region item name (reverse lookup)
    private static readonly Dictionary<string, string> KeyToRegion =
        ItemToSwitchKey.ToDictionary(kv => kv.Value, kv => kv.Key);

    // Scene-qualified switch key → AP location ID
    private static readonly Dictionary<string, long> KeyToLocationId = new()
    {
        [Key("zoneFields", "ruinSwitch")]      = LocationConstants.RegionGate_EmberValley,
        [Key("zoneFields", "ruinSwitch (2)")]  = LocationConstants.RegionGate_StarlightStrand,
        // PB handled separately — PuzzleSlotLockable, not WorldStatePrimarySwitch.
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
    // Powderfall Bluffs gate door (PuzzleSlotLockable, zoneGorge_Area3)
    // Confirmed via PlortDoorPoller debug dump 2026-06-21:
    //   name='objLabyrinthPlortDoor01Small'  scene='zoneGorge_Area3'
    //   posKey='zoneGorge_Area3_-645_34_681'
    //
    // Note: zoneGorge_Area4_-892_12_508 (objLabyrinthPlortDoor02Small) is the Boom Gordo
    // door — it shows ShouldUnlock=True after the Gordo is defeated. It has no AP location
    // and is intentionally excluded from the plort door pool.
    // -------------------------------------------------------------------------
    public const string PBDoorObjectName = "objLabyrinthPlortDoor01Small";
    public const string PBGatePosKey     = "zoneGorge_Area3_-645_34_681";
    public const string PBRegionItemName = "Powderfall Bluffs Access";

    /// <summary>
    /// Returns the raw WorldStatePrimarySwitch GameObject name for the given region
    /// access item name.  Used for log messages only.
    /// </summary>
    public static bool TryGetSwitch(string itemName, out string switchName)
    {
        if (!ItemToSwitchKey.TryGetValue(itemName, out var compositeKey))
        {
            switchName = "";
            return false;
        }
        // Strip the "scene:" prefix — callers only need the bare name for display.
        var colon = compositeKey.IndexOf(':');
        switchName = colon >= 0 ? compositeKey[(colon + 1)..] : compositeKey;
        return true;
    }

    /// <summary>
    /// Returns the world-switch definition ID for a region's Rainbow Fields gate — the key for
    /// <c>GameModel.GetSwitchState</c>, which is readable whether or not the scene is loaded.
    /// </summary>
    public static bool TryGetSwitchDefinitionId(string itemName, out string definitionId)
        => ItemToSwitchDefinitionId.TryGetValue(itemName, out definitionId!);

    /// <summary>
    /// Returns the scene AND object name of a region's gate switch, so a caller matching switch
    /// objects can disambiguate. <see cref="TryGetSwitch"/> drops the scene and is display-only —
    /// using it to identify a switch matches any scene's object of that name.
    /// </summary>
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

    /// <summary>
    /// Returns the region access item name for the given switch, disambiguated by scene.
    /// Switches with the same name in different scenes will not collide.
    /// </summary>
    public static bool TryGetRegionForSwitch(string switchName, string sceneName, out string regionName)
        => KeyToRegion.TryGetValue(Key(sceneName, switchName), out regionName!);

    /// <summary>
    /// Returns the AP location ID for the given gate switch, disambiguated by scene.
    /// </summary>
    public static bool TryGetLocationId(string switchName, string sceneName, out long locationId)
        => KeyToLocationId.TryGetValue(Key(sceneName, switchName), out locationId);

    /// <summary>
    /// Returns the AP location ID for the given region access item name (e.g. "Ember Valley Access").
    /// Used by <c>ItemHandler.ApplyRegionAccess</c> to send the check directly when the item
    /// arrives, independent of whether the gate button is ever pressed again.
    /// </summary>
    public static bool TryGetLocationIdForRegion(string regionItemName, out long locationId)
        => ItemToLocationId.TryGetValue(regionItemName, out locationId);
}
