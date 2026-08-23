using HarmonyLib;
using Il2CppMonomiPark.SlimeRancher.UI.Popup;

namespace SlimeRancher2AP.Patches.UiPatches;

/// <summary>
/// Tells <see cref="UI.ApPopup"/> exactly when a <c>PreviewPopup</c> binds, and to which
/// view model.
///
/// <para>
/// <c>PreviewPopup</c> instances are <b>pooled</b> — the stack owns a small fixed array and
/// rebinds them for every popup the game shows. ApPopup displays mod text by
/// disabling the <c>LocalizeStringEvent</c> on each label and writing TMP text directly, and
/// without this hook that freeze was never undone: the next vanilla popup reused the same
/// instance, reset its icon via <c>SetData</c>, and rendered the game's sprite next to the last
/// AP item's text. (Observed: a Cotton Slime pedia unlock showing "Radiant Projector Blueprint".)
/// </para>
///
/// <para>
/// <c>SetData</c> is CallerCount(1), so it is safe to patch. It is also the only point at which
/// a popup's contents change, which makes it the right place to decide whether this instance
/// should carry our text or have vanilla localization handed back to it.
/// </para>
/// </summary>
[HarmonyPatch(typeof(PreviewPopup), nameof(PreviewPopup.SetData))]
internal static class PreviewPopupSetDataPatch
{
    private static void Postfix(PreviewPopup __instance, PreviewPopupViewModel data)
    {
        try
        {
            UI.ApPopup.OnPopupBound(__instance, data);
        }
        catch (System.Exception ex)
        {
            Logger.Warning($"[AP-Popup] OnPopupBound threw: {ex.Message}");
        }
    }
}
