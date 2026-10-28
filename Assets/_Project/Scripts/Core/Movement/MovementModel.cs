using System;
using Kordon.Core.Utility;

namespace Kordon.Core.Movement
{
    /// <summary>Player movement intent for one tick, produced from local input.</summary>
    public struct MovementIntent
    {
        /// <summary>-1..1 forward/back.</summary>
        public float Forward;

        /// <summary>-1..1 strafe.</summary>
        public float Right;
        public bool WantsSprint;
        public bool WantsJump;
        public bool IsAiming;
        public Stance Stance;

        public KordonVec3 WishDirection(KordonVec3 forward, KordonVec3 right)
        {
            KordonVec3 flatForward = new KordonVec3(forward.X, 0f, forward.Z).Normalized();
            KordonVec3 flatRight = new KordonVec3(right.X, 0f, right.Z).Normalized();
            KordonVec3 wish = flatForward * Forward + flatRight * Right;
            if (wish.SqrMagnitude > 1f)
            {
                wish = wish.Normalized();
            }

            return wish;
        }

        public float InputMagnitude => MathUtil.Clamp01((float)Math.Sqrt(Forward * Forward + Right * Right));
    }

    /// <summary>Result of one movement integration step.</summary>
    public struct MovementStep
    {
        public KordonVec3 Velocity;
        public Gait Gait;
        public bool Sprinting;
        public bool Jumped;
        public float SpeedMultiplier;
        public float WeaponControlPenalty;
    }

    /// <summary>
    /// Inertial ground/air movement.  Server authoritative: the server integrates
    /// this every tick from the client's intent and rejects anything that moves
    /// faster than the profile allows (see Kordon.Core.AntiCheat).
    /// </summary>
    [Serializable]
    public sealed class MovementModel
    {
        public MovementProfile Profile { get; }

        public MovementModel(MovementProfile profile = null)
        {
            Profile = profile ?? new MovementProfile();
        }

        /// <summary>
        /// Integrate one step.  <paramref name="velocity"/> is the current velocity in m/s
        /// (Y is vertical), <paramref name="wish"/> is the world space desired direction.
        /// </summary>
        public MovementStep Integrate(KordonVec3 velocity, KordonVec3 wish, MovementIntent intent,
            bool grounded, float deltaTime, float mobilityMultiplier = 1f, float gearMassKg = 0f,
            float armorMobility = 1f, bool canSprint = true, float stanceTransition01 = 1f)
        {
            if (deltaTime <= 0f)
            {
                deltaTime = 1f / 60f;
            }

            MovementStep step = new MovementStep { Velocity = velocity };

            bool sprinting = intent.WantsSprint && grounded && canSprint &&
                             intent.InputMagnitude > 0.55f && intent.Stance == Stance.Stand && !intent.IsAiming;

            float maxSpeed = Profile.MaxSpeedFor(intent.Stance, intent.IsAiming, sprinting,
                mobilityMultiplier, gearMassKg, armorMobility);

            // Directional modifiers: backwards and strafing are slower.
            if (intent.Forward < -0.1f)
            {
                maxSpeed *= MovementProfile.BackwardsMultiplier;
            }
            else if (Math.Abs(intent.Forward) < 0.1f && Math.Abs(intent.Right) > 0.1f)
            {
                maxSpeed *= MovementProfile.StrafeMultiplier;
            }

            // Stance transitions slow you down while the body changes posture.
            maxSpeed *= MathUtil.Lerp(0.55f, 1f, MathUtil.Clamp01(stanceTransition01));

            KordonVec3 horizontal = new KordonVec3(velocity.X, 0f, velocity.Z);
            KordonVec3 wishVelocity = wish * maxSpeed;

            if (grounded)
            {
                float control = sprinting ? 1f : 1f;
                if (wish.SqrMagnitude > 0.0001f)
                {
                    float accel = Profile.GroundAcceleration * control * MathUtil.Clamp(mobilityMultiplier, 0.2f, 1.2f);
                    horizontal = Approach(horizontal, wishVelocity, accel * deltaTime);
                }
                else
                {
                    horizontal = Approach(horizontal, KordonVec3.Zero, Profile.GroundDeceleration * deltaTime);
                }

                float vertical = 0f;
                if (intent.WantsJump)
                {
                    vertical = Profile.JumpUpVelocity *
                               (intent.Stance == Stance.Crouch ? Profile.CrouchJumpVelocityMultiplier : 1f);
                    step.Jumped = true;
                }
                else if (velocity.Y < 0f)
                {
                    vertical = 0f;
                }

                step.Velocity = new KordonVec3(horizontal.X, vertical, horizontal.Z);
            }
            else
            {
                // Weak air control: jumping never becomes an efficient way to travel.
                float airAccel = Profile.GroundAcceleration * Profile.AirControlMultiplier;
                KordonVec3 airVelocity = horizontal + wish * (airAccel * deltaTime);
                float maxAir = Math.Max(horizontal.Magnitude, maxSpeed * 0.9f);
                if (airVelocity.Magnitude > maxAir)
                {
                    airVelocity = airVelocity.Normalized() * maxAir;
                }

                float drag = MathUtil.Clamp01(Profile.AirDragPerSecond * deltaTime);
                airVelocity = airVelocity * (1f - drag);

                float vertical = velocity.Y - Profile.Gravity * deltaTime;
                if (intent.WantsJump)
                {
                    // Jumping while airborne is not possible.
                    step.Jumped = false;
                }

                step.Velocity = new KordonVec3(airVelocity.X, vertical, airVelocity.Z);
            }

            step.Sprinting = sprinting;
            step.SpeedMultiplier = MathUtil.Clamp01(new KordonVec3(step.Velocity.X, 0f, step.Velocity.Z).Magnitude /
                                                    Math.Max(0.01f, MovementProfile.SprintSpeed));
            step.Gait = ClassifyGait(intent.Stance, sprinting, step.Velocity);
            step.WeaponControlPenalty = grounded
                ? MathUtil.Lerp(1f, Profile.AirborneWeaponControlPenalty * 0.55f,
                    MathUtil.Clamp01(new KordonVec3(velocity.X, 0f, velocity.Z).Magnitude / MovementProfile.SprintSpeed))
                : Profile.AirborneWeaponControlPenalty;
            return step;
        }

        public static Gait ClassifyGait(Stance stance, bool sprinting, KordonVec3 velocity)
        {
            float flat = new KordonVec3(velocity.X, 0f, velocity.Z).Magnitude;
            switch (stance)
            {
                case Stance.Prone:
                    return flat > 0.12f ? Gait.ProneCrawl : Gait.Idle;
                case Stance.Crouch:
                    return flat > 0.12f ? Gait.CrouchWalk : Gait.Idle;
                default:
                    if (flat < 0.15f)
                    {
                        return Gait.Idle;
                    }

                    return sprinting ? Gait.Sprint : Gait.Walk;
            }
        }

        private static KordonVec3 Approach(KordonVec3 current, KordonVec3 target, float maxDelta)
        {
            KordonVec3 delta = target - current;
            float length = delta.Magnitude;
            if (length <= maxDelta || length < MathUtil.Epsilon)
            {
                return length < MathUtil.Epsilon ? current : target;
            }

            return current + delta / length * maxDelta;
        }

        /// <summary>Damage from a fall, in metres of drop height.</summary>
        public static float FallDamage(float fallHeightM)
        {
            const float safeHeight = 3.2f;
            const float lethalHeight = 17f;
            if (fallHeightM <= safeHeight)
            {
                return 0f;
            }

            float t = MathUtil.Clamp01((fallHeightM - safeHeight) / (lethalHeight - safeHeight));
            return t * t * 120f;
        }
    }
}
