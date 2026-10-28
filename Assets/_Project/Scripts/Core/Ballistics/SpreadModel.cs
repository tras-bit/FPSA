using System;
using Kordon.Core.Movement;
using Kordon.Core.Utility;

namespace Kordon.Core.Ballistics
{
    /// <summary>
    /// Cone based hit dispersion.  Normal bullets are hitscan: the server picks the
    /// final ray direction from this model and raycasts it itself, so the client can
    /// never claim a hit the server did not compute.
    /// </summary>
    [Serializable]
    public sealed class SpreadProfile
    {
        /// <summary>Cone half angle (degrees) when aiming down sights, standing still.</summary>
        public float AimingBaseDeg = 0.08f;

        /// <summary>Cone half angle (degrees) from the hip, standing still.</summary>
        public float HipBaseDeg = 2.6f;

        public float MovementAddDegPerMps = 0.42f;
        public float SprintAddDeg = 3.4f;
        public float CrouchMultiplier = 0.8f;
        public float ProneMultiplier = 0.55f;
        public float AirborneMultiplier = 3.2f;
        public float LeanAddDeg = 0.55f;
        public float StaminaExhaustedAddDeg = 0.85f;
        public float SuppressionAddDeg = 1.6f;

        /// <summary>Bloom added per consecutive shot and how fast it bleeds off.</summary>
        public float BloomPerShotDeg = 0.22f;
        public float BloomMaxDeg = 3.2f;
        public float BloomRecoveryDegPerSecond = 4.5f;

        /// <summary>How much of the spread is removed when the weapon has fully settled after ADS.</summary>
        public float AdsSettleTimeSec = 0.22f;

        public SpreadProfile Clone() => (SpreadProfile)MemberwiseClone();
    }

    /// <summary>Stateful bloom tracking plus the final cone calculation.</summary>
    [Serializable]
    public sealed class SpreadModel
    {
        public float Bloom { get; private set; }

        public void Tick(float deltaTime, SpreadProfile profile)
        {
            if (deltaTime <= 0f || profile == null)
            {
                return;
            }

            Bloom = Math.Max(0f, Bloom - profile.BloomRecoveryDegPerSecond * deltaTime);
        }

        public void AddBloom(SpreadProfile profile)
        {
            if (profile == null)
            {
                return;
            }

            Bloom = Math.Min(profile.BloomMaxDeg, Bloom + profile.BloomPerShotDeg);
        }

        public void Reset() => Bloom = 0f;

        /// <summary>Cone half angle in degrees for the given weapon state.</summary>
        public float ComputeCone(SpreadProfile profile, ShotContext context)
        {
            if (profile == null)
            {
                profile = new SpreadProfile();
            }

            float cone = context.IsAiming ? profile.AimingBaseDeg : profile.HipBaseDeg;

            switch (context.Stance)
            {
                case Stance.Crouch: cone *= profile.CrouchMultiplier; break;
                case Stance.Prone: cone *= profile.ProneMultiplier; break;
            }

            if (!context.IsGrounded)
            {
                cone *= profile.AirborneMultiplier;
            }

            cone += context.Speed * profile.MovementAddDegPerMps;
            if (context.Speed >= MovementProfile.SprintSpeed * 0.9f)
            {
                cone += profile.SprintAddDeg;
            }

            if (context.IsLeaning)
            {
                cone += profile.LeanAddDeg;
            }

            cone += (1f - MathUtil.Clamp01(context.Stamina01)) * profile.StaminaExhaustedAddDeg;
            cone += MathUtil.Clamp01(context.Suppression01) * profile.SuppressionAddDeg;

            float stability = context.AimStability <= 0f ? 1f : context.AimStability;
            cone /= MathUtil.Clamp(stability, 0.4f, 1f);

            return cone + Bloom;
        }

        /// <summary>
        /// Sample a direction inside the cone. Uses a uniform disc distribution so the
        /// density does not bunch up in the middle.
        /// </summary>
        public static KordonVec3 SampleDirection(KordonVec3 forward, float coneDegrees, DeterministicRandom random)
        {
            KordonVec3 dir = forward.Normalized();
            if (dir.SqrMagnitude < 0.5f || coneDegrees <= 0f)
            {
                return dir;
            }

            float r = random != null ? random.Next01() : 0f;
            float theta = random != null ? random.Next01() * 360f : 0f;
            float radius = (float)Math.Sqrt(r) * coneDegrees * MathUtil.Deg2Rad;

            KordonVec3 up = Math.Abs(KordonVec3.Dot(dir, KordonVec3.Up)) > 0.99f
                ? KordonVec3.Right
                : KordonVec3.Up;
            KordonVec3 right = KordonVec3.Cross(up, dir).Normalized();
            KordonVec3 localUp = KordonVec3.Cross(dir, right).Normalized();

            float sinR = (float)Math.Sin(radius);
            float cosR = (float)Math.Cos(radius);
            float sinT = (float)Math.Sin(theta * MathUtil.Deg2Rad);
            float cosT = (float)Math.Cos(theta * MathUtil.Deg2Rad);

            return (dir * cosR + right * (sinR * cosT) + localUp * (sinR * sinT)).Normalized();
        }
    }
}
