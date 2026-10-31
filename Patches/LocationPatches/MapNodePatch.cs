using HarmonyLib;
using Il2CppMonomiPark.SlimeRancher.UI.Map;
using SlimeRancher2AP.Data;
using SlimeRancher2AP.Utils;

namespace SlimeRancher2AP.Patches.LocationPatches;

/// <summary>
/// A map node's first reveal sends its check, keyed by posKey (every node is named
/// <c>nodeMapEntry</c>). Hooks the unlock analytics event, which fires only for real discoveries,
/// not save restore. (Not <c>Activate()</c> — that crashed after the 5/13/2026 update.)
/// </summary>
[HarmonyPatch(typeof(MapNodeActivator), nameof(MapNodeActivator.SendMapNodeUnlockedAnalyticsEvent))]
internal static class MapNodePatch
{
    private static void Postfix(MapNodeActivator __instance)
    {
        if (!Plugin.Instance.ModEnabled || !Plugin.Instance.SaveManager.IsSaveBound) return;

        // Off = not in the seed. Without slot data (offline) record it anyway rather than lose it.
        var slotData = Plugin.Instance.ApClient.SlotData;
        if (slotData != null && !slotData.RandomizeMapNodes) return;

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
