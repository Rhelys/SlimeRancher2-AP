using HarmonyLib;
using Il2CppMonomiPark.SlimeRancher;
using Il2CppMonomiPark.SlimeRancher.DataModel;
using SlimeRancher2AP.Archipelago;
using SlimeRancher2AP.SaveData;
using SlimeRancher2AP.UI;

namespace SlimeRancher2AP.Patches.PlayerPatches;

// ─────────────────────────────────────────────────────────────────────────────
// NewGamePatch — a new game started while connected is bound to the AP slot (a
// SaveSlot_{N}_binding.json, for auto-connect) and gets every item replayed. Unconnected,
// it is a vanilla save.
//
// Patch point: AutoSaveDirector.StartNewGame(int saveSlotIndex, GameSettingsModel)
// ─────────────────────────────────────────────────────────────────────────────

[HarmonyPatch(typeof(AutoSaveDirector), "StartNewGame")]
internal static class NewGamePatch
{
    private static void Prefix(AutoSaveDirector __instance, int __0, GameSettingsModel __1)
    {
        if (!Plugin.Instance.ModEnabled) return;
        if (!Plugin.Instance.ApClient.IsConnected) return;

        var session = Plugin.Instance.ApClient.Session;
        if (session == null) return;

        int slotIndex = __0;

        var data = ArchipelagoData.LoadFromConfig(Plugin.Instance.Config);
        var binding = new SaveBindingManager.SaveBinding
        {
            Host     = data.Uri,
            Port     = data.Port,
            Slot     = data.SlotName,
            Password = data.Password,
            Seed     = session.RoomState.Seed,
        };
        SaveBindingManager.Save(slotIndex, binding);

        // Connect() only queued items above the slot's watermark, which an earlier SR2 save on
        // this slot may have advanced. The new save needs everything.
        Plugin.Instance.ApClient.RequestFullReplay();
        Logger.Info(
            $"[AP] NewGamePatch: full replay requested — all items from AP snapshot will be applied to new save.");

        // No AP save-slot icon: SetGameIconForNewGame with a transient GameIconDefinition fails
        // save creation. A display-side patch would be the way to do it.

        // Clear a seed left over from an earlier load attempt.
        SaveGuard.SetExpectedSeed(null);

        SlimeRancher2AP.Archipelago.ItemHandler.MarkFreshSave();

        StatusHUD.Instance?.ShowNotification(
            $"New AP game — slot {slotIndex + 1} linked to {data.SlotName}");
    }

}

// ─────────────────────────────────────────────────────────────────────────────
// LoadGamePatch — loading a bound save auto-connects in the background and binds the save
// for offline play meanwhile. The load is never held for the connection; only a known seed
// mismatch stops it, for confirmation.
//
// Patch point: AutoSaveDirector.BeginLoad(GameSaveIdentifier identifier)
// ─────────────────────────────────────────────────────────────────────────────

[HarmonyPatch(typeof(AutoSaveDirector), "BeginLoad")]
internal static class LoadGamePatch
{
    /// <summary>Set by the "load anyway" confirm action so the retry is not blocked again.</summary>
    private static bool _bypassSeedCheck;

    /// <summary>Newline for modal text, built explicitly to avoid escape-sequence mangling.</summary>
    private static readonly string NewLine = ((char)10).ToString();

    private static bool Prefix(AutoSaveDirector __instance, GameSaveIdentifier __0)
    {
        if (!Plugin.Instance.ModEnabled) return true;
#if DEBUG
        // Let Once() traces fire again for this load.
        SlimeRancher2AP.Utils.DebugTrace.Reset();
        SlimeRancher2AP.Utils.DebugTrace.All("LoadGamePatch.Prefix — trace set reset for new load");
#endif

        int slotIndex = ResolveSlotIndex(__instance, __0.GameName);
        if (slotIndex < 0) return true;

        var binding = SaveBindingManager.Load(slotIndex);
        if (binding == null)
        {
            // Vanilla save: drop any live session so no AP logic touches it.
            if (Plugin.Instance.ApClient.IsConnected || Plugin.Instance.SaveManager.HasActiveSession)
            {
                Logger.Info(
                    $"[AP] LoadGamePatch: slot {slotIndex} ({__0.GameName}) has no AP binding — " +
                    "disconnecting so the vanilla save is not affected by the live AP session.");
                Plugin.Instance.ApClient.Disconnect();
                StatusHUD.Instance?.ShowNotification("Vanilla save loaded — Archipelago disconnected");
            }
            SaveGuard.SetExpectedSeed(null);
            return true;
        }

        Logger.Info(
            $"[AP] LoadGamePatch: slot {slotIndex} ({__0.GameName}) has AP binding " +
            $"(seed={binding.Seed}, slot={binding.Slot}) — auto-connecting...");

        // Wrong seed: ask before loading, while a modal still works (it doesn't mid-load).
        // Only decidable while connected; SaveGuard is the backstop otherwise.
        if (!_bypassSeedCheck
            && Plugin.Instance.ApClient.IsConnected
            && !string.IsNullOrEmpty(binding.Seed))
        {
            var liveSeed = Plugin.Instance.ApClient.Session?.RoomState.Seed;
            if (!string.IsNullOrEmpty(liveSeed)
                && !string.Equals(liveSeed, binding.Seed, System.StringComparison.Ordinal))
            {
                Logger.Warning(
                    $"[AP] LoadGamePatch: save is bound to seed {binding.Seed} but the connected " +
                    $"server is seed {liveSeed} — load blocked pending confirmation.");

                StatusHUD.Instance?.ShowWarningModal(
                    "This save belongs to a different Archipelago seed." + NewLine + NewLine +
                    "Save seed: " + binding.Seed + NewLine +
                    "Connected: " + liveSeed + NewLine + NewLine +
                    "Loading it will not send checks or receive items until you connect to the " +
                    "matching seed. Connect to that seed first, or load the save for this one.",
                    () =>
                    {
                        _bypassSeedCheck = true;
                        __instance.BeginLoad(__0);
                    },
                    "Load Anyway");

                return false;   // stay on the save-select screen
            }
        }
        _bypassSeedCheck = false;

        // Only now — a rejected save's seed would condemn the next save loaded.
        SaveGuard.SetExpectedSeed(binding.Seed);

        SlimeRancher2AP.Archipelago.ItemHandler.MarkLoadedSave();

        // Already on this seed: no reconnect, but re-validate against the reloaded model.
        if (Plugin.Instance.ApClient.IsConnected &&
            Plugin.Instance.ApClient.Session?.RoomState.Seed == binding.Seed)
        {
            Logger.Info("[AP] LoadGamePatch: already connected to correct AP session — scheduling upgrade re-validation for scene reload.");
            Plugin.Instance.ApClient.ScheduleUpgradeValidation();
            return true;
        }

        // Preload the watermark before connecting (see PreloadLastItemIndex) — but not over a
        // live session's, which would come from a different save.
        bool activeSession = Plugin.Instance.SaveManager.HasActiveSession;
        Logger.Info(
            $"[AP] LoadGamePatch: HasActiveSession={activeSession} watermark={Plugin.Instance.SaveManager.LastItemIndex}");
        if (!activeSession &&
            !string.IsNullOrEmpty(binding.Seed) && !string.IsNullOrEmpty(binding.Slot))
        {
            Plugin.Instance.SaveManager.PreloadLastItemIndex(binding.Seed, binding.Slot);

            // Bind now, so an unreachable server still gets correct offline behaviour.
            Plugin.Instance.SaveManager.BindOfflineSave(binding.Seed, binding.Slot);
            Plugin.Instance.ApClient.RestorePersistedSlotData();
        }

        var connectData = new ArchipelagoData
        {
            Uri      = binding.Host,
            Port     = binding.Port,
            SlotName = binding.Slot,
            Password = binding.Password,
        };
        Plugin.Instance.ApClient.Connect(connectData);
        return true;
    }

    /// <summary>The SaveSlotIndex for a GameName (e.g. "Game 3"), or -1.</summary>
    private static int ResolveSlotIndex(AutoSaveDirector director, string gameName)
    {
        try
        {
            var byName = director.AvailableGamesByGameName();
            if (byName == null || !byName.ContainsKey(gameName)) return -1;

            var summaries = byName[gameName];
            if (summaries == null || summaries.Count == 0) return -1;

            return summaries[0].SaveSlotIndex;
        }
        catch (Exception ex)
        {
            Logger.Warning(
                $"[AP] LoadGamePatch: could not resolve slot index for '{gameName}' — {ex.Message}");
            return -1;
        }
    }
}

// ─────────────────────────────────────────────────────────────────────────────
// SessionEndedPatch — disconnects on Save and Quit, so the next save loaded can
// auto-connect to its own slot.
//
// Patch point: AutoSaveDirector.OnSessionEnded()  [CallerCount = 1; not on app exit]
// ─────────────────────────────────────────────────────────────────────────────

[HarmonyPatch(typeof(AutoSaveDirector), "OnSessionEnded")]
internal static class SessionEndedPatch
{
    private static void Postfix()
    {
        if (!Plugin.Instance.ModEnabled) return;
        if (!Plugin.Instance.ApClient.IsConnected) return;

        Logger.Info("[AP] SessionEndedPatch: Save and Quit detected — disconnecting from AP server.");
        Plugin.Instance.ApClient.Disconnect();
    }
}

// ─────────────────────────────────────────────────────────────────────────────
// DeleteGamePatch — deleting a save also deletes its binding and AP progress files.
//
// Patch point: AutoSaveDirector.DeleteGame(string gameName)
// ─────────────────────────────────────────────────────────────────────────────

[HarmonyPatch(typeof(AutoSaveDirector), "DeleteGame")]
internal static class DeleteGamePatch
{
    private static void Prefix(AutoSaveDirector __instance, string __0)
    {
        if (!Plugin.Instance.ModEnabled) return;

        int slotIndex = -1;
        try
        {
            var byName = __instance.AvailableGamesByGameName();
            if (byName != null && byName.ContainsKey(__0))
            {
                var s = byName[__0];
                if (s != null && s.Count > 0) slotIndex = s[0].SaveSlotIndex;
            }
        }
        catch { /* best-effort */ }

        if (slotIndex < 0) return;
        if (!SaveBindingManager.Exists(slotIndex)) return;

        // Read it first: the progress files are keyed by its seed+slot.
        var binding = SaveBindingManager.Load(slotIndex);
        SaveBindingManager.Delete(slotIndex);

        if (binding != null && !string.IsNullOrEmpty(binding.Seed) && !string.IsNullOrEmpty(binding.Slot))
        {
            ApSaveManager.DeleteSaveData(binding.Seed, binding.Slot);
            Logger.Info(
                $"[AP] Cleaned up AP save data for slot {slotIndex} (seed={binding.Seed}, slot={binding.Slot})");
        }
    }
}
