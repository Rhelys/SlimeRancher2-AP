using Archipelago.MultiClient.Net;
using Archipelago.MultiClient.Net.BounceFeatures.DeathLink;
using Archipelago.MultiClient.Net.Enums;
using Archipelago.MultiClient.Net.Helpers;
using Archipelago.MultiClient.Net.Models;
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
    // Connection
    // -------------------------------------------------------------------------

    public void Connect(ArchipelagoData data)
    {
        // Unconditionally, so even the first connect starts from clean state.
        Disconnect();

        Task.Run(() =>
        {
            try
            {
                Session = ArchipelagoSessionFactory.CreateSession(data.Uri, data.Port);

                // ItemReceived is registered after login, below.
                Session.Socket.ErrorReceived += (ex, msg) =>
                    Logger.Error($"[AP] Socket error: {msg} — {ex?.Message}");
                Session.Socket.SocketClosed += reason =>
                {
                    Logger.Warning($"[AP] Disconnected: {reason}");
                    // Fires on the socket thread — handlers touch Unity UI, so defer.
                    _mainThreadActions.Enqueue(() => OnDisconnected?.Invoke());
                };

                var result = Session.TryConnectAndLogin(
                    PluginInfo.GAME,
                    data.SlotName,
                    ItemsHandlingFlags.AllItems,
                    password: string.IsNullOrWhiteSpace(data.Password) ? null : data.Password,
                    requestSlotData: true
                );

                if (result is LoginSuccessful success)
                {
                    data.Seed = Session.RoomState.Seed;
                    data.Team = Session.ConnectionInfo.Team;
                    data.Slot = Session.ConnectionInfo.Slot;

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
                        $"newbucks_mult={SlotData.NewbucksMultiplier}%");

                    if (SlotData.DeathLink)
                    {
                        DeathLink = new DeathLinkHandler(Session, data.SlotName);
                        DeathLink.Enable();
                    }

                    // Loads LastItemIndex for this seed+slot (file I/O, fine off-thread).
                    Plugin.Instance.SaveManager.OnConnected(data.Seed, data.SlotName);

                    // Queue history above the watermark.
                    var snapshot = Session.Items.AllItemsReceived.ToList();
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
                    Session.Items.ItemReceived += OnItemReceived;
                    Logger.Info("[AP] ItemReceived handler registered.");

                    _seedLocations = new HashSet<long>(Session.Locations.AllLocations);

                    FlushPendingChecks();
                    var capturedSession = Session;
                    _ = ScoutAllLocationsAsync(capturedSession);

                    Logger.Info(
                        $"[AP] Connected as '{data.SlotName}' (slot {data.Slot}, seed {data.Seed})");

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
                    Logger.Error($"[AP] Login failed: {errors}");
                    _mainThreadActions.Enqueue(() => OnConnectionFailed?.Invoke(errors));
                    Session = null;
                }
            }
            catch (Exception ex)
            {
                Logger.Error($"[AP] Connection exception: {ex.Message}");
                var message = ex.Message;
                _mainThreadActions.Enqueue(() => OnConnectionFailed?.Invoke(message));
                Session = null;
            }
        });
    }

    /// <summary>Schedules <see cref="ItemHandler.ValidateAndRepairUpgrades"/>. Any thread.</summary>
    public void ScheduleUpgradeValidation() => _pendingUpgradeValidation = true;

    public void Disconnect()
    {
        DeathLink?.Disable();
        DeathLink       = null;
        _liveScoutCache = null;
        _seedLocations  = null;
        SaveGuard.Reset();
        RanchPlotHandler.Reset();
        PrismaShardHandler.Reset();
        ShopCatalogHandler.Reset();
        Patches.UiPatches.ShopCatalogNotice.Reset();
        PrismacoreFulfiller.Reset();
        GoalHandler.Reset();   // persisted goal state lives in the save manager, reset below
        ItemHandler.ClearHeldDroneModules();
        // Close the socket explicitly, or the server never sees the client leave.
        if (Session != null)
        {
            Session.Items.ItemReceived -= OnItemReceived;
            try { Session.Socket.DisconnectAsync(); } catch { /* best-effort */ }
        }
        Session = null;
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
            Session.Locations.CompleteLocationChecks(pending.ToArray());
            Logger.Info($"[AP] Flushed {pending.Count} location check(s) to server.");
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
            var watermark  = Plugin.Instance.SaveManager.LastItemIndex;
            bool isLiveItem = (pos >= _snapshotCount);
            if (isLiveItem || pos > watermark)
            {
                lock (_queueLock) { _itemQueue.Enqueue((item, pos)); }
                Logger.Info(
                    $"[AP] Queued item: {item.ItemName} (id={item.ItemId}, idx={pos}, watermark={watermark}, live={isLiveItem})");
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
                lock (_queueLock)
                {
                    for (int j = i + 1; j < pending.Count; j++)
                        _itemQueue.Enqueue(pending[j]);
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
            Session!.Locations.CompleteLocationChecks(locationId);
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
        Session!.SetGoalAchieved();
        Logger.Info("[AP] Goal achieved!");
    }
}
