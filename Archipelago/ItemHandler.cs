using System;
using Archipelago.MultiClient.Net.Models;
using Il2CppMonomiPark.World;
using Il2CppMonomiPark.SlimeRancher.DataModel;
using Il2CppMonomiPark.SlimeRancher.Economy;
using Il2CppMonomiPark.SlimeRancher.Player;
using Il2CppMonomiPark.SlimeRancher.Player.Component;
using Il2CppMonomiPark.SlimeRancher.Util;
using Il2CppMonomiPark.SlimeRancher.Weather;
using Il2CppMonomiPark.SlimeRancher.Weather.Activity;
using Il2CppMonomiPark.SlimeRancher.SceneManagement;
using Il2CppMonomiPark.SlimeRancher.World;
using Il2CppMonomiPark.SlimeRancher.World.Teleportation;
using SlimeRancher2AP.Data;
using SlimeRancher2AP.Utils;
using UnityEngine;
using ApItemInfo = Archipelago.MultiClient.Net.Models.ItemInfo;

namespace SlimeRancher2AP.Archipelago;

/// <summary>
/// Applies received Archipelago items to the SR2 player state.
/// Must be called on the Unity main thread (from ApUpdateBehaviour.Update via ProcessItemQueue).
/// </summary>
public static class ItemHandler
{
    private static readonly System.Random _rng = new();

    /// <summary>
    /// The player's ActorUpgradeHandler, cached by ActorUpgradeHandlerCtorPatch. It is not a
    /// MonoBehaviour, so GetComponent cannot find it.
    /// </summary>
    public static ActorUpgradeHandler? UpgradeHandler { get; set; }

    /// <summary>
    /// True while the AP pipeline reads or writes an upgrade level. Lets the write past
    /// <c>FabricatorUpgradeBlockPatch</c> and exempts the read from <c>UpgradeModelGetLevelPatch</c>.
    /// </summary>
    internal static bool IsApplyingItem { get; private set; }

    /// <summary>
    /// True while the mod grants Newbucks (filler, plot refunds), so <c>NewbucksEarnings</c>
    /// neither scales nor counts them toward the "newbucks" goal.
    /// </summary>
    internal static bool IsGrantingCurrency { get; set; }

    // IdentifiableType names confirmed via AP-Dump log (GadgetDirector.RefineryTypeGroup).
    private static readonly string[] CommonPlorts =
    [
        "PinkPlort", "TabbyPlort", "RockPlort", "PhosphorPlort", "CottonPlort",
        "HoneyPlort", "CrystalPlort", "BoomPlort", "BattyPlort",
    ];

    private static readonly string[] UncommonPlorts =
    [
        "DervishPlort", "TanglePlort", "HunterPlort", "RingtailPlort",
        "FlutterPlort", "AnglerPlort", "YolkyPlort",
    ];

    private static readonly string[] RarePlorts =
    [
        "HyperPlort", "TwinPlort", "SaberPlort", "ShadowPlort", "GoldPlort",
        "PuddlePlort", "SloomberPlort", "FirePlort", "UnstablePlort", "StablePlort",
    ];

    private static readonly string[] RainbowFieldsCrafts =
    [
        "JellystoneCraft", "DeepBrineCraft",
    ];

    private static readonly string[] EmberValleyCrafts =
    [
        "LavaDustCraft", "PrimordyOilCraft", "SilkySandCraft", "RadiantOreCraft", "BuzzWaxCraft",
    ];

    private static readonly string[] StarlightStrandCrafts =
    [
        "WildHoneyCraft", "SilkySandCraft", "RadiantOreCraft", "BuzzWaxCraft",
    ];

    private static readonly string[] PowderfallBluffsCrafts =
    [
        "SlimeFossilCraft", "PerfectSnowflakeCraft", "SunSapCraft",
    ];

    private static readonly string[] GreyLabyrinthCrafts =
    [
        "TinPetalCraft", "DreamBubbleCraft", "BlackIndigoniumCraft", "MagmaCombCraft", "AquaGlassCraft",
        "RoyalJellyCraft",
    ];

    private static readonly string[] RareCrafts =
    [
        "StrangeDiamondCraft", "LightningMoteCraft", "StormGlassCraft", "DriftCrystalCraft",
    ];

    // -------------------------------------------------------------------------

    /// <summary>Debug-only entry point — applies an item by ID without a real AP session.</summary>
    public static void ApplyById(long itemId, int itemIndex)
    {
        if (SceneContext.Instance == null)
        {
            Logger.Warning("[AP-Debug] SceneContext not ready — load a save first");
            return;
        }

        var item = ItemTable.Get(itemId);
        if (item == null)
        {
            Logger.Warning($"[AP-Debug] Unknown item ID: {itemId}");
            return;
        }

        Logger.Info($"[AP-Debug] Granting item: {item.Name}");

        switch (item.Type)
        {
            case ItemType.RegionAccess:          ApplyRegionAccess(item);                        break;
            // -1, not the debug counter: the deferred-open paths would write it to the watermark.
            case ItemType.ConservatoryExpansion: ApplyConservatoryExpansion(item, -1);          break;
            case ItemType.Upgrade:               ApplyUpgrade(item, null, itemIndex);          break;
            case ItemType.Gadget:                ApplyGadget(item, null, itemIndex);           break;
            case ItemType.Palette:               ApplyPalette(item, null, itemIndex);          break;
            case ItemType.Filler:                ApplyFiller(item, null, itemIndex);           break;
            case ItemType.Useful:                ApplyUseful(item, null, itemIndex);           break;
            case ItemType.UpgradeComponent:      ApplyUpgradeComponent(item, null, itemIndex); break;
            case ItemType.Trap:                  ApplyTrap(item, null, itemIndex);             break; // return ignored — debug path has no requeue
            case ItemType.RanchPlot:             ApplyRanchPlot(item, null, itemIndex);        break;
#if DEBUG
            case ItemType.ShopCatalog:
                // Persisted count, so the debug grant uses the live path.
                Plugin.Instance.SaveManager.AccumulateShopCatalog();
                ShopCatalogHandler.MarkDirty();
                Logger.Info(
                    $"[AP-Debug] Shop Catalog granted — {ShopCatalogHandler.Held}/" +
                    $"{ShopCatalogHandler.Total}");
                break;
            case ItemType.PrismaShard:
                // Snapshot-only count — a local grant cannot add to it, so use the debug offset.
                PrismaShardHandler.DebugGrant();
                Logger.Info(
                    $"[AP-Debug] Prisma Shard granted — {PrismaShardHandler.Progress}" +
                    $"  encounter {(PrismaShardHandler.IsEncounterUnlocked ? "UNLOCKED" : "SEALED")}");
                break;
#endif
        }

        // No UpdateLastItemIndex: the debug counter starts at 9000 and would make every real
        // item read as already applied.
    }

    public static void Apply(ApItemInfo apItem, int itemIndex)
    {
        // Defer while loading. Only requeue items above the watermark: on reconnect,
        // OnItemReceived can enqueue history before OnConnected loads LastItemIndex, and
        // requeuing those would loop forever on the loading screen.
        if (SceneContext.Instance == null)
        {
            if (itemIndex > Plugin.Instance.SaveManager.LastItemIndex)
                Plugin.Instance.ApClient.RequeueItem(apItem, itemIndex);
            return;
        }

        Logger.Info(
            $"[AP] About to apply: {apItem.ItemName} " +
            $"(id={apItem.ItemId}, idx={itemIndex}, " +
            $"lastIdx={Plugin.Instance.SaveManager.LastItemIndex})");

        var item = ItemTable.Get(apItem.ItemId);
        if (item == null)
        {
            Logger.Warning($"[AP] Unknown item ID: {apItem.ItemId}");
            Plugin.Instance.SaveManager.UpdateLastItemIndex(itemIndex);
            return;
        }

        // Name mismatch = the server runs a different apworld version. Apply by ID, but warn.
        if (apItem.ItemName != item.Name)
            Logger.Warning(
                $"[AP] DATA PACKAGE MISMATCH — server says id={apItem.ItemId} is '{apItem.ItemName}', " +
                $"but local table maps that ID to '{item.Name}'. " +
                $"Regenerate the game on the AP server with the current apworld version to fix.");

        // One-shot items must not re-fire when a watermark reset replays from index 0.
        // Everything else is idempotent and deliberately replays.
        bool isEphemeral = item.Type is ItemType.Filler or ItemType.Useful or ItemType.Trap;
        if (isEphemeral && Plugin.Instance.SaveManager.IsEphemeralApplied(itemIndex))
        {
            Logger.Info(
                $"[AP] Skipping already-applied {item.Type.ToString().ToLowerInvariant()}: " +
                $"{item.Name} (idx={itemIndex})");
            Plugin.Instance.SaveManager.UpdateLastItemIndex(itemIndex);
            Plugin.Instance.SaveManager.RemoveDeferredItem(itemIndex);
            return;
        }

        // These types log inside their own apply methods; logging here would spam every frame
        // while a rate-limited item waits.
        if (item.Type is not ItemType.Trap and not ItemType.Filler and not ItemType.Useful and not ItemType.UpgradeComponent)
            Logger.Info($"[AP] Applying item: {item.Name} (id={item.Id}, idx={itemIndex})");

        // Apply* returns false when the item was requeued; the watermark must not advance then.
        bool advanceWatermark = true;

        switch (item.Type)
        {
            case ItemType.RegionAccess:
                ApplyRegionAccess(item);   // always succeeds — writes config, never requeues
                break;
            case ItemType.ConservatoryExpansion:
                if (!ApplyConservatoryExpansion(item, itemIndex))
                    advanceWatermark = false;
                break;
            case ItemType.Upgrade:
                if (!ApplyUpgrade(item, apItem, itemIndex))
                    advanceWatermark = false;
                break;
            case ItemType.Gadget:
                if (!ApplyGadget(item, apItem, itemIndex))
                    advanceWatermark = false;
                break;
            case ItemType.Palette:
                if (!ApplyPalette(item, apItem, itemIndex))
                    advanceWatermark = false;
                break;
            case ItemType.Filler:
                if (!ApplyFiller(item, apItem, itemIndex))
                    advanceWatermark = false;
                break;
            case ItemType.Useful:
                if (!ApplyUseful(item, apItem, itemIndex))
                    advanceWatermark = false;
                break;
            case ItemType.UpgradeComponent:
                if (!ApplyUpgradeComponent(item, apItem, itemIndex))
                    advanceWatermark = false;
                break;
            case ItemType.Trap:
                if (!ApplyTrap(item, apItem, itemIndex))
                    advanceWatermark = false;
                break;
            case ItemType.RanchPlot:
                ApplyRanchPlot(item, apItem, itemIndex); // count-based, always succeeds
                break;
            case ItemType.PrismaShard:
                // Counter only — invalidate so the next gate check recounts.
                PrismaShardHandler.MarkDirty();
                Logger.Info(
                    $"[AP] Prisma Shard received — {PrismaShardHandler.Collected}/" +
                    $"{PrismaShardHandler.Required}");
                break;
            case ItemType.ShopCatalog:
                // Counter only. Persisted because the server snapshot reads 0 offline.
                Plugin.Instance.SaveManager.AccumulateShopCatalog();
                ShopCatalogHandler.MarkDirty();
                {
                    int held     = ShopCatalogHandler.Held;
                    int released = ShopCatalogHandler.CountInWave(held);
                    Logger.Info(
                        $"[AP] Progressive Shop Catalog received — {held}/" +
                        $"{ShopCatalogHandler.Total}, releasing {released} shop item(s)");

                    // The shop list only refreshes when opened, so tell the player now.
                    if (released > 0)
                        UI.StatusHUD.Instance?.ShowNotification(
                            $"Shop Catalog {held} — {released} new items at Polestar Provisions");
                }
                break;
        }

        if (advanceWatermark)
        {
            Plugin.Instance.SaveManager.UpdateLastItemIndex(itemIndex);
            if (isEphemeral)
                Plugin.Instance.SaveManager.MarkEphemeralApplied(itemIndex);

            // Applied path only: a parked item's deferred entry is what keeps it from being
            // lost below the watermark on reconnect.
            Plugin.Instance.SaveManager.RemoveDeferredItem(itemIndex);

            // Announced here, not at Notify() sites, so a requeued item cannot announce twice.
            SlimeRancher2AP.UI.ItemNotifier.OnItemApplied(item, apItem);
        }
    }

    // -------------------------------------------------------------------------

    // Region access item name → zone teleporter GadgetDefinition name (bundled and auto modes).
    private static readonly Dictionary<string, string> RegionToZoneTeleporter = new()
    {
        ["Ember Valley Access"]      = "TeleporterZoneGorge",
        ["Starlight Strand Access"]  = "TeleporterZoneStrand",
        ["Powderfall Bluffs Access"] = "TeleporterZoneBluffs",
        // Grey Labyrinth has no region access item yet — add when implemented
    };

    private static void ApplyRegionAccess(Data.ItemInfo item)
    {
        Plugin.Instance.SaveManager.UnlockRegion(item.Name);
        Notify($"Received: {item.Name}");

        var mode = Plugin.Instance.ApClient.SlotData?.RegionAccessMode ?? "vanilla";

        // Bundled mode: this grant is the teleporter's only source. It often fails mid-scene-load
        // and the watermark advances regardless, so a failure is parked for retry.
        if (mode == "bundled" && !GrantRegionTeleporter(item.Name))
        {
            _pendingRegionTeleporters.Add(item.Name);
            Logger.Info(
                $"[AP] Zone teleporter for '{item.Name}' not grantable yet (scene loading?) — queued for retry.");
        }

        // ── Powderfall Bluffs: a plort door, not a button ──
        // There is nothing to press again, so open it directly. (Early entry is handled by
        // GateReturnEnforcer — the door opens from native code we can't patch.)
        if (item.Name == RegionTable.PBRegionItemName)
        {
            // By posKey — the door's object name is shared across zones.
            bool pbFound = false;
            foreach (var door in Resources.FindObjectsOfTypeAll<PuzzleSlotLockable>())
            {
                string posKey;
                try { posKey = WorldUtils.PositionKey(door.gameObject!); }
                catch { continue; }
                if (posKey != RegionTable.PBGatePosKey) continue;

                bool ready = false;
                try { ready = door.ShouldUnlock(); } catch { /* guard */ }
                if (!ready)
                {
                    Logger.Info(
                        "[AP] PB Slime Door found but ShouldUnlock()=false (not fully filled yet) — " +
                        "door will open once all plort slots are filled.");
                    pbFound = true;
                    break;
                }
                try { door.ActivateOnUnlock(); }
                catch (Exception ex)
                {
                    Logger.Warning($"[AP] PB ActivateOnUnlock threw: {ex.Message}");
                }
                Logger.Info("[AP] Opened PB Slime Door via ActivateOnUnlock.");

                // Normally sent by the ActivateOnUnlock Prefix; repeated in case it didn't fire.
                if (mode != "vanilla")
                    Plugin.Instance.ApClient.SendCheck(LocationConstants.RegionGate_PowderfallBluffs);

                pbFound = true;
                break;
            }
            if (!pbFound)
                Logger.Warning(
                    "[AP] PB Slime Door not found in current scene — will open when player reaches it.");
            return;
        }

        // ── EV / SS button gates ──
        // Nothing to do: the next button press now passes RegionGatePatch.
        if (!RegionTable.TryGetSwitch(item.Name, out var switchName))
        {
            Logger.Info($"[AP] Region '{item.Name}' unlocked — no switch mapping, nothing more to do.");
            return;
        }

        Logger.Info(
            $"[AP] Region '{item.Name}' unlocked — press the gate button ('{switchName}') to open it.");
    }

    // Doors whose item arrived before their terminal check was sent. Held closed until then —
    // an already-OPEN door makes OnInteract skip the purchase dialog, so the check could never
    // be sent. Drained by TriggerPendingDoorUnlock.
    private static readonly Dictionary<string, (string itemName, int itemIndex)> _pendingDoorUnlocks = new();

    // Doors whose sub-scene wasn't loaded yet; polled by TickExpansionDoors.
    private static readonly List<(string doorId, string itemName, int itemIndex)> _pendingExpansions = new();
    private static int _expansionTickCounter = 0;
    private const int ExpansionTickInterval = 180; // ~3 seconds at 60 fps

    /// <summary>
    /// Per-frame; every ~3 s retries expansion doors whose sub-scene wasn't loaded, advancing
    /// the watermark as each opens.
    /// </summary>
    internal static void TickExpansionDoors()
    {
        if (_pendingExpansions.Count == 0) return;
        if (++_expansionTickCounter < ExpansionTickInterval) return;
        _expansionTickCounter = 0;

        for (int i = _pendingExpansions.Count - 1; i >= 0; i--)
        {
            var (doorId, itemName, idx) = _pendingExpansions[i];
            var door = Resources.FindObjectsOfTypeAll<AccessDoor>()
                                 .FirstOrDefault(d => d._id == doorId);
            if (door == null) continue;

            OpenDoor(door, doorId, itemName);
            if (idx >= 0) // -1 = debug-panel grant; no watermark/deferred entry to touch
            {
                Plugin.Instance.SaveManager.UpdateLastItemIndex(idx);
                Plugin.Instance.SaveManager.RemoveDeferredItem(idx);
            }
            _pendingExpansions.RemoveAt(i);
        }
    }

    /// <summary>
    /// Called by <c>AccessDoorPurchasePatch</c> after the check is sent; opens a door whose
    /// item was held waiting for it.
    /// </summary>
    internal static void TriggerPendingDoorUnlock(string doorId)
    {
        if (!_pendingDoorUnlocks.TryGetValue(doorId, out var pending)) return;
        _pendingDoorUnlocks.Remove(doorId);

        var door = Resources.FindObjectsOfTypeAll<AccessDoor>()
                             .FirstOrDefault(d => d._id == doorId);
        if (door == null)
        {
            // Sub-scene not loaded — promote to the normal deferred-open queue.
            if (!_pendingExpansions.Exists(e => e.doorId == doorId))
                _pendingExpansions.Add((doorId, pending.itemName, pending.itemIndex));
            Logger.Info($"[AP] TriggerPendingDoorUnlock: door '{doorId}' not in scene — promoted to expansion poll");
            return;
        }

        OpenDoor(door, doorId, pending.itemName);
        if (pending.itemIndex >= 0) // -1 = debug-panel grant; no watermark/deferred entry to touch
        {
            Plugin.Instance.SaveManager.UpdateLastItemIndex(pending.itemIndex);
            Plugin.Instance.SaveManager.RemoveDeferredItem(pending.itemIndex);
        }
    }

    /// <summary>
    /// Opens the item's Conservatory expansion door. Returns false when it must wait — for its
    /// terminal check, or for its sub-scene to load.
    /// </summary>
    private static bool ApplyConservatoryExpansion(Data.ItemInfo item, int itemIndex)
    {
        var doorId = ItemTable.GetExpansionDoorId(item.Id);
        if (doorId == null || doorId.StartsWith("PLACEHOLDER"))
        {
            Logger.Warning($"[AP] No confirmed door ID for expansion '{item.Name}' (id={item.Id}) — run the AccessDoor dump and fill in ItemTable.");
            return true; // don't retry — nothing will change until the placeholder is replaced
        }

        Notify($"Received: {item.Name}");

        if (!Data.LocationTable.TryGetByObjectName(doorId, out var locInfo) || locInfo == null)
        {
            Logger.Warning($"[AP] ApplyConservatoryExpansion: no LocationTable entry for door '{doorId}' — opening immediately.");
            goto openNow;
        }

        // Terminal not pressed yet — hold the door (see _pendingDoorUnlocks).
        if (!Plugin.Instance.SaveManager.IsChecked(locInfo.Id))
        {
            // A real index replaces a -1 debug entry.
            if (!_pendingDoorUnlocks.TryGetValue(doorId, out var existing) || existing.itemIndex < 0)
                _pendingDoorUnlocks[doorId] = (item.Name, itemIndex);
            // The hold can span sessions while later items advance the watermark past it.
            if (itemIndex >= 0)
                Plugin.Instance.SaveManager.AddDeferredItem(itemIndex);
            Logger.Info($"[AP] Expansion '{item.Name}' received before terminal check — door '{doorId}' held until check is sent.");
            return false; // watermark NOT advanced — TriggerPendingDoorUnlock will advance it
        }

        openNow:
        var door = Resources.FindObjectsOfTypeAll<AccessDoor>()
                             .FirstOrDefault(d => d._id == doorId);
        if (door == null)
        {
            if (!_pendingExpansions.Exists(e => e.doorId == doorId))
                _pendingExpansions.Add((doorId, item.Name, itemIndex));
            if (itemIndex >= 0)
                Plugin.Instance.SaveManager.AddDeferredItem(itemIndex);
            Logger.Info($"[AP] AccessDoor '{doorId}' not in scene yet — queued for deferred open.");
            return false; // TickExpansionDoors advances the watermark when the door opens
        }

        OpenDoor(door, doorId, item.Name);
        return true;
    }

    private static void OpenDoor(AccessDoor door, string doorId, string itemName)
    {
        door._model?.Push(AccessDoor.State.OPEN);
        door._model?.NotifyParticipants();
        door.ForceUpdateBarrierController();
        door.ForceUpdateAnimator();
        Logger.Info($"[AP] Opened conservatory expansion: {itemName} (door='{doorId}')");
    }

    /// <summary>Auto mode: grants the zone teleporter when a gate opens in-world (RegionGatePatch).</summary>
    internal static void TryGrantRegionTeleporterForSwitch(string switchName, string sceneName)
    {
        if (!RegionTable.TryGetRegionForSwitch(switchName, sceneName, out var regionName)) return;
        if (!GrantRegionTeleporter(regionName))
            _pendingRegionTeleporters.Add(regionName); // retried by TickRegionTeleporters
    }

    /// <summary>
    /// Grants the region's zone teleporter. Returns <c>false</c> when not yet confirmed — callers
    /// must park it for retry, as the item's watermark has already advanced.
    /// </summary>
    private static bool GrantRegionTeleporter(string regionName)
    {
        if (!RegionToZoneTeleporter.TryGetValue(regionName, out var gadgetName))
            return true; // no teleporter mapped for this region — nothing to grant

        return EnsureGadgetBlueprint(gadgetName);
    }

    /// <summary>
    /// Ensures the player possesses the gadget's blueprint. Returns <c>true</c> only when
    /// confirmed; <c>false</c> means retry (director or assets not ready, or AddBlueprint threw).
    /// </summary>
    /// <remarks>
    /// Verify with <c>HasBlueprint</c> (the set AddBlueprint writes), never
    /// <c>IsBlueprintUnlocked</c> — that reads a separate set only vanilla unlock events fill, so
    /// it stays false forever in AP mode and the retry loop re-grants endlessly.
    /// </remarks>
    private static bool EnsureGadgetBlueprint(string gadgetName)
    {
        var director = SceneContext.Instance?.GadgetDirector;
        if (director == null) return false; // scene loading — retry

        var gadgetDef = Resources.FindObjectsOfTypeAll<GadgetDefinition>()
                                 .FirstOrDefault(d => d.name == gadgetName);
        if (gadgetDef == null) return false; // gadget assets not loaded yet — retry

        if (director.HasBlueprint(gadgetDef)) return true;

        GrantSingleGadget(director, gadgetName);

        // GrantSingleGadget swallows AddBlueprint's mid-load throws, so check the result.
        return director.HasBlueprint(gadgetDef);
    }

    // Regions whose zone teleporter grant failed; drained by TickRegionTeleporters.
    private static readonly HashSet<string> _pendingRegionTeleporters = new();
    // Retries only bridge scene loading. Past the cap the failure is systemic — give up until
    // the next connect rather than re-grant (and inflate the inventory) all session.
    private static readonly Dictionary<string, int> _teleporterRetryCounts = new();
    private const int MaxTeleporterRetries = 40; // ~2 minutes at one attempt per 3 s
    private static int _teleporterTickCounter = 0;
    private const int TeleporterTickInterval = 180; // ~3 seconds at 60 fps

    // Set on connect: in bundled mode, re-check every unlocked region's teleporter, healing a
    // grant lost in an earlier session (the access item never replays below the watermark).
    private static volatile bool _pendingBundledValidation;

    // The DroneStation blueprint's vanilla source (a treasure pod) is a check in AP, so Quantum
    // Drone Station items bundle it. Validated on connect to heal saves that got modules without it.
    private const string DroneStationGadgetName = "DroneStation"; // confirmed via DumpGadgets
    private static volatile bool _pendingDroneBlueprintValidation;
    private static int _droneBlueprintRetryCount;

    /// <summary>Schedules the connect-time gadget validation sweep (bundled zone teleporters
    /// and the DroneStation blueprint). Safe from any thread.</summary>
    public static void ScheduleBundledTeleporterValidation()
    {
        _pendingBundledValidation = true;
        _pendingDroneBlueprintValidation = true;
        _droneBlueprintRetryCount = 0;
    }

    /// <summary>Clears retry state on disconnect (the next connect re-schedules validation).</summary>
    public static void ResetRegionTeleporterRetries()
    {
        _pendingRegionTeleporters.Clear();
        _teleporterRetryCounts.Clear();
        _pendingBundledValidation = false;
        _pendingDroneBlueprintValidation = false;
        _droneBlueprintRetryCount = 0;
    }

    /// <summary>Per-frame; every ~3 s retries teleporter grants and runs connect-time validation.</summary>
    internal static void TickRegionTeleporters()
    {
        if (!_pendingBundledValidation && !_pendingDroneBlueprintValidation
            && _pendingRegionTeleporters.Count == 0) return;
        if (++_teleporterTickCounter < TeleporterTickInterval) return;
        _teleporterTickCounter = 0;

        // Any received Quantum Drone Station implies the DroneStation blueprint.
        if (_pendingDroneBlueprintValidation && SceneContext.Instance?.Player != null)
        {
            if (!HasReceivedQuantumDroneStation())
            {
                _pendingDroneBlueprintValidation = false;
            }
            else if (EnsureGadgetBlueprint(DroneStationGadgetName))
            {
                _pendingDroneBlueprintValidation = false;
                Logger.Info("[AP] DroneStation blueprint verified (connect-time validation).");
            }
            else if (++_droneBlueprintRetryCount >= MaxTeleporterRetries)
            {
                _pendingDroneBlueprintValidation = false;
                Logger.Warning(
                    $"[AP] DroneStation blueprint could not be verified after {_droneBlueprintRetryCount} attempts — " +
                    "giving up until the next connection.");
            }
        }

        // Bundled mode: every unlocked region must have its teleporter.
        if (_pendingBundledValidation && SceneContext.Instance?.Player != null)
        {
            _pendingBundledValidation = false;
            var mode = Plugin.Instance.ApClient.SlotData?.RegionAccessMode ?? "vanilla";
            if (mode == "bundled")
            {
                foreach (var regionName in RegionToZoneTeleporter.Keys)
                    if (Plugin.Instance.SaveManager.IsRegionUnlocked(regionName))
                        _pendingRegionTeleporters.Add(regionName);
                if (_pendingRegionTeleporters.Count > 0)
                    Logger.Info(
                        $"[AP] Bundled teleporter validation: checking {_pendingRegionTeleporters.Count} unlocked region(s).");
            }
        }

        if (_pendingRegionTeleporters.Count == 0) return;

        foreach (var regionName in _pendingRegionTeleporters.ToArray())
        {
            if (GrantRegionTeleporter(regionName))
            {
                _pendingRegionTeleporters.Remove(regionName);
                _teleporterRetryCounts.Remove(regionName);
                Logger.Info($"[AP] Zone teleporter for '{regionName}' granted (retry/validation pass).");
                continue;
            }

            int attempts = _teleporterRetryCounts.GetValueOrDefault(regionName) + 1;
            _teleporterRetryCounts[regionName] = attempts;
            if (attempts >= MaxTeleporterRetries)
            {
                _pendingRegionTeleporters.Remove(regionName);
                _teleporterRetryCounts.Remove(regionName);
                Logger.Warning(
                    $"[AP] Zone teleporter for '{regionName}' could not be verified after {attempts} attempts — " +
                    "giving up until the next connection (bundled validation will retry it).");
            }
        }
    }

    /// <summary>True when the server snapshot holds at least one Quantum Drone Station.</summary>
    private static bool HasReceivedQuantumDroneStation()
    {
        var items = Plugin.Instance.ApClient.Session?.Items?.AllItemsReceived;
        if (items == null) return false;
        for (int i = 0; i < items.Count; i++)
            if (items[i].ItemId == ItemTable.QuantumDroneStation) return true;
        return false;
    }

    /// <summary>
    /// Last known level per upgrade, from <c>OnUpgradeChanged</c>, save restore, and our own writes.
    /// </summary>
    private static readonly Dictionary<string, int> _upgradeLevels = new();

    private static bool _warnedNullModel;

    public static void TrackUpgradeLevel(string upgradeName, int newLevel)
        => _upgradeLevels[upgradeName] = newLevel;

    /// <summary>
    /// Called on disconnect, so the next save or slot does not inherit levels — stale ones made
    /// grants skip as "already maxed".
    /// </summary>
    public static void ResetUpgradeTracking() => _upgradeLevels.Clear();

    /// <summary>
    /// True once <c>UpgradeModel.Push</c> has restored the save's levels this session. Before
    /// that every level reads -1, so reconciliation would "repair" the whole set.
    /// </summary>
    public static bool UpgradesRestored { get; private set; }

    /// <summary>
    /// Called on disconnect and scene change. Leaves <see cref="NoRestoreExpected"/> alone — a
    /// new game sets it and then changes scene.
    /// </summary>
    public static void ResetUpgradeRestoreState() => UpgradesRestored = false;

    /// <summary>
    /// True for a save created this session: no <c>Push</c> is coming, so reconciliation runs
    /// immediately instead of waiting out the grace period.
    /// </summary>
    public static bool NoRestoreExpected { get; private set; }

    /// <summary>Marks the active save as freshly created — no restore <c>Push</c> will arrive.</summary>
    public static void MarkFreshSave() => NoRestoreExpected = true;

    /// <summary>Marks the active save as loaded from disk — a restore <c>Push</c> is expected.</summary>
    public static void MarkLoadedSave() => NoRestoreExpected = false;

    /// <summary>
    /// Called by <c>UpgradeModelPushPatch</c>; rebuilds the tracked cache from the restored model.
    /// </summary>
    public static void OnUpgradesRestored()
    {
        UpgradesRestored = true;

        foreach (var info in Data.ItemTable.All)
        {
            var name = GetUpgradeName(info.Id);
            if (name == null || _upgradeLevels.ContainsKey(name)) continue;

            var def = Resources.FindObjectsOfTypeAll<UpgradeDefinition>()
                               .FirstOrDefault(d => d.name == name);
            if (def == null) continue;

            int level = GetRealModelLevel(def);
            if (level >= 0) _upgradeLevels[name] = level;
        }
    }

    /// <summary>
    /// Last tracked level, or -1. <c>UpgradeModelGetLevelPatch</c> floors its result with this so
    /// queries like <c>PlayerUpgradeObtainedQueryComponent</c> still see AP-granted upgrades.
    /// </summary>
    public static int GetTrackedLevel(string upgradeName)
        => _upgradeLevels.TryGetValue(upgradeName, out var lvl) ? lvl : -1;

    /// <summary>
    /// Writes an absolute level into the <c>UpgradeModel</c> — what SR2 saves and queries read.
    /// </summary>
    /// <remarks>
    /// Never <c>ActorUpgradeHandler.ApplyUpgrade</c>: it applies stat modifiers only, so the
    /// upgrade is never saved and invisible to the game's own upgrade queries. The handler
    /// applies modifiers itself via OnUpgradeChanged, so calling both would apply them twice.
    /// </remarks>
    private static bool WriteModelLevel(UpgradeDefinition def, int level)
    {
        var model = UpgradeHandler?._model;
        if (model == null) return false;

        bool previous = IsApplyingItem;
        try
        {
            IsApplyingItem = true;          // allow the write past FabricatorUpgradeBlockPatch
            model.SetUpgradeLevel(def, level);
            return true;
        }
        catch (Exception ex)
        {
            Logger.Warning($"[AP] SetUpgradeLevel('{def?.name}', {level}) failed: {ex.Message}");
            return false;
        }
        finally { IsApplyingItem = previous; }
    }

    /// <summary>
    /// The true model level, or -1 if not ready. Bypasses <c>UpgradeModelGetLevelPatch</c>;
    /// the flag is restored, not cleared, so this is safe inside an item application.
    /// </summary>
    public static int GetRealModelLevel(UpgradeDefinition def)
    {
        if (def == null || UpgradeHandler == null) return -1;

        // A null model reads like "the player has nothing" — say so once in the log.
        if (UpgradeHandler._model == null)
        {
            if (!_warnedNullModel)
            {
                _warnedNullModel = true;
                Logger.Warning(
                    "[AP] ActorUpgradeHandler._model is NULL — every upgrade level reads -1, so " +
                    "watermark reconciliation cannot see what the save actually holds.");
            }
            return -1;
        }

        bool previous = IsApplyingItem;
        try
        {
            IsApplyingItem = true;
            return UpgradeHandler._model?.GetUpgradeLevel(def) ?? -1;
        }
        catch { return -1; }
        finally { IsApplyingItem = previous; }
    }

    /// <summary>AP item ID → UpgradeDefinition name, or null if not an upgrade.</summary>
    private static string? GetUpgradeName(long itemId) => itemId switch
    {
        ItemTable.ProgressiveHealthTank        => "HealthCapacity",
        ItemTable.ProgressiveEnergyTank        => "EnergyCapacity",
        ItemTable.ProgressiveExtraTank         => "AmmoSlots",
        ItemTable.ProgressiveJetpack           => "Jetpack",
        ItemTable.ProgressiveWaterTank         => "LiquidSlot",
        ItemTable.ProgressiveDashBoots         => "RunEfficiency",
        ItemTable.ProgressiveTankBooster       => "AmmoCapacity",
        ItemTable.ProgressivePowerInjector     => "EnergyDelay",
        ItemTable.ProgressiveRegenerator       => "EnergyRegen",
        ItemTable.ProgressiveGoldenSureshot    => "GoldenSureshot",
        ItemTable.ProgressiveShadowSureshot    => "ShadowSureshot",
        ItemTable.ProgressiveTankGuard         => "TankGuard",
        ItemTable.PulseWave         => "PulseWave",
        ItemTable.ResourceHarvester => "ResourceNodeHarvester",
        ItemTable.DroneArchiveKey   => "ArchiveKey",
        _                           => null
    };

    /// <summary>
    /// Reconciles each upgrade's model level with the count received up to the watermark —
    /// e.g. after a crash before autosave. Raises missing levels and lowers undelivered ones
    /// (the latter only when the server history covers the watermark). Idempotent; main thread.
    /// </summary>
    public static void ValidateAndRepairUpgrades()
    {
        if (UpgradeHandler == null) return;

        var session = Plugin.Instance.ApClient.Session;
        if (session == null) return;

        int watermark = Plugin.Instance.SaveManager.LastItemIndex;
        var snapshot  = session.Items.AllItemsReceived;
        if (snapshot == null || snapshot.Count == 0) return;

        // Count per upgrade; level = count - 1.
        var expectedLevels = new Dictionary<string, int>();
        for (int i = 0; i <= watermark && i < snapshot.Count; i++)
        {
            var name = GetUpgradeName(snapshot[i].ItemId);
            if (name == null) continue;
            expectedLevels[name] = expectedLevels.GetValueOrDefault(name) + 1;
        }

        // Seed every upgrade at 0 so one held but never received is still examined.
        foreach (var d in ItemTable.All)
        {
            var n = GetUpgradeName(d.Id);
            if (n != null) expectedLevels.TryAdd(n, 0);
        }

        if (expectedLevels.Count == 0) return;

        // A history shorter than the watermark undercounts, and lowering would strip real upgrades.
        bool historyCoversWatermark = snapshot.Count > watermark;

        foreach (var kvp in expectedLevels)
        {
            var upgradeName = kvp.Key;
            int targetLevel = kvp.Value - 1; // convert count → 0-based level index

            var upgradeDef = Resources.FindObjectsOfTypeAll<UpgradeDefinition>()
                                      .FirstOrDefault(d => d.name == upgradeName);
            if (upgradeDef == null) continue;

            int currentLevel = GetRealModelLevel(upgradeDef);

            // Always logged — the first thing to check when a player reports a missing tier.
            Logger.Info(
                $"[AP] Upgrade validation: '{upgradeName}' items received={kvp.Value} " +
                $"(expected level {targetLevel}), SR2 model level={currentLevel}");

            if (currentLevel > targetLevel)
            {
                // Ahead of the server. A bound save has no legitimate source for that (the
                // Fabricator's vanilla grant is suppressed), so the server's count wins.
                if (!historyCoversWatermark)
                {
                    Logger.Warning(
                        $"[AP] Upgrade drift: '{upgradeName}' SR2 level={currentLevel} exceeds " +
                        $"AP-expected {targetLevel} (items received={kvp.Value}), but the server " +
                        $"history ({snapshot.Count} item(s)) does not cover the watermark ({watermark}) " +
                        $"— not lowered, the expected level may be undercounted.");
                    _upgradeLevels[upgradeName] = currentLevel;
                    continue;
                }

                Logger.Warning(
                    $"[AP] Upgrade drift: '{upgradeName}' SR2 level={currentLevel} exceeds " +
                    $"AP-expected {targetLevel} (items received={kvp.Value}) — lowering to match the server");

                bool lowered  = WriteModelLevel(upgradeDef, targetLevel);
                int afterDrop = GetRealModelLevel(upgradeDef);
                _upgradeLevels[upgradeName] = afterDrop;
                if (afterDrop == targetLevel)
                    Logger.Info(
                        $"[AP] Upgrade drift read-back: '{upgradeName}' now reports {afterDrop} — " +
                        $"lowered{(lowered ? "" : " (SetUpgradeLevel threw, likely UI)")}");
                else
                    Logger.Warning(
                        $"[AP] Upgrade drift read-back: '{upgradeName}' now reports {afterDrop} " +
                        $"(wanted {targetLevel}) — LOWER DID NOT LAND");
                continue;
            }

            if (currentLevel == targetLevel)
            {
                // Correct, but still refill the tracked cache after a disconnect cleared it.
                _upgradeLevels[upgradeName] = currentLevel;
                continue;
            }

            Logger.Info(
                $"[AP] Upgrade repair: '{upgradeName}' SR2 level={currentLevel} < expected={targetLevel} — correcting");

            // The read-back is the verdict: SetUpgradeLevel can throw from a UI listener after
            // the model already advanced.
            bool wrote   = WriteModelLevel(upgradeDef, targetLevel);
            int readBack = GetRealModelLevel(upgradeDef);
            if (readBack >= targetLevel)
            {
                _upgradeLevels[upgradeName] = readBack;
                Logger.Info(
                    $"[AP] Upgrade repair read-back: '{upgradeName}' now reports {readBack} " +
                    $"(wanted {targetLevel}) — write landed{(wrote ? "" : " (SetUpgradeLevel threw, likely UI)")}");
            }
            else
            {
                Logger.Warning(
                    $"[AP] Upgrade repair read-back: '{upgradeName}' now reports {readBack} " +
                    $"(wanted {targetLevel}) — WRITE DID NOT LAND");
            }
        }
    }

    private static bool ApplyUpgrade(Data.ItemInfo item, ApItemInfo? apItem, int itemIndex)
    {
        if (UpgradeHandler == null)
        {
            Logger.Warning("[AP] ActorUpgradeHandler not yet cached — requeuing upgrade");
            if (apItem != null) Plugin.Instance.ApClient.RequeueItem(apItem, itemIndex);
            return false;
        }

        var upgradeName = GetUpgradeName(item.Id);
        if (upgradeName == null) return true; // unknown upgrade ID — don't retry

        var upgradeDef = Resources.FindObjectsOfTypeAll<UpgradeDefinition>()
                                  .FirstOrDefault(d => d.name == upgradeName);
        if (upgradeDef == null)
        {
            Logger.Warning($"[AP] UpgradeDefinition '{upgradeName}' not found");
            return true; // asset issue — retrying won't help
        }

        // Levels are absolute: -1 (locked) to LevelCount-1. The cache covers several grants in
        // one frame before the model catches up. IsApplyingItem bypasses UpgradeModelGetLevelPatch.
        IsApplyingItem = true;
        int modelLevel   = UpgradeHandler._model?.GetUpgradeLevel(upgradeDef) ?? -1;
        IsApplyingItem = false;
        int cachedLevel  = _upgradeLevels.TryGetValue(upgradeName, out var lvl) ? lvl : -1;
        int currentLevel = System.Math.Max(modelLevel, cachedLevel);
        int maxLevel     = upgradeDef.LevelCount - 1;
        int targetLevel  = currentLevel + 1;

        if (targetLevel > maxLevel)
        {
            Logger.Info($"[AP] Upgrade '{upgradeName}' already at max level ({maxLevel}) — skipping");
            Notify($"Received: {item.Name} (already maxed)");
            return true;
        }

        // Judge by read-back: SetUpgradeLevel can throw from an uninitialized UI listener AFTER
        // the model advanced, which is a success.
        bool wrote     = WriteModelLevel(upgradeDef, targetLevel);
        int levelAfter = GetRealModelLevel(upgradeDef);
        if (levelAfter < targetLevel)
        {
            // Not requeued (would retry every frame). Validation counts this item and retries once.
            Logger.Warning(
                $"[AP] Upgrade write did not land for '{upgradeName}' " +
                $"(wanted {targetLevel}, model reports {levelAfter}) — scheduling upgrade validation to retry");
            Plugin.Instance.ApClient.ScheduleUpgradeValidation();
            return true;
        }
        if (!wrote)
            Logger.Warning(
                $"[AP] SetUpgradeLevel threw (UI prefab not ready?) but model DID advance to {levelAfter} — " +
                "upgrade applied successfully. UI will refresh when the upgrade screen is opened.");

        _upgradeLevels[upgradeName] = targetLevel; // the change callback may fire late or not at all
        Logger.Info($"[AP] Applied upgrade: {upgradeName} → level {targetLevel}/{maxLevel}");
        Notify($"Received: {item.Name}");
        return true;
    }

    // AP item ID → GadgetDefinition.name. All confirmed via DumpGadgets.
    private static readonly Dictionary<long, string> SimpleGadgets = new()
    {
        // Zone Teleporters
        [ItemTable.TeleporterEmberValley]      = "TeleporterZoneGorge",
        [ItemTable.TeleporterStarlightStrand]  = "TeleporterZoneStrand",
        [ItemTable.TeleporterPowderfallBluffs] = "TeleporterZoneBluffs",
        [ItemTable.TeleporterGreyLabyrinth]    = "TeleporterZoneLabyrinth",

        // Home Teleporters
        [ItemTable.HomeTeleporterBlue]   = "TeleporterHomeBlue",
        [ItemTable.HomeTeleporterGreen]  = "TeleporterHomeGreen",
        [ItemTable.HomeTeleporterRed]    = "TeleporterHomeRed",
        [ItemTable.HomeTeleporterYellow] = "TeleporterHomeYellow",

        // Warp Depots
        [ItemTable.WarpDepotGrey]   = "WarpDepotGrey",
        [ItemTable.WarpDepotBerry]  = "WarpDepotBerry",    // orange-coloured variant
        [ItemTable.WarpDepotViolet] = "WarpDepotViolet",
        [ItemTable.WarpDepotSnowy]  = "WarpDepotSnowy",

        // Functional gadgets
        [ItemTable.MarketLink]         = "MarketLink",
        [ItemTable.SuperHydroTurret]   = "SuperHydroTurret",
        [ItemTable.PortableScareSlime] = "PortableScareSlime",
        [ItemTable.GordoSnareAdvanced] = "GordoSnareAdvanced",
        [ItemTable.MedStation]         = "MedStation",
        [ItemTable.DreamLanternT2]     = "DreamLanternT2",

        // Movement / utility gadgets
        [ItemTable.DashPad]            = "DashPad",
        [ItemTable.SpringPad]          = "SpringPad",
        [ItemTable.PortableWaterTap]   = "PortableWaterTap",

        // Always in the pool. Refinery Link's vanilla source (Mochi's intro call) is always
        // suppressed — see ConversationActiveTrackerPatch.AlwaysApGiftAssets.
        [ItemTable.RefineryLink]       = "RefineryLink",
        [ItemTable.PortableFabricator] = "PortableFabricator",

        // ×3 for prismacore/slimepedia goals; duplicates hit GrantSingleGadget's guard.
        [ItemTable.DisruptionDetector] = "PrismaDisruptionDetector",

        // Radiant Projector Blueprint
        [ItemTable.RadiantProjectorBlueprint] = "EnergyBeamNode",
    };

    /// <summary>Vac palette item ID -> <c>Palette</c> asset name (docs/dumps/1.3/palettes.txt).</summary>
    internal static readonly System.Collections.Generic.Dictionary<long, string> PaletteAssets = new()
    {
        [Data.ItemTable.Palette_Blue] = "VacPalette_Blue",
        [Data.ItemTable.Palette_Purple] = "VacPalette_Purple",
        [Data.ItemTable.Palette_Green] = "VacPalette_Green",
        [Data.ItemTable.Palette_Red] = "VacPalette_Red",
        [Data.ItemTable.Palette_Pink] = "VacPalette_Pink",
        [Data.ItemTable.Palette_Angelic] = "VacPalette_Angelic",
        [Data.ItemTable.Palette_Tidepools] = "VacPalette_Tidepools",
        [Data.ItemTable.Palette_Magma] = "VacPalette_Magma",
        [Data.ItemTable.Palette_StarlightStrand] = "VacPalette_StarlightStrand",
        [Data.ItemTable.Palette_Gray] = "VacPalette_Gray",
        [Data.ItemTable.Palette_PowderfallBluffs] = "VacPalette_PowderfallBluffs",
        [Data.ItemTable.Palette_Peach] = "VacPalette_Peach",
        [Data.ItemTable.Palette_HotRod] = "VacPalette_HotRod",
        [Data.ItemTable.Palette_Punk] = "VacPalette_Punk",
        [Data.ItemTable.Palette_LilacDaze] = "VacPalette_LilacDaze",
        [Data.ItemTable.Palette_Dreamland] = "VacPalette_Dreamland",
        [Data.ItemTable.Palette_DreamPop] = "VacPalette_DreamPop",
        [Data.ItemTable.Palette_Prismatic] = "VacPalette_Prismatic",
        [Data.ItemTable.Palette_Candied] = "VacPalette_Candied",
        [Data.ItemTable.Palette_Gold] = "VacPalette_Gold",
        [Data.ItemTable.Palette_Mossy] = "VacPalette_Mossy",
        [Data.ItemTable.Palette_Burgundy] = "VacPalette_Burgundy",
    };

#if DEBUG
    /// <summary>Debug: grants the first vac palette the player does not yet own.</summary>
    internal static void DebugGrantNextPalette()
    {
        var director = SceneContext.Instance?.PaletteDirector;
        if (director == null) { Logger.Warning("[AP-Debug] No PaletteDirector — load a save first"); return; }

        var all = Resources.FindObjectsOfTypeAll<Il2CppMonomiPark.SlimeRancher.Ranch.Palette>();
        foreach (var kv in PaletteAssets)
        {
            var pal = all.FirstOrDefault(p => p != null && p.name == kv.Value);
            if (pal == null || director.IsOwned(pal)) continue;
            ApplyById(kv.Key, -1);
            return;
        }
        Logger.Info("[AP-Debug] Every vac palette is already owned");
    }

    /// <summary>
    /// Debug: re-grants every owned palette, repairing ones left owned-but-locked by the first
    /// ApplyPalette.
    /// </summary>
    internal static void DebugRepairOwnedPalettes()
    {
        var director = SceneContext.Instance?.PaletteDirector;
        if (director == null) { Logger.Warning("[AP-Debug] No PaletteDirector — load a save first"); return; }

        var all = Resources.FindObjectsOfTypeAll<Il2CppMonomiPark.SlimeRancher.Ranch.Palette>();
        int touched = 0;
        foreach (var kv in PaletteAssets)
        {
            var pal = all.FirstOrDefault(p => p != null && p.name == kv.Value);
            if (pal == null || !director.IsOwned(pal)) continue;
            ApplyById(kv.Key, -1);
            touched++;
        }
        Logger.Info($"[AP-Debug] Re-applied {touched} owned palette(s)");
    }

    /// <summary>Debug: logs ownership of all 22 vac palettes.</summary>
    internal static void DebugLogPaletteOwnership()
    {
        var director = SceneContext.Instance?.PaletteDirector;
        if (director == null) { Logger.Warning("[AP-Debug] No PaletteDirector — load a save first"); return; }

        var all = Resources.FindObjectsOfTypeAll<Il2CppMonomiPark.SlimeRancher.Ranch.Palette>();
        int owned = 0;
        foreach (var kv in PaletteAssets)
        {
            var pal = all.FirstOrDefault(p => p != null && p.name == kv.Value);
            bool isOwned = pal != null && director.IsOwned(pal);
            if (isOwned) owned++;
            Logger.Info($"[AP-Debug]   {(isOwned ? "OWNED " : "      ")} {kv.Value}{(pal == null ? "  (asset not found)" : "")}");
        }
        Logger.Info($"[AP-Debug] Vac palettes owned: {owned}/{PaletteAssets.Count}");
    }
#endif

    /// <summary>
    /// Owns the palette and unlocks its pedia entry — both are needed: the vac style screen lists
    /// unlocked pedia entries, so ownership alone shows nothing.
    /// </summary>
    /// <remarks>
    /// <c>SetOwned</c> is CallerCount(0), so the result is verified through <c>IsOwned</c>.
    /// Idempotent, so not ephemeral-guarded. The apworld never places a palette that is also a
    /// shop check, so this cannot make a Polestar check unbuyable.
    /// </remarks>
    private static bool ApplyPalette(Data.ItemInfo item, ApItemInfo? apItem, int itemIndex)
    {
        var director = SceneContext.Instance?.PaletteDirector;
        if (director == null)
        {
            if (apItem != null) Plugin.Instance.ApClient.RequeueItem(apItem, itemIndex);
            return false;
        }

        if (!PaletteAssets.TryGetValue(item.Id, out var assetName))
        {
            Logger.Warning($"[AP] Unhandled palette item ID: {item.Id}");
            return true;   // unknown — do not retry forever
        }

        var palette = Resources.FindObjectsOfTypeAll<Il2CppMonomiPark.SlimeRancher.Ranch.Palette>()
                               .FirstOrDefault(p => p != null && p.name == assetName);
        if (palette == null)
        {
            Logger.Warning($"[AP] Palette asset '{assetName}' not found in loaded assets");
            return true;
        }

        var pedia = SceneContext.Instance?.PediaDirector;
        if (pedia == null)
        {
            if (apItem != null) Plugin.Instance.ApClient.RequeueItem(apItem, itemIndex);
            return false;
        }

        // Ownership alone is invisible — the palette screen lists unlocked pedia entries. Both
        // steps always run, so an owned-but-locked palette is repaired on replay.
        bool changed = false;
        try
        {
            if (!director.IsOwned(palette))
            {
                director.SetOwned(palette);
                changed = true;
                if (!director.IsOwned(palette))
                    Logger.Warning($"[AP] SetOwned('{assetName}') returned but the palette is still not owned");
            }

            var entry = palette._pediaEntry;
            if (entry == null)
                Logger.Warning($"[AP] Palette '{assetName}' has no pedia entry — it will not appear in the palette screen");
            else if (pedia.Unlock(entry, false))
                changed = true;   // showPopup false: the received-item notification already announces it
        }
        catch (System.Exception ex)
        {
            Logger.Warning($"[AP] Palette grant '{assetName}' failed: {ex.Message}");
            return true;
        }

        if (!changed)
        {
            Logger.Info($"[AP] Palette '{assetName}' already owned and unlocked — nothing to do");
            return true;
        }

        Logger.Info($"[AP] Palette unlocked: {item.Name} ('{assetName}')");
        Notify($"Received: {item.Name}");
        return true;
    }

    private static bool ApplyGadget(Data.ItemInfo item, ApItemInfo? apItem, int itemIndex)
    {
        var gadgetDirector = SceneContext.Instance.GadgetDirector;
        if (gadgetDirector == null)
        {
            if (apItem != null) Plugin.Instance.ApClient.RequeueItem(apItem, itemIndex);
            return false;
        }

        if (SimpleGadgets.TryGetValue(item.Id, out var gadgetName))
        {
            GrantSingleGadget(gadgetDirector, gadgetName);
            Notify($"Received: {item.Name}");
            return true;
        }

        Logger.Warning($"[AP] Unhandled gadget item ID: {item.Id}");
        return true; // unknown gadget — don't retry
    }

    /// <summary>
    /// Unlocks the gadget's blueprint (<c>AddBlueprint(def, false)</c>, no pedia popup) and adds
    /// one unit (<c>AddItem</c> — not <c>OnGadgetPickedUp</c>, which is unreliable outside pick-up).
    /// </summary>
    private static void GrantSingleGadget(GadgetDirector director, string gadgetName)
    {
        var gadgetDef = Resources.FindObjectsOfTypeAll<GadgetDefinition>()
                                 .FirstOrDefault(d => d.name == gadgetName);
        if (gadgetDef == null)
        {
            Logger.Warning($"[AP] GadgetDefinition '{gadgetName}' not found in loaded assets");
            return;
        }

        // Already granted (earlier session or replay) — don't add a duplicate unit.
        // HasBlueprint, not IsBlueprintUnlocked (see EnsureGadgetBlueprint).
        if (director.HasBlueprint(gadgetDef))
        {
            Logger.Info($"[AP] Gadget '{gadgetName}' blueprint already owned — skipping");
            return;
        }

        // Both can throw from an analytics ctor during scene load, AFTER the real update —
        // so catch and verify rather than treat a throw as failure.
        try
        {
            director.AddBlueprint(gadgetDef, false);
        }
        catch (Exception ex)
        {
            if (!director.HasBlueprint(gadgetDef))
            {
                Logger.Warning(
                    $"[AP] AddBlueprint threw and blueprint is still missing for '{gadgetName}' — grant failed: {ex.Message}");
                return;
            }
            Logger.Warning(
                $"[AP] AddBlueprint threw (analytics? player location not ready) but '{gadgetName}' IS owned — OK: {ex.Message}");
        }

        try
        {
            director.AddItem(gadgetDef, 1); // GadgetDefinition : IdentifiableType
        }
        catch (Exception ex)
        {
            Logger.Warning(
                $"[AP] AddItem threw for '{gadgetName}' (analytics?): {ex.Message}");
        }

        Logger.Info($"[AP] Granted gadget: {gadgetName}");
    }

    private static bool ApplyFiller(Data.ItemInfo item, ApItemInfo? apItem, int itemIndex)
    {
        Logger.Info($"[AP] Applying filler: {item.Name} (id={item.Id}, idx={itemIndex})");

        if (item.Id is ItemTable.Newbucks250 or ItemTable.Newbucks500 or ItemTable.Newbucks1000)
            return ApplyNewbucks(item, apItem, itemIndex);

        // Rate-limited like a trap, so it can return false.
        if (item.Id == ItemTable.SlimeRing)
            return TrapHandler.ApplySlimeRingTrap(apItem, itemIndex);

        // Caches — 10 random picks from the pool into the refinery
        var pool = item.Id switch
        {
            ItemTable.CommonPlortCache            => CommonPlorts,
            ItemTable.UncommonPlortCache          => UncommonPlorts,
            ItemTable.RarePlortCache              => RarePlorts,
            ItemTable.RainbowFieldsCraftCache     => RainbowFieldsCrafts,
            ItemTable.EmberValleyCraftCache       => EmberValleyCrafts,
            ItemTable.StarlightStrandCraftCache   => StarlightStrandCrafts,
            ItemTable.PowderfallBluffsCraftCache  => PowderfallBluffsCrafts,
            ItemTable.GreyLabyrinthCraftCache     => GreyLabyrinthCrafts,
            ItemTable.RareCraftCache              => RareCrafts,
            _                                     => (string[]?)null
        };
        if (pool != null)
            return ApplyRefineryCache(pool, item.Name, count: 10, apItem, itemIndex);

        Logger.Warning($"[AP] Unhandled filler item ID: {item.Id}");
        return true; // unknown filler — don't retry
    }

    /// <summary>
    /// Ranch plot items have no world effect: RanchPlotHandler derives gate state from the
    /// server snapshot, so this only invalidates its counts (or bumps the debug counter).
    /// </summary>
    private static bool ApplyRanchPlot(Data.ItemInfo item, ApItemInfo? apItem, int itemIndex)
    {
        if (apItem == null)
            RanchPlotHandler.AddDebugCount(item.Id); // debug grant — no server snapshot entry
        else
            RanchPlotHandler.MarkDirty();

        Logger.Info($"[AP] Ranch item received: {item.Name} (id={item.Id}, idx={itemIndex})");
        UI.StatusHUD.Instance?.ShowNotification($"Received: {item.Name}");
        return true;
    }

    // Drone Station modules refused by a full store; requeued one at a time every 3 s.
    private static readonly List<(ApItemInfo apItem, int itemIndex)> _heldDroneModules = new();
    private static float _nextHeldDroneRetry;

    public static void TickHeldDroneModules()
    {
        if (_heldDroneModules.Count == 0) return;
        if (UnityEngine.Time.time < _nextHeldDroneRetry) return;
        _nextHeldDroneRetry = UnityEngine.Time.time + 3f;

        var (apItem, itemIndex) = _heldDroneModules[0];
        _heldDroneModules.RemoveAt(0);
        Plugin.Instance.ApClient.RequeueItem(apItem, itemIndex);
    }

    /// <summary>On disconnect — reconnect requeues them from the deferred set, so keeping them would double-grant.</summary>
    public static void ClearHeldDroneModules() => _heldDroneModules.Clear();

    private static bool ApplyUseful(Data.ItemInfo item, ApItemInfo? apItem, int itemIndex)
    {
        Logger.Info($"[AP] Applying useful: {item.Name} (id={item.Id}, idx={itemIndex})");

        // Quantum Drone Station — every copy ensures the blueprint, then adds one station unit.
        if (item.Id == ItemTable.QuantumDroneStation)
        {
            var director = SceneContext.Instance?.GadgetDirector;
            if (director == null)
            {
                if (apItem != null) Plugin.Instance.ApClient.RequeueItem(apItem, itemIndex);
                return false;
            }
            if (!EnsureGadgetBlueprint(DroneStationGadgetName))
            {
                // Assets still loading — retry the whole grant.
                if (apItem != null) Plugin.Instance.ApClient.RequeueItem(apItem, itemIndex);
                return false;
            }
            // Candidate types for a placeable station. 'ComponentAcqDrone' is the ghostly-drone
            // world collectible with an ownable limit of 0, so it is never stored.
            string[] candidates = { DroneStationGadgetName, "ComponentAcqDrone" };
            bool storableButFull = false;

            foreach (var typeName in candidates)
            {
                var identType = Resources.FindObjectsOfTypeAll<IdentifiableType>()
                                         .FirstOrDefault(t => t.name == typeName);
                if (identType == null) continue;

                int limit = -1, space = -1;
                try { limit = director.GetOwnableCountLimit(identType); }      catch { }
                try { space = director.GetRefinerySpaceAvailable(identType); } catch { }

                int added = director.AddItem(identType, 1);
                // Only worth an overflow retry when the type is storable at all.
                if (added <= 0 && limit > 0) added = director.AddItem(identType, 1, true);

                if (added > 0)
                {
                    Logger.Info(
                        $"[AP] Quantum Drone Station: granted '{typeName}' x{added} " +
                        $"(ownable limit={limit}, space available={space})");
                    Notify("Received: Quantum Drone Station");
                    return true;
                }

                Logger.Warning(
                    $"[AP] Quantum Drone Station: '{typeName}' refused " +
                    $"(ownable limit={limit}, space available={space})");

                // Storable but full — worth retrying once the player frees space.
                if (limit > 0) storableButFull = true;
            }

            if (storableButFull && apItem != null)
            {
                _heldDroneModules.Add((apItem, itemIndex));
                Plugin.Instance.SaveManager.AddDeferredItem(itemIndex);
                Logger.Info($"[AP] Quantum Drone Station held for retry (queue: {_heldDroneModules.Count})");
                return false;
            }

            // Limit 0 is permanent — don't hold. The blueprint is the meaningful part anyway.
            Logger.Warning(
                "[AP] Quantum Drone Station: no storable station type — blueprint granted only. " +
                "If stations are still capped in-game, the count is governed by something " +
                "other than an inventory item.");
            Notify("Received: Quantum Drone Station (blueprint)");
            return true;
        }

        if (item.Id == ItemTable.WeatherChange)
            return TrapHandler.Schedule(item.Id, apItem, itemIndex);

        // Market Recovery — recomputes saturation from the total received (snapshot includes this one).
        if (item.Id is ItemTable.MarketRecovery20 or ItemTable.MarketRecovery10)
        {
            if (!Patches.EconomyPatches.PlortMarketModePatch.ApplyRecovery())
            {
                if (apItem != null) Plugin.Instance.ApClient.RequeueItem(apItem, itemIndex);
                return false;
            }
            Notify($"Received: {item.Name}");
            return true;
        }

        Logger.Warning($"[AP] Unhandled useful item ID: {item.Id}");
        return true;
    }

    // ItemTable ID → UpgradeComponent asset name (confirmed via upgrade_components.txt dump).
    private static readonly Dictionary<long, string> _upgradeComponentNames = new()
    {
        { ItemTable.ArchiveKeyComponent,  "ArchiveKeyComponent"      },  // ArchiveKey[lvl0]
        { ItemTable.SureshotModule,       "SureShotComponent"        },  // GoldenSureshot[lvl0–2]
        { ItemTable.TankLiner,            "TankGuardComponent"       },  // TankGuard[lvl0–2]
        { ItemTable.HeartCell,            "HeartModuleComponent"     },  // HealthCapacity[lvl1–3]
        { ItemTable.PowerChip,            "PowerCoreComponent"       },  // EnergyCapacity[lvl1–4]
        { ItemTable.DashBootModule,       "DashBootComponent"        },  // RunEfficiency[lvl1]
        { ItemTable.JetpackDrive,         "JetpackComponent"         },  // Jetpack[lvl1]
        { ItemTable.StorageCell,          "TankBoosterComponent"     },  // AmmoCapacity[lvl1–7]
        { ItemTable.ShadowSureshotModule, "ShadowSureShotComponent"  },  // ShadowSureshot[lvl0]
        { ItemTable.InjectorModule,       "PowerInjectorComponent"   },  // EnergyDelay[lvl0–1]
        { ItemTable.RegenModule,          "RegenComponent"           },  // EnergyRegen[lvl0–1]
        { ItemTable.VacTank,              "ExtraTankComponent"       },  // AmmoSlots[lvl1] — in-game "Vac Tank"
    };

    private static bool ApplyUpgradeComponent(Data.ItemInfo item, ApItemInfo? apItem, int itemIndex)
    {
        if (!_upgradeComponentNames.TryGetValue(item.Id, out var compName))
            return true; // unknown component ID — don't retry

        if (!SceneContextUtility.TryGetUpgradeComponentsModel(out var model) || model == null)
        {
            if (apItem != null) Plugin.Instance.ApClient.RequeueItem(apItem, itemIndex);
            return false;
        }

        var comp = Resources.FindObjectsOfTypeAll<UpgradeComponent>()
                            .FirstOrDefault(c => c.name == compName);
        if (comp == null)
        {
            Logger.Warning($"[AP] UpgradeComponent asset '{compName}' not found — grant skipped");
            return true; // asset issue — retrying won't help
        }

        Logger.Info($"[AP] Applying component: {item.Name} (id={item.Id}, idx={itemIndex})");
        model.GainComponent(comp);
        Notify($"Received: {item.Name}");
        Logger.Info($"[AP] Granted upgrade component: {compName}");
        return true;
    }

    private static bool ApplyNewbucks(Data.ItemInfo item, ApItemInfo? apItem, int itemIndex)
    {
        var playerState = SceneContext.Instance?.PlayerState;
        if (playerState == null)
        {
            if (apItem != null) Plugin.Instance.ApClient.RequeueItem(apItem, itemIndex);
            return false;
        }

        var amount = item.Id switch
        {
            ItemTable.Newbucks250  => 250,
            ItemTable.Newbucks500  => 500,
            ItemTable.Newbucks1000 => 1000,
            _                      => 0
        };
        if (amount <= 0) return true; // unknown denomination — don't retry

        var currency = Resources.FindObjectsOfTypeAll<CurrencyDefinition>()
                                .FirstOrDefault(c => c.name.Contains("Newbucks", StringComparison.OrdinalIgnoreCase));
        if (currency == null)
        {
            Logger.Warning("[AP] Could not find Newbucks CurrencyDefinition — grant skipped");
            return true; // asset issue — retrying won't help
        }

        IsGrantingCurrency = true;
        try
        {
            playerState.AddCurrency(currency.Cast<ICurrency>(), amount);
        }
        finally
        {
            IsGrantingCurrency = false;
        }
        Notify($"Received: {amount} Newbucks");
        return true;
    }

    private static bool ApplyRefineryCache(string[] pool, string cacheName, int count,
                                             ApItemInfo? apItem, int itemIndex)
    {
        var director = SceneContext.Instance?.GadgetDirector;
        if (director == null)
        {
            if (apItem != null) Plugin.Instance.ApClient.RequeueItem(apItem, itemIndex);
            return false;
        }

        var allIdents = Resources.FindObjectsOfTypeAll<IdentifiableType>();

        int totalAdded = 0;
        for (int i = 0; i < count; i++)
        {
            var typeName = pool[_rng.Next(pool.Length)];
            var identType = allIdents.FirstOrDefault(t => t.name == typeName);
            if (identType == null)
            {
                Logger.Warning($"[AP] Could not find IdentifiableType '{typeName}' — skipping this pick");
                continue;
            }
            director.AddItem(identType, 1);
            totalAdded++;
        }

        Logger.Info($"[AP] Deposited {totalAdded} items from '{cacheName}' into refinery");
        Notify($"Received: {cacheName} ({totalAdded} items) → Refinery");
        return true;
    }

    private static bool ApplyTrap(Data.ItemInfo item, ApItemInfo? apItem, int itemIndex)
    {
        return TrapHandler.Schedule(item.Id, apItem, itemIndex);
    }

    private static void Notify(string message) =>
        SlimeRancher2AP.UI.StatusHUD.Instance?.ShowNotification(message);

#if DEBUG
    // -------------------------------------------------------------------------
    // Debug-only helpers — not part of the normal AP item flow
    // -------------------------------------------------------------------------

    private const string RadiantProjectorGadgetName = "EnergyBeamNode"; // confirmed via DumpGadgets

    /// <summary>Unlocks the Radiant Projector blueprint.</summary>
    public static void DebugGrantRadiantProjector()
    {
        var director = SceneContext.Instance?.GadgetDirector;
        if (director == null)
        {
            Logger.Warning("[AP-Debug] GadgetDirector not available — load a save first");
            return;
        }

        var def = Resources.FindObjectsOfTypeAll<GadgetDefinition>()
                           .FirstOrDefault(d => d.name == RadiantProjectorGadgetName);
        if (def == null)
        {
            Logger.Warning(
                $"[AP-Debug] GadgetDefinition '{RadiantProjectorGadgetName}' not found");
            return;
        }

        // Blueprint only, as Viktor's call does — AddItem'd units don't persist for this gadget.
        director.AddBlueprint(def, false);
        Logger.Info($"[AP-Debug] Unlocked blueprint: '{def.name}' — craft units in the Fabricator");
    }

    /// <summary>Blueprint plus one unit, to test beam puzzles without crafting.</summary>
    public static void DebugGrantRadiantProjectorUnit()
    {
        var director = SceneContext.Instance?.GadgetDirector;
        if (director == null)
        {
            Logger.Warning("[AP-Debug] GadgetDirector not available — load a save first");
            return;
        }

        var def = Resources.FindObjectsOfTypeAll<GadgetDefinition>()
                           .FirstOrDefault(d => d.name == RadiantProjectorGadgetName);
        if (def == null)
        {
            Logger.Warning(
                $"[AP-Debug] GadgetDefinition '{RadiantProjectorGadgetName}' not found");
            return;
        }

        director.AddBlueprint(def, false);
        director.AddItem(def, 1); // GadgetDefinition : IdentifiableType — adds one placeable unit
        Logger.Info($"[AP-Debug] Granted blueprint + 1 unit: '{def.name}'");
    }

    /// <summary>
    /// Puts <paramref name="count"/> of an IdentifiableType into vacpack slots that are empty or
    /// already hold it. Find names with Dump IdentifiableTypes (Misc page).
    /// </summary>
    public static void DebugGrantFoodToVacpack(string typeName, int count = 20)
    {
        var playerState = SceneContext.Instance?.PlayerState;
        if (playerState == null)
        {
            Logger.Warning("[AP-Debug] PlayerState not available — load a save first");
            return;
        }

        var identType = Resources.FindObjectsOfTypeAll<IdentifiableType>()
                                 .FirstOrDefault(t => string.Equals(
                                     t.name, typeName, System.StringComparison.OrdinalIgnoreCase));
        if (identType == null)
        {
            Logger.Warning(
                $"[AP-Debug] IdentifiableType '{typeName}' not found — use Dump IdentifiableTypes (Misc page) to find the correct name");
            return;
        }

        var ammo = playerState.Ammo;
        if (ammo == null)
        {
            Logger.Warning("[AP-Debug] PlayerState.Ammo not available");
            return;
        }

        var slots = ammo.Slots; // Il2CppReferenceArray<AmmoSlot>
        int granted = 0;

        for (int i = 0; i < slots.Length && granted < count; i++)
        {
            var slot = slots[i];
            if (slot == null || !slot.IsUnlocked) continue;

            if (slot.Id != null && slot.Count > 0 && slot.Id.name != typeName) continue;

            if (slot.Id == null || slot.Count == 0)
                slot.Id = identType;

            int space = slot.MaxCount - slot.Count;
            if (space <= 0) continue;

            int toAdd = System.Math.Min(count - granted, space);
            slot.Count += toAdd;
            granted += toAdd;
        }

        if (granted > 0)
        {
            Logger.Info($"[AP-Debug] Placed {granted}x '{typeName}' into vacpack");
            SlimeRancher2AP.UI.StatusHUD.Instance?.ShowNotification($"Debug: {granted}x {typeName} → vacpack");
        }
        else
        {
            Logger.Warning(
                "[AP-Debug] No free vacpack slots for this food type — move or discard current contents first");
        }
    }
#endif
}

/// <summary>Schedules and ticks trap effects. <c>Tick()</c> runs every frame.</summary>
public static class TrapHandler
{
    private static readonly System.Random _rng = new();

    // -------------------------------------------------------------------------
    // Runtime region-gate tracking (for auto mode / vanilla gate opens)
    // -------------------------------------------------------------------------

    // Region items whose gate was seen opening this session (auto mode writes no save data).
    private static readonly HashSet<string> _runtimeOpenRegions = new();

    // Zones visited this session — reachable by definition. Fallback when no gate event fired.
    private static readonly HashSet<string> _visitedSceneGroupRefs = new();
    private static string? _lastSeenGroupRef = null;

    /// <summary>
    /// The player's current <c>SceneGroup.ReferenceId</c>, updated each frame. Use this rather
    /// than your own <c>GetComponent&lt;TeleportablePlayer&gt;()</c> per frame.
    /// </summary>
    public static string? CurrentZoneRef => _lastSeenGroupRef;

    /// <summary>Called by <c>RegionGatePatch</c> when a region gate opens.</summary>
    public static void MarkRegionOpen(string regionItemName)
        => _runtimeOpenRegions.Add(regionItemName);

    /// <summary>
    /// True if the gate was seen opening this session. False means "not seen", not "closed" —
    /// fall back to the switch state.
    /// </summary>
    public static bool IsRegionOpenThisSession(string regionItemName)
        => _runtimeOpenRegions.Contains(regionItemName);

    /// <summary>Clears deferred traps on disconnect so they don't fire in the next session.</summary>
    public static void ClearDeferred()
    {
        int total = 0;
        foreach (var q in _deferredByGroup) { total += q.Count; q.Clear(); }
        if (total > 0)
            Logger.Info($"[AP] TrapHandler: cleared {total} deferred trap(s) on disconnect");
    }

    /// <summary>Per-frame; records zone visits (the teleport trap's destination pool).</summary>
    public static void TrackCurrentZone(string? sceneGroupRef)
    {
        if (sceneGroupRef == null || sceneGroupRef == _lastSeenGroupRef) return;
        string? previousZone = _lastSeenGroupRef;
        _lastSeenGroupRef = sceneGroupRef;
        // An unauthorized entry is not a visit.
        bool enforced = GateReturnEnforcer.OnZoneChanged(sceneGroupRef, previousZone);
        if (enforced) return;
        _visitedSceneGroupRefs.Add(sceneGroupRef);
        Plugin.Instance.SaveManager.MarkZoneVisited(sceneGroupRef);
#if DEBUG
        Logger.Info($"[AP] TrapHandler: zone visit recorded — '{sceneGroupRef}'");
#endif
    }

    // -------------------------------------------------------------------------
    // Trap rate-limiting — per-group independent queues
    // -------------------------------------------------------------------------

    /// <summary>
    /// Independent rate-limit groups: each has its own cooldown (<see cref="GroupMinInterval"/>)
    /// and deferred queue.
    /// </summary>
    private enum TrapGroup
    {
        Spawn    = 0,  // Tarr Spawn, Slime Ring — lightweight spawns; rapid fire OK
        Weather  = 1,  // Weather Change (useful, not a trap), Tarr Rain — prolonged effects
        Teleport = 2,  // Teleport — disorienting but recoverable; moderate gap
    }

    // Set when Player first becomes non-null after a load; starts the grace period.
    private static bool  _sceneWasReady   = false;
    private static float _sceneReadySince = -1f;

    /// <summary>Seconds after scene load before ANY trap may fire (all groups).</summary>
    private const float SceneGracePeriod = 10f;

    /// <summary>
    /// Hold traps this long after a check, so a trap rewarded by it cannot interrupt the
    /// interaction that sent it.
    /// </summary>
    private const int CheckInteractionGraceMs = 2000;

    /// <summary>Per-group minimum seconds between fires, indexed by <see cref="TrapGroup"/>.</summary>
    private static readonly float[] GroupMinInterval =
    {
        1f,   // Spawn   — Tarr Spawn, Slime Ring
        30f,  // Weather — Weather Change, Tarr Rain
        5f,   // Teleport
    };

    /// <summary>Per-group last fire (Time.time); -1 = never.</summary>
    private static readonly float[] _lastGroupFiredAt = { -1f, -1f, -1f };

    /// <summary>
    /// Per-group parked traps; <see cref="Tick"/> promotes one per group once allowed.
    /// Promotion picks at random, not FIFO: a release delivers items grouped by type, and FIFO
    /// played them in long uniform runs. Order doesn't matter to the watermark — traps are
    /// tracked by index.
    /// </summary>
    private static readonly List<(ApItemInfo apItem, int itemIndex)>[] _deferredByGroup =
    {
        new(), // Spawn
        new(), // Weather
        new(), // Teleport
    };

    private static readonly System.Random _deferredRng = new();

    /// <summary>True when the scene is ready and no grace period or group cooldown applies.</summary>
    private static bool IsTrapAllowed(TrapGroup group, string trapName)
    {
        if (_sceneReadySince < 0f) return false; // scene not ready yet

        float now = UnityEngine.Time.time;

        float graceRemaining = (_sceneReadySince + SceneGracePeriod) - now;
        if (graceRemaining > 0f)
        {
#if DEBUG
            Logger.Info(
                $"[AP] Trap '{trapName}' deferred — {graceRemaining:F0}s of load grace period remaining");
#endif
            return false;
        }

        if (InCheckInteractionGrace())
        {
#if DEBUG
            Logger.Info(
                $"[AP] Trap '{trapName}' deferred — within the {CheckInteractionGraceMs} ms " +
                "interaction grace after a location check");
#endif
            return false;
        }

        float lastFired = _lastGroupFiredAt[(int)group];
        if (lastFired >= 0f)
        {
            float cooldownRemaining = (lastFired + GroupMinInterval[(int)group]) - now;
            if (cooldownRemaining > 0f)
            {
#if DEBUG
                Logger.Info(
                    $"[AP] Trap '{trapName}' deferred — {cooldownRemaining:F0}s of {group} group cooldown remaining");
#endif
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// True within <see cref="CheckInteractionGraceMs"/> of a check. A trap from the player's own
    /// check arrives ~100-300 ms later and could interrupt it (a teleport once left a map node
    /// unactivated). Also gates promotion in <see cref="Tick"/>, which would otherwise bounce.
    /// </summary>
    private static bool InCheckInteractionGrace()
    {
        int msSinceCheck = System.Environment.TickCount - ArchipelagoClient.LastCheckSentTickMs;
        return msSinceCheck >= 0 && msSinceCheck < CheckInteractionGraceMs;
    }

    // -------------------------------------------------------------------------
    // Weather trap state
    // -------------------------------------------------------------------------

    // Unity Time.time at which the forced weather state should be stopped; -1 = not active.
    private static float _weatherResetAt = -1f;

    // The WeatherStateDefinition we started — kept so we can call StopState on it.
    private static WeatherStateDefinition? _activeWeatherState = null;

    // The WeatherDirectors we applied the state to — cached on activation.
    private static WeatherDirector[]? _cachedDirectors = null;

    // Per-director pattern for StopPatternState on expiry. Null for Tarr Rain (uses RunState).
    private static IWeatherPattern[]? _cachedPatterns = null;

    // SpawnActorActivity.ActorType values overridden by the Slime Rain trap; restored on weather reset.
    private static System.Collections.Generic.Dictionary<SpawnActorActivity, IdentifiableType>?
        _savedSlimeRainActorTypes = null;

    // -------------------------------------------------------------------------
    // Public API
    // -------------------------------------------------------------------------

    /// <summary>Per-frame: scene readiness, weather expiry, deferred promotion.</summary>
    public static void Tick()
    {
        bool sceneReady = SceneContext.Instance?.Player != null;
        if (!sceneReady)
        {
            _sceneWasReady   = false;
            _sceneReadySince = -1f;
        }
        else if (!_sceneWasReady)
        {
            _sceneWasReady   = true;
            _sceneReadySince = UnityEngine.Time.time;
            Logger.Info(
                $"[AP] TrapHandler: scene ready — trap grace period begins ({SceneGracePeriod}s)");

            // Carry an active weather trap into the newly loaded zone.
            if (_activeWeatherState != null && _weatherResetAt >= 0f && UnityEngine.Time.time < _weatherResetAt)
                ReapplyWeatherToNewZone();
        }

        if (_weatherResetAt >= 0f && UnityEngine.Time.time >= _weatherResetAt)
        {
            ResetWeather();
            _weatherResetAt = -1f;
        }

        // Promote at most one trap per ready group per frame, keeping each group's spacing.
        if (_sceneReadySince >= 0f)
        {
            float now     = UnityEngine.Time.time;
            bool  graceOk = now >= _sceneReadySince + SceneGracePeriod
                         && !InCheckInteractionGrace();
            if (graceOk)
            {
                for (int g = 0; g < _deferredByGroup.Length; g++)
                {
                    var q = _deferredByGroup[g];
                    if (q.Count == 0) continue;
                    bool cooldownOk = _lastGroupFiredAt[g] < 0f
                                   || now >= _lastGroupFiredAt[g] + GroupMinInterval[g];
                    if (!cooldownOk) continue;
                    int pick = _deferredRng.Next(q.Count);
                    var (trapApItem, trapIndex) = q[pick];
                    q.RemoveAt(pick);
                    Plugin.Instance.ApClient.RequeueItem(trapApItem, trapIndex);
                    Logger.Info(
                        $"[AP] TrapHandler: promoted deferred {(TrapGroup)g} trap to item queue " +
                        $"(remaining in group: {q.Count})");
                }
            }
        }
    }

    /// <summary>Applies a trap. Returns false when requeued (watermark must not advance).</summary>
    public static bool Schedule(long trapItemId, ApItemInfo? apItem, int itemIndex)
    {
        // Suppress disruptive traps once the goal is complete.
        if (GoalHandler.IsGoalComplete)
        {
            switch (trapItemId)
            {
                case ItemTable.TrapTarrSpawn:
                case ItemTable.TrapTeleport:
                case ItemTable.TrapTarrRain:
                case ItemTable.WeatherChange:
                    Logger.Info($"[AP] Trap id={trapItemId} suppressed — goal already complete");
                    return true;
            }
        }

        switch (trapItemId)
        {
            case ItemTable.TrapTarrSpawn:     return ApplyTarrSpawnTrap(apItem, itemIndex);
            case ItemTable.TrapTeleport:      return ApplyTeleportTrap(apItem, itemIndex);
            case ItemTable.WeatherChange:     return ApplyWeatherChange(apItem, itemIndex);
            case ItemTable.TrapTarrRain:      return ApplySlimeRainTrap(apItem, itemIndex);
            case ItemTable.TrapVacExpel:      return ApplyVacExpelTrap(apItem, itemIndex);
            case ItemTable.TrapVacFill:   return ApplyVacpackFillTrap(apItem, itemIndex);
            default:
                Logger.Info($"[AP] Trap received: id={trapItemId} (not yet implemented)");
                return true; // unimplemented traps do not retry
        }
    }

    // Parks the item and returns false. Rate-limited traps pass their group and wait in its
    // deferred queue (requeuing them spun every frame for the whole cooldown); other failures
    // (scene not ready, etc.) pass null and requeue for the next frame.
    private static bool Requeue(ApItemInfo? apItem, int itemIndex, string reason,
                                TrapGroup? deferGroup = null)
    {
        if (deferGroup.HasValue)
        {
            if (apItem != null)
            {
                var q = _deferredByGroup[(int)deferGroup.Value];
                q.Add((apItem, itemIndex));
                // Persisted: the watermark can pass it before it fires.
                Plugin.Instance.SaveManager.AddDeferredItem(itemIndex);
                Logger.Info(
                    $"[AP] Trap deferred ({deferGroup.Value} group, queue depth: {q.Count})");
            }
        }
        else
        {
#if DEBUG
            Logger.Info($"[AP] Trap requeued — {reason} (will retry)");
#else
            Logger.Warning($"[AP] Trap requeued — {reason} (will retry)");
#endif
            if (apItem != null)
                Plugin.Instance.ApClient.RequeueItem(apItem, itemIndex);
        }
        return false;
    }

    // -------------------------------------------------------------------------
    // Slime Ring trap — spawns a burst of common slimes around the player
    // -------------------------------------------------------------------------

    // Slime pools by region (matching the apworld's locations.py); only accessible ones spawn.

    // Rainbow Fields — always available
    private static readonly string[] _slimesFields =
    {
        "Pink", "Tabby", "Cotton", "Phosphor", "Tangle", "Dervish", "Yolky", "Gold", "Lucky",
    };

    // Ember Valley (requires "Ember Valley Access")
    private static readonly string[] _slimesEmberValley =
    {
        "Batty", "Crystal", "Angler", "Ringtail", "Puddle", "Rock", "Boom", "Fire",
    };

    // Starlight Strand (requires "Starlight Strand Access")
    private static readonly string[] _slimesStarlightStrand =
    {
        "Honey", "Puddle", "Flutter", "Ringtail", "Hunter", "Rock",
    };

    // Powderfall Bluffs (requires "Powderfall Bluffs Access")
    private static readonly string[] _slimesPowderfallBluffs =
    {
        "Saber",
    };

    // Grey Labyrinth (reachable via either Ember Valley or Starlight Strand entrance)
    private static readonly string[] _slimesGreyLabyrinth =
    {
        "Shadow", "Sloomber", "Twin", "Hyper",
    };

    internal static bool ApplySlimeRingTrap(ApItemInfo? apItem, int itemIndex)
    {
        if (!IsTrapAllowed(TrapGroup.Spawn, "SlimeRing"))
            return Requeue(apItem, itemIndex, "trap rate-limited", TrapGroup.Spawn);

        var playerGo = SceneContext.Instance?.Player;
        if (playerGo == null)
            return Requeue(apItem, itemIndex, "no Player in scene");

        bool hasEV = IsRegionGateOpen("Ember Valley Access");
        bool hasSS = IsRegionGateOpen("Starlight Strand Access");

        var candidateNames = new System.Collections.Generic.List<string>(_slimesFields);
        if (hasEV)                        candidateNames.AddRange(_slimesEmberValley);
        if (hasSS)                        candidateNames.AddRange(_slimesStarlightStrand);
        if (IsRegionGateOpen("Powderfall Bluffs Access")) candidateNames.AddRange(_slimesPowderfallBluffs);
        // Labyrinth only once visited — EV/SS access alone doesn't mean the beam puzzles are solved.
        if (Plugin.Instance.SaveManager.HasVisitedZone("SceneGroup.Labyrinth")
            || _visitedSceneGroupRefs.Contains("SceneGroup.Labyrinth"))
            candidateNames.AddRange(_slimesGreyLabyrinth);

        var allTypes = Resources.FindObjectsOfTypeAll<IdentifiableType>();
        var slimeTypes = new System.Collections.Generic.List<IdentifiableType>();
        foreach (var name in candidateNames)
        {
            for (int i = 0; i < allTypes.Count; i++)
            {
                if (allTypes[i].name == name && allTypes[i].prefab != null)
                {
                    slimeTypes.Add(allTypes[i]);
                    break;
                }
            }
        }

        if (slimeTypes.Count == 0)
        {
            Logger.Warning("[AP] SlimeRing: no valid slime IdentifiableTypes found — skipped");
            return true; // asset issue; don't retry
        }

#if DEBUG
        Logger.Info(
            $"[AP] SlimeRing: pool={string.Join(", ", candidateNames)} ({slimeTypes.Count} resolved)");
#endif

#if DEBUG
        const int count = 4;
#else
        const int count = 8;
#endif
        var origin = playerGo.transform.position;
        int spawned = 0;

        // SpawnActorActivity.Spawn (Slime Rain's path), not Object.Instantiate — that skips the
        // actor registry and AI init. CreateInstance because it is a ScriptableObject.
        var activity = ScriptableObject.CreateInstance<SpawnActorActivity>();

        for (int i = 0; i < count; i++)
        {
            var slimeType = slimeTypes[_rng.Next(slimeTypes.Count)];
            activity.ActorType = slimeType;

            float angle  = (float)(_rng.NextDouble() * System.Math.PI * 2.0);
            float radius = 2f + (float)(_rng.NextDouble() * 2f);    // 2–4 m radius
            float height = 3f + (float)(_rng.NextDouble() * 2f);    // 3–5 m above
            var   spawnPos = origin + new Vector3(
                UnityEngine.Mathf.Cos(angle) * radius,
                height,
                UnityEngine.Mathf.Sin(angle) * radius);

            var go = activity.Spawn(spawnPos, UnityEngine.Quaternion.identity);
            if (go != null) spawned++;
        }

        _lastGroupFiredAt[(int)TrapGroup.Spawn] = UnityEngine.Time.time;
        Logger.Info($"[AP] SlimeRing: spawned {spawned} slimes around player");
        SlimeRancher2AP.UI.StatusHUD.Instance?.ShowNotification("Slime Ring!");
        return true;
    }

    // -------------------------------------------------------------------------
    // Tarr Spawn trap — spawns Tarr near the player
    // -------------------------------------------------------------------------

    private static bool ApplyTarrSpawnTrap(ApItemInfo? apItem, int itemIndex)
    {
        if (!IsTrapAllowed(TrapGroup.Spawn, "TarrSpawn"))
            return Requeue(apItem, itemIndex, "trap rate-limited", TrapGroup.Spawn);

        var playerGo = SceneContext.Instance?.Player;
        if (playerGo == null)
            return Requeue(apItem, itemIndex, "no Player in scene");

        var allTypes = Resources.FindObjectsOfTypeAll<IdentifiableType>();
        IdentifiableType? tarrType = null;
        for (int i = 0; i < allTypes.Count; i++)
        {
            if (allTypes[i].name == "Tarr" && allTypes[i].prefab != null)
            {
                tarrType = allTypes[i];
                break;
            }
        }

        if (tarrType == null)
        {
            Logger.Warning("[AP] TarrSpawnTrap: 'Tarr' IdentifiableType not found or has no prefab — skipped");
            return true; // asset issue; don't retry
        }

#if DEBUG
        const int count = 1;
#else
        const int count = 2;
#endif
        var origin = playerGo.transform.position;
        int spawned = 0;

        // See ApplySlimeRingTrap — Instantiate'd Tarr never aggro.
        var activity = ScriptableObject.CreateInstance<SpawnActorActivity>();
        activity.ActorType = tarrType;

        for (int i = 0; i < count; i++)
        {
            // 2–4 m away at head height, so they aggro immediately.
            float angle    = (float)(_rng.NextDouble() * System.Math.PI * 2.0);
            float radius   = 2f + (float)(_rng.NextDouble() * 2f);
            var   spawnPos = origin + new Vector3(
                UnityEngine.Mathf.Cos(angle) * radius,
                1.5f,
                UnityEngine.Mathf.Sin(angle) * radius);

            var go = activity.Spawn(spawnPos, UnityEngine.Quaternion.identity);
            if (go != null) spawned++;
        }

        _lastGroupFiredAt[(int)TrapGroup.Spawn] = UnityEngine.Time.time;
        Logger.Info($"[AP] TarrSpawnTrap: spawned {spawned} Tarr near player");
        SlimeRancher2AP.UI.StatusHUD.Instance?.ShowNotification("Trap: Tarr Incoming!");
        return true;
    }

    // -------------------------------------------------------------------------
    // Vacpack Spew trap
    // -------------------------------------------------------------------------

    /// <summary>Expels the whole vacpack into the world, then clears every slot.</summary>
    private static bool ApplyVacExpelTrap(ApItemInfo? apItem, int itemIndex)
    {
        if (!IsTrapAllowed(TrapGroup.Spawn, "VacExpel"))
            return Requeue(apItem, itemIndex, "trap rate-limited", TrapGroup.Spawn);

        var playerState = SceneContext.Instance?.PlayerState;
        var vac         = playerState?.VacuumItem;
        var ammo        = playerState?.Ammo;

        if (vac == null || ammo == null)
            return Requeue(apItem, itemIndex, "scene not ready");

        var slots   = ammo.Slots;
        int expelled = 0;

        for (int i = 0; i < slots.Count; i++)
        {
            var slot = slots[i];
            if (slot == null || slot.Id == null || slot.Count <= 0) continue;

            int  count = slot.Count;
            var  meta  = slot.Metadata;

            for (int j = 0; j < count; j++)
                vac.Expel(meta, false, 0f, false);

            ammo.Clear(i);
            expelled += count;
        }

        _lastGroupFiredAt[(int)TrapGroup.Spawn] = UnityEngine.Time.time;
        Logger.Info($"[AP] VacpackSpewTrap: expelled {expelled} item(s) from vacpack");
        SlimeRancher2AP.UI.StatusHUD.Instance?.ShowNotification("Trap: Vacpack Spew!");
        return true;
    }

    // -------------------------------------------------------------------------
    // Vacpack Fill trap — fills all empty vacpack slots with Pink slimes
    // -------------------------------------------------------------------------

    private static bool ApplyVacpackFillTrap(ApItemInfo? apItem, int itemIndex)
    {
        if (!IsTrapAllowed(TrapGroup.Spawn, "VacpackFill"))
            return Requeue(apItem, itemIndex, "trap rate-limited", TrapGroup.Spawn);

        var ammo = SceneContext.Instance?.PlayerState?.Ammo;
        if (ammo == null)
            return Requeue(apItem, itemIndex, "scene not ready");

        var pinkSlime = Resources.FindObjectsOfTypeAll<IdentifiableType>()
                                 .FirstOrDefault(t => t.name == "Pink");
        if (pinkSlime == null)
        {
            Logger.Warning("[AP] VacpackFillTrap: 'Pink' IdentifiableType not found — skipped");
            return true; // asset issue; don't retry
        }

        var slots  = ammo.Slots;
        int filled = 0;

        for (int i = 0; i < slots.Count; i++)
        {
            var slot = slots[i];
            if (slot == null || !slot.IsUnlocked) continue;
            if (slot.Id != null && slot.Count > 0) continue; // leave occupied slots alone

            slot.Id    = pinkSlime;
            slot.Count = slot.MaxCount;
            filled++;
        }

        _lastGroupFiredAt[(int)TrapGroup.Spawn] = UnityEngine.Time.time;
        Logger.Info($"[AP] VacpackFillTrap: filled {filled} empty slot(s) with Pink slimes");
        SlimeRancher2AP.UI.StatusHUD.Instance?.ShowNotification("Trap: Vacpack Fill!");
        return true;
    }

    // -------------------------------------------------------------------------
    // Weather Change — useful filler item, triggers a 3-minute severe weather event
    // -------------------------------------------------------------------------

    private static bool ApplyWeatherChange(ApItemInfo? apItem, int itemIndex)
    {
        if (!IsTrapAllowed(TrapGroup.Weather, "WeatherChange"))
            return Requeue(apItem, itemIndex, "trap rate-limited", TrapGroup.Weather);

        var found = Resources.FindObjectsOfTypeAll<WeatherDirector>();
        if (found == null || found.Count == 0)
            return Requeue(apItem, itemIndex, "no WeatherDirector in scene");

        var allStates = Resources.FindObjectsOfTypeAll<WeatherStateDefinition>();
        if (allStates == null || allStates.Count == 0)
        {
            Logger.Warning("[AP] WeatherChange: no WeatherStateDefinition assets found — trap skipped");
            return true; // asset issue; don't retry
        }

        // Logged to diagnose StateName values.
        var sb = new System.Text.StringBuilder();
        for (int i = 0; i < allStates.Count; i++)
        {
            if (i > 0) sb.Append(", ");
            sb.Append('\'').Append(allStates[i].StateName ?? allStates[i].name).Append('\'');
        }
        Logger.Info($"[AP] WeatherChange: {allStates.Count} state(s) available: {sb}");

        // Heavy tier only (MapTier 3: Thunder/Rain/Pollen/Wind Heavy). Snow is zone-specific, so
        // excluded. Wind Heavy is fine via the registry, but see the RunState fallback below.
        var eligible = new System.Collections.Generic.List<WeatherStateDefinition>();
        for (int i = 0; i < allStates.Count; i++)
        {
            var s = allStates[i];
            if (s == null) continue;
            var sn = (s.StateName ?? s.name) ?? "";
            if (sn.IndexOf("Snow", System.StringComparison.OrdinalIgnoreCase) >= 0) continue;
            if (s.MapTier != 3) continue;
            eligible.Add(s);
        }

        if (eligible.Count == 0)
        {
            Logger.Warning("[AP] WeatherChange: no eligible Heavy weather states — trap skipped");
            return true; // filtering issue; don't retry
        }

        // Dedupe by StateName — per-zone copies would otherwise dominate the pick.
        var seenNames = new System.Collections.Generic.HashSet<string>(System.StringComparer.Ordinal);
        var unique    = new System.Collections.Generic.List<WeatherStateDefinition>(eligible.Count);
        for (int i = 0; i < eligible.Count; i++)
        {
            var key = eligible[i].StateName ?? eligible[i].name ?? "";
            if (seenNames.Add(key)) unique.Add(eligible[i]);
        }

        var chosen      = unique[_rng.Next(unique.Count)];
        var chosenName  = chosen.StateName ?? chosen.name ?? "";
        var chosenState = chosen.Cast<IWeatherState>();

        StopActiveWeatherTrap();
        _activeWeatherState = chosen;

        _cachedDirectors = new WeatherDirector[found.Count];
        _cachedPatterns  = new IWeatherPattern[found.Count];
        for (int i = 0; i < found.Count; i++) _cachedDirectors[i] = found[i];

#if DEBUG
        const float durationSeconds = 120f;
#else
        const float durationSeconds = 180f;
#endif
        _weatherResetAt = UnityEngine.Time.time + durationSeconds;

        // Per director: stop what's running, then start ours through the registry (so the map
        // overlay updates) with the zone's own pattern. RunState is the fallback.
        for (int di = 0; di < _cachedDirectors.Length; di++)
        {
            var director = _cachedDirectors[di];
            if (director == null) continue;

            // Snapshot — StopState mutates the list.
            var running = director._runningStates;
            if (running != null && running.Count > 0)
            {
                var toStop = new IWeatherState[running.Count];
                for (int i = 0; i < running.Count; i++) toStop[i] = running[i];
                foreach (var s in toStop)
                    if (s != null) director.StopState(s, null, immediate: true);
            }

            var registry       = director._registry;
            var zone           = director.Zone;
            var zonePatternDef = (registry != null && zone != null)
                ? FindPatternForZone(registry, zone, chosenName) : null;

            if (registry != null && zone != null && zonePatternDef != null)
            {
                var pattern = zonePatternDef.CreatePattern();
                _cachedPatterns[di] = pattern;
                Logger.Info($"[AP] WeatherChange: director[{di}] zone='{zone.name}' pattern='{zonePatternDef.name}' → registry.RunPatternState");
                registry.RunPatternState(zone, pattern, chosenState, true);
            }
            else
            {
                // Wind Heavy crashes RunState with null ZoneWeatherParameters.
                Logger.Warning($"[AP] WeatherChange: director[{di}] fallback (registry={registry != null}, zone={zone != null}, pattern={zonePatternDef != null})");
                if (chosenName.IndexOf("Wind", System.StringComparison.OrdinalIgnoreCase) < 0)
                    director.RunState(chosenState, null, immediate: false);
                else
                    Logger.Warning($"[AP] WeatherChange: skipping Wind Heavy on director {di} — no zone pattern found, direct RunState would crash");
            }
        }

        _lastGroupFiredAt[(int)TrapGroup.Weather] = UnityEngine.Time.time;
        string label = chosen.StateName ?? chosen.name ?? "Unknown";
        Logger.Info($"[AP] WeatherChange: started '{label}' on {found.Count} director(s) for {durationSeconds}s");
        SlimeRancher2AP.UI.StatusHUD.Instance?.ShowNotification($"Weather Change: {label} for {(int)(durationSeconds / 60)}m!");
        return true;
    }

    // -------------------------------------------------------------------------
    // Teleport trap
    // -------------------------------------------------------------------------

    // Zone SceneGroups: ConservatoryFields = home ranch, LuminousStrand = Starlight Strand,
    // RumblingGorge = Ember Valley. Arrival nodes are "Teleporter{Dest}To{Source}Main" (all
    // confirmed by dump); the home ranch uses Teleport_ResetPlayer instead.

    private readonly struct TrapTeleportDest
    {
        public readonly string NodeId;         // arrival node ID, or "" = use Teleport_ResetPlayer
        public readonly string SceneGroupRef;

        public TrapTeleportDest(string nodeId, string sceneGroupRef)
        { NodeId = nodeId; SceneGroupRef = sceneGroupRef; }
    }

    private static readonly TrapTeleportDest[] _trapDestinations =
    {
        new("",                                 "SceneGroup.ConservatoryFields"),
        new("TeleporterStrandToFieldsMain",     "SceneGroup.LuminousStrand"),
        new("TeleporterLabyrinthToStrandMain",  "SceneGroup.Labyrinth"),         // Strand-side entrance
        new("TeleporterGorgeToFieldsMain",      "SceneGroup.RumblingGorge"),
        new("TeleporterLabyrinthToGorgeMain",   "SceneGroup.Labyrinth"),         // Gorge-side entrance
        new("TeleporterBluffsToGorgeMain",      "SceneGroup.PowderfallBluffs"),  // PB connects to Gorge
    };

    // Region item → zone, for visited-zone inference (vanilla mode never records an unlock).
    private static readonly System.Collections.Generic.Dictionary<string, string> _regionToSceneGroupRef = new()
    {
        ["Ember Valley Access"]      = "SceneGroup.RumblingGorge",
        ["Starlight Strand Access"]  = "SceneGroup.LuminousStrand",
        ["Powderfall Bluffs Access"] = "SceneGroup.PowderfallBluffs",
    };

    /// <summary>Region open per the AP save, a gate event this session, or a past visit.</summary>
    private static bool IsRegionGateOpen(string regionItemName)
        => Plugin.Instance.SaveManager.IsRegionUnlocked(regionItemName)
        || _runtimeOpenRegions.Contains(regionItemName)
        || (_regionToSceneGroupRef.TryGetValue(regionItemName, out var sgRef)
            && (Plugin.Instance.SaveManager.HasVisitedZone(sgRef)
                || _visitedSceneGroupRefs.Contains(sgRef)));

    /// <summary>
    /// Teleports the player to a random visited zone. Visited, not unlocked: a visit is only
    /// recorded for an authorized entry, and vanilla region mode records no unlocks.
    /// </summary>
    private static bool ApplyTeleportTrap(ApItemInfo? apItem, int itemIndex)
    {
        if (!IsTrapAllowed(TrapGroup.Teleport, "Teleport"))
            return Requeue(apItem, itemIndex, "trap rate-limited", TrapGroup.Teleport);

        var playerGo = SceneContext.Instance?.Player;
        if (playerGo == null)
            return Requeue(apItem, itemIndex, "no Player in scene");

        var teleportable = playerGo.GetComponent<TeleportablePlayer>();
        if (teleportable == null)
            return Requeue(apItem, itemIndex, "TeleportablePlayer component not found");

        var network = UnityEngine.Object.FindObjectOfType<TeleportNetwork>();
        if (network == null)
            return Requeue(apItem, itemIndex, "TeleportNetwork not in scene");

        var allGroups    = Resources.FindObjectsOfTypeAll<SceneGroup>();
        var currentGroup = teleportable.SceneGroup;
        var eligible     = new System.Collections.Generic.List<(TrapTeleportDest dest, SceneGroup sg)>();

#if DEBUG
        Logger.Info($"[AP] TeleportTrap: currentGroup='{currentGroup?.ReferenceId ?? "null"}', allGroups={allGroups.Count}");
#endif
        foreach (var dest in _trapDestinations)
        {
            // Never-entered zones could strand the player behind unpressed gate buttons.
            bool visited = Plugin.Instance.SaveManager.HasVisitedZone(dest.SceneGroupRef)
                        || _visitedSceneGroupRefs.Contains(dest.SceneGroupRef);
            if (!visited)
            {
#if DEBUG
                Logger.Info(
                    $"[AP] TeleportTrap: dest='{dest.SceneGroupRef}' skipped — never visited");
#endif
                continue;
            }

            SceneGroup? sg = null;
            for (int i = 0; i < allGroups.Count; i++)
                if (allGroups[i].ReferenceId == dest.SceneGroupRef) { sg = allGroups[i]; break; }

            if (sg == null)
            {
                Logger.Warning($"[AP] TeleportTrap: SceneGroup '{dest.SceneGroupRef}' not found — skipped");
                continue;
            }
#if DEBUG
            Logger.Info($"[AP] TeleportTrap: dest='{dest.SceneGroupRef}' eligible (node='{dest.NodeId}')");
#endif
            eligible.Add((dest, sg));
        }

        if (eligible.Count == 0)
        {
            Logger.Warning("[AP] TeleportTrap: no eligible destinations — skipped");
            return true; // don't retry
        }

        var nonCurrent = eligible.FindAll(e => e.sg != currentGroup);
        var candidates = nonCurrent.Count > 0 ? nonCurrent : eligible;
        var chosen     = candidates[_rng.Next(candidates.Count)];

        if (string.IsNullOrEmpty(chosen.dest.NodeId))
        {
            network.Teleport_ResetPlayer(teleportable);
            Logger.Info("[AP] TeleportTrap: sent to home ranch via Teleport_ResetPlayer");
        }
        else
        {
            network.TeleportToDestinationImpl(teleportable, chosen.dest.NodeId, chosen.sg, forceSceneTransition: true);
            Logger.Info(
                $"[AP] TeleportTrap: sent to '{chosen.sg.ReferenceId}' via node '{chosen.dest.NodeId}'");
        }

        _lastGroupFiredAt[(int)TrapGroup.Teleport] = UnityEngine.Time.time;
        SlimeRancher2AP.UI.StatusHUD.Instance?.ShowNotification("Trap: Teleported!");
        return true;
    }

    // -------------------------------------------------------------------------
    // Slime Rain trap — triggers the Slime Rain weather but overrides spawned slimes to Tarr
    // -------------------------------------------------------------------------

    private static bool ApplySlimeRainTrap(ApItemInfo? apItem, int itemIndex)
    {
        if (!IsTrapAllowed(TrapGroup.Weather, "TarrRain"))
            return Requeue(apItem, itemIndex, "trap rate-limited", TrapGroup.Weather);

        var allStates = Resources.FindObjectsOfTypeAll<WeatherStateDefinition>();
        WeatherStateDefinition? slimeRainDef = null;
        for (int i = 0; i < allStates.Count; i++)
        {
            var sn = (allStates[i].StateName ?? allStates[i].name) ?? "";
            if (sn.IndexOf("Slime Rain", System.StringComparison.OrdinalIgnoreCase) >= 0)
            {
                slimeRainDef = allStates[i];
                break;
            }
        }

        if (slimeRainDef == null)
        {
            Logger.Warning("[AP] SlimeRainTrap: no 'Slime Rain' WeatherStateDefinition found — falling back to SlimeRing");
            return ApplySlimeRingTrap(apItem, itemIndex);
        }

        // Before touching anything, so a requeue leaves no state to undo.
        var found = Resources.FindObjectsOfTypeAll<WeatherDirector>();
        if (found == null || found.Count == 0)
            return Requeue(apItem, itemIndex, "no WeatherDirector in scene");

        // Before the override: a live Tarr Rain's Tarr would be saved as the "original".
        StopActiveWeatherTrap();

        var allIdents = Resources.FindObjectsOfTypeAll<IdentifiableType>();
        IdentifiableType? tarrType = null;
        for (int i = 0; i < allIdents.Count; i++)
        {
            if (allIdents[i].name == "Tarr" && allIdents[i].prefab != null)
            {
                tarrType = allIdents[i];
                break;
            }
        }

        if (tarrType == null)
        {
            Logger.Warning("[AP] SlimeRainTrap: 'Tarr' IdentifiableType not found — running normal Slime Rain weather instead");
        }
        else
        {
            // Swap every spawn activity to Tarr; ResetWeather restores the originals.
            _savedSlimeRainActorTypes = new System.Collections.Generic.Dictionary<SpawnActorActivity, IdentifiableType>();
            var activities = slimeRainDef.Activities;
            for (int i = 0; i < activities.Count; i++)
            {
                var activity = activities[i].Activity?.TryCast<SpawnActorActivity>();
                if (activity != null && activity.ActorType != null)
                {
                    _savedSlimeRainActorTypes[activity] = activity.ActorType;
                    activity.ActorType = tarrType;
                    Logger.Info(
                        $"[AP] SlimeRainTrap: overrode SpawnActorActivity ActorType '{_savedSlimeRainActorTypes[activity].name}' → 'Tarr'");
                }
            }

            if (_savedSlimeRainActorTypes.Count == 0)
                Logger.Warning("[AP] SlimeRainTrap: no SpawnActorActivity found in Slime Rain weather — Tarr override has no effect");
        }

        _activeWeatherState = slimeRainDef;
        _cachedDirectors    = new WeatherDirector[found.Count];
        _cachedPatterns     = null; // Tarr Rain uses RunState directly; no registry stop needed
        for (int i = 0; i < found.Count; i++) _cachedDirectors[i] = found[i];

#if DEBUG
        const float durationSeconds = 20f;
#else
        const float durationSeconds = 60f;
#endif
        _weatherResetAt = UnityEngine.Time.time + durationSeconds;

        foreach (var director in _cachedDirectors)
        {
            if (director == null) continue;
            var running = director._runningStates;
            if (running != null && running.Count > 0)
            {
                var toStop = new IWeatherState[running.Count];
                for (int i = 0; i < running.Count; i++) toStop[i] = running[i];
                foreach (var s in toStop)
                    if (s != null) director.StopState(s, null, immediate: true);
            }
            director.RunState(slimeRainDef.Cast<IWeatherState>(), null, immediate: false);
        }

        _lastGroupFiredAt[(int)TrapGroup.Weather] = UnityEngine.Time.time;
        Logger.Info(
            $"[AP] SlimeRainTrap: started Slime Rain (Tarr override) on {found.Count} director(s) for {durationSeconds}s");
        SlimeRancher2AP.UI.StatusHUD.Instance?.ShowNotification("Trap: Tarr Rain!");
        return true;
    }

    // The zone's own pattern containing the state (by StateName), or null.
    private static WeatherPatternDefinition? FindPatternForZone(
        WeatherRegistry registry, ZoneDefinition zone, string stateName)
    {
        var configs = registry.ZoneConfigList;
        if (configs == null) return null;
        for (int ci = 0; ci < configs.Count; ci++)
        {
            var config = configs[ci];
            if (config?.Zone?.Pointer != zone?.Pointer) continue;
            var patterns = config!.Patterns;
            if (patterns == null) break;
            for (int pi = 0; pi < patterns.Count; pi++)
            {
                if (WeatherPatternContainsState(patterns[pi], stateName)) return patterns[pi];
            }
            break;
        }
        return null;
    }

    // True if any transition in the pattern touches the state.
    private static bool WeatherPatternContainsState(WeatherPatternDefinition? pat, string stateName)
    {
        if (pat == null) return false;
        var startTrans = pat.StartingTransitions;
        if (startTrans != null)
            for (int j = 0; j < startTrans.Count; j++)
            {
                var sd = startTrans[j]?.ToState?.TryCast<WeatherStateDefinition>();
                if (sd != null && (sd.StateName ?? sd.name ?? "") == stateName) return true;
            }
        var runTrans = pat.RunningTransitions;
        if (runTrans == null) return false;
        for (int j = 0; j < runTrans.Count; j++)
        {
            var tl = runTrans[j];
            if (tl == null) continue;
            var fromSd = tl.FromState?.TryCast<WeatherStateDefinition>();
            if (fromSd != null && (fromSd.StateName ?? fromSd.name ?? "") == stateName) return true;
            var tls = tl.Transitions;
            if (tls == null) continue;
            for (int k = 0; k < tls.Count; k++)
            {
                var toSd = tls[k]?.ToState?.TryCast<WeatherStateDefinition>();
                if (toSd != null && (toSd.StateName ?? toSd.name ?? "") == stateName) return true;
            }
        }
        return false;
    }

    /// <summary>
    /// Ends a live weather trap before the next starts. The group cooldown (30 s) is shorter
    /// than either effect, so overlap is routine; without this the first pattern never stops and
    /// natural Slime Rain could keep spawning Tarr all session.
    /// </summary>
    private static void StopActiveWeatherTrap()
    {
        if (_activeWeatherState == null && _savedSlimeRainActorTypes == null) return;
        Logger.Info("[AP] Weather trap already active — stopping it before starting the next one");
        ResetWeather();
        _weatherResetAt = -1f;
    }

    private static void RestoreSlimeRainActorTypes()
    {
        if (_savedSlimeRainActorTypes == null) return;
        foreach (var kvp in _savedSlimeRainActorTypes)
            kvp.Key.ActorType = kvp.Value;
        _savedSlimeRainActorTypes = null;
    }

    /// <summary>Extends an active weather trap to directors that loaded after it fired.</summary>
    private static void ReapplyWeatherToNewZone()
    {
        if (_activeWeatherState == null || _weatherResetAt < 0f) return;

        var found = Resources.FindObjectsOfTypeAll<WeatherDirector>();
        if (found == null || found.Count == 0) return;

        var newDirectors = new WeatherDirector[found.Count];
        var newPatterns  = new IWeatherPattern[found.Count];
        for (int i = 0; i < found.Count; i++) newDirectors[i] = found[i];

        var chosenState = _activeWeatherState.Cast<IWeatherState>();
        int applied = 0;

        for (int di = 0; di < newDirectors.Length; di++)
        {
            var director = newDirectors[di];
            if (director == null) continue;

            bool alreadyTracked = false;
            if (_cachedDirectors != null)
            {
                for (int ci = 0; ci < _cachedDirectors.Length; ci++)
                    if (_cachedDirectors[ci] != null && _cachedDirectors[ci].Pointer == director.Pointer)
                    {
                        newPatterns[di] = _cachedPatterns?[ci]!;
                        alreadyTracked  = true;
                        break;
                    }
            }
            if (alreadyTracked) continue;

            var running = director._runningStates;
            if (running != null && running.Count > 0)
            {
                var toStop = new IWeatherState[running.Count];
                for (int i = 0; i < running.Count; i++) toStop[i] = running[i];
                foreach (var s in toStop)
                    if (s != null) director.StopState(s, null, immediate: true);
            }

            var registry       = director._registry;
            var zone           = director.Zone;
            var activeName     = _activeWeatherState.StateName ?? _activeWeatherState.name ?? "";
            var zonePatternDef = (registry != null && zone != null)
                ? FindPatternForZone(registry, zone, activeName) : null;

            if (registry != null && zone != null && zonePatternDef != null)
            {
                var pattern = zonePatternDef.CreatePattern();
                newPatterns[di] = pattern;
                registry.RunPatternState(zone, pattern, chosenState, true);
                Logger.Info($"[AP] WeatherChange: re-applied '{activeName}' (pattern='{zonePatternDef.name}') to new director zone='{zone.name}'");
            }
            else
            {
                if (activeName.IndexOf("Wind", System.StringComparison.OrdinalIgnoreCase) < 0)
                    director.RunState(chosenState, null, immediate: false);
                Logger.Info($"[AP] WeatherChange: re-applied '{activeName}' (fallback) to new director zone='{zone?.name ?? "?"}'");
            }
            applied++;
        }

        _cachedDirectors = newDirectors;
        _cachedPatterns  = newPatterns;

        if (applied > 0)
            Logger.Info($"[AP] WeatherChange: zone change — {applied} new director(s) added to weather trap");
    }

    private static void ResetWeather()
    {
        if (_cachedDirectors == null || _activeWeatherState == null)
        {
            _cachedDirectors    = null;
            _activeWeatherState = null;
            _cachedPatterns     = null;
            RestoreSlimeRainActorTypes();
            return;
        }

        var stateAsIWeather = _activeWeatherState.Cast<IWeatherState>();
        for (int di = 0; di < _cachedDirectors.Length; di++)
        {
            var director = _cachedDirectors[di];
            if (director == null) continue;
            var pattern  = _cachedPatterns?[di];
            var registry = director._registry;
            var zone     = director.Zone;
            if (pattern != null && registry != null && zone != null)
                registry.StopPatternState(zone, pattern, stateAsIWeather);
            else
                director.StopState(stateAsIWeather, null, immediate: false);
        }

        string label = _activeWeatherState.StateName ?? _activeWeatherState.name;
        _cachedDirectors    = null;
        _activeWeatherState = null;
        _cachedPatterns     = null;

        RestoreSlimeRainActorTypes();

        Logger.Info($"[AP] WeatherChange: stopped '{label}', natural weather cycle resumed");
    }
}
