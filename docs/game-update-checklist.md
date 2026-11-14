# Game Update Readiness — API Surface Inventory

> ## Results — 2026-09-23 update
>
> **Tier 1 (patch targets): clean.** 65/65 overloads resolve, **0 gone, 0 signature
> changes**. Re-run 2026-09-26 against the same interop: unchanged 65, changed 0, gone 0.
> `check-api.py` reads the live interop directly, so this verdict was never affected by the
> stale-`libs/` problem described below. Three `CallerCount` drifts only, all with identical signatures:
> `UpgradeModel.GetUpgradeLevel` 20→19, `FabricatorItemDetails.SetItem` 5→4,
> `ShopItemDescriptionBlurb.UpdateDisplay` 6→5. None reached 0. Far gentler than 5/13.
>
> **Tier 3c/3d (fields, enum renames): clean — but only after a correction, see below.**
> `LandPlot.Upgrade` still 21 members; the mod maps 19 and deliberately excludes
> `NONE`/`MIRACLE_MIX` — complete coverage, no gap.
>
> > **CORRECTION (2026-09-26): the original "0 errors" was a false all-clear.**
> > The csproj prefers the committed `libs/` over the live interop:
> > `<LibsDir Condition="Exists('.../libs/Assembly-CSharp.dll')">`. That snapshot was from
> > **2026-04-17**, so `dotnet build` was type-checking against an API five months and two
> > updates old and could not see the 2026-09-23 surface at all. A build cannot clear Tier 3c
> > when it is not compiled against the new build.
> >
> > One rename had already shipped through that gap:
> > `UIImageDynamicLoader._onLoaded` → `_onSetSprite`. It reached a player as a
> > `MissingMethodException` from `ShopFullArtPatch.Prefix` while browsing the shop on an
> > **unmodded, disconnected** save — Il2CppInterop resolves members at JIT time, so the
> > `IsEnabled` guard on the patch's first line never ran.
> >
> > `libs/Assembly-CSharp.dll` has been refreshed to the 2026-09-23 interop and the rename
> > fixed. The build is now genuinely 0 errors, so Tier 3c/3d is clear on its own terms.
> >
> > Only `Assembly-CSharp.dll` was refreshed. Refreshing all eleven committed DLLs produced
> > 1446 `CS0656 NullableAttribute` errors — the new `Il2Cppmscorlib.dll` is incompatible with
> > this project's nullable-reference-types setting. The other ten stay at their committed
> > versions; game types live in `Assembly-CSharp` and that is the one that must track the game.
> >
> > This is structurally the same bug as the stale-interop guard added to `check-api.py` during
> > the original run — the checker was taught to refuse stale input, the build was not. Refresh
> > `libs/Assembly-CSharp.dll` as step 0 of any future update, before trusting a build result.
>
> **Tier 2 (trampoline): unverified — still needs the 12-row in-game smoke test.**
> Static analysis cannot clear this; a changed prologue decompiles fine and crashes at
> runtime.
>
> **Tier 3a/3b (posKeys, asset names): largely cleared 2026-10-02 - see below.** The 1.3
> dumps now live in `docs/dumps/1.3/` alongside the 1.2 set; diffing them answers most of
> 3b. posKeys (3a) remain unverified, and that is where the remaining risk now lives.
>
> Corrections to this document from the run:
> - The "`STORAGE` ×3" in Tier 3d was a regex artifact (`[A-Za-z_]+` dropped the digits of
>   `STORAGE2/3/4`), not a real mapping problem.
> - `PediaDirector` is in the **root namespace**, not `Il2CppMonomiPark.SlimeRancher.Pedia`.
>
> Two fixes to the tooling, both prompted by gaps this run exposed:
> - **Stale-interop guard.** The interop DLL had not been regenerated when we started; the
>   checker would have compared the old build against the old baseline and reported a false
>   all-clear. It now refuses to run when the interop DLL predates `GameAssembly.dll`.
> - **Overload coverage.** The checker recorded only the first overload per target. The mod
>   patches two `PediaDirector.Unlock` overloads by explicit argument-type array, resolved by
>   Harmony at runtime — the compiler cannot catch a missing one. Both are now tracked
>   (`Unlock(PediaEntry, bool)` CC=14, `Unlock(IdentifiableType, bool)` CC=3, both intact).
>
> `baseline.tsv` has been updated to the new build; the pre-update record is preserved as
> `baseline-2026-05-13.tsv`.
>
> **What's new:** no reliable type-level diff was possible — see "What's new" at the end.


Inventory of every game-owned API the mod depends on, graded by how a change would
manifest, with the validation step for each. Written ahead of the update expected the
week of 2026-09-22.

Baseline captured against the interop DLL from the current (5/13/2026) build.

**Companion tooling:** `tools/apicheck/check-api.py` re-derives every Harmony target's
signature and `CallerCount` from the live interop DLL and diffs it against
`tools/apicheck/baseline.tsv`. Run it first after the update — it mechanically answers
most of Tier 1 and Tier 2 below.

```bash
python tools/apicheck/check-api.py
```

---

## The three ways this breaks

The mod's dependence on the game falls into three tiers that differ *entirely* in how a
change announces itself. This matters more than raw count: a randomizer that crashes is
an annoyance, a randomizer that silently stops sending a check ships unbeatable seeds.

| Tier | Surface | Size | Failure mode | Detectability |
|---|---|---|---|---|
| **1 — Loud** | Harmony patch targets | 64 sites / 46 types | `PatchAll` throws → **entire mod dead** | Immediate, at load |
| **2 — Crashy** | Native prologues of patched methods | 22 at `CallerCount(0)` | Trampoline crash → game hard-crashes on one interaction | On first use of that feature |
| **3 — Silent** | Asset names, posKeys, enums, private fields | ~300 identity keys | Check never fires → **unbeatable seed** | Only by deliberate testing |

Tier 3 is the one that needs real playtesting. Tiers 1 and 2 are largely automatable.

---

## Tier 1 — Harmony patch targets (64 sites, 46 types)

### The single highest-leverage fix, before the update lands

[Plugin.cs:72](../Plugin.cs) applies every patch in one unguarded call:

```csharp
new Harmony(PluginInfo.GUID).PatchAll(typeof(Plugin).Assembly);
```

If the update renames or removes **any one** of the 64 target methods, `PatchAll` throws
and *every* patch fails to apply. The mod loads, logs an exception, and then silently
behaves like vanilla — no checks, no items, no gates. The log names the offending method,
but nothing else works until it's fixed.

Wrapping each patch class in its own try/catch would degrade this from "total outage" to
"one feature offline, named in the log." This is worth doing *before* the update rather
than after, so the first launch on the new build produces a usable diagnostic instead of a
single stack trace. Suggested: replace the blanket `PatchAll` with a loop over
`[HarmonyPatch]`-annotated types, catching per type.

### Targets by owning type

Full signatures and `CallerCount` values are in `tools/apicheck/baseline.tsv`.
Types where a patch has already been abandoned once are flagged.

| Type | Patched methods | Notes |
|---|---|---|
| `ActorUpgradeHandler` | `OnUpgradeChanged`⁰, `SetModifiersFromModel`, `CheckUpgradePropertiesAreAvailable` | Not a MonoBehaviour |
| `UpgradeModel` | `GetUpgradeLevel` (CC=20), `IncrementUpgradeLevel`, `Push` | `GetUpgradeLevel` is a **global** detour with 3 exemptions |
| `AutoSaveDirector` | `StartNewGame`, `BeginLoad`, `OnSessionEnded`⁰, `DeleteGame` | Positional args (`__0`, `__1`) |
| `OptionsUIRoot` | `BindCategories`, `BindItemCategory`, `SwapCategory`, `Close`⁰, `CheckThenSwapCategory`⁰ | Hosts the AP connect UI |
| `PlayerUpgradeFabricatableItem` | `…IFabricatableItem_FabricateAndSpendCost`⁰, `get_CurrentUpgradeLevel`, `get_NextUpgradeLevelDefinition` | Explicit interface impl — **mangled name is the fragile part** |
| `AccessDoorUI` / `AccessDoorUIActivator` | `…IDisplayShowDataHandler…UpdateShowData`⁰, `OnInteract`⁰, `OnPurchaseMenuResult`⁰ | All three CC=0; mangled interface name |
| `ConversationViewHolder` | `ShowConversation`, `ShowConversationPage` | Do **not** Prefix-false these (softlock — see CLAUDE.md) |
| `ConversationPageGift{Blueprint,Gadget,UpgradeComponent}` | `ApplyChanges`⁰ | All CC=0 |
| `ShopRuntimeItem`, `ShopCategoryRuntime`, `ShopItemButton`, `ShopItemDescriptionBlurb`, `ShopPurchasePopup`, `ShopHeaderContent`, `ShopCategoryBlurb`, `UIImageDynamicLoader` | 8 UI/economy methods | Largest single feature surface |
| `PediaDirector` | `Unlock` ×2 overloads (CC=14) | Overload set matters — two distinct signatures patched |
| `WorldStatePrimarySwitch` | `Activate`⁰ | **`SetStateForAll` already abandoned** (prologue changed 5/13) |
| `WorldStateInvisibleSwitch` | `SetStateForAll` | Still safe; fires repeatedly |
| `PuzzleSlotLockable` | `ActivateOnUnlock` | **`PlortDepositor.ActivateOnFill` already abandoned** |
| `TreasurePod` / `TreasurePodUIInteractable` | `Activate`, `OnInteract`⁰ | |
| `RadiantSlimeDirector` | `DrawFromRadiantShuffleBag` | **Signature changed 5/13** (+`SceneGroup`) — highest precedent for churn |
| `MapNodeActivator` | `SendMapNodeUnlockedAnalyticsEvent` | **`Activate` already abandoned** |
| `ResearchDroneUI` | `ToggleArchive`⁰ | `ResearchDroneActivator` patches already abandoned |
| `GordoEat` | `Awake`⁰, `ImmediateReachedTarget` | |
| `PlayerState`, `PlayerDamageable`, `PlayerDeathHandler` | `AddCurrency`, `Damage`, `OnDeath`⁰ | DeathLink + newbucks goal |
| `LandPlotLocation`, `LandPlotUIRoot`, `UIActivator` | `Replace`, `Upgrade`, `OnInteract` | Ranch plots |
| `BossFightController` | `TryToStartFight`⁰ | Prismacore gate |
| `CoreRoomController` | `UpdateState` | Prismacore goal |
| `PlortEconomyDirector` | `RegisterSold` (patched twice) | Plort goal + market |
| `FabricatorItemDetails`, `PreviewPopup`, `NewGameOptionsUIRoot`, `FixedConversation`, `PlortDepositor`, `AcquisitionUtility`, `WeatherRegistry`, `PlayerUpgradeObtainedQueryComponent` | 1 method each | |

⁰ = `CallerCount(0)` — see Tier 2.

**Validation:** run `check-api.py`. Anything reported as `GONE` must be re-targeted before
the mod will load at all.

---

## Tier 2 — Trampoline-crash risk (22 targets at `CallerCount(0)`)

`CallerCount(0)` means Cpp2IL found no managed callers, so xref data for the method is
thin and its reconstructed prologue is less trustworthy. Every patch abandoned in the
5/13 update was `CallerCount(0)`:

- `RadiantSlimeDirector.DrawFromRadiantShuffleBag` — signature changed
- `WorldStatePrimarySwitch.SetStateForAll` — prologue changed
- `MapNodeActivator.Activate` — CC(0) + virtual
- `ResearchDroneActivator.OnInteract` / `ActivateDrone` — CC(0)
- `PlortDepositor.ActivateOnFill` / `OnTriggerEnter` — stack overflow

**But CC=0 is a correlate, not a predictor.** Plenty of current CC=0 patches work fine
today — `PlayerDeathHandler.OnDeath`, `WorldStatePrimarySwitch.Activate`,
`BossFightController.TryToStartFight` are all CC=0 and all reliable. Treat the list as a
watch list ordered by prior, not a list of expected breakages.

The 22 current CC=0 targets, grouped by the interaction that would crash the game:

| Interaction to exercise | CC=0 targets at risk |
|---|---|
| Open a Conservatory expansion terminal | `AccessDoorUI.…UpdateShowData`, `AccessDoorUIActivator.OnInteract`, `.OnPurchaseMenuResult` |
| Receive any conversation gift | `ConversationPageGift{Blueprint,Gadget,UpgradeComponent}.ApplyChanges`, `FixedConversation.RecordPlayed` |
| Craft anything at the Fabricator | `PlayerUpgradeFabricatableItem.…FabricateAndSpendCost`, `FabricatorItemDetails.ItemCrafted` |
| Open a treasure pod | `TreasurePodUIInteractable.OnInteract` |
| Open a drone archive page | `ResearchDroneUI.ToggleArchive` |
| Feed a Gordo / deposit a shadow plort | `GordoEat.Awake`, `PlortDepositor.Awake` |
| Receive any upgrade | `ActorUpgradeHandler.OnUpgradeChanged`, `PlayerUpgradeObtainedQueryComponent.IsSatisfied` |
| Die | `PlayerDeathHandler.OnDeath` |
| Press an EV/SS region gate switch | `WorldStatePrimarySwitch.Activate` |
| Start the Prismacore fight | `BossFightController.TryToStartFight` |
| Start a new game / quit to menu | `NewGameOptionsUIRoot.OnSubmit`, `AutoSaveDirector.OnSessionEnded` |
| Open/close the Options menu | `OptionsUIRoot.Close`, `.CheckThenSwapCategory` |

**Validation:** these cannot be checked statically — a changed prologue decompiles fine and
crashes at runtime. Each row is one deliberate in-game action. Doing all 12 is the smoke
test; do them on a throwaway save with the debug panel open.

---

### Tier 3b results - 1.2 vs 1.3 dump diff (2026-10-02)

Dumps are now versioned: `docs/dumps/1.2/` (pre-update) and `docs/dumps/1.3/` (post-update).
Source comments in the mod cite the **1.2** paths, because that is where each claim was
actually confirmed; the dates on them predate 1.3.

Comparing the two by asset name:

| Surface | 1.2 | 1.3 | Removed | Verdict |
|---|---|---|---|---|
| Gadget names | 391 | 429 | **0** | clear, 38 pure additions |
| IdentifiableType names | 768 | 806 | **0** | clear, 38 pure additions |
| Pedia entries | 569 | 630 | **0** | clear, 61 pure additions |
| Shop rule-set groups | 14 | 14 | **0** | clear, same groups and same ALL_OF/ANY_OF shapes |
| Shop item GUIDs | 153 | 173 | **0** | clear, verified against the mod's 149 locations |

Nothing the mod identifies by asset name disappeared. Every difference is new 1.3 content.

**The Slimepedia goal is unaffected**, worth stating because the raw numbers look alarming.
1.3 adds a whole new pedia category (`Palettes`, 23 entries) and grows `Blueprints` 395 to
433. The three categories the goal actually polls are untouched: Slimes 30, Resources 55,
Radiant Slimes 22. This is the payoff from scoping the goal to the AP location table rather
than `PediaRuntimeCategory.AllUnlocked()` - the old fast path would have started demanding 23
Palette entries that have no AP location, making the goal unwinnable.

**Shop locations: verified clear.** The first 1.3 shop dump was taken before visiting all
zones (`RangeExchange items=43` against 1.2's 153) and was useless for comparison; it has
since been re-taken from an all-zones save (`items=174`).

The dump carries `guid=`, which is what the mod actually keys shop items by, so this is a
direct check rather than a proxy. All **149** `LocationType.PolestarShop` GUIDs in
`LocationTable` are present in 1.3. Nothing from 1.2 vanished either.

Sixteen 1.3 entries report `asset='<not loaded>'` where 1.2 had a real name. That is the
dumper reading an unloaded asset, not a rename — the GUIDs are unchanged. It is also a
decent argument for the existing GUID keying: the asset *name* is the field that degrades.

Twenty GUIDs appear in 1.3 that were absent from 1.2. One is not new content at all:
`GoldenYolkyStatue` is already an AP location, and was simply invisible in the 1.2 dump
because it sits behind a Yolky discovery event (hence its place in `_RNG_SLIMES_EXCLUDED`).
A conditional item missing from a dump means the condition was unmet, not that the item is
gone — worth remembering when reading any future dump diff.

The other 19 are genuinely new 1.3 shop content with no AP location, and are candidates if
the shop pool is ever extended: `DroneStationComponent`, `SunflowerStandie`, three more
Golden statues (`Batty`, `Honey`, `Tabby`), and 14 `VacPalette_*` entries matching the new
`Palettes` pedia category.

**Still missing from 1.3:** `Components.txt`, `Conversations.txt`, `plot.txt`. These back the
upgrade-component mappings, the conversation gift table, and the ranch-plot upgrade and price
tables. All three are name-keyed and all three are unverified against 1.3.

---

### Tier 2 smoke test — runnable form (added 2026-09-26)

Every CC=0 patch now logs a one-shot `[TRACE] <Patch>.<Method> — first entry` on its first
invocation (Debug builds only), so a row is *confirmed* by a log line rather than inferred
from the game not crashing. Absence of the line means the interaction never reached the
patch — which is a finding in itself, and a different one from a crash.

Run these in one session, then grep the log:

```bash
grep -E "first entry|Prismacore|MissingMethod|trampoline" BepInEx/LogOutput.log
```

| # | Do this in game | Expect to see |
|---|---|---|
| 1 | Launch, open and close the Options menu | `OptionsMenuInjectionPatch.Prefix`, `OptionsMenuClosePatch.Postfix` |
| 2 | Start a new game (can abandon it) | `NewGameBlockPatch.Prefix` |
| 3 | Load a save | `LoadGamePatch.Prefix — trace set reset` |
| 4 | Open a treasure pod | `TreasurePodUIInteractablePatch.Prefix`, `TreasurePodActivatePatch.Prefix` |
| 5 | Craft anything at the Fabricator | `FabricatorPatch.Prefix`, `FabricatorDetailsPatch.Postfix`, `FabricatorItemCraftedPatch.Postfix` |
| 6 | Receive any upgrade | `UpgradeLevelTrackingPatch.Postfix`, `ActorUpgradeHandlerCachePatch.Postfix` |
| 7 | Feed a Gordo | `GordoPatch.Postfix`, `GordoFeedRequirementPatch.Postfix` |
| 8 | Approach a Grey Labyrinth shadow plort door | `ShadowPlortRequirementPatch.Postfix` |
| 9 | Press an EV/SS region gate switch | `RegionGateActivatePatch.Prefix` + `.Postfix` |
| 10 | Trigger any conversation that gives a gift | `ConversationPageGift*Patch.Prefix`, `ConversationRecordedPatch.Prefix` |
| 11 | Open a drone archive page (needs Archive Key) | `ResearchDroneArchivePatch.Postfix` |
| 12 | Open a Conservatory expansion terminal | `AccessDoorInteractPatch.Prefix`, `AccessDoorUITextPatch.Postfix` |
| 13 | Die | `PlayerDeathPatch.Prefix` |
| 14 | Start the Prismacore fight (`prisma_shard_hunt` only) | `PrismacoreGatePatch.Prefix` |
| 15 | Browse the Polestar shop | no `MissingMethodException` — regression guard for `_onSetSprite` |

Rows 5, 6 and 15 are the priority: the three `CallerCount` drifts this update
(`UpgradeModel.GetUpgradeLevel`, `FabricatorItemDetails.SetItem`,
`ShopItemDescriptionBlurb.UpdateDisplay`) all sit in the fabricator/shop/upgrade corner, and
that is also where the one confirmed break landed.

A hard crash on any row identifies its target directly — the trace immediately before the
crash is the call site, which is what `DebugTrace` exists for.

---

## Tier 3 — Silent identity drift (~300 keys)

Nothing crashes. The check simply never fires, the item never grants, and the seed may be
unbeatable. **This tier will not be caught by the tooling or by casual play** — it needs
targeted verification against the dumps.

### 3a. Position keys — 193 locations, ±0.5 unit tolerance

[WorldUtils.PositionKey](../Utils/WorldUtils.cs) builds `sceneName_roundX_roundY_roundZ`.
Any object nudged half a unit, or any scene renamed, silently orphans its location.

| Location type | Count |
|---|---|
| Treasure Pods | 113 |
| Plort Doors | 28 |
| Shadow Plort Doors | 25 |
| Map Data Nodes | 17 |
| Ghostly Drones | 10 |
| PB region gate | 1 (`RegionTable.PBGatePosKey`) |

**Validation:** F9 → Dumps → "Dump Locations", "Dump Puzzle Slot Lockables". Diff the
emitted posKeys against `Data/LocationTable.cs`. Worth scripting the diff given 193 rows —
this is the single biggest silent-failure surface and a level-design pass in the update
could move dozens at once. If the update ships new zones or reworks existing ones, assume
this is broken until proven otherwise.

### 3b. Asset-name keys — ~200 locations

| Key source | Locations | Where |
|---|---|---|
| `ShopRuntimeItem.AssetGuid` | 151 shop items | Addressables GUID — stable, but new/removed shop stock shifts the seed pool |
| `IdentifiableType.name` | 28 plort market + filler grants | e.g. `PinkPlort`, `JellystoneCraft` |
| `ResearchDroneEntry.name` / `.archivedEntry.name` | 25 + 21 drones | Archives already proved non-uniform (4 PB drones have none) |
| `PediaEntry` / `RadiantSlimePediaEntry.name` | 33 + 57 + 24 pedia | |
| `UpgradeDefinition.name` | 40 fabricator crafts + upgrade grants | `HealthCapacity`, `AmmoSlots`, … ([ItemHandler.cs](../Archipelago/ItemHandler.cs)) |
| `GadgetDefinition.name` | gadget grants | `TeleporterGrey`, `WarpDepot*`, … |
| `AccessDoor._id` | 5 expansion terminals | |

Plus hand-written magic strings that gate behaviour:

| String | Where | If it changes |
|---|---|---|
| `"Harmonizer"` | `PrismacoreFulfiller.cs:37` | Prismacore goal unwinnable |
| `"labySwitchBell"` | `PrismacoreFulfiller.cs:40` | Prismacore goal unwinnable |
| `"energyBeamReceiver"` ×2 scenes | `GoalHandler.cs:57` | `labyrinth_open` goal unwinnable |
| `"ShadowPlort"` | `ShadowPlortRequirementPatch.cs:47` | 25 shadow doors unsendable |
| `"node"` prefix | `TreasurePodPatch.cs:85` | 10 ghostly drones unsendable |
| `"Newbucks"` | `ItemHandler.cs:1567` | All currency grants fail |
| `"Tarr"`, `"Pink"` | `ItemHandler.cs:2301`, `:2407` | Trap items no-op |
| `objLabyrinthPlortDoor01Small` | `RegionTable.cs:81` | PB gate detection fails |
| `"Title"`/`"Desc"`/`"Body"` substrings | `AccessDoorPatch.cs:125` | Expansion terminal text not overridden (cosmetic) |

**Validation:** the goal-critical four (`Harmonizer`, `labySwitchBell`,
`energyBeamReceiver`, `ShadowPlort`) are each a one-line grep of a dump and should be
checked first — each one alone makes a goal unachievable. F9 → "Dump Prismacore State",
"Dump World Switches", "Dump Gadget Names".

### 3c. Private serialized fields — ~100 distinct

`._model`, `._id`, `._icon`, `._radiantShuffleBagSizes`, `._catchIdentifiableType`,
`._researchDroneEntry`, `._state`, `._depositors`, `._itemListings`, … Renaming a
serialized field is a routine refactor and produces a **compile error**, not a runtime one
— so the build catches these for free. Low priority.

### 3d. Enum members

`SwitchHandler.State.DOWN`, `CoreRoomController.CoreRoomState.POST_FIGHT`,
`LandPlot.Id.*` (7), `LandPlot.Upgrade.*` (21), `HideFlags.DontUnloadUnusedAsset`.

Renames are compile errors. **Renumbering is not** — and `LandPlot.Upgrade` is the
exposure: 21 members mapped by name to purchase assets in
[RanchPlotHandler.cs](../Archipelago/RanchPlotHandler.cs). If the update inserts a new
plot upgrade mid-enum, plot upgrade items could map to the wrong upgrade silently.

**Validation:** F9 → "Dump Land Plots" (open a plot UI first) and confirm the 21 upgrade
names still map to the same assets.

### 3e. Runtime type scans

~115 `Resources.FindObjectsOfTypeAll<T>()` call sites over ~50 game types. A removed or
renamed *type* is a compile error; the runtime risk is a type that still exists but is no
longer populated at the moment we scan. Covered implicitly by the Tier 2 smoke test.

---

## Suggested order of work

**Before the update:**
1. Make `PatchAll` per-type fault-tolerant (`Plugin.cs:72`). Highest value — turns a total
   outage into a named, partial one.
2. Commit the baseline (done: `tools/apicheck/`).
3. Consider scripting the posKey dump-vs-table diff for 3a, since 193 manual rows is
   otherwise the bottleneck.

**After the update, in order:**
1. Launch once so BepInEx regenerates `BepInEx/interop/`.
2. `python tools/apicheck/check-api.py` → fix every `GONE`, review every `CHANGED`.
3. `dotnet build -c Debug` → catches 3c and 3d renames for free.
4. Run the 12-row Tier 2 smoke test.
5. Dump-and-diff Tier 3a and 3b, goal-critical strings first.
6. Full goal playthrough on a short seed.

Steps 1–3 are mechanical and fast. Step 4 is an hour. Step 5 is where the real risk lives.

---

## What's new in the 2026-09-23 update

**A reliable type-level diff was not possible, and the numbers below should not be quoted
as counts.** The pre-update `Assembly-CSharp.dll` was deleted by BepInEx during
regeneration before it could be snapshotted, and the only other pre-update record —
the `apis/` folder — is provably incomplete: `apis/Il2CppMonomiPark.SlimeRancher.Analytics/`
is an **empty directory**, so ilspycmd created the folder and silently wrote zero files.
A naive diff against it reports 795 "new" types, including `Economy.CurrencyDefinition`
and `Labyrinth.DiscordantWall` — types the mod has always used. Restricting to namespaces
where the old decompile produced at least one file still leaves 257, with the same kind of
false positive, because the skipping happens per *file*, not per namespace.

**Going forward this is fixed:** `tools/apicheck/typelist-current.txt` is now committed, so
the next update has a trustworthy "before" snapshot. Capture it *before* launching the
game, since BepInEx wipes the interop directory on first launch after an update.

What can be said with moderate confidence, from clusters that appear as coherent whole
subsystems spanning UI + persistence + pedia (unlikely to be decompile artifacts across
that many files at once), cross-checked against how often each is referenced anywhere in
the 2910 old-build files:

| Candidate feature | Evidence | Verdict |
|---|---|---|
| **Music player** (`MusicPlayerMusicDefinition`, `MusicPediaCategory`, `PediaMusicPlayerScreen`, `MusicPlayerCategoryList`, `Persist.PlayerSelectedMusicV01`) | **0 references** across old build | Likely new |
| **Vac / colour palettes** (`PaletteShopItemHelper`, `PaletteSelectorUI`, `PediaPaletteCategoryScreen`, `Persist.PaletteInventoryV01`, `Ranch.VacPalette*`) | `PaletteShopItemHelper` 0 refs; `VacPalette` 1 ref | Likely new or heavily reworked |
| `RecipePinning.*` | **20 references** in old build | Pre-existing, not new |

Both candidates are cosmetic-customization features, and both touch the two systems where
the mod has the most locations — the **Pedia** (114 AP locations) and the **Shop** (151).

Assessed impact:

- **Pedia — safe, already hardened.** New categories will fire `PediaDirector.Unlock` for
  entries with no `LocationTable` row. `SlimepediaPatchHelper.SendCheckForEntry` logs at
  `Logger.Debug` and returns, so no spam and no misfire. The Slimepedia goal scopes to the
  AP location table rather than `PediaRuntimeCategory.AllUnlocked()`, so extra categories
  cannot make the goal unachievable — the earlier fix covers this case.
- **Shop — worth checking.** The 151 shop locations key on `ShopRuntimeItem.AssetGuid`.
  New palette/music stock would add catalog entries with no AP location. Confirm
  `ShopCatalogListPatch` still filters cleanly and that the shop UI renders, then decide
  whether the new stock is worth adding to the apworld's location pool.
- **Save format.** `Persist.PlayerSelectedMusicV01` / `PaletteInventoryV01` indicate a save
  schema addition. The mod patches `AutoSaveDirector` and keys its own state by
  `{seed}_{slotName}` outside the game save, so this should not interact — but exercise
  new-game / load / save-and-quit in the smoke test.

Neither candidate is confirmed. Both need an in-game look before anything is built on them.
