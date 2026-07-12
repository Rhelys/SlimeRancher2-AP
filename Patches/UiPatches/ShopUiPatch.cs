using HarmonyLib;
using Il2CppMonomiPark.SlimeRancher.UI.Shop;
using Il2CppMonomiPark.SlimeRancher.UI.Shop.Pages;
using SlimeRancher2AP.Data;
using SlimeRancher2AP.Patches.LocationPatches;
using TMPro;

namespace SlimeRancher2AP.Patches.UiPatches;

/// <summary>
/// Shared helpers for the Polestar Provisions shop UI overrides. Mirrors the
/// FabricatorDetailsPatch technique: freeze the <c>LocalizeStringEvent</c> driving a text
/// element (so localization ticks don't overwrite us) and write to its TMP_Text directly;
/// re-enable it whenever the element binds a non-AP item again (buttons are pooled and
/// reused across items while scrolling).
/// </summary>
internal static class ShopUiHelper
{
    /// <summary>
    /// The scouted AP item name at this location — shown as the primary item name in the
    /// details panel and purchase popup. Falls back to a generic label until scout data
    /// arrives (scouting runs async right after connect).
    /// </summary>
    internal static string HeaderText(LocationInfo info)
        => Plugin.Instance.ApClient.GetScoutedItem(info.Id)?.ItemName ?? "Archipelago Check";

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

        string line = $"An Archipelago check for {owner}";
        return sent ? $"{line}\n(Check sent)" : line;
    }

    /// <summary>
    /// Freezes <paramref name="localizeEvent"/> (a LocalizeStringEvent, typed as
    /// MonoBehaviour to avoid coupling) and writes <paramref name="text"/> to its TMP_Text.
    /// Pass <c>freeze: false</c> to restore vanilla localization instead.
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

    /// <summary>
    /// Swaps <paramref name="image"/> to the Archipelago logo — the vanilla sprite
    /// advertises a decoration the player is not receiving. Same logo as the conversation
    /// callout and Options-menu tab (<c>OptionsMenuInjectionPatch.GetLogoSprite()</c>).
    /// </summary>
    internal static void ShowLogo(UnityEngine.UI.Image? image)
    {
        var logo = OptionsMenuInjectionPatch.GetLogoSprite();
        if (logo == null || image == null) return;
        image.sprite         = logo;
        image.preserveAspect = true;
    }

    /// <summary>
    /// Restores the vanilla item sprite on <paramref name="image"/> if it is currently
    /// showing our logo. Needed because shop UI elements are pooled and re-bound across
    /// items — vanilla code doesn't always re-assign the icon on rebind, so a logo set for
    /// an AP slot could otherwise linger on a vanilla item.
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

    // The blurb currently displaying an AP check slot, and the location it shows.
    // The big item image (_itemIcon) is fed by the item's FullArtReference, which loads
    // asynchronously — its completion callback overwrites the sprite AFTER our
    // UpdateDisplay Postfix runs. The callback path (HandleItemLoadCompleted) is
    // CallerCount(0) and unsafe to patch, so ShopUiHelper.Tick() re-asserts the logo
    // instead while an AP slot is on display.
    internal static ShopItemDescriptionBlurb? ActiveBlurb;
    internal static LocationInfo?             ActiveBlurbInfo;

    private static int _tickCounter;

    /// <summary>Called every frame from <c>Plugin.Update</c>; does work every ~15 frames.</summary>
    internal static void Tick()
    {
        if (ActiveBlurb == null || ActiveBlurbInfo == null) return;
        if (++_tickCounter < 15) return;
        _tickCounter = 0;

        try
        {
            var blurb = ActiveBlurb;
            // Unity-null / closed panel / re-bound to a different item → stop tracking.
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
}

/// <summary>
/// Marks AP-check slots in the shop grid: "[AP]" name prefix, plus the sold-out overlay
/// once the check has been sent. Patched method:
/// <c>ShopItemButton.RepopulateDisplay()</c> — CallerCount(3), runs on every (re)bind and
/// on purchase/load refreshes.
/// </summary>
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
            // Pooled button re-bound to a vanilla item — restore localization and icon.
            ShopUiHelper.OverrideText(nameEvent, null, freeze: false);
            ShopUiHelper.RestoreVanillaIcon(__instance._icon, item, preferSmall: true);
            return;
        }

        string title;
        try { title = item!.Title?.GetLocalizedString() ?? info.EntryName ?? info.Name; }
        catch { title = info.EntryName ?? info.Name; }
        ShopUiHelper.OverrideText(nameEvent, $"[AP] {title}", freeze: true);
        ShopUiHelper.ShowLogo(__instance._icon);

        if (ShopPatchState.IsChecked(info))
        {
            try { __instance._soldOutDisplay?.SetActive(true); } catch { /* cosmetic */ }
        }
    }
}

/// <summary>
/// Shows the scouted Archipelago item in the shop's right-hand details panel for AP-check
/// slots. Patched method: <c>ShopItemDescriptionBlurb.UpdateDisplay()</c> — CallerCount(6).
/// </summary>
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

        // Primary name = the scouted AP item ("Uncommon Plort Cache"), description = who
        // it belongs to. The vanilla item name stays visible on the grid button ("[AP] …").
        ShopUiHelper.OverrideText(headerEvent, ShopUiHelper.HeaderText(info), freeze: true);
        ShopUiHelper.OverrideText(descEvent, ShopUiHelper.BuildScoutDescription(info), freeze: true);
        ShopUiHelper.ShowLogo(__instance._itemIcon);

        // Track for Tick(): the FullArtReference async load overwrites _itemIcon after
        // this Postfix, so the logo must be re-asserted while this slot is on display.
        // Only track the on-screen instance — the shop keeps one blurb per page layout
        // (6 total) and they all run UpdateDisplay; tracking an off-screen one would make
        // Tick() see !isActiveAndEnabled, drop tracking, and never re-assert the real one.
        if (__instance.isActiveAndEnabled)
        {
            ShopUiHelper.ActiveBlurb = __instance;
            ShopUiHelper.ActiveBlurbInfo = info;
        }
    }
}

/// <summary>
/// Replaces the big full-art display with the Archipelago logo for AP-check slots.
/// Patched method: <c>UIImageDynamicLoader.SetFullArtSource(IFullArtAndIconSource source)</c>
/// — CallerCount(5), called when the details panel binds an item that has full art.
/// </summary>
/// <remarks>
/// The full art is NOT the blurb's <c>_itemIcon</c> — it is loaded asynchronously by this
/// framework component from the item's <c>FullArtReference</c> addressable and delivered to
/// the actual renderer via the <c>_onLoaded</c> UnityEvent (confirmed via in-game UI dump:
/// no scene Image ever holds the art directly). For AP slots we skip the vanilla load
/// entirely and fire <c>_onLoaded</c> with the logo, mimicking a completed load.
/// <c>_iconSource</c> is cleared so a later <c>TryAcquireCurrentHandle</c> (page re-enable)
/// cannot restart the vanilla art load.
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

        // The source may be the ShopRuntimeItem itself or the underlying asset — resolve
        // the AP location through whichever identity is available.
        var info = ShopPatchState.GetActiveLocation(source?.TryCast<Il2CppMonomiPark.SlimeRancher.Shop.Runtime.ShopRuntimeItem>());
        if (info == null)
            info = ShopPatchState.GetActiveLocationByAssetName(source?.TryCast<UnityEngine.Object>()?.name);
        if (info == null) return true;

        var logo = OptionsMenuInjectionPatch.GetLogoSprite();
        if (logo == null) return true; // no logo available — better vanilla art than nothing

        try
        {
            __instance._iconSource = null;
            __instance._onLoaded?.Invoke(logo);
        }
        catch (System.Exception ex)
        {
            Logger.Warning($"[AP] Shop: full-art logo injection failed for '{info.EntryName}': {ex.Message}");
            return true;
        }
        return false; // skip the vanilla full-art load
    }
}

/// <summary>
/// Shows the scouted Archipelago item in the purchase confirmation popup for AP-check
/// slots (info blurb line under the item name). Patched method:
/// <c>ShopPurchasePopup.Repopulate()</c> — CallerCount(3).
/// </summary>
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
            return;
        }

        ShopUiHelper.OverrideText(nameEvent, ShopUiHelper.HeaderText(info), freeze: true);
        ShopUiHelper.OverrideText(blurbEvent, ShopUiHelper.BuildScoutDescription(info), freeze: true);
        try { __instance._infoBlurbContainer?.SetActive(true); } catch { /* cosmetic */ }
    }
}
