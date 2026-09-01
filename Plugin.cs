using BepInEx;
using BepInEx.Configuration;
using BepInEx.Unity.IL2CPP;
using HarmonyLib;
using Il2CppInterop.Runtime.Injection;
using SlimeRancher2AP.Archipelago;
using SlimeRancher2AP.SaveData;
using SlimeRancher2AP.UI;
using UnityEngine;

namespace SlimeRancher2AP;

[BepInPlugin(PluginInfo.GUID, PluginInfo.NAME, PluginInfo.VERSION)]
public class Plugin : BasePlugin
{
    public static Plugin Instance { get; private set; } = null!;

    public ArchipelagoClient ApClient    { get; private set; } = null!;
    public ApSaveManager     SaveManager { get; private set; } = null!;
    public ConnectionUI?     ConnectionUi { get; private set; }

    /// <summary>
    /// When false, all AP logic is bypassed and the game runs as vanilla SR2.
    /// Persisted to the BepInEx config file so it survives restarts.
    /// </summary>
    public bool ModEnabled { get; private set; }

    private ConfigEntry<bool>? _modEnabledEntry;

    public override void Load()
    {
        Instance = this;

        Logger.Info($"{PluginInfo.NAME} v{PluginInfo.VERSION} loading...");

        _modEnabledEntry = Config.Bind("Mod", "Enabled", true,
            "Set to false to disable all Archipelago logic and play vanilla SR2.");
        ModEnabled = _modEnabledEntry.Value;

        // Frame-cost profiling. Off by default so shipping builds pay nothing; set to a
        // positive number of seconds to have the per-Tick breakdown written to this log on
        // that interval. The debug panel's F9 button only exists in Debug builds, so this
        // config entry is the only way to profile a Release build.
        var perfEntry = Config.Bind("Diagnostics", "PerfLogSeconds", 0f,
            "Seconds between mod frame-cost reports in the log. 0 disables profiling entirely.");
        Utils.ModProfiler.LogIntervalSeconds = perfEntry.Value;
        Utils.ModProfiler.Enabled            = perfEntry.Value > 0f;
#if DEBUG
        Utils.ModProfiler.Enabled = true; // debug panel exposes the report button
#endif
        if (perfEntry.Value > 0f)
            Logger.Info($"[AP-Perf] Frame-cost profiling ON — reporting every {perfEntry.Value}s.");

        // Register IL2CPP MonoBehaviour types before use
        ClassInjector.RegisterTypeInIl2Cpp<ApUpdateBehaviour>();
        ClassInjector.RegisterTypeInIl2Cpp<ConnectionUI>();
        ClassInjector.RegisterTypeInIl2Cpp<StatusHUD>();
#if DEBUG
        ClassInjector.RegisterTypeInIl2Cpp<SlimeRancher2AP.UI.DebugPanel>();
#endif

        SaveManager  = new ApSaveManager(Config);
        ApClient     = new ArchipelagoClient();
        ConnectionUi = AddComponent<ConnectionUI>();
        AddComponent<StatusHUD>();
        AddComponent<ApUpdateBehaviour>();
#if DEBUG
        AddComponent<SlimeRancher2AP.UI.DebugPanel>();
#endif

        // Apply all Harmony patches discovered by attribute scan
        new Harmony(PluginInfo.GUID).PatchAll(typeof(Plugin).Assembly);

        Logger.Info($"All patches applied. Mod is {(ModEnabled ? "ENABLED" : "DISABLED — vanilla mode")}. Awaiting Archipelago connection.");
    }

    // -------------------------------------------------------------------------
    // Mod toggle
    // -------------------------------------------------------------------------

    /// <summary>
    /// Toggle or set the mod-enabled flag and persist the new value to config.
    /// Call from the main thread only (UI toggle button).
    /// </summary>
    public void SetModEnabled(bool value)
    {
        ModEnabled = value;
        if (_modEnabledEntry != null)
            _modEnabledEntry.Value = value;
        Config.Save();
        Logger.Info($"[AP] Mod {(value ? "ENABLED" : "DISABLED — vanilla mode")}");
    }
}

/// <summary>
/// MonoBehaviour injected into the BepInEx game object to provide a Unity Update loop.
/// Required for IL2CPP plugins because BasePlugin has no Update method.
/// </summary>
public class ApUpdateBehaviour : MonoBehaviour
{
    private static class Prof
    {
        public static void BeginFrame() => SlimeRancher2AP.Utils.ModProfiler.BeginFrame();
        public static void Time(string name, Action body) => SlimeRancher2AP.Utils.ModProfiler.Time(name, body);
    }

    public ApUpdateBehaviour(IntPtr handle) : base(handle) { }

    // Ghost drone spawn retry state.
    // SR2 loads zone sub-scenes additively and they finish streaming in several seconds
    // after SceneContext.Player becomes available. We retry every 3 seconds for up to 60
    // seconds after each scene change so we catch spawners in late-loading sub-scenes.
    private string _lastScene          = "";
    private float  _droneSpawnDeadline = 0f;
    private float  _droneSpawnNextTry  = 0f;
    private float  _nextSceneCheck     = 0f;

    // Cached player + its TeleportablePlayer so the zone read below is not a per-frame
    // GetComponent across the IL2CPP boundary. Invalidated whenever the player changes.
    private UnityEngine.GameObject?   _cachedPlayer;
    private TeleportablePlayer?       _cachedTeleportable;

    // Cached Action instances for the profiled Update sections — allocated once, not per frame.
    private static readonly Action _tItemQueue    = () => Plugin.Instance?.ApClient?.ProcessItemQueue();
    private static readonly Action _tDeathQueue   = () => Plugin.Instance?.ApClient?.DeathLink?.ProcessDeathQueue();
    private static readonly Action _tTrap         = TrapHandler.Tick;
    private static readonly Action _tGate         = GateReturnEnforcer.Tick;
    private static readonly Action _tPlortDoor    = PlortDoorPoller.Tick;
    private static readonly Action _tAccessDoor   = SlimeRancher2AP.Patches.LocationPatches.AccessDoorUITextPatch.Tick;
    private static readonly Action _tConvTracker  = SlimeRancher2AP.Patches.LocationPatches.ConversationActiveTrackerPatch.Tick;
    private static readonly Action _tConvCallout  = SlimeRancher2AP.Patches.LocationPatches.ConversationCalloutOverridePatch.Tick;
    private static readonly Action _tShopUi       = SlimeRancher2AP.Patches.UiPatches.ShopUiHelper.Tick;
    private static readonly Action _tRanchPlot    = SlimeRancher2AP.Archipelago.RanchPlotHandler.Tick;
    private static readonly Action _tPauseMenu    = SlimeRancher2AP.UI.PauseMenuGoalDisplay.Tick;
    private static readonly Action _tExpansion    = SlimeRancher2AP.Archipelago.ItemHandler.TickExpansionDoors;
    private static readonly Action _tRegionTp     = SlimeRancher2AP.Archipelago.ItemHandler.TickRegionTeleporters;
    private static readonly Action _tDroneModules = SlimeRancher2AP.Archipelago.ItemHandler.TickHeldDroneModules;
    private static readonly Action _tWeather      = SlimeRancher2AP.Patches.PlayerPatches.WeatherPatch.TryApplyIfNeeded;
    private static readonly Action _tRadiant      = SlimeRancher2AP.Patches.LocationPatches.RadiantSlimeSpawnRatePatch.TryApplyIfNeeded;
    private static readonly Action _tGoldLucky    = SlimeRancher2AP.Patches.LocationPatches.GoldLuckySpawnRatePatch.TryApplyIfNeeded;
    private static readonly Action _tMarketMode   = SlimeRancher2AP.Patches.EconomyPatches.PlortMarketModePatch.TryApplyIfNeeded;
    private static readonly Action _tDronePoll    = SlimeRancher2AP.Patches.LocationPatches.ResearchDronePatch.Tick;
    private static readonly Action _tNotifier     = SlimeRancher2AP.UI.ItemNotifier.Tick;
    private static readonly Action _tPopup        = SlimeRancher2AP.UI.ApPopup.Tick;
    private static readonly Action _tPrismacore   = SlimeRancher2AP.Archipelago.PrismacoreFulfiller.Tick;

    private void Update()
    {
#if DEBUG
        SlimeRancher2AP.Utils.DebugTrace.Once("Update.1 — first frame (post-reset or ever)");
        if (Plugin.Instance?.ApClient?.IsConnected == true)
            SlimeRancher2AP.Utils.DebugTrace.Once("Update.2 — first frame while AP connected");
#endif
        SlimeRancher2AP.Utils.ModProfiler.Time("ProcessItemQueue", _tItemQueue);
#if DEBUG
        SlimeRancher2AP.Utils.DebugTrace.Once("Update.3 — after ProcessItemQueue");
#endif
        SlimeRancher2AP.Utils.ModProfiler.Time("ProcessDeathQueue", _tDeathQueue);
#if DEBUG
        SlimeRancher2AP.Utils.DebugTrace.Once("Update.4 — after ProcessDeathQueue");
#endif
        // Each Tick is timed under its own name so a frame-cost regression can be attributed to
        // the exact poll responsible. The delegates are cached statics — building them inline
        // would allocate a closure per section per frame and pollute what we are measuring.
        Prof.BeginFrame();
        Prof.Time("TrapHandler",          _tTrap);
        Prof.Time("GateReturnEnforcer",   _tGate);
        Prof.Time("PlortDoorPoller",      _tPlortDoor);
        Prof.Time("AccessDoorUIText",     _tAccessDoor);
        Prof.Time("ConversationTracker",  _tConvTracker);
        Prof.Time("ConversationCallout",  _tConvCallout);
        Prof.Time("ShopUiHelper",         _tShopUi);
        Prof.Time("RanchPlotHandler",     _tRanchPlot);
        Prof.Time("PauseMenuGoalDisplay", _tPauseMenu);
        Prof.Time("ExpansionDoors",       _tExpansion);
        Prof.Time("RegionTeleporters",    _tRegionTp);
        Prof.Time("HeldDroneModules",     _tDroneModules);
        Prof.Time("WeatherPatch",         _tWeather);
        Prof.Time("RadiantSpawnRate",     _tRadiant);
        Prof.Time("GoldLuckySpawnRate",   _tGoldLucky);
        Prof.Time("PlortMarketMode",      _tMarketMode);
        Prof.Time("ResearchDronePoll",    _tDronePoll);
        Prof.Time("ItemNotifier",         _tNotifier);
        Prof.Time("ApPopup",              _tPopup);
        Prof.Time("PrismacoreFulfiller",  _tPrismacore);
#if DEBUG
        SlimeRancher2AP.Utils.DebugTrace.Once("Update.5 — after TrapHandler.Tick");
#endif
        // Track which zones the player visits so the teleport trap can infer region accessibility
        // even when gates were opened in a previous session or via gadget teleporters.
        //
        // The TeleportablePlayer component is cached against the player GameObject: GetComponent
        // crosses the IL2CPP boundary and was being called every single frame purely to read a
        // zone id that changes a handful of times per session.
        try
        {
            var player = SceneContext.Instance?.Player;
            if (player == null)
            {
                _cachedTeleportable = null;
                _cachedPlayer       = null;
            }
            else
            {
                if (_cachedTeleportable == null || _cachedPlayer != player)
                {
                    _cachedPlayer       = player;
                    _cachedTeleportable = player.GetComponent<TeleportablePlayer>();
                }
                TrapHandler.TrackCurrentZone(_cachedTeleportable?.SceneGroup?.ReferenceId);
            }
        }
        catch { _cachedTeleportable = null; _cachedPlayer = null; /* SceneContext not ready */ }
        GoalHandler.Tick();
#if DEBUG
        SlimeRancher2AP.Utils.DebugTrace.Once("Update.6 — after GoalHandler.Tick");
        SlimeRancher2AP.Utils.NoClipManager.Tick();
#endif

        // Ghost drone spawner fix — retry every 3s for 60s after each scene change.
        // Sub-scenes finish streaming in well after SceneContext.Player is available,
        // so a one-shot trigger misses spawners that load a few seconds later.
        // Scene-change detection, sampled 5×/second rather than every frame: GetActiveScene().name
        // marshals a fresh string across the IL2CPP boundary on every call, and a scene change is
        // never so urgent that a 200 ms detection delay matters here.
        float nowT = UnityEngine.Time.unscaledTime;
        if (nowT >= _nextSceneCheck)
        {
            _nextSceneCheck = nowT + 0.2f;
            var sceneName = UnityEngine.SceneManagement.SceneManager.GetActiveScene().name;
            if (sceneName != _lastScene)
            {
                _lastScene          = sceneName;
                _droneSpawnDeadline = UnityEngine.Time.time + 60f;
                _droneSpawnNextTry  = 0f;
                SlimeRancher2AP.Patches.LocationPatches.ComponentAcqDroneSpawnerFix.ClearSpawnedSet();
                SlimeRancher2AP.UI.PauseMenuGoalDisplay.Reset();
                SlimeRancher2AP.UI.ItemNotifier.Reset();
                SlimeRancher2AP.UI.ApPopup.Reset();
                SlimeRancher2AP.Archipelago.PrismacoreFulfiller.Rearm();
                // Decode the AP logo here rather than on the first popup — see Prewarm().
                SlimeRancher2AP.UI.ApPopup.Prewarm();
            }
        }
        if (UnityEngine.Time.time < _droneSpawnDeadline
            && UnityEngine.Time.time >= _droneSpawnNextTry
            && SceneContext.Instance?.Player != null)
        {
            SlimeRancher2AP.Patches.LocationPatches.ComponentAcqDroneSpawnerFix.ForceSpawnAll();
            _droneSpawnNextTry = UnityEngine.Time.time + 3f;
        }
    }
}
