using HarmonyLib;
using Il2CppMonomiPark.SlimeRancher.DataModel;
using SlimeRancher2AP.Archipelago;

namespace SlimeRancher2AP.Patches.PlayerPatches;

/// <summary>
/// Keeps <c>ItemHandler._upgradeLevels</c> in sync by intercepting every upgrade level change,
/// including save-data restoration on game load.
/// <c>OnUpgradeChanged</c> is non-virtual (private in original C#), so it is safe to patch.
/// </summary>
[HarmonyPatch(typeof(ActorUpgradeHandler), "OnUpgradeChanged")]
internal static class UpgradeLevelTrackingPatch
{
    private static void Postfix(UpgradeDefinition definition, int fromLevel, int toLevel)
    {
#if DEBUG
        SlimeRancher2AP.Utils.DebugTrace.Once("UpgradeLevelTrackingPatch.Postfix — first entry");
#endif
        if (!Plugin.Instance.ModEnabled) return;
        try { ItemHandler.TrackUpgradeLevel(definition.name, toLevel); } catch { /* guard against partially-initialised UpgradeDefinition during scene load */ }
    }
}

/// <summary>
/// Caches the player's ActorUpgradeHandler by hooking CheckUpgradePropertiesAreAvailable.
/// </summary>
/// <remarks>
/// <para>
/// Both the constructor and virtual methods (InitModel, SetModel) are unsafe patch targets on
/// IL2CPP non-MonoBehaviour types:
/// - Constructors: the Harmony fallback path does not fire for them in practice.
/// - Virtual methods: Harmony patches the vtable slot at the interface level, so the Postfix
///   fires with whatever type is at that vtable position — causing an AccessViolationException
///   when Il2CppObjectPool tries to cast the wrong pointer to ActorUpgradeHandler.
/// </para>
/// <para>
/// <c>CheckUpgradePropertiesAreAvailable()</c> is non-virtual (no vtable dispatch) and has
/// CallerCount=1 (called exactly once, from within or immediately after the constructor).
/// Patching it is safe: the trampoline is installed only in ActorUpgradeHandler's own method
/// table, so __instance is always a valid ActorUpgradeHandler pointer.
/// </para>
/// </remarks>
[HarmonyPatch(typeof(ActorUpgradeHandler), nameof(ActorUpgradeHandler.CheckUpgradePropertiesAreAvailable))]
internal static class ActorUpgradeHandlerCachePatch
{
    private static void Postfix(ActorUpgradeHandler __instance)
    {
#if DEBUG
        SlimeRancher2AP.Utils.DebugTrace.All("ActorUpgradeHandlerCachePatch.Postfix — entry");
#endif
        if (ItemHandler.UpgradeHandler != null) return; // already cached
        ItemHandler.UpgradeHandler = __instance;
        Plugin.Instance.Log.LogInfo("[AP] ActorUpgradeHandler cached via CheckUpgradePropertiesAreAvailable Postfix");
    }
}
