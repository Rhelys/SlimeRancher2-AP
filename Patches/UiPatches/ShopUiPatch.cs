using Archipelago.MultiClient.Net.Enums;
using HarmonyLib;
using Il2CppMonomiPark.SlimeRancher.UI.Shop;
using Il2CppMonomiPark.SlimeRancher.UI.Shop.Pages;
using SlimeRancher2AP.Data;
using SlimeRancher2AP.Patches.LocationPatches;
using TMPro;

namespace SlimeRancher2AP.Patches.UiPatches;

/// <summary>
/// Shop UI helpers. Text is overridden by freezing its <c>LocalizeStringEvent</c> and writing
/// the TMP_Text; it is unfrozen when a pooled element rebinds to a vanilla item.
/// </summary>
internal static class ShopUiHelper
{
    /// <summary>
    /// The scouted item's tier, from the live scout or (offline) the persisted one. Trap is tested
    /// first so a trap never reads as Progression.
    /// </summary>
    internal static string TierOf(LocationInfo info)
    {
        var live = Plugin.Instance.ApClient.GetScoutedItem(info.Id);
        if (live != null)
        {
            if (live.Flags.HasFlag(ItemFlags.Trap))         return "Trap";
            if (live.Flags.HasFlag(ItemFlags.Advancement))  return "Progression";
            if (live.Flags.HasFlag(ItemFlags.NeverExclude)) return "Useful";
            return "Filler";
        }

        return Plugin.Instance.SaveManager.GetScout(info.Id)?.Tier ?? "";
    }

    /// <summary>
    /// The scouted item's name, or a generic label until scouting finishes. The tier goes in the
    /// description, not here, where it would read as part of the name.
    /// </summary>
    internal static string HeaderText(LocationInfo info)
        => Plugin.Instance.ApClient.GetScoutedItem(info.Id)?.ItemName
           ?? Plugin.Instance.SaveManager.GetScout(info.Id)?.ItemName
           ?? "Archipelago Check";

    /// <summary>Builds the "An Archipelago check for X" description for a location.</summary>
    internal static string BuildScoutDescription(LocationInfo info)
    {
        bool sent = ShopPatchState.IsChecked(info);

        var scouted = Plugin.Instance.ApClient.GetScoutedItem(info.Id);
        if (scouted == null)
            return sent ? "Archipelago check (sent)" : "Archipelago check";

        var session = Plugin.Instance.ApClient.Session;
        bool isSelf  = session != null && session.ConnectionInfo.Slot == scouted.Player;
        string owner = isSelf
            ? "your game"
            : (session?.Players.GetPlayerAlias(scouted.Player) ?? "Unknown");

        string line = $"An Archipelago check for {owner} ({TierOf(info)})";
        return sent ? $"{line}\n(Check sent)" : line;
    }

    /// <summary>
    /// Freezes the LocalizeStringEvent and writes <paramref name="text"/>; <c>freeze: false</c>
    /// restores localization.
    /// </summary>
    internal static void OverrideText(UnityEngine.MonoBehaviour? localizeEvent, string? text, bool freeze)
    {
        if (localizeEvent == null) return;
        localizeEvent.enabled = !freeze;
        if (!freeze || text == null) return;

        var tmp = localizeEvent.GetComponent<TMP_Text>()
                  ?? localizeEvent.gameObject.GetComponentInChildren<TMP_Text>();
        if (tmp != null) tmp.text = text;
    }

    /// <summary>Swaps the image to the AP logo.</summary>
    internal static void ShowLogo(UnityEngine.UI.Image? image)
    {
        var logo = OptionsMenuInjectionPatch.GetLogoSprite();
        if (logo == null || image == null) return;
        image.sprite         = logo;
        image.preserveAspect = true;
    }

    /// <summary>
    /// Puts the vanilla sprite back if the image shows our logo — rebinding a pooled element
    /// doesn't always reassign it.
    /// </summary>
    internal static void RestoreVanillaIcon(UnityEngine.UI.Image? image,
                                            Il2CppMonomiPark.SlimeRancher.Shop.Runtime.ShopRuntimeItem? item,
                                            bool preferSmall)
    {
        var logo = OptionsMenuInjectionPatch.GetLogoSprite();
        if (image == null || logo == null || image.sprite != logo || item == null) return;
        try
        {
            var sprite = preferSmall ? (item.SmallIcon ?? item.Icon) : (item.Icon ?? item.SmallIcon);
            if (sprite != null) image.sprite = sprite;
        }
        catch { /* item mid-load — vanilla refresh will set it */ }
    }

    // -------------------------------------------------------------------------
    // Full-art re-assert tick
    // -------------------------------------------------------------------------

    // The blurb showing an AP slot. Its _itemIcon is overwritten by an async art load after our
    // Postfix (the callback can't be patched), so Tick() re-asserts the logo.
    internal static ShopItemDescriptionBlurb? ActiveBlurb;
    internal static LocationInfo?             ActiveBlurbInfo;

    // The purchase popup open on an AP check. Its name can resolve asynchronously after our
    // Postfix and land on the frozen label anyway, so Tick() re-asserts it every frame (which
    // also covers a re-open without Repopulate).
    internal static ShopPurchasePopup? ActivePopup;

    private static int _tickCounter;

    /// <summary>Per-frame; the blurb work runs every ~15 frames.</summary>
    internal static void Tick()
    {
        if (ActivePopup != null) TickPurchasePopup();
        if (_soldOutLocation >= 0) TickSoldOut();

        if (ActiveBlurb == null || ActiveBlurbInfo == null) return;
        if (++_tickCounter < 15) return;
        _tickCounter = 0;

        try
        {
            var blurb = ActiveBlurb;
            if (blurb == null || !blurb.isActiveAndEnabled
                || ShopPatchState.GetActiveLocation(blurb._item)?.Id != ActiveBlurbInfo.Id)
            {
                ActiveBlurb = null;
                ActiveBlurbInfo = null;
                return;
            }

            ShowLogo(blurb._itemIcon);
        }
        catch
        {
            ActiveBlurb = null;
            ActiveBlurbInfo = null;
        }
    }

    private static void TickPurchasePopup()
    {
        try
        {
            var popup = ActivePopup;
            if (popup == null || !popup.isActiveAndEnabled) { ActivePopup = null; return; }

            var info = ShopPatchState.GetActiveLocation(popup._itemToBuy);
            if (info == null)
            {
                OverrideText(popup._itemNameLabel,  null, freeze: false);
                OverrideText(popup._infoBlurbLabel, null, freeze: false);
                ActivePopup = null;
                return;
            }

            EnsureText(popup._itemNameLabel,  HeaderText(info));
            EnsureText(popup._infoBlurbLabel, BuildScoutDescription(info));
        }
        catch { ActivePopup = null; }   // popup torn down mid-frame
    }

    /// <summary><see cref="OverrideText"/> only when it differs, so the TMP mesh isn't dirtied every frame.</summary>
    private static void EnsureText(UnityEngine.MonoBehaviour? localizeEvent, string text)
    {
        if (localizeEvent == null) return;
        var tmp = localizeEvent.GetComponent<TMP_Text>()
                  ?? localizeEvent.gameObject.GetComponentInChildren<TMP_Text>();
        if (tmp != null && tmp.text == text && !localizeEvent.enabled) return;
        OverrideText(localizeEvent, text, freeze: true);
    }

    /// <summary>
    /// Redraws the item's buttons and details panel once its check is sent — the game's own
    /// refresh ran inside <c>TryPurchase</c>, before the check.
    /// </summary>
    internal static void RefreshAfterPurchase(Il2CppMonomiPark.SlimeRancher.Shop.Runtime.ShopRuntimeItem item)
    {
        try
        {
            var info = ShopPatchState.GetActiveLocation(item);
            if (info == null) return;

            // Inactive ones too — the grid is inactive behind the popup.
            int matched = 0, active = 0;
            foreach (var b in ButtonsFor(info.Id))
            {
                matched++;
                if (b.isActiveAndEnabled) { b.RepopulateDisplay(); active++; }
                else ShopItemButtonPatch.ApplySoldOut(b);
            }

            var blurb = ActiveBlurb;
            if (blurb != null && ShopPatchState.GetActiveLocation(blurb._item)?.Id == info.Id)
                blurb.UpdateDisplay();

            // The game redraws the button again later; keep re-asserting for up to ~10 s.
            _soldOutLocation = info.Id;
            _soldOutFrames   = SoldOutWatchFrames;

            Logger.Info($"[AP] Shop: sold-out refresh for '{info.EntryName}' — " +
                        $"{matched} button(s), {active} active");
        }
        catch (System.Exception ex)
        {
            Logger.Warning($"[AP] Shop: sold-out refresh failed: {ex.Message}");
        }
    }

    private const int  SoldOutWatchFrames = 600;
    private static long _soldOutLocation = -1;
    private static int  _soldOutFrames;

    /// <summary>Every loaded shop button showing this AP location, active or not.</summary>
    private static System.Collections.Generic.IEnumerable<ShopItemButton> ButtonsFor(long locationId)
    {
        var buttons = UnityEngine.Resources.FindObjectsOfTypeAll<ShopItemButton>();
        for (int i = 0; i < buttons.Length; i++)
        {
            var b = buttons[i];
            if (b == null || !b.gameObject.scene.IsValid()) continue;   // skip prefabs
            if (ShopPatchState.GetActiveLocation(b._currentItem)?.Id == locationId)
                yield return b;
        }
    }

    /// <summary>Re-asserts the overlay until a visible button shows it (searching every 10 frames).</summary>
    private static void TickSoldOut()
    {
        if (--_soldOutFrames <= 0) { _soldOutLocation = -1; return; }
        if (_soldOutFrames % 10 != 0) return;
        try
        {
            foreach (var b in ButtonsFor(_soldOutLocation))
            {
                if (!b.isActiveAndEnabled) continue;   // grid still hidden behind the popup
                var overlay = b._soldOutDisplay;
                if (overlay == null) continue;
                if (!overlay.activeSelf)
                {
                    overlay.SetActive(true);
                    Logger.Info($"[AP] Shop: sold-out overlay re-shown " +
                                $"{SoldOutWatchFrames - _soldOutFrames} frame(s) after purchase");
                }
                _soldOutLocation = -1;
                return;
            }
        }
        catch { _soldOutLocation = -1; }
    }
}

/// <summary>Marks AP slots in the grid: "[AP]" prefix, logo, and sold-out once sent.</summary>
[HarmonyPatch(typeof(ShopItemButton), nameof(ShopItemButton.RepopulateDisplay))]
internal static class ShopItemButtonPatch
{
    private static void Postfix(ShopItemButton __instance)
    {
        if (!ShopPatchState.IsEnabled) return;

        var item = __instance._currentItem;
        var info = ShopPatchState.GetActiveLocation(item);
        var nameEvent = (UnityEngine.MonoBehaviour?)__instance._displayText;

        if (info == null)
        {
            ShopUiHelper.OverrideText(nameEvent, null, freeze: false);
            ShopUiHelper.RestoreVanillaIcon(__instance._icon, item, preferSmall: true);
            return;
        }

        string title;
        try { title = item!.Title?.GetLocalizedString() ?? info.EntryName ?? info.Name; }
        catch { title = info.EntryName ?? info.Name; }
        ShopUiHelper.OverrideText(nameEvent, $"[AP] {title}", freeze: true);
        ShopUiHelper.ShowLogo(__instance._icon);

        ApplySoldOut(__instance);
    }

    /// <summary>Shows the sold-out overlay on a button whose AP check has been sent.</summary>
    internal static void ApplySoldOut(ShopItemButton button)
    {
        try
        {
            var info = ShopPatchState.GetActiveLocation(button._currentItem);
            if (info != null && ShopPatchState.IsChecked(info))
                button._soldOutDisplay?.SetActive(true);
        }
        catch { /* cosmetic */ }
    }
}

/// <summary>
/// Re-applies the AP sold-out overlay after the button's own purchase/wallet redraws, which
/// read the vanilla sold-out state and hide it. (The handlers can't be patched; these can.)
/// </summary>
[HarmonyPatch(typeof(ShopItemButton), nameof(ShopItemButton.UpdateForItemSoldOut))]
internal static class ShopItemButtonSoldOutPatch
{
    private static void Postfix(ShopItemButton __instance)
    {
        if (ShopPatchState.IsEnabled) ShopItemButtonPatch.ApplySoldOut(__instance);
    }
}

[HarmonyPatch(typeof(ShopItemButton), nameof(ShopItemButton.UpdatePriceDisplay))]
internal static class ShopItemButtonPricePatch
{
    private static void Postfix(ShopItemButton __instance)
    {
        if (ShopPatchState.IsEnabled) ShopItemButtonPatch.ApplySoldOut(__instance);
    }
}

/// <summary>Shows the scouted item in the details panel for AP slots.</summary>
[HarmonyPatch(typeof(ShopItemDescriptionBlurb), nameof(ShopItemDescriptionBlurb.UpdateDisplay))]
internal static class ShopItemDescriptionBlurbPatch
{
    private static void Postfix(ShopItemDescriptionBlurb __instance)
    {
        if (!ShopPatchState.IsEnabled) return;

        var info = ShopPatchState.GetActiveLocation(__instance._item);
        var headerEvent = (UnityEngine.MonoBehaviour?)__instance._headerLabel;
        var descEvent   = (UnityEngine.MonoBehaviour?)__instance._descriptionLabel;

        if (info == null)
        {
            ShopUiHelper.OverrideText(headerEvent, null, freeze: false);
            ShopUiHelper.OverrideText(descEvent,   null, freeze: false);
            ShopUiHelper.RestoreVanillaIcon(__instance._itemIcon, __instance._item, preferSmall: false);
            if (ShopUiHelper.ActiveBlurb == __instance)
            {
                ShopUiHelper.ActiveBlurb = null;
                ShopUiHelper.ActiveBlurbInfo = null;
            }
            return;
        }

        // Header: the AP item. Description: whose it is. The grid button keeps the vanilla name.
        ShopUiHelper.OverrideText(headerEvent, ShopUiHelper.HeaderText(info), freeze: true);
        ShopUiHelper.OverrideText(descEvent, ShopUiHelper.BuildScoutDescription(info), freeze: true);
        ShopUiHelper.ShowLogo(__instance._itemIcon);

        // Track for Tick() — only the on-screen blurb; there is one per page layout.
        if (__instance.isActiveAndEnabled)
        {
            ShopUiHelper.ActiveBlurb = __instance;
            ShopUiHelper.ActiveBlurbInfo = info;
        }
    }
}

/// <summary>
/// Replaces the full-art display with the AP logo: skips the async load and fires
/// <c>_onSetSprite</c> as if it had completed.
/// </summary>
/// <remarks>
/// <c>_iconSource</c> is cleared so a page re-enable can't restart the load. A renamed member
/// (<c>_onLoaded</c> → <c>_onSetSprite</c>, 2026-09-23 update) throws when this method JITs,
/// before the IsEnabled guard — so it breaks unmodded saves too.
/// </remarks>
[HarmonyPatch(typeof(Il2CppMonomiPark.SlimeRancher.UI.Framework.Components.UIImageDynamicLoader),
    nameof(Il2CppMonomiPark.SlimeRancher.UI.Framework.Components.UIImageDynamicLoader.SetFullArtSource))]
internal static class ShopFullArtPatch
{
    private static bool Prefix(
        Il2CppMonomiPark.SlimeRancher.UI.Framework.Components.UIImageDynamicLoader __instance,
        Il2CppMonomiPark.SlimeRancher.IFullArtAndIconSource source)
    {
        if (!ShopPatchState.IsEnabled) return true;

        // The source is either the ShopRuntimeItem or its asset.
        var info = ShopPatchState.GetActiveLocation(source?.TryCast<Il2CppMonomiPark.SlimeRancher.Shop.Runtime.ShopRuntimeItem>());
        if (info == null)
            info = ShopPatchState.GetActiveLocationByAssetName(source?.TryCast<UnityEngine.Object>()?.name);
        if (info == null) return true;

        var logo = OptionsMenuInjectionPatch.GetLogoSprite();
        if (logo == null) return true;

        try
        {
            __instance._iconSource = null;
            __instance._onSetSprite?.Invoke(logo);
        }
        catch (System.Exception ex)
        {
            Logger.Warning($"[AP] Shop: full-art logo injection failed for '{info.EntryName}': {ex.Message}");
            return true;
        }
        return false; // skip the vanilla full-art load
    }
}

/// <summary>Shows the scouted item in the purchase popup for AP slots.</summary>
[HarmonyPatch(typeof(ShopPurchasePopup), nameof(ShopPurchasePopup.Repopulate))]
internal static class ShopPurchasePopupPatch
{
    private static void Postfix(ShopPurchasePopup __instance)
    {
        if (!ShopPatchState.IsEnabled) return;

        var info = ShopPatchState.GetActiveLocation(__instance._itemToBuy);
        var nameEvent  = (UnityEngine.MonoBehaviour?)__instance._itemNameLabel;
        var blurbEvent = (UnityEngine.MonoBehaviour?)__instance._infoBlurbLabel;

        if (info == null)
        {
            ShopUiHelper.OverrideText(nameEvent,  null, freeze: false);
            ShopUiHelper.OverrideText(blurbEvent, null, freeze: false);
            if (ShopUiHelper.ActivePopup == __instance) ShopUiHelper.ActivePopup = null;
            return;
        }

        ShopUiHelper.OverrideText(nameEvent, ShopUiHelper.HeaderText(info), freeze: true);
        ShopUiHelper.OverrideText(blurbEvent, ShopUiHelper.BuildScoutDescription(info), freeze: true);
        try { __instance._infoBlurbContainer?.SetActive(true); } catch { /* cosmetic */ }

        // The name can still be overwritten after this returns — see ShopUiHelper.ActivePopup.
        ShopUiHelper.ActivePopup = __instance;
    }
}
