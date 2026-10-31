using System.Collections.Concurrent;
using Archipelago.MultiClient.Net;
using Archipelago.MultiClient.Net.BounceFeatures.DeathLink;
using UnityEngine;

namespace SlimeRancher2AP.Archipelago;

/// <summary>
/// DeathLink: sends from PlayerDeathPatch; received deaths queue for the main thread
/// (ProcessDeathQueue). A death we caused is never re-broadcast.
/// </summary>
public class DeathLinkHandler
{
    /// <summary>
    /// Incoming deaths are ignored this long after any death — no second death mid-respawn, and
    /// a burst counts once.
    /// </summary>
    private const float DeathCooldownSeconds = 10f;

    /// <summary>
    /// Older queued deaths are dropped — deaths wait while there is no player, and one from the
    /// main menu shouldn't kill on load.
    /// </summary>
    private static readonly TimeSpan MaxQueuedAge = TimeSpan.FromSeconds(30);

    private readonly DeathLinkService _service;
    private readonly string           _playerName;

    // Network thread → main thread, stamped on arrival.
    private readonly ConcurrentQueue<(DeathLink death, DateTime receivedUtc)> _pendingDeaths = new();

    // Set while applying a received death, so PlayerDeathPatch doesn't echo it.
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

    /// <summary>On a local death; skipped for one we applied.</summary>
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

    /// <summary>Kills the player through the full death pipeline (also the debug panel's).</summary>
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
            Logger.Warning("[AP] PlayerDeathHandler component not found — falling back to SetHealth(0)");
            SceneContext.Instance?.PlayerState?.SetHealth(0);
        }
    }

    public void ProcessDeathQueue()
    {
        if (_pendingDeaths.IsEmpty) return;

        // Hold, don't dequeue, while there is no player.
        if (SceneContext.Instance?.Player == null) return;

        // One kill answers the whole queue.
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

        // Guard first — PlayerDeathPatch fires inside OnDeath.
        _lastDeathAt    = Time.unscaledTime;
        _isSendingDeath = true;
        try     { KillPlayer(); }
        finally { _isSendingDeath = false; }
    }
}
