using System.Collections.Concurrent;
using Archipelago.MultiClient.Net;
using Archipelago.MultiClient.Net.BounceFeatures.DeathLink;
using UnityEngine;

namespace SlimeRancher2AP.Archipelago;

/// <summary>
/// Wraps the Archipelago DeathLink service. Sending and receiving deaths are decoupled:
/// sends happen from the Unity main thread (via patch); receives are queued and processed
/// on the main thread in ProcessDeathQueue() to safely call Unity APIs.
/// A feedback-loop guard prevents re-broadcasting a death we caused.
/// </summary>
public class DeathLinkHandler
{
    /// <summary>
    /// Seconds after any death (local, or one we applied) during which incoming DeathLinks are
    /// ignored. Covers the death/respawn sequence, where calling <c>OnDeath</c> again would
    /// stack a second death on the first, and collapses a burst of near-simultaneous deaths
    /// from several players into one.
    /// </summary>
    private const float DeathCooldownSeconds = 10f;

    /// <summary>
    /// A queued death older than this is discarded rather than applied. Deaths are held while
    /// there is no player (loading, main menu); without a limit, one received minutes earlier
    /// on the main menu would kill the player the instant they load in.
    /// </summary>
    private static readonly TimeSpan MaxQueuedAge = TimeSpan.FromSeconds(30);

    private readonly DeathLinkService _service;
    private readonly string           _playerName;

    // Written by the network thread (OnDeathReceived), read by the main thread — must be
    // concurrent. Receipt time is stamped on arrival for the staleness check.
    private readonly ConcurrentQueue<(DeathLink death, DateTime receivedUtc)> _pendingDeaths = new();

    // True while we are in the process of sending a death, so that the incoming
    // PlayerDeathPatch does not fire again for the death we triggered.
    private bool _isSendingDeath;

    /// <summary><c>Time.unscaledTime</c> of the most recent death; -inf before the first.</summary>
    private float _lastDeathAt = float.NegativeInfinity;

    public DeathLinkHandler(ArchipelagoSession session, string playerName)
    {
        _playerName = playerName;
        _service    = session.CreateDeathLinkService();
        _service.OnDeathLinkReceived += OnDeathReceived;
    }

    public void Enable()  => _service.EnableDeathLink();
    public void Disable() => _service.DisableDeathLink();

    /// <summary>
    /// Called from PlayerDeathPatch when the local player dies.
    /// Skipped if we caused this death via an incoming DeathLink (feedback-loop guard).
    /// Does NOT modify _isSendingDeath — that flag is owned by ProcessDeathQueue only.
    /// </summary>
    public void SendDeath(string? cause = null)
    {
        if (_isSendingDeath) return;
        _lastDeathAt = Time.unscaledTime;   // incoming deaths during our own respawn are ignored
        var message = cause ?? $"{_playerName} was consumed by the slimes.";
        _service.SendDeathLink(new DeathLink(_playerName, message));
        Logger.Info($"[AP] DeathLink sent: {message}");
    }

    private void OnDeathReceived(DeathLink death)
    {
        Logger.Info($"[AP] DeathLink received from {death.Source}: {death.Cause}");
        _pendingDeaths.Enqueue((death, DateTime.UtcNow));
    }

    /// <summary>
    /// Kills the local player by triggering the full death pipeline via PlayerDeathHandler.
    /// Also called directly from the debug panel for death testing.
    /// </summary>
    public static void KillPlayer()
    {
        var player = SceneContext.Instance?.Player;
        if (player == null) return;

        var deathHandler = player.GetComponent<PlayerDeathHandler>();
        if (deathHandler != null)
        {
            deathHandler.OnDeath(null, null, "DeathLink");
        }
        else
        {
            // Fallback: set health to 0 if the component isn't found
            Logger.Warning("[AP] PlayerDeathHandler component not found — falling back to SetHealth(0)");
            SceneContext.Instance?.PlayerState?.SetHealth(0);
        }
    }

    public void ProcessDeathQueue()
    {
        if (_pendingDeaths.IsEmpty) return;

        // Hold — don't dequeue — while there is no player to kill (loading, main menu). The old
        // code dequeued first and then bailed, silently dropping the death.
        if (SceneContext.Instance?.Player == null) return;

        // Drain everything queued: one kill answers all of it. Applying each separately would
        // call OnDeath again mid-respawn for every extra death in the burst.
        var now = DateTime.UtcNow;
        DeathLink? toApply = null;
        int stale = 0, merged = 0;
        while (_pendingDeaths.TryDequeue(out var entry))
        {
            if (now - entry.receivedUtc > MaxQueuedAge) { stale++; continue; }
            if (toApply == null) toApply = entry.death;
            else merged++;
        }

        if (stale > 0)
            Logger.Info(
                $"[AP] DeathLink: discarded {stale} death(s) held longer than {MaxQueuedAge.TotalSeconds:F0}s " +
                "while no player was in the scene");
        if (toApply == null) return;

        float sinceLastDeath = Time.unscaledTime - _lastDeathAt;
        if (sinceLastDeath < DeathCooldownSeconds)
        {
            Logger.Info(
                $"[AP] DeathLink from {toApply.Source} ignored — player died {sinceLastDeath:F1}s ago " +
                $"(cooldown {DeathCooldownSeconds:F0}s)");
            return;
        }

        Logger.Info(
            $"[AP] Applying DeathLink from {toApply.Source}" +
            (merged > 0 ? $" (+{merged} more received at the same time)" : ""));

        // Set the guard BEFORE killing the player so that PlayerDeathPatch.Prefix fires
        // during OnDeath() and sees _isSendingDeath = true, blocking the echo signal.
        _lastDeathAt    = Time.unscaledTime;
        _isSendingDeath = true;
        try     { KillPlayer(); }
        finally { _isSendingDeath = false; }
    }
}
