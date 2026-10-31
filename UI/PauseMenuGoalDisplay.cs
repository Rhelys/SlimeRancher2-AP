using Il2CppMonomiPark.SlimeRancher.UI;
using SlimeRancher2AP.Archipelago;
using System.Linq;
using TMPro;
using UnityEngine;

namespace SlimeRancher2AP.UI;

/// <summary>
/// Adds the AP goal (with progress) to the pause menu, plus a per-plort sales panel for
/// plort_seller. Polled, not patched — the menu's show methods can't be patched since 5/13.
/// The labels are children of <c>PauseMenuRoot</c>, so they show and hide with it.
/// </summary>
public static class PauseMenuGoalDisplay
{
    private const string LabelName    = "APGoalLabel";
    private const string PanelName    = "APPlortSalesPanel";

    /// <summary>Seconds between polls — by time, so the rate doesn't scale with frame rate.</summary>
    private const float  PollSeconds = 0.25f;
    private static float _nextPoll;

    /// <summary>Seconds between root re-scans while no root has been cached yet.</summary>
    private const float  RescanSeconds = 2f;
    private static float _nextScan;

    /// <summary>
    /// The scene's pause root, which exists whether or not the menu is open — so the costly
    /// FindObjectsOfTypeAll scan runs once per scene, not every poll.
    /// </summary>
    private static PauseMenuRoot? _cachedRoot;

    /// <summary>Drops the cached root (scene change).</summary>
    public static void Reset() => _cachedRoot = null;

    internal static void Tick()
    {
        float now = Time.unscaledTime;
        if (now < _nextPoll) return;
        _nextPoll = now + PollSeconds;

        if (!Plugin.Instance.ModEnabled) return;

        PauseMenuRoot? activeRoot = null;
        try
        {
            if (_cachedRoot != null)
            {
                activeRoot = _cachedRoot.isActiveAndEnabled && _cachedRoot.gameObject.activeInHierarchy
                    ? _cachedRoot
                    : null;
            }
            else if (now >= _nextScan || Time.timeScale == 0f)
            {
                // Until cached: scan rarely, but at once while paused.
                _nextScan = now + RescanSeconds;
                var roots = Resources.FindObjectsOfTypeAll<PauseMenuRoot>();
                PauseMenuRoot? sceneRoot = null;
                for (int i = 0; i < roots.Length; i++)
                {
                    var r = roots[i];
                    if (r == null) continue;

                    // Never cache a prefab — the label would never appear for the whole scene.
                    if (!r.gameObject.scene.IsValid()) continue;

                    // Prefer an active root; any scene root will do.
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
                int inScene = 0;
                for (int i = 0; i < roots.Length; i++)
                    if (roots[i] != null && roots[i].gameObject.scene.IsValid()) inScene++;
                Utils.DebugTrace.Once(
                    $"PauseMenu — scan: {roots.Length} root(s), {inScene} in a scene, "
                    + $"cached={sceneRoot != null}, active={activeRoot != null}");
#endif
            }
        }
        catch { _cachedRoot = null; return; } // scene transition

        if (activeRoot == null)
        {
#if DEBUG
            // Paused with no active root means the wrong root is cached.
            if (Time.timeScale == 0f)
                Utils.DebugTrace.Once($"PauseMenu — paused but no active root (cached={_cachedRoot != null})");
#endif
            return; // menu closed
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
            Logger.Warning($"[AP] PauseMenuGoalDisplay tick failed: {ex.Message}");
        }
    }

    /// <summary>Creates or updates one label; null <paramref name="text"/> hides it.</summary>
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

    /// <summary>The goal line, or null without a session. ASCII only — the font lacks ★/→/….</summary>
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
                int target = slotData.PlortGoalAmount;
                var (done, total) = GoalHandler.PlortSellerProgress();
                body = $"SELL {target} OF EACH PLORT - {done} / {total} TYPES DONE";
                break;
            }
            case "slimepedia":
            {
                // List the categories this seed counts.
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

    /// <summary>One sold/target line per plort (green when reached), or null if not plort_seller.</summary>
    private static string? BuildPlortPanelText()
    {
        if (!Plugin.Instance.SaveManager.HasActiveSession) return null;
        var slotData = Plugin.Instance.ApClient?.SlotData;
        if (slotData == null || slotData.Goal != "plort_seller") return null;

        int target = slotData.PlortGoalAmount;

        // Alphabetical; the summary is in the goal label.
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

    /// <summary>The goal label, bottom-centre below the buttons, in the menu's font.</summary>
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

    /// <summary>The plort panel, left edge, beside the buttons.</summary>
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
