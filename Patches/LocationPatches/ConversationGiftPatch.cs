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
/// Tracks the conversation on screen, so the gift-page patches below know whether to
/// suppress the in-game grant. Main thread only.
/// </summary>
[HarmonyPatch(typeof(ConversationViewHolder), nameof(ConversationViewHolder.ShowConversation))]
internal static class ConversationActiveTrackerPatch
{
    /// <summary>Debug name of the open conversation, or null.</summary>
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
    // The name MUST clear when the UI closes, or gifts stay suppressed all session.
    // EndConversation can't be patched (CallerCount(0)), so poll for an active view holder.

    private const int UiPollInterval = 30; // ~0.5 s at 60 fps
    private static int _uiPollCounter;

    /// <summary>Per-frame; no-op unless a name is latched.</summary>
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
        // For conversations exited early (no RecordPlayed).
        KillConversationTweens("UI closed");
    }

    /// <summary>
    /// Kills tweens animating the conversation UI before teardown destroys their targets —
    /// otherwise DOTween spams "Target or field is missing/null" (e.g. closing during the
    /// gift award shake).
    /// </summary>
    internal static void KillConversationTweens(string reason)
    {
        try
        {
            var holders = UnityEngine.Resources.FindObjectsOfTypeAll<ConversationViewHolder>();
            var uiTransforms = new HashSet<System.IntPtr>();
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
                    uiTransforms.Add(tr.Pointer);
                    killed += DG.Tweening.DOTween.Kill(tr, false);
                }
            }
            // Phase 2, null-target tweens, which kill-by-target can't reach:
            //  • After a suppressed gift, the award shake (its target was never created). Only
            //    then — any DOTween.To tween has a null target, and killing unrelated ones
            //    skipped their OnComplete (suspected of silencing game audio all session).
            //  • A Sequence whose children animate the conversation UI or a destroyed object
            //    (the looping page Sequence); Kill(target) only matches top-level tweens.
            int reaped = 0;
            bool giftSuppressed = GiftSuppressedThisConversation;
            GiftSuppressedThisConversation = false;
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

                    if (!giftSuppressed && !AnimatesConversationUi(tw, uiTransforms, 0))
                    {
#if DEBUG
                        Logger.Info($"[AP-Conv] Tween cleanup ({reason}): left null-target tween alone — {DescribeTween(tw)}");
#endif
                        continue;
                    }

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

    /// <summary>Set when a gift Prefix blocks a grant; makes reaping null-target tweens safe.</summary>
    internal static bool GiftSuppressedThisConversation;

    /// <summary>
    /// True when <paramref name="tw"/> — or, for a Sequence, any tween nested in it — targets a
    /// transform under the conversation UI, or a Unity object that has already been destroyed.
    /// </summary>
    private static bool AnimatesConversationUi(DG.Tweening.Tween tw, HashSet<System.IntPtr> uiTransforms, int depth)
    {
        if (depth > 4) return false;   // Sequences nest shallowly; bound it anyway

        var seq = tw.TryCast<DG.Tweening.Sequence>();
        if (seq != null)
        {
            var children = seq.sequencedTweens;
            if (children == null) return false;
            for (int i = 0; i < children.Count; i++)
            {
                var child = children[i];
                if (child != null && AnimatesConversationUi(child, uiTransforms, depth + 1)) return true;
            }
            return false;
        }

        Il2CppSystem.Object? target = null;
        try { target = tw.target; } catch { return false; }
        var uo = target?.TryCast<UnityEngine.Object>();
        if (uo is null) return false;

        // Unity's equality: a destroyed object compares equal to null. Nothing left to animate.
        if (uo == null) return true;

        var tr = uo.TryCast<UnityEngine.Transform>() ?? uo.TryCast<UnityEngine.Component>()?.transform;
        return tr != null && uiTransforms.Contains(tr.Pointer);
    }

#if DEBUG
    private static string DescribeTween(DG.Tweening.Tween tw)
    {
        try
        {
            return $"type={tw.GetIl2CppType().Name}  duration={DG.Tweening.TweenExtensions.Duration(tw, false):0.##}s" +
                   $"  loops={DG.Tweening.TweenExtensions.Loops(tw)}";
        }
        catch (System.Exception ex) { return $"<unreadable: {ex.GetType().Name}>"; }
    }

    /// <summary>Logs every playing tween's target, for when the cleanup finds nothing to kill.</summary>
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

    /// <summary>True when the open conversation is a check in this seed — its gift comes from AP instead.</summary>
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

    /// <summary>
    /// Gift assets the apworld puts in EVERY seed's pool, so their vanilla gift is suppressed
    /// even with conversations off — a free Radiant Projector would bypass the Labyrinth logic.
    /// </summary>
    /// <remarks>
    /// The other conversation gadgets are deliberately absent: the apworld only pools them when
    /// their conversation tier is on, so otherwise the vanilla gift is their only source.
    /// </remarks>
    private static readonly HashSet<string> AlwaysApGiftAssets = new(StringComparer.Ordinal)
    {
        "EnergyBeamNode",       // Radiant Projector Blueprint  (ViktorStoryCipher4)
        "ArchiveKeyComponent",  // Archive Key Component
        "RefineryLink",         // Refinery Link                (MochiIntroCall)
    };

    /// <summary>True for a gift in <see cref="AlwaysApGiftAssets"/> on a bound save.</summary>
    internal static bool IsAlwaysApGift(string? assetName)
        => Plugin.Instance != null
           && Plugin.Instance.ModEnabled
           && Plugin.Instance.SaveManager.IsSaveBound
           && !string.IsNullOrEmpty(assetName)
           && AlwaysApGiftAssets.Contains(assetName!);
}

// ---------------------------------------------------------------------------
// Gift blueprint suppression
// ---------------------------------------------------------------------------

/// <summary>
/// Blocks a blueprint gift when its conversation is a check (or the asset is always an AP
/// item). The check itself is sent by <c>ConversationRecordedPatch</c> — once, even for
/// multi-gift conversations.
/// </summary>
[HarmonyPatch(typeof(ConversationPageGiftBlueprint), nameof(ConversationPageGiftBlueprint.ApplyChanges))]
internal static class ConversationPageGiftBlueprintPatch
{
    private static bool Prefix(ConversationPageGiftBlueprint __instance, out bool __state)
    {
        // HasBlueprint, not IsBlueprintUnlocked (see ItemHandler.EnsureGadgetBlueprint).
        var director = SceneContext.Instance?.GadgetDirector;
        __state = director != null && __instance.gadget != null
                  && director.HasBlueprint(__instance.gadget);

        bool alwaysAp = ConversationActiveTrackerPatch.IsAlwaysApGift(__instance.gadget?.name);
        if (ConversationActiveTrackerPatch.ShouldSuppressGift() || alwaysAp)
        {
            Logger.Info(
                $"[AP-Conv] GiftBlueprint suppressed (AP will deliver item): " +
                $"gadget='{__instance.gadget?.name}'  " +
                $"conv='{ConversationActiveTrackerPatch.ActiveConversationDebugName}'" +
                (alwaysAp ? "  [always an AP item]" : ""));
            ConversationActiveTrackerPatch.GiftSuppressedThisConversation = true;
            return false; // skip ApplyChanges
        }

        return true;
    }

    private static void Postfix(ConversationPageGiftBlueprint __instance, bool __state)
    {
        if (!Plugin.Instance.ModEnabled) return;
        if (__instance.gadget == null) return;
        if (ConversationActiveTrackerPatch.ShouldSuppressGift()
            || ConversationActiveTrackerPatch.IsAlwaysApGift(__instance.gadget?.name)) return; // already logged in Prefix

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
/// Blocks a gadget-unit gift when its conversation is a check. Rarely fires: most such
/// gifts aren't locations.
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
            ConversationActiveTrackerPatch.GiftSuppressedThisConversation = true;
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

/// <summary>Blocks an UpgradeComponent gift when its conversation is a check (or always AP).</summary>
[HarmonyPatch(typeof(ConversationPageGiftUpgradeComponent), nameof(ConversationPageGiftUpgradeComponent.ApplyChanges))]
internal static class ConversationPageGiftUpgradeComponentPatch
{
    private static bool Prefix(ConversationPageGiftUpgradeComponent __instance)
    {
        bool alwaysAp = ConversationActiveTrackerPatch.IsAlwaysApGift(__instance.upgradeComponent?.name);
        if (ConversationActiveTrackerPatch.ShouldSuppressGift() || alwaysAp)
        {
            Logger.Info(
                $"[AP-Conv] GiftUpgradeComponent suppressed (AP will deliver item): " +
                $"component='{__instance.upgradeComponent?.name}'  " +
                $"conv='{ConversationActiveTrackerPatch.ActiveConversationDebugName}'" +
                (alwaysAp ? "  [always an AP item]" : ""));
            ConversationActiveTrackerPatch.GiftSuppressedThisConversation = true;
            return false;
        }

        return true;
    }

    private static void Postfix(ConversationPageGiftUpgradeComponent __instance)
    {
        if (!Plugin.Instance.ModEnabled) return;
        if (ConversationActiveTrackerPatch.ShouldSuppressGift()
            || ConversationActiveTrackerPatch.IsAlwaysApGift(__instance.upgradeComponent?.name)) return;

        var compName = __instance.upgradeComponent?.name ?? "(null)";
        Logger.Info(
            $"[AP-Conv] GiftUpgradeComponent (granted in-game): component='{compName}'");
    }
}

// ---------------------------------------------------------------------------
// Callout content override — shows the scouted AP item on suppressed gift pages
// ---------------------------------------------------------------------------
//
// Never patch ShouldShowCallout to hide the callout: its trivial native body is folded with
// unrelated methods, so the detour fired for foreign callers and emptied the pause menu.

/// <summary>
/// Shows the scouted AP item (and AP logo) in a suppressed gift page's callout. Hooks
/// <c>ShowConversationPage</c> — the notice holder's own binds are CallerCount(0) — and
/// re-applies for a few frames from <see cref="Tick"/> to outlast async localization.
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
            // Unfreeze any label we froze — view holders are pooled.
            RestoreLocalization();
            _framesLeft = 0;
            return;
        }

        _message    = BuildScoutMessage();
        _framesLeft = 30; // localization updates 1-2 frames later; margin
        Apply();
    }

    /// <summary>Per-frame; no-op when idle.</summary>
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

                // Freeze localization so it stops overwriting our text.
                var locEvent = (UnityEngine.MonoBehaviour?)h._calloutText;
                if (locEvent != null)
                {
                    locEvent.enabled = false;
                    var tmp = locEvent.GetComponent<TMPro.TMP_Text>()
                           ?? locEvent.gameObject.GetComponentInChildren<TMPro.TMP_Text>();
                    if (tmp != null)
                    {
                        // AP names run long — auto-size down, never above the designed size.
                        if (!tmp.enableAutoSizing)
                        {
                            tmp.fontSizeMax      = tmp.fontSize;
                            tmp.fontSizeMin      = tmp.fontSize * 0.35f;
                            tmp.enableAutoSizing = true;
                        }
                        tmp.text = _message;
                    }
                }

                var logo = SlimeRancher2AP.Patches.UiPatches.OptionsMenuInjectionPatch.GetLogoSprite();
                if (logo != null && h._calloutImage != null)
                {
                    h._calloutImage.sprite         = logo;
                    h._calloutImage.preserveAspect = true;
                }

                // The count belongs to the vanilla gift.
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
    /// "* item" (progression) / "! item" (trap), plus "FOR player" for another world. ASCII
    /// only — the callout font lacks ★/⚠/→.
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
