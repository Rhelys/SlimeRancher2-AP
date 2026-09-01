using System.Linq;
using Il2CppMonomiPark.SlimeRancher.World;
using UnityEngine;

namespace SlimeRancher2AP.Archipelago;

/// <summary>
/// Satisfies the vanilla prerequisites for the Prismacore encounter, rather than bypassing them.
///
/// <para>
/// The <c>prismacore_hunt</c> goal replaces the normal unlock path (deposit 20 Prisma Plorts for
/// the Nullifier blueprint, then activate every Harmonizer) with collecting Prisma Shards. Simply
/// forcing the fight conversation past those checks would leave the world in a state vanilla
/// never produces — hologram offline, bells un-rung — and risks an unwinnable encounter. So this
/// puts the world into the state the vanilla fight expects and lets the game's own checks pass.
/// </para>
///
/// <para>
/// <b>What the Harmonizers actually are.</b> Not any Prisma/Stabilizer component: they are
/// <c>WorldStateInvisibleSwitch</c> objects named <c>labySwitchBell</c>. Established from a live
/// log — activating one emitted
/// <c>[AP-Gate] InvisibleSwitch DOWN: name='labySwitchBell' scene='zoneLabyrinthCorePath'</c>.
/// A conversation-conditions dump had already ruled out serialized query trees (the
/// <c>GigiCore_*</c> conversations carry no conditions at all), and a name-based GameObject scan
/// missed the bells entirely.
/// </para>
///
/// <para>
/// <b>Nullifier naming.</b> The gadget asset is called <c>Harmonizer</c> but displays as
/// "Nullifier" (pedia dump: <c>entry='Harmonizer' title='Nullifier'</c>). It is the wall-dispelling
/// gadget, unrelated to the Harmonizer bells despite the shared word.
/// </para>
/// </summary>
public static class PrismacoreFulfiller
{
    /// <summary>Asset name of the Nullifier gadget — displays as "Nullifier" in-game.</summary>
    private const string NullifierGadgetName = "Harmonizer";

    /// <summary>Substring identifying a Harmonizer bell switch.</summary>
    private const string BellSwitchName = "labySwitchBell";

    // --- automatic fulfilment ---------------------------------------------------------

    /// <summary>Seconds between polls while there is still something to satisfy.</summary>
    private const float ActiveSeconds = 3f;

    /// <summary>Seconds between polls once a pass found nothing left to do.</summary>
    private const float IdleSeconds = 15f;

    /// <summary>Idle passes tolerated before the poll stops entirely.</summary>
    private const int IdlePassesBeforeDormant = 3;

    private static float _nextPoll;
    private static bool  _blueprintGranted;
    private static int   _idlePasses;
    private static bool  _dormant;

    /// <summary>Clears all state. Called on disconnect.</summary>
    public static void Reset()
    {
        _nextPoll         = 0f;
        _blueprintGranted = false;
        _idlePasses       = 0;
        _dormant          = false;
    }

    /// <summary>
    /// Wakes the poll after a scene change, without re-granting the blueprint.
    /// </summary>
    /// <remarks>
    /// Going dormant is what stops the poll running forever once the bells are down, but the
    /// Labyrinth streams: a scene loaded later can bring in a bell that was never reachable
    /// during the earlier passes. Re-arming on scene change keeps that covered while still
    /// letting the poll fall silent the rest of the time.
    /// </remarks>
    public static void Rearm()
    {
        _idlePasses = 0;
        _dormant    = false;
        _nextPoll   = 0f;
    }

    /// <summary>
    /// Called every frame from <c>ApUpdateBehaviour.Update()</c>. Satisfies the encounter
    /// prerequisites once the shard requirement is met.
    /// </summary>
    /// <remarks>
    /// Condition-driven rather than fired once on the final shard: shards arrive from anywhere
    /// in the multiworld, so the last one commonly lands while no Labyrinth scene is loaded and
    /// there would be nothing to ring. Polling instead means the bells are rung whenever the
    /// player is somewhere they exist.
    ///
    /// Cost is guarded: the expensive <c>FindObjectsOfTypeAll</c> only runs after the goal and
    /// shard checks pass (a cached int comparison), and the interval backs off to
    /// <see cref="IdleSeconds"/> once a pass finds nothing left to do.
    /// </remarks>
    public static void Tick()
    {
        if (!Plugin.Instance.ModEnabled) return;
        if (!PrismaShardHandler.IsHuntGoal) return;
        if (!PrismaShardHandler.IsEncounterUnlocked) return;   // shards not complete yet

        if (_dormant) return;

        float now = Time.unscaledTime;
        if (now < _nextPoll) return;

        bool didWork = false;

        if (!_blueprintGranted && GrantNullifierBlueprint())
        {
            _blueprintGranted = true;
            didWork = true;
        }

        if (RingAllBells() > 0) didWork = true;

        if (didWork)
        {
            _idlePasses = 0;
        }
        else if (++_idlePasses >= IdlePassesBeforeDormant)
        {
            // Everything reachable is satisfied. Stop polling rather than logging
            // "rang 0, N already down" forever; a scene change re-arms via Rearm().
            _dormant = true;
            Logger.Info("[AP-Prismacore] Prerequisites satisfied — poll dormant until the next scene change.");
            return;
        }

        _nextPoll = now + (didWork ? ActiveSeconds : IdleSeconds);
    }

    /// <summary>
    /// Grants the Nullifier blueprint, the reward normally gated behind depositing 20 Prisma
    /// Plorts. Returns true if the blueprint was granted.
    /// </summary>
    public static bool GrantNullifierBlueprint()
    {
        try
        {
            var director = SceneContext.Instance?.GadgetDirector;
            if (director == null)
            {
                // Expected while a save is still loading — the poll simply retries. Not a
                // warning: it fired twice on every single load and read like a fault.
#if DEBUG
                Utils.DebugTrace.Once("[AP-Prismacore] GadgetDirector not ready yet — will retry");
#endif
                return false;
            }

            var def = Resources.FindObjectsOfTypeAll<GadgetDefinition>()
                               .FirstOrDefault(g => g != null && g.name == NullifierGadgetName);
            if (def == null)
            {
                Logger.Warning($"[AP-Prismacore] GadgetDefinition '{NullifierGadgetName}' not found");
                return false;
            }

            director.AddBlueprint(def, false);
            Logger.Info("[AP-Prismacore] Nullifier blueprint granted");
            return true;
        }
        catch (System.Exception ex)
        {
            Logger.Warning($"[AP-Prismacore] GrantNullifierBlueprint failed: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Drives every currently-loaded Harmonizer bell to DOWN. Returns the number rung.
    /// </summary>
    /// <remarks>
    /// Only bells in loaded scenes can be reached — SR2 streams the Labyrinth, so this may need
    /// to run more than once, or from a poll, to cover all of them. The count is logged so a
    /// caller can tell how many were actually reachable.
    /// </remarks>
    public static int RingAllBells()
    {
        int rung = 0, alreadyDown = 0;
        try
        {
            foreach (var sw in Resources.FindObjectsOfTypeAll<WorldStateInvisibleSwitch>())
            {
                if (sw == null) continue;

                string nm;
                try { nm = sw.gameObject?.name ?? ""; } catch { continue; }
                if (nm.IndexOf(BellSwitchName, System.StringComparison.OrdinalIgnoreCase) < 0)
                    continue;

                // Skip bells already down so the game does not re-fire their open effects.
                try
                {
                    var model = sw._model;
                    if (model != null && model.state == SwitchHandler.State.DOWN)
                    {
                        alreadyDown++;
                        continue;
                    }
                }
                catch { /* no model bound yet — attempt the set anyway */ }

                try
                {
                    // immediate:false — take the animated path a real activation takes,
                    // so any open effects hanging off the transition still run.
                    sw.SetStateForAll(SwitchHandler.State.DOWN, false);
                    rung++;
                }
                catch (System.Exception ex)
                {
                    Logger.Warning($"[AP-Prismacore] bell '{nm}' failed: {ex.Message}");
                }
            }
        }
        catch (System.Exception ex)
        {
            Logger.Warning($"[AP-Prismacore] RingAllBells failed: {ex.Message}");
        }

        Logger.Info($"[AP-Prismacore] Harmonizer bells: rang {rung}, {alreadyDown} already down");
        return rung;
    }
}
