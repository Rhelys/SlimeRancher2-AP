using HarmonyLib;
using Il2CppMonomiPark.SlimeRancher.Damage;

namespace SlimeRancher2AP.Patches.PlayerPatches;

/// <summary>
/// Multiplies all incoming player damage by the <c>incoming_damage_multiplier</c> slot data value.
/// A multiplier of 1 (the default) is a no-op — the original damage amount passes through unchanged.
/// </summary>
[HarmonyPatch(typeof(PlayerDamageable), nameof(PlayerDamageable.Damage))]
internal static class PlayerDamageablePatch
{
    private static void Prefix(Damage damage)
    {
        var multiplier = Plugin.Instance.ApClient?.SlotData?.IncomingDamageMultiplier ?? 1;
        if (multiplier <= 1) return;
        damage.Amount = damage.Amount * multiplier;
    }
}
