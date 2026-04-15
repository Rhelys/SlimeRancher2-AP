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
    private readonly DeathLinkService _service;
    private readonly string           _playerName;
    private readonly Queue<DeathLink> _pendingDeaths = new();

    // True while we are in the process of sending a death, so that the incoming
    // PlayerDeathPatch does not fire again for the death we triggered.
    private bool _isSendingDeath;

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
        var message = cause ?? $"{_playerName} was consumed by the slimes.";
        _service.SendDeathLink(new DeathLink(_playerName, message));
        Plugin.Instance.Log.LogInfo($"[AP] DeathLink sent: {message}");
    }

    private void OnDeathReceived(DeathLink death)
    {
        Plugin.Instance.Log.LogInfo($"[AP] DeathLink received from {death.Source}: {death.Cause}");
        _pendingDeaths.Enqueue(death);
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
            Plugin.Instance.Log.LogWarning("[AP] PlayerDeathHandler component not found — falling back to SetHealth(0)");
            SceneContext.Instance?.PlayerState?.SetHealth(0);
        }
    }

    public void ProcessDeathQueue()
    {
        if (!_pendingDeaths.TryDequeue(out var death)) return;

        var player = SceneContext.Instance?.Player?.gameObject;
        if (player == null) return;

        Plugin.Instance.Log.LogInfo($"[AP] Applying DeathLink from {death.Source}");

        // Set the guard BEFORE killing the player so that PlayerDeathPatch.Prefix fires
        // during OnDeath() and sees _isSendingDeath = true, blocking the echo signal.
        _isSendingDeath = true;
        KillPlayer();
        _isSendingDeath = false;
    }
}
