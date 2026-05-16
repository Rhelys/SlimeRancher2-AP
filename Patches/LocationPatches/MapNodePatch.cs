using HarmonyLib;
using Il2CppMonomiPark.SlimeRancher.UI.Map;
using SlimeRancher2AP.Data;
using SlimeRancher2AP.Utils;

namespace SlimeRancher2AP.Patches.LocationPatches;

/// <summary>
/// Fires a location check when a map node is first revealed by the player.
///
/// <para>
/// Hook point: <c>MapNodeActivator.SendMapNodeUnlockedAnalyticsEvent()</c> — CallerCount(1).
/// This method fires only on genuine new discoveries; it is NOT called during
/// scene-state restoration (previously-revealed nodes restored from save skip it).
/// This replaces the old <c>MapNodeActivator.Activate()</c> patch, which was
/// CallerCount(0)/virtual and crashed after the 5/13/2026 game update changed its
/// native prologue.
/// </para>
///
/// <para>
/// Identity: posKey via <see cref="WorldUtils.PositionKey"/> — all map node GameObjects
/// share the generic name <c>nodeMapEntry</c>, so world position is the only stable
/// identifier. Stored as <see cref="LocationInfo.GameObjectName"/> in
/// <see cref="LocationTable"/>.
/// </para>
/// </summary>
[HarmonyPatch(typeof(MapNodeActivator), nameof(MapNodeActivator.SendMapNodeUnlockedAnalyticsEvent))]
internal static class MapNodePatch
{
    private static void Postfix(MapNodeActivator __instance)
    {
        if (!Plugin.Instance.ModEnabled || !Plugin.Instance.SaveManager.HasActiveSession) return;

        var posKey = WorldUtils.PositionKey(__instance.gameObject);
        if (!LocationTable.TryGetByObjectName(posKey, out var info) || info == null)
        {
            Logger.Warning(
                $"[AP] Unknown MapNode at key '{posKey}' " +
                $"(go='{__instance.gameObject.name}') — run AP-Dump and add to LocationTable");
            return;
        }

        Plugin.Instance.ApClient.SendCheck(info.Id);
    }
}
