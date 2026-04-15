using HarmonyLib;
using Il2CppMonomiPark.SlimeRancher;
using Il2CppMonomiPark.SlimeRancher.World;
using SlimeRancher2AP.Data;
using SlimeRancher2AP.Utils;

namespace SlimeRancher2AP.Patches.LocationPatches;

/// <summary>
/// Intercepts treasure pod activation to suppress the vanilla item grant and send an AP check,
/// while still allowing the pod to open visually.
///
/// <para>
/// Strategy: instead of blocking <c>Activate()</c> entirely (which would keep the pod sealed),
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
/// Ghostly drone pickup nodes (<c>nodeComponentAcqDrone</c>) also extend <c>TreasurePod</c> and
/// are caught here. They use the same position-key lookup and are stored as
/// <see cref="LocationType.GhostlyDrone"/> entries in <see cref="LocationTable"/>.
/// </para>
/// </summary>
[HarmonyPatch(typeof(TreasurePod), nameof(TreasurePod.Activate))]
internal static class TreasurePodPatch
{
    private static bool Prefix(TreasurePod __instance)
    {
#if DEBUG
        SlimeRancher2AP.Utils.DebugTrace.All("TreasurePodPatch.Prefix — entry");
#endif
        if (!Plugin.Instance.ModEnabled || !Plugin.Instance.SaveManager.HasActiveSession)
            return true;   // not in AP mode — let vanilla grant the item
#if DEBUG
        SlimeRancher2AP.Utils.DebugTrace.All("TreasurePodPatch.Prefix — past ModEnabled+HasActiveSession");
#endif

        // Guard: PlayerModel is null until the save data has been fully applied.
        // While it's null the world is still loading and Activate() calls are scene-state
        // restoration, not genuine player interactions — let vanilla run so the pod's
        // visual/physics state restores correctly without risking IL2CPP crashes on
        // partially-initialised game objects.
        if (SceneContext.Instance?.PlayerState?._model == null)
            return true;

        // Slot-data guards — if the relevant category is not randomized, skip AP interception.
        // Ghostly Drone nodes extend TreasurePod and are caught by this same patch;
        // they need their own flag check before the posKey lookup.
        bool isNodeDrone = __instance.gameObject.name.StartsWith("node");
        var slotData = Plugin.Instance.ApClient.SlotData;
        if (isNodeDrone)
        {
            if (slotData?.RandomizeGhostlyDrones != true) return true;
        }
        else
        {
            if (slotData?.RandomizePods != true) return true;
        }
#if DEBUG
        SlimeRancher2AP.Utils.DebugTrace.All("TreasurePodPatch.Prefix — past _model guard");
#endif

        var posKey = WorldUtils.PositionKey(__instance.gameObject);
#if DEBUG
        SlimeRancher2AP.Utils.DebugTrace.All($"TreasurePodPatch.Prefix — posKey='{posKey}'");
#endif
        if (!LocationTable.TryGetByObjectName(posKey, out var info) || info == null)
        {
            Plugin.Instance.Log.LogWarning(
                $"[AP] Unknown TreasurePod at key '{posKey}' (go='{__instance.gameObject.name}') — run AP-Dump and add to LocationTable");
            return true;   // unknown pod — fall back to vanilla so the player isn't stuck
        }

        // If this pod was already checked in a previous session, this Activate() call is
        // scene-state restoration (SR2 replaying the opened state from the save file).
        // The TreasurePodRewarder component may not exist on an already-opened pod, and
        // SendCheck will no-op anyway — just let vanilla restore the visual state.
        if (Plugin.Instance.SaveManager.IsChecked(info.Id))
            return true;

        // Suppress the vanilla item grant by zeroing the rewarder's reward fields.
        // TreasurePodRewarder.OnEnableCoroutine passes these to TreasurePodUtil.AwardPrizesDefault
        // after an openDelay; clearing them here (before the coroutine reads them) means the
        // coroutine will grant nothing. The pod still opens visually because Activate() runs.
#if DEBUG
        SlimeRancher2AP.Utils.DebugTrace.All($"TreasurePodPatch.Prefix — known pod id={info.Id}, getting rewarder");
#endif
        // Suppress vanilla item grant by clearing all reward fields.
        // Some pods use a TreasurePodRewarder child component; others drive
        // TreasurePodUtil.AwardPrizesDefault directly from TreasurePod.OnEnableCoroutine
        // using fields stored on the TreasurePod itself. We must null both paths.
        var rewarder = __instance.GetComponentInChildren<TreasurePodRewarder>(true);
        if (rewarder != null)
        {
            rewarder.Blueprint                          = null;
            rewarder.UpgradeComponent                  = null;
            rewarder.SpawnObjs                         = null;
            rewarder.UnlockedSlimeAppearance           = null;
            rewarder.UnlockedSlimeAppearanceDefinition = null;
        }

        // Always also null the fields on TreasurePod itself — the direct-coroutine path
        // reads these regardless of whether a TreasurePodRewarder component is present.
        __instance.Blueprint                          = null;
        __instance.UpgradeComponent                  = null;
        __instance.SpawnObjs                         = null;
        __instance.UnlockedSlimeAppearance           = null;
        __instance.UnlockedSlimeAppearanceDefinition = null;

        Plugin.Instance.ApClient.SendCheck(info.Id);
        return true;   // let Activate() run so the pod opens visually
    }
}
