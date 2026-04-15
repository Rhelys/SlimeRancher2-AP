using HarmonyLib;
using Il2CppMonomiPark.SlimeRancher;
using Il2CppMonomiPark.SlimeRancher.DataModel;
using SlimeRancher2AP.Archipelago;
using SlimeRancher2AP.SaveData;
using SlimeRancher2AP.UI;

namespace SlimeRancher2AP.Patches.PlayerPatches;

// ─────────────────────────────────────────────────────────────────────────────
// NewGamePatch — fired when the player confirms a new game in NewGameOptionsUIRoot.
//
// If the mod is enabled AND the player is connected to AP:
//   • Writes a SaveSlot_{N}_binding.json so this SR2 slot auto-connects on future loads.
//   • Injects the Archipelago logo as the save-slot icon so AP saves are visually distinct.
//
// If not connected, the game proceeds as a vanilla save (no binding, no AP logic).
// The player can connect via Options → Archipelago and then start a new game.
//
// Patch point: AutoSaveDirector.StartNewGame(int saveSlotIndex, GameSettingsModel)
//   __0 = saveSlotIndex (0-based)
//   __1 = GameSettingsModel — we call SetGameIconForNewGame() on it to inject the logo.
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

        // Build and persist the binding so this slot auto-connects on future loads.
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

        // NOTE: Icon injection via SetGameIconForNewGame is intentionally skipped.
        // Passing a transient GameIconDefinition with an unknown persistenceId causes
        // the game to fail when it tries to resolve the icon from the asset catalog
        // during save creation.  Save-slot icon support can be revisited by patching
        // the save slot *display* UI instead of new-game creation.

        StatusHUD.Instance?.ShowNotification(
            $"New AP game — slot {slotIndex + 1} linked to {data.SlotName}");
    }

}

// ─────────────────────────────────────────────────────────────────────────────
// LoadGamePatch — fired when the player selects a save slot to load.
//
// If the selected slot has a SaveBinding sidecar, we trigger an auto-connect
// in the background so the AP session is (or is being) established by the time
// gameplay starts.  We never block the load — an offline load is valid and checks
// accumulate locally until the next connection.
//
// Patch point: AutoSaveDirector.BeginLoad(GameSaveIdentifier identifier)
//   __0 = GameSaveIdentifier { GameName = "Game 1", SaveName = "..." }
// ─────────────────────────────────────────────────────────────────────────────

[HarmonyPatch(typeof(AutoSaveDirector), "BeginLoad")]
internal static class LoadGamePatch
{
    private static void Prefix(AutoSaveDirector __instance, GameSaveIdentifier __0)
    {
        if (!Plugin.Instance.ModEnabled) return;
#if DEBUG
        // Reset the Once() deduplication set so all patch traces can fire again
        // for this Continue attempt, even if they already fired on the main menu.
        SlimeRancher2AP.Utils.DebugTrace.Reset();
        SlimeRancher2AP.Utils.DebugTrace.All("LoadGamePatch.Prefix — trace set reset for new load");
#endif

        // Resolve slot index from GameName via the live save summary list.
        int slotIndex = ResolveSlotIndex(__instance, __0.GameName);
        if (slotIndex < 0) return;

        var binding = SaveBindingManager.Load(slotIndex);
        if (binding == null) return;   // vanilla save — nothing to do

        Plugin.Instance.Log.LogInfo(
            $"[AP] LoadGamePatch: slot {slotIndex} ({__0.GameName}) has AP binding " +
            $"(seed={binding.Seed}, slot={binding.Slot}) — auto-connecting...");

        // If already connected to the correct session, no need to reconnect.
        if (Plugin.Instance.ApClient.IsConnected &&
            Plugin.Instance.ApClient.Session?.RoomState.Seed == binding.Seed)
        {
            Plugin.Instance.Log.LogInfo("[AP] LoadGamePatch: already connected to correct AP session.");
            return;
        }

        // Pre-load LastItemIndex BEFORE connecting so that when Session.Items.ItemReceived
        // fires during TryConnectAndLogin (replaying all historical items), the dedup guard
        // in ArchipelagoClient.OnItemReceived already has the correct high-water mark.
        // Without this, all historical items are enqueued (since _lastItemIdx starts at -1)
        // and then requeued in an infinite loop while the scene is loading.
        if (!string.IsNullOrEmpty(binding.Seed) && !string.IsNullOrEmpty(binding.Slot))
            Plugin.Instance.SaveManager.PreloadLastItemIndex(binding.Seed, binding.Slot);

        // Kick off connection in background; game load proceeds immediately.
        var connectData = new ArchipelagoData
        {
            Uri      = binding.Host,
            Port     = binding.Port,
            SlotName = binding.Slot,
            Password = binding.Password,
        };
        Plugin.Instance.ApClient.Connect(connectData);
    }

    /// <summary>
    /// Finds the SaveSlotIndex for a given GameName (e.g. "Game 3") by querying the
    /// AutoSaveDirector's in-memory summary list.
    /// </summary>
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
            Plugin.Instance.Log.LogWarning(
                $"[AP] LoadGamePatch: could not resolve slot index for '{gameName}' — {ex.Message}");
            return -1;
        }
    }
}

// ─────────────────────────────────────────────────────────────────────────────
// DeleteGamePatch — removes the binding sidecar when a save slot is deleted so
// the slot is no longer treated as an AP save.
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

        // Load the binding before deleting it — we need the seed + slot name to locate
        // the AP progress config and scout JSON files, which are keyed by seed+slot, not
        // by slot index.
        var binding = SaveBindingManager.Load(slotIndex);
        SaveBindingManager.Delete(slotIndex);

        if (binding != null && !string.IsNullOrEmpty(binding.Seed) && !string.IsNullOrEmpty(binding.Slot))
        {
            ApSaveManager.DeleteSaveData(binding.Seed, binding.Slot);
            Plugin.Instance.Log.LogInfo(
                $"[AP] Cleaned up AP save data for slot {slotIndex} (seed={binding.Seed}, slot={binding.Slot})");
        }
    }
}
