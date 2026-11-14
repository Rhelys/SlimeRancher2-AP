#!/usr/bin/env python3
"""
Post-game-update API drift check for the SR2-AP mod.

Re-decompiles every type the mod Harmony-patches from the CURRENT interop DLL and
diffs each target's signature + CallerCount against tools/apicheck/baseline.tsv.

Run this AFTER a game update, once BepInEx has regenerated BepInEx/interop/
(i.e. after launching the game once on the new build).

    python tools/apicheck/check-api.py                  # diff against baseline
    python tools/apicheck/check-api.py --write-baseline # accept current as new baseline

Exit code 1 if any target changed or disappeared.
"""
import argparse, os, re, subprocess, sys

ROOT = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
HERE = os.path.join(ROOT, "tools", "apicheck")
DLL = os.environ.get(
    "SR2_INTEROP_DLL",
    r"E:/SteamLibrary/steamapps/common/Slime Rancher 2/BepInEx/interop/Assembly-CSharp.dll")

DECL = re.compile(
    r"\[CallerCount\((-?\d+)\)\]\s*\n"
    r"(?:\s*\[CachedScanResults\([^\]]*\)\]\s*\n)?"
    r"\s*((?:public|private|protected|internal|unsafe|virtual|override|static|sealed|new|extern|\s)+"
    r"[^\n;{]*?)\s*\n?\s*\{", re.M)


def check_interop_fresh(allow_stale=False):
    """
    Refuse to run against an interop DLL older than the game assembly it is meant to
    mirror. BepInEx only regenerates BepInEx/interop/ when the game is launched, so
    immediately after an update the DLL on disk still describes the PREVIOUS build --
    and every target would compare equal, reporting a false all-clear.

    This is not hypothetical: it is exactly what this script was about to do on
    2026-09-23 before the game had been relaunched.
    """
    game_dir = os.path.dirname(os.path.dirname(os.path.dirname(DLL)))
    game_asm = os.path.join(game_dir, "GameAssembly.dll")
    if not os.path.exists(game_asm):
        print(f"note: GameAssembly.dll not found next to interop; skipping freshness check")
        return

    interop_mtime, game_mtime = os.path.getmtime(DLL), os.path.getmtime(game_asm)
    if interop_mtime >= game_mtime:
        return

    import datetime as _dt
    fmt = lambda t: _dt.datetime.fromtimestamp(t).strftime("%Y-%m-%d %H:%M")
    msg = (f"\nSTALE INTEROP - results would be meaningless.\n"
           f"  GameAssembly.dll : {fmt(game_mtime)}\n"
           f"  interop DLL      : {fmt(interop_mtime)}   <-- older\n\n"
           f"The interop DLL still describes the PREVIOUS game build, so every target\n"
           f"would compare equal and this script would report a false all-clear.\n"
           f"Launch the game once so BepInEx regenerates BepInEx/interop/, then re-run.\n")
    if allow_stale:
        print(msg + "  (--allow-stale given; continuing anyway)\n")
        return
    sys.exit(msg)


def ilspy(*args):
    out = subprocess.run(["ilspycmd", DLL, *args], capture_output=True, text=True,
                         encoding="utf-8", errors="replace")
    return "\n".join(l for l in (out.stdout or "").splitlines()
                     if "not using" not in l and "Latest version" not in l)


def probe(source, method):
    """
    Return [(callercount, signature), ...] for EVERY overload of `method`, or [].

    All overloads matter: the mod patches two `PediaDirector.Unlock` overloads by
    explicit argument-type array, and Harmony resolves those at runtime -- the C#
    compiler cannot catch a missing overload, so a checker that recorded only the
    first would miss exactly the failure it exists to catch.
    """
    hits = []
    for m in DECL.finditer(source):
        sig = " ".join(m.group(2).split())
        if re.search(r"[\s.]" + re.escape(method) + r"\s*\(", " " + sig):
            hits.append((m.group(1), sig))
    if hits:
        return hits
    if method.startswith("get_"):
        prop = method[4:]
        m = re.search(r"public unsafe ([\w.<>\[\]]+) " + re.escape(prop) + r"\s*\n", source)
        if m:
            return [("n/a (property getter)", f"public {m.group(1)} {prop} {{ get; }}")]
    return []


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--write-baseline", action="store_true")
    ap.add_argument("--allow-stale", action="store_true",
                    help="run even if the interop DLL predates GameAssembly.dll")
    args = ap.parse_args()

    if not os.path.exists(DLL):
        sys.exit(f"interop DLL not found: {DLL}\n"
                 "Launch the game once after updating so BepInEx regenerates it, "
                 "or set SR2_INTEROP_DLL.")

    check_interop_fresh(args.allow_stale)

    targets = [l.strip().split("|") for l in
               open(os.path.join(HERE, "targets.txt"), encoding="utf-8") if l.strip()]

    print(f"Resolving {len(targets)} patch targets against {DLL} ...")
    alltypes = ilspy("-l", "c")
    shorts = sorted({t[0] for t in targets})
    full = {}
    for s in shorts:
        m = re.search(r"^Class (.*[.]%s|%s)$" % (re.escape(s), re.escape(s)), alltypes, re.M)
        full[s] = m.group(1) if m else None

    sources = {}
    for s in shorts:
        if full[s]:
            sources[s] = ilspy("-t", full[s])

    # One entry per OVERLOAD. Key is the source location, suffixed "#n" when a target
    # has several overloads (sorted by signature so the ordinal is stable run to run).
    current, keys = {}, []

    def add(key, row):
        current[key] = row
        keys.append(key)

    for short, method, loc in targets:
        if not full[short]:
            add(loc, (short, "TYPE MISSING", "TYPE MISSING", "—"))
            continue
        hits = probe(sources[short], method)
        if not hits:
            add(loc, (full[short], method, "METHOD MISSING", "—"))
        elif len(hits) == 1:
            add(loc, (full[short], method, hits[0][0], hits[0][1]))
        else:
            for i, (cc, sig) in enumerate(sorted(hits, key=lambda h: h[1])):
                add(f"{loc}#{i}", (full[short], method, cc, sig))

    if args.write_baseline:
        with open(os.path.join(HERE, "baseline.tsv"), "w", encoding="utf-8") as f:
            for key in keys:
                t, m, cc, sig = current[key]
                f.write(f"{key}\t{t}\t{m}\t{cc}\t{sig}\n")
        print(f"Wrote new baseline for {len(keys)} overloads across {len(targets)} targets.")
        return 0

    base = {}
    for line in open(os.path.join(HERE, "baseline.tsv"), encoding="utf-8"):
        p = line.rstrip("\n").split("\t")
        if len(p) == 5:
            base[p[0]] = tuple(p[1:])

    broken, changed, ok = [], [], 0
    for loc in keys:
        cur, old = current[loc], base.get(loc)
        if old is None:
            changed.append((loc, "(not in baseline)", " ".join(cur)))
        elif "MISSING" in cur[2]:
            broken.append((loc, cur))
        elif cur[3] != old[3]:
            changed.append((loc, f"SIG was: {old[3]}", f"SIG now: {cur[3]}"))
        elif cur[2] != old[2]:
            changed.append((loc, f"CallerCount {old[2]} -> {cur[2]}", cur[3]))
        else:
            ok += 1

    print()
    if broken:
        print("=" * 70)
        print(f"GONE - patch will throw at PatchAll ({len(broken)}):")
        for loc, cur in broken:
            print(f"  {loc}\n      {cur[0]}.{cur[1]}  [{cur[2]}]")
    if changed:
        print("=" * 70)
        print(f"CHANGED - re-verify these patches ({len(changed)}):")
        for loc, a, b in changed:
            print(f"  {loc}\n      {a}\n      {b}")
    print("=" * 70)
    print(f"unchanged: {ok}   changed: {len(changed)}   gone: {len(broken)}")
    return 1 if (broken or changed) else 0


if __name__ == "__main__":
    sys.exit(main())
