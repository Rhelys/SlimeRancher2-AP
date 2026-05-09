using Il2CppInterop.Runtime;
using Il2CppInterop.Runtime.Attributes;
using Il2CppMonomiPark.SlimeRancher.UI.Options;
using SlimeRancher2AP.Archipelago;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace SlimeRancher2AP.UI;

/// <summary>
/// Archipelago connection panel, rendered as a proper UGUI panel inside the game's
/// native Options menu when the "Archipelago" tab is selected.
///
/// On first Show() the panel is built programmatically and reparented as a sibling of
/// OptionsUIRoot.itemsPanel so it occupies the same screen area.  itemsPanel is
/// deactivated while our tab is active, then restored on tab switch / close.
///
/// No IMGUI is used — GUI.TextField is stripped in this IL2CPP build.
/// TMP_InputField handles text capture and keyboard focus natively.
/// </summary>
public class ConnectionUI : MonoBehaviour
{
    public ConnectionUI(IntPtr handle) : base(handle) { }

    private GameObject? _panel;
    private TMP_InputField? _hostField;
    private TMP_InputField? _slotField;
    private TMP_InputField? _passField;
    private TMP_Text? _statusText;

    private void Awake()
    {
        Plugin.Instance.ApClient.OnConnected        += () => SetStatus("Connected!", new Color(0.2f, 0.9f, 0.3f));
        Plugin.Instance.ApClient.OnConnectionFailed += msg => SetStatus($"Failed: {msg}", new Color(0.9f, 0.3f, 0.2f));
        Plugin.Instance.ApClient.OnDisconnected     += () => SetStatus("Disconnected", new Color(0.9f, 0.75f, 0.1f));
    }

    /// <summary>True while our panel GameObject is active in the scene.</summary>
    public bool IsVisible => _panel != null && _panel.activeSelf;

    // -------------------------------------------------------------------------
    // Show / Hide — called from OptionsMenuTabSelectedPatch
    // -------------------------------------------------------------------------

    /// <summary>No-op overload kept for legacy call sites (e.g. NewGameBlockPatch).</summary>
    public void Show() =>
        Logger.Info("[AP] ConnectionUI.Show() — use Options > Archipelago to connect");

    /// <summary>
    /// Called when the Archipelago tab is selected.
    /// Builds the panel on first call, then makes it visible and hides the native
    /// items panel so our UI occupies the same space.
    /// </summary>
    public void Show(Transform panelParent, GameObject nativeItemsPanel)
    {
        if (_panel == null)
            BuildPanel(panelParent, nativeItemsPanel);

        // itemsPanel toggling is handled by the Prefix which holds the live reference.
        _panel!.SetActive(true);

        // Refresh fields from saved config each time the tab is opened.
        var saved = ArchipelagoData.LoadFromConfig(Plugin.Instance.Config);
        if (_hostField != null) _hostField.text = $"{saved.Uri}:{saved.Port}";
        if (_slotField != null) _slotField.text = saved.SlotName;
        if (_passField != null) _passField.text = saved.Password;
    }

    /// <summary>
    /// Hides our panel. itemsPanel restoration is the caller's responsibility —
    /// the Prefix always has the live OptionsUIRoot reference; the Close patch
    /// doesn't need to restore it (the game tears down the menu itself).
    /// </summary>
    public void Hide()
    {
        if (_panel != null && _panel.gameObject != null)
            _panel.SetActive(false);
    }

    // -------------------------------------------------------------------------
    // UGUI panel construction
    // -------------------------------------------------------------------------

    [HideFromIl2Cpp]
    private void BuildPanel(Transform panelParent, GameObject nativeItemsPanel)
    {
        // Root panel — sibling of itemsPanel, same RectTransform so it fills the
        // same area in the Options menu content zone.
        _panel = new GameObject("APConnectionPanel");
        _panel.transform.SetParent(panelParent, false);

        var rt = _panel.AddComponent<RectTransform>();
        var src = nativeItemsPanel.GetComponent<RectTransform>();
        if (src != null)
        {
            rt.anchorMin = src.anchorMin;
            rt.anchorMax = src.anchorMax;
            rt.offsetMin = src.offsetMin;
            rt.offsetMax = src.offsetMax;
        }

        // Sample font and native button style from an existing OptionsItemViewHolder so our
        // UI matches the rest of the options menu exactly.
        TMP_FontAsset? font          = null;
        ColorBlock     nativeCb      = ColorBlock.defaultColorBlock;
        Color          nativeBgColor = new Color(0.09f, 0.09f, 0.13f, 0.85f);

        var existingHolder = Resources.FindObjectsOfTypeAll<OptionsItemViewHolder>()
            .FirstOrDefault(h => h != null);
        if (existingHolder != null)
        {
            if (existingHolder.button != null)
                nativeCb = existingHolder.button.colors;

            var bgImg = existingHolder._backgroundRect?.GetComponent<Image>();
            if (bgImg != null)
                nativeBgColor = bgImg.color;

            Logger.Info("[AP] ConnectionUI: sampled native options button style");
        }

        // Grab a font from any existing TMP element in the scene so text renders.
        var allTmp = Resources.FindObjectsOfTypeAll<TextMeshProUGUI>();
        for (int i = 0; i < allTmp.Count; i++)
        {
            if (allTmp[i].font != null) { font = allTmp[i].font; break; }
        }

        // Lay out the four content blocks with a VerticalLayoutGroup.
        var vlg = _panel.AddComponent<VerticalLayoutGroup>();
        vlg.padding    = new RectOffset(24, 24, 24, 24);
        vlg.spacing    = 10;
        vlg.childForceExpandWidth  = true;
        vlg.childForceExpandHeight = false;
        vlg.childAlignment         = TextAnchor.UpperLeft;

        _hostField = AddLabeledField("Host : Port",   font, password: false);
        _slotField = AddLabeledField("Slot Name",     font, password: false);
        _passField = AddLabeledField("Password",      font, password: true);

        AddButtonRow(font, nativeCb, nativeBgColor);

        _statusText = AddStatusText(font);

        _panel.SetActive(false);
    }

    // -------------------------------------------------------------------------
    // Helpers — each returns the TMP_InputField or TMP_Text for later wiring.
    // -------------------------------------------------------------------------

    [HideFromIl2Cpp]
    private TMP_InputField AddLabeledField(string label, TMP_FontAsset? font, bool password)
    {
        // Container stacks label + field vertically.
        var container = new GameObject(label.Replace(" ", "") + "_Container");
        container.transform.SetParent(_panel!.transform, false);
        container.AddComponent<RectTransform>();

        var cle = container.AddComponent<LayoutElement>();
        cle.minHeight = 58;

        var cvlg = container.AddComponent<VerticalLayoutGroup>();
        cvlg.spacing    = 4;
        cvlg.childForceExpandWidth  = true;
        cvlg.childForceExpandHeight = false;

        // Label
        var labelGo = new GameObject("Label");
        labelGo.transform.SetParent(container.transform, false);
        labelGo.AddComponent<RectTransform>();

        var lle = labelGo.AddComponent<LayoutElement>();
        lle.minHeight = 20;

        var labelText = labelGo.AddComponent<TextMeshProUGUI>();
        labelText.text     = label;
        labelText.fontSize = 16;
        labelText.color    = new Color(0.65f, 0.85f, 1.0f);   // light blue — stands out on dark panel
        if (font != null) labelText.font = font;

        // Input field root
        var fieldGo = new GameObject("InputField");
        fieldGo.transform.SetParent(container.transform, false);
        fieldGo.AddComponent<RectTransform>();

        var fle = fieldGo.AddComponent<LayoutElement>();
        fle.minHeight = 34;

        var bg = fieldGo.AddComponent<Image>();
        bg.color = new Color(0.12f, 0.12f, 0.16f, 0.95f);

        // Text area (acts as viewport inside TMP_InputField)
        var textArea = new GameObject("TextArea");
        textArea.transform.SetParent(fieldGo.transform, false);
        var taRt = textArea.AddComponent<RectTransform>();
        taRt.anchorMin = Vector2.zero;
        taRt.anchorMax = Vector2.one;
        taRt.offsetMin = new Vector2(6, 4);
        taRt.offsetMax = new Vector2(-6, -4);
        textArea.AddComponent<RectMask2D>();

        // Placeholder
        var phGo = new GameObject("Placeholder");
        phGo.transform.SetParent(textArea.transform, false);
        var phRt = phGo.AddComponent<RectTransform>();
        phRt.anchorMin = Vector2.zero;
        phRt.anchorMax = Vector2.one;
        phRt.offsetMin = Vector2.zero;
        phRt.offsetMax = Vector2.zero;
        var phText = phGo.AddComponent<TextMeshProUGUI>();
        phText.text                = label;
        phText.fontSize            = 15;
        phText.color               = new Color(0.45f, 0.45f, 0.45f);
        phText.enableWordWrapping  = false;
        if (font != null) phText.font = font;

        // Main text
        var textGo = new GameObject("Text");
        textGo.transform.SetParent(textArea.transform, false);
        var textRt = textGo.AddComponent<RectTransform>();
        textRt.anchorMin = Vector2.zero;
        textRt.anchorMax = Vector2.one;
        textRt.offsetMin = Vector2.zero;
        textRt.offsetMax = Vector2.zero;
        var textComp = textGo.AddComponent<TextMeshProUGUI>();
        textComp.fontSize           = 15;
        textComp.color              = Color.white;
        textComp.enableWordWrapping = false;
        if (font != null) textComp.font = font;

        // Wire up TMP_InputField
        var inputField = fieldGo.AddComponent<TMP_InputField>();
        inputField.textViewport  = taRt;
        inputField.textComponent = textComp;
        inputField.placeholder   = phText.Cast<Graphic>();
        if (font != null) inputField.fontAsset = font;
        inputField.pointSize     = 15;

        if (password)
            inputField.contentType = TMP_InputField.ContentType.Password;

        return inputField;
    }

    [HideFromIl2Cpp]
    private void AddButtonRow(TMP_FontAsset? font, ColorBlock nativeCb, Color nativeBgColor)
    {
        // Spacer before buttons to visually separate from fields
        var spacer = new GameObject("ButtonSpacer");
        spacer.transform.SetParent(_panel!.transform, false);
        spacer.AddComponent<RectTransform>();
        spacer.AddComponent<LayoutElement>().minHeight = 4;

        // Each button is a full-width options-row-style selectable
        AddOptionRow("Connect",    font, nativeCb, nativeBgColor, OnConnectClicked);
        AddOptionRow("Disconnect", font, nativeCb, nativeBgColor, () => Plugin.Instance.ApClient.Disconnect());
    }

    /// <summary>
    /// Creates a full-width selectable row using the exact same ColorBlock and background
    /// color as the native OptionsItemViewHolder so it blends with the rest of the menu.
    /// </summary>
    [HideFromIl2Cpp]
    private void AddOptionRow(string label, TMP_FontAsset? font,
        ColorBlock nativeCb, Color nativeBgColor, System.Action onClick)
    {
        var go = new GameObject(label + "Row");
        go.transform.SetParent(_panel!.transform, false);
        go.AddComponent<RectTransform>();

        var le = go.AddComponent<LayoutElement>();
        le.minHeight = 52;

        // Background — set to white so the ColorBlock multiplier works correctly.
        var img = go.AddComponent<Image>();
        img.color = Color.white;

        var btn = go.AddComponent<Button>();
        // Inject the native ColorBlock directly — normal/highlight/press colours match
        // the rest of the options rows exactly.
        btn.colors        = nativeCb;
        btn.targetGraphic = img;

        // Force the image to the native normal colour right away so it doesn't flash white.
        img.color = nativeBgColor;

        btn.onClick.AddListener(DelegateSupport.ConvertDelegate<UnityEngine.Events.UnityAction>(onClick));

        // Label — left-aligned, same weight as options item labels
        var textGo = new GameObject("Label");
        textGo.transform.SetParent(go.transform, false);
        var textRt = textGo.AddComponent<RectTransform>();
        textRt.anchorMin = Vector2.zero;
        textRt.anchorMax = Vector2.one;
        textRt.offsetMin = new Vector2(16, 0);
        textRt.offsetMax = new Vector2(-16, 0);
        var textComp = textGo.AddComponent<TextMeshProUGUI>();
        textComp.text      = label;
        textComp.fontSize  = 16;
        textComp.color     = Color.white;
        textComp.alignment = TextAlignmentOptions.MidlineLeft;
        if (font != null) textComp.font = font;
    }

    [HideFromIl2Cpp]
    private TMP_Text AddStatusText(TMP_FontAsset? font)
    {
        var go = new GameObject("StatusText");
        go.transform.SetParent(_panel!.transform, false);
        go.AddComponent<RectTransform>();

        var le = go.AddComponent<LayoutElement>();
        le.minHeight = 28;

        var text = go.AddComponent<TextMeshProUGUI>();
        text.text     = "";
        text.fontSize = 15;
        text.color    = Color.white;
        if (font != null) text.font = font;
        return text;
    }

    // -------------------------------------------------------------------------
    // Actions
    // -------------------------------------------------------------------------

    [HideFromIl2Cpp]
    private void OnConnectClicked()
    {
        var hostRaw = _hostField?.text.Trim() ?? "";
        var parts   = hostRaw.Split(':');
        var uri     = parts[0];
        var port    = parts.Length > 1 && int.TryParse(parts[1], out var p) ? p : 38281;

        var data = new ArchipelagoData
        {
            Uri      = uri,
            Port     = port,
            SlotName = _slotField?.text.Trim() ?? "",
            Password = _passField?.text.Trim() ?? "",
        };

        data.SaveToConfig(Plugin.Instance.Config);
        SetStatus("Connecting...", Color.white);
        Plugin.Instance.ApClient.Connect(data);
    }

    [HideFromIl2Cpp]
    private void SetStatus(string message, Color color)
    {
        if (_statusText != null)
        {
            _statusText.text  = message;
            _statusText.color = color;
        }
    }
}
