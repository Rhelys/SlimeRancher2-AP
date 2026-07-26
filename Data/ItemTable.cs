namespace SlimeRancher2AP.Data;

public enum ItemType { RegionAccess, Upgrade, Gadget, Filler, Useful, UpgradeComponent, Trap, ConservatoryExpansion, RanchPlot }

/// <summary>Describes a single Archipelago item this game can send or receive.</summary>
public record ItemInfo(long Id, string Name, ItemType Type);

/// <summary>
/// Master table of all items and their IDs. IDs MUST match the companion Python apworld exactly.
/// Base offset: 819000 (items start at 819500).
///
/// Verified against Archipelago/worlds/slime_rancher_2/items.py (2026-04-12).
/// GadgetDefinition asset names marked (?) have not been confirmed via DumpGadgets yet.
/// </summary>
public static class ItemTable
{
    // -------------------------------------------------------------------------
    // Item IDs — must match items.py exactly
    // -------------------------------------------------------------------------

    // Region Access: 819500–819502
    public const long EmberValleyAccess      = 819500;
    public const long StarlightStrandAccess  = 819501;
    public const long PowderfallBluffsAccess = 819502;

    // Special Access: 819510
    public const long RadiantProjectorBlueprint = 819510; // grants EnergyBeamNode blueprint

    // Crafting Components: 819511–819514, 819530–819536
    // Consumed by Fabricator recipes; received from AP pool (one per consuming craft tier).
    // Game asset names confirmed via upgrade_components.txt dump (2026-04-13).
    public const long ArchiveKeyComponent  = 819511; // → ArchiveKeyComponent      (Drone Archive Key ×1)
    public const long SureshotModule       = 819512; // → SureShotComponent        (Golden Sureshot I/II/III ×3)
    public const long TankLiner            = 819513; // → TankGuardComponent       (Tank Guard I/II/III ×3)
    public const long HeartCell            = 819514; // → HeartModuleComponent     (Health Tank II/III/IV ×3)
    public const long PowerChip            = 819530; // → PowerCoreComponent       (Energy Tank II/III/IV/V ×4)
    public const long DashBootModule       = 819531; // → DashBootComponent        (Dash Boots II ×1)
    public const long JetpackDrive         = 819532; // → JetpackComponent         (Jetpack II ×1)
    public const long StorageCell          = 819533; // → TankBoosterComponent     (Tank Booster II–VIII ×7)
    public const long ShadowSureshotModule = 819534; // → ShadowSureShotComponent  (Shadow Sureshot ×1)
    public const long InjectorModule       = 819535; // → PowerInjectorComponent   (Power Injector I/II ×2)
    public const long RegenModule          = 819536; // → RegenComponent           (Regenerator I/II ×2)
    public const long VacTank              = 819538; // → ExtraTankComponent       (Extra Tank II ×1)

    // Progressive Vacpack Upgrades: 819515–819529 (15 IDs, each received N times per apworld)
    public const long ProgressiveHealthTank        = 819515; // HealthCapacity   × 4
    public const long ProgressiveEnergyTank        = 819516; // EnergyCapacity   × 5
    public const long ProgressiveExtraTank         = 819517; // AmmoSlots        × 2
    public const long ProgressiveJetpack           = 819518; // Jetpack          × 2
    public const long ProgressiveWaterTank         = 819519; // LiquidSlot       × 1
    public const long ProgressiveDashBoots         = 819520; // RunEfficiency    × 2
    public const long ProgressiveTankBooster       = 819521; // AmmoCapacity     × 8
    public const long ProgressivePowerInjector     = 819522; // EnergyDelay      × 2
    public const long ProgressiveRegenerator       = 819523; // EnergyRegen      × 2
    public const long ProgressiveGoldenSureshot    = 819524; // GoldenSureshot   × 3
    public const long ProgressiveShadowSureshot    = 819525; // ShadowSureshot   × 1
    public const long ProgressiveTankGuard         = 819526; // TankGuard        × 3
    public const long PulseWave           = 819527; // PulseWave             × 1
    public const long ResourceHarvester   = 819528; // ResourceNodeHarvester × 1
    public const long DroneArchiveKey     = 819529; // ArchiveKey            × 1

    // Useful — Quantum Drone Station: 819537 (formerly "Drone Station Module").
    // First copy grants the DroneStation blueprint + one placeable station; each copy
    // also adds one ComponentAcqDrone module (another craftable station).
    public const long QuantumDroneStation = 819537;

    // Gadgets: 819540–819557
    // Zone Teleporters — one per region (confirmed via DumpGadgets)
    public const long TeleporterEmberValley      = 819540; // TeleporterZoneGorge
    public const long TeleporterStarlightStrand  = 819541; // TeleporterZoneStrand
    public const long TeleporterPowderfallBluffs = 819542; // TeleporterZoneBluffs
    public const long TeleporterGreyLabyrinth    = 819543; // TeleporterZoneLabyrinth

    // Home Teleporters — four colours (confirmed via DumpGadgets)
    public const long HomeTeleporterBlue   = 819544; // TeleporterHomeBlue
    public const long HomeTeleporterGreen  = 819545; // TeleporterHomeGreen
    public const long HomeTeleporterRed    = 819546; // TeleporterHomeRed
    public const long HomeTeleporterYellow = 819547; // TeleporterHomeYellow

    // Warp Depots — four variants (confirmed via DumpGadgets)
    public const long WarpDepotGrey   = 819548; // WarpDepotGrey
    public const long WarpDepotBerry  = 819549; // WarpDepotBerry  (orange-coloured)
    public const long WarpDepotViolet = 819550; // WarpDepotViolet
    public const long WarpDepotSnowy  = 819551; // WarpDepotSnowy

    // Functional gadgets (?)
    public const long MarketLink        = 819552; // MarketLink        (?)
    public const long SuperHydroTurret  = 819553; // SuperHydroTurret  (?)
    public const long PortableScareSlime= 819554; // PortableScareSlime (?)
    public const long GordoSnareAdvanced= 819555; // GordoSnareAdvanced (?)
    public const long MedStation        = 819556; // MedStation        (?)
    public const long DreamLanternT2    = 819557; // DreamLanternT2    (confirmed: DreamLanternT2)

    // Movement / utility gadgets — always in the apworld pool (items.py GADGET_ITEMS)
    public const long DashPad           = 819558; // DashPad           (confirmed via DumpGadgets)
    public const long SpringPad         = 819559; // SpringPad         (confirmed via DumpGadgets)
    public const long PortableWaterTap  = 819560; // PortableWaterTap  (confirmed via DumpGadgets)

    // Labyrinth goal gadget — in pool ×3 for prismacore/slimepedia goals (items.py)
    public const long DisruptionDetector = 819561; // PrismaDisruptionDetector (confirmed via DumpGadgets)

    // Filler — Newbucks: 819580–819582
    public const long Newbucks250  = 819580;
    public const long Newbucks500  = 819581;
    public const long Newbucks1000 = 819582;

    // Filler — Plort Caches: 819590–819592
    public const long CommonPlortCache   = 819590;
    public const long UncommonPlortCache = 819591;
    public const long RarePlortCache     = 819592;

    // Filler — Craft Caches: 819600–819605
    public const long RainbowFieldsCraftCache    = 819600;
    public const long EmberValleyCraftCache      = 819601;
    public const long StarlightStrandCraftCache  = 819602;
    public const long PowderfallBluffsCraftCache = 819603;
    public const long GreyLabyrinthCraftCache    = 819604;
    public const long RareCraftCache             = 819605;

    // Filler — Slime Ring & Weather Change: 819610–819611
    public const long SlimeRing     = 819610; // spawns common slimes in a ring around the player
    public const long WeatherChange = 819611; // triggers random Heavy/SlimeRain weather for 3 minutes

    // Market Recovery: 819635–819636 — in pool when plort_market_mode is 5_items/10_items.
    // Each reduces every plort's saturation by the stated fraction of its FullSaturation cap;
    // implemented declaratively in Patches/EconomyPatches/PlortMarketModePatch.
    public const long MarketRecovery20 = 819635; // Market Recovery (20%) ×5
    public const long MarketRecovery10 = 819636; // Market Recovery (10%) ×10

    // Conservatory Expansions: 819630–819634
    // Received from AP pool; opens the corresponding AccessDoor in the Conservatory.
    // Door _id strings confirmed via F9 → Dumps → "Dump Access Doors" in the Conservatory.
    public const long ExpansionGully     = 819630;
    public const long ExpansionTidepools = 819631;
    public const long ExpansionArchway   = 819632;
    public const long ExpansionDen       = 819633;
    public const long ExpansionDigsite   = 819634;

    // Traps: 819612–819629
    public const long TrapTarrSpawn   = 819612; // spawns Tarr near the player
    public const long TrapTeleport    = 819613; // teleports player to a random accessible zone
    public const long TrapTarrRain    = 819614; // triggers Slime Rain weather but overrides spawns to Tarr
    public const long TrapVacExpel    = 819615; // expels all vacpack contents as world objects and clears slots
    public const long TrapVacFill = 819616; // fills all empty vacpack slots with Pink slimes

    // Ranch Plot randomization: 819640–819672 — three tiers, each behind its own apworld
    // option. Counts are always derived from the AP server snapshot (RanchPlotHandler),
    // never from replay state, so these items are safe to re-apply.
    //
    // Per-area plot unlocks (randomize_plots) — progressive; count per area from the
    // in-game plot dump (docs/dumps/plot.txt): region cell name in comment.
    public const long RanchPlotConservatory = 819640; // cellConservatory      ×8
    public const long RanchPlotGully        = 819641; // cellExpansionGully    ×5
    public const long RanchPlotTidepools    = 819642; // cellExpansionPools    ×5
    public const long RanchPlotArchway      = 819643; // cellExpansionArchway  ×5
    public const long RanchPlotDen          = 819644; // cellExpansionDen      ×5
    public const long RanchPlotDigsite      = 819645; // cellExpansionDigsite  ×4

    // Building type unlocks (randomize_plot_buildings) — PlotPatchPurchaseItemModel asset
    // name in comment (the gate key).
    public const long CorralPlans      = 819646; // 'Corral Patch'
    public const long CoopPlans        = 819647; // 'Coop Patch'
    public const long GardenPlans      = 819648; // 'Garden Patch'
    public const long SiloPlans        = 819649; // 'Silo Patch'
    public const long PondPlans        = 819650; // 'Pond Patch'
    public const long IncineratorPlans = 819651; // 'Incinerator Patch'

    // Plot upgrade unlocks (randomize_plot_upgrades) — PlotUpgradePurchaseItemModel asset
    // name in comment. WALLS/FEEDER enum values are shared between corral and coop, so the
    // asset name (not the LandPlot.Upgrade enum) is the gate key.
    public const long CorralUpgradeWalls          = 819652; // 'Walls Upgrade'
    public const long CorralUpgradeAirNet         = 819653; // 'AirNet Upgrade'
    public const long CorralUpgradeMusicBox       = 819654; // 'MusicBox Upgrade'
    public const long CorralUpgradePlortCollector = 819655; // 'PlortCollector Upgrade'
    public const long CorralUpgradeSolarShield    = 819656; // 'SolarShield Upgrade'
    public const long CorralUpgradeFeeder         = 819657; // 'Feeder Upgrade'
    public const long CoopUpgradeWalls            = 819658; // 'CoopWalls Upgrade'
    public const long CoopUpgradeFeeder           = 819659; // 'CoopFeeder Upgrade'
    public const long CoopUpgradeDeluxe           = 819660; // 'DeluxeCoop Upgrade'
    public const long GardenUpgradeSoil           = 819661; // 'Soil Upgrade'
    public const long GardenUpgradeSprinkler      = 819662; // 'Sprinkler Upgrade'
    public const long GardenUpgradeScareslime     = 819663; // 'Scareslime Upgrade'
    // Vitamizer sits in the COOP purchase category in-game (confirmed via category
    // diagnostics 2026-07-12), despite the initial garden guess.
    public const long CoopUpgradeVitamizer        = 819664; // 'Vitamizer Upgrade'
    public const long GardenUpgradeDeluxe         = 819665; // 'DeluxeGarden Upgrade'
    public const long SiloUpgradeStorage2         = 819666; // 'Storage2 Upgrade'
    public const long SiloUpgradeStorage3         = 819667; // 'Storage3 Upgrade'
    public const long SiloUpgradeStorage4         = 819668; // 'Storage4 Upgrade'
    public const long SiloUpgradeCapacity         = 819669; // 'Storage Capacity Upgrade'
    public const long PondUpgradePlortCollector   = 819670; // 'PlortCollectorPond Upgrade'
    public const long IncineratorUpgradeAshTrough      = 819671; // 'AshTrough Upgrade'
    public const long IncineratorUpgradePlortCollector = 819672; // 'PlortCollectorIncerator Upgrade' (game asset typo is real)

    // -------------------------------------------------------------------------
    // Item rows
    // -------------------------------------------------------------------------

    public static readonly IReadOnlyList<ItemInfo> All = new ItemInfo[]
    {
        // Region Access
        new(EmberValleyAccess,      "Ember Valley Access",      ItemType.RegionAccess),
        new(StarlightStrandAccess,  "Starlight Strand Access",  ItemType.RegionAccess),
        new(PowderfallBluffsAccess, "Powderfall Bluffs Access", ItemType.RegionAccess),

        // Special Access
        new(RadiantProjectorBlueprint, "Radiant Projector Blueprint", ItemType.Gadget),

        // Crafting Components — fabricator ingredients received from AP pool
        new(ArchiveKeyComponent,   "Archive Key Component",   ItemType.UpgradeComponent),
        new(SureshotModule,       "Sureshot Module",         ItemType.UpgradeComponent),
        new(TankLiner,             "Tank Liner",              ItemType.UpgradeComponent),
        new(HeartCell,             "Heart Cell",              ItemType.UpgradeComponent),
        new(PowerChip,             "Power Chip",              ItemType.UpgradeComponent),
        new(DashBootModule,        "Dash Boot Module",        ItemType.UpgradeComponent),
        new(JetpackDrive,          "Jetpack Drive",           ItemType.UpgradeComponent),
        new(StorageCell,           "Storage Cell",            ItemType.UpgradeComponent),
        new(ShadowSureshotModule, "Shadow Sureshot Module",  ItemType.UpgradeComponent),
        new(InjectorModule,        "Injector Module",         ItemType.UpgradeComponent),
        new(RegenModule,           "Regen Module",            ItemType.UpgradeComponent),
        new(VacTank,               "Vac Tank",                ItemType.UpgradeComponent),

        // Progressive Vacpack Upgrades
        new(ProgressiveHealthTank,     "Progressive Health Tank",     ItemType.Upgrade),
        new(ProgressiveEnergyTank,     "Progressive Energy Tank",     ItemType.Upgrade),
        new(ProgressiveExtraTank,      "Progressive Extra Tank",      ItemType.Upgrade),
        new(ProgressiveJetpack,        "Progressive Jetpack",         ItemType.Upgrade),
        new(ProgressiveWaterTank,      "Progressive Water Tank",      ItemType.Upgrade),
        new(ProgressiveDashBoots,      "Progressive Dash Boots",      ItemType.Upgrade),
        new(ProgressiveTankBooster,    "Progressive Tank Booster",    ItemType.Upgrade),
        new(ProgressivePowerInjector,  "Progressive Power Injector",  ItemType.Upgrade),
        new(ProgressiveRegenerator,    "Progressive Regenerator",     ItemType.Upgrade),
        new(ProgressiveGoldenSureshot, "Progressive Golden Sureshot", ItemType.Upgrade),
        new(ProgressiveShadowSureshot, "Progressive Shadow Sureshot", ItemType.Upgrade),
        new(ProgressiveTankGuard,      "Progressive Tank Guard",      ItemType.Upgrade),
        new(PulseWave,                 "Pulse Wave",                  ItemType.Upgrade),
        new(ResourceHarvester,         "Resource Harvester",          ItemType.Upgrade),
        new(DroneArchiveKey,           "Drone Archive Key",           ItemType.Upgrade),

        // Useful — Quantum Drone Station (blueprint+station on first copy, then modules;
        // module IdentType confirmed: ComponentAcqDrone, max 19 in game)
        new(QuantumDroneStation, "Quantum Drone Station", ItemType.Useful),

        // Gadgets — Zone Teleporters
        new(TeleporterEmberValley,      "Teleporter (Ember Valley)",      ItemType.Gadget),
        new(TeleporterStarlightStrand,  "Teleporter (Starlight Strand)",  ItemType.Gadget),
        new(TeleporterPowderfallBluffs, "Teleporter (Powderfall Bluffs)", ItemType.Gadget),
        new(TeleporterGreyLabyrinth,    "Teleporter (Grey Labyrinth)",    ItemType.Gadget),

        // Gadgets — Home Teleporters
        new(HomeTeleporterBlue,   "Home Teleporter Blue",   ItemType.Gadget),
        new(HomeTeleporterGreen,  "Home Teleporter Green",  ItemType.Gadget),
        new(HomeTeleporterRed,    "Home Teleporter Red",    ItemType.Gadget),
        new(HomeTeleporterYellow, "Home Teleporter Yellow", ItemType.Gadget),

        // Gadgets — Warp Depots
        new(WarpDepotGrey,   "Warp Depot (Grey/Ember Valley)", ItemType.Gadget),
        new(WarpDepotBerry,  "Warp Depot (Berry)",             ItemType.Gadget),
        new(WarpDepotViolet, "Warp Depot (Violet)",            ItemType.Gadget),
        new(WarpDepotSnowy,  "Warp Depot (Snowy)",             ItemType.Gadget),

        // Gadgets — Functional
        new(MarketLink,         "Market Link",          ItemType.Gadget),
        new(SuperHydroTurret,   "Super Hydro Turret",   ItemType.Gadget),
        new(PortableScareSlime, "Portable Scare Slime", ItemType.Gadget),
        new(GordoSnareAdvanced, "Gordo Snare Advanced", ItemType.Gadget),
        new(MedStation,         "Med Station",          ItemType.Gadget),
        new(DreamLanternT2,     "Dream Lantern T2",     ItemType.Gadget),

        // Gadgets — Movement / utility (always in the apworld pool)
        new(DashPad,            "Dash Pad",             ItemType.Gadget),
        new(SpringPad,          "Spring Pad",           ItemType.Gadget),
        new(PortableWaterTap,   "Portable Water Tap",   ItemType.Gadget),

        // Gadgets — Labyrinth goal (×3 in pool for prismacore/slimepedia goals; duplicate
        // receipts are skipped by GrantSingleGadget's blueprint-already-unlocked guard)
        new(DisruptionDetector, "Disruption Detector",  ItemType.Gadget),

        // Conservatory Expansions
        new(ExpansionGully,     "The Gully Access",     ItemType.ConservatoryExpansion),
        new(ExpansionTidepools, "The Tidepools Access", ItemType.ConservatoryExpansion),
        new(ExpansionArchway,   "The Archway Access",   ItemType.ConservatoryExpansion),
        new(ExpansionDen,       "The Den Access",       ItemType.ConservatoryExpansion),
        new(ExpansionDigsite,   "The Digsite Access",   ItemType.ConservatoryExpansion),

        // Filler — Newbucks
        new(Newbucks250,  "250 Newbucks",  ItemType.Filler),
        new(Newbucks500,  "500 Newbucks",  ItemType.Filler),
        new(Newbucks1000, "1000 Newbucks", ItemType.Filler),

        // Filler — Plort Caches
        new(CommonPlortCache,   "Common Plort Cache",   ItemType.Filler),
        new(UncommonPlortCache, "Uncommon Plort Cache", ItemType.Filler),
        new(RarePlortCache,     "Rare Plort Cache",     ItemType.Filler),

        // Filler — Craft Caches
        new(RainbowFieldsCraftCache,    "Rainbow Fields Craft Cache",    ItemType.Filler),
        new(EmberValleyCraftCache,      "Ember Valley Craft Cache",      ItemType.Filler),
        new(StarlightStrandCraftCache,  "Starlight Strand Craft Cache",  ItemType.Filler),
        new(PowderfallBluffsCraftCache, "Powderfall Bluffs Craft Cache", ItemType.Filler),
        new(GreyLabyrinthCraftCache,    "Grey Labyrinth Craft Cache",    ItemType.Filler),
        new(RareCraftCache,             "Rare Craft Cache",              ItemType.Filler),

        new(SlimeRing,     "Slime Ring",     ItemType.Filler),
        new(WeatherChange, "Weather Change", ItemType.Useful),

        // Market Recovery (only in pool when plort_market_mode != disabled)
        new(MarketRecovery20, "Market Recovery (20%)", ItemType.Useful),
        new(MarketRecovery10, "Market Recovery (10%)", ItemType.Useful),

        // Traps
        new(TrapTarrSpawn,  "Tarr Spawn Trap",  ItemType.Trap),
        new(TrapTeleport,   "Teleport Trap",     ItemType.Trap),
        new(TrapTarrRain,   "Tarr Rain Trap",    ItemType.Trap),
        new(TrapVacExpel,    "Vacpack Spew Trap", ItemType.Trap),
        new(TrapVacFill, "Vacpack Fill Trap", ItemType.Trap),

        // Ranch plot randomization — names must match items.py exactly
        new(RanchPlotConservatory, "Ranch Plot: Conservatory",  ItemType.RanchPlot),
        new(RanchPlotGully,        "Ranch Plot: The Gully",     ItemType.RanchPlot),
        new(RanchPlotTidepools,    "Ranch Plot: The Tidepools", ItemType.RanchPlot),
        new(RanchPlotArchway,      "Ranch Plot: The Archway",   ItemType.RanchPlot),
        new(RanchPlotDen,          "Ranch Plot: The Den",       ItemType.RanchPlot),
        new(RanchPlotDigsite,      "Ranch Plot: The Digsite",   ItemType.RanchPlot),

        new(CorralPlans,      "Corral Plans",      ItemType.RanchPlot),
        new(CoopPlans,        "Coop Plans",        ItemType.RanchPlot),
        new(GardenPlans,      "Garden Plans",      ItemType.RanchPlot),
        new(SiloPlans,        "Silo Plans",        ItemType.RanchPlot),
        new(PondPlans,        "Pond Plans",        ItemType.RanchPlot),
        new(IncineratorPlans, "Incinerator Plans", ItemType.RanchPlot),

        new(CorralUpgradeWalls,          "Corral Upgrade: Walls",           ItemType.RanchPlot),
        new(CorralUpgradeAirNet,         "Corral Upgrade: Air Net",         ItemType.RanchPlot),
        new(CorralUpgradeMusicBox,       "Corral Upgrade: Music Box",       ItemType.RanchPlot),
        new(CorralUpgradePlortCollector, "Corral Upgrade: Plort Collector", ItemType.RanchPlot),
        new(CorralUpgradeSolarShield,    "Corral Upgrade: Solar Shield",    ItemType.RanchPlot),
        new(CorralUpgradeFeeder,         "Corral Upgrade: Auto-Feeder",     ItemType.RanchPlot),
        new(CoopUpgradeWalls,            "Coop Upgrade: Walls",             ItemType.RanchPlot),
        new(CoopUpgradeFeeder,           "Coop Upgrade: Auto-Feeder",       ItemType.RanchPlot),
        new(CoopUpgradeDeluxe,           "Coop Upgrade: Deluxe Coop",       ItemType.RanchPlot),
        new(GardenUpgradeSoil,           "Garden Upgrade: Nutrient Soil",   ItemType.RanchPlot),
        new(GardenUpgradeSprinkler,      "Garden Upgrade: Sprinkler",       ItemType.RanchPlot),
        new(GardenUpgradeScareslime,     "Garden Upgrade: Scareslime",      ItemType.RanchPlot),
        new(CoopUpgradeVitamizer,        "Coop Upgrade: Vitamizer",         ItemType.RanchPlot),
        new(GardenUpgradeDeluxe,         "Garden Upgrade: Deluxe Garden",   ItemType.RanchPlot),
        new(SiloUpgradeStorage2,         "Silo Upgrade: Storage 2",         ItemType.RanchPlot),
        new(SiloUpgradeStorage3,         "Silo Upgrade: Storage 3",         ItemType.RanchPlot),
        new(SiloUpgradeStorage4,         "Silo Upgrade: Storage 4",         ItemType.RanchPlot),
        new(SiloUpgradeCapacity,         "Silo Upgrade: Storage Capacity",  ItemType.RanchPlot),
        new(PondUpgradePlortCollector,   "Pond Upgrade: Plort Collector",   ItemType.RanchPlot),
        new(IncineratorUpgradeAshTrough,      "Incinerator Upgrade: Ash Trough",      ItemType.RanchPlot),
        new(IncineratorUpgradePlortCollector, "Incinerator Upgrade: Plort Collector", ItemType.RanchPlot),
    };

    private static readonly Dictionary<long, ItemInfo> _byId = All.ToDictionary(i => i.Id);

    public static ItemInfo? Get(long id) => _byId.TryGetValue(id, out var info) ? info : null;

    // Maps expansion item ID → AccessDoor._id string.
    // Placeholder values must be replaced after running the in-game AccessDoor dump
    // (F9 → Dumps → "Dump Access Doors" while standing in the Conservatory).
    private static readonly Dictionary<long, string> _expansionDoorIds = new()
    {
        [ExpansionGully]     = "door1733849867", // zoneConservatory_Arboretum
        [ExpansionTidepools] = "door0129604684", // zoneConservatory_Pools
        [ExpansionArchway]   = "door0749608168", // zoneConservatory_Garden
        [ExpansionDen]       = "door0010140679", // zoneConservatory_Den
        [ExpansionDigsite]   = "door1356553442", // zoneConservatory_Digsite
    };

    /// <summary>
    /// Returns the <c>AccessDoor._id</c> string for a conservatory expansion item, or
    /// <c>null</c> if the item ID is not a conservatory expansion.
    /// </summary>
    public static string? GetExpansionDoorId(long itemId)
        => _expansionDoorIds.TryGetValue(itemId, out var id) ? id : null;
}
