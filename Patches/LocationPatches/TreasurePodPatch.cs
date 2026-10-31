using HarmonyLib;
using Il2CppMonomiPark.SlimeRancher;
using Il2CppMonomiPark.SlimeRancher.UI;
using Il2CppMonomiPark.SlimeRancher.World;
using SlimeRancher2AP.Data;
using SlimeRancher2AP.Utils;

namespace SlimeRancher2AP.Patches.LocationPatches;

/// <summary>
/// Treasure pods send a check instead of their reward. The pod still opens normally — its
/// reward fields are cleared first, so the delayed award grants nothing. Pods are identified by
/// position (<see cref="WorldUtils.PositionKey"/>): their names describe contents, not identity.
/// Regular pods open through <c>TreasurePodUIInteractable</c>; ghostly drone nodes still call
/// <c>TreasurePod.Activate()</c>.
/// </summary>

// ─────────────────────────────────────────────────────────────────────────────
// Regular treasure pods — TreasurePodUIInteractable.OnInteract()
// ─────────────────────────────────────────────────────────────────────────────

[HarmonyPatch(typeof(TreasurePodUIInteractable), nameof(TreasurePodUIInteractable.OnInteract))]
internal static class TreasurePodUIInteractablePatch
{
    private static void Prefix(TreasurePodUIInteractable __instance)
    {
        if (!Plugin.Instance.ModEnabled || !Plugin.Instance.SaveManager.IsSaveBound)
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
            Logger.Warning(
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
// Ghostly drone nodes — TreasurePod.Activate()
// ─────────────────────────────────────────────────────────────────────────────

[HarmonyPatch(typeof(TreasurePod), nameof(TreasurePod.Activate))]
internal static class TreasurePodActivatePatch
{
    private static bool Prefix(TreasurePod __instance)
    {
        if (!__instance.gameObject.name.StartsWith("node"))
            return true;

        if (!Plugin.Instance.ModEnabled || !Plugin.Instance.SaveManager.IsSaveBound)
            return true;
        if (SceneContext.Instance?.PlayerState?._model == null)
            return true;
        if (Plugin.Instance.ApClient.SlotData?.RandomizeGhostlyDrones != true)
            return true;

        var posKey = WorldUtils.PositionKey(__instance.gameObject);
        if (!LocationTable.TryGetByObjectName(posKey, out var info) || info == null)
        {
            Logger.Warning(
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
// Zeroes every reward field, so the award grants nothing.
// ─────────────────────────────────────────────────────────────────────────────

file static class TreasurePodRewardSuppressor
{
    internal static void SuppressVanillaRewards(TreasurePod pod)
    {
        // Some pods reward through a child TreasurePodRewarder…
        var rewarder = pod.GetComponentInChildren<TreasurePodRewarder>(true);
        if (rewarder != null)
        {
            rewarder.Blueprint                          = null;
            rewarder.UpgradeComponent                  = null;
            rewarder.SpawnObjs                         = null;
            rewarder.UnlockedSlimeAppearance           = null;
            rewarder.UnlockedSlimeAppearanceDefinition = null;
        }

        // …others from fields on the pod itself.
        pod.Blueprint                          = null;
        pod.UpgradeComponent                   = null;
        pod.SpawnObjs                          = null;
        pod.UnlockedSlimeAppearance            = null;
        pod.UnlockedSlimeAppearanceDefinition  = null;
    }
}
