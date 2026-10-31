using HarmonyLib;
using Il2CppMonomiPark.SlimeRancher.Pedia;
using SlimeRancher2AP.Data;

namespace SlimeRancher2AP.Patches.LocationPatches;

/// <summary>
/// First-time Slimepedia unlocks become checks (by <c>PediaEntry.name</c>), per the category's
/// option. <c>Unlock</c> has two overloads with separate native bodies, so both are patched:
/// <c>Unlock(PediaEntry, bool)</c> here, and <c>Unlock(IdentifiableType, bool)</c> — resource
/// pickups — in <see cref="SlimepediaIdentTypePatch"/>.
/// </summary>
[HarmonyPatch(typeof(PediaDirector), nameof(PediaDirector.Unlock),
    new System.Type[] { typeof(PediaEntry), typeof(bool) })]
internal static class SlimepediaPatch
{
    /// <summary>Skips a null entry (the original would NRE); <c>__result</c> stays false.</summary>
    private static bool Prefix(PediaEntry entry)
    {
        if (entry != null) return true;
        Logger.Warning("[AP-Pedia] Unlock(PediaEntry, bool) called with null entry — skipping to prevent NRE");
        return false;
    }

    private static void Postfix(bool __result, PediaEntry entry)
    {
        if (!Plugin.Instance.ModEnabled) return;
        if (!__result) return; // not new
        if (entry == null) return;

        var slotData = Plugin.Instance.ApClient?.SlotData;
        if (slotData == null) return;
        if (!slotData.RandomizeSlimepedia && !slotData.RandomizeSlimepediaResources
            && !slotData.RandomizeSlimepediaRadiant) return;

        SlimepediaPatchHelper.SendCheckForEntry(entry.name, slotData);
    }

    /// <summary>
    /// Swallows the native body's own exceptions (e.g. an analytics-ctor NRE), which vanilla
    /// swallows too — our trampoline would otherwise surface them as errors.
    /// </summary>
    [HarmonyFinalizer]
    private static System.Exception? Finalizer(System.Exception? __exception)
    {
        if (__exception != null)
            Logger.Warning(
                $"[AP-Pedia] Suppressed exception in PediaDirector.Unlock(PediaEntry): " +
                $"{__exception.GetType().Name}: {__exception.Message}");
        return null;
    }
}

/// <summary>
/// The <c>Unlock(IdentifiableType)</c> overload (resource pickups). Returns void, so the Prefix
/// records whether it was already unlocked.
/// </summary>
[HarmonyPatch(typeof(PediaDirector), nameof(PediaDirector.Unlock),
    new System.Type[] { typeof(IdentifiableType), typeof(bool) })]
internal static class SlimepediaIdentTypePatch
{
    // __state: already unlocked before this call
    private static void Prefix(PediaDirector __instance, IdentifiableType identifiableType,
                                out bool __state)
    {
        __state = __instance.IsUnlocked(identifiableType);
    }

    private static void Postfix(PediaDirector __instance, IdentifiableType identifiableType,
                                 bool __state)
    {
        if (__state) return;
        if (!Plugin.Instance.ModEnabled) return;

        var slotData = Plugin.Instance.ApClient?.SlotData;
        if (slotData == null) return;
        if (!slotData.RandomizeSlimepedia && !slotData.RandomizeSlimepediaResources
            && !slotData.RandomizeSlimepediaRadiant) return;

        var entry = __instance.GetEntry(identifiableType);
        if (entry == null)
        {
            Logger.Debug(
                $"[AP-Pedia] Unlock(IdentType) fired for '{identifiableType?.name}' but GetEntry returned null");
            return;
        }

        SlimepediaPatchHelper.SendCheckForEntry(entry.name, slotData);
    }
}

file static class SlimepediaPatchHelper
{
    internal static void SendCheckForEntry(string? entryName,
                                           SlimeRancher2AP.Archipelago.SlotData slotData)
    {
        if (string.IsNullOrEmpty(entryName)) return;

        if (!LocationTable.TryGetByEntryName(entryName, out var loc) || loc == null)
        {
            Logger.Debug(
                $"[AP-Pedia] Unmapped entry unlocked: '{entryName}'");
            return;
        }

        bool enabled = loc.Type switch
        {
            LocationType.SlimepediaEntry         => slotData.RandomizeSlimepedia,
            LocationType.SlimepediaResourceEntry => slotData.RandomizeSlimepediaResources,
            LocationType.SlimepediaRadiantEntry  => slotData.RandomizeSlimepediaRadiant,
            _                                    => false,
        };
        if (!enabled) return;

        // Entries the apworld may have left out of the pool. RadiantSlime follows the radiant
        // toggle, not the Slimes one.
        if (entryName == "RadiantSlime" && !slotData.RandomizeSlimepediaRadiant) return;
        if (entryName == "Sprinkles"    && !slotData.RandomizeSanctuary) return;

        Logger.Info(
            $"[AP-Pedia] Check: '{loc.Name}' (id={loc.Id}  entry='{entryName}')");
        Plugin.Instance.ApClient?.SendCheck(loc.Id);
    }
}
