using HarmonyLib;
using Il2CppMonomiPark.SlimeRancher.Player.PlayerItems;
using Il2CppMonomiPark.SlimeRancher.World;

namespace SlimeRancher2AP.Patches.PlayerPatches;

/// <summary>
/// Quality-of-life tweaks applied to every Archipelago game and never to a vanilla one.
/// </summary>
/// <remarks>No apworld option. "Archipelago game" = an AP-bound save, connected or not.</remarks>
internal static class QualityOfLife
{
    internal static bool IsActive
        => Plugin.Instance.ModEnabled && Plugin.Instance.SaveManager.IsSaveBound;

    // -------------------------------------------------------------------------
    // Vacpack shoot rate
    // -------------------------------------------------------------------------

    /// <summary>Shots per second relative to vanilla. 2 = twice as fast.</summary>
    internal const float VacShootRateMultiplier = 2f;

    // Vanilla cooldown of the vacpack last adjusted, by instance — a rebuilt one is re-captured.
    private static System.IntPtr _vacPtr;
    private static float         _vanillaShootCooldown = -1f;
    private static int           _tickCounter;

    /// <summary>
    /// About once a second: sets the plain field <c>VacuumItem.ShootCooldown</c> — re-checked as
    /// the vacpack is rebuilt on scene load, and restored for a vanilla save.
    /// </summary>
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
/// Moves the world-time deadline <c>GadgetModel.waitForChargeupTime</c>, which everything reads,
/// converting with the game's own time factor. Only ever shortens. Placement only — gadgets
/// restored from a save are untouched.
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
