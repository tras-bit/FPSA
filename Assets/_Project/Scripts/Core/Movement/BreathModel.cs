using System;
using Kordon.Core.Utility;

namespace Kordon.Core.Movement
{
    /// <summary>
    /// Breathing drives idle weapon sway and aim stability.  It is fed by stamina,
    /// posture, suppression, airborne state and arm injuries, which is what makes
    /// sprinting into a shot a bad idea.
    /// </summary>
    [Serializable]
    public sealed class BreathModel
    {
        public float BaseSwayDegreesStand = 0.085f;
        public float BaseSwayDegreesCrouch = 0.055f;
        public float BaseSwayDegreesProne = 0.03f;
        public float AimingMultiplier = 0.55f;
        public float ExhaustionMultiplier = 2.8f;
        public float SuppressionMultiplier = 2.2f;
        public float AirborneMultiplier = 2.4f;
        public float ArmInjuryMultiplier = 2.1f;

        /// <summary>Breaths per minute at rest; rises with exhaustion.</summary>
        public float RestBreathRatePerMinute = 14f;
        public float MaxBreathRatePerMinute = 42f;

        /// <summary>0..1 how hard the character is breathing right now.</summary>
        public float Intensity(Stance stance, float stamina01, float suppression01, bool airborne,
            float aimStability)
        {
            float intensity = 0f;
            intensity += (1f - MathUtil.Clamp01(stamina01)) * 0.6f;
            intensity += MathUtil.Clamp01(suppression01) * 0.3f;
            intensity += airborne ? 0.2f : 0f;
            intensity += (1f - MathUtil.Clamp(aimStability, 0.4f, 1f)) * 0.35f;
            if (stance == Stance.Prone)
            {
                intensity *= 0.6f;
            }
            else if (stance == Stance.Crouch)
            {
                intensity *= 0.8f;
            }

            return MathUtil.Clamp01(intensity);
        }

        public float BreathRatePerMinute(float intensity01) =>
            MathUtil.Lerp(RestBreathRatePerMinute, MaxBreathRatePerMinute, intensity01);

        /// <summary>Sway cone half angle in degrees for the current state.</summary>
        public float SwayDegrees(Stance stance, float intensity01, bool aiming)
        {
            float baseSway;
            switch (stance)
            {
                case Stance.Crouch: baseSway = BaseSwayDegreesCrouch; break;
                case Stance.Prone: baseSway = BaseSwayDegreesProne; break;
                default: baseSway = BaseSwayDegreesStand; break;
            }

            float multiplier = MathUtil.Lerp(1f, ExhaustionMultiplier, intensity01);
            if (aiming)
            {
                multiplier *= AimingMultiplier;
            }

            return baseSway * multiplier;
        }

        /// <summary>
        /// Lissajous style sway offset in degrees, so the reticle-less sight picture
        /// drifts believably instead of jittering randomly.
        /// </summary>
        public KordonVec3 SampleSway(float timeSeconds, float intensity01, float swayDegrees)
        {
            float rate = BreathRatePerMinute(intensity01) / 60f;
            float phase = timeSeconds * rate * MathUtil.Deg2Rad * 360f;
            float x = (float)Math.Sin(phase) * 0.6f + (float)Math.Sin(phase * 2.3f) * 0.4f;
            float y = (float)Math.Cos(phase * 0.5f) * 0.7f + (float)Math.Sin(phase * 1.7f) * 0.3f;
            return new KordonVec3(x * swayDegrees, y * swayDegrees, 0f);
        }

        /// <summary>Whether an audible breathing loop should play (exhaustion or heavy suppression).</summary>
        public bool ShouldPlayBreathing(float intensity01) => intensity01 > 0.55f;
    }
}
