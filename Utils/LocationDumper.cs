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
}
#endif
