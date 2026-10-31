using Il2CppInterop.Runtime;
using Il2CppInterop.Runtime.Attributes;
using Il2CppMonomiPark.SlimeRancher.UI.Options;
using SlimeRancher2AP.Archipelago;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace SlimeRancher2AP.UI;

/// <summary>
/// The connection panel on the Options menu's Archipelago tab: UGUI built on first Show(), as a
/// sibling of OptionsUIRoot.itemsPanel in its screen area. Not IMGUI — GUI.TextField is
/// stripped in this IL2CPP build.
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

    public bool IsVisible => _panel != null && _panel.activeSelf;

    // -------------------------------------------------------------------------
    // Show / Hide — called from OptionsMenuTabSelectedPatch
    // -------------------------------------------------------------------------

    /// <summary>No-op overload kept for legacy call sites (e.g. NewGameBlockPatch).</summary>
    public void Show() =>
        Logger.Info("[AP] ConnectionUI.Show() — use Options > Archipelago to connect");

    /// <summary>On selecting the tab: builds the panel once, then shows it.</summary>
    public void Show(Transform panelParent, GameObject nativeItemsPanel)
    {
        if (_panel == null)
            BuildPanel(panelParent, nativeItemsPanel);

        _panel!.SetActive(true);   // the caller toggles itemsPanel

        var saved = ArchipelagoData.LoadFromConfig(Plugin.Instance.Config);
        if (_hostField != null) _hostField.text = $"{saved.Uri}:{saved.Port}";
        if (_slotField != null) _slotField.text = saved.SlotName;
        if (_passField != null) _passField.text = saved.Password;
    }

    /// <summary>Hides the panel; restoring itemsPanel is the caller's job.</summary>
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
        // Sibling of itemsPanel with the same RectTransform.
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

        // Borrow the native row style so the panel matches.
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

        var allTmp = Resources.FindObjectsOfTypeAll<TextMeshProUGUI>();
        for (int i = 0; i < allTmp.Count; i++)
        {
            if (allTmp[i].font != null) { font = allTmp[i].font; break; }
        }

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
    // Helpers
    // -------------------------------------------------------------------------

    [HideFromIl2Cpp]
    private TMP_InputField AddLabeledField(string label, TMP_FontAsset? font, bool password)
    {
        var container = new GameObject(label.Replace(" ", "") + "_Container");
        container.transform.SetParent(_panel!.transform, false);
        container.AddComponent<RectTransform>();

        var cle = container.AddComponent<LayoutElement>();
        cle.minHeight = 58;

        var cvlg = container.AddComponent<VerticalLayoutGroup>();
        cvlg.spacing    = 4;
        cvlg.childForceExpandWidth  = true;
        cvlg.childForceExpandHeight = false;

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

        var fieldGo = new GameObject("InputField");
        fieldGo.transform.SetParent(container.transform, false);
        fieldGo.AddComponent<RectTransform>();

        var fle = fieldGo.AddComponent<LayoutElement>();
        fle.minHeight = 34;

        var bg = fieldGo.AddComponent<Image>();
        bg.color = new Color(0.12f, 0.12f, 0.16f, 0.95f);

        // The input field's viewport
        var textArea = new GameObject("TextArea");
        textArea.transform.SetParent(fieldGo.transform, false);
        var taRt = textArea.AddComponent<RectTransform>();
        taRt.anchorMin = Vector2.zero;
        taRt.anchorMax = Vector2.one;
        taRt.offsetMin = new Vector2(6, 4);
        taRt.offsetMax = new Vector2(-6, -4);
        textArea.AddComponent<RectMask2D>();

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
        // Not the game font: it is all-caps and lacks lowercase glyphs. TMP's default does both.

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
        var inputField = fieldGo.AddComponent<TMP_InputField>();
        inputField.textViewport  = taRt;
        inputField.textComponent = textComp;
        inputField.placeholder   = phText.Cast<Graphic>();
        inputField.pointSize     = 15;

        if (password)
            inputField.contentType = TMP_InputField.ContentType.Password;

        return inputField;
    }

    [HideFromIl2Cpp]
    private void AddButtonRow(TMP_FontAsset? font, ColorBlock nativeCb, Color nativeBgColor)
    {
        var spacer = new GameObject("ButtonSpacer");
        spacer.transform.SetParent(_panel!.transform, false);
        spacer.AddComponent<RectTransform>();
        spacer.AddComponent<LayoutElement>().minHeight = 4;

        AddOptionRow("Connect",    font, nativeCb, nativeBgColor, OnConnectClicked);
        AddOptionRow("Disconnect", font, nativeCb, nativeBgColor, () => Plugin.Instance.ApClient.Disconnect());
    }

    /// <summary>A full-width button row in the native options-row colours.</summary>
    [HideFromIl2Cpp]
    private void AddOptionRow(string label, TMP_FontAsset? font,
        ColorBlock nativeCb, Color nativeBgColor, System.Action onClick)
    {
        var go = new GameObject(label + "Row");
        go.transform.SetParent(_panel!.transform, false);
        go.AddComponent<RectTransform>();

        var le = go.AddComponent<LayoutElement>();
        le.minHeight = 52;

        var img = go.AddComponent<Image>();
        img.color = Color.white;

        var btn = go.AddComponent<Button>();
        btn.colors        = nativeCb;
        btn.targetGraphic = img;

        // Avoid a white flash before the first transition.
        img.color = nativeBgColor;

        btn.onClick.AddListener(DelegateSupport.ConvertDelegate<UnityEngine.Events.UnityAction>(onClick));

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
