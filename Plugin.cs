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
    /// When false, all AP logic is bypassed (vanilla SR2). Read once at startup from config —
    /// there is no in-game toggle: flipping it inside a save discarded checks while items kept
    /// arriving.
    /// </summary>
    public bool ModEnabled { get; private set; }

    public override void Load()
    {
        Instance = this;

        Logger.Info($"{PluginInfo.NAME} v{PluginInfo.VERSION} loading...");

        ModEnabled = Config.Bind("Mod", "Enabled", true,
            "Set to false to disable all Archipelago logic and play vanilla SR2. Restart to apply.").Value;

        // Frame-cost profiling: seconds between per-Tick reports, 0 = off. The only way to
        // profile a Release build (F9 is Debug-only).
        var perfEntry = Config.Bind("Diagnostics", "PerfLogSeconds", 0f,
            "Seconds between mod frame-cost reports in the log. 0 disables profiling entirely.");
        Utils.ModProfiler.LogIntervalSeconds = perfEntry.Value;
        Utils.ModProfiler.Enabled            = perfEntry.Value > 0f;
#if DEBUG
        Utils.ModProfiler.Enabled = true; // debug panel exposes the report button
#endif
        if (perfEntry.Value > 0f)
            Logger.Info($"[AP-Perf] Frame-cost profiling ON — reporting every {perfEntry.Value}s.");

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

        new Harmony(PluginInfo.GUID).PatchAll(typeof(Plugin).Assembly);

        Logger.Info($"All patches applied. Mod is {(ModEnabled ? "ENABLED" : "DISABLED — vanilla mode")}. Awaiting Archipelago connection.");
    }
}

/// <summary>The mod's Update loop — BasePlugin has none in IL2CPP.</summary>
public class ApUpdateBehaviour : MonoBehaviour
{
    private static class Prof
    {
        public static void BeginFrame() => SlimeRancher2AP.Utils.ModProfiler.BeginFrame();
        public static void Time(string name, Action body) => SlimeRancher2AP.Utils.ModProfiler.Time(name, body);
    }

    public ApUpdateBehaviour(IntPtr handle) : base(handle) { }

    // Ghost drone spawn retries: every 3 s for 60 s after each scene change, since sub-scenes
    // stream in well after the Player exists.
    private string _lastScene          = "";
    private float  _droneSpawnDeadline = 0f;
    private float  _droneSpawnNextTry  = 0f;
    private float  _nextSceneCheck     = 0f;

    // Cached, so the per-frame zone read isn't a GetComponent across IL2CPP.
    private UnityEngine.GameObject?   _cachedPlayer;
    private TeleportablePlayer?       _cachedTeleportable;

    // Cached delegates for the profiled sections — no per-frame allocation.
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
    private static readonly Action _tHarmonizer   = SlimeRancher2AP.Archipelago.HarmonizerHandler.Tick;
    private static readonly Action _tQol          = SlimeRancher2AP.Patches.PlayerPatches.QualityOfLife.Tick;
    private static readonly Action _tSaveFlush    = () => Plugin.Instance?.SaveManager?.Flush();

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
        // Each Tick is timed separately, so a regression points at its poll.
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
        Prof.Time("HarmonizerHandler",    _tHarmonizer);
        Prof.Time("QualityOfLife",        _tQol);
#if DEBUG
        SlimeRancher2AP.Utils.DebugTrace.Once("Update.5 — after TrapHandler.Tick");
#endif
        // Zone tracking (teleport trap pool, gate enforcement).
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

        // Scene-change detection, 5×/second — GetActiveScene().name marshals a new string per call.
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
                // The upgrade model is repopulated by Push on the next save load.
                SlimeRancher2AP.Archipelago.ItemHandler.ResetUpgradeRestoreState();
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

        // Last: one save for everything this frame changed.
        Prof.Time("SaveFlush", _tSaveFlush);
    }

    // Per-frame flushing leaves at most one frame unwritten; this closes that gap on exit.
    private void OnApplicationQuit() => Plugin.Instance?.SaveManager?.Flush();
}
