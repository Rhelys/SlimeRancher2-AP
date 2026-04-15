using HarmonyLib;
using Il2CppMonomiPark.SlimeRancher;
using SlimeRancher2AP.Data;

namespace SlimeRancher2AP.Patches.LocationPatches;

/// <summary>
/// Fires a location check when a gordo slime pops.
/// Postfix on GordoEat.ImmediateReachedTarget() — confirmed from SR2PopAllGordos.
/// </summary>
[HarmonyPatch(typeof(GordoEat), nameof(GordoEat.ImmediateReachedTarget))]
internal static class GordoPatch
{
    private static void Postfix(GordoEat __instance)
    {
#if DEBUG
        SlimeRancher2AP.Utils.DebugTrace.All("GordoPatch.Postfix — entry");
#endif
        if (!Plugin.Instance.ModEnabled || !Plugin.Instance.SaveManager.HasActiveSession) return;

        // Guard: PlayerModel is null while the world is still loading from save.
        if (SceneContext.Instance?.PlayerState?._model == null) return;

        // Gordos with an empty/invalid scene name are GordoSnare prefab templates loaded globally —
        // NOT static world gordos. Only send checks for static gordos with a real scene.
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
            Plugin.Instance.Log.LogWarning($"[AP] Unknown static Gordo: '{gordoName}' (scene='{sceneName}') — add to LocationTable");
            return;
        }

        Plugin.Instance.ApClient.SendCheck(info.Id);
    }
}
