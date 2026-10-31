using Il2CppMonomiPark.SlimeRancher;
using Il2CppMonomiPark.SlimeRancher.DataModel;
using Il2CppMonomiPark.SlimeRancher.Economy;
using Il2CppMonomiPark.SlimeRancher.Pedia;
using Il2CppList = Il2CppSystem.Collections.Generic.List<Il2CppMonomiPark.SlimeRancher.Pedia.PediaRuntimeCategory>;
using System.Linq;
using UnityEngine;

namespace SlimeRancher2AP.Archipelago;

/// <summary>
/// Goal detection. Polled goals (newbucks, slimepedia, plort_seller) run in Tick(); event goals
/// (labyrinth_open, prismacore) come from patches via the On* methods. Runs on a bound, trusted save
/// even offline: the goal is persisted and reported now or by <see cref="Initialize"/> on the
/// next connect.
/// </summary>
public static class GoalHandler
{
    // -------------------------------------------------------------------------
    // Labyrinth switch tracking
    // -------------------------------------------------------------------------

    // "scene:switchName" of the two beam gates; both must open. Persisted in ApSaveManager.
    private const string LabyrinthSwitchStrand = "zoneStrandLabyrinthGate:energyBeamReceiver";
    private const string LabyrinthSwitchValley = "zoneGorgeGateTransfer:energyBeamReceiver";

    // -------------------------------------------------------------------------
    // Polling throttle (~60-frame cadence ≈ once per second)
    // -------------------------------------------------------------------------

    private static int _tickCounter = 0;
    private const int TickInterval  = 60;

    // -------------------------------------------------------------------------
    // State
    // -------------------------------------------------------------------------

    private static bool _goalAchieved = false;

    /// <summary>True once the goal is met, this session or earlier (persisted).</summary>
    public static bool IsGoalComplete => _goalAchieved || Plugin.Instance.SaveManager.GoalReached;

    /// <summary>
    /// Bound, trusted save with slot data — no connection required. Costly (crosses IL2CPP),
    /// so callers test it last.
    /// </summary>
    private static bool CanTrackGoal()
        => Plugin.Instance.ModEnabled
        && Plugin.Instance.SaveManager.IsSaveBound
        && Plugin.Instance.ApClient.SlotData != null
        && SaveGuard.IsSaveTrusted();

    // Newbucks goal caches
    private static int                _newbucksGoalAmount     = -1;
    private static CurrencyDefinition? _newbucksDef           = null;

    // PediaRuntimeCategory names for the slimepedia goal.
    private const string SlimesCategoryName    = "Slimes";
    private const string ResourcesCategoryName = "Resources";
    private const string RadiantCategoryName   = "Radiant Slimes";

    // -------------------------------------------------------------------------
    // Lifecycle
    // -------------------------------------------------------------------------

    /// <summary>After login, main thread: reports an earlier goal and caches parameters.</summary>
    public static void Initialize()
    {
        Reset();

        // Resent on every connect; the server treats it as idempotent.
        if (Plugin.Instance.SaveManager.GoalReached)
        {
            Logger.Info("[AP] Goal was reached earlier — reporting it to the server.");
            Plugin.Instance.ApClient.SetGoalComplete();
        }

        var slotData = Plugin.Instance.ApClient.SlotData;
        if (slotData == null) return;

        _newbucksGoalAmount = (int)slotData.NewbucksGoalAmount;

        // Retried on Tick if not loaded yet.
        if (slotData.Goal == "newbucks")
            TryCacheNewbucksDef();

        Logger.Info($"[AP] GoalHandler initialized for goal '{slotData.Goal}'");
    }

    // -------------------------------------------------------------------------
    // Polling
    // -------------------------------------------------------------------------

    /// <summary>Clears per-session state (Initialize and disconnect).</summary>
    public static void Reset()
    {
        _goalAchieved = false;
        _tickCounter  = 0;
    }

    public static void Tick()
    {
        if (IsGoalComplete) return;

        if (++_tickCounter < TickInterval) return;
        _tickCounter = 0;

        if (!CanTrackGoal()) return;

        var goal = Plugin.Instance.ApClient.SlotData?.Goal;
        switch (goal)
        {
            case "newbucks":     CheckNewbucksGoal();    break;
            case "slimepedia":   CheckSlimepediaGoal();  break;
            case "plort_seller": CheckPlortSellerGoal(); break;
        }
    }

    // -------------------------------------------------------------------------
    // Event hooks (called by Harmony patches on the main thread)
    // -------------------------------------------------------------------------

    /// <summary>Called whenever a world switch goes DOWN. Repeats are cheap and harmless.</summary>
    public static void OnSwitchOpened(string switchName, string sceneName)
    {
        if (IsGoalComplete) return;
        if (Plugin.Instance.ApClient.SlotData?.Goal != "labyrinth_open") return;

        var key = $"{sceneName}:{switchName}";
        if (key != LabyrinthSwitchStrand && key != LabyrinthSwitchValley) return;

        // The receiver pulses while lit — skip the costly trust check once recorded.
        var saveManager = Plugin.Instance.SaveManager;
        if (saveManager.IsLabyrinthGateOpened(key)) return;
        if (!CanTrackGoal()) return;

        if (saveManager.MarkLabyrinthGateOpened(key))
        {
            int opened = (saveManager.IsLabyrinthGateOpened(LabyrinthSwitchStrand) ? 1 : 0)
                       + (saveManager.IsLabyrinthGateOpened(LabyrinthSwitchValley) ? 1 : 0);
            Logger.Info($"[AP] Labyrinth gate opened: '{key}' ({opened}/2)");
            CheckLabyrinthComplete();
        }
    }

    private static void CheckLabyrinthComplete()
    {
        var saveManager = Plugin.Instance.SaveManager;
        if (saveManager.IsLabyrinthGateOpened(LabyrinthSwitchStrand) &&
            saveManager.IsLabyrinthGateOpened(LabyrinthSwitchValley))
        {
            NotifyGoalComplete();
        }
    }

    /// <summary>
    /// From CoreRoomControllerPatch. POST_FIGHT (core stabilized) wins; PRE_FIGHT fires on every
    /// scene load and means nothing.
    /// </summary>
    public static void OnCoreRoomStateChanged(CoreRoomController.CoreRoomState state)
    {
        if (IsGoalComplete) return;
        if (state != CoreRoomController.CoreRoomState.POST_FIGHT) return;
        if (!CanTrackGoal()) return;

        var goal = Plugin.Instance.ApClient.SlotData?.Goal;

        // Shards only gate entry to the fight; winning it is still the goal.
        if ((goal == "prismacore" || goal == "prisma_shard_hunt")
            && state == CoreRoomController.CoreRoomState.POST_FIGHT)
        {
            Logger.Info($"[AP] Prismacore stabilized (POST_FIGHT state, goal='{goal}')");
            NotifyGoalComplete();
        }
    }

    // -------------------------------------------------------------------------
    // Goal checks
    // -------------------------------------------------------------------------

    private static void CheckNewbucksGoal()
    {
        if (_newbucksGoalAmount < 0) return;

        if (_newbucksDef == null)
            TryCacheNewbucksDef();
        if (_newbucksDef == null) return;

        // Our own counter — the game never updates AmountEverCollected.
        long earned = Plugin.Instance.SaveManager.NewbucksEarned;
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

        // Wrap by pointer for indexed access.
        var categories = new Il2CppList(rawCategories.Pointer);
#if DEBUG
        SlimeRancher2AP.Utils.DebugTrace.Once($"CheckSlimepediaGoal — step 2: list count={categories.Count}");
#endif

        var slotData = Plugin.Instance.ApClient.SlotData;

        bool needSlimes    = slotData?.RandomizeSlimepedia          ?? false;
        bool needResources = slotData?.RandomizeSlimepediaResources  ?? false;
        bool needRadiant   = slotData?.RandomizeSlimepediaRadiant    ?? false;

        // Must match the apworld's pool exclusions (_RNG_SLIMES_EXCLUDED / _WEATHER_CHECKS_EXCLUDED).
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

        // The radiant concept entry follows the radiant toggle, not the Slimes one.
        if (!needRadiant)
            slimesExcluded.Add("RadiantSlime");

        if (!(slotData?.RandomizeSanctuary ?? false))
            resourcesExcluded.Add("Sprinkles");

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

        // Disabled categories start complete.
        bool slimesUnlocked    = !needSlimes;
        bool resourcesUnlocked = !needResources;
        bool radiantUnlocked   = !needRadiant;

        for (int i = 0; i < categories.Count; i++)
        {
            var cat = categories[i];
            var catName = cat?._category?.name;

            if (needSlimes    && catName == SlimesCategoryName)
                slimesUnlocked    = IsCategoryUnlockedForAp(
                    pedia, cat!, Data.LocationType.SlimepediaEntry, slimesExcluded);
            else if (needResources && catName == ResourcesCategoryName)
                resourcesUnlocked = IsCategoryUnlockedForAp(
                    pedia, cat!, Data.LocationType.SlimepediaResourceEntry, resourcesExcluded);
            else if (needRadiant   && catName == RadiantCategoryName)
                radiantUnlocked   = IsCategoryUnlockedForAp(
                    pedia, cat!, Data.LocationType.SlimepediaRadiantEntry, radiantExcluded);

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

    // -------------------------------------------------------------------------
    // plort_seller goal
    // -------------------------------------------------------------------------

    /// <summary>
    /// The plort_seller scope: all 25 types (GL included) minus the RNG/weather exclusions —
    /// the same scope as PlortMarketPatch and the apworld.
    /// </summary>
    public static List<string> PlortSellerScope()
    {
        var slotData = Plugin.Instance.ApClient.SlotData;
        var scope = new List<string>();
        foreach (var plortName in Data.LocationTable.AllPlortMarketPlortNames)
        {
            if ((slotData?.ExcludeRngSlimes ?? false)
                && Patches.LocationPatches.PlortMarketPatch.IsRngExcludedPlort(plortName)) continue;
            if ((slotData?.ExcludeWeatherChecks ?? false)
                && Patches.LocationPatches.PlortMarketPatch.IsWeatherExcludedPlort(plortName)) continue;
            scope.Add(plortName);
        }
        return scope;
    }

    /// <summary>Types at/above the target vs total in scope — shared with the pause menu display.</summary>
    public static (int complete, int total) PlortSellerProgress()
    {
        int target = Plugin.Instance.ApClient.SlotData?.PlortGoalAmount ?? 0;
        var scope  = PlortSellerScope();
        int done   = 0;
        foreach (var plortName in scope)
            if (Plugin.Instance.SaveManager.PlortsSold(plortName) >= target)
                done++;
        return (done, scope.Count);
    }

    private static void CheckPlortSellerGoal()
    {
        var (done, total) = PlortSellerProgress();
        if (total > 0 && done >= total)
        {
            Logger.Info(
                $"[AP] Plort Seller goal met: all {total} in-scope plort types reached " +
                $"{Plugin.Instance.ApClient.SlotData?.PlortGoalAmount ?? 0} sold");
            NotifyGoalComplete();
        }
    }

    /// <summary>
    /// True when every non-excluded entry AP has a location for in this category is unlocked.
    /// </summary>
    /// <remarks>
    /// Never the game's <c>AllUnlocked()</c>: categories can hold entries with no AP location
    /// and grow at runtime (<c>AddDynamicItem</c>), which once made the goal unreachable.
    /// </remarks>
    private static bool IsCategoryUnlockedForAp(PediaDirector pedia,
                                                 PediaRuntimeCategory cat,
                                                 Data.LocationType locationType,
                                                 HashSet<string> excludeNames)
    {
        var required = Data.LocationTable.SlimepediaEntryNames(locationType);
        var items    = cat._items;
        if (items == null) return false;

        for (int i = 0; i < items.Count; i++)
        {
            var entry = items[i];
            if (entry == null) continue;
            var name = entry.name;
            if (!required.Contains(name)) continue;   // not an AP location
            if (excludeNames.Contains(name)) continue;
            if (!pedia.IsUnlocked(entry)) return false;
        }
        return true;
    }

    /// <summary>
    /// Logs, per category, AP-tracked entries still locked and game entries AP doesn't track —
    /// for "why hasn't my slimepedia goal fired?".
    /// </summary>
    public static void LogSlimepediaProgress()
    {
        var pedia = SceneContext.Instance?.PediaDirector;
        if (pedia == null)
        {
            Logger.Warning("[AP] Slimepedia progress: PediaDirector not available — load a save first");
            return;
        }
        var rawCategories = pedia.Categories;
        if (rawCategories == null) return;
        var categories = new Il2CppList(rawCategories.Pointer);

        Logger.Info("[AP] ===== Slimepedia goal progress =====");
        for (int i = 0; i < categories.Count; i++)
        {
            var cat     = categories[i];
            var catName = cat?._category?.name;
            Data.LocationType type;
            if      (catName == SlimesCategoryName)    type = Data.LocationType.SlimepediaEntry;
            else if (catName == ResourcesCategoryName) type = Data.LocationType.SlimepediaResourceEntry;
            else if (catName == RadiantCategoryName)   type = Data.LocationType.SlimepediaRadiantEntry;
            else continue;

            var required = Data.LocationTable.SlimepediaEntryNames(type);
            var items    = cat!._items;
            if (items == null) continue;

            int tracked = 0, unlocked = 0;
            var locked    = new List<string>();
            var untracked = new List<string>();
            for (int j = 0; j < items.Count; j++)
            {
                var entry = items[j];
                if (entry == null) continue;
                var name = entry.name;
                if (!required.Contains(name)) { untracked.Add(name); continue; }
                tracked++;
                if (pedia.IsUnlocked(entry)) unlocked++;
                else locked.Add(name);
            }

            Logger.Info(
                $"[AP]   '{catName}': {unlocked}/{tracked} AP-tracked entries unlocked " +
                $"(game category has {items.Count} total)");
            if (locked.Count > 0)
                Logger.Info($"[AP]     still locked: {string.Join(", ", locked)}");
            if (untracked.Count > 0)
                Logger.Info($"[AP]     not AP-tracked (never required): {string.Join(", ", untracked)}");
        }
        Logger.Info("[AP] ====================================");
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
                $"[AP] Newbucks currency cached: name='{def.name}' PersistenceId={def.PersistenceId}" +
                (_newbucksGoalAmount >= 0 ? $", goal target={_newbucksGoalAmount:N0}" : ""));
        }
    }

    // -------------------------------------------------------------------------
    // Completion
    // -------------------------------------------------------------------------

    /// <summary>
    /// Marks the goal achieved, persists it and reports it if connected. Idempotent. The debug
    /// force-goal button persists it too.
    /// </summary>
    public static void NotifyGoalComplete()
    {
        if (IsGoalComplete) return;
        _goalAchieved = true;
        Plugin.Instance.SaveManager.MarkGoalReached();

        if (Plugin.Instance.ApClient.IsConnected)
            Logger.Info("[AP] Goal complete!");
        else
            Logger.Info("[AP] Goal complete (offline) — it will be reported to the server on the next connection.");
        Plugin.Instance.ApClient.SetGoalComplete();
    }

#if DEBUG
    // -------------------------------------------------------------------------
    // Debug helpers (Debug builds only)
    // -------------------------------------------------------------------------

    /// <summary>Raises <c>NewbucksEarned</c> to the target, for Force Check.</summary>
    public static void DebugSetLifetimeNewbucksToGoal()
    {
        var saveManager = Plugin.Instance.SaveManager;
        if (!saveManager.HasActiveSession)
        {
            Logger.Warning("[AP-Debug] No active AP session — load a save first");
            return;
        }

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

    /// <summary>Checks the polled goal now, skipping the Tick interval.</summary>
    public static void DebugForceCheck()
    {
        var goal = Plugin.Instance.ApClient.SlotData?.Goal ?? "";
        switch (goal)
        {
            case "newbucks":     CheckNewbucksGoal();     break;
            case "slimepedia":   CheckSlimepediaGoal();   break;
            case "plort_seller": CheckPlortSellerGoal();  break;
            default:
                Logger.Info($"[AP-Debug] DebugForceCheck: goal '{goal}' is event-based, use Sim buttons");
                break;
        }
    }

    /// <summary>Fills every in-scope plort counter to the target, for Force Check.</summary>
    public static void DebugSimPlortSales()
    {
        var saveManager = Plugin.Instance.SaveManager;
        if (!saveManager.HasActiveSession)
        {
            Logger.Warning("[AP-Debug] No active AP session — load a save first");
            return;
        }

        int target = Plugin.Instance.ApClient.SlotData?.PlortGoalAmount ?? 0;
        foreach (var plortName in PlortSellerScope())
        {
            long sold = saveManager.PlortsSold(plortName);
            if (sold < target)
                saveManager.AccumulatePlortSold(plortName, (int)(target - sold));
        }
        Logger.Info($"[AP-Debug] All in-scope plort sale counters set to {target}");
    }

    /// <summary>Simulates the Prismacore stabilizing.</summary>
    public static void DebugSimPrismacore() =>
        OnCoreRoomStateChanged(CoreRoomController.CoreRoomState.POST_FIGHT);

    /// <summary>Simulates both Labyrinth gates opening (labyrinth_open goal only).</summary>
    public static void DebugSimLabyrinth()
    {
        Logger.Info("[AP-Debug] Simulating both Labyrinth gate opens");

        {
            var key = LabyrinthSwitchStrand;
            var idx = key.IndexOf(':');
            if (idx >= 0) OnSwitchOpened(key[(idx + 1)..], key[..idx]);
        }

        {
            var key = LabyrinthSwitchValley;
            var idx = key.IndexOf(':');
            if (idx >= 0) OnSwitchOpened(key[(idx + 1)..], key[..idx]);
        }
    }
#endif
}
