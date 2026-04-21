namespace SlimeRancher2AP.Data;

/// <summary>
/// Numeric location IDs. These MUST match the IDs defined in the companion Python apworld.
/// Base offset: 819000
///
/// AUTHORITATIVE RANGES (see APWORLD_DESIGN.md for full table):
///   819000â€“819199  Treasure Pods (all regions incl. Labyrinth) â€” 200 slots
///   819200â€“819249  Shadow Plort Doors (Grey Labyrinth)          â€”  50 slots
///   819250â€“819299  Gordo Slimes (wild/static)                   â€”  50 slots
///   819300â€“819349  Map Data Nodes                               â€”  50 slots
///   819350â€“819399  Slimepedia Entries                           â€”  50 slots
///   819400â€“819449  Fabricator â€” Vacpack Upgrade crafts          â€”  50 slots
///   819450â€“819479  Research Drones                              â€”  30 slots
///   819480â€“819494  Ghostly Drones                               â€”  15 slots
///   819495â€”819499  Reserved                                     â€”   5 slots
///   819630â€”819683  Slimepedia Resources                         â€”  54 slots
///   819700â€”819714  Conversation Key Gifts (functional items)    â€”  15 slots
///   819821–819842  Radiant Slimepedia Entries (22 entries, confirmed via DumpRadiantSlimes)
///   819843–819845  Region Gate Switches (3 entries, locations/bundled mode only)
///   819715â€”819762  Conversation Decoration Gifts                â€”  48 slots
///   819763â€”819816  Conversation Non-Gift (story/lore/deflect)   â€”  54 slots
///
/// Zone scene name â†’ in-game name mapping (confirmed via AP-Dump):
///   zoneConservatory(_*) â†’ Conservatory (home base, always accessible)
///   zoneFields(_Area*)   â†’ Rainbow Fields
///   zoneGorge(_Area*)    â†’ Ember Valley
///   zoneStrand(_Area*)   â†’ Starlight Strand
///   zoneBluffs(_Area*)   â†’ Powderfall Bluffs
///   zoneLabyrinth(_*)    â†’ Grey Labyrinth  (TBD â€” not yet dumped)
/// </summary>
public static class LocationConstants
{
    // =========================================================================
    // TREASURE PODS: 819000 â€“ 819199
    // posKey format: "sceneName_X_Y_Z" (world position rounded to nearest int).
    //
    // Per-region sub-ranges:
    //   819000â€“819019  Conservatory     ( 3 confirmed âœ…, 17 spare)
    //   819020â€“819037  Rainbow Fields   (18 confirmed âœ…,  0 spare)
    //   819038â€“819059  Ember Valley     (36 confirmed âœ…,  0 spare â€” tight fit)
    //   Wait, that doesn't work. Let me use:
    //
    //   819000â€“819019  Conservatory     ( 3 used of 20)
    //   819020â€“819037  Rainbow Fields   (18 used of 18 â†’ extend if needed)
    //   819038â€“819073  Ember Valley     (36 used of 36)
    //   819074â€“819106  Starlight Strand (33 used of 33)
    //   819107â€“819131  Powderfall Bluffs (25 used of 25)
    //   819132â€“819199  Grey Labyrinth   (TBD)
    // =========================================================================

    // Conservatory â€” 3 pods confirmed âœ…
    public const long TreasurePod_Conservatory_01  = 819000;  // Arboretum: Flag Meat
    public const long TreasurePod_Conservatory_02  = 819001;  // Den: Flag Veggie
    public const long TreasurePod_Conservatory_03  = 819002;  // Garden: Flag Fruit
    // 819003â€“819019 spare

    // Rainbow Fields (zoneFields) â€” 18 pods confirmed âœ…
    public const long TreasurePod_RainbowFields_01  = 819020;  // zoneFields: Tank Guard Component
    public const long TreasurePod_RainbowFields_02  = 819021;  // zoneFields: Coastal Rock
    public const long TreasurePod_RainbowFields_03  = 819022;  // zoneFields_Area1: Slimestage
    public const long TreasurePod_RainbowFields_04  = 819023;  // zoneFields_Area1: Large Pink Bonsai
    public const long TreasurePod_RainbowFields_05  = 819024;  // zoneFields_Area1: Heart Module
    public const long TreasurePod_RainbowFields_06  = 819025;  // zoneFields_Area2: Simplebench
    public const long TreasurePod_RainbowFields_07  = 819026;  // zoneFields_Area2: Pink Warp Depot
    public const long TreasurePod_RainbowFields_08  = 819027;  // zoneFields_Area2: Umbrella
    public const long TreasurePod_RainbowFields_09  = 819028;  // zoneFields_Area2: Strange Diamond A
    public const long TreasurePod_RainbowFields_10  = 819029;  // zoneFields_Area2: Strange Diamond B
    public const long TreasurePod_RainbowFields_11  = 819030;  // zoneFields_Area2: Small Boulder
    public const long TreasurePod_RainbowFields_12  = 819031;  // zoneFields_Area2: Hydroturret
    public const long TreasurePod_RainbowFields_13  = 819032;  // zoneFields_Area3: Overjoyed Statue
    public const long TreasurePod_RainbowFields_14  = 819033;  // zoneFields_Area3: Emerald Cypress
    public const long TreasurePod_RainbowFields_15  = 819034;  // zoneFields_Area3: Swing
    public const long TreasurePod_RainbowFields_16  = 819035;  // zoneFields_Area3: Power Core
    public const long TreasurePod_RainbowFields_17  = 819036;  // zoneFields_Area3: Emerald Cypress Cluster
    public const long TreasurePod_RainbowFields_18  = 819037;  // zoneFields_Area4: Boombox

    // Ember Valley (zoneGorge) â€” 36 pods confirmed âœ…
    public const long TreasurePod_EmberValley_01    = 819038;  // Area1: DashPad
    public const long TreasurePod_EmberValley_02    = 819039;  // Area1: Stony Egg Lamp Statue
    public const long TreasurePod_EmberValley_03    = 819040;  // Area1: Drones (cosmetic pod)
    public const long TreasurePod_EmberValley_04    = 819041;  // Area1: BasicTallLamp
    public const long TreasurePod_EmberValley_05    = 819042;  // Area1: AmberCypressCluster
    public const long TreasurePod_EmberValley_06    = 819043;  // Area1: TankBoosterComponent
    public const long TreasurePod_EmberValley_07    = 819044;  // Area2: PortableSlimeBaitMeat
    public const long TreasurePod_EmberValley_08    = 819045;  // Area2: Sureshot Component
    public const long TreasurePod_EmberValley_09    = 819046;  // Area2: TeacupLargo
    public const long TreasurePod_EmberValley_10    = 819047;  // Area2: ExtraTankComponent
    public const long TreasurePod_EmberValley_11    = 819048;  // Area2: Wheelbarrow
    public const long TreasurePod_EmberValley_12    = 819049;  // Area2: ItemDisplay
    public const long TreasurePod_EmberValley_13    = 819050;  // Area2: Gold Chicken Statue
    public const long TreasurePod_EmberValley_14    = 819051;  // Area3: PinballBumper
    public const long TreasurePod_EmberValley_15    = 819052;  // Area3: WarpDepotBlue
    public const long TreasurePod_EmberValley_16    = 819053;  // Area3: HappyStatue
    public const long TreasurePod_EmberValley_17    = 819054;  // Area3: WindChimes
    public const long TreasurePod_EmberValley_18    = 819055;  // Area3: PinkStripedLamp
    public const long TreasurePod_EmberValley_19    = 819056;  // Area3: CrystalGordo Science Resources
    public const long TreasurePod_EmberValley_20    = 819057;  // Area3: Gold Angler Slime Statue
    public const long TreasurePod_EmberValley_21    = 819058;  // Area3: JetpackComponent
    public const long TreasurePod_EmberValley_22    = 819059;  // Area3: TallAmberCypress
    public const long TreasurePod_EmberValley_23    = 819060;  // Area3: HeartModuleComponent
    public const long TreasurePod_EmberValley_24    = 819061;  // Area4: WarpDepotGrey
    public const long TreasurePod_EmberValley_25    = 819062;  // Area4: PortableSlimeBaitFruit
    public const long TreasurePod_EmberValley_26    = 819063;  // Area4: StrangeDiamond
    public const long TreasurePod_EmberValley_27    = 819064;  // Area4: TeleporterZoneGorge
    public const long TreasurePod_EmberValley_28    = 819065;  // Area4: Stalagmite
    public const long TreasurePod_EmberValley_29    = 819066;  // Area4: MagmaPool
    public const long TreasurePod_EmberValley_30    = 819067;  // Area4: BattyGordo Science Resources
    public const long TreasurePod_EmberValley_31    = 819068;  // Area4: PottedPlants
    public const long TreasurePod_EmberValley_32    = 819069;  // Area4: TallMagmaClump
    public const long TreasurePod_EmberValley_33    = 819070;  // Area4: MediumPalm
    public const long TreasurePod_EmberValley_34    = 819071;  // Area5: Accelerator
    public const long TreasurePod_EmberValley_35    = 819072;  // Area5: GoldenDervishStatue
    public const long TreasurePod_EmberValley_36    = 819073;  // Area5: Carousel

    // Starlight Strand (zoneStrand) â€” 33 pods confirmed âœ…
    public const long TreasurePod_StarlightStrand_01  = 819074;  // main: TankBoosterComponent
    public const long TreasurePod_StarlightStrand_02  = 819075;  // main: Coastal Rock Pillar
    public const long TreasurePod_StarlightStrand_03  = 819076;  // main: LanternBeach
    public const long TreasurePod_StarlightStrand_04  = 819077;  // main: Cheerful Statue
    public const long TreasurePod_StarlightStrand_05  = 819078;  // main: Violet Warp Depot
    public const long TreasurePod_StarlightStrand_06  = 819079;  // main: TrellisArch
    public const long TreasurePod_StarlightStrand_07  = 819080;  // Area1: Tall Violet Swirlshroom
    public const long TreasurePod_StarlightStrand_08  = 819081;  // Area1: Azure Mangrove
    public const long TreasurePod_StarlightStrand_09  = 819082;  // Area1: Gordo Snare Novice (cosmetic)
    public const long TreasurePod_StarlightStrand_10  = 819083;  // Area1: TeacupBase
    public const long TreasurePod_StarlightStrand_11  = 819084;  // Area1: Gold Cotton Statue
    public const long TreasurePod_StarlightStrand_12  = 819085;  // Area1: SwivelFan
    public const long TreasurePod_StarlightStrand_13  = 819086;  // Area1: PowerCoreComponent
    public const long TreasurePod_StarlightStrand_14  = 819087;  // Area2: Tall Pink Coral Columns
    public const long TreasurePod_StarlightStrand_15  = 819088;  // Area2: WideTrellis
    public const long TreasurePod_StarlightStrand_16  = 819089;  // Area2: Cave Pillar
    public const long TreasurePod_StarlightStrand_17  = 819090;  // Area2: Slime Bait Veggie
    public const long TreasurePod_StarlightStrand_18  = 819091;  // Area2: Root Tangle
    public const long TreasurePod_StarlightStrand_19  = 819092;  // Area2: TankGuardComponent
    public const long TreasurePod_StarlightStrand_20  = 819093;  // Area2: (unnamed)
    public const long TreasurePod_StarlightStrand_21  = 819094;  // Area2: DashBootComponent
    public const long TreasurePod_StarlightStrand_22  = 819095;  // Area3: Gold Flutter Statue
    public const long TreasurePod_StarlightStrand_23  = 819096;  // Area3: Science Resources
    public const long TreasurePod_StarlightStrand_24  = 819097;  // Area3: Mushroom Planter
    public const long TreasurePod_StarlightStrand_25  = 819098;  // Area3: TankGuardComponent
    public const long TreasurePod_StarlightStrand_26  = 819099;  // Area4: End Teleporter Strand
    public const long TreasurePod_StarlightStrand_27  = 819100;  // Area4: Strange Diamond
    public const long TreasurePod_StarlightStrand_28  = 819101;  // Area4: Springpad
    public const long TreasurePod_StarlightStrand_29  = 819102;  // Area4: Sureshot Component
    public const long TreasurePod_StarlightStrand_30  = 819103;  // Area4: Starbloom
    public const long TreasurePod_StarlightStrand_31  = 819104;  // Area5: (unnamed A)
    public const long TreasurePod_StarlightStrand_32  = 819105;  // Area5: (unnamed B)
    public const long TreasurePod_StarlightStrand_33  = 819106;  // Area5: (unnamed C)

    // Powderfall Bluffs (zoneBluffs) â€” 25 pods confirmed âœ…
    public const long TreasurePod_PowderfallBluffs_01  = 819107;  // Area1: GlacialCrystal
    public const long TreasurePod_PowderfallBluffs_02  = 819108;  // Area1: IceLamp
    public const long TreasurePod_PowderfallBluffs_03  = 819109;  // Area1: PowerCore
    public const long TreasurePod_PowderfallBluffs_04  = 819110;  // Area1: CrystalSpires
    public const long TreasurePod_PowderfallBluffs_05  = 819111;  // Area1: WarpDepotWhite
    public const long TreasurePod_PowderfallBluffs_06  = 819112;  // Area1: FrostedShell
    public const long TreasurePod_PowderfallBluffs_07  = 819113;  // Area1: IceCubed
    public const long TreasurePod_PowderfallBluffs_08  = 819114;  // Area1: ChillySlimeStack
    public const long TreasurePod_PowderfallBluffs_09  = 819115;  // Area2: SnowMachine
    public const long TreasurePod_PowderfallBluffs_10  = 819116;  // Area2: IceTreeo
    public const long TreasurePod_PowderfallBluffs_11  = 819117;  // Area2: MajesticSnowflake
    public const long TreasurePod_PowderfallBluffs_12  = 819118;  // Area2: Frozen Flame
    public const long TreasurePod_PowderfallBluffs_13  = 819119;  // Area2: SnowGlobe
    public const long TreasurePod_PowderfallBluffs_14  = 819120;  // Area2: HydroShower
    public const long TreasurePod_PowderfallBluffs_15  = 819121;  // Area2: Sun Sap
    public const long TreasurePod_PowderfallBluffs_16  = 819122;  // Area2: TankBooster
    public const long TreasurePod_PowderfallBluffs_17  = 819123;  // Area2: SnowZBench
    public const long TreasurePod_PowderfallBluffs_18  = 819124;  // Area2: SnowyBush
    public const long TreasurePod_PowderfallBluffs_19  = 819125;  // Area3: AuroraPine
    public const long TreasurePod_PowderfallBluffs_20  = 819126;  // Area3: Sureshot
    public const long TreasurePod_PowderfallBluffs_21  = 819127;  // Area3: Fireflower
    public const long TreasurePod_PowderfallBluffs_22  = 819128;  // Area3: TeleporterPowderfall
    public const long TreasurePod_PowderfallBluffs_23  = 819129;  // Area3: TeleporterWhite
    public const long TreasurePod_PowderfallBluffs_24  = 819130;  // Area3: Drones (cosmetic pod)
    public const long TreasurePod_PowderfallBluffs_25  = 819131;  // Area4: AuroraFlowers

    // Grey Labyrinth â€” TODO: run dump while in zone (Goals 3 and 4 only)
    // 819132â€“819199 reserved for Grey Labyrinth pods

    // =========================================================================
    // SHADOW PLORT DOORS: 819200 â€“ 819249  (Grey Labyrinth only)
    // Only active in the location pool for Goals 3 and 4 (Enter/Stabilize Prismacore).
    // posKeys must be confirmed by running dump in Grey Labyrinth.
    // =========================================================================
    // All 25 doors confirmed âœ…. All share gameObject.name='TriggerActivate' â€”
    // PlortDepositorPatch uses WorldUtils.PositionKey() for lookup.
    public const long ShadowPlortDoor_01 = 819200;  // zoneLabStrandEntrance_918_56_-1238
    public const long ShadowPlortDoor_02 = 819201;  // zoneLabStrandEntrance_1160_51_-1357
    public const long ShadowPlortDoor_03 = 819202;  // zoneLabStrandEntranceMain_B_1195_38_-1410
    public const long ShadowPlortDoor_04 = 819203;  // zoneLabStrandEntranceMain_B_1309_81_-1444
    public const long ShadowPlortDoor_05 = 819204;  // zoneLabValleyEntrance_1755_89_-1154
    public const long ShadowPlortDoor_06 = 819205;  // zoneLabValleyEntrance_1947_71_-1103
    public const long ShadowPlortDoor_07 = 819206;  // zoneLabValleyEntrance_1812_50_-1150
    public const long ShadowPlortDoor_08 = 819207;  // zoneLabValleyEntrance_B_1692_69_-953
    public const long ShadowPlortDoor_09 = 819208;  // zoneLabValleyEntrance_B_1829_71_-999
    public const long ShadowPlortDoor_10 = 819209;  // zoneLabyrinthHub_1386_61_-1123
    public const long ShadowPlortDoor_11 = 819210;  // zoneLabyrinthHub_B_1099_106_-1002
    public const long ShadowPlortDoor_12 = 819211;  // zoneLabyrinthHub_C_1571_122_-1095
    public const long ShadowPlortDoor_13 = 819212;  // zoneLabyrinthHub_C_1496_148_-924
    public const long ShadowPlortDoor_14 = 819213;  // zoneLabyrinthHub_C_1372_101_-1054
    public const long ShadowPlortDoor_15 = 819214;  // zoneLabyrinthDreamland_1141_156_-870
    public const long ShadowPlortDoor_16 = 819215;  // zoneLabyrinthDreamland_B_839_154_-1059
    public const long ShadowPlortDoor_17 = 819216;  // zoneLabyrinthDreamland_B_962_157_-951
    public const long ShadowPlortDoor_18 = 819217;  // zoneLabyrinthDreamland_B_763_187_-796
    public const long ShadowPlortDoor_19 = 819218;  // zoneLabyrinthDreamland_B_778_151_-987
    public const long ShadowPlortDoor_20 = 819219;  // zoneLabyrinthDreamland_C_719_203_-424
    public const long ShadowPlortDoor_21 = 819220;  // zoneLabyrinthTerrarium_FoyerGazebo_2115_171_-792
    public const long ShadowPlortDoor_22 = 819221;  // zoneLabyrinthTerrarium_FoyerGazebo_2113_151_-693
    public const long ShadowPlortDoor_23 = 819222;  // zoneLabyrinthTerrarium_JungleGlacier_1926_139_-993
    public const long ShadowPlortDoor_24 = 819223;  // zoneLabyrinthTerrarium_JungleGlacier_1972_160_-973
    public const long ShadowPlortDoor_25 = 819224;  // zoneLabyrinthTerrarium_JungleGlacier_2079_185_-975

    // =========================================================================
    // GORDO SLIMES: 819250 â€“ 819299  (wild/static only â€” no Gordo Snare gordos)
    // 16 total confirmed across all regions. GordoPatch filters scene='' gordos.
    //
    // Note: gordoAngler and gordoFlutter static world instances have Unity " (1)"
    // suffix (snare templates have the base name); see LocationTable.
    // =========================================================================
    public const long Gordo_Pink      = 819250;  // zoneFields_Area3
    // 819251 intentionally unused (was Gordo_Pink_02 â€” only one static Pink Gordo exists)
    public const long Gordo_Cotton    = 819252;  // zoneFields_Area1
    public const long Gordo_Phosphor  = 819253;  // zoneFields
    public const long Gordo_Rock      = 819254;  // zoneGorge_Area3     (was listed as Tabby)
    public const long Gordo_Tabby     = 819255;  // zoneGorge_Area3
    public const long Gordo_Crystal   = 819256;  // zoneGorge_Area3
    public const long Gordo_Batty     = 819257;  // zoneGorge_Area4
    public const long Gordo_Boom      = 819258;  // zoneGorge_Area4
    public const long Gordo_Hunter    = 819259;  // zoneStrand_Area1
    public const long Gordo_Honey     = 819260;  // zoneStrand_Area2
    public const long Gordo_Ringtail  = 819261;  // zoneStrand_Area3
    public const long Gordo_Angler    = 819262;  // zoneStrand_Area3  (GO name: "gordoAngler (1)")
    public const long Gordo_Flutter   = 819263;  // zoneStrand_Area4  (GO name: "gordoFlutter (1)")
    public const long Gordo_Saber     = 819264;  // zoneBluffs_Area2
    public const long Gordo_Sloomber  = 819265;  // zoneLabyrinthDreamland_C  (GO name: "SloomberGordo" â€” note caps/no prefix)
    public const long Gordo_Twin      = 819266;  // zoneLabyrinthHub_C
    public const long Gordo_Kinetic   = 819267;  // zoneLabyrinthTerrarium_FoyerGazebo  (not in original design â€” discovered via dump)

    // =========================================================================
    // MAP DATA NODES: 819300 â€“ 819349
    // posKeys confirmed for Ember Valley, Starlight Strand, Powderfall Bluffs.
    // Rainbow Fields posKeys pending (second dump didn't include that section).
    // =========================================================================
    public const long MapNode_RainbowFields_01     = 819300;  // zoneFields_Area1_188_4_216 âœ…
    public const long MapNode_RainbowFields_02     = 819301;  // zoneFields_Area3_103_16_290 âœ…
    public const long MapNode_EmberValley_01       = 819302;  // zoneGorge_Area1_-443_17_560
    public const long MapNode_EmberValley_02       = 819303;  // zoneGorge_Area3_-561_47_747
    public const long MapNode_EmberValley_03       = 819304;  // zoneGorge_Area4_-757_29_461
    public const long MapNode_StarlightStrand_01   = 819305;  // zoneStrand_-8_21_-17
    public const long MapNode_StarlightStrand_02   = 819306;  // zoneStrand_Area2_166_13_-284
    public const long MapNode_StarlightStrand_03   = 819307;  // zoneStrand_Area4_261_54_-655
    public const long MapNode_PowderfallBluffs_01  = 819308;  // zoneBluffs_Area2_-821_8_1709
    public const long MapNode_PowderfallBluffs_02  = 819309;  // zoneBluffs_Area3_-575_86_1579
    public const long MapNode_GreyLabyrinth_01     = 819310;  // zoneLabStrandEntranceMain_B_1275_64_-1426 âœ…
    public const long MapNode_GreyLabyrinth_02     = 819311;  // zoneLabValleyEntrance_1782_66_-1165 âœ…
    public const long MapNode_GreyLabyrinth_03     = 819312;  // zoneLabyrinthHub_1472_92_-1149 âœ…
    public const long MapNode_GreyLabyrinth_04     = 819313;  // zoneLabyrinthDreamland_B_771_151_-998 âœ…
    public const long MapNode_GreyLabyrinth_05     = 819314;  // zoneLabyrinthDreamland_C_926_164_-434 âœ…
    public const long MapNode_GreyLabyrinth_06     = 819315;  // zoneLabyrinthTerrarium_FoyerGazebo_2171_162_-772 âœ…
    public const long MapNode_GreyLabyrinth_07     = 819316;  // zoneLabyrinthTerrarium_JungleGlacier_2020_167_-969 âœ…

    // =========================================================================
    // SLIMEPEDIA ENTRIES: 819350 â€“ 819399
    // PediaEntry.name values confirmed via in-game DumpPedia() on 2026-04-10.
    // PediaCategory asset name: "Slimes" â€” 29 entries total, all included.
    // Largo, FeralSlime, and Gordo are included because PediaRuntimeCategory.AllUnlocked()
    // checks all 29 and they are required for the slimepedia goal.
    // =========================================================================
    // Ordering matches the apworld locations.py exactly (verified 2026-04-12).
    // Rainbow Fields slimes: 819350â€“819361, then EmberValley: 819362â€“819367,
    // StarlightStrand: 819368, PowderfallBluffs: 819369â€“819373,
    // GreyLabyrinth: 819374, misc: 819375â€“819378.
    public const long Slimepedia_Pink     = 819350;
    public const long Slimepedia_Cotton   = 819351;
    public const long Slimepedia_Tabby    = 819352;
    public const long Slimepedia_Phosphor = 819353;
    public const long Slimepedia_Boom     = 819354;
    public const long Slimepedia_Rock     = 819355;
    public const long Slimepedia_Honey    = 819356;
    public const long Slimepedia_Puddle   = 819357;
    public const long Slimepedia_Lucky    = 819358;
    public const long Slimepedia_Gold     = 819359;
    public const long Slimepedia_Dervish  = 819360;
    public const long Slimepedia_Tangle   = 819361;
    public const long Slimepedia_Crystal  = 819362;
    public const long Slimepedia_Hunter   = 819363;
    public const long Slimepedia_Fire     = 819364;
    public const long Slimepedia_Batty    = 819365;
    public const long Slimepedia_Angler   = 819366;
    public const long Slimepedia_Ringtail = 819367;
    public const long Slimepedia_Flutter  = 819368;
    public const long Slimepedia_Yolky    = 819369;
    public const long Slimepedia_Sloomber = 819370;
    public const long Slimepedia_Twin     = 819371;
    public const long Slimepedia_Hyper    = 819372;
    public const long Slimepedia_Saber    = 819373;
    public const long Slimepedia_Shadow   = 819374;
    public const long Slimepedia_Tarr       = 819375;
    public const long Slimepedia_Largo      = 819376;
    public const long Slimepedia_FeralSlime = 819377;
    public const long Slimepedia_Gordo      = 819378;

    // =========================================================================
    // FABRICATOR — VACPACK UPGRADE CRAFTS: 819400–819449
    // Order matches the apworld exactly (locations.py FABRICATOR_LOCATIONS list).
    // UpgradeDefinition.name values confirmed via DumpUpgradeComponents() in-game.
    // =========================================================================
    public const long Fabricator_ResourceHarvester  = 819400; // UpgradeDef: ResourceNodeHarvester (confirmed)
    public const long Fabricator_HealthCapacity_1   = 819401; // UpgradeDef: HealthCapacity (confirmed)
    public const long Fabricator_HealthCapacity_2   = 819402;
    public const long Fabricator_HealthCapacity_3   = 819403;
    public const long Fabricator_HealthCapacity_4   = 819404;
    public const long Fabricator_EnergyCapacity_1   = 819405; // UpgradeDef: EnergyCapacity (confirmed)
    public const long Fabricator_EnergyCapacity_2   = 819406;
    public const long Fabricator_EnergyCapacity_3   = 819407;
    public const long Fabricator_EnergyCapacity_4   = 819408;
    public const long Fabricator_EnergyCapacity_5   = 819409;
    public const long Fabricator_AmmoSlots_1        = 819410; // UpgradeDef: AmmoSlots (confirmed)
    public const long Fabricator_AmmoSlots_2        = 819411;
    public const long Fabricator_Jetpack_1          = 819412; // UpgradeDef: Jetpack (confirmed)
    public const long Fabricator_Jetpack_2          = 819413;
    public const long Fabricator_RunEfficiency_1    = 819414; // UpgradeDef: RunEfficiency (confirmed)
    public const long Fabricator_RunEfficiency_2    = 819415;
    public const long Fabricator_PulseWave          = 819416; // UpgradeDef: PulseWave (confirmed)
    public const long Fabricator_LiquidSlot         = 819417; // UpgradeDef: LiquidSlot (confirmed)
    public const long Fabricator_AmmoCapacity_1     = 819418; // UpgradeDef: AmmoCapacity (confirmed)
    public const long Fabricator_AmmoCapacity_2     = 819419;
    public const long Fabricator_AmmoCapacity_3     = 819420;
    public const long Fabricator_AmmoCapacity_4     = 819421;
    public const long Fabricator_AmmoCapacity_5     = 819422;
    public const long Fabricator_AmmoCapacity_6     = 819423;
    public const long Fabricator_AmmoCapacity_7     = 819424;
    public const long Fabricator_AmmoCapacity_8     = 819425;
    public const long Fabricator_TankGuard_1        = 819426; // UpgradeDef: TankGuard (confirmed)
    public const long Fabricator_TankGuard_2        = 819427;
    public const long Fabricator_TankGuard_3        = 819428;
    public const long Fabricator_GoldenSureshot_1   = 819429; // UpgradeDef: GoldenSureshot (confirmed)
    public const long Fabricator_GoldenSureshot_2   = 819430;
    public const long Fabricator_GoldenSureshot_3   = 819431;
    public const long Fabricator_ShadowSureshot     = 819432; // UpgradeDef: ShadowSureshot (confirmed)
    public const long Fabricator_ArchiveKey         = 819433; // UpgradeDef: ArchiveKey (confirmed)
    public const long Fabricator_EnergyDelay_1      = 819434; // UpgradeDef: EnergyDelay (confirmed)
    public const long Fabricator_EnergyDelay_2      = 819435;
    public const long Fabricator_EnergyRegen_1      = 819436; // UpgradeDef: EnergyRegen (confirmed)
    public const long Fabricator_EnergyRegen_2      = 819437;

    // =========================================================================
    // RESEARCH DRONES: 819450 â€“ 819479
    // Constant names match the ResearchDroneEntry asset name for clarity.
    // 23 confirmed across Conservatory/Fields/Gorge/Strand/Bluffs. Grey Lab pending.
    // =========================================================================
    public const long ResearchDrone_Gully                  = 819450;
    public const long ResearchDrone_Conservatory           = 819451;
    public const long ResearchDrone_TheDen                 = 819452;
    public const long ResearchDrone_Archway                = 819453;
    public const long ResearchDrone_Tidepools              = 819454;
    public const long ResearchDrone_FieldsExpanse          = 819455;
    public const long ResearchDrone_FieldsBluff            = 819456;
    public const long ResearchDrone_GorgeOverlook          = 819457;
    public const long ResearchDrone_GorgeRuinedOverlook    = 819458;
    public const long ResearchDrone_GorgeOceanPerch        = 819459;
    public const long ResearchDrone_GorgeCaveHub           = 819460;
    public const long ResearchDrone_GorgeLabyrinthGate     = 819461;
    public const long ResearchDrone_GorgeMagmaFieldsPerch  = 819462;
    public const long ResearchDrone_StrandField            = 819463;
    public const long ResearchDrone_StrandMushroomAlley    = 819464;
    public const long ResearchDrone_StrandWaterfall        = 819465;
    public const long ResearchDrone_StrandSplitTree        = 819466;
    public const long ResearchDrone_StrandSlopedCliff      = 819467;
    public const long ResearchDrone_StrandLabyrinthGate    = 819468;
    public const long ResearchDrone_BluffsAurora           = 819469;
    public const long ResearchDrone_BluffsIntro            = 819470;
    public const long ResearchDrone_BluffsSaber            = 819471;
    public const long ResearchDrone_BluffsFinal            = 819472;
    // =========================================================================
    // RESEARCH DRONE ARCHIVES: 819473 â€” 819495
    // 13 of the 23 drones have an archivedEntry (confirmed via Resources scan).
    // Conservatory (5) + Rainbow Fields (2) + Ember Valley (3) + Strand (3).
    // Repurposed from the “GL research drones” reserved block (no GL drones exist).
    // =========================================================================
    public const long ResearchDroneArchive_Gully                  = 819473;
    public const long ResearchDroneArchive_Conservatory           = 819474;
    public const long ResearchDroneArchive_TheDen                 = 819475;
    public const long ResearchDroneArchive_Archway                = 819476;
    public const long ResearchDroneArchive_Tidepools              = 819477;
    public const long ResearchDroneArchive_FieldsExpanse          = 819478;
    public const long ResearchDroneArchive_FieldsBluff            = 819479;
    // 819480–819489 = Ghostly Drones (do not use)
    public const long ResearchDroneArchive_GorgeOverlook          = 819490;
    public const long ResearchDroneArchive_GorgeRuinedOverlook    = 819491;
    public const long ResearchDroneArchive_GorgeLabyrinthGate     = 819492;
    public const long ResearchDroneArchive_StrandField            = 819493;
    public const long ResearchDroneArchive_StrandWaterfall        = 819494;
    public const long ResearchDroneArchive_StrandLabyrinthGate    = 819495;
    // 819496–819499 spare

    // =========================================================================
    // GHOSTLY DRONES: 819480 â€” 819494
    // posKey confirmed for Gorge/Strand/Bluffs. Conservatory + Fields pending
    // (second dump run didn't paste that section â€” rerun in those zones).
    // =========================================================================
    public const long GhostlyDrone_Conservatory_01    = 819480;  // zoneConservatory_560_42_466 âœ… (ComponentAcquisitionDroneConservatory)
    public const long GhostlyDrone_Conservatory_02    = 819481;  // zoneConservatory_589_27_27  âœ… (ComponentAcquisitionDroneConservatory3)
    public const long GhostlyDrone_Conservatory_03    = 819482;  // zoneConservatory_740_17_506 âœ… (ComponentAcquisitionDroneConservatory2)
    public const long GhostlyDrone_RainbowFields_01   = 819483;  // zoneFields_Area1_219_62_277 âœ… (ComponentAcquisitionDroneFields)
    public const long GhostlyDrone_EmberValley_01     = 819484;  // zoneGorge_Area3_-615_82_692 âœ…
    public const long GhostlyDrone_StarlightStrand_01 = 819485;  // zoneStrand_Area2_176_71_-395 âœ…
    public const long GhostlyDrone_PowderfallBluffs_01 = 819486; // zoneBluffs_Area4_-1050_71_1823 âœ…
    public const long GhostlyDrone_GreyLabyrinth_01   = 819487;  // zoneLabyrinthDreamland_1208_142_-834 âœ… (parent: Drone_Component)
    public const long GhostlyDrone_GreyLabyrinth_02   = 819488;  // zoneLabyrinthTerrarium_FoyerGazebo_1941_170_-792 âœ… (parent: Drone_Component)
    public const long GhostlyDrone_GreyLabyrinth_03   = 819489;  // zoneLabyrinthCorePath_1415_57_-1008 âœ… (parent: DroneComponent)
    // 819490â€“819494 spare

    // =========================================================================
    // CONVERSATION KEY GIFTS: 819700 â€” 819714
    // debug name â†’ location (ConversationCheckMode.KeyItems and above)
    // =========================================================================

    // Viktor â€” functional gadgets
    public const long Conv_Viktor_IntroCall            = 819700;  // ViktorIntroCall             â†’ TeleporterHomeYellow (first phone call)
    public const long Conv_Viktor_GadgetIntro          = 819714;  // ViktorGift_GadgetIntro      â†’ MedStation + SimpleTable + SimpleChair
    public const long Conv_Viktor_TeleporterPink        = 819701;  // ViktorGift1_TeleporterPink â†’ TeleporterPink
    public const long Conv_Viktor_TeleporterBlue        = 819702;  // ViktorGift2_TeleporterBlue â†’ TeleporterBlue
    public const long Conv_Viktor_TeleporterGrey        = 819703;  // ViktorGift3_TeleporterGrey â†’ TeleporterGrey
    public const long Conv_Viktor_TeleporterViolet      = 819704;  // ViktorGift4_TeleporterViolet â†’ TeleporterViolet
    public const long Conv_Viktor_TeleporterHomeBlue    = 819705;  // ViktorGift_TeleporterHome1Blue â†’ TeleporterHomeBlue
    public const long Conv_Viktor_TeleporterHomeRed     = 819706;  // ViktorGift_TeleporterHome2Red  â†’ TeleporterHomeRed
    public const long Conv_Viktor_TeleporterHomeGreen   = 819707;  // ViktorGift_TeleporterHome3Green â†’ TeleporterHomeGreen
    public const long Conv_Viktor_EnergyBeamNode        = 819708;  // ViktorStoryCipher4          â†’ EnergyBeamNode (Radiant Projector)

    // Thora â€” functional gadget
    public const long Conv_Thora_GordoSnareAdvanced     = 819709;  // ThoraGift2                  â†’ GordoSnareAdvanced

    // Ogden â€” functional gadgets
    public const long Conv_Ogden_SuperHydroTurret       = 819710;  // OgdenGift1                  â†’ SuperHydroTurret
    public const long Conv_Ogden_PortableScareSlime     = 819711;  // OgdenGift3                  â†’ PortableScareSlime

    // Mochi â€” functional items
    public const long Conv_Mochi_MarketLink             = 819712;  // MochiGift1                  â†’ MarketLink
    public const long Conv_Mochi_ArchiveKeyComponent    = 819713;  // MochiStoryDrones3            â†’ ArchiveKeyComponent

    // =========================================================================
    // CONVERSATION DECORATION GIFTS: 819715 â€” 819762
    // (ConversationCheckMode.AllGifts and above)
    // =========================================================================

    // BOb â€” decorative ranch items (rocks)
    public const long Conv_BOb_RainbowLumps             = 819715;  // BObGift2          â†’ RainbowLumps
    public const long Conv_BOb_ThinCavePillar           = 819716;  // BObGift3          â†’ ThinCavePillar
    public const long Conv_BOb_RockClump                = 819717;  // BObGift4          â†’ RockClump
    public const long Conv_BOb_ShortMagmaClump          = 819718;  // BObGift6          â†’ ShortMagmaClump
    public const long Conv_BOb_RockFragments            = 819719;  // BObGiftRelocated1 â†’ RockFragments
    public const long Conv_BOb_StalagmiteCluster        = 819720;  // BObGiftRelocated2 â†’ StalagmiteCluster
    public const long Conv_BOb_RockCluster              = 819721;  // BObGiftRelocated3 â†’ RockCluster
    public const long Conv_BOb_SharpBoulder             = 819722;  // BObGiftRelocated4 â†’ SharpBoulder

    // Mochi â€” decorative ranch items (plants/fungi)
    public const long Conv_Mochi_RootArches             = 819723;  // MochiGift3           â†’ RootArches
    public const long Conv_Mochi_VioletSwirlShroom      = 819724;  // MochiGift4           â†’ VioletSwirlShroom
    public const long Conv_Mochi_ShortPinkCoralColumns  = 819725;  // MochiGift6           â†’ ShortPinkCoralColumns
    public const long Conv_Mochi_AshBlooms              = 819726;  // MochiGift7           â†’ AshBlooms
    public const long Conv_Mochi_TallAshwood            = 819727;  // MochiGift8           â†’ TallAshwood
    public const long Conv_Mochi_ShortRedAshwood        = 819728;  // MochiGift9           â†’ ShortRedAshwood
    public const long Conv_Mochi_RoundedMagmaPool       = 819729;  // MochiGift10          â†’ RoundedMagmaPool
    public const long Conv_Mochi_PinkGlowShroom         = 819730;  // MochiGiftRelocated1  â†’ PinkGlowShroom
    public const long Conv_Mochi_GnarledAshwood         = 819731;  // MochiGiftRelocated2  â†’ GnarledAshwood
    public const long Conv_Mochi_AzureGlowShroom        = 819732;  // MochiGiftRelocated3  â†’ AzureGlowShroom
    public const long Conv_Mochi_MediumRedAshwood       = 819733;  // MochiGiftRelocated4  â†’ MediumRedAshwood

    // Ogden â€” decorative ranch items (plants/trees)
    public const long Conv_Ogden_TallEmeraldCypress     = 819734;  // OgdenGift4           â†’ TallEmeraldCypress
    public const long Conv_Ogden_EmeraldShrubs          = 819735;  // OgdenGift6           â†’ EmeraldShrubs
    public const long Conv_Ogden_AmberShrubs            = 819736;  // OgdenGift7           â†’ AmberShrubs
    public const long Conv_Ogden_OchrePoppies           = 819737;  // OgdenGift8           â†’ OchrePoppies
    public const long Conv_Ogden_EmeraldVineTrellis     = 819738;  // OgdenGift9           â†’ EmeraldVineTrellis
    public const long Conv_Ogden_PinkBonsai             = 819739;  // OgdenGift10          â†’ PinkBonsai
    public const long Conv_Ogden_AzureShrubs            = 819740;  // OgdenGiftRelocated1  â†’ AzureShrubs
    public const long Conv_Ogden_AmberCypress           = 819741;  // OgdenGiftRelocated3  â†’ AmberCypress
    public const long Conv_Ogden_MediumPinkCoralColumns = 819742;  // OgdenGiftRelocated4  â†’ MediumPinkCoralColumns

    // Thora â€” decorative ranch items (plants/grass)
    public const long Conv_Thora_PinkMangrove           = 819743;  // ThoraGift1           â†’ PinkMangrove
    public const long Conv_Thora_RainbowGrass           = 819744;  // ThoraGift3           â†’ RainbowGrass
    public const long Conv_Thora_PinkGrass              = 819745;  // ThoraGift4           â†’ PinkGrass
    public const long Conv_Thora_AmberGrass             = 819746;  // ThoraGift5           â†’ AmberGrass
    public const long Conv_Thora_ShortPalm              = 819747;  // ThoraGift6           â†’ ShortPalm
    public const long Conv_Thora_GreenGrass             = 819748;  // ThoraGift7           â†’ GreenGrass
    public const long Conv_Thora_RedGrass               = 819749;  // ThoraGift8           â†’ RedGrass
    public const long Conv_Thora_GoldpetalFlowers       = 819750;  // ThoraGiftRelocated1  â†’ GoldpetalFlowers
    public const long Conv_Thora_AzureGrass             = 819751;  // ThoraGiftRelocated2  â†’ AzureGrass
    public const long Conv_Thora_CinderSpikeBlossoms    = 819752;  // ThoraGiftRelocated3  â†’ CinderSpikeBlossoms
    public const long Conv_Thora_SunfireDaisies         = 819753;  // ThoraGiftRelocated4  â†’ SunfireDaisies

    // Viktor â€” decorative ranch items (decor/flags)
    public const long Conv_Viktor_PinwheelLarge         = 819754;  // ViktorGift_PinwheelLarge    â†’ PinwheelLarge
    public const long Conv_Viktor_PinwheelSmall         = 819755;  // ViktorGift_PinwheelSmall    â†’ PinwheelSmall
    public const long Conv_Viktor_Streamer              = 819756;  // ViktorGift_StreamerSimple   â†’ Streamer
    public const long Conv_Viktor_StreamerScarf         = 819757;  // ViktorGift_StreamerScarf    â†’ StreamerScarf
    public const long Conv_Viktor_SimpleFlag            = 819758;  // ViktorGift_FlagSimple       â†’ SimpleFlag
    public const long Conv_Viktor_WindSocks             = 819759;  // ViktorGift_Windsocks        â†’ WindSocks
    public const long Conv_Viktor_FlagAttention         = 819760;  // ViktorGift_FlagAttention    â†’ FlagAttention
    public const long Conv_Viktor_FlagCautious          = 819761;  // ViktorGift_FlagCautious     â†’ FlagCautious
    public const long Conv_Viktor_FlagCurious           = 819762;  // ViktorGift_FlagCurious      â†’ FlagCurious

    // =========================================================================
    // CONVERSATION NON-GIFT (story / lore / deflect): 819763 â€” 819816
    // (ConversationCheckMode.All only)
    // =========================================================================

    // Viktor â€” story + deflect
    public const long Conv_Viktor_StoryCipher1          = 819763;  // ViktorStoryCipher1
    public const long Conv_Viktor_StoryCipher2          = 819764;  // ViktorStoryCipher2
    public const long Conv_Viktor_StoryCipher3          = 819765;  // ViktorStoryCipher3
    public const long Conv_Viktor_Deflect1              = 819766;  // ViktorDeflect1
    public const long Conv_Viktor_Deflect2              = 819767;  // ViktorDeflect2
    public const long Conv_Viktor_Deflect3              = 819768;  // ViktorDeflect3

    // Mochi â€” story + deflect
    public const long Conv_Mochi_StoryDrones1           = 819769;  // MochiStoryDrones1
    public const long Conv_Mochi_StoryDrones2           = 819770;  // MochiStoryDrones2
    public const long Conv_Mochi_Deflect1               = 819771;  // MochiDeflect1
    public const long Conv_Mochi_Deflect2               = 819772;  // MochiDeflect2
    public const long Conv_Mochi_Deflect3               = 819773;  // MochiDeflect3

    // Ogden â€” story + deflect
    public const long Conv_Ogden_StoryRainbow1          = 819774;  // OgdenStoryRainbow1
    public const long Conv_Ogden_StoryRainbow2          = 819775;  // OgdenStoryRainbow2
    public const long Conv_Ogden_StoryRainbow3          = 819776;  // OgdenStoryRainbow3
    public const long Conv_Ogden_Deflect1               = 819777;  // OgdenDeflect1
    public const long Conv_Ogden_Deflect2               = 819778;  // OgdenDeflect2
    public const long Conv_Ogden_Deflect3               = 819779;  // OgdenDeflect3

    // Thora â€” story + deflect
    public const long Conv_Thora_StoryBigNews1          = 819780;  // ThoraStoryBigNews1
    public const long Conv_Thora_StoryBigNews2          = 819781;  // ThoraStoryBigNews2
    public const long Conv_Thora_StoryBigNews3          = 819782;  // ThoraStoryBigNews3
    public const long Conv_Thora_Deflect1               = 819783;  // ThoraDeflect1
    public const long Conv_Thora_Deflect2               = 819784;  // ThoraDeflect2
    public const long Conv_Thora_Deflect3               = 819785;  // ThoraDeflect3

    // BOb â€” misc non-gift
    public const long Conv_BOb_Yolky1                   = 819786;  // BObYolky1

    // Gigi â€” all non-gift conversations
    public const long Conv_Gigi_Intro                   = 819787;  // GigiIntro
    public const long Conv_Gigi_IntroAlt                = 819788;  // GigiIntroAlt
    public const long Conv_Gigi_Hub                     = 819789;  // GigiHub
    public const long Conv_Gigi_ShadowPlort             = 819790;  // GigiShadowPlort
    public const long Conv_Gigi_Architecture            = 819791;  // GigiArchitecture
    public const long Conv_Gigi_Detector                = 819792;  // GigiDetector
    public const long Conv_Gigi_DetectorAlt             = 819793;  // GigiDetectorAlt
    public const long Conv_Gigi_Secret                  = 819794;  // GigiSecret
    public const long Conv_Gigi_DreamWorld              = 819795;  // GigiDreamWorld
    public const long Conv_Gigi_DreamGold               = 819796;  // GigiDreamGold
    public const long Conv_Gigi_EndTemp                 = 819797;  // GigiEndTemp
    public const long Conv_Gigi_BadConnection           = 819798;  // GigiBadConnection
    public const long Conv_Gigi_EnteringTerrarium       = 819799;  // GigiEnteringTerrarium
    public const long Conv_Gigi_MiniatureMuseum         = 819800;  // GigiMiniatureMuseum
    public const long Conv_Gigi_OutsideGoldRoom         = 819801;  // GigiOutsideGoldRoom
    public const long Conv_Gigi_AlcoveWithTree          = 819802;  // GigiAlcoveWithTree
    public const long Conv_Gigi_Core_Intro              = 819803;  // GigiCore_Intro
    public const long Conv_Gigi_RewardsKiosk            = 819804;  // GigiRewardsKiosk
    public const long Conv_Gigi_RewardsKiosk_PlortReminder     = 819805;  // GigiRewardsKiosk_PlortReminder
    public const long Conv_Gigi_RewardsKiosk_HarmonizerReminder = 819806; // GigiRewardsKiosk_HarmonizerReminder
    public const long Conv_Gigi_RewardsKiosk_PostGameDialog    = 819807;  // GigiRewardsKiosk_PostGameDialog
    public const long Conv_Gigi_RewardsKiosk_BossFightPrompt   = 819808;  // GigiRewardsKiosk_BossFightPrompt
    public const long Conv_Gigi_Core_HarmonizerQuest    = 819809;  // GigiCore_HarmonizerQuest
    public const long Conv_Gigi_Core_HydroShower        = 819810;  // GigiCore_HydroShower
    public const long Conv_Gigi_Core_NeedsWaterTank     = 819811;  // GigiCore_NeedsWaterTank
    public const long Conv_Gigi_Core_StartFight         = 819812;  // GigiCore_StartFight
    public const long Conv_Gigi_Core_StartFightAlt      = 819813;  // GigiCore_StartFightAlt
    public const long Conv_Gigi_Core_RetryFight         = 819814;  // GigiCore_RetryFight
    public const long Conv_Gigi_Core_FightComplete      = 819815;  // GigiCore_FightComplete
    public const long Conv_Gigi_Core_PostGame           = 819816;  // GigiCore_PostGame

    // NPC Intro Calls — missing from original key-gift range; added at 819817+
    // These are the first call from each NPC; in-game gift is replaced by AP check.
    public const long Conv_Thora_IntroCall              = 819817;  // ThoraIntroCall
    public const long Conv_Ogden_IntroCall              = 819818;  // OgdenIntroCall
    public const long Conv_Mochi_IntroCall              = 819819;  // MochiIntroCall  (gives RefineryLink in vanilla)
    public const long Conv_BOb_IntroCall                = 819820;  // BObGift1Intro   → RainbowMound blueprint (standalone FixedConversation, requires EV+Strand+ThoraIntroCall)

    // =========================================================================
    // SLIMEPEDIA RESOURCES ENTRIES: 819630 – 819683  (54 entries)
    // EntryName = PediaEntry.name (confirmed via AP-Dump DumpPedia, 2026-04-14).
    // Zones reflect where each resource is first encountered:
    //   819630–819650  Rainbow Fields  (21 entries)
    //   819651–819660  Ember Valley    (10 entries)
    //   819661–819672  Starlight Strand (12 entries)
    //   819673–819680  Powderfall Bluffs (8 entries)
    //   819681–819683  Grey Labyrinth   (3 entries)
    // =========================================================================

    // Rainbow Fields (21)
    public const long SlimepediaRes_CarrotVeggie    = 819630;
    public const long SlimepediaRes_LettuceVeggie   = 819631;
    public const long SlimepediaRes_BeetVeggie      = 819632;
    public const long SlimepediaRes_OnionVeggie     = 819633;
    public const long SlimepediaRes_PogoFruit       = 819634;
    public const long SlimepediaRes_MangoFruit      = 819635;
    public const long SlimepediaRes_Hen             = 819636;
    public const long SlimepediaRes_Chick           = 819637;
    public const long SlimepediaRes_Rooster         = 819638;
    public const long SlimepediaRes_PaintedHen      = 819639;
    public const long SlimepediaRes_PaintedChick    = 819640;
    public const long SlimepediaRes_CandiedHen      = 819641;
    public const long SlimepediaRes_CandedChick     = 819642;
    public const long SlimepediaRes_ElderHen        = 819643;
    public const long SlimepediaRes_ElderRooster    = 819644;
    public const long SlimepediaRes_WildHoneyCraft  = 819645;
    public const long SlimepediaRes_BuzzWaxCraft    = 819646;
    public const long SlimepediaRes_JellystoneCraft = 819647;
    public const long SlimepediaRes_SlimeFossilCraft = 819648;
    public const long SlimepediaRes_RoyalJelly      = 819649;
    public const long SlimepediaRes_Water           = 819650;

    // Ember Valley (10)
    public const long SlimepediaRes_TaterVeggie         = 819651;
    public const long SlimepediaRes_CuberryFruit        = 819652;
    public const long SlimepediaRes_PomegraniteFruit    = 819653;
    public const long SlimepediaRes_StonyHen            = 819654;
    public const long SlimepediaRes_StonyChick          = 819655;
    public const long SlimepediaRes_PrimordyOilCraft    = 819656;
    public const long SlimepediaRes_LavaDustCraft       = 819657;
    public const long SlimepediaRes_MagmaCombCraft      = 819658;
    public const long SlimepediaRes_RadiantOreCraft     = 819659;
    public const long SlimepediaRes_SunSapCraft         = 819660;

    // Starlight Strand (12)
    public const long SlimepediaRes_PearFruit           = 819661;
    public const long SlimepediaRes_SeaHen              = 819662;
    public const long SlimepediaRes_SeaChick            = 819663;
    public const long SlimepediaRes_BriarHen            = 819664;
    public const long SlimepediaRes_BriarChick          = 819665;
    public const long SlimepediaRes_MoondewNectar       = 819666;
    public const long SlimepediaRes_DeepBrineCraft      = 819667;
    public const long SlimepediaRes_AquaGlassCraft      = 819668;
    public const long SlimepediaRes_DreamBubbleCraft    = 819669;
    public const long SlimepediaRes_TinPetalCraft       = 819670;
    public const long SlimepediaRes_SilkySandCraft      = 819671;
    public const long SlimepediaRes_StrangeDiamondCraft = 819672;

    // Powderfall Bluffs (8)
    public const long SlimepediaRes_PolaricherryFruit   = 819673;
    public const long SlimepediaRes_ThunderHen          = 819674;
    public const long SlimepediaRes_ThunderChick        = 819675;
    public const long SlimepediaRes_PerfectSnowflakeCraft = 819676;
    public const long SlimepediaRes_StormGlassCraft     = 819677;
    public const long SlimepediaRes_LightningMoteCraft  = 819678;
    public const long SlimepediaRes_DriftCrystalCraft   = 819679;
    public const long SlimepediaRes_Snowball            = 819680;

    // Grey Labyrinth (3)
    public const long SlimepediaRes_BlackIndigoniumCraft = 819681;
    public const long SlimepediaRes_PrismaPlorts         = 819682;
    public const long SlimepediaRes_PrismaResources      = 819683;

    // =========================================================================
    // RADIANT SLIMEPEDIA ENTRIES: 819821 – 819842  (22 entries)
    // EntryName = RadiantSlimePediaEntry.name (Unity asset name, stable).
    // Confirmed via DumpRadiantSlimes() in-game; alphabetical order.
    // =========================================================================
    public const long SlimepediaRadiant_Angler   = 819821;
    public const long SlimepediaRadiant_Batty    = 819822;
    public const long SlimepediaRadiant_Boom     = 819823;
    public const long SlimepediaRadiant_Cotton   = 819824;
    public const long SlimepediaRadiant_Crystal  = 819825;
    public const long SlimepediaRadiant_Dervish  = 819826;
    public const long SlimepediaRadiant_Fire     = 819827;
    public const long SlimepediaRadiant_Flutter  = 819828;
    public const long SlimepediaRadiant_Honey    = 819829;
    public const long SlimepediaRadiant_Hunter   = 819830;
    public const long SlimepediaRadiant_Hyper    = 819831;
    public const long SlimepediaRadiant_Phosphor = 819832;
    public const long SlimepediaRadiant_Pink     = 819833;
    public const long SlimepediaRadiant_Puddle   = 819834;
    public const long SlimepediaRadiant_Ringtail = 819835;
    public const long SlimepediaRadiant_Rock     = 819836;
    public const long SlimepediaRadiant_Saber    = 819837;
    public const long SlimepediaRadiant_Sloomber = 819838;
    public const long SlimepediaRadiant_Tabby    = 819839;
    public const long SlimepediaRadiant_Tangle   = 819840;
    public const long SlimepediaRadiant_Twin     = 819841;
    public const long SlimepediaRadiant_Yolky    = 819842;

    // =========================================================================
    // REGION GATE SWITCHES: 819843 – 819845  (3 entries)
    // Only active when region_access_mode = "locations" or "bundled".
    // Check is sent when the player activates a gate switch that is still blocked.
    // Switch name → ID mapping mirrors RegionTable.SwitchToLocationId.
    // =========================================================================
    public const long RegionGate_EmberValley      = 819843;
    public const long RegionGate_StarlightStrand  = 819844;
    public const long RegionGate_PowderfallBluffs = 819845;
    // 819846 spare

    // =========================================================================
    // OTHER PUZZLE DOORS: 819847 – 819895  (49 slots reserved)
    // PuzzleSlotLockable doors (lockTag='plort_door') other than:
    //   • Grey Labyrinth shadow plort doors (819200–819249)
    //   • PB region gate (819845)
    // All 28 doors confirmed via AP-Dump across all zones (2026-04-21).
    // Optional locations enabled by the "randomize_puzzle_doors" apworld option.
    //
    // Lookup key: posKey "sceneName_X_Y_Z" (same as TreasurePod).
    // =========================================================================

    // Ember Valley (4 doors — excluding PB gate at zoneGorge_Area3_-645_34_681)
    public const long PlortDoor_EmberValley_1 = 819847; // zoneGorge_Area1_-193_-1_471
    public const long PlortDoor_EmberValley_2 = 819848; // zoneGorge_Area2_-332_10_285
    public const long PlortDoor_EmberValley_3 = 819849; // zoneGorge_Area3_-353_6_625
    public const long PlortDoor_EmberValley_4 = 819850; // zoneGorge_Area4_-892_12_508

    // Rainbow Fields (1 door)
    public const long PlortDoor_RainbowFields_1 = 819851; // zoneFields_338_-2_348

    // Starlight Strand (12 doors)
    public const long PlortDoor_StarlightStrand_1  = 819852; // zoneStrand_149_1_-257
    public const long PlortDoor_StarlightStrand_2  = 819853; // zoneStrand_101_1_-232
    public const long PlortDoor_StarlightStrand_3  = 819854; // zoneStrand_-5_13_-100
    public const long PlortDoor_StarlightStrand_4  = 819855; // zoneStrand_Area1_59_14_-423
    public const long PlortDoor_StarlightStrand_5  = 819856; // zoneStrand_Area1_53_2_-487
    public const long PlortDoor_StarlightStrand_6  = 819857; // zoneStrand_Area1_49_-3_-534
    public const long PlortDoor_StarlightStrand_7  = 819858; // zoneStrand_Area2_257_10_-308
    public const long PlortDoor_StarlightStrand_8  = 819859; // zoneStrand_Area2_162_16_-360
    public const long PlortDoor_StarlightStrand_9  = 819860; // zoneStrand_Area2_198_6_-207
    public const long PlortDoor_StarlightStrand_10 = 819861; // zoneStrand_Area2_308_16_-278
    public const long PlortDoor_StarlightStrand_11 = 819862; // zoneStrand_Area2_251_2_-232
    public const long PlortDoor_StarlightStrand_12 = 819863; // zoneStrand_Area4_116_60_-651

    // Grey Labyrinth (11 plort_door entries — excludes shadow_plort_collector/door)
    public const long PlortDoor_GreyLabyrinth_1  = 819864; // zoneLabStrandEntrance_1159_53_-1404
    public const long PlortDoor_GreyLabyrinth_2  = 819865; // zoneLabStrandEntranceMain_B_1222_29_-1307
    public const long PlortDoor_GreyLabyrinth_3  = 819866; // zoneLabStrandEntranceMain_B_1266_50_-1435
    public const long PlortDoor_GreyLabyrinth_4  = 819867; // zoneLabValleyEntrance_1786_66_-1155
    public const long PlortDoor_GreyLabyrinth_5  = 819868; // zoneLabValleyEntrance_1811_66_-1155
    public const long PlortDoor_GreyLabyrinth_6  = 819869; // zoneLabValleyEntrance_1767_66_-1155
    public const long PlortDoor_GreyLabyrinth_7  = 819870; // zoneLabValleyEntrance_1919_63_-1112
    public const long PlortDoor_GreyLabyrinth_8  = 819871; // zoneLabValleyEntrance_B_1833_49_-1094
    public const long PlortDoor_GreyLabyrinth_9  = 819872; // zoneLabyrinthHub_C_1469_88_-1057
    public const long PlortDoor_GreyLabyrinth_10 = 819873; // zoneLabyrinthTerrarium_JungleGlacier_1951_140_-854
    public const long PlortDoor_GreyLabyrinth_11 = 819874; // zoneLabyrinthTerrarium_JungleGlacier_2145_155_-856
    // 819875–819895 spare
}
