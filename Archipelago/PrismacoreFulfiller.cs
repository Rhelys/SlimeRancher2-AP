using System.Linq;
using Il2CppMonomiPark.SlimeRancher.World;
using UnityEngine;

namespace SlimeRancher2AP.Archipelago;

/// <summary>
/// For <c>prisma_shard_hunt</c>: once the shards are in, satisfies the vanilla Prismacore
/// prerequisites (Nullifier blueprint, all Harmonizer bells rung) rather than bypassing them,
/// which risks an unwinnable fight. Beware the names: the gadget asset "Harmonizer" is the
/// in-game Nullifier, while the in-game Harmonizers are the <c>labySwitchBell</c> switches.
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
    /// Wakes the poll on scene change — the Labyrinth streams, so a later scene can hold a bell
    /// earlier passes couldn't reach.
    /// </summary>
    public static void Rearm()
    {
        _idlePasses = 0;
        _dormant    = false;
        _nextPoll   = 0f;
    }

    /// <summary>
    /// Per-frame poll, not a one-shot on the last shard — that usually lands with no Labyrinth
    /// loaded. Cheap until the shards are in, then backs off once idle.
    /// </summary>
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
            _dormant = true;   // until Rearm()
            Logger.Info("[AP-Prismacore] Prerequisites satisfied — poll dormant until the next scene change.");
            return;
        }

        _nextPoll = now + (didWork ? ActiveSeconds : IdleSeconds);
    }

    /// <summary>Grants the Nullifier blueprint (vanilla: 20 Prisma Plorts).</summary>
    public static bool GrantNullifierBlueprint()
    {
        try
        {
            var director = SceneContext.Instance?.GadgetDirector;
            if (director == null)
            {
                // Normal while loading.
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

    /// <summary>Rings every loaded Harmonizer bell; returns the number rung.</summary>
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

                // Don't re-fire a rung bell's effects.
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
                    // immediate:false — the path a real activation takes.
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
