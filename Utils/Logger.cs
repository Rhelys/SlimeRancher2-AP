// Logger lives in the root SlimeRancher2AP namespace so all subnamespaces
// (Archipelago, Patches.*, Utils) can call Logger.Info() without a using directive.
namespace SlimeRancher2AP;

/// <summary>BepInEx logging with a <c>[HH:mm:ss.fff]</c> prefix.</summary>
public static class Logger
{
    private static string Ts => System.DateTime.Now.ToString("HH:mm:ss.fff");

    public static void Info(string msg)    => Plugin.Instance.Log.LogInfo($"[{Ts}] {msg}");
    public static void Warning(string msg) => Plugin.Instance.Log.LogWarning($"[{Ts}] {msg}");
    public static void Error(string msg)   => Plugin.Instance.Log.LogError($"[{Ts}] {msg}");
    public static void Debug(string msg)   => Plugin.Instance.Log.LogDebug($"[{Ts}] {msg}");
}
