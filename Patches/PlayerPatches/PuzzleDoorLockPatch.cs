using HarmonyLib;
using Il2CppMonomiPark.SlimeRancher;
using Il2CppMonomiPark.SlimeRancher.World;
using SlimeRancher2AP.Data;
using SlimeRancher2AP.Utils;

namespace SlimeRancher2AP.Patches.PlayerPatches;

/// <summary>
/// Shadow plort doors (Grey Labyrinth) send their check on <c>ActivateOnUnlock</c>, which they
/// reach through a managed caller. Other plort doors open from native code — see PlortDoorPoller.
/// Identified by posKey; door names repeat.
/// </summary>
[HarmonyPatch(typeof(PuzzleSlotLockable), "ActivateOnUnlock")]
internal static class PuzzleSlotLockableActivatePatch
{
    private static bool Prefix(PuzzleSlotLockable __instance)
    {
        if (!Plugin.Instance.ModEnabled) return true;
        if (SceneContext.Instance?.PlayerState?._model == null) return true;

        string posKey;
        try { posKey = WorldUtils.PositionKey(__instance.gameObject!); }
        catch { return true; }

#if DEBUG
        Logger.Info($"[AP-PuzzleDoor] ActivateOnUnlock: name='{__instance.gameObject?.name ?? "?"}'  posKey='{posKey}'");
#endif

        if (LocationTable.TryGetByObjectName(posKey, out var locInfo) && locInfo != null
            && locInfo.Type == LocationType.ShadowPlortDoor)
        {
            // Unconditional — randomize_shadow_doors gates only the check (see SlotData).
            SuppressVanillaReward(__instance);

            bool isCheck = string.Equals(
                Plugin.Instance.ApClient.SlotData?.RandomizeShadowDoors, "locations",
                System.StringComparison.OrdinalIgnoreCase);

            if (isCheck)
            {
                Plugin.Instance.ApClient.SendCheck(locInfo.Id);
                Logger.Info($"[AP] Shadow Plort Door check: '{locInfo.Name}' (id={locInfo.Id}) posKey='{posKey}'");
            }
            else
            {
                Logger.Info($"[AP] Shadow Plort Door opened (not a check): '{locInfo.Name}' posKey='{posKey}'");
            }
        }

        return true;
    }

    /// <summary>
    /// Nulls the reward fields of every rewarder the door can fire, as for treasure pods; the
    /// door still opens. Runs before ActivateOnUnlock, so before any of it is switched on.
    /// Two sources:
    /// <list type="bullet">
    /// <item><c>_rewardOnUnlock</c> — the door's own rewarder.</item>
    /// <item>A <c>TreasurePodRewarder</c> under an <c>_activateOnUnlock</c> object — it pays out
    /// from its own OnEnable when the door activates it. Shadow Plort Door 2 (Waterworks
    /// glass-button puzzle) gives its Storage Cell this way, with <c>_rewardOnUnlock</c> null.</item>
    /// </list>
    /// A rewarder belonging to a treasure pod is left alone: that pod is its own location.
    /// </summary>
    private static void SuppressVanillaReward(PuzzleSlotLockable door)
    {
        int suppressed = 0;
        try
        {
            if (door._rewardOnUnlock != null)
            {
                Clear(door._rewardOnUnlock);
                suppressed++;
                Logger.Info("[AP] Shadow Plort Door vanilla reward suppressed (_rewardOnUnlock)");
            }

            var activated = door._activateOnUnlock;
            if (activated != null)
            {
                for (int i = 0; i < activated.Length; i++)
                {
                    var go = activated[i];
                    if (go == null) continue;
                    var rewarders = go.GetComponentsInChildren<TreasurePodRewarder>(true);
                    for (int j = 0; j < rewarders.Length; j++)
                    {
                        var r = rewarders[j];
                        if (r == null || r.GetComponentInParent<TreasurePod>(true) != null) continue;
                        Clear(r);
                        suppressed++;
                        Logger.Info(
                            $"[AP] Shadow Plort Door vanilla reward suppressed (activated object '{go.name}' → '{r.gameObject.name}')");
                    }
                }
            }

            if (suppressed == 0)
            {
                // Something still pays out the reward; name the objects to look at next.
                var names = new System.Collections.Generic.List<string>();
                if (activated != null)
                    for (int i = 0; i < activated.Length; i++)
                        names.Add(activated[i] != null ? activated[i].name : "<null>");
                Logger.Warning(
                    "[AP] Shadow Plort Door: no rewarder found to suppress. _activateOnUnlock=["
                    + string.Join(", ", names) + "]");
            }
        }
        catch (System.Exception ex)
        {
            Logger.Warning($"[AP] Shadow Plort Door reward suppression failed: {ex.Message}");
        }
    }

    private static void Clear(TreasurePodRewarder rewarder)
    {
        rewarder.Blueprint                          = null;
        rewarder.UpgradeComponent                   = null;
        rewarder.SpawnObjs                          = null;
        rewarder.UnlockedSlimeAppearance            = null;
        rewarder.UnlockedSlimeAppearanceDefinition  = null;
    }
}

// Don't patch PuzzleDoorLock, PuzzleGateActivator, or PuzzleSlotLockable's Notify /
// SendAnalytics: they crashed the trampoline after the 5/13/2026 update. Nor PlortDepositor:
//   • ActivateOnFill — infinite recursion through the managed bridge.
//   • PlortDepositorModel.Push — called natively during scene init; the trampoline fails.
//   • OnTriggerEnter — trampoline crash on scene load since the 5/13/2026 update.
// Plort doors other than the shadow doors, and the PB gate, are PlortDoorPoller's.
