using Il2CppMonomiPark.SlimeRancher.UI;
using SlimeRancher2AP.Archipelago;
using TMPro;
using UnityEngine;

namespace SlimeRancher2AP.UI;

/// <summary>
/// Injects a goal-status label into the game's pause menu showing the active Archipelago
/// goal and, for the newbucks goal, live progress toward the target (which is otherwise
/// visible nowhere in-game).
///
/// <para>
/// Polling-based — no Harmony. The pause menu's bind/show methods are in the same
/// CallerCount(0)/native-called family that has crashed trampolines since the 5/13/2026
/// game update, so a throttled <see cref="Tick"/> instead looks for an active
/// <c>PauseMenuRoot</c> and creates/updates a TMP label parented under it. Parenting under
/// the root means the label appears and disappears with the menu automatically.
/// </para>
/// </summary>
public static class PauseMenuGoalDisplay
{
    private const string LabelName    = "APGoalLabel";
    private const int    PollInterval = 15; // ~4×/second at 60 fps
    private static int   _pollCounter;

    /// <summary>Called every frame from <c>ApUpdateBehaviour.Update</c>.</summary>
    internal static void Tick()
    {
        if (++_pollCounter < PollInterval) return;
        _pollCounter = 0;

        if (!Plugin.Instance.ModEnabled) return;

        PauseMenuRoot? activeRoot = null;
        try
        {
            var roots = Resources.FindObjectsOfTypeAll<PauseMenuRoot>();
            for (int i = 0; i < roots.Length; i++)
            {
                var r = roots[i];
                if (r != null && r.isActiveAndEnabled && r.gameObject.activeInHierarchy)
                {
                    activeRoot = r;
                    break;
                }
            }
        }
        catch { return; } // scene transition

        if (activeRoot == null) return; // menu closed — the label (a child) is hidden with it

        try
        {
            var text = BuildGoalText();

            // Locate (or create) our label as a direct child of the pause root.
            var existing = activeRoot.transform.Find(LabelName);
            var label    = existing != null ? existing.GetComponent<TextMeshProUGUI>() : null;

            if (text == null)
            {
                // No active AP session — keep the vanilla menu untouched.
                if (label != null) label.gameObject.SetActive(false);
                return;
            }

            if (label == null)
                label = CreateLabel(activeRoot);
            if (label == null) return;

            label.gameObject.SetActive(true);
            label.text = text;
        }
        catch { /* menu tearing down mid-tick — retry on next poll */ }
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

        // Match the menu's own typeface.
        var samples = root.GetComponentsInChildren<TMP_Text>(true);
        for (int i = 0; i < samples.Length; i++)
        {
            if (samples[i] != null && samples[i].font != null)
            {
                tmp.font = samples[i].font;
                break;
            }
        }

        Logger.Info("[AP] Pause menu goal label created.");
        return tmp;
    }
}
