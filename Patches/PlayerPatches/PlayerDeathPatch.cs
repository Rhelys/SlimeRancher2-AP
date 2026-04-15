using HarmonyLib;

namespace SlimeRancher2AP.Patches.PlayerPatches;

/// <summary>
/// Sends a DeathLink notification when the local player dies.
/// Patches PlayerDeathHandler.OnDeath which is called by the game's damage system
/// when the player's health reaches zero.
/// </summary>
[HarmonyPatch(typeof(PlayerDeathHandler), nameof(PlayerDeathHandler.OnDeath))]
internal static class PlayerDeathPatch
{
    private static void Prefix()
    {
        Plugin.Instance.ApClient.DeathLink?.SendDeath();
    }
}
