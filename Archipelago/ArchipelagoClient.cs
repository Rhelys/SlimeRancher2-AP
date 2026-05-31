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
/// Manages the lifecycle of the Archipelago session: connect, disconnect, item queue, and
/// location check sending. All network operations are run off-thread; item processing is
/// dispatched back to the Unity main thread via ProcessItemQueue().
/// </summary>
public class ArchipelagoClient
{
    public ArchipelagoSession? Session    { get; private set; }
    public SlotData?           SlotData   { get; private set; }
    public DeathLinkHandler?   DeathLink  { get; private set; }

    public bool IsConnected => Session?.Socket.Connected ?? false;

    public event Action<string>? OnConnectionFailed;
    public event Action?         OnConnected;
    public event Action?         OnDisconnected;

    // Queue holds (item, index) tuples. Index is the library's canonical 0-based position
    // (ReceivedItemsHelper.Index captured before DequeueItem) — matches the AP server's
    // own position counter regardless of when the handler fires or how many items are batched.
    // Used to skip items already applied in a previous session (reconnect dedup).
    private readonly Queue<(ItemInfo item, int index)> _itemQueue = new();
    private readonly object _queueLock = new();
    // _receivedCount is a plain sequential counter: how many items we have personally
    // dequeued from the ReceivedItemsHelper across all OnItemReceived calls this session.
    // It starts at 0 (reset by Disconnect → set by Connect → resets Disconnect again).
    // Each item's 0-based AP position = _receivedCount before the dequeue (then we ++).
    // This matches AllItemsReceived indexing exactly, regardless of whether items arrive
    // one-at-a-time or in a batch (the library delivers them in order).
    //
    // Note: helper.Index is NOT used for per-item positions because it tracks total items
    // received by the helper (1-based count), not each item's individual position. In a
    // batch of N items, helper.Index equals N for all N dequeues — wrong for our purposes.
    private volatile int _receivedCount = 0;

    // Number of items in AllItemsReceived at login time (after the sanity check + historical
    // replay).  Any item whose index >= _snapshotCount is a *live* item generated after
    // login and must always be queued — even if the saved watermark is stale/high.
    // This prevents the race where OnConnected writes _lastItemIdx=9017 from the config
    // before ForceLastItemIndex(0) corrects it, causing a fast live item to be skipped.
    private volatile int _snapshotCount = 0;

    // The watermark as it stood at connect time, AFTER the stale-watermark sanity check
    // (and after ForceLastItemIndex reset it to -1 if necessary).  Used as the threshold
    // for the secondary dedup in ProcessItemQueue so that a stale on-disk watermark (e.g.
    // 9018 from a previous AP slot) does not cause freshly-replayed historical items
    // (indices 0..N) to be incorrectly skipped.  The live LastItemIndex can drift upward
    // as items are applied, but this snapshot never changes for the lifetime of the session.
    private volatile int _connectTimeWatermark = -1;

    // In-memory scout cache — populated after connect, then refreshed from server.
    // Also readable from ApSaveManager for offline sessions.
    private Dictionary<long, ScoutedItemInfo>? _liveScoutCache;

    // Actions that must run on the Unity main thread after a successful login.
    // Set on the network thread, consumed by ProcessItemQueue on the main thread.
    // Volatile so the main thread always sees the latest write without a full lock.
    private volatile Action? _pendingMainThreadAction;

// When true, ProcessItemQueue will run ValidateAndRepairUpgrades() once as soon as
    // UpgradeHandler is non-null (i.e., the scene has loaded far enough).  Set on every
    // connect so that if the SR2 save is behind the AP watermark (e.g. the game crashed
    // before the autosave after items were applied), the correct levels are restored.
    private volatile bool _pendingUpgradeValidation;

    /// <summary>
    /// Returns scout info for a location.  Checks the live cache first (online),
    /// then falls back to the persisted data from the last successful scout (offline).
    /// </summary>
    public ScoutedItemInfo? GetScoutedItem(long locationId)
        => _liveScoutCache != null && _liveScoutCache.TryGetValue(locationId, out var info) ? info : null;

    // -------------------------------------------------------------------------
    // Connection
    // -------------------------------------------------------------------------

    public void Connect(ArchipelagoData data)
    {
        // Always disconnect before reconnecting — resets _receivedCount, _snapshotCount,
        // clears the item queue, and resets the save-manager session pointer.
        // Previously guarded by "if (Session != null)" but that skipped the reset on the
        // very first connection (Session==null) leaving stale state from any previous run.
        Disconnect();

        Task.Run(() =>
        {
            try
            {
                Session = ArchipelagoSessionFactory.CreateSession(data.Uri, data.Port);

                // Wire up socket error / close handlers before login.
                // ItemReceived is registered AFTER login (see below) — not before.
                Session.Socket.ErrorReceived += (ex, msg) =>
                    Logger.Error($"[AP] Socket error: {msg} — {ex?.Message}");
                Session.Socket.SocketClosed += reason =>
                {
                    Logger.Warning($"[AP] Disconnected: {reason}");
                    OnDisconnected?.Invoke();
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

                    // Persist the seed so the next reconnect can read it.
                    data.SaveSeedToConfig(Plugin.Instance.Config);

                    SlotData = SlotData.Parse(success.SlotData);
                    Logger.Info(
                        $"[AP] SlotData: goal='{SlotData.Goal}' region_access_mode='{SlotData.RegionAccessMode}' " +
                        $"conversation_checks='{SlotData.ConversationChecks}' " +
                        $"weather_freq_mult={SlotData.WeatherFrequencyMultiplier} force_heavy={SlotData.ForceHeavyWeather} " +
                        $"all_radiant={SlotData.AllRadiantSlimes} radiant_mult={SlotData.RadiantSpawnRateMultiplier} " +
                        $"start_harvester={SlotData.StartWithResourceHarvester}");

                    if (SlotData.DeathLink)
                    {
                        DeathLink = new DeathLinkHandler(Session, data.SlotName);
                        DeathLink.Enable();
                    }

                    // File I/O — safe on background thread.
                    // This sets LastItemIndex to the saved watermark for this seed+slot.
                    Plugin.Instance.SaveManager.OnConnected(data.Seed, data.SlotName);

                    // Replay historical items from AllItemsReceived.
                    //
                    // AllItemsReceived contains items accumulated in the current session
                    // connection (populated during TryConnectAndLogin with AllItems).
                    // ItemReceived only fires for live items received AFTER login, not for
                    // these historical ones — so we process them here instead.
                    //
                    // _receivedCount is set to snapshot.Count so that live items arriving
                    // via OnItemReceived get sequential indices starting immediately after
                    // the historical range.
                    var snapshot = Session.Items.AllItemsReceived.ToList();
                    int savedWatermark = Plugin.Instance.SaveManager.LastItemIndex;

                    Logger.Info(
                        $"[AP] Reconnect state: savedWatermark={savedWatermark}, " +
                        $"snapshot.Count={snapshot.Count} — " +
                        $"expect {System.Math.Max(0, snapshot.Count - (savedWatermark + 1))} item(s) to replay");

                    // Sanity-check: the watermark should never exceed the highest item
                    // position the server has on record (snapshot.Count - 1).  If it does,
                    // the saved value is stale — either from a fresh SR2 save on an old AP
                    // slot, a server reset that truncated history, or manually-sent fix items
                    // that now sit in the snapshot at indices below the old watermark.
                    //
                    // Reset to -1 (replay ALL snapshot items) rather than snapshot.Count-1
                    // (skip all).  Resetting to snapshot.Count-1 would silently drop any
                    // manually-sent fix items that are at idx < old watermark.  Replaying
                    // from -1 is safe: upgrades/gadgets/regions are idempotent; filler may
                    // be duplicated in the edge case but that is preferable to missing items.
                    if (savedWatermark >= snapshot.Count)
                    {
                        Logger.Warning(
                            $"[AP] Watermark ({savedWatermark}) exceeds server history ({snapshot.Count} item(s)) — " +
                            $"resetting to -1 and replaying all {snapshot.Count} item(s).");
                        Plugin.Instance.SaveManager.ForceLastItemIndex(-1);
                        savedWatermark = -1;
                    }

                    // Freeze the effective watermark for this session's secondary dedup.
                    // savedWatermark is now either the disk value or -1 (if sanity-check reset it).
                    _connectTimeWatermark = savedWatermark;

                    int enqueued = 0;
                    lock (_queueLock)
                    {
                        for (int i = savedWatermark + 1; i < snapshot.Count; i++)
                        {
                            _itemQueue.Enqueue((snapshot[i], i));
                            enqueued++;
                        }

                        // Re-queue any trap items that were rate-limited (deferred) last session
                        // and whose watermark has already advanced past their index.
                        //
                        // Scenario: trap at idx=6 was deferred; live item at idx=7 was applied
                        // (advancing the watermark to 7); then the game disconnected before the
                        // trap could fire.  Without this, idx=6 would be permanently lost on
                        // reconnect (below watermark=7, not in ephemeral set).
                        //
                        // We only need to re-queue deferred items at or below savedWatermark;
                        // those above the watermark are already handled by the replay loop above.
                        var pendingDeferred = Plugin.Instance.SaveManager.GetPendingDeferredTraps();
                        int deferredRequeued = 0;
                        foreach (var deferredIdx in pendingDeferred)
                        {
                            if (deferredIdx < snapshot.Count && deferredIdx <= savedWatermark)
                            {
                                _itemQueue.Enqueue((snapshot[deferredIdx], deferredIdx));
                                deferredRequeued++;
                            }
                        }
                        if (deferredRequeued > 0)
                            Logger.Info(
                                $"[AP] Re-queued {deferredRequeued} deferred trap(s) from previous session " +
                                $"(below watermark={savedWatermark}, will fire this session).");
                        enqueued += deferredRequeued;
                    }
                    // _receivedCount stays at 0 (reset by Disconnect at the top of Connect).
                    // It is a plain dequeue counter — assigned sequentially as OnItemReceived
                    // pulls items from the helper.  Starting from 0 ensures the positions
                    // match AllItemsReceived 0-based indexing regardless of batch vs single.
                    _snapshotCount = snapshot.Count; // live-item boundary: pos >= this means freshly generated
                    Logger.Info(
                        $"[AP] Historical replay: {snapshot.Count} item(s) in history, " +
                        $"watermark={savedWatermark}, {enqueued} new item(s) queued.");
                    Logger.Info(
                        $"[AP] Counters frozen: _receivedCount={_receivedCount} (starts at 0), _snapshotCount={_snapshotCount}");

                    // Register ItemReceived NOW so only live (post-login) items come through.
                    // _receivedCount is already set to snapshot.Count, so new items get
                    // sequential indices starting from there.
                    Logger.Info("[AP] Registering ItemReceived handler...");
                    Session.Items.ItemReceived += OnItemReceived;
                    Logger.Info("[AP] ItemReceived handler registered.");

                    // Flush offline checks and start scout from the background thread.
                    FlushPendingChecks();
                    var capturedSession = Session;
                    _ = ScoutAllLocationsAsync(capturedSession);

                    Logger.Info(
                        $"[AP] Connected as '{data.SlotName}' (slot {data.Slot}, seed {data.Seed})");

                    // Schedule upgrade validation: once the scene and UpgradeHandler are ready,
                    // ProcessItemQueue will call ValidateAndRepairUpgrades() to fix any discrepancy
                    // between the SR2 save levels and what the AP server has delivered.
                    _pendingUpgradeValidation = true;

                    // GoalHandler.Initialize(), WeatherPatch, and OnConnected touch Unity APIs — defer to main thread.
                    _pendingMainThreadAction = () =>
                    {
                        GoalHandler.Initialize();
                        WeatherPatch.OnSlotDataReceived(); // applies WeatherFrequencyMultiplier to the persistent WeatherRegistry
                        OnConnected?.Invoke();
                    };
                }
                else if (result is LoginFailure failure)
                {
                    var errors = string.Join(", ", failure.Errors);
                    Logger.Error($"[AP] Login failed: {errors}");
                    // OnConnectionFailed also touches UI — defer to main thread.
                    _pendingMainThreadAction = () => OnConnectionFailed?.Invoke(errors);
                    Session = null;
                }
            }
            catch (Exception ex)
            {
                Logger.Error($"[AP] Connection exception: {ex.Message}");
                OnConnectionFailed?.Invoke(ex.Message);
                Session = null;
            }
        });
    }

    /// <summary>
    /// Schedules <see cref="ItemHandler.ValidateAndRepairUpgrades"/> to run on the next
    /// <see cref="ProcessItemQueue"/> frame in which <see cref="ItemHandler.UpgradeHandler"/>
    /// is non-null.  Safe to call from any thread (volatile write).
    /// </summary>
    public void ScheduleUpgradeValidation() => _pendingUpgradeValidation = true;

    public void Disconnect()
    {
        DeathLink?.Disable();
        DeathLink       = null;
        _liveScoutCache = null;
        // Unregister and close the socket before nulling Session.
        // Without DisconnectAsync the underlying WebSocket stays open and the AP
        // server never sees the client leave — it only observes the tag change from
        // DeathLink.Disable() above.
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
        _receivedCount            = 0;
        _snapshotCount            = 0;
        _connectTimeWatermark     = -1;
        _pendingMainThreadAction  = null;
        _pendingUpgradeValidation = false;
        GateReturnEnforcer.Clear();
        TrapHandler.ClearDeferred();
        lock (_queueLock) { _itemQueue.Clear(); }
        // Reset the save-manager session pointer so HasActiveSession returns false.
        // Without this, a reconnect would see the previous session's _saveFile as
        // still valid and process items before the new OnConnected completes.
        Plugin.Instance.SaveManager.ResetSession();
        Logger.Info("[AP] Disconnected.");
    }

    // -------------------------------------------------------------------------
    // Scout — runs after connect to cache what item lives at each location
    // -------------------------------------------------------------------------

    /// <summary>
    /// Scouts every location in the game and persists the results so that
    /// location-check notifications work even in offline sessions.
    /// Runs off the main thread; safe to fire-and-forget.
    /// </summary>
    private async Task ScoutAllLocationsAsync(ArchipelagoSession session)
    {
        try
        {
            var ids = Data.LocationTable.All.Select(l => l.Id).ToArray();
            if (ids.Length == 0) return;

            var result = await session.Locations.ScoutLocationsAsync(
                HintCreationPolicy.None, ids);

            _liveScoutCache = result;

            // Convert to persisted format and save to disk so offline sessions can use it.
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

    /// <summary>
    /// Re-sends all locally-tracked checked locations to the server.
    /// Idempotent: the server ignores already-completed locations.
    /// This catches any checks made while offline.
    /// </summary>
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

    /// <summary>
    /// Called on the network thread for every item received (including historical replay on reconnect).
    /// Assigns a local index based on receipt order and skips items already applied.
    /// </summary>
    private void OnItemReceived(ReceivedItemsHelper helper)
    {
        // Log on EVERY call so we can see silent/unexpected invocations.
        Logger.Info(
            $"[AP] OnItemReceived-ENTER: _receivedCount={_receivedCount}, _snapshotCount={_snapshotCount}");
#if DEBUG
        SlimeRancher2AP.Utils.DebugTrace.Once("[BG] OnItemReceived — first call (background thread)");
#endif
        // Drain the ENTIRE helper queue, not just the first item.
        //
        // The AP server may bundle multiple items into a single ReceivedItems packet
        // (e.g. when a live check and another player's check resolve simultaneously).
        // In that case the library fires ItemReceived ONCE with all pending items in
        // the helper queue.  Dequeuing only the first item silently drops the rest —
        // they remain in the helper with no further event fired this session.
        //
        // Draining fully is safe for both library behaviours:
        //  • One event per item  — loop runs once, exits; next event drains its item.
        //  • One event per batch — loop drains all items in the batch correctly.
        while (helper.Any())
        {
            // pos is the 0-based AP position of this item, matching AllItemsReceived[pos].
            // We assign it by reading _receivedCount BEFORE incrementing — this is a plain
            // sequential counter (reset to 0 each Connect via Disconnect()) that advances
            // by 1 for each item dequeued, regardless of how many arrive per call.
            //
            // This is correct for both delivery patterns:
            //   • One event per item  — pos = 0,1,2,… across separate calls, one item each.
            //   • Batch event         — pos = 0,1,2,3 within a single call for 4 items.
            // In either case, pos matches AllItemsReceived[pos], giving correct dedup.
            //
            // We do NOT use helper.Index for per-item positions because helper.Index tracks
            // the total count of items received by the helper (incremented when the network
            // packet arrives, not when we dequeue). In a batch of N items, helper.Index
            // equals N for every dequeue in the loop — the same value for all N items —
            // making it useless as a per-item discriminator.
            int pos  = _receivedCount++;
            var item = helper.DequeueItem();
            var watermark  = Plugin.Instance.SaveManager.LastItemIndex;
            // pos >= _snapshotCount means this item was generated AFTER login — it is a live
            // item and must always be queued, even if the saved watermark is stale.
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

    /// <summary>
    /// Processes items from the queue on the Unity main thread (called from ApUpdateBehaviour.Update).
    /// Also drains any deferred post-login actions (GoalHandler init, UI callbacks) that
    /// were queued from the network thread to avoid calling Unity APIs off the main thread.
    /// </summary>
    public void ProcessItemQueue()
    {
        // Drain the post-login action (if any) before processing items.
        // Use Interlocked.Exchange so we see the write from the background thread and
        // atomically clear the field so it only fires once.
        var action = System.Threading.Interlocked.Exchange(ref _pendingMainThreadAction, null);
        action?.Invoke();

        // Do not process items until the save file is open.
        //
        // SaveManager.OnConnected (which sets _saveFile) runs on the background thread
        // concurrently with this method.  If we drain the queue before OnConnected
        // completes, calls like UnlockRegion() silently no-op because _saveFile is null,
        // causing region unlocks to be lost across scene reloads.
        //
        // The background thread always sets _saveFile BEFORE writing _pendingMainThreadAction
        // (strong happens-before via the volatile write), so by the time the action above
        // fires, HasActiveSession is guaranteed to be true.  Items are therefore held for at
        // most one extra Update() frame beyond the frame the action fires — negligible.
        if (!IsConnected || !Plugin.Instance.SaveManager.HasActiveSession) return;

        // Don't drain the item queue until the game scene has a live Player object.
        // Every Apply method needs SceneContext/GadgetDirector/UpgradeHandler, none of which
        // exist on the main menu or save-select screen.  Holding items here (rather than
        // dequeuing → failing → requeueing every frame) eliminates the per-frame log spam
        // while the player is still in the pre-game UI.
        if (SceneContext.Instance?.Player == null) return;

#if DEBUG
        SlimeRancher2AP.Utils.DebugTrace.Once("ProcessItemQueue.1 — entered while connected");
#endif
        // Drain the queue into a local list under the lock, then process OUTSIDE the lock.
        //
        // If we called ItemHandler.Apply() while holding _queueLock and Apply() decided to
        // requeue the item (e.g. SceneContext is null in the main menu), RequeueItem() would
        // re-acquire the reentrant lock and enqueue the item back — causing the while loop to
        // pick it up immediately and loop forever, freezing the main thread.
        //
        // Draining first (O(N) under lock) ensures the queue is empty before we process.
        // Any requeue during processing safely lands in the now-empty queue for the next frame.
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

        // Upgrade validation: runs once per connect, as soon as UpgradeHandler is available.
        // Compares expected levels (from AP snapshot) against actual SR2 model levels and
        // applies any correction — handles the case where SR2 was behind due to a crash.
        if (_pendingUpgradeValidation && ItemHandler.UpgradeHandler != null)
        {
            _pendingUpgradeValidation = false;
            ItemHandler.ValidateAndRepairUpgrades();
        }


#if DEBUG
        if (pending.Count > 0)
            Logger.Info(
                $"[AP] ProcessItemQueue: {pending.Count} item(s) to process, " +
                $"_snapshotCount={_snapshotCount}, _connectTimeWatermark={_connectTimeWatermark}");
#endif

        foreach (var entry in pending)
        {
#if DEBUG
            SlimeRancher2AP.Utils.DebugTrace.Once($"ProcessItemQueue.3 — first dequeued item id={entry.item.ItemId} idx={entry.index}");
#endif
            // Secondary dedup: skip items whose index is at or below the connect-time watermark.
            //
            // OnItemReceived uses _lastItemIdx as a gate, but that field starts at -1 and
            // is only loaded from disk once SaveManager.OnConnected runs.  On a reconnect,
            // all historical items replay before OnConnected completes, so items with
            // index <= the saved watermark can sneak into the queue.  Checking again here
            // (with the frozen connect-time watermark) prevents double-grants.
            //
            // We use _connectTimeWatermark (the disk watermark AFTER the stale-watermark sanity
            // check, i.e. -1 if ForceLastItemIndex reset it) rather than the live LastItemIndex.
            // Using the live value caused items to be wrongly skipped when the disk watermark
            // was stale from an old AP slot (e.g. 9018) even though the sanity check had already
            // reset it to -1 so all historical items should replay.
            // Exception: live items (idx >= _snapshotCount) were generated after login and
            // must always be applied even if the saved watermark is stale.
            bool isLiveItem = (entry.index >= _snapshotCount);
            if (!isLiveItem && entry.index <= _connectTimeWatermark)
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
                // An unhandled exception inside Apply would normally abort the foreach,
                // silently dropping every remaining item in `pending` (they've already been
                // dequeued and are not going back into the queue).  Catching here lets the
                // loop continue and logs enough detail to diagnose the root cause.
                Logger.Error(
                    $"[AP] Exception applying item {entry.item.ItemName} " +
                    $"(id={entry.item.ItemId}, idx={entry.index}): {ex}");
            }
#if DEBUG
            SlimeRancher2AP.Utils.DebugTrace.Once($"ProcessItemQueue.4 — Apply returned for first item idx={entry.index}");
#endif
        }
    }

    /// <summary>Re-enqueues an item that could not be applied yet (e.g., scene not ready).</summary>
    public void RequeueItem(ItemInfo item, int index)
    {
        lock (_queueLock) { _itemQueue.Enqueue((item, index)); }
    }

    /// <summary>
    /// Clears the item queue and re-enqueues every item in the server's AllItemsReceived
    /// snapshot starting from index 0, then resets the watermark to -1.
    ///
    /// Used when the player starts a <b>new SR2 game</b> while connected to an AP slot that
    /// already has history (e.g. start items, or a previously-played SR2 run on the same slot).
    /// Without this, the watermark from the previous run prevents items 0..watermark from
    /// being applied to the fresh SR2 save — the player would start with missing items.
    ///
    /// Ephemerals (filler, traps) that are already in <c>_ephemeralSet</c> are skipped to
    /// avoid refiring one-shot effects from a previous run on the same AP slot.
    /// For a genuinely new AP slot, <c>_ephemeralSet</c> is empty, so all items replay.
    ///
    /// Must be called on the main thread (from a Harmony Prefix/Postfix or game event).
    /// </summary>
    public void RequestFullReplay()
    {
        if (Session == null)
        {
            Logger.Warning("[AP] RequestFullReplay: no active session — skipped");
            return;
        }

        Plugin.Instance.SaveManager.ForceLastItemIndex(-1);
        // Also reset _connectTimeWatermark: the secondary dedup in ProcessItemQueue uses
        // this frozen value to skip items that were already applied before this session.
        // After RequestFullReplay resets the watermark to -1, we must lower the threshold
        // here too — otherwise ProcessItemQueue's secondary dedup would kill items at
        // indices 0.._connectTimeWatermark (the old connect-time value), which are exactly
        // the items we just re-queued for the fresh SR2 save.
        _connectTimeWatermark = -1;

        var snapshot = Session.Items.AllItemsReceived.ToList();
        int enqueued = 0;
        lock (_queueLock)
        {
            _itemQueue.Clear();
            for (int i = 0; i < snapshot.Count; i++)
            {
                // Skip ephemerals already applied in a previous SR2 run on this AP slot.
                // Non-ephemerals (upgrades, gadgets, regions) are idempotent and always queued.
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
    /// Records a location check locally and, if connected, reports it to the AP server.
    /// Safe to call offline — checks accumulate locally and are flushed on the next connect.
    /// Idempotent: already-checked locations are silently skipped.
    /// Shows a notification via StatusHUD based on persisted or live scout data.
    /// </summary>
    public void SendCheck(long locationId)
    {
        if (!Plugin.Instance.ModEnabled) return;
        if (!Plugin.Instance.SaveManager.HasActiveSession) return;
        if (Plugin.Instance.SaveManager.IsChecked(locationId)) return;

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

    /// <summary>
    /// Shows a StatusHUD notification for a location check, using scout data to describe
    /// what item was found and for whom.
    /// </summary>
    private void ShowCheckNotification(long locationId)
    {
        // Try live cache first (fresh from server), then fall back to persisted data.
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
            // No scout data — fall back to location name from table or raw ID.
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
