#if DEBUG
namespace SlimeRancher2AP.Utils;

/// <summary>
/// Debug-build crash tracer: the last line logged before a native crash names the call site.
/// Once() for hot paths, All() for rare ones, Step() for checkpoints within a method.
/// </summary>
internal static class DebugTrace
{
    private static readonly System.Collections.Generic.HashSet<string> _seen = new();

    /// <summary>Logs only the first time this tag is seen.</summary>
    public static void Once(string tag)
    {
        if (_seen.Add(tag))
            Logger.Info($"[TRACE] {tag}");
    }

    /// <summary>Logs every time.</summary>
    public static void All(string tag)
        => Logger.Info($"[TRACE] {tag}");

    /// <summary>Logs a checkpoint: "MethodName.step N — description".</summary>
    public static void Step(string tag)
        => Logger.Info($"[TRACE]   {tag}");

    /// <summary>Lets every Once() tag fire again.</summary>
    public static void Reset() => _seen.Clear();
}
#endif
