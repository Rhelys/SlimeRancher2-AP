using HarmonyLib;
using Il2CppMonomiPark.SlimeRancher.Player.PlayerItems;
using Il2CppMonomiPark.SlimeRancher.World;

namespace SlimeRancher2AP.Patches.PlayerPatches;

/// <summary>
/// Quality-of-life tweaks applied to every Archipelago game and never to a vanilla one.
/// </summary>
/// <remarks>
/// No apworld option: these just make a randomizer run less tedious. "Archipelago game" means
/// an AP-bound save (<c>ApSaveManager.IsSaveBound</c>), connected or not — the same rule
/// location checks use, so the tweaks hold offline and never leak into an unbound save.
/// </remarks>
internal static class QualityOfLife
{
    internal static bool IsActive
        => Plugin.Instance.ModEnabled && Plugin.Instance.SaveManager.IsSaveBound;

    // -------------------------------------------------------------------------
    // Vacpack shoot rate
    // -------------------------------------------------------------------------

    /// <summary>Shots per second relative to vanilla. 2 = twice as fast.</summary>
    internal const float VacShootRateMultiplier = 2f;

    // The vanilla cooldown of the VacuumItem we last adjusted, keyed by instance so a new
    // vacpack (scene reload) is captured fresh rather than compounding the multiplier.
    private static System.IntPtr _vacPtr;
    private static float         _vanillaShootCooldown = -1f;
    private static int           _tickCounter;

    /// <summary>
    /// Called every frame from <c>ApUpdateBehaviour.Update</c>; does work about once a second.
    /// </summary>
    /// <remarks>
    /// <c>VacuumItem.ShootCooldown</c> is a plain serialized field — the gap between shots
    /// while the fire button is held — so it is set directly rather than patched. It is
    /// re-checked periodically because the vacpack is rebuilt on scene load, and restored when
    /// the save is no longer AP-bound so a vanilla save loaded afterwards plays at vanilla speed.
    /// </remarks>
    internal static void Tick()
    {
        if (++_tickCounter < 60) return;
        _tickCounter = 0;

        try
        {
            var vac = SceneContext.Instance?.PlayerState?.VacuumItem;
            if (vac == null) return;

            if (vac.Pointer != _vacPtr)
            {
                _vacPtr = vac.Pointer;
                _vanillaShootCooldown = vac.ShootCooldown;   // fresh instance: still vanilla
            }
            if (_vanillaShootCooldown <= 0f) return;

            float want = IsActive ? _vanillaShootCooldown / VacShootRateMultiplier : _vanillaShootCooldown;
            if (System.Math.Abs(vac.ShootCooldown - want) < 0.0001f) return;

            vac.ShootCooldown = want;
            Logger.Info($"[AP-QoL] Vacpack shoot cooldown {_vanillaShootCooldown:0.###}s → {want:0.###}s" +
                        (IsActive ? $" (×{VacShootRateMultiplier} rate)" : " (vanilla restored)"));
        }
        catch { /* player mid-load — retry next second */ }
    }

    // -------------------------------------------------------------------------
    // Gadget warm-up
    // -------------------------------------------------------------------------

    /// <summary>Real seconds a freshly placed gadget waits before it can be used.</summary>
    internal const float GadgetChargeupRealSeconds = 5f;
}

/// <summary>
/// Caps the warm-up of a freshly placed gadget (warp depots, teleporters, Refinery Link and
/// the like) at <see cref="QualityOfLife.GadgetChargeupRealSeconds"/> real seconds.
/// </summary>
/// <remarks>
/// <para>
/// The wait is <c>Gadget.CooldownTimeGameHrs</c> of game time, stored on the model as an
/// absolute world-time deadline (<c>GadgetModel.waitForChargeupTime</c>); the chargeup
/// indicator and <c>InGadgetCooldown()</c> both read that deadline. Moving the deadline is
/// therefore enough. <c>TimeDirector.WorldTimeRealSecFromNowOrStart</c> converts real seconds
/// to world time with the game's own time factor, so the cap holds at any time scale.
/// </para>
/// <para>
/// Only ever shortens: a gadget with no warm-up, or one already under the cap, is untouched.
/// Patched method: <c>Gadget.OnPlaced(SpringConfiguration, float)</c> — CallerCount(1), the
/// placement path. Gadgets restored from a save are not affected.
/// </para>
/// </remarks>
[HarmonyPatch(typeof(Gadget), nameof(Gadget.OnPlaced))]
internal static class GadgetChargeupPatch
{
    private static void Postfix(Gadget __instance)
    {
        try
        {
            if (!QualityOfLife.IsActive) return;

            var model   = __instance._model;
            var timeDir = SceneContext.Instance?.TimeDirector;
            if (model == null || timeDir == null) return;

            double cap = timeDir.WorldTimeRealSecFromNowOrStart(QualityOfLife.GadgetChargeupRealSeconds);
            if (model.waitForChargeupTime <= cap) return;

            double before = timeDir.RealSecondsUntil(model.waitForChargeupTime);
            model.waitForChargeupTime = cap;
            Logger.Info($"[AP-QoL] Gadget '{__instance.name}' warm-up {before:0.#}s → " +
                        $"{QualityOfLife.GadgetChargeupRealSeconds:0.#}s");
        }
        catch (System.Exception ex)
        {
            Logger.Warning($"[AP-QoL] GadgetChargeupPatch threw: {ex.Message}");
        }
    }
}
