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

    public bool   RandomizeGordos        { get; init; } = true;
    public bool   RandomizePods          { get; init; } = true;
    public bool   RandomizeMapNodes      { get; init; } = true;
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
    /// When true, the 28 plort doors (PuzzleSlotLockable, lockTag='plort_door') that are not
    /// Grey Labyrinth shadow plort doors or the PB region gate become Archipelago location checks.
    /// Slot data key: <c>"randomize_puzzle_doors"</c>.
    /// </summary>
    public bool   RandomizePuzzleDoors     { get; init; } = false;

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

    public static SlotData Parse(Dictionary<string, object> raw)
    {
        return new SlotData
        {
            DeathLink          = GetBool(raw,   "death_link"),
            TrapPercentage     = (int)GetLong(raw, "trap_percentage", 0),
            Goal               = GetString(raw, "goal",               "labyrinth_open"),
            NewbucksGoalAmount = GetLong(raw,   "newbucks_goal_amount", 1_000_000),
            RandomizeGordos          = GetBool(raw, "randomize_gordos",        defaultVal: true),
            RandomizePods            = GetBool(raw, "randomize_pods",          defaultVal: true),
            RandomizeMapNodes        = GetBool(raw, "randomize_map_nodes",     defaultVal: true),
            RandomizeSlimepedia          = GetBool(raw, "randomize_slimepedia",           defaultVal: false),
            RandomizeSlimepediaResources = GetBool(raw, "randomize_slimepedia_resources", defaultVal: false),
            RandomizeSlimepediaRadiant   = GetBool(raw, "randomize_slimepedia_radiant",   defaultVal: false),
            AllRadiantSlimes             = GetBool(raw, "all_radiant_slimes",             defaultVal: false),
            RadiantSpawnRateMultiplier   = (int)GetLong(raw, "radiant_spawn_rate_multiplier", 1),
            GoldLuckySpawnRateMultiplier = (int)Math.Clamp(GetLong(raw, "gold_lucky_spawn_rate_multiplier", 1), 1, 35),
            RandomizeResearchDrones  = GetBool(raw, "randomize_research_drones", defaultVal: false),
            RandomizeGhostlyDrones   = GetBool(raw, "randomize_ghostly_drones",  defaultVal: false),
            RandomizePuzzleDoors     = GetBool(raw, "randomize_puzzle_doors",     defaultVal: false),
            RegionAccessMode            = GetString(raw, "region_access_mode", "vanilla"),
            ConversationChecks          = GetConversationCheckMode(raw, "conversation_checks"),
            DisableTarr                 = GetBool(raw, "disable_tarr",   defaultVal: false),
            TarrInstakill               = GetBool(raw, "tarr_instakill", defaultVal: false),
            IncomingDamageMultiplier    = (int)GetLong(raw, "incoming_damage_multiplier", 1),
            ForceHeavyWeather           = GetBool(raw, "force_heavy_weather",          defaultVal: true),
            WeatherFrequencyMultiplier  = (int)GetLong(raw, "weather_frequency_multiplier", 1),
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
