using HarmonyLib;
using Il2CppInterop.Runtime;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using Il2CppMonomiPark.SlimeRancher.Options;
using Il2CppMonomiPark.SlimeRancher.UI.Adapter;
using Il2CppMonomiPark.SlimeRancher.UI.Options;
using System;
using System.IO;
using System.Reflection;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.Localization.Components;

namespace SlimeRancher2AP.Patches.UiPatches;

// ─────────────────────────────────────────────────────────────────────────────
// OptionsMenuPatch — injects an "Archipelago" tab into the native Options menu.
//
// HOW THE OPTIONS SYSTEM WORKS (confirmed from ILSpy):
//   - OptionsConfiguration : ListAsset<OptionsItemCategory> holds all normal tabs
//     (Audio, Video, Controls, Gameplay…).
//   - OptionsUIRoot.BindCategories(OptionsConfiguration, OptionsAboutCategory)
//     is called once from LateStart; it maps every IOptionsCategory to a
//     CategoryViewModel and drives the tab bar via categoryAdapter.
//   - When the player clicks a tab → SwapCategory(int, bool) → BindItemCategory(category)
//     switches the right-panel item list to that category's OptionsItemDefinitions.
//   - The special "About" tab is handled separately via BindAboutCategory().
//
// OUR APPROACH:
//   1. Prefix  BindCategories  — create a blank OptionsItemCategory ScriptableObject
//      and append it to config's _items list *before* binding runs, so it gets a
//      tab slot.  Access to the list uses raw IL2CPP field access on the parent
//      ListAsset<T> class (field name "_items" — Unity convention).
//
//   2. Postfix BindCategories  — after tabs are rendered, find the last
//      CategoryTabViewHolder, disable its LocalizeStringEvent, and set the label
//      text to "Archipelago" directly (same technique as FabricatorDetailsPatch).
//
//   3. Postfix BindItemCategory — show ConnectionUI when our tab is selected;
//      hide it for every other tab.
//
//   4. Postfix Close — hide ConnectionUI when the options menu is closed.
//
// KNOWN RISKS / TODOs:
//   - "_items" field name on ListAsset<T> is inferred from Unity convention.
//     If GetIl2CppField returns IntPtr.Zero the mod logs a warning; no crash.
//   - We pass a blank LocalizedString() (IsEmpty == true) for _title; Unity
//     Localization returns "" gracefully for empty refs.  The Postfix then writes
//     the real text after the tab is rendered.
//   - Cursor handling: ConnectionUI.Show() doesn't save/restore cursor state.
//     If needed, add cursor save/restore as in DebugPanel.
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>
/// Prefix + Postfix on OptionsUIRoot.BindCategories — injects the AP tab.
/// Parameter positions (__0, __1) used because private-method param names
/// are not guaranteed to match IL2CPP interop naming.
/// </summary>
[HarmonyPatch(typeof(OptionsUIRoot), "BindCategories")]
internal static class OptionsMenuInjectionPatch
{
    /// <summary>The category instance we injected; null until first inject.</summary>
    internal static OptionsItemCategory? ApCategory { get; private set; }

    // 0-based index of our category in the OptionsConfiguration list, recorded at
    // injection time so the Postfix and SwapCategory patch can identify our tab.
    internal static int OurCategoryIndex { get; private set; } = -1;

    // Cached logo sprite — loaded once from the embedded PNG resource.
    private static Sprite? _logoSprite;

    /// <summary>
    /// Returns the Archipelago logo sprite, loading it from the embedded resource on first call.
    /// Shared with <see cref="NewGameLoadGamePatch"/> for save-slot icon injection.
    /// </summary>
    internal static Sprite? GetLogoSprite()
    {
        _logoSprite ??= LoadLogoSprite();
        return _logoSprite;
    }

    private static void Prefix(OptionsUIRoot __instance, OptionsConfiguration __0)
    {
        if (!Plugin.Instance.ModEnabled) return;
        if (ApCategory != null) return;   // already injected this session

        // Create a blank OptionsItemCategory ScriptableObject.
        // _title defaults to an empty (IsEmpty) LocalizedString — safe to render.
        var cat = ScriptableObject.CreateInstance<OptionsItemCategory>();
        ApCategory = cat;

        TryInjectIntoConfig(__0, cat);
    }

    private static void Postfix(OptionsUIRoot __instance, OptionsConfiguration __0)
    {
        if (!Plugin.Instance.ModEnabled) return;
        if (ApCategory == null || OurCategoryIndex < 0) return;

        // After BindCategories the tab bar has been populated.
        // CategoryTabViewHolder components are created 1-to-1 with OptionsItemCategories,
        // in the same order.  Our category is at OurCategoryIndex, so its holder is at
        // the same position (About tab uses a different holder type and is excluded).
        var holders = __instance.GetComponentsInChildren<CategoryTabViewHolder>(includeInactive: true);
        if (holders == null || OurCategoryIndex >= holders.Length)
        {
            Plugin.Instance.Log.LogWarning(
                $"[AP] OptionsMenuPatch: expected holder at index {OurCategoryIndex} but only {holders?.Length ?? 0} found");
            return;
        }

        var ourHolder = holders[OurCategoryIndex];

        // Disable the LocalizeStringEvent so it can't overwrite our text.
        if (ourHolder.titleTextString != null)
            ourHolder.titleTextString.enabled = false;

        if (ourHolder.titleText != null)
            ourHolder.titleText.text = "Archipelago";

        // Set the Archipelago logo on the icon Image only.
        // unselectedSprite / selectedSprite drive the tab *background* — leave those
        // alone so the native selection highlight still works correctly.
        var logoSprite = GetLogoSprite();
        if (logoSprite != null && ourHolder.icon != null)
        {
            ourHolder.icon.sprite         = logoSprite;
            ourHolder.icon.preserveAspect = true;
            // Force white (full opacity) — native tab icons inherit a colour tint from
            // the selection state machine; without this the sprite renders invisible or
            // heavily tinted when the tab is unselected.
            ourHolder.icon.color          = Color.white;
            ourHolder.icon.enabled        = true;
        }

        Plugin.Instance.Log.LogInfo($"[AP] OptionsMenuPatch: Archipelago tab configured (holder index {OurCategoryIndex})");
    }

    /// <summary>
    /// Appends <paramref name="cat"/> to the OptionsConfiguration's category list
    /// via raw IL2CPP field access.
    ///
    /// OptionsConfiguration : ListAsset&lt;OptionsItemCategory&gt;.
    /// The list field "_items" is on the generic ListAsset&lt;T&gt; base class.
    /// We resolve it via il2cpp_class_get_parent + GetIl2CppField.
    /// </summary>
    private static void TryInjectIntoConfig(OptionsConfiguration config, OptionsItemCategory cat)
    {
        try
        {
            var configClassPtr = Il2CppClassPointerStore<OptionsConfiguration>.NativeClassPtr;
            // Walk up one level to ListAsset<OptionsItemCategory> where _items lives.
            var parentClassPtr  = IL2CPP.il2cpp_class_get_parent(configClassPtr);
            // Field name confirmed at runtime: "items" (not "_items") on ListAsset<T>.
            var itemsFieldPtr   = IL2CPP.GetIl2CppField(parentClassPtr, "items");

            if (itemsFieldPtr == IntPtr.Zero)
            {
                Plugin.Instance.Log.LogWarning("[AP] OptionsMenuPatch: 'items' field not found on ListAsset parent — Archipelago tab will not appear.");
                return;
            }

            IntPtr listNativePtr;
            unsafe
            {
                var instancePtr = IL2CPP.Il2CppObjectBaseToPtrNotNull(config);
                listNativePtr   = *(IntPtr*)((nint)instancePtr + (int)IL2CPP.il2cpp_field_get_offset(itemsFieldPtr));
            }

            if (listNativePtr == IntPtr.Zero)
            {
                Plugin.Instance.Log.LogWarning("[AP] OptionsMenuPatch: config._items is null — cannot inject");
                return;
            }

            // All IL2CPP interop types expose an (IntPtr pointer) ctor for wrapping
            // a native pointer in a managed object — use that instead of Il2CppObjectPool.
            var list = new Il2CppSystem.Collections.Generic.List<OptionsItemCategory>(listNativePtr);
            list.Add(cat);
            OurCategoryIndex = list.Count - 1;

            Plugin.Instance.Log.LogInfo($"[AP] OptionsMenuPatch: Archipelago category injected at index {OurCategoryIndex} ({list.Count} total)");
        }
        catch (Exception ex)
        {
            Plugin.Instance.Log.LogError($"[AP] OptionsMenuPatch: Exception during category injection: {ex}");
        }
    }

    /// <summary>
    /// Loads the Archipelago logo from the embedded PNG resource and returns a Sprite.
    /// Resource name: "SlimeRancher2-AP.logo.png" (LogicalName set in csproj).
    /// Returns null and logs a warning if the resource is missing or fails to decode.
    /// </summary>
    private static Sprite? LoadLogoSprite()
    {
        try
        {
            var asm = Assembly.GetExecutingAssembly();
            using var stream = asm.GetManifestResourceStream("SlimeRancher2-AP.logo.png");
            if (stream == null)
            {
                Plugin.Instance.Log.LogWarning("[AP] Logo resource 'SlimeRancher2-AP.logo.png' not found — tab will have no icon.");
                return null;
            }

            var bytes = new byte[stream.Length];
            _ = stream.Read(bytes, 0, bytes.Length);

            // ImageConversion.LoadImage is stripped from this game's IL2CPP build (the game
            // never calls it, so the IL2CPP linker removes it).  Decode the PNG ourselves
            // using .NET BCL's DeflateStream, then push the raw RGBA bytes into the texture.
            var rgba = DecodePngToRgba(bytes, out int w, out int h);
            if (rgba == null)
            {
                Plugin.Instance.Log.LogWarning("[AP] Logo PNG decode failed.");
                return null;
            }

            var tex = new Texture2D(w, h, TextureFormat.RGBA32, mipChain: false);

            // Attempt A: LoadRawTextureData — takes raw RGBA bytes directly.
            // May be stripped if the game doesn't use it; wrapped in try/catch.
            var loaded = false;
            try
            {
                var raw = new Il2CppStructArray<byte>(rgba.LongLength);
                for (int i = 0; i < rgba.Length; i++) raw[i] = rgba[i];
                tex.LoadRawTextureData(raw);
                tex.Apply();
                loaded = true;
                Plugin.Instance.Log.LogInfo($"[AP] Logo loaded via LoadRawTextureData: {w}×{h}");
            }
            catch (Exception rawEx)
            {
                Plugin.Instance.Log.LogWarning($"[AP] LoadRawTextureData unavailable ({rawEx.Message}) — trying SetPixels32");
            }

            // Attempt B: SetPixels32 — the game uses dynamic textures, so this is almost
            // certainly not stripped.
            if (!loaded)
            {
                try
                {
                    var colors = new Il2CppStructArray<Color32>((long)(w * h));
                    for (int i = 0; i < w * h; i++)
                        colors[i] = new Color32(rgba[i*4], rgba[i*4+1], rgba[i*4+2], rgba[i*4+3]);
                    tex.SetPixels32(colors);
                    tex.Apply();
                    loaded = true;
                    Plugin.Instance.Log.LogInfo($"[AP] Logo loaded via SetPixels32: {w}×{h}");
                }
                catch (Exception px32Ex)
                {
                    Plugin.Instance.Log.LogWarning($"[AP] SetPixels32 failed: {px32Ex.Message}");
                }
            }

            if (!loaded)
            {
                Plugin.Instance.Log.LogWarning("[AP] All logo-load attempts failed — tab will have no icon.");
                return null;
            }

            return Sprite.Create(tex, new Rect(0, 0, w, h), new Vector2(0.5f, 0.5f), 100f);
        }
        catch (Exception ex)
        {
            Plugin.Instance.Log.LogError($"[AP] Logo load failed: {ex}");
            return null;
        }
    }

    // ── PNG decoder ──────────────────────────────────────────────────────────────
    // ImageConversion.LoadImage is stripped from this game's IL2CPP build (the game
    // uses AssetBundles and never calls LoadImage, so the IL2CPP linker removes it).
    // We decode PNGs ourselves using only .NET BCL APIs (no external dependencies).
    // Supports 8-bit RGB and RGBA PNGs — the only formats the logo will ever be.
    // Output: raw RGBA32 bytes, bottom-row-first (Unity texture origin = bottom-left).

    /// <summary>
    /// Decodes an 8-bit RGB or RGBA PNG file into a raw RGBA32 byte array
    /// (4 bytes per pixel, rows ordered bottom-to-top for Unity's texture origin).
    /// Returns null on any parse or decompression error.
    /// </summary>
    private static byte[]? DecodePngToRgba(byte[] png, out int width, out int height)
    {
        width = height = 0;
        try
        {
            // Verify PNG signature
            if (png.Length < 8 ||
                png[0] != 0x89 || png[1] != 0x50 || png[2] != 0x4E || png[3] != 0x47 ||
                png[4] != 0x0D || png[5] != 0x0A || png[6] != 0x1A || png[7] != 0x0A)
            {
                Plugin.Instance.Log.LogWarning("[AP] DecodePng: not a PNG file");
                return null;
            }

            int w = 0, h = 0, bitDepth = 0, colorType = 0;
            var idatBuf = new System.IO.MemoryStream();
            int pos = 8;

            while (pos + 12 <= png.Length)
            {
                int  len  = ReadInt32BE(png, pos);                  pos += 4;
                var  type = System.Text.Encoding.ASCII.GetString(png, pos, 4); pos += 4;

                if (type == "IHDR")
                {
                    w         = ReadInt32BE(png, pos);
                    h         = ReadInt32BE(png, pos + 4);
                    bitDepth  = png[pos + 8];
                    colorType = png[pos + 9];
                }
                else if (type == "IDAT")
                {
                    idatBuf.Write(png, pos, len);
                }
                else if (type == "IEND") break;

                pos += len + 4; // skip chunk data + CRC
            }

            if (w <= 0 || h <= 0 || bitDepth != 8)
            {
                Plugin.Instance.Log.LogWarning($"[AP] DecodePng: unsupported format w={w} h={h} depth={bitDepth}");
                return null;
            }
            int channels = colorType == 6 ? 4 : colorType == 2 ? 3 : 0;
            if (channels == 0)
            {
                Plugin.Instance.Log.LogWarning($"[AP] DecodePng: unsupported color type {colorType} (only RGB=2/RGBA=6 supported)");
                return null;
            }

            // Decompress: IDAT stream is zlib-wrapped DEFLATE.
            // Skip 2-byte zlib header (CMF + FLG) before feeding to DeflateStream.
            var idatData = idatBuf.ToArray();
            byte[] filtered;
            using (var ms  = new System.IO.MemoryStream(idatData, 2, idatData.Length - 2))
            using (var def = new System.IO.Compression.DeflateStream(
                                ms, System.IO.Compression.CompressionMode.Decompress))
            using (var out_ = new System.IO.MemoryStream())
            {
                def.CopyTo(out_);
                filtered = out_.ToArray();
            }

            // Reconstruct PNG scanline filters and build RGBA output.
            // Output is written bottom-row-first for Unity's bottom-left texture origin.
            var rgba    = new byte[w * h * 4];
            int stride  = w * channels;
            var prior   = new byte[stride]; // reconstructed previous row (starts as zeros)
            var current = new byte[stride]; // reconstructed current row

            for (int y = 0; y < h; y++)
            {
                int rowBase = y * (stride + 1); // +1 for the filter byte
                int filter  = filtered[rowBase];
                int srcBase = rowBase + 1;

                for (int i = 0; i < stride; i++)
                {
                    byte raw = filtered[srcBase + i];
                    byte a   = i >= channels ? current[i - channels] : (byte)0; // left
                    byte b   = prior[i];                                          // up
                    byte c   = i >= channels ? prior[i - channels]   : (byte)0; // up-left
                    current[i] = filter switch
                    {
                        1 => (byte)(raw + a),
                        2 => (byte)(raw + b),
                        3 => (byte)(raw + ((a + b) >> 1)),
                        4 => (byte)(raw + PaethPredictor(a, b, c)),
                        _ => raw,
                    };
                }
                System.Array.Copy(current, prior, stride);

                // Write into output buffer bottom-row-first
                int dstRow = (h - 1 - y) * w * 4;
                for (int x = 0; x < w; x++)
                {
                    int src = x * channels;
                    int dst = dstRow + x * 4;
                    rgba[dst    ] = current[src    ]; // R
                    rgba[dst + 1] = current[src + 1]; // G
                    rgba[dst + 2] = current[src + 2]; // B
                    rgba[dst + 3] = channels == 4 ? current[src + 3] : (byte)255; // A
                }
            }

            width  = w;
            height = h;
            Plugin.Instance.Log.LogInfo($"[AP] DecodePng: decoded {w}×{h} ({channels}ch) PNG successfully");
            return rgba;
        }
        catch (Exception ex)
        {
            Plugin.Instance.Log.LogWarning($"[AP] DecodePng: {ex.Message}");
            return null;
        }
    }

    private static int ReadInt32BE(byte[] b, int pos)
        => (b[pos] << 24) | (b[pos+1] << 16) | (b[pos+2] << 8) | b[pos+3];

    private static int PaethPredictor(int a, int b, int c)
    {
        int p  = a + b - c;
        int pa = Math.Abs(p - a);
        int pb = Math.Abs(p - b);
        int pc = Math.Abs(p - c);
        return pa <= pb && pa <= pc ? a : pb <= pc ? b : c;
    }

}

/// <summary>
/// Prefix on BindItemCategory — intercepts tab selection.
/// When our tab is selected, skip the original entirely (it would crash trying to
/// call BindOptionsDetailsView on our empty category) and show ConnectionUI instead.
/// When any other tab is selected, let the original run and hide ConnectionUI.
/// </summary>
[HarmonyPatch(typeof(OptionsUIRoot), "BindItemCategory")]
internal static class OptionsMenuTabSelectedPatch
{
    private static bool Prefix(OptionsUIRoot __instance, OptionsItemCategory __0)
    {
        if (!Plugin.Instance.ModEnabled) return true;

        try
        {
            bool isOurTab = __0 == OptionsMenuInjectionPatch.ApCategory;

            if (isOurTab)
            {
                // Hide the native items panel and the About panel (separate GameObject on
                // OptionsUIRoot — activated by BindAboutCategory, not cleared otherwise).
                __instance.itemsPanel.SetActive(false);
                if (__instance.aboutPanel != null)
                    __instance.aboutPanel.SetActive(false);
                Plugin.Instance.ConnectionUi?.Show(
                    __instance.itemsPanel.transform.parent,
                    __instance.itemsPanel);
                return false;   // skip original — empty category would throw
            }

            // Switching to any other tab: hide our panel and ensure itemsPanel is visible
            // so the original BindItemCategory can populate it normally.
            Plugin.Instance.ConnectionUi?.Hide();
            __instance.itemsPanel.SetActive(true);
        }
        catch (Exception ex)
        {
            // Never let our code silently swallow other tabs — log and fall through.
            Plugin.Instance.Log.LogError($"[AP] OptionsMenuTabSelectedPatch.Prefix: {ex.Message}");
        }

        return true;
    }
}

/// <summary>
/// Postfix on OptionsUIRoot.Close — hide ConnectionUI when the options menu closes
/// so it doesn't linger in-game.
/// </summary>
/// <summary>
/// Prefix on SwapCategory — called for EVERY tab click, including the About tab
/// (which has its own BindAboutCategory path that bypasses BindItemCategory).
/// When switching to any tab other than ours, hide our panel and ensure itemsPanel
/// is active before the original runs.  When switching to ours, do nothing here —
/// BindItemCategory's Prefix handles the show logic.
/// </summary>
[HarmonyPatch(typeof(OptionsUIRoot), "SwapCategory",
    new System.Type[] { typeof(int), typeof(bool) })]
internal static class OptionsMenuSwapCategoryPatch
{
    private static bool Prefix(OptionsUIRoot __instance, int __0, bool __1)
    {
        if (!Plugin.Instance.ModEnabled) return true;
        if (OptionsMenuInjectionPatch.OurCategoryIndex < 0) return true;

        try
        {
            bool isOurTab = __0 == OptionsMenuInjectionPatch.OurCategoryIndex;

            // Block SwapCategory when our panel is visible and Q/E tries to leave our tab.
            // CheckThenSwapCategory is the primary gate but the game update may call
            // SwapCategory more directly for keyboard/gamepad navigation.
            if (Plugin.Instance.ConnectionUi?.IsVisible == true && !isOurTab)
                return false;

            if (!isOurTab)
            {
                Plugin.Instance.ConnectionUi?.Hide();
                __instance.itemsPanel.SetActive(true);
            }
        }
        catch (Exception ex)
        {
            Plugin.Instance.Log.LogError($"[AP] OptionsMenuSwapCategoryPatch.Prefix: {ex.Message}");
        }

        return true;
    }
}

/// <summary>
/// Postfix on OptionsUIRoot.Close — hide ConnectionUI when the options menu closes
/// so it doesn't linger in-game.
/// </summary>
[HarmonyPatch(typeof(OptionsUIRoot), nameof(OptionsUIRoot.Close))]
internal static class OptionsMenuClosePatch
{
    private static void Postfix()
    {
        Plugin.Instance.ConnectionUi?.Hide();
    }
}

/// <summary>
/// Prefix on OptionsUIRoot.CheckThenSwapCategory — suppresses Q/E tab navigation
/// whenever the Archipelago panel is visible.
///
/// This covers two cases:
///   1. Panel visible, no field focused yet — Q/E would otherwise still cycle the
///      visible tab highlight even if the content doesn't switch (because the visual
///      selection updates before CheckThenSwapCategory is even reached).
///   2. Panel visible, a TMP_InputField IS focused — Q/E must go to the text box.
///
/// The player can still switch away from the AP tab by clicking another tab directly.
/// CheckThenSwapCategory is the single entry point for ALL tab-navigation input
/// (Q / E / shoulder buttons) so patching it here is sufficient.
/// </summary>
[HarmonyPatch(typeof(OptionsUIRoot), nameof(OptionsUIRoot.CheckThenSwapCategory))]
internal static class OptionsMenuBlockTabNavWhileTypingPatch
{
    private static bool Prefix()
    {
        // If our panel is currently showing, block all keyboard tab navigation.
        if (Plugin.Instance.ConnectionUi?.IsVisible == true)
            return false;

        // Also block if any TMP_InputField has UGUI focus (safety net for edge cases).
        var selected = EventSystem.current?.currentSelectedGameObject;
        if (selected != null && selected.GetComponent<TMP_InputField>() != null)
            return false;

        return true;
    }
}
