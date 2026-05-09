#if DEBUG
using SlimeRancher2AP.Archipelago;
using SlimeRancher2AP.Data;
using SlimeRancher2AP.Utils;
using UnityEngine;

namespace SlimeRancher2AP.UI;

/// <summary>
/// Debug-only IMGUI panel for testing item grants and location checks without
/// an Archipelago server. Only compiled and registered in Debug builds.
/// Toggle with F9. Use Q/E to page through sections.
/// </summary>
public class DebugPanel : MonoBehaviour
{
    public DebugPanel(IntPtr handle) : base(handle) { }

    private bool _visible = false;
    private int  _page    = 0;

    // Saved cursor state so we can restore it when the panel closes
    private CursorLockMode _savedLockMode;
    private bool           _savedCursorVisible;

    // Simulated item index counter — increments with each manual grant
    private int _debugItemIndex = 9000;

    // PanelX is derived at draw-time from Screen.width so the panel stays right-anchored
    // and never overlaps the StatusHUD which lives on the left edge.
    private const float PanelY  = 10;
    private const float PanelW  = 340;
    private const float BtnH    = 26;
    private const float Gap     = 4;
    private const float LabelH  = 20;
    private const int   Pages   = 11;

    // Teleport page — coordinate state (floats; adjusted with ± step buttons)
    private float _tpX = 0f;
    private float _tpY = 0f;
    private float _tpZ = 0f;

    private void Update()
    {
        var kb = UnityEngine.InputSystem.Keyboard.current;

        if (kb?.f9Key.wasPressedThisFrame == true)
        {
            _visible = !_visible;
            if (_visible)
            {
                // Unlock cursor so the player can click buttons
                _savedLockMode      = Cursor.lockState;
                _savedCursorVisible = Cursor.visible;
                Cursor.lockState    = CursorLockMode.None;
                Cursor.visible      = true;
            }
            else
            {
                // Restore game cursor state
                Cursor.lockState = _savedLockMode;
                Cursor.visible   = _savedCursorVisible;
            }
        }

        // Page navigation — handled in Update (not OnGUI) to avoid double-firing
        if (_visible)
        {
            if (kb?.qKey.wasPressedThisFrame == true) PagePrev();
            if (kb?.eKey.wasPressedThisFrame == true) PageNext();
        }
    }

    private void OnGUI()
    {
        if (!_visible) return;

        try
        {
            float x = Screen.width - PanelW - 10;
            float y = PanelY;

            // Page header
            GUI.Box(new Rect(x, y, PanelW, 28), $"AP Debug  [F9 close]  [Page {_page + 1}/{Pages}  Q/E prev/next]");
            y += 32;

            // Persistent player coordinates — visible on every page
            var playerGo = SceneContext.Instance?.Player;
            string coordText = playerGo != null
                ? $"Pos: ({playerGo.transform.position.x:F1}, {playerGo.transform.position.y:F1}, {playerGo.transform.position.z:F1})"
                : "Pos: (no player / main menu)";
            var coordPrev = GUI.color;
            GUI.color = new Color(0.7f, 1f, 0.7f);
            GUI.Label(new Rect(x + 4, y, PanelW - 8, LabelH), coordText);
            GUI.color = coordPrev;
            y += LabelH + 4;

            switch (_page)
            {
                case 0: DrawPageRegionAndUpgrades(x, y); break;
                case 1: DrawPageGadgetsTeleporters(x, y); break;
                case 2: DrawPageGadgetsDeployment(x, y);  break;
                case 3: DrawPageGadgetsFunctional(x, y);  break;
                case 4: DrawPageFiller(x, y);             break;
                case 5: DrawPageMisc(x, y);               break;
                case 6: DrawPageRadiant(x, y);            break;
                case 7: DrawPageGoals(x, y);              break;
                case 8: DrawPageDumps(x, y);              break;
                case 9:  DrawPageWeatherDumps(x, y);  break;
                case 10: DrawPageTeleport(x, y);      break;
            }

            // Nav buttons sit below 20 content rows (tallest page is ~18 rows)
            float navY = y + 20 * (BtnH + Gap) + 10;
            if (GUI.Button(new Rect(x,                 navY, 100, BtnH), "◀ Prev (Q)")) PagePrev();
            if (GUI.Button(new Rect(x + PanelW - 100,  navY, 100, BtnH), "Next ▶ (E)")) PageNext();
        }
        catch (System.Exception ex)
        {
            Logger.Error($"[AP] DebugPanel.OnGUI exception: {ex}");
        }
    }

    // -------------------------------------------------------------------------
    // Pages
    // -------------------------------------------------------------------------

    private void DrawPageRegionAndUpgrades(float x, float y)
    {
        y = SectionLabel(x, y, "Region Access");
        y = ItemBtn(x, y, "Ember Valley Access",      ItemTable.EmberValleyAccess);
        y = ItemBtn(x, y, "Starlight Strand Access",  ItemTable.StarlightStrandAccess);
        y = ItemBtn(x, y, "Powderfall Bluffs Access", ItemTable.PowderfallBluffsAccess);

        y = SectionLabel(x, y, "Upgrades");
        y = ItemBtn(x, y, "Progressive Health Tank",        ItemTable.ProgressiveHealthTank);
        y = ItemBtn(x, y, "Progressive Energy Tank",        ItemTable.ProgressiveEnergyTank);
        y = ItemBtn(x, y, "Progressive Extra Tank",         ItemTable.ProgressiveExtraTank);
        y = ItemBtn(x, y, "Progressive Jetpack",            ItemTable.ProgressiveJetpack);
        y = ItemBtn(x, y, "Progressive Water Tank",         ItemTable.ProgressiveWaterTank);
        y = ItemBtn(x, y, "Progressive Dash Boots",         ItemTable.ProgressiveDashBoots);
        y = ItemBtn(x, y, "Progressive Tank Booster",       ItemTable.ProgressiveTankBooster);
        y = ItemBtn(x, y, "Progressive Power Injector",     ItemTable.ProgressivePowerInjector);
        y = ItemBtn(x, y, "Progressive Regenerator",        ItemTable.ProgressiveRegenerator);
        y = ItemBtn(x, y, "Progressive Golden Sureshot",    ItemTable.ProgressiveGoldenSureshot);
        y = ItemBtn(x, y, "Progressive Shadow Sureshot",    ItemTable.ProgressiveShadowSureshot);
        y = ItemBtn(x, y, "Progressive Tank Guard",         ItemTable.ProgressiveTankGuard);
        y = ItemBtn(x, y, "Pulse Wave",           ItemTable.PulseWave);
        y = ItemBtn(x, y, "Resource Harvester",   ItemTable.ResourceHarvester);
        y = ItemBtn(x, y, "Drone Archive Key",    ItemTable.DroneArchiveKey);

        y = SectionLabel(x, y, "Crafting Components");
        y = ItemBtn(x, y, "Archive Key Component",  ItemTable.ArchiveKeyComponent);
        y = ItemBtn(x, y, "Sureshot Module",         ItemTable.SureshotModule);
        y = ItemBtn(x, y, "Tank Liner",              ItemTable.TankLiner);
        y = ItemBtn(x, y, "Heart Cell",              ItemTable.HeartCell);
        y = ItemBtn(x, y, "Power Chip",              ItemTable.PowerChip);
        y = ItemBtn(x, y, "Dash Boot Module",        ItemTable.DashBootModule);
        y = ItemBtn(x, y, "Jetpack Drive",           ItemTable.JetpackDrive);
        y = ItemBtn(x, y, "Storage Cell",            ItemTable.StorageCell);
        y = ItemBtn(x, y, "Shadow Sureshot Module",  ItemTable.ShadowSureshotModule);
        y = ItemBtn(x, y, "Injector Module",         ItemTable.InjectorModule);
        y = ItemBtn(x, y, "Regen Module",            ItemTable.RegenModule);
    }

    // Page 1: Zone + Home Teleporters
    private void DrawPageGadgetsTeleporters(float x, float y)
    {
        y = SectionLabel(x, y, "Zone Teleporters");
        y = ItemBtn(x, y, "Teleporter (Ember Valley)",      ItemTable.TeleporterEmberValley);
        y = ItemBtn(x, y, "Teleporter (Starlight Strand)",  ItemTable.TeleporterStarlightStrand);
        y = ItemBtn(x, y, "Teleporter (Powderfall Bluffs)", ItemTable.TeleporterPowderfallBluffs);
        y = ItemBtn(x, y, "Teleporter (Grey Labyrinth)",    ItemTable.TeleporterGreyLabyrinth);

        y = SectionLabel(x, y, "Home Teleporters");
        y = ItemBtn(x, y, "Home Teleporter Blue",   ItemTable.HomeTeleporterBlue);
        y = ItemBtn(x, y, "Home Teleporter Green",  ItemTable.HomeTeleporterGreen);
        y = ItemBtn(x, y, "Home Teleporter Red",    ItemTable.HomeTeleporterRed);
        y = ItemBtn(x, y, "Home Teleporter Yellow", ItemTable.HomeTeleporterYellow);
    }

    // Page 2: Warp Depots + Functional gadgets
    private void DrawPageGadgetsDeployment(float x, float y)
    {
        y = SectionLabel(x, y, "Warp Depots");
        y = ItemBtn(x, y, "Warp Depot (Grey/Ember Valley)", ItemTable.WarpDepotGrey);
        y = ItemBtn(x, y, "Warp Depot (Berry)",             ItemTable.WarpDepotBerry);
        y = ItemBtn(x, y, "Warp Depot (Violet)",            ItemTable.WarpDepotViolet);
        y = ItemBtn(x, y, "Warp Depot (Snowy)",             ItemTable.WarpDepotSnowy);

        y = SectionLabel(x, y, "Functional Gadgets");
        y = ItemBtn(x, y, "Market Link",          ItemTable.MarketLink);
        y = ItemBtn(x, y, "Super Hydro Turret",   ItemTable.SuperHydroTurret);
        y = ItemBtn(x, y, "Portable Scare Slime", ItemTable.PortableScareSlime);
        y = ItemBtn(x, y, "Gordo Snare Advanced", ItemTable.GordoSnareAdvanced);
        y = ItemBtn(x, y, "Med Station",          ItemTable.MedStation);
        y = ItemBtn(x, y, "Dream Lantern T2",     ItemTable.DreamLanternT2);
    }

    // Page 3: Special access + Archive Key Component
    private void DrawPageGadgetsFunctional(float x, float y)
    {
        y = SectionLabel(x, y, "Special Access");
        GUI.color = new Color(1f, 1f, 0.6f);
        if (GUI.Button(new Rect(x, y, PanelW, BtnH), "Unlock: Radiant Projector Blueprint (?)"))
            ItemHandler.DebugGrantRadiantProjector();
        y += BtnH + Gap;
        if (GUI.Button(new Rect(x, y, PanelW, BtnH), "Grant: Radiant Projector Unit (skip craft)"))
            ItemHandler.DebugGrantRadiantProjectorUnit();
        y += BtnH + Gap;
        GUI.color = Color.white;

        y = ItemBtn(x, y, "Archive Key Component", ItemTable.ArchiveKeyComponent);
    }

    private void DrawPageFiller(float x, float y)
    {
        y = SectionLabel(x, y, "Newbucks");
        y = ItemBtn(x, y, "250 Newbucks",  ItemTable.Newbucks250);
        y = ItemBtn(x, y, "500 Newbucks",  ItemTable.Newbucks500);
        y = ItemBtn(x, y, "1000 Newbucks", ItemTable.Newbucks1000);

        y = SectionLabel(x, y, "Plort Caches (10x random assortment → Refinery)");
        y = ItemBtn(x, y, "Common Plort Cache",   ItemTable.CommonPlortCache);
        y = ItemBtn(x, y, "Uncommon Plort Cache", ItemTable.UncommonPlortCache);
        y = ItemBtn(x, y, "Rare Plort Cache",     ItemTable.RarePlortCache);

        y = SectionLabel(x, y, "Craft Caches (10x random assortment → Refinery)");
        y = ItemBtn(x, y, "Rainbow Fields Craft Cache",    ItemTable.RainbowFieldsCraftCache);
        y = ItemBtn(x, y, "Ember Valley Craft Cache",      ItemTable.EmberValleyCraftCache);
        y = ItemBtn(x, y, "Starlight Strand Craft Cache",  ItemTable.StarlightStrandCraftCache);
        y = ItemBtn(x, y, "Powderfall Bluffs Craft Cache", ItemTable.PowderfallBluffsCraftCache);
        y = ItemBtn(x, y, "Grey Labyrinth Craft Cache",    ItemTable.GreyLabyrinthCraftCache);
        y = ItemBtn(x, y, "Rare Craft Cache",              ItemTable.RareCraftCache);

        y = SectionLabel(x, y, "Plorts → Vacpack ×20");
        GUI.color = new Color(1f, 0.85f, 0.2f);
        y = FoodBtn(x, y, "Gold Plort ×20", "GoldPlort");
        GUI.color = new Color(0.7f, 0.5f, 1f);
        y = FoodBtn(x, y, "Shadow Plort ×20 (Shadow Door testing)", "ShadowPlort");
        GUI.color = Color.white;

        // Food → Vacpack (for gordo-feeding tests on fresh playthroughs).
        // IdentifiableType names are best guesses based on SR2 naming conventions —
        // if a button logs "not found", run Dump IdentifiableTypes (Misc page) to get the real name.
        y = SectionLabel(x, y, "Food → Vacpack ×20 (?) [for gordo testing]");
        GUI.color = new Color(1f, 0.9f, 0.6f);
        y = FoodBtn(x, y, "Hen ×20",            "Hen");
        y = FoodBtn(x, y, "Rooster ×20",        "Rooster");
        y = FoodBtn(x, y, "PogoFruit ×20",      "PogoFruit");
        y = FoodBtn(x, y, "MoondewNectar ×20",  "MoondewNectar");
        y = FoodBtn(x, y, "CarrotVeggie ×20",   "CarrotVeggie");
        y = FoodBtn(x, y, "BeetVeggie ×20",     "BeetVeggie");
        GUI.color = Color.white;
    }

    private void DrawPageMisc(float x, float y)
    {
        y = SectionLabel(x, y, "Filler (misc)");
        y = ItemBtn(x, y, "Slime Ring",          ItemTable.SlimeRing);

        y = SectionLabel(x, y, "Traps");
        y = ItemBtn(x, y, "Tarr Spawn Trap",     ItemTable.TrapTarrSpawn);
        y = ItemBtn(x, y, "Teleport Trap",       ItemTable.TrapTeleport);
        y = ItemBtn(x, y, "Weather Change Trap", ItemTable.TrapWeatherChange);
        y = ItemBtn(x, y, "Tarr Rain Trap",      ItemTable.TrapTarrRain);

        y = SectionLabel(x, y, "Location Checks");
        y = CheckBtn(x, y, "Treasure Pod 1", 819000);
        y = CheckBtn(x, y, "Gordo 1",        819250);
        y = CheckBtn(x, y, "Map Node 1",     819300);

        y = SectionLabel(x, y, "DeathLink");
        if (GUI.Button(new Rect(x, y, PanelW, BtnH), "Kill Player (DeathLink)"))
            DeathLinkHandler.KillPlayer();
        y += BtnH + Gap;
    }

    // Page 6: Radiant slime tools
    private void DrawPageRadiant(float x, float y)
    {
        y = SectionLabel(x, y, "Radiant Spawn Rate");

        GUI.color = new Color(1f, 0.85f, 0.4f);
        if (GUI.Button(new Rect(x, y, PanelW, BtnH), "Set All Bag Sizes → 2  (every other spawn radiant)"))
            LocationDumper.SetRadiantBagSizes(2);
        y += BtnH + Gap;

        GUI.color = new Color(0.5f, 1f, 0.5f);
        if (GUI.Button(new Rect(x, y, PanelW, BtnH), "Force Radiant Spawn ON  (Harmony Postfix override)"))
            LocationDumper.SetForceRadiantSpawn(true);
        y += BtnH + Gap;

        GUI.color = new Color(1f, 0.5f, 0.5f);
        if (GUI.Button(new Rect(x, y, PanelW, BtnH), "Force Radiant Spawn OFF (restore normal bag draws)"))
            LocationDumper.SetForceRadiantSpawn(false);
        y += BtnH + Gap;

        GUI.color = Color.white;
        y = SectionLabel(x, y, "Gold / Lucky Spawn Weight");

        GUI.color = new Color(1f, 0.85f, 0.4f);
        if (GUI.Button(new Rect(x, y, PanelW, BtnH), "Set Gold/Lucky Weight → 1000  (near-guaranteed spawn)"))
            LocationDumper.SetGoldLuckySpawnWeight(1000f);
        y += BtnH + Gap;
        if (GUI.Button(new Rect(x, y, PanelW, BtnH), "Set Gold/Lucky Weight → 100   (very frequent)"))
            LocationDumper.SetGoldLuckySpawnWeight(100f);
        y += BtnH + Gap;

        GUI.color = Color.white;
    }

    // Page 7: Goal testing + Upgrade Components
    private void DrawPageGoals(float x, float y)
    {
        y = SectionLabel(x, y, "Goal Triggers");

        GUI.color = new Color(0.6f, 1f, 0.6f);
        if (GUI.Button(new Rect(x, y, PanelW, BtnH), "Force Goal Complete"))
            GoalHandler.NotifyGoalComplete();
        y += BtnH + Gap;

        if (GUI.Button(new Rect(x, y, PanelW, BtnH), "Force Check (newbucks / slimepedia)"))
            GoalHandler.DebugForceCheck();
        y += BtnH + Gap;

        if (GUI.Button(new Rect(x, y, PanelW, BtnH), "Set Lifetime Newbucks = Goal Amount"))
            GoalHandler.DebugSetLifetimeNewbucksToGoal();
        y += BtnH + Gap;

        if (GUI.Button(new Rect(x, y, PanelW, BtnH), "Sim: Prismacore — Enter Room"))
            GoalHandler.DebugSimPrismacore(stabilize: false);
        y += BtnH + Gap;

        if (GUI.Button(new Rect(x, y, PanelW, BtnH), "Sim: Prismacore — Stabilize"))
            GoalHandler.DebugSimPrismacore(stabilize: true);
        y += BtnH + Gap;

        if (GUI.Button(new Rect(x, y, PanelW, BtnH), "Sim: Labyrinth — Open Both Gates"))
            GoalHandler.DebugSimLabyrinth();
        y += BtnH + Gap;

        GUI.color = Color.white;

        y = SectionLabel(x, y, "Upgrade Components");
        y = ItemBtn(x, y, "Grant: Archive Key Component", ItemTable.ArchiveKeyComponent);
    }

    // Page 8: All data dump buttons
    private void DrawPageDumps(float x, float y)
    {
        GUI.color = new Color(1f, 0.9f, 0.5f);

        y = SectionLabel(x, y, "Location / Asset Dumps (→ BepInEx log)");
        if (GUI.Button(new Rect(x, y, PanelW, BtnH), "Dump Locations"))
            LocationDumper.DumpAll();
        y += BtnH + Gap;
        if (GUI.Button(new Rect(x, y, PanelW, BtnH), "Dump Gadget Names"))
            LocationDumper.DumpGadgets();
        y += BtnH + Gap;
        if (GUI.Button(new Rect(x, y, PanelW, BtnH), "Dump Pedia Entries"))
            LocationDumper.DumpPedia();
        y += BtnH + Gap;
        if (GUI.Button(new Rect(x, y, PanelW, BtnH), "Dump Upgrade Components"))
            LocationDumper.DumpUpgradeComponents();
        y += BtnH + Gap;
        if (GUI.Button(new Rect(x, y, PanelW, BtnH), "Dump IdentifiableTypes"))
            LocationDumper.DumpIdentifiableTypes();
        y += BtnH + Gap;
        if (GUI.Button(new Rect(x, y, PanelW, BtnH), "Dump Slime Zones (NativeZones)"))
            LocationDumper.DumpSlimeZones();
        y += BtnH + Gap;
        if (GUI.Button(new Rect(x, y, PanelW, BtnH), "Dump IdentType Zones (showForZones)"))
            LocationDumper.DumpIdentifiableTypeZones();
        y += BtnH + Gap;
        if (GUI.Button(new Rect(x, y, PanelW, BtnH), "Dump Conversations (gifts)"))
            LocationDumper.DumpConversations();
        y += BtnH + Gap;
        if (GUI.Button(new Rect(x, y, PanelW, BtnH), "Dump Conversation Conditions"))
            LocationDumper.DumpConversationConditions();
        y += BtnH + Gap;
        if (GUI.Button(new Rect(x, y, PanelW, BtnH), "Dump Access Doors"))
            LocationDumper.DumpAccessDoors();
        y += BtnH + Gap;
        if (GUI.Button(new Rect(x, y, PanelW, BtnH), "Dump Slime Gates"))
            LocationDumper.DumpSlimeGates();
        y += BtnH + Gap;
        if (GUI.Button(new Rect(x, y, PanelW, BtnH), "Dump Puzzle Slot Lockables"))
            LocationDumper.DumpPuzzleSlotLockables();
        y += BtnH + Gap;
        if (GUI.Button(new Rect(x, y, PanelW, BtnH), "Dump Scene Groups"))
            LocationDumper.DumpSceneGroups();
        y += BtnH + Gap;

        y = SectionLabel(x, y, "Spawn Weights");

        // ── Live loaded-scene list ────────────────────────────────────────────
        // Shows every Unity scene currently in memory. Zone scenes (starting with
        // "zone") are highlighted so you can see at a glance which streaming areas
        // are loaded. Walk to a new sub-area and watch for new entries before
        // pressing Export Spawn JSON.
        var prevColor = GUI.color;
        int sceneCount = UnityEngine.SceneManagement.SceneManager.sceneCount;
        for (int si = 0; si < sceneCount; si++)
        {
            var sc = UnityEngine.SceneManagement.SceneManager.GetSceneAt(si);
            bool isZone = sc.name.StartsWith("zone", System.StringComparison.OrdinalIgnoreCase);
            GUI.color = isZone ? new Color(0.4f, 1f, 0.7f) : new Color(0.4f, 0.4f, 0.5f);
            GUI.Label(new Rect(x + 4, y, PanelW - 8, LabelH), sc.name);
            y += LabelH;
        }
        if (sceneCount == 0)
        {
            GUI.color = new Color(0.4f, 0.4f, 0.5f);
            GUI.Label(new Rect(x + 4, y, PanelW - 8, LabelH), "(no scenes loaded)");
            y += LabelH;
        }
        GUI.color = prevColor;
        y += Gap;

        if (GUI.Button(new Rect(x, y, PanelW, BtnH), "Dump Slime Spawn Weights (PeriodicActorSpawner)"))
            LocationDumper.DumpSlimeSpawnWeights();
        y += BtnH + Gap;
        if (GUI.Button(new Rect(x, y, PanelW, BtnH), "Export Spawn JSON (append/merge)"))
            LocationDumper.ExportSpawnRatesJson();
        y += BtnH + Gap;
        if (GUI.Button(new Rect(x, y, PanelW, BtnH), "Clear Spawn JSON"))
            LocationDumper.ClearSpawnRatesJson();
        y += BtnH + Gap;

        y = SectionLabel(x, y, "Radiant Dumps");
        if (GUI.Button(new Rect(x, y, PanelW, BtnH), "Dump Radiant Slimes (pedia + bag sizes)"))
            LocationDumper.DumpRadiantSlimes();
        y += BtnH + Gap;
        if (GUI.Button(new Rect(x, y, PanelW, BtnH), "Dump Radiant Bag State"))
            LocationDumper.DumpRadiantBagState();
        y += BtnH + Gap;
        if (GUI.Button(new Rect(x, y, PanelW, BtnH), "Dump Allow Radiant Query"))
            LocationDumper.DumpAllowRadiantQuery();
        y += BtnH + Gap;
        if (GUI.Button(new Rect(x, y, PanelW, BtnH), "Dump Feral Spawner Radiant Flags"))
            LocationDumper.DumpFeralSpawners();
        y += BtnH + Gap;
        if (GUI.Button(new Rect(x, y, PanelW, BtnH), "Dump Gold/Lucky Spawn Weights"))
            LocationDumper.DumpGoldLuckySpawnWeights();
        y += BtnH + Gap;

        GUI.color = Color.white;
    }

    // Page 9: Weather dump buttons
    private void DrawPageWeatherDumps(float x, float y)
    {
        GUI.color = new Color(0.7f, 0.9f, 1f);

        y = SectionLabel(x, y, "Weather Dumps (→ BepInEx log)");

        GUI.color = new Color(1f, 0.9f, 0.5f);
        if (GUI.Button(new Rect(x, y, PanelW, BtnH), "Dump Weather Patterns (transition graphs)"))
            LocationDumper.DumpWeatherPatterns();
        y += BtnH + Gap;
        if (GUI.Button(new Rect(x, y, PanelW, BtnH), "Dump Weather Registry (runtime state)"))
            LocationDumper.DumpWeatherRegistry();
        y += BtnH + Gap;

        GUI.color = Color.white;
    }

    // Page 10: NoClip + Coordinate Teleport
    private void DrawPageTeleport(float x, float y)
    {
        // ── NoClip toggle ────────────────────────────────────────────────────
        y = SectionLabel(x, y, "Movement");
        bool noclip = NoClipManager.IsActive;
        GUI.color = noclip ? new Color(0.4f, 1f, 0.4f) : Color.white;
        if (GUI.Button(new Rect(x, y, PanelW, BtnH),
                noclip ? "NoClip  ON  [Space=up  LCtrl=down  LShift=3x]  (click to disable)"
                       : "NoClip  OFF  (click to enable)"))
            NoClipManager.Toggle();
        GUI.color = Color.white;
        y += BtnH + Gap;

        // ── Current position readout ─────────────────────────────────────────
        y = SectionLabel(x, y, "Coordinate Teleport");
        try
        {
            var pos = GetMotor()?._transientPosition;
            if (pos.HasValue)
            {
                GUI.color = new Color(0.6f, 1f, 0.6f);
                GUI.Label(new Rect(x + 4, y, PanelW - 8, LabelH),
                    $"Current:  X {pos.Value.x:F1}   Y {pos.Value.y:F1}   Z {pos.Value.z:F1}");
                GUI.color = Color.white;
            }
        }
        catch { /* SceneContext not ready */ }
        y += LabelH + Gap;

        if (!noclip)
        {
            GUI.color = new Color(1f, 0.75f, 0.4f);
            GUI.Label(new Rect(x + 4, y, PanelW - 8, LabelH), "Enable NoClip above to use teleport.");
            GUI.color = Color.white;
            return;
        }

        // ── Coordinate step buttons ──────────────────────────────────────────
        // GUI.TextField is stripped in this IL2CPP build; use ± buttons instead.
        // "Fill from Current" seeds the values; step buttons fine-tune them.
        // Step sizes: ±0.5 / ±5 / ±50 / ±500 — covers the full SR2 world range.
        y = DrawCoordRow(x, y, "X", ref _tpX);
        y = DrawCoordRow(x, y, "Y", ref _tpY);
        y = DrawCoordRow(x, y, "Z", ref _tpZ);
        y += 4;

        // ── Action buttons ───────────────────────────────────────────────────
        float half = (PanelW - Gap) / 2f;

        GUI.color = new Color(0.4f, 1f, 0.4f);
        if (GUI.Button(new Rect(x, y, half, BtnH), "Teleport"))
        {
            try
            {
                GetMotor()?.SetPosition(new Vector3(_tpX, _tpY, _tpZ));
                Logger.Info($"[AP] Teleport → ({_tpX:F2}, {_tpY:F2}, {_tpZ:F2})");
            }
            catch (System.Exception ex)
            {
                Logger.Error($"[AP] Teleport failed: {ex.Message}");
            }
        }

        GUI.color = new Color(0.7f, 0.7f, 1f);
        if (GUI.Button(new Rect(x + half + Gap, y, half, BtnH), "Fill from Current"))
        {
            try
            {
                var pos = GetMotor()?._transientPosition;
                if (pos.HasValue) { _tpX = pos.Value.x; _tpY = pos.Value.y; _tpZ = pos.Value.z; }
            }
            catch { /* SceneContext not ready */ }
        }

        GUI.color = Color.white;
    }

    /// <summary>
    /// Draws a labelled coordinate row: current value label + four pairs of ± step buttons.
    /// Step sizes: ±0.5, ±5, ±50, ±500. Returns the next Y position.
    /// </summary>
    private float DrawCoordRow(float x, float y, string label, ref float val)
    {
        // Value display
        GUI.color = Color.white;
        GUI.Label(new Rect(x, y, PanelW, LabelH), $"  {label}:  {val:F2}");
        y += LabelH + 2;

        // Six step buttons per row — three negative on the left, three positive on the right
        float[] steps = { 0.5f, 5f, 50f, 500f };
        float btnW = (PanelW - Gap * (steps.Length * 2 - 1)) / (steps.Length * 2);

        float bx = x;
        // Negative buttons (left → right: −500, −50, −5, −0.5)
        for (int i = steps.Length - 1; i >= 0; i--)
        {
            GUI.color = new Color(1f, 0.5f, 0.5f);
            if (GUI.Button(new Rect(bx, y, btnW, BtnH), $"−{steps[i]}")) val -= steps[i];
            bx += btnW + Gap;
        }
        // Positive buttons (left → right: +0.5, +5, +50, +500)
        for (int i = 0; i < steps.Length; i++)
        {
            GUI.color = new Color(0.5f, 1f, 0.5f);
            if (GUI.Button(new Rect(bx, y, btnW, BtnH), $"+{steps[i]}")) val += steps[i];
            bx += btnW + Gap;
        }

        GUI.color = Color.white;
        return y + BtnH + Gap + 2;
    }

    /// <summary>Returns the player's <see cref="KinematicCharacterController.KinematicCharacterMotor"/>, or null if unavailable.</summary>
    private static KinematicCharacterController.KinematicCharacterMotor? GetMotor()
    {
        var player = SceneContext.Instance?.Player;
        return player?.GetComponent<Il2CppMonomiPark.SlimeRancher.Player.CharacterController.SRCharacterController>()?._motor;
    }

    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    private float SectionLabel(float x, float y, string text)
    {
        var prev = GUI.color;
        GUI.color = new Color(0.7f, 0.9f, 1f);
        GUI.Label(new Rect(x, y, PanelW, LabelH), $"── {text} ──");
        GUI.color = prev;
        return y + LabelH;
    }

    private float ItemBtn(float x, float y, string label, long itemId)
    {
        if (GUI.Button(new Rect(x, y, PanelW, BtnH), label))
            ItemHandler.ApplyById(itemId, _debugItemIndex++);
        return y + BtnH + Gap;
    }

    private float FoodBtn(float x, float y, string label, string typeName)
    {
        if (GUI.Button(new Rect(x, y, PanelW, BtnH), label))
            ItemHandler.DebugGrantFoodToVacpack(typeName, 20);
        return y + BtnH + Gap;
    }

private float CheckBtn(float x, float y, string label, long locationId)
    {
        if (GUI.Button(new Rect(x, y, PanelW, BtnH), $"Check: {label}"))
            Plugin.Instance.ApClient.SendCheck(locationId);
        return y + BtnH + Gap;
    }

    private void PageNext() => _page = (_page + 1) % Pages;
    private void PagePrev() => _page = (_page + Pages - 1) % Pages;
}
#endif
