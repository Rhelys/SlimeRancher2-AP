using HarmonyLib;
using Il2CppMonomiPark.SlimeRancher;
using Il2CppMonomiPark.SlimeRancher.UI;
using Il2CppMonomiPark.SlimeRancher.World;
using SlimeRancher2AP.Data;
using SlimeRancher2AP.Utils;

namespace SlimeRancher2AP.Patches.LocationPatches;

/// <summary>
/// Intercepts treasure pod activation to suppress the vanilla item grant and send an AP check,
/// while still allowing the pod to open visually.
///
/// <para>
/// Strategy: instead of blocking the interaction entirely (which would keep the pod sealed),
/// we let it run — which drives the open animation and model state change — but first clear all
/// reward fields on the <c>TreasurePodRewarder</c> component so that when the opening coroutine
/// fires <c>TreasurePodUtil.AwardPrizesDefault()</c> after its delay, all reward parameters
/// are null and nothing is granted in-game. The AP server delivers the randomised item instead.
/// </para>
///
/// <para>
/// TreasurePod GameObjects do NOT have unique names — they are named after their contents
/// (e.g., "treasurePod Rank1 HeartModuleComponent"). Identity is determined by world position
/// via <see cref="WorldUtils.PositionKey"/>, stored in <see cref="LocationInfo.GameObjectName"/>.
/// </para>
///
/// <para>
/// As of the SR2 update that added <c>TreasurePodUIInteractable</c>, the player interaction
/// entry point is <c>TreasurePodUIInteractable.OnInteract()</c>, which no longer calls
/// <c>TreasurePod.Activate()</c> directly. Regular pods are now patched via
/// <see cref="TreasurePodUIInteractablePatch"/>. The <c>Activate()</c> patch below is kept
/// exclusively for ghostly drone nodes (<c>nodeComponentAcqDrone</c>) which extend
/// <c>TreasurePod</c> but do not use <c>TreasurePodUIInteractable</c>.
/// </para>
/// </summary>

// ─────────────────────────────────────────────────────────────────────────────
// Regular treasure pods — patched via TreasurePodUIInteractable.OnInteract()
// (added in the SR2 update that decoupled the interaction UI from the pod itself)
// ─────────────────────────────────────────────────────────────────────────────

[HarmonyPatch(typeof(TreasurePodUIInteractable), nameof(TreasurePodUIInteractable.OnInteract))]
internal static class TreasurePodUIInteractablePatch
{
    private static void Prefix(TreasurePodUIInteractable __instance)
    {
        if (!Plugin.Instance.ModEnabled || !Plugin.Instance.SaveManager.HasActiveSession)
            return;
        if (SceneContext.Instance?.PlayerState?._model == null)
            return;
        if (Plugin.Instance.ApClient.SlotData?.RandomizePods != true)
            return;

        var pod = __instance.treasurePod;
        if (pod == null) return;

        var posKey = WorldUtils.PositionKey(pod.gameObject);
        if (!LocationTable.TryGetByObjectName(posKey, out var info) || info == null)
        {
            Plugin.Instance.Log.LogWarning(
                $"[AP] Unknown TreasurePod at key '{posKey}' (go='{pod.gameObject.name}') — run AP-Dump and add to LocationTable");
            return;   // unknown pod — fall back to vanilla so the player isn't stuck
        }

        if (Plugin.Instance.SaveManager.IsChecked(info.Id))
            return;   // already checked (scene-state restore) — vanilla grant is safe

        TreasurePodRewardSuppressor.SuppressVanillaRewards(pod);
        Plugin.Instance.ApClient.SendCheck(info.Id);
    }
}

// ─────────────────────────────────────────────────────────────────────────────
// Ghostly drone nodes — these extend TreasurePod but use their own interaction
// path that still calls TreasurePod.Activate() directly (not UIInteractable).
// ─────────────────────────────────────────────────────────────────────────────

[HarmonyPatch(typeof(TreasurePod), nameof(TreasurePod.Activate))]
internal static class TreasurePodActivatePatch
{
    private static bool Prefix(TreasurePod __instance)
    {
        // Only handle ghostly drone nodes here — regular pods are handled above.
        if (!__instance.gameObject.name.StartsWith("node"))
            return true;

        if (!Plugin.Instance.ModEnabled || !Plugin.Instance.SaveManager.HasActiveSession)
            return true;
        if (SceneContext.Instance?.PlayerState?._model == null)
            return true;
        if (Plugin.Instance.ApClient.SlotData?.RandomizeGhostlyDrones != true)
            return true;

        var posKey = WorldUtils.PositionKey(__instance.gameObject);
        if (!LocationTable.TryGetByObjectName(posKey, out var info) || info == null)
        {
            Plugin.Instance.Log.LogWarning(
                $"[AP] Unknown ghostly drone node at key '{posKey}' (go='{__instance.gameObject.name}')");
            return true;
        }

        if (Plugin.Instance.SaveManager.IsChecked(info.Id))
            return true;

        TreasurePodRewardSuppressor.SuppressVanillaRewards(__instance);
        Plugin.Instance.ApClient.SendCheck(info.Id);
        return true;   // let Activate() run so the drone node opens visually
    }
}

// ─────────────────────────────────────────────────────────────────────────────
// Shared helper — zeroes all reward fields on TreasurePod and its TreasurePodRewarder
// child so that TreasurePodUtil.AwardPrizesDefault grants nothing when it fires.
// ─────────────────────────────────────────────────────────────────────────────

file static class TreasurePodRewardSuppressor
{
    internal static void SuppressVanillaRewards(TreasurePod pod)
    {
        // Child TreasurePodRewarder (used by some pods)
        var rewarder = pod.GetComponentInChildren<TreasurePodRewarder>(true);
        if (rewarder != null)
        {
            rewarder.Blueprint                          = null;
            rewarder.UpgradeComponent                  = null;
            rewarder.SpawnObjs                         = null;
            rewarder.UnlockedSlimeAppearance           = null;
            rewarder.UnlockedSlimeAppearanceDefinition = null;
        }

        // Fields on TreasurePod itself (direct-coroutine path, used by other pods)
        pod.Blueprint                          = null;
        pod.UpgradeComponent                   = null;
        pod.SpawnObjs                          = null;
        pod.UnlockedSlimeAppearance            = null;
        pod.UnlockedSlimeAppearanceDefinition  = null;
    }
}
