namespace SlimeRancher2AP.Archipelago;

/// <summary>
/// Binds an AP slot to one SR2 save — the first loaded while the session is active. Any other
/// save is untrusted: items are held and checks refused until the bound save is loaded again.
/// </summary>
/// <remarks>
/// To re-bind, clear <c>AssociatedSaveName</c> in
/// <c>BepInEx/config/SlimeRancher2-AP/AP_{seed}_{slot}.cfg</c> (or use F9 in debug builds).
/// </remarks>
public static class SaveGuard
{
    // The save last evaluated; a different save re-evaluates.
    private static string? _evaluatedSave;
    private static bool    _trusted;

    /// <summary>
    /// True when the loaded save is the slot's bound save (binding it now if the slot has none).
    /// Main thread.
    /// </summary>
    public static bool IsSaveTrusted()
    {
        var saveManager = Plugin.Instance.SaveManager;
        if (!saveManager.IsSaveBound) { Reset(); return false; }

        var current = GetCurrentSaveName();
        if (string.IsNullOrEmpty(current)) return false; // main menu / save not open yet

        if (current == _evaluatedSave) return _trusted;
        _evaluatedSave = current;

        // Seed first: a save from another seed is foreign whatever the association says — and a
        // new slot would otherwise adopt it.
        var liveSeed = Plugin.Instance.ApClient.Session?.RoomState.Seed;
        if (!string.IsNullOrEmpty(ExpectedSeed) && !string.IsNullOrEmpty(liveSeed)
            && !string.Equals(ExpectedSeed, liveSeed, StringComparison.Ordinal))
        {
            _trusted = false;
            Logger.Warning(
                $"[AP] SaveGuard: save '{current}' belongs to seed {ExpectedSeed} but the " +
                $"connected server is seed {liveSeed} — item delivery and location checks are " +
                "PAUSED for this save.");
            // Not a modal: mid-load it can't be clicked. LoadGamePatch catches what it can earlier.
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

    /// <summary>The loaded save's seed, from its binding; empty for a vanilla save.</summary>
    public static string ExpectedSeed { get; private set; } = "";

    public static void SetExpectedSeed(string? seed)
    {
        ExpectedSeed   = seed ?? "";
        _evaluatedSave = null;   // re-evaluate trust for the incoming save
    }

    /// <summary>On disconnect.</summary>
    /// <remarks>
    /// Leaves <see cref="ExpectedSeed"/> alone: it belongs to the loaded save, and loading an AP
    /// save reconnects (and so disconnects) right after recording it.
    /// </remarks>
    public static void Reset()
    {
        _evaluatedSave = null;
        _trusted = false;
    }

    /// <summary>Binds the loaded save to the session (debug panel).</summary>
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
