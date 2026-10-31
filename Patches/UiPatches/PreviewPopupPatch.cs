using HarmonyLib;
using Il2CppMonomiPark.SlimeRancher.UI.Popup;

namespace SlimeRancher2AP.Patches.UiPatches;

/// <summary>
/// Tells <see cref="UI.ApPopup"/> when a pooled popup binds — the one point its contents change
/// — so a frozen label never leaks into the next vanilla popup. CallerCount(1), safe.
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
