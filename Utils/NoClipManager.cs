#if DEBUG
using Il2CppMonomiPark.KFC;
using Il2CppMonomiPark.SlimeRancher.Player.CharacterController;
using KinematicCharacterController;
using UnityEngine;
using UnityEngine.InputSystem;

namespace SlimeRancher2AP.Utils;

/// <summary>
/// Debug-only no-clip mode. While active:
/// <list type="bullet">
///   <item>Capsule collisions are disabled — player passes through all geometry.</item>
///   <item>Movement and grounding solving are disabled — no floor snapping or wall sliding.</item>
///   <item>Gravity is bypassed — player floats freely.</item>
///   <item><see cref="Tick"/> reads Space (up) and Left Ctrl (down) each frame and injects
///   vertical velocity via <see cref="KinematicCharacterMotor.BaseVelocity"/>.</item>
///   <item>Hold Left Shift for <see cref="SpeedBoostMultiplier"/>× speed on all axes.
///   Horizontal boost works by raising <c>MaxGroundedMoveSpeed</c>/<c>MaxAirMoveSpeed</c> on the
///   character parameters — this routes through the KCC's own speed cap and avoids compounding.</item>
/// </list>
/// Horizontal WASD movement continues through the normal KCC input path.
/// Toggle via the debug panel (F9 → Misc page) or call <see cref="Toggle"/>.
/// </summary>
public static class NoClipManager
{
    public static bool IsActive { get; private set; }

    /// <summary>Vertical movement speed in world units per second while noclip is active (no sprint).</summary>
    private const float VerticalSpeed = 15f;

    /// <summary>
    /// Multiplier applied to speed when Left Shift is held.
    /// Vertical: <see cref="VerticalSpeed"/> × this value.
    /// Horizontal: <c>MaxGroundedMoveSpeed</c> and <c>MaxAirMoveSpeed</c> on
    /// <see cref="CharacterControllerParameters"/> are temporarily raised by this factor,
    /// letting the KCC's own velocity cap enforce the limit each frame without compounding.
    /// </summary>
    private const float SpeedBoostMultiplier = 3f;

    // Original parameter values saved on Enable() and restored on Disable().
    private static float _origGroundSpeed;
    private static float _origAirSpeed;

    public static void Enable()
    {
        if (IsActive) return;
        var cc = GetCC();
        if (cc == null)
        {
            Logger.Warning("[AP] NoClip: SRCharacterController not found — load a save first");
            return;
        }
        var motor = cc._motor;
        if (motor == null)
        {
            Logger.Warning("[AP] NoClip: KinematicCharacterMotor not found");
            return;
        }

        // Save original horizontal speed caps before touching them.
        // The public properties are getter-only in the IL2CPP interop; use the backing fields.
        _origGroundSpeed = cc._parameters._maxGroundedMoveSpeed;
        _origAirSpeed    = cc._parameters._maxAirMoveSpeed;

        motor.SetCapsuleCollisionsActivation(false);
        motor.SetMovementCollisionsSolvingActivation(false);
        motor.SetGroundSolvingActivation(false);
        cc.BypassGravity = true;

        // Clear any existing vertical velocity so the player doesn't launch upward.
        var v = motor.BaseVelocity;
        motor.BaseVelocity = new Vector3(v.x, 0f, v.z);

        IsActive = true;
        Logger.Info("[AP] NoClip enabled");
    }

    public static void Disable()
    {
        if (!IsActive) return;
        var cc = GetCC();
        if (cc != null)
        {
            var motor = cc._motor;
            if (motor != null)
            {
                motor.SetCapsuleCollisionsActivation(true);
                motor.SetMovementCollisionsSolvingActivation(true);
                motor.SetGroundSolvingActivation(true);

                // Clear vertical velocity — otherwise the player may continue flying after
                // re-enabling gravity until the motor's own drag bleeds it off.
                var v = motor.BaseVelocity;
                motor.BaseVelocity = new Vector3(v.x, 0f, v.z);
            }

            // Always restore the speed parameters, even if Shift wasn't held at disable time.
            cc._parameters._maxGroundedMoveSpeed = _origGroundSpeed;
            cc._parameters._maxAirMoveSpeed      = _origAirSpeed;
            cc.BypassGravity = false;
        }

        IsActive = false;
        Logger.Info("[AP] NoClip disabled");
    }

    public static void Toggle()
    {
        if (IsActive) Disable(); else Enable();
    }

    /// <summary>
    /// Called from <see cref="SlimeRancher2AP.ApUpdateBehaviour.Update"/> every frame.
    /// Reads vertical keyboard input, drives <see cref="KinematicCharacterMotor.BaseVelocity"/>.y,
    /// and adjusts horizontal speed caps for the sprint modifier.
    /// </summary>
    public static void Tick()
    {
        if (!IsActive) return;

        var cc = GetCC();
        if (cc == null) { Disable(); return; }
        var motor = cc._motor;
        if (motor == null) return;

        float vert   = 0f;
        bool  sprint = false;
        var kb = Keyboard.current;
        if (kb != null)
        {
            if (kb.spaceKey.isPressed)     vert  += 1f;
            if (kb.leftCtrlKey.isPressed)  vert  -= 1f;
            if (kb.leftShiftKey.isPressed) sprint  = true;
        }

        // Horizontal boost: raise the KCC speed cap while Shift is held.
        // Using the cap (rather than scaling BaseVelocity directly) avoids the
        // compounding problem where scaling last frame's already-scaled velocity
        // causes exponential acceleration.
        float boost = sprint ? SpeedBoostMultiplier : 1f;
        cc._parameters._maxGroundedMoveSpeed = _origGroundSpeed * boost;
        cc._parameters._maxAirMoveSpeed      = _origAirSpeed    * boost;

        // Vertical: we own this axis entirely — write it directly.
        var v = motor.BaseVelocity;
        motor.BaseVelocity = new Vector3(v.x, vert * VerticalSpeed * boost, v.z);
    }

    private static SRCharacterController? GetCC()
    {
        try
        {
            var player = SceneContext.Instance?.Player;
            return player?.GetComponent<SRCharacterController>();
        }
        catch { return null; }
    }
}
#endif
