using HarmonyLib;
using Il2CppMonomiPark.SlimeRancher;
using SlimeRancher2AP.Data;

namespace SlimeRancher2AP.Patches.LocationPatches;

/// <summary>A gordo popping sends its check.</summary>
[HarmonyPatch(typeof(GordoEat), nameof(GordoEat.ImmediateReachedTarget))]
internal static class GordoPatch
{
    private static void Postfix(GordoEat __instance)
    {
#if DEBUG
        SlimeRancher2AP.Utils.DebugTrace.All("GordoPatch.Postfix — entry");
#endif
        if (!Plugin.Instance.ModEnabled || !Plugin.Instance.SaveManager.IsSaveBound) return;

        // Off = not in the seed. Without slot data (offline) record it anyway rather than lose it.
        var slotData = Plugin.Instance.ApClient.SlotData;
        if (slotData != null && !slotData.RandomizeGordos) return;

        // Still loading from save.
        if (SceneContext.Instance?.PlayerState?._model == null) return;

        // No valid scene = a GordoSnare template, not a world gordo.
        string gordoName, sceneName;
        try
        {
            gordoName = __instance.gameObject.name;
            var scene = __instance.gameObject.scene;
            sceneName = scene.IsValid() ? (scene.name ?? "") : "";
        }
        catch { return; }

        if (string.IsNullOrEmpty(sceneName)) return;

        if (!LocationTable.TryGetByObjectName(gordoName, out var info) || info == null)
        {
            Logger.Warning($"[AP] Unknown static Gordo: '{gordoName}' (scene='{sceneName}') — add to LocationTable");
            return;
        }

        Plugin.Instance.ApClient.SendCheck(info.Id);
    }
}
