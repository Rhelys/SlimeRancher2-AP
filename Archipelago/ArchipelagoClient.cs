using Archipelago.MultiClient.Net;
using Archipelago.MultiClient.Net.BounceFeatures.DeathLink;
using Archipelago.MultiClient.Net.Enums;
using Archipelago.MultiClient.Net.Helpers;
using Archipelago.MultiClient.Net.Models;
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

    // Queue holds (item, index) tuples. Index is a local counter (_receivedCount) that
    // tracks receipt order within this connection. It matches the AP server's own position
    // counter because AllItems causes a full replay from 0 on every connect.
    // Used to skip items already applied in a previous session (reconnect dedup).
    private readonly Queue<(ItemInfo item, int index)> _itemQueue = new();
    private readonly object _queueLock = new();
    // volatile: OnItemReceived runs on the network thread; Connect() writes this field on
    // the background task thread.  volatile ensures the network thread always sees the
    // value written by Connect() (_receivedCount = snapshot.Count) rather than a stale
    // cached copy.  Also, ++ in OnItemReceived should be Interlocked for atomicity in
    // case the library can fire ItemReceived concurrently (not expected, but safe).
    private volatile int _receivedCount = 0;

    // Number of items in AllItemsReceived at login time (after the sanity check + historical
    // replay).  Any item whose index >= _snapshotCount is a *live* item generated after
    // login and must always be queued — even if the saved watermark is stale/high.
    // This prevents the race where OnConnected writes _lastItemIdx=9017 from the config
    // before ForceLastItemIndex(0) corrects it, causing a fast live item to be skipped.
    private volatile int _snapshotCount = 0;

    // In-memory scout cache — populated after connect, then refreshed from server.
    // Also readable from ApSaveManager for offline sessions.
    private Dictionary<long, ScoutedItemInfo>? _liveScoutCache;

    // Actions that must run on the Unity main thread after a successful login.
    // Set on the network thread, consumed by ProcessItemQueue on the main thread.
    // Volatile so the main thread always sees the latest write without a full lock.
    private volatile Action? _pendingMainThreadAction;

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
                    Plugin.Instance.Log.LogError($"[AP] Socket error: {msg} — {ex?.Message}");
                Session.Socket.SocketClosed += reason =>
                {
                    Plugin.Instance.Log.LogWarning($"[AP] Disconnected: {reason}");
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
                    Plugin.Instance.Log.LogInfo(
                        $"[AP] SlotData: goal='{SlotData.Goal}' region_access_mode='{SlotData.RegionAccessMode}' " +
                        $"conversation_checks='{SlotData.ConversationChecks}'");

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
                        Plugin.Instance.Log.LogWarning(
                            $"[AP] Watermark ({savedWatermark}) exceeds server history ({snapshot.Count} item(s)) — " +
                            $"resetting to -1 and replaying all {snapshot.Count} item(s).");
                        Plugin.Instance.SaveManager.ForceLastItemIndex(-1);
                        savedWatermark = -1;
                    }

                    int enqueued = 0;
                    lock (_queueLock)
                    {
                        for (int i = savedWatermark + 1; i < snapshot.Count; i++)
                        {
                            _itemQueue.Enqueue((snapshot[i], i));
                            enqueued++;
                        }
                    }
                    _receivedCount = snapshot.Count;
                    _snapshotCount = snapshot.Count; // live-item boundary: idx >= this means freshly generated
                    Plugin.Instance.Log.LogInfo(
                        $"[AP] Historical replay: {snapshot.Count} item(s) in history, " +
                        $"watermark={savedWatermark}, {enqueued} new item(s) queued.");

                    // Register ItemReceived NOW so only live (post-login) items come through.
                    // _receivedCount is already set to snapshot.Count, so new items get
                    // sequential indices starting from there.
                    Session.Items.ItemReceived += OnItemReceived;

                    // Flush offline checks and start scout from the background thread.
                    FlushPendingChecks();
                    var capturedSession = Session;
                    _ = ScoutAllLocationsAsync(capturedSession);

                    Plugin.Instance.Log.LogInfo(
                        $"[AP] Connected as '{data.SlotName}' (slot {data.Slot}, seed {data.Seed})");

                    // GoalHandler.Initialize() and OnConnected touch Unity APIs — defer to main thread.
                    _pendingMainThreadAction = () =>
                    {
                        GoalHandler.Initialize();
                        OnConnected?.Invoke();
                    };
                }
                else if (result is LoginFailure failure)
                {
                    var errors = string.Join(", ", failure.Errors);
                    Plugin.Instance.Log.LogError($"[AP] Login failed: {errors}");
                    // OnConnectionFailed also touches UI — defer to main thread.
                    _pendingMainThreadAction = () => OnConnectionFailed?.Invoke(errors);
                    Session = null;
                }
            }
            catch (Exception ex)
            {
                Plugin.Instance.Log.LogError($"[AP] Connection exception: {ex.Message}");
                OnConnectionFailed?.Invoke(ex.Message);
                Session = null;
            }
        });
    }

    public void Disconnect()
    {
        DeathLink?.Disable();
        DeathLink       = null;
        _liveScoutCache = null;
        // Unregister before nulling Session so the -= doesn't throw on a null ref.
        if (Session != null)
            Session.Items.ItemReceived -= OnItemReceived;
        Session = null;
        SlotData                 = null;
        _receivedCount           = 0;
        _snapshotCount           = 0;
        _pendingMainThreadAction = null;
        lock (_queueLock) { _itemQueue.Clear(); }
        // Reset the save-manager session pointer so HasActiveSession returns false.
        // Without this, a reconnect would see the previous session's _saveFile as
        // still valid and process items before the new OnConnected completes.
        Plugin.Instance.SaveManager.ResetSession();
        Plugin.Instance.Log.LogInfo("[AP] Disconnected.");
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
            Plugin.Instance.Log.LogInfo($"[AP] Scouted {result.Count} location(s).");
        }
        catch (Exception ex)
        {
            Plugin.Instance.Log.LogWarning($"[AP] Scout failed: {ex.Message}");
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
            Plugin.Instance.Log.LogInfo($"[AP] Flushed {pending.Count} location check(s) to server.");
        }
        catch (Exception ex)
        {
            Plugin.Instance.Log.LogWarning($"[AP] Flush failed: {ex.Message}");
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
#if DEBUG
        SlimeRancher2AP.Utils.DebugTrace.Once("[BG] OnItemReceived — first call (background thread)");
#endif
        var item       = helper.DequeueItem();
        var idx        = System.Threading.Interlocked.Increment(ref _receivedCount) - 1;
        var watermark  = Plugin.Instance.SaveManager.LastItemIndex;
        // idx >= _snapshotCount means this item was generated AFTER login — it is a live
        // item and must always be queued.  This guards against the race where the saved
        // watermark is stale (e.g. 9017 from a previous run) and hasn't been corrected
        // by ForceLastItemIndex yet at the moment this callback fires.
        var isLiveItem = (idx >= _snapshotCount);
        if (isLiveItem || idx > watermark)
        {
            lock (_queueLock) { _itemQueue.Enqueue((item, idx)); }
            Plugin.Instance.Log.LogInfo(
                $"[AP] Queued item: {item.ItemName} (id={item.ItemId}, idx={idx}, watermark={watermark}, live={isLiveItem})");
        }
        else
        {
            Plugin.Instance.Log.LogInfo(
                $"[AP] Skipped item (already applied): {item.ItemName} (id={item.ItemId}, idx={idx}, watermark={watermark})");
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

        foreach (var entry in pending)
        {
#if DEBUG
            SlimeRancher2AP.Utils.DebugTrace.Once($"ProcessItemQueue.3 — first dequeued item id={entry.item.ItemId} idx={entry.index}");
#endif
            // Secondary dedup: skip items whose index is at or below the saved watermark.
            //
            // OnItemReceived uses _lastItemIdx as a gate, but that field starts at -1 and
            // is only loaded from disk once SaveManager.OnConnected runs.  On a reconnect,
            // all historical items replay before OnConnected completes, so items with
            // index <= the saved watermark can sneak into the queue.  Checking again here
            // (with the now-correct watermark) prevents double-grants.
            // Exception: live items (idx >= _snapshotCount) were generated after login and
            // must always be applied even if the saved watermark is stale.
            bool isLiveItem = (entry.index >= _snapshotCount);
            if (!isLiveItem && entry.index <= Plugin.Instance.SaveManager.LastItemIndex)
            {
                Plugin.Instance.Log.LogInfo(
                    $"[AP] Skipped item (secondary dedup): {entry.item.ItemName} (id={entry.item.ItemId}, idx={entry.index}, watermark={Plugin.Instance.SaveManager.LastItemIndex})");
                continue;
            }

            ItemHandler.Apply(entry.item, entry.index);
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
            Plugin.Instance.Log.LogInfo($"[AP] Checked: {name} ({locationId})");
        }
        else
        {
            Plugin.Instance.Log.LogInfo($"[AP] Checked (offline): {locationId}");
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
        Plugin.Instance.Log.LogInfo("[AP] Goal achieved!");
    }
}
