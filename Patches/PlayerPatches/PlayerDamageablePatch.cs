using HarmonyLib;
using Il2CppMonomiPark.SlimeRancher.Damage;

namespace SlimeRancher2AP.Patches.PlayerPatches;

/// <summary>
/// <c>incoming_damage_multiplier</c> scales all player damage; <c>tarr_instakill</c> makes a Tarr
/// bite (a <c>TarrSludgeDamager</c> source) lethal, ahead of the multiplier.
/// </summary>
[HarmonyPatch(typeof(PlayerDamageable), nameof(PlayerDamageable.Damage))]
internal static class PlayerDamageablePatch
{
    private static void Prefix(Damage damage)
    {
        var slotData = Plugin.Instance.ApClient?.SlotData;

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
