using Il2CppInterop.Runtime;
using Il2CppMonomiPark.SlimeRancher.UI.Plot;
using SlimeRancher2AP.Data;
using System;
using System.Collections.Generic;
using UnityEngine;

namespace SlimeRancher2AP.Archipelago;

/// <summary>
/// Ranch plot randomization — three independently-toggled tiers of AP items
/// (see the apworld options <c>randomize_plots</c> / <c>randomize_plot_buildings</c> /
/// <c>randomize_plot_upgrades</c>):
/// <list type="number">
/// <item><b>Plot spots</b> — an EMPTY plot can only be built on while the number of built
///   plots in its ranch area is below the received "Ranch Plot" item count for that area.
///   Enforced by blocking the plot UI from opening (RanchPlotInteractPatch) plus a
///   belt-and-braces block on <c>LandPlotLocation.Replace</c>. Demolishing frees a slot
///   naturally, since built plots are counted live.</item>
/// <item><b>Building types</b> — the six <c>PlotPatchPurchaseItemModel</c> menu entries are
///   removed from the build menu until their "... Plans" item arrives.</item>
/// <item><b>Upgrades</b> — the 21 <c>PlotUpgradePurchaseItemModel</c> menu assets likewise.
///   Keyed by ASSET NAME, not <c>LandPlot.Upgrade</c>: WALLS and FEEDER exist once per
///   building (corral 'Walls Upgrade' vs coop 'CoopWalls Upgrade').</item>
/// </list>
/// <para>
/// Item counts are always derived from the AP server snapshot
/// (<c>Session.Items.AllItemsReceived</c>), NEVER from replay/apply state — Useful-class
/// items are ephemeral-guarded and skipped on replay, so a locally tracked count would
/// desync on reconnect. The snapshot recount is cached and invalidated on item receipt.
/// </para>
/// <para>
/// Tiers 2/3 are enforced by filtering the menu's DATA SOURCE — locked purchase models
/// are removed from the <c>PlotPurchaseCategory.items</c> lists and restored when their
/// item arrives (<see cref="Tick"/>). See the comment on that section for why the
/// delegate-wrapping approaches (IsAvailable / IsHidden) were abandoned.
/// </para>
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
    // Static mappings (from docs/dumps/plot.txt, 2026-07-11)
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

    /// <summary>
    /// Resolves an upgrade purchase to its AP item for the hard-enforcement patch on
    /// <c>LandPlotUIRoot.Upgrade</c>, which only sees the enum + the plot being upgraded.
    /// WALLS and FEEDER are per-building; everything else maps 1:1.
    /// </summary>
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
    /// Vanilla Newbucks price of building each plot type (docs/dumps/plot.txt). Used to
    /// refund the player when the Replace backstop blocks a build — the game charges
    /// BEFORE LandPlotLocation.Replace runs, so a block would otherwise eat the money
    /// (player-reported).
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

    /// <summary>Refunds Newbucks without counting toward the newbucks goal accumulator.</summary>
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

    /// <summary>Invalidate the cached snapshot counts (item received / connected).</summary>
    public static void MarkDirty() => _dirty = true;

    /// <summary>Debug-panel grant without an AP session (ItemHandler.ApplyById path).</summary>
    public static void AddDebugCount(long itemId)
        => _debugCounts[itemId] = _debugCounts.GetValueOrDefault(itemId) + 1;

    /// <summary>Called on disconnect — clears all cached state and restores the vanilla
    /// plot menu item lists.</summary>
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
        if (snapshot == null) return; // keep _dirty — retry once a session exists

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
    /// True when building on <paramref name="plot"/> (an EMPTY plot) is currently allowed.
    /// When blocked, <paramref name="blockMessage"/> explains the shortfall for the HUD.
    /// Unknown region cells are never blocked (fail open — a future game update adding a
    /// ranch area must not brick building there).
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

    /// <summary>
    /// Live count of non-empty plots in a region cell. Scene-bound objects only —
    /// FindObjectsOfTypeAll also returns the corral/garden/... prefabs, which must not
    /// inflate the count. Only called on plot interaction, so the area is loaded.
    /// </summary>
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
    // The plot menu builds its list from PlotPurchaseCategory.items (a List on the
    // ScriptableObject asset). Locked models are REMOVED from that list and restored
    // when their item arrives, so the menu opens with a clean, correctly-bound list.
    //
    // Two earlier delegate-based approaches failed (player-tested):
    //  • Wrapping IsAvailable rendered locked entries as "already purchased".
    //  • Wrapping IsHidden after the menu had bound corrupted the pooled list views —
    //    entries went visually stale (every slot showing the first item's title/cost) —
    //    and left a payment window where a locked purchase took money before the
    //    LandPlotLocation.Replace backstop blocked the build.
    // Filtering the data source has neither problem: nothing locked is ever offered,
    // so no money can be spent on it, and the game's own bind path runs untouched.

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

    /// <summary>
    /// Called every frame from <c>Plugin.Update</c>; does work every ~30 frames.
    /// Rebuilds each plot purchase category's item list when the locked-set changes
    /// (first run, or an AP item arrived).
    /// </summary>
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
            // Assets collected (Resources.UnloadUnusedAssets) — refind on the next tick.
            _categories = null;
        }
    }

    /// <summary>
    /// Replaces a stale category's cached model wrappers with fresh ones resolved from
    /// the live asset set (matched by instance id — stable for the same native object).
    /// The originals list, not the (filtered) live list, is rebuilt, so removed entries
    /// are recoverable; the next tick re-applies the filter with working wrappers.
    /// </summary>
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

    /// <summary>Restores the vanilla item lists (called from <see cref="Reset"/>).</summary>
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
            catch { /* asset gone — nothing to restore */ }
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

                // CRITICAL: pin every model against the unused-asset sweep. The category
                // list is these assets' only native reference — removing a locked model
                // from it let Resources.UnloadUnusedAssets DESTROY the asset a few
                // seconds after scene load (player log: every removed entry read back
                // as '[model null]' while every kept entry survived), after which the
                // menu rendered garbage and the filter could never restore the entry.
                // Interop wrappers hold weak handles, so caching them does not protect.
                try { if (m != null) m.hideFlags |= HideFlags.DontUnloadUnusedAsset; } catch { }
            }
            if (state.OriginalItems.Count == 0) continue;

            state.OriginalIds = new int[state.OriginalItems.Count];
            for (int i = 0; i < state.OriginalItems.Count; i++)
                try { state.OriginalIds[i] = state.OriginalItems[i]?.GetInstanceID() ?? 0; } catch { }

            result.Add(state);
        }
        // Per-entry gate diagnostics follow from the first ApplyCategoryFilter pass.
        Logger.Info(
            $"[AP] RanchPlotHandler: filtering {result.Count} plot purchase categor(ies) " +
            "(models pinned against asset unload)");
        return result;
    }

    /// <summary>
    /// The building a category's upgrade entries belong to, from the category asset name
    /// (e.g. 'Coop Plot Purchases'). Needed because WALLS/FEEDER enum values are shared
    /// between corral and coop.
    /// </summary>
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
    /// AP item gating this menu model, or null when the model is never gated (demolish,
    /// clear crop, or a tier whose option is off). Keyed on the models' own DATA —
    /// <c>_plotDefinition.Type</c> / <c>_upgrade</c> — not on asset names: the category
    /// lists reference per-menu model instances whose names don't match the loose assets
    /// (player-tested: name matching resolved nothing and every entry stayed visible).
    /// <paramref name="why"/> explains the resolution for the mask-change diagnostics.
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
                return BuildingItemFor(type); // null for demolish (no plot definition)
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
            return null; // clear crop / unknown model types — never gated
        }
        catch (Exception ex)
        {
            // A throwing read means the cached wrapper went stale (asset churn) — the
            // caller treats an all-ungated pass as stale and re-snapshots.
            why = $"read failed: {ex.GetType().Name}";
            return null;
        }
    }

    /// <summary>
    /// Applies the lock filter; returns false when the category's cached wrappers look
    /// stale (see below) and the whole snapshot should be rebuilt.
    /// </summary>
    private static bool ApplyCategoryFilter(CategoryState state)
    {
        var building = BuildingForCategory(state.Category.name);

        // Compute the locked bitmask over the ORIGINAL item order; only touch the
        // live list when it actually changed (menu-open reads are then always clean).
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

        // Stale-wrapper guard: this category previously had gated entries, but now NONE
        // of its entries resolves a gate at all (as opposed to resolving and being
        // unlocked). That pattern appeared in testing ~4s after a correct first filter —
        // the cached model wrappers stopped resolving after scene-load asset churn —
        // and rebuilding the list from them would unhide everything (and re-add
        // possibly-dead references). Re-snapshot from live assets instead.
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

        // The category's initial selection may point at a removed model — retarget it
        // to the first visible entry so the menu doesn't try to select a missing item.
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

    /// <summary>Per-entry gate resolution — logged on every mask change so a wrong
    /// visibility report always comes with the reason.</summary>
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
