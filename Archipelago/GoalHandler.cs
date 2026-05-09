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
///   <item><term>prismacore_enter</term><description>
///     Event-based: CoreRoomController.UpdateState Postfix fires OnCoreRoomStateChanged(PRE_FIGHT).
///   </description></item>
///   <item><term>prismacore_stabilize</term><description>
///     Event-based: CoreRoomController.UpdateState Postfix fires OnCoreRoomStateChanged(POST_FIGHT).
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

    // Newbucks goal caches
    private static int                _newbucksGoalAmount     = -1;
    private static CurrencyDefinition? _newbucksDef           = null;

    /// <summary>
    /// PersistenceId of the Newbucks CurrencyDefinition, or -1 if not yet cached.
    /// Used by <c>PlayerStateAddCurrencyPatch</c> to filter for newbucks-only AddCurrency calls.
    /// </summary>
    internal static int NewbucksPersistenceId => _newbucksDef?.PersistenceId ?? -1;

    // Slimepedia goal: category names to search for.
    // Asset names confirmed via DumpPedia() — Category 'Slimes' (29 entries), Category 'Resources' (54 entries).
    private const string SlimesCategoryName     = "Slimes";
    private const string ResourcesCategoryName  = "Resources";

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
        Patches.PlayerPatches.AccessDoorOpenPatch.Reset();

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
#if DEBUG
        SlimeRancher2AP.Utils.DebugTrace.Once("GoalHandler.Tick — past throttle (first check)");
#endif

        var goal = Plugin.Instance.ApClient.SlotData?.Goal;
#if DEBUG
        SlimeRancher2AP.Utils.DebugTrace.Once($"GoalHandler.Tick — goal='{goal}'");
#endif
        switch (goal)
        {
            case "newbucks":    CheckNewbucksGoal();    break;
            case "slimepedia":  CheckSlimepediaGoal();  break;
        }
#if DEBUG
        SlimeRancher2AP.Utils.DebugTrace.Once("GoalHandler.Tick — goal check complete");
#endif
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
    /// Handles both prismacore_enter (PRE_FIGHT) and prismacore_stabilize (POST_FIGHT).
    /// </summary>
    public static void OnCoreRoomStateChanged(CoreRoomController.CoreRoomState state)
    {
        if (!Plugin.Instance.ApClient.IsConnected || _goalAchieved) return;

        var goal = Plugin.Instance.ApClient.SlotData?.Goal;

        // prismacore_enter: PRE_FIGHT is set when the player first enters the room.
        if (goal == "prismacore_enter" && state == CoreRoomController.CoreRoomState.PRE_FIGHT)
        {
            Logger.Info("[AP] Prismacore entered (PRE_FIGHT state)");
            NotifyGoalComplete();
        }

        // prismacore_stabilize: POST_FIGHT is set after the core is stabilized.
        if (goal == "prismacore_stabilize" && state == CoreRoomController.CoreRoomState.POST_FIGHT)
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

    /// <summary>
    /// Checks whether BOTH the Slimes category and the Resources category are fully unlocked.
    /// Goal fires only when both AllUnlocked() calls return true.
    /// </summary>
    // PediaEntry.name for the Tarr slimepedia entry — excluded from the goal check when
    // DisableTarr is on, since it is removed from the AP location pool in that mode.
    private const string TarrPediaEntryName = "Tarr";

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

        bool disableTarr = Plugin.Instance.ApClient.SlotData?.DisableTarr ?? false;

        bool slimesUnlocked    = false;
        bool resourcesUnlocked = false;

        for (int i = 0; i < categories.Count; i++)
        {
            var cat = categories[i];
            var name = cat?._category?.name;

            if (name == SlimesCategoryName)
            {
                // When DisableTarr is on, Tarr's slimepedia location is not in the AP pool.
                // The player may never unlock it, so check each entry individually and skip Tarr.
                if (disableTarr)
                    slimesUnlocked = IsCategoryUnlockedExcluding(pedia, cat!, TarrPediaEntryName);
                else
                    slimesUnlocked = cat!.AllUnlocked();
            }
            else if (name == ResourcesCategoryName)
            {
                resourcesUnlocked = cat!.AllUnlocked();
            }

            if (slimesUnlocked && resourcesUnlocked) break;
        }
#if DEBUG
        SlimeRancher2AP.Utils.DebugTrace.Once(
            $"CheckSlimepediaGoal — slimes={slimesUnlocked} resources={resourcesUnlocked} disableTarr={disableTarr}");
#endif

        if (slimesUnlocked && resourcesUnlocked)
        {
            Logger.Info(
                "[AP] Slimepedia goal met: all Slimes and Resources entries unlocked");
            NotifyGoalComplete();
        }
    }

    /// <summary>
    /// Returns true if every entry in <paramref name="cat"/> is unlocked, ignoring any entry
    /// whose <c>PediaEntry.name</c> matches <paramref name="excludeEntryName"/>.
    /// Used so that the slimepedia goal still fires when Tarr is excluded from the AP pool.
    /// </summary>
    private static bool IsCategoryUnlockedExcluding(PediaDirector pedia,
                                                     PediaRuntimeCategory cat,
                                                     string excludeEntryName)
    {
        var items = cat._items;
        if (items == null) return false;
        for (int i = 0; i < items.Count; i++)
        {
            var entry = items[i];
            if (entry == null) continue;
            if (entry.name == excludeEntryName) continue;   // skip excluded entry
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

    /// <summary>Simulates the Prismacore room state changing. Calls OnCoreRoomStateChanged directly.</summary>
    public static void DebugSimPrismacore(bool stabilize) =>
        OnCoreRoomStateChanged(stabilize
            ? CoreRoomController.CoreRoomState.POST_FIGHT
            : CoreRoomController.CoreRoomState.PRE_FIGHT);

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
