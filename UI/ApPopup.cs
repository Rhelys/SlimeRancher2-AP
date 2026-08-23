using Il2CppMonomiPark.SlimeRancher.UI.Popup;
using SlimeRancher2AP.Patches.UiPatches;
using System.Collections.Generic;
using UnityEngine;

namespace SlimeRancher2AP.UI;

/// <summary>
/// Displays an arbitrary message through the game's own major popup — the large panel SR2 shows
/// for a new Slimepedia entry or upgrade component — branded with the Archipelago logo.
///
/// <para>
/// Extracted from <see cref="ItemNotifier"/>, which was the first consumer. Item announcements
/// are only one use: any moment where the mod refuses a player action (a region gate pressed
/// without its access item, a locked ranch plot, an encounter gated behind collected items) needs
/// to say why, and the IMGUI corner text those refusals previously used is easy to miss at exactly
/// the moment it matters. <see cref="ItemNotifier"/> now owns only item-specific policy and calls
/// in here to draw.
/// </para>
///
/// <para>
/// <b>Text.</b> <c>PreviewPopupViewModel</c>'s fields are <c>LocalizedString</c> and cannot carry
/// runtime text, so the popup is enqueued with a blank runtime config and its labels are then
/// frozen (<c>LocalizeStringEvent</c> disabled) and written directly — the technique used by
/// <see cref="ShopUiHelper.OverrideText"/> and <c>FabricatorDetailsPatch</c>. Writes are
/// re-asserted for a few frames because localization fills labels asynchronously after bind.
/// </para>
///
/// <para>
/// <b>Pooling.</b> <c>PreviewPopup</c> instances are reused by the stack, so every bind must
/// either claim the popup or hand localization back — a freeze left behind renders the previous
/// message's text under the next popup's icon. That is what
/// <see cref="Patches.UiPatches.PreviewPopupSetDataPatch"/> exists for. Active state is
/// deliberately never restored: it is per-bind and vanilla's <c>SetData</c> has already set it
/// correctly by the time the Postfix runs.
/// </para>
/// </summary>
public static class ApPopup
{
    // ── Cached scene objects ─────────────────────────────────────────────────
    private static PreviewPopupStack?            _stack;
    private static PreviewPopupStandaloneConfig? _config;

    /// <summary>The three lines a popup can carry.</summary>
    private readonly struct Message
    {
        public readonly string Title;
        public readonly string Header;
        public readonly string Intro;
        public Message(string title, string header, string intro)
        { Title = title; Header = header; Intro = intro; }
    }

    /// <summary>
    /// View models we created, paired with the message they should display. Keyed by native
    /// pointer because the bind hook hands back the same IL2CPP object. Bounded so a long session
    /// cannot leak; 8 is far more than can be in flight at once.
    /// </summary>
    private const int TitleRingSize = 8;
    private static readonly Queue<(System.IntPtr ptr, Message msg)> _ourViewModels = new();

    // The pooled popup currently carrying our frozen labels, plus the re-assert countdown.
    private static PreviewPopup? _frozenPopup;
    private static Message?      _frozenMessage;
    private static int           _pendingFrames;

    // Fallback claim path: watch the stack's current view model directly for a short window after
    // each enqueue, so the text still appears if SetData ever stops being patchable.
    private static float _watchUntil;

    // Per-message cooldown for throttled callers. A refusal fires on every interaction attempt,
    // and a player pressing a locked gate button repeatedly should not stack popups.
    private static readonly Dictionary<string, float> _cooldowns = new();

    // -------------------------------------------------------------------------
    // Public API
    // -------------------------------------------------------------------------

    /// <summary>Shows a popup immediately.</summary>
    public static void Show(string title, string header = "Archipelago", string intro = "")
        => Enqueue(new Message(title, header, intro));

    /// <summary>
    /// Shows a popup unless one with the same <paramref name="key"/> was shown within
    /// <paramref name="cooldownSeconds"/>. For refusals, which repeat as fast as the player can
    /// retry the blocked action.
    /// </summary>
    public static void ShowThrottled(string key, string title,
                                     string header = "Archipelago", string intro = "",
                                     float cooldownSeconds = 8f)
    {
        float now = Time.unscaledTime;
        if (_cooldowns.TryGetValue(key, out float next) && now < next) return;
        _cooldowns[key] = now + cooldownSeconds;
        Show(title, header, intro);
    }

    /// <summary>
    /// Forces the Archipelago logo to decode now rather than on the first popup.
    /// <c>ImageConversion.LoadImage</c> is stripped from this IL2CPP build so the PNG is inflated
    /// by hand on the main thread; doing it lazily put that cost on a gameplay frame. Called from
    /// the scene-change path so it lands during a loading screen. The sprite is pinned with
    /// <c>DontUnloadUnusedAsset</c>, so it happens at most once per session.
    /// </summary>
    public static void Prewarm()
    {
        try { OptionsMenuInjectionPatch.GetLogoSprite(); }
        catch (System.Exception ex) { Logger.Warning($"[AP-Popup] Logo pre-warm failed: {ex.Message}"); }
    }

    /// <summary>Drops cached scene references. Call on scene change / disconnect.</summary>
    public static void Reset()
    {
        // Hand localization back before letting go, so a scene that keeps its popup pool alive
        // does not inherit frozen labels.
        if (_frozenPopup != null) RestoreVanillaText(_frozenPopup);

        // Only scene-bound references are dropped.
        //
        // _ourViewModels is deliberately KEPT. A popup enqueued during a zone transition — which
        // is exactly when the region-gate refusals fire — binds after this reset runs. Clearing
        // the ring made MessageFor() miss it, so it was treated as a vanilla popup, had its
        // labels handed back to localization, and rendered blank because our runtime config
        // carries empty LocalizedStrings. The ring is pointer-keyed and capped at 8, so stale
        // entries cost nothing and cannot match a later popup.
        _stack         = null;
        _frozenPopup   = null;
        _frozenMessage = null;
        _pendingFrames = 0;
    }

    /// <summary>Called every frame from <c>ApUpdateBehaviour.Update</c>.</summary>
    public static void Tick()
    {
        if (_pendingFrames > 0)
        {
            _pendingFrames--;
            ApplyPendingText();
        }

        if (_watchUntil > 0f && Time.unscaledTime <= _watchUntil) PollTopPopup();
    }

    // -------------------------------------------------------------------------
    // Display
    // -------------------------------------------------------------------------

    private static void Enqueue(Message msg)
    {
        try
        {
            var stack  = ResolveStack();
            var config = stack != null ? ResolveConfig() : null;
            if (stack == null || config == null)
            {
                // No popup stack in this scene (main menu, loading) — fall back to corner text
                // rather than dropping the message entirely.
                StatusHUD.Instance?.ShowNotification(msg.Title);
                return;
            }

            var vm = new PreviewPopupViewModel(config);
            var logo = OptionsMenuInjectionPatch.GetLogoSprite();
            if (logo != null) vm.Icon = logo;

            // Register BEFORE enqueueing: the stack may bind synchronously, and the bind hook
            // recognises our popups by looking the view model up in this ring.
            if (_ourViewModels.Count >= TitleRingSize) _ourViewModels.Dequeue();
            _ourViewModels.Enqueue((vm.Pointer, msg));

            stack.EnqueuePopup(vm);
            _watchUntil = Time.unscaledTime + 8f;

            Logger.Info($"[AP-Popup] {msg.Header}: {msg.Title}");
        }
        catch (System.Exception ex)
        {
            Logger.Warning($"[AP-Popup] Enqueue threw: {ex.Message}");
            StatusHUD.Instance?.ShowNotification(msg.Title);
        }
    }

    // -------------------------------------------------------------------------
    // Bind hook
    // -------------------------------------------------------------------------

    /// <summary>
    /// Called from <see cref="Patches.UiPatches.PreviewPopupSetDataPatch"/> whenever the game
    /// binds a view model into a pooled <c>PreviewPopup</c>. Every bind either claims the popup
    /// as ours or gives localization back.
    /// </summary>
    internal static void OnPopupBound(PreviewPopup popup, PreviewPopupViewModel? data)
    {
        if (popup == null) return;

        var msg = MessageFor(data);
        if (msg.HasValue)
        {
            _frozenPopup   = popup;
            _frozenMessage = msg;
            _pendingFrames = 10;
            ApplyPendingText();
            return;
        }

        RestoreVanillaText(popup);
        if (_frozenPopup == popup)
        {
            _frozenPopup   = null;
            _frozenMessage = null;
            _pendingFrames = 0;
        }
    }

    /// <summary>
    /// Reads the stack's currently-displayed view model and claims the popup if it is ours.
    /// Independent of the Harmony patch, so text still appears if <c>SetData</c> stops being
    /// patchable on a future game build.
    /// </summary>
    private static void PollTopPopup()
    {
        try
        {
            var stack = ResolveStack();
            if (stack == null) return;

            var msg = MessageFor(stack.TopPopupData);
            if (!msg.HasValue) return;

            var popup = FindActivePopup(stack);
            if (popup == null) return;
            if (_frozenPopup == popup && _pendingFrames > 0) return;   // already writing

            _frozenPopup   = popup;
            _frozenMessage = msg;
            _pendingFrames = 10;
            ApplyPendingText();
        }
        catch { /* stack mid-teardown — retry next frame */ }
    }

    private static PreviewPopup? FindActivePopup(PreviewPopupStack stack)
    {
        var popups = stack._popups;
        if (popups == null) return null;
        for (int i = 0; i < popups.Length; i++)
        {
            var p = popups[i];
            if (p != null && p.gameObject.activeInHierarchy) return p;
        }
        return null;
    }

    /// <summary>Returns the message for a view model we created, or null if it is vanilla's.</summary>
    private static Message? MessageFor(PreviewPopupViewModel? data)
    {
        if (data == null) return null;
        var ptr = data.Pointer;
        foreach (var entry in _ourViewModels)
            if (entry.ptr == ptr) return entry.msg;
        return null;
    }

    // -------------------------------------------------------------------------
    // Label writing
    // -------------------------------------------------------------------------

    private static void ApplyPendingText()
    {
        if (_frozenPopup == null || _frozenMessage == null) return;
        try
        {
            var msg = _frozenMessage.Value;
            WriteLabel(_frozenPopup._title,  msg.Title);
            WriteLabel(_frozenPopup._header, msg.Header);
            WriteLabel(_frozenPopup._intro,  msg.Intro);

            var logo = OptionsMenuInjectionPatch.GetLogoSprite();
            if (logo != null && _frozenPopup._icon != null)
            {
                _frozenPopup._icon.sprite         = logo;
                _frozenPopup._icon.preserveAspect = true;
            }
        }
        catch
        {
            // Popup torn down mid-write — stop re-asserting against a dead object.
            _frozenPopup   = null;
            _frozenMessage = null;
            _pendingFrames = 0;
        }
    }

    /// <summary>
    /// Freezes one label's localization and writes <paramref name="text"/> into its TMP.
    /// Re-activates the label first: <c>SetData</c> feeds each label through
    /// <c>TryPopulateText</c>, and our runtime config supplies empty <c>LocalizedString</c>s, so
    /// a label with no value gets hidden and would render nothing however it is written to.
    /// </summary>
    private static void WriteLabel(UnityEngine.MonoBehaviour? localizeEvent, string text)
    {
        if (localizeEvent == null) return;

        var go = localizeEvent.gameObject;
        if (go != null && !go.activeSelf) go.SetActive(true);

        ShopUiHelper.OverrideText(localizeEvent, text, freeze: true);
    }

    /// <summary>Re-enables the localization events we disabled, restoring vanilla text.</summary>
    private static void RestoreVanillaText(PreviewPopup popup)
    {
        try
        {
            ShopUiHelper.OverrideText(popup._title,  null, freeze: false);
            ShopUiHelper.OverrideText(popup._header, null, freeze: false);
            ShopUiHelper.OverrideText(popup._intro,  null, freeze: false);

            // Deliberately does NOT touch GameObject active state. An earlier version recorded
            // "this label was hidden" while showing OUR popup and re-hid it here, which replayed
            // our layout onto the next vanilla popup — a Slimepedia unlock rendered its icon and
            // hint with a blank title bar. Active state is per-bind and vanilla's SetData has
            // already set it correctly by the time this Postfix runs.
        }
        catch { /* popup destroyed — nothing to restore */ }
    }

    // -------------------------------------------------------------------------
    // Scene object resolution
    // -------------------------------------------------------------------------

    private static PreviewPopupStack? ResolveStack()
    {
        if (_stack != null) return _stack;
        _stack = UnityEngine.Object.FindObjectOfType<PreviewPopupStack>();
        return _stack;
    }

    /// <summary>
    /// Builds the standalone popup config once. Category is copied from the game's own
    /// upgrade-component popup config rather than guessed at an enum value, so our popup is
    /// grouped and styled exactly like the vanilla one it imitates.
    /// </summary>
    private static PreviewPopupStandaloneConfig? ResolveConfig()
    {
        if (_config != null) return _config;
        try
        {
            var cfg = ScriptableObject.CreateInstance<PreviewPopupStandaloneConfig>();
            // Pin it: a config held only by this static field is exactly the kind of asset the
            // Resources.UnloadUnusedAssets sweep destroys (see the RanchPlotHandler note).
            cfg.hideFlags |= HideFlags.DontUnloadUnusedAsset;

            var logo = OptionsMenuInjectionPatch.GetLogoSprite();
            if (logo != null) cfg._icon = logo;

            var director = UnityEngine.Object.FindObjectOfType<PopupDirector>();
            var template = director?.ComponentPopupConfig;
            if (template != null) cfg._category = template.Category;

            _config = cfg;
            return _config;
        }
        catch (System.Exception ex)
        {
            Logger.Warning($"[AP-Popup] Could not build popup config: {ex.Message}");
            return null;
        }
    }
}
