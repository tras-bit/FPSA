using System;
using Kordon.Core.Utility;

namespace Kordon.Core.Movement
{
    /// <summary>
    /// Movement tuning for the whole game.  Deliberately slow and inertial: this is a
    /// tactical shooter where repositioning costs time and noise.
    /// All speeds are metres per second.
    /// </summary>
    [Serializable]
    public sealed class MovementProfile
    {
        public const float WalkSpeed = 2.55f;
        public const float CrouchSpeed = 1.45f;
        public const float ProneSpeed = 0.62f;
        public const float SprintSpeed = 4.35f;
        public const float AimingSpeed = 1.75f;
        public const float BackwardsMultiplier = 0.72f;
        public const float StrafeMultiplier = 0.86f;

        /// <summary>Ground acceleration - low values give the heavy, inertial feel.</summary>
        public float GroundAcceleration = 11f;
        public float GroundDeceleration = 17f;

        /// <summary>Air control is intentionally weak so bunny hopping is not a movement option.</summary>
        public float AirControlMultiplier = 0.16f;
        public float AirDragPerSecond = 0.12f;

        public float JumpUpVelocity = 3.4f;
        public float Gravity = 19.6f;
        public float CrouchJumpVelocityMultiplier = 0.62f;

        public float StanceTransitionSeconds = 0.42f;
        public float ProneTransitionSeconds = 0.85f;

        public float LeanAngleDeg = 42f;
        public float LeanTransitionSeconds = 0.28f;

        /// <summary>Weapon control while airborne (used by recoil/spread and the viewmodel).</summary>
        public float AirborneWeaponControlPenalty = 2.6f;

        /// <summary>Sprint cannot be started below this stamina fraction.</summary>
        public float SprintStaminaFloor = 0.08f;

        /// <summary>Camera height per stance, metres.</summary>
        public float StandEyeHeight = 1.62f;
        public float CrouchEyeHeight = 1.06f;
        public float ProneEyeHeight = 0.48f;

        /// <summary>Mass of the reference kit (weapon + armour + magazines) in kilograms.</summary>
        public float ReferenceGearMassKg = 14f;

        /// <summary>Speed penalty per kilogram above the reference mass.</summary>
        public float SpeedPenaltyPerExtraKg = 0.012f;

        public float MaxSpeedFor(Stance stance, bool aiming, bool sprinting,
            float mobilityMultiplier = 1f, float gearMassKg = 0f, float armorMobility = 1f)
        {
            float baseSpeed;
            switch (stance)
            {
                case Stance.Crouch: baseSpeed = CrouchSpeed; break;
                case Stance.Prone: baseSpeed = ProneSpeed; break;
                default: baseSpeed = sprinting ? SprintSpeed : WalkSpeed; break;
            }

            if (aiming && stance != Stance.Prone)
            {
                baseSpeed = Math.Min(baseSpeed, AimingSpeed);
            }

            float massPenalty = 1f;
            if (gearMassKg > ReferenceGearMassKg)
            {
                massPenalty = MathUtil.Clamp(1f - (gearMassKg - ReferenceGearMassKg) * SpeedPenaltyPerExtraKg, 0.7f, 1f);
            }

            return baseSpeed * MathUtil.Clamp(mobilityMultiplier, 0.2f, 1.2f) *
                   MathUtil.Clamp(armorMobility, 0.5f, 1.2f) * massPenalty;
        }

        public float EyeHeightFor(Stance stance, float transition01 = 1f)
        {
            float target;
            switch (stance)
            {
                case Stance.Crouch: target = CrouchEyeHeight; break;
                case Stance.Prone: target = ProneEyeHeight; break;
                default: target = StandEyeHeight; break;
            }

            return MathUtil.Lerp(StandEyeHeight, target, MathUtil.Clamp01(transition01));
        }
    }
}
