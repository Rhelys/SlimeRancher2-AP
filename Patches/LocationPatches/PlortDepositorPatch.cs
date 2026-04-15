using HarmonyLib;
using Il2CppMonomiPark.SlimeRancher;
using SlimeRancher2AP.Data;
using SlimeRancher2AP.Utils;

namespace SlimeRancher2AP.Patches.LocationPatches;

/// <summary>
/// Intercepts <c>PlortDepositor.ActivateOnFill()</c> to detect when the player fully fills a
/// Shadow Plort door in the Grey Labyrinth. <c>PlortDepositor</c> is a general-purpose class
/// used for all plort-deposit locks in the game, so we filter to Shadow Plorts only.
/// </summary>
/// <remarks>
/// All 25 Shadow Plort doors share the same <c>gameObject.name</c> ("TriggerActivate"), so
/// lookup uses <c>WorldUtils.PositionKey()</c> — the same posKey scheme as TreasurePods.
/// The posKeys are stored in <c>LocationInfo.GameObjectName</c>.
/// </remarks>
[HarmonyPatch(typeof(PlortDepositor), nameof(PlortDepositor.ActivateOnFill))]
internal static class PlortDepositorPatch
{
    private static void Postfix(PlortDepositor __instance)
    {
#if DEBUG
        SlimeRancher2AP.Utils.DebugTrace.Once("PlortDepositorPatch.Postfix — first entry");
#endif
        // Only fire for Shadow Plort doors (Grey Labyrinth) — PlortDepositor is reused elsewhere.
        // Wrap in try/catch: _catchIdentifiableType.name can crash on partially-initialised
        // IL2CPP objects during scene state restoration.
        bool isShadowPlort;
        try { isShadowPlort = __instance._catchIdentifiableType?.name == "ShadowPlort"; }
        catch { return; }
        if (!isShadowPlort) return;

        if (!Plugin.Instance.ModEnabled || !Plugin.Instance.SaveManager.HasActiveSession) return;

        // Guard: PlayerModel is null while the world is still loading from save.
        // ActivateOnFill() fires during restoration for previously-filled doors — skip those.
        if (SceneContext.Instance?.PlayerState?._model == null) return;

        var posKey = WorldUtils.PositionKey(__instance.gameObject);
        Plugin.Instance.Log.LogInfo($"[AP] Shadow Plort Door filled: posKey='{posKey}'");

        if (!LocationTable.TryGetByObjectName(posKey, out var info) || info == null)
        {
            Plugin.Instance.Log.LogWarning($"[AP] Unknown Shadow Plort Door at posKey='{posKey}' — add to LocationTable");
            return;
        }

        Plugin.Instance.ApClient.SendCheck(info.Id);
    }
}
