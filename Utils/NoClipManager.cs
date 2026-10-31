#if DEBUG
using Il2CppMonomiPark.KFC;
using Il2CppMonomiPark.SlimeRancher.Player.CharacterController;
using KinematicCharacterController;
using UnityEngine;
using UnityEngine.InputSystem;

namespace SlimeRancher2AP.Utils;

/// <summary>
/// Debug no-clip: no collisions, grounding or gravity. Space/Left Ctrl fly up/down; Left Shift
/// boosts all axes. WASD still goes through the normal input path. Toggled from F9.
/// </summary>
public static class NoClipManager
{
    public static bool IsActive { get; private set; }

    /// <summary>Vertical speed, units/s.</summary>
    private const float VerticalSpeed = 15f;

    /// <summary>Left Shift speed multiplier.</summary>
    private const float SpeedBoostMultiplier = 5f;

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

        // Backing fields — the interop properties are getter-only.
        _origGroundSpeed = cc._parameters._maxGroundedMoveSpeed;
        _origAirSpeed    = cc._parameters._maxAirMoveSpeed;

        motor.SetCapsuleCollisionsActivation(false);
        motor.SetMovementCollisionsSolvingActivation(false);
        motor.SetGroundSolvingActivation(false);
        cc.BypassGravity = true;

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

                // Or the player keeps flying until drag bleeds it off.
                var v = motor.BaseVelocity;
                motor.BaseVelocity = new Vector3(v.x, 0f, v.z);
            }

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

    /// <summary>Per-frame: vertical input and the sprint boost.</summary>
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

        // Horizontal: raise the speed cap — scaling the velocity would compound each frame.
        float boost = sprint ? SpeedBoostMultiplier : 1f;
        cc._parameters._maxGroundedMoveSpeed = _origGroundSpeed * boost;
        cc._parameters._maxAirMoveSpeed      = _origAirSpeed    * boost;

        // Vertical: ours entirely.
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
