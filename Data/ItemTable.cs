namespace SlimeRancher2AP.Data;

public enum ItemType { RegionAccess, Upgrade, Gadget, Filler, UpgradeComponent, Trap }

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

    // Traps: 819610–819614
    public const long TrapSlimeRing     = 819610; // spawns common slimes in a ring around the player
    public const long TrapTarrSpawn     = 819611; // spawns Tarr near the player
    public const long TrapTeleport      = 819612; // teleports player to a random accessible zone
    public const long TrapWeatherChange = 819613; // triggers random Heavy/SlimeRain weather
    public const long TrapTarrRain      = 819614; // triggers Slime Rain weather but overrides spawns to Tarr

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

        // Traps
        new(TrapSlimeRing,     "Slime Ring Trap",     ItemType.Trap),
        new(TrapTarrSpawn,     "Tarr Spawn Trap",     ItemType.Trap),
        new(TrapTeleport,      "Teleport Trap",       ItemType.Trap),
        new(TrapWeatherChange, "Weather Change Trap", ItemType.Trap),
        new(TrapTarrRain,      "Tarr Rain Trap",      ItemType.Trap),
    };

    private static readonly Dictionary<long, ItemInfo> _byId = All.ToDictionary(i => i.Id);

    public static ItemInfo? Get(long id) => _byId.TryGetValue(id, out var info) ? info : null;
}
