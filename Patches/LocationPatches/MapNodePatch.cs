using HarmonyLib;
using Il2CppMonomiPark.SlimeRancher;
using Il2CppMonomiPark.SlimeRancher.UI.Map;
using SlimeRancher2AP.Data;
using SlimeRancher2AP.Utils;

namespace SlimeRancher2AP.Patches.LocationPatches;

/// <summary>
/// Fires a location check when a map node is revealed.
/// <c>MapNodeActivator.Activate()</c> is called when the player discovers a new map node.
/// <para>
/// MapNode GameObjects are all named <c>nodeMapEntry</c> (or <c>nodeMapEntry (1)</c> etc.) —
/// not unique. Identity is determined by world position via <see cref="WorldUtils.PositionKey"/>,
/// stored in <see cref="LocationInfo.GameObjectName"/>.
/// </para>
/// </summary>
[HarmonyPatch(typeof(MapNodeActivator), nameof(MapNodeActivator.Activate))]
internal static class MapNodePatch
{
    private static void Postfix(MapNodeActivator __instance)
    {
#if DEBUG
        SlimeRancher2AP.Utils.DebugTrace.All("MapNodePatch.Postfix — entry");
#endif
        if (!Plugin.Instance.ModEnabled || !Plugin.Instance.SaveManager.HasActiveSession) return;

        // Guard: PlayerModel is null while the world is still loading from save.
        // Activate() is called during scene state restoration to reveal previously-discovered
        // nodes — we skip those and only send checks for genuine new discoveries.
        if (SceneContext.Instance?.PlayerState?._model == null) return;

        var posKey = WorldUtils.PositionKey(__instance.gameObject);
        if (!LocationTable.TryGetByObjectName(posKey, out var info) || info == null)
        {
            Plugin.Instance.Log.LogWarning($"[AP] Unknown MapNode at key '{posKey}' (go='{__instance.gameObject.name}') — run AP-Dump and add to LocationTable");
            return;
        }

        Plugin.Instance.ApClient.SendCheck(info.Id);
    }
}
