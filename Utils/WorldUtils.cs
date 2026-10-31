using UnityEngine;

namespace SlimeRancher2AP.Utils;

/// <summary>
/// Shared world-object utilities used by both patches and the debug dumper.
/// </summary>
public static class WorldUtils
{
    /// <summary>
    /// <c>"sceneName_X_Y_Z"</c> (position rounded): the <c>LocationTable</c> key for objects whose
    /// names aren't unique. Stable only for objects that never move.
    /// </summary>
    public static string PositionKey(GameObject go)
    {
        // Half-initialised objects during save restore can throw; the "unknown_0_0_0" fallback
        // matches no location, so no check fires during restore.
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
