using Il2CppInterop.Runtime;
using Il2CppMonomiPark.SlimeRancher.UI.Plot;
using SlimeRancher2AP.Data;
using System;
using System.Collections.Generic;
using UnityEngine;

namespace SlimeRancher2AP.Archipelago;

/// <summary>
/// Ranch plot randomization, three independent tiers:
/// <list type="number">
/// <item><b>Plot spots</b> — an empty plot is buildable while its area's built count is below
///   its received "Ranch Plot" count. Blocked at the plot UI (RanchPlotInteractPatch), with a
///   backstop on <c>LandPlotLocation.Replace</c>. Built plots are counted live, so demolishing
///   frees a slot.</item>
/// <item><b>Buildings</b> — each build-menu entry is hidden until its "Plans" item arrives.</item>
/// <item><b>Upgrades</b> — likewise. WALLS and FEEDER exist per building (corral/coop).</item>
/// </list>
/// Counts always come from the server snapshot, cached and invalidated on receipt — never local
/// apply state, which desyncs on reconnect. Tiers 2/3 filter the menu's data source (see that
/// section).
/// </summary>
public static class RanchPlotHandler
{
    // -------------------------------------------------------------------------
    // Enablement
    // -------------------------------------------------------------------------

    private static bool SessionActive =>
        Plugin.Instance.ModEnabled && Plugin.Instance.SaveManager.HasActiveSession;

    public static bool PlotsEnabled     => SessionActive && Plugin.Instance.ApClient.SlotData?.RandomizePlots         == true;
    public static bool BuildingsEnabled => SessionActive && Plugin.Instance.ApClient.SlotData?.RandomizePlotBuildings == true;
    public static bool UpgradesEnabled  => SessionActive && Plugin.Instance.ApClient.SlotData?.RandomizePlotUpgrades  == true;

    // -------------------------------------------------------------------------
    // Static mappings (from docs/dumps/1.2/plot.txt, 2026-07-11)
    // -------------------------------------------------------------------------

    /// <summary>Region cell name (LandPlot._region.name) → per-area plot unlock item.</summary>
    private static readonly Dictionary<string, long> RegionToPlotItem = new()
    {
        ["cellConservatory"]     = ItemTable.RanchPlotConservatory,
        ["cellExpansionGully"]   = ItemTable.RanchPlotGully,
        ["cellExpansionPools"]   = ItemTable.RanchPlotTidepools,
        ["cellExpansionArchway"] = ItemTable.RanchPlotArchway,
        ["cellExpansionDen"]     = ItemTable.RanchPlotDen,
        ["cellExpansionDigsite"] = ItemTable.RanchPlotDigsite,
    };

    private static readonly Dictionary<long, string> PlotItemAreaLabel = new()
    {
        [ItemTable.RanchPlotConservatory] = "Conservatory",
        [ItemTable.RanchPlotGully]        = "The Gully",
        [ItemTable.RanchPlotTidepools]    = "The Tidepools",
        [ItemTable.RanchPlotArchway]      = "The Archway",
        [ItemTable.RanchPlotDen]          = "The Den",
        [ItemTable.RanchPlotDigsite]      = "The Digsite",
    };

    /// <summary>PlotPatchPurchaseItemModel asset name → building Plans item.</summary>
    private static readonly Dictionary<string, long> BuildAssetToItem = new()
    {
        ["Corral Patch"]      = ItemTable.CorralPlans,
        ["Coop Patch"]        = ItemTable.CoopPlans,
        ["Garden Patch"]      = ItemTable.GardenPlans,
        ["Silo Patch"]        = ItemTable.SiloPlans,
        ["Pond Patch"]        = ItemTable.PondPlans,
        ["Incinerator Patch"] = ItemTable.IncineratorPlans,
        // 'Plot Demolish' intentionally absent — demolish is never gated.
    };

    /// <summary>PlotUpgradePurchaseItemModel asset name → upgrade item.</summary>
    private static readonly Dictionary<string, long> UpgradeAssetToItem = new()
    {
        ["Walls Upgrade"]                   = ItemTable.CorralUpgradeWalls,
        ["AirNet Upgrade"]                  = ItemTable.CorralUpgradeAirNet,
        ["MusicBox Upgrade"]                = ItemTable.CorralUpgradeMusicBox,
        ["PlortCollector Upgrade"]          = ItemTable.CorralUpgradePlortCollector,
        ["SolarShield Upgrade"]             = ItemTable.CorralUpgradeSolarShield,
        ["Feeder Upgrade"]                  = ItemTable.CorralUpgradeFeeder,
        ["CoopWalls Upgrade"]               = ItemTable.CoopUpgradeWalls,
        ["CoopFeeder Upgrade"]              = ItemTable.CoopUpgradeFeeder,
        ["DeluxeCoop Upgrade"]              = ItemTable.CoopUpgradeDeluxe,
        ["Soil Upgrade"]                    = ItemTable.GardenUpgradeSoil,
        ["Sprinkler Upgrade"]               = ItemTable.GardenUpgradeSprinkler,
        ["Scareslime Upgrade"]              = ItemTable.GardenUpgradeScareslime,
        ["Vitamizer Upgrade"]               = ItemTable.CoopUpgradeVitamizer,
        ["DeluxeGarden Upgrade"]            = ItemTable.GardenUpgradeDeluxe,
        ["Storage2 Upgrade"]                = ItemTable.SiloUpgradeStorage2,
        ["Storage3 Upgrade"]                = ItemTable.SiloUpgradeStorage3,
        ["Storage4 Upgrade"]                = ItemTable.SiloUpgradeStorage4,
        ["Storage Capacity Upgrade"]        = ItemTable.SiloUpgradeCapacity,
        ["PlortCollectorPond Upgrade"]      = ItemTable.PondUpgradePlortCollector,
        ["AshTrough Upgrade"]               = ItemTable.IncineratorUpgradeAshTrough,
        // The in-game asset really is spelled 'Incerator'.
        ["PlortCollectorIncerator Upgrade"] = ItemTable.IncineratorUpgradePlortCollector,
    };

    /// <summary>An upgrade's AP item; WALLS and FEEDER depend on the building.</summary>
    public static long? UpgradeItemFor(LandPlot.Upgrade upgrade, LandPlot.Id building) => upgrade switch
    {
        LandPlot.Upgrade.WALLS  => building == LandPlot.Id.COOP ? ItemTable.CoopUpgradeWalls  : ItemTable.CorralUpgradeWalls,
        LandPlot.Upgrade.FEEDER => building == LandPlot.Id.COOP ? ItemTable.CoopUpgradeFeeder : ItemTable.CorralUpgradeFeeder,
        LandPlot.Upgrade.AIR_NET                     => ItemTable.CorralUpgradeAirNet,
        LandPlot.Upgrade.MUSIC_BOX                   => ItemTable.CorralUpgradeMusicBox,
        LandPlot.Upgrade.PLORT_COLLECTOR             => ItemTable.CorralUpgradePlortCollector,
        LandPlot.Upgrade.SOLAR_SHIELD                => ItemTable.CorralUpgradeSolarShield,
        LandPlot.Upgrade.DELUXE_COOP                 => ItemTable.CoopUpgradeDeluxe,
        LandPlot.Upgrade.SOIL                        => ItemTable.GardenUpgradeSoil,
        LandPlot.Upgrade.SPRINKLER                   => ItemTable.GardenUpgradeSprinkler,
        LandPlot.Upgrade.SCARESLIME                  => ItemTable.GardenUpgradeScareslime,
        LandPlot.Upgrade.VITAMIZER                   => ItemTable.CoopUpgradeVitamizer,
        LandPlot.Upgrade.DELUXE_GARDEN               => ItemTable.GardenUpgradeDeluxe,
        LandPlot.Upgrade.STORAGE2                    => ItemTable.SiloUpgradeStorage2,
        LandPlot.Upgrade.STORAGE3                    => ItemTable.SiloUpgradeStorage3,
        LandPlot.Upgrade.STORAGE4                    => ItemTable.SiloUpgradeStorage4,
        LandPlot.Upgrade.STORAGE_CAPACITY_INCREASE   => ItemTable.SiloUpgradeCapacity,
        LandPlot.Upgrade.PLORT_COLLECTOR_POND        => ItemTable.PondUpgradePlortCollector,
        LandPlot.Upgrade.ASH_TROUGH                  => ItemTable.IncineratorUpgradeAshTrough,
        LandPlot.Upgrade.PLORT_COLLECTOR_INCINERATOR => ItemTable.IncineratorUpgradePlortCollector,
        _ => null, // NONE / MIRACLE_MIX (no purchase asset) — never gated
    };

    /// <summary>
    /// Vanilla build price, refunded when the Replace backstop blocks a build — the game has
    /// already charged by then.
    /// </summary>
    public static int VanillaBuildCost(LandPlot.Id building) => building switch
    {
        LandPlot.Id.CORRAL      => 250,
        LandPlot.Id.COOP        => 250,
        LandPlot.Id.GARDEN      => 250,
        LandPlot.Id.SILO        => 450,
        LandPlot.Id.POND        => 450,
        LandPlot.Id.INCINERATOR => 450,
        _ => 0,
    };

    /// <summary>Refunds Newbucks without counting toward the newbucks goal.</summary>
    public static void RefundNewbucks(int amount, string reason)
    {
        if (amount <= 0) return;
        var playerState = SceneContext.Instance?.PlayerState;
        var currency = Resources.FindObjectsOfTypeAll<Il2CppMonomiPark.SlimeRancher.Economy.CurrencyDefinition>()
            .FirstOrDefault(c => c.name.Contains("Newbucks", StringComparison.OrdinalIgnoreCase));
        if (playerState == null || currency == null)
        {
            Logger.Warning($"[AP] Could not refund {amount} Newbucks ({reason}) — PlayerState/currency unavailable");
            return;
        }

        ItemHandler.IsGrantingCurrency = true;
        try
        {
            playerState.AddCurrency(currency.Cast<Il2CppMonomiPark.SlimeRancher.Economy.ICurrency>(), amount);
        }
        finally
        {
            ItemHandler.IsGrantingCurrency = false;
        }
        Logger.Info($"[AP] Refunded {amount} Newbucks — {reason}");
    }

    public static long? BuildingItemFor(LandPlot.Id building) => building switch
    {
        LandPlot.Id.CORRAL      => ItemTable.CorralPlans,
        LandPlot.Id.COOP        => ItemTable.CoopPlans,
        LandPlot.Id.GARDEN      => ItemTable.GardenPlans,
        LandPlot.Id.SILO        => ItemTable.SiloPlans,
        LandPlot.Id.POND        => ItemTable.PondPlans,
        LandPlot.Id.INCINERATOR => ItemTable.IncineratorPlans,
        _ => null, // EMPTY / NONE
    };

    // -------------------------------------------------------------------------
    // Received-item counts — server snapshot is the source of truth
    // -------------------------------------------------------------------------

    private static readonly Dictionary<long, int> _serverCounts = new();
    private static readonly Dictionary<long, int> _debugCounts  = new(); // F9 grants, no session
    private static bool _dirty = true;

    public static void MarkDirty() => _dirty = true;

    public static void AddDebugCount(long itemId)
        => _debugCounts[itemId] = _debugCounts.GetValueOrDefault(itemId) + 1;

    /// <summary>On disconnect: clears state and restores the vanilla menus.</summary>
    public static void Reset()
    {
        _serverCounts.Clear();
        _debugCounts.Clear();
        _dirty = true;
        RestoreCategories();
    }

    public static int ReceivedCount(long itemId)
    {
        EnsureCounts();
        return _serverCounts.GetValueOrDefault(itemId) + _debugCounts.GetValueOrDefault(itemId);
    }

    public static bool HasItem(long itemId) => ReceivedCount(itemId) > 0;

    private static void EnsureCounts()
    {
        if (!_dirty) return;

        var snapshot = Plugin.Instance.ApClient.Session?.Items?.AllItemsReceived;
        if (snapshot == null) return; // retry once a session exists

        _serverCounts.Clear();
        for (int i = 0; i < snapshot.Count; i++)
        {
            long id = snapshot[i].ItemId;
            if (id < ItemTable.RanchPlotConservatory || id > ItemTable.IncineratorUpgradePlortCollector)
                continue;
            _serverCounts[id] = _serverCounts.GetValueOrDefault(id) + 1;
        }
        _dirty = false;
    }

    // -------------------------------------------------------------------------
    // Tier 1 — plot access gate
    // -------------------------------------------------------------------------

    /// <summary>
    /// True when the empty plot may be built on; otherwise <paramref name="blockMessage"/> says
    /// why. Unknown areas fail open, so a new ranch area isn't bricked.
    /// </summary>
    public static bool CanBuildOnPlot(LandPlot plot, out string blockMessage)
    {
        blockMessage = "";

        string? regionCell = null;
        try { regionCell = plot._region?.name; } catch { }
        if (regionCell == null || !RegionToPlotItem.TryGetValue(regionCell, out var itemId))
            return true;

        int unlocked = ReceivedCount(itemId);
        int built    = CountBuiltPlotsInRegion(regionCell);
        if (built < unlocked) return true;

        string area = PlotItemAreaLabel.GetValueOrDefault(itemId, regionCell);
        blockMessage =
            $"AP: {area} plots in use: {built}/{unlocked} unlocked - " +
            $"find more \"Ranch Plot: {area}\" items (or demolish a building)";
        return false;
    }

    /// <summary>Built plots in the area, scene objects only (prefabs excluded).</summary>
    private static int CountBuiltPlotsInRegion(string regionCell)
    {
        int built = 0;
        foreach (var p in Resources.FindObjectsOfTypeAll<LandPlot>())
        {
            if (p == null) continue;
            try
            {
                if (!p.gameObject.scene.IsValid()) continue;
                if (p._region?.name != regionCell) continue;
                var t = p.TypeId;
                if (t != LandPlot.Id.EMPTY && t != LandPlot.Id.NONE) built++;
            }
            catch { /* stale IL2CPP object mid-unload */ }
        }
        return built;
    }

    // -------------------------------------------------------------------------
    // Tiers 2/3 — purchase-menu data-source filtering
    // -------------------------------------------------------------------------
    // Locked models are removed from PlotPurchaseCategory.items and restored when their item
    // arrives. Not by wrapping IsAvailable (renders "already purchased") or IsHidden (corrupts
    // the pooled list views while the menu is open).

    private sealed class CategoryState
    {
        public PlotPurchaseCategory Category = null!;
        public List<PlotPurchaseItemModel> OriginalItems = new(); // original order
        public int[] OriginalIds = System.Array.Empty<int>();     // instance ids, same order
        public PlotPurchaseItemModel? OriginalInitialSelection;
        public ulong AppliedMask = ulong.MaxValue;                // bit i = original item i locked
    }

    private static List<CategoryState>? _categories;
    private static int _tickCounter;

    /// <summary>Per-frame; every ~30 frames re-filters categories whose locked set changed.</summary>
    public static void Tick()
    {
        if (!BuildingsEnabled && !UpgradesEnabled) return;
        if (++_tickCounter < 30) return;
        _tickCounter = 0;

        try
        {
            _categories ??= SnapshotCategories();
            List<CategoryState>? stale = null;
            foreach (var state in _categories)
                if (!ApplyCategoryFilter(state))
                    (stale ??= new List<CategoryState>()).Add(state);
            if (stale != null)
                RefreshStaleWrappers(stale);
        }
        catch
        {
            _categories = null;   // assets unloaded — refind next tick
        }
    }

    /// <summary>Re-resolves stale cached wrappers from live assets by instance id.</summary>
    private static void RefreshStaleWrappers(List<CategoryState> stale)
    {
        var byId = new Dictionary<int, PlotPurchaseItemModel>();
        foreach (var m in Resources.FindObjectsOfTypeAll<PlotPurchaseItemModel>())
        {
            if (m == null) continue;
            try { byId[m.GetInstanceID()] = m; } catch { }
        }

        foreach (var state in stale)
        {
            int refreshed = 0;
            for (int i = 0; i < state.OriginalItems.Count && i < state.OriginalIds.Length; i++)
            {
                if (byId.TryGetValue(state.OriginalIds[i], out var fresh))
                {
                    state.OriginalItems[i] = fresh;
                    refreshed++;
                }
            }
            state.AppliedMask = ulong.MaxValue; // force a re-apply next tick

            string catName = "(unreadable)";
            try { catName = state.Category.name; } catch { }
            Logger.Info(
                $"[AP] Plot menu category '{catName}': refreshed {refreshed}/{state.OriginalItems.Count} " +
                "model wrappers from live assets");
        }
    }

    private static void RestoreCategories()
    {
        if (_categories == null) return;
        foreach (var state in _categories)
        {
            try
            {
                var list = state.Category.items;
                if (list == null) continue;
                list.Clear();
                foreach (var m in state.OriginalItems) list.Add(m);
                state.Category.initialSelection = state.OriginalInitialSelection;
            }
            catch { /* asset gone */ }
        }
        _categories = null;
    }

    private static List<CategoryState> SnapshotCategories()
    {
        var result = new List<CategoryState>();
        foreach (var cat in Resources.FindObjectsOfTypeAll<PlotPurchaseCategory>())
        {
            if (cat == null || cat.items == null) continue;
            var state = new CategoryState
            {
                Category = cat,
                OriginalInitialSelection = cat.initialSelection,
            };
            for (int i = 0; i < cat.items.Count; i++)
            {
                var m = cat.items[i];
                state.OriginalItems.Add(m);

                // CRITICAL: pin before removing — the list is the asset's last native reference,
                // and Resources.UnloadUnusedAssets would destroy it for good. Cached interop
                // wrappers don't keep it alive.
                try { if (m != null) m.hideFlags |= HideFlags.DontUnloadUnusedAsset; } catch { }
            }
            if (state.OriginalItems.Count == 0) continue;

            state.OriginalIds = new int[state.OriginalItems.Count];
            for (int i = 0; i < state.OriginalItems.Count; i++)
                try { state.OriginalIds[i] = state.OriginalItems[i]?.GetInstanceID() ?? 0; } catch { }

            result.Add(state);
        }
        Logger.Info(
            $"[AP] RanchPlotHandler: filtering {result.Count} plot purchase categor(ies) " +
            "(models pinned against asset unload)");
        return result;
    }

    /// <summary>The building from the category name (for WALLS/FEEDER).</summary>
    private static LandPlot.Id BuildingForCategory(string categoryName)
    {
        if (categoryName.StartsWith("Corral"))      return LandPlot.Id.CORRAL;
        if (categoryName.StartsWith("Coop"))        return LandPlot.Id.COOP;
        if (categoryName.StartsWith("Garden"))      return LandPlot.Id.GARDEN;
        if (categoryName.StartsWith("Silo"))        return LandPlot.Id.SILO;
        if (categoryName.StartsWith("Pond"))        return LandPlot.Id.POND;
        if (categoryName.StartsWith("Incinerator")) return LandPlot.Id.INCINERATOR;
        return LandPlot.Id.NONE; // 'Empty Plot Purchases' (building patches, not upgrades)
    }

    /// <summary>
    /// The AP item gating a menu model, or null if ungated. Read from the model's data, not its
    /// name — the menu's per-instance names don't match the assets. <paramref name="why"/> is for logs.
    /// </summary>
    private static long? GateItemFor(PlotPurchaseItemModel? model, LandPlot.Id categoryBuilding,
                                     out string why)
    {
        if (model == null) { why = "model null"; return null; }

        try
        {
            var patch = model.TryCast<PlotPatchPurchaseItemModel>();
            if (patch != null)
            {
                if (!BuildingsEnabled) { why = "buildings tier off"; return null; }
                var type = patch._plotDefinition?.Type ?? LandPlot.Id.NONE;
                why = $"patch type={type}";
                return BuildingItemFor(type); // null for demolish
            }

            var upgrade = model.TryCast<PlotUpgradePurchaseItemModel>();
            if (upgrade != null)
            {
                if (!UpgradesEnabled) { why = "upgrades tier off"; return null; }
                var u = upgrade._upgrade;
                why = $"upgrade={u} building={categoryBuilding}";
                return UpgradeItemFor(u, categoryBuilding);
            }

            why = "not a patch/upgrade model";
            return null; // clear crop etc.
        }
        catch (Exception ex)
        {
            // Stale wrapper; the caller detects an all-ungated pass and re-snapshots.
            why = $"read failed: {ex.GetType().Name}";
            return null;
        }
    }

    /// <summary>Applies the lock filter; false means the cached wrappers look stale.</summary>
    private static bool ApplyCategoryFilter(CategoryState state)
    {
        var building = BuildingForCategory(state.Category.name);

        // Locked bitmask over the original order; touch the live list only when it changes.
        ulong mask = 0;
        int gatesResolved = 0;
        for (int i = 0; i < state.OriginalItems.Count && i < 64; i++)
        {
            var gate = GateItemFor(state.OriginalItems[i], building, out _);
            if (!gate.HasValue) continue;
            gatesResolved++;
            if (!HasItem(gate.Value))
                mask |= 1UL << i;
        }
        if (mask == state.AppliedMask) return true;

        // Had gates before, resolves none now: the wrappers went stale after asset churn.
        // Rebuilding from them would unhide everything.
        bool hadGates = state.AppliedMask != ulong.MaxValue && state.AppliedMask != 0;
        if (gatesResolved == 0 && hadGates)
        {
            Logger.Warning(
                $"[AP] Plot menu category '{state.Category.name}': cached entries no longer " +
                "resolve any gate — treating snapshot as stale and rebuilding");
            LogCategoryDiagnostics(state, building);
            return false;
        }

        var list = state.Category.items;
        if (list == null) return true;
        list.Clear();
        PlotPurchaseItemModel? firstVisible = null;
        for (int i = 0; i < state.OriginalItems.Count; i++)
        {
            if (i < 64 && (mask & (1UL << i)) != 0) continue;
            list.Add(state.OriginalItems[i]);
            firstVisible ??= state.OriginalItems[i];
        }

        // Don't leave the initial selection on a removed model.
        var initial = state.OriginalInitialSelection;
        var initialGate = GateItemFor(initial, building, out _);
        state.Category.initialSelection =
            (initialGate.HasValue && !HasItem(initialGate.Value)) ? firstVisible : initial;

        state.AppliedMask = mask;
        Logger.Info(
            $"[AP] Plot menu category '{state.Category.name}': {list.Count}/{state.OriginalItems.Count} entries visible");
        LogCategoryDiagnostics(state, building);
        return true;
    }

    /// <summary>Per-entry gate resolution, logged on every mask change.</summary>
    private static void LogCategoryDiagnostics(CategoryState state, LandPlot.Id building)
    {
        foreach (var m in state.OriginalItems)
        {
            var gate = GateItemFor(m, building, out var why);
            string mName = "(unreadable)";
            try { mName = m?.name ?? "(null)"; } catch { }
            string status = gate.HasValue
                ? $"item {gate.Value} owned={HasItem(gate.Value)} (n={ReceivedCount(gate.Value)})"
                : "not gated";
            Logger.Info($"[AP]     '{mName}' → {status} [{why}]");
        }
    }
}
