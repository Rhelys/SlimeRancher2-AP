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
///     Event-based: both Grey Labyrinth beam gates must open, reported through OnSwitchOpened()
///     by InvisibleSwitchPatch (EnergyBeamReceiver "energyBeamReceiver" →
///     WorldStateInvisibleSwitch). Keys: "zoneStrandLabyrinthGate:energyBeamReceiver" and
///     "zoneGorgeGateTransfer:energyBeamReceiver". Opened gates are persisted, so the two can
///     open in different sessions.
///   </description></item>
///   <item><term>newbucks</term><description>
///     Polled via Tick() — compares the persisted <c>ApSaveManager.NewbucksEarned</c> counter
///     (accumulated by NewbucksEarnings in PlayerStateCurrencyPatch.cs, from both PlayerState
///     and Quantum Cloud payouts; the game's own AmountEverCollected is never updated) against
///     slot data "newbucks_goal_amount".
///   </description></item>
///   <item><term>prismacore / prisma_shard_hunt</term><description>
///     Event-based: CoreRoomController.UpdateState Postfix fires OnCoreRoomStateChanged(POST_FIGHT)
///     when the boss fight completes and the Prismacore is stabilized. For prisma_shard_hunt the
///     shards only unlock the fight (PrismacoreGatePatch); winning it is still the goal.
///   </description></item>
///   <item><term>slimepedia</term><description>
///     Polled via Tick() — every entry the AP location table tracks must be unlocked, in each
///     category enabled by the randomize_slimepedia* options, minus entries excluded by seed
///     options. Never the game's AllUnlocked(): see <see cref="IsCategoryUnlockedForAp"/>.
///   </description></item>
///   <item><term>plort_seller</term><description>
///     Polled via Tick() — per-type sold counters (accumulated by PlortMarketPatch via
///     PlortEconomyDirector.RegisterSold, persisted in ApSaveManager) must all reach the
///     per-seed target (slot data "plort_goal_amount"). Scope: all 25 plort types minus
///     RNG/weather exclusions.
///   </description></item>
/// </list>
/// <para>
/// Detection runs whenever an AP save is bound and trusted, connected or not. Reaching the goal
/// is persisted (<c>ApSaveManager.GoalReached</c>) and reported to the server immediately when
/// connected, otherwise by <see cref="Initialize"/> on the next connect — so a goal met offline,
/// including the one-shot Prismacore event, is never lost.
/// </para>
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
    // Detected via InvisibleSwitchPatch → OnSwitchOpened. Opened keys are persisted in
    // ApSaveManager (LabyrinthGatesOpened), not held here.
    //
    // NOTE: The old LabyrinthSwitchStrand = "zoneStrand_Area4:ruinSwitch" was incorrect —
    //   "ruinSwitch" is a different WorldStatePrimarySwitch in the zone, not the labyrinth gate.
    private const string LabyrinthSwitchStrand = "zoneStrandLabyrinthGate:energyBeamReceiver";  // confirmed
    private const string LabyrinthSwitchValley = "zoneGorgeGateTransfer:energyBeamReceiver";   // confirmed

    // -------------------------------------------------------------------------
    // Polling throttle (~60-frame cadence ≈ once per second)
    // -------------------------------------------------------------------------

    private static int _tickCounter = 0;
    private const int TickInterval  = 60;

    // -------------------------------------------------------------------------
    // State
    // -------------------------------------------------------------------------

    private static bool _goalAchieved = false;

    /// <summary>
    /// True once the goal has been met — this session, or on any earlier one for the bound save
    /// (persisted), so goal-gated behaviour such as trap and popup suppression survives reloads.
    /// </summary>
    public static bool IsGoalComplete => _goalAchieved || Plugin.Instance.SaveManager.GoalReached;

    /// <summary>
    /// True when goal progress should be tracked: an AP save is bound and trusted and its slot
    /// data is known. Deliberately does not require a live connection — see the class remarks.
    /// </summary>
    /// <remarks>
    /// Same trust rule as <c>ArchipelagoClient.SendCheck</c>: a save that is not associated with
    /// this slot must not record the goal. Evaluated last by callers because it reads the
    /// current save name across the IL2CPP boundary.
    /// </remarks>
    private static bool CanTrackGoal()
        => Plugin.Instance.ModEnabled
        && Plugin.Instance.SaveManager.IsSaveBound
        && Plugin.Instance.ApClient.SlotData != null
        && SaveGuard.IsSaveTrusted();

    // Newbucks goal caches
    private static int                _newbucksGoalAmount     = -1;
    private static CurrencyDefinition? _newbucksDef           = null;

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
        Reset();

        // A goal met while offline (or on an earlier connection) is reported now. Sending it
        // again on every connect is harmless — the server treats it as idempotent.
        if (Plugin.Instance.SaveManager.GoalReached)
        {
            Logger.Info("[AP] Goal was reached earlier — reporting it to the server.");
            Plugin.Instance.ApClient.SetGoalComplete();
        }

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

    /// <summary>Clears per-session state. Called by Initialize and on disconnect.</summary>
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

    /// <summary>
    /// Called from RegionGatePatch (WorldStatePrimarySwitch) and InvisibleSwitchPatch
    /// (WorldStateInvisibleSwitch) whenever either type of switch transitions to DOWN.
    /// Key format is "scene:switchName". Safe to call repeatedly — HashSet deduplicates,
    /// so the spam from a continuous beam-pulse receiver is harmless.
    /// </summary>
    public static void OnSwitchOpened(string switchName, string sceneName)
    {
        if (IsGoalComplete) return;
        if (Plugin.Instance.ApClient.SlotData?.Goal != "labyrinth_open") return;

        var key = $"{sceneName}:{switchName}";
        if (key != LabyrinthSwitchStrand && key != LabyrinthSwitchValley) return;

        // The receiver pulses continuously while a beam hits it — skip the trust check (which
        // crosses the IL2CPP boundary) once this gate is already recorded.
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
    /// Called from CoreRoomControllerPatch Postfix on every UpdateState call.
    /// Handles the prismacore goal: fires when POST_FIGHT is set (boss fight complete, core stabilized).
    /// PRE_FIGHT fires on scene load and is intentionally ignored — it does NOT indicate room entry.
    /// </summary>
    public static void OnCoreRoomStateChanged(CoreRoomController.CoreRoomState state)
    {
        if (IsGoalComplete) return;
        if (state != CoreRoomController.CoreRoomState.POST_FIGHT) return; // PRE_FIGHT etc. — cheap exit before the trust check
        if (!CanTrackGoal()) return;

        var goal = Plugin.Instance.ApClient.SlotData?.Goal;

        // prisma_shard_hunt is the same victory condition — the shards gate ENTRY to the fight,
        // they are not the win themselves. Beating the boss is still what completes the goal.
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

        // Retry cache if it failed during Initialize() (e.g. Resources not loaded yet)
        if (_newbucksDef == null)
            TryCacheNewbucksDef();
        if (_newbucksDef == null) return;

        // Read our own persisted counter — PlayerModel.AmountEverCollected is never updated
        // by any SR2 code path (plort selling, etc.) and cannot be relied upon.
        // NewbucksEarnings (PlayerStateCurrencyPatch.cs) accumulates every in-game payout into
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

        // The "Radiant Slimes" concept entry lives in the Slimes category but only unlocks
        // after encountering a radiant slime, so it follows the radiant toggle — the same
        // option that governs the 22 individual radiant entries.
        if (!needRadiant)
            slimesExcluded.Add("RadiantSlime");

        // Post-game Sanctuary content is opt-in; without it, Sprinkles is not a check and
        // must not be required by the goal.
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
    /// The plort types counted by the plort_seller goal: all 25 sellable types (the
    /// 5 Grey Labyrinth plorts are in scope — GL locations are in the pool for this
    /// goal), minus the RNG / weather exclusions when those options are on. Mirrors
    /// the Plort Market check scope in PlortMarketPatch and the apworld's Goal
    /// docstring.
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
    /// Returns true when every Slimepedia entry AP has a location for in this category is
    /// unlocked, skipping any whose <c>PediaEntry.name</c> is in <paramref name="excludeNames"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Deliberately does NOT use the native <c>PediaRuntimeCategory.AllUnlocked()</c>, which
    /// this method previously called as a fast path when no exclusions were active. Two
    /// problems with that:
    /// </para>
    /// <list type="number">
    /// <item>It made the goal's meaning depend on an unrelated option — with any exclusion
    ///   enabled the mod iterated entries itself, without one it deferred to the game, and
    ///   the two do not necessarily agree.</item>
    /// <item>Either way the goal required every entry the GAME puts in the category, which
    ///   is a superset of what AP has locations for. Confirmed from docs/dumps/1.2/Pedia.txt:
    ///   'Resources' contains <c>Sprinkles</c> (post-game Sanctuary content) and 'Slimes'
    ///   contains the <c>RadiantSlime</c> concept entry — neither is an AP location, so a
    ///   player could complete every check in their seed and still not trigger the goal
    ///   (player-reported 2026-07-24). Categories can also grow at runtime via
    ///   <c>PediaDirector.OnPediaEntriesRegistered</c> → <c>AddDynamicItem</c>, so the
    ///   game-side contents are not even fixed for a given version.</item>
    /// </list>
    /// <para>
    /// The AP location table is therefore the authority: the goal fires when the seed's own
    /// Slimepedia checks are all unlocked, which is what "complete the Slimepedia" means to
    /// the player looking at their tracker.
    /// </para>
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
            if (!required.Contains(name)) continue;   // not an AP location (e.g. Sprinkles)
            if (excludeNames.Contains(name)) continue; // excluded by seed options
            if (!pedia.IsUnlocked(entry)) return false;
        }
        return true;
    }

    /// <summary>
    /// Logs, per enabled Slimepedia category, which AP-tracked entries are still locked —
    /// and which entries the game lists that AP does not track. This is the answer to
    /// "I've done every Slimepedia check, why hasn't my goal fired?".
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
            // Goal target is only meaningful on the newbucks goal; do not imply one that is not set.
            Logger.Info(
                $"[AP] Newbucks currency cached: name='{def.name}' PersistenceId={def.PersistenceId}" +
                (_newbucksGoalAmount >= 0 ? $", goal target={_newbucksGoalAmount:N0}" : ""));
        }
    }

    // -------------------------------------------------------------------------
    // Completion
    // -------------------------------------------------------------------------

    /// <summary>
    /// Marks the goal as achieved, persists it, and notifies the AP server if connected (otherwise
    /// <see cref="Initialize"/> reports it on the next connect). Idempotent — safe to call multiple times.
    /// </summary>
    /// <remarks>
    /// Also reached from the debug panel's force-goal button, which therefore persists the goal
    /// for the bound save just like a real completion.
    /// </remarks>
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

    /// <summary>
    /// Raises the persisted <c>NewbucksEarned</c> counter to the goal target so the next Force
    /// Check will trigger.
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
            case "newbucks":     CheckNewbucksGoal();     break;
            case "slimepedia":   CheckSlimepediaGoal();   break;
            case "plort_seller": CheckPlortSellerGoal();  break;
            default:
                Logger.Info($"[AP-Debug] DebugForceCheck: goal '{goal}' is event-based, use Sim buttons");
                break;
        }
    }

    /// <summary>
    /// Fills every in-scope plort type's sold counter to the goal target so the next
    /// Force Check fires the plort_seller goal.
    /// </summary>
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

        // Strand beam gate
        {
            var key = LabyrinthSwitchStrand;
            var idx = key.IndexOf(':');
            if (idx >= 0) OnSwitchOpened(key[(idx + 1)..], key[..idx]);
        }

        // Valley beam gate
        {
            var key = LabyrinthSwitchValley;
            var idx = key.IndexOf(':');
            if (idx >= 0) OnSwitchOpened(key[(idx + 1)..], key[..idx]);
        }
    }
#endif
}
