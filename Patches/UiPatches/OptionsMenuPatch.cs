using HarmonyLib;
using Il2CppInterop.Runtime;
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
//   - When the player clicks a tab → SwapCategory(int) → BindItemCategory(category)
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
            var asm    = Assembly.GetExecutingAssembly();
            using var stream = asm.GetManifestResourceStream("SlimeRancher2-AP.logo.png");
            if (stream == null)
            {
                Plugin.Instance.Log.LogWarning("[AP] Logo resource 'SlimeRancher2-AP.logo.png' not found — tab will have no icon. " +
                    "Convert static/logo.webp to static/logo.png and rebuild.");
                return null;
            }

            var bytes = new byte[stream.Length];
            _ = stream.Read(bytes, 0, bytes.Length);

            var tex = new Texture2D(2, 2, TextureFormat.RGBA32, mipChain: false);
            if (!ImageConversion.LoadImage(tex, bytes))
            {
                Plugin.Instance.Log.LogWarning("[AP] Logo PNG failed to decode — check that static/logo.png is a valid PNG file.");
                return null;
            }

            var sprite = Sprite.Create(
                tex,
                new Rect(0, 0, tex.width, tex.height),
                new Vector2(0.5f, 0.5f),
                pixelsPerUnit: 100f);

            Plugin.Instance.Log.LogInfo($"[AP] Logo loaded: {tex.width}×{tex.height}");
            return sprite;
        }
        catch (Exception ex)
        {
            Plugin.Instance.Log.LogError($"[AP] Logo load failed: {ex}");
            return null;
        }
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
[HarmonyPatch(typeof(OptionsUIRoot), "SwapCategory")]
internal static class OptionsMenuSwapCategoryPatch
{
    private static bool Prefix(OptionsUIRoot __instance, int __0)
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
