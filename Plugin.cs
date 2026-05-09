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
    public ApUpdateBehaviour(IntPtr handle) : base(handle) { }

    private void Update()
    {
#if DEBUG
        SlimeRancher2AP.Utils.DebugTrace.Once("Update.1 — first frame (post-reset or ever)");
        if (Plugin.Instance?.ApClient?.IsConnected == true)
            SlimeRancher2AP.Utils.DebugTrace.Once("Update.2 — first frame while AP connected");
#endif
        Plugin.Instance?.ApClient?.ProcessItemQueue();
#if DEBUG
        SlimeRancher2AP.Utils.DebugTrace.Once("Update.3 — after ProcessItemQueue");
#endif
        Plugin.Instance?.ApClient?.DeathLink?.ProcessDeathQueue();
#if DEBUG
        SlimeRancher2AP.Utils.DebugTrace.Once("Update.4 — after ProcessDeathQueue");
#endif
        TrapHandler.Tick();
        GateReturnEnforcer.Tick();
        SlimeRancher2AP.Patches.PlayerPatches.WeatherPatch.TryApplyIfNeeded();
        SlimeRancher2AP.Patches.LocationPatches.RadiantSlimeSpawnRatePatch.TryApplyIfNeeded();
        SlimeRancher2AP.Patches.LocationPatches.GoldLuckySpawnRatePatch.TryApplyIfNeeded();
#if DEBUG
        SlimeRancher2AP.Utils.DebugTrace.Once("Update.5 — after TrapHandler.Tick");
#endif
        // Track which zones the player visits so the teleport trap can infer region accessibility
        // even when gates were opened in a previous session or via gadget teleporters.
        try
        {
            var player = SceneContext.Instance?.Player;
            if (player != null)
            {
                var tp = player.GetComponent<TeleportablePlayer>();
                TrapHandler.TrackCurrentZone(tp?.SceneGroup?.ReferenceId);
            }
        }
        catch { /* SceneContext not ready */ }
        GoalHandler.Tick();
#if DEBUG
        SlimeRancher2AP.Utils.DebugTrace.Once("Update.6 — after GoalHandler.Tick");
        SlimeRancher2AP.Utils.NoClipManager.Tick();
#endif
    }
}
