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
using Il2CppMonomiPark.SlimeRancher.DataModel;
using Il2CppMonomiPark.SlimeRancher.Weather;
using Il2CppMonomiPark.SlimeRancher.World.ResearchDrone;
using SlimeRancher2AP.Utils;
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace SlimeRancher2AP.Utils;

/// <summary>
/// Development utility — scans all currently-loaded game objects of each checkable type and
/// logs their identity strings to the BepInEx log.
/// <para>
/// Because SR2 uses zone streaming, call this once per zone while standing in it. The log
/// output is then used to populate the <c>GameObjectName</c>/<c>EntryName</c> fields in
/// <c>LocationTable.cs</c>.
/// </para>
/// <para>
/// Trigger from the debug panel (F9 → Misc page → "Dump Locations").
/// </para>
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

    /// <summary>
    /// Logs every Slimepedia entry name, its localized display title, and whether it is
    /// currently unlocked. Call from the debug panel Misc page while in-game (PediaDirector
    /// must be active). Output is used to populate EntryName and display name values in
    /// LocationTable.cs and the apworld locations.py.
    /// </summary>
    public static void DumpPedia()
    {
        var log = Plugin.Instance.Log;
        var pedia = SceneContext.Instance?.PediaDirector;
        if (pedia == null)
        {
            log.LogWarning("[AP-Dump] PediaDirector not available — load a save first");
            return;
        }

        // PediaCategory is a ScriptableObject that holds _items (Il2CppReferenceArray<PediaEntry>)
        // — one category per Slimepedia tab (Slimes, Resources, World, etc.).
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
    /// Walks all loaded <c>RancherDefinition</c> ScriptableObjects and logs every conversation
    /// that contains a gift page (GiftBlueprint, GiftGadget, or GiftUpgradeComponent).
    /// <para>
    /// Output format per line: rancher name → conversation ID → page type → gifted asset name.
    /// Use this to build the LocationTable entries for comm-station gift checks.
    /// </para>
    /// Call from the debug panel Misc page while a save is loaded.
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

                    // Walk pages to find gift pages.
                    // Cast to FixedConversation to access its typed pages list directly —
                    // IEnumerable<IConversationPage> from GetPages() uses an Il2Cpp enumerator
                    // that doesn't expose MoveNext() at the CLR level.
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

        // -------------------------------------------------------------------------
        // Second pass: standalone FixedConversations not in any rancher provider.
        // These are conversations that fire via world-event triggers rather than
        // through a RancherDefinition gift queue — e.g. ViktorIntroCall (fires on
        // first Research Drone activation) and BObGift1Intro (fires after visiting
        // Gorge + Strand). DumpConversationConditions() already shows their conditions;
        // this section confirms their debug names.
        // -------------------------------------------------------------------------
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
    /// Walks all loaded <c>FixedConversation</c> ScriptableObjects and logs their unlock
    /// condition trees, revealing prerequisite chains (e.g. "conversation B unlocks only
    /// after conversation A is completed").
    /// <para>
    /// Builds GUID-to-name lookup maps for conversations, gadgets, zones, and research drones
    /// so GUID hashes in condition DataKeys are resolved to human-readable names.
    /// <c>GameTimeSinceEventComponent</c> wrappers are unwrapped to show both the delay and
    /// the underlying event condition.
    /// </para>
    /// Call from the debug panel Misc page while a save is loaded. Assets must be resident
    /// (stand in the relevant zone so conversation assets stream in first).
    /// </summary>
    public static void DumpConversationConditions()
    {
        var log = Plugin.Instance.Log;

        // Build GUID → name lookup map for FixedConversations.
        // FixedConversation extends AbstractConversation extends ScriptableObjectWithGuid,
        // which exposes a .Guid string property. Other asset types (GadgetDefinition,
        // ZoneDefinition, ResearchDroneEntry) don't share that interface in the interop, so
        // their DataKeys are logged raw — likely GUIDs that can be cross-referenced manually.
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

            // Composite — recurse into sub-children
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

            // GameTimeSinceEventComponent — time-delay wrapper; unwrap to show delay + inner event
            var timeQuery = child.TryCast<GameTimeSinceEventComponent>();
            if (timeQuery != null)
            {
                double days = 0;
                try { days = timeQuery._daysSinceEvent; } catch { }
                var innerChild = timeQuery._child;
                log.LogInfo($"{indent}[{i}] TimeSinceEvent(>={days:F1} days):");
                if (innerChild != null)
                {
                    // Wrap single child in a temporary list for recursive logging
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

            // StaticEventQueryComponent — a static named game-event (story milestone etc.)
            var staticQuery = child.TryCast<StaticEventQueryComponent>();
            if (staticQuery != null)
            {
                var eventName = staticQuery._gameEvent?.name ?? "(null)";
                int count = 0;
                try { count = staticQuery.Count; } catch { }
                log.LogInfo($"{indent}[{i}] StaticEvent  event='{eventName}'  count={count}");
                continue;
            }

            // For all SerializedProducedEventQueryComponent subtypes, call GetEvent()
            // to obtain the IGameEvent whose DataKey encodes the asset GUID (or string key).
            // Then look up the GUID in the appropriate map for a readable name.
            var eventQuery = child.TryCast<GameEventQueryComponent>();
            if (eventQuery != null)
            {
                // Determine concrete type name via IL2CPP for labelling
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

                // Resolve GUID to human-readable name.
                // Only FixedConversation GUIDs are in our lookup map; other asset types
                // (GadgetDefinition, ZoneDefinition, ResearchDroneEntry) log their DataKey raw.
                if (!string.IsNullOrEmpty(dataKey) && typeName.Contains("Conversation"))
                {
                    if (convGuids.TryGetValue(dataKey, out var cName))
                        resolvedName = $" → '{cName}'";
                }
                // StringEvent: dataKey IS the string key directly — no lookup needed.

                log.LogInfo($"{indent}[{i}] {typeName}  dataKey='{dataKey}'{resolvedName}  count={count}");
                continue;
            }

            // Truly unknown type — log IL2CPP type name for future handling
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

    /// <summary>
    /// Logs all loaded GadgetDefinition asset names — useful for confirming zone teleporter
    /// names (TeleporterZoneGorge, TeleporterZoneStrand?, TeleporterZoneBluffs, etc.).
    /// Call from the debug panel Misc page. Assets load lazily so call while in-game.
    /// </summary>
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
    /// Dumps every loaded <c>SlimeDefinition</c> and the <c>ZoneDefinition</c> names stored
    /// in its <c>NativeZones</c> array.
    /// <para>
    /// Used to verify which AP region each Slimepedia slime entry belongs to (i.e. which zones
    /// a slime naturally spawns in). SlimeDefinition assets load globally — no specific zone
    /// needs to be loaded first, but call after loading a save so all assets are resident.
    /// </para>
    /// Output format per line: <c>name='…'  zones=[zone1, zone2, …]</c>
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
    /// Dumps every loaded <c>IdentifiableType</c> and the <c>ZoneDefinition</c> names stored
    /// in its <c>showForZones</c> array.
    /// <para>
    /// Covers food, resources, craft materials, and plorts — used to verify region assignments
    /// in <c>SLIMEPEDIA_RESOURCE_LOCATIONS</c> and ingredient zone requirements in
    /// <c>FABRICATOR_LOCATIONS</c> in the apworld.
    /// </para>
    /// <para>
    /// Types with no zone bindings (globally available or zone-irrelevant) are listed separately
    /// at the end. Call after loading a save — IdentifiableType assets load globally.
    /// </para>
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

    /// <summary>
    /// Logs every loaded <c>IdentifiableType</c> asset name, sorted alphabetically.
    /// Use this to discover food item names (for gordo-feeding tests), live animal names,
    /// and any other identifiable object that can be granted via the Refinery.
    /// Call from the debug panel Misc page while in-game.
    /// </summary>
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
    /// Walks every loaded <c>UpgradeDefinition</c> asset, reads each level's <c>PurchaseCost</c>,
    /// converts it to ingredients via <c>RecipeFactory.IngredientsFromCost</c>, and for each
    /// <c>UpgradeIngredient</c> calls <c>TryGetIdentifiableType</c> to get the exact asset name.
    /// This is the authoritative way to discover the IdentifiableType names for Fabricator
    /// recipe ingredients (Heart Cell, Power Chip, Dash Boot Module, etc.).
    /// Call from the debug panel Misc page while the Fabricator scene is loaded.
    /// </summary>
    public static void DumpUpgradeComponents()
    {
        var log = Plugin.Instance.Log;

        var upgradeDefs = Resources.FindObjectsOfTypeAll<UpgradeDefinition>()
            .OrderBy(d => d.name)
            .ToList();

        log.LogInfo($"[AP-Dump] ========== UPGRADE COMPONENT DUMP ({upgradeDefs.Count} UpgradeDefinitions) ==========");

        // Log all definition names first — including those whose recipes are currency/plort-only
        // (those won't appear in the per-level component listing below).
        log.LogInfo("[AP-Dump] --- All UpgradeDefinition names ---");
        foreach (var def in upgradeDefs)
            log.LogInfo($"[AP-Dump] UpgradeDef  name='{def.name}'  levels={def._levels?.Count ?? 0}");

        // Collect unique IdentifiableType names across all levels of all upgrades
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

                // The concrete type is always List<IIngredient>; wrap by pointer to get Count/indexer
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

    /// <summary>
    /// Logs every loaded <c>PuzzleSlotLockable</c> (plort doors and other puzzle locks)
    /// with its scene, name, posKey, lockTag, current unlock state, and required plort types.
    /// Run in each zone to build the full list for LocationTable.cs / locations.py.
    /// </summary>
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

            // Collect required plort type for each depositor slot
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

    /// <summary>
    /// Logs all loaded <c>AccessDoor</c> instances and their current model state.
    /// Run while standing near the Valley Labyrinth gate — before AND after solving
    /// the beam puzzle — to see if an AccessDoor changes state.
    /// </summary>
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
            log.LogInfo($"[AP-Dump] AccessDoor  scene='{door.gameObject.scene.name}'  name='{door.gameObject.name}'  state={state}  pos={PosStr(door.gameObject)}");
        }
    }

    /// <summary>
    /// Logs all loaded <c>SlimeGateActivator</c> instances (Radiant Projector beam-puzzle gates).
    /// Stand in Ember Valley near the Valley Labyrinth entrance before calling.
    /// The <c>scene</c> and <c>name</c> fields become the <c>LabyrinthGateValley</c> constant.
    /// </summary>
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

        // Separate regular treasure pods from ghostly drone nodes (nodeComponentAcqDrone)
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

        // Gordos with an empty scene name are GordoSnare prefab templates loaded globally —
        // not static world gordos. Only log the ones with a real scene name.
        var staticGordos = gordos.Where(g => !string.IsNullOrEmpty(g.gameObject.scene.name)).ToList();
        var snareGordos  = gordos.Where(g =>  string.IsNullOrEmpty(g.gameObject.scene.name)).ToList();

        log.LogInfo($"[AP-Dump] --- GordoEat ({staticGordos.Count} static world, {snareGordos.Count} snare/global) ---");
        foreach (var gordo in staticGordos)
        {
            // _id is from IdHandler base class: IdPrefix() ("gordo") + numeric suffix assigned at runtime.
            // This is the exact string event key fired on burst — matches ViktorStoryCipher4 conditions.
            string gordoId = "(err)";
            try { gordoId = gordo._id ?? "(null)"; } catch { }
            log.LogInfo($"[AP-Dump] GordoEat    scene='{gordo.gameObject.scene.name}'  name='{gordo.gameObject.name}'  stringEventKey='{gordoId}'");
        }
        // Log snare gordo names only as a reference (not checks)
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
        // Filter to Shadow Plort doors only — PlortDepositor is used for many plort locks
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
            var entryName  = controller?.ResearchDroneEntry?.name ?? "<null>";
            log.LogInfo($"[AP-Dump] ResearchDrone  scene='{activator.gameObject.scene.name}'  entryName='{entryName}'");
        }
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

    /// <summary>
    /// Dumps all SceneGroup assets and the TeleportNetwork's wake-up destinations.
    /// Used to discover zone ReferenceId strings for the teleport trap.
    /// Call this once per zone to build the full picture.
    /// </summary>
    public static void DumpSceneGroups()
    {
        var log = Plugin.Instance.Log;
        log.LogInfo("[AP-Dump] ========== SCENE GROUPS + TELEPORT WAKE-UP DESTINATIONS ==========");

        // All SceneGroup assets loaded in memory
        var allGroups = Resources.FindObjectsOfTypeAll<Il2CppMonomiPark.SlimeRancher.SceneManagement.SceneGroup>();
        log.LogInfo($"[AP-Dump] SceneGroup assets found: {allGroups.Count}");
        foreach (var sg in allGroups.OrderBy(g => g.ReferenceId ?? ""))
            log.LogInfo($"[AP-Dump] SceneGroup  refId='{sg.ReferenceId}'  name='{sg.name}'");

        // TeleportNetwork wake-up destinations — populated per zone as the player visits them
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

            // Also dump registered teleporter node IDs for reference
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
    /// Scans all loaded <c>RadiantSlimePediaEntry</c> assets and logs their Unity asset name
    /// (the lookup key for <c>LocationTable</c>) and associated <c>SlimeDefinition.name</c>.
    /// Also dumps the per-slime shuffle-bag sizes from <c>RadiantSlimeConfig</c>.
    ///
    /// <para>Run once while a save is loaded. Output is used to populate
    /// <c>LocationConstants.cs</c> and <c>LocationTable.cs</c> for the
    /// <see cref="SlimeRancher2AP.Data.LocationType.SlimepediaRadiantEntry"/> category.</para>
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
    /// Sets all radiant shuffle bag sizes to <paramref name="size"/>.
    ///
    /// <para>Two layers are updated so the change is both immediate and sticky for
    /// the duration of the session:</para>
    /// <list type="number">
    ///   <item><term>Config layer</term><description>
    ///     <c>RadiantSlimeConfig._radiantShuffleBagSizes[i].BagSize</c> — the value used when a
    ///     new bag is initialised (e.g. after a scene reload).
    ///   </description></item>
    ///   <item><term>Live bag layer</term><description>
    ///     <c>director.RadiantSlimesModel.RadiantShuffleBags[id].SetNewSize(size)</c> — calls the
    ///     game's own resize method, which updates <c>Size</c> and immediately reshuffles
    ///     (<c>CurrentIndex = 0</c>, new random <c>RadiantSpawnIndex</c> within the new range).
    ///     This means the first encounter after pressing the button starts a fresh cycle of
    ///     the new size with no carry-over from the old cycle.
    ///   </description></item>
    /// </list>
    ///
    /// <list type="bullet">
    ///   <item><term>size = 2</term><description>Every other encounter is radiant.</description></item>
    ///   <item><term>size = 1</term><description>Every encounter is radiant (only one slot, so
    ///   <c>RadiantSpawnIndex</c> is always 0).</description></item>
    /// </list>
    ///
    /// <para>In-memory only — resets to vanilla on scene reload or game restart.</para>
    /// </summary>
    public static void SetRadiantBagSizes(int size)
    {
        var log = Plugin.Instance.Log;

        // ── 1. Config layer (affects bags initialised after this point) ────────
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

        // ── 2. Live bag layer (immediately resets current cycle) ───────────────
        var directors = Resources.FindObjectsOfTypeAll<Il2CppMonomiPark.SlimeRancher.Slime.RadiantSlimeDirector>();
        int liveCount = 0;
        foreach (var director in directors)
        {
            if (director == null) continue;
            var model = director.RadiantSlimesModel;
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

    /// <summary>
    /// Dumps the current live state of every <c>RadiantShuffleBag</c> — shows
    /// <c>CurrentIndex</c>, <c>RadiantSpawnIndex</c>, and <c>Size</c> per slime type.
    /// Useful for verifying that a <see cref="SetRadiantBagSizes"/> call took effect
    /// and for watching cycle progress.
    /// </summary>
    public static void DumpRadiantBagState()
    {
        var log = Plugin.Instance.Log;
        var directors = Resources.FindObjectsOfTypeAll<Il2CppMonomiPark.SlimeRancher.Slime.RadiantSlimeDirector>();

        if (directors.Count == 0)
        {
            log.LogWarning("[AP-Debug] DumpRadiantBagState: no RadiantSlimeDirector found — load a save first");
            return;
        }

        // NOTE: RadiantSlimesModel.RadiantShuffleBags is the PERSISTED BAG SNAPSHOT loaded
        // from the save file, NOT the live native bag state. The game tracks active bags in
        // a private native field. Bags only appear here after they've been serialized to a
        // save file at least once. Missing slimes are NOT proof the multiplier is broken —
        // they just haven't been saved mid-cycle yet.
        log.LogInfo("[AP-Debug] ===== RADIANT BAG STATE (persisted snapshot from save) =====");
        foreach (var director in directors)
        {
            if (director == null) continue;
            bool spawnAllowed = director._isRadiantSpawnAllowedCached;
            log.LogInfo($"[AP-Debug]   isRadiantSpawnAllowedCached={spawnAllowed}  DEBUG_ForceRadiantSpawn={director.DEBUG_ForceRadiantSpawn}  DEBUG_BagSizeScalar={director.DEBUG_BagSizeScalar}");
            var model = director.RadiantSlimesModel;
            if (model == null) { log.LogInfo("[AP-Debug]   (director has null RadiantSlimesModel)"); continue; }
            var savedBags = model.RadiantShuffleBags;
            if (savedBags == null) { log.LogInfo("[AP-Debug]   (RadiantShuffleBags dictionary is null)"); continue; }

            log.LogInfo($"[AP-Debug]   {savedBags.Count} saved bag(s):");
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

    /// <summary>
    /// Sets <c>RadiantSlimeDirector.DEBUG_ForceRadiantSpawn</c> on every loaded director.
    /// When <c>true</c>, every call to <c>DrawFromRadiantShuffleBag</c> returns <c>true</c>
    /// unconditionally — the bag cycle is bypassed entirely, so every eligible slime
    /// encounter is radiant immediately with no cycle-reset delay.
    ///
    /// <para>Set to <c>false</c> to restore normal bag behaviour.</para>
    /// <para>In-memory only — resets on scene reload.</para>
    /// </summary>
    public static void SetForceRadiantSpawn(bool enabled)
    {
        // Drive our own Harmony Postfix flag — more reliable than DEBUG_ForceRadiantSpawn
        // on the director, which appears not to be read by the shipping native code.
        Patches.LocationPatches.RadiantDebugFlags.ForceRadiantSpawn = enabled;
        Logger.Info($"[AP-Debug] SetForceRadiantSpawn: ForceRadiantSpawn = {enabled}");
    }

    // -------------------------------------------------------------------------
    // Weather dumps
    // -------------------------------------------------------------------------

    /// <summary>
    /// Dumps the runtime state of the <c>WeatherRegistry</c> — global forecast timing,
    /// per-zone eligible patterns, currently running patterns, upcoming forecast schedule,
    /// and the <c>NextChangeAt</c> timestamp that controls when the next weather event starts.
    ///
    /// <para>This is the companion to <see cref="DumpWeatherPatterns"/>, which shows the
    /// static transition graphs. This dump shows live scheduling data so you can see how
    /// long until the next weather change fires and which patterns are currently active.</para>
    ///
    /// <para>Format per zone:
    /// <code>
    ///   Zone 'Name'  NextChangeAt=X  ForecastUnlocked=Y
    ///     Eligible patterns: A, B, C
    ///     Running: A [state: foo]
    ///     Forecast (N entries): ...
    /// </code>
    /// </para>
    ///
    /// Trigger: F9 → Weather Dumps page → "Dump Weather Registry".
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

        // Also walk _zones dict for NextChangeAt / ForecastUnlocked
        Il2CppSystem.Collections.Generic.Dictionary<ZoneDefinition, WeatherRegistry.ZoneWeatherData>? zoneDict = null;
        try { zoneDict = registry._zones; }
        catch (Exception ex) { log.LogWarning($"[AP-Dump] _zones field error: {ex.Message}"); }

        for (int zi = 0; zi < weatherZones.Count; zi++)
        {
            var zone = weatherZones[zi];
            if (zone == null) { log.LogInfo($"[AP-Dump]   Zone[{zi}] NULL"); continue; }
            string zoneName = zone.name ?? $"Zone[{zi}]";

            // NextChangeAt + ForecastUnlocked from _zones dict
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

            // Eligible patterns
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

            // Currently running patterns
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

            // Forecast schedule
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
    /// Dumps the full condition tree of <c>RadiantSlimeConfig._allowRadiantSlimesToSpawnQuery</c>
    /// — the master gate that must be satisfied before any radiant slime can spawn.
    ///
    /// <para>For each child condition in the query the log shows:</para>
    /// <list type="bullet">
    ///   <item>The IL2CPP concrete type (e.g. <c>PediaEntryEventQueryComponent</c>)</item>
    ///   <item>The <c>DataKey</c> from the associated game event (usually an asset GUID)</item>
    ///   <item>The <c>Count</c> threshold the event must reach to satisfy the condition</item>
    /// </list>
    /// <para>
    /// Also logs <c>_sanctuaryPediaEntry.name</c> directly from the director so any
    /// <c>PediaEntryEventQueryComponent</c> DataKeys can be cross-referenced against it,
    /// and logs <c>_isRadiantSpawnAllowedCached</c> to show the current gate state.
    /// </para>
    /// <para>
    /// Call twice — before and after discovering the Sanctuary — to observe which
    /// conditions transition and confirm the hypothesis that Sanctuary pedia unlock
    /// is the gate condition.
    /// </para>
    /// Call from the debug panel Misc page while a save is loaded.
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

        // Log the sanctuary pedia entry asset name.
        // Its name is the value we expect to see as the DataKey (or resolved name)
        // in any PediaEntryEventQueryComponent child of the query.
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
            // Reuse the existing tree-walk helper. Pass an empty convGuids dict — the
            // radiant query doesn't reference FixedConversations, so no GUID resolution
            // is needed for that type. DataKeys for PediaEntry assets will appear raw;
            // cross-reference against the sanctuaryPediaEntry name logged above.
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

    /// <summary>
    /// Iterates every <c>DirectedActorSpawner</c> in the scene and logs any entry whose
    /// <c>SlimeSet.Member.IdentType.name</c> contains "Gold" or "Lucky".
    ///
    /// <para>For each hit the log shows:</para>
    /// <list type="bullet">
    ///   <item>The spawner's <c>gameObject.name</c> and scene path (parent chain)</item>
    ///   <item>The slime <c>IdentType.name</c></item>
    ///   <item>The member's current <c>Weight</c></item>
    ///   <item>The spawner's own outer <c>Weight</c> (relative probability vs other spawners)</item>
    /// </list>
    /// </summary>
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

                // Sum the total weight of all members in this constraint so we can compute
                // a true 1/X spawn chance for each Gold/Lucky entry.
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

                    // Express as 1/X where X = totalWeight / memberWeight.
                    // Round X to the nearest integer for clean display; show one decimal only
                    // when the raw value isn't close to a whole number (>0.05 error).
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
    /// Sets the <c>Weight</c> of every Gold and Lucky <c>SlimeSet.Member</c> across all
    /// loaded <c>DirectedActorSpawner</c> instances to <paramref name="weight"/>.
    ///
    /// <para>Weight is relative within a <c>SlimeSet</c> — a member with weight 100 in a set
    /// where all others sum to 100 has a 50% spawn chance. Vanilla Gold/Lucky weights are
    /// typically very small (often 1–5) compared to common slimes (50–200+).
    /// Setting this to a large value (e.g. 1000) effectively makes every spawn a
    /// Gold or Lucky slime.</para>
    ///
    /// <para>In-memory only — resets on scene reload.</para>
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
    /// Iterates every <c>DirectedActorSpawner</c> and logs the ones that contain at least
    /// one <c>SpawnConstraint</c> with <c>Feral = true</c>, along with the spawner-level
    /// radiant flags:
    /// <list type="bullet">
    ///   <item><term>_blockRadiantSpawning</term><description>True → this spawner never produces radiant slimes.</description></item>
    ///   <item><term>_onlySpawnRadiant</term><description>True → every spawn from this spawner is radiant.</description></item>
    ///   <item><term>_radiantChanceOverride</term><description>Non-zero → overrides the normal bag-draw chance (0 = use bag).</description></item>
    /// </list>
    /// Call from the debug panel Radiant page while standing in a zone with feral slimes loaded.
    /// </summary>
    public static void DumpFeralSpawners()
    {
        var log = Plugin.Instance.Log;
        var spawners = Resources.FindObjectsOfTypeAll<DirectedActorSpawner>();

        // activeInHierarchy filter: Resources.FindObjectsOfTypeAll is memory-wide and includes
        // spawners from other streaming zones. Only count/log the ones active in the current scene.
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

            // Summarise what the radiant roll will do for this spawner
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

        // ── Second pass: ALL spawners with _onlySpawnRadiant=true, regardless of feral flag ──
        // These are guaranteed-radiant spawn points that may not use feral constraints.
        // Filter by activeInHierarchy so only spawners in the currently loaded scene are shown
        // (Resources.FindObjectsOfTypeAll is memory-wide and includes other streaming zones).
        log.LogInfo("[AP-Dump] --- Guaranteed Radiant Spawners (_onlySpawnRadiant=true, active in scene) ---");
        int guaranteedCount = 0;
        foreach (var spawner in spawners)
        {
            if (spawner == null) continue;
            if (!spawner.gameObject.activeInHierarchy) continue;  // skip spawners from other streamed zones
            if (!spawner._onlySpawnRadiant) continue;

            string path = GetGameObjectPath(spawner.gameObject);

            // Log what slime types this spawner can produce.
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
    /// Dumps all WeatherPatternDefinition transition graphs and WeatherStateDefinition
    /// timing values (MinDurationHours, MapTier) to the BepInEx log.
    ///
    /// Output format per transition:
    ///   [Pattern 'Name'] 'FromState' → 'ToState'  ChancePerHour=X  (MinDurationHours=Y  MapTier=Z)
    ///
    /// ChancePerHour is the probability per in-game hour that this transition fires.
    /// MinDurationHours is the minimum time the ToState must run before any outbound
    /// transition is evaluated.
    ///
    /// Trigger: F9 → Dumps page → "Dump Weather Patterns".
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
    /// Dumps all active <c>DirectedActorSpawner</c> instances, grouped by zone, in a compact
    /// hierarchical format designed for easy post-processing.
    ///
    /// <para>Per-spawner fields logged:</para>
    /// <list type="bullet">
    ///   <item><term>Zone</term><description>Human-readable zone name derived from scene name.</description></item>
    ///   <item><term>Spawn point name</term><description>Short GameObject path (up to 3 levels).</description></item>
    ///   <item><term>directedWeight</term><description>
    ///     Relative weight among all spawners registered with the cell's director — higher means
    ///     this spawn point is chosen more often.
    ///   </description></item>
    ///   <item><term>delayFactor</term><description>Multiplier on the base spawn delay (1.0 = vanilla rate).</description></item>
    ///   <item><term>Per-constraint</term><description>
    ///     Selection chance (if multiple constraint sets exist), feral flag, and time window.
    ///   </description></item>
    ///   <item><term>Per-slime</term><description>Chance % within the chosen constraint's pool.</description></item>
    /// </list>
    ///
    /// Only active-in-hierarchy spawners are included (others belong to streaming zones not
    /// currently loaded). Run once per zone while standing in it.
    /// Trigger: F9 → Dumps page → "Dump Slime Spawn Weights".
    /// </summary>
    public static void DumpSlimeSpawnWeights()
    {
        var log      = Plugin.Instance.Log;
        var allSpawners = Resources.FindObjectsOfTypeAll<DirectedActorSpawner>();

        // ── Collect only active spawners that have at least one slimeset ──────────
        // Key: (sceneName, cellRootName) — spawners sharing the same cell compete in the
        // same directed-spawn pool, so their directedWeight values sum to the cell total.
        var byCell = new System.Collections.Generic.Dictionary<
            (string scene, string cell),
            List<DirectedActorSpawner>>();

        for (int si = 0; si < allSpawners.Count; si++)
        {
            var s = allSpawners[si];
            if (s == null) continue;
            // Skip prefab/template instances that live outside any scene (empty scene name).
            // Do NOT filter by activeInHierarchy — inactive spawners belong to zones that are
            // resident in memory but not currently streamed in, and we want all of them.
            if (string.IsNullOrEmpty(s.gameObject.scene.name)) continue;

            var cons = s.Constraints;
            if (cons == null || cons.Length == 0) continue;

            // Keep all spawners that have at least one constraint — the per-constraint loop
            // below already skips empty slimesets, so non-slime spawners just produce no output.
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

        // allSpawners.Count is the raw Resources.FindObjectsOfTypeAll count — includes prefab
        // template instances (no scene name) and constraint-less spawners that are not real
        // world spawners. totalSpawners is the filtered count of world-placed slime spawners.
        log.LogInfo($"[AP-Dump] ======= SLIME SPAWN WEIGHTS" +
                    $"  raw={allSpawners.Count}" +
                    $"  world-placed={totalSpawners}" +
                    $"  cells={byCell.Count} =======");

        // ── Output grouped by zone → cell, spawners sorted high→low chance ────────
        // Sort cells: zone display name first, then cell name within zone.
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

            // Compute the cell-total directedWeight so we can express each spawner as a %.
            float cellTotal = 0f;
            for (int i = 0; i < list.Count; i++)
            {
                try { cellTotal += list[i].DirectedSpawnWeight; } catch { }
            }

            // Sort high→low chance.
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

                // Pre-sum constraint weights so multi-constraint spawners can show selection %
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

                    // Constraint selection probability — only meaningful when > 1 constraint
                    string constraintPct = "";
                    if (constraints.Length > 1 && totalConstraintWeight > 0f)
                        constraintPct = $"  ({constraint.Weight / totalConstraintWeight * 100f:F0}% of spawns)";

                    log.LogInfo($"[AP-Dump]       constraint[{ci}]  feral={feral}  time={timeStr}{constraintPct}");

                    // Per-member chances within this constraint's pool
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
    /// Walks up the transform hierarchy to find the first ancestor whose name starts with
    /// "cell" (SR2 naming convention: <c>cellGorge_A</c>, <c>cellFloorSwitchRoom</c>, etc.).
    /// That node is where the <c>CellDirector</c> lives, and all <c>DirectedActorSpawner</c>
    /// instances under it register with the same director — so their <c>DirectedSpawnWeight</c>
    /// values are comparable and sum to the cell total.
    /// Falls back to the hierarchy root name if no "cell*" ancestor is found.
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
            // Include grandparent only when it's not a root-level scene object
            if (grandparent != null && grandparent.parent != null)
                name = grandparent.gameObject.name + "/" + name;
        }
        return name;
    }

    /// <summary>
    /// Exports the current zone's spawn rate data to
    /// <c>BepInEx/spawn_rates.json</c>, merging with any data already in that
    /// file (existing scene keys are overwritten; other zones' keys are kept).
    /// <para>
    /// Also records the full list of scene names baked into the game's build
    /// settings so the companion HTML viewer can show a "coverage" panel —
    /// which zone scenes have been captured and which still need a visit.
    /// </para>
    /// Run once per zone area while standing in it to build up a complete
    /// dataset across all zones. Call <see cref="ClearSpawnRatesJson"/> to
    /// wipe the file and start fresh.
    /// Trigger: F9 → Dumps page → "Export Spawn JSON".
    /// </summary>
    public static void ExportSpawnRatesJson()
    {
        var log = Plugin.Instance.Log;
        var allSpawners = Resources.FindObjectsOfTypeAll<DirectedActorSpawner>();

        // ── Build scene → cell → spawner row list ─────────────────────────────
        // Same grouping logic as DumpSlimeSpawnWeights; produces one JSON object
        // per (zone, cell, spawner, constraint, slime) tuple.
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

                        // Inline mini-JSON serialisation — avoids any Json.NET dependency
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

        // ── Accumulate all-ever-seen scene names for coverage tracking ───────
        // SR2 uses Addressables, so SceneManager.sceneCountInBuildSettings only
        // returns 1 ("Bootstrap"). Instead we build the list from all scene keys
        // that have appeared across every export run — existing file + this run.
        // The HTML viewer uses this to show which zones have been captured.

        // ── Read existing JSON file (if any), merge, write back ───────────────
        string jsonPath = System.IO.Path.Combine(BepInEx.Paths.BepInExRootPath, "spawn_rates.json");

        // Existing file is stored as a hand-rolled JSON object so we can merge
        // without a full JSON parser — we just look for our sentinel keys.
        var existingSceneData = new System.Collections.Generic.Dictionary<string, string>();
        if (System.IO.File.Exists(jsonPath))
        {
            try
            {
                string existing = System.IO.File.ReadAllText(jsonPath);
                // Extract per-scene blocks: "zoneXxx": { ... rows ... }
                // Simple regex-free approach: split on our sentinel comment pattern
                // Actually easier: we write it ourselves, so we know the format.
                // Each scene block starts with  "SCENE_sceneName": [  and ends with  ],
                // We'll re-parse on next export by just replacing the matching scene key.
                // For simplicity, store the whole existing object, strip the outer {},
                // remove any existing entry for scenes we're overwriting, and re-insert.
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
            // The existing file's scene_rows block as a raw string — we need to
            // extract individual scene entries. Since we wrote them, each entry is:
            //   "scene_rows": {
            //     "zoneName": [ ... ],     ← one per scene
            //   }
            // Re-parse the whole file as a string hunt for scene_rows content
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
                    // Split into individual scene entries by finding top-level "key": [ ... ]
                    // For each, check if the key is one we're overwriting; if not, keep it.
                    var scenesToOverwrite = new System.Collections.Generic.HashSet<string>(rowsByScene.Keys);
                    // Simple line-oriented split: each scene entry starts with whitespace + "zoneName":
                    // We wrote one scene per entry block ending in ],
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
}
#endif
