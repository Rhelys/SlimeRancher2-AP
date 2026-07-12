# Polestar Provisions — Shop Location Logic

Sources of truth (both in `docs/dumps/`):
- `shop_condition.txt` — static rule-set/table dump (`ShopCategorySourceRuleSet` group
  conditions + per-item `AvailableCondition`), 2026-07-11.
- `shop.txt` — runtime item dump from an all-zones-visited save (resolves names), 2026-07-11.

The wiki's "new items are added for purchase whenever a new location is visited for the
first time" is implemented through these conditions.

## How availability works in-game

The Polestar Provisions category (`RangeExchange`) is assembled from six rule-set groups.
A group's items only appear in the shop when the group condition is met; individual items
can additionally carry their own condition.

| Rule set | Condition | Items |
|---|---|---|
| `RangeExchange_BaseRuleSet` | none — available from game start | 94 |
| `RangeExchange_ValleyUnlocksRuleSet` | visited Ember Valley | 14 |
| `RangeExchange_StrandUnlocksRuleSet` | visited Starlight Strand | 7 |
| `RangeExchange_PowderfallUnlocksRuleSet` | visited Powderfall Bluffs | 1 |
| `RangeExchange_LabyUnlocksRuleSet` | visited ANY Grey Labyrinth zone (4 scenes) | 43 |
| `RangeExchange_DroneRuleSet` | gadget event (Drone Station related) | 2 |

## AP location pool (149 locations, IDs 819926–820074)

| Block | IDs | Region / logic |
|---|---|---|
| Base items (84) | 819926–820009 | `Conservatory` — on sale from game start, sphere-1 accessible |
| Ember Valley items (14) | 820010–820023 | `EmberValley` — appear after visiting Ember Valley |
| Starlight Strand items (7) | 820024–820030 | `StarlightStrand` |
| Powderfall Bluffs item (1) | 820031 | `PowderfallBluffs` |
| Grey Labyrinth items (43) | 820032–820074 | `GreyLabyrinth` — **never selected when the goal is `labyrinth_open`** (`_gl_included()`), matching every other GL location pool |

Special cases:

- **`Polestar Provisions: Golden Yolky Statue` (10,000nb, base set)** carries an
  `IdentifiableEventQueryComponent` condition (a Yolky discovery event — RNG-gated).
  It is listed in `_RNG_SLIMES_EXCLUDED` and never selected as a shop check when
  `exclude_rng_slimes` is enabled, matching "Plort Market: Yolky Plort".
- **Functional items in the Grey Labyrinth group** (Shadow Sureshot Module 5,000nb,
  Master Gordo Snare 1,200nb, Gold Slime Floor Panel 500nb): when selected as a check,
  the vanilla grant is suppressed like any other shop check. When NOT selected, they
  remain purchasable as a vanilla second source for content AP items also grant —
  an accepted softening of gating for players who have already reached the Labyrinth
  (decision 2026-07-11).

Selection (apworld `generate_early`): count rolled between `shop_check_count_min/max`
(range 1–149), pick spread across price tiers cheapest-first, after filtering the
eligible pool by `_gl_included()` and `exclude_rng_slimes`. Counts above the eligible
pool size are clamped by exhaustion.

## Zone-gated locations

### Ember Valley (14 locations, region `EmberValley`)

Shop condition: visited Ember Valley (`RangeExchange_ValleyUnlocksRuleSet`).

| Location | ID | Cost |
|---|---|---|
| Striped Beach Blanket | 820010 | 80nb |
| Sakura Umbrella | 820011 | 150nb |
| Small Trellis | 820012 | 80nb |
| Wide Emerald Trellis | 820013 | 150nb |
| Tarr Standee | 820014 | 300nb |
| Sunflower Umbrella | 820015 | 150nb |
| Moonflower Umbrella | 820016 | 150nb |
| Peach Sunflower Umbrella | 820017 | 150nb |
| Gold Accelerator | 820018 | 50nb |
| Green Accelerator | 820019 | 50nb |
| Grey Accelerator | 820020 | 50nb |
| Pink Accelerator | 820021 | 50nb |
| Purple Accelerator | 820022 | 50nb |
| Red Accelerator | 820023 | 50nb |

### Starlight Strand (7 locations, region `StarlightStrand`)

Shop condition: visited Starlight Strand (`RangeExchange_StrandUnlocksRuleSet`).

| Location | ID | Cost |
|---|---|---|
| Small Sandcastle | 820024 | 300nb |
| Large Sandcastle | 820025 | 300nb |
| Clam Throne | 820026 | 300nb |
| Wavey Beach Blanket | 820027 | 80nb |
| Medium Net | 820028 | 300nb |
| Small Net | 820029 | 200nb |
| Large Net | 820030 | 500nb |

### Powderfall Bluffs (1 locations, region `PowderfallBluffs`)

Shop condition: visited Powderfall Bluffs (`RangeExchange_PowderfallUnlocksRuleSet`).

| Location | ID | Cost |
|---|---|---|
| Snowman Standee | 820031 | 300nb |

### Grey Labyrinth (43 locations, region `GreyLabyrinth`)

Shop condition: visited ANY Grey Labyrinth zone (`RangeExchange_LabyUnlocksRuleSet`).

| Location | ID | Cost |
|---|---|---|
| Ancient Round Pillar | 820032 | 80nb |
| Excavation Lights | 820033 | 80nb |
| Ancient Arched Wall | 820034 | 80nb |
| Gold Slime Floor Panel | 820035 | 500nb |
| Shadow Sureshot Module | 820036 | 5,000nb |
| Wooden Fence | 820037 | 100nb |
| Straight Stone Fence | 820038 | 160nb |
| Curved Stone Fence | 820039 | 160nb |
| Giant Stalks | 820040 | 160nb |
| Overgrown Lilypad | 820041 | 160nb |
| Azure Water Flower | 820042 | 160nb |
| Pink Floral Slime Stage | 820043 | 750nb |
| Blue Floral Slime Tree | 820044 | 800nb |
| Small Pink Flower Pillow | 820045 | 150nb |
| Large Blue Flower Pillow | 820046 | 200nb |
| Blue Flower Lamp | 820047 | 800nb |
| Gold Linked Cannon | 820048 | 500nb |
| Violet Linked Cannon | 820049 | 500nb |
| Master Gordo Snare | 820050 | 1,200nb |
| Orange Flower Lamp | 820051 | 800nb |
| Pink Flower Lamp | 820052 | 800nb |
| Purple Flower Lamp | 820053 | 800nb |
| Large Coral Flower Pillow | 820054 | 200nb |
| Large Purple Flower Pillow | 820055 | 200nb |
| Large Yellow Flower Pillow | 820056 | 200nb |
| Small Blue Flower Pillow | 820057 | 150nb |
| Small Purple Flower Pillow | 820058 | 150nb |
| Small Yellow Flower Pillow | 820059 | 150nb |
| Coral Floral Slime Tree | 820060 | 800nb |
| Purple Floral Slime Tree | 820061 | 800nb |
| Yellow Floral Slime Tree | 820062 | 800nb |
| Blue Floral Slime Stage | 820063 | 750nb |
| Purple Floral Slime Stage | 820064 | 750nb |
| Yellow Floral Slime Stage | 820065 | 750nb |
| Indigo Cypress | 820066 | 40nb |
| Indigo Cypress Cluster | 820067 | 50nb |
| Indigo Flowers | 820068 | 25nb |
| Indigo Grass | 820069 | 25nb |
| Indigo Shrubs | 820070 | 40nb |
| Labyrinth Standing Lamp | 820071 | 150nb |
| Labyrinth Wall Lamp | 820072 | 150nb |
| Tall Indigo Cypress | 820073 | 40nb |
| Shadow Lava Lamp | 820074 | 500nb |

## NOT in the AP pool

These shop entries remain vanilla-only:

| Item | Cost | Why excluded |
|---|---|---|
| Red Teleporter | 2,500nb | base set, per-item condition: TWO zone visits (pair not identifiable from data) |
| Green Teleporter | 2,500nb | same |
| Amber Teleporter | 3,000nb | same |
| Berry Teleporter | 3,000nb | same |
| Resource Detector | 1,000nb | same |
| 5 unresolved GUIDs (300–4,000nb) | — | base set, gated on identifiable discovery events that stayed unmet even on an all-zones save |
| Drone rule set item (`57b15317…`) | 20,000 / 100,000nb | gadget-event gated, unresolved |
