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
        if (!saveManager.IsSaveBound) { Reset(); return false; }

        var current = GetCurrentSaveName();
        if (string.IsNullOrEmpty(current)) return false; // main menu / save not open yet

        if (current == _evaluatedSave) return _trusted;
        _evaluatedSave = current;

        // Seed check first — it is stronger evidence than the slot association.
        //
        // The save records the seed it belongs to. If we are connected to a DIFFERENT seed,
        // this save belongs to another run no matter what the slot association says. That
        // matters most for a brand-new slot, where AssociatedSaveName is empty and the branch
        // below would happily adopt a save that plainly belongs elsewhere — which is how a
        // save bound to one seed silently became the save for another.
        var liveSeed = Plugin.Instance.ApClient.Session?.RoomState.Seed;
        if (!string.IsNullOrEmpty(ExpectedSeed) && !string.IsNullOrEmpty(liveSeed)
            && !string.Equals(ExpectedSeed, liveSeed, StringComparison.Ordinal))
        {
            _trusted = false;
            Logger.Warning(
                $"[AP] SaveGuard: save '{current}' belongs to seed {ExpectedSeed} but the " +
                $"connected server is seed {liveSeed} — item delivery and location checks are " +
                "PAUSED for this save.");
            // Notification, not a modal: this path runs mid-load (SaveGuard is consulted
            // lazily, by the first thing that wants to send a check), and a modal raised then
            // renders but cannot be clicked. LoadGamePatch blocks the load up front for the
            // case we can detect early; this is only the backstop for the rest.
            UI.StatusHUD.Instance?.ShowNotification(
                "AP: this save belongs to a different seed - items and checks paused");
            return _trusted;
        }

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

    /// <summary>
    /// Seed the currently-loading save says it belongs to, taken from its own AP binding.
    /// Empty for a vanilla save, or before any save has been loaded this session.
    /// </summary>
    public static string ExpectedSeed { get; private set; } = "";

    /// <summary>Records the seed a save is bound to. Called by the load-game patch.</summary>
    public static void SetExpectedSeed(string? seed)
    {
        ExpectedSeed   = seed ?? "";
        _evaluatedSave = null;   // re-evaluate trust for the incoming save
    }

    /// <summary>Clears the per-save evaluation cache (called on disconnect).</summary>
    /// <remarks>
    /// <see cref="ExpectedSeed"/> is deliberately NOT cleared here. It describes the SAVE that is
    /// loaded, not the session, and loading an AP save reconnects — <c>Connect</c> calls
    /// <c>Disconnect</c>, which lands here. Clearing it made the seed check dead code on exactly
    /// the path it exists for: the load recorded the seed, the reconnect wiped it moments later,
    /// and <see cref="IsSaveTrusted"/> then fell through to the association branch. That branch
    /// only catches this when the slot has already adopted some other save, so on a fresh config
    /// a wrong-seed save was adopted silently.
    ///
    /// Its lifetime is "until the next save is established" instead, which every path that
    /// establishes one already honours: new game and vanilla load clear it, an AP load sets it.
    /// </remarks>
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
