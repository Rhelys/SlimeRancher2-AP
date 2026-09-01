using Il2CppMonomiPark.SlimeRancher.UI;
using SlimeRancher2AP.Archipelago;
using System.Linq;
using TMPro;
using UnityEngine;

namespace SlimeRancher2AP.UI;

/// <summary>
/// Injects a goal-status label into the game's pause menu showing the active Archipelago
/// goal and, for the newbucks goal, live progress toward the target (which is otherwise
/// visible nowhere in-game). For the plort_seller goal it additionally shows a panel on
/// the left side of the menu listing every in-scope plort type with its sold/target
/// progress.
///
/// <para>
/// Polling-based — no Harmony. The pause menu's bind/show methods are in the same
/// CallerCount(0)/native-called family that has crashed trampolines since the 5/13/2026
/// game update, so a throttled <see cref="Tick"/> instead looks for an active
/// <c>PauseMenuRoot</c> and creates/updates TMP labels parented under it. Parenting under
/// the root means the labels appear and disappear with the menu automatically.
/// </para>
/// </summary>
public static class PauseMenuGoalDisplay
{
    private const string LabelName    = "APGoalLabel";
    private const string PanelName    = "APPlortSalesPanel";

    /// <summary>
    /// Seconds between polls.  Time-based, not frame-counted: a frame counter makes the poll
    /// rate scale with the frame rate, so the work per second grows exactly when the machine
    /// is already busy — at 144 fps the old "every 15 frames" ran ~10×/second, not the ~4
    /// it was written for.
    /// </summary>
    private const float  PollSeconds = 0.25f;
    private static float _nextPoll;

    /// <summary>Seconds between root re-scans while no root has been cached yet.</summary>
    private const float  RescanSeconds = 2f;
    private static float _nextScan;

    /// <summary>
    /// Cached pause-menu root.  <c>PauseMenuRoot</c> exists in the loaded scene whether or not
    /// the menu is open — it is the *active* state that changes — so the expensive scan only
    /// needs to run once per scene, and each poll afterwards is a property read.
    ///
    /// This matters: <c>Resources.FindObjectsOfTypeAll</c> walks every loaded object and asset
    /// and marshals the matches across the IL2CPP boundary. Running it ~10×/second purely to
    /// discover that the pause menu is closed was the single most expensive thing the mod did
    /// during ordinary play.
    /// </summary>
    private static PauseMenuRoot? _cachedRoot;

    /// <summary>Drops the cached root so the next poll re-scans (scene load / teardown).</summary>
    public static void Reset() => _cachedRoot = null;

    /// <summary>Called every frame from <c>ApUpdateBehaviour.Update</c>.</summary>
    internal static void Tick()
    {
        float now = Time.unscaledTime;
        if (now < _nextPoll) return;
        _nextPoll = now + PollSeconds;

        if (!Plugin.Instance.ModEnabled) return;

        PauseMenuRoot? activeRoot = null;
        try
        {
            // Fast path: re-use the cached root. Unity-null means the scene changed under us.
            if (_cachedRoot != null)
            {
                activeRoot = _cachedRoot.isActiveAndEnabled && _cachedRoot.gameObject.activeInHierarchy
                    ? _cachedRoot
                    : null;
            }
            else if (now >= _nextScan || Time.timeScale == 0f)
            {
                // Only reached until a root has been cached. Rate-limited well below the poll
                // rate so a scene with no PauseMenuRoot yet cannot reintroduce a per-poll scan,
                // with an immediate scan whenever the game is paused (timeScale 0) so a root
                // created on first open is picked up without waiting out the interval.
                _nextScan = now + RescanSeconds;
                var roots = Resources.FindObjectsOfTypeAll<PauseMenuRoot>();
                PauseMenuRoot? sceneRoot = null;
                for (int i = 0; i < roots.Length; i++)
                {
                    var r = roots[i];
                    if (r == null) continue;

                    // FindObjectsOfTypeAll also returns prefabs and other loaded assets, which
                    // are never active in a hierarchy. Caching one permanently disables this
                    // display: the fast path above never re-scans once _cachedRoot is set, so
                    // the label can never appear again for the rest of the scene. Only a root
                    // that belongs to a loaded scene is the real menu.
                    if (!r.gameObject.scene.IsValid()) continue;

                    // Keep the first scene root as the fallback (inactive = menu simply closed),
                    // but keep looking for one that is actually active rather than stopping at
                    // the first hit.
                    sceneRoot ??= r;
                    if (r.isActiveAndEnabled && r.gameObject.activeInHierarchy)
                    {
                        sceneRoot  = r;
                        activeRoot = r;
                        break;
                    }
                }
                if (sceneRoot != null) _cachedRoot = sceneRoot;
#if DEBUG
                // Records what the scan actually saw, so a future "label never appears" report
                // can be settled from the log instead of another instrumented build.
                int inScene = 0;
                for (int i = 0; i < roots.Length; i++)
                    if (roots[i] != null && roots[i].gameObject.scene.IsValid()) inScene++;
                Utils.DebugTrace.Once(
                    $"PauseMenu — scan: {roots.Length} root(s), {inScene} in a scene, "
                    + $"cached={sceneRoot != null}, active={activeRoot != null}");
#endif
            }
        }
        catch { _cachedRoot = null; return; } // scene transition — re-scan next poll

        if (activeRoot == null)
        {
#if DEBUG
            // timeScale 0 means the game is paused, so a root SHOULD be active. If this fires,
            // the cached root is the wrong object (FindObjectsOfTypeAll also returns prefabs).
            if (Time.timeScale == 0f)
                Utils.DebugTrace.Once($"PauseMenu — paused but no active root (cached={_cachedRoot != null})");
#endif
            return; // menu closed — the labels (children) hide with it
        }

        try
        {
            var goalText = BuildGoalText();
#if DEBUG
            Utils.DebugTrace.Once($"PauseMenu — goal text: {goalText ?? "<null>"}");
#endif
            UpdateInjectedLabel(activeRoot, LabelName, goalText,              CreateLabel);
            UpdateInjectedLabel(activeRoot, PanelName, BuildPlortPanelText(), CreatePlortPanel);
        }
        catch (System.Exception ex)
        {
            // Was silent, which hid real failures — the menu genuinely can tear down mid-tick,
            // so this stays non-fatal, but it must not disappear without a trace.
            Logger.Warning($"[AP] PauseMenuGoalDisplay tick failed: {ex.Message}");
        }
    }

    /// <summary>
    /// Shows/hides/updates one injected TMP element under the pause root.
    /// <paramref name="text"/> null means "not applicable right now" — the element (if it
    /// exists) is deactivated so the vanilla menu is untouched.
    /// </summary>
    private static void UpdateInjectedLabel(PauseMenuRoot root, string name, string? text,
                                            System.Func<PauseMenuRoot, TextMeshProUGUI?> create)
    {
        var existing = root.transform.Find(name);
        var label    = existing != null ? existing.GetComponent<TextMeshProUGUI>() : null;

        if (text == null)
        {
            if (label != null) label.gameObject.SetActive(false);
            return;
        }

        bool creating = label == null;
        label ??= create(root);
        if (label == null)
        {
#if DEBUG
            Utils.DebugTrace.Once($"PauseMenu — create failed for '{name}'");
#endif
            return;
        }
#if DEBUG
        if (creating) Utils.DebugTrace.Once($"PauseMenu — created '{name}'");
#endif

        label.gameObject.SetActive(true);
        label.text = text;
    }

    /// <summary>
    /// Builds the label text, or null when no AP session is active.
    /// ASCII only — the game's HemispheresCaps2 font has no glyphs for ★/→/… .
    /// </summary>
    private static string? BuildGoalText()
    {
        if (!Plugin.Instance.SaveManager.HasActiveSession) return null;
        var slotData = Plugin.Instance.ApClient?.SlotData;
        if (slotData == null) return null;

        string body;
        switch (slotData.Goal)
        {
            case "labyrinth_open":
                body = "OPEN THE GREY LABYRINTH";
                break;
            case "newbucks":
            {
                long earned = Plugin.Instance.SaveManager.NewbucksEarned;
                long target = slotData.NewbucksGoalAmount;
                long pct    = target > 0 ? System.Math.Min(100, earned * 100 / target) : 0;
                body = $"EARN NEWBUCKS - {earned:N0} / {target:N0} ({pct}%)";
                break;
            }
            case "prismacore":
                body = "STABILIZE THE PRISMACORE";
                break;
            case "prisma_shard_hunt":
            {
                int have = Archipelago.PrismaShardHandler.Collected;
                int need = Archipelago.PrismaShardHandler.Required;
                body = have >= need
                    ? $"STABILIZE THE PRISMACORE - SHARDS {have}/{need} (UNLOCKED)"
                    : $"COLLECT PRISMA SHARDS - {have}/{need}";
                break;
            }
            case "plort_seller":
            {
                // Per-type progress display is a future bespoke panel; for now show the
                // per-type target and how many types have reached it.
                int target = slotData.PlortGoalAmount;
                var (done, total) = GoalHandler.PlortSellerProgress();
                body = $"SELL {target} OF EACH PLORT - {done} / {total} TYPES DONE";
                break;
            }
            case "slimepedia":
            {
                // Which categories count toward the goal is option-driven — show them so
                // the player knows what "complete" means for their seed.
                var categories = new System.Collections.Generic.List<string>(3);
                if (slotData.RandomizeSlimepedia)          categories.Add("SLIMES");
                if (slotData.RandomizeSlimepediaResources) categories.Add("RESOURCES");
                if (slotData.RandomizeSlimepediaRadiant)   categories.Add("RADIANT");
                body = categories.Count > 0
                    ? $"COMPLETE THE SLIMEPEDIA ({string.Join(", ", categories)})"
                    : "COMPLETE THE SLIMEPEDIA";
                break;
            }
            default:
                body = slotData.Goal.ToUpperInvariant();
                break;
        }

        if (GoalHandler.IsGoalComplete)
            body += "  -  COMPLETE!";

        return $"ARCHIPELAGO GOAL: {body}";
    }

    /// <summary>
    /// Builds the left-side plort sales panel text, or null when it should be hidden
    /// (goal is not plort_seller, or no active session). One line per in-scope plort
    /// type: sold/target, green once the target is reached. ASCII only (font glyphs),
    /// rich-text color tags are fine.
    /// </summary>
    private static string? BuildPlortPanelText()
    {
        if (!Plugin.Instance.SaveManager.HasActiveSession) return null;
        var slotData = Plugin.Instance.ApClient?.SlotData;
        if (slotData == null || slotData.Goal != "plort_seller") return null;

        int target = slotData.PlortGoalAmount;

        // Alphabetical by display name — the types-complete summary lives in the goal
        // label at the bottom of the menu, so no header here.
        var rows = GoalHandler.PlortSellerScope()
            .Select(n => (Name: n, Display: PlortDisplayName(n)))
            .OrderBy(r => r.Display, System.StringComparer.Ordinal)
            .ToList();

        var sb = new System.Text.StringBuilder(32 * (rows.Count + 1));
        foreach (var (plortName, display) in rows)
        {
            long sold    = Plugin.Instance.SaveManager.PlortsSold(plortName);
            bool reached = sold >= target;
            if (reached) sb.Append("<color=#8CE68C>");
            sb.Append($"{display}  {sold} / {target}");
            if (reached) sb.Append("</color>");
            sb.Append('\n');
        }
        return sb.ToString();
    }

    /// <summary>"PinkPlort" → "PINK"; the Prisma plort's asset name is "StablePlort".</summary>
    private static string PlortDisplayName(string identName)
    {
        var trimmed = identName.EndsWith("Plort") ? identName[..^5] : identName;
        if (trimmed == "Stable") trimmed = "Prisma";
        return trimmed.ToUpperInvariant();
    }

    /// <summary>
    /// Creates the TMP label as a direct child of the pause menu root, anchored to the
    /// bottom-center so it sits below the button stack. Uses the menu's own font (sampled
    /// from an existing TMP label) so it matches the native style.
    /// </summary>
    private static TextMeshProUGUI? CreateLabel(PauseMenuRoot root)
    {
        var go = new GameObject(LabelName);
        go.transform.SetParent(root.transform, false);

        var rt = go.AddComponent<RectTransform>();
        rt.anchorMin        = new Vector2(0.5f, 0f);
        rt.anchorMax        = new Vector2(0.5f, 0f);
        rt.pivot            = new Vector2(0.5f, 0f);
        rt.anchoredPosition = new Vector2(0f, 40f);
        rt.sizeDelta        = new Vector2(1100f, 50f);

        var tmp = go.AddComponent<TextMeshProUGUI>();
        tmp.fontSize          = 28f;
        tmp.enableAutoSizing  = true;   // long slot names / big numbers shrink to fit
        tmp.fontSizeMax       = 28f;
        tmp.fontSizeMin       = 14f;
        tmp.alignment         = TextAlignmentOptions.Center;
        tmp.color             = new Color(0.96f, 0.93f, 0.82f); // cream, matching the day/time banner
        tmp.enableWordWrapping = false;
        tmp.raycastTarget     = false;  // never block clicks on the menu buttons

        ApplyMenuFont(root, tmp);

        Logger.Info("[AP] Pause menu goal label created.");
        return tmp;
    }

    /// <summary>
    /// Creates the plort sales panel as a direct child of the pause menu root, anchored to
    /// the left edge at mid-height so it sits beside (not behind) the centered button stack.
    /// </summary>
    private static TextMeshProUGUI? CreatePlortPanel(PauseMenuRoot root)
    {
        var go = new GameObject(PanelName);
        go.transform.SetParent(root.transform, false);

        var rt = go.AddComponent<RectTransform>();
        rt.anchorMin        = new Vector2(0f, 0.5f);
        rt.anchorMax        = new Vector2(0f, 0.5f);
        rt.pivot            = new Vector2(0f, 0.5f);
        rt.anchoredPosition = new Vector2(50f, 0f);
        rt.sizeDelta        = new Vector2(430f, 940f);

        var tmp = go.AddComponent<TextMeshProUGUI>();
        tmp.fontSize          = 22f;
        tmp.enableAutoSizing  = true;   // 21-25 rows shrink to fit the fixed panel height
        tmp.fontSizeMax       = 22f;
        tmp.fontSizeMin       = 10f;
        tmp.alignment         = TextAlignmentOptions.MidlineLeft;
        tmp.color             = new Color(0.96f, 0.93f, 0.82f); // cream, matching the goal label
        tmp.enableWordWrapping = false;
        tmp.richText          = true;   // per-line green highlight for completed types
        tmp.raycastTarget     = false;  // never block clicks on the menu buttons

        ApplyMenuFont(root, tmp);

        Logger.Info("[AP] Pause menu plort sales panel created.");
        return tmp;
    }

    /// <summary>Matches the menu's own typeface (sampled from an existing TMP label).</summary>
    private static void ApplyMenuFont(PauseMenuRoot root, TextMeshProUGUI tmp)
    {
        var samples = root.GetComponentsInChildren<TMP_Text>(true);
        for (int i = 0; i < samples.Length; i++)
        {
            if (samples[i] != null && samples[i].font != null)
            {
                tmp.font = samples[i].font;
                break;
            }
        }
    }
}
