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

    // Queue holds (item, index) tuples. Index is a local counter tracking receipt order,
    // used to skip items already applied in a previous session (reconnect dedup).
    private readonly Queue<(ItemInfo item, int index)> _itemQueue = new();
    private readonly object _queueLock = new();
    private int _receivedCount = 0;

    // In-memory scout cache — populated after connect, then refreshed from server.
    // Also readable from ApSaveManager for offline sessions.
    private Dictionary<long, ScoutedItemInfo>? _liveScoutCache;

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
        Task.Run(() =>
        {
            try
            {
                Session = ArchipelagoSessionFactory.CreateSession(data.Uri, data.Port);

                // Register item handler BEFORE login so historical items replay on reconnect
                Session.Items.ItemReceived += OnItemReceived;
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

                    SlotData = SlotData.Parse(success.SlotData);

                    if (SlotData.DeathLink)
                    {
                        DeathLink = new DeathLinkHandler(Session, data.SlotName);
                        DeathLink.Enable();
                    }

                    Plugin.Instance.SaveManager.OnConnected(data.Seed, data.SlotName);
                    GoalHandler.Initialize();

                    Plugin.Instance.Log.LogInfo(
                        $"[AP] Connected as '{data.SlotName}' (slot {data.Slot}, seed {data.Seed})");
                    OnConnected?.Invoke();

                    // Flush any location checks accumulated offline, then scout all locations.
                    FlushPendingChecks();
                    _ = ScoutAllLocationsAsync(Session);
                }
                else if (result is LoginFailure failure)
                {
                    var errors = string.Join(", ", failure.Errors);
                    Plugin.Instance.Log.LogError($"[AP] Login failed: {errors}");
                    OnConnectionFailed?.Invoke(errors);
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
        Session         = null;
        SlotData        = null;
        _receivedCount  = 0;
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
        var item = helper.DequeueItem();
        var idx  = _receivedCount++;
        if (idx > Plugin.Instance.SaveManager.LastItemIndex)
            lock (_queueLock) { _itemQueue.Enqueue((item, idx)); }
    }

    /// <summary>
    /// Processes items from the queue on the Unity main thread (called from ApUpdateBehaviour.Update).
    /// </summary>
    public void ProcessItemQueue()
    {
        if (!IsConnected) return;
#if DEBUG
        SlimeRancher2AP.Utils.DebugTrace.Once("ProcessItemQueue.1 — entered while connected");
#endif
        lock (_queueLock)
        {
#if DEBUG
            SlimeRancher2AP.Utils.DebugTrace.Once($"ProcessItemQueue.2 — inside lock, queue count={_itemQueue.Count}");
#endif
            while (_itemQueue.TryDequeue(out var entry))
            {
#if DEBUG
                SlimeRancher2AP.Utils.DebugTrace.Once($"ProcessItemQueue.3 — first dequeued item id={entry.item.ItemId} idx={entry.index}");
#endif
                ItemHandler.Apply(entry.item, entry.index);
#if DEBUG
                SlimeRancher2AP.Utils.DebugTrace.Once($"ProcessItemQueue.4 — Apply returned for first item idx={entry.index}");
#endif
            }
        }
#if DEBUG
        SlimeRancher2AP.Utils.DebugTrace.Once("ProcessItemQueue.5 — exited lock");
#endif
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
