namespace SlimeRancher2AP.Data;

public enum LocationType
{
    TreasurePod,
    Gordo,
    MapNode,
    SlimepediaEntry,
    SlimepediaResourceEntry,
    FabricatorCraft,
    ResearchDrone,
    /// <summary>
    /// Archive page of a Research Drone (unlocked by the Drone Archive Key upgrade).
    /// 13 of the 23 drones have archive content (confirmed via Resources scan).
    /// Lookup key: <c>ResearchDroneEntry.archivedEntry.name</c> (e.g. "ResearchDroneConservatoryArchive").
    /// </summary>
    ResearchDroneArchive,
    GhostlyDrone,
    ShadowPlortDoor,

    /// <summary>
    /// A Radiant Slime Slimepedia entry unlocked when the player first catches a radiant
    /// variant of that slime type.
    /// Included when <see cref="SlimeRancher2AP.Archipelago.SlotData.RandomizeSlimepediaRadiant"/> is true.
    /// Lookup key: <c>RadiantSlimePediaEntry.name</c> (stable Unity asset name).
    /// </summary>
    SlimepediaRadiantEntry,

    /// <summary>
    /// Conversation with confirmed zone or chain access conditions (8 total).
    /// Included when <see cref="ConversationCheckMode"/> ≥ <see cref="ConversationCheckMode.Conditional"/>.
    /// Examples: Radiant Projector Blueprint (EV or Strand), Archive Key (EV+Strand),
    /// Gordo Snare Advanced, BOb's first gift (EV+Strand), rancher intro-call chain.
    /// </summary>
    ConversationConditional,

    /// <summary>
    /// Conversation gifting a functional/progression item (teleporter, EnergyBeamNode, etc.)
    /// but without strong access conditions. Included only when
    /// <see cref="ConversationCheckMode"/> = <see cref="ConversationCheckMode.All"/>.
    /// </summary>
    ConversationKeyGift,

    /// <summary>
    /// Conversation gifting a cosmetic/decorative item (grass, rocks, flags, etc.).
    /// Included only when <see cref="ConversationCheckMode"/> = <see cref="ConversationCheckMode.All"/>.
    /// </summary>
    ConversationDecoGift,

    /// <summary>
    /// Non-gift conversation (story arc, lore, deflect dialogue).
    /// Included only when <see cref="ConversationCheckMode"/> = <see cref="ConversationCheckMode.All"/>.
    /// </summary>
    ConversationNonGift,
}

/// <summary>
/// Controls which CommStation conversations become Archipelago location checks.
/// Value read from slot data key <c>"conversation_checks"</c>.
/// </summary>
public enum ConversationCheckMode
{
    /// <summary>No conversation locations. All ranchers give vanilla gifts directly.</summary>
    Off         = 0,
    /// <summary>
    /// The 8 conversations with confirmed zone or chain access conditions:
    /// Radiant Projector Blueprint, Gordo Snare Advanced, Archive Key, Mochi/Ogden/Thora
    /// intro calls, BOb's first gift and Yolky 1 (both require EV+Strand access).
    /// </summary>
    Conditional = 1,
    /// <summary>Every conversation — all 120 total including decorative gifts and story/lore.</summary>
    All         = 2,
}

/// <summary>Describes a single randomizable location in the game world.</summary>
/// <param name="GameObjectName">
///   Primary lookup key — matches <c>gameObject.name</c> in-game for most types, or
///   the position key (<c>"sceneName_X_Y_Z"</c>) for TreasurePod, MapNode, and GhostlyDrone.
///   Not used for ResearchDrone (use <see cref="EntryName"/>) or FabricatorCraft.
/// </param>
/// <param name="EntryName">
///   Secondary lookup key used only by ResearchDrone locations.
///   Matches the <c>ResearchDroneEntry</c> asset name (NOT the GameObject name).
///   Null for all other location types.
/// </param>
public record LocationInfo(
    long         Id,
    string       Name,
    LocationType Type,
    string       SceneName,
    string       GameObjectName,
    string?      EntryName = null
);

/// <summary>
/// Master table of all randomizable locations.
/// <para>
/// Entries marked <b>PLACEHOLDER</b> in GameObjectName need their posKey confirmed by running
/// the in-game AP-Dump (F9 → Misc → Dump Locations) while standing in the relevant zone.
/// </para>
/// </summary>
public static class LocationTable
{
    public static readonly IReadOnlyList<LocationInfo> All = new LocationInfo[]
    {
        // -------------------------------------------------------------------------
        // TREASURE PODS (IDs 819000–819199)
        // GameObjectName = posKey "sceneName_X_Y_Z" (confirmed via AP-Dump).
        // PLACEHOLDER entries still need a dump run in their zone.
        // -------------------------------------------------------------------------

        // Conservatory — 3 pods ✅
        new(LocationConstants.TreasurePod_Conservatory_01, "Conservatory Pod: Flag Meat",   LocationType.TreasurePod, "zoneConservatory_Arboretum", "zoneConservatory_Arboretum_511_61_502"),
        new(LocationConstants.TreasurePod_Conservatory_02, "Conservatory Pod: Flag Veggie", LocationType.TreasurePod, "zoneConservatory_Den",       "zoneConservatory_Den_686_2_50"),
        new(LocationConstants.TreasurePod_Conservatory_03, "Conservatory Pod: Flag Fruit",  LocationType.TreasurePod, "zoneConservatory_Garden",    "zoneConservatory_Garden_836_23_289"),

        // Rainbow Fields — 18 pods ✅
        new(LocationConstants.TreasurePod_RainbowFields_01, "Rainbow Fields Pod: Tank Guard",              LocationType.TreasurePod, "zoneFields",       "zoneFields_315_-1_345"),
        new(LocationConstants.TreasurePod_RainbowFields_02, "Rainbow Fields Pod: Coastal Rock",            LocationType.TreasurePod, "zoneFields",       "zoneFields_343_-3_323"),
        new(LocationConstants.TreasurePod_RainbowFields_03, "Rainbow Fields Pod: Slimestage",              LocationType.TreasurePod, "zoneFields_Area1", "zoneFields_Area1_213_48_279"),
        new(LocationConstants.TreasurePod_RainbowFields_04, "Rainbow Fields Pod: Large Pink Bonsai",       LocationType.TreasurePod, "zoneFields_Area1", "zoneFields_Area1_210_19_287"),
        new(LocationConstants.TreasurePod_RainbowFields_05, "Rainbow Fields Pod: Heart Module",            LocationType.TreasurePod, "zoneFields_Area1", "zoneFields_Area1_268_2_398"),
        new(LocationConstants.TreasurePod_RainbowFields_06, "Rainbow Fields Pod: Simplebench",             LocationType.TreasurePod, "zoneFields_Area2", "zoneFields_Area2_250_9_541"),
        new(LocationConstants.TreasurePod_RainbowFields_07, "Rainbow Fields Pod: Pink Warp Depot",         LocationType.TreasurePod, "zoneFields_Area2", "zoneFields_Area2_350_-2_152"),
        new(LocationConstants.TreasurePod_RainbowFields_08, "Rainbow Fields Pod: Umbrella",                LocationType.TreasurePod, "zoneFields_Area2", "zoneFields_Area2_348_12_106"),
        new(LocationConstants.TreasurePod_RainbowFields_09, "Rainbow Fields Pod: Strange Diamond A",       LocationType.TreasurePod, "zoneFields_Area2", "zoneFields_Area2_333_10_164"),
        new(LocationConstants.TreasurePod_RainbowFields_10, "Rainbow Fields Pod: Strange Diamond B",       LocationType.TreasurePod, "zoneFields_Area2", "zoneFields_Area2_279_0_512"),
        new(LocationConstants.TreasurePod_RainbowFields_11, "Rainbow Fields Pod: Small Boulder",           LocationType.TreasurePod, "zoneFields_Area2", "zoneFields_Area2_224_27_415"),
        new(LocationConstants.TreasurePod_RainbowFields_12, "Rainbow Fields Pod: Hydroturret",             LocationType.TreasurePod, "zoneFields_Area2", "zoneFields_Area2_273_-4_474"),
        new(LocationConstants.TreasurePod_RainbowFields_13, "Rainbow Fields Pod: Overjoyed Statue",        LocationType.TreasurePod, "zoneFields_Area3", "zoneFields_Area3_145_18_336"),
        new(LocationConstants.TreasurePod_RainbowFields_14, "Rainbow Fields Pod: Emerald Cypress",         LocationType.TreasurePod, "zoneFields_Area3", "zoneFields_Area3_88_1_294"),
        new(LocationConstants.TreasurePod_RainbowFields_15, "Rainbow Fields Pod: Swing",                   LocationType.TreasurePod, "zoneFields_Area3", "zoneFields_Area3_42_-2_158"),
        new(LocationConstants.TreasurePod_RainbowFields_16, "Rainbow Fields Pod: Power Core",              LocationType.TreasurePod, "zoneFields_Area3", "zoneFields_Area3_141_-3_219"),
        new(LocationConstants.TreasurePod_RainbowFields_17, "Rainbow Fields Pod: Emerald Cypress Cluster", LocationType.TreasurePod, "zoneFields_Area3", "zoneFields_Area3_-9_-2_409"),
        new(LocationConstants.TreasurePod_RainbowFields_18, "Rainbow Fields Pod: Boombox",                 LocationType.TreasurePod, "zoneFields_Area4", "zoneFields_Area4_275_32_627"),

        // Ember Valley (zoneGorge) — 36 pods ✅
        new(LocationConstants.TreasurePod_EmberValley_01, "Ember Valley Pod: Dash Pad",                  LocationType.TreasurePod, "zoneGorge_Area1", "zoneGorge_Area1_-440_37_485"),
        new(LocationConstants.TreasurePod_EmberValley_02, "Ember Valley Pod: Stony Egg Lamp Statue",     LocationType.TreasurePod, "zoneGorge_Area1", "zoneGorge_Area1_-272_22_502"),
        new(LocationConstants.TreasurePod_EmberValley_03, "Ember Valley Pod: Drones",                    LocationType.TreasurePod, "zoneGorge_Area1", "zoneGorge_Area1_-519_66_530"),
        new(LocationConstants.TreasurePod_EmberValley_04, "Ember Valley Pod: Basic Tall Lamp",           LocationType.TreasurePod, "zoneGorge_Area1", "zoneGorge_Area1_-517_6_571"),
        new(LocationConstants.TreasurePod_EmberValley_05, "Ember Valley Pod: Amber Cypress Cluster",     LocationType.TreasurePod, "zoneGorge_Area1", "zoneGorge_Area1_-524_33_524"),
        new(LocationConstants.TreasurePod_EmberValley_06, "Ember Valley Pod: Tank Booster Component",    LocationType.TreasurePod, "zoneGorge_Area1", "zoneGorge_Area1_-207_-1_465"),
        new(LocationConstants.TreasurePod_EmberValley_07, "Ember Valley Pod: Portable Slime Bait Meat",  LocationType.TreasurePod, "zoneGorge_Area2", "zoneGorge_Area2_-352_19_461"),
        new(LocationConstants.TreasurePod_EmberValley_08, "Ember Valley Pod: Sureshot Component",        LocationType.TreasurePod, "zoneGorge_Area2", "zoneGorge_Area2_-323_6_304"),
        new(LocationConstants.TreasurePod_EmberValley_09, "Ember Valley Pod: Teacup Largo",              LocationType.TreasurePod, "zoneGorge_Area2", "zoneGorge_Area2_-451_4_404"),
        new(LocationConstants.TreasurePod_EmberValley_10, "Ember Valley Pod: Extra Tank Component",      LocationType.TreasurePod, "zoneGorge_Area2", "zoneGorge_Area2_-524_30_418"),
        new(LocationConstants.TreasurePod_EmberValley_11, "Ember Valley Pod: Wheelbarrow",               LocationType.TreasurePod, "zoneGorge_Area2", "zoneGorge_Area2_-503_-4_362"),
        new(LocationConstants.TreasurePod_EmberValley_12, "Ember Valley Pod: Item Display",              LocationType.TreasurePod, "zoneGorge_Area2", "zoneGorge_Area2_-630_25_217"),
        new(LocationConstants.TreasurePod_EmberValley_13, "Ember Valley Pod: Gold Chicken Statue",       LocationType.TreasurePod, "zoneGorge_Area2", "zoneGorge_Area2_-588_82_279"),
        new(LocationConstants.TreasurePod_EmberValley_14, "Ember Valley Pod: Pinball Bumper",            LocationType.TreasurePod, "zoneGorge_Area3", "zoneGorge_Area3_-500_48_769"),
        new(LocationConstants.TreasurePod_EmberValley_15, "Ember Valley Pod: Warp Depot Blue",           LocationType.TreasurePod, "zoneGorge_Area3", "zoneGorge_Area3_-572_27_868"),
        new(LocationConstants.TreasurePod_EmberValley_16, "Ember Valley Pod: Happy Statue",              LocationType.TreasurePod, "zoneGorge_Area3", "zoneGorge_Area3_-532_-2_654"),
        new(LocationConstants.TreasurePod_EmberValley_17, "Ember Valley Pod: Wind Chimes",               LocationType.TreasurePod, "zoneGorge_Area3", "zoneGorge_Area3_-361_25_620"),
        new(LocationConstants.TreasurePod_EmberValley_18, "Ember Valley Pod: Pink Striped Lamp",         LocationType.TreasurePod, "zoneGorge_Area3", "zoneGorge_Area3_-364_5_776"),
        new(LocationConstants.TreasurePod_EmberValley_19, "Ember Valley Pod: Crystal Gordo Science",     LocationType.TreasurePod, "zoneGorge_Area3", "zoneGorge_Area3_-512_50_680"),
        new(LocationConstants.TreasurePod_EmberValley_20, "Ember Valley Pod: Gold Angler Slime Statue",  LocationType.TreasurePod, "zoneGorge_Area3", "zoneGorge_Area3_-609_62_697"),
        new(LocationConstants.TreasurePod_EmberValley_21, "Ember Valley Pod: Jetpack Component",         LocationType.TreasurePod, "zoneGorge_Area3", "zoneGorge_Area3_-291_35_568"),
        new(LocationConstants.TreasurePod_EmberValley_22, "Ember Valley Pod: Tall Amber Cypress",        LocationType.TreasurePod, "zoneGorge_Area3", "zoneGorge_Area3_-633_38_687"),
        new(LocationConstants.TreasurePod_EmberValley_23, "Ember Valley Pod: Heart Module Component",    LocationType.TreasurePod, "zoneGorge_Area3", "zoneGorge_Area3_-542_19_651"),
        new(LocationConstants.TreasurePod_EmberValley_24, "Ember Valley Pod: Warp Depot Grey",           LocationType.TreasurePod, "zoneGorge_Area4", "zoneGorge_Area4_-634_32_518"),
        new(LocationConstants.TreasurePod_EmberValley_25, "Ember Valley Pod: Portable Slime Bait Fruit", LocationType.TreasurePod, "zoneGorge_Area4", "zoneGorge_Area4_-698_31_545"),
        new(LocationConstants.TreasurePod_EmberValley_26, "Ember Valley Pod: Strange Diamond",           LocationType.TreasurePod, "zoneGorge_Area4", "zoneGorge_Area4_-903_12_500"),
        new(LocationConstants.TreasurePod_EmberValley_27, "Ember Valley Pod: Teleporter Zone Gorge",     LocationType.TreasurePod, "zoneGorge_Area4", "zoneGorge_Area4_-905_14_500"),
        new(LocationConstants.TreasurePod_EmberValley_28, "Ember Valley Pod: Stalagmite",                LocationType.TreasurePod, "zoneGorge_Area4", "zoneGorge_Area4_-608_48_370"),
        new(LocationConstants.TreasurePod_EmberValley_29, "Ember Valley Pod: Magma Pool",                LocationType.TreasurePod, "zoneGorge_Area4", "zoneGorge_Area4_-705_6_339"),
        new(LocationConstants.TreasurePod_EmberValley_30, "Ember Valley Pod: Batty Gordo Science",       LocationType.TreasurePod, "zoneGorge_Area4", "zoneGorge_Area4_-685_15_418"),
        new(LocationConstants.TreasurePod_EmberValley_31, "Ember Valley Pod: Potted Plants",             LocationType.TreasurePod, "zoneGorge_Area4", "zoneGorge_Area4_-679_26_304"),
        new(LocationConstants.TreasurePod_EmberValley_32, "Ember Valley Pod: Tall Magma Clump",          LocationType.TreasurePod, "zoneGorge_Area4", "zoneGorge_Area4_-758_20_407"),
        new(LocationConstants.TreasurePod_EmberValley_33, "Ember Valley Pod: Medium Palm",               LocationType.TreasurePod, "zoneGorge_Area4", "zoneGorge_Area4_-758_18_489"),
        new(LocationConstants.TreasurePod_EmberValley_34, "Ember Valley Pod: Accelerator",               LocationType.TreasurePod, "zoneGorge_Area5", "zoneGorge_Area5_-519_-2_298"),
        new(LocationConstants.TreasurePod_EmberValley_35, "Ember Valley Pod: Golden Dervish Statue",     LocationType.TreasurePod, "zoneGorge_Area5", "zoneGorge_Area5_-810_-3_383"),
        new(LocationConstants.TreasurePod_EmberValley_36, "Ember Valley Pod: Carousel",                  LocationType.TreasurePod, "zoneGorge_Area5", "zoneGorge_Area5_-802_7_196"),

        // Starlight Strand (zoneStrand) — 33 pods ✅
        new(LocationConstants.TreasurePod_StarlightStrand_01, "Starlight Strand Pod: Tank Booster Component",  LocationType.TreasurePod, "zoneStrand",       "zoneStrand_115_2_-256"),
        new(LocationConstants.TreasurePod_StarlightStrand_02, "Starlight Strand Pod: Coastal Rock Pillar",     LocationType.TreasurePod, "zoneStrand",       "zoneStrand_92_5_-63"),
        new(LocationConstants.TreasurePod_StarlightStrand_03, "Starlight Strand Pod: Lantern Beach",           LocationType.TreasurePod, "zoneStrand",       "zoneStrand_-152_-3_-46"),
        new(LocationConstants.TreasurePod_StarlightStrand_04, "Starlight Strand Pod: Cheerful Statue",         LocationType.TreasurePod, "zoneStrand",       "zoneStrand_-17_24_-112"),
        new(LocationConstants.TreasurePod_StarlightStrand_05, "Starlight Strand Pod: Violet Warp Depot",       LocationType.TreasurePod, "zoneStrand",       "zoneStrand_126_27_-260"),
        new(LocationConstants.TreasurePod_StarlightStrand_06, "Starlight Strand Pod: Trellis Arch",            LocationType.TreasurePod, "zoneStrand",       "zoneStrand_-16_0_41"),
        new(LocationConstants.TreasurePod_StarlightStrand_07, "Starlight Strand Pod: Tall Violet Swirlshroom", LocationType.TreasurePod, "zoneStrand_Area1", "zoneStrand_Area1_90_16_-549"),
        new(LocationConstants.TreasurePod_StarlightStrand_08, "Starlight Strand Pod: Azure Mangrove",          LocationType.TreasurePod, "zoneStrand_Area1", "zoneStrand_Area1_145_64_-541"),
        new(LocationConstants.TreasurePod_StarlightStrand_09, "Starlight Strand Pod: Gordo Snare Novice",      LocationType.TreasurePod, "zoneStrand_Area1", "zoneStrand_Area1_46_14_-427"),
        new(LocationConstants.TreasurePod_StarlightStrand_10, "Starlight Strand Pod: Teacup Base",             LocationType.TreasurePod, "zoneStrand_Area1", "zoneStrand_Area1_47_1_-428"),
        new(LocationConstants.TreasurePod_StarlightStrand_11, "Starlight Strand Pod: Gold Cotton Statue",      LocationType.TreasurePod, "zoneStrand_Area1", "zoneStrand_Area1_32_37_-499"),
        new(LocationConstants.TreasurePod_StarlightStrand_12, "Starlight Strand Pod: Swivel Fan",              LocationType.TreasurePod, "zoneStrand_Area1", "zoneStrand_Area1_-19_21_-321"),
        new(LocationConstants.TreasurePod_StarlightStrand_13, "Starlight Strand Pod: Power Core Component",    LocationType.TreasurePod, "zoneStrand_Area1", "zoneStrand_Area1_-42_12_-702"),
        new(LocationConstants.TreasurePod_StarlightStrand_14, "Starlight Strand Pod: Tall Pink Coral Columns", LocationType.TreasurePod, "zoneStrand_Area2", "zoneStrand_Area2_274_3_-225"),
        new(LocationConstants.TreasurePod_StarlightStrand_15, "Starlight Strand Pod: Wide Trellis",            LocationType.TreasurePod, "zoneStrand_Area2", "zoneStrand_Area2_288_68_-287"),
        new(LocationConstants.TreasurePod_StarlightStrand_16, "Starlight Strand Pod: Cave Pillar",             LocationType.TreasurePod, "zoneStrand_Area2", "zoneStrand_Area2_303_19_-326"),
        new(LocationConstants.TreasurePod_StarlightStrand_17, "Starlight Strand Pod: Slime Bait Veggie",       LocationType.TreasurePod, "zoneStrand_Area2", "zoneStrand_Area2_252_23_-176"),
        new(LocationConstants.TreasurePod_StarlightStrand_18, "Starlight Strand Pod: Root Tangle",             LocationType.TreasurePod, "zoneStrand_Area2", "zoneStrand_Area2_193_12_-380"),
        new(LocationConstants.TreasurePod_StarlightStrand_19, "Starlight Strand Pod: Tank Guard Component A",  LocationType.TreasurePod, "zoneStrand_Area2", "zoneStrand_Area2_241_-5_-202"),
        new(LocationConstants.TreasurePod_StarlightStrand_20, "Starlight Strand Pod: Unnamed",                 LocationType.TreasurePod, "zoneStrand_Area2", "zoneStrand_Area2_375_16_-238"),
        new(LocationConstants.TreasurePod_StarlightStrand_21, "Starlight Strand Pod: Dash Boot Component",     LocationType.TreasurePod, "zoneStrand_Area2", "zoneStrand_Area2_175_32_-405"),
        new(LocationConstants.TreasurePod_StarlightStrand_22, "Starlight Strand Pod: Gold Flutter Statue",     LocationType.TreasurePod, "zoneStrand_Area3", "zoneStrand_Area3_315_43_-455"),
        new(LocationConstants.TreasurePod_StarlightStrand_23, "Starlight Strand Pod: Science Resources",       LocationType.TreasurePod, "zoneStrand_Area3", "zoneStrand_Area3_394_35_-572"),
        new(LocationConstants.TreasurePod_StarlightStrand_24, "Starlight Strand Pod: Mushroom Planter",        LocationType.TreasurePod, "zoneStrand_Area3", "zoneStrand_Area3_389_0_-549"),
        new(LocationConstants.TreasurePod_StarlightStrand_25, "Starlight Strand Pod: Tank Guard Component B",  LocationType.TreasurePod, "zoneStrand_Area3", "zoneStrand_Area3_463_52_-574"),
        new(LocationConstants.TreasurePod_StarlightStrand_26, "Starlight Strand Pod: End Teleporter",          LocationType.TreasurePod, "zoneStrand_Area4", "zoneStrand_Area4_192_8_-669"),
        new(LocationConstants.TreasurePod_StarlightStrand_27, "Starlight Strand Pod: Strange Diamond",         LocationType.TreasurePod, "zoneStrand_Area4", "zoneStrand_Area4_182_10_-644"),
        new(LocationConstants.TreasurePod_StarlightStrand_28, "Starlight Strand Pod: Springpad",               LocationType.TreasurePod, "zoneStrand_Area4", "zoneStrand_Area4_260_37_-667"),
        new(LocationConstants.TreasurePod_StarlightStrand_29, "Starlight Strand Pod: Sureshot Component",      LocationType.TreasurePod, "zoneStrand_Area4", "zoneStrand_Area4_117_58_-687"),
        new(LocationConstants.TreasurePod_StarlightStrand_30, "Starlight Strand Pod: Starbloom",               LocationType.TreasurePod, "zoneStrand_Area4", "zoneStrand_Area4_241_43_-526"),
        new(LocationConstants.TreasurePod_StarlightStrand_31, "Starlight Strand Pod: Area 5 A",                LocationType.TreasurePod, "zoneStrand_Area5", "zoneStrand_Area5_-242_-3_-613"),
        new(LocationConstants.TreasurePod_StarlightStrand_32, "Starlight Strand Pod: Area 5 B",                LocationType.TreasurePod, "zoneStrand_Area5", "zoneStrand_Area5_348_33_-337"),
        new(LocationConstants.TreasurePod_StarlightStrand_33, "Starlight Strand Pod: Area 5 C",                LocationType.TreasurePod, "zoneStrand_Area5", "zoneStrand_Area5_-138_24_-236"),

        // Powderfall Bluffs (zoneBluffs) — 25 pods ✅
        new(LocationConstants.TreasurePod_PowderfallBluffs_01, "Powderfall Bluffs Pod: Glacial Crystal",    LocationType.TreasurePod, "zoneBluffs_Area1", "zoneBluffs_Area1_-711_-5_1278"),
        new(LocationConstants.TreasurePod_PowderfallBluffs_02, "Powderfall Bluffs Pod: Ice Lamp",           LocationType.TreasurePod, "zoneBluffs_Area1", "zoneBluffs_Area1_-881_20_1449"),
        new(LocationConstants.TreasurePod_PowderfallBluffs_03, "Powderfall Bluffs Pod: Power Core",         LocationType.TreasurePod, "zoneBluffs_Area1", "zoneBluffs_Area1_-837_69_1492"),
        new(LocationConstants.TreasurePod_PowderfallBluffs_04, "Powderfall Bluffs Pod: Crystal Spires",     LocationType.TreasurePod, "zoneBluffs_Area1", "zoneBluffs_Area1_-647_-1_1483"),
        new(LocationConstants.TreasurePod_PowderfallBluffs_05, "Powderfall Bluffs Pod: Warp Depot White",   LocationType.TreasurePod, "zoneBluffs_Area1", "zoneBluffs_Area1_-838_20_1527"),
        new(LocationConstants.TreasurePod_PowderfallBluffs_06, "Powderfall Bluffs Pod: Frosted Shell",      LocationType.TreasurePod, "zoneBluffs_Area1", "zoneBluffs_Area1_-808_-3_1543"),
        new(LocationConstants.TreasurePod_PowderfallBluffs_07, "Powderfall Bluffs Pod: Ice Cubed",          LocationType.TreasurePod, "zoneBluffs_Area1", "zoneBluffs_Area1_-728_50_1526"),
        new(LocationConstants.TreasurePod_PowderfallBluffs_08, "Powderfall Bluffs Pod: Chilly Slime Stack",  LocationType.TreasurePod, "zoneBluffs_Area1", "zoneBluffs_Area1_-740_10_1496"),
        new(LocationConstants.TreasurePod_PowderfallBluffs_09, "Powderfall Bluffs Pod: Snow Machine",       LocationType.TreasurePod, "zoneBluffs_Area2", "zoneBluffs_Area2_-656_0_1656"),
        new(LocationConstants.TreasurePod_PowderfallBluffs_10, "Powderfall Bluffs Pod: Ice Treeo",          LocationType.TreasurePod, "zoneBluffs_Area2", "zoneBluffs_Area2_-901_44_1820"),
        new(LocationConstants.TreasurePod_PowderfallBluffs_11, "Powderfall Bluffs Pod: Majestic Snowflake",  LocationType.TreasurePod, "zoneBluffs_Area2", "zoneBluffs_Area2_-745_22_1765"),
        new(LocationConstants.TreasurePod_PowderfallBluffs_12, "Powderfall Bluffs Pod: Frozen Flame",       LocationType.TreasurePod, "zoneBluffs_Area2", "zoneBluffs_Area2_-730_34_1654"),
        new(LocationConstants.TreasurePod_PowderfallBluffs_13, "Powderfall Bluffs Pod: Snow Globe",         LocationType.TreasurePod, "zoneBluffs_Area2", "zoneBluffs_Area2_-1012_21_1575"),
        new(LocationConstants.TreasurePod_PowderfallBluffs_14, "Powderfall Bluffs Pod: Hydro Shower",       LocationType.TreasurePod, "zoneBluffs_Area2", "zoneBluffs_Area2_-669_42_1683"),
        new(LocationConstants.TreasurePod_PowderfallBluffs_15, "Powderfall Bluffs Pod: Sun Sap",            LocationType.TreasurePod, "zoneBluffs_Area2", "zoneBluffs_Area2_-862_29_1705"),
        new(LocationConstants.TreasurePod_PowderfallBluffs_16, "Powderfall Bluffs Pod: Tank Booster",       LocationType.TreasurePod, "zoneBluffs_Area2", "zoneBluffs_Area2_-624_38_1719"),
        new(LocationConstants.TreasurePod_PowderfallBluffs_17, "Powderfall Bluffs Pod: Snow Z Bench",       LocationType.TreasurePod, "zoneBluffs_Area2", "zoneBluffs_Area2_-689_42_1578"),
        new(LocationConstants.TreasurePod_PowderfallBluffs_18, "Powderfall Bluffs Pod: Snowy Bush",         LocationType.TreasurePod, "zoneBluffs_Area2", "zoneBluffs_Area2_-820_-5_1629"),
        new(LocationConstants.TreasurePod_PowderfallBluffs_19, "Powderfall Bluffs Pod: Aurora Pine",        LocationType.TreasurePod, "zoneBluffs_Area3", "zoneBluffs_Area3_-563_52_1622"),
        new(LocationConstants.TreasurePod_PowderfallBluffs_20, "Powderfall Bluffs Pod: Sureshot",           LocationType.TreasurePod, "zoneBluffs_Area3", "zoneBluffs_Area3_-638_123_1569"),
        new(LocationConstants.TreasurePod_PowderfallBluffs_21, "Powderfall Bluffs Pod: Fireflower",         LocationType.TreasurePod, "zoneBluffs_Area3", "zoneBluffs_Area3_-591_-3_1586"),
        new(LocationConstants.TreasurePod_PowderfallBluffs_22, "Powderfall Bluffs Pod: Teleporter Powderfall", LocationType.TreasurePod, "zoneBluffs_Area3", "zoneBluffs_Area3_-645_51_1565"),
        new(LocationConstants.TreasurePod_PowderfallBluffs_23, "Powderfall Bluffs Pod: Teleporter White",   LocationType.TreasurePod, "zoneBluffs_Area3", "zoneBluffs_Area3_-652_92_1584"),
        new(LocationConstants.TreasurePod_PowderfallBluffs_24, "Powderfall Bluffs Pod: Drones",             LocationType.TreasurePod, "zoneBluffs_Area3", "zoneBluffs_Area3_-682_122_1616"),
        new(LocationConstants.TreasurePod_PowderfallBluffs_25, "Powderfall Bluffs Pod: Aurora Flowers",     LocationType.TreasurePod, "zoneBluffs_Area4", "zoneBluffs_Area4_-1077_8_1761"),

        // Grey Labyrinth — 0 treasure pods ✅ (none exist in this zone)

        // -------------------------------------------------------------------------
        // SHADOW PLORT DOORS (IDs 819200–819224) — Grey Labyrinth only
        // GameObjectName = posKey "sceneName_X_Y_Z" — all doors share name='TriggerActivate'
        // so PlortDepositorPatch uses WorldUtils.PositionKey() for lookup, same as TreasurePods.
        // All 25 confirmed ✅ — Door 2 (y=48) and Door 10 (y=67) corrected vs original estimates.
        // -------------------------------------------------------------------------
        new(LocationConstants.ShadowPlortDoor_01, "Shadow Plort Door 1",  LocationType.ShadowPlortDoor, "zoneLabStrandEntrance",              "zoneLabStrandEntrance_918_56_-1238"),
        new(LocationConstants.ShadowPlortDoor_02, "Shadow Plort Door 2",  LocationType.ShadowPlortDoor, "zoneLabStrandEntrance",              "zoneLabStrandEntrance_1160_48_-1357"),
        new(LocationConstants.ShadowPlortDoor_03, "Shadow Plort Door 3",  LocationType.ShadowPlortDoor, "zoneLabStrandEntranceMain_B",        "zoneLabStrandEntranceMain_B_1195_38_-1410"),
        new(LocationConstants.ShadowPlortDoor_04, "Shadow Plort Door 4",  LocationType.ShadowPlortDoor, "zoneLabStrandEntranceMain_B",        "zoneLabStrandEntranceMain_B_1309_81_-1444"),
        new(LocationConstants.ShadowPlortDoor_05, "Shadow Plort Door 5",  LocationType.ShadowPlortDoor, "zoneLabValleyEntrance",              "zoneLabValleyEntrance_1755_89_-1154"),
        new(LocationConstants.ShadowPlortDoor_06, "Shadow Plort Door 6",  LocationType.ShadowPlortDoor, "zoneLabValleyEntrance",              "zoneLabValleyEntrance_1947_71_-1103"),
        new(LocationConstants.ShadowPlortDoor_07, "Shadow Plort Door 7",  LocationType.ShadowPlortDoor, "zoneLabValleyEntrance",              "zoneLabValleyEntrance_1812_50_-1150"),
        new(LocationConstants.ShadowPlortDoor_08, "Shadow Plort Door 8",  LocationType.ShadowPlortDoor, "zoneLabValleyEntrance_B",            "zoneLabValleyEntrance_B_1692_69_-953"),
        new(LocationConstants.ShadowPlortDoor_09, "Shadow Plort Door 9",  LocationType.ShadowPlortDoor, "zoneLabValleyEntrance_B",            "zoneLabValleyEntrance_B_1829_71_-999"),
        new(LocationConstants.ShadowPlortDoor_10, "Shadow Plort Door 10", LocationType.ShadowPlortDoor, "zoneLabyrinthHub",                   "zoneLabyrinthHub_1386_67_-1123"),
        new(LocationConstants.ShadowPlortDoor_11, "Shadow Plort Door 11", LocationType.ShadowPlortDoor, "zoneLabyrinthHub_B",                 "zoneLabyrinthHub_B_1099_106_-1002"),
        new(LocationConstants.ShadowPlortDoor_12, "Shadow Plort Door 12", LocationType.ShadowPlortDoor, "zoneLabyrinthHub_C",                 "zoneLabyrinthHub_C_1571_122_-1095"),
        new(LocationConstants.ShadowPlortDoor_13, "Shadow Plort Door 13", LocationType.ShadowPlortDoor, "zoneLabyrinthHub_C",                 "zoneLabyrinthHub_C_1496_148_-924"),
        new(LocationConstants.ShadowPlortDoor_14, "Shadow Plort Door 14", LocationType.ShadowPlortDoor, "zoneLabyrinthHub_C",                 "zoneLabyrinthHub_C_1372_101_-1054"),
        new(LocationConstants.ShadowPlortDoor_15, "Shadow Plort Door 15", LocationType.ShadowPlortDoor, "zoneLabyrinthDreamland",             "zoneLabyrinthDreamland_1141_156_-870"),
        new(LocationConstants.ShadowPlortDoor_16, "Shadow Plort Door 16", LocationType.ShadowPlortDoor, "zoneLabyrinthDreamland_B",           "zoneLabyrinthDreamland_B_839_154_-1059"),
        new(LocationConstants.ShadowPlortDoor_17, "Shadow Plort Door 17", LocationType.ShadowPlortDoor, "zoneLabyrinthDreamland_B",           "zoneLabyrinthDreamland_B_962_157_-951"),
        new(LocationConstants.ShadowPlortDoor_18, "Shadow Plort Door 18", LocationType.ShadowPlortDoor, "zoneLabyrinthDreamland_B",           "zoneLabyrinthDreamland_B_763_187_-796"),
        new(LocationConstants.ShadowPlortDoor_19, "Shadow Plort Door 19", LocationType.ShadowPlortDoor, "zoneLabyrinthDreamland_B",           "zoneLabyrinthDreamland_B_778_151_-987"),
        new(LocationConstants.ShadowPlortDoor_20, "Shadow Plort Door 20", LocationType.ShadowPlortDoor, "zoneLabyrinthDreamland_C",           "zoneLabyrinthDreamland_C_719_203_-424"),
        new(LocationConstants.ShadowPlortDoor_21, "Shadow Plort Door 21", LocationType.ShadowPlortDoor, "zoneLabyrinthTerrarium_FoyerGazebo", "zoneLabyrinthTerrarium_FoyerGazebo_2115_171_-792"),
        new(LocationConstants.ShadowPlortDoor_22, "Shadow Plort Door 22", LocationType.ShadowPlortDoor, "zoneLabyrinthTerrarium_FoyerGazebo", "zoneLabyrinthTerrarium_FoyerGazebo_2113_151_-693"),
        new(LocationConstants.ShadowPlortDoor_23, "Shadow Plort Door 23", LocationType.ShadowPlortDoor, "zoneLabyrinthTerrarium_JungleGlacier","zoneLabyrinthTerrarium_JungleGlacier_1926_139_-993"),
        new(LocationConstants.ShadowPlortDoor_24, "Shadow Plort Door 24", LocationType.ShadowPlortDoor, "zoneLabyrinthTerrarium_JungleGlacier","zoneLabyrinthTerrarium_JungleGlacier_1972_160_-973"),
        new(LocationConstants.ShadowPlortDoor_25, "Shadow Plort Door 25", LocationType.ShadowPlortDoor, "zoneLabyrinthTerrarium_JungleGlacier","zoneLabyrinthTerrarium_JungleGlacier_2079_185_-975"),

        // -------------------------------------------------------------------------
        // GORDO SLIMES (IDs 819250–819299)
        // GameObjectName = gameObject.name (confirmed via AP-Dump).
        // GordoPatch filters out GordoSnare templates (scene='') at runtime.
        // gordoAngler / gordoFlutter static instances get Unity " (1)" suffix because
        // the GordoSnare template registered the base name first.
        // -------------------------------------------------------------------------

        // Rainbow Fields (zoneFields_*)
        new(LocationConstants.Gordo_Pink,    "Pink Gordo",    LocationType.Gordo, "zoneFields_Area3",  "gordoPink"),
        new(LocationConstants.Gordo_Cotton,  "Cotton Gordo",  LocationType.Gordo, "zoneFields_Area1",  "gordoCotton"),
        new(LocationConstants.Gordo_Phosphor,"Phosphor Gordo",LocationType.Gordo, "zoneFields",        "gordoPhosphor"),

        // Ember Valley (zoneGorge_*)
        new(LocationConstants.Gordo_Rock,    "Rock Gordo",    LocationType.Gordo, "zoneGorge_Area3",   "gordoRock"),
        new(LocationConstants.Gordo_Tabby,   "Tabby Gordo",   LocationType.Gordo, "zoneGorge_Area3",   "gordoTabby"),
        new(LocationConstants.Gordo_Crystal, "Crystal Gordo", LocationType.Gordo, "zoneGorge_Area3",   "gordoCrystal"),
        new(LocationConstants.Gordo_Batty,   "Batty Gordo",   LocationType.Gordo, "zoneGorge_Area4",   "gordoBatty"),
        new(LocationConstants.Gordo_Boom,    "Boom Gordo",    LocationType.Gordo, "zoneGorge_Area4",   "gordoBoom"),

        // Starlight Strand (zoneStrand_*)
        new(LocationConstants.Gordo_Hunter,  "Hunter Gordo",  LocationType.Gordo, "zoneStrand_Area1",  "gordoHunter"),
        new(LocationConstants.Gordo_Honey,   "Honey Gordo",   LocationType.Gordo, "zoneStrand_Area2",  "gordoHoney"),
        new(LocationConstants.Gordo_Ringtail,"Ringtail Gordo",LocationType.Gordo, "zoneStrand_Area3",  "gordoRingtail"),
        new(LocationConstants.Gordo_Angler,  "Angler Gordo",  LocationType.Gordo, "zoneStrand_Area3",  "gordoAngler (1)"),
        new(LocationConstants.Gordo_Flutter, "Flutter Gordo", LocationType.Gordo, "zoneStrand_Area4",  "gordoFlutter (1)"),

        // Powderfall Bluffs (zoneBluffs_*)
        new(LocationConstants.Gordo_Saber,   "Saber Gordo",   LocationType.Gordo, "zoneBluffs_Area2",  "gordoSaber"),

        // Grey Labyrinth (Goals 3 and 4 only) ✅
        // Note: SloomberGordo uses capital S and no "gordo" prefix — unlike all other gordos.
        // Kinetic Gordo confirmed in zoneLabyrinthTerrarium_FoyerGazebo (not Hub_C).
        new(LocationConstants.Gordo_Sloomber,"Sloomber Gordo",LocationType.Gordo, "zoneLabyrinthDreamland_C",           "SloomberGordo"),
        new(LocationConstants.Gordo_Twin,    "Twin Gordo",    LocationType.Gordo, "zoneLabyrinthHub_C",                 "gordoTwin"),
        new(LocationConstants.Gordo_Kinetic, "Kinetic Gordo", LocationType.Gordo, "zoneLabyrinthTerrarium_FoyerGazebo", "gordoKinetic"),

        // -------------------------------------------------------------------------
        // MAP DATA NODES (IDs 819300–819349)
        // GameObjectName = posKey "sceneName_X_Y_Z" (confirmed for all zones ✅).
        // -------------------------------------------------------------------------
        new(LocationConstants.MapNode_RainbowFields_01,    "Rainbow Fields Map Node 1",    LocationType.MapNode, "zoneFields_Area1",   "zoneFields_Area1_188_4_216"),
        new(LocationConstants.MapNode_RainbowFields_02,    "Rainbow Fields Map Node 2",    LocationType.MapNode, "zoneFields_Area3",   "zoneFields_Area3_103_16_290"),
        new(LocationConstants.MapNode_EmberValley_01,      "Ember Valley Map Node 1",      LocationType.MapNode, "zoneGorge_Area1",    "zoneGorge_Area1_-443_17_560"),
        new(LocationConstants.MapNode_EmberValley_02,      "Ember Valley Map Node 2",      LocationType.MapNode, "zoneGorge_Area3",    "zoneGorge_Area3_-561_47_747"),
        new(LocationConstants.MapNode_EmberValley_03,      "Ember Valley Map Node 3",      LocationType.MapNode, "zoneGorge_Area4",    "zoneGorge_Area4_-757_29_461"),
        new(LocationConstants.MapNode_StarlightStrand_01,  "Starlight Strand Map Node 1",  LocationType.MapNode, "zoneStrand",         "zoneStrand_-8_21_-17"),
        new(LocationConstants.MapNode_StarlightStrand_02,  "Starlight Strand Map Node 2",  LocationType.MapNode, "zoneStrand_Area2",   "zoneStrand_Area2_166_13_-284"),
        new(LocationConstants.MapNode_StarlightStrand_03,  "Starlight Strand Map Node 3",  LocationType.MapNode, "zoneStrand_Area4",   "zoneStrand_Area4_261_54_-655"),
        new(LocationConstants.MapNode_PowderfallBluffs_01, "Powderfall Bluffs Map Node 1", LocationType.MapNode, "zoneBluffs_Area2",   "zoneBluffs_Area2_-821_8_1709"),
        new(LocationConstants.MapNode_PowderfallBluffs_02, "Powderfall Bluffs Map Node 2", LocationType.MapNode, "zoneBluffs_Area3",   "zoneBluffs_Area3_-575_86_1579"),
        new(LocationConstants.MapNode_GreyLabyrinth_01,    "Grey Labyrinth Map Node: Waterworks",        LocationType.MapNode, "zoneLabStrandEntranceMain_B",        "zoneLabStrandEntranceMain_B_1275_64_-1426"),
        new(LocationConstants.MapNode_GreyLabyrinth_02,    "Grey Labyrinth Map Node: Lava Depths",       LocationType.MapNode, "zoneLabValleyEntrance",              "zoneLabValleyEntrance_1782_66_-1165"),
        new(LocationConstants.MapNode_GreyLabyrinth_03,    "Grey Labyrinth Map Node: Hub",               LocationType.MapNode, "zoneLabyrinthHub",                   "zoneLabyrinthHub_1472_92_-1149"),
        new(LocationConstants.MapNode_GreyLabyrinth_04,    "Grey Labyrinth Map Node: Dreamland The Maze",LocationType.MapNode, "zoneLabyrinthDreamland_B",           "zoneLabyrinthDreamland_B_771_151_-998"),
        new(LocationConstants.MapNode_GreyLabyrinth_05,    "Grey Labyrinth Map Node: Dreamland Windfarm",LocationType.MapNode, "zoneLabyrinthDreamland_C",           "zoneLabyrinthDreamland_C_926_164_-434"),
        new(LocationConstants.MapNode_GreyLabyrinth_06,    "Grey Labyrinth Map Node: Terrarium Gazebo",  LocationType.MapNode, "zoneLabyrinthTerrarium_FoyerGazebo", "zoneLabyrinthTerrarium_FoyerGazebo_2171_162_-772"),
        new(LocationConstants.MapNode_GreyLabyrinth_07,    "Grey Labyrinth Map Node: Terrarium Jungle",  LocationType.MapNode, "zoneLabyrinthTerrarium_JungleGlacier","zoneLabyrinthTerrarium_JungleGlacier_2020_167_-969"),

        // -------------------------------------------------------------------------
        // SLIMEPEDIA ENTRIES (IDs 819350–819378)
        // Patch: PediaDirector.Unlock(PediaEntry, bool) Postfix — fires only on NEW unlocks.
        // EntryName = PediaEntry.name (confirmed via DumpPedia, 2026-04-10).
        // Scene and GameObjectName are unused for this type (lookup is EntryName-only).
        // All 29 entries in the 'Slimes' PediaCategory are included — Largo, FeralSlime,
        // and Gordo are natural encounters during gameplay and required for the
        // slimepedia goal (PediaRuntimeCategory.AllUnlocked() checks all 29).
        // -------------------------------------------------------------------------
        new(LocationConstants.Slimepedia_Pink,     "Slimepedia: Pink Slime",     LocationType.SlimepediaEntry, "", "", "Pink"),
        new(LocationConstants.Slimepedia_Cotton,   "Slimepedia: Cotton Slime",   LocationType.SlimepediaEntry, "", "", "Cotton"),
        new(LocationConstants.Slimepedia_Tabby,    "Slimepedia: Tabby Slime",    LocationType.SlimepediaEntry, "", "", "Tabby"),
        new(LocationConstants.Slimepedia_Phosphor, "Slimepedia: Phosphor Slime", LocationType.SlimepediaEntry, "", "", "Phosphor"),
        new(LocationConstants.Slimepedia_Angler,   "Slimepedia: Angler Slime",   LocationType.SlimepediaEntry, "", "", "Angler"),
        new(LocationConstants.Slimepedia_Rock,     "Slimepedia: Rock Slime",     LocationType.SlimepediaEntry, "", "", "Rock"),
        new(LocationConstants.Slimepedia_Batty,    "Slimepedia: Batty Slime",    LocationType.SlimepediaEntry, "", "", "Batty"),
        new(LocationConstants.Slimepedia_Flutter,  "Slimepedia: Flutter Slime",  LocationType.SlimepediaEntry, "", "", "Flutter"),
        new(LocationConstants.Slimepedia_Ringtail, "Slimepedia: Ringtail Slime", LocationType.SlimepediaEntry, "", "", "Ringtail"),
        new(LocationConstants.Slimepedia_Boom,     "Slimepedia: Boom Slime",     LocationType.SlimepediaEntry, "", "", "Boom"),
        new(LocationConstants.Slimepedia_Honey,    "Slimepedia: Honey Slime",    LocationType.SlimepediaEntry, "", "", "Honey"),
        new(LocationConstants.Slimepedia_Puddle,   "Slimepedia: Puddle Slime",   LocationType.SlimepediaEntry, "", "", "Puddle"),
        new(LocationConstants.Slimepedia_Crystal,  "Slimepedia: Crystal Slime",  LocationType.SlimepediaEntry, "", "", "Crystal"),
        new(LocationConstants.Slimepedia_Hunter,   "Slimepedia: Hunter Slime",   LocationType.SlimepediaEntry, "", "", "Hunter"),
        new(LocationConstants.Slimepedia_Fire,     "Slimepedia: Fire Slime",     LocationType.SlimepediaEntry, "", "", "Fire"),
        new(LocationConstants.Slimepedia_Lucky,    "Slimepedia: Lucky Slime",    LocationType.SlimepediaEntry, "", "", "Lucky"),
        new(LocationConstants.Slimepedia_Gold,     "Slimepedia: Gold Slime",     LocationType.SlimepediaEntry, "", "", "Gold"),
        new(LocationConstants.Slimepedia_Saber,    "Slimepedia: Saber Slime",    LocationType.SlimepediaEntry, "", "", "Saber"),
        new(LocationConstants.Slimepedia_Tangle,   "Slimepedia: Tangle Slime",   LocationType.SlimepediaEntry, "", "", "Tangle"),
        new(LocationConstants.Slimepedia_Dervish,  "Slimepedia: Dervish Slime",  LocationType.SlimepediaEntry, "", "", "Dervish"),
        new(LocationConstants.Slimepedia_Yolky,    "Slimepedia: Yolky Slime",    LocationType.SlimepediaEntry, "", "", "Yolky"),
        new(LocationConstants.Slimepedia_Sloomber, "Slimepedia: Sloomber Slime", LocationType.SlimepediaEntry, "", "", "Sloomber"),
        new(LocationConstants.Slimepedia_Twin,     "Slimepedia: Twin Slime",     LocationType.SlimepediaEntry, "", "", "Twin"),
        new(LocationConstants.Slimepedia_Hyper,    "Slimepedia: Hyper Slime",    LocationType.SlimepediaEntry, "", "", "Hyper"),
        new(LocationConstants.Slimepedia_Shadow,   "Slimepedia: Shadow Slime",   LocationType.SlimepediaEntry, "", "", "Shadow"),
        new(LocationConstants.Slimepedia_Tarr,       "Slimepedia: Tarr",        LocationType.SlimepediaEntry, "", "", "Tarr"),
        new(LocationConstants.Slimepedia_Largo,      "Slimepedia: Largo",       LocationType.SlimepediaEntry, "", "", "Largo"),
        new(LocationConstants.Slimepedia_FeralSlime, "Slimepedia: Feral Slime", LocationType.SlimepediaEntry, "", "", "FeralSlime"),
        new(LocationConstants.Slimepedia_Gordo,      "Slimepedia: Gordo",       LocationType.SlimepediaEntry, "", "", "Gordo"),

        // -------------------------------------------------------------------------
        // SLIMEPEDIA RESOURCES ENTRIES (IDs 819630–819683)
        // EntryName = PediaEntry.name (confirmed via AP-Dump DumpPedia, 2026-04-14).
        // Zone assignments reflect where each resource is first encountered.
        // -------------------------------------------------------------------------

        // Rainbow Fields (21)
        new(LocationConstants.SlimepediaRes_CarrotVeggie,    "Slimepedia: Carrot",               LocationType.SlimepediaResourceEntry, "", "", "CarrotVeggie"),
        new(LocationConstants.SlimepediaRes_LettuceVeggie,   "Slimepedia: Water Lettuce",        LocationType.SlimepediaResourceEntry, "", "", "LettuceVeggie"),
        new(LocationConstants.SlimepediaRes_BeetVeggie,      "Slimepedia: Heart Beet",           LocationType.SlimepediaResourceEntry, "", "", "BeetVeggie"),
        new(LocationConstants.SlimepediaRes_OnionVeggie,     "Slimepedia: Odd Onion",            LocationType.SlimepediaResourceEntry, "", "", "OnionVeggie"),
        new(LocationConstants.SlimepediaRes_PogoFruit,       "Slimepedia: Pogofruit",            LocationType.SlimepediaResourceEntry, "", "", "PogoFruit"),
        new(LocationConstants.SlimepediaRes_MangoFruit,      "Slimepedia: Mint Mango",           LocationType.SlimepediaResourceEntry, "", "", "MangoFruit"),
        new(LocationConstants.SlimepediaRes_Hen,             "Slimepedia: Hen Hen",              LocationType.SlimepediaResourceEntry, "", "", "Hen"),
        new(LocationConstants.SlimepediaRes_Chick,           "Slimepedia: Chickadoo",            LocationType.SlimepediaResourceEntry, "", "", "Chick"),
        new(LocationConstants.SlimepediaRes_Rooster,         "Slimepedia: Roostro",              LocationType.SlimepediaResourceEntry, "", "", "Rooster"),
        new(LocationConstants.SlimepediaRes_PaintedHen,      "Slimepedia: Painted Hen",          LocationType.SlimepediaResourceEntry, "", "", "PaintedHen"),
        new(LocationConstants.SlimepediaRes_PaintedChick,    "Slimepedia: Painted Chick",        LocationType.SlimepediaResourceEntry, "", "", "PaintedChick"),
        new(LocationConstants.SlimepediaRes_CandiedHen,      "Slimepedia: Candied Hen",          LocationType.SlimepediaResourceEntry, "", "", "CandiedHen"),
        new(LocationConstants.SlimepediaRes_CandedChick,     "Slimepedia: Candied Chick",        LocationType.SlimepediaResourceEntry, "", "", "CandedChick"),
        new(LocationConstants.SlimepediaRes_ElderHen,        "Slimepedia: Elder Hen",            LocationType.SlimepediaResourceEntry, "", "", "ElderHen"),
        new(LocationConstants.SlimepediaRes_ElderRooster,    "Slimepedia: Elder Roostro",        LocationType.SlimepediaResourceEntry, "", "", "ElderRooster"),
        new(LocationConstants.SlimepediaRes_WildHoneyCraft,  "Slimepedia: Wild Honey",           LocationType.SlimepediaResourceEntry, "", "", "WildHoneyCraft"),
        new(LocationConstants.SlimepediaRes_BuzzWaxCraft,    "Slimepedia: Buzz Wax",             LocationType.SlimepediaResourceEntry, "", "", "BuzzWaxCraft"),
        new(LocationConstants.SlimepediaRes_JellystoneCraft, "Slimepedia: Jellystone",           LocationType.SlimepediaResourceEntry, "", "", "JellystoneCraft"),
        new(LocationConstants.SlimepediaRes_SlimeFossilCraft,"Slimepedia: Slime Fossil",         LocationType.SlimepediaResourceEntry, "", "", "SlimeFossilCraft"),
        new(LocationConstants.SlimepediaRes_RoyalJelly,      "Slimepedia: Royal Jelly",          LocationType.SlimepediaResourceEntry, "", "", "RoyalJelly"),
        new(LocationConstants.SlimepediaRes_Water,           "Slimepedia: Water",                LocationType.SlimepediaResourceEntry, "", "", "Water"),

        // Ember Valley (10)
        new(LocationConstants.SlimepediaRes_TaterVeggie,     "Slimepedia: Turbo Tater",          LocationType.SlimepediaResourceEntry, "", "", "TaterVeggie"),
        new(LocationConstants.SlimepediaRes_CuberryFruit,    "Slimepedia: Cuberry",              LocationType.SlimepediaResourceEntry, "", "", "CuberryFruit"),
        new(LocationConstants.SlimepediaRes_PomegraniteFruit,"Slimepedia: Pomegranite",          LocationType.SlimepediaResourceEntry, "", "", "PomegraniteFruit"),
        new(LocationConstants.SlimepediaRes_StonyHen,        "Slimepedia: Stony Hen",            LocationType.SlimepediaResourceEntry, "", "", "StonyHen"),
        new(LocationConstants.SlimepediaRes_StonyChick,      "Slimepedia: Stony Chick",          LocationType.SlimepediaResourceEntry, "", "", "StonyChick"),
        new(LocationConstants.SlimepediaRes_PrimordyOilCraft,"Slimepedia: Primordy Oil",         LocationType.SlimepediaResourceEntry, "", "", "PrimordyOilCraft"),
        new(LocationConstants.SlimepediaRes_LavaDustCraft,   "Slimepedia: Lava Dust",            LocationType.SlimepediaResourceEntry, "", "", "LavaDustCraft"),
        new(LocationConstants.SlimepediaRes_MagmaCombCraft,  "Slimepedia: Magma Comb",           LocationType.SlimepediaResourceEntry, "", "", "MagmaCombCraft"),
        new(LocationConstants.SlimepediaRes_RadiantOreCraft, "Slimepedia: Radiant Ore",          LocationType.SlimepediaResourceEntry, "", "", "RadiantOreCraft"),
        new(LocationConstants.SlimepediaRes_SunSapCraft,     "Slimepedia: Sun Sap",              LocationType.SlimepediaResourceEntry, "", "", "SunSapCraft"),

        // Starlight Strand (12)
        new(LocationConstants.SlimepediaRes_PearFruit,       "Slimepedia: Prickle Pear",         LocationType.SlimepediaResourceEntry, "", "", "PearFruit"),
        new(LocationConstants.SlimepediaRes_SeaHen,          "Slimepedia: Sea Hen",              LocationType.SlimepediaResourceEntry, "", "", "SeaHen"),
        new(LocationConstants.SlimepediaRes_SeaChick,        "Slimepedia: Sea Chick",            LocationType.SlimepediaResourceEntry, "", "", "SeaChick"),
        new(LocationConstants.SlimepediaRes_BriarHen,        "Slimepedia: Briar Hen",            LocationType.SlimepediaResourceEntry, "", "", "BriarHen"),
        new(LocationConstants.SlimepediaRes_BriarChick,      "Slimepedia: Briar Chick",          LocationType.SlimepediaResourceEntry, "", "", "BriarChick"),
        new(LocationConstants.SlimepediaRes_MoondewNectar,   "Slimepedia: Moondew Nectar",       LocationType.SlimepediaResourceEntry, "", "", "MoondewNectar"),
        new(LocationConstants.SlimepediaRes_DeepBrineCraft,  "Slimepedia: Deep Brine",           LocationType.SlimepediaResourceEntry, "", "", "DeepBrineCraft"),
        new(LocationConstants.SlimepediaRes_AquaGlassCraft,  "Slimepedia: Aqua Glass",           LocationType.SlimepediaResourceEntry, "", "", "AquaGlassCraft"),
        new(LocationConstants.SlimepediaRes_DreamBubbleCraft,"Slimepedia: Dream Bubble",         LocationType.SlimepediaResourceEntry, "", "", "DreamBubbleCraft"),
        new(LocationConstants.SlimepediaRes_TinPetalCraft,   "Slimepedia: Tin Petal",            LocationType.SlimepediaResourceEntry, "", "", "TinPetalCraft"),
        new(LocationConstants.SlimepediaRes_SilkySandCraft,  "Slimepedia: Silky Sand",           LocationType.SlimepediaResourceEntry, "", "", "SilkySandCraft"),
        new(LocationConstants.SlimepediaRes_StrangeDiamondCraft, "Slimepedia: Strange Diamond",  LocationType.SlimepediaResourceEntry, "", "", "StrangeDiamondCraft"),

        // Powderfall Bluffs (8)
        new(LocationConstants.SlimepediaRes_PolaricherryFruit,   "Slimepedia: Polaricherry",         LocationType.SlimepediaResourceEntry, "", "", "PolaricherryFruit"),
        new(LocationConstants.SlimepediaRes_ThunderHen,          "Slimepedia: Thundercluck",         LocationType.SlimepediaResourceEntry, "", "", "ThunderHen"),
        new(LocationConstants.SlimepediaRes_ThunderChick,        "Slimepedia: Thunder Chick",        LocationType.SlimepediaResourceEntry, "", "", "ThunderChick"),
        new(LocationConstants.SlimepediaRes_PerfectSnowflakeCraft,"Slimepedia: Perfect Snowflake",   LocationType.SlimepediaResourceEntry, "", "", "PerfectSnowflakeCraft"),
        new(LocationConstants.SlimepediaRes_StormGlassCraft,     "Slimepedia: Storm Glass",          LocationType.SlimepediaResourceEntry, "", "", "StormGlassCraft"),
        new(LocationConstants.SlimepediaRes_LightningMoteCraft,  "Slimepedia: Lightning Mote",       LocationType.SlimepediaResourceEntry, "", "", "LightningMoteCraft"),
        new(LocationConstants.SlimepediaRes_DriftCrystalCraft,   "Slimepedia: Drift Crystal",        LocationType.SlimepediaResourceEntry, "", "", "DriftCrystalCraft"),
        new(LocationConstants.SlimepediaRes_Snowball,            "Slimepedia: Snowball",             LocationType.SlimepediaResourceEntry, "", "", "Snowball"),

        // Grey Labyrinth (3)
        new(LocationConstants.SlimepediaRes_BlackIndigoniumCraft,"Slimepedia: Black Indigonium",     LocationType.SlimepediaResourceEntry, "", "", "BlackIndigoniumCraft"),
        new(LocationConstants.SlimepediaRes_PrismaPlorts,        "Slimepedia: Prisma Plorts",        LocationType.SlimepediaResourceEntry, "", "", "PrismaPlorts"),
        new(LocationConstants.SlimepediaRes_PrismaResources,     "Slimepedia: Unstable Resources",   LocationType.SlimepediaResourceEntry, "", "", "PrismaResources"),

        // -------------------------------------------------------------------------
        // FABRICATOR — VACPACK UPGRADE CRAFTS (IDs 819400–819449)
        // Order matches apworld locations.py FABRICATOR_LOCATIONS exactly.
        // EntryName = UpgradeDefinition.name (used by FabricatorPatch for lookup).
        // Confirmed names: ResourceNodeHarvester, HealthCapacity, EnergyCapacity,
        //   AmmoSlots, Jetpack, RunEfficiency, PulseWave, LiquidSlot.
        // Unconfirmed (educated guess, verify via DumpUpgradeComponents):
        //   AmmoCapacity, TankGuard, GoldenSureshot, ShadowSureshot, ArchiveKey,
        //   EnergyDelay, EnergyRegen.
        // NOTE: EnergyCapacity has 5 tiers in apworld — verify game supports level 5.
        //       RunEfficiency has 2 tiers in apworld — verify game supports level 2.
        // -------------------------------------------------------------------------
        new(LocationConstants.Fabricator_ResourceHarvester,  "Craft: Resource Harvester",   LocationType.FabricatorCraft, "zoneRanch", "Fabricator", "ResourceNodeHarvester"),
        new(LocationConstants.Fabricator_HealthCapacity_1,   "Craft: Health Tank I",        LocationType.FabricatorCraft, "zoneRanch", "Fabricator", "HealthCapacity"),
        new(LocationConstants.Fabricator_HealthCapacity_2,   "Craft: Health Tank II",       LocationType.FabricatorCraft, "zoneRanch", "Fabricator", "HealthCapacity"),
        new(LocationConstants.Fabricator_HealthCapacity_3,   "Craft: Health Tank III",      LocationType.FabricatorCraft, "zoneRanch", "Fabricator", "HealthCapacity"),
        new(LocationConstants.Fabricator_HealthCapacity_4,   "Craft: Health Tank IV",       LocationType.FabricatorCraft, "zoneRanch", "Fabricator", "HealthCapacity"),
        new(LocationConstants.Fabricator_EnergyCapacity_1,   "Craft: Energy Tank I",        LocationType.FabricatorCraft, "zoneRanch", "Fabricator", "EnergyCapacity"),
        new(LocationConstants.Fabricator_EnergyCapacity_2,   "Craft: Energy Tank II",       LocationType.FabricatorCraft, "zoneRanch", "Fabricator", "EnergyCapacity"),
        new(LocationConstants.Fabricator_EnergyCapacity_3,   "Craft: Energy Tank III",      LocationType.FabricatorCraft, "zoneRanch", "Fabricator", "EnergyCapacity"),
        new(LocationConstants.Fabricator_EnergyCapacity_4,   "Craft: Energy Tank IV",       LocationType.FabricatorCraft, "zoneRanch", "Fabricator", "EnergyCapacity"),
        new(LocationConstants.Fabricator_EnergyCapacity_5,   "Craft: Energy Tank V",        LocationType.FabricatorCraft, "zoneRanch", "Fabricator", "EnergyCapacity"),
        new(LocationConstants.Fabricator_AmmoSlots_1,        "Craft: Extra Tank I",         LocationType.FabricatorCraft, "zoneRanch", "Fabricator", "AmmoSlots"),
        new(LocationConstants.Fabricator_AmmoSlots_2,        "Craft: Extra Tank II",        LocationType.FabricatorCraft, "zoneRanch", "Fabricator", "AmmoSlots"),
        new(LocationConstants.Fabricator_Jetpack_1,          "Craft: Jetpack I",            LocationType.FabricatorCraft, "zoneRanch", "Fabricator", "Jetpack"),
        new(LocationConstants.Fabricator_Jetpack_2,          "Craft: Jetpack II",           LocationType.FabricatorCraft, "zoneRanch", "Fabricator", "Jetpack"),
        new(LocationConstants.Fabricator_RunEfficiency_1,    "Craft: Dash Boots I",         LocationType.FabricatorCraft, "zoneRanch", "Fabricator", "RunEfficiency"),
        new(LocationConstants.Fabricator_RunEfficiency_2,    "Craft: Dash Boots II",        LocationType.FabricatorCraft, "zoneRanch", "Fabricator", "RunEfficiency"),
        new(LocationConstants.Fabricator_PulseWave,          "Craft: Pulse Wave",           LocationType.FabricatorCraft, "zoneRanch", "Fabricator", "PulseWave"),
        new(LocationConstants.Fabricator_LiquidSlot,         "Craft: Water Tank",           LocationType.FabricatorCraft, "zoneRanch", "Fabricator", "LiquidSlot"),
        new(LocationConstants.Fabricator_AmmoCapacity_1,     "Craft: Tank Booster I",       LocationType.FabricatorCraft, "zoneRanch", "Fabricator", "AmmoCapacity"),
        new(LocationConstants.Fabricator_AmmoCapacity_2,     "Craft: Tank Booster II",      LocationType.FabricatorCraft, "zoneRanch", "Fabricator", "AmmoCapacity"),
        new(LocationConstants.Fabricator_AmmoCapacity_3,     "Craft: Tank Booster III",     LocationType.FabricatorCraft, "zoneRanch", "Fabricator", "AmmoCapacity"),
        new(LocationConstants.Fabricator_AmmoCapacity_4,     "Craft: Tank Booster IV",      LocationType.FabricatorCraft, "zoneRanch", "Fabricator", "AmmoCapacity"),
        new(LocationConstants.Fabricator_AmmoCapacity_5,     "Craft: Tank Booster V",       LocationType.FabricatorCraft, "zoneRanch", "Fabricator", "AmmoCapacity"),
        new(LocationConstants.Fabricator_AmmoCapacity_6,     "Craft: Tank Booster VI",      LocationType.FabricatorCraft, "zoneRanch", "Fabricator", "AmmoCapacity"),
        new(LocationConstants.Fabricator_AmmoCapacity_7,     "Craft: Tank Booster VII",     LocationType.FabricatorCraft, "zoneRanch", "Fabricator", "AmmoCapacity"),
        new(LocationConstants.Fabricator_AmmoCapacity_8,     "Craft: Tank Booster VIII",    LocationType.FabricatorCraft, "zoneRanch", "Fabricator", "AmmoCapacity"),
        new(LocationConstants.Fabricator_TankGuard_1,        "Craft: Tank Guard I",         LocationType.FabricatorCraft, "zoneRanch", "Fabricator", "TankGuard"),
        new(LocationConstants.Fabricator_TankGuard_2,        "Craft: Tank Guard II",        LocationType.FabricatorCraft, "zoneRanch", "Fabricator", "TankGuard"),
        new(LocationConstants.Fabricator_TankGuard_3,        "Craft: Tank Guard III",       LocationType.FabricatorCraft, "zoneRanch", "Fabricator", "TankGuard"),
        new(LocationConstants.Fabricator_GoldenSureshot_1,   "Craft: Golden Sureshot I",    LocationType.FabricatorCraft, "zoneRanch", "Fabricator", "GoldenSureshot"),
        new(LocationConstants.Fabricator_GoldenSureshot_2,   "Craft: Golden Sureshot II",   LocationType.FabricatorCraft, "zoneRanch", "Fabricator", "GoldenSureshot"),
        new(LocationConstants.Fabricator_GoldenSureshot_3,   "Craft: Golden Sureshot III",  LocationType.FabricatorCraft, "zoneRanch", "Fabricator", "GoldenSureshot"),
        new(LocationConstants.Fabricator_ShadowSureshot,     "Craft: Shadow Sureshot",      LocationType.FabricatorCraft, "zoneRanch", "Fabricator", "ShadowSureshot"),
        new(LocationConstants.Fabricator_ArchiveKey,         "Craft: Drone Archive Key",    LocationType.FabricatorCraft, "zoneRanch", "Fabricator", "ArchiveKey"),
        new(LocationConstants.Fabricator_EnergyDelay_1,      "Craft: Power Injector I",     LocationType.FabricatorCraft, "zoneRanch", "Fabricator", "EnergyDelay"),
        new(LocationConstants.Fabricator_EnergyDelay_2,      "Craft: Power Injector II",    LocationType.FabricatorCraft, "zoneRanch", "Fabricator", "EnergyDelay"),
        new(LocationConstants.Fabricator_EnergyRegen_1,      "Craft: Regenerator I",        LocationType.FabricatorCraft, "zoneRanch", "Fabricator", "EnergyRegen"),
        new(LocationConstants.Fabricator_EnergyRegen_2,      "Craft: Regenerator II",       LocationType.FabricatorCraft, "zoneRanch", "Fabricator", "EnergyRegen"),

        // -------------------------------------------------------------------------
        // RESEARCH DRONES (IDs 819450–819479)
        // EntryName = ResearchDroneEntry asset name (confirmed via AP-Dump).
        // GameObjectName is NOT used for lookup (always something generic like "Drone_2").
        // SceneName is approximate — sub-scene may differ; not used for runtime lookup.
        // -------------------------------------------------------------------------

        // Conservatory sub-zones (5 drones) ✅
        new(LocationConstants.ResearchDrone_Gully,               "Research Drone: Gully",               LocationType.ResearchDrone, "zoneConservatory_Arboretum", "Drone_2", "ResearchDroneGully"),
        new(LocationConstants.ResearchDrone_Conservatory,        "Research Drone: Conservatory",        LocationType.ResearchDrone, "zoneConservatory_Arboretum", "Drone_2", "ResearchDroneConservatory"),
        new(LocationConstants.ResearchDrone_TheDen,              "Research Drone: The Den",             LocationType.ResearchDrone, "zoneConservatory_Den",       "Drone_2", "ResearchDroneTheDen"),
        new(LocationConstants.ResearchDrone_Archway,             "Research Drone: Archway",             LocationType.ResearchDrone, "zoneConservatory_Garden",    "Drone_2", "ResearchDroneArchway"),
        new(LocationConstants.ResearchDrone_Tidepools,           "Research Drone: Tidepools",           LocationType.ResearchDrone, "zoneConservatory_Pools",     "Drone_2", "ResearchDroneTidepools"),

        // Rainbow Fields (2 drones) ✅
        new(LocationConstants.ResearchDrone_FieldsExpanse,       "Research Drone: Fields Expanse",      LocationType.ResearchDrone, "zoneFields_Area1",           "Drone_2", "ResearchDroneFieldsExpanse"),
        new(LocationConstants.ResearchDrone_FieldsBluff,         "Research Drone: Fields Bluff",        LocationType.ResearchDrone, "zoneFields_Area3",           "Drone_2", "ResearchDroneFieldsBluff"),

        // Ember Valley / zoneGorge (6 drones) ✅
        new(LocationConstants.ResearchDrone_GorgeOverlook,       "Research Drone: Gorge Overlook",          LocationType.ResearchDrone, "zoneGorge_Area1", "Drone_2", "ResearchDroneGorgeOverlook"),
        new(LocationConstants.ResearchDrone_GorgeRuinedOverlook, "Research Drone: Gorge Ruined Overlook",   LocationType.ResearchDrone, "zoneGorge_Area2", "Drone_2", "ResearchDroneGorgeRuinedOverlook"),
        new(LocationConstants.ResearchDrone_GorgeOceanPerch,     "Research Drone: Gorge Ocean Perch",       LocationType.ResearchDrone, "zoneGorge_Area3", "Drone_2", "ResearchDroneGorgeOceanPerch"),
        new(LocationConstants.ResearchDrone_GorgeCaveHub,        "Research Drone: Gorge Cave Hub",          LocationType.ResearchDrone, "zoneGorge_Area4", "Drone_2", "ResearchDroneGorgeCaveHub"),
        new(LocationConstants.ResearchDrone_GorgeLabyrinthGate,  "Research Drone: Gorge Labyrinth Gate",    LocationType.ResearchDrone, "zoneGorge_Area4", "Drone_2", "ResearchDroneGorgeLabyrinthGate"),
        new(LocationConstants.ResearchDrone_GorgeMagmaFieldsPerch,"Research Drone: Gorge Magma Fields Perch",LocationType.ResearchDrone,"zoneGorge_Area4", "Drone_2", "ResearchDroneGorgeMagmaFieldsPerch"),

        // Starlight Strand / zoneStrand (6 drones) ✅
        new(LocationConstants.ResearchDrone_StrandField,         "Research Drone: Strand Field",         LocationType.ResearchDrone, "zoneStrand",       "Drone_2", "ResearchDroneStrandField"),
        new(LocationConstants.ResearchDrone_StrandMushroomAlley, "Research Drone: Strand Mushroom Alley", LocationType.ResearchDrone, "zoneStrand_Area1", "Drone_2", "ResearchDroneStrandMushroomAlley"),
        new(LocationConstants.ResearchDrone_StrandWaterfall,     "Research Drone: Strand Waterfall",     LocationType.ResearchDrone, "zoneStrand_Area2", "Drone_2", "ResearchDroneStrandWaterfall"),
        new(LocationConstants.ResearchDrone_StrandSplitTree,     "Research Drone: Strand Split Tree",    LocationType.ResearchDrone, "zoneStrand_Area2", "Drone_2", "ResearchDroneStrandSplitTree"),
        new(LocationConstants.ResearchDrone_StrandSlopedCliff,   "Research Drone: Strand Sloped Cliff",  LocationType.ResearchDrone, "zoneStrand_Area3", "Drone_2", "ResearchDroneStrandSlopedCliff"),
        new(LocationConstants.ResearchDrone_StrandLabyrinthGate, "Research Drone: Strand Labyrinth Gate",LocationType.ResearchDrone, "zoneStrand_Area4", "Drone_2", "ResearchDroneStrandLabyrinthGate"),

        // Powderfall Bluffs / zoneBluffs (4 drones) ✅
        new(LocationConstants.ResearchDrone_BluffsAurora,        "Research Drone: Bluffs Aurora",       LocationType.ResearchDrone, "zoneBluffs_Area1", "Drone_2", "ResearchDroneBluffsAurora"),
        new(LocationConstants.ResearchDrone_BluffsIntro,         "Research Drone: Bluffs Intro",        LocationType.ResearchDrone, "zoneBluffs_Area1", "Drone_2", "ResearchDroneBluffsIntro"),
        new(LocationConstants.ResearchDrone_BluffsSaber,         "Research Drone: Bluffs Saber",        LocationType.ResearchDrone, "zoneBluffs_Area3", "Drone_2", "ResearchDroneBluffsSaber"),
        new(LocationConstants.ResearchDrone_BluffsFinal,         "Research Drone: Bluffs Final",        LocationType.ResearchDrone, "zoneBluffs_Area3", "Drone_2", "ResearchDroneBluffsFinal"),

        // -------------------------------------------------------------------------
        // RESEARCH DRONE ARCHIVES (IDs 819473–819495)
        // EntryName = ResearchDroneEntry.archivedEntry asset name (confirmed via Resources scan).
        // 13 drones have archive content; 10 do not (archivedEntry == null, silently skipped).
        // -------------------------------------------------------------------------

        // Conservatory sub-zones (5 archives — all 5 Conservatory drones have archives)
        new(LocationConstants.ResearchDroneArchive_Gully,               "Research Drone Archive: Gully",               LocationType.ResearchDroneArchive, "zoneConservatory_Arboretum", "Drone_2", "ResearchDroneGullyArchive"),
        new(LocationConstants.ResearchDroneArchive_Conservatory,        "Research Drone Archive: Conservatory",        LocationType.ResearchDroneArchive, "zoneConservatory_Arboretum", "Drone_2", "ResearchDroneConservatoryArchive"),
        new(LocationConstants.ResearchDroneArchive_TheDen,              "Research Drone Archive: The Den",             LocationType.ResearchDroneArchive, "zoneConservatory_Den",       "Drone_2", "ResearchDroneTheDenArchive"),
        new(LocationConstants.ResearchDroneArchive_Archway,             "Research Drone Archive: Archway",             LocationType.ResearchDroneArchive, "zoneConservatory_Garden",    "Drone_2", "ResearchDroneArchwayArchive"),
        new(LocationConstants.ResearchDroneArchive_Tidepools,           "Research Drone Archive: Tidepools",           LocationType.ResearchDroneArchive, "zoneConservatory_Pools",     "Drone_2", "ResearchDroneTidepoolsArchive"),

        // Rainbow Fields (2 archives — all 2 Rainbow Fields drones have archives)
        new(LocationConstants.ResearchDroneArchive_FieldsExpanse,       "Research Drone Archive: Fields Expanse",      LocationType.ResearchDroneArchive, "zoneFields_Area1",           "Drone_2", "ResearchDroneFieldsExpanseArchive"),
        new(LocationConstants.ResearchDroneArchive_FieldsBluff,         "Research Drone Archive: Fields Bluff",        LocationType.ResearchDroneArchive, "zoneFields_Area3",           "Drone_2", "ResearchDroneFieldsBluffArchive"),

        // Ember Valley / zoneGorge (3 of 6 drones have archives)
        new(LocationConstants.ResearchDroneArchive_GorgeOverlook,       "Research Drone Archive: Gorge Overlook",      LocationType.ResearchDroneArchive, "zoneGorge_Area1", "Drone_2", "ResearchDroneGorgeOverlookArchive"),
        new(LocationConstants.ResearchDroneArchive_GorgeRuinedOverlook, "Research Drone Archive: Gorge Ruined Overlook", LocationType.ResearchDroneArchive, "zoneGorge_Area2", "Drone_2", "ResearchDroneGorgeRuinedOverlookArchive"),
        new(LocationConstants.ResearchDroneArchive_GorgeLabyrinthGate,  "Research Drone Archive: Gorge Labyrinth Gate",  LocationType.ResearchDroneArchive, "zoneGorge_Area4", "Drone_2", "ResearchDroneGorgeLabyrinthGateArchive"),

        // Starlight Strand / zoneStrand (3 of 6 drones have archives)
        new(LocationConstants.ResearchDroneArchive_StrandField,         "Research Drone Archive: Strand Field",         LocationType.ResearchDroneArchive, "zoneStrand",       "Drone_2", "ResearchDroneStrandFieldArchive"),
        new(LocationConstants.ResearchDroneArchive_StrandWaterfall,     "Research Drone Archive: Strand Waterfall",     LocationType.ResearchDroneArchive, "zoneStrand_Area2", "Drone_2", "ResearchDroneStrandWaterfallArchive"),
        new(LocationConstants.ResearchDroneArchive_StrandLabyrinthGate, "Research Drone Archive: Strand Labyrinth Gate", LocationType.ResearchDroneArchive, "zoneStrand_Area4", "Drone_2", "ResearchDroneStrandLabyrinthGateArchive"),

        // -------------------------------------------------------------------------
        // GHOSTLY DRONES (IDs 819480–819494)
        // These are TreasurePod subclass nodes (nodeComponentAcqDrone).
        // GameObjectName = posKey "sceneName_X_Y_Z".
        // Confirmed: EV ✅, SS ✅, PB ✅, Conservatory ✅, Rainbow Fields ✅, Grey Labyrinth ✅.
        // -------------------------------------------------------------------------
        new(LocationConstants.GhostlyDrone_Conservatory_01,     "Ghostly Drone: Conservatory 1",     LocationType.GhostlyDrone, "zoneConservatory",  "zoneConservatory_560_42_466"),   // parent: ComponentAcquisitionDroneConservatory
        new(LocationConstants.GhostlyDrone_Conservatory_02,     "Ghostly Drone: Conservatory 2",     LocationType.GhostlyDrone, "zoneConservatory",  "zoneConservatory_589_27_27"),    // parent: ComponentAcquisitionDroneConservatory3
        new(LocationConstants.GhostlyDrone_Conservatory_03,     "Ghostly Drone: Conservatory 3",     LocationType.GhostlyDrone, "zoneConservatory",  "zoneConservatory_740_17_506"),   // parent: ComponentAcquisitionDroneConservatory2
        new(LocationConstants.GhostlyDrone_RainbowFields_01,    "Ghostly Drone: Rainbow Fields",     LocationType.GhostlyDrone, "zoneFields_Area1",  "zoneFields_Area1_219_62_277"),   // parent: ComponentAcquisitionDroneFields
        new(LocationConstants.GhostlyDrone_EmberValley_01,      "Ghostly Drone: Ember Valley",       LocationType.GhostlyDrone, "zoneGorge_Area3",  "zoneGorge_Area3_-615_82_692"),
        new(LocationConstants.GhostlyDrone_StarlightStrand_01,  "Ghostly Drone: Starlight Strand",   LocationType.GhostlyDrone, "zoneStrand_Area2", "zoneStrand_Area2_176_71_-395"),
        new(LocationConstants.GhostlyDrone_PowderfallBluffs_01, "Ghostly Drone: Powderfall Bluffs",  LocationType.GhostlyDrone, "zoneBluffs_Area4", "zoneBluffs_Area4_-1050_71_1823"),
        new(LocationConstants.GhostlyDrone_GreyLabyrinth_01,    "Ghostly Drone: Grey Labyrinth 1",   LocationType.GhostlyDrone, "zoneLabyrinthDreamland",             "zoneLabyrinthDreamland_1208_142_-834"),
        new(LocationConstants.GhostlyDrone_GreyLabyrinth_02,    "Ghostly Drone: Grey Labyrinth 2",   LocationType.GhostlyDrone, "zoneLabyrinthTerrarium_FoyerGazebo", "zoneLabyrinthTerrarium_FoyerGazebo_1941_170_-792"),
        new(LocationConstants.GhostlyDrone_GreyLabyrinth_03,    "Ghostly Drone: Grey Labyrinth 3",   LocationType.GhostlyDrone, "zoneLabyrinthCorePath",              "zoneLabyrinthCorePath_1415_57_-1008"),

        // -------------------------------------------------------------------------
        // CONVERSATION CONDITIONAL (10) — zone or chain access requirements
        // ConversationCheckMode.Conditional and above.
        // SceneName = rancher name.  GameObjectName = conversation debug name.
        // Note: BOb: Rainbow Lumps Blueprint and BOb: Yolky 1 also have ConversationConditional
        // type but are listed in their respective DECO / STORY sections below.
        // -------------------------------------------------------------------------

        new(LocationConstants.Conv_Viktor_IntroCall,           "Viktor: Intro Call",                  LocationType.ConversationConditional, "Viktor", "ViktorIntroCall"),
        new(LocationConstants.Conv_Viktor_EnergyBeamNode,     "Viktor: Radiant Projector Blueprint", LocationType.ConversationConditional, "Viktor", "ViktorStoryCipher4"),
        new(LocationConstants.Conv_Mochi_IntroCall,            "Mochi: Intro Call",                   LocationType.ConversationConditional, "Mochi",  "MochiIntroCall"),
        new(LocationConstants.Conv_Ogden_IntroCall,            "Ogden: Intro Call",                   LocationType.ConversationConditional, "Ogden",  "OgdenIntroCall"),
        new(LocationConstants.Conv_Thora_IntroCall,            "Thora: Intro Call",                   LocationType.ConversationConditional, "Thora",  "ThoraIntroCall"),
        new(LocationConstants.Conv_Thora_GordoSnareAdvanced,  "Thora: Gordo Snare Advanced",         LocationType.ConversationConditional, "Thora",  "ThoraGift2"),
        new(LocationConstants.Conv_BOb_IntroCall,              "BOb: Intro Call",                     LocationType.ConversationConditional, "BOb",    "BObGift1Intro"),  // → RainbowMound blueprint
        new(LocationConstants.Conv_Mochi_ArchiveKeyComponent, "Mochi: Archive Key",                  LocationType.ConversationConditional, "Mochi",  "MochiStoryDrones3"),

        // -------------------------------------------------------------------------
        // CONVERSATION KEY GIFTS (819701–819714) — functional gifts without strong access gates
        // ConversationCheckMode.All only.
        // -------------------------------------------------------------------------
        new(LocationConstants.Conv_Viktor_GadgetIntro,        "Viktor: Gadget Introduction",         LocationType.ConversationKeyGift, "Viktor", "ViktorGift_GadgetIntro"),
        new(LocationConstants.Conv_Viktor_TeleporterPink,     "Viktor: Teleporter Pink",             LocationType.ConversationKeyGift, "Viktor", "ViktorGift1_TeleporterPink"),
        new(LocationConstants.Conv_Viktor_TeleporterBlue,     "Viktor: Teleporter Blue",             LocationType.ConversationKeyGift, "Viktor", "ViktorGift2_TeleporterBlue"),
        new(LocationConstants.Conv_Viktor_TeleporterGrey,     "Viktor: Teleporter Grey",             LocationType.ConversationKeyGift, "Viktor", "ViktorGift3_TeleporterGrey"),
        new(LocationConstants.Conv_Viktor_TeleporterViolet,   "Viktor: Teleporter Violet",           LocationType.ConversationKeyGift, "Viktor", "ViktorGift4_TeleporterViolet"),
        new(LocationConstants.Conv_Viktor_TeleporterHomeBlue, "Viktor: Home Teleporter Blue",        LocationType.ConversationKeyGift, "Viktor", "ViktorGift_TeleporterHome1Blue"),
        new(LocationConstants.Conv_Viktor_TeleporterHomeRed,  "Viktor: Home Teleporter Red",         LocationType.ConversationKeyGift, "Viktor", "ViktorGift_TeleporterHome2Red"),
        new(LocationConstants.Conv_Viktor_TeleporterHomeGreen,"Viktor: Home Teleporter Green",       LocationType.ConversationKeyGift, "Viktor", "ViktorGift_TeleporterHome3Green"),
        new(LocationConstants.Conv_Ogden_SuperHydroTurret,    "Ogden: Super Hydro Turret",           LocationType.ConversationKeyGift, "Ogden",  "OgdenGift1"),
        new(LocationConstants.Conv_Ogden_PortableScareSlime,  "Ogden: Portable Scare Slime",         LocationType.ConversationKeyGift, "Ogden",  "OgdenGift3"),
        new(LocationConstants.Conv_Mochi_MarketLink,          "Mochi: Market Link",                  LocationType.ConversationKeyGift, "Mochi",  "MochiGift1"),

        // -------------------------------------------------------------------------
        // CONVERSATION DECORATION GIFTS (819714–819761)
        // ConversationCheckMode.All only.
        // -------------------------------------------------------------------------

        // BOb — Rainbow Lumps is ConversationConditional (EV+Strand gate); rest are All-only
        new(LocationConstants.Conv_BOb_RainbowLumps,            "BOb: Rainbow Lumps Blueprint",            LocationType.ConversationConditional, "BOb", "BObGift2"),
        new(LocationConstants.Conv_BOb_ThinCavePillar,          "BOb: Thin Cave Pillar Blueprint",         LocationType.ConversationDecoGift, "BOb", "BObGift3"),
        new(LocationConstants.Conv_BOb_RockClump,               "BOb: Rock Clump Blueprint",               LocationType.ConversationDecoGift, "BOb", "BObGift4"),
        new(LocationConstants.Conv_BOb_ShortMagmaClump,         "BOb: Short Magma Clump Blueprint",        LocationType.ConversationDecoGift, "BOb", "BObGift6"),
        new(LocationConstants.Conv_BOb_RockFragments,           "BOb: Rock Fragments Blueprint",           LocationType.ConversationDecoGift, "BOb", "BObGiftRelocated1"),
        new(LocationConstants.Conv_BOb_StalagmiteCluster,       "BOb: Stalagmite Cluster Blueprint",       LocationType.ConversationDecoGift, "BOb", "BObGiftRelocated2"),
        new(LocationConstants.Conv_BOb_RockCluster,             "BOb: Rock Cluster Blueprint",             LocationType.ConversationDecoGift, "BOb", "BObGiftRelocated3"),
        new(LocationConstants.Conv_BOb_SharpBoulder,            "BOb: Sharp Boulder Blueprint",            LocationType.ConversationDecoGift, "BOb", "BObGiftRelocated4"),

        // Mochi
        new(LocationConstants.Conv_Mochi_RootArches,            "Mochi: Root Arches Blueprint",            LocationType.ConversationDecoGift, "Mochi", "MochiGift3"),
        new(LocationConstants.Conv_Mochi_VioletSwirlShroom,     "Mochi: Violet Swirl Shroom Blueprint",    LocationType.ConversationDecoGift, "Mochi", "MochiGift4"),
        new(LocationConstants.Conv_Mochi_ShortPinkCoralColumns, "Mochi: Short Pink Coral Columns Blueprint",LocationType.ConversationDecoGift, "Mochi", "MochiGift6"),
        new(LocationConstants.Conv_Mochi_AshBlooms,             "Mochi: Ash Blooms Blueprint",             LocationType.ConversationDecoGift, "Mochi", "MochiGift7"),
        new(LocationConstants.Conv_Mochi_TallAshwood,           "Mochi: Tall Ashwood Blueprint",           LocationType.ConversationDecoGift, "Mochi", "MochiGift8"),
        new(LocationConstants.Conv_Mochi_ShortRedAshwood,       "Mochi: Short Red Ashwood Blueprint",      LocationType.ConversationDecoGift, "Mochi", "MochiGift9"),
        new(LocationConstants.Conv_Mochi_RoundedMagmaPool,      "Mochi: Rounded Magma Pool Blueprint",     LocationType.ConversationDecoGift, "Mochi", "MochiGift10"),
        new(LocationConstants.Conv_Mochi_PinkGlowShroom,        "Mochi: Pink Glow Shroom Blueprint",       LocationType.ConversationDecoGift, "Mochi", "MochiGiftRelocated1"),
        new(LocationConstants.Conv_Mochi_GnarledAshwood,        "Mochi: Gnarled Ashwood Blueprint",        LocationType.ConversationDecoGift, "Mochi", "MochiGiftRelocated2"),
        new(LocationConstants.Conv_Mochi_AzureGlowShroom,       "Mochi: Azure Glow Shroom Blueprint",      LocationType.ConversationDecoGift, "Mochi", "MochiGiftRelocated3"),
        new(LocationConstants.Conv_Mochi_MediumRedAshwood,      "Mochi: Medium Red Ashwood Blueprint",     LocationType.ConversationDecoGift, "Mochi", "MochiGiftRelocated4"),

        // Ogden
        new(LocationConstants.Conv_Ogden_TallEmeraldCypress,    "Ogden: Tall Emerald Cypress Blueprint",   LocationType.ConversationDecoGift, "Ogden", "OgdenGift4"),
        new(LocationConstants.Conv_Ogden_EmeraldShrubs,         "Ogden: Emerald Shrubs Blueprint",         LocationType.ConversationDecoGift, "Ogden", "OgdenGift6"),
        new(LocationConstants.Conv_Ogden_AmberShrubs,           "Ogden: Amber Shrubs Blueprint",           LocationType.ConversationDecoGift, "Ogden", "OgdenGift7"),
        new(LocationConstants.Conv_Ogden_OchrePoppies,          "Ogden: Ochre Poppies Blueprint",          LocationType.ConversationDecoGift, "Ogden", "OgdenGift8"),
        new(LocationConstants.Conv_Ogden_EmeraldVineTrellis,    "Ogden: Emerald Vine Trellis Blueprint",   LocationType.ConversationDecoGift, "Ogden", "OgdenGift9"),
        new(LocationConstants.Conv_Ogden_PinkBonsai,            "Ogden: Pink Bonsai Blueprint",            LocationType.ConversationDecoGift, "Ogden", "OgdenGift10"),
        new(LocationConstants.Conv_Ogden_AzureShrubs,           "Ogden: Azure Shrubs Blueprint",           LocationType.ConversationDecoGift, "Ogden", "OgdenGiftRelocated1"),
        new(LocationConstants.Conv_Ogden_AmberCypress,          "Ogden: Amber Cypress Blueprint",          LocationType.ConversationDecoGift, "Ogden", "OgdenGiftRelocated3"),
        new(LocationConstants.Conv_Ogden_MediumPinkCoralColumns,"Ogden: Medium Pink Coral Columns Blueprint",LocationType.ConversationDecoGift,"Ogden","OgdenGiftRelocated4"),

        // Thora
        new(LocationConstants.Conv_Thora_PinkMangrove,          "Thora: Pink Mangrove Blueprint",          LocationType.ConversationDecoGift, "Thora", "ThoraGift1"),
        new(LocationConstants.Conv_Thora_RainbowGrass,          "Thora: Rainbow Grass Blueprint",          LocationType.ConversationDecoGift, "Thora", "ThoraGift3"),
        new(LocationConstants.Conv_Thora_PinkGrass,             "Thora: Pink Grass Blueprint",             LocationType.ConversationDecoGift, "Thora", "ThoraGift4"),
        new(LocationConstants.Conv_Thora_AmberGrass,            "Thora: Amber Grass Blueprint",            LocationType.ConversationDecoGift, "Thora", "ThoraGift5"),
        new(LocationConstants.Conv_Thora_ShortPalm,             "Thora: Short Palm Blueprint",             LocationType.ConversationDecoGift, "Thora", "ThoraGift6"),
        new(LocationConstants.Conv_Thora_GreenGrass,            "Thora: Green Grass Blueprint",            LocationType.ConversationDecoGift, "Thora", "ThoraGift7"),
        new(LocationConstants.Conv_Thora_RedGrass,              "Thora: Red Grass Blueprint",              LocationType.ConversationDecoGift, "Thora", "ThoraGift8"),
        new(LocationConstants.Conv_Thora_GoldpetalFlowers,      "Thora: Goldpetal Flowers Blueprint",      LocationType.ConversationDecoGift, "Thora", "ThoraGiftRelocated1"),
        new(LocationConstants.Conv_Thora_AzureGrass,            "Thora: Azure Grass Blueprint",            LocationType.ConversationDecoGift, "Thora", "ThoraGiftRelocated2"),
        new(LocationConstants.Conv_Thora_CinderSpikeBlossoms,   "Thora: Cinder Spike Blossoms Blueprint",  LocationType.ConversationDecoGift, "Thora", "ThoraGiftRelocated3"),
        new(LocationConstants.Conv_Thora_SunfireDaisies,        "Thora: Sunfire Daisies Blueprint",        LocationType.ConversationDecoGift, "Thora", "ThoraGiftRelocated4"),

        // Viktor decorative
        new(LocationConstants.Conv_Viktor_PinwheelLarge,        "Viktor: Large Pinwheel Blueprint",        LocationType.ConversationDecoGift, "Viktor", "ViktorGift_PinwheelLarge"),
        new(LocationConstants.Conv_Viktor_PinwheelSmall,        "Viktor: Small Pinwheel Blueprint",        LocationType.ConversationDecoGift, "Viktor", "ViktorGift_PinwheelSmall"),
        new(LocationConstants.Conv_Viktor_Streamer,             "Viktor: Streamer Blueprint",              LocationType.ConversationDecoGift, "Viktor", "ViktorGift_StreamerSimple"),
        new(LocationConstants.Conv_Viktor_StreamerScarf,        "Viktor: Streamer Scarf Blueprint",        LocationType.ConversationDecoGift, "Viktor", "ViktorGift_StreamerScarf"),
        new(LocationConstants.Conv_Viktor_SimpleFlag,           "Viktor: Simple Flag Blueprint",           LocationType.ConversationDecoGift, "Viktor", "ViktorGift_FlagSimple"),
        new(LocationConstants.Conv_Viktor_WindSocks,            "Viktor: Wind Socks Blueprint",            LocationType.ConversationDecoGift, "Viktor", "ViktorGift_Windsocks"),
        new(LocationConstants.Conv_Viktor_FlagAttention,        "Viktor: Attention Flag Blueprint",        LocationType.ConversationDecoGift, "Viktor", "ViktorGift_FlagAttention"),
        new(LocationConstants.Conv_Viktor_FlagCautious,         "Viktor: Cautious Flag Blueprint",         LocationType.ConversationDecoGift, "Viktor", "ViktorGift_FlagCautious"),
        new(LocationConstants.Conv_Viktor_FlagCurious,          "Viktor: Curious Flag Blueprint",          LocationType.ConversationDecoGift, "Viktor", "ViktorGift_FlagCurious"),

        // -------------------------------------------------------------------------
        // CONVERSATION NON-GIFTS (819762–819815)
        // ConversationCheckMode.All only (except BOb: Yolky 1 which is Conditional).
        // -------------------------------------------------------------------------

        new(LocationConstants.Conv_Viktor_StoryCipher1,         "Viktor: Cipher 1",                        LocationType.ConversationNonGift, "Viktor", "ViktorStoryCipher1"),
        new(LocationConstants.Conv_Viktor_StoryCipher2,         "Viktor: Cipher 2",                        LocationType.ConversationNonGift, "Viktor", "ViktorStoryCipher2"),
        new(LocationConstants.Conv_Viktor_StoryCipher3,         "Viktor: Cipher 3",                        LocationType.ConversationNonGift, "Viktor", "ViktorStoryCipher3"),
        new(LocationConstants.Conv_Viktor_Deflect1,             "Viktor: Deflect 1",                       LocationType.ConversationNonGift, "Viktor", "ViktorDeflect1"),
        new(LocationConstants.Conv_Viktor_Deflect2,             "Viktor: Deflect 2",                       LocationType.ConversationNonGift, "Viktor", "ViktorDeflect2"),
        new(LocationConstants.Conv_Viktor_Deflect3,             "Viktor: Deflect 3",                       LocationType.ConversationNonGift, "Viktor", "ViktorDeflect3"),

        new(LocationConstants.Conv_Mochi_StoryDrones1,          "Mochi: Drone Story 1",                    LocationType.ConversationNonGift, "Mochi", "MochiStoryDrones1"),
        new(LocationConstants.Conv_Mochi_StoryDrones2,          "Mochi: Drone Story 2",                    LocationType.ConversationNonGift, "Mochi", "MochiStoryDrones2"),
        new(LocationConstants.Conv_Mochi_Deflect1,              "Mochi: Deflect 1",                        LocationType.ConversationNonGift, "Mochi", "MochiDeflect1"),
        new(LocationConstants.Conv_Mochi_Deflect2,              "Mochi: Deflect 2",                        LocationType.ConversationNonGift, "Mochi", "MochiDeflect2"),
        new(LocationConstants.Conv_Mochi_Deflect3,              "Mochi: Deflect 3",                        LocationType.ConversationNonGift, "Mochi", "MochiDeflect3"),

        new(LocationConstants.Conv_Ogden_StoryRainbow1,         "Ogden: Rainbow Story 1",                  LocationType.ConversationNonGift, "Ogden", "OgdenStoryRainbow1"),
        new(LocationConstants.Conv_Ogden_StoryRainbow2,         "Ogden: Rainbow Story 2",                  LocationType.ConversationNonGift, "Ogden", "OgdenStoryRainbow2"),
        new(LocationConstants.Conv_Ogden_StoryRainbow3,         "Ogden: Rainbow Story 3",                  LocationType.ConversationNonGift, "Ogden", "OgdenStoryRainbow3"),
        new(LocationConstants.Conv_Ogden_Deflect1,              "Ogden: Deflect 1",                        LocationType.ConversationNonGift, "Ogden", "OgdenDeflect1"),
        new(LocationConstants.Conv_Ogden_Deflect2,              "Ogden: Deflect 2",                        LocationType.ConversationNonGift, "Ogden", "OgdenDeflect2"),
        new(LocationConstants.Conv_Ogden_Deflect3,              "Ogden: Deflect 3",                        LocationType.ConversationNonGift, "Ogden", "OgdenDeflect3"),

        new(LocationConstants.Conv_Thora_StoryBigNews1,         "Thora: Big News 1",                       LocationType.ConversationNonGift, "Thora", "ThoraStoryBigNews1"),
        new(LocationConstants.Conv_Thora_StoryBigNews2,         "Thora: Big News 2",                       LocationType.ConversationNonGift, "Thora", "ThoraStoryBigNews2"),
        new(LocationConstants.Conv_Thora_StoryBigNews3,         "Thora: Big News 3",                       LocationType.ConversationNonGift, "Thora", "ThoraStoryBigNews3"),
        new(LocationConstants.Conv_Thora_Deflect1,              "Thora: Deflect 1",                        LocationType.ConversationNonGift, "Thora", "ThoraDeflect1"),
        new(LocationConstants.Conv_Thora_Deflect2,              "Thora: Deflect 2",                        LocationType.ConversationNonGift, "Thora", "ThoraDeflect2"),
        new(LocationConstants.Conv_Thora_Deflect3,              "Thora: Deflect 3",                        LocationType.ConversationNonGift, "Thora", "ThoraDeflect3"),

        new(LocationConstants.Conv_BOb_Yolky1,                  "BOb: Yolky 1",                            LocationType.ConversationConditional, "BOb", "BObYolky1"),

        new(LocationConstants.Conv_Gigi_Intro,                  "Gigi: Intro",                             LocationType.ConversationNonGift, "Gigi", "GigiIntro"),
        new(LocationConstants.Conv_Gigi_IntroAlt,               "Gigi: Intro Alt",                         LocationType.ConversationNonGift, "Gigi", "GigiIntroAlt"),
        new(LocationConstants.Conv_Gigi_Hub,                    "Gigi: Hub",                               LocationType.ConversationNonGift, "Gigi", "GigiHub"),
        new(LocationConstants.Conv_Gigi_ShadowPlort,            "Gigi: Shadow Plort",                      LocationType.ConversationNonGift, "Gigi", "GigiShadowPlort"),
        new(LocationConstants.Conv_Gigi_Architecture,           "Gigi: Architecture",                      LocationType.ConversationNonGift, "Gigi", "GigiArchitecture"),
        new(LocationConstants.Conv_Gigi_Detector,               "Gigi: Detector",                          LocationType.ConversationNonGift, "Gigi", "GigiDetector"),
        new(LocationConstants.Conv_Gigi_DetectorAlt,            "Gigi: Detector Alt",                      LocationType.ConversationNonGift, "Gigi", "GigiDetectorAlt"),
        new(LocationConstants.Conv_Gigi_Secret,                 "Gigi: Secret",                            LocationType.ConversationNonGift, "Gigi", "GigiSecret"),
        new(LocationConstants.Conv_Gigi_DreamWorld,             "Gigi: Dream World",                       LocationType.ConversationNonGift, "Gigi", "GigiDreamWorld"),
        new(LocationConstants.Conv_Gigi_DreamGold,              "Gigi: Dream Gold",                        LocationType.ConversationNonGift, "Gigi", "GigiDreamGold"),
        new(LocationConstants.Conv_Gigi_EndTemp,                "Gigi: End (Temp)",                        LocationType.ConversationNonGift, "Gigi", "GigiEndTemp"),
        new(LocationConstants.Conv_Gigi_BadConnection,          "Gigi: Bad Connection",                    LocationType.ConversationNonGift, "Gigi", "GigiBadConnection"),
        new(LocationConstants.Conv_Gigi_EnteringTerrarium,      "Gigi: Entering Terrarium",                LocationType.ConversationNonGift, "Gigi", "GigiEnteringTerrarium"),
        new(LocationConstants.Conv_Gigi_MiniatureMuseum,        "Gigi: Miniature Museum",                  LocationType.ConversationNonGift, "Gigi", "GigiMiniatureMuseum"),
        new(LocationConstants.Conv_Gigi_OutsideGoldRoom,        "Gigi: Outside Gold Room",                 LocationType.ConversationNonGift, "Gigi", "GigiOutsideGoldRoom"),
        new(LocationConstants.Conv_Gigi_AlcoveWithTree,         "Gigi: Alcove With Tree",                  LocationType.ConversationNonGift, "Gigi", "GigiAlcoveWithTree"),
        new(LocationConstants.Conv_Gigi_Core_Intro,             "Gigi: Core Intro",                        LocationType.ConversationNonGift, "Gigi", "GigiCore_Intro"),
        new(LocationConstants.Conv_Gigi_RewardsKiosk,           "Gigi: Rewards Kiosk",                     LocationType.ConversationNonGift, "Gigi", "GigiRewardsKiosk"),
        new(LocationConstants.Conv_Gigi_RewardsKiosk_PlortReminder,       "Gigi: Plort Reminder",          LocationType.ConversationNonGift, "Gigi", "GigiRewardsKiosk_PlortReminder"),
        new(LocationConstants.Conv_Gigi_RewardsKiosk_HarmonizerReminder,  "Gigi: Harmonizer Reminder",     LocationType.ConversationNonGift, "Gigi", "GigiRewardsKiosk_HarmonizerReminder"),
        new(LocationConstants.Conv_Gigi_RewardsKiosk_PostGameDialog,      "Gigi: Post-Game Dialog",        LocationType.ConversationNonGift, "Gigi", "GigiRewardsKiosk_PostGameDialog"),
        new(LocationConstants.Conv_Gigi_RewardsKiosk_BossFightPrompt,     "Gigi: Boss Fight Prompt",       LocationType.ConversationNonGift, "Gigi", "GigiRewardsKiosk_BossFightPrompt"),
        new(LocationConstants.Conv_Gigi_Core_HarmonizerQuest,  "Gigi: Harmonizer Quest",                  LocationType.ConversationNonGift, "Gigi", "GigiCore_HarmonizerQuest"),
        new(LocationConstants.Conv_Gigi_Core_HydroShower,      "Gigi: Hydro Shower",                      LocationType.ConversationNonGift, "Gigi", "GigiCore_HydroShower"),
        new(LocationConstants.Conv_Gigi_Core_NeedsWaterTank,   "Gigi: Needs Water Tank",                  LocationType.ConversationNonGift, "Gigi", "GigiCore_NeedsWaterTank"),
        new(LocationConstants.Conv_Gigi_Core_StartFight,       "Gigi: Start Fight",                       LocationType.ConversationNonGift, "Gigi", "GigiCore_StartFight"),
        new(LocationConstants.Conv_Gigi_Core_StartFightAlt,    "Gigi: Start Fight Alt",                   LocationType.ConversationNonGift, "Gigi", "GigiCore_StartFightAlt"),
        new(LocationConstants.Conv_Gigi_Core_RetryFight,       "Gigi: Retry Fight",                       LocationType.ConversationNonGift, "Gigi", "GigiCore_RetryFight"),
        new(LocationConstants.Conv_Gigi_Core_FightComplete,    "Gigi: Fight Complete",                    LocationType.ConversationNonGift, "Gigi", "GigiCore_FightComplete"),
        new(LocationConstants.Conv_Gigi_Core_PostGame,         "Gigi: Post Game",                         LocationType.ConversationNonGift, "Gigi", "GigiCore_PostGame"),

        // -------------------------------------------------------------------------
        // RADIANT SLIMEPEDIA ENTRIES (819821–819842)
        // 22 entries confirmed via DumpRadiantSlimes() — alphabetical order.
        // Lookup key: RadiantSlimePediaEntry.name (stable Unity asset name).
        // The existing SlimepediaPatch on PediaDirector.Unlock(PediaEntry, bool) fires
        // for RadiantSlimePediaEntry because it extends PediaEntry — no extra patch needed.
        // Enabled when SlotData.RandomizeSlimepediaRadiant is true.
        // -------------------------------------------------------------------------
        new(LocationConstants.SlimepediaRadiant_Angler,   "Radiant Slimepedia: Angler",   LocationType.SlimepediaRadiantEntry, "", "", "RadiantAngler"),
        new(LocationConstants.SlimepediaRadiant_Batty,    "Radiant Slimepedia: Batty",    LocationType.SlimepediaRadiantEntry, "", "", "RadiantBatty"),
        new(LocationConstants.SlimepediaRadiant_Boom,     "Radiant Slimepedia: Boom",     LocationType.SlimepediaRadiantEntry, "", "", "RadiantBoom"),
        new(LocationConstants.SlimepediaRadiant_Cotton,   "Radiant Slimepedia: Cotton",   LocationType.SlimepediaRadiantEntry, "", "", "RadiantCotton"),
        new(LocationConstants.SlimepediaRadiant_Crystal,  "Radiant Slimepedia: Crystal",  LocationType.SlimepediaRadiantEntry, "", "", "RadiantCrystal"),
        new(LocationConstants.SlimepediaRadiant_Dervish,  "Radiant Slimepedia: Dervish",  LocationType.SlimepediaRadiantEntry, "", "", "RadiantDervish"),
        new(LocationConstants.SlimepediaRadiant_Fire,     "Radiant Slimepedia: Fire",     LocationType.SlimepediaRadiantEntry, "", "", "RadiantFire"),
        new(LocationConstants.SlimepediaRadiant_Flutter,  "Radiant Slimepedia: Flutter",  LocationType.SlimepediaRadiantEntry, "", "", "RadiantFlutter"),
        new(LocationConstants.SlimepediaRadiant_Honey,    "Radiant Slimepedia: Honey",    LocationType.SlimepediaRadiantEntry, "", "", "RadiantHoney"),
        new(LocationConstants.SlimepediaRadiant_Hunter,   "Radiant Slimepedia: Hunter",   LocationType.SlimepediaRadiantEntry, "", "", "RadiantHunter"),
        new(LocationConstants.SlimepediaRadiant_Hyper,    "Radiant Slimepedia: Hyper",    LocationType.SlimepediaRadiantEntry, "", "", "RadiantHyper"),
        new(LocationConstants.SlimepediaRadiant_Phosphor, "Radiant Slimepedia: Phosphor", LocationType.SlimepediaRadiantEntry, "", "", "RadiantPhosphor"),
        new(LocationConstants.SlimepediaRadiant_Pink,     "Radiant Slimepedia: Pink",     LocationType.SlimepediaRadiantEntry, "", "", "RadiantPink"),
        new(LocationConstants.SlimepediaRadiant_Puddle,   "Radiant Slimepedia: Puddle",   LocationType.SlimepediaRadiantEntry, "", "", "RadiantPuddle"),
        new(LocationConstants.SlimepediaRadiant_Ringtail, "Radiant Slimepedia: Ringtail", LocationType.SlimepediaRadiantEntry, "", "", "RadiantRingtail"),
        new(LocationConstants.SlimepediaRadiant_Rock,     "Radiant Slimepedia: Rock",     LocationType.SlimepediaRadiantEntry, "", "", "RadiantRock"),
        new(LocationConstants.SlimepediaRadiant_Saber,    "Radiant Slimepedia: Saber",    LocationType.SlimepediaRadiantEntry, "", "", "RadiantSaber"),
        new(LocationConstants.SlimepediaRadiant_Sloomber, "Radiant Slimepedia: Sloomber", LocationType.SlimepediaRadiantEntry, "", "", "RadiantSloomber"),
        new(LocationConstants.SlimepediaRadiant_Tabby,    "Radiant Slimepedia: Tabby",    LocationType.SlimepediaRadiantEntry, "", "", "RadiantTabby"),
        new(LocationConstants.SlimepediaRadiant_Tangle,   "Radiant Slimepedia: Tangle",   LocationType.SlimepediaRadiantEntry, "", "", "RadiantTangle"),
        new(LocationConstants.SlimepediaRadiant_Twin,     "Radiant Slimepedia: Twin",     LocationType.SlimepediaRadiantEntry, "", "", "RadiantTwin"),
        new(LocationConstants.SlimepediaRadiant_Yolky,    "Radiant Slimepedia: Yolky",    LocationType.SlimepediaRadiantEntry, "", "", "RadiantYolky"),
    };

    // -------------------------------------------------------------------------
    // Lookup dictionaries
    // -------------------------------------------------------------------------

    /// <summary>
    /// Keyed by <c>GameObjectName</c> (posKey or gameObject.name).
    /// Covers: TreasurePod, Gordo, MapNode, GhostlyDrone, ShadowPlortDoor.
    /// Excludes ResearchDrone, SlimepediaEntry (EntryName lookup), FabricatorCraft,
    /// and all Conversation types (ConversationDebugName lookup).
    /// </summary>
    private static readonly Dictionary<string, LocationInfo> _byGameObjectName
        = All.Where(l => l.Type != LocationType.ResearchDrone
                      && l.Type != LocationType.ResearchDroneArchive
                      && l.Type != LocationType.SlimepediaEntry
                      && l.Type != LocationType.SlimepediaResourceEntry
                      && l.Type != LocationType.SlimepediaRadiantEntry   // keyed by EntryName, GameObjectName is ""
                      && l.Type != LocationType.FabricatorCraft
                      && l.Type != LocationType.ConversationConditional
                      && l.Type != LocationType.ConversationKeyGift
                      && l.Type != LocationType.ConversationDecoGift
                      && l.Type != LocationType.ConversationNonGift)
             .ToDictionary(l => l.GameObjectName);

    /// <summary>
    /// Keyed by conversation debug name (e.g. "ViktorGift1_TeleporterPink").
    /// Covers all three conversation types: ConversationKeyGift, ConversationDecoGift,
    /// ConversationNonGift. Used by <c>ConversationRecordedPatch</c>.
    /// </summary>
    private static readonly Dictionary<string, LocationInfo> _byConversationDebugName
        = All.Where(l => l.Type == LocationType.ConversationConditional
                      || l.Type == LocationType.ConversationKeyGift
                      || l.Type == LocationType.ConversationDecoGift
                      || l.Type == LocationType.ConversationNonGift)
             .ToDictionary(l => l.GameObjectName);

    /// <summary>
    /// Keyed by entry asset name. Used by ResearchDrone and SlimepediaEntry locations.
    /// FabricatorCraft entries are excluded even though they have a non-null EntryName —
    /// those are duplicate-keyed by design and handled by a separate mechanism.
    /// </summary>
    private static readonly Dictionary<string, LocationInfo> _byEntryName
        = All.Where(l => (l.Type == LocationType.ResearchDrone
                       || l.Type == LocationType.ResearchDroneArchive
                       || l.Type == LocationType.SlimepediaEntry
                       || l.Type == LocationType.SlimepediaResourceEntry
                       || l.Type == LocationType.SlimepediaRadiantEntry)   // EntryName = RadiantXxx asset name
                      && l.EntryName != null)
             .ToDictionary(l => l.EntryName!);

    /// <summary>Keyed by numeric location ID.</summary>
    private static readonly Dictionary<long, LocationInfo> _byId
        = All.ToDictionary(l => l.Id);

    // -------------------------------------------------------------------------
    // Lookup methods
    // -------------------------------------------------------------------------

    /// <summary>
    /// Look up by <c>GameObjectName</c> / posKey.
    /// Used by TreasurePod, Gordo, MapNode, GhostlyDrone, and ShadowPlortDoor patches.
    /// </summary>
    public static bool TryGetByObjectName(string name, out LocationInfo? info)
        => _byGameObjectName.TryGetValue(name, out info);

    /// <summary>
    /// Look up a Research Drone location by its <c>ResearchDroneEntry</c> asset name.
    /// Used only by <c>ResearchDronePatch</c>.
    /// </summary>
    public static bool TryGetByEntryName(string entryName, out LocationInfo? info)
        => _byEntryName.TryGetValue(entryName, out info);

    /// <summary>
    /// Returns all Fabricator craft entries for a given upgrade type, in ID order.
    /// Used by FabricatorPatch to assign sequential check IDs.
    /// </summary>
    public static IReadOnlyList<LocationInfo> GetFabricatorCrafts(string upgradeName)
        => All.Where(l => l.Type == LocationType.FabricatorCraft && l.EntryName == upgradeName)
              .OrderBy(l => l.Id)
              .ToList();

    /// <summary>
    /// Look up a conversation location by its debug name (e.g. "ViktorGift1_TeleporterPink").
    /// Used by <c>ConversationRecordedPatch</c> after <c>FixedConversation.RecordPlayed()</c>.
    /// </summary>
    public static bool TryGetByConversation(string debugName, out LocationInfo? info)
        => _byConversationDebugName.TryGetValue(debugName, out info);

    /// <summary>
    /// Returns true if a conversation location of the given type should be checked
    /// under the current <see cref="ConversationCheckMode"/>.
    /// </summary>
    public static bool IsConversationIncluded(LocationType type, ConversationCheckMode mode)
        => mode switch
        {
            ConversationCheckMode.Conditional => type == LocationType.ConversationConditional,
            ConversationCheckMode.All         => type == LocationType.ConversationConditional
                                             || type == LocationType.ConversationKeyGift
                                             || type == LocationType.ConversationDecoGift
                                             || type == LocationType.ConversationNonGift,
            _                                 => false,   // Off
        };

    /// <summary>Look up a location by its numeric ID.</summary>
    public static LocationInfo? GetById(long id)
        => _byId.TryGetValue(id, out var info) ? info : null;
}
