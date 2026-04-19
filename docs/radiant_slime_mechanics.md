# Radiant Slime Mechanics — Research Document

> **Audit trail**: All findings sourced from decompiled IL2CPP interop DLLs via `ilspycmd` against
> `E:/SteamLibrary/steamapps/common/Slime Rancher 2/BepInEx/interop/Assembly-CSharp.dll`
> and from mod source files in this repository. Source classes are cited inline.

---

## Table of Contents

1. [Overview](#1-overview)
2. [Shuffle Bag Algorithm](#2-shuffle-bag-algorithm)
3. [Spawn Gate — `_allowRadiantSlimesToSpawnQuery`](#3-spawn-gate)
4. [Zone Scoping — `DrawFromRadiantShuffleBag`](#4-zone-scoping)
5. [Sanctuary Boost Scalar](#5-sanctuary-boost-scalar)
6. [Spawn Source Classification (Analytics)](#6-spawn-source-classification)
7. [Radiant Eligibility — `CanSlimeBeRadiant`](#7-radiant-eligibility)
8. [Lost Radiant Tracking](#8-lost-radiant-tracking)
9. [Bag Resizing Lifecycle](#9-bag-resizing-lifecycle)
10. [Confirmed Radiant Slime Types (22)](#10-confirmed-radiant-slime-types)
11. [AP Mod Integration](#11-ap-mod-integration)
12. [Debug Fields](#12-debug-fields)
13. [Known Unknowns](#13-known-unknowns)

---

## 1. Overview

Radiant Slimes are rare variants of normal slimes that yield a unique Slimepedia entry when
collected. The game controls their spawn probability through a **shuffle bag** system — a
draw-based RNG that guarantees a radiant spawn within at most `BagSize` draws, then resets.

**Primary source classes:**
- `MonomiPark.SlimeRancher.Slime.RadiantSlimeConfig` — configuration asset (ScriptableObject)
- `MonomiPark.SlimeRancher.Slime.RadiantSlimeDirector` — runtime manager (MonoBehaviour)
- `MonomiPark.SlimeRancher.Slime.RadiantSlimesModel` — save model
- `MonomiPark.SlimeRancher.Slime.RadiantSlimeDirectorConfiguration` — pedia/event wiring

---

## 2. Shuffle Bag Algorithm

**Source**: `MonomiPark.SlimeRancher.Slime.RadiantSlimeConfig` (Assembly-CSharp.dll)

### Configuration Structure

```
RadiantSlimeConfig
├── _radiantShuffleBagSizes : Il2CppReferenceArray<RadiantShuffleBagConfigEntry>
│     // One entry per eligible slime type
│     RadiantShuffleBagConfigEntry
│     ├── Slime    : SlimeDefinition   // which slime this bag covers
│     └── BagSize  : int               // total draws before guaranteed spawn
└── _sanctuaryUnlockedRadiantBagSizeScalar : float
```

### Draw Mechanics

Every time a slime of an eligible type spawns in the world, the game calls:

```
RadiantSlimeDirector.DrawFromRadiantShuffleBag(IdentifiableType, SceneGroup) : bool
```

Internally this draws from the runtime `RadiantShuffleBag` for that slime+zone combination.
The persisted save state for each bag (`RadiantShuffleBagV01`) contains exactly three fields:

```
RadiantShuffleBagV01
├── CurrentIndex      : int   — how many draws have been made in the current cycle
├── RadiantSpawnIndex : int   — the pre-seeded draw number that will produce a radiant
└── Size              : int   — effective bag size (config BagSize after scalar applied)
```

**Source**: `MonomiPark.SlimeRancher.Persist.RadiantShuffleBagV01` (Assembly-CSharp.dll)

Each draw increments `CurrentIndex`. The spawn is radiant when `CurrentIndex == RadiantSpawnIndex`.
When the bag resets (after a radiant spawns), a **new `RadiantSpawnIndex` is randomly chosen**
within `[0, Size - 1]` for the next cycle.

This means:
- The radiant can appear as early as draw **1** (if `RadiantSpawnIndex = 0`) or as late as
  draw **Size** (if `RadiantSpawnIndex = Size - 1`), but is guaranteed to appear **exactly
  once** per cycle.
- The position is fixed at the start of each cycle — there is no per-draw probability roll.
  The game effectively pre-places the "golden marble" at a random slot, then draws through
  the bag in order.
- Two radiants in a row are impossible (a new bag resets after each radiant).
- Going more than `Size` draws without a radiant is impossible.

**Source**: `RadiantSlimeDirector` method signatures and analytics enum (see §6).

### Persistence

**Bag state fully persists across sessions.** `RadiantShuffleBagV01` extends `PersistedDataSet`
and implements `LoadData(BinaryReader)` / `WriteData(BinaryWriter)` — it is written into the
SR2 save file on every session exit and restored on load. Both `CurrentIndex` (progress through
the current cycle) and `RadiantSpawnIndex` (which draw is the radiant this cycle) survive
closing and reopening the game.

**Source**: `MonomiPark.SlimeRancher.Persist.RadiantShuffleBagV01` (Assembly-CSharp.dll)

### Confirmed Vanilla Bag Sizes

**Source**: `DumpRadiantSlimes()` runtime output (`Utils/LocationDumper.cs`)

Bag sizes vary significantly between species, reflecting their rarity and zone availability.
Smaller bags mean more frequent radiant spawns for that species.

| Slime | Vanilla BagSize | Notes |
|---|---|---|
| Yolky | 25 | Most frequent radiant; very small bag |
| Dervish | 50 | |
| Tangle | 50 | |
| Fire | 75 | Limited spawn zones |
| Puddle | 75 | Rare slime; limited spawn zones |
| Ringtail | 150 | Nocturnal; limited spawn windows |
| Flutter | 1000 | |
| Hunter | 1000 | |
| Hyper | 1000 | |
| Saber | 1000 | |
| Sloomber | 1000 | |
| Angler | 1500 | |
| Boom | 1500 | |
| Honey | 1500 | |
| Tabby | 1500 | |
| Twin | 1500 | |
| Batty | 2000 | Rarest radiant tier |
| Cotton | 2000 | |
| Crystal | 2000 | |
| Phosphor | 2000 | |
| Pink | 2000 | |
| Rock | 2000 | |

With the sanctuary scalar of `0.95` applied, these values floor to approximately 95% of the
above (e.g. 2000 → 1900, 25 → 23). With the AP `radiant_spawn_rate_multiplier` at `N`, each
bag is additionally divided by `N` (floored at 1).

### AP Mod Modification

The AP mod divides all `BagSize` values by the `radiant_spawn_rate_multiplier` slot option (range 1–10):

```csharp
// Source: Patches/LocationPatches/RadiantSlimePatch.cs
// RadiantSlimeDirector.Start() Postfix
entry.BagSize = System.Math.Max(1, entry.BagSize / multiplier);
```

At multiplier `1` (default), vanilla bag sizes are unchanged.
At multiplier `10`, each bag size is reduced to ~1/10 of vanilla (floored at 1), making radiant
spawns approximately 10× more frequent.

---

## 3. Spawn Gate — `_allowRadiantSlimesToSpawnQuery`

**Source**: `MonomiPark.SlimeRancher.Slime.RadiantSlimeConfig` (Assembly-CSharp.dll),
`MonomiPark.SlimeRancher.Event.Query.Query` (Assembly-CSharp.dll),
`MonomiPark.SlimeRancher.Event.Query.CompositeQueryComponent` (Assembly-CSharp.dll)

```
RadiantSlimeConfig._allowRadiantSlimesToSpawnQuery : Query
```

This is **not a simple bool flag** — it is a `Query` ScriptableObject from the game's
event/condition system. The director caches the evaluated result:

```
RadiantSlimeDirector._isRadiantSpawnAllowedCached : bool
```

Updated via:

```
RadiantSlimeDirector.IsRadiantSpawnAllowed() : bool
```

The `AllowRadiantSlimesToSpawnQuery` property on `RadiantSlimeConfig` has `CallerCount = 731`,
meaning 731 different code call sites reference it — the query gate is checked everywhere slime
spawning is considered, not just in the director.

### Query System Architecture

A `Query` is a `ScriptableObject` with:
- `_root : CompositeQueryComponent` — a tree of conditions
- `IsSatisfied() : bool` — evaluates the tree
- `OnQuerySatisfied : Action<bool>` — fires when satisfied state changes (used to update
  `_isRadiantSpawnAllowedCached`)
- `GetChildCount() : int` — number of direct children in the root component
- `CountSatisfied() : int` — how many children are currently satisfied

`CompositeQueryComponent` composes children using one of three modes:

```csharp
enum BoolOperation { ALL_OF, ANY_OF, NONE_OF }
```

Children can be any `IGameQueryComponent` implementor. Confirmed available component types
from the DLL include:

| Component Type | What it checks |
|---|---|
| `TutorialEventQueryComponent` | A tutorial step has been completed |
| `PediaEntryEventQueryComponent` | A specific Slimepedia entry has been unlocked |
| `ZoneEventQueryComponent` | A zone has been unlocked |
| `UpgradeEventQueryComponent` | A vacpack upgrade has been obtained |
| `WorldSwitchQueryComponent` | A world switch is in a certain state |
| `GameEventQueryComponent` | A game event has occurred |
| `StaticEventQueryComponent` | Static/always-true condition (placeholder) |

### Confirmed Conditions (verified via runtime dump)

**Source**: `DumpAllowRadiantQuery()` output — `Utils/LocationDumper.cs`

```
Query='StrandAndValleyDiscoveredQuery'  satisfied=False  topLevelChildren=2  satisfiedChildren=0
Root: operation=ALL_OF  children=2
  [0] ZoneEventQueryComponent  dataKey='strand'  count=1
  [1] ZoneEventQueryComponent  dataKey='gorge'   count=1
```

The gate requires **both** of the following to have occurred (ALL_OF):
1. The player has visited the **Strand** zone at least once (`dataKey='strand'`, `count=1`)
2. The player has visited the **Gorge** zone at least once (`dataKey='gorge'`, `count=1`)

The query is named `StrandAndValleyDiscoveredQuery` in the asset data, which matches the
conditions exactly. Once both zones have been visited for the first time, the gate opens
permanently and `_isRadiantSpawnAllowedCached` flips to `true`.

**Note on `_sanctuaryPediaEntry`**: This field on the director is NOT related to the spawn gate.
It is used elsewhere in the director (likely in the sanctuary bag scalar logic — `ResizeBagsIfNeeded`
checks whether the sanctuary zone has been unlocked and applies `_sanctuaryUnlockedRadiantBagSizeScalar`).
The initial hypothesis that it controlled the spawn gate was incorrect.

### Implication for Modding

You cannot simply set `_isRadiantSpawnAllowedCached = true` on the director instance — the
`Query` re-evaluates and overwrites it. The cleanest override for testing is the debug field
`DEBUG_ForceRadiantSpawn` (see §12), which bypasses the query entirely at the draw-call level.

For AP logic: if the player is randomizing radiant slimes and has not yet visited both Strand
and Gorge, no radiant spawns will occur regardless of bag state or spawn rate multiplier. This
is worth noting in the apworld's documentation or hinting logic.

---

## 4. Zone Scoping

**Source**: `MonomiPark.SlimeRancher.Slime.RadiantSlimeDirector` (Assembly-CSharp.dll),
`MonomiPark.SlimeRancher.Slime.RadiantSlimeConfig` (Assembly-CSharp.dll)

```csharp
DrawFromRadiantShuffleBag(IdentifiableType slimeType, SceneGroup zone) : bool
```

### Bag Size vs. Bag Counter — an Important Distinction

**Bag sizes are NOT per-zone.** `RadiantSlimeConfig._radiantShuffleBagSizes` is a flat array of
`{Slime, BagSize}` pairs with no zone dimension. Every zone uses the same cap for a given slime
type — a Pink Slime's bag is the same maximum depth in Rainbow Fields as it is in Ember Valley.

**What IS zone-scoped is the draw counter.** The `SceneGroup` parameter in
`DrawFromRadiantShuffleBag` determines which runtime `RadiantShuffleBag` instance tracks the
progress toward the next guaranteed spawn. Each `(slimeType, SceneGroup)` pair has its own
independent counter.

This means:
- A zone with many Pink Slime spawns will exhaust its Pink counter faster than a zone with few,
  reaching the guaranteed spawn sooner.
- Changing zones does not reset the counter for the zone you left — both zones continue their
  independent counts across play sessions.
- A species that only spawns in one zone effectively has a single counter (since the other zone
  counters never advance).
- Bag state is persisted in `RadiantSlimesModel` across play sessions.

---

## 5. Sanctuary Boost Scalar

**Source**: `MonomiPark.SlimeRancher.Slime.RadiantSlimeConfig` (Assembly-CSharp.dll)

```
RadiantSlimeConfig._sanctuaryUnlockedRadiantBagSizeScalar : float
```

When the player unlocks the Sanctuary, this `float` scalar is applied to bag sizes via
`ResizeBagsIfNeeded()`. The scalar is a **divisor** applied to `BagSize`, reducing it (i.e.
making radiant spawns more frequent).

The director calls `ResizeBagsIfNeeded()` on startup and whenever a zone unlocks:

```csharp
// Source: RadiantSlimeDirector.OnZoneUnlocked(ZoneDefinition)
// Fires on zone-unlock event; triggers ResizeBagsIfNeeded()
```

**Confirmed value**: `_sanctuaryUnlockedRadiantBagSizeScalar = 0.95`

**Source**: `DumpRadiantSlimes()` runtime output (`Utils/LocationDumper.cs`), logged as:
```
[AP-Dump] config='RadiantSlimeConfig'  sanctuaryScalar=0.95
```

Applied as a multiplier to `BagSize` when `sanctuaryRadiantBoostUnlocked = true` inside
`GetBagSize(IdentifiableType, bool)`. A scalar of `0.95` means each bag shrinks to **95% of
its base size** (a **5% reduction**) once the Sanctuary is unlocked. This is a mild bonus —
a bag of 2000 becomes 1900, and a bag of 25 becomes 23. It meaningfully impacts only the
smallest bags; for large-bag slimes the difference is nearly imperceptible.

---

## 6. Spawn Source Classification

**Source**: `MonomiPark.SlimeRancher.Slime.RadiantSlimeDirector` (Assembly-CSharp.dll)

The game classifies every radiant spawn into one of two categories for analytics:

```csharp
enum RadiantSpawnedSourceAnalyticsStrings
{
    GUARANTEED,   // bag was exhausted; radiant was forced
    SHUFFLE_BAG,  // bag draw succeeded before exhaustion
}
```

- **`GUARANTEED`**: The bag counter reached `BagSize`, triggering a forced radiant spawn.
- **`SHUFFLE_BAG`**: The draw succeeded before the bag was exhausted (lucky early spawn).

This distinction is reported via analytics events (`RadiantSlimeSpawnedAnalyticsEvent`) but does
not affect gameplay behavior — both paths result in the same radiant slime spawning.

---

## 7. Radiant Eligibility

**Source**: `MonomiPark.SlimeRancher.Slime.RadiantSlimeConfig` (Assembly-CSharp.dll)

```csharp
RadiantSlimeConfig.CanSlimeBeRadiant(IdentifiableType id) : bool
```

Not all slime types are eligible to become radiant. The config asset contains a fixed list
(`_radiantShuffleBagSizes`) covering only slimes that have radiant variants. Any `IdentifiableType`
not in this list returns `false` from `CanSlimeBeRadiant`.

The director also exposes this check:

```csharp
RadiantSlimeDirector.CanSlimeBeRadiant(IdentifiableType) : bool
```

**22 confirmed eligible slime types** are listed in §10.

---

## 8. Lost Radiant Tracking

**Source**: `MonomiPark.SlimeRancher.Slime.RadiantSlimeDirector` (Assembly-CSharp.dll)

The game tracks radiant slimes that escape or despawn without being collected. This uses a
timestamp-based queue:

```
RadiantSlimesModel.LostRadiantQueue  // priority queue ordered by timestamp
```

Relevant director methods:

```csharp
RegisterNewRadiantAsCollected(IdentifiableType, Double)
    // Called when player collects a radiant; records timestamp

MarkRadiantAsLost(IdentifiableType, Double timestamp, Boolean wasEscaped, Int32 reason)
    // Called when radiant despawns/escapes; enqueues to lost queue

PeekEarliestLostRadiantTime()  : Double
DequeueEarliestLostRadiant()   : (IdentifiableType, Double)
TryPeekEarliestLostRadiant(out ...) : bool
    // Queue access methods
```

The "lost" tracking appears to be used for analytics (reporting how many radiants escaped) and
potentially for future gameplay features. It does **not** appear to affect spawn rates or bag
sizes based on current decompilation.

---

## 9. Bag Resizing Lifecycle

**Source**: `MonomiPark.SlimeRancher.Slime.RadiantSlimeDirector` (Assembly-CSharp.dll)

Bag sizes are not fixed at startup — they are recalculated at two points:

1. **`RadiantSlimeDirector.Start()`** — Initial bag setup on scene load
   - Reads `_radiantShuffleBagSizes` from config
   - Applies `_sanctuaryUnlockedRadiantBagSizeScalar` if sanctuary is already unlocked

2. **`RadiantSlimeDirector.OnZoneUnlocked(ZoneDefinition)`** — Zone unlock event handler
   - Calls `ResizeBagsIfNeeded()` when any zone becomes accessible
   - Covers the sanctuary unlock specifically via `_sanctuaryZoneDef` reference

**AP mod patch timing**: The `RadiantSlimePatch` patches `Start()` as a Postfix, applying the
`radiant_spawn_rate_multiplier` divisor after the game's own initialization completes. This means
the AP multiplier stacks on top of the sanctuary scalar (both are applied before the first draw).

**Source**: `Patches/LocationPatches/RadiantSlimePatch.cs`

---

## 10. Confirmed Radiant Slime Types (22)

**Source**: `Data/LocationTable.cs` — `SlimepediaRadiantEntry` rows, location IDs 819821–819842
**Verification method**: In-game `DumpRadiantSlimes()` output (see `Utils/LocationDumper.cs`)

| # | AP Location Name | Asset Name (`EntryName`) | Location ID |
|---|---|---|---|
| 1 | Radiant Angler Slime | `RadiantAngler` | 819821 |
| 2 | Radiant Batty Slime | `RadiantBatty` | 819822 |
| 3 | Radiant Boom Slime | `RadiantBoom` | 819823 |
| 4 | Radiant Cotton Slime | `RadiantCotton` | 819824 |
| 5 | Radiant Crystal Slime | `RadiantCrystal` | 819825 |
| 6 | Radiant Dervish Slime | `RadiantDervish` | 819826 |
| 7 | Radiant Fire Slime | `RadiantFire` | 819827 |
| 8 | Radiant Flutter Slime | `RadiantFlutter` | 819828 |
| 9 | Radiant Honey Slime | `RadiantHoney` | 819829 |
| 10 | Radiant Hunter Slime | `RadiantHunter` | 819830 |
| 11 | Radiant Hyper Slime | `RadiantHyper` | 819831 |
| 12 | Radiant Phosphor Slime | `RadiantPhosphor` | 819832 |
| 13 | Radiant Pink Slime | `RadiantPink` | 819833 |
| 14 | Radiant Puddle Slime | `RadiantPuddle` | 819834 |
| 15 | Radiant Ringtail Slime | `RadiantRingtail` | 819835 |
| 16 | Radiant Rock Slime | `RadiantRock` | 819836 |
| 17 | Radiant Saber Slime | `RadiantSaber` | 819837 |
| 18 | Radiant Sloomber Slime | `RadiantSloomber` | 819838 |
| 19 | Radiant Tabby Slime | `RadiantTabby` | 819839 |
| 20 | Radiant Tangle Slime | `RadiantTangle` | 819840 |
| 21 | Radiant Twin Slime | `RadiantTwin` | 819841 |
| 22 | Radiant Yolky Slime | `RadiantYolky` | 819842 |

**Note on Largo/Gordo eligibility**: The `CanSlimeBeRadiant` method takes an `IdentifiableType`,
which means it can theoretically apply to any identifiable including largos. However, all 22
confirmed types above are base slime variants. Whether largos can be radiant is not confirmed
from the decompilation alone.

---

## 11. AP Mod Integration

**Source**: `Archipelago/SlotData.cs`, `Patches/LocationPatches/RadiantSlimePatch.cs`

### Slot Data Options

| Slot Key | Type | Default | Description |
|---|---|---|---|
| `"randomize_slimepedia_radiant"` | `bool` | `false` | When `true`, enables the 22 radiant slime Slimepedia entries as AP location checks. Each is sent via `ArchipelagoClient.SendCheck()` when the player first collects that radiant variant. |
| `"radiant_spawn_rate_multiplier"` | `int` | `1` | Range 1–10. Divides all shuffle bag sizes by this value, increasing radiant spawn frequency. At `10`, each bag is at most 1/10 its vanilla size (minimum 1). |

### Location Check Trigger

**Source**: `Patches/LocationPatches/RadiantSlimePatch.cs` (patch target TBD — likely
`RadiantSlimeDirector.RegisterNewRadiantAsCollected` Postfix)

The check fires when `RegisterNewRadiantAsCollected(IdentifiableType, Double)` is called by the
game, confirming the radiant was collected. The `IdentifiableType.name` (e.g. `"RadiantPink"`) is
used as a lookup key in `LocationTable._byEntryName`.

### Lookup Path

```
IdentifiableType.name → LocationTable.GetByEntryName(name) → LocationInfo.Id → SendCheck(id)
```

The `_byEntryName` dictionary covers `SlimepediaRadiantEntry` rows (added in the
`TypeInitializationException` fix — see `Data/LocationTable.cs`).

---

## 12. Debug Fields

**Source**: `MonomiPark.SlimeRancher.Slime.RadiantSlimeDirector` (Assembly-CSharp.dll)

```csharp
DEBUG_BagSizeScalar   : float   // runtime override for all bag sizes (multiplicative)
DEBUG_ForceRadiantSpawn : bool  // if true, every draw returns radiant
```

These fields appear to be developer tools compiled into the release build (Unity does not strip
`DEBUG_`-prefixed fields in IL2CPP unless explicitly excluded). They are not normally accessible
via in-game UI but can be set via the BepInEx debug panel or a Harmony patch.

**Practical use**: Setting `DEBUG_ForceRadiantSpawn = true` on the director instance is the
most reliable way to test radiant collection patches without waiting for bag exhaustion.

---

## 13. Known Unknowns

The following data points could not be determined from static decompilation alone and require
runtime inspection or additional research:

| Unknown | How to Verify |
|---|---|
| ~~Vanilla `BagSize` values per slime type~~ | ✅ **Confirmed via `DumpRadiantSlimes()`** — full table in §2. Range: Yolky (25) to Pink/Rock/Batty/Cotton/Crystal/Phosphor (2000). |
| ~~Exact `_sanctuaryUnlockedRadiantBagSizeScalar` float value~~ | ✅ **Confirmed via `DumpRadiantSlimes()`** — value is `0.95`. Bags shrink to 95% of base size when Sanctuary is unlocked (mild ~5% reduction). See §5. |
| **Which `SceneGroup` each slime type is eligible in** | Requires runtime bag inspection — the config asset lists eligible types but not which zones each type appears in. Use `DumpRadiantSlimes()` with zone filtering. Note: bag SIZES are uniform per slime type across zones; only the draw counter differs (see §4). |
| ~~Exact conditions in `_allowRadiantSlimesToSpawnQuery`~~ | ✅ **Confirmed via runtime dump** — `StrandAndValleyDiscoveredQuery`, `ALL_OF` two `ZoneEventQueryComponent` conditions: `dataKey='strand'` AND `dataKey='gorge'`, each `count=1`. Radiant spawns are gated until both Strand and Gorge have been visited for the first time. See §3. |
| **Largo radiant eligibility** | Pass a Largo `IdentifiableType` to `RadiantSlimeConfig.CanSlimeBeRadiant()` at runtime. |
| **Lost radiant gameplay impact** | `MarkRadiantAsLost` parameters include `wasEscaped: bool` and `reason: int` — the meaning of `reason` values is unknown without further decompilation of the call sites. |
| ~~Per-zone bag counter persistence~~ | ✅ **Confirmed** — `RadiantShuffleBagV01` extends `PersistedDataSet` with `LoadData`/`WriteData`. Both `CurrentIndex` and `RadiantSpawnIndex` are saved to the SR2 save file and fully restored on load. Progress is never lost between sessions. See §2. |

---

## 14. Plain-English Summary

This section explains everything above for people who aren't familiar with reading code or game
internals. No programming knowledge required.

---

### What is a Radiant Slime?

Radiant Slimes are rare, glowing versions of normal slimes. Each slime species (Pink, Tabby,
Crystal, etc.) has exactly one radiant variant. When you catch one and it gets logged in your
Slimepedia, that's considered "collected." There are 22 radiant variants in total.

---

### How Does the Game Decide When to Spawn One?

The game uses something called a **shuffle bag**. Imagine a bag of numbered slots — one of
those slots secretly contains the golden marble. The game picks that slot's number at random
when the bag is created, then works through the bag one draw at a time. When the draw number
matches the golden slot, that slime spawns as a radiant. The bag then resets with a newly
randomised golden slot for the next cycle.

This is subtly different from a pure coin-flip on every spawn. Because the golden slot is
fixed at the start of each cycle, you are guaranteed **exactly one radiant per cycle** —
no more, no less. The radiant could appear early in the cycle (lucky) or late (unlucky), but
it will always appear before the bag runs out. You can never get two radiants back-to-back,
and you can never go longer than the full bag size between radiants.

Each slime species has its own independent bag, and each *zone* in the game has its own copy of
that bag too. So the game is tracking something like "Pink Slime bag in Rainbow Fields" and "Pink
Slime bag in Ember Valley" as two completely separate counters. A radiant Pink spawning in one
zone has no effect on when the next radiant Pink will appear in another zone.

---

### Can the Player Do Anything to Influence the Rate?

**In vanilla:** Unlocking the Sanctuary makes all bags shrink by **5%** (the scalar is 0.95,
so a bag of 2000 becomes 1900, a bag of 25 becomes 23). This is a mild bonus — it nudges
radiant spawns slightly more frequent but won't be noticeable for most species. It has the
most practical effect on already-small bags like Yolky (25 → 23) and Dervish/Tangle (50 → 47).

Not all radiant slimes are equally rare. Bag sizes vary enormously across species:
- **Easiest to find:** Yolky (bag of 25), Dervish and Tangle (50 each) — you're almost
  guaranteed to see these relatively quickly in any active session
- **Middle tier:** Flutter, Hunter, Hyper, Saber, Sloomber (all 1000)
- **Hardest to find:** Batty, Cotton, Crystal, Phosphor, Pink, Rock (all 2000) — on average
  you'd need to encounter 2000 of that species before one goes radiant, though the guaranteed
  draw means it can't go longer than that

**In the Archipelago randomizer mod:** There's a setting called `radiant_spawn_rate_multiplier`
(range 1–10). At the default of `1`, nothing changes from vanilla. At `10`, every bag is shrunk
to roughly one-tenth its normal size — so radiant slimes appear about ten times more often. This
is capped so no bag goes below a size of 1 (meaning every spawn of that species would be radiant).

---

### Is There Any Way to Completely Block Radiant Spawns?

Yes. The game has a master on/off switch for all radiant spawning called the "allow radiant
spawn query." This isn't a simple toggle — it's a condition the game evaluates continuously by
checking a set of rules (probably things like "is the tutorial done?" or "is the player past a
certain story point?"). If the condition fails, no radiant slimes will appear anywhere regardless
of bag state. The bags continue filling up, so once the condition is met again, they'll be
primed to spawn soon.

---

### Do Radiant Spawn Rates Differ Between Zones?

No — and this is a subtle but important point. The *maximum gap* between radiant spawns is set
once per slime species and applies the same way in every zone. A Pink Slime has the same bag
depth in Rainbow Fields as in Ember Valley.

What *does* differ between zones is the *progress* toward the next guaranteed spawn. Each zone
tracks independently how many Pink Slimes have spawned there since the last radiant. So if
Rainbow Fields has had 40 Pink Slime spawns and Ember Valley has only had 5, Rainbow Fields is
much closer to forcing a radiant Pink. The bags fill at their own pace based purely on how much
activity that zone sees.

The practical upshot: if you want to find a Radiant Pink Slime faster, spend time in whichever
zone has the most Pink Slime activity — that zone's counter is advancing fastest.

### What is the Radiant Spawn Gate and When is it Active?

There is a master switch the game calls the "allow radiant slimes to spawn query." When this
switch is off, no radiant slimes appear anywhere, regardless of bag state. The bags keep filling
during this time, so once the switch turns on, radiant spawns can happen quickly.

**What turns the switch on?** This has been confirmed via in-game runtime inspection. The gate
is named `StrandAndValleyDiscoveredQuery` and requires **both** of the following to be true at
the same time:

1. The player has **visited the Strand** at least once
2. The player has **visited the Gorge** at least once

Both must happen before any radiant slime can appear anywhere in the game. This is a
progression gate — it ensures the player has moved beyond early-game Rainbow Fields play and
has explored the second and third main zones before the radiant system activates.

The switch is checked constantly throughout the game's spawning code (731 different call sites),
so the moment both zones are visited, it activates immediately with no delay.

**Note:** The game code also has a `_sanctuaryPediaEntry` reference in the radiant director,
which initially suggested the Sanctuary discovery was the gate. It turns out that reference is
used for a different purpose (the Sanctuary bag size bonus), not for the spawn gate itself.

### Does Progress Reset When I Close the Game?

**No — bag progress is fully saved.** The game writes both your current draw position and the
pre-seeded radiant slot number into your SR2 save file every time you exit. When you reload,
both values are restored exactly. If you were 1,800 draws into a Pink Slime bag of 2000 before
closing, you'll still be 1,800 draws in when you load back up.

This also means the outcome of the next radiant is already determined before you even open the
game — the golden slot was chosen when the last bag reset, and it's sitting in the save file
waiting for you to reach it.

### What Happens if a Radiant Slime Escapes?

The game keeps a log of every radiant slime that spawned but wasn't collected — either because
it escaped the ranch, despawned, or was otherwise lost. Each lost radiant is recorded with a
timestamp and a reason code. This data appears to be used mainly for analytics (tracking how
often players miss radiant slimes), and does not currently appear to directly affect future
spawn rates or bag sizes.

---

### How Does the Archipelago Mod Use All This?

When you're playing with the Archipelago randomizer and the "randomize radiant slimepedia"
option is turned on, each of the 22 radiant Slimepedia entries becomes a **location check** —
meaning collecting that radiant sends a check to the Archipelago server, which might unlock
something for you or another player in the multiworld.

The mod detects a radiant being collected by watching for the same function the game calls
internally when it logs a radiant as "registered collected." It then looks up which AP location
that slime species corresponds to and sends the check.

The spawn rate multiplier option is there specifically because radiant slimes can be quite rare
in vanilla. Without it, some players might spend hours without seeing a specific species. Turning
the multiplier up makes them appear more frequently so the randomizer doesn't become a
frustrating waiting game.

---

### What Don't We Know Yet?

A few things can only be determined by actually running the game with logging enabled:

- **Which zones each slime is eligible to be radiant in** — Not all slimes appear in all zones,
  so their radiant bags are only relevant in zones where they naturally spawn. The exact zone
  mapping requires runtime inspection.

---

## Appendix: Source Reference Index

| Source | Path | Used In |
|---|---|---|
| `RadiantSlimeConfig` class | `Assembly-CSharp.dll` → `MonomiPark.SlimeRancher.Slime` | §2, §3, §5, §7 |
| `RadiantSlimeDirector` class | `Assembly-CSharp.dll` → `MonomiPark.SlimeRancher.Slime` | §2, §3, §4, §6, §8, §9, §12 |
| `RadiantSlimeDirectorConfiguration` class | `Assembly-CSharp.dll` → `MonomiPark.SlimeRancher.Slime` | §1 |
| `RadiantSlimesModel` class | `Assembly-CSharp.dll` → `MonomiPark.SlimeRancher.Slime` | §4, §8 |
| `RadiantSlimeSpawnedAnalyticsEvent` | `Assembly-CSharp.dll` | §6 |
| `Query` class | `Assembly-CSharp.dll` → `MonomiPark.SlimeRancher.Event.Query` | §3 |
| `CompositeQueryComponent` class | `Assembly-CSharp.dll` → `MonomiPark.SlimeRancher.Event.Query` | §3 |
| `IGameQueryComponent` implementors (7 types) | `Assembly-CSharp.dll` → `MonomiPark.SlimeRancher.Event.Query` | §3 |
| `Data/LocationTable.cs` | This repo | §10, §11 |
| `Archipelago/SlotData.cs` | This repo | §11 |
| `Patches/LocationPatches/RadiantSlimePatch.cs` | This repo | §2, §9, §11 |
| `Utils/LocationDumper.cs` (lines 765–806) | This repo | §5, §13 |
