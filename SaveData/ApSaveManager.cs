using BepInEx.Configuration;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace SlimeRancher2AP.SaveData;

/// <summary>
/// Persists Archipelago randomizer progress to a per-seed BepInEx ConfigFile.
/// One file per {seed}_{slotName} combination so different seeds/slots don't conflict.
/// The file is written immediately on any change to survive crashes.
///
/// Scout data (what item lives at each location) is stored alongside as a separate JSON
/// file so it persists across offline sessions and enables location-check notifications
/// without a live AP connection.
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
    }

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private readonly ConfigFile _pluginConfig;

    private ConfigFile?          _saveFile;
    private ConfigEntry<string>? _checkedLocations;
    private ConfigEntry<int>?    _lastItemIndex;
    private ConfigEntry<string>? _unlockedRegions;
    private ConfigEntry<long>?   _newbucksEarned;

    private readonly HashSet<long>   _checkedSet = new();
    private readonly HashSet<string> _regionSet  = new();
    // volatile: _lastItemIdx and _sessionActive are written on the background thread
    // (OnConnected / ResetSession) and read on the main thread (LoadGamePatch) and the
    // network-callback thread (OnItemReceived). Without volatile the JIT / CPU is free to
    // cache these in a register or L1 cache, causing the main thread to see stale values
    // and the HasActiveSession guard to fail to block PreloadLastItemIndex.
    private volatile int  _lastItemIdx      = -1;
    private volatile bool _sessionActive    = false;
    private long _newbucksEarnedVal = 0;

    // Scout data — loaded from JSON on connect, updated after fresh server scout.
    private Dictionary<long, PersistedScout> _scoutData = new();
    private string? _scoutFilePath;

    // -------------------------------------------------------------------------
    // Public state
    // -------------------------------------------------------------------------

    public int  LastItemIndex   => _lastItemIdx;
    /// <summary>Cumulative newbucks earned this AP run (tracked via PlayerState.AddCurrency Postfix).</summary>
    public long NewbucksEarned  => _newbucksEarnedVal;

    /// <summary>
    /// True once <see cref="OnConnected"/> has started processing the current session.
    /// Set volatile so the main-thread guard in LoadGamePatch always sees the up-to-date
    /// value written by the background connection thread.
    /// </summary>
    public bool HasActiveSession => _sessionActive;

    /// <summary>All locally-tracked checked location IDs (for flush-on-reconnect).</summary>
    public IReadOnlyCollection<long> CheckedLocations => _checkedSet;

    public ApSaveManager(ConfigFile pluginConfig)
    {
        _pluginConfig = pluginConfig;
    }

    // -------------------------------------------------------------------------
    // Session lifecycle
    // -------------------------------------------------------------------------

    /// <summary>
    /// Pre-loads <see cref="LastItemIndex"/> from the on-disk config file for the given
    /// seed + slot BEFORE the AP session connects. This prevents a race condition where
    /// <c>Session.Items.ItemReceived</c> fires (replaying historical items) during
    /// <c>TryConnectAndLogin</c> before <c>OnConnected</c> has had a chance to read the
    /// saved index, causing already-applied items to be enqueued and then requeued forever
    /// when the scene is not yet ready.
    ///
    /// Safe to call even if the file does not exist yet (first-time connect for this slot).
    /// </summary>
    public void PreloadLastItemIndex(string seed, string slotName)
    {
        // Do NOT overwrite the watermark if a session is already active.
        // OnConnected already set _lastItemIdx correctly for the running session.
        // Overwriting it here (e.g. when LoadGamePatch fires for a different save slot
        // while a session is live) would corrupt the in-memory watermark and cause newly
        // received items to be skipped as "already applied".
        if (HasActiveSession)
        {
            Plugin.Instance.Log.LogInfo(
                $"[AP] PreloadLastItemIndex: skipped — session already active (seed={seed} slot={slotName})");
            return;
        }

        var safeSlot  = string.Concat(slotName.Split(Path.GetInvalidFileNameChars()));
        var dir       = Path.Combine(BepInEx.Paths.ConfigPath, "SlimeRancher2-AP");
        var cfgPath   = Path.Combine(dir, $"AP_{seed}_{safeSlot}.cfg");

        if (!File.Exists(cfgPath)) return;

        try
        {
            // Read only the LastItemIndex entry to avoid full config deserialization.
            var tempFile = new ConfigFile(cfgPath, false);
            var entry    = tempFile.Bind("Progress", "LastItemIndex", -1,
                "Index of last applied item (dedup key for reconnect)");
            _lastItemIdx = entry.Value;
            Plugin.Instance.Log.LogInfo(
                $"[AP] PreloadLastItemIndex: seed={seed} slot={slotName} → LastItemIndex={_lastItemIdx}");
        }
        catch (Exception ex)
        {
            Plugin.Instance.Log.LogWarning(
                $"[AP] PreloadLastItemIndex failed for seed={seed} slot={slotName}: {ex.Message}");
        }
    }

    /// <summary>
    /// Clears the active-session state so <see cref="HasActiveSession"/> returns false.
    /// Must be called whenever the AP connection is dropped (explicit disconnect or
    /// before a reconnect attempt) so that <c>ProcessItemQueue</c> does not process
    /// items against a stale save file from the previous connection.
    ///
    /// Does NOT delete or flush any on-disk data — it only resets in-memory pointers.
    /// Also resets <see cref="LastItemIndex"/> to -1 so that
    /// <see cref="PreloadLastItemIndex"/> can set it cleanly for the new session.
    /// </summary>
    public void ResetSession()
    {
        // Clear the session flag FIRST so HasActiveSession returns false immediately,
        // preventing any concurrent LoadGamePatch from seeing a half-torn-down session.
        _sessionActive    = false;
        _saveFile         = null;
        _checkedLocations = null;
        _lastItemIndex    = null;
        _unlockedRegions  = null;
        _newbucksEarned   = null;
        _lastItemIdx      = -1;
        // Keep _checkedSet, _regionSet, _scoutData in memory — they'll be re-loaded
        // from the correct file by the next OnConnected call.
    }

    /// <summary>
    /// Called after a successful AP login. Opens (or creates) the save file keyed
    /// to this specific seed + slot combination and deserializes stored progress.
    /// Also loads persisted scout data if available.
    /// </summary>
    public void OnConnected(string seed, string slotName)
    {
        // Mark session active FIRST — before reading _lastItemIdx from disk — so that any
        // concurrent main-thread call to PreloadLastItemIndex sees HasActiveSession=true
        // and exits immediately, rather than overwriting the watermark we're about to load.
        _sessionActive = true;

        var safeSlot = string.Concat(slotName.Split(Path.GetInvalidFileNameChars()));
        var dir      = Path.Combine(BepInEx.Paths.ConfigPath, "SlimeRancher2-AP");
        Directory.CreateDirectory(dir);

        var baseName = $"AP_{seed}_{safeSlot}";
        _saveFile     = new ConfigFile(Path.Combine(dir, baseName + ".cfg"), true);
        _scoutFilePath = Path.Combine(dir, baseName + "_scouts.json");

        _checkedLocations = _saveFile.Bind("Progress", "CheckedLocations", "",
            "Comma-separated checked location IDs");
        _lastItemIndex    = _saveFile.Bind("Progress", "LastItemIndex", -1,
            "Index of last applied item (dedup key for reconnect)");
        _unlockedRegions  = _saveFile.Bind("Progress", "UnlockedRegions", "",
            "Comma-separated unlocked region names");
        _newbucksEarned   = _saveFile.Bind("Progress", "NewbucksEarned", 0L,
            "Cumulative newbucks earned this AP run (tracked via PlayerState.AddCurrency)");

        // Deserialize checked locations
        _checkedSet.Clear();
        foreach (var s in (_checkedLocations.Value ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries))
            if (long.TryParse(s, out var id)) _checkedSet.Add(id);

        // Deserialize unlocked regions
        _regionSet.Clear();
        foreach (var r in (_unlockedRegions.Value ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries))
            if (!string.IsNullOrWhiteSpace(r)) _regionSet.Add(r);

        _lastItemIdx        = _lastItemIndex.Value;
        _newbucksEarnedVal  = _newbucksEarned.Value;

        // Load persisted scout data (may not exist on first connect)
        _scoutData.Clear();
        LoadScoutFile();

        Plugin.Instance.Log.LogInfo(
            $"[AP] Save loaded: {_checkedSet.Count} locations checked, " +
            $"last item index {_lastItemIdx}, {_regionSet.Count} regions unlocked, " +
            $"{_scoutData.Count} scout entries cached.");
    }

    // -------------------------------------------------------------------------
    // Location checks
    // -------------------------------------------------------------------------

    public bool IsChecked(long id)              => _checkedSet.Contains(id);
    public bool IsRegionUnlocked(string name)   => _regionSet.Contains(name);

    public void MarkChecked(long id)
    {
        if (_saveFile == null || !_checkedSet.Add(id)) return;
        _checkedLocations!.Value = string.Join(",", _checkedSet);
        _saveFile.Save();
    }

    public void UnlockRegion(string name)
    {
        if (_saveFile == null) return;
        bool added = _regionSet.Add(name);
        Plugin.Instance.Log.LogInfo($"[AP] UnlockRegion: '{name}' — {(added ? "newly unlocked" : "already unlocked")}");
        if (!added) return;
        _unlockedRegions!.Value = string.Join(",", _regionSet);
        _saveFile.Save();
    }

    public void UpdateLastItemIndex(int idx)
    {
        if (_saveFile == null || idx <= _lastItemIdx) return;
        _lastItemIdx           = idx;
        _lastItemIndex!.Value  = idx;
        _saveFile.Save();
    }

    /// <summary>
    /// Forcibly sets <see cref="LastItemIndex"/> to <paramref name="idx"/>, even if lower
    /// than the current value. Used to correct a corrupted/stale watermark (e.g. when the
    /// saved value exceeds the actual number of items the AP server has on record).
    /// </summary>
    public void ForceLastItemIndex(int idx)
    {
        _lastItemIdx = idx;
        if (_saveFile == null) return;
        _lastItemIndex!.Value = idx;
        _saveFile.Save();
    }

    /// <summary>
    /// Adds <paramref name="amount"/> to the lifetime newbucks-earned counter and persists it.
    /// Called from the <c>PlayerState.AddCurrency</c> Postfix whenever newbucks are added.
    /// </summary>
    public void AccumulateNewbucks(int amount)
    {
        if (_saveFile == null || amount <= 0) return;
        _newbucksEarnedVal    += amount;
        _newbucksEarned!.Value = _newbucksEarnedVal;
        _saveFile.Save();
    }

    // -------------------------------------------------------------------------
    // Scout data
    // -------------------------------------------------------------------------

    /// <summary>
    /// Returns persisted scout info for a location, or null if not available.
    /// Used by SendCheck to show check notifications even when offline.
    /// </summary>
    public PersistedScout? GetScout(long locationId)
        => _scoutData.TryGetValue(locationId, out var s) ? s : null;

    /// <summary>
    /// Replaces the scout cache with fresh data from the AP server and persists it.
    /// Called from <see cref="ArchipelagoClient"/> after ScoutAllLocationsAsync completes.
    /// </summary>
    public void UpdateAndSaveScoutData(Dictionary<long, PersistedScout> data)
    {
        _scoutData = data;
        if (_scoutFilePath == null) return;
        try
        {
            File.WriteAllText(_scoutFilePath, JsonSerializer.Serialize(_scoutData, JsonOpts));
            Plugin.Instance.Log.LogInfo($"[AP] Scout data saved: {_scoutData.Count} entries.");
        }
        catch (Exception ex)
        {
            Plugin.Instance.Log.LogWarning($"[AP] Could not persist scout data: {ex.Message}");
        }
    }

    // -------------------------------------------------------------------------
    // Deletion
    // -------------------------------------------------------------------------

    /// <summary>
    /// Deletes both the progress config file and the scout JSON file for the given seed +
    /// slot combination.  Called from <c>DeleteGamePatch</c> when an AP-linked save slot is
    /// erased so that no stale data accumulates in the config directory.
    /// </summary>
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
            Plugin.Instance.Log.LogInfo($"[AP] Deleted AP {label}: {Path.GetFileName(path)}");
        }
        catch (Exception ex)
        {
            Plugin.Instance.Log.LogWarning(
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
            Plugin.Instance.Log.LogWarning($"[AP] Could not load scout data: {ex.Message}");
        }
    }
}
