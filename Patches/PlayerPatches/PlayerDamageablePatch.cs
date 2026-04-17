using HarmonyLib;
using Il2CppMonomiPark.SlimeRancher.Damage;

namespace SlimeRancher2AP.Patches.PlayerPatches;

/// <summary>
/// Multiplies all incoming player damage by the <c>incoming_damage_multiplier</c> slot data value.
/// A multiplier of 1 (the default) is a no-op — the original damage amount passes through unchanged.
/// All damage sources go through this patch including Tarr bites (20 base damage).
/// At 5× a Tarr bite deals 100 damage, which kills the player at default health but is
/// survivable with Health Tank upgrades.
///
/// When <c>tarr_instakill</c> is enabled, Tarr bites (identified by a <c>TarrSludgeDamager</c>
/// component on <c>damage.SourceObject</c>) set damage to 9999, guaranteeing death regardless
/// of health or multiplier. This check runs before the multiplier so the two options are independent.
/// </summary>
[HarmonyPatch(typeof(PlayerDamageable), nameof(PlayerDamageable.Damage))]
internal static class PlayerDamageablePatch
{
    private static void Prefix(Damage damage)
    {
        var slotData = Plugin.Instance.ApClient?.SlotData;

        // Tarr instakill — overrides the multiplier entirely.
        if (slotData?.TarrInstakill == true
            && damage.SourceObject != null
            && damage.SourceObject.GetComponent<TarrSludgeDamager>() != null)
        {
            damage.Amount = 9999;
            return;
        }

        var multiplier = slotData?.IncomingDamageMultiplier ?? 1;
        if (multiplier <= 1) return;
        damage.Amount = damage.Amount * multiplier;
    }
}
