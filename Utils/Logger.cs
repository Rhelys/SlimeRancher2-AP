namespace SlimeRancher2AP.Utils;

/// <summary>Thin wrapper around BepInEx logging with a consistent mod prefix.</summary>
public static class Logger
{
    public static void Info(string msg)    => Plugin.Instance.Log.LogInfo($"[SR2-AP] {msg}");
    public static void Warning(string msg) => Plugin.Instance.Log.LogWarning($"[SR2-AP] {msg}");
    public static void Error(string msg)   => Plugin.Instance.Log.LogError($"[SR2-AP] {msg}");
    public static void Debug(string msg)   => Plugin.Instance.Log.LogDebug($"[SR2-AP] {msg}");
}
