using HarmonyLib;
using Il2CppMonomiPark.SlimeRancher;
using SlimeRancher2AP.Data;
using SlimeRancher2AP.Utils;
using UnityEngine;

namespace SlimeRancher2AP.Patches.LocationPatches;

/// <summary>
/// Detects when the player fully fills a Shadow Plort door in the Grey Labyrinth.
///
/// <para>
/// <b>Hook history:</b>
/// <list type="bullet">
///   <item><c>ActivateOnFill()</c> — stack overflow: Harmony's "call original" via
///     <c>il2cpp_runtime_invoke</c> routes back through the managed bridge wrapper
///     (which is the patched version), creating infinite recursion before our Postfix runs.
///     A re-entry guard cannot help because the recursion is inside the trampoline, not our code.</item>
///   <item><c>PlortDepositorModel.Push(int)</c> — <c>AccessViolationException</c> on game load:
///     <c>Push</c> fires during save restoration before <c>SetGameObject</c> has been called on the
///     model, leaving <c>_gameObject</c> with a garbage IntPtr. <c>AccessViolationException</c>
///     bypasses managed <c>try/catch</c> entirely.</item>
///   <item><c>OnTriggerEnter(Collider)</c> — current approach: Unity physics callbacks only fire
///     during active gameplay, never during save loading. Safe to access all component fields here.</item>
/// </list>
/// </para>
///
/// <para>
/// In the Postfix we filter to <c>ShadowPlort</c> depositors, then call
/// <c>_puzLockable.ShouldUnlock()</c> to confirm all slots are now filled.
/// The AP client's <c>CheckedLocations</c> set de-dupes any repeat firings.
/// </para>
/// </summary>
[HarmonyPatch(typeof(PlortDepositor), "OnTriggerEnter")]
internal static class PlortDepositorPatch
{
    private static void Postfix(PlortDepositor __instance)
    {
        if (!Plugin.Instance.ModEnabled || !Plugin.Instance.SaveManager.HasActiveSession) return;

        // Filter to Shadow Plort doors only.
        string plortTypeName;
        try { plortTypeName = __instance._catchIdentifiableType?.name ?? ""; }
        catch { return; }
        if (plortTypeName != "ShadowPlort") return;

        // Check if all slots are now filled and the door is about to open.
        PuzzleSlotLockable? puzLockable;
        try { puzLockable = __instance._puzLockable; }
        catch { return; }
        if (puzLockable == null) return;

        bool shouldUnlock;
        try { shouldUnlock = puzLockable.ShouldUnlock(); }
        catch { return; }
        if (!shouldUnlock) return;

#if DEBUG
        SlimeRancher2AP.Utils.DebugTrace.Once("PlortDepositorPatch.Postfix — ShadowPlort door full");
#endif

        var posKey = WorldUtils.PositionKey(__instance.gameObject);

        Logger.Info($"[AP] Shadow Plort Door filled: posKey='{posKey}'");

        if (!LocationTable.TryGetByObjectName(posKey, out var info) || info == null)
        {
            Logger.Warning(
                $"[AP] Unknown Shadow Plort Door at posKey='{posKey}' — add to LocationTable");
            return;
        }

        Plugin.Instance.ApClient.SendCheck(info.Id);
    }
}
