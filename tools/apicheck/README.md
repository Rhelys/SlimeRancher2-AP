# apicheck — post-update API drift detection

`check-api.py` re-derives the current signature and `CallerCount` of every game method the
mod Harmony-patches, straight from `BepInEx/interop/Assembly-CSharp.dll`, and diffs the
result against the committed `baseline.tsv`.

It exists because the 5/13/2026 game update silently changed
`RadiantSlimeDirector.DrawFromRadiantShuffleBag`'s signature and several native prologues,
which only surfaced as runtime trampoline crashes. This catches the signature class of
breakage before the game is ever launched with the mod.

## Usage

After a game update, launch the game once so BepInEx regenerates the interop DLLs, then:

```bash
python tools/apicheck/check-api.py
```

Exit code 1 if anything changed. Output has two sections:

- **GONE** — the method no longer exists. `Harmony.PatchAll` will throw and *every* patch
  in the mod will fail to apply. Must be fixed before the mod loads at all.
- **CHANGED** — signature or `CallerCount` moved. Re-verify that patch; a changed parameter
  list means the Prefix/Postfix parameter names no longer bind, and a `CallerCount` drop to
  0 raises the odds of a trampoline crash.

Once the drift is understood and the patches are updated, accept the new state:

```bash
python tools/apicheck/check-api.py --write-baseline
```

## Files

| File | Purpose |
|---|---|
| `targets.txt` | `ShortTypeName|MethodName|source location` — one row per `[HarmonyPatch]` site |
| `baseline.tsv` | Last accepted signature + `CallerCount` per target |
| `check-api.py` | Regenerate and diff |

`targets.txt` is maintained by hand. When you add or remove a Harmony patch, add or remove
its row, then `--write-baseline`. To re-derive the list from scratch:

```bash
grep -rn "HarmonyPatch" --include="*.cs" Patches/
```

The interop DLL path defaults to the one in `Directory.Build.props`; override with the
`SR2_INTEROP_DLL` environment variable.

## Caveats

- Only covers **Harmony patch targets**. It says nothing about asset names, position keys,
  or enum values — see `docs/game-update-checklist.md` Tier 3 for those.
- `CallerCount(0)` is a risk correlate, not a prediction. Many CC=0 patches work fine.
- Explicit interface implementations appear under their mangled names (e.g.
  `MonomiPark_SlimeRancher_UI_Fabricator_IFabricatableItem_FabricateAndSpendCost`); that
  mangled name is itself update-fragile.

## typelist-current.txt

A sorted list of every type in the current interop `Assembly-CSharp.dll`, so the *next*
update has a trustworthy "before" snapshot to diff against.

**Capture it before launching the game after an update.** BepInEx wipes and regenerates
`BepInEx/interop/` on the first launch, which destroys the previous build's DLLs. In the
2026-09-23 update that happened before a snapshot was taken, and the `apis/` folder was not
a usable fallback: ilspycmd had silently written zero files for several namespaces, so a
diff against it produced hundreds of false "new type" hits.

```bash
ilspycmd "<interop>/Assembly-CSharp.dll" -l c \
  | grep -v "not using\|Latest version" | sed 's/^Class //' | sort -u \
  > tools/apicheck/typelist-current.txt
```
