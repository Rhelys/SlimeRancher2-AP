using Archipelago.MultiClient.Net;
using Archipelago.MultiClient.Net.BounceFeatures.DeathLink;
using Archipelago.MultiClient.Net.Enums;
using Archipelago.MultiClient.Net.Helpers;
using Archipelago.MultiClient.Net.Models;
using Archipelago.MultiClient.Net.Packets;
using SlimeRancher2AP.Patches.LocationPatches;
using SlimeRancher2AP.Patches.PlayerPatches;
using SlimeRancher2AP.SaveData;
using SlimeRancher2AP.UI;
using System.Linq;
using System.Threading.Tasks;

namespace SlimeRancher2AP.Archipelago;

/// <summary>
/// The Archipelago session: connect, disconnect, item queue, location checks. Network work runs
/// off-thread; items are applied on the main thread by ProcessItemQueue().
/// </summary>
public class ArchipelagoClient
{
    public ArchipelagoSession? Session    { get; private set; }
    public SlotData?           SlotData   { get; private set; }

    /// <summary>Restores persisted slot data for an offline session. No-op while connected.</summary>
    public void RestorePersistedSlotData()
    {
        // Guard on the connection, not SlotData == null: offline save A then save B would
        // otherwise keep A's options.
        if (IsConnected) return;

        var json = Plugin.Instance.SaveManager.SlotDataJson;
        if (string.IsNullOrEmpty(json)) return;

        try
        {
            var raw = Newtonsoft.Json.JsonConvert
                .DeserializeObject<Dictionary<string, object>>(json);
            if (raw == null) return;

            SlotData = SlotData.Parse(raw);
            Logger.Info(
                $"[AP] Slot data restored from disk for offline play: goal='{SlotData.Goal}' " +
                $"region_access_mode='{SlotData.RegionAccessMode}'");
        }
        catch (Exception ex)
        {
            Logger.Warning($"[AP] Could not restore persisted slot data: {ex.Message}");
        }
    }
    public DeathLinkHandler?   DeathLink  { get; private set; }

    public bool IsConnected => Session?.Socket.Connected ?? false;

    public event Action<string>? OnConnectionFailed;
    public event Action?         OnConnected;
    public event Action?         OnDisconnected;

    // (item, AP position) pairs; the position matches AllItemsReceived and drives dedup.
    private readonly Queue<(ItemInfo item, int index)> _itemQueue = new();
    private readonly object _queueLock = new();
    // Items dequeued from the helper this session; an item's AP position is the value before
    // its dequeue. Not helper.Index — that is the batch total, the same for every item in it.
    private volatile int _receivedCount = 0;

    // AllItemsReceived count at login. A position at or above it is a live item, queued even
    // when the saved watermark is stale.
    private volatile int _snapshotCount = 0;

    // The watermark at connect, after the stale-watermark reset. Threshold for the secondary
    // dedup in ProcessItemQueue; fixed for the session while LastItemIndex advances.
    private volatile int _connectTimeWatermark = -1;

    private Dictionary<long, ScoutedItemInfo>? _liveScoutCache;

    // Work for the main thread from network threads: post-login init and every UI event
    // (their handlers touch Unity). Drained by ProcessItemQueue before its connection guards,
    // so failure and disconnect notices still fire offline.
    private readonly System.Collections.Concurrent.ConcurrentQueue<Action> _mainThreadActions = new();

    // Set on connect: run ValidateAndRepairUpgrades once the handler exists, restoring levels
    // if the save fell behind the watermark (e.g. a crash before autosave).
    private volatile bool _pendingUpgradeValidation;

    /// <summary>Seconds to wait for UpgradeModel.Push before reconciling regardless.</summary>
    private const float UpgradeRestoreGraceSeconds = 10f;

    /// <summary>Deadline for the Push wait; 0 when no validation is pending.</summary>
    private float _upgradeRestoreDeadline;

    /// <summary>Live scout info for a location, or null (offline callers use ApSaveManager.GetScout).</summary>
    public ScoutedItemInfo? GetScoutedItem(long locationId)
        => _liveScoutCache != null && _liveScoutCache.TryGetValue(locationId, out var info) ? info : null;

    // The seed's location IDs, captured on login; null when disconnected.
    private volatile HashSet<long>? _seedLocations;

    /// <summary>
    /// True when the location exists in the connected seed. Optional pools (e.g. the shop
    /// subset) must leave the rest vanilla. False while disconnected.
    /// </summary>
    public bool IsLocationInSeed(long locationId)
        => _seedLocations?.Contains(locationId) ?? false;

    // -------------------------------------------------------------------------
    // Automatic reconnect
    //
    // A socket that drops after a successful login is retried with backoff. The dead session
    // stays in place during the outage, so the save stays bound (checks are recorded offline)
    // and its item history still answers count queries (shards, plots, catalogs). A successful
    // retry swaps in the new session without tearing anything down. Disconnect() — the button,
    // Save and Quit, loading another save — cancels it.
    // -------------------------------------------------------------------------

    /// <summary>Fired on the main thread when a retry is scheduled: (attempt, seconds until it).</summary>
    public event Action<int, float>? OnReconnecting;

    /// <summary>True from a lost connection until it is restored or abandoned. Main thread.</summary>
    public bool IsReconnecting => _reconnectScheduled || _reconnectInFlight;

    // Seconds before each retry; the last value repeats.
    private static readonly float[] ReconnectBackoff = { 2f, 5f, 10f, 20f, 30f };

    // The session whose loss was already handled, so the SocketClosed event and the watchdog
    // (WatchSocket) cannot both act on one drop. Main thread.
    private ArchipelagoSession? _closeHandledFor;

    // The last successful login; null until one succeeds and after Disconnect().
    private volatile ArchipelagoData? _lastLogin;
    private volatile string?          _lastLoginSeed;

    // Main thread only.
    private bool  _reconnectScheduled;
    private float _nextReconnectAt;
    private int   _reconnectAttempts;

    // Set while a retry's login runs off-thread.
    private volatile bool _reconnectInFlight;

    // Outage start, for a single reassurance notice once it has run OutageNoticeAfter seconds —
    // the "connection lost" toast is easy to miss, and retries never give up on their own.
    private float _outageStartedAt;
    private bool  _outageNoticeShown;
    private const float OutageNoticeAfter = 300f;

    // When the in-flight retry started; one still running after AttemptTimeout is abandoned.
    private float _attemptStartedAt;
    private const float AttemptTimeout = 30f;   // ConnectTimeout + LoginTimeout + slack

    // Bumped by Disconnect(). A login that finishes after one belongs to a connection the player
    // already ended, and is abandoned instead of installed.
    private int _generation;
    private readonly object _sessionLock = new();

    // -------------------------------------------------------------------------
    // Connection
    // -------------------------------------------------------------------------

    public void Connect(ArchipelagoData data)
    {
        // Unconditionally, so even the first connect starts from clean state.
        Disconnect();
        StartLogin(data, reconnectSeed: null);
    }

    private static readonly TimeSpan ConnectTimeout = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan LoginTimeout   = TimeSpan.FromSeconds(10);

    /// <summary>
    /// Connects and logs in with bounded waits. Off-thread; never blocks past the timeouts.
    /// Replaces <c>TryConnectAndLogin</c>, which can hang forever: when the library's two 4 s
    /// timers race, a cancelled connect reads as completed and it goes on to <c>LoginAsync</c>,
    /// whose synchronous send waits on a send loop that never started (seen in game: a retry
    /// that never returned, ending all further retries). With <paramref name="expectedSeed"/>,
    /// a server running another seed is refused from its RoomInfo, before logging in.
    /// </summary>
    private static LoginResult ConnectAndLogin(ArchipelagoSession session, ArchipelagoData data,
                                               string? expectedSeed, out bool wrongSeed)
    {
        wrongSeed = false;

        var connect = session.ConnectAsync();
        try
        {
            connect.Wait(ConnectTimeout);
        }
        catch (AggregateException ex)
        {
            return ex.GetBaseException() is OperationCanceledException
                ? new LoginFailure("Connection timed out.")
                : new LoginFailure(ex.GetBaseException().Message);
        }
        // Status, not IsCompleted — a cancelled connect is "completed" too.
        if (connect.Status != TaskStatus.RanToCompletion || !session.Socket.Connected)
            return new LoginFailure("Connection timed out.");

        if (expectedSeed != null &&
            !string.Equals(connect.Result.SeedName, expectedSeed, StringComparison.Ordinal))
        {
            wrongSeed = true;
            return new LoginFailure("The server is running a different seed.");
        }

        // LoginAsync sends synchronously and blocks forever if the socket closes first, so it
        // runs on its own task and the wait is bounded. A hang there costs one pool thread.
        var login = Task.Run(() => session.LoginAsync(
            PluginInfo.GAME,
            data.SlotName,
            ItemsHandlingFlags.AllItems,
            password: string.IsNullOrWhiteSpace(data.Password) ? null : data.Password,
            requestSlotData: true));
        try
        {
            if (!login.Wait(LoginTimeout))
                return new LoginFailure("The server did not answer the login.");
        }
        catch (AggregateException ex)
        {
            return new LoginFailure(ex.GetBaseException().Message);
        }

        // A login answered just before the socket dropped is not a usable session.
        if (login.Result.Successful && !session.Socket.Connected)
            return new LoginFailure("The connection closed during login.");
        return login.Result;
    }

    /// <summary>
    /// Logs in off-thread. With <paramref name="reconnectSeed"/> it is a retry after a lost
    /// connection: on success the new session replaces the dead one in place
    /// (<see cref="InstallReconnectedSession"/>) instead of starting a session from scratch.
    /// </summary>
    private void StartLogin(ArchipelagoData data, string? reconnectSeed)
    {
        int  generation  = _generation;
        bool isReconnect = reconnectSeed != null;

        Task.Run(() =>
        {
            ArchipelagoSession? session = null;
            try
            {
                session = ArchipelagoSessionFactory.CreateSession(data.Uri, data.Port);

                // ItemReceived is registered after login, below.
                session.Socket.ErrorReceived += (ex, msg) =>
                    Logger.Warning($"[AP] Socket error: {msg}");
                var thisSession = session;
                int closeLogged = 0;
                session.Socket.SocketClosed += reason =>
                {
                    // The library can raise this twice for one close; log it once. A session
                    // already retired (Disconnect, replaced) was closed on purpose — not a warning.
                    if (System.Threading.Interlocked.Exchange(ref closeLogged, 1) == 0)
                    {
                        if (ReferenceEquals(thisSession, Session))
                            Logger.Warning($"[AP] Disconnected: {reason}");
                        else
                            Logger.Debug("[AP] Socket closed for a retired session (deliberate close)");
                    }
                    // Fires on the socket thread — handlers touch Unity UI, so defer.
                    _mainThreadActions.Enqueue(() => OnSocketClosed(thisSession));
                };

                var result = ConnectAndLogin(session, data, reconnectSeed, out bool wrongSeed);

                if (wrongSeed)
                {
                    // Checked from RoomInfo, before logging in — the other multiworld never sees us join.
                    Logger.Error(
                        $"[AP] Reconnect: the server is now running seed {session.RoomState.Seed}, " +
                        $"not {reconnectSeed} — not reconnecting to it.");
                    CloseQuietly(session);
                    _mainThreadActions.Enqueue(() => OnReconnectFailed(
                        generation, "the server is running a different seed", retry: false));
                    return;
                }

                if (isReconnect && result is LoginSuccessful)
                {
                    InstallReconnectedSession(session, data, reconnectSeed!, generation);
                }
                else if (result is LoginSuccessful success)
                {
                    lock (_sessionLock)
                    {
                        if (generation != _generation)
                        {
                            Logger.Info("[AP] Login finished after a disconnect — discarded.");
                            CloseQuietly(session);
                            return;
                        }
                        Session = session;
                    }

                    data.Seed = session.RoomState.Seed;
                    data.Team = session.ConnectionInfo.Team;
                    data.Slot = session.ConnectionInfo.Slot;

                    data.SaveSeedToConfig(Plugin.Instance.Config);

                    SlotData = SlotData.Parse(success.SlotData);

                    // Persisted so offline sessions have the options — without them every
                    // option-gated check bails.
                    try
                    {
                        Plugin.Instance.SaveManager.PersistSlotData(
                            Newtonsoft.Json.JsonConvert.SerializeObject(success.SlotData));
                    }
                    catch (Exception ex)
                    {
                        Logger.Warning($"[AP] Could not persist slot data: {ex.Message}");
                    }
                    Logger.Info(
                        $"[AP] SlotData: goal='{SlotData.Goal}' region_access_mode='{SlotData.RegionAccessMode}' " +
                        $"conversation_checks='{SlotData.ConversationChecks}' " +
                        $"weather_freq_mult={SlotData.WeatherFrequencyMultiplier} force_heavy={SlotData.ForceHeavyWeather} " +
                        $"all_radiant={SlotData.AllRadiantSlimes} radiant_mult={SlotData.RadiantSpawnRateMultiplier} " +
                        $"start_harvester={SlotData.StartWithResourceHarvester} " +
                        $"newbucks_mult={SlotData.NewbucksMultiplier}% vac_shoot_rate={SlotData.VacpackShootRate}% " +
                        $"pod_keys='{SlotData.TreasurePodKeys}' drone_key={SlotData.DroneAccessKey}");

                    if (SlotData.DeathLink)
                    {
                        DeathLink = new DeathLinkHandler(session, data.SlotName);
                        DeathLink.Enable();
                    }

                    // Loads LastItemIndex for this seed+slot (file I/O, fine off-thread).
                    Plugin.Instance.SaveManager.OnConnected(data.Seed, data.SlotName);

                    // Queue history above the watermark.
                    var snapshot = session.Items.AllItemsReceived.ToList();
                    int savedWatermark = Plugin.Instance.SaveManager.LastItemIndex;

                    Logger.Info(
                        $"[AP] Reconnect state: savedWatermark={savedWatermark}, " +
                        $"snapshot.Count={snapshot.Count} — " +
                        $"expect {System.Math.Max(0, snapshot.Count - (savedWatermark + 1))} item(s) to replay");

                    // A watermark beyond the server's history is stale (old slot, truncated
                    // history). Replay everything rather than skip everything: non-ephemerals
                    // are idempotent, and a duplicate is better than a missing item.
                    if (savedWatermark >= snapshot.Count)
                    {
                        Logger.Warning(
                            $"[AP] Watermark ({savedWatermark}) exceeds server history ({snapshot.Count} item(s)) — " +
                            $"resetting to -1 and replaying all {snapshot.Count} item(s).");
                        Plugin.Instance.SaveManager.ForceLastItemIndex(-1);
                        savedWatermark = -1;
                    }

                    _connectTimeWatermark = savedWatermark;

                    int enqueued = 0;
                    lock (_queueLock)
                    {
                        for (int i = savedWatermark + 1; i < snapshot.Count; i++)
                        {
                            _itemQueue.Enqueue((snapshot[i], i));
                            enqueued++;
                        }

                        // Re-queue items deferred last session that the watermark has since
                        // passed (a parked trap, a held expansion) — otherwise they are lost.
                        // Indices outside the server history are stale; prune them.
                        var pendingDeferred = Plugin.Instance.SaveManager.GetPendingDeferredItems();
                        int deferredRequeued = 0;
                        foreach (var deferredIdx in pendingDeferred.ToList())
                        {
                            if (deferredIdx < 0 || deferredIdx >= snapshot.Count)
                            {
                                Logger.Warning(
                                    $"[AP] Deferred item idx={deferredIdx} is outside server history " +
                                    $"({snapshot.Count} item(s)) — pruning stale entry.");
                                Plugin.Instance.SaveManager.RemoveDeferredItem(deferredIdx);
                                continue;
                            }
                            if (deferredIdx <= savedWatermark)
                            {
                                _itemQueue.Enqueue((snapshot[deferredIdx], deferredIdx));
                                deferredRequeued++;
                            }
                        }
                        if (deferredRequeued > 0)
                            Logger.Info(
                                $"[AP] Re-queued {deferredRequeued} deferred item(s) from previous session " +
                                $"(below watermark={savedWatermark}, will apply this session).");
                        enqueued += deferredRequeued;
                    }
                    // _receivedCount stays 0, so OnItemReceived's positions match AllItemsReceived.
                    _snapshotCount = snapshot.Count;
                    Logger.Info(
                        $"[AP] Historical replay: {snapshot.Count} item(s) in history, " +
                        $"watermark={savedWatermark}, {enqueued} new item(s) queued.");
                    Logger.Info(
                        $"[AP] Counters frozen: _receivedCount={_receivedCount} (starts at 0), _snapshotCount={_snapshotCount}");

                    Logger.Info("[AP] Registering ItemReceived handler...");
                    session.Items.ItemReceived += OnItemReceived;
                    Logger.Info("[AP] ItemReceived handler registered.");

                    _seedLocations = new HashSet<long>(session.Locations.AllLocations);

                    FlushPendingChecks();
                    _ = ScoutAllLocationsAsync(session);

                    Logger.Info(
                        $"[AP] Connected as '{data.SlotName}' (slot {data.Slot}, seed {data.Seed})");

                    // What a lost connection retries with.
                    _lastLogin     = data;
                    _lastLoginSeed = data.Seed;

                    _pendingUpgradeValidation = true;

                    // These touch Unity — main thread.
                    _mainThreadActions.Enqueue(() =>
                    {
                        GoalHandler.Initialize();
                        WeatherPatch.OnSlotDataReceived(); // applies WeatherFrequencyMultiplier to the persistent WeatherRegistry
                        Patches.EconomyPatches.PlortMarketModePatch.OnSlotDataReceived(); // saturates the market when plort_market_mode is active
                        ItemHandler.ScheduleBundledTeleporterValidation();
                        OnConnected?.Invoke();
                    });
                }
                else if (result is LoginFailure failure)
                {
                    var errors = string.Join(", ", failure.Errors);
                    // Only a server refusal (bad password, slot gone) carries ErrorCodes, and it
                    // will be refused again — stop. The library reports an unreachable server as
                    // a LoginFailure too ("Connection timed out."), with no codes — keep trying.
                    bool refused = failure.ErrorCodes is { Length: > 0 };
                    CloseQuietly(session);
                    if (isReconnect)
                    {
                        if (refused) Logger.Error($"[AP] Reconnect refused by the server: {errors}");
                        else         Logger.Info($"[AP] Reconnect attempt failed: {errors}");
                        _mainThreadActions.Enqueue(() => OnReconnectFailed(generation, errors, retry: !refused));
                    }
                    else
                    {
                        Logger.Error($"[AP] Login failed: {errors}");
                        _mainThreadActions.Enqueue(() => OnConnectionFailed?.Invoke(errors));
                    }
                }
            }
            catch (Exception ex)
            {
                // Expected on every retry while the server is down; an error only on a fresh connect.
                if (isReconnect) Logger.Info($"[AP] Reconnect attempt failed: {ex.Message}");
                else             Logger.Error($"[AP] Connection exception: {ex.Message}");
                var message = ex.Message;
                if (session != null) CloseQuietly(session);
                if (isReconnect)
                {
                    // Server still unreachable — keep trying.
                    _mainThreadActions.Enqueue(() => OnReconnectFailed(generation, message, retry: true));
                }
                else
                {
                    _mainThreadActions.Enqueue(() => OnConnectionFailed?.Invoke(message));
                    lock (_sessionLock)
                    {
                        if (generation == _generation && ReferenceEquals(Session, session))
                            Session = null;
                    }
                }
            }
        });
    }

    /// <summary>
    /// Off-thread, after a retry logged in. Swaps the new session in for the dead one, queues
    /// the items that arrived during the outage and re-sends the checks made during it.
    /// </summary>
    /// <remarks>
    /// Everything else is left in place on purpose: the save binding, slot data, the item queue
    /// and every in-memory hold (parked traps, held expansions, drone modules). A full Connect
    /// would clear the holds and re-queue them from the deferred set — fine on a fresh load, but
    /// mid-play it drops the save binding for the length of the outage.
    /// </remarks>
    private void InstallReconnectedSession(ArchipelagoSession session, ArchipelagoData data,
                                           string expectedSeed, int generation)
    {
        var seed = session.RoomState.Seed;
        if (!string.Equals(seed, expectedSeed, StringComparison.Ordinal))
        {
            Logger.Error(
                $"[AP] Reconnect: the server is now running seed {seed}, not {expectedSeed} — " +
                "not reconnecting to it.");
            CloseQuietly(session);
            _mainThreadActions.Enqueue(() => OnReconnectFailed(
                generation, "the server is running a different seed", retry: false));
            return;
        }

        int enqueued;
        lock (_sessionLock)
        {
            if (generation != _generation)
            {
                Logger.Info("[AP] Reconnect finished after a disconnect — discarded.");
                CloseQuietly(session);
                return;
            }

            var old = Session;
            if (old != null) old.Items.ItemReceived -= OnItemReceived;

            lock (_queueLock)
            {
                // The new session replays its history through OnItemReceived from position 0.
                _receivedCount = 0;

                var snapshot  = session.Items.AllItemsReceived.ToList();
                int watermark = Plugin.Instance.SaveManager.LastItemIndex;
                if (watermark >= snapshot.Count)
                {
                    // Same seed, shorter history: the server was restored from an older save.
                    Logger.Warning(
                        $"[AP] Reconnect: watermark ({watermark}) exceeds server history " +
                        $"({snapshot.Count} item(s)) — resetting to -1 and replaying.");
                    Plugin.Instance.SaveManager.ForceLastItemIndex(-1);
                    _connectTimeWatermark = -1;
                    watermark = -1;
                    _pendingUpgradeValidation = true;
                }

                // Items still queued (requeued, or over last frame's budget) keep their place;
                // only what is neither queued nor applied is added.
                var queued = new HashSet<int>();
                foreach (var entry in _itemQueue) queued.Add(entry.index);

                enqueued = 0;
                for (int i = watermark + 1; i < snapshot.Count; i++)
                {
                    if (!queued.Add(i)) continue;
                    _itemQueue.Enqueue((snapshot[i], i));
                    enqueued++;
                }
                _snapshotCount = snapshot.Count;

                Session = session;
                session.Items.ItemReceived += OnItemReceived;
            }
        }

        // The dead session's DeathLink service is bound to its socket — drop it, never Disable()
        // it: that sends a ConnectUpdate synchronously, and MultiClient.Net's SendPacket on a
        // dead socket never returns (the send loop throws and leaves the wait pending). Calling
        // it here hung this thread after the session swap.
        DeathLink = null;
        if (SlotData?.DeathLink == true)
        {
            DeathLink = new DeathLinkHandler(session, data.SlotName);
            DeathLink.Enable();
        }

        // Items for checks made offline are not counted here: they do not exist on the server until
        // the flush below sends those checks, and then arrive through OnItemReceived.
        Logger.Info(
            $"[AP] Reconnected as '{data.SlotName}' (seed {seed}) — {enqueued} item(s) received " +
            "by the server during the outage.");
        FlushPendingChecks();

        _mainThreadActions.Enqueue(() =>
        {
            _reconnectInFlight = false;
            _reconnectAttempts = 0;

            // Counts cached from the dead session's history; recount from the new one.
            RanchPlotHandler.MarkDirty();
            ShopCatalogHandler.MarkDirty();
            PrismaShardHandler.MarkDirty();

            // A goal reached during the outage could not be reported then.
            if (Plugin.Instance.SaveManager.GoalReached) SetGoalComplete();

            StatusHUD.Instance?.ShowNotification("AP: reconnected");
            OnConnected?.Invoke();
        });
    }

    /// <summary>Main thread. Starts the reconnect when the live session's socket drops.</summary>
    private void OnSocketClosed(ArchipelagoSession closed)
    {
        // A replaced, discarded or deliberately closed session: not ours to report.
        if (!ReferenceEquals(closed, Session)) return;
        if (ReferenceEquals(closed, _closeHandledFor)) return;   // the other detector got it first
        _closeHandledFor = closed;

        OnDisconnected?.Invoke();

        if (_lastLogin == null) return;   // never logged in on this connection
        if (!Plugin.Instance.SaveManager.IsSaveBound) return;

        Logger.Warning("[AP] Connection lost — reconnecting automatically.");
        _outageStartedAt   = UnityEngine.Time.unscaledTime;
        _outageNoticeShown = false;
        ScheduleReconnect();
    }

    /// <summary>
    /// Main thread, every frame. Catches a socket that died without a close handshake — the
    /// server killed, the network gone. MultiClient.Net then raises only ErrorReceived from its
    /// receive loop and exits; SocketClosed never fires, so without this the reconnect never
    /// starts and the HUD keeps reading "Connected".
    /// </summary>
    private void WatchSocket()
    {
        var session = Session;
        if (session == null || ReferenceEquals(session, _closeHandledFor)) return;
        if (_reconnectScheduled || _reconnectInFlight) return;

        bool alive;
        try { alive = session.Socket.Connected; }
        catch { alive = false; }
        if (alive) return;

        Logger.Warning("[AP] Socket is no longer open (closed without a handshake).");
        OnSocketClosed(session);
    }

    /// <summary>Main thread. Arms the next retry on the backoff schedule.</summary>
    private void ScheduleReconnect()
    {
        float delay = ReconnectBackoff[System.Math.Min(_reconnectAttempts, ReconnectBackoff.Length - 1)];
        _reconnectScheduled = true;
        _nextReconnectAt    = UnityEngine.Time.unscaledTime + delay;

        int attempt = _reconnectAttempts + 1;
        Logger.Info($"[AP] Reconnect attempt {attempt} in {delay:F0}s.");
        StatusHUD.Instance?.ShowNotification($"AP: connection lost - retrying in {delay:F0}s");
        OnReconnecting?.Invoke(attempt, delay);
    }

    /// <summary>Main thread, every frame. Starts a scheduled retry once it is due.</summary>
    private void TickReconnect()
    {
        if (IsReconnecting && !_outageNoticeShown &&
            UnityEngine.Time.unscaledTime - _outageStartedAt > OutageNoticeAfter)
        {
            _outageNoticeShown = true;
            Logger.Info("[AP] Offline for 5 minutes — still retrying every 30s.");
            StatusHUD.Instance?.ShowNotification(
                "AP: still offline - retrying, checks are saved");
        }

        // Safety net: an attempt that never reports back would otherwise end all retries. The
        // waits in ConnectAndLogin are bounded, so this should only fire for a hang not yet known.
        if (_reconnectInFlight &&
            UnityEngine.Time.unscaledTime - _attemptStartedAt > AttemptTimeout)
        {
            Logger.Warning(
                $"[AP] Reconnect attempt {_reconnectAttempts} did not finish in {AttemptTimeout:F0}s — abandoning it.");
            lock (_sessionLock) { _generation++; }   // a late finish is then discarded
            _reconnectInFlight = false;
            ScheduleReconnect();
            return;
        }

        if (!_reconnectScheduled || _reconnectInFlight) return;
        if (UnityEngine.Time.unscaledTime < _nextReconnectAt) return;

        var data = _lastLogin;
        var seed = _lastLoginSeed;
        _reconnectScheduled = false;
        if (data == null || string.IsNullOrEmpty(seed)) return;

        _reconnectAttempts++;
        _reconnectInFlight = true;
        _attemptStartedAt  = UnityEngine.Time.unscaledTime;
        Logger.Info($"[AP] Reconnect attempt {_reconnectAttempts}: {data.Uri}:{data.Port} as '{data.SlotName}'.");
        StartLogin(data, reconnectSeed: seed);
    }

    /// <summary>Main thread. A retry failed: try again later, or give up for good.</summary>
    private void OnReconnectFailed(int generation, string message, bool retry)
    {
        if (generation != _generation) return;   // the player disconnected meanwhile
        _reconnectInFlight = false;

        if (retry)
        {
            ScheduleReconnect();
            return;
        }

        Logger.Warning($"[AP] Reconnect abandoned: {message}. Playing offline — save and reload to reconnect.");
        _reconnectScheduled = false;
        _reconnectAttempts  = 0;
        _lastLogin          = null;
        StatusHUD.Instance?.ShowNotification($"AP: reconnect stopped - {message}");
        OnConnectionFailed?.Invoke(message);
    }

    /// <summary>Closes a session that will not be used; never throws.</summary>
    private void CloseQuietly(ArchipelagoSession session)
    {
        try { session.Socket.DisconnectAsync(); } catch { /* best-effort */ }
    }

    /// <summary>Schedules <see cref="ItemHandler.ValidateAndRepairUpgrades"/>. Any thread.</summary>
    public void ScheduleUpgradeValidation() => _pendingUpgradeValidation = true;

    public void Disconnect()
    {
        // First, so a login or retry still in flight is discarded when it finishes.
        ArchipelagoSession? closing;
        lock (_sessionLock)
        {
            _generation++;
            closing = Session;
            Session = null;   // before closing it, so its SocketClosed reads as deliberate
        }
        _lastLogin          = null;
        _lastLoginSeed      = null;
        _reconnectScheduled = false;
        _reconnectInFlight  = false;
        _reconnectAttempts  = 0;

        // Dropped, not Disable()d: the socket closes below anyway, and a synchronous send on a
        // socket that died during an outage would freeze the main thread (see InstallReconnectedSession).
        DeathLink       = null;
        _liveScoutCache = null;
        _seedLocations  = null;
        SaveGuard.Reset();
        RanchPlotHandler.Reset();
        PrismaShardHandler.Reset();
        ShopCatalogHandler.Reset();
        HarmonizerHandler.Reset();
        Patches.UiPatches.ShopCatalogNotice.Reset();
        PrismacoreFulfiller.Reset();
        GoalHandler.Reset();   // persisted goal state lives in the save manager, reset below
        ItemHandler.ClearHeldDroneModules();
        // Close the socket explicitly, or the server never sees the client leave.
        if (closing != null)
        {
            closing.Items.ItemReceived -= OnItemReceived;
            CloseQuietly(closing);
        }
        SlotData                 = null;
        WeatherPatch.OnDisconnected();
        RadiantSlimeSpawnRatePatch.OnDisconnected();
        GoldLuckySpawnRatePatch.OnDisconnected();
        Patches.EconomyPatches.PlortMarketModePatch.OnDisconnected();
        _receivedCount            = 0;
        _snapshotCount            = 0;
        _connectTimeWatermark     = -1;
        while (_mainThreadActions.TryDequeue(out _)) { } // drop stale deferred actions
        _pendingUpgradeValidation = false;
        _upgradeRestoreDeadline   = 0f;
        GateReturnEnforcer.Clear();
        PlortDoorPoller.Reset();
        TrapHandler.ClearDeferred();
        ItemHandler.ResetUpgradeTracking();
        ItemHandler.ResetRegionTeleporterRetries();
        lock (_queueLock) { _itemQueue.Clear(); }
        // Or a reconnect would apply items into the old save file before OnConnected finishes.
        Plugin.Instance.SaveManager.ResetSession();

        // The closed socket's own event is ignored (see OnSocketClosed), so announce it here —
        // after the drain above, which would have dropped it.
        if (closing != null)
            _mainThreadActions.Enqueue(() => OnDisconnected?.Invoke());
        Logger.Info("[AP] Disconnected.");
    }

    // -------------------------------------------------------------------------
    // Scout — runs after connect to cache what item lives at each location
    // -------------------------------------------------------------------------

    /// <summary>Scouts the seed's locations and persists them for offline check notifications.</summary>
    private async Task ScoutAllLocationsAsync(ArchipelagoSession session)
    {
        try
        {
            // Only IDs in this seed — one unknown ID fails the whole batch.
            var serverLocations = new HashSet<long>(session.Locations.AllLocations);
            var ids = Data.LocationTable.All
                          .Select(l => l.Id)
                          .Where(serverLocations.Contains)
                          .ToArray();
            if (ids.Length == 0)
            {
                Logger.Warning(
                    $"[AP] Scout skipped — none of the {Data.LocationTable.All.Count} known locations " +
                    $"are present in this seed (server reports {serverLocations.Count}).");
                return;
            }
            Logger.Info(
                $"[AP] Scouting {ids.Length} location(s) present in this seed " +
                $"(of {Data.LocationTable.All.Count} known).");

            var result = await session.Locations.ScoutLocationsAsync(
                HintCreationPolicy.None, ids);

            _liveScoutCache = result;

            var mySlotName = session.Players.GetPlayerInfo(session.ConnectionInfo.Slot)?.Name ?? "";
            var persisted  = new Dictionary<long, ApSaveManager.PersistedScout>(result.Count);
            foreach (var kv in result)
            {
                var scout = kv.Value;
                persisted[kv.Key] = new ApSaveManager.PersistedScout
                {
                    ItemName      = scout.ItemName ?? $"Item #{scout.ItemId}",
                    PlayerName    = scout.Player?.Name ?? "Unknown",
                    PlayerGame    = scout.Player?.Game ?? "Unknown",
                    IsProgression = scout.Flags.HasFlag(ItemFlags.Advancement),
                    IsTrap        = scout.Flags.HasFlag(ItemFlags.Trap),
                    IsUseful      = scout.Flags.HasFlag(ItemFlags.NeverExclude),
                };
            }

            Plugin.Instance.SaveManager.UpdateAndSaveScoutData(persisted);
            Logger.Info($"[AP] Scouted {result.Count} location(s).");
        }
        catch (Exception ex)
        {
            Logger.Warning($"[AP] Scout failed: {ex.Message}");
        }
    }

    // -------------------------------------------------------------------------
    // Pending check flush — called right after reconnect
    // -------------------------------------------------------------------------

    /// <summary>Re-sends every locally checked location (covers offline checks); idempotent.</summary>
    private void FlushPendingChecks()
    {
        if (Session == null) return;
        var pending = Plugin.Instance.SaveManager.CheckedLocations;
        if (pending.Count == 0) return;

        try
        {
            // Counted before sending: the send marks them checked locally. Everything is re-sent
            // (the server ignores known checks), but "new" is the number that means something —
            // the checks made offline, or lost in flight.
            var known  = new HashSet<long>(Session.Locations.AllLocationsChecked);
            int unsent = pending.Count(id => !known.Contains(id));

            // Async: the synchronous form blocks forever if the socket dies mid-send.
            ObserveSend(Session.Locations.CompleteLocationChecksAsync(pending.ToArray()), "Flush");
            Logger.Info(
                $"[AP] Flushed {pending.Count} location check(s) to server — {unsent} new to it.");
        }
        catch (Exception ex)
        {
            Logger.Warning($"[AP] Flush failed: {ex.Message}");
        }
    }

    // -------------------------------------------------------------------------
    // Items
    // -------------------------------------------------------------------------

    /// <summary>Network thread. Assigns each item its AP position and queues those not yet applied.</summary>
    private void OnItemReceived(ReceivedItemsHelper helper)
    {
        Logger.Info(
            $"[AP] OnItemReceived-ENTER: _receivedCount={_receivedCount}, _snapshotCount={_snapshotCount}");
#if DEBUG
        SlimeRancher2AP.Utils.DebugTrace.Once("[BG] OnItemReceived — first call (background thread)");
#endif
        // Drain everything: one event can carry a whole batch, and nothing fires again for
        // items left in the helper.
        while (helper.Any())
        {
            // Position = AllItemsReceived index (see _receivedCount).
            int pos  = _receivedCount++;
            var item = helper.DequeueItem();

            // The helper hands over the login history on its first event too. Login already
            // queued those positions from the snapshot; queuing them again applied any not yet
            // applied twice (an item arriving during a load screen did it).
            if (pos < _snapshotCount) continue;

            // A new position is above everything applied, so this only skips history that
            // arrived after the login snapshot was taken and is already applied.
            var watermark = Plugin.Instance.SaveManager.LastItemIndex;
            if (pos > watermark)
            {
                lock (_queueLock) { _itemQueue.Enqueue((item, pos)); }
                Logger.Info(
                    $"[AP] Queued item: {item.ItemName} (id={item.ItemId}, idx={pos}, watermark={watermark})");
            }
            else
            {
                Logger.Info(
                    $"[AP] Skipped item (already applied): {item.ItemName} (id={item.ItemId}, idx={pos}, watermark={watermark})");
            }
        }
    }

    /// <summary>Per-frame, main thread: runs deferred main-thread actions, then applies queued items.</summary>
    public void ProcessItemQueue()
    {
        // Before the guards, so disconnect/failure notices fire offline. Each isolated.
        while (_mainThreadActions.TryDequeue(out var action))
        {
            try { action(); }
            catch (Exception ex)
            {
                Logger.Error($"[AP] Deferred main-thread action threw: {ex}");
            }
        }

        WatchSocket();
        TickReconnect();

        // Wait for the save file: SaveManager.OnConnected opens it on the connection thread,
        // and writes before then (e.g. UnlockRegion) silently no-op.
        if (!IsConnected || !Plugin.Instance.SaveManager.HasActiveSession) return;

        // Hold items on the menus rather than fail and requeue every frame.
        if (SceneContext.Instance?.Player == null) return;

        // Never deliver into a save not bound to this slot.
        if (!SaveGuard.IsSaveTrusted()) return;

#if DEBUG
        SlimeRancher2AP.Utils.DebugTrace.Once("ProcessItemQueue.1 — entered while connected");
#endif
        // Drain under the lock, apply outside it: a requeue during Apply would otherwise be
        // picked straight back up, looping forever.
        List<(ItemInfo item, int index)> pending;
        lock (_queueLock)
        {
#if DEBUG
            SlimeRancher2AP.Utils.DebugTrace.Once($"ProcessItemQueue.2 — inside lock, queue count={_itemQueue.Count}");
#endif
            pending = new List<(ItemInfo item, int index)>(_itemQueue.Count);
            while (_itemQueue.TryDequeue(out var e))
                pending.Add(e);
        }
#if DEBUG
        SlimeRancher2AP.Utils.DebugTrace.Once("ProcessItemQueue.5 — exited lock");
#endif

        // Upgrade validation waits for UpgradeModel.Push (see ItemHandler.UpgradesRestored),
        // except on a fresh save, and at most the grace period — a save with no upgrades may
        // never Push. unscaledTime: Time.time stalls while timeScale is 0.
        if (_pendingUpgradeValidation && ItemHandler.UpgradeHandler != null)
        {
            if (_upgradeRestoreDeadline <= 0f)
                _upgradeRestoreDeadline = UnityEngine.Time.unscaledTime + UpgradeRestoreGraceSeconds;

            bool restored = ItemHandler.UpgradesRestored || ItemHandler.NoRestoreExpected;
            bool timedOut = UnityEngine.Time.unscaledTime >= _upgradeRestoreDeadline;

            if (restored || timedOut)
            {
                if (!restored)
                    Logger.Warning(
                        "[AP] Upgrade validation: UpgradeModel.Push never fired within " +
                        $"{UpgradeRestoreGraceSeconds}s — reconciling against a possibly " +
                        "unrestored model.");

                _pendingUpgradeValidation = false;
                _upgradeRestoreDeadline   = 0f;
                ItemHandler.ValidateAndRepairUpgrades();
            }
        }


#if DEBUG
        if (pending.Count > 0)
            Logger.Info(
                $"[AP] ProcessItemQueue: {pending.Count} item(s) to process, " +
                $"_snapshotCount={_snapshotCount}, _connectTimeWatermark={_connectTimeWatermark}");
#endif

        // 8 ms per frame, so a big release doesn't stall the game; the rest waits a frame.
        const double MaxMsPerFrame = 8.0;
        var sw = System.Diagnostics.Stopwatch.StartNew();

        for (int i = 0; i < pending.Count; i++)
        {
            var entry = pending[i];
#if DEBUG
            SlimeRancher2AP.Utils.DebugTrace.Once($"ProcessItemQueue.3 — first dequeued item id={entry.item.ItemId} idx={entry.index}");
#endif
            // Secondary dedup: OnItemReceived can queue history before OnConnected loads the
            // watermark. Uses the connect-time value (after the stale reset), not the live one.
            // Live items always apply; deferred items sit below the watermark by design.
            bool isLiveItem     = (entry.index >= _snapshotCount);
            bool isDeferredItem = Plugin.Instance.SaveManager.IsDeferredItem(entry.index);
            if (!isLiveItem && !isDeferredItem && entry.index <= _connectTimeWatermark)
            {
                Logger.Info(
                    $"[AP] Skipped item (secondary dedup): {entry.item.ItemName} (id={entry.item.ItemId}, idx={entry.index}, watermark={_connectTimeWatermark})");
                continue;
            }

            try
            {
                ItemHandler.Apply(entry.item, entry.index);
            }
            catch (Exception ex)
            {
                // Don't let one item's exception drop the rest of `pending`.
                Logger.Error(
                    $"[AP] Exception applying item {entry.item.ItemName} " +
                    $"(id={entry.item.ItemId}, idx={entry.index}): {ex}");
            }
#if DEBUG
            SlimeRancher2AP.Utils.DebugTrace.Once($"ProcessItemQueue.4 — Apply returned for first item idx={entry.index}");
#endif

            if (sw.Elapsed.TotalMilliseconds > MaxMsPerFrame && i < pending.Count - 1)
            {
                int remaining = pending.Count - i - 1;
                // Put the rest back AHEAD of anything that arrived meanwhile. Appending them
                // let a burst jump the watermark past items still waiting (seen on a
                // 290-item release: watermark 290 with 2..289 pending), which a quit at that
                // moment would lose for good.
                lock (_queueLock)
                {
                    var arrived = new List<(ItemInfo item, int index)>(_itemQueue.Count);
                    while (_itemQueue.TryDequeue(out var e))
                        arrived.Add(e);
                    for (int j = i + 1; j < pending.Count; j++)
                        _itemQueue.Enqueue(pending[j]);
                    foreach (var e in arrived)
                        _itemQueue.Enqueue(e);
                }
                Logger.Info(
                    $"[AP] ProcessItemQueue: budget exceeded ({sw.Elapsed.TotalMilliseconds:F1}ms), " +
                    $"deferred {remaining} item(s) to next frame");
                break;
            }
        }
    }

    /// <summary>Re-enqueues an item that could not be applied yet (e.g., scene not ready).</summary>
    public void RequeueItem(ItemInfo item, int index)
    {
        lock (_queueLock) { _itemQueue.Enqueue((item, index)); }
    }

    /// <summary>
    /// For a new SR2 game on a slot with history: resets the watermark and requeues every item,
    /// skipping one-shot items already applied in an earlier run. Main thread.
    /// </summary>
    public void RequestFullReplay()
    {
        if (Session == null)
        {
            Logger.Warning("[AP] RequestFullReplay: no active session — skipped");
            return;
        }

        Plugin.Instance.SaveManager.ForceLastItemIndex(-1);
        // Or the secondary dedup would drop exactly the items requeued below.
        _connectTimeWatermark = -1;

        var snapshot = Session.Items.AllItemsReceived.ToList();
        int enqueued = 0;
        lock (_queueLock)
        {
            _itemQueue.Clear();
            for (int i = 0; i < snapshot.Count; i++)
            {
                var itemInfo = Data.ItemTable.Get(snapshot[i].ItemId);
                bool isEph = itemInfo?.Type is Data.ItemType.Filler or Data.ItemType.Trap;
                if (isEph && Plugin.Instance.SaveManager.IsEphemeralApplied(i))
                    continue;
                _itemQueue.Enqueue((snapshot[i], i));
                enqueued++;
            }
        }

        Logger.Info(
            $"[AP] RequestFullReplay: {snapshot.Count} item(s) in history, {enqueued} queued for fresh SR2 save.");
    }

    // -------------------------------------------------------------------------
    // Locations
    // -------------------------------------------------------------------------

    /// <summary>
    /// <c>Environment.TickCount</c> of the last check, for the trap post-check grace.
    /// Starts an hour in the past so the grace is inactive.
    /// </summary>
    internal static int LastCheckSentTickMs = System.Environment.TickCount - 3_600_000;

    /// <summary>
    /// Records a check locally and sends it if connected; offline checks are flushed on the
    /// next connect. Idempotent. Shows a notification from scout data.
    /// </summary>
    public void SendCheck(long locationId)
    {
        if (!Plugin.Instance.ModEnabled) return;
        // IsSaveBound, not HasActiveSession: offline checks are real checks.
        if (!Plugin.Instance.SaveManager.IsSaveBound) return;
        if (Plugin.Instance.SaveManager.IsChecked(locationId)) return;

        if (!SaveGuard.IsSaveTrusted())
        {
            Logger.Warning($"[AP] SendCheck({locationId}) ignored — current save is not associated with this AP slot.");
            return;
        }

        LastCheckSentTickMs = System.Environment.TickCount;
        Plugin.Instance.SaveManager.MarkChecked(locationId);
        ShowCheckNotification(locationId);

        if (IsConnected)
        {
            // Async: this runs on the main thread, and the synchronous form never returns if the
            // socket dies first. A lost send is safe — the check is persisted and re-flushed on
            // the next connect.
            ObserveSend(Session!.Locations.CompleteLocationChecksAsync(locationId), $"Check {locationId}");
            var name = Session.Locations.GetLocationNameFromId(locationId);
            Logger.Info($"[AP] Checked: {name} ({locationId})");
        }
        else
        {
            Logger.Info($"[AP] Checked (offline): {locationId}");
        }
    }

    /// <summary>Notifies what a check held and for whom, from persisted scout data.</summary>
    private void ShowCheckNotification(long locationId)
    {
        string message;

        var persisted = Plugin.Instance.SaveManager.GetScout(locationId);
        if (persisted != null)
        {
            var mySlot = Session?.ConnectionInfo.Slot ?? -1;
            var myName = mySlot >= 0
                ? (Session?.Players.GetPlayerInfo(mySlot)?.Name ?? "")
                : "";

            bool isMine = string.Equals(persisted.PlayerName, myName, StringComparison.OrdinalIgnoreCase)
                       || mySlot < 0;   // if not connected yet, assume it's ours

            var itemLabel = persisted.IsProgression
                ? $"★ {persisted.ItemName}"
                : persisted.IsTrap
                    ? $"⚠ {persisted.ItemName}"
                    : persisted.ItemName;

            message = isMine
                ? $"Found {itemLabel}!"
                : $"Found {itemLabel} for {persisted.PlayerName} ({persisted.PlayerGame})";
        }
        else
        {
            var info = Data.LocationTable.GetById(locationId);
            message = info != null
                ? $"Checked {info.Name}"
                : $"Checked location {locationId}";
        }

        StatusHUD.Instance?.ShowNotification(message);
    }

    // -------------------------------------------------------------------------
    // Goal
    // -------------------------------------------------------------------------

    public void SetGoalComplete()
    {
        if (!IsConnected) return;
        // SetGoalAchieved() sends synchronously (see SendCheck); this is the same packet, async.
        // GoalReached is persisted, so a lost send is re-reported on the next connect.
        ObserveSend(Session!.Socket.SendPacketAsync(
            new StatusUpdatePacket { Status = ArchipelagoClientState.ClientGoal }), "Goal");
        Logger.Info("[AP] Goal achieved!");
    }

    /// <summary>Logs a fire-and-forget send's failure instead of leaving it unobserved.</summary>
    private static void ObserveSend(System.Threading.Tasks.Task task, string what)
    {
        task.ContinueWith(t => Logger.Warning(
                $"[AP] {what} send failed: {t.Exception?.GetBaseException().Message}"),
            System.Threading.Tasks.TaskContinuationOptions.OnlyOnFaulted);
    }
}
