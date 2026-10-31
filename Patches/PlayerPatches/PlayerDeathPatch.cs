using HarmonyLib;

namespace SlimeRancher2AP.Patches.PlayerPatches;

/// <summary>Sends a DeathLink when the player dies.</summary>
[HarmonyPatch(typeof(PlayerDeathHandler), nameof(PlayerDeathHandler.OnDeath))]
internal static class PlayerDeathPatch
{
    private static void Prefix()
    {
        Plugin.Instance.ApClient.DeathLink?.SendDeath();
    }
}
