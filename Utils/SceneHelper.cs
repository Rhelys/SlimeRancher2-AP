using UnityEngine.SceneManagement;

namespace SlimeRancher2AP.Utils;

/// <summary>Utilities for detecting the currently active Unity scene.</summary>
public static class SceneHelper
{
    public static string CurrentScene => SceneManager.GetActiveScene().name;

    public static bool IsInGame  => SceneContext.Instance != null;
    public static bool IsInMenu  => !IsInGame;
}
