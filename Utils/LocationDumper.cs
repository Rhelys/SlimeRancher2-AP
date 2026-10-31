#if DEBUG
using Il2CppInterop.Runtime;
using Il2CppMonomiPark.SlimeRancher;
using Il2CppMonomiPark.SlimeRancher.Dialogue.CommStation;
using Il2CppMonomiPark.SlimeRancher.Dialogue.ResearchDrone;
using Il2CppMonomiPark.SlimeRancher.Event.Query;
using Il2CppMonomiPark.SlimeRancher.Pedia;
using Il2CppMonomiPark.SlimeRancher.RecipePinning;
using Il2CppMonomiPark.SlimeRancher.UI.Map;
using Il2CppMonomiPark.SlimeRancher.World;
using Il2CppMonomiPark.SlimeRancher.Slime;
using Il2CppMonomiPark.SlimeRancher.Labyrinth;
using Il2CppMonomiPark.SlimeRancher.DataModel;
using Il2CppMonomiPark.SlimeRancher.Weather;
using Il2CppMonomiPark.SlimeRancher.Drone;
using Il2CppMonomiPark.SlimeRancher.Shop;
using Il2CppMonomiPark.SlimeRancher.Shop.Runtime;
using Il2CppMonomiPark.SlimeRancher.UI.Shop.Pages;
using Il2CppMonomiPark.SlimeRancher.World.ResearchDrone;
using SlimeRancher2AP.Data;
using SlimeRancher2AP.Utils;
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace SlimeRancher2AP.Utils;

/// <summary>
/// Development dumps (F9 debug panel) that log game data for building the location tables.
/// SR2 streams zones, so most dumps only see the zone you stand in — run once per zone.
/// </summary>
public static class LocationDumper
{
    public static void DumpAll()
    {
        var log = Plugin.Instance.Log;
        log.LogInfo("[AP-Dump] ========== LOCATION DUMP START ==========");

        DumpTreasurePods(log);   // also separates out nodeComponentAcqDrone (GhostlyDrone nodes)
        DumpGordos(log);
        DumpMapNodes(log);
        DumpShadowPlortDoors(log);
        DumpResearchDrones(log);
        DumpSwitches(log);
        DumpSlimeGates(log);

        log.LogInfo("[AP-Dump] =========== LOCATION DUMP END ===========");
    }

    /// <summary>Logs every Slimepedia entry: name, display title, unlocked state.</summary>
    public static void DumpPedia()
    {
        var log = Plugin.Instance.Log;
        var pedia = SceneContext.Instance?.PediaDirector;
        if (pedia == null)
        {
            log.LogWarning("[AP-Dump] PediaDirector not available — load a save first");
            return;
        }

        // One PediaCategory per Slimepedia tab.
        var categories = Resources.FindObjectsOfTypeAll<PediaCategory>()
            .OrderBy(c => c.name)
            .ToList();

        log.LogInfo($"[AP-Dump] ========== PEDIA DUMP ({categories.Count} categories) ==========");
        foreach (var category in categories)
        {
            var items = category._items;
            int count = items?.Length ?? 0;
            log.LogInfo($"[AP-Dump] ── Category '{category.name}' ({count} entries) ──");
            if (items == null) continue;

            foreach (var entry in items)
            {
                if (entry == null) continue;
                bool unlocked = pedia.IsUnlocked(entry);
                string title = "<no title>";
                try { title = entry.Title?.GetLocalizedString() ?? "<null>"; }
                catch { title = "<error>"; }
                log.LogInfo($"[AP-Dump]   entry='{entry.name}'  title='{title}'  unlocked={unlocked}");
            }
        }
        log.LogInfo("[AP-Dump] =========== PEDIA DUMP END ===========");
    }

    /// <summary>
    /// Logs every rancher conversation with a gift page, as rancher → conversation → page type
    /// → gifted asset. Then lists standalone conversations triggered by world events.
    /// </summary>
    public static void DumpConversations()
    {
        var log = Plugin.Instance.Log;

        var rancherDefs = Resources.FindObjectsOfTypeAll<RancherDefinition>()
            .OrderBy(r => r.name)
            .ToList();

        log.LogInfo($"[AP-Dump] ========== CONVERSATION DUMP ({rancherDefs.Count} RancherDefinitions) ==========");

        foreach (var rancher in rancherDefs)
        {
            var rancherName = rancher.name;
            var providers = rancher._conversationProviders;
            if (providers == null)
            {
                log.LogInfo($"[AP-Dump] Rancher '{rancherName}' — _conversationProviders NULL");
                continue;
            }

            int provCount = providers.Count;
            log.LogInfo($"[AP-Dump] ── Rancher '{rancherName}' ({provCount} provider(s)) ──");

            for (int p = 0; p < provCount; p++)
            {
                var provider = providers[p];
                if (provider == null) { log.LogInfo($"[AP-Dump]   [prov {p}] NULL"); continue; }

                string providerId = "(err)";
                try { providerId = provider.GetId() ?? "(null)"; } catch { /* ignore */ }

                Il2CppSystem.Collections.Generic.List<IConversation>? convList = null;
                try { convList = provider.GetDebugConversationList(); } catch { /* some providers may throw */ }

                if (convList == null)
                {
                    log.LogInfo($"[AP-Dump]   [prov {p}] id='{providerId}'  — GetDebugConversationList() returned null");
                    continue;
                }

                for (int c = 0; c < convList.Count; c++)
                {
                    var conv = convList[c];
                    if (conv == null) continue;

                    string convId      = "(err)";
                    string convDebug   = "";
                    try { convId    = conv.GetId()        ?? "(null)"; } catch { /* ignore */ }
                    try { convDebug = conv.GetDebugName() ?? "";       } catch { /* ignore */ }

                    // Via FixedConversation's typed list — GetPages()'s Il2Cpp enumerator has no
                    // usable MoveNext().
                    var gifts = new System.Collections.Generic.List<string>();
                    var fixedConv = conv.TryCast<FixedConversation>();
                    var pageList  = fixedConv?.pages;

                    if (pageList != null)
                    {
                        for (int pg = 0; pg < pageList.Count; pg++)
                        {
                            var page = pageList[pg];
                            if (page == null) continue;

                            var giftBP = page.TryCast<ConversationPageGiftBlueprint>();
                            if (giftBP != null)
                            {
                                gifts.Add($"GiftBlueprint:'{giftBP.gadget?.name ?? "(null)"}'  noticeEvent='{giftBP.noticeEvent?.name ?? "(null)"}'");
                                continue;
                            }
                            var giftGadget = page.TryCast<ConversationPageGiftGadget>();
                            if (giftGadget != null)
                            {
                                gifts.Add($"GiftGadget:'{giftGadget.gadget?.name ?? "(null)"}'");
                                continue;
                            }
                            var giftComp = page.TryCast<ConversationPageGiftUpgradeComponent>();
                            if (giftComp != null)
                            {
                                gifts.Add($"GiftUpgradeComponent:'{giftComp.upgradeComponent?.name ?? "(null)"}'");
                                continue;
                            }
                        }
                    }

                    string debugSuffix = string.IsNullOrEmpty(convDebug) ? "" : $"  debug='{convDebug}'";
                    if (gifts.Count > 0)
                        log.LogInfo($"[AP-Dump]   conv[{c}] RANCHER='{rancherName}'  id='{convId}'{debugSuffix}  GIFTS: {string.Join(" | ", gifts)}");
                    else
                        log.LogInfo($"[AP-Dump]   conv[{c}] RANCHER='{rancherName}'  id='{convId}'{debugSuffix}");
                }
            }
        }

        // Second pass: conversations fired by world events, not a rancher's queue
        // (e.g. ViktorIntroCall, BObGift1Intro).
        var coveredNames = new System.Collections.Generic.HashSet<string>(StringComparer.Ordinal);
        foreach (var rancher2 in rancherDefs)
        {
            var providers2 = rancher2._conversationProviders;
            if (providers2 == null) continue;
            for (int p = 0; p < providers2.Count; p++)
            {
                var prov2 = providers2[p];
                if (prov2 == null) continue;
                Il2CppSystem.Collections.Generic.List<IConversation>? list2 = null;
                try { list2 = prov2.GetDebugConversationList(); } catch { }
                if (list2 == null) continue;
                for (int c = 0; c < list2.Count; c++)
                {
                    var conv2 = list2[c];
                    if (conv2 == null) continue;
                    string n = "";
                    try { n = conv2.GetDebugName() ?? ""; } catch { }
                    if (!string.IsNullOrEmpty(n)) coveredNames.Add(n);
                }
            }
        }

        var allFixed = Resources.FindObjectsOfTypeAll<FixedConversation>()
            .OrderBy(c => c.name)
            .ToList();

        var standalone = allFixed.Where(c => c != null && !string.IsNullOrEmpty(c.name)
                                              && !coveredNames.Contains(c.name)).ToList();

        if (standalone.Count > 0)
        {
            log.LogInfo($"[AP-Dump] ── Standalone FixedConversations not in any provider ({standalone.Count}) ──");
            foreach (var sc in standalone)
            {
                var gifts2 = new System.Collections.Generic.List<string>();
                var pages2 = sc.pages;
                if (pages2 != null)
                {
                    for (int pg = 0; pg < pages2.Count; pg++)
                    {
                        var page = pages2[pg];
                        if (page == null) continue;
                        var giftBP2 = page.TryCast<ConversationPageGiftBlueprint>();
                        if (giftBP2 != null) { gifts2.Add($"GiftBlueprint:'{giftBP2.gadget?.name ?? "(null)"}'"); continue; }
                        var giftGadget2 = page.TryCast<ConversationPageGiftGadget>();
                        if (giftGadget2 != null) { gifts2.Add($"GiftGadget:'{giftGadget2.gadget?.name ?? "(null)"}'"); continue; }
                        var giftComp2 = page.TryCast<ConversationPageGiftUpgradeComponent>();
                        if (giftComp2 != null) { gifts2.Add($"GiftUpgradeComponent:'{giftComp2.upgradeComponent?.name ?? "(null)"}'"); continue; }
                    }
                }
                string giftStr = gifts2.Count > 0 ? $"  GIFTS: {string.Join(" | ", gifts2)}" : "";
                log.LogInfo($"[AP-Dump]   standalone  debug='{sc.name}'  id='{sc.Guid}'{giftStr}");
            }
        }
        else
        {
            log.LogInfo("[AP-Dump] ── Standalone FixedConversations: none (all covered by providers) ──");
        }

        log.LogInfo("[AP-Dump] =========== CONVERSATION DUMP END ===========");
    }

    /// <summary>
    /// Dumps the Prismacore prerequisites: the Nullifier blueprint, every DiscordantWall, and the
    /// Harmonizer bells. (GigiCore_* conversations carry no serialized conditions, so the room
    /// is the only place to look.) Diff a run before and after ringing a bell.
    /// </summary>
    public static void DumpPrismacoreState()
    {
        var log = Plugin.Instance.Log;
        log.LogInfo("[AP-Dump] ========== PRISMACORE STATE DUMP ==========");

        // --- Nullifier (asset name 'Harmonizer', pedia title 'Nullifier') ---
        try
        {
            var def = UnityEngine.Resources.FindObjectsOfTypeAll<GadgetDefinition>()
                                 .FirstOrDefault(g => g != null && g.name == "Harmonizer");
            log.LogInfo($"[AP-Dump] Nullifier GadgetDefinition ('Harmonizer'): "
                        + (def == null ? "NOT FOUND" : "found"));
        }
        catch (Exception ex) { log.LogWarning($"[AP-Dump] Nullifier lookup failed: {ex.Message}"); }

        // --- Discordant walls (what the Nullifier dispels) ---
        try
        {
            var walls = UnityEngine.Resources.FindObjectsOfTypeAll<DiscordantWall>();
            log.LogInfo($"[AP-Dump] DiscordantWall count={walls.Length}");
            for (int i = 0; i < walls.Length && i < 40; i++)
            {
                var w = walls[i];
                if (w == null) continue;
                string lvl = "?", cur = "?", tgt = "?";
                try { lvl = w._currentLevel.ToString(); } catch { }
                try { cur = w._currentHarmonizationAmount.ToString("0.##"); } catch { }
                try { tgt = w._targetHarmonizationAmount.ToString("0.##"); } catch { }
                log.LogInfo($"[AP-Dump]   wall '{w.gameObject?.name ?? "?"}'  level={lvl}  "
                            + $"harmonization={cur}/{tgt}  scene={w.gameObject?.scene.name ?? "-"}");
            }
        }
        catch (Exception ex) { log.LogWarning($"[AP-Dump] DiscordantWall scan failed: {ex.Message}"); }

        // --- The Harmonizer bells: WorldStateInvisibleSwitch objects named 'labySwitchBell' ---
        try
        {
            var switches = UnityEngine.Resources.FindObjectsOfTypeAll<WorldStateInvisibleSwitch>();
            log.LogInfo($"[AP-Dump] WorldStateInvisibleSwitch count={switches.Length}");
            int bells = 0, bellsDown = 0;
            for (int i = 0; i < switches.Length; i++)
            {
                var sw = switches[i];
                if (sw == null) continue;
                string nm = "?", scene = "-", state = "no model";
                try { nm = sw.gameObject?.name ?? "?"; } catch { }
                try { var sc = sw.gameObject.scene; scene = sc.IsValid() ? (sc.name ?? "-") : "-"; } catch { }
                try { var m = sw._model; if (m != null) state = m.state.ToString(); } catch { }

                bool isBell = nm.IndexOf("Bell", StringComparison.OrdinalIgnoreCase) >= 0;
                if (isBell)
                {
                    bells++;
                    if (state == "DOWN") bellsDown++;
                }
                log.LogInfo($"[AP-Dump]   switch '{nm}'  state={state}  scene={scene}"
                            + (isBell ? "   <-- BELL" : ""));
            }
            log.LogInfo($"[AP-Dump] bells loaded={bells}  down={bellsDown}");
        }
        catch (Exception ex) { log.LogWarning($"[AP-Dump] Switch scan failed: {ex.Message}"); }

        log.LogInfo("[AP-Dump] =========== PRISMACORE STATE DUMP END ===========");
    }

    /// <summary>
    /// Logs every conversation's unlock condition tree, with GUIDs resolved to names where
    /// known. Stand in the relevant zone first so the assets stream in.
    /// </summary>
    public static void DumpConversationConditions()
    {
        var log = Plugin.Instance.Log;

        // GUID → name for conversations only; other asset types' DataKeys are logged raw.
        var convGuids = new Dictionary<string, string>(StringComparer.Ordinal);
        try
        {
            foreach (var c in Resources.FindObjectsOfTypeAll<FixedConversation>())
            {
                if (c == null) continue;
                var guid = c.Guid;
                if (!string.IsNullOrEmpty(guid))
                    convGuids[guid] = c.name ?? guid;
            }
            log.LogInfo($"[AP-Dump] (Built GUID map: {convGuids.Count} FixedConversation GUIDs)");
        }
        catch (Exception ex) { log.LogWarning($"[AP-Dump] GUID map build failed: {ex.Message}"); }

        var allConversations = Resources.FindObjectsOfTypeAll<FixedConversation>()
            .OrderBy(c => c.name)
            .ToList();

        log.LogInfo($"[AP-Dump] ========== CONVERSATION CONDITIONS DUMP ({allConversations.Count} FixedConversations) ==========");

        int withConditions = 0;
        foreach (var conv in allConversations)
        {
            var conditions = conv.conditions;
            if (conditions == null) continue;
            var children = conditions._children;
            if (children == null || children.Count == 0) continue;

            withConditions++;
            log.LogInfo($"[AP-Dump] ── '{conv.name}' requires ({conditions._operation}): ──");
            LogQueryChildren(log, children, "    ", convGuids);
        }

        log.LogInfo($"[AP-Dump] {withConditions} of {allConversations.Count} conversations have conditions.");
        log.LogInfo("[AP-Dump] =========== CONVERSATION CONDITIONS DUMP END ===========");
    }

    private static void LogQueryChildren(
        BepInEx.Logging.ManualLogSource log,
        Il2CppSystem.Collections.Generic.List<IGameQueryComponent> children,
        string indent,
        Dictionary<string, string> convGuids)
    {
        for (int i = 0; i < children.Count; i++)
        {
            var child = children[i];
            if (child == null) { log.LogInfo($"{indent}[{i}] NULL"); continue; }

            // Composite — recurse
            var composite = child.TryCast<CompositeQueryComponent>();
            if (composite != null)
            {
                var subChildren = composite._children;
                int subCount = subChildren?.Count ?? 0;
                log.LogInfo($"{indent}[{i}] Composite({composite._operation}, {subCount} children):");
                if (subChildren != null)
                    LogQueryChildren(log, subChildren, indent + "  ", convGuids);
                continue;
            }

            // Time-delay wrapper — show the delay and the inner event
            var timeQuery = child.TryCast<GameTimeSinceEventComponent>();
            if (timeQuery != null)
            {
                double days = 0;
                try { days = timeQuery._daysSinceEvent; } catch { }
                var innerChild = timeQuery._child;
                log.LogInfo($"{indent}[{i}] TimeSinceEvent(>={days:F1} days):");
                if (innerChild != null)
                {
                    var innerAsQuery = innerChild.TryCast<IGameQueryComponent>();
                    if (innerAsQuery != null)
                    {
                        var tmp = new Il2CppSystem.Collections.Generic.List<IGameQueryComponent>();
                        tmp.Add(innerAsQuery);
                        LogQueryChildren(log, tmp, indent + "  ", convGuids);
                    }
                    else
                    {
                        log.LogInfo($"{indent}  inner={GetIl2CppTypeName(innerChild)}");
                    }
                }
                else
                {
                    log.LogInfo($"{indent}  (no inner child)");
                }
                continue;
            }

            // Static named game event (story milestone etc.)
            var staticQuery = child.TryCast<StaticEventQueryComponent>();
            if (staticQuery != null)
            {
                var eventName = staticQuery._gameEvent?.name ?? "(null)";
                int count = 0;
                try { count = staticQuery.Count; } catch { }
                log.LogInfo($"{indent}[{i}] StaticEvent  event='{eventName}'  count={count}");
                continue;
            }

            // Produced-event queries: the DataKey on GetEvent() is an asset GUID or a string key.
            var eventQuery = child.TryCast<GameEventQueryComponent>();
            if (eventQuery != null)
            {
                string typeName = GetIl2CppTypeName(child);
                string dataKey = "(err)", eventKey = "(err)", resolvedName = "";
                int count = 0;
                try
                {
                    var gameEvent = eventQuery.GetEvent();
                    eventKey = gameEvent?.EventKey ?? "(null)";
                    dataKey  = gameEvent?.DataKey  ?? "(null)";
                }
                catch (Exception ex) { eventKey = $"(exc:{ex.Message})"; dataKey = ""; }
                try { count = eventQuery.Count; } catch { }

                if (!string.IsNullOrEmpty(dataKey) && typeName.Contains("Conversation"))
                {
                    if (convGuids.TryGetValue(dataKey, out var cName))
                        resolvedName = $" → '{cName}'";
                }
                // StringEvent: the DataKey is the key itself.

                log.LogInfo($"{indent}[{i}] {typeName}  dataKey='{dataKey}'{resolvedName}  count={count}");
                continue;
            }

            // Unknown type — log its IL2CPP name
            log.LogInfo($"{indent}[{i}] Unknown({GetIl2CppTypeName(child)})");
        }
    }

    private static string GetIl2CppTypeName(Il2CppInterop.Runtime.InteropTypes.Il2CppObjectBase obj)
    {
        try
        {
            var ptr = IL2CPP.Il2CppObjectBaseToPtr(obj);
            if (ptr != IntPtr.Zero)
            {
                var classPtr = IL2CPP.il2cpp_object_get_class(ptr);
                return System.Runtime.InteropServices.Marshal.PtrToStringAnsi(IL2CPP.il2cpp_class_get_name(classPtr)) ?? "(null)";
            }
        }
        catch { }
        return "(unknown)";
    }

    /// <summary>Logs all loaded GadgetDefinition names. Assets load lazily, so run in-game.</summary>
    public static void DumpGadgets()
    {
        var log = Plugin.Instance.Log;
        var defs = Resources.FindObjectsOfTypeAll<GadgetDefinition>();
        log.LogInfo($"[AP-Dump] ========== GADGET DUMP ({defs.Count}) ==========");
        foreach (var def in defs.OrderBy(d => d.name))
            log.LogInfo($"[AP-Dump] Gadget  name='{def.name}'");
        log.LogInfo("[AP-Dump] =========== GADGET DUMP END ===========");
    }

    /// <summary>
    /// Dumps every shop, category and item (ShopItemId, asset GUID and name, title, type, cost,
    /// limits). <c>ShopItemId</c> is the stable key purchases persist under. Browse each shop
    /// category first so lazily loaded names resolve; unloaded items still dump id/GUID.
    /// </summary>
    public static void DumpShopItems()
    {
        var log = Plugin.Instance.Log;
        var directors = Resources.FindObjectsOfTypeAll<ShopDirector>();
        log.LogInfo($"[AP-Dump] ========== SHOP DUMP ({directors.Count} director(s)) ==========");

        foreach (var director in directors)
        {
            var runtimes = director._runtimes;
            if (runtimes == null || runtimes.Count == 0)
            {
                log.LogInfo("[AP-Dump] ShopDirector has no runtimes (shop scene not loaded?)");
                continue;
            }

            for (int r = 0; r < runtimes.Count; r++)
            {
                var runtime = runtimes[r];
                if (runtime == null) continue;

                string defName = "<null>";
                try { defName = runtime.Definition?.TryCast<ScriptableObject>()?.name ?? "<non-asset>"; }
                catch { /* keep placeholder */ }
                log.LogInfo($"[AP-Dump] Shop[{r}]  definition='{defName}'  available={runtime.IsShopAvailable}  doneLoading={runtime.IsDoneLoading}");

                // Not AllCategories: its lazy iterator is invalidated mid-enumeration by the
                // director's Update. Index the backing list instead.
                var cats = runtime._dataProvider?.GetLoadedCategories()
                    ?.TryCast<Il2CppSystem.Collections.Generic.List<ShopCategoryRuntime>>();
                if (cats == null)
                {
                    log.LogInfo("[AP-Dump]   (category list unavailable — provider still loading?)");
                    continue;
                }

                for (int c = 0; c < cats.Count; c++)
                {
                    var category = cats[c];
                    if (category == null) continue;

                    string catName = "?", catTag = "?", catDisplay = "";
                    try { catName    = category.AnalyticsName ?? "?"; }       catch { }
                    try { catTag     = category.SourceShopAssetTag ?? "?"; }  catch { }
                    try { catDisplay = category.DisplayName?.GetLocalizedString() ?? ""; } catch { }

                    var items = category._cachedItemList;
                    log.LogInfo($"[AP-Dump]   Category '{catName}'  tag='{catTag}'  display='{catDisplay}'  items={items?.Count ?? 0}");
                    if (items == null) continue;

                    for (int i = 0; i < items.Count; i++)
                    {
                        var item = items[i];
                        if (item == null) continue;
                        try
                        {
                            string uid = "<none>", guid = "", assetName = "<not loaded>", title = "";
                            try { uid       = item.UniqueId.Id ?? "<none>"; }  catch { }
                            try { guid      = item.AssetGuid ?? ""; }          catch { }
                            try { assetName = item.Asset?.TryCast<UnityEngine.Object>()?.name ?? "<not loaded>"; } catch { }
                            try { title     = item.Title?.GetLocalizedString() ?? ""; } catch { }

                            // Fallback: the provider's addressables cache.
                            if (assetName == "<not loaded>" && guid.Length > 0)
                            {
                                try
                                {
                                    var addrProvider = runtime._dataProvider?.TryCast<ShopRuntimeAddressablesDataProvider>();
                                    var found = addrProvider?.FindAssetByRuntimeKey(guid);
                                    if (found != null) assetName = found.name;
                                }
                                catch { /* keep placeholder */ }
                            }

                            // Start the load, so a second dump a few seconds later resolves it.
                            try { if (!item.IsDoneLoading) item.AcquireResources(); } catch { }

                            int newbucks = -1;
                            try { newbucks = item.PurchaseCost?.NewbuckCost ?? -1; } catch { }

                            string limit = "∞";
                            try
                            {
                                var lim = item.ItemLimit;
                                if (lim.HasValue) limit = lim.Value.ToString();
                            }
                            catch { limit = "?"; }

                            int purchased = -1;
                            try { purchased = item.GetCountPurchased(); } catch { }

                            // ItemType NREs while unloaded — guard each read separately.
                            string type = "<unloaded>", unlock = "?", soldOut = "?";
                            try { type    = item.ItemType.ToString(); }  catch { }
                            try { unlock  = item.UnlockType.ToString(); } catch { }
                            try { soldOut = item.IsSoldOut.ToString(); }  catch { }

                            log.LogInfo(
                                $"[AP-Dump]     Item[{i}] id='{uid}'  asset='{assetName}'  title='{title}'  " +
                                $"type={type}  unlock={unlock}  cost={newbucks}nb  " +
                                $"limit={limit}  purchased={purchased}  soldOut={soldOut}  guid='{guid}'");
                        }
                        catch (Exception ex)
                        {
                            log.LogInfo($"[AP-Dump]     Item[{i}] <failed to read: {ex.Message}>");
                        }
                    }
                }
            }
        }

        log.LogInfo("[AP-Dump] =========== SHOP DUMP END ===========");
    }

    /// <summary>
    /// Logs every shop UI <c>Image</c> (path, active, sprite). Run with a vanilla item and with an
    /// AP item selected, and diff, to find what renders the full-art display.
    /// </summary>
    public static void DumpShopUiImages()
    {
        var log = Plugin.Instance.Log;
        var blurbs = Resources.FindObjectsOfTypeAll<ShopItemDescriptionBlurb>();
        log.LogInfo($"[AP-Dump] ===== SHOP UI IMAGE DUMP ({blurbs.Count} blurb(s)) =====");

        var dumpedRoots = new HashSet<int>();
        foreach (var blurb in blurbs)
        {
            if (blurb == null) continue;
            var root = blurb.transform.root;
            if (!dumpedRoots.Add(root.GetInstanceID())) continue;

            log.LogInfo($"[AP-Dump] Root '{root.name}'  (blurb active={blurb.isActiveAndEnabled})");
            log.LogInfo($"[AP-Dump]  blurb._itemIcon path='{PathTo(blurb._itemIcon?.transform, root)}'  sprite='{SpriteLabel(blurb._itemIcon?.sprite)}'");

            var images = root.GetComponentsInChildren<UnityEngine.UI.Image>(true);
            log.LogInfo($"[AP-Dump]  {images.Length} Image component(s):");
            foreach (var img in images)
            {
                if (img == null) continue;
                string state = img.gameObject.activeInHierarchy ? "ON " : "off";
                log.LogInfo($"[AP-Dump]   {state} '{PathTo(img.transform, root)}'  sprite='{SpriteLabel(img.sprite)}'");
            }

            var sprites = root.GetComponentsInChildren<SpriteRenderer>(true);
            if (sprites.Length > 0)
            {
                log.LogInfo($"[AP-Dump]  {sprites.Length} SpriteRenderer component(s):");
                foreach (var sr in sprites)
                {
                    if (sr == null) continue;
                    string state = sr.gameObject.activeInHierarchy ? "ON " : "off";
                    log.LogInfo($"[AP-Dump]   {state} '{PathTo(sr.transform, root)}'  sprite='{sr.sprite?.name ?? "<null>"}'");
                }
            }
        }

        // Also sweep the scene for RawImages and active Images under roots with no blurb.
        log.LogInfo("[AP-Dump]  --- scene-wide sweep (active only) ---");
        foreach (var raw in Resources.FindObjectsOfTypeAll<UnityEngine.UI.RawImage>())
        {
            if (raw == null || !raw.gameObject.activeInHierarchy) continue;
            var root = raw.transform.root;
            log.LogInfo($"[AP-Dump]  RawImage ON  root='{root.name}'  '{PathTo(raw.transform, root)}'  texture='{raw.texture?.name ?? "<null>"}'");
        }
        foreach (var img in Resources.FindObjectsOfTypeAll<UnityEngine.UI.Image>())
        {
            if (img == null || !img.gameObject.activeInHierarchy) continue;
            var root = img.transform.root;
            if (dumpedRoots.Contains(root.GetInstanceID())) continue; // already listed above
            log.LogInfo($"[AP-Dump]  Image    ON  root='{root.name}'  '{PathTo(img.transform, root)}'  sprite='{SpriteLabel(img.sprite)}'");
        }

        // Whether the full-art tick is engaged.
        var trackedBlurb = SlimeRancher2AP.Patches.UiPatches.ShopUiHelper.ActiveBlurb;
        var trackedInfo  = SlimeRancher2AP.Patches.UiPatches.ShopUiHelper.ActiveBlurbInfo;
        log.LogInfo($"[AP-Dump]  Tick tracking: ActiveBlurb={(trackedBlurb != null ? $"set (active={trackedBlurb.isActiveAndEnabled})" : "NULL")}  info='{trackedInfo?.Name ?? "<null>"}'");
        log.LogInfo("[AP-Dump] ===== SHOP UI IMAGE DUMP END =====");
    }

    /// <summary>
    /// Dumps every shop item's availability condition, per rule-set group and per item — the
    /// authoritative source for the apworld's shop region logic.
    /// </summary>
    public static void DumpShopItemConditions()
    {
        var log = Plugin.Instance.Log;
        BuildDescribeGuidNames();   // so event data keys resolve to conversation names
        var directors = Resources.FindObjectsOfTypeAll<ShopDirector>();
        log.LogInfo("[AP-Dump] ===== SHOP CONDITION DUMP =====");

        foreach (var director in directors)
        {
            var runtimes = director._runtimes;
            if (runtimes == null) continue;
            for (int r = 0; r < runtimes.Count; r++)
            {
                var cats = runtimes[r]?._dataProvider?.GetLoadedCategories()
                    ?.TryCast<Il2CppSystem.Collections.Generic.List<ShopCategoryRuntime>>();
                if (cats == null) continue;

                for (int c = 0; c < cats.Count; c++)
                {
                    var category = cats[c];
                    if (category == null) continue;
                    string catName = "?";
                    try { catName = category.AnalyticsName ?? "?"; } catch { }

                    var providers = category._providers;
                    log.LogInfo($"[AP-Dump] Category '{catName}'  providers={providers?.Count ?? 0}");
                    if (providers == null) continue;

                    for (int p = 0; p < providers.Count; p++)
                    {
                        var group = providers[p]?.TryCast<ShopRuntimeRuleSetGroup>();
                        if (group == null)
                        {
                            log.LogInfo($"[AP-Dump]  Provider[{p}] <not a rule set group>");
                            continue;
                        }

                        string groupName = "?", groupCond = "<none>";
                        try { groupName = group.RuleSetName ?? "?"; } catch { }
                        try
                        {
                            var ruleSet = group._ruleSet?.TryCast<ShopCategorySourceRuleSet>();
                            if (ruleSet?._allItemsAvailableCondition != null)
                                groupCond = DescribeQuery(ruleSet._allItemsAvailableCondition, 0);
                        }
                        catch (Exception ex) { groupCond = $"<error: {ex.Message}>"; }
                        log.LogInfo($"[AP-Dump]  Group[{p}] '{groupName}'  groupCondition={groupCond}");

                        var sources = group._dataSources;
                        if (sources == null) continue;
                        for (int s = 0; s < sources.Count; s++)
                        {
                            var dataSource = sources[s]?.DataSource;
                            var srcName = dataSource?.TryCast<UnityEngine.Object>()?.name ?? "<null>";
                            var table   = dataSource?.TryCast<ShopFixedItemsTable>();
                            log.LogInfo($"[AP-Dump]   Table[{s}] '{srcName}'  fixedItems={(table != null)}");
                            if (table?._itemListings == null) continue;

                            var listings = table._itemListings;
                            for (int i = 0; i < listings.Length; i++)
                            {
                                var entry = listings[i];
                                if (entry == null) continue;

                                string label = "<no guid>", cond = "<always>";
                                int nb = -1;
                                try
                                {
                                    var guid = entry._itemCost?.ShopItem?.AssetGuid ?? "";
                                    label = guid;
                                    if (Data.LocationTable.TryGetShopByAssetGuid(guid, out var linfo) && linfo != null)
                                        label = linfo.Name;
                                }
                                catch { }
                                try { nb = entry._itemCost?.PurchaseCost?.NewbuckCost ?? -1; } catch { }
                                try
                                {
                                    if (entry.AvailableCondition != null)
                                        cond = DescribeQuery(entry.AvailableCondition, 0);
                                }
                                catch (Exception ex) { cond = $"<error: {ex.Message}>"; }

                                log.LogInfo($"[AP-Dump]    Item[{i}] '{label}'  cost={nb}nb  condition={cond}");
                            }
                        }
                    }
                }
            }
        }
        log.LogInfo("[AP-Dump] ===== SHOP CONDITION DUMP END =====");
    }

    /// <summary>GUID → asset name for <see cref="DescribeQuery"/>; filled by <see cref="BuildDescribeGuidNames"/>.</summary>
    private static Dictionary<string, string> _describeGuidNames = new(StringComparer.Ordinal);

    /// <summary>Conversations, ranchers and gadgets — the asset kinds shop conditions reference.</summary>
    private static void BuildDescribeGuidNames()
    {
        var map = new Dictionary<string, string>(StringComparer.Ordinal);
        try
        {
            foreach (var c in Resources.FindObjectsOfTypeAll<FixedConversation>())
            {
                if (c == null) continue;
                var guid = c.Guid;
                if (!string.IsNullOrEmpty(guid)) map[guid] = $"conv:{c.name ?? guid}";
            }
        }
        catch { /* partial map still beats none */ }

        // Ranchers too: `convoRancherPlayed` keys on the rancher's GUID.
        try
        {
            foreach (var r in Resources.FindObjectsOfTypeAll<Il2CppMonomiPark.SlimeRancher.Dialogue.CommStation.RancherDefinition>())
            {
                if (r == null) continue;
                var guid = r.Guid;
                if (!string.IsNullOrEmpty(guid)) map[guid] = $"rancher:{r.name ?? guid}";
            }
        }
        catch { /* partial map still beats none */ }
        _describeGuidNames = map;
    }

    /// <summary>
    /// Describes a query component recursively: composites as <c>Operation(child, …)</c>,
    /// leaves as their IL2CPP type plus referenced assets and short values (via reflection).
    /// </summary>
    private static string DescribeQuery(Il2CppInterop.Runtime.InteropTypes.Il2CppObjectBase? comp, int depth)
    {
        if (comp == null) return "<null>";
        if (depth > 6) return "<max depth>";

        var composite = comp.TryCast<Il2CppMonomiPark.SlimeRancher.Event.Query.CompositeQueryComponent>();
        if (composite != null)
        {
            var parts = new List<string>();
            var children = composite._children;
            if (children != null)
                for (int i = 0; i < children.Count; i++)
                    parts.Add(DescribeQuery(children[i], depth + 1));
            return $"{composite._operation}({string.Join(" , ", parts)})";
        }

        // Event-backed queries: identity lives on GetEvent(), not the fields — reflecting the
        // fields only yields a pointer rendered as an int.
        var eventQuery = comp.TryCast<GameEventQueryComponent>();
        if (eventQuery != null)
        {
            string typeName = GetIl2CppTypeName(comp);
            string dataKey = "(err)", eventKey = "(err)", resolved = "";
            int count = 0;
            try
            {
                var gameEvent = eventQuery.GetEvent();
                eventKey = gameEvent?.EventKey ?? "(null)";
                dataKey  = gameEvent?.DataKey  ?? "(null)";
            }
            catch (Exception ex) { eventKey = $"(exc:{ex.Message})"; dataKey = ""; }
            try { count = eventQuery.Count; } catch { /* count is a bonus, not the identity */ }

            if (!string.IsNullOrEmpty(dataKey) && _describeGuidNames.TryGetValue(dataKey, out var nm))
                resolved = $" -> '{nm}'";

            return $"{typeName}[event='{eventKey}' dataKey='{dataKey}'{resolved} count={count}]";
        }

        try
        {
            var sysObj = comp.TryCast<Il2CppSystem.Object>();
            var type = sysObj?.GetIl2CppType();
            if (type == null) return comp.GetType().Name;

            var bits = new List<string>();
            var fields = type.GetFields(Il2CppSystem.Reflection.BindingFlags.Instance
                                        | Il2CppSystem.Reflection.BindingFlags.Public
                                        | Il2CppSystem.Reflection.BindingFlags.NonPublic);
            foreach (var field in fields)
            {
                Il2CppSystem.Object? val = null;
                try { val = field.GetValue(sysObj); } catch { continue; }
                if (val == null) continue;

                var unityObj = val.TryCast<UnityEngine.Object>();
                if (unityObj != null) { bits.Add($"{field.Name}='{unityObj.name}'"); continue; }

                string strVal;
                try { strVal = val.ToString(); } catch { continue; }
                if (!string.IsNullOrEmpty(strVal) && strVal.Length <= 48 && !strVal.Contains("Il2Cpp"))
                    bits.Add($"{field.Name}={strVal}");
            }
            return $"{type.Name}[{string.Join(", ", bits)}]";
        }
        catch (Exception ex)
        {
            return $"<describe failed: {ex.Message}>";
        }
    }

    /// <summary>Sprite name, marking the mod's AP logo explicitly (embedded sprites are unnamed).</summary>
    private static string SpriteLabel(Sprite? sprite)
    {
        if (sprite == null) return "<null>";
        var logo = SlimeRancher2AP.Patches.UiPatches.OptionsMenuInjectionPatch.GetLogoSprite();
        if (logo != null && sprite == logo) return "<AP LOGO>";
        return string.IsNullOrEmpty(sprite.name) ? "<unnamed, NOT logo>" : sprite.name;
    }

    /// <summary>Hierarchy path of <paramref name="t"/> relative to <paramref name="root"/>.</summary>
    private static string PathTo(Transform? t, Transform root)
    {
        if (t == null) return "<null>";
        var parts = new List<string>();
        var cur = t;
        while (cur != null && cur != root)
        {
            parts.Add(cur.name);
            cur = cur.parent;
        }
        parts.Reverse();
        return string.Join("/", parts);
    }

    /// <summary>
    /// Logs each SlimeDefinition's <c>NativeZones</c>, to verify slime region assignments.
    /// Loads globally; run with a save loaded.
    /// </summary>
    public static void DumpSlimeZones()
    {
        var log  = Plugin.Instance.Log;
        var defs = Resources.FindObjectsOfTypeAll<SlimeDefinition>()
            .OrderBy(d => d.name)
            .ToList();

        log.LogInfo($"[AP-Dump] ========== SLIME ZONE DUMP ({defs.Count} SlimeDefinitions) ==========");
        foreach (var def in defs)
        {
            if (def == null) continue;
            var zones = def.NativeZones;
            if (zones == null || zones.Length == 0)
            {
                log.LogInfo($"[AP-Dump] Slime  name='{def.name}'  zones=(none)");
                continue;
            }
            var zoneNames = new System.Collections.Generic.List<string>();
            for (int i = 0; i < zones.Length; i++)
            {
                var z = zones[i];
                if (z != null) zoneNames.Add(z.name ?? "(null)");
            }
            log.LogInfo($"[AP-Dump] Slime  name='{def.name}'  zones=[{string.Join(", ", zoneNames)}]");
        }
        log.LogInfo("[AP-Dump] =========== SLIME ZONE DUMP END ===========");
    }

    /// <summary>
    /// Logs each IdentifiableType's <c>showForZones</c>, to verify resource and ingredient regions
    /// in the apworld. Unbound types are listed last. Run with a save loaded.
    /// </summary>
    public static void DumpIdentifiableTypeZones()
    {
        var log   = Plugin.Instance.Log;
        var types = Resources.FindObjectsOfTypeAll<IdentifiableType>()
            .OrderBy(t => t.name)
            .ToList();

        var withZones    = types.Where(t => t?.showForZones != null && t.showForZones.Length > 0).ToList();
        var withoutZones = types.Where(t => t != null && (t.showForZones == null || t.showForZones.Length == 0)).ToList();

        log.LogInfo($"[AP-Dump] ========== IDENTIFIABLE TYPE ZONE DUMP ({withZones.Count} with zones, {withoutZones.Count} without) ==========");

        log.LogInfo("[AP-Dump] --- Types WITH zone bindings ---");
        foreach (var t in withZones)
        {
            var zoneNames = new System.Collections.Generic.List<string>();
            for (int i = 0; i < t.showForZones.Length; i++)
            {
                var z = t.showForZones[i];
                if (z != null) zoneNames.Add(z.name ?? "(null)");
            }
            log.LogInfo($"[AP-Dump] IdentType  name='{t.name}'  zones=[{string.Join(", ", zoneNames)}]");
        }

        log.LogInfo($"[AP-Dump] --- Types WITHOUT zone bindings ({withoutZones.Count}) ---");
        foreach (var t in withoutZones)
            log.LogInfo($"[AP-Dump] IdentType  name='{t.name}'  zones=(none)");

        log.LogInfo("[AP-Dump] =========== IDENTIFIABLE TYPE ZONE DUMP END ===========");
    }

    /// <summary>Logs every IdentifiableType name, sorted.</summary>
    public static void DumpIdentifiableTypes()
    {
        var log = Plugin.Instance.Log;
        var types = Resources.FindObjectsOfTypeAll<IdentifiableType>()
            .OrderBy(t => t.name)
            .ToList();

        log.LogInfo($"[AP-Dump] ========== IDENTIFIABLE TYPE DUMP ({types.Count}) ==========");
        foreach (var t in types)
            log.LogInfo($"[AP-Dump] IdentType  name='{t.name}'");
        log.LogInfo("[AP-Dump] =========== IDENTIFIABLE TYPE DUMP END ===========");
    }

    /// <summary>
    /// Logs the IdentifiableType of every Fabricator recipe ingredient, from each upgrade level's
    /// <c>PurchaseCost</c>. Run with the Fabricator loaded.
    /// </summary>
    public static void DumpUpgradeComponents()
    {
        var log = Plugin.Instance.Log;

        var upgradeDefs = Resources.FindObjectsOfTypeAll<UpgradeDefinition>()
            .OrderBy(d => d.name)
            .ToList();

        log.LogInfo($"[AP-Dump] ========== UPGRADE COMPONENT DUMP ({upgradeDefs.Count} UpgradeDefinitions) ==========");

        // All names first — currency/plort-only recipes don't appear below.
        log.LogInfo("[AP-Dump] --- All UpgradeDefinition names ---");
        foreach (var def in upgradeDefs)
            log.LogInfo($"[AP-Dump] UpgradeDef  name='{def.name}'  levels={def._levels?.Count ?? 0}");

        var seen = new System.Collections.Generic.HashSet<string>();

        foreach (var def in upgradeDefs)
        {
            var levels = def._levels;
            if (levels == null) continue;

            for (int lvl = 0; lvl < levels.Count; lvl++)
            {
                var levelDef = levels[lvl];
                if (levelDef == null) continue;

                var cost = levelDef._costs;
                if (cost == null) continue;

                var rawIngredients = RecipeFactory.BaseRecipe.IngredientsFromCost(cost);
                if (rawIngredients == null) { log.LogInfo($"[AP-Dump]   lvl{lvl}: IngredientsFromCost returned NULL"); continue; }

                // Always List<IIngredient>; wrap by pointer for Count/indexer
                var ingredients = new Il2CppSystem.Collections.Generic.List<IIngredient>(rawIngredients.Pointer);
                for (int i = 0; i < ingredients.Count; i++)
                {
                    var ing = ingredients[i];
                    if (ing == null) continue;

                    var upgradeIng = ing.TryCast<RecipeFactory.UpgradeIngredient>();
                    if (upgradeIng == null) continue; // skip CurrencyIngredient / craft materials

                    var comp = upgradeIng._upgrade;
                    var compName = comp?.name ?? "<null>";
                    log.LogInfo($"[AP-Dump]   {def.name}[lvl{lvl}] → component='{compName}' ×{ing.CountRequired}");
                    if (comp != null) seen.Add(compName);
                }
            }
        }

        log.LogInfo($"[AP-Dump] --- Unique IdentifiableType names ({seen.Count}) ---");
        foreach (var name in seen.OrderBy(n => n))
            log.LogInfo($"[AP-Dump] Component  name='{name}'");

        log.LogInfo("[AP-Dump] =========== UPGRADE COMPONENT DUMP END ===========");
    }

    /// <summary>Logs every PuzzleSlotLockable: scene, name, posKey, lockTag, state, required plorts.</summary>
    public static void DumpPuzzleSlotLockables()
    {
        var log   = Plugin.Instance.Log;
        var doors = Resources.FindObjectsOfTypeAll<PuzzleSlotLockable>();
        log.LogInfo($"[AP-Dump] ========== PUZZLE SLOT LOCKABLE DUMP ({doors.Count}) ==========");

        foreach (var door in doors
            .OrderBy(d => d.gameObject?.scene.name ?? "")
            .ThenBy(d => d.gameObject?.name ?? ""))
        {
            string scene, name, posKey, lockTag;
            try
            {
                scene   = door.gameObject?.scene.name ?? "?";
                name    = door.gameObject?.name ?? "?";
                posKey  = WorldUtils.PositionKey(door.gameObject!);
                lockTag = door.AnalyticsLockTag ?? "";
            }
            catch { continue; }

            bool unlocked = false;
            try { unlocked = door.ShouldUnlock(); } catch { /* guard */ }

            var plortTypes = new System.Text.StringBuilder();
            try
            {
                if (door._depositors != null)
                {
                    foreach (var dep in door._depositors)
                    {
                        var typeName = dep?._catchIdentifiableType?.name ?? "?";
                        if (plortTypes.Length > 0) plortTypes.Append(", ");
                        plortTypes.Append(typeName);
                    }
                }
            }
            catch { /* guard */ }

            log.LogInfo(
                $"[AP-Dump] PuzzleSlotLockable  scene='{scene}'  name='{name}'  " +
                $"posKey='{posKey}'  lockTag='{lockTag}'  " +
                $"unlocked={unlocked}  plorts=[{plortTypes}]");
        }

        log.LogInfo("[AP-Dump] =========== PUZZLE SLOT LOCKABLE DUMP END ===========");
    }

    /// <summary>Logs every AccessDoor and its model state.</summary>
    public static void DumpAccessDoors()
    {
        var log   = Plugin.Instance.Log;
        var doors = Resources.FindObjectsOfTypeAll<Il2CppMonomiPark.World.AccessDoor>();
        log.LogInfo($"[AP-Dump] --- AccessDoor ({doors.Count}) ---");
        foreach (var door in doors
            .OrderBy(d => d.gameObject.scene.name)
            .ThenBy(d => d.gameObject.name))
        {
            var state = door._model != null ? door._model.state.ToString() : "(no model)";
            log.LogInfo($"[AP-Dump] AccessDoor  id='{door._id}'  scene='{door.gameObject.scene.name}'  name='{door.gameObject.name}'  state={state}  pos={PosStr(door.gameObject)}");
        }
    }

    /// <summary>Logs every SlimeGateActivator (Radiant Projector beam-puzzle gate).</summary>
    public static void DumpSlimeGates()
    {
        var log = Plugin.Instance.Log;
        DumpSlimeGates(log);
    }

    private static void DumpSlimeGates(BepInEx.Logging.ManualLogSource log)
    {
        var gates = Resources.FindObjectsOfTypeAll<SlimeGateActivator>();
        log.LogInfo($"[AP-Dump] --- SlimeGateActivator ({gates.Count}) ---");
        foreach (var gate in gates
            .OrderBy(g => g.gameObject.scene.name)
            .ThenBy(g => g.gameObject.name))
        {
            var door = gate.GateDoor;
            var doorName = door != null ? door.gameObject.name : "<null>";
            log.LogInfo($"[AP-Dump] SlimeGate  scene='{gate.gameObject.scene.name}'  name='{gate.gameObject.name}'  door='{doorName}'  pos={PosStr(gate.gameObject)}");
        }
    }

    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    private static string PosStr(GameObject go)
    {
        var p = go.transform.position;
        return $"({Mathf.RoundToInt(p.x)}, {Mathf.RoundToInt(p.y)}, {Mathf.RoundToInt(p.z)})";
    }

    // -------------------------------------------------------------------------

    private static void DumpTreasurePods(BepInEx.Logging.ManualLogSource log)
    {
        var pods = Resources.FindObjectsOfTypeAll<TreasurePod>();

        // Ghostly drone nodes are TreasurePods too
        var regularPods = pods.Where(p => !p.gameObject.name.StartsWith("node")).ToList();
        var dronePods   = pods.Where(p =>  p.gameObject.name.StartsWith("node")).ToList();

        log.LogInfo($"[AP-Dump] --- TreasurePods ({regularPods.Count} regular, {dronePods.Count} node/drone) ---");
        foreach (var pod in regularPods)
        {
            var posKey = WorldUtils.PositionKey(pod.gameObject);
            log.LogInfo($"[AP-Dump] TreasurePod  scene='{pod.gameObject.scene.name}'  name='{pod.gameObject.name}'  pos={PosStr(pod.gameObject)}  posKey='{posKey}'");
        }

        if (dronePods.Count > 0)
        {
            log.LogInfo($"[AP-Dump] --- GhostlyDrone nodes (TreasurePod subtype) ({dronePods.Count}) ---");
            foreach (var pod in dronePods)
            {
                var posKey = WorldUtils.PositionKey(pod.gameObject);
                var parent = pod.transform.parent?.gameObject.name ?? "<no parent>";
                log.LogInfo($"[AP-Dump] GhostlyNode  scene='{pod.gameObject.scene.name}'  name='{pod.gameObject.name}'  parent='{parent}'  pos={PosStr(pod.gameObject)}  posKey='{posKey}'");
            }
        }
    }

    private static void DumpGordos(BepInEx.Logging.ManualLogSource log)
    {
        var gordos = Resources.FindObjectsOfTypeAll<GordoEat>();

        // An empty scene name means a GordoSnare prefab template, not a world gordo.
        var staticGordos = gordos.Where(g => !string.IsNullOrEmpty(g.gameObject.scene.name)).ToList();
        var snareGordos  = gordos.Where(g =>  string.IsNullOrEmpty(g.gameObject.scene.name)).ToList();

        log.LogInfo($"[AP-Dump] --- GordoEat ({staticGordos.Count} static world, {snareGordos.Count} snare/global) ---");
        foreach (var gordo in staticGordos)
        {
            // _id ("gordo" + runtime suffix) is the event key fired on burst.
            string gordoId = "(err)";
            try { gordoId = gordo._id ?? "(null)"; } catch { }
            log.LogInfo($"[AP-Dump] GordoEat    scene='{gordo.gameObject.scene.name}'  name='{gordo.gameObject.name}'  stringEventKey='{gordoId}'");
        }
        // Snare gordos, for reference (not checks)
        log.LogInfo($"[AP-Dump] GordoSnare names (NOT checks): {string.Join(", ", snareGordos.Select(g => g.gameObject.name))}");
    }

    private static void DumpMapNodes(BepInEx.Logging.ManualLogSource log)
    {
        var nodes = Resources.FindObjectsOfTypeAll<MapNodeActivator>();
        log.LogInfo($"[AP-Dump] --- MapNodeActivator ({nodes.Count}) ---");
        foreach (var node in nodes)
        {
            var posKey = WorldUtils.PositionKey(node.gameObject);
            log.LogInfo($"[AP-Dump] MapNode     scene='{node.gameObject.scene.name}'  name='{node.gameObject.name}'  pos={PosStr(node.gameObject)}  posKey='{posKey}'");
        }
    }

    private static void DumpShadowPlortDoors(BepInEx.Logging.ManualLogSource log)
    {
        var depositors = Resources.FindObjectsOfTypeAll<PlortDepositor>();
        // Shadow Plort doors only
        var shadowDoors = depositors
            .Where(d => d._catchIdentifiableType != null &&
                        d._catchIdentifiableType.name == "ShadowPlort")
            .ToList();

        log.LogInfo($"[AP-Dump] --- Shadow Plort Doors ({shadowDoors.Count} of {depositors.Count} PlortDepositors) ---");
        foreach (var door in shadowDoors)
        {
            var posKey = WorldUtils.PositionKey(door.gameObject);
            log.LogInfo($"[AP-Dump] ShadowDoor  scene='{door.gameObject.scene.name}'  name='{door.gameObject.name}'  pos={PosStr(door.gameObject)}  posKey='{posKey}'");
        }
    }

    private static void DumpResearchDrones(BepInEx.Logging.ManualLogSource log)
    {
        var activators = Resources.FindObjectsOfTypeAll<ResearchDroneActivator>();
        log.LogInfo($"[AP-Dump] --- ResearchDroneActivator ({activators.Count}) ---");
        foreach (var activator in activators)
        {
            var controller = activator._researchDroneController;
            var entry      = controller?.ResearchDroneEntry;
            var entryName  = entry?.name ?? "<null>";
            var archName   = entry?.archivedEntry?.name ?? "<none>";
            log.LogInfo(
                $"[AP-Dump] ResearchDrone  scene='{activator.gameObject.scene.name}'  " +
                $"entryName='{entryName}'  archive='{archName}'");
        }
    }

    /// <summary>
    /// Logs each ResearchDroneEntry, its <c>archivedEntry</c>, and whether that archive has a
    /// <see cref="LocationTable"/> row. Run once per zone; the summary lists drones not yet seen.
    /// </summary>
    public static void DumpResearchDroneArchives()
    {
        var log = Plugin.Instance.Log;
        log.LogInfo("[AP-Dump] ===== RESEARCH DRONE ARCHIVES =====");

        var entries = Resources.FindObjectsOfTypeAll<ResearchDroneEntry>();
        log.LogInfo($"[AP-Dump] Loaded ResearchDroneEntry assets: {entries.Count}");

        // Archives are ResearchDroneEntry assets too — collect their names to tell mains apart.
        var archiveNames = new HashSet<string>(StringComparer.Ordinal);
        foreach (var e in entries)
        {
            var a = e?.archivedEntry;
            if (a != null && !string.IsNullOrEmpty(a.name)) archiveNames.Add(a.name);
        }

        var seenMains = new HashSet<string>(StringComparer.Ordinal);
        foreach (var entry in entries.OrderBy(e => e?.name ?? ""))
        {
            if (entry == null) continue;
            var name = entry.name ?? "<null>";
            if (archiveNames.Contains(name)) continue; // listed via its main below

            var archive = entry.archivedEntry;
            seenMains.Add(name);

            bool droneKnown = LocationTable.TryGetByEntryName(name, out var droneInfo) && droneInfo != null;

            if (archive == null)
            {
                log.LogInfo(
                    $"[AP-Dump] Drone '{name}'  archive=<NONE>  " +
                    $"(drone location: {(droneKnown ? droneInfo!.Id.ToString() : "NOT IN TABLE")})");
                continue;
            }

            bool archKnown = LocationTable.TryGetByEntryName(archive.name, out var archInfo) && archInfo != null;
            log.LogInfo(
                $"[AP-Dump] Drone '{name}'  archive='{archive.name}'  " +
                $"archiveLocation={(archKnown ? archInfo!.Id.ToString() : "*** MISSING FROM LOCATIONTABLE ***")}  " +
                $"(drone location: {(droneKnown ? droneInfo!.Id.ToString() : "NOT IN TABLE")})");
        }

        // Table drones this run did not observe
        var tableDrones = LocationTable.All
            .Where(l => l.Type == LocationType.ResearchDrone && !string.IsNullOrEmpty(l.EntryName))
            .Select(l => l.EntryName!)
            .ToList();
        var missing = tableDrones.Where(n => !seenMains.Contains(n)).ToList();
        log.LogInfo(
            $"[AP-Dump] Coverage: {tableDrones.Count - missing.Count}/{tableDrones.Count} known drones " +
            $"observed in this dump.");
        if (missing.Count > 0)
            log.LogInfo($"[AP-Dump] Not loaded (visit their zones and dump again): {string.Join(", ", missing)}");

        log.LogInfo("[AP-Dump] ===== END RESEARCH DRONE ARCHIVES =====");
    }

    private static void DumpSwitches(BepInEx.Logging.ManualLogSource log)
    {
        var switches = Resources.FindObjectsOfTypeAll<WorldStatePrimarySwitch>();
        log.LogInfo($"[AP-Dump] --- WorldStatePrimarySwitch ({switches.Count}) ---");
        foreach (var sw in switches
            .OrderBy(s => s.gameObject.scene.name)
            .ThenBy(s => s.gameObject.name))
        {
            log.LogInfo($"[AP-Dump] Switch  scene='{sw.gameObject.scene.name}'  name='{sw.gameObject.name}'  pos={PosStr(sw.gameObject)}");
        }
    }

    // -------------------------------------------------------------------------

    /// <summary>Logs SceneGroups and TeleportNetwork destinations (zone ids for the teleport trap).</summary>
    public static void DumpSceneGroups()
    {
        var log = Plugin.Instance.Log;
        log.LogInfo("[AP-Dump] ========== SCENE GROUPS + TELEPORT WAKE-UP DESTINATIONS ==========");

        var allGroups = Resources.FindObjectsOfTypeAll<Il2CppMonomiPark.SlimeRancher.SceneManagement.SceneGroup>();
        log.LogInfo($"[AP-Dump] SceneGroup assets found: {allGroups.Count}");
        foreach (var sg in allGroups.OrderBy(g => g.ReferenceId ?? ""))
            log.LogInfo($"[AP-Dump] SceneGroup  refId='{sg.ReferenceId}'  name='{sg.name}'");

        // Wake-up destinations — populated per zone as visited
        var network = UnityEngine.Object.FindObjectOfType<Il2CppMonomiPark.SlimeRancher.World.Teleportation.TeleportNetwork>();
        if (network == null)
        {
            log.LogInfo("[AP-Dump] TeleportNetwork: not found in scene");
        }
        else
        {
            var dests = network._wakeUpDestinations;
            log.LogInfo($"[AP-Dump] TeleportNetwork._wakeUpDestinations count: {dests?.Count ?? 0}");
            if (dests != null)
            {
                var enumerator = dests.GetEnumerator();
                while (enumerator.MoveNext())
                {
                    var sg   = enumerator.Current.Key;
                    var info = enumerator.Current.Value;
                    log.LogInfo($"[AP-Dump] WakeUp  refId='{sg?.ReferenceId}'  pos={info?.Position}  rot={info?.Rotation}");
                }
            }

            // Teleporter node IDs
            var nodes = network._nodeIdToTeleporterNodeDict;
            log.LogInfo($"[AP-Dump] TeleportNetwork._nodeIdToTeleporterNodeDict count: {nodes?.Count ?? 0}");
            if (nodes != null)
            {
                var ne = nodes.GetEnumerator();
                while (ne.MoveNext())
                    log.LogInfo($"[AP-Dump] TeleporterNode  id='{ne.Current.Key}'");
            }
        }

        log.LogInfo("[AP-Dump] ========== SCENE GROUPS DUMP END ==========");
    }

    /// <summary>
    /// Logs each RadiantSlimePediaEntry name (the <c>LocationTable</c> key) and its slime, plus
    /// the per-slime shuffle-bag sizes.
    /// </summary>
    public static void DumpRadiantSlimes()
    {
        var log = Plugin.Instance.Log;

        // ── Pedia entries ──────────────────────────────────────────────────────
        var entries = Resources.FindObjectsOfTypeAll<Il2CppMonomiPark.SlimeRancher.Pedia.RadiantSlimePediaEntry>()
            .OrderBy(e => e.name)
            .ToList();

        log.LogInfo($"[AP-Dump] ===== RADIANT SLIMEPEDIA DUMP ({entries.Count} entries) =====");
        foreach (var entry in entries)
        {
            if (entry == null) continue;
            string slimeName = "(null)";
            try { slimeName = entry._slimeDefinition?.name ?? "(null)"; } catch { slimeName = "(err)"; }
            log.LogInfo($"[AP-Dump]   entry='{entry.name}'  slimeDef='{slimeName}'");
        }

        // ── Shuffle bag sizes ──────────────────────────────────────────────────
        var configs = Resources.FindObjectsOfTypeAll<Il2CppMonomiPark.SlimeRancher.Slime.RadiantSlimeConfig>()
            .ToList();

        log.LogInfo($"[AP-Dump] ===== RADIANT SHUFFLE BAG SIZES ({configs.Count} config(s)) =====");
        foreach (var config in configs)
        {
            if (config == null) continue;
            log.LogInfo($"[AP-Dump]   config='{config.name}'  sanctuaryScalar={config._sanctuaryUnlockedRadiantBagSizeScalar}");
            var bags = config._radiantShuffleBagSizes;
            if (bags == null) { log.LogInfo("[AP-Dump]   (null bag array)"); continue; }
            for (int i = 0; i < bags.Length; i++)
            {
                var bag = bags[i];
                if (bag == null) continue;
                string slimeName = "(null)";
                try { slimeName = bag.Slime?.name ?? "(null)"; } catch { slimeName = "(err)"; }
                log.LogInfo($"[AP-Dump]   [{i}] slime='{slimeName}'  bagSize={bag.BagSize}");
            }
        }

        log.LogInfo("[AP-Dump] ===== RADIANT DUMP END =====");
    }

    /// <summary>
    /// Sets every radiant shuffle bag to <paramref name="size"/> (1 = every encounter radiant,
    /// 2 = every other), in the config (for new bags) and on live bags via <c>SetNewSize</c>,
    /// which reshuffles immediately. In-memory only.
    /// </summary>
    public static void SetRadiantBagSizes(int size)
    {
        var log = Plugin.Instance.Log;

        // ── 1. Config (bags initialised from now on) ────────
        var configs = Resources.FindObjectsOfTypeAll<Il2CppMonomiPark.SlimeRancher.Slime.RadiantSlimeConfig>()
            .ToList();

        int configCount = 0;
        foreach (var config in configs)
        {
            if (config == null) continue;
            var entries = config._radiantShuffleBagSizes;
            if (entries == null) continue;
            for (int i = 0; i < entries.Length; i++)
            {
                var entry = entries[i];
                if (entry == null) continue;
                entry.BagSize = size;
                configCount++;
            }
        }

        // ── 2. Live bags (resets the current cycle) ───────────────
        var directors = Resources.FindObjectsOfTypeAll<Il2CppMonomiPark.SlimeRancher.Slime.RadiantSlimeDirector>();
        int liveCount = 0;
        foreach (var director in directors)
        {
            if (director == null) continue;
            var model = director._radiantSlimesModel;   // field, not the CallerCount(0) property — see DumpRadiantBagState
            if (model == null) continue;
            var liveBags = model.RadiantShuffleBags;
            if (liveBags == null) continue;
            foreach (var kvp in liveBags)
            {
                var bag = kvp.Value;
                if (bag == null) continue;
                bag.SetNewSize(size);   // updates Size + reshuffles (CurrentIndex=0, new RadiantSpawnIndex)
                liveCount++;
            }
        }

        if (configCount == 0 && liveCount == 0)
        {
            log.LogWarning($"[AP-Debug] SetRadiantBagSizes({size}): nothing found — load a save first");
            return;
        }

        log.LogInfo($"[AP-Debug] SetRadiantBagSizes({size}): config entries={configCount}  live bags reset={liveCount}");
    }

    /// <summary>Logs each saved RadiantShuffleBag's index, radiant index and size.</summary>
    public static void DumpRadiantBagState()
    {
        var log = Plugin.Instance.Log;
        var directors = Resources.FindObjectsOfTypeAll<Il2CppMonomiPark.SlimeRancher.Slime.RadiantSlimeDirector>();

        if (directors.Count == 0)
        {
            log.LogWarning("[AP-Debug] DumpRadiantBagState: no RadiantSlimeDirector found — load a save first");
            return;
        }

        // This is the snapshot persisted in the save, not the live native bags — a slime is
        // missing until its bag has been saved once.
        log.LogInfo("[AP-Debug] ===== RADIANT BAG STATE (persisted snapshot from save) =====");
        foreach (var director in directors)
        {
            if (director == null) continue;
            bool spawnAllowed = director._isRadiantSpawnAllowedCached;
            log.LogInfo($"[AP-Debug]   isRadiantSpawnAllowedCached={spawnAllowed}  DEBUG_ForceRadiantSpawn={director.DEBUG_ForceRadiantSpawn}  DEBUG_BagSizeScalar={director.DEBUG_BagSizeScalar}");
            // The field, not the CallerCount(0) property, which can hard-crash the process.
            var model = director._radiantSlimesModel;
            if (model == null) { log.LogInfo("[AP-Debug]   (director has null RadiantSlimesModel)"); continue; }
            var savedBags = model.RadiantShuffleBags;
            if (savedBags == null) { log.LogInfo("[AP-Debug]   (RadiantShuffleBags dictionary is null)"); continue; }

            log.LogInfo($"[AP-Debug]   {savedBags.Count} saved bag(s):");
            if (savedBags.Count == 0)
                // Empty is normal for a save that never serialized its bags — say so.
                log.LogInfo("[AP-Debug]     (none persisted yet — normal for a save that has not " +
                            "serialized its bags; live bags are native and not observable here. " +
                            "To confirm radiants spawn, look for 'radiant=True' in [AP-Bag] lines.)");
            foreach (var kvp in savedBags)
            {
                string slimeName = kvp.Key?.name ?? "(null)";
                var bag = kvp.Value;
                if (bag == null) { log.LogInfo($"[AP-Debug]     slime='{slimeName}'  bag=null"); continue; }
                log.LogInfo($"[AP-Debug]     slime='{slimeName}'  currentIndex={bag.CurrentIndex}  radiantAt={bag.RadiantSpawnIndex}  size={bag.Size}");
            }
        }
        log.LogInfo("[AP-Debug] ===== END BAG STATE =====");
    }

    /// <summary>When true, every <c>DrawFromRadiantShuffleBag</c> returns true. In-memory only.</summary>
    public static void SetForceRadiantSpawn(bool enabled)
    {
        // Our own Postfix flag — shipping native code ignores DEBUG_ForceRadiantSpawn.
        Patches.LocationPatches.RadiantDebugFlags.ForceRadiantSpawn = enabled;
        Logger.Info($"[AP-Debug] SetForceRadiantSpawn: ForceRadiantSpawn = {enabled}");
    }

    // -------------------------------------------------------------------------
    // Weather dumps
    // -------------------------------------------------------------------------

    /// <summary>
    /// Logs the live WeatherRegistry: forecast timing, and per zone its eligible and running
    /// patterns, forecast, and <c>NextChangeAt</c>. (<see cref="DumpWeatherPatterns"/> shows the
    /// static graphs.)
    /// </summary>
    public static void DumpWeatherRegistry()
    {
        var log = Plugin.Instance.Log;
        log.LogInfo("[AP-Dump] ======= WEATHER REGISTRY DUMP START =======");

        var registries = Resources.FindObjectsOfTypeAll<WeatherRegistry>();
        if (registries.Count == 0)
        {
            log.LogWarning("[AP-Dump] WeatherRegistry not found — load a save first (registry is scene-level)");
            log.LogInfo("[AP-Dump] ======== WEATHER REGISTRY DUMP END ========");
            return;
        }

        var registry = registries[0];
        log.LogInfo($"[AP-Dump] WeatherRegistry found: '{registry.gameObject?.name ?? "(no go)"}'");

        // ── Global forecast timing ─────────────────────────────────────────────
        try
        {
            float daysToForecast = registry.DaysToForecast;
            float intervalLow    = registry.ForecastHourIntervalLow;
            float intervalHigh   = registry.ForecastHourIntervalHigh;
            log.LogInfo($"[AP-Dump] DaysToForecast={daysToForecast:F2}  " +
                        $"ForecastHourInterval=[{intervalLow:F2}, {intervalHigh:F2}]");
        }
        catch (Exception ex) { log.LogWarning($"[AP-Dump] Timing fields error: {ex.Message}"); }

        // ── Zone config list ───────────────────────────────────────────────────
        try
        {
            var cfgList = registry.ZoneConfigList;
            int cfgCount = cfgList?.Count ?? 0;
            log.LogInfo($"[AP-Dump] ZoneConfigList count: {cfgCount}");
            if (cfgList != null)
            {
                for (int i = 0; i < cfgList.Count; i++)
                {
                    var cfg = cfgList[i];
                    string zoneName = cfg?.Zone?.name ?? "(null)";
                    int patCount = cfg?.Patterns?.Count ?? 0;
                    log.LogInfo($"[AP-Dump]   config[{i}] zone='{zoneName}'  eligiblePatterns={patCount}");
                }
            }
        }
        catch (Exception ex) { log.LogWarning($"[AP-Dump] ZoneConfigList error: {ex.Message}"); }

        // ── Per-zone runtime state ─────────────────────────────────────────────
        Il2CppSystem.Collections.Generic.List<ZoneDefinition>? weatherZones = null;
        try { weatherZones = registry.DEBUG_GetWeatherZones(); }
        catch (Exception ex) { log.LogWarning($"[AP-Dump] DEBUG_GetWeatherZones error: {ex.Message}"); }

        if (weatherZones == null || weatherZones.Count == 0)
        {
            log.LogWarning("[AP-Dump] No weather zones returned — zone streaming may not have loaded the weather director yet.");
            log.LogInfo("[AP-Dump] ======== WEATHER REGISTRY DUMP END ========");
            return;
        }

        log.LogInfo($"[AP-Dump] Weather zones ({weatherZones.Count}):");

        Il2CppSystem.Collections.Generic.Dictionary<ZoneDefinition, WeatherRegistry.ZoneWeatherData>? zoneDict = null;
        try { zoneDict = registry._zones; }
        catch (Exception ex) { log.LogWarning($"[AP-Dump] _zones field error: {ex.Message}"); }

        for (int zi = 0; zi < weatherZones.Count; zi++)
        {
            var zone = weatherZones[zi];
            if (zone == null) { log.LogInfo($"[AP-Dump]   Zone[{zi}] NULL"); continue; }
            string zoneName = zone.name ?? $"Zone[{zi}]";

            double nextChangeAt = -1;
            bool   forecastUnlocked = false;
            try
            {
                if (zoneDict != null && zoneDict.ContainsKey(zone))
                {
                    var zd = zoneDict[zone];
                    nextChangeAt    = zd.NextChangeAt;
                    forecastUnlocked = zd.ForecastUnlocked;
                }
            }
            catch { /* guard */ }

            log.LogInfo($"[AP-Dump]   Zone '{zoneName}'  NextChangeAt={nextChangeAt:F4}  ForecastUnlocked={forecastUnlocked}");

            try
            {
                var eligible = registry.DEBUG_GetZoneWeathers(zone);
                int ec = eligible?.Count ?? 0;
                if (ec == 0)
                {
                    log.LogInfo($"[AP-Dump]     Eligible patterns: (none)");
                }
                else
                {
                    var names = new System.Collections.Generic.List<string>(ec);
                    for (int i = 0; i < ec; i++) names.Add(eligible![i]?.name ?? "(null)");
                    log.LogInfo($"[AP-Dump]     Eligible patterns ({ec}): {string.Join(", ", names)}");
                }
            }
            catch (Exception ex) { log.LogWarning($"[AP-Dump]     DEBUG_GetZoneWeathers error: {ex.Message}"); }

            try
            {
                var running = registry.DEBUG_GetRunningWeather(zone);
                int rc = running?.Count ?? 0;
                if (rc == 0)
                {
                    log.LogInfo($"[AP-Dump]     Running: (clear — no active weather)");
                }
                else
                {
                    for (int i = 0; i < rc; i++)
                    {
                        var rp = running![i];
                        if (rp == null) continue;
                        string patName = "(unknown)";
                        try { patName = rp.TryCast<WeatherPatternDefinition>()?.name ?? "(IWeatherPattern — not a def)"; } catch { }
                        log.LogInfo($"[AP-Dump]     Running[{i}]: '{patName}'");
                    }
                }
            }
            catch (Exception ex) { log.LogWarning($"[AP-Dump]     DEBUG_GetRunningWeather error: {ex.Message}"); }

            try
            {
                var forecast = registry.DEBUG_GetForecast(zone);
                int fc = forecast?.Count ?? 0;
                log.LogInfo($"[AP-Dump]     Forecast: {fc} scheduled entries");
            }
            catch (Exception ex) { log.LogWarning($"[AP-Dump]     DEBUG_GetForecast error: {ex.Message}"); }
        }

        log.LogInfo("[AP-Dump] ======== WEATHER REGISTRY DUMP END ========");
    }

    /// <summary>
    /// Logs the condition tree of <c>_allowRadiantSlimesToSpawnQuery</c> — the master gate for
    /// radiant spawns — beside the sanctuary pedia entry and the cached gate state.
    /// </summary>
    public static void DumpAllowRadiantQuery()
    {
        var log = Plugin.Instance.Log;
        log.LogInfo("[AP-Dump] ========== ALLOW RADIANT SPAWN QUERY DUMP ==========");

        // ── Find the director ────────────────────────────────────────────────
        var directors = Resources.FindObjectsOfTypeAll<RadiantSlimeDirector>();
        if (directors.Count == 0)
        {
            log.LogWarning("[AP-Dump] RadiantSlimeDirector not found — load a save first");
            log.LogInfo("[AP-Dump] =========== ALLOW RADIANT SPAWN QUERY DUMP END ===========");
            return;
        }

        var director = directors[0];

        // Expected to appear as a DataKey in the query.
        var sanctuaryEntry = director._sanctuaryPediaEntry;
        log.LogInfo($"[AP-Dump] Director._sanctuaryPediaEntry  name='{sanctuaryEntry?.name ?? "(null)"}'");
        log.LogInfo($"[AP-Dump] Director._isRadiantSpawnAllowedCached={director._isRadiantSpawnAllowedCached}");

        // ── Get the config + query ────────────────────────────────────────────
        var config = director._radiantSlimeConfig;
        if (config == null)
        {
            log.LogWarning("[AP-Dump] Director._radiantSlimeConfig is null");
            log.LogInfo("[AP-Dump] =========== ALLOW RADIANT SPAWN QUERY DUMP END ===========");
            return;
        }
        log.LogInfo($"[AP-Dump] RadiantSlimeConfig='{config.name}'");

        Query? query = null;
        try   { query = config.AllowRadiantSlimesToSpawnQuery; }
        catch (Exception ex)
        {
            log.LogWarning($"[AP-Dump] AllowRadiantSlimesToSpawnQuery threw: {ex.Message}");
            log.LogInfo("[AP-Dump] =========== ALLOW RADIANT SPAWN QUERY DUMP END ===========");
            return;
        }

        if (query == null)
        {
            log.LogInfo("[AP-Dump] AllowRadiantSlimesToSpawnQuery is null — no gate configured; radiant spawns always allowed");
            log.LogInfo("[AP-Dump] =========== ALLOW RADIANT SPAWN QUERY DUMP END ===========");
            return;
        }

        // ── Overall query state ───────────────────────────────────────────────
        bool satisfied     = false;
        int  childCount    = 0;
        int  satisfiedCount = 0;
        try { satisfied      = query.IsSatisfied();   } catch (Exception ex) { log.LogWarning($"[AP-Dump] IsSatisfied() threw: {ex.Message}"); }
        try { childCount     = query.GetChildCount();  } catch { }
        try { satisfiedCount = query.CountSatisfied(); } catch { }

        log.LogInfo($"[AP-Dump] Query='{query.name}'  satisfied={satisfied}  topLevelChildren={childCount}  satisfiedChildren={satisfiedCount}");

        // ── Walk condition tree ───────────────────────────────────────────────
        var root = query._root;
        if (root == null)
        {
            log.LogInfo("[AP-Dump] Query._root is null — query has no conditions (always satisfied)");
            log.LogInfo("[AP-Dump] =========== ALLOW RADIANT SPAWN QUERY DUMP END ===========");
            return;
        }

        string opStr = "(unknown)";
        try { opStr = root._operation.ToString(); } catch { }
        var children = root._children;
        int rootChildCount = children?.Count ?? 0;
        log.LogInfo($"[AP-Dump] Root: operation={opStr}  children={rootChildCount}");

        if (rootChildCount > 0)
        {
            // No conversation GUIDs to resolve here.
            LogQueryChildren(log, children!, "  ", new Dictionary<string, string>());
        }
        else
        {
            log.LogInfo("[AP-Dump]   (root has no children — query is always satisfied)");
        }

        log.LogInfo("[AP-Dump] =========== ALLOW RADIANT SPAWN QUERY DUMP END ===========");
    }

    // -------------------------------------------------------------------------
    // Gold / Lucky slime spawn weight tools
    // -------------------------------------------------------------------------

    private static readonly string[] GoldLuckyNames = { "Gold", "Lucky" };

    /// <summary>Logs every Gold/Lucky spawner member: spawner path, weight, and 1/X chance.</summary>
    public static void DumpGoldLuckySpawnWeights()
    {
        var log = Plugin.Instance.Log;
        var spawners = Resources.FindObjectsOfTypeAll<DirectedActorSpawner>();

        log.LogInfo($"[AP-Dump] ===== GOLD / LUCKY SPAWN WEIGHTS ({spawners.Count} total spawners) =====");

        int hits = 0;
        foreach (var spawner in spawners)
        {
            if (spawner == null) continue;
            var constraints = spawner.Constraints;
            if (constraints == null) continue;

            for (int ci = 0; ci < constraints.Length; ci++)
            {
                var constraint = constraints[ci];
                if (constraint == null) continue;
                var slimeset = constraint.Slimeset;
                if (slimeset == null) continue;
                var members = slimeset.Members;
                if (members == null) continue;

                float totalWeight = 0f;
                int memberCount = 0;
                for (int mi = 0; mi < members.Length; mi++)
                {
                    var m = members[mi];
                    if (m == null) continue;
                    totalWeight += m.Weight;
                    memberCount++;
                }

                for (int mi = 0; mi < members.Length; mi++)
                {
                    var member = members[mi];
                    if (member == null) continue;
                    string identName = member.IdentType?.name ?? "(null)";
                    bool isTarget = false;
                    foreach (var keyword in GoldLuckyNames)
                        if (identName.IndexOf(keyword, StringComparison.OrdinalIgnoreCase) >= 0)
                        { isTarget = true; break; }
                    if (!isTarget) continue;

                    // 1/X, with a decimal only when X isn't near a whole number.
                    string chanceStr;
                    if (totalWeight > 0f && member.Weight > 0f)
                    {
                        float x = totalWeight / member.Weight;
                        float rounded = MathF.Round(x);
                        chanceStr = MathF.Abs(x - rounded) < 0.05f
                            ? $"1/{(int)rounded}"
                            : $"1/{x:F1}";
                    }
                    else
                    {
                        chanceStr = "1/?";
                    }

                    string path = GetGameObjectPath(spawner.gameObject);
                    log.LogInfo($"[AP-Dump]   ident='{identName}'  chance={chanceStr}  (memberWeight={member.Weight}  poolTotal={totalWeight:F3}  members={memberCount})  path='{path}'");
                    hits++;
                }
            }
        }

        if (hits == 0)
            log.LogInfo("[AP-Dump]   (no Gold/Lucky entries found — load a save with the relevant zone loaded)");

        log.LogInfo("[AP-Dump] ===== END GOLD / LUCKY SPAWN WEIGHTS =====");
    }

    /// <summary>
    /// Sets every Gold/Lucky member's weight (relative within its set; vanilla is ~1–5 vs 50–200+
    /// for commons, so 1000 makes nearly every spawn one). In-memory only.
    /// </summary>
    public static void SetGoldLuckySpawnWeight(float weight)
    {
        var log = Plugin.Instance.Log;
        var spawners = Resources.FindObjectsOfTypeAll<DirectedActorSpawner>();

        int changed = 0;
        foreach (var spawner in spawners)
        {
            if (spawner == null) continue;
            var constraints = spawner.Constraints;
            if (constraints == null) continue;

            for (int ci = 0; ci < constraints.Length; ci++)
            {
                var constraint = constraints[ci];
                if (constraint == null) continue;
                var slimeset = constraint.Slimeset;
                if (slimeset == null) continue;
                var members = slimeset.Members;
                if (members == null) continue;

                for (int mi = 0; mi < members.Length; mi++)
                {
                    var member = members[mi];
                    if (member == null) continue;
                    string identName = member.IdentType?.name ?? "";
                    bool isTarget = false;
                    foreach (var keyword in GoldLuckyNames)
                        if (identName.IndexOf(keyword, StringComparison.OrdinalIgnoreCase) >= 0)
                        { isTarget = true; break; }
                    if (!isTarget) continue;

                    member.Weight = weight;
                    changed++;
                }
            }
        }

        if (changed == 0)
            log.LogWarning($"[AP-Debug] SetGoldLuckySpawnWeight({weight}): no Gold/Lucky entries found — load a save with the relevant zone loaded");
        else
            log.LogInfo($"[AP-Debug] SetGoldLuckySpawnWeight: set {changed} member weight(s) to {weight}");
    }

    /// <summary>
    /// Logs active spawners with a feral constraint, and every <c>_onlySpawnRadiant</c> spawner,
    /// with their radiant flags (<c>_blockRadiantSpawning</c>, <c>_onlySpawnRadiant</c>,
    /// <c>_radiantChanceOverride</c> — non-zero overrides the bag).
    /// </summary>
    public static void DumpFeralSpawners()
    {
        var log = Plugin.Instance.Log;
        var spawners = Resources.FindObjectsOfTypeAll<DirectedActorSpawner>();

        // Active only — FindObjectsOfTypeAll also returns other resident zones.
        int activeCount = 0;
        for (int i = 0; i < spawners.Count; i++)
            if (spawners[i] != null && spawners[i].gameObject.activeInHierarchy) activeCount++;

        log.LogInfo($"[AP-Dump] ===== FERAL SPAWNER RADIANT FLAGS ({activeCount} active / {spawners.Count} total spawners) =====");

        int feralCount = 0;
        foreach (var spawner in spawners)
        {
            if (spawner == null) continue;
            if (!spawner.gameObject.activeInHierarchy) continue;  // skip other streamed zones
            var constraints = spawner.Constraints;
            if (constraints == null) continue;

            bool hasFeral = false;
            for (int ci = 0; ci < constraints.Length; ci++)
            {
                var c = constraints[ci];
                if (c != null && c.Feral) { hasFeral = true; break; }
            }
            if (!hasFeral) continue;

            bool blockRadiant    = spawner._blockRadiantSpawning;
            bool onlyRadiant     = spawner._onlySpawnRadiant;
            float chanceOverride = spawner._radiantChanceOverride;
            string path          = GetGameObjectPath(spawner.gameObject);

            string radiantStatus;
            if (blockRadiant)
                radiantStatus = "BLOCKED (never radiant)";
            else if (onlyRadiant)
                radiantStatus = "FORCED (always radiant)";
            else if (chanceOverride != 0f && chanceOverride != 1f)
                radiantStatus = $"override={chanceOverride:F4} (non-default — may affect bag)";
            else if (chanceOverride == 1f)
                radiantStatus = $"override=1.0 (default — same as all zones, no effect observed)";
            else
                radiantStatus = "normal bag draw (override=0)";

            log.LogInfo($"[AP-Dump]   radiant={radiantStatus}  path='{path}'");
            feralCount++;
        }

        if (feralCount == 0)
            log.LogInfo("[AP-Dump]   (no feral spawners found — load a save in a zone with feral slimes)");

        // ── Second pass: every active _onlySpawnRadiant spawner, feral or not ──
        log.LogInfo("[AP-Dump] --- Guaranteed Radiant Spawners (_onlySpawnRadiant=true, active in scene) ---");
        int guaranteedCount = 0;
        foreach (var spawner in spawners)
        {
            if (spawner == null) continue;
            if (!spawner.gameObject.activeInHierarchy) continue;  // skip spawners from other streamed zones
            if (!spawner._onlySpawnRadiant) continue;

            string path = GetGameObjectPath(spawner.gameObject);

            var constraints = spawner.Constraints;
            var slimeNames = new List<string>();
            if (constraints != null)
            {
                for (int ci = 0; ci < constraints.Length; ci++)
                {
                    var c = constraints[ci];
                    if (c?.Slimeset?.Members == null) continue;
                    for (int mi = 0; mi < c.Slimeset.Members.Length; mi++)
                    {
                        var m = c.Slimeset.Members[mi];
                        if (m?.IdentType != null) slimeNames.Add(m.IdentType.name);
                    }
                }
            }

            string slimeList = slimeNames.Count > 0 ? string.Join(", ", slimeNames) : "(no slimeset)";
            log.LogInfo($"[AP-Dump]   GUARANTEED RADIANT  path='{path}'  slimes=[{slimeList}]");
            guaranteedCount++;
        }

        if (guaranteedCount == 0)
            log.LogInfo("[AP-Dump]   (none found in current scene)");

        log.LogInfo("[AP-Dump] ===== END FERAL SPAWNER RADIANT FLAGS =====");
    }

    /// <summary>
    /// Logs every weather pattern's transitions (<c>ChancePerHour</c> per in-game hour) and each
    /// state's <c>MinDurationHours</c> (before any outbound transition) and <c>MapTier</c>.
    /// </summary>
    public static void DumpWeatherPatterns()
    {
        var log = Plugin.Instance.Log;
        log.LogInfo("[AP-Dump] ======= WEATHER PATTERN DUMP START =======");

        // ── WeatherStateDefinition timing ──────────────────────────────────────
        var allStates = Resources.FindObjectsOfTypeAll<WeatherStateDefinition>();
        log.LogInfo($"[AP-Dump] WeatherStateDefinitions ({allStates.Count} total):");
        for (int i = 0; i < allStates.Count; i++)
        {
            var s = allStates[i];
            if (s == null) continue;
            log.LogInfo($"[AP-Dump]   State '{s.StateName ?? s.name}'  " +
                        $"MinDurationHours={s.MinDurationHours:F4}  MapTier={s.MapTier}");
        }

        // ── WeatherPatternDefinition transition graph ──────────────────────────
        var allPatterns = Resources.FindObjectsOfTypeAll<WeatherPatternDefinition>();
        log.LogInfo($"[AP-Dump] WeatherPatternDefinitions ({allPatterns.Count} total):");
        for (int pi = 0; pi < allPatterns.Count; pi++)
        {
            var pattern = allPatterns[pi];
            if (pattern == null) continue;
            string patName = pattern.name ?? $"Pattern[{pi}]";
            log.LogInfo($"[AP-Dump]   Pattern '{patName}':");

            var transitionLists = pattern.RunningTransitions;
            if (transitionLists == null || transitionLists.Count == 0)
            {
                log.LogInfo($"[AP-Dump]     (no transitions)");
                continue;
            }

            for (int ti = 0; ti < transitionLists.Count; ti++)
            {
                var tl = transitionLists[ti];
                if (tl == null) continue;
                string fromName = tl.FromState?.StateName ?? tl.FromState?.name ?? "(null)";

                var transitions = tl.Transitions;
                if (transitions == null || transitions.Count == 0)
                {
                    log.LogInfo($"[AP-Dump]     '{fromName}' → (no outbound transitions)");
                    continue;
                }

                for (int tti = 0; tti < transitions.Count; tti++)
                {
                    var t = transitions[tti];
                    if (t == null) continue;
                    string toName = t.ToState?.StateName ?? t.ToState?.name ?? "(null)";
                    float minDur  = t.ToState?.MinDurationHours ?? 0f;
                    int   mapTier = t.ToState?.MapTier ?? 0;
                    log.LogInfo($"[AP-Dump]     '{fromName}' → '{toName}'  " +
                                $"ChancePerHour={t.ChancePerHour:F4}  " +
                                $"(MinDurationHours={minDur:F4}  MapTier={mapTier})");
                }
            }
        }

        log.LogInfo("[AP-Dump] ======== WEATHER PATTERN DUMP END ========");
    }

    /// <summary>
    /// Logs slime spawners grouped by zone and cell: each spawner's share of its cell's directed
    /// weight, delay factor, constraint chances (feral, time window), and per-slime chance.
    /// </summary>
    public static void DumpSlimeSpawnWeights()
    {
        var log      = Plugin.Instance.Log;
        var allSpawners = Resources.FindObjectsOfTypeAll<DirectedActorSpawner>();

        // ── Group by (scene, cell): a cell's spawners share one directed-weight pool ──────────
        var byCell = new System.Collections.Generic.Dictionary<
            (string scene, string cell),
            List<DirectedActorSpawner>>();

        for (int si = 0; si < allSpawners.Count; si++)
        {
            var s = allSpawners[si];
            if (s == null) continue;
            // Skip prefab templates (no scene). Inactive spawners are kept — they belong to
            // resident zones not streamed in.
            if (string.IsNullOrEmpty(s.gameObject.scene.name)) continue;

            var cons = s.Constraints;
            if (cons == null || cons.Length == 0) continue;

            string scene = s.gameObject.scene.name ?? "unknown";
            string cell  = CellAncestorName(s.gameObject);   // first "cell*" ancestor = pool boundary
            var key = (scene, cell);
            if (!byCell.TryGetValue(key, out var lst))
            {
                lst = new List<DirectedActorSpawner>();
                byCell[key] = lst;
            }
            lst.Add(s);
        }

        int totalSpawners = 0;
        foreach (var kvp in byCell) totalSpawners += kvp.Value.Count;

        // Raw count includes prefab templates; totalSpawners is world spawners only.
        log.LogInfo($"[AP-Dump] ======= SLIME SPAWN WEIGHTS" +
                    $"  raw={allSpawners.Count}" +
                    $"  world-placed={totalSpawners}" +
                    $"  cells={byCell.Count} =======");

        // ── Output by zone → cell, spawners by descending chance ────────
        var cellKeys = new List<(string scene, string cell)>(byCell.Keys);
        cellKeys.Sort((a, b) =>
        {
            int cmp = string.Compare(SceneToZoneName(a.scene), SceneToZoneName(b.scene), StringComparison.Ordinal);
            if (cmp != 0) return cmp;
            return string.Compare(a.cell, b.cell, StringComparison.Ordinal);
        });

        string lastZone = "";
        foreach (var key in cellKeys)
        {
            string zoneName = SceneToZoneName(key.scene);
            if (zoneName != lastZone)
            {
                log.LogInfo($"[AP-Dump]");
                log.LogInfo($"[AP-Dump] ══ {zoneName}  (scene prefix: {key.scene}) ══════════════════════");
                lastZone = zoneName;
            }

            var list = byCell[key];

            float cellTotal = 0f;
            for (int i = 0; i < list.Count; i++)
            {
                try { cellTotal += list[i].DirectedSpawnWeight; } catch { }
            }

            list.Sort((a, b) =>
            {
                float wa = 0f, wb = 0f;
                try { wa = a.DirectedSpawnWeight; } catch { }
                try { wb = b.DirectedSpawnWeight; } catch { }
                return wb.CompareTo(wa);
            });

            log.LogInfo($"[AP-Dump]   ── Cell: {key.cell}  ({list.Count} spawn point(s)  cellTotal={cellTotal:F1})");

            foreach (var spawner in list)
            {
                float directedWeight = 0f, delayFactor = 1f;
                try { directedWeight = spawner.DirectedSpawnWeight; } catch { }
                try { delayFactor    = spawner.SpawnDelayFactor;    } catch { }

                float spawnChancePct = cellTotal > 0f ? directedWeight / cellTotal * 100f : 0f;

                string radiantTag = spawner._onlySpawnRadiant     ? " [GUARANTEED RADIANT]"
                                  : spawner._blockRadiantSpawning ? " [RADIANT BLOCKED]"
                                  : "";

                string spawnName = SpawnerShortName(spawner);
                log.LogInfo($"[AP-Dump]     {spawnName}" +
                            $"  spawnChance={spawnChancePct:F1}%" +
                            $"  delayFactor={delayFactor:F1}x" +
                            radiantTag);

                var constraints = spawner.Constraints;

                float totalConstraintWeight = 0f;
                for (int ci = 0; ci < constraints.Length; ci++)
                    if (constraints[ci] != null) totalConstraintWeight += constraints[ci].Weight;

                for (int ci = 0; ci < constraints.Length; ci++)
                {
                    var constraint = constraints[ci];
                    if (constraint == null) continue;
                    var slimeset = constraint.Slimeset;
                    if (slimeset?.Members == null || slimeset.Members.Length == 0) continue;

                    bool feral = false;
                    try { feral = constraint.Feral; } catch { }

                    string timeStr = "ANY";
                    try { timeStr = constraint.Window?.TimeMode.ToString() ?? "ANY"; } catch { }

                    string constraintPct = "";
                    if (constraints.Length > 1 && totalConstraintWeight > 0f)
                        constraintPct = $"  ({constraint.Weight / totalConstraintWeight * 100f:F0}% of spawns)";

                    log.LogInfo($"[AP-Dump]       constraint[{ci}]  feral={feral}  time={timeStr}{constraintPct}");

                    float totalMemberWeight = 0f;
                    for (int mi = 0; mi < slimeset.Members.Length; mi++)
                        if (slimeset.Members[mi] != null) totalMemberWeight += slimeset.Members[mi].Weight;

                    var sorted = new List<(string name, float pct)>();
                    for (int mi = 0; mi < slimeset.Members.Length; mi++)
                    {
                        var m = slimeset.Members[mi];
                        if (m == null) continue;
                        float pct = totalMemberWeight > 0f ? m.Weight / totalMemberWeight * 100f : 0f;
                        sorted.Add((m.IdentType?.name ?? "(null)", pct));
                    }
                    sorted.Sort((a, b) => b.pct.CompareTo(a.pct));

                    foreach (var (name, pct) in sorted)
                        log.LogInfo($"[AP-Dump]         {name.PadRight(26)} {pct:F1}%");
                }
            }
        }

        log.LogInfo("[AP-Dump] ======= END SLIME SPAWN WEIGHTS =======");
    }

    /// <summary>
    /// The nearest "cell*" ancestor (where the CellDirector lives — its spawners' weights sum to
    /// the cell total), else the root name.
    /// </summary>
    private static string CellAncestorName(GameObject go)
    {
        var t = go.transform;
        Transform? root = null;
        while (t != null)
        {
            if (t.gameObject.name.StartsWith("cell", StringComparison.OrdinalIgnoreCase))
                return t.gameObject.name;
            root = t;
            t = t.parent;
        }
        return root?.gameObject.name ?? go.name;
    }

    /// <summary>Maps a scene name to a human-readable SR2 zone display name.</summary>
    private static string SceneToZoneName(string sceneName)
    {
        if (sceneName.IndexOf("Gorge",       StringComparison.OrdinalIgnoreCase) >= 0) return "Ember Valley";
        if (sceneName.IndexOf("Bluffs",      StringComparison.OrdinalIgnoreCase) >= 0) return "Powderfall Bluffs";
        if (sceneName.IndexOf("Strand",      StringComparison.OrdinalIgnoreCase) >= 0) return "Starlight Strand";
        if (sceneName.IndexOf("Sands",       StringComparison.OrdinalIgnoreCase) >= 0) return "Shimmering Sands";
        if (sceneName.IndexOf("Labyrinth",   StringComparison.OrdinalIgnoreCase) >= 0 ||
            sceneName.IndexOf("LavaDepths",  StringComparison.OrdinalIgnoreCase) >= 0 ||
            sceneName.IndexOf("Dreamland",   StringComparison.OrdinalIgnoreCase) >= 0 ||
            sceneName.IndexOf("LabValley",   StringComparison.OrdinalIgnoreCase) >= 0) return "Grey Labyrinth";
        if (sceneName.IndexOf("Fields",      StringComparison.OrdinalIgnoreCase) >= 0) return "Rainbow Fields";
        if (sceneName.IndexOf("Ranch",        StringComparison.OrdinalIgnoreCase) >= 0 ||
            sceneName.IndexOf("Conservatory", StringComparison.OrdinalIgnoreCase) >= 0 ||
            sceneName.IndexOf("Home",         StringComparison.OrdinalIgnoreCase) >= 0) return "The Ranch";
        return sceneName;   // unknown zone — return raw scene name
    }

    /// <summary>Returns a compact 2–3 level path for a spawner's GameObject.</summary>
    private static string SpawnerShortName(DirectedActorSpawner spawner)
    {
        string name = spawner.gameObject.name;
        var parent = spawner.transform.parent;
        if (parent != null)
        {
            name = parent.gameObject.name + "/" + name;
            var grandparent = parent.parent;
            if (grandparent != null && grandparent.parent != null)
                name = grandparent.gameObject.name + "/" + name;
        }
        return name;
    }

    /// <summary>
    /// Merges the current zone's spawn rates into <c>BepInEx/spawn_rates.json</c> (this run's
    /// scenes replace their old entries), plus every scene seen so far for the HTML viewer's
    /// coverage panel.
    /// </summary>
    public static void ExportSpawnRatesJson()
    {
        var log = Plugin.Instance.Log;
        var allSpawners = Resources.FindObjectsOfTypeAll<DirectedActorSpawner>();

        // ── One row per (zone, cell, spawner, constraint, slime), grouped as in DumpSlimeSpawnWeights ──
        var byScene = new System.Collections.Generic.Dictionary<
            string,
            System.Collections.Generic.List<System.Collections.Generic.Dictionary<string, object>>>();

        var byCellForWeights = new System.Collections.Generic.Dictionary<
            (string scene, string cell), (float total, System.Collections.Generic.List<DirectedActorSpawner> spawners)>();

        for (int si = 0; si < allSpawners.Count; si++)
        {
            var s = allSpawners[si];
            if (s == null) continue;
            if (string.IsNullOrEmpty(s.gameObject.scene.name)) continue;
            var cons = s.Constraints;
            if (cons == null || cons.Length == 0) continue;

            string scene = s.gameObject.scene.name;
            string cell  = CellAncestorName(s.gameObject);
            var key = (scene, cell);
            if (!byCellForWeights.TryGetValue(key, out var entry))
                entry = (0f, new System.Collections.Generic.List<DirectedActorSpawner>());
            float w = 0f; try { w = s.DirectedSpawnWeight; } catch { }
            byCellForWeights[key] = (entry.total + w, entry.spawners);
            entry.spawners.Add(s);
            byCellForWeights[key] = (entry.total + w, entry.spawners);
        }

        // Second pass: build rows now that cell totals are known
        var rowsByScene = new System.Collections.Generic.Dictionary<
            string,
            System.Collections.Generic.List<object>>();

        foreach (var kvp in byCellForWeights)
        {
            string scene     = kvp.Key.scene;
            string cell      = kvp.Key.cell;
            float  cellTotal = kvp.Value.total;
            string zoneName  = SceneToZoneName(scene);

            if (!rowsByScene.TryGetValue(scene, out var sceneRows))
            {
                sceneRows = new System.Collections.Generic.List<object>();
                rowsByScene[scene] = sceneRows;
            }

            foreach (var spawner in kvp.Value.spawners)
            {
                float dirW = 0f, delay = 1f;
                try { dirW  = spawner.DirectedSpawnWeight; } catch { }
                try { delay = spawner.SpawnDelayFactor;    } catch { }
                float spawnChancePct = cellTotal > 0f ? dirW / cellTotal * 100f : 0f;
                bool  radOnly    = spawner._onlySpawnRadiant;
                bool  radBlocked = spawner._blockRadiantSpawning;
                string spawnerPath = SpawnerShortName(spawner);
                var pos = spawner.transform.position;

                var constraints = spawner.Constraints;
                float totalCW = 0f;
                for (int ci = 0; ci < constraints.Length; ci++)
                    if (constraints[ci] != null) totalCW += constraints[ci].Weight;

                for (int ci = 0; ci < constraints.Length; ci++)
                {
                    var constraint = constraints[ci];
                    if (constraint == null) continue;
                    var slimeset = constraint.Slimeset;
                    if (slimeset?.Members == null || slimeset.Members.Length == 0) continue;

                    bool feral = false;
                    try { feral = constraint.Feral; } catch { }
                    string time = "ANY";
                    try { time = constraint.Window?.TimeMode.ToString() ?? "ANY"; } catch { }
                    float cPct = constraints.Length > 1 && totalCW > 0f
                        ? constraint.Weight / totalCW * 100f : 100f;

                    float totalMW = 0f;
                    for (int mi = 0; mi < slimeset.Members.Length; mi++)
                        if (slimeset.Members[mi] != null) totalMW += slimeset.Members[mi].Weight;

                    for (int mi = 0; mi < slimeset.Members.Length; mi++)
                    {
                        var m = slimeset.Members[mi];
                        if (m == null) continue;
                        float slimePct = totalMW > 0f ? m.Weight / totalMW * 100f : 0f;
                        string slimeName = m.IdentType?.name ?? "(null)";

                        // Hand-rolled JSON — no Json.NET dependency
                        sceneRows.Add(new {
                            zone        = zoneName,
                            scene       = scene,
                            cell        = cell,
                            spawner     = spawnerPath,
                            x           = System.Math.Round(pos.x, 2),
                            y           = System.Math.Round(pos.y, 2),
                            z           = System.Math.Round(pos.z, 2),
                            spawnChance = System.Math.Round(spawnChancePct, 2),
                            delayFactor = System.Math.Round(delay, 2),
                            radiantOnly = radOnly,
                            radiantBlocked = radBlocked,
                            cIdx        = ci,
                            feral       = feral,
                            time        = time,
                            cPct        = System.Math.Round(cPct, 1),
                            slime       = slimeName,
                            slimePct    = System.Math.Round(slimePct, 2),
                        });
                    }
                }
            }
        }

        // ── Coverage: every scene seen across runs (build settings list only "Bootstrap") ───────

        // ── Read existing JSON file (if any), merge, write back ───────────────
        string jsonPath = System.IO.Path.Combine(BepInEx.Paths.BepInExRootPath, "spawn_rates.json");

        // We wrote the file ourselves, so merging only needs to find our own keys.
        var existingSceneData = new System.Collections.Generic.Dictionary<string, string>();
        if (System.IO.File.Exists(jsonPath))
        {
            try
            {
                string existing = System.IO.File.ReadAllText(jsonPath);
                string trimmed = existing.Trim();
                if (trimmed.StartsWith("{") && trimmed.EndsWith("}"))
                    existingSceneData["__raw__"] = trimmed.Substring(1, trimmed.Length - 2).Trim();
            }
            catch { /* corrupt file — start fresh */ }
        }

        // ── Extract kept existing scene entries FIRST (needed for allSeenScenes) ─
        var existingParts = new System.Collections.Generic.List<string>();
        if (existingSceneData.TryGetValue("__raw__", out string? raw) && !string.IsNullOrEmpty(raw))
        {
            // scene_rows is { "zoneName": [ ... ], ... } — one entry per scene.
            string existingFull = System.IO.File.Exists(jsonPath)
                ? System.IO.File.ReadAllText(jsonPath) : "";
            int srStart = existingFull.IndexOf("\"scene_rows\":", StringComparison.Ordinal);
            if (srStart >= 0)
            {
                // Find the opening { of scene_rows value
                int braceOpen = existingFull.IndexOf('{', srStart + 13);
                if (braceOpen >= 0)
                {
                    // Walk brackets to find matching close
                    int depth2 = 0, pos = braceOpen;
                    while (pos < existingFull.Length)
                    {
                        char ch = existingFull[pos];
                        if (ch == '{') depth2++;
                        else if (ch == '}') { depth2--; if (depth2 == 0) break; }
                        pos++;
                    }
                    string sceneRowsContent = existingFull.Substring(braceOpen + 1, pos - braceOpen - 1).Trim();
                    // Keep each top-level "key": [ ... ] entry this run isn't overwriting.
                    var scenesToOverwrite = new System.Collections.Generic.HashSet<string>(rowsByScene.Keys);
                    int idx2 = 0;
                    while (idx2 < sceneRowsContent.Length)
                    {
                        // Find next quoted key
                        int keyStart = sceneRowsContent.IndexOf('"', idx2);
                        if (keyStart < 0) break;
                        int keyEnd = sceneRowsContent.IndexOf('"', keyStart + 1);
                        if (keyEnd < 0) break;
                        string sceneKey = sceneRowsContent.Substring(keyStart + 1, keyEnd - keyStart - 1);
                        // Find the colon then the opening [
                        int colon = sceneRowsContent.IndexOf(':', keyEnd);
                        if (colon < 0) break;
                        int arrOpen = sceneRowsContent.IndexOf('[', colon);
                        if (arrOpen < 0) break;
                        // Walk to matching ]
                        int d = 0, p = arrOpen;
                        while (p < sceneRowsContent.Length)
                        {
                            char c = sceneRowsContent[p];
                            if (c == '[') d++;
                            else if (c == ']') { d--; if (d == 0) break; }
                            p++;
                        }
                        string entryBlock = sceneRowsContent.Substring(keyStart, p - keyStart + 1);
                        if (!scenesToOverwrite.Contains(sceneKey))
                            existingParts.Add("    " + entryBlock.Trim());
                        idx2 = p + 1;
                        // Skip trailing comma/whitespace
                        while (idx2 < sceneRowsContent.Length &&
                               (sceneRowsContent[idx2] == ',' || sceneRowsContent[idx2] == '\r' ||
                                sceneRowsContent[idx2] == '\n' || sceneRowsContent[idx2] == ' '))
                            idx2++;
                    }
                }
            }
        }

        // ── Now build the JSON — existingParts is fully populated ────────────
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("{");
        sb.AppendLine($"  \"exported_at\": \"{System.DateTime.UtcNow:O}\",");

        // all_seen_scenes: union of current run + all previously kept scenes
        var allSeenScenes = new System.Collections.Generic.SortedSet<string>(rowsByScene.Keys);
        foreach (var ep in existingParts)
        {
            int q1 = ep.IndexOf('"');
            int q2 = q1 >= 0 ? ep.IndexOf('"', q1 + 1) : -1;
            if (q1 >= 0 && q2 > q1) allSeenScenes.Add(ep.Substring(q1 + 1, q2 - q1 - 1));
        }
        sb.Append("  \"all_seen_scenes\": [");
        bool firstSeen = true;
        foreach (var sn in allSeenScenes)
        {
            if (!firstSeen) sb.Append(", ");
            sb.Append($"\"{JsonEsc(sn)}\"");
            firstSeen = false;
        }
        sb.AppendLine("],");

        // scenes_in_this_run
        var scenesThisRun = new System.Collections.Generic.List<string>(rowsByScene.Keys);
        sb.Append("  \"scenes_in_this_run\": [");
        for (int i = 0; i < scenesThisRun.Count; i++)
        {
            sb.Append($"\"{JsonEsc(scenesThisRun[i])}\"");
            if (i < scenesThisRun.Count - 1) sb.Append(", ");
        }
        sb.AppendLine("],");

        sb.AppendLine("  \"scene_rows\": {");

        // Write kept existing scene entries
        for (int i = 0; i < existingParts.Count; i++)
        {
            sb.Append(existingParts[i]);
            sb.AppendLine(",");
        }

        // Write new/updated scene entries
        int sceneIdx = 0;
        foreach (var sceneKvp in rowsByScene)
        {
            string sceneName = sceneKvp.Key;
            var    rows2     = sceneKvp.Value;

            sb.Append($"    \"{JsonEsc(sceneName)}\": [");
            for (int ri = 0; ri < rows2.Count; ri++)
            {
                if (ri % 5 == 0) sb.AppendLine().Append("      ");
                sb.Append(SerialiseRow(rows2[ri]));
                if (ri < rows2.Count - 1) sb.Append(", ");
            }
            sb.AppendLine();
            sb.Append("    ]");
            if (sceneIdx < rowsByScene.Count - 1) sb.Append(",");
            sb.AppendLine();
            sceneIdx++;
        }

        sb.AppendLine("  }");
        sb.AppendLine("}");

        try
        {
            // UTF8 without BOM — browsers reject BOM in JSON.parse
            System.IO.File.WriteAllText(jsonPath, sb.ToString(),
                new System.Text.UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            int totalRows = 0;
            foreach (var v in rowsByScene.Values) totalRows += v.Count;
            log.LogInfo($"[AP-Export] Wrote {totalRows} rows for {rowsByScene.Count} scene(s)" +
                        $"  (+{existingParts.Count} kept from previous runs)" +
                        $"  total seen scenes: {allSeenScenes.Count}" +
                        $"  → {jsonPath}");
        }
        catch (Exception ex)
        {
            log.LogError($"[AP-Export] Failed to write {jsonPath}: {ex.Message}");
        }
    }

    /// <summary>Wipes <c>BepInEx/spawn_rates.json</c> so the next export starts fresh.</summary>
    public static void ClearSpawnRatesJson()
    {
        string jsonPath = System.IO.Path.Combine(BepInEx.Paths.BepInExRootPath, "spawn_rates.json");
        try
        {
            System.IO.File.Delete(jsonPath);
            Logger.Info($"[AP-Export] Cleared {jsonPath}");
        }
        catch (Exception ex)
        {
            Logger.Warning($"[AP-Export] Could not clear file: {ex.Message}");
        }
    }

    private static string JsonEsc(string s) =>
        s.Replace("\\", "\\\\").Replace("\"", "\\\"");

    private static string SerialiseRow(object row)
    {
        // row is an anonymous type — reflect its properties
        var sb = new System.Text.StringBuilder("{");
        var props = row.GetType().GetProperties();
        for (int i = 0; i < props.Length; i++)
        {
            var prop = props[i];
            sb.Append($"\"{prop.Name}\":");
            object? val = prop.GetValue(row);
            if (val is string s)       sb.Append($"\"{JsonEsc(s)}\"");
            else if (val is bool b)    sb.Append(b ? "true" : "false");
            else if (val is int n)     sb.Append(n);
            else if (val is double d)  sb.Append(d.ToString(System.Globalization.CultureInfo.InvariantCulture));
            else                       sb.Append(val?.ToString() ?? "null");
            if (i < props.Length - 1) sb.Append(",");
        }
        sb.Append("}");
        return sb.ToString();
    }

    private static string GetGameObjectPath(UnityEngine.GameObject go)
    {
        if (go == null) return "(null)";
        string path = go.name;
        var t = go.transform.parent;
        int depth = 0;
        while (t != null && depth++ < 4)
        {
            path = t.gameObject.name + "/" + path;
            t = t.parent;
        }
        return path;
    }

    /// <summary>
    /// Logs every <c>Palette</c> asset (name, GUID, ref id, title, pedia entry), independent of
    /// which shops currently stock them. The GUID matches the shop's <c>AssetGuid</c>.
    /// </summary>
    public static void DumpPalettes()
    {
        var log = Plugin.Instance.Log;
        var all = Resources.FindObjectsOfTypeAll<Il2CppMonomiPark.SlimeRancher.Ranch.Palette>();
        log.LogInfo($"[AP-Dump] ========== PALETTE DUMP ({all.Length}) ==========");
        foreach (var pal in all)
        {
            if (pal == null) continue;
            string name = "?", guid = "?", refId = "?", title = "?", pedia = "(none)";
            try { name  = pal.name ?? "?"; } catch { }
            try { guid  = pal.Guid ?? "?"; } catch { }
            try { refId = pal.ReferenceId ?? "?"; } catch { }
            try { title = pal.DisplayName?.GetLocalizedString() ?? "?"; } catch { }
            try { pedia = pal._pediaEntry != null ? pal._pediaEntry.name : "(none)"; } catch { }
            log.LogInfo(
                $"[AP-Dump] Palette  name='{name}'  title='{title}'  guid='{guid}'  " +
                $"ref='{refId}'  pedia='{pedia}'");
        }
        log.LogInfo("[AP-Dump] =================================");
    }

    /// <summary>
    /// Logs the save-persistent switch registry (<c>GameModel.AllSwitches()</c>) beside the loaded
    /// <c>WorldStatePrimarySwitch</c> objects, mapping registry IDs to the GameObject names
    /// <c>RegionTable</c> uses — so gate state can be read without the scene loaded. Run in
    /// Rainbow Fields, where the EV/SS gate switches are loaded.
    /// </summary>
    public static void DumpWorldSwitches()
    {
        var log = Plugin.Instance.Log;
        log.LogInfo("[AP-Dump] ========== WORLD SWITCH REGISTRY ==========");

        var gameModel = SceneContext.Instance?.GameModel;
        if (gameModel == null)
        {
            log.LogWarning("[AP-Dump] GameModel not available — load a save first.");
            return;
        }

        // GameObject name → definition ID, from the loaded switches.
        var resolved = new Dictionary<string, string>();
        try
        {
            foreach (var sw in Resources.FindObjectsOfTypeAll<WorldStatePrimarySwitch>())
            {
                if (sw == null) continue;
                try
                {
                    var go = sw.gameObject;
                    // Skip prefabs — only scene instances have a model.
                    if (go == null || !go.scene.IsValid()) continue;

                    string defId = sw.SwitchDefinition?.ID ?? "(no definition)";
                    string state = sw._model != null ? sw._model.state.ToString() : "(no model)";
                    log.LogInfo(
                        $"[AP-Dump]   LOADED  obj='{go.name}'  scene='{go.scene.name}'  " +
                        $"definitionId='{defId}'  state={state}");
                    if (defId != "(no definition)") resolved[go.name] = defId;
                }
                catch (System.Exception ex)
                {
                    log.LogWarning($"[AP-Dump]   switch read failed: {ex.Message}");
                }
            }
        }
        catch (System.Exception ex)
        {
            log.LogWarning($"[AP-Dump] Loaded-switch scan failed: {ex.Message}");
        }

        try
        {
            var all = gameModel.AllSwitches();
            log.LogInfo($"[AP-Dump] --- registry: {all?.Count ?? 0} switch(es) ---");
            if (all != null)
            {
                foreach (var kv in all)
                {
                    string id    = kv.Key ?? "(null)";
                    string state = "(null model)";
                    string obj   = "(not loaded)";
                    string scene = "";
                    try
                    {
                        var model = kv.Value;
                        if (model != null)
                        {
                            state = model.state.ToString();
                            var go = model.GetGameObject();
                            if (go != null)
                            {
                                obj = go.name;
                                // The LOADED list above only covers WorldStatePrimarySwitch, so
                                // this is the only place the Harmonizer bells (and other
                                // WorldStateInvisibleSwitch puzzles) show which scene they are in.
                                if (go.scene.IsValid()) scene = $"  scene='{go.scene.name}'";
                            }
                        }
                    }
                    catch { /* entry unreadable — still worth listing its key */ }

                    log.LogInfo($"[AP-Dump]   id='{id}'  state={state}  obj='{obj}'{scene}");
                }
            }
        }
        catch (System.Exception ex)
        {
            log.LogWarning($"[AP-Dump] AllSwitches() failed: {ex.Message}");
        }

        // The gate switches RegionTable names.
        log.LogInfo("[AP-Dump] --- RegionTable gate switches ---");
        foreach (var objName in new[] { "ruinSwitch", "ruinSwitch (2)" })
        {
            if (resolved.TryGetValue(objName, out var defId))
            {
                string live = "?";
                try { live = gameModel.GetSwitchState(defId).ToString(); } catch { }
                log.LogInfo(
                    $"[AP-Dump]   '{objName}'  →  definitionId='{defId}'  " +
                    $"GameModel.GetSwitchState='{live}'");
            }
            else
            {
                log.LogInfo(
                    $"[AP-Dump]   '{objName}'  →  NOT RESOLVED (not loaded — run this in Rainbow Fields)");
            }
        }

        log.LogInfo("[AP-Dump] ==========================================");
    }

    /// <summary>Logs every ComponentAcqDroneSpawner in the scene: pod state, name, position.</summary>
    public static void DumpGhostDroneSpawners()
    {
        var log = Plugin.Instance.Log;
        var spawners = Resources.FindObjectsOfTypeAll<Il2CppMonomiPark.SlimeRancher.Drone.ComponentAcqDroneSpawner>();
        log.LogInfo($"[AP-Dump] ========== GHOST DRONE SPAWNER DUMP ({spawners.Length} found) ==========");

        int eligible = 0;
        foreach (var spawner in spawners)
        {
            if (spawner == null) continue;
            try
            {
                var pod = spawner._treasurePod;
                string podState = pod == null ? "NO_POD" : pod.CurrState.ToString();
                bool wouldSpawn = pod != null && pod.CurrState == TreasurePod.State.LOCKED;
                if (wouldSpawn) eligible++;

                var go = spawner.gameObject;
                string pos = go != null
                    ? $"({go.transform.position.x:F1}, {go.transform.position.y:F1}, {go.transform.position.z:F1})"
                    : "(no go)";
                string name = go?.name ?? "(null)";
                string scene = go?.scene.name ?? "(null)";

                string ident = spawner._ident?.name ?? "(null)";
                log.LogInfo($"[AP-Dump]   spawner='{name}'  scene='{scene}'  ident='{ident}'  podState={podState}  wouldSpawn={wouldSpawn}  pos={pos}");
            }
            catch (Exception ex)
            {
                log.LogWarning($"[AP-Dump]   spawner exception: {ex.Message}");
            }
        }

        log.LogInfo($"[AP-Dump] Eligible for ForceSpawn (LOCKED): {eligible}/{spawners.Length}");
        log.LogInfo($"[AP-Dump] =========== GHOST DRONE SPAWNER DUMP END ===========");
    }

    /// <summary>
    /// Dumps the three ranch plot tiers: plot spots (<c>LandPlotLocation</c> — per area, zone
    /// streaming applies), buildable types and upgrades (purchase-model assets, with cost — open
    /// any plot menu first if these come back empty).
    /// </summary>
    public static void DumpLandPlots()
    {
        var log = Plugin.Instance.Log;
        log.LogInfo("[AP-Dump] ========== LAND PLOT DUMP START ==========");

        // ── Tier 1: plot spots ────────────────────────────────────────────────
        var locations = Resources.FindObjectsOfTypeAll<LandPlotLocation>()
            .OrderBy(l => { try { return l.Id ?? ""; } catch { return ""; } })
            .ToList();
        log.LogInfo($"[AP-Dump] --- Plot spots: {locations.Count} LandPlotLocation(s) loaded ---");
        var typeCounts = new Dictionary<string, int>();
        foreach (var loc in locations)
        {
            string id = "(no id)";
            try { id = loc.Id ?? "(null)"; } catch { }

            string plotType = "(no LandPlot child)";
            string region   = "(none)";
            try
            {
                var plot = loc.GetComponentInChildren<LandPlot>(true);
                if (plot != null)
                {
                    plotType = plot.TypeId.ToString();
                    try { region = plot._region?.name ?? "(null)"; } catch { }
                }
            }
            catch (Exception ex) { plotType = $"(error: {ex.Message})"; }

            string pos = "";
            try
            {
                var p = loc.transform.position;
                pos = $"({p.x:F0}, {p.y:F0}, {p.z:F0})";
            }
            catch { }

            string goName = "", sceneName = "";
            try { goName = loc.gameObject.name; } catch { }
            try { sceneName = loc.gameObject.scene.name; } catch { }

            // The hierarchy often names the expansion area when the Region doesn't.
            string rootName = "";
            try { rootName = loc.transform.root?.name ?? ""; } catch { }

            typeCounts[plotType] = typeCounts.GetValueOrDefault(plotType) + 1;
            log.LogInfo($"[AP-Dump]   PLOT id='{id}'  type={plotType}  region='{region}'  " +
                        $"scene='{sceneName}'  root='{rootName}'  go='{goName}'  pos={pos}");
        }
        log.LogInfo($"[AP-Dump]   Summary by current type: " +
            string.Join(", ", typeCounts.OrderBy(kv => kv.Key).Select(kv => $"{kv.Key}={kv.Value}")));

        // ── Tier 2: buildable plot types ──────────────────────────────────────
        var patchModels = Resources.FindObjectsOfTypeAll<Il2CppMonomiPark.SlimeRancher.UI.Plot.PlotPatchPurchaseItemModel>()
            .OrderBy(m => m.name)
            .ToList();
        log.LogInfo($"[AP-Dump] --- Buildable types: {patchModels.Count} PlotPatchPurchaseItemModel asset(s) ---");
        foreach (var m in patchModels)
        {
            string plotType = "(no definition)";
            try
            {
                var def = m._plotDefinition;
                if (def != null) plotType = def.Type.ToString();
            }
            catch (Exception ex) { plotType = $"(error: {ex.Message})"; }

            log.LogInfo($"[AP-Dump]   BUILD asset='{m.name}'  type={plotType}  cost={DescribeCost(m)}");
        }

        // ── Tier 3: plot upgrades ─────────────────────────────────────────────
        var upgradeModels = Resources.FindObjectsOfTypeAll<Il2CppMonomiPark.SlimeRancher.UI.Plot.PlotUpgradePurchaseItemModel>()
            .OrderBy(m => m.name)
            .ToList();
        log.LogInfo($"[AP-Dump] --- Upgrades: {upgradeModels.Count} PlotUpgradePurchaseItemModel asset(s) ---");
        foreach (var m in upgradeModels)
        {
            string upgrade = "(unknown)";
            try { upgrade = m._upgrade.ToString(); } catch { }

            log.LogInfo($"[AP-Dump]   UPGRADE asset='{m.name}'  upgrade={upgrade}  cost={DescribeCost(m)}");
        }

        log.LogInfo("[AP-Dump] =========== LAND PLOT DUMP END ===========");
    }

    /// <summary>First currency cost of a purchase item model, e.g. "450 Newbucks".</summary>
    private static string DescribeCost(Il2CppMonomiPark.SlimeRancher.UI.Purchase.MenuPurchaseItemModel model)
    {
        try
        {
            var costs = model._purchaseCost._currencyCosts;
            if (costs == null || costs.Count == 0) return "(none)";
            var entry = costs[0];
            return $"{entry._amount} {entry._currency?.name ?? "(currency?)"}";
        }
        catch (Exception ex)
        {
            return $"(error: {ex.Message})";
        }
    }

    // -------------------------------------------------------------------------
    // Gigi's rewards kiosk
    // -------------------------------------------------------------------------

    /// <summary>
    /// Every reward entry on the loaded rewards kiosk(s): the stable <c>UniqueId</c> that
    /// <c>KioskRewardPatch</c> keys on, its cost, its rewards, and whether this save has claimed it.
    /// Run near Gigi's kiosk (<c>zoneLabyrinthCoreBase</c>).
    /// </summary>
    public static void DumpRewardsKiosk()
    {
        var log = Plugin.Instance.Log;
        log.LogInfo("[AP-Dump] ========== GIGI REWARDS KIOSK ==========");
        int kiosks = 0;
        foreach (var k in Resources.FindObjectsOfTypeAll<Il2CppMonomiPark.SlimeRancher.Prestige.RewardsKiosk>())
        {
            if (k == null || !k.gameObject.scene.IsValid()) continue;   // skip prefabs
            kiosks++;
            log.LogInfo($"[AP-Dump] Kiosk '{k.gameObject.name}'  scene='{k.gameObject.scene.name}'");
            DumpKioskTrack("unique", k._uniqueRewards, k);
            DumpKioskTrack("repeat", k._repeatRewards, k);
        }
        if (kiosks == 0) log.LogInfo("[AP-Dump] No kiosk loaded — run this near Gigi's kiosk.");
        log.LogInfo("[AP-Dump] =======================================");
    }

    private static void DumpKioskTrack(string label,
        Il2CppMonomiPark.SlimeRancher.Prestige.EndGameRewardsDefinition? def,
        Il2CppMonomiPark.SlimeRancher.Prestige.RewardsKiosk kiosk)
    {
        var log = Plugin.Instance.Log;
        if (def == null) { log.LogInfo($"[AP-Dump]   {label}: (none)"); return; }

        var entries = def.CostsAndRewards;
        int n = entries == null ? 0
            : entries.Cast<Il2CppSystem.Collections.Generic.IReadOnlyCollection<
                  Il2CppMonomiPark.SlimeRancher.Prestige.EndGameCostRewardEntry>>().Count;
        log.LogInfo($"[AP-Dump]   {label} track '{def.name}': {n} entr(ies), repeats={def.RepeatsTrackAtEnd}");

        var giver = kiosk.TryCast<Il2CppMonomiPark.SlimeRancher.Prestige.IRewardGiver>();
        for (int i = 0; i < n; i++)
        {
            var e = entries![i];
            if (e is null) continue;   // the game types overload ==/!=, which throw on null

            var costs = new List<string>();
            if (e.Costs != null)
                foreach (var c in e.Costs)
                    if (c is not null) costs.Add($"{c.Amount}x {c.IdentType?.name}");

            string given = "?";
            try { if (giver != null) given = giver.HasBeenGivenReward(e.UniqueId).ToString(); } catch { }

            string kioskLoc = LocationTable.TryGetKioskReward(e.UniqueId, out var loc) && loc != null
                ? $"  → AP location {loc.Id}" : "";
            log.LogInfo($"[AP-Dump]     [{i}] id='{e.UniqueId}'  given={given}  costs=[{string.Join(", ", costs)}]{kioskLoc}");

            if (e.Rewards == null) continue;
            foreach (var r in e.Rewards)
            {
                if (r is null) continue;
                string guid = "?", name = "?";
                try { guid = r.Item?.AssetGuid ?? "null"; } catch { }
                try
                {
                    if (r.Item?.UnderlyingLoadedAsset == null) r.Item?.LoadAssetBlocking();   // one-off debug load
                    name = r.Item?.UnderlyingLoadedAsset?.name ?? "?";
                }
                catch (System.Exception ex) { name = $"<load failed: {ex.GetType().Name}>"; }
                log.LogInfo($"[AP-Dump]         reward: '{name}'  x{r.Count}  gadgetType={r.GadgetType}  guid={guid}");
            }
        }
    }
}
#endif
