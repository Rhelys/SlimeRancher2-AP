using Il2CppMonomiPark.SlimeRancher.UI.Popup;
using SlimeRancher2AP.Patches.UiPatches;
using System.Collections.Generic;
using UnityEngine;

namespace SlimeRancher2AP.UI;

/// <summary>
/// Shows a message in the game's major popup (the Slimepedia-unlock panel) with the AP logo —
/// for item announcements and for explaining refused actions.
/// </summary>
/// <remarks>
/// The view model only takes LocalizedStrings, so the labels are frozen and written directly,
/// re-asserted for a few frames while localization settles. Popups are pooled: every bind
/// (<see cref="Patches.UiPatches.PreviewPopupSetDataPatch"/>) either claims the popup or hands
/// localization back.
/// </remarks>
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

    /// <summary>Our view models (by native pointer) and their messages; a small bounded ring.</summary>
    private const int TitleRingSize = 8;
    private static readonly Queue<(System.IntPtr ptr, Message msg)> _ourViewModels = new();

    // The pooled popup currently carrying our frozen labels, plus the re-assert countdown.
    private static PreviewPopup? _frozenPopup;
    private static Message?      _frozenMessage;
    private static int           _pendingFrames;

    // Fallback if SetData ever can't be patched: poll the stack for a while after each enqueue.
    private static float _watchUntil;

    // Per-key cooldowns, so a repeated refusal doesn't stack popups.
    private static readonly Dictionary<string, float> _cooldowns = new();

    // -------------------------------------------------------------------------
    // Public API
    // -------------------------------------------------------------------------

    /// <summary>Shows a popup immediately.</summary>
    public static void Show(string title, string header = "Archipelago", string intro = "")
        => Enqueue(new Message(title, header, intro));

    /// <summary>Shows a popup unless the same <paramref name="key"/> showed within the cooldown.</summary>
    public static void ShowThrottled(string key, string title,
                                     string header = "Archipelago", string intro = "",
                                     float cooldownSeconds = 8f)
    {
        float now = Time.unscaledTime;
        if (_cooldowns.TryGetValue(key, out float next) && now < next) return;
        _cooldowns[key] = now + cooldownSeconds;
        Show(title, header, intro);
    }

    /// <summary>Decodes the logo during a loading screen rather than on a gameplay frame.</summary>
    public static void Prewarm()
    {
        try { OptionsMenuInjectionPatch.GetLogoSprite(); }
        catch (System.Exception ex) { Logger.Warning($"[AP-Popup] Logo pre-warm failed: {ex.Message}"); }
    }

    /// <summary>Drops cached scene references. Call on scene change / disconnect.</summary>
    public static void Reset()
    {
        if (_frozenPopup != null) RestoreVanillaText(_frozenPopup);

        // _ourViewModels is kept: a popup enqueued mid-transition binds after this reset, and
        // would otherwise render blank.
        _stack         = null;
        _frozenPopup   = null;
        _frozenMessage = null;
        _pendingFrames = 0;
    }

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
                // No popup stack here (menus, loading) — use corner text.
                StatusHUD.Instance?.ShowNotification(msg.Title);
                return;
            }

            var vm = new PreviewPopupViewModel(config);
            var logo = OptionsMenuInjectionPatch.GetLogoSprite();
            if (logo != null) vm.Icon = logo;

            // Register first: the stack may bind synchronously.
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

    /// <summary>On every popup bind: claims it if the view model is ours, else restores localization.</summary>
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

    /// <summary>Claims the top popup if it is ours — the patch-independent fallback.</summary>
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
            if (_frozenPopup == popup && _pendingFrames > 0) return;

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
            // Torn down mid-write.
            _frozenPopup   = null;
            _frozenMessage = null;
            _pendingFrames = 0;
        }
    }

    /// <summary>
    /// Writes a label, re-activating it first — SetData hides labels whose (empty) LocalizedString
    /// has no value.
    /// </summary>
    private static void WriteLabel(UnityEngine.MonoBehaviour? localizeEvent, string text)
    {
        if (localizeEvent == null) return;

        var go = localizeEvent.gameObject;
        if (go != null && !go.activeSelf) go.SetActive(true);

        ShopUiHelper.OverrideText(localizeEvent, text, freeze: true);
    }

    private static void RestoreVanillaText(PreviewPopup popup)
    {
        try
        {
            ShopUiHelper.OverrideText(popup._title,  null, freeze: false);
            ShopUiHelper.OverrideText(popup._header, null, freeze: false);
            ShopUiHelper.OverrideText(popup._intro,  null, freeze: false);

            // Never touch active state: it is per-bind, already set by vanilla's SetData.
        }
        catch { /* popup destroyed */ }
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

    /// <summary>Builds the popup config once, copying Category from the game's component popup.</summary>
    private static PreviewPopupStandaloneConfig? ResolveConfig()
    {
        if (_config != null) return _config;
        try
        {
            var cfg = ScriptableObject.CreateInstance<PreviewPopupStandaloneConfig>();
            // Pin it, or the unused-asset sweep destroys it.
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
