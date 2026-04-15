# SlimeRancher2-AP — apworld Design Document

This file covers the design of the companion Archipelago Python apworld.
The C# BepInEx mod lives in this repository; the apworld will live in a separate repo.
This file is the authoritative reference for decisions that must be consistent between both sides.

---

## ID Scheme

Base offset: **819000** — must be identical in both the mod and the apworld.

### Locations (checks sent to AP server)

| Range | Category | Confirmed Count | Slots | Default On? |
|---|---|---|---|---|
| 819000–819199 | Treasure Pods (all regions incl. Labyrinth) | 140 | 200 | ✅ Yes |
| 819200–819249 | Shadow Plort Doors (Grey Labyrinth only) | 25 | 50 | Goal-dependent |
| 819250–819299 | Gordo Slimes (wild/static, 17 total) | 17 | 50 | ✅ Yes |
| 819300–819349 | Map Data Nodes (15 total) | 15 | 50 | ⚙️ Optional |
| 819350–819399 | Slimepedia Entries (29 — all Slimes category) | 29 | 50 | ⚙️ Optional |
| 819400–819449 | Fabricator — Vacpack Upgrade crafts (38 tiers, 15 upgrades) | 38 | 50 | ✅ Yes |
| 819450–819479 | Research Drones / Narrative Drones (23 total) | 23 | 30 | ⚙️ Optional |
| 819480–819494 | Ghostly Drones (10 total) | 10 | 15 | ⚙️ Optional |
| 819495–819499 | **Reserved** | — | 5 | — |
| 819700–819714 | Conversation Key Gifts (15 total) | 15 | 15 | ⚙️ Option-dependent |
| 819715–819762 | Conversation Decoration Gifts (48 total) | 48 | 48 | ⚙️ Option-dependent |
| 819763–819816 | Conversation Non-Gift (story/deflect, 54 total) | 54 | 54 | ⚙️ Option-dependent |

> **Shadow Plort Doors** are included only when the goal requires entering or completing the Grey
> Labyrinth. They are excluded when the goal is Newbucks or another non-Labyrinth win condition.

> **Conversation locations** are controlled by the `conversation_checks` YAML option (see below).
> Which specific conversations are active depends on the chosen mode.

### Items (received from AP server)

| Range | Category | Notes |
|---|---|---|
| 819500–819509 | Region Access | 1 item per gated region (Ember Valley, Starlight Strand, Powderfall Bluffs; 4th slot reserved) |
| 819510 | Special Access — Radiant Projector Blueprint | Always in pool; unlocks Grey Labyrinth beam entrance |
| 819511–819514 | Crafting Components (I) | Archive Key Component, Sureshot Module, Tank Liner, Heart Cell — always in pool (progression) |
| 819515–819529 | Progressive Vacpack Upgrades | 15 item IDs, each received N times (38 tiers total) |
| 819530–819536 | Crafting Components (II) | Power Chip, Dash Boot Module, Jetpack Drive, Storage Cell, Shadow Sureshot Module, Injector Module, Regen Module — always in pool (progression) |
| 819537–819539 | Reserved | — |
| 819540–819579 | Gadgets | Teleporters, Warp Depots, functional utilities |
| 819580–819589 | Filler — Newbucks | 250 / 500 / 1000 tiers |
| 819590–819599 | Filler — Plort Caches | Common / Uncommon / Rare (5 random plorts each) |
| 819600–819609 | Filler — Craft Caches | Common / Rare (5 random craft materials each) |
| 819610–819629 | Traps | Slime Ring / Tarr Spawn / Teleport / Weather Change / Slime Rain |
| 819630–819639 | Market Items (when `randomize_market: true`) | Market Boom / Saturation Reset / Market Insight |
| 819640–819649 | Market Traps (when `randomize_market: true`) | Market Crash / Market Shutdown / Oversaturation |
| 819650–819699 | **Reserved** for future items | |

---

## Goals

Five goal options, selectable as a YAML option in the apworld. Each changes what locations are
in-logic and what the win condition triggers are.

### Goal 1 — Open the Grey Labyrinth (`"labyrinth_open"`)
- **Win condition**: Both Grey Labyrinth entrances are opened (the light-beam puzzles solved)
- **Mod detection**: ✅ Implemented — both entrances use `EnergyBeamReceiver` → `WorldStateInvisibleSwitch.SetStateForAll(DOWN)`. Detected via `InvisibleSwitchPatch` (Harmony Postfix on `WorldStateInvisibleSwitch.SetStateForAll`). Strand: scene `zoneStrandLabyrinthGate`, switch `energyBeamReceiver` ✅. Valley: scene `zoneGorgeGateTransfer`, switch `energyBeamReceiver` ✅. Both gates confirmed via in-game log. `GoalHandler` deduplicates per-frame beam pulses via `HashSet.Add()`.
- **Logic requirements**:
  - Ember Valley Access (in item pool)
  - Starlight Strand Access (in item pool)
  - Radiant Projector (in item pool — craftable gadget; 2 needed per entrance, but reusable so 2 suffice)
- **Shadow Plort Doors**: NOT in location pool (player hasn't been inside yet)
- **Grey Labyrinth Gordos/Nodes**: NOT in location pool
- **Note**: If `conversation_checks` includes key items, `ViktorStoryCipher4` (Radiant Projector blueprint) becomes a location check — so Viktor's cipher story IS gated content

### Goal 2 — Newbucks Milestone (`"newbucks"`)
- **Win condition**: Player accumulates a configurable Newbucks threshold
- **Mod detection**: ✅ Implemented — `GoalHandler.CheckNewbucksGoal()` polls `PlayerModel._currencies[persistenceId].AmountEverCollected` (lifetime total, unaffected by spending) each second; `CurrencyDefinition.PersistenceId` cached on connect
- **Logic requirements**: Minimal — any region access needed to reach farming locations
- **Shadow Plort Doors**: NOT in location pool
- **Grey Labyrinth**: NOT required; content excluded from logic
- **YAML option**: `newbucks_goal_amount` — integer, default 50000

### Goal 3 — Enter the Prismacore (`"prismacore_enter"`)
- **Win condition**: Player enters the Prismacore area for the first time
- **Mod detection**: ✅ Implemented — `CoreRoomController.UpdateState` Postfix fires `GoalHandler.OnCoreRoomStateChanged(PRE_FIGHT)`. `PRE_FIGHT` is set when the player first enters the Prismacore room.
- **Logic requirements**: Everything for Goal 1, plus traversing enough of the Grey Labyrinth to reach the Prismacore entrance
- **Shadow Plort Doors**: In location pool
- **Grey Labyrinth Gordos/Nodes**: In location pool

### Goal 4 — Stabilize the Prismacore (`"prismacore_stabilize"`)
- **Win condition**: Main story completion — Prismacore stabilization cutscene triggers
- **Mod detection**: ✅ Implemented — same `CoreRoomController.UpdateState` Postfix fires `GoalHandler.OnCoreRoomStateChanged(POST_FIGHT)`. `POST_FIGHT` is set after the Prismacore is stabilized.
- **Logic requirements**: Everything for Goal 3, plus the Gigi boss fight / Prismacore stabilization sequence
- **Shadow Plort Doors**: In location pool
- **This is the "full" randomizer experience**

### Goal 5 — Complete the Slimepedia: Slimes (`"slimepedia_slimes"`)
- **Win condition**: All 29 entries in the Slimepedia "Slimes" category are unlocked
- **Mod detection**: ✅ Implemented — `GoalHandler.CheckSlimepediaSlimesGoal()` polls `PediaRuntimeCategory.AllUnlocked()` on the `"Slimes"` category each second (60-frame throttle)
- **Requires**: `randomize_slimepedia: true` — otherwise slimepedia entries are not in the location pool and the goal is trivially completable without any AP interaction
- **Logic requirements**:
  - Access to all regions that have exclusive slimes (Ember Valley, Starlight Strand, Powderfall Bluffs for most rare/uncommon slimes)
  - Gordo Snare Advanced (or at least Gordo Snare Novice) to pop Gordos whose pops reveal slimes — Gordo pedia entry unlocks on first encounter with any Gordo
  - Lucky Slime / Gold Slime are random rare spawns and should be in-logic as long as any region is accessible (they can appear anywhere)
  - Tarr, Largo, and FeralSlime unlock naturally through gameplay in any accessible region
  - Shadow Slime requires Grey Labyrinth access
- **Shadow Plort Doors**: NOT required by the goal itself, but if Grey Labyrinth access is needed for Shadow Slime, they may be in pool
- **Notes**:
  - All 29 entries confirmed via `DumpPedia()` (2026-04-10): Pink, Cotton, Tabby, Phosphor, Angler, Rock, Batty, Flutter, Ringtail, Boom, Honey, Puddle, Crystal, Hunter, Fire, Lucky, Gold, Saber, Tangle, Dervish, Yolky, Sloomber, Twin, Hyper, Shadow, Tarr, Largo, FeralSlime, Gordo
  - Category name `"Slimes"` confirmed — `GoalHandler` uses this exact string
  - IDs 819350–819378; all 29 in `LocationTable` with `LocationType.SlimepediaEntry`

---

## Location Categories — Design Notes

### Treasure Pods (140 confirmed)
- Static world objects, one-time loot
- Patch point: `TreasurePod.Activate()` Postfix
- Lookup key: posKey (`"sceneName_X_Y_Z"`) — gameObject.name is not unique across scenes
- **Always in pool** for all goals; Grey Labyrinth pods (range reserved, none currently exist) only for Goals 3–4

### Shadow Plort Doors (25, Grey Labyrinth only)
- Cost Shadow Plorts to open; function like treasure pods inside the Labyrinth
- Only in pool for Goals 3 and 4
- Patch class: `PlortDepositor.ActivateOnFill()` Postfix — fires exactly once when deposit completes
- Filter: `__instance._catchIdentifiableType.name == "ShadowPlort"` — PlortDepositor is also used for non-Labyrinth plort locks
- Identity: posKey via `WorldUtils.PositionKey(__instance.gameObject)` — all doors share `gameObject.name = "TriggerActivate"`, so name lookup alone doesn't work

### Gordo Slimes (16 confirmed)
- Popping a Gordo is a one-time event per save
- Patch point: `GordoEat.ImmediateReachedTarget()` Postfix
- **Always in pool** for all goals
- Wild Gordos only — Gordo Snare spawned Gordos NOT included (not static locations)
- Per-region: Rainbow Fields 3, Ember Valley 5, Starlight Strand 5, Powderfall Bluffs 1, Grey Labyrinth 3 (Sloomber + Twin + Kinetic)
- Quirks: `gordoAngler (1)`, `gordoFlutter (1)` have Unity auto-suffix; `SloomberGordo` has no "gordo" prefix; `gordoKinetic` discovered via AP-Dump (not in original design), ID 819267
- Note: ID 819251 is intentionally unused (originally reserved for a second Pink Gordo that does not exist)
- Grey Labyrinth Gordos (3) only in pool for Goals 3 and 4

### Map Data Nodes (15 confirmed)
- Static collectibles across all regions
- Patch point: `MapNodeActivator.Activate()` Postfix
- **Optional** — YAML option `include_map_nodes: true`
- Grey Labyrinth nodes only in pool for Goals 3 and 4

### Slimepedia Entries (29 confirmed)
- Triggered by first encounter with a slime species (or Tarr, Gordo, Largo, FeralSlime)
- Patch point: `PediaDirector.Unlock(PediaEntry entry, bool showPopup)` Postfix — returns `true` only on first unlock; re-discoveries and save-load replay all return `false` and are skipped
- Identity: `PediaEntry.name` — confirmed via `DumpPedia()` (2026-04-10); all 29 `"Slimes"` category entries match the `EntryName` values in `LocationTable`
- All 29 entries in the `"Slimes"` PediaCategory are location checks: the 26 unique species + Tarr, Largo, FeralSlime, and Gordo (all required for `PediaRuntimeCategory.AllUnlocked()` to return true for the goal)
- **Optional** — YAML option `randomize_slimepedia: true` (default false); mod slot data key `"randomize_slimepedia"`
- No gift suppression needed — pedia entries are informational, not progression items

### Fabricator — Vacpack Upgrade Crafts (38 tiers, 15 upgrades)
- Each upgrade tier crafted at the Fabricator for the first time is a location check
- Crafting is the check; the upgrade itself is the AP item placed by the apworld
- Patch point: `PlayerUpgradeFabricatableItem.FabricateAndSpendCost()` Postfix
- Deduplication: stateful counter per upgrade name, compared against completed location IDs
- **Always in pool** for all goals
- Late-game tiers requiring Prisma Plorts are out-of-logic for goals that don't reach Prismacore
- Tiers requiring Shadow Plorts (`ShadowSureshot`, `TankGuard` late) require Labyrinth access
- Tiers requiring Sloomber/Twin/Hyper Plorts require access to zones where those slimes live

**Complete upgrade list (confirmed from game dump + wiki 2026-04-10):**

| Upgrade | Game Name | Tiers | Notes |
|---|---|---|---|
| Resource Harvester | `ResourceNodeHarvester` | 1 | Cotton Plort + Newbucks only |
| Heart Module | `HealthCapacity` | 4 | Tier 1: Pink Plort only; tiers 2–4: HeartModuleComponent |
| Power Core | `EnergyCapacity` | 5 | Tier 1: Cotton Plort only; tiers 2–5: PowerCoreComponent; tier 5 needs Prisma Plort |
| Tank Booster | `AmmoCapacity` | 8 | Tier 1: Tabby Plort only; tiers 2–8: TankBoosterComponent; tier 5+ needs Sloomber/Twin/Hyper Plort |
| Extra Tank | `AmmoSlots` | 2 | Tier 1: Rock Plort + Silky Sand; tier 2: ExtraTankComponent |
| Jetpack | `Jetpack` | 2 | Tier 1: Phosphor Plort + Radiant Ore; tier 2: JetpackComponent |
| Dash Boots | `RunEfficiency` | 2 | Tier 1: Boom Plort; tier 2: DashBootComponent |
| Pulse Wave | `PulseWave` | 1 | Flutter Plort + Jellystone + Wild Honey only |
| Water Tank | `LiquidSlot` | 1 | Deep Brine only |
| Tank Guard | `TankGuard` | 3 | All tiers: TankGuardComponent; requires Hunter Plort (Ember Valley) |
| Golden Sureshot | `GoldenSureshot` | 3 | All tiers: SureShotComponent + Gold Plort + Saber Plort |
| Shadow Sureshot | `ShadowSureshot` | 1 | ShadowSureShotComponent + Sloomber/Twin/Hyper Plort — requires Labyrinth |
| Drone Archive Key | `ArchiveKey` | 1 | ArchiveKeyComponent (gifted by Mochi's MochiStoryDrones3 in vanilla; AP item when conversation is a check) |
| Power Injector | `EnergyDelay` | 2 | All tiers: PowerInjectorComponent + Sloomber/Prisma Plort — late game |
| Regenerator | `EnergyRegen` | 2 | All tiers: RegenComponent + Twin/Sloomber/Hyper Plort — late game |
| **Total** | | **38** | |

### Research Drones / Narrative Drones (23)
- Gigi's journal entry drones; player approaches to interact
- Patch class: `ResearchDroneActivator.OnInteract()` Postfix
- Identity: `_researchDroneController.ResearchDroneEntry` asset name
- **Optional** — YAML option `include_research_drones: true`

### Ghostly Drones (10)
- Flying mechanical drones that give a Drone Station Module when interacted with
- Patch class: `ComponentAcquisitionDroneUIInteractable.OnInteract()` Postfix (world drone, NOT ProntoMart purchase)
- Identity: `_componentAcqDrone.gameObject.name` → posKey lookup (same mechanism as treasure pods)
- **Optional** — YAML option `include_ghostly_drones: true`
- Per-region: Conservatory 3, Rainbow Fields 1, Ember Valley 1, Starlight Strand 1, Powderfall Bluffs 1, Grey Labyrinth 3
- Grey Labyrinth drones only in pool for Goals 3 and 4

### CommStation Conversations
Controlled by YAML option `conversation_checks` with four modes:

| Mode | Slot Data Value | Locations Included | Count |
|---|---|---|---|
| No conversations | `"none"` | Nothing | 0 |
| Key items only | `"key_items"` | Conversations gifting functional/progression items | 14 |
| All gifts | `"all_gifts"` | All conversations gifting any item (incl. decorations) | 62 |
| Every conversation | `"all"` | All of the above + story/lore/deflect dialogue | 116 |

**Mod-side hook**: `FixedConversation.RecordPlayed()` Prefix/Postfix — captures `HasBeenPlayed()` before the original call, sends check only on first-ever completion. Multi-gift conversations produce exactly ONE check regardless of how many gift pages they contain.

**Identity**: `AbstractConversation.GetDebugName()` — stable Unity asset name (e.g. `"ViktorGift1_TeleporterPink"`); stored in `LocationTable.GameObjectName` for conversation entries.

#### Key Gift Conversations (IDs 819700–819714)
These are functional/progression items — included in `key_items` mode and above:

| ID | Location Name | Debug Name | Gifted Item | Rancher | Likely Trigger |
|---|---|---|---|---|---|
| 819700 | Viktor: Intro Call | `ViktorIntroCall` | TeleporterHomeYellow | Viktor | Very first phone call at game start (scripted, not CommStation) |
| 819701 | Viktor: Teleporter Pink | `ViktorGift1_TeleporterPink` | TeleporterPink | Viktor | 1st gordo milestone |
| 819702 | Viktor: Teleporter Blue | `ViktorGift2_TeleporterBlue` | TeleporterBlue | Viktor | 2nd gordo milestone |
| 819703 | Viktor: Teleporter Grey | `ViktorGift3_TeleporterGrey` | TeleporterGrey | Viktor | 3rd gordo milestone |
| 819704 | Viktor: Teleporter Violet | `ViktorGift4_TeleporterViolet` | TeleporterViolet | Viktor | 4th gordo milestone |
| 819705 | Viktor: Home Teleporter Blue | `ViktorGift_TeleporterHome1Blue` | TeleporterHomeBlue | Viktor | Relationship tier |
| 819706 | Viktor: Home Teleporter Red | `ViktorGift_TeleporterHome2Red` | TeleporterHomeRed | Viktor | Relationship tier |
| 819707 | Viktor: Home Teleporter Green | `ViktorGift_TeleporterHome3Green` | TeleporterHomeGreen | Viktor | Relationship tier |
| 819708 | Viktor: Radiant Projector Blueprint | `ViktorStoryCipher4` | EnergyBeamNode | Viktor | 4th cipher story beat |
| 819709 | Thora: Gordo Snare Advanced | `ThoraGift2` | GordoSnareAdvanced | Thora | 2nd Thora gift tier |
| 819710 | Ogden: Super Hydro Turret | `OgdenGift1` | SuperHydroTurret | Ogden | 1st Ogden gift |
| 819711 | Ogden: Portable Scare Slime | `OgdenGift3` | PortableScareSlime | Ogden | 3rd Ogden gift |
| 819712 | Mochi: Market Link | `MochiGift1` | MarketLink | Mochi | 1st Mochi gift |
| 819713 | Mochi: Archive Key | `MochiStoryDrones3` | ArchiveKeyComponent | Mochi | 3rd drone story beat |
| 819714 | Viktor: Gadget Introduction | `ViktorGift_GadgetIntro` | MedStation + SimpleTable + SimpleChair | Viktor | CommStation workshop intro |

#### Notes on Conversation Gift Logic
- **GiftPrefab conversations** (e.g. `ViktorGift_FlagAttentionPrefab`) gift a pre-crafted unit rather than a blueprint — these are NOT location checks. They are the "sample unit" conversations that fire after the blueprint is unlocked.
- **GiftGadget-only conversations** are excluded from AP checks for the same reason.
- **Conversation trigger conditions** are fully confirmed via `DumpConversationConditions()` runtime walk of `FixedConversation.conditions` (2026-04-10). See Conversation Location Access section below.
- **Gift suppression** (✅ implemented): `ConversationActiveTrackerPatch` + Prefix patches on all three `ApplyChanges()` gift page types return false when the conversation is an active AP location, blocking the vanilla in-game gift.

---

## Item Pool Design

### Region Access Items
| Item | ID | Gate Mechanism |
|---|---|---|
| Ember Valley Access | 819500 | `WorldStatePrimarySwitch.SetStateForAll` Prefix blocks passage |
| Starlight Strand Access | 819501 | Same |
| Powderfall Bluffs Access | 819502 | Same |
| *(4th slot reserved)* | 819503–819509 | — |
| Radiant Projector Blueprint | 819510 | Grants `EnergyBeamNode` GadgetDefinition blueprint via `AddBlueprint` |

**Note on Radiant Projector**: In vanilla, the blueprint is gifted via `ViktorStoryCipher4`. When `conversation_checks = key_items` (or higher), that conversation becomes a location check and the in-game gift is suppressed (✅ suppression implemented via `ConversationPageGiftBlueprint.ApplyChanges()` Prefix returning false). The Radiant Projector Blueprint (819510) is then a standalone AP item that can be placed anywhere in the world. When `conversation_checks = none`, Viktor always gives the blueprint directly (vanilla behavior unchanged) and 819510 is not in the item pool.

### Progressive Vacpack Upgrades
Each AP item ID is sent multiple times; the mod applies one tier per receipt using `ActorUpgradeHandler.ApplyUpgrade(def, absoluteLevel)`.
All 15 upgrades confirmed from game dump + wiki (2026-04-10). Total 38 tiers = 38 location checks, 15 AP item IDs (each received N times).

| Item | ID | Tiers | Game Name |
|---|---|---|---|
| Progressive Health Tank | 819515 | 4 | `HealthCapacity` |
| Progressive Energy Tank | 819516 | 5 | `EnergyCapacity` |
| Progressive Extra Tank | 819517 | 2 | `AmmoSlots` |
| Progressive Jetpack | 819518 | 2 | `Jetpack` |
| Progressive Water Tank | 819519 | 1 | `LiquidSlot` |
| Progressive Dash Boots | 819520 | 2 | `RunEfficiency` |
| Progressive Tank Booster | 819521 | 8 | `AmmoCapacity` |
| Progressive Power Injector | 819522 | 2 | `EnergyDelay` |
| Progressive Regenerator | 819523 | 2 | `EnergyRegen` |
| Progressive Golden Sureshot | 819524 | 3 | `GoldenSureshot` |
| Progressive Shadow Sureshot | 819525 | 1 | `ShadowSureshot` |
| Progressive Tank Guard | 819526 | 3 | `TankGuard` |
| Pulse Wave | 819527 | 1 | `PulseWave` |
| Resource Harvester | 819528 | 1 | `ResourceNodeHarvester` |
| Drone Archive Key | 819529 | 1 | `ArchiveKey` |

#### Crafting Components (IDs 819511–819514, 819530–819536)

These are fabricator recipe ingredients received from the AP item pool — one copy per craft tier that consumes them. All are `progression` classification. Game asset names confirmed via upgrade_components.txt dump (2026-04-13).

| Item | ID | Qty | Game Asset (`UpgradeComponent.name`) | Consumed by |
|---|---|---|---|---|
| Archive Key Component | 819511 | 1 | `ArchiveKeyComponent` | Drone Archive Key |
| Sureshot Module | 819512 | 3 | `SureShotComponent` | Golden Sureshot I/II/III |
| Tank Liner | 819513 | 3 | `TankGuardComponent` | Tank Guard I/II/III |
| Heart Cell | 819514 | 3 | `HeartModuleComponent` | Health Tank II/III/IV |
| Power Chip | 819530 | 4 | `PowerCoreComponent` | Energy Tank II/III/IV/V |
| Dash Boot Module | 819531 | 1 | `DashBootComponent` | Dash Boots II |
| Jetpack Drive | 819532 | 1 | `JetpackComponent` | Jetpack II |
| Storage Cell | 819533 | 7 | `TankBoosterComponent` | Tank Booster II–VIII |
| Shadow Sureshot Module | 819534 | 1 | `ShadowSureShotComponent` | Shadow Sureshot |
| Injector Module | 819535 | 2 | `PowerInjectorComponent` | Power Injector I/II |
| Regen Module | 819536 | 2 | `RegenComponent` | Regenerator I/II |

**Note on Archive Key Component**: In vanilla, `ArchiveKeyComponent` is gifted by Mochi's `MochiStoryDrones3` conversation. That conversation is always a location check (ID 819713) when `conversation_checks >= key_items`, suppressing the vanilla gift. The Archive Key Component item (819511) is always in the AP pool regardless of `conversation_checks` setting — if conv_checks = none and the vanilla gift fires, the mod must suppress it to avoid a double-grant.

### Gadgets (received, not crafted)
Grants blueprint via `GadgetDirector.AddBlueprint(gadgetDef, false)`.

**Confirmed GadgetDefinition asset names** (from runtime dump — 357 gadgets total):

| Item | Asset Name |
|---|---|
| Teleporter (Gorge/Ember Valley) | `TeleporterGrey` |
| Teleporter (Strand/Starlight) | `TeleporterPink` |
| Teleporter (Bluffs/Powderfall) | `TeleporterBlue` |
| Teleporter (Labyrinth) | `TeleporterViolet` |
| Teleporter (zone Berry) | `TeleporterBerry` |
| Teleporter (zone Snowy) | `TeleporterSnowy` |
| Home Teleporter Blue | `TeleporterHomeBlue` |
| Home Teleporter Green | `TeleporterHomeGreen` |
| Home Teleporter Red | `TeleporterHomeRed` |
| Home Teleporter Yellow | `TeleporterHomeYellow` |
| Warp Depot (Grey) | `WarpDepotGrey` |
| Warp Depot (Berry) | `WarpDepotBerry` |
| Warp Depot (Violet) | `WarpDepotViolet` |
| Warp Depot (Snowy) | `WarpDepotSnowy` |
| Radiant Projector | `EnergyBeamNode` |
| Market Link | `MarketLink` |
| Super Hydro Turret | `SuperHydroTurret` |
| Portable Scare Slime | `PortableScareSlime` |
| Gordo Snare Advanced | `GordoSnareAdvanced` |
| Med Station | `MedStation` |
| Dream Lantern T2 | `DreamLanternT2` |

**Note on zone teleporters**: When `conversation_checks` includes key items, the zone teleporters from Viktor (`TeleporterPink/Blue/Grey/Violet`) become AP location checks and their in-game grant is suppressed. In this case, the zone teleporter gadgets ARE in the AP item pool as items players can receive from any location. When `conversation_checks = none`, Viktor always gives them directly and they are removed from the item pool (players have vanilla access).

### Filler Items
| Item | Notes |
|---|---|
| 250 / 500 / 1000 Newbucks | `PlayerState.AddCurrency(currency.Cast<ICurrency>(), amount)` |
| Common Plort Cache | 5 random common plorts → Refinery via `GadgetDirector.AddItem` |
| Uncommon Plort Cache | 5 random uncommon plorts → Refinery |
| Rare Plort Cache | 5 random rare plorts → Refinery |
| Common Craft Cache | 5 random common craft materials → Refinery |
| Rare Craft Cache | 5 random rare craft materials → Refinery |

**Confirmed plort IdentifiableType names** (from runtime dump):
- Common: `PinkPlort`, `TabbyPlort`, `RockPlort`, `PhosphorPlort`, `CottonPlort`, `HoneyPlort`, `CrystalPlort`, `BoomPlort`, `BattyPlort`
- Uncommon: `DervishPlort`, `TanglePlort`, `HunterPlort`, `RingtailPlort`, `FlutterPlort`, `AnglerPlort`, `YolkyPlort`
- Rare: `HyperPlort`, `TwinPlort`, `SaberPlort`, `ShadowPlort`, `GoldPlort`, `PuddlePlort`, `SloomberPlort`, `FirePlort`, `UnstablePlort`, `StablePlort`

**Confirmed craft material IdentifiableType names** (from runtime dump):
- Common: `JellystoneCraft`, `SlimeFossilCraft`, `TinPetalCraft`, `BuzzWaxCraft`, `WildHoneyCraft`, `SilkySandCraft`, `AquaGlassCraft`, `DreamBubbleCraft`, `SunSapCraft`, `RadiantOreCraft`
- Rare: `StrangeDiamondCraft`, `BlackIndigoniumCraft`, `MagmaCombCraft`, `PrimordyOilCraft`, `DeepBrineCraft`, `LavaDustCraft`, `PerfectSnowflakeCraft`, `RoyalJellyCraft`, `DriftCrystalCraft`, `LightningMoteCraft`, `StormGlassCraft`

### Traps
| Trap | ID | Mod Status | Effect |
|---|---|---|---|
| Slime Ring | 819610 | ✅ Implemented (untested) | Spawns 8 random common slimes (Pink/Tabby/Rock/Cotton/Honey/Phosphor/Crystal/Ringtail) in a ring around the player, dropped from 3–5 m above |
| Tarr Spawn | 819611 | ✅ Implemented (untested) | Spawns 2 Tarr at 2–4 m from the player at head height so they immediately aggro |
| Teleport | 819612 | ✅ Implemented, verified in-game | Teleports player to a random accessible zone entrance (excludes current zone; respects region gate state) |
| Weather Change | 819613 | ✅ Implemented (untested) | Starts a random Heavy or Slime Rain weather state for 3 minutes; restores natural weather cycle after |
| Slime Rain | 819614 | ✅ Implemented (untested) | Finds the Slime Rain `WeatherStateDefinition`, overrides all `SpawnActorActivity.ActorType` fields to `Tarr`, triggers the weather for 3 minutes, then restores original actor types on reset. Falls back to Slime Ring if the Slime Rain weather state is not found. |

---

## Slot Data Keys (mod side reads these from AP server on connect)

| Key | Type | Values / Default | Controls |
|---|---|---|---|
| `goal` | string | `"labyrinth_open"` / `"newbucks"` / `"prismacore_enter"` / `"prismacore_stabilize"` / `"slimepedia_slimes"` | Win condition |
| `death_link` | bool | false | DeathLink enabled |
| `trap_link` | bool | false | Trap items enabled |
| `newbucks_goal_amount` | int | 50000 | Threshold for newbucks goal |
| `randomize_gordos` | bool | true | Gordo locations in pool |
| `randomize_pods` | bool | true | Treasure pod locations in pool |
| `randomize_map_nodes` | bool | false | Map node locations in pool |
| `randomize_slimepedia` | bool | false | Slimepedia Slimes entries in pool; required for `slimepedia_slimes` goal |
| `randomize_research_drones` | bool | false | Research drone locations in pool |
| `randomize_ghostly_drones` | bool | false | Ghostly drone locations in pool |
| `zone_teleporter_mode` | string | `"item"` / `"bundled"` / `"auto"` | How zone teleporters are granted |
| `conversation_checks` | string | `"none"` / `"key_items"` / `"all_gifts"` / `"all"` | Which conversation completions are locations |

### YAML Option Definitions (apworld side)

| Option | Python Type | Range / Choices | Default |
|---|---|---|---|
| `goal` | `Choice` | `labyrinth_open` / `newbucks` / `prismacore_enter` / `prismacore_stabilize` / `slimepedia_slimes` | `labyrinth_open` |
| `newbucks_goal_amount` | `Choice` | 10000 / 25000 / 50000 / 100000 / 250000 / 1000000 | `50000` |
| `randomize_gordos` | `Toggle` | on / off | on |
| `randomize_pods` | `Toggle` | on / off | on |
| `randomize_map_nodes` | `Toggle` | on / off | off |
| `randomize_slimepedia` | `Toggle` | on / off | off |
| `randomize_research_drones` | `Toggle` | on / off | off |
| `randomize_ghostly_drones` | `Toggle` | on / off | off |
| `zone_teleporter_mode` | `Choice` | `item` / `bundled` / `auto` | `item` |
| `conversation_checks` | `Choice` | `none` / `key_items` / `all_gifts` / `all` | `none` |
| `trap_link` | `Toggle` | on / off | off |
| `death_link` | `Toggle` | on / off | off |

**Notes:**
- `randomize_slimepedia` is forced `true` when `goal = slimepedia_slimes`
- `newbucks_goal_amount` uses `Choice` (preset values) rather than `Range` for friendlier YAML generator display
- `randomize_map_nodes` defaults off — nodes are low-difficulty collectibles that add padding without much interest
- `randomize_research_drones` and `randomize_ghostly_drones` default off — optional depth for veteran players

---

## Item Classifications

### Progression (sphere-gating — placed early, required to reach other locations)
- Ember Valley Access
- Starlight Strand Access
- Powderfall Bluffs Access
- Radiant Projector Blueprint *(when `conversation_checks >= key_items`)*

### Useful (never filler-replaced; meaningful quality-of-life or unlocks)
- All 15 Progressive Vacpack Upgrades (all tiers)
- Zone Teleporter blueprints: TeleporterGrey, TeleporterPink, TeleporterBlue, TeleporterViolet *(when `zone_teleporter_mode = item` and `conversation_checks >= key_items`)*
- Home Teleporter blueprints: TeleporterHomeBlue, TeleporterHomeGreen, TeleporterHomeRed *(when `conversation_checks >= key_items`)*
- Market Link, Super Hydro Turret, Portable Scare Slime, Gordo Snare Advanced *(when `conversation_checks >= key_items`)*
- Archive Key Component *(when `conversation_checks >= key_items`)*
- Warp Depots (all variants)

### Filler (used to fill remaining location slots)
- 250 / 500 / 1000 Newbucks
- Common / Uncommon / Rare Plort Cache
- Common / Rare Craft Cache
- Med Station, Dream Lantern T2 *(minor gadgets with no meaningful progression impact)*

### Trap *(when `trap_link = true`; replaces a portion of filler)*
- Slime Rain, Tarr Spawn, Teleport, Weather Change

---

## Logic Rules

### Region Access
```
Rainbow Fields:    always accessible
Ember Valley:      requires Ember Valley Access
Starlight Strand:  requires Starlight Strand Access
Powderfall Bluffs: requires Powderfall Bluffs Access
Grey Labyrinth:    requires Ember Valley Access + Starlight Strand Access + Radiant Projector Blueprint
Prismacore:        requires Grey Labyrinth access + Shadow Plort Door traversal (exact count TBD)
```

### Location Access by Region
All locations in a region inherit that region's access rule. Exceptions:

```
Grey Labyrinth Gordos (Goals 3–4 only):  Grey Labyrinth access
Grey Labyrinth Ghostly Drones (3 total): Grey Labyrinth access
Shadow Plort Doors:                      Grey Labyrinth access (Goals 3–4 only)
```

### Gordo Access
```
Rainbow Fields Gordos (3):  always accessible
Ember Valley Gordos (5):    Ember Valley Access
Starlight Strand Gordos (5): Starlight Strand Access
Powderfall Bluffs Gordo (1): Powderfall Bluffs Access
Grey Labyrinth Gordos (3):  Grey Labyrinth access (Goals 3–4 only)
```

### Slimepedia Entry Access (when `randomize_slimepedia = true`)
```
Always accessible (Rainbow Fields or weather anywhere):
  Pink, Cotton, Tabby, Phosphor, Boom, Rock, Honey, Puddle,
  Lucky, Gold, Dervish (Wind Storm L3), Tangle (Pollen Storm L3),
  Largo, Tarr, FeralSlime, Gordo

Ember Valley Access:
  Crystal, Hunter, Fire, Batty

Ember Valley Access OR Starlight Strand Access:
  Angler, Ringtail

Starlight Strand Access:
  Flutter

Powderfall Bluffs Access:
  Yolky, Sloomber, Twin, Hyper, Saber

Grey Labyrinth Access:
  Shadow
```

### Fabricator Craft Access
All crafts require the Fabricator to be accessible (always, it's in the Conservatory).
Individual tier logic based on ingredient zone requirements:

```
Tier 1 of all upgrades:          always (Rainbow Fields plorts / basic crafts)
Tiers requiring Hunter Plort:    Ember Valley Access  (Tank Guard all tiers)
Tiers requiring Saber Plort:     Powderfall Bluffs Access  (GoldenSureshot T2–3, TankBooster T4)
Tiers requiring Sloomber/Twin/Hyper Plort: Powderfall Bluffs Access  (TankBooster T5–8, PowerInjector, Regenerator)
Tiers requiring Shadow Plort:    Grey Labyrinth Access  (ShadowSureshot T1)
Tiers requiring Prisma Plort:    Prismacore access — out of logic for Goals 1–2
                                 (HealthCapacity T4, EnergyCapacity T5, TankBooster T5+)
Gold Plort requirement:          always (Lucky/Gold spawn anywhere)
ArchiveKey craft:                requires ArchiveKeyComponent in inventory
```

> **Note**: Prisma Plort tiers are not required for any goal completion and should be marked out-of-logic for safety — players can craft them opportunistically if they reach Prismacore.

### Conversation Location Access (when `conversation_checks >= key_items`)

#### Rancher Intro Call Chain (confirmed via DumpConversationConditions, 2026-04-10)

All rancher contacts are time-gated from each other. AP logic treats these as "always accessible given zone access" since real-world time passes regardless of AP state; the only hard AP prerequisites are zone access items.

```
ViktorIntroCall:  ANY_OF(any ResearchDrone activated, OR 2 in-game days after Conservatory visit)
                  → Conservatory = Ranch home area; fires within first 2 days of any run. Always accessible.

MochiIntroCall:   1 in-game day after ViktorIntroCall
OgdenIntroCall:   2 in-game days after MochiIntroCall
ThoraIntroCall:   3 in-game days after OgdenIntroCall

BObGift1Intro:    ALL_OF(2+ days after Gorge/Ember Valley visited,
                         2+ days after Strand/Starlight Strand visited,
                         1+ day after ThoraIntroCall)
                  → Requires Ember Valley AND Starlight Strand access items in AP logic.
```

**AP Logic consequence**: Viktor/Mochi/Ogden/Thora conversations are in logic from the start (time-only gating). BOb conversations require `has(EmberValleyAccess) AND has(StarlingStrandAccess)`.

#### Individual Gift Conversation Conditions (confirmed)

Gift conversations for each rancher have NO additional `CompositeQueryComponent` conditions beyond the intro call chain above (they were absent from the conditions dump, meaning `conditions._children` is empty). The game's native conversation sequencing within each rancher's tree (e.g. Viktor gift 1 before gift 2) is handled by conversation ordering flags not conditions, and plays out automatically with time. For AP logic, treat each rancher's gifts as sequentially ordered but time-gated, not item-gated:

```
Viktor gifts:     accessible after ViktorIntroCall (always = start of run)
                  Gift order: GadgetIntro → TeleporterPink → TeleporterBlue → TeleporterGrey →
                              TeleporterViolet → TeleporterHome1 → TeleporterHome2 → TeleporterHome3
                  Each fires on gordo milestones / relationship tiers — time/progress, not AP items.

ViktorStoryCipher4: ANY_OF(Flutter Gordo fed [gordo0589091050, Starlight Strand Area4],
                           OR Boom Gordo fed [gordo0091013817, Ember Valley Area4])
                    AP logic: has(StarlingStrandAccess) OR has(EmberValleyAccess)
                    In practice always reachable before late-game since both zones are early access.

ThoraGift2:       after ThoraGift1 (sequential, time-gated, no AP items needed)
OgdenGift1:       after OgdenIntroCall (no AP items needed)
OgdenGift3:       after OgdenGift2 (sequential)
MochiGift1:       after MochiIntroCall (no AP items needed)

MochiStoryDrones3: ALL_OF(MochiStoryDrones2 completed,
                           ResearchDrone 'ResearchDroneStrandLabyrinthGate' activated,
                           ResearchDrone 'ResearchDroneGorgeLabyrinthGate' activated)
                   → Requires Starlight Strand AND Ember Valley access in AP logic.
                   → Archive Key Component (gifted here) is AP item when conv_checks >= key_items.
```

> **Note on GiftRelocated conversations**: `BObGiftRelocated1–4`, `MochiGiftRelocated1–4`, `OgdenGiftRelocated1/3/4`, `ThoraGiftRelocated1–4` all have `NONE_OF(GadgetEventQueryComponent)` conditions — they are vanilla alternates that fire when the decorative gadget has already been placed elsewhere. These are NOT location checks; they duplicate the gift to a different slot when the original is already claimed.

### Zone Teleporter Mode Interaction
```
zone_teleporter_mode = "item":
  Region gate blocked until Region Access item received.
  Zone teleporter blueprint is a separate AP item (Useful).
  When conversation_checks >= key_items: Viktor's teleporter convs are locations;
    teleporter blueprints come from AP item pool.
  When conversation_checks = none: Viktor gives blueprints directly;
    teleporter blueprints NOT in AP item pool.

zone_teleporter_mode = "bundled":
  Region gate blocked until Region Access item received.
  Zone teleporter blueprint auto-granted alongside Region Access item.
  Teleporter NOT a separate AP item regardless of conversation_checks.

zone_teleporter_mode = "auto":
  Region gate NOT blocked — switch press works immediately (vanilla).
  Switch press IS a location check (sent when player first opens the gate).
  Zone teleporter blueprint auto-granted when gate opens.
  RegionGatePatch sends check instead of blocking.
```

### Goal-Specific Logic
```
labyrinth_open:
  Win = both Labyrinth entrances opened via Radiant Projector puzzles.
  Required: Ember Valley Access + Starlight Strand Access + Radiant Projector Blueprint.
  Shadow Plort Doors: NOT in pool.

newbucks:
  Win = lifetime Newbucks >= newbucks_goal_amount.
  Required: any region access sufficient to farm plorts.
  Shadow Plort Doors, Grey Labyrinth content: NOT in pool.

prismacore_enter:
  Win = player first enters Prismacore area.
  Required: Grey Labyrinth access + sufficient Shadow Plort Door progress to reach entrance.
  Shadow Plort Doors: IN pool.

prismacore_stabilize:
  Win = Prismacore stabilization sequence complete.
  Required: prismacore_enter requirements + boss fight completion.
  Shadow Plort Doors: IN pool.

slimepedia_slimes:
  Win = all 29 Slimes pedia entries unlocked.
  Required: all region accesses (for Crystal/Hunter/Fire/Batty/Flutter/Yolky etc.)
            + Grey Labyrinth access (for Shadow Slime).
  Forces randomize_slimepedia = true.
  Shadow Plort Doors: NOT in pool (Labyrinth access needed for Shadow Slime, but not door traversal).
```

---

## apworld File Structure

```
slime_rancher_2/
├── __init__.py          ← World class: generate_early, create_items, create_regions,
│                          set_rules, generate_output, fill_slot_data
├── options.py           ← All YAML option class definitions (Goal, ZoneTeleporterMode, etc.)
├── items.py             ← Item table (id, name, classification), create_item helper
├── locations.py         ← Location table mirroring mod's LocationTable
│                          (id, name, region, access conditions)
├── regions.py           ← Region definitions and connection rules
└── rules.py             ← Access rule helper functions (has_region_access, can_reach_labyrinth, etc.)
```

### Key implementation notes

**`generate_early`**: Read options, determine which location categories are active (based on goal + toggles), force `randomize_slimepedia = True` when `goal = slimepedia_slimes`.

**`create_items`**: Build item pool from active categories. Region Access items always included (3 items). Radiant Projector Blueprint always included (1 item). Crafting Components always included (one copy per consuming craft tier — 26 total across 11 component types). Zone teleporter blueprints included only when `zone_teleporter_mode = item`. Gadgets gifted by NPC conversations only included when `conversation_checks >= key_items`. Fill remaining slots with Filler/Trap items to match location count exactly.

**`create_regions`**: One region per zone (Menu, Conservatory, RainbowFields, EmberValley, StarlightStrand, PowderfallBluffs, GreyLabyrinth, Prismacore). Connect with access rules from the Logic Rules section. When `zone_teleporter_mode = auto`, the region gate connection sends a check on first traversal instead of being blocked.

**`set_rules`**: Apply `lambda state:` access rules to each location using the helper functions from `rules.py`. Slimepedia entries get per-slime region rules. Fabricator tiers get ingredient-derived region rules. Conversation locations get prerequisite chain rules.

**`fill_slot_data`**: Return all option values as the slot data dict sent to the mod on connect.

---

## Open Questions

### Resolved ✅
- **Drone class names** — Both Research Drone (`ResearchDroneActivator.OnInteract`) and Ghostly Drone (`ComponentAcquisitionDroneUIInteractable.OnInteract`) confirmed via ILSpy ✅
- **Shadow Plort Door class** — `PlortDepositor.ActivateOnFill()` confirmed; posKey required since all doors share `gameObject.name = "TriggerActivate"` ✅
- **Fabricator craft deduplication** — stateful counter per upgrade name, compared against completed location IDs ✅
- **Exact gadget asset names** — confirmed from 357-gadget runtime dump (no color-based names; zone-purpose names instead) ✅
- **Conversation gift structure** — full `DumpConversations()` run confirmed all rancher conversation trees, gift types, and debug names ✅
- **Grey Labyrinth Gordos** — Sloomber + one other; confirmed gameObject names from dump ✅
- **Slimepedia entry asset names** — all 29 `"Slimes"` category `PediaEntry.name` values confirmed via `DumpPedia()` (2026-04-10); LocationTable fully populated ✅
- **Conversation gift suppression** — ✅ Implemented: `ConversationActiveTrackerPatch` hooks `ConversationViewHolder.ShowConversation()` to track active conversation; all three gift page `ApplyChanges()` methods have Prefix patches returning false when the conversation is an active AP location ✅
- **Full Vacpack upgrade list** — all 15 upgrades, 38 tiers confirmed from game dump + wiki (2026-04-10); IDs 819515–819529 assigned ✅
- **zone_teleporter_mode design** — 3-way choice retained: `item` (teleporters are separate AP items), `bundled` (teleporter auto-granted with region access item), `auto` (switch press is a location check, teleporter auto-granted when gate opens). Interaction with `conversation_checks` is clean: Viktor's conversations are checked/suppressed independently of how the teleporter itself arrives ✅
- **YAML options schema** — all options defined with types, ranges, and defaults in Slot Data Keys section ✅
- **Newbucks threshold** — `Choice` with presets: 10k / 25k / 50k / 100k / 250k / 1M; default 50k ✅
- **Slime zone distribution** — all 29 slimes mapped to region requirements (2026-04-10); Dervish and Tangle are weather-dependent but accessible from any non-Labyrinth region so effectively always in logic; Shadow Slime is the only progression-gated slimepedia entry ✅
- **Item classifications** — all items classified as Progression / Useful / Filler / Trap ✅
- **Complete logic rules** — region access, gordo access, slimepedia entry access, fabricator tier access, conversation prerequisites, zone teleporter mode interaction, and per-goal rules all documented ✅
- **Conversation unlock conditions** — full `CompositeQueryComponent` condition trees confirmed via `DumpConversationConditions()` (2026-04-10): rancher intro chain is strictly sequential (Viktor→Mochi→Ogden→Thora→BOb), all time-gated; BOb requires Gorge+Strand access; MochiStoryDrones3 requires Strand+Gorge drones + prior story beat; ViktorStoryCipher4 triggers on specific gordo-fed events (not conversation chain); GiftRelocated conversations are vanilla decoration alternates, not AP checks ✅

### Still Open
1. **Prismacore stabilization requirements** — what exactly does the game require to complete the main story? Needs playthrough/wiki verification.
2. **Valley Labyrinth gate detection** — `PuzzleDoorLock` and `PuzzleGateActivator` patches written but not firing; the Ember Valley labyrinth puzzle entrance likely uses a different class. Strand gate confirmed as `WorldStatePrimarySwitch` named `ruinSwitch` in `zoneStrand_Area4` (already in GoalHandler as `LabyrinthSwitchStrand`) ✅ — Valley switch name still TODO.
3. **Shadow Plort Door count to reach Prismacore** — how many of the 25 doors must be opened to traverse the Labyrinth to the Prismacore entrance? Needed to set the logic requirement for `prismacore_enter` / `prismacore_stabilize`.
4. **Plort market items and traps** — see "Plort Market" section below (IDs 819630–819649, option `randomize_market`; also Progressive Plort Market item for `newbucks` goal synergy, option `shuffle_plort_prices`).

### Resolved ✅
*(In addition to the list above)*
- **ViktorStoryCipher4 gordo identities** — `gordo0091013817` = **Boom Gordo** (Ember Valley, zoneGorge_Area4); `gordo0589091050` = **Flutter Gordo** (Starlight Strand, zoneStrand_Area4). AP logic: `has(StarlingStrandAccess) OR has(EmberValleyAccess)` ✅

---

## Plort Market Items and Traps

**Status**: Designed — not yet implemented. Gated behind apworld option `randomize_market` (toggle, default off).

### How the market works (confirmed via ILSpy, 2026-04-12)

- **`PlortEconomyDirector`** (`SRBehaviour`, accessible at `SceneContext.Instance.PlortEconomyDirector`):
  - `TryGetMarketValues(IdentifiableType id, out int currentValue, out int delta)` — current price and direction (delta drives the up/down/neutral UI arrows)
  - `TrySell(IdentifiableType id, int count, bool ignoreMarketShutdown, ...)` — main sell path; rejects all sales when `IsMarketShutdown()` is true
  - `RegisterSold(IdentifiableType id, int count, int price)` — called by `TrySell` to increment per-plort saturation
  - `GetTargetValue(WorldModel, IdentifiableType, float baseValue, float fullSaturation, float day)` — pricing formula; price decays as saturation grows and recovers over time
  - `ResetPrices(WorldModel, int day)` — snaps all plort prices back to day-start state
  - `IsMarketShutdown()` — market goes offline once per in-game day for `DailyShutdownMins` minutes

- **`PlortEconomySettings`** (ScriptableObject holding market config):
  - `PlortsTable` (`PlortValueConfigurationTable`) — per-plort base price and full-saturation volume
  - `SaturationRecovery` (float) — rate at which selling pressure decays back toward base price
  - `DailyShutdownMins` (float) — minutes per in-game day the market is closed
  - `MarketNoiseAmplitude` (float) — global random price jitter applied each day
  - `IndivNoiseAmplitude` (float) — per-plort random jitter

- **`PlortValueConfiguration`** (per-plort struct): `Type` (IdentifiableType), `InitialValue` (base price), `FullSaturation` (sell volume that floors the price)

- **`CurrValueEntry`** (inner class of `PlortEconomyDirector`): tracks `baseValue`, `currValue`, `prevValue`, `fullSaturation` for each live plort type

### Item IDs (819630–819639, when `randomize_market: true`)

| ID | Name | Classification | Effect | Implementation |
|---|---|---|---|---|
| 819630 | Market Insight | Filler | Shows the highest-priced plort right now as a HUD notification | Read `TryGetMarketValues` across all plort types; no state change |
| 819631 | Market Boom | Useful | +50% payout on all plort sales for 3 in-game days | Patch `TryGetMarketValues` to multiply `currentValue` × 1.5 while active; timer via `TrapHandler`-style countdown |
| 819632 | Saturation Reset | Useful | Zero out saturation on all plorts; prices snap back to base immediately | Call `ResetPrices(worldModel, currentDay)` on `PlortEconomyDirector` |
| 819633 | Progressive Plort Market | Useful/Progression | Permanently increases all plort prices by one tier (see below) | — |

### Progressive Plort Market (for consideration — `newbucks` goal synergy)

**Concept**: A second apworld option, `shuffle_plort_prices` (toggle, default off; only meaningful when `goal = newbucks`), that makes the newbucks grind feel like a proper randomizer location sweep rather than passive farming.

**Behaviour**:
- When `shuffle_plort_prices: true`, all plort prices start at **10% of their normal value** when the session begins (the floor is always available; the player can still sell, just very slowly).
- Scattered through the location pool are **N copies** of the "Progressive Plort Market" item (AP item ID 819833). Each copy received permanently raises all plort prices by one tier.
- With 9 copies in the pool, prices climb in steps: 10% → 20% → 30% → … → 100%. Finding all 9 restores vanilla pricing.
- The player can still reach the newbucks goal at partial tiers — it just takes longer per tier, so finding more items is a meaningful speed-up rather than purely required.
- Compatible with Market Boom trap/item: multipliers compose as `currentValue × priceTier × boomMultiplier`.

**Slot data keys** (both required together):
- `shuffle_plort_prices` (bool) — whether to apply the price floor and put Progressive Plort Market items in the pool
- `market_price_tiers` (int, 1–9, default 9) — how many Progressive Plort Market items are in the pool; controls granularity of the ramp

**ID assignment**: 819833 is one item ID used N times (progressive pattern, same as Vacpack Upgrades). Reserved in the 819630–819649 block — update the item table entry for 819633 to this ID once finalised.

**Mod implementation**:
- `ApSaveManager` gains a persisted `MarketPriceTier` counter (int, 0–9), initialized to 0 when `shuffle_plort_prices` is active, loaded from save otherwise.
- `ItemHandler` increments `MarketPriceTier` on each Progressive Plort Market receipt (capped at `market_price_tiers`).
- The `TryGetMarketValues` Postfix patch (already needed for Market Boom) multiplies `currentValue` by `(MarketPriceTier + 1) / 10f` when `shuffle_plort_prices` is active. At tier 9 this is 1.0 — full vanilla prices.
- On session start with `shuffle_plort_prices` active, the multiplier starts at 0.1 automatically (tier = 0 → `(0 + 1) / 10 = 0.1`).
- **Stacking formula**: `finalValue = rawCurrentValue × priceTierMultiplier × boomMultiplier` (both default to 1.0 when not active).

**Logic rules (apworld)**: Progressive Plort Market items have no region access requirements. When `goal = newbucks` and `shuffle_plort_prices: true`, the newbucks threshold should be reachable at partial tier (e.g., tier 5/9) within normal play time — the YAML validator should warn if `newbucks_goal_amount` is set very high alongside `shuffle_plort_prices: true` and low `market_price_tiers`, as that combination could be extremely grindy.

### Trap IDs (819640–819649, when `randomize_market: true`)

| ID | Name | Effect | Duration | Implementation |
|---|---|---|---|---|
| 819640 | Market Crash | All plort prices floor to 1 newbuck | 1 in-game day | Patch `TryGetMarketValues` to return `currentValue = 1` while active |
| 819641 | Market Shutdown | Market rejects all sales | 5 real-time minutes | Patch `IsMarketShutdown()` to return `true` while active |
| 819642 | Oversaturation | One random plort's saturation jumps to its `FullSaturation` threshold; its price floors immediately | Permanent until saturation recovers naturally | Write max value into `CurrValueEntry.fullSaturation` for the chosen plort; identify target plort by randomizing over `PlortValueConfigurationTable` entries |

### Mod implementation plan

**New files:**
- `Archipelago/MarketHandler.cs` — manages active market effects (boom multiplier, crash floor, shutdown override); exposes `Tick()` for countdown, `ApplyMarketBoom()`, `ApplyMarketCrash()`, `ApplyMarketShutdown()`, `ApplySaturationReset()`, `ApplyOversaturation()`

**Patches needed:**
- Harmony Postfix on `PlortEconomyDirector.TryGetMarketValues` — apply boom multiplier or crash floor when active
- Harmony Prefix on `PlortEconomyDirector.IsMarketShutdown` returning `true` when shutdown trap is active (return false to original `__result` otherwise)

**`TrapHandler.cs` changes:** add market trap cases to `TrapHandler.Schedule()` and `TrapHandler.Tick()`

**apworld `SlotData` key:** `randomize_market` (bool) — read in `SlotData.Parse()`, stored as `SlotData.RandomizeMarket`

**`ItemHandler.Apply()` cases:** `ItemType.MarketItem` and `ItemType.MarketTrap` — dispatch to `MarketHandler`

**Logic rules (apworld side):** market items/traps have no region requirements; they are always accessible once the game loads. They are not progression items.

### Open questions
- What is the in-game unit of "day" for the 3-day boom duration? Need to confirm if `WorldModel` exposes current day number or if we measure elapsed in-game time.
- Does `ResetPrices` fire the `DidUpdate` event and refresh the market UI automatically, or do we need to manually trigger a UI refresh?
- Can we directly write to `CurrValueEntry.fullSaturation`? The field is on an `Il2CppSystem.Object` inner class — may need pointer arithmetic if there's no setter.
