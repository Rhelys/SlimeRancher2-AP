using HarmonyLib;

namespace SlimeRancher2AP.Patches.LocationPatches;

/// <summary><c>gordo_feed_requirement</c>: scales <c>GordoEat.TargetCount</c> on Awake (at least 1).</summary>
[HarmonyPatch(typeof(GordoEat), "Awake")]
internal static class GordoFeedRequirementPatch
{
    private static void Postfix(GordoEat __instance)
    {
        var pct = Plugin.Instance.ApClient?.SlotData?.GordoFeedRequirement ?? 100;
        if (pct == 100) return;

        int original = __instance.TargetCount;
        __instance.TargetCount = System.Math.Max(1, (int)System.Math.Ceiling(original * pct / 100.0));
    }
}
