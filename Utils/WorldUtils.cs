using UnityEngine;

namespace SlimeRancher2AP.Utils;

/// <summary>
/// Shared world-object utilities used by both patches and the debug dumper.
/// </summary>
public static class WorldUtils
{
    /// <summary>
    /// Builds a stable string key from a GameObject's scene name and rounded world position.
    /// Used as the <c>GameObjectName</c> lookup key in <c>LocationTable</c> for types whose
    /// actual <c>gameObject.name</c> is not unique (TreasurePods, MapNodes, GhostlyDrone nodes).
    /// <para>
    /// Format: <c>"sceneName_X_Y_Z"</c> where X/Y/Z are world positions rounded to the nearest
    /// integer. Objects must not move between sessions for this key to remain stable.
    /// </para>
    /// </summary>
    public static string PositionKey(GameObject go)
    {
        // All native IL2CPP accesses (transform.position, scene.name) are wrapped in a single
        // try/catch.  During scene state restoration (save loading), game objects may be
        // partially initialised in native memory; any of these calls can throw an access
        // violation.  Returning the "unknown_0_0_0" fallback will not match any LocationTable
        // entry, causing the patch to fall through to vanilla behaviour for that call, which
        // is correct — we do not want to fire AP checks during scene restoration.
        int x = 0, y = 0, z = 0;
        string sceneName = "unknown";
        try
        {
            var p = go.transform.position;
            x = Mathf.RoundToInt(p.x);
            y = Mathf.RoundToInt(p.y);
            z = Mathf.RoundToInt(p.z);
            var scene = go.scene;
            sceneName = scene.IsValid() ? (scene.name ?? $"scene{scene.handle}") : $"scene{scene.handle}";
        }
        catch { /* partially-initialised object — caller will receive "unknown_0_0_0" */ }
        return $"{sceneName}_{x}_{y}_{z}";
    }
}
