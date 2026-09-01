using HarmonyLib;

namespace SlimeRancher2AP.Patches.LocationPatches;

/// <summary>
/// Scales <c>PlortDepositor._fillAmount</c> on Awake from the <c>shadow_plort_requirement</c>
/// slot data option, so a Grey Labyrinth shadow door can be made cheaper or dearer to open.
/// 100 (vanilla) is a no-op; the result is always at least 1.
/// </summary>
/// <remarks>
/// <para>
/// <b>Which field.</b> <c>PlortDepositor._fillAmount</c> is the serialized TARGET count — the
/// running total lives on <c>PlortDepositorModel.AmountDeposited</c>, which is save state and
/// must not be touched. This mirrors <see cref="GordoFeedRequirementPatch"/> scaling
/// <c>GordoEat.TargetCount</c> rather than the gordo's current fill.
/// </para>
///
/// <para>
/// <b>Shadow plorts only.</b> Gated on <c>_catchIdentifiableType.name == "ShadowPlort"</c> so
/// the ordinary coloured plort doors in EV / SS / RF keep their vanilla counts. The option is
/// about the cost of the Grey Labyrinth doors specifically.
/// </para>
///
/// <para>
/// <b>Patch-safety note.</b> <c>PlortDepositor</c> has a history of unsafe patch targets — see
/// the header of <c>PlortDepositorPatch.cs</c> for the three that were removed
/// (<c>ActivateOnFill</c>, <c>PlortDepositorModel.Push</c>, <c>OnTriggerEnter</c>). <c>Awake</c>
/// was NOT among them, and carries the same CallerCount(0) / CachedScanResults profile as
/// <c>GordoEat.Awake</c>, which this mod already patches successfully in shipping builds.
/// If this ever does crash the trampoline, the fallback is to set <c>_fillAmount</c> from
/// <c>PlortDoorPoller</c> instead — it already walks the shadow doors, and their
/// <c>PuzzleSlotLockable._depositors</c> array reaches the same objects with no Harmony patch
/// at all.
/// </para>
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
            // Never let a cosmetic scaling failure break door setup.
            Logger.Warning($"[AP] ShadowPlortRequirementPatch threw: {ex.Message}");
        }
    }
}
