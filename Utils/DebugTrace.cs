#if DEBUG
namespace SlimeRancher2AP.Utils;

/// <summary>
/// Lightweight crash-diagnosis tracer — Debug builds only.
/// Call Trace() at the absolute first line of any patch method to record that it was entered.
/// Call Step() for numbered checkpoints within a method.
/// The last line written to the BepInEx log before a native crash identifies the call site.
///
/// Use Once() for high-frequency patches (fires every frame / every save-state restore) so
/// the log doesn't get flooded; All() for low-frequency methods where every call matters.
/// </summary>
internal static class DebugTrace
{
    // Keys that have already fired Once() — not reset between sessions so the log
    // shows only the first call of each high-frequency patch.
    private static readonly System.Collections.Generic.HashSet<string> _seen = new();

    /// <summary>
    /// Logs a trace message only the first time this key is seen.
    /// Use for methods that fire many times (e.g. upgrade tracking, puzzle gates).
    /// </summary>
    public static void Once(string tag)
    {
        if (_seen.Add(tag))
            Logger.Info($"[TRACE] {tag}");
    }

    /// <summary>
    /// Logs a trace message every time it is called.
    /// Use for low-frequency methods (e.g. treasure pod activate, gordo pop).
    /// </summary>
    public static void All(string tag)
        => Logger.Info($"[TRACE] {tag}");

    /// <summary>
    /// Logs a numbered step within a method (always, not deduplicated).
    /// tag format: "MethodName.step N — description"
    /// </summary>
    public static void Step(string tag)
        => Logger.Info($"[TRACE]   {tag}");

    /// <summary>Clears the seen-once set (e.g. call from GoalHandler.Initialize on reconnect).</summary>
    public static void Reset() => _seen.Clear();
}
#endif
