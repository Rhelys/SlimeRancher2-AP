using Il2CppMonomiPark.SlimeRancher;
using Il2CppMonomiPark.SlimeRancher.DataModel;
using Il2CppMonomiPark.SlimeRancher.Economy;
using Il2CppMonomiPark.SlimeRancher.Pedia;
using Il2CppList = Il2CppSystem.Collections.Generic.List<Il2CppMonomiPark.SlimeRancher.Pedia.PediaRuntimeCategory>;
using System.Linq;
using UnityEngine;

namespace SlimeRancher2AP.Archipelago;

/// <summary>
/// Detects when the configured goal condition is met and notifies the AP server.
/// <list type="bullet">
///   <item><term>labyrinth_open</term><description>
///     Tracks two WorldStateInvisibleSwitch openings via OnSwitchOpened() (InvisibleSwitchPatch).
///     Both gates: EnergyBeamReceiver "energyBeamReceiver" → WorldStateInvisibleSwitch in sub-scenes.
///     Strand: "zoneStrandLabyrinthGate:energyBeamReceiver" — confirmed.
///     Valley: "zoneGorgeGateTransfer:energyBeamReceiver" — confirmed.
///   </description></item>
///   <item><term>newbucks</term><description>
///     Polled via Tick() — checks PlayerModel.CurrencyInfo.AmountEverCollected (lifetime total).
///   </description></item>
///   <item><term>prismacore</term><description>
///     Event-based: CoreRoomController.UpdateState Postfix fires OnCoreRoomStateChanged(POST_FIGHT)
///     when the boss fight completes and the Prismacore is stabilized.
///   </description></item>
///   <item><term>slimepedia</term><description>
///     Polled via Tick() — checks PediaRuntimeCategory.AllUnlocked() for BOTH the "Slimes"
///     category (29 entries) and the "Resources" category (54 entries).
///     Goal fires only when both categories are fully unlocked.
///   </description></item>
/// </list>
/// Call Initialize() after AP connect, Tick() each frame, and the On* event methods from patches.
/// </summary>
public static class GoalHandler
{
    // -------------------------------------------------------------------------
    // Labyrinth switch tracking
    // -------------------------------------------------------------------------

    // Keys are "scene:switchName". BOTH portals must open for the goal to fire.
    //
    // Both gates use: EnergyBeamReceiver (name='energyBeamReceiver') → WorldStateInvisibleSwitch.SetStateForAll(DOWN)
    // Detected via InvisibleSwitchPatch → OnSwitchOpened.
    //
    // Strand gate: scene='zoneStrandLabyrinthGate' — switch name TBD from [AP-Gate] InvisibleSwitch DOWN log
    // Valley gate: scene='zoneGorgeGateTransfer'  — switch name TBD from [AP-Gate] InvisibleSwitch DOWN log
    //
    // NOTE: The old LabyrinthSwitchStrand = "zoneStrand_Area4:ruinSwitch" was incorrect —
    //   "ruinSwitch" is a different WorldStatePrimarySwitch in the zone, not the labyrinth gate.
    private const string LabyrinthSwitchStrand = "zoneStrandLabyrinthGate:energyBeamReceiver";  // confirmed
    private const string LabyrinthSwitchValley = "zoneGorgeGateTransfer:energyBeamReceiver";   // confirmed

    private static readonly HashSet<string> _openedLabyrinthSwitches = new();

    // -------------------------------------------------------------------------
    // Polling throttle (~60-frame cadence ≈ once per second)
    // -------------------------------------------------------------------------

    private static int _tickCounter = 0;
    private const int TickInterval  = 60;

    // -------------------------------------------------------------------------
    // State
    // -------------------------------------------------------------------------

    private static bool _goalAchieved = false;

    /// <summary>True once the goal has been completed this session.</summary>
    public static bool IsGoalComplete => _goalAchieved;

    // Newbucks goal caches
    private static int                _newbucksGoalAmount     = -1;
    private static CurrencyDefinition? _newbucksDef           = null;

    /// <summary>
    /// PersistenceId of the Newbucks CurrencyDefinition, or -1 if not yet cached.
    /// Used by <c>PlayerStateAddCurrencyPatch</c> to filter for newbucks-only AddCurrency calls.
    /// </summary>
    internal static int NewbucksPersistenceId => _newbucksDef?.PersistenceId ?? -1;

    // Slimepedia goal: PediaRuntimeCategory names confirmed via DumpPedia().
    // 'Slimes' (29 entries), 'Resources' (54 entries), 'Radiant Slimes' (22 entries).
    // With ExcludeRngSlimes / ExcludeWeatherChecks, some entries are skipped — counts vary.
    // Goal fires when all entries in every *enabled* category are unlocked.
    private const string SlimesCategoryName    = "Slimes";
    private const string ResourcesCategoryName = "Resources";
    private const string RadiantCategoryName   = "Radiant Slimes";

    // -------------------------------------------------------------------------
    // Lifecycle
    // -------------------------------------------------------------------------

    /// <summary>
    /// Called after a successful AP login. Caches goal parameters from slot data.
    /// Must be called on the main thread.
    /// </summary>
    public static void Initialize()
    {
        _goalAchieved = false;
        _tickCounter  = 0;
        _openedLabyrinthSwitches.Clear();

        var slotData = Plugin.Instance.ApClient.SlotData;
        if (slotData == null) return;

        _newbucksGoalAmount = (int)slotData.NewbucksGoalAmount;

        // Pre-cache CurrencyDefinition so we don't search every tick.
        // Resources are loaded lazily; if unavailable now they will be found on first Tick().
        if (slotData.Goal == "newbucks")
            TryCacheNewbucksDef();

        Logger.Info($"[AP] GoalHandler initialized for goal '{slotData.Goal}'");
    }

    // -------------------------------------------------------------------------
    // Polling (called from ApUpdateBehaviour.Update each frame)
    // -------------------------------------------------------------------------

    public static void Tick()
    {
        if (!Plugin.Instance.ApClient.IsConnected || _goalAchieved) return;

        if (++_tickCounter < TickInterval) return;
        _tickCounter = 0;

        var goal = Plugin.Instance.ApClient.SlotData?.Goal;
        switch (goal)
        {
            case "newbucks":    CheckNewbucksGoal();    break;
            case "slimepedia":  CheckSlimepediaGoal();  break;
        }
    }

    // -------------------------------------------------------------------------
    // Event hooks (called by Harmony patches on the main thread)
    // -------------------------------------------------------------------------

    /// <summary>
    /// Called from RegionGatePatch (WorldStatePrimarySwitch) and InvisibleSwitchPatch
    /// (WorldStateInvisibleSwitch) whenever either type of switch transitions to DOWN.
    /// Key format is "scene:switchName". Safe to call repeatedly — HashSet deduplicates,
    /// so the spam from a continuous beam-pulse receiver is harmless.
    /// </summary>
    public static void OnSwitchOpened(string switchName, string sceneName)
    {
        if (!Plugin.Instance.ApClient.IsConnected || _goalAchieved) return;
        if (Plugin.Instance.ApClient.SlotData?.Goal != "labyrinth_open") return;

        var key = $"{sceneName}:{switchName}";
        if (key == LabyrinthSwitchStrand || key == LabyrinthSwitchValley)
        {
            if (_openedLabyrinthSwitches.Add(key))  // Add() returns false if already present
            {
                Logger.Info(
                    $"[AP] Labyrinth gate opened: '{key}' ({_openedLabyrinthSwitches.Count}/2)");
                CheckLabyrinthComplete();
            }
        }
    }

    private static void CheckLabyrinthComplete()
    {
        if (_openedLabyrinthSwitches.Contains(LabyrinthSwitchStrand) &&
            _openedLabyrinthSwitches.Contains(LabyrinthSwitchValley))
        {
            NotifyGoalComplete();
        }
    }

    /// <summary>
    /// Called from CoreRoomControllerPatch Postfix on every UpdateState call.
    /// Handles the prismacore goal: fires when POST_FIGHT is set (boss fight complete, core stabilized).
    /// PRE_FIGHT fires on scene load and is intentionally ignored — it does NOT indicate room entry.
    /// </summary>
    public static void OnCoreRoomStateChanged(CoreRoomController.CoreRoomState state)
    {
        if (!Plugin.Instance.ApClient.IsConnected || _goalAchieved) return;

        var goal = Plugin.Instance.ApClient.SlotData?.Goal;

        if (goal == "prismacore" && state == CoreRoomController.CoreRoomState.POST_FIGHT)
        {
            Logger.Info("[AP] Prismacore stabilized (POST_FIGHT state)");
            NotifyGoalComplete();
        }
    }

    // -------------------------------------------------------------------------
    // Goal checks
    // -------------------------------------------------------------------------

    private static void CheckNewbucksGoal()
    {
        if (_newbucksGoalAmount < 0) return;

        // Retry cache if it failed during Initialize() (e.g. Resources not loaded yet)
        if (_newbucksDef == null)
            TryCacheNewbucksDef();
        if (_newbucksDef == null) return;

        // Read our own persisted counter — PlayerModel.AmountEverCollected is never updated
        // by any SR2 code path (plort selling, etc.) and cannot be relied upon.
        // PlayerStateAddCurrencyPatch accumulates all positive AddCurrency calls into
        // ApSaveManager.NewbucksEarned, which persists across sessions.
        long earned = Plugin.Instance.SaveManager.NewbucksEarned;
        // Logger.Info($"[AP] Newbucks check: earned={earned:N0} / target={_newbucksGoalAmount:N0}");
        if (earned >= _newbucksGoalAmount)
        {
            Logger.Info(
                $"[AP] Newbucks goal met: {earned:N0} earned >= {_newbucksGoalAmount:N0} target");
            NotifyGoalComplete();
        }
    }

    private static void CheckSlimepediaGoal()
    {
#if DEBUG
        SlimeRancher2AP.Utils.DebugTrace.Once("CheckSlimepediaGoal — step 1: entry");
#endif
        var pedia = SceneContext.Instance?.PediaDirector;
        if (pedia == null) return;

        var rawCategories = pedia.Categories;
        if (rawCategories == null) return;

        // IL2CPP IReadOnlyList<T> wrapper: wrap via Pointer for indexed access.
        var categories = new Il2CppList(rawCategories.Pointer);
#if DEBUG
        SlimeRancher2AP.Utils.DebugTrace.Once($"CheckSlimepediaGoal — step 2: list count={categories.Count}");
#endif

        var slotData = Plugin.Instance.ApClient.SlotData;

        bool needSlimes    = slotData?.RandomizeSlimepedia          ?? false;
        bool needResources = slotData?.RandomizeSlimepediaResources  ?? false;
        bool needRadiant   = slotData?.RandomizeSlimepediaRadiant    ?? false;

        // Build per-category exclusion sets from active slot data options.
        // PediaEntry.name values are Unity ScriptableObject asset names confirmed via LocationTable.
        // The apworld removes the same entries from the location pool (_RNG_SLIMES_EXCLUDED /
        // _WEATHER_CHECKS_EXCLUDED in __init__.py), including the radiant variants.
        var slimesExcluded    = new HashSet<string>();
        var resourcesExcluded = new HashSet<string>();
        var radiantExcluded   = new HashSet<string>();

        if (slotData?.DisableTarr ?? false)
            slimesExcluded.Add("Tarr");

        if (slotData?.ExcludeRngSlimes ?? false)
        {
            slimesExcluded.Add("Gold");
            slimesExcluded.Add("Lucky");
            slimesExcluded.Add("Yolky");
            radiantExcluded.Add("RadiantYolky");
        }

        if (slotData?.ExcludeWeatherChecks ?? false)
        {
            slimesExcluded.Add("Tangle");
            slimesExcluded.Add("Dervish");
            resourcesExcluded.Add("StormGlassCraft");
            resourcesExcluded.Add("LightningMoteCraft");
            resourcesExcluded.Add("DriftCrystalCraft");
            radiantExcluded.Add("RadiantTangle");
            radiantExcluded.Add("RadiantDervish");
        }

        // Track completion per enabled category.
        // Disabled categories start true so they don't block the goal.
        bool slimesUnlocked    = !needSlimes;
        bool resourcesUnlocked = !needResources;
        bool radiantUnlocked   = !needRadiant;

        for (int i = 0; i < categories.Count; i++)
        {
            var cat = categories[i];
            var catName = cat?._category?.name;

            if (needSlimes    && catName == SlimesCategoryName)
                slimesUnlocked    = IsCategoryUnlockedExcluding(pedia, cat!, slimesExcluded);
            else if (needResources && catName == ResourcesCategoryName)
                resourcesUnlocked = IsCategoryUnlockedExcluding(pedia, cat!, resourcesExcluded);
            else if (needRadiant   && catName == RadiantCategoryName)
                radiantUnlocked   = IsCategoryUnlockedExcluding(pedia, cat!, radiantExcluded);

            if (slimesUnlocked && resourcesUnlocked && radiantUnlocked) break;
        }
#if DEBUG
        SlimeRancher2AP.Utils.DebugTrace.Once(
            $"CheckSlimepediaGoal — slimes={slimesUnlocked}(need={needSlimes}) " +
            $"resources={resourcesUnlocked}(need={needResources}) " +
            $"radiant={radiantUnlocked}(need={needRadiant})");
#endif

        if (slimesUnlocked && resourcesUnlocked && radiantUnlocked)
        {
            Logger.Info("[AP] Slimepedia goal met: all enabled Slimepedia categories complete");
            NotifyGoalComplete();
        }
    }

    /// <summary>
    /// Returns true if every entry in <paramref name="cat"/> is unlocked, skipping any whose
    /// <c>PediaEntry.name</c> is in <paramref name="excludeNames"/>.
    /// When <paramref name="excludeNames"/> is empty, falls back to the native
    /// <c>AllUnlocked()</c> call (faster path, no per-entry iteration).
    /// </summary>
    private static bool IsCategoryUnlockedExcluding(PediaDirector pedia,
                                                     PediaRuntimeCategory cat,
                                                     HashSet<string> excludeNames)
    {
        if (excludeNames.Count == 0) return cat.AllUnlocked();
        var items = cat._items;
        if (items == null) return false;
        for (int i = 0; i < items.Count; i++)
        {
            var entry = items[i];
            if (entry == null) continue;
            if (excludeNames.Contains(entry.name)) continue;
            if (!pedia.IsUnlocked(entry)) return false;
        }
        return true;
    }

    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    private static void TryCacheNewbucksDef()
    {
        var def = Resources.FindObjectsOfTypeAll<CurrencyDefinition>()
                           .FirstOrDefault(c => c.name.IndexOf(
                               "Newbucks", System.StringComparison.OrdinalIgnoreCase) >= 0);
        if (def != null)
        {
            _newbucksDef = def;
            Logger.Info(
                $"[AP] Newbucks currency cached: name='{def.name}' PersistenceId={def.PersistenceId}, " +
                $"goal target={_newbucksGoalAmount:N0}");
        }
    }

    // -------------------------------------------------------------------------
    // Completion
    // -------------------------------------------------------------------------

    /// <summary>
    /// Marks the goal as achieved and notifies the AP server. Idempotent — safe to call multiple times.
    /// </summary>
    public static void NotifyGoalComplete()
    {
        if (_goalAchieved) return;
        _goalAchieved = true;
        Logger.Info("[AP] Goal complete!");
        Plugin.Instance.ApClient.SetGoalComplete();
    }

#if DEBUG
    // -------------------------------------------------------------------------
    // Debug helpers (Debug builds only)
    // -------------------------------------------------------------------------

    /// <summary>
    /// Sets AmountEverCollected to the goal target so the next Force Check will trigger.
    /// Calls PlayerModel.SetCurrencyAndAmountEverCollected, which is the only managed API
    /// that writes AmountEverCollected. PlayerState.AddCurrency does NOT update it.
    /// </summary>
    public static void DebugSetLifetimeNewbucksToGoal()
    {
        var saveManager = Plugin.Instance.SaveManager;
        if (!saveManager.HasActiveSession)
        {
            Logger.Warning("[AP-Debug] No active AP session — load a save first");
            return;
        }

        // Force-set the persisted earned counter to the goal amount so Force Check fires.
        long current = saveManager.NewbucksEarned;
        if (_newbucksGoalAmount > current)
        {
            saveManager.AccumulateNewbucks((int)(_newbucksGoalAmount - current));
            Logger.Info(
                $"[AP-Debug] Set NewbucksEarned to {_newbucksGoalAmount:N0} (was {current:N0})");
        }
        else
        {
            Logger.Info(
                $"[AP-Debug] NewbucksEarned already {current:N0} >= {_newbucksGoalAmount:N0} — use Force Check");
        }
    }

    /// <summary>
    /// Forces an immediate check of the current polled goal (newbucks / slimepedia),
    /// bypassing the Tick interval. Useful for testing without waiting 60 frames.
    /// </summary>
    public static void DebugForceCheck()
    {
        var goal = Plugin.Instance.ApClient.SlotData?.Goal ?? "";
        switch (goal)
        {
            case "newbucks":   CheckNewbucksGoal();   break;
            case "slimepedia": CheckSlimepediaGoal(); break;
            default:
                Logger.Info($"[AP-Debug] DebugForceCheck: goal '{goal}' is event-based, use Sim buttons");
                break;
        }
    }

    /// <summary>Simulates the Prismacore stabilizing. Calls OnCoreRoomStateChanged with POST_FIGHT directly.</summary>
    public static void DebugSimPrismacore() =>
        OnCoreRoomStateChanged(CoreRoomController.CoreRoomState.POST_FIGHT);

    /// <summary>
    /// Simulates both Labyrinth gates opening for testing the labyrinth_open goal.
    /// Fires OnSwitchOpened for both the Strand and Valley switches.
    /// Only triggers if the current AP goal is "labyrinth_open".
    /// </summary>
    public static void DebugSimLabyrinth()
    {
        Logger.Info("[AP-Debug] Simulating both Labyrinth gate opens");

        // Strand: WorldStatePrimarySwitch
        {
            var key = LabyrinthSwitchStrand;
            var idx = key.IndexOf(':');
            if (idx >= 0) OnSwitchOpened(key[(idx + 1)..], key[..idx]);
        }

        // Valley: WorldStateInvisibleSwitch (name TBD — read from [AP-Gate] InvisibleSwitch DOWN log)
        {
            var key = LabyrinthSwitchValley;
            var idx = key.IndexOf(':');
            if (idx >= 0) OnSwitchOpened(key[(idx + 1)..], key[..idx]);
        }
    }
#endif
}
