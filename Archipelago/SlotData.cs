using SlimeRancher2AP.Data;

namespace SlimeRancher2AP.Archipelago;

/// <summary>
/// Slot data from login. Keys and option semantics are defined by the apworld (options.py,
/// fill_slot_data); the docs here note only what the mod does with them.
/// </summary>
public class SlotData
{
    public bool   DeathLink          { get; init; }

    public int    TrapPercentage     { get; init; } = 0;
    public string Goal               { get; init; } = "labyrinth_open";

    public long   NewbucksGoalAmount { get; init; } = 1_000_000;

    /// <summary>Plorts of EACH in-scope type to sell for "plort_seller" (rolled per seed).</summary>
    public int    PlortGoalAmount { get; init; } = 25;

    public bool   RandomizeGordos        { get; init; } = true;
    public bool   RandomizePods          { get; init; } = true;
    public bool   RandomizeMapNodes      { get; init; } = true;
    /// <summary>
    /// Diagnostics only — never gate on it. Crafts are checks in both modes; the option only
    /// decides whether they hold shuffled items or their vanilla upgrade.
    /// </summary>
    public bool   RandomizeFabricator    { get; init; } = true;
    public bool   RandomizeSlimepedia          { get; init; } = false;
    public bool   RandomizeSlimepediaResources { get; init; } = false;

    public bool   RandomizeSlimepediaRadiant   { get; init; } = false;

    /// <summary>Every encounter radiant, bypassing the shuffle bag (overrides the multiplier).</summary>
    public bool   AllRadiantSlimes             { get; init; } = false;
    /// <summary>Divides shuffle-bag sizes (<c>RadiantSlimeSpawnRatePatch</c>).</summary>
    public int    RadiantSpawnRateMultiplier   { get; init; } = 1;

    /// <summary>Scales Gold/Lucky <c>SlimeSet.Member.Weight</c> (<c>GoldLuckySpawnRatePatch</c>).</summary>
    public int    GoldLuckySpawnRateMultiplier { get; init; } = 1;

    public bool   RandomizeResearchDrones  { get; init; } = false;
    public bool   RandomizeGhostlyDrones   { get; init; } = false;

    /// <summary><c>"vanilla"</c> or <c>"locations"</c> (the 28 plort doors are checks).</summary>
    public string RandomizePlortDoors      { get; init; } = "vanilla";

    /// <summary>
    /// <c>"disabled"</c> or <c>"locations"</c>. Gates only the check — the vanilla reward (some
    /// doors dispense pool items) is suppressed in both modes.
    /// </summary>
    public string RandomizeShadowDoors     { get; init; } = "disabled";

    public bool   RandomizePlortMarket     { get; init; } = false;

    /// <summary>Only the seed's subset is active (<c>IsLocationInSeed</c>).</summary>
    public bool   RandomizeShop            { get; init; } = false;

    /// <summary>Adds the Sprinkles pedia location, which the slimepedia goal then requires.</summary>
    public bool   RandomizeSanctuary       { get; init; } = false;

    /// <summary>
    /// Ranch plots, three independent tiers — see <c>RanchPlotHandler</c>. Starting plots arrive
    /// as precollected items, so the mod only counts received items.
    /// </summary>
    public bool   RandomizePlots           { get; init; } = false;
    public int    StartingPlots            { get; init; } = 2;
    public bool   RandomizePlotBuildings   { get; init; } = false;
    public bool   RandomizePlotUpgrades    { get; init; } = false;

    /// <summary>
    /// <c>"vanilla"</c>: gates open normally and grant their zone teleporter. <c>"locations"</c>:
    /// pressing a gate is a check; it opens on its Region Access item. <c>"bundled"</c>: as
    /// locations, and the access item also grants the zone teleporter.
    /// </summary>
    public string RegionAccessMode { get; init; } = "vanilla";

    /// <summary>See <see cref="ConversationCheckMode"/>; values <c>"none"</c>, <c>"conditional"</c>, <c>"all"</c>.</summary>
    public ConversationCheckMode ConversationChecks { get; init; } = ConversationCheckMode.Off;

    public bool DisableTarr { get; init; } = false;

    /// <summary>The slimepedia and plort_seller goals skip the excluded entries too.</summary>
    public bool ExcludeRngSlimes { get; init; } = false;

    /// <summary>The slimepedia and plort_seller goals skip the excluded entries too.</summary>
    public bool ExcludeWeatherChecks { get; init; } = false;

    public bool TarrInstakill { get; init; } = false;

    public int IncomingDamageMultiplier { get; init; } = 1;

    /// <summary>Weather jumps straight to its Heavy state; single-state patterns are unaffected.</summary>
    public bool ForceHeavyWeather { get; init; } = true;

    /// <summary>Divides <c>WeatherRegistry.ForecastHourIntervalLow/High</c> on scene load.</summary>
    public int WeatherFrequencyMultiplier { get; init; } = 1;

    /// <summary>Precollected by the apworld, so it arrives through the normal item pipeline.</summary>
    public bool StartWithResourceHarvester { get; init; } = false;

    /// <summary>Percent of the vanilla feed count; scales <c>GordoEat.TargetCount</c>.</summary>
    public int GordoFeedRequirement { get; init; } = 100;

    /// <summary>Percent of the vanilla shadow plort count per door; at least 1.</summary>
    public int ShadowPlortRequirement { get; init; } = 100;

    /// <summary>
    /// Percent applied to in-game payouts only — AP filler and plot refunds are excluded
    /// (<c>PlayerStateAddCurrencyScalePatch</c>).
    /// </summary>
    public int NewbucksMultiplier { get; init; } = 100;

    /// <summary>Terminals are checks; each expansion opens on its item, at no Newbucks cost.</summary>
    public bool RandomizeConservatoryExpansions { get; init; } = false;

    /// <summary><c>"disabled"</c>, <c>"5_items"</c> or <c>"10_items"</c> (saturated start plus recovery items).</summary>
    public string PlortMarketMode { get; init; } = "disabled";

    /// <summary>
    /// <c>"none"</c>, <c>"progression"</c>, <c>"progression_useful"</c> or <c>"all"</c> — see
    /// <see cref="UI.ItemNotifier"/>.
    /// </summary>
    public string ItemNotifications { get; init; } = "progression_useful";

    /// <summary>Shards needed to unlock the Prismacore fight (<c>prisma_shard_hunt</c>).</summary>
    public int PrismaShardsRequired { get; init; } = 0;

    /// <summary>Shards in the pool. Display only.</summary>
    public int PrismaShardsTotal { get; init; } = 0;

    /// <summary>
    /// Catalog copies actually placed (0 = off) — already lowered when the seed had too few shop
    /// checks, so never read the raw option.
    /// </summary>
    public int ShopCatalogItems { get; init; } = 0;

    /// <summary>Wave per shop location: wave N needs N catalog copies; 0 is on sale from the start.</summary>
    public IReadOnlyDictionary<long, int> ShopCatalogWaves { get; init; }
        = new Dictionary<long, int>();

    /// <summary>True when this seed gates shop checks behind catalog items.</summary>
    public bool ShopCatalogActive => ShopCatalogItems > 0 && ShopCatalogWaves.Count > 0;

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
            NewbucksMultiplier             = (int)Math.Clamp(GetLong(raw, "newbucks_multiplier", 100), 10, 500),
            RandomizeConservatoryExpansions = GetBool(raw, "randomize_conservatory_expansions", defaultVal: false),
            PlortMarketMode                 = GetString(raw, "plort_market_mode", "disabled"),
            ItemNotifications               = GetString(raw, "item_notifications", "progression_useful"),
            PrismaShardsRequired            = (int)GetLong(raw, "prisma_shards_required", 0),
            ShopCatalogItems                = (int)GetLong(raw, "shop_catalog_items", 0),
            ShopCatalogWaves                = GetIntMap(raw, "shop_catalog_waves"),
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
        // Older apworlds may send integers.
        var str = v.ToString();
        return string.IsNullOrEmpty(str) ? defaultVal : str;
    }

    private static long GetLong(Dictionary<string, object> d, string key, long defaultVal = 0)
        => d.TryGetValue(key, out var v) && v is not null
            ? Convert.ToInt64(v)
            : defaultVal;

    /// <summary>
    /// Reads <c>"locationId": wave</c> pairs, from either a <c>JObject</c> (persisted JSON) or a
    /// dictionary (live). Bad entries are skipped, never thrown.
    /// </summary>
    private static IReadOnlyDictionary<long, int> GetIntMap(Dictionary<string, object> d, string key)
    {
        var result = new Dictionary<long, int>();
        if (!d.TryGetValue(key, out var v) || v is null) return result;

        try
        {
            switch (v)
            {
                case Newtonsoft.Json.Linq.JObject jo:
                    foreach (var prop in jo.Properties())
                        if (long.TryParse(prop.Name, out var jid))
                            result[jid] = prop.Value.ToObject<int>();
                    break;

                case System.Collections.IDictionary dict:
                    foreach (System.Collections.DictionaryEntry e in dict)
                    {
                        if (e.Key?.ToString() is { } k && long.TryParse(k, out var did)
                            && e.Value is not null)
                            result[did] = Convert.ToInt32(e.Value);
                    }
                    break;

                default:
                    Logger.Warning(
                        $"[AP] Slot data '{key}' had unexpected type {v.GetType().Name} — " +
                        "shop catalog gating disabled for this seed.");
                    break;
            }
        }
        catch (Exception ex)
        {
            Logger.Warning($"[AP] Could not parse slot data '{key}': {ex.Message}");
        }

        return result;
    }

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
