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
// Injects an "Archipelago" tab into the Options menu:
//   1. BindCategories Prefix  — append a blank category to the config's list, so it gets a tab.
//   2. BindCategories Postfix — label the tab and give it the AP logo.
//   3. BindItemCategory / SwapCategory / Close — show ConnectionUI on our tab, hide it otherwise.
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>
/// Injects the AP tab. Positional params (__0): private-method names aren't reliable in interop.
/// </summary>
[HarmonyPatch(typeof(OptionsUIRoot), "BindCategories")]
internal static class OptionsMenuInjectionPatch
{
    internal static OptionsItemCategory? ApCategory { get; private set; }

    // Our category's index in the config list — and so of its tab.
    internal static int OurCategoryIndex { get; private set; } = -1;

    private static Sprite? _logoSprite;

    /// <summary>The AP logo sprite (embedded resource), shared across the mod's UI.</summary>
    internal static Sprite? GetLogoSprite()
    {
        // Not ??=: that misses a sprite Unity destroyed between scenes.
        if (_logoSprite == null)
            _logoSprite = LoadLogoSprite();
        return _logoSprite;
    }

    private static void Prefix(OptionsUIRoot __instance, OptionsConfiguration __0)
    {
        if (!Plugin.Instance.ModEnabled) return;
        if (ApCategory != null) return;   // already injected

        // Its empty _title renders as ""; the Postfix writes the label.
        var cat = ScriptableObject.CreateInstance<OptionsItemCategory>();
        ApCategory = cat;

        TryInjectIntoConfig(__0, cat);
    }

    private static void Postfix(OptionsUIRoot __instance, OptionsConfiguration __0)
    {
        if (!Plugin.Instance.ModEnabled) return;
        if (ApCategory == null || OurCategoryIndex < 0) return;

        // Tab holders are created 1:1 and in order with the categories (About uses another type).
        var holders = __instance.GetComponentsInChildren<CategoryTabViewHolder>(includeInactive: true);
        if (holders == null || OurCategoryIndex >= holders.Length)
        {
            Logger.Warning(
                $"[AP] OptionsMenuPatch: expected holder at index {OurCategoryIndex} but only {holders?.Length ?? 0} found");
            return;
        }

        var ourHolder = holders[OurCategoryIndex];

        // Freeze localization so it can't overwrite our text.
        if (ourHolder.titleTextString != null)
            ourHolder.titleTextString.enabled = false;

        if (ourHolder.titleText != null)
            ourHolder.titleText.text = "Archipelago";

        // Icon only — the selected/unselected sprites are the tab background.
        var logoSprite = GetLogoSprite();
        if (logoSprite != null && ourHolder.icon != null)
        {
            ourHolder.icon.sprite         = logoSprite;
            ourHolder.icon.preserveAspect = true;
            // White, or the selection tint can hide it.
            ourHolder.icon.color          = Color.white;
            ourHolder.icon.enabled        = true;
        }

        Logger.Info($"[AP] OptionsMenuPatch: Archipelago tab configured (holder index {OurCategoryIndex})");
    }

    /// <summary>
    /// Appends <paramref name="cat"/> to the config's list, a field on the generic
    /// ListAsset&lt;T&gt; base, reached by raw IL2CPP field access.
    /// </summary>
    private static void TryInjectIntoConfig(OptionsConfiguration config, OptionsItemCategory cat)
    {
        try
        {
            var configClassPtr = Il2CppClassPointerStore<OptionsConfiguration>.NativeClassPtr;
            var parentClassPtr  = IL2CPP.il2cpp_class_get_parent(configClassPtr);
            // "items", not "_items" (confirmed at runtime).
            var itemsFieldPtr   = IL2CPP.GetIl2CppField(parentClassPtr, "items");

            if (itemsFieldPtr == IntPtr.Zero)
            {
                Logger.Warning("[AP] OptionsMenuPatch: 'items' field not found on ListAsset parent — Archipelago tab will not appear.");
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
                Logger.Warning("[AP] OptionsMenuPatch: config._items is null — cannot inject");
                return;
            }

            var list = new Il2CppSystem.Collections.Generic.List<OptionsItemCategory>(listNativePtr);
            list.Add(cat);
            OurCategoryIndex = list.Count - 1;

            Logger.Info($"[AP] OptionsMenuPatch: Archipelago category injected at index {OurCategoryIndex} ({list.Count} total)");
        }
        catch (Exception ex)
        {
            Logger.Error($"[AP] OptionsMenuPatch: Exception during category injection: {ex}");
        }
    }

    /// <summary>Loads the embedded "SlimeRancher2-AP.logo.png" as a Sprite, or null.</summary>
    private static Sprite? LoadLogoSprite()
    {
        try
        {
            var asm = Assembly.GetExecutingAssembly();
            using var stream = asm.GetManifestResourceStream("SlimeRancher2-AP.logo.png");
            if (stream == null)
            {
                Logger.Warning("[AP] Logo resource 'SlimeRancher2-AP.logo.png' not found — tab will have no icon.");
                return null;
            }

            var bytes = new byte[stream.Length];
            _ = stream.Read(bytes, 0, bytes.Length);

            // ImageConversion.LoadImage is stripped from this build — decode it ourselves.
            var rgba = DecodePngToRgba(bytes, out int w, out int h);
            if (rgba == null)
            {
                Logger.Warning("[AP] Logo PNG decode failed.");
                return null;
            }

            var tex = new Texture2D(w, h, TextureFormat.RGBA32, mipChain: false);

            // Attempt A: LoadRawTextureData (may be stripped).
            var loaded = false;
            try
            {
                // One bulk copy — per-byte interop writes stalled ~450 ms.
                var raw = new Il2CppStructArray<byte>(rgba);
                tex.LoadRawTextureData(raw);
                tex.Apply();
                loaded = true;
                Logger.Info($"[AP] Logo loaded via LoadRawTextureData: {w}×{h}");
            }
            catch (Exception rawEx)
            {
                Logger.Warning($"[AP] LoadRawTextureData unavailable ({rawEx.Message}) — trying SetPixels32");
            }

            // Attempt B: SetPixels32.
            if (!loaded)
            {
                try
                {
                    var managed = new Color32[w * h];
                    for (int i = 0; i < managed.Length; i++)
                        managed[i] = new Color32(rgba[i*4], rgba[i*4+1], rgba[i*4+2], rgba[i*4+3]);
                    var colors = new Il2CppStructArray<Color32>(managed);
                    tex.SetPixels32(colors);
                    tex.Apply();
                    loaded = true;
                    Logger.Info($"[AP] Logo loaded via SetPixels32: {w}×{h}");
                }
                catch (Exception px32Ex)
                {
                    Logger.Warning($"[AP] SetPixels32 failed: {px32Ex.Message}");
                }
            }

            if (!loaded)
            {
                Logger.Warning("[AP] All logo-load attempts failed — tab will have no icon.");
                return null;
            }

            // Survive Resources.UnloadUnusedAssets between scenes.
            tex.hideFlags = HideFlags.DontUnloadUnusedAsset;
            var sprite = Sprite.Create(tex, new Rect(0, 0, w, h), new Vector2(0.5f, 0.5f), 100f);
            if (sprite != null) sprite.hideFlags = HideFlags.DontUnloadUnusedAsset;
            return sprite;
        }
        catch (Exception ex)
        {
            Logger.Error($"[AP] Logo load failed: {ex}");
            return null;
        }
    }

    // ── PNG decoder ──────────────────────────────────────────────────────────────

    /// <summary>
    /// Decodes an 8-bit RGB/RGBA PNG to RGBA32, bottom row first (Unity's origin). Null on error.
    /// </summary>
    private static byte[]? DecodePngToRgba(byte[] png, out int width, out int height)
    {
        width = height = 0;
        try
        {
            if (png.Length < 8 ||
                png[0] != 0x89 || png[1] != 0x50 || png[2] != 0x4E || png[3] != 0x47 ||
                png[4] != 0x0D || png[5] != 0x0A || png[6] != 0x1A || png[7] != 0x0A)
            {
                Logger.Warning("[AP] DecodePng: not a PNG file");
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
                Logger.Warning($"[AP] DecodePng: unsupported format w={w} h={h} depth={bitDepth}");
                return null;
            }
            int channels = colorType == 6 ? 4 : colorType == 2 ? 3 : 0;
            if (channels == 0)
            {
                Logger.Warning($"[AP] DecodePng: unsupported color type {colorType} (only RGB=2/RGBA=6 supported)");
                return null;
            }

            // IDAT is zlib-wrapped DEFLATE; skip the 2-byte zlib header.
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

            // Undo the scanline filters.
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
            Logger.Info($"[AP] DecodePng: decoded {w}×{h} ({channels}ch) PNG successfully");
            return rgba;
        }
        catch (Exception ex)
        {
            Logger.Warning($"[AP] DecodePng: {ex.Message}");
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
/// On our tab, shows ConnectionUI and skips the original (it would throw on the empty
/// category); on others, hides ConnectionUI.
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
                // The About panel is separate and nothing else clears it.
                __instance.itemsPanel.SetActive(false);
                if (__instance.aboutPanel != null)
                    __instance.aboutPanel.SetActive(false);
                Plugin.Instance.ConnectionUi?.Show(
                    __instance.itemsPanel.transform.parent,
                    __instance.itemsPanel);
                return false;
            }

            Plugin.Instance.ConnectionUi?.Hide();
            __instance.itemsPanel.SetActive(true);
        }
        catch (Exception ex)
        {
            Logger.Error($"[AP] OptionsMenuTabSelectedPatch.Prefix: {ex.Message}");
        }

        return true;
    }
}

/// <summary>
/// Hides ConnectionUI when leaving our tab — including for About, which bypasses
/// BindItemCategory. Must never block: direct tab clicks arrive here.
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

            if (!isOurTab)
            {
                Plugin.Instance.ConnectionUi?.Hide();
                __instance.itemsPanel.SetActive(true);
            }
        }
        catch (Exception ex)
        {
            Logger.Error($"[AP] OptionsMenuSwapCategoryPatch.Prefix: {ex.Message}");
        }

        return true;
    }
}

[HarmonyPatch(typeof(OptionsUIRoot), nameof(OptionsUIRoot.Close))]
internal static class OptionsMenuClosePatch
{
    private static void Postfix()
    {
        Plugin.Instance.ConnectionUi?.Hide();
    }
}

/// <summary>
/// Blocks Q/E tab navigation while an AP input field has focus. Only then: in the pause menu,
/// tab clicks come through here too.
/// </summary>
[HarmonyPatch(typeof(OptionsUIRoot), nameof(OptionsUIRoot.CheckThenSwapCategory))]
internal static class OptionsMenuBlockTabNavWhileTypingPatch
{
    private static bool Prefix()
    {
        if (Plugin.Instance.ConnectionUi?.IsVisible != true)
            return true;

        var selected = EventSystem.current?.currentSelectedGameObject;
        if (selected != null && selected.GetComponent<TMP_InputField>() != null)
            return false;

        return true;
    }
}
