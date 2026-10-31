using HarmonyLib;

namespace SlimeRancher2AP.Patches.LocationPatches;

/// <summary>
/// <c>shadow_plort_requirement</c>: scales shadow plort depositors' <c>_fillAmount</c> (the
/// target — never the saved running total) on Awake. Other plort doors keep vanilla counts.
/// </summary>
/// <remarks>
/// Awake has the same profile as the safely patched <c>GordoEat.Awake</c>. Should it ever crash
/// the trampoline, set <c>_fillAmount</c> from PlortDoorPoller instead, which reaches the same
/// depositors with no patch.
/// </remarks>
[HarmonyPatch(typeof(PlortDepositor), "Awake")]
internal static class ShadowPlortRequirementPatch
{
    private static void Postfix(PlortDepositor __instance)
    {
        try
        {
            var pct = Plugin.Instance.ApClient?.SlotData?.ShadowPlortRequirement ?? 100;
            if (pct == 100) return;

            var catchType = __instance._catchIdentifiableType;
            if (catchType == null || catchType.name != "ShadowPlort") return;

            int original = __instance._fillAmount;
            if (original <= 0) return;

            int scaled = System.Math.Max(1, (int)System.Math.Ceiling(original * pct / 100.0));
            if (scaled == original) return;

            __instance._fillAmount = scaled;
            Logger.Info(
                $"[AP] Shadow plort door '{__instance.gameObject?.name ?? "?"}': " +
                $"required {original} → {scaled} ({pct}%)");
        }
        catch (System.Exception ex)
        {
            Logger.Warning($"[AP] ShadowPlortRequirementPatch threw: {ex.Message}");
        }
    }
}
