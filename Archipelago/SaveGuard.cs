namespace SlimeRancher2AP.Archipelago;

/// <summary>
/// Binds an AP session (seed + slot) to a specific SR2 save game and pauses AP activity
/// when a different save is loaded while connected.
/// </summary>
/// <remarks>
/// <para>
/// Without this guard, connecting to the server and then loading ANY save (including an
/// unrelated vanilla playthrough) delivered the session's items into that save and let it
/// send location checks (player-reported). The first save that reaches a live Player while
/// the session is active becomes the associated save (covers both "connect from the pause
/// menu mid-save" and "connect on the main menu, then load/new-game"); afterwards, any
/// other save is untrusted: <c>ProcessItemQueue</c> holds items and <c>SendCheck</c>
/// refuses checks until the associated save is loaded again.
/// </para>
/// <para>
/// Re-associating on purpose (e.g. restarting the run with a fresh save file): clear the
/// <c>AssociatedSaveName</c> entry in <c>BepInEx/config/SlimeRancher2-AP/AP_{seed}_{slot}.cfg</c>
/// — the warning notification includes this hint. Debug builds also expose an
/// "Associate current save" button on the F9 panel.
/// </para>
/// </remarks>
public static class SaveGuard
{
    // Save name evaluated most recently; re-evaluated whenever the loaded save changes
    // (loading a different save always tears the Player down first, and the name changes).
    private static string? _evaluatedSave;
    private static bool    _trusted;

    /// <summary>
    /// True when the currently loaded save is the one associated with the active AP
    /// session (associating it now if the slot has no save yet). False on the main menu,
    /// while no session is active, or when a foreign save is loaded.
    /// Main-thread only (reads Unity singletons).
    /// </summary>
    public static bool IsSaveTrusted()
    {
        var saveManager = Plugin.Instance.SaveManager;
        if (!saveManager.HasActiveSession) { Reset(); return false; }

        var current = GetCurrentSaveName();
        if (string.IsNullOrEmpty(current)) return false; // main menu / save not open yet

        if (current == _evaluatedSave) return _trusted;
        _evaluatedSave = current;

        var associated = saveManager.AssociatedSaveName;
        if (string.IsNullOrEmpty(associated))
        {
            saveManager.AssociateSave(current!);
            Logger.Info($"[AP] SaveGuard: save '{current}' is now associated with this AP slot.");
            _trusted = true;
        }
        else if (associated == current)
        {
            _trusted = true;
        }
        else
        {
            _trusted = false;
            Logger.Warning(
                $"[AP] SaveGuard: loaded save '{current}' but this AP slot is associated with " +
                $"'{associated}' — item delivery and location checks are PAUSED for this save. " +
                "Load the associated save, or clear AssociatedSaveName in " +
                "BepInEx/config/SlimeRancher2-AP/ to bind the slot to a different save.");
            UI.StatusHUD.Instance?.ShowNotification(
                "AP: this save is not associated with the connected slot - items and checks paused");
        }
        return _trusted;
    }

    /// <summary>Clears the per-save evaluation cache (called on disconnect).</summary>
    public static void Reset()
    {
        _evaluatedSave = null;
        _trusted = false;
    }

    /// <summary>
    /// Explicitly binds the currently loaded save to the active session (debug panel).
    /// </summary>
    public static void ForceAssociateCurrentSave()
    {
        var current = GetCurrentSaveName();
        if (string.IsNullOrEmpty(current) || !Plugin.Instance.SaveManager.HasActiveSession)
        {
            Logger.Warning("[AP] SaveGuard: cannot associate — no active session or no save loaded.");
            return;
        }
        Plugin.Instance.SaveManager.AssociateSave(current!);
        _evaluatedSave = current;
        _trusted = true;
        Logger.Info($"[AP] SaveGuard: save '{current}' force-associated with this AP slot.");
    }

    private static string? GetCurrentSaveName()
    {
        try { return GameContext.Instance?.AutoSaveDirector?.CurrentSaveGameName(); }
        catch { return null; } // GameContext not up yet (very early boot)
    }
}
