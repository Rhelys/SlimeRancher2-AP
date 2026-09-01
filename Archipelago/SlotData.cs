using SlimeRancher2AP.Data;

namespace SlimeRancher2AP.Archipelago;

/// <summary>
/// Strongly-typed representation of the slot data returned by the Archipelago server on login.
/// Keys must match those defined in the companion Python apworld.
/// </summary>
public class SlotData
{
    public bool   DeathLink          { get; init; }

    /// <summary>
    /// Percentage of filler slots replaced with traps (0 = no traps, 100 = all filler is traps).
    /// Slot data key: <c>"trap_percentage"</c>.
    /// </summary>
    public int    TrapPercentage     { get; init; } = 0;
    public string Goal               { get; init; } = "labyrinth_open";

    /// <summary>
    /// Lifetime newbucks earned threshold for the "newbucks" goal.
    /// Read from slot data key "newbucks_goal_amount"; default 1,000,000.
    /// </summary>
    public long   NewbucksGoalAmount { get; init; } = 1_000_000;

    /// <summary>
    /// Number of plorts of EACH in-scope type that must be sold for the "plort_seller"
    /// goal (per-seed value rolled by the apworld between the min/max options).
    /// Read from slot data key "plort_goal_amount"; default 25.
    /// </summary>
    public int    PlortGoalAmount { get; init; } = 25;

    public bool   RandomizeGordos        { get; init; } = true;
    public bool   RandomizePods          { get; init; } = true;
    public bool   RandomizeMapNodes      { get; init; } = true;
    /// <summary>
    /// Informational only — no mod behaviour depends on this.
    /// </summary>
    /// <remarks>
    /// Fabricator crafts are AP location checks in both modes. The option decides what the
    /// apworld puts in them: a shuffled multiworld item when true, or the upgrade that craft
    /// grants in the vanilla game, locked in place, when false. Either way the mod sends the
    /// check and applies whatever item comes back, so nothing here should gate on it.
    /// Retained for diagnostics. Slot data key: <c>"randomize_fabricator"</c>.
    /// </remarks>
    public bool   RandomizeFabricator    { get; init; } = true;
    public bool   RandomizeSlimepedia          { get; init; } = false;
    public bool   RandomizeSlimepediaResources { get; init; } = false;

    /// <summary>
    /// When true, catching a new radiant slime type for the first time unlocks its
    /// Slimepedia entry as an Archipelago location check.
    /// Slot data key: <c>"randomize_slimepedia_radiant"</c>.
    /// </summary>
    public bool   RandomizeSlimepediaRadiant   { get; init; } = false;

    /// <summary>
    /// Multiplier applied to radiant slime spawn frequency. 1 = normal (default).
    /// Higher values increase spawn frequency: 2 = twice as common, 5 = five times as common.
    /// Implemented by dividing all shuffle-bag sizes by this value in
    /// <c>RadiantSlimeSpawnRatePatch</c> on scene init.
    /// Slot data key: <c>"radiant_spawn_rate_multiplier"</c>. Range: 1–10.
    /// </summary>
    /// <summary>
    /// When true, every slime encounter is forced to be a radiant spawn — the shuffle-bag
    /// algorithm is bypassed entirely. <see cref="RadiantSpawnRateMultiplier"/> has no
    /// additional effect when this is enabled.
    /// Slot data key: <c>"all_radiant_slimes"</c>.
    /// </summary>
    public bool   AllRadiantSlimes             { get; init; } = false;
    public int    RadiantSpawnRateMultiplier   { get; init; } = 1;

    /// <summary>
    /// Multiplier applied to Gold and Lucky slime spawn weight.
    /// 1 = vanilla weights (default). Higher values make Gold/Lucky slimes proportionally
    /// more likely to spawn from any spawner that includes them.
    /// Implemented by scaling <c>SlimeSet.Member.Weight</c> for Gold/Lucky members in
    /// <c>GoldLuckySpawnRatePatch</c> on scene init.
    /// Slot data key: <c>"gold_lucky_spawn_rate_multiplier"</c>. Range: 1–50.
    /// </summary>
    public int    GoldLuckySpawnRateMultiplier { get; init; } = 1;

    public bool   RandomizeResearchDrones  { get; init; } = false;
    public bool   RandomizeGhostlyDrones   { get; init; } = false;

    /// <summary>
    /// Controls whether the 28 plort doors become Archipelago location checks.
    /// <c>"vanilla"</c> — disabled; <c>"locations"</c> — filling a door sends a check.
    /// Slot data key: <c>"randomize_plort_doors"</c>.
    /// </summary>
    public string RandomizePlortDoors      { get; init; } = "vanilla";

    /// <summary>
    /// Grey Labyrinth shadow plort doors (25). <c>"disabled"</c> — not checks;
    /// <c>"locations"</c> — opening a door sends a check. Only meaningful when the Grey
    /// Labyrinth is in scope (every goal except <c>labyrinth_open</c>).
    /// </summary>
    /// <remarks>
    /// The vanilla reward is suppressed in BOTH modes — several of these doors are dispensers
    /// that hand out a blueprint / upgrade component / spawned items, which would conflict
    /// with the same items being randomized into the pool. This flag gates only the check.
    /// Slot data key: <c>"randomize_shadow_doors"</c>.
    /// </remarks>
    public string RandomizeShadowDoors     { get; init; } = "disabled";

    /// <summary>
    /// When true, the first time each of the 25 sellable plort types is sold at the Plort Market
    /// becomes an Archipelago location check (25 checks total).
    /// Slot data key: <c>"randomize_plort_market"</c>.
    /// </summary>
    public bool   RandomizePlortMarket     { get; init; } = false;

    /// <summary>
    /// When true, a per-seed random subset of Polestar Provisions shop items are Archipelago
    /// location checks (the apworld picks the subset; the mod activates only the locations
    /// that exist in the seed). First purchase sends the check with the vanilla blueprint
    /// grant suppressed; the slot then reads as sold out.
    /// Slot data key: <c>"randomize_shop"</c>.
    /// </summary>
    public bool   RandomizeShop            { get; init; } = false;

    /// <summary>
    /// When true, post-game Sanctuary content is part of the seed — currently the
    /// "Slimepedia: Sprinkles" location, which also becomes required by the slimepedia
    /// goal. When false the Sprinkles entry is neither a check nor a goal requirement.
    /// Slot data key: <c>"randomize_sanctuary"</c>.
    /// </summary>
    public bool   RandomizeSanctuary       { get; init; } = false;

    /// <summary>
    /// Ranch plot randomization — three independent tiers (see <c>RanchPlotHandler</c>).
    /// When <c>RandomizePlots</c> is true, an empty plot can only be built on while the
    /// number of built plots in its area is below the received "Ranch Plot" item count for
    /// that area (StartingPlots Conservatory copies arrive as precollected items, so the
    /// mod just counts received items). Buildings and upgrades gate their purchase-menu
    /// entries until their item is received.
    /// Slot data keys: <c>"randomize_plots"</c>, <c>"starting_plots"</c>,
    /// <c>"randomize_plot_buildings"</c>, <c>"randomize_plot_upgrades"</c>.
    /// </summary>
    public bool   RandomizePlots           { get; init; } = false;
    public int    StartingPlots            { get; init; } = 2;
    public bool   RandomizePlotBuildings   { get; init; } = false;
    public bool   RandomizePlotUpgrades    { get; init; } = false;

    /// <summary>
    /// Controls how region gate switches and zone teleporters are handled.
    /// <list type="bullet">
    ///   <item><term>"vanilla"</term><description>
    ///     Region gates work as in the base game — activating a switch opens the zone immediately.
    ///     No region access items are in the AP pool. RegionGatePatch does not block gates.
    ///     The zone teleporter is granted automatically when the gate opens in-world (default).
    ///   </description></item>
    ///   <item><term>"locations"</term><description>
    ///     Gate switches become blocked location checks. Region gates will not open until the
    ///     matching Region Access item is received. TeleporterZone is not auto-granted.
    ///   </description></item>
    ///   <item><term>"bundled"</term><description>
    ///     Same as locations, but also grants the matching zone teleporter blueprint automatically
    ///     when the Region Access item is received.
    ///   </description></item>
    /// </list>
    /// Slot data key: <c>"region_access_mode"</c>.
    /// </summary>
    public string RegionAccessMode { get; init; } = "vanilla";

    /// <summary>
    /// Controls which CommStation conversations become Archipelago location checks.
    /// <list type="bullet">
    ///   <item><term><see cref="ConversationCheckMode.Off"/></term>
    ///     <description>No conversation locations (default). All ranchers give vanilla gifts.</description></item>
    ///   <item><term><see cref="ConversationCheckMode.Conditional"/></term>
    ///     <description>The 8 conversations with confirmed zone or chain access conditions
    ///     (Radiant Projector Blueprint, Gordo Snare Advanced, Archive Key,
    ///     Mochi/Ogden/Thora intro calls, BOb's first gift and Yolky 1).</description></item>
    ///   <item><term><see cref="ConversationCheckMode.All"/></term>
    ///     <description>Every conversation — all 120 total including decorative gifts and story/lore dialogue.</description></item>
    /// </list>
    /// Slot data key: <c>"conversation_checks"</c> — values: <c>"none"</c>, <c>"conditional"</c>, <c>"all"</c>.
    /// </summary>
    public ConversationCheckMode ConversationChecks { get; init; } = ConversationCheckMode.Off;

    /// <summary>
    /// When true, Tarr slimes are excluded from the randomizer: the Slimepedia Tarr location
    /// is removed from the pool and Tarr Spawn / Tarr Rain traps have weight 0.
    /// Slot data key: <c>"disable_tarr"</c>.
    /// </summary>
    public bool DisableTarr { get; init; } = false;

    /// <summary>
    /// When true, RNG-gated slime locations are excluded from the pool:
    /// Slimepedia Gold/Lucky/Yolky, Radiant Yolky, and Fabricator Golden Sureshot I/II/III.
    /// The slimepedia goal skips these entries when checking for completion.
    /// Slot data key: <c>"exclude_rng_slimes"</c>.
    /// </summary>
    public bool ExcludeRngSlimes { get; init; } = false;

    /// <summary>
    /// When true, weather-dependent locations are excluded from the pool:
    /// Slimepedia Tangle/Dervish, Radiant Tangle/Dervish, and Resources
    /// Lightning Mote/Storm Glass/Drift Crystal.
    /// The slimepedia goal skips these entries when checking for completion.
    /// Slot data key: <c>"exclude_weather_checks"</c>.
    /// </summary>
    public bool ExcludeWeatherChecks { get; init; } = false;

    /// <summary>
    /// When true, Tarr bites always kill the player instantly regardless of health or multiplier.
    /// Slot data key: <c>"tarr_instakill"</c>.
    /// </summary>
    public bool TarrInstakill { get; init; } = false;

    /// <summary>
    /// Multiplier applied to all incoming player damage. 1 = normal (default). Range: 1–5.
    /// Slot data key: <c>"incoming_damage_multiplier"</c>.
    /// </summary>
    public int IncomingDamageMultiplier { get; init; } = 1;

    /// <summary>
    /// When true, weather events jump directly to their Heavy state as soon as they start.
    /// Light and Medium states are bypassed. Flat single-state patterns (Slime Rain, Snow)
    /// are unaffected (MapTier 0 — no tiered variants exist for them).
    /// On by default to ensure players reliably see severe-weather resource spawns.
    /// Slot data key: <c>"force_heavy_weather"</c>.
    /// </summary>
    public bool ForceHeavyWeather { get; init; } = true;

    /// <summary>
    /// Divides <c>WeatherRegistry.ForecastHourIntervalLow/High</c> by this value on scene load,
    /// making weather events start more frequently.
    /// 1 = vanilla interval (default). Range: 1–4.
    /// Slot data key: <c>"weather_frequency_multiplier"</c>.
    /// </summary>
    public int WeatherFrequencyMultiplier { get; init; } = 1;

    /// <summary>
    /// When true, the Resource Harvester upgrade is precollected — it appears in
    /// <c>AllItemsReceived</c> at session start and is applied through the normal
    /// item pipeline on first connect.
    /// Slot data key: <c>"start_with_resource_harvester"</c>.
    /// </summary>
    public bool StartWithResourceHarvester { get; init; } = false;

    /// <summary>
    /// Percentage of the vanilla feed count required to pop a Gordo (10–200).
    /// 100 = vanilla (default). Applied by scaling <c>GordoEat.TargetCount</c> on Awake.
    /// Slot data key: <c>"gordo_feed_requirement"</c>.
    /// </summary>
    public int GordoFeedRequirement { get; init; } = 100;

    /// <summary>
    /// Percentage of the vanilla Shadow Plort count required to trigger a Grey Labyrinth
    /// shadow plort door / dispenser. 100 = vanilla. Clamped to 10–200; the resulting count
    /// is always at least 1. Slot data key: <c>"shadow_plort_requirement"</c>.
    /// </summary>
    public int ShadowPlortRequirement { get; init; } = 100;

    /// <summary>
    /// When true, each of the 5 conservatory expansion terminals is a location check.
    /// Interacting and confirming sends the check; the expansion unlocks only when the
    /// corresponding AP item is received (no Newbucks cost in randomized mode).
    /// Slot data key: <c>"randomize_conservatory_expansions"</c>.
    /// </summary>
    public bool RandomizeConservatoryExpansions { get; init; } = false;

    /// <summary>
    /// Controls plort market saturation at game start.
    /// "disabled" = vanilla; "5_items" = full saturation + 5×20% recovery items;
    /// "10_items" = full saturation + 10×10% recovery items.
    /// Slot data key: <c>"plort_market_mode"</c>.
    /// </summary>
    public string PlortMarketMode { get; init; } = "disabled";

    /// <summary>
    /// How prominently received items are announced on the HUD.
    /// "none" = silent; "progression" = major popup for progression items only;
    /// "progression_useful" = major popup for progression and useful; "all" = both of those
    /// plus a small side notification for filler and traps.
    /// Slot data key: <c>"item_notifications"</c>. Consumed by <see cref="UI.ItemNotifier"/>.
    /// </summary>
    public string ItemNotifications { get; init; } = "progression_useful";

    /// <summary>
    /// Prisma Shards needed to unlock the Prismacore encounter (<c>prismacore_hunt</c> goal).
    /// Slot data key: <c>"prisma_shards_required"</c>.
    /// </summary>
    public int PrismaShardsRequired { get; init; } = 0;

    /// <summary>
    /// Total Prisma Shards placed in the pool. Display only — the gate uses
    /// <see cref="PrismaShardsRequired"/>. Slot data key: <c>"prisma_shards_total"</c>.
    /// </summary>
    public int PrismaShardsTotal { get; init; } = 0;

    public static SlotData Parse(Dictionary<string, object> raw)
    {
        return new SlotData
        {
            DeathLink          = GetBool(raw,   "death_link"),
            TrapPercentage     = (int)GetLong(raw, "trap_percentage", 0),
            Goal               = GetString(raw, "goal",               "labyrinth_open"),
            NewbucksGoalAmount = GetLong(raw,   "newbucks_goal_amount", 1_000_000),
            PlortGoalAmount    = (int)GetLong(raw, "plort_goal_amount", 25),
            RandomizeGordos          = GetBool(raw, "randomize_gordos",        defaultVal: true),
            RandomizePods            = GetBool(raw, "randomize_pods",          defaultVal: true),
            RandomizeMapNodes        = GetBool(raw, "randomize_map_nodes",     defaultVal: true),
            RandomizeFabricator      = GetBool(raw, "randomize_fabricator",    defaultVal: true),
            RandomizeSlimepedia          = GetBool(raw, "randomize_slimepedia",           defaultVal: false),
            RandomizeSlimepediaResources = GetBool(raw, "randomize_slimepedia_resources", defaultVal: false),
            RandomizeSlimepediaRadiant   = GetBool(raw, "randomize_slimepedia_radiant",   defaultVal: false),
            AllRadiantSlimes             = GetBool(raw, "all_radiant_slimes",             defaultVal: false),
            RadiantSpawnRateMultiplier   = (int)GetLong(raw, "radiant_spawn_rate_multiplier", 1),
            GoldLuckySpawnRateMultiplier = (int)Math.Clamp(GetLong(raw, "gold_lucky_spawn_rate_multiplier", 1), 1, 35),
            RandomizeResearchDrones  = GetBool(raw, "randomize_research_drones", defaultVal: false),
            RandomizeGhostlyDrones   = GetBool(raw, "randomize_ghostly_drones",  defaultVal: false),
            RandomizePlortDoors      = GetString(raw, "randomize_plort_doors", "vanilla"),
            RandomizeShadowDoors     = GetString(raw, "randomize_shadow_doors", "disabled"),
            RandomizePlortMarket     = GetBool(raw, "randomize_plort_market",     defaultVal: false),
            RandomizeShop            = GetBool(raw, "randomize_shop",             defaultVal: false),
            RandomizeSanctuary       = GetBool(raw, "randomize_sanctuary",        defaultVal: false),
            RandomizePlots           = GetBool(raw, "randomize_plots",            defaultVal: false),
            StartingPlots            = (int)GetLong(raw, "starting_plots", 2),
            RandomizePlotBuildings   = GetBool(raw, "randomize_plot_buildings",   defaultVal: false),
            RandomizePlotUpgrades    = GetBool(raw, "randomize_plot_upgrades",    defaultVal: false),
            RegionAccessMode            = GetString(raw, "region_access_mode", "vanilla"),
            ConversationChecks          = GetConversationCheckMode(raw, "conversation_checks"),
            DisableTarr                 = GetBool(raw, "disable_tarr",          defaultVal: false),
            ExcludeRngSlimes            = GetBool(raw, "exclude_rng_slimes",    defaultVal: false),
            ExcludeWeatherChecks        = GetBool(raw, "exclude_weather_checks", defaultVal: false),
            TarrInstakill               = GetBool(raw, "tarr_instakill", defaultVal: false),
            IncomingDamageMultiplier    = (int)GetLong(raw, "incoming_damage_multiplier", 1),
            ForceHeavyWeather              = GetBool(raw, "force_heavy_weather",              defaultVal: true),
            WeatherFrequencyMultiplier     = (int)GetLong(raw, "weather_frequency_multiplier", 1),
            StartWithResourceHarvester     = GetBool(raw, "start_with_resource_harvester",    defaultVal: false),
            GordoFeedRequirement           = (int)Math.Clamp(GetLong(raw, "gordo_feed_requirement", 100), 10, 200),
            ShadowPlortRequirement         = (int)Math.Clamp(GetLong(raw, "shadow_plort_requirement", 100), 10, 200),
            RandomizeConservatoryExpansions = GetBool(raw, "randomize_conservatory_expansions", defaultVal: false),
            PlortMarketMode                 = GetString(raw, "plort_market_mode", "disabled"),
            ItemNotifications               = GetString(raw, "item_notifications", "progression_useful"),
            PrismaShardsRequired            = (int)GetLong(raw, "prisma_shards_required", 0),
            PrismaShardsTotal               = (int)GetLong(raw, "prisma_shards_total", 0),
        };
    }

    private static bool GetBool(Dictionary<string, object> d, string key, bool defaultVal = false)
        => d.TryGetValue(key, out var v) && v is not null
            ? Convert.ToBoolean(v)
            : defaultVal;

    private static string GetString(Dictionary<string, object> d, string key, string defaultVal = "")
    {
        if (!d.TryGetValue(key, out var v) || v is null) return defaultVal;
        // New apworld sends human-readable strings; old/other apworlds may send integers.
        // Accept either — ToString() on a string is a no-op.
        var str = v.ToString();
        return string.IsNullOrEmpty(str) ? defaultVal : str;
    }

    private static long GetLong(Dictionary<string, object> d, string key, long defaultVal = 0)
        => d.TryGetValue(key, out var v) && v is not null
            ? Convert.ToInt64(v)
            : defaultVal;

    private static ConversationCheckMode GetConversationCheckMode(Dictionary<string, object> d, string key)
    {
        if (!d.TryGetValue(key, out var v) || v is null) return ConversationCheckMode.Off;
        return (v as string ?? v.ToString()) switch
        {
            "conditional" => ConversationCheckMode.Conditional,
            "all"         => ConversationCheckMode.All,
            _             => ConversationCheckMode.Off,
        };
    }
}
