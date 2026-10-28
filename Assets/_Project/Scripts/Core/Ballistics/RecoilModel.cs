using System;
using Kordon.Core.Movement;
using Kordon.Core.Utility;

namespace Kordon.Core.Ballistics
{
    /// <summary>Selector positions a weapon can be switched to.</summary>
    public enum FireMode
    {
        Semi = 0,
        Burst = 1,
        Auto = 2
    }

    /// <summary>
    /// Everything the recoil/spread model needs to know about the shooter at the
    /// moment of the shot.  Built on the server from replicated player state so
    /// both sides compute identical numbers.
    /// </summary>
    public struct ShotContext
    {
        public Stance Stance;
        public bool IsAiming;
        public bool IsGrounded;
        public bool IsLeaning;

        /// <summary>Horizontal speed in m/s at the moment of the shot.</summary>
        public float Speed;

        /// <summary>0 = exhausted, 1 = full stamina.</summary>
        public float Stamina01;

        /// <summary>0 = calm, 1 = fully suppressed.</summary>
        public float Suppression01;

        /// <summary>From PlayerVitals.AimStabilityMultiplier (damaged arms/torso).</summary>
        public float AimStability;

        /// <summary>Seconds since the character landed, used for the landing recovery penalty.</summary>
        public float TimeSinceLanding;

        public static ShotContext Calm() => new ShotContext
        {
            Stance = Stance.Stand,
            IsAiming = true,
            IsGrounded = true,
            Speed = 0f,
            Stamina01 = 1f,
            Suppression01 = 0f,
            AimStability = 1f,
            TimeSinceLanding = 5f
        };
    }

    /// <summary>Per weapon recoil character. Authored in Data/Definitions/weapons.json.</summary>
    [Serializable]
    public sealed class RecoilProfile
    {
        public float VerticalBaseDeg = 1.1f;
        public float HorizontalBaseDeg = 0.32f;
        public float VerticalVariance = 0.22f;
        public float HorizontalVariance = 0.65f;

        /// <summary>How many consecutive shots before the climb reaches its plateau.</summary>
        public float ClimbRampShots = 6f;

        /// <summary>Climb multiplier at the plateau (long bursts get harder, not easier).</summary>
        public float PlateauMultiplier = 1.55f;

        /// <summary>First shot of a burst is always the most controlled one.</summary>
        public float FirstShotMultiplier = 0.62f;

        public float RecoveryDelaySec = 0.09f;
        public float RecoveryTimeSec = 0.22f;

        public float AimingVerticalMultiplier = 0.62f;
        public float AimingHorizontalMultiplier = 0.7f;

        public float CrouchMultiplier = 0.78f;
        public float ProneMultiplier = 0.55f;

        /// <summary>Moving shots kick noticeably more.</summary>
        public float MovementMultiplierAtWalk = 1.35f;
        public float MovementMultiplierAtSprint = 2.1f;

        /// <summary>
        /// Airborne control penalty: the design rule is that jumping must never be a
        /// free way to close distance while shooting accurately.
        /// </summary>
        public float AirborneMultiplier = 3.1f;
        public float LandingRecoverySeconds = 0.45f;
        public float LandingPenaltyMultiplier = 2.2f;

        public float LeanMultiplier = 1.18f;
        public float StaminaExhaustedMultiplier = 1.45f;
        public float SuppressionMultiplier = 1.5f;

        /// <summary>Camera kick impulse (visual only, added on top of the aim displacement).</summary>
        public float VisualKick = 1f;

        public RecoilProfile Clone() => (RecoilProfile)MemberwiseClone();
    }

    /// <summary>One shot's contribution to aim displacement.</summary>
    public struct RecoilShot
    {
        public float PitchDeg;
        public float YawDeg;
        public float VisualKick;
        public int ShotIndex;

        public override string ToString() =>
            string.Format(System.Globalization.CultureInfo.InvariantCulture,
                "pitch={0:F3} yaw={1:F3} idx={2}", PitchDeg, YawDeg, ShotIndex);
    }

    /// <summary>
    /// Stateful recoil solver: tracks burst index and recovers over time.  The same
    /// class runs on the server (to validate shots) and on the client (to move the
    /// camera), which is why it is deterministic and engine independent.
    /// </summary>
    [Serializable]
    public sealed class RecoilSolver
    {
        public int ShotIndex { get; private set; }
        public float AccumulatedPitch { get; private set; }
        public float AccumulatedYaw { get; private set; }
        public float TimeSinceLastShot { get; private set; } = float.MaxValue;
        public float LastShotTime { get; private set; } = float.MinValue;

        public void Reset()
        {
            ShotIndex = 0;
            AccumulatedPitch = 0f;
            AccumulatedYaw = 0f;
            TimeSinceLastShot = float.MaxValue;
            LastShotTime = float.MinValue;
        }

        /// <summary>Advance recovery. Call once per fixed step with the same delta on server and client.</summary>
        public void Tick(float deltaTime, RecoilProfile profile)
        {
            if (deltaTime <= 0f)
            {
                return;
            }

            TimeSinceLastShot += deltaTime;
            if (profile == null)
            {
                return;
            }

            if (TimeSinceLastShot < profile.RecoveryDelaySec)
            {
                return;
            }

            float t = Math.Min(1f, (TimeSinceLastShot - profile.RecoveryDelaySec) /
                                   Math.Max(0.001f, profile.RecoveryTimeSec));
            float recover = MathUtil.SmoothStep(t);
            AccumulatedPitch = MathUtil.Lerp(AccumulatedPitch, 0f, recover * deltaTime * 8f);
            AccumulatedYaw = MathUtil.Lerp(AccumulatedYaw, 0f, recover * deltaTime * 8f);
            if (TimeSinceLastShot > profile.RecoveryDelaySec + profile.RecoveryTimeSec * 3f)
            {
                ShotIndex = 0;
            }
        }

        /// <summary>Produce the next shot's recoil contribution.</summary>
        public RecoilShot Fire(RecoilProfile profile, ShotContext context, DeterministicRandom random, float nowSeconds)
        {
            if (profile == null)
            {
                profile = new RecoilProfile();
            }

            // A long pause between shots resets the climb.
            if (TimeSinceLastShot > profile.RecoveryDelaySec + profile.RecoveryTimeSec * 3f)
            {
                ShotIndex = 0;
            }

            float multiplier = 1f;

            if (ShotIndex == 0)
            {
                multiplier *= profile.FirstShotMultiplier;
            }
            else
            {
                float ramp = MathUtil.Clamp01(ShotIndex / Math.Max(1f, profile.ClimbRampShots));
                multiplier *= MathUtil.Lerp(1f, profile.PlateauMultiplier, ramp);
            }

            switch (context.Stance)
            {
                case Stance.Crouch: multiplier *= profile.CrouchMultiplier; break;
                case Stance.Prone: multiplier *= profile.ProneMultiplier; break;
            }

            if (context.IsAiming)
            {
                multiplier *= profile.AimingVerticalMultiplier;
            }

            if (!context.IsGrounded)
            {
                multiplier *= profile.AirborneMultiplier;
            }
            else if (context.TimeSinceLanding < profile.LandingRecoverySeconds)
            {
                float t = 1f - MathUtil.Clamp01(context.TimeSinceLanding / profile.LandingRecoverySeconds);
                multiplier *= MathUtil.Lerp(1f, profile.LandingPenaltyMultiplier, t);
            }

            float speedFactor = MathUtil.Clamp01(context.Speed / MovementProfile.SprintSpeed);
            multiplier *= MathUtil.Lerp(1f, context.IsAiming
                ? profile.MovementMultiplierAtSprint
                : profile.MovementMultiplierAtWalk, speedFactor);

            if (context.IsLeaning)
            {
                multiplier *= profile.LeanMultiplier;
            }

            multiplier *= MathUtil.Lerp(profile.StaminaExhaustedMultiplier, 1f, MathUtil.Clamp01(context.Stamina01));
            multiplier *= MathUtil.Lerp(profile.SuppressionMultiplier, 1f, MathUtil.Clamp01(context.Suppression01));
            multiplier *= MathUtil.Clamp(context.AimStability <= 0f ? 1f : 1f / context.AimStability, 1f, 2.5f);

            float horizontalSign = random != null ? (random.Next01() < 0.5f ? -1f : 1f) : 1f;
            float verticalJitter = random != null ? random.NextBell() * profile.VerticalVariance : 0f;
            float horizontalJitter = random != null ? random.NextBell() * profile.HorizontalVariance : 0f;

            float horizontalScale = context.IsAiming ? profile.AimingHorizontalMultiplier : 1f;

            RecoilShot shot = new RecoilShot
            {
                PitchDeg = Math.Max(0f, (profile.VerticalBaseDeg + verticalJitter) * multiplier),
                YawDeg = (profile.HorizontalBaseDeg * horizontalSign + horizontalJitter) * multiplier * horizontalScale,
                VisualKick = profile.VisualKick * multiplier,
                ShotIndex = ShotIndex
            };

            AccumulatedPitch += shot.PitchDeg;
            AccumulatedYaw += shot.YawDeg;
            ShotIndex++;
            TimeSinceLastShot = 0f;
            LastShotTime = nowSeconds;
            return shot;
        }
    }
}
