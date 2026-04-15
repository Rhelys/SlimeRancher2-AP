using System;
using Archipelago.MultiClient.Net.Models;
using Il2CppMonomiPark.SlimeRancher.DataModel;
using Il2CppMonomiPark.SlimeRancher.Economy;
using Il2CppMonomiPark.SlimeRancher.Player;
using Il2CppMonomiPark.SlimeRancher.Player.Component;
using Il2CppMonomiPark.SlimeRancher.Util;
using Il2CppMonomiPark.SlimeRancher.Weather;
using Il2CppMonomiPark.SlimeRancher.Weather.Activity;
using Il2CppMonomiPark.SlimeRancher.SceneManagement;
using Il2CppMonomiPark.SlimeRancher.World.Teleportation;
using SlimeRancher2AP.Data;
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
    /// Cached reference to the player's ActorUpgradeHandler, populated by
    /// ActorUpgradeHandlerCtorPatch on first game load. ActorUpgradeHandler is not a
    /// MonoBehaviour so GetComponent always returns null; constructor interception is required.
    /// </summary>
    public static ActorUpgradeHandler? UpgradeHandler { get; set; }

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
            Plugin.Instance.Log.LogWarning("[AP-Debug] SceneContext not ready — load a save first");
            return;
        }

        var item = ItemTable.Get(itemId);
        if (item == null)
        {
            Plugin.Instance.Log.LogWarning($"[AP-Debug] Unknown item ID: {itemId}");
            return;
        }

        Plugin.Instance.Log.LogInfo($"[AP-Debug] Granting item: {item.Name}");

        switch (item.Type)
        {
            case ItemType.RegionAccess: ApplyRegionAccess(item);                        break;
            case ItemType.Upgrade:      ApplyUpgrade(item, null, itemIndex);            break;
            case ItemType.Gadget:       ApplyGadget(item, null, itemIndex);             break;
            case ItemType.Filler:           ApplyFiller(item, null, itemIndex);              break;
            case ItemType.UpgradeComponent: ApplyUpgradeComponent(item, null, itemIndex);    break;
            case ItemType.Trap:             ApplyTrap(item);                                 break;
        }

        Plugin.Instance.SaveManager.UpdateLastItemIndex(itemIndex);
    }

    public static void Apply(ApItemInfo apItem, int itemIndex)
    {
        // Defer if the scene context isn't ready (e.g., loading screen).
        // Guard: only requeue items that haven't been applied yet. Items with
        // itemIndex <= LastItemIndex were already applied in a previous session;
        // requeuing them here would cause an infinite loop on the loading screen
        // because OnItemReceived may enqueue them before OnConnected loads the
        // correct LastItemIndex from the save file (race condition on reconnect).
        if (SceneContext.Instance == null)
        {
            if (itemIndex > Plugin.Instance.SaveManager.LastItemIndex)
                Plugin.Instance.ApClient.RequeueItem(apItem, itemIndex);
            return;
        }

        var item = ItemTable.Get(apItem.ItemId);
        if (item == null)
        {
            Plugin.Instance.Log.LogWarning($"[AP] Unknown item ID: {apItem.ItemId}");
            Plugin.Instance.SaveManager.UpdateLastItemIndex(itemIndex);
            return;
        }

        Plugin.Instance.Log.LogInfo($"[AP] Applying item: {item.Name} (id={item.Id}, idx={itemIndex})");

        switch (item.Type)
        {
            case ItemType.RegionAccess: ApplyRegionAccess(item);              break;
            case ItemType.Upgrade:      ApplyUpgrade(item, apItem, itemIndex); break;
            case ItemType.Gadget:       ApplyGadget(item, apItem, itemIndex);  break;
            case ItemType.Filler:           ApplyFiller(item, apItem, itemIndex);           break;
            case ItemType.UpgradeComponent: ApplyUpgradeComponent(item, apItem, itemIndex); break;
            case ItemType.Trap:             ApplyTrap(item);                                break;
        }

        Plugin.Instance.SaveManager.UpdateLastItemIndex(itemIndex);
    }

    // -------------------------------------------------------------------------

    // Maps region access item name → zone teleporter GadgetDefinition asset name.
    // Used by both bundled mode (ApplyRegionAccess) and auto mode (RegionGatePatch Postfix).
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

        if (!RegionTable.TryGetSwitch(item.Name, out var switchName)) return;

        foreach (var sw in Resources.FindObjectsOfTypeAll<WorldStatePrimarySwitch>())
        {
            if (sw.gameObject.name != switchName) continue;
            sw.SetStateForAll(SwitchHandler.State.DOWN, true);
            Plugin.Instance.Log.LogInfo($"[AP] Opened region gate: {switchName}");
            break;
        }

        // Bundled mode: grant the matching zone teleporter alongside the region access item
        if ((Plugin.Instance.ApClient.SlotData?.ZoneTeleporterMode ?? "item") == "bundled")
            GrantRegionTeleporter(item.Name);
    }

    /// <summary>
    /// Called from RegionGatePatch Postfix (auto mode) when a zone gate opens in-world.
    /// Looks up the region name for the given switch and grants its zone teleporter.
    /// </summary>
    internal static void TryGrantRegionTeleporterForSwitch(string switchName)
    {
        if (!RegionTable.TryGetRegionForSwitch(switchName, out var regionName)) return;
        GrantRegionTeleporter(regionName);
    }

    private static void GrantRegionTeleporter(string regionName)
    {
        if (!RegionToZoneTeleporter.TryGetValue(regionName, out var gadgetName)) return;
        var director = SceneContext.Instance?.GadgetDirector;
        if (director == null) return;

        // Idempotency: skip if already unlocked (handles reconnect replays and double-fires)
        var gadgetDef = Resources.FindObjectsOfTypeAll<GadgetDefinition>()
                                 .FirstOrDefault(d => d.name == gadgetName);
        if (gadgetDef != null && director.IsBlueprintUnlocked(gadgetDef)) return;

        GrantSingleGadget(director, gadgetName);
    }

    /// <summary>
    /// Tracks the current absolute level of each upgrade, populated by the
    /// <c>OnUpgradeChanged</c> patch in <c>ActorUpgradeHandlerPatch.cs</c>.
    /// Fires both on save-data restore and on explicit <c>ApplyUpgrade</c> calls,
    /// so it always reflects the true in-game state.
    /// </summary>
    private static readonly Dictionary<string, int> _upgradeLevels = new();

    /// <summary>Called by <c>ActorUpgradeHandlerPatch.OnUpgradeChangedPostfix</c>.</summary>
    public static void TrackUpgradeLevel(string upgradeName, int newLevel)
        => _upgradeLevels[upgradeName] = newLevel;

    private static void ApplyUpgrade(Data.ItemInfo item, ApItemInfo? apItem, int itemIndex)
    {
        // ActorUpgradeHandler is not a MonoBehaviour — GetComponent always returns null.
        // Instead we use UpgradeHandler, which is populated by ActorUpgradeHandlerCtorPatch
        // the first time the player's upgrade handler is constructed.
        if (UpgradeHandler == null)
        {
            Plugin.Instance.Log.LogWarning("[AP] ActorUpgradeHandler not yet cached — requeuing upgrade");
            if (apItem != null) Plugin.Instance.ApClient.RequeueItem(apItem, itemIndex);
            return;
        }

        // Maps item ID → UpgradeDefinition.name (confirmed via AP-Dump log + APWORLD_DESIGN.md)
        var upgradeName = item.Id switch
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
            _                                      => (string?)null
        };
        if (upgradeName == null) return;

        var upgradeDef = Resources.FindObjectsOfTypeAll<UpgradeDefinition>()
                                  .FirstOrDefault(d => d.name == upgradeName);
        if (upgradeDef == null)
        {
            Plugin.Instance.Log.LogWarning($"[AP] UpgradeDefinition '{upgradeName}' not found");
            return;
        }

        // ApplyUpgrade takes an ABSOLUTE level (not a delta).
        // Valid range: -1 (locked) to LevelCount-1 (fully upgraded).
        // Read current level directly from the model (always matches game save state).
        // Fall back to _upgradeLevels for rapid successive calls within the same frame
        // before ApplyUpgrade has had a chance to update the model.
        int modelLevel   = UpgradeHandler._model?.GetUpgradeLevel(upgradeDef) ?? -1;
        int cachedLevel  = _upgradeLevels.TryGetValue(upgradeName, out var lvl) ? lvl : -1;
        int currentLevel = System.Math.Max(modelLevel, cachedLevel);
        int maxLevel     = upgradeDef.LevelCount - 1;
        int targetLevel  = currentLevel + 1;

        if (targetLevel > maxLevel)
        {
            Plugin.Instance.Log.LogInfo($"[AP] Upgrade '{upgradeName}' already at max level ({maxLevel}) — skipping");
            Notify($"Received: {item.Name} (already maxed)");
            return;
        }

        UpgradeHandler.ApplyUpgrade(upgradeDef, targetLevel);
        _upgradeLevels[upgradeName] = targetLevel; // update immediately; patch callback may fire late or not at all
        Plugin.Instance.Log.LogInfo($"[AP] Applied upgrade: {upgradeName} → level {targetLevel}/{maxLevel}");
        Notify($"Received: {item.Name}");
    }

    // Simple 1:1 grants: AP item ID → GadgetDefinition.name (from DumpGadgets).
    // Asset names marked (?) need in-game verification via DumpGadgets.
    // Verified against apworld items.py (2026-04-12).
    private static readonly Dictionary<long, string> SimpleGadgets = new()
    {
        // Zone Teleporters — one per region (confirmed via DumpGadgets)
        [ItemTable.TeleporterEmberValley]      = "TeleporterZoneGorge",
        [ItemTable.TeleporterStarlightStrand]  = "TeleporterZoneStrand",
        [ItemTable.TeleporterPowderfallBluffs] = "TeleporterZoneBluffs",
        [ItemTable.TeleporterGreyLabyrinth]    = "TeleporterZoneLabyrinth",

        // Home Teleporters (confirmed via DumpGadgets)
        [ItemTable.HomeTeleporterBlue]   = "TeleporterHomeBlue",
        [ItemTable.HomeTeleporterGreen]  = "TeleporterHomeGreen",
        [ItemTable.HomeTeleporterRed]    = "TeleporterHomeRed",
        [ItemTable.HomeTeleporterYellow] = "TeleporterHomeYellow",

        // Warp Depots (confirmed via DumpGadgets)
        [ItemTable.WarpDepotGrey]   = "WarpDepotGrey",
        [ItemTable.WarpDepotBerry]  = "WarpDepotBerry",    // orange-coloured variant
        [ItemTable.WarpDepotViolet] = "WarpDepotViolet",
        [ItemTable.WarpDepotSnowy]  = "WarpDepotSnowy",

        // Functional gadgets (?)
        [ItemTable.MarketLink]         = "MarketLink",         // (?)
        [ItemTable.SuperHydroTurret]   = "SuperHydroTurret",   // (?)
        [ItemTable.PortableScareSlime] = "PortableScareSlime", // (?)
        [ItemTable.GordoSnareAdvanced] = "GordoSnareAdvanced", // (?)
        [ItemTable.MedStation]         = "MedStation",         // (?)
        [ItemTable.DreamLanternT2]     = "DreamLanternT2",     // confirmed via DumpGadgets

        // Radiant Projector Blueprint (Special Access — grants via AddBlueprint, not AddItem)
        [ItemTable.RadiantProjectorBlueprint] = "EnergyBeamNode",
    };

    private static void ApplyGadget(Data.ItemInfo item, ApItemInfo? apItem, int itemIndex)
    {
        var gadgetDirector = SceneContext.Instance.GadgetDirector;
        if (gadgetDirector == null) return;

        // Simple 1:1 gadget grant (covers zone teleporters, home teleporters, warp depots,
        // functional gadgets, and the Radiant Projector Blueprint via SimpleGadgets lookup)
        if (SimpleGadgets.TryGetValue(item.Id, out var gadgetName))
        {
            GrantSingleGadget(gadgetDirector, gadgetName);
            Notify($"Received: {item.Name}");
            return;
        }

        Plugin.Instance.Log.LogWarning($"[AP] Unhandled gadget item ID: {item.Id}");
    }

    /// <summary>
    /// Looks up a GadgetDefinition by asset name, unlocks its blueprint, and adds one unit
    /// to the player's usable gadget inventory.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <c>AddBlueprint(def, false)</c> — unlocks the gadget type in the Fabricator/Gadget menu
    /// (second param suppresses the Pedia unlock notification).
    /// </para>
    /// <para>
    /// <c>AddItem(def, 1)</c> — adds one usable unit to inventory. <c>GadgetDefinition</c>
    /// extends <c>IdentifiableType</c>, so it is accepted directly by the overload.
    /// This replaces the previous <c>OnGadgetPickedUp</c> call, which is a world-event
    /// callback (CallerCount=1) and does not reliably add to inventory when called outside
    /// the normal pick-up flow.
    /// </para>
    /// </remarks>
    private static void GrantSingleGadget(GadgetDirector director, string gadgetName)
    {
        var gadgetDef = Resources.FindObjectsOfTypeAll<GadgetDefinition>()
                                 .FirstOrDefault(d => d.name == gadgetName);
        if (gadgetDef == null)
        {
            Plugin.Instance.Log.LogWarning($"[AP] GadgetDefinition '{gadgetName}' not found in loaded assets");
            return;
        }

        director.AddBlueprint(gadgetDef, false);
        director.AddItem(gadgetDef, 1); // GadgetDefinition : IdentifiableType
        Plugin.Instance.Log.LogInfo($"[AP] Granted gadget: {gadgetName}");
    }

    private static void ApplyFiller(Data.ItemInfo item, ApItemInfo? apItem, int itemIndex)
    {
        // Newbucks — grant directly to player wallet
        if (item.Id is ItemTable.Newbucks250 or ItemTable.Newbucks500 or ItemTable.Newbucks1000)
        {
            ApplyNewbucks(item, apItem, itemIndex);
            return;
        }

        // Plort/craft caches — deposit a random assortment of 10 items into the refinery
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
            ApplyRefineryCache(pool, item.Name, count: 10, apItem, itemIndex);
    }

    // Map ItemTable ID → UpgradeComponent asset name.
    // All names confirmed via upgrade_components.txt dump (2026-04-13).
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
    };

    private static void ApplyUpgradeComponent(Data.ItemInfo item, ApItemInfo? apItem, int itemIndex)
    {
        if (!_upgradeComponentNames.TryGetValue(item.Id, out var compName)) return;

        if (!SceneContextUtility.TryGetUpgradeComponentsModel(out var model) || model == null)
        {
            if (apItem != null) Plugin.Instance.ApClient.RequeueItem(apItem, itemIndex);
            return;
        }

        var comp = Resources.FindObjectsOfTypeAll<UpgradeComponent>()
                            .FirstOrDefault(c => c.name == compName);
        if (comp == null)
        {
            Plugin.Instance.Log.LogWarning($"[AP] UpgradeComponent asset '{compName}' not found — grant skipped");
            return;
        }

        model.GainComponent(comp);
        Notify($"Received: {item.Name}");
        Plugin.Instance.Log.LogInfo($"[AP] Granted upgrade component: {compName}");
    }

    private static void ApplyNewbucks(Data.ItemInfo item, ApItemInfo? apItem, int itemIndex)
    {
        var playerState = SceneContext.Instance?.PlayerState;
        if (playerState == null) { if (apItem != null) Plugin.Instance.ApClient.RequeueItem(apItem, itemIndex); return; }

        var amount = item.Id switch
        {
            ItemTable.Newbucks250  => 250,
            ItemTable.Newbucks500  => 500,
            ItemTable.Newbucks1000 => 1000,
            _                      => 0
        };
        if (amount <= 0) return;

        var currency = Resources.FindObjectsOfTypeAll<CurrencyDefinition>()
                                .FirstOrDefault(c => c.name.Contains("Newbucks", StringComparison.OrdinalIgnoreCase));
        if (currency == null)
        {
            Plugin.Instance.Log.LogWarning("[AP] Could not find Newbucks CurrencyDefinition — grant skipped");
            return;
        }

        playerState.AddCurrency(currency.Cast<ICurrency>(), amount);
        Notify($"Received: {amount} Newbucks");
    }

    private static void ApplyRefineryCache(string[] pool, string cacheName, int count,
                                             ApItemInfo? apItem, int itemIndex)
    {
        var director = SceneContext.Instance?.GadgetDirector;
        if (director == null) { if (apItem != null) Plugin.Instance.ApClient.RequeueItem(apItem, itemIndex); return; }

        // Pre-fetch all IdentifiableType assets once to avoid repeated FindObjectsOfTypeAll calls.
        var allIdents = Resources.FindObjectsOfTypeAll<IdentifiableType>();

        int totalAdded = 0;
        for (int i = 0; i < count; i++)
        {
            // Each pick is independent — yields a random assortment rather than all-one-type.
            var typeName = pool[_rng.Next(pool.Length)];
            var identType = allIdents.FirstOrDefault(t => t.name == typeName);
            if (identType == null)
            {
                Plugin.Instance.Log.LogWarning($"[AP] Could not find IdentifiableType '{typeName}' — skipping this pick");
                continue;
            }
            director.AddItem(identType, 1);
            totalAdded++;
        }

        Plugin.Instance.Log.LogInfo($"[AP] Deposited {totalAdded} items from '{cacheName}' into refinery");
        Notify($"Received: {cacheName} ({totalAdded} items) → Refinery");
    }

    private static void ApplyTrap(Data.ItemInfo item)
    {
        TrapHandler.Schedule(item.Id);
    }

    private static void Notify(string message) =>
        SlimeRancher2AP.UI.StatusHUD.Instance?.ShowNotification(message);

#if DEBUG
    // -------------------------------------------------------------------------
    // Debug-only helpers — not part of the normal AP item flow
    // -------------------------------------------------------------------------

    /// <summary>
    /// Grants the Radiant Projector gadget blueprint and one unit to the player's inventory.
    /// The GadgetDefinition asset name "RadiantProjector" follows the established naming
    /// pattern — verify via Dump Gadget Names if this logs "not found".
    /// </summary>
    // Confirmed GadgetDefinition asset name for the Radiant Projector — update once known.
    // If this logs "not found", check the BepInEx log immediately below for all loaded gadget
    // names containing "radiant", "puzzle", or "projector" to identify the correct name.
    private const string RadiantProjectorGadgetName = "EnergyBeamNode"; // confirmed via DumpGadgets — "RadiantProjector" does not exist

    public static void DebugGrantRadiantProjector()
    {
        var director = SceneContext.Instance?.GadgetDirector;
        if (director == null)
        {
            Plugin.Instance.Log.LogWarning("[AP-Debug] GadgetDirector not available — load a save first");
            return;
        }

        var def = Resources.FindObjectsOfTypeAll<GadgetDefinition>()
                           .FirstOrDefault(d => d.name == RadiantProjectorGadgetName);
        if (def == null)
        {
            Plugin.Instance.Log.LogWarning(
                $"[AP-Debug] GadgetDefinition '{RadiantProjectorGadgetName}' not found");
            return;
        }

        // Only unlock the blueprint — the player crafts units from the Fabricator as normal.
        // This matches what Viktor's communication does in-game, and ensures the unlock persists.
        // AddItem is intentionally NOT called here; pre-crafted unit counts don't persist for
        // this gadget type (the Fabricator crafting system tracks the inventory, not AddItem).
        director.AddBlueprint(def, false);
        Plugin.Instance.Log.LogInfo($"[AP-Debug] Unlocked blueprint: '{def.name}' — craft units in the Fabricator");
    }

    /// <summary>
    /// Debug-only: grants the Radiant Projector blueprint AND immediately adds one pre-crafted
    /// unit to the player's inventory — skips the Fabricator step so the beam puzzle can be
    /// tested without gathering crafting materials.
    /// </summary>
    public static void DebugGrantRadiantProjectorUnit()
    {
        var director = SceneContext.Instance?.GadgetDirector;
        if (director == null)
        {
            Plugin.Instance.Log.LogWarning("[AP-Debug] GadgetDirector not available — load a save first");
            return;
        }

        var def = Resources.FindObjectsOfTypeAll<GadgetDefinition>()
                           .FirstOrDefault(d => d.name == RadiantProjectorGadgetName);
        if (def == null)
        {
            Plugin.Instance.Log.LogWarning(
                $"[AP-Debug] GadgetDefinition '{RadiantProjectorGadgetName}' not found");
            return;
        }

        director.AddBlueprint(def, false);
        director.AddItem(def, 1); // GadgetDefinition : IdentifiableType — adds one placeable unit
        Plugin.Instance.Log.LogInfo($"[AP-Debug] Granted blueprint + 1 unit: '{def.name}'");
    }

    /// <summary>
    /// Places <paramref name="count"/> of the named food type directly into the player's vacpack.
    /// Fills the first unlocked slot that is empty or already contains the same type.
    /// IdentifiableType asset names — use Dump IdentifiableTypes (Misc page) to discover them.
    /// </summary>
    public static void DebugGrantFoodToVacpack(string typeName, int count = 20)
    {
        var playerState = SceneContext.Instance?.PlayerState;
        if (playerState == null)
        {
            Plugin.Instance.Log.LogWarning("[AP-Debug] PlayerState not available — load a save first");
            return;
        }

        var identType = Resources.FindObjectsOfTypeAll<IdentifiableType>()
                                 .FirstOrDefault(t => string.Equals(
                                     t.name, typeName, System.StringComparison.OrdinalIgnoreCase));
        if (identType == null)
        {
            Plugin.Instance.Log.LogWarning(
                $"[AP-Debug] IdentifiableType '{typeName}' not found — use Dump IdentifiableTypes (Misc page) to find the correct name");
            return;
        }

        var ammo = playerState.Ammo;
        if (ammo == null)
        {
            Plugin.Instance.Log.LogWarning("[AP-Debug] PlayerState.Ammo not available");
            return;
        }

        var slots = ammo.Slots; // Il2CppReferenceArray<AmmoSlot>
        int granted = 0;

        for (int i = 0; i < slots.Length && granted < count; i++)
        {
            var slot = slots[i];
            if (slot == null || !slot.IsUnlocked) continue;

            // Skip slots already occupied by a different type
            if (slot.Id != null && slot.Count > 0 && slot.Id.name != typeName) continue;

            // Assign type if the slot is empty
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
            Plugin.Instance.Log.LogInfo($"[AP-Debug] Placed {granted}x '{typeName}' into vacpack");
            SlimeRancher2AP.UI.StatusHUD.Instance?.ShowNotification($"Debug: {granted}x {typeName} → vacpack");
        }
        else
        {
            Plugin.Instance.Log.LogWarning(
                "[AP-Debug] No free vacpack slots for this food type — move or discard current contents first");
        }
    }
#endif
}

/// <summary>
/// Schedules and ticks active trap effects.
/// <c>Tick()</c> must be called every frame from <c>ApUpdateBehaviour.Update()</c>.
/// </summary>
public static class TrapHandler
{
    private static readonly System.Random _rng = new();

    // -------------------------------------------------------------------------
    // Runtime region-gate tracking (for auto mode / vanilla gate opens)
    // -------------------------------------------------------------------------

    // Region item names (e.g. "Ember Valley Access") that have been observed going DOWN
    // during this session. Populated by RegionGatePatch.Postfix so the teleport trap can
    // check accessibility without relying on AP save data (which is never written in auto mode).
    private static readonly HashSet<string> _runtimeOpenRegions = new();

    // SceneGroup.ReferenceId strings for every zone the player has been in this session.
    // If the player is physically in a zone, that zone is reachable by definition.
    // Used as a fallback when gate events haven't fired (previous session, gadget teleporters, etc.)
    private static readonly HashSet<string> _visitedSceneGroupRefs = new();
    private static string? _lastSeenGroupRef = null;

    /// <summary>
    /// Called by <c>RegionGatePatch.Postfix</c> whenever a tracked region gate opens (DOWN).
    /// Records the region as accessible for this session.
    /// </summary>
    public static void MarkRegionOpen(string regionItemName)
        => _runtimeOpenRegions.Add(regionItemName);

    /// <summary>
    /// Called every frame from <c>ApUpdateBehaviour.Update()</c>. Records the player's current
    /// zone so that teleport trap eligibility can be inferred from visited zones.
    /// </summary>
    public static void TrackCurrentZone(string? sceneGroupRef)
    {
        if (sceneGroupRef == null || sceneGroupRef == _lastSeenGroupRef) return;
        _lastSeenGroupRef = sceneGroupRef;
        _visitedSceneGroupRefs.Add(sceneGroupRef);
#if DEBUG
        Plugin.Instance.Log.LogInfo($"[AP] TrapHandler: zone visit recorded — '{sceneGroupRef}'");
#endif
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

    // SpawnActorActivity.ActorType values overridden by the Slime Rain trap; restored on weather reset.
    private static System.Collections.Generic.Dictionary<SpawnActorActivity, IdentifiableType>?
        _savedSlimeRainActorTypes = null;

    // -------------------------------------------------------------------------
    // Public API
    // -------------------------------------------------------------------------

    /// <summary>Called every frame from ApUpdateBehaviour to tick timed trap effects.</summary>
    public static void Tick()
    {
        if (_weatherResetAt < 0f) return;

        if (UnityEngine.Time.time >= _weatherResetAt)
        {
            ResetWeather();
            _weatherResetAt = -1f;
            return;
        }
    }

    public static void Schedule(long trapItemId)
    {
        switch (trapItemId)
        {
            case ItemTable.TrapSlimeRing:    ApplySlimeRingTrap();  break;
            case ItemTable.TrapTarrSpawn:    ApplyTarrSpawnTrap();  break;
            case ItemTable.TrapTeleport:     ApplyTeleportTrap();   break;
            case ItemTable.TrapWeatherChange: ApplyWeatherTrap();   break;
            case ItemTable.TrapTarrRain:    ApplySlimeRainTrap();  break;
            default:
                Plugin.Instance.Log.LogInfo($"[AP] Trap received: id={trapItemId} (not yet implemented)");
                break;
        }
    }

    // -------------------------------------------------------------------------
    // Slime Ring trap — spawns a burst of common slimes around the player
    // -------------------------------------------------------------------------

    // Base (non-largo) slime IdentifiableType names confirmed from identifiable_types.txt dump.
    // Excludes dangerous/situational slimes (Boom = explosive, Saber/Ringtail = rare zones).
    private static readonly string[] _commonSlimeNames =
    {
        "Pink", "Tabby", "Rock", "Cotton", "Honey", "Phosphor", "Crystal", "Ringtail",
    };

    private static void ApplySlimeRingTrap()
    {
        var playerGo = SceneContext.Instance?.Player;
        if (playerGo == null)
        {
            Plugin.Instance.Log.LogWarning("[AP] SlimeRainTrap: no Player in scene — skipped");
            return;
        }

        // Resolve IdentifiableType assets once, keeping only those with valid prefabs.
        var allTypes = Resources.FindObjectsOfTypeAll<IdentifiableType>();
        var slimeTypes = new System.Collections.Generic.List<IdentifiableType>();
        foreach (var name in _commonSlimeNames)
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
            Plugin.Instance.Log.LogWarning("[AP] SlimeRainTrap: no valid slime IdentifiableTypes found — skipped");
            return;
        }

#if DEBUG
        const int count = 4;
#else
        const int count = 8;
#endif
        var origin = playerGo.transform.position;
        int spawned = 0;

        for (int i = 0; i < count; i++)
        {
            var slimeType = slimeTypes[_rng.Next(slimeTypes.Count)];

            // Scatter in a ring around the player, dropping from slightly above.
            float angle  = (float)(_rng.NextDouble() * System.Math.PI * 2.0);
            float radius = 2f + (float)(_rng.NextDouble() * 2f);    // 2–4 m radius
            float height = 3f + (float)(_rng.NextDouble() * 2f);    // 3–5 m above
            var   spawnPos = origin + new Vector3(
                UnityEngine.Mathf.Cos(angle) * radius,
                height,
                UnityEngine.Mathf.Sin(angle) * radius);

            var go = UnityEngine.Object.Instantiate(slimeType.prefab, spawnPos, UnityEngine.Quaternion.identity);
            if (go != null) spawned++;
        }

        Plugin.Instance.Log.LogInfo($"[AP] SlimeRingTrap: spawned {spawned} slimes around player");
        SlimeRancher2AP.UI.StatusHUD.Instance?.ShowNotification("Trap: Slime Ring!");
    }

    // -------------------------------------------------------------------------
    // Tarr Spawn trap — spawns Tarr near the player
    // -------------------------------------------------------------------------

    private static void ApplyTarrSpawnTrap()
    {
        var playerGo = SceneContext.Instance?.Player;
        if (playerGo == null)
        {
            Plugin.Instance.Log.LogWarning("[AP] TarrSpawnTrap: no Player in scene — skipped");
            return;
        }

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
            Plugin.Instance.Log.LogWarning("[AP] TarrSpawnTrap: 'Tarr' IdentifiableType not found or has no prefab — skipped");
            return;
        }

#if DEBUG
        const int count = 1;
#else
        const int count = 2;
#endif
        var origin = playerGo.transform.position;
        int spawned = 0;

        // Use SpawnActorActivity.Spawn() — the same code path the weather system uses
        // for Slime Rain / Tarr Rain. Raw Object.Instantiate skips Tarr's AI and state
        // machine initialization, producing a broken entity that does not aggro.
        var activity = new SpawnActorActivity();
        activity.ActorType = tarrType;

        for (int i = 0; i < count; i++)
        {
            // Spawn close to the player (2–4 m away) at head height so they immediately aggro.
            float angle    = (float)(_rng.NextDouble() * System.Math.PI * 2.0);
            float radius   = 2f + (float)(_rng.NextDouble() * 2f);
            var   spawnPos = origin + new Vector3(
                UnityEngine.Mathf.Cos(angle) * radius,
                1.5f,
                UnityEngine.Mathf.Sin(angle) * radius);

            var go = activity.Spawn(spawnPos, UnityEngine.Quaternion.identity);
            if (go != null) spawned++;
        }

        Plugin.Instance.Log.LogInfo($"[AP] TarrSpawnTrap: spawned {spawned} Tarr near player");
        SlimeRancher2AP.UI.StatusHUD.Instance?.ShowNotification("Trap: Tarr Incoming!");
    }

    // -------------------------------------------------------------------------
    // Weather trap
    // -------------------------------------------------------------------------

    private static void ApplyWeatherTrap()
    {
        var found = Resources.FindObjectsOfTypeAll<WeatherDirector>();
        if (found == null || found.Count == 0)
        {
            Plugin.Instance.Log.LogWarning("[AP] WeatherTrap: no WeatherDirector found in scene — trap skipped");
            return;
        }

        // Find all WeatherStateDefinition assets — these are the game's actual weather states
        // (Rain, Thunderstorm, Pollen, SlimeRain, etc.) and implement IWeatherState.
        var allStates = Resources.FindObjectsOfTypeAll<WeatherStateDefinition>();
        if (allStates == null || allStates.Count == 0)
        {
            Plugin.Instance.Log.LogWarning("[AP] WeatherTrap: no WeatherStateDefinition assets found — trap skipped");
            return;
        }

        // Log available states on first trap fire (helpful for diagnosing StateName values).
        var sb = new System.Text.StringBuilder();
        for (int i = 0; i < allStates.Count; i++)
        {
            if (i > 0) sb.Append(", ");
            sb.Append('\'').Append(allStates[i].StateName ?? allStates[i].name).Append('\'');
        }
        Plugin.Instance.Log.LogInfo($"[AP] WeatherTrap: {allStates.Count} state(s) available: {sb}");

        // Only pick the most impactful weather states:
        // - "Heavy" variants (Thunder Heavy, Rain Heavy, Pollen Heavy)
        // - "Slime Rain" (no intensity modifier; inherently severe)
        // Excluded: Wind (crashes with null ZoneWeatherParameters), Snow (zone-specific),
        //           Light/Medium variants (not impactful enough as a trap).
        var eligible = new System.Collections.Generic.List<WeatherStateDefinition>();
        for (int i = 0; i < allStates.Count; i++)
        {
            var s = allStates[i];
            var sn = (s.StateName ?? s.name) ?? "";
            if (sn.IndexOf("Wind",  System.StringComparison.OrdinalIgnoreCase) >= 0) continue;
            if (sn.IndexOf("Snow",  System.StringComparison.OrdinalIgnoreCase) >= 0) continue;
            if (sn.IndexOf("Light",  System.StringComparison.OrdinalIgnoreCase) >= 0) continue;
            if (sn.IndexOf("Medium", System.StringComparison.OrdinalIgnoreCase) >= 0) continue;
            eligible.Add(s);
        }

        if (eligible.Count == 0)
        {
            Plugin.Instance.Log.LogWarning("[AP] WeatherTrap: no eligible weather states after filtering — trap skipped");
            return;
        }

        // Pick a random state.
        var chosen = eligible[_rng.Next(eligible.Count)];
        _activeWeatherState = chosen;

        // Cache directors once — reused by Tick() expiry / ResetWeather().
        _cachedDirectors = new WeatherDirector[found.Count];
        for (int i = 0; i < found.Count; i++) _cachedDirectors[i] = found[i];

#if DEBUG
        const float durationSeconds = 20f;
#else
        const float durationSeconds = 180f;
#endif
        _weatherResetAt = UnityEngine.Time.time + durationSeconds;

        // On each director: stop all currently running states, then start ours.
        // This integrates with the game's own weather system — WeatherDirector.FixedUpdate()
        // drives the active states, so no per-frame re-application is needed from our side.
        foreach (var director in _cachedDirectors)
        {
            if (director == null) continue;

            // Snapshot current states to avoid modifying the list while iterating it.
            var running = director._runningStates;
            if (running != null && running.Count > 0)
            {
                var toStop = new IWeatherState[running.Count];
                for (int i = 0; i < running.Count; i++) toStop[i] = running[i];
                foreach (var s in toStop)
                    if (s != null) director.StopState(s, null, immediate: true);
            }

            director.RunState(chosen.Cast<IWeatherState>(), null, immediate: false);
        }

        string label = chosen.StateName ?? chosen.name;
        Plugin.Instance.Log.LogInfo($"[AP] WeatherTrap: started '{label}' on {found.Count} director(s) for {durationSeconds}s");
        SlimeRancher2AP.UI.StatusHUD.Instance?.ShowNotification($"Trap: {label} weather for {(int)durationSeconds}s!");
    }

    // -------------------------------------------------------------------------
    // Teleport trap
    // -------------------------------------------------------------------------

    // Confirmed zone SceneGroup ReferenceIds (from DumpSceneGroups, 2026-04-13):
    //   SceneGroup.ConservatoryFields = Home ranch
    //   SceneGroup.LuminousStrand     = Starlight Strand
    //   SceneGroup.RumblingGorge      = Ember Valley
    //   SceneGroup.PowderfallBluffs   = Powderfall Bluffs
    //
    // Entrance node IDs — format: "Teleporter{DestZone}To{SourceZone}Main"
    //   (the node the player ARRIVES at in the destination zone)
    //   TeleporterStrandToFieldsMain      confirmed (Strand dump)
    //   TeleporterLabyrinthToStrandMain   confirmed (Labyrinth dump) — Labyrinth entrance via Strand side
    //   TeleporterGorgeToFieldsMain       confirmed (EV dump)
    //   TeleporterLabyrinthToGorgeMain    confirmed (Labyrinth dump) — Labyrinth entrance via Gorge side
    //   TeleporterBluffsToGorgeMain       confirmed (PB dump) — PB connects to Gorge, not Fields
    //   Home ranch uses Teleport_ResetPlayer; no node ID required.
    //
    // Gadget nodes (TeleporterGrey.*, TeleporterOneWayGadget*) appear in the node dict but are
    // player-placed/one-way gadgets — not used for the trap.

    private readonly struct TrapTeleportDest
    {
        public readonly string   NodeId;         // arrival node ID, or "" = use Teleport_ResetPlayer
        public readonly string   SceneGroupRef;  // SceneGroup.ReferenceId of target zone
        public readonly string[] RegionGates;    // all gates that must be unlocked; empty = always accessible

        public TrapTeleportDest(string nodeId, string sceneGroupRef, params string[] regionGates)
        { NodeId = nodeId; SceneGroupRef = sceneGroupRef; RegionGates = regionGates; }
    }

    private static readonly TrapTeleportDest[] _trapDestinations =
    {
        // Home ranch — always accessible
        new("",                                 "SceneGroup.ConservatoryFields"),
        // Starlight Strand — direct portal from Fields
        new("TeleporterStrandToFieldsMain",     "SceneGroup.LuminousStrand",   "Starlight Strand Access"),                              // confirmed
        // Grey Labyrinth via Strand entrance — requires Strand access
        new("TeleporterLabyrinthToStrandMain",  "SceneGroup.Labyrinth",        "Starlight Strand Access"),                              // confirmed
        // Ember Valley (Rumbling Gorge) — direct portal from Fields
        new("TeleporterGorgeToFieldsMain",      "SceneGroup.RumblingGorge",    "Ember Valley Access"),                                  // confirmed
        // Grey Labyrinth via Gorge entrance — requires EV access
        new("TeleporterLabyrinthToGorgeMain",   "SceneGroup.Labyrinth",        "Ember Valley Access"),                                  // confirmed
        // Powderfall Bluffs — accessed via Gorge; requires both EV and PB unlocked
        new("TeleporterBluffsToGorgeMain",      "SceneGroup.PowderfallBluffs", "Ember Valley Access", "Powderfall Bluffs Access"),      // confirmed
    };

    /// <summary>
    /// Returns true if the named region gate (by AP item name, e.g. "Ember Valley Access") is
    /// considered open. Checks in order: AP save, runtime gate-open events, visited zone inference.
    /// </summary>
    private static bool IsRegionGateOpen(string regionItemName)
        => Plugin.Instance.SaveManager.IsRegionUnlocked(regionItemName)
        || _runtimeOpenRegions.Contains(regionItemName);

    /// <summary>
    /// Teleports the player to a random accessible zone entrance.
    /// Home ranch is always eligible; other zones require their region gate to be unlocked.
    /// Uses <c>TeleportToDestinationImpl</c> for zone entrances and
    /// <c>Teleport_ResetPlayer</c> for the home ranch.
    /// </summary>
    private static void ApplyTeleportTrap()
    {
        var playerGo = SceneContext.Instance?.Player;
        if (playerGo == null)
        {
            Plugin.Instance.Log.LogWarning("[AP] TeleportTrap: no Player in scene — skipped");
            return;
        }

        var teleportable = playerGo.GetComponent<TeleportablePlayer>();
        if (teleportable == null)
        {
            Plugin.Instance.Log.LogWarning("[AP] TeleportTrap: TeleportablePlayer component not found — skipped");
            return;
        }

        var network = UnityEngine.Object.FindObjectOfType<TeleportNetwork>();
        if (network == null)
        {
            Plugin.Instance.Log.LogWarning("[AP] TeleportTrap: TeleportNetwork not in scene — skipped");
            return;
        }

        var allGroups    = Resources.FindObjectsOfTypeAll<SceneGroup>();
        var currentGroup = teleportable.SceneGroup;
        var eligible     = new System.Collections.Generic.List<(TrapTeleportDest dest, SceneGroup sg)>();

#if DEBUG
        Plugin.Instance.Log.LogInfo($"[AP] TeleportTrap: currentGroup='{currentGroup?.ReferenceId ?? "null"}', allGroups={allGroups.Count}");
#endif
        foreach (var dest in _trapDestinations)
        {
            // A destination is eligible if:
            //   (a) all required gates are recorded as open (AP save or gate-open event), OR
            //   (b) the player has physically visited this zone this session — if you can be
            //       there, you can be sent there.
            bool gatesOpen = true;
            string? failedGate = null;
            foreach (var gate in dest.RegionGates)
            {
                if (!IsRegionGateOpen(gate)) { gatesOpen = false; failedGate = gate; break; }
            }
            bool visited = _visitedSceneGroupRefs.Contains(dest.SceneGroupRef);
            if (!gatesOpen && !visited)
            {
#if DEBUG
                Plugin.Instance.Log.LogInfo(
                    $"[AP] TeleportTrap: dest='{dest.SceneGroupRef}' skipped — gate '{failedGate}' locked, not visited");
#endif
                continue;
            }

            SceneGroup? sg = null;
            for (int i = 0; i < allGroups.Count; i++)
                if (allGroups[i].ReferenceId == dest.SceneGroupRef) { sg = allGroups[i]; break; }

            if (sg == null)
            {
                Plugin.Instance.Log.LogWarning($"[AP] TeleportTrap: SceneGroup '{dest.SceneGroupRef}' not found — skipped");
                continue;
            }
#if DEBUG
            Plugin.Instance.Log.LogInfo($"[AP] TeleportTrap: dest='{dest.SceneGroupRef}' eligible (node='{dest.NodeId}')");
#endif
            eligible.Add((dest, sg));
        }

        if (eligible.Count == 0)
        {
            Plugin.Instance.Log.LogWarning("[AP] TeleportTrap: no eligible destinations — skipped");
            return;
        }

        // Prefer a different zone than the current one.
        var nonCurrent = eligible.FindAll(e => e.sg != currentGroup);
        var candidates = nonCurrent.Count > 0 ? nonCurrent : eligible;
        var chosen     = candidates[_rng.Next(candidates.Count)];

        if (string.IsNullOrEmpty(chosen.dest.NodeId))
        {
            network.Teleport_ResetPlayer(teleportable);
            Plugin.Instance.Log.LogInfo("[AP] TeleportTrap: sent to home ranch via Teleport_ResetPlayer");
        }
        else
        {
            network.TeleportToDestinationImpl(teleportable, chosen.dest.NodeId, chosen.sg, forceSceneTransition: true);
            Plugin.Instance.Log.LogInfo(
                $"[AP] TeleportTrap: sent to '{chosen.sg.ReferenceId}' via node '{chosen.dest.NodeId}'");
        }

        SlimeRancher2AP.UI.StatusHUD.Instance?.ShowNotification("Trap: Teleported!");
    }

    // -------------------------------------------------------------------------
    // Slime Rain trap — triggers the Slime Rain weather but overrides spawned slimes to Tarr
    // -------------------------------------------------------------------------

    private static void ApplySlimeRainTrap()
    {
        // Find the Slime Rain WeatherStateDefinition.
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
            Plugin.Instance.Log.LogWarning("[AP] SlimeRainTrap: no 'Slime Rain' WeatherStateDefinition found — falling back to SlimeRingTrap");
            ApplySlimeRingTrap();
            return;
        }

        // Find Tarr IdentifiableType to use as the override.
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
            Plugin.Instance.Log.LogWarning("[AP] SlimeRainTrap: 'Tarr' IdentifiableType not found — running normal Slime Rain weather instead");
        }
        else
        {
            // Walk the weather state's activities and override any SpawnActorActivity to Tarr.
            // Save the originals so ResetWeather() can restore them.
            _savedSlimeRainActorTypes = new System.Collections.Generic.Dictionary<SpawnActorActivity, IdentifiableType>();
            var activities = slimeRainDef.Activities;
            for (int i = 0; i < activities.Count; i++)
            {
                var activity = activities[i].Activity?.TryCast<SpawnActorActivity>();
                if (activity != null && activity.ActorType != null)
                {
                    _savedSlimeRainActorTypes[activity] = activity.ActorType;
                    activity.ActorType = tarrType;
                    Plugin.Instance.Log.LogInfo(
                        $"[AP] SlimeRainTrap: overrode SpawnActorActivity ActorType '{_savedSlimeRainActorTypes[activity].name}' → 'Tarr'");
                }
            }

            if (_savedSlimeRainActorTypes.Count == 0)
                Plugin.Instance.Log.LogWarning("[AP] SlimeRainTrap: no SpawnActorActivity found in Slime Rain weather — Tarr override has no effect");
        }

        // Apply the weather to all directors (same logic as the Weather Change trap).
        var found = Resources.FindObjectsOfTypeAll<WeatherDirector>();
        if (found == null || found.Count == 0)
        {
            Plugin.Instance.Log.LogWarning("[AP] SlimeRainTrap: no WeatherDirector in scene — trap skipped");
            RestoreSlimeRainActorTypes();
            return;
        }

        _activeWeatherState = slimeRainDef;
        _cachedDirectors    = new WeatherDirector[found.Count];
        for (int i = 0; i < found.Count; i++) _cachedDirectors[i] = found[i];

#if DEBUG
        const float durationSeconds = 20f;
#else
        const float durationSeconds = 180f;
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

        Plugin.Instance.Log.LogInfo(
            $"[AP] SlimeRainTrap: started Slime Rain (Tarr override) on {found.Count} director(s) for {durationSeconds}s");
        SlimeRancher2AP.UI.StatusHUD.Instance?.ShowNotification("Trap: Slime Rain!");
    }

    private static void RestoreSlimeRainActorTypes()
    {
        if (_savedSlimeRainActorTypes == null) return;
        foreach (var kvp in _savedSlimeRainActorTypes)
            kvp.Key.ActorType = kvp.Value;
        _savedSlimeRainActorTypes = null;
    }

    private static void ResetWeather()
    {
        if (_cachedDirectors == null || _activeWeatherState == null)
        {
            _cachedDirectors    = null;
            _activeWeatherState = null;
            return;
        }

        var stateAsIWeather = _activeWeatherState.Cast<IWeatherState>();
        foreach (var director in _cachedDirectors)
        {
            if (director == null) continue;
            director.StopState(stateAsIWeather, null, immediate: false);
        }

        string label = _activeWeatherState.StateName ?? _activeWeatherState.name;
        _cachedDirectors    = null;
        _activeWeatherState = null;

        // Restore any SpawnActorActivity overrides made by the Slime Rain trap.
        RestoreSlimeRainActorTypes();

        Plugin.Instance.Log.LogInfo($"[AP] WeatherTrap: stopped '{label}', natural weather cycle resumed");
    }
}
