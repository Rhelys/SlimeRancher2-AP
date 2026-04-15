using HarmonyLib;
using Il2CppMonomiPark.SlimeRancher.Pedia;
using SlimeRancher2AP.Data;

namespace SlimeRancher2AP.Patches.LocationPatches;

/// <summary>
/// Detects first-time Slimepedia unlocks and converts them into Archipelago location checks.
///
/// Two overloads of <c>PediaDirector.Unlock</c> exist with SEPARATE native implementations:
/// <list type="bullet">
///   <item>
///     <c>Unlock(PediaEntry, bool)</c> — CallerCount(12), returns <c>bool</c> (true = newly
///     unlocked). Used for slime entries discovered from world interaction. Patched below in
///     <see cref="SlimepediaPatch"/> via the <c>__result</c> guard.
///   </item>
///   <item>
///     <c>Unlock(IdentifiableType)</c> — CallerCount(3), returns <c>void</c>. Used for resource
///     entries unlocked when the player first picks up or discovers an identifiable object.
///     Does NOT call through to the PediaEntry overload at the native level (confirmed by
///     slime checks firing but resource checks not firing). Patched below in
///     <see cref="SlimepediaIdentTypePatch"/> using Prefix/Postfix + IsUnlocked state.
///   </item>
/// </list>
///
/// Entry identity: <c>PediaEntry.name</c> (stable Unity ScriptableObject asset name) looked up
/// in <see cref="LocationTable"/> via <see cref="LocationTable.TryGetByEntryName"/>.
///
/// Active for <c>SlimepediaEntry</c> locations when <c>SlotData.RandomizeSlimepedia</c> is true,
/// and for <c>SlimepediaResourceEntry</c> locations when <c>SlotData.RandomizeSlimepediaResources</c>
/// is true. Both flags are checked independently per entry type.
/// </summary>
[HarmonyPatch(typeof(PediaDirector), nameof(PediaDirector.Unlock),
    new System.Type[] { typeof(PediaEntry), typeof(bool) })]
internal static class SlimepediaPatch
{
    private static void Postfix(bool __result, PediaEntry entry)
    {
        if (!Plugin.Instance.ModEnabled) return;
        if (!__result) return; // already unlocked — save-load replay or re-discovery
        if (entry == null) return;

        var slotData = Plugin.Instance.ApClient?.SlotData;
        if (slotData == null) return;
        if (!slotData.RandomizeSlimepedia && !slotData.RandomizeSlimepediaResources) return;

        SlimepediaPatchHelper.SendCheckForEntry(entry.name, slotData);
    }
}

/// <summary>
/// Companion patch for <c>PediaDirector.Unlock(IdentifiableType)</c>.
///
/// This overload (CallerCount=3) is called when a resource (food, craft material, livestock,
/// etc.) is first picked up or otherwise discovered. Its native implementation is SEPARATE
/// from <c>Unlock(PediaEntry, bool)</c> — Harmony on the PediaEntry overload does not fire
/// for this path.
///
/// Strategy: record <c>IsUnlocked(identType)</c> in a Prefix (before the unlock), then in
/// the Postfix, if the entry was not yet unlocked, resolve it to a <c>PediaEntry</c> via
/// <c>GetEntry(identType)</c> and forward to the shared helper.
/// </summary>
[HarmonyPatch(typeof(PediaDirector), nameof(PediaDirector.Unlock),
    new System.Type[] { typeof(IdentifiableType) })]
internal static class SlimepediaIdentTypePatch
{
    // __state = true  → was already unlocked before this call (no-op, skip)
    // __state = false → was NOT unlocked yet (this call is the first unlock)
    private static void Prefix(PediaDirector __instance, IdentifiableType identifiableType,
                                out bool __state)
    {
        __state = __instance.IsUnlocked(identifiableType);
    }

    private static void Postfix(PediaDirector __instance, IdentifiableType identifiableType,
                                 bool __state)
    {
        if (__state) return; // was already unlocked before this call
        if (!Plugin.Instance.ModEnabled) return;

        var slotData = Plugin.Instance.ApClient?.SlotData;
        if (slotData == null) return;
        if (!slotData.RandomizeSlimepedia && !slotData.RandomizeSlimepediaResources) return;

        var entry = __instance.GetEntry(identifiableType);
        if (entry == null)
        {
            Plugin.Instance.Log.LogDebug(
                $"[AP-Pedia] Unlock(IdentType) fired for '{identifiableType?.name}' but GetEntry returned null");
            return;
        }

        SlimepediaPatchHelper.SendCheckForEntry(entry.name, slotData);
    }
}

// ---------------------------------------------------------------------------
// Shared helper — kept outside the patch classes so both can call it.
// ---------------------------------------------------------------------------

file static class SlimepediaPatchHelper
{
    internal static void SendCheckForEntry(string? entryName,
                                           SlimeRancher2AP.Archipelago.SlotData slotData)
    {
        if (string.IsNullOrEmpty(entryName)) return;

        if (!LocationTable.TryGetByEntryName(entryName, out var loc) || loc == null)
        {
            Plugin.Instance.Log.LogDebug(
                $"[AP-Pedia] Unmapped entry unlocked: '{entryName}'");
            return;
        }

        bool enabled = loc.Type switch
        {
            LocationType.SlimepediaEntry         => slotData.RandomizeSlimepedia,
            LocationType.SlimepediaResourceEntry => slotData.RandomizeSlimepediaResources,
            _                                    => false,
        };
        if (!enabled) return;

        Plugin.Instance.Log.LogInfo(
            $"[AP-Pedia] Check: '{loc.Name}' (id={loc.Id}  entry='{entryName}')");
        Plugin.Instance.ApClient?.SendCheck(loc.Id);
    }
}
