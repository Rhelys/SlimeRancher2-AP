using HarmonyLib;
using Il2CppMonomiPark.SlimeRancher.Dialogue.CommStation;
using Il2CppMonomiPark.SlimeRancher.UI.CommStation;
using SlimeRancher2AP.Data;
using UnityEngine;

namespace SlimeRancher2AP.Patches.LocationPatches;

// ---------------------------------------------------------------------------
// Active conversation tracker
// ---------------------------------------------------------------------------

/// <summary>
/// Tracks which <see cref="FixedConversation"/> is currently being displayed so that
/// the gift-page patches below know whether to suppress the in-game grant.
///
/// Hook: <c>ConversationViewHolder.ShowConversation(IConversation, bool)</c> fires
/// whenever a new conversation begins playing. The debug name is extracted and stored
/// in <see cref="ActiveConversationDebugName"/>. All conversation UI and page callbacks
/// run on the main thread, so this static field is safe to read without locking.
/// </summary>
[HarmonyPatch(typeof(ConversationViewHolder), nameof(ConversationViewHolder.ShowConversation))]
internal static class ConversationActiveTrackerPatch
{
    /// <summary>
    /// Debug name (<c>AbstractConversation.GetDebugName()</c>) of the conversation
    /// currently open in the CommStation UI, or <see langword="null"/> if none.
    /// Updated each time <c>ShowConversation</c> fires.
    /// </summary>
    internal static string? ActiveConversationDebugName { get; private set; }

    private static void Postfix(IConversation conversation)
    {
        if (conversation == null)
        {
            ActiveConversationDebugName = null;
            return;
        }

        var fixedConv = conversation.TryCast<FixedConversation>();
        ActiveConversationDebugName = fixedConv?.GetDebugName();

        Logger.Info(
            $"[AP-Conv] Conversation started: '{ActiveConversationDebugName}'" +
            $"  (cast {(fixedConv == null ? "FAILED — not FixedConversation" : "ok")}");
    }

    // ── Latch release ─────────────────────────────────────────────────────────
    // ActiveConversationDebugName MUST be cleared once the CommStation UI closes.
    // If it stays latched, ShouldSuppressGift() keeps returning true for the rest of
    // the session, and the gift-page patches keep intervening long after any
    // conversation is on screen. That permanently broke the pause menu (every item
    // except Screenshot vanished) — see ConversationPageGiftBlueprintCalloutPatch.
    //
    // ConversationViewHolder.EndConversation is CallerCount(0) (unsafe to patch since
    // the 5/13/2026 update), so the close is detected by polling: while a name is
    // latched, check ~2×/second whether any ConversationViewHolder is still active.

    private const int UiPollInterval = 30; // ~0.5 s at 60 fps
    private static int _uiPollCounter;

    /// <summary>Called every frame from <c>ApUpdateBehaviour.Update</c>. Cheap: no-ops unless a conversation name is latched.</summary>
    internal static void Tick()
    {
        if (ActiveConversationDebugName == null) return;
        if (++_uiPollCounter < UiPollInterval) return;
        _uiPollCounter = 0;

        try
        {
            var holders = UnityEngine.Resources.FindObjectsOfTypeAll<ConversationViewHolder>();
            for (int i = 0; i < holders.Length; i++)
            {
                var h = holders[i];
                if (h != null && h.isActiveAndEnabled) return; // conversation UI still open
            }
        }
        catch { return; } // scene transition — retry next interval

        Logger.Info(
            $"[AP-Conv] Conversation UI closed — clearing active conversation '{ActiveConversationDebugName}'");
        ActiveConversationDebugName = null;
        // Safety net for conversations exited early (no RecordPlayed): kill any award
        // tweens still running against the (about-to-be / already) torn-down UI.
        KillConversationTweens("UI closed");
    }

    /// <summary>
    /// Kills any DOTween tweens targeting transforms in the CommStation conversation UI.
    /// The gift-page award animation (a Sequence with DOShakePosition) is started natively
    /// when the callout displays; when the player closes the conversation within the shake's
    /// duration — typical for the final gift page — the UI teardown destroys the tween's
    /// target Transform mid-animation and DOTween spams "Target or field is missing/null"
    /// warnings until its safe mode reaps the tween. Killing by target before teardown
    /// prevents the spam. complete=false: the UI is closing, no need to snap to end state.
    /// </summary>
    internal static void KillConversationTweens(string reason)
    {
        try
        {
            var holders = UnityEngine.Resources.FindObjectsOfTypeAll<ConversationViewHolder>();
            int killed = 0;
            for (int i = 0; i < holders.Length; i++)
            {
                var h = holders[i];
                if (h == null) continue;
                var transforms = h.GetComponentsInChildren<UnityEngine.Transform>(true);
                for (int t = 0; t < transforms.Length; t++)
                {
                    var tr = transforms[t];
                    if (tr == null) continue;
                    killed += DG.Tweening.DOTween.Kill(tr, false);
                }
            }
            // Phase 2: the gift-page award shake is a Sequence whose target is NULL from
            // creation (confirmed via the tween dump — the shake references a presentation
            // object that the suppressed grant never instantiated), so the kill-by-target
            // pass above can never reach it. Reap any playing top-level tween with a null
            // target: at conversation end such a tween can only ever spam null-target
            // warnings — there is nothing valid left for it to animate.
            int reaped = 0;
            var playing = DG.Tweening.DOTween.PlayingTweens();
            if (playing != null)
            {
                for (int i = 0; i < playing.Count; i++)
                {
                    var tw = playing[i];
                    if (tw == null) continue;

                    Il2CppSystem.Object? target = null;
                    try { target = tw.target; } catch { /* treat unreadable as null */ }
                    if (target != null) continue;

                    try
                    {
                        DG.Tweening.TweenExtensions.Kill(tw, false);
                        reaped++;
                    }
                    catch { /* already reaped by DOTween safe mode */ }
                }
            }

            if (killed > 0 || reaped > 0)
                Logger.Info(
                    $"[AP-Conv] Conversation tween cleanup ({reason}): {killed} by target, {reaped} null-target.");
#if DEBUG
            else
                DumpPlayingTweens(reason);
#endif
        }
        catch { /* DOTween not initialised or scene tearing down — nothing to kill */ }
    }

#if DEBUG
    /// <summary>
    /// Diagnostic: the null-target DOShakePosition warning comes from a tween whose target is
    /// NOT under the conversation UI hierarchy (the kill above finds nothing). Dump every
    /// playing tween's target so the culprit can be identified from the log.
    /// </summary>
    private static void DumpPlayingTweens(string reason)
    {
        try
        {
            var playing = DG.Tweening.DOTween.PlayingTweens();
            if (playing == null || playing.Count == 0)
            {
                Logger.Info($"[AP-Conv] Tween dump ({reason}): no playing tweens.");
                return;
            }

            for (int i = 0; i < playing.Count; i++)
            {
                string desc;
                try
                {
                    var tw = playing[i];
                    var target = tw?.target;
                    if (target == null) { desc = "target=NULL"; }
                    else
                    {
                        var uo = target.TryCast<UnityEngine.Object>();
                        desc = uo != null
                            ? $"target={uo.GetIl2CppType().Name} '{(uo == null ? "?" : uo.name)}'"
                            : $"target(non-Unity)={target.GetIl2CppType().FullName}";
                    }
                }
                catch (System.Exception ex) { desc = $"target=<unreadable: {ex.GetType().Name}>"; }
                Logger.Info($"[AP-Conv] Tween dump ({reason}) [{i}]: {desc}");
            }
        }
        catch (System.Exception ex)
        {
            Logger.Info($"[AP-Conv] Tween dump failed: {ex.Message}");
        }
    }
#endif

    /// <summary>
    /// Returns <see langword="true"/> when the currently-active conversation is an AP
    /// location check under the current <see cref="ConversationCheckMode"/>.
    /// When <see langword="true"/>, gift-page <c>ApplyChanges()</c> should be suppressed
    /// so the AP server delivers the randomized item instead of the hardcoded in-game gift.
    /// </summary>
    internal static bool ShouldSuppressGift()
    {
        if (Plugin.Instance == null || !Plugin.Instance.ModEnabled) return false;

        var mode = Plugin.Instance.ApClient?.SlotData?.ConversationChecks
                   ?? ConversationCheckMode.Off;
        if (mode == ConversationCheckMode.Off) return false;

        var debugName = ActiveConversationDebugName;
        if (string.IsNullOrEmpty(debugName)) return false;

        if (!LocationTable.TryGetByConversation(debugName, out var loc) || loc is null)
            return false;

        return LocationTable.IsConversationIncluded(loc.Type, mode);
    }
}

// ---------------------------------------------------------------------------
// Gift blueprint suppression
// ---------------------------------------------------------------------------

/// <summary>
/// Intercepts CommStation conversations that gift a gadget blueprint.
///
/// <list type="bullet">
///   <item>When the active conversation is an AP location:
///     <c>Prefix</c> returns <see langword="false"/>, blocking <c>ApplyChanges()</c>
///     so the blueprint is NOT granted. The AP server delivers the randomized item
///     via <see cref="SlimeRancher2AP.Archipelago.ItemHandler"/> instead.</item>
///   <item>Otherwise the original runs normally and <c>Postfix</c> logs the grant.</item>
/// </list>
///
/// Note: <c>ConversationRecordedPatch</c> is responsible for sending the actual AP check —
/// this patch only suppresses the duplicate in-game gift. Multi-gift conversations
/// (e.g. <c>ViktorGift_GadgetIntro</c> with three blueprint pages) are suppressed on
/// every page, but only one AP check is sent.
/// </summary>
[HarmonyPatch(typeof(ConversationPageGiftBlueprint), nameof(ConversationPageGiftBlueprint.ApplyChanges))]
internal static class ConversationPageGiftBlueprintPatch
{
    private static bool Prefix(ConversationPageGiftBlueprint __instance, out bool __state)
    {
        // __state must be set before any early return so Harmony can pass it to Postfix.
        // HasBlueprint = possession (the set AddBlueprint writes); IsBlueprintUnlocked reads
        // the separate availability set and is wrong for "does the player already have this".
        var director = SceneContext.Instance?.GadgetDirector;
        __state = director != null && __instance.gadget != null
                  && director.HasBlueprint(__instance.gadget);

        if (ConversationActiveTrackerPatch.ShouldSuppressGift())
        {
            Logger.Info(
                $"[AP-Conv] GiftBlueprint suppressed (AP will deliver item): " +
                $"gadget='{__instance.gadget?.name}'  " +
                $"conv='{ConversationActiveTrackerPatch.ActiveConversationDebugName}'");
            return false; // skip ApplyChanges
        }

        return true;
    }

    private static void Postfix(ConversationPageGiftBlueprint __instance, bool __state)
    {
        if (!Plugin.Instance.ModEnabled) return;
        if (__instance.gadget == null) return;
        if (ConversationActiveTrackerPatch.ShouldSuppressGift()) return; // already logged in Prefix

        var gadgetName = __instance.gadget.name;
        var eventName  = __instance.noticeEvent?.name ?? "(null)";
        var debugStr   = __instance.ToDebugString() ?? "(null)";

        if (__state)
        {
            Logger.Debug(
                $"[AP-Conv] GiftBlueprint (already unlocked, skipping): " +
                $"gadget='{gadgetName}'  event='{eventName}'  debug='{debugStr}'");
            return;
        }

        Logger.Info(
            $"[AP-Conv] GiftBlueprint (granted in-game): " +
            $"gadget='{gadgetName}'  event='{eventName}'  debug='{debugStr}'");
    }
}

// ---------------------------------------------------------------------------
// Gift gadget (unit) suppression
// ---------------------------------------------------------------------------

/// <summary>
/// Intercepts conversations that gift a gadget unit directly (not a blueprint).
/// Suppresses the in-game grant when the conversation is an active AP location check.
///
/// Note: Most GiftGadget conversations are the GiftPrefab variants (e.g. a pre-built
/// Warp Depot) that are NOT in the LocationTable, so suppression will rarely fire here.
/// The guard is present for correctness in case a GiftGadget conversation is ever
/// added to the location pool.
/// </summary>
[HarmonyPatch(typeof(ConversationPageGiftGadget), nameof(ConversationPageGiftGadget.ApplyChanges))]
internal static class ConversationPageGiftGadgetPatch
{
    private static bool Prefix(ConversationPageGiftGadget __instance)
    {
        if (ConversationActiveTrackerPatch.ShouldSuppressGift())
        {
            Logger.Info(
                $"[AP-Conv] GiftGadget suppressed (AP will deliver item): " +
                $"gadget='{__instance.gadget?.name}'  " +
                $"conv='{ConversationActiveTrackerPatch.ActiveConversationDebugName}'");
            return false;
        }

        return true;
    }

    private static void Postfix(ConversationPageGiftGadget __instance)
    {
        if (!Plugin.Instance.ModEnabled) return;
        if (__instance.gadget == null) return;
        if (ConversationActiveTrackerPatch.ShouldSuppressGift()) return;

        Logger.Info(
            $"[AP-Conv] GiftGadget (granted in-game): gadget='{__instance.gadget.name}'");
    }
}

// ---------------------------------------------------------------------------
// Gift upgrade component suppression
// ---------------------------------------------------------------------------

/// <summary>
/// Intercepts conversations that gift an UpgradeComponent
/// (e.g. Mochi's <c>Conv_Mochi_ArchiveKeyComponent</c>).
/// Suppresses the in-game grant when the conversation is an active AP location check.
/// </summary>
[HarmonyPatch(typeof(ConversationPageGiftUpgradeComponent), nameof(ConversationPageGiftUpgradeComponent.ApplyChanges))]
internal static class ConversationPageGiftUpgradeComponentPatch
{
    private static bool Prefix(ConversationPageGiftUpgradeComponent __instance)
    {
        if (ConversationActiveTrackerPatch.ShouldSuppressGift())
        {
            Logger.Info(
                $"[AP-Conv] GiftUpgradeComponent suppressed (AP will deliver item): " +
                $"component='{__instance.upgradeComponent?.name}'  " +
                $"conv='{ConversationActiveTrackerPatch.ActiveConversationDebugName}'");
            return false;
        }

        return true;
    }

    private static void Postfix(ConversationPageGiftUpgradeComponent __instance)
    {
        if (!Plugin.Instance.ModEnabled) return;
        if (ConversationActiveTrackerPatch.ShouldSuppressGift()) return;

        var compName = __instance.upgradeComponent?.name ?? "(null)";
        Logger.Info(
            $"[AP-Conv] GiftUpgradeComponent (granted in-game): component='{compName}'");
    }
}

// ---------------------------------------------------------------------------
// Callout content override — shows the scouted AP item on suppressed gift pages
// ---------------------------------------------------------------------------
//
// NOTE: the old ConversationPageGiftBlueprintCalloutPatch (Prefix on ShouldShowCallout that
// hid the callout while suppressing) was REMOVED entirely, for three compounding reasons:
//
//  1. ShouldShowCallout's trivial native body ("return true") is shared/folded with unrelated
//     methods in the IL2CPP build, so the Harmony detour fired for foreign callers — once a
//     check conversation latched ShouldSuppressGift() true, the always-skip version emptied
//     the pause menu of everything but Screenshot (player-reported). Removing the detour
//     eliminates that hazard class at the root.
//  2. Hiding the callout left the notice page's award animation (DOShakePosition via the
//     _animators list) tweening a deactivated/destroyed transform, spamming benign but noisy
//     "DOTWEEN ► Target or field is missing/null" warnings after the conversation closed.
//  3. Hiding the callout meant the player never saw WHAT the check delivered.
//
// Instead the callout now shows vanilla-style (live tween target, no warnings) and
// ConversationCalloutOverridePatch below rewrites its content: the Archipelago logo replaces
// the vanilla gift icon and the label shows the scouted item ("★ Anti-Spark 10 bundle → Rhelys")
// so the conversation no longer advertises a gadget the player is not actually receiving.

/// <summary>
/// Rewrites the gift callout on suppressed gift pages to display the scouted Archipelago
/// item instead of the vanilla gift. Hook: <c>ConversationViewHolder.ShowConversationPage</c>
/// (CallerCount 2 — safe; the notice view holder's own bind methods are CallerCount(0) and
/// cannot be patched). The override is re-applied for several frames from
/// <see cref="Tick"/> to outlast Unity Localization's async label update, mirroring
/// <c>AccessDoorUITextPatch</c>.
/// </summary>
[HarmonyPatch(typeof(ConversationViewHolder), "ShowConversationPage")]
internal static class ConversationCalloutOverridePatch
{
    private static int    _framesLeft;
    private static string _message = "";

    private static void Postfix()
    {
        if (!ConversationActiveTrackerPatch.ShouldSuppressGift())
        {
            // Non-check conversation (or checks off) — hand any frozen callout label back to
            // Unity Localization so vanilla gifts display their real names again. View
            // holders are pooled and reused across conversations.
            RestoreLocalization();
            _framesLeft = 0;
            return;
        }

        _message    = BuildScoutMessage();
        _framesLeft = 30; // localization's async OnUpdateString fires 1-2 frames later; add margin
        Apply();
    }

    /// <summary>Called every frame from <c>ApUpdateBehaviour.Update</c>. No-ops when idle.</summary>
    internal static void Tick()
    {
        if (_framesLeft <= 0) return;
        _framesLeft--;
        Apply();
    }

    private static void Apply()
    {
        try
        {
            var holders = UnityEngine.Resources.FindObjectsOfTypeAll<ConversationPageNoticeViewHolder>();
            for (int i = 0; i < holders.Length; i++)
            {
                var h = holders[i];
                if (h == null || !h.isActiveAndEnabled) continue;

                var calloutGo = h._calloutHolder;
                if (calloutGo == null || !calloutGo.activeInHierarchy) continue; // page has no visible callout

                // Freeze the LocalizeStringEvent so it stops overwriting our text, then write
                // the scout message into the TMP label it drives.
                var locEvent = (UnityEngine.MonoBehaviour?)h._calloutText;
                if (locEvent != null)
                {
                    locEvent.enabled = false;
                    var tmp = locEvent.GetComponent<TMPro.TMP_Text>()
                           ?? locEvent.gameObject.GetComponentInChildren<TMPro.TMP_Text>();
                    if (tmp != null)
                    {
                        // AP item names can be far longer than vanilla gift names — let TMP
                        // shrink the font to fit. Max is pinned to the label's designed size
                        // so short names render exactly like vanilla.
                        if (!tmp.enableAutoSizing)
                        {
                            tmp.fontSizeMax      = tmp.fontSize;
                            tmp.fontSizeMin      = tmp.fontSize * 0.35f;
                            tmp.enableAutoSizing = true;
                        }
                        tmp.text = _message;
                    }
                }

                // Replace the vanilla gift icon with the Archipelago logo — the vanilla sprite
                // advertises a gadget the player is not receiving.
                var logo = SlimeRancher2AP.Patches.UiPatches.OptionsMenuInjectionPatch.GetLogoSprite();
                if (logo != null && h._calloutImage != null)
                {
                    h._calloutImage.sprite         = logo;
                    h._calloutImage.preserveAspect = true;
                }

                // Hide the quantity bubble — any count refers to the vanilla gift, not the AP item.
                if (h._quantityHolder != null) h._quantityHolder.SetActive(false);
            }
        }
        catch { /* scene transition — retry next frame while armed */ }
    }

    private static void RestoreLocalization()
    {
        try
        {
            var holders = UnityEngine.Resources.FindObjectsOfTypeAll<ConversationPageNoticeViewHolder>();
            for (int i = 0; i < holders.Length; i++)
            {
                var locEvent = (UnityEngine.MonoBehaviour?)holders[i]?._calloutText;
                if (locEvent != null && !locEvent.enabled) locEvent.enabled = true;
            }
        }
        catch { /* best-effort */ }
    }

    /// <summary>
    /// Builds the callout label from persisted scout data for the active conversation's
    /// location: "* item" (progression), "! item" (trap), plus "FOR player" when the item
    /// belongs to another world. ASCII markers only — the game's HemispheresCaps2 font has
    /// no glyphs for ★/⚠/→ (they render as blank spaces and spam font warnings).
    /// Falls back to a generic label when scout data is missing.
    /// </summary>
    private static string BuildScoutMessage()
    {
        var debugName = ConversationActiveTrackerPatch.ActiveConversationDebugName;
        if (!string.IsNullOrEmpty(debugName)
            && LocationTable.TryGetByConversation(debugName!, out var loc) && loc != null)
        {
            var scout = Plugin.Instance.SaveManager.GetScout(loc.Id);
            if (scout != null)
            {
                var label = scout.IsProgression ? $"* {scout.ItemName}"
                          : scout.IsTrap        ? $"! {scout.ItemName}"
                          :                       scout.ItemName;

                var session = Plugin.Instance.ApClient.Session;
                var mySlot  = session?.ConnectionInfo.Slot ?? -1;
                var myName  = mySlot >= 0 ? (session?.Players.GetPlayerInfo(mySlot)?.Name ?? "") : "";
                bool isMine = string.Equals(scout.PlayerName, myName, System.StringComparison.OrdinalIgnoreCase);

                return isMine ? label : $"{label} FOR {scout.PlayerName}";
            }
        }
        return "Archipelago check";
    }
}
