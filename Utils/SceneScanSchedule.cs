using UnityEngine.SceneManagement;

namespace SlimeRancher2AP.Utils;

/// <summary>
/// Counts changes to the set of loaded scenes. SR2 streams zones in as additive scenes, so
/// <c>SceneManager.sceneCount</c> moves whenever world objects can appear or vanish — a cheap
/// signal (one icall per frame) for when a <c>FindObjectsOfTypeAll</c> scan is worth paying for.
/// </summary>
public static class SceneSetWatcher
{
    private static int _lastCount = -1;

    /// <summary>Bumped on every change in the loaded-scene count.</summary>
    public static int Version { get; private set; }

    /// <summary>Unscaled time of the latest change.</summary>
    public static float LastChangeTime { get; private set; }

    /// <summary>Call once per frame.</summary>
    public static void Tick()
    {
        int count = SceneManager.sceneCount;
        if (count == _lastCount) return;
        _lastCount     = count;
        Version++;
        LastChangeTime = UnityEngine.Time.unscaledTime;
    }
}

/// <summary>
/// When to run an expensive object scan: a few times after the scene set settles, then only on a
/// slow safety interval. <c>Resources.FindObjectsOfTypeAll</c> on a MonoBehaviour type walks every
/// loaded MonoBehaviour (~20 ms in SR2), so a fixed 1–5 s cadence shows up as a regular hitch.
/// Scans right after a zone loads are hidden by the load itself.
/// </summary>
public sealed class SceneScanSchedule
{
    // Seconds after the last scene-set change: one scan once streaming settles, one to catch
    // objects spawned shortly after the scene itself.
    private static readonly float[] AfterChange = { 1.5f, 8f };

    private readonly float _safetySeconds;
    private int   _seenVersion = -1;
    private int   _stage;
    private float _nextScan;

    public SceneScanSchedule(float safetySeconds) => _safetySeconds = safetySeconds;

    /// <summary>Forces a scan on the next <see cref="Due"/>.</summary>
    public void Restart()
    {
        _seenVersion = -1;
        _stage       = 0;
        _nextScan    = 0f;
    }

    /// <summary>True when a scan should run now; advances the schedule.</summary>
    public bool Due()
    {
        float now = UnityEngine.Time.unscaledTime;

        if (_seenVersion != SceneSetWatcher.Version)
        {
            bool first   = _seenVersion == -1;
            _seenVersion = SceneSetWatcher.Version;
            _stage       = 0;
            // Debounced: each further change pushes the scan back.
            _nextScan    = first ? now : SceneSetWatcher.LastChangeTime + AfterChange[0];
        }

        if (now < _nextScan) return false;

        _stage++;
        _nextScan = _stage < AfterChange.Length
            ? SceneSetWatcher.LastChangeTime + AfterChange[_stage]
            : now + _safetySeconds;
        if (_nextScan <= now) _nextScan = now + _safetySeconds;
        return true;
    }
}
