using BepInEx.Configuration;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace SlimeRancher2AP.SaveData;

/// <summary>
/// Persists AP progress to one BepInEx ConfigFile per {seed}_{slotName}, written by Flush() at
/// most once per frame. Scout data sits beside it as JSON, for offline check notifications.
/// </summary>
public class ApSaveManager
{
    // DTO stored in the scout JSON file
    public record PersistedScout
    {
        [JsonPropertyName("item")]   public string ItemName   { get; init; } = "";
        [JsonPropertyName("player")] public string PlayerName { get; init; } = "";
        [JsonPropertyName("game")]   public string PlayerGame { get; init; } = "";
        [JsonPropertyName("prog")]   public bool   IsProgression { get; init; }
        [JsonPropertyName("trap")]   public bool   IsTrap        { get; init; }
        /// <summary>ItemFlags.NeverExclude — Archipelago's "useful" tier.</summary>
        [JsonPropertyName("useful")] public bool   IsUseful      { get; init; }

        /// <summary>Display tier; Trap is tested first so a trap never reads as Progression.</summary>
        public string Tier =>
            IsTrap        ? "Trap"
          : IsProgression ? "Progression"
          : IsUseful      ? "Useful"
          :                 "Filler";
    }

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private readonly ConfigFile _pluginConfig;

    private ConfigFile?          _saveFile;
    private ConfigEntry<string>? _slotDataJson;
    private ConfigEntry<string>? _checkedLocations;
    private ConfigEntry<int>?    _lastItemIndex;
    private ConfigEntry<string>? _unlockedRegions;
    private ConfigEntry<string>? _visitedZones;
    private ConfigEntry<long>?   _newbucksEarned;
    private ConfigEntry<int>?    _shopCatalogsHeld;
    private ConfigEntry<string>? _appliedEphemeralIndices;
    private ConfigEntry<string>? _deferredItemIndices;
    private ConfigEntry<string>? _associatedSaveName;
    private ConfigEntry<string>? _plortsSold;
    private ConfigEntry<bool>?   _goalReached;
    private ConfigEntry<string>? _labyrinthGatesOpened;

    private readonly Dictionary<string, long> _plortsSoldMap = new();   // by plort type name

    private readonly HashSet<long>   _checkedSet      = new();
    private readonly HashSet<string> _regionSet       = new();
    private readonly HashSet<string> _visitedZoneSet  = new();
    private readonly HashSet<int>    _ephemeralSet    = new();
    // Labyrinth beam-gate keys ("scene:switchName") seen opening, for the labyrinth_open goal.
    private readonly HashSet<string> _labyrinthGateSet = new();
    // Received items still waiting to apply (parked traps, held expansions). Persisted so a
    // disconnect after the watermark passes them re-queues them instead of losing them.
    private readonly HashSet<int>    _deferredSet     = new();
    // volatile: written on the connection thread, read on the main and network threads.
    private volatile int  _lastItemIdx      = -1;
    private volatile bool _sessionActive    = false;
    private volatile bool _saveBound       = false;
    private long _newbucksEarnedVal = 0;
    private int  _shopCatalogsHeldVal = 0;
    private bool _goalReachedVal = false;

    // Mutators set _dirty; Flush() writes everything in one Save — per frame, before
    // ResetSession, and on quit. _dirty is volatile (some mutators run on the connection
    // thread); _ioLock stops Flush racing LoadSaveState.
    private volatile bool _dirty;
    private bool _warnedFlushFailure;
    private readonly object _ioLock = new();

    private Dictionary<long, PersistedScout> _scoutData = new();
    private string? _scoutFilePath;

    // -------------------------------------------------------------------------
    // Public state
    // -------------------------------------------------------------------------

    public int  LastItemIndex   => _lastItemIdx;
    /// <summary>Newbucks earned in-game this AP run (the "newbucks" goal counter).</summary>
    public long NewbucksEarned  => _newbucksEarnedVal;

    /// <summary>
    /// Shop Catalog copies applied. Persisted because the server snapshot reads 0 offline;
    /// <see cref="Archipelago.ShopCatalogHandler"/> uses it as a floor.
    /// </summary>
    public int  ShopCatalogsHeld => _shopCatalogsHeldVal;

    /// <summary>True once <see cref="OnConnected"/> has started for the current session.</summary>
    public bool HasActiveSession => _sessionActive;

    /// <summary>
    /// True when the AP save file is bound, online or offline (<see cref="BindOfflineSave"/>).
    /// Gates what must work offline too — recording checks, suppressing vanilla fabricator
    /// grants — unlike <see cref="HasActiveSession"/>.
    /// </summary>
    public bool IsSaveBound => _saveBound;

    /// <summary>Slot data JSON from the last connect, or empty.</summary>
    public string SlotDataJson => _slotDataJson?.Value ?? "";

    /// <summary>Persists the raw slot data so an offline session can reload the options.</summary>
    public void PersistSlotData(string json)
    {
        lock (_ioLock)
        {
            if (_slotDataJson == null || string.IsNullOrEmpty(json)) return;
            if (_slotDataJson.Value == json) return;   // unchanged — avoid rewriting the file
            _slotDataJson.Value = json;
            MarkDirty();
        }
    }

    /// <summary>Every locally checked location ID.</summary>
    public IReadOnlyCollection<long> CheckedLocations => _checkedSet;

    public ApSaveManager(ConfigFile pluginConfig)
    {
        _pluginConfig = pluginConfig;
    }

    // -------------------------------------------------------------------------
    // Session lifecycle
    // -------------------------------------------------------------------------

    /// <summary>
    /// Loads <see cref="LastItemIndex"/> before connecting, so history replayed during login is
    /// not queued (and requeued forever) before <c>OnConnected</c> reads it. Fine if no file exists.
    /// </summary>
    public void PreloadLastItemIndex(string seed, string slotName)
    {
        // Never overwrite a live session's watermark (e.g. LoadGamePatch for another save).
        var safeSlot  = string.Concat(slotName.Split(Path.GetInvalidFileNameChars()));
        var dir       = Path.Combine(BepInEx.Paths.ConfigPath, "SlimeRancher2-AP");
        var cfgPath   = Path.Combine(dir, $"AP_{seed}_{safeSlot}.cfg");

        if (HasActiveSession)
        {
            // Log what the file says, but don't apply it.
            if (File.Exists(cfgPath))
            {
                try
                {
                    var peekFile  = new ConfigFile(cfgPath, false);
                    var peekEntry = peekFile.Bind("Progress", "LastItemIndex", -1,
                        "Index of last applied item (dedup key for reconnect)");
                    Logger.Info(
                        $"[AP] PreloadLastItemIndex: skipped — session already active " +
                        $"(seed={seed} slot={slotName}, file says {peekEntry.Value}, " +
                        $"current _lastItemIdx={_lastItemIdx})");
                }
                catch
                {
                    Logger.Info(
                        $"[AP] PreloadLastItemIndex: skipped — session already active " +
                        $"(seed={seed} slot={slotName}, could not peek file, current _lastItemIdx={_lastItemIdx})");
                }
            }
            else
            {
                Logger.Info(
                    $"[AP] PreloadLastItemIndex: skipped — session already active " +
                    $"(seed={seed} slot={slotName}, file does not exist)");
            }
            return;
        }

        if (!File.Exists(cfgPath)) return;

        try
        {
            var tempFile = new ConfigFile(cfgPath, false);
            var entry    = tempFile.Bind("Progress", "LastItemIndex", -1,
                "Index of last applied item (dedup key for reconnect)");
            _lastItemIdx = entry.Value;
            Logger.Info(
                $"[AP] PreloadLastItemIndex: seed={seed} slot={slotName} → LastItemIndex={_lastItemIdx}");
        }
        catch (Exception ex)
        {
            Logger.Warning(
                $"[AP] PreloadLastItemIndex failed for seed={seed} slot={slotName}: {ex.Message}");
        }
    }

    /// <summary>
    /// Flushes, then unbinds the save file and resets the watermark to -1, so nothing applies
    /// against the previous connection's file. Call on every disconnect.
    /// </summary>
    public void ResetSession()
    {
        Flush();

        lock (_ioLock)
        {
            // Flag first, so nothing sees a half-torn-down session.
            _sessionActive           = false;
            _saveBound               = false;
            _saveFile                = null;
            _checkedLocations        = null;
            _lastItemIndex           = null;
            _unlockedRegions         = null;
            _visitedZones            = null;
            _newbucksEarned          = null;
            _shopCatalogsHeld        = null;
            _appliedEphemeralIndices = null;
            _deferredItemIndices     = null;
            _associatedSaveName      = null;
            _plortsSold              = null;
            _plortsSoldMap.Clear();
            _goalReached             = null;
            _labyrinthGatesOpened    = null;
            _goalReachedVal          = false;
            _labyrinthGateSet.Clear();
            _lastItemIdx             = -1;
            _dirty                   = false;
            // The other sets are reloaded by the next bind.
        }
    }

    // -------------------------------------------------------------------------
    // Persistence
    // -------------------------------------------------------------------------

    private void MarkDirty() => _dirty = true;

    /// <summary>Writes pending changes in one save; no-op when clean. Main thread.</summary>
    public void Flush()
    {
        if (!_dirty) return;
        lock (_ioLock)
        {
            if (_saveFile == null) { _dirty = false; return; }
            _dirty = false;
            try
            {
                _checkedLocations!.Value        = string.Join(",", _checkedSet);
                _lastItemIndex!.Value           = _lastItemIdx;
                _unlockedRegions!.Value         = string.Join(",", _regionSet);
                _visitedZones!.Value            = string.Join(",", _visitedZoneSet);
                _newbucksEarned!.Value          = _newbucksEarnedVal;
                _shopCatalogsHeld!.Value        = _shopCatalogsHeldVal;
                _appliedEphemeralIndices!.Value = string.Join(",", _ephemeralSet);
                _deferredItemIndices!.Value     = string.Join(",", _deferredSet);
                _plortsSold!.Value              = string.Join(",", _plortsSoldMap.Select(kv => $"{kv.Key}:{kv.Value}"));
                _goalReached!.Value             = _goalReachedVal;
                _labyrinthGatesOpened!.Value    = string.Join(",", _labyrinthGateSet);
                _saveFile.Save();
                _warnedFlushFailure = false;
            }
            catch (Exception ex)
            {
                _dirty = true;   // retry next frame
                if (!_warnedFlushFailure)
                {
                    _warnedFlushFailure = true;
                    Logger.Warning($"[AP] Could not write AP save file (will keep retrying): {ex.Message}");
                }
            }
        }
    }

    /// <summary>The SR2 save this slot is bound to, or "" (see <c>SaveGuard</c>).</summary>
    public string AssociatedSaveName => _associatedSaveName?.Value ?? "";

    /// <summary>Binds this AP slot to <paramref name="saveName"/> (persisted).</summary>
    public void AssociateSave(string saveName)
    {
        lock (_ioLock)
        {
            if (_associatedSaveName == null) return;
            _associatedSaveName.Value = saveName;
            MarkDirty();
        }
    }

    /// <summary>After login: binds the seed+slot save file and loads its progress.</summary>
    public void OnConnected(string seed, string slotName)
    {
        // Active first, so a concurrent PreloadLastItemIndex backs off.
        _sessionActive = true;
        LoadSaveState(seed, slotName);
    }

    /// <summary>
    /// Binds the save file without a connection, so offline play records checks and keeps
    /// fabricator suppression. Leaves <see cref="HasActiveSession"/> false.
    /// </summary>
    public void BindOfflineSave(string seed, string slotName)
    {
        if (_sessionActive) return;   // a live session already owns the binding
        LoadSaveState(seed, slotName);
        Logger.Info(
            $"[AP] Offline save binding: seed={seed} slot={slotName} — checks will be recorded " +
            "locally and flushed on the next connect.");
    }

    /// <summary>Binds the per-slot file and loads its progress.</summary>
    private void LoadSaveState(string seed, string slotName)
    {
        lock (_ioLock) { LoadSaveStateLocked(seed, slotName); }
    }

    private void LoadSaveStateLocked(string seed, string slotName)
    {
        _saveBound = true;

        var safeSlot = string.Concat(slotName.Split(Path.GetInvalidFileNameChars()));
        var dir      = Path.Combine(BepInEx.Paths.ConfigPath, "SlimeRancher2-AP");
        Directory.CreateDirectory(dir);

        var baseName = $"AP_{seed}_{safeSlot}";
        _saveFile     = new ConfigFile(Path.Combine(dir, baseName + ".cfg"), true)
        {
            // Writes go through Flush().
            SaveOnConfigSet = false,
        };
        _scoutFilePath = Path.Combine(dir, baseName + "_scouts.json");

        _checkedLocations        = _saveFile.Bind("Progress", "CheckedLocations", "",
            "Comma-separated checked location IDs");
        _lastItemIndex           = _saveFile.Bind("Progress", "LastItemIndex", -1,
            "Index of last applied item (dedup key for reconnect)");
        _unlockedRegions         = _saveFile.Bind("Progress", "UnlockedRegions", "",
            "Comma-separated unlocked region names");
        _visitedZones            = _saveFile.Bind("Progress", "VisitedZones", "",
            "Comma-separated SceneGroup.ReferenceId strings for zones the player has physically visited");
        _newbucksEarned          = _saveFile.Bind("Progress", "NewbucksEarned", 0L,
            "Cumulative newbucks earned this AP run (tracked via PlayerState.AddCurrency)");
        _shopCatalogsHeld        = _saveFile.Bind("Progress", "ShopCatalogsHeld", 0,
            "Progressive Shop Catalog copies applied to this save. Persisted so the shop shows " +
            "the right waves offline, where the server snapshot is unavailable.");
        _appliedEphemeralIndices = _saveFile.Bind("Progress", "AppliedEphemeralIndices", "",
            "Comma-separated item indices of filler/trap items already applied — never re-applied on replay");
        _deferredItemIndices     = _saveFile.Bind("Progress", "DeferredItemIndices", "",
            "Comma-separated item indices of received-but-not-yet-applied items (rate-limited " +
            "traps, held conservatory expansions). Re-queued on reconnect if the watermark has " +
            "already advanced past their index.");
        _associatedSaveName      = _saveFile.Bind("Progress", "AssociatedSaveName", "",
            "The SR2 save game this AP slot is bound to (set on the first in-save session). " +
            "Loading a different save while connected pauses item delivery and checks. " +
            "Clear this value to re-associate the slot with the next save you load.");
        _slotDataJson            = _saveFile.Bind("Progress", "SlotDataJson", "",
            "Raw slot data from the last successful connect, as JSON. Reloaded when the save is " +
            "opened offline so option-gated checks (pods, gordos, drones, shop, market) still " +
            "evaluate correctly with no server.");
        _plortsSold              = _saveFile.Bind("Progress", "PlortsSold", "",
            "Per-type plorts sold at the market, as 'PinkPlort:12,RockPlort:3,...' " +
            "(tracked via PlortEconomyDirector.RegisterSold for the plort_seller goal)");
        _goalReached             = _saveFile.Bind("Progress", "GoalReached", false,
            "True once this slot's goal has been met in game. Recorded even while offline, and " +
            "reported to the server on every connect until then.");
        _labyrinthGatesOpened    = _saveFile.Bind("Progress", "LabyrinthGatesOpened", "",
            "Comma-separated Grey Labyrinth beam gates seen opening ('scene:switch'), so the " +
            "labyrinth_open goal can be completed across sessions");

        _checkedSet.Clear();
        foreach (var s in (_checkedLocations.Value ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries))
            if (long.TryParse(s, out var id)) _checkedSet.Add(id);

        _regionSet.Clear();
        foreach (var r in (_unlockedRegions.Value ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries))
            if (!string.IsNullOrWhiteSpace(r)) _regionSet.Add(r);

        _visitedZoneSet.Clear();
        foreach (var z in (_visitedZones.Value ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries))
            if (!string.IsNullOrWhiteSpace(z)) _visitedZoneSet.Add(z);

        _ephemeralSet.Clear();
        foreach (var s in (_appliedEphemeralIndices.Value ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries))
            if (int.TryParse(s, out var idx)) _ephemeralSet.Add(idx);

        _deferredSet.Clear();
        foreach (var s in (_deferredItemIndices.Value ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries))
            if (int.TryParse(s, out var idx)) _deferredSet.Add(idx);

        // "PinkPlort:12,RockPlort:3,..."
        _plortsSoldMap.Clear();
        foreach (var pair in (_plortsSold.Value ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries))
        {
            var sep = pair.IndexOf(':');
            if (sep > 0 && long.TryParse(pair[(sep + 1)..], out var sold))
                _plortsSoldMap[pair[..sep]] = sold;
        }

        _labyrinthGateSet.Clear();
        foreach (var g in (_labyrinthGatesOpened.Value ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries))
            if (!string.IsNullOrWhiteSpace(g)) _labyrinthGateSet.Add(g);

        _goalReachedVal     = _goalReached.Value;
        _lastItemIdx        = _lastItemIndex.Value;
        _newbucksEarnedVal  = _newbucksEarned.Value;
        _shopCatalogsHeldVal = _shopCatalogsHeld?.Value ?? 0;
        Logger.Info($"[AP] OnConnected: _lastItemIdx loaded from disk = {_lastItemIdx}");

        _scoutData.Clear();
        LoadScoutFile();

        Logger.Info(
            $"[AP] Save file: {Path.GetFileName(_saveFile.ConfigFilePath)} — " +
            $"LastItemIndex on disk={_lastItemIndex.Value}, " +
            $"EphemeralIndices on disk='{_appliedEphemeralIndices!.Value}', " +
            $"DeferredItems on disk='{_deferredItemIndices!.Value}'");
        Logger.Info(
            $"[AP] Save loaded: {_checkedSet.Count} locations checked, " +
            $"last item index {_lastItemIdx}, {_regionSet.Count} regions unlocked, " +
            $"{_scoutData.Count} scout entries cached.");
    }

    // -------------------------------------------------------------------------
    // Location checks
    // -------------------------------------------------------------------------

    public bool IsChecked(long id)              => _checkedSet.Contains(id);
    public bool IsRegionUnlocked(string name)   => _regionSet.Contains(name);
    public bool HasVisitedZone(string sgRef)    => _visitedZoneSet.Contains(sgRef);

    public void MarkChecked(long id)
    {
        if (_saveFile == null || !_checkedSet.Add(id)) return;
        MarkDirty();
    }

    public void UnlockRegion(string name)
    {
        if (_saveFile == null) return;
        bool added = _regionSet.Add(name);
        Logger.Info($"[AP] UnlockRegion: '{name}' — {(added ? "newly unlocked" : "already unlocked")}");
        if (!added) return;
        MarkDirty();
    }

    /// <summary>Records a first visit to a SceneGroup (the teleport trap's pool).</summary>
    public void MarkZoneVisited(string sgRef)
    {
        if (!_visitedZoneSet.Add(sgRef)) return;
        if (_saveFile == null) return;              // in-memory only
        MarkDirty();
        Logger.Info($"[AP] Zone visited (first time): '{sgRef}'");
    }

    public void UpdateLastItemIndex(int idx)
    {
        if (_saveFile == null)
        {
            Logger.Warning($"[AP] UpdateLastItemIndex({idx}) SKIPPED — no save file open");
            return;
        }
        if (idx <= _lastItemIdx)
        {
            // Normally off by a few at most.
            if (_lastItemIdx - idx > 100)
                Logger.Warning(
                    $"[AP] UpdateLastItemIndex({idx}) SKIPPED — current watermark={_lastItemIdx} " +
                    $"(gap={_lastItemIdx - idx}, possible watermark corruption)");
            return;
        }
        Logger.Info($"[AP] UpdateLastItemIndex: {_lastItemIdx} → {idx}");
        _lastItemIdx = idx;
        MarkDirty();
    }

    /// <summary>True if the one-shot item at this index was already applied (survives watermark resets).</summary>
    public bool IsEphemeralApplied(int idx) => _ephemeralSet.Contains(idx);

    public void MarkEphemeralApplied(int idx)
    {
        if (_saveFile == null || !_ephemeralSet.Add(idx)) return;
        MarkDirty();
    }

    // -------------------------------------------------------------------------
    // Deferred item persistence
    // -------------------------------------------------------------------------

    /// <summary>Received-but-unapplied item indices (see <c>_deferredSet</c>).</summary>
    public IReadOnlyCollection<int> GetPendingDeferredItems() => _deferredSet;

    public bool IsDeferredItem(int idx) => _deferredSet.Contains(idx);

    public void AddDeferredItem(int idx)
    {
        if (_saveFile == null || !_deferredSet.Add(idx)) return;
        MarkDirty();
    }

    public void RemoveDeferredItem(int idx)
    {
        if (_saveFile == null || !_deferredSet.Remove(idx)) return;
        MarkDirty();
    }

    /// <summary>Sets the watermark even if lower — to correct a stale one.</summary>
    public void ForceLastItemIndex(int idx)
    {
        Logger.Info($"[AP] ForceLastItemIndex: {_lastItemIdx} → {idx}");
        _lastItemIdx = idx;
        if (_saveFile == null) return;
        MarkDirty();
    }

    public void AccumulateNewbucks(int amount)
    {
        if (_saveFile == null || amount <= 0) return;
        _newbucksEarnedVal += amount;
        MarkDirty();
    }

    public void AccumulateShopCatalog()
    {
        if (_saveFile == null) return;
        _shopCatalogsHeldVal += 1;
        MarkDirty();
    }

    public void AccumulatePlortSold(string plortName, int count)
    {
        if (_saveFile == null || _plortsSold == null || count <= 0 || string.IsNullOrEmpty(plortName))
            return;
        _plortsSoldMap[plortName] = _plortsSoldMap.GetValueOrDefault(plortName) + count;
        MarkDirty();
    }

    public long PlortsSold(string plortName) => _plortsSoldMap.GetValueOrDefault(plortName);

    // -------------------------------------------------------------------------
    // Goal
    // -------------------------------------------------------------------------

    /// <summary>True once this slot's goal has been met in game (persisted).</summary>
    public bool GoalReached => _goalReachedVal;

    public void MarkGoalReached()
    {
        if (_saveFile == null || _goalReachedVal) return;
        _goalReachedVal = true;
        MarkDirty();
    }

    public bool IsLabyrinthGateOpened(string key) => _labyrinthGateSet.Contains(key);

    /// <summary>Returns true only when newly recorded.</summary>
    public bool MarkLabyrinthGateOpened(string key)
    {
        if (_saveFile == null || !_labyrinthGateSet.Add(key)) return false;
        MarkDirty();
        return true;
    }

    // -------------------------------------------------------------------------
    // Scout data
    // -------------------------------------------------------------------------

    public PersistedScout? GetScout(long locationId)
        => _scoutData.TryGetValue(locationId, out var s) ? s : null;

    public void UpdateAndSaveScoutData(Dictionary<long, PersistedScout> data)
    {
        _scoutData = data;
        if (_scoutFilePath == null) return;
        try
        {
            File.WriteAllText(_scoutFilePath, JsonSerializer.Serialize(_scoutData, JsonOpts));
            Logger.Info($"[AP] Scout data saved: {_scoutData.Count} entries.");
        }
        catch (Exception ex)
        {
            Logger.Warning($"[AP] Could not persist scout data: {ex.Message}");
        }
    }

    // -------------------------------------------------------------------------
    // Deletion
    // -------------------------------------------------------------------------

    /// <summary>Deletes the slot's progress and scout files (from <c>DeleteGamePatch</c>).</summary>
    public static void DeleteSaveData(string seed, string slotName)
    {
        var safeSlot = string.Concat(slotName.Split(Path.GetInvalidFileNameChars()));
        var dir      = Path.Combine(BepInEx.Paths.ConfigPath, "SlimeRancher2-AP");
        var baseName = $"AP_{seed}_{safeSlot}";

        TryDelete(Path.Combine(dir, baseName + ".cfg"),          "progress config");
        TryDelete(Path.Combine(dir, baseName + "_scouts.json"),  "scout data");
    }

    private static void TryDelete(string path, string label)
    {
        if (!File.Exists(path)) return;
        try
        {
            File.Delete(path);
            Logger.Info($"[AP] Deleted AP {label}: {Path.GetFileName(path)}");
        }
        catch (Exception ex)
        {
            Logger.Warning(
                $"[AP] Could not delete AP {label} '{Path.GetFileName(path)}': {ex.Message}");
        }
    }

    private void LoadScoutFile()
    {
        if (_scoutFilePath == null || !File.Exists(_scoutFilePath)) return;
        try
        {
            var loaded = JsonSerializer.Deserialize<Dictionary<long, PersistedScout>>(
                File.ReadAllText(_scoutFilePath), JsonOpts);
            if (loaded != null) _scoutData = loaded;
        }
        catch (Exception ex)
        {
            Logger.Warning($"[AP] Could not load scout data: {ex.Message}");
        }
    }
}
