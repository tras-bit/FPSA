using System;
using Kordon.Core.Utility;

namespace Kordon.Core.Combat
{
    /// <summary>
    /// Suppression is a bounded, readable state: it degrades aim and perception but
    /// never removes control and never hides information the player needs.
    /// All effect curves are clamped so the HUD/visual layer stays legible
    /// (see docs/01_GAME_DESIGN.md, "suppression must stay fair").
    /// </summary>
    [Serializable]
    public sealed class SuppressionModel
    {
        public const float MaxSuppression = 1f;

        /// <summary>Suppression per second lost when nothing is happening.</summary>
        public float DecayPerSecond = 0.42f;

        /// <summary>Above this value the visual layer starts to react (vignette/desaturation).</summary>
        public float VisualThreshold = 0.35f;

        public float Value { get; private set; }

        public SuppressionModel(float initial = 0f)
        {
            Value = MathUtil.Clamp01(initial);
        }

        public void Add(float amount)
        {
            Value = MathUtil.Clamp01(Value + Math.Max(0f, amount));
        }

        public void Tick(float deltaTime)
        {
            if (deltaTime <= 0f)
            {
                return;
            }

            Value = Math.Max(0f, Value - DecayPerSecond * deltaTime);
        }

        public void Reset() => Value = 0f;

        /// <summary>Weapon sway multiplier: 1x calm, up to 2.6x fully suppressed.</summary>
        public float SwayMultiplier => 1f + 1.6f * Value;

        /// <summary>Aim-down-sight speed multiplier: slows to 55% at full suppression.</summary>
        public float AdsSpeedMultiplier => 1f - 0.45f * Value;

        /// <summary>Recovery time multiplier after a sprint (heavier breathing).</summary>
        public float BreathRecoveryMultiplier => 1f + 0.8f * Value;

        /// <summary>0..1, only above <see cref="VisualThreshold"/>, and hard capped for readability.</summary>
        public float VisualIntensity => MathUtil.Clamp01((Value - VisualThreshold) / (1f - VisualThreshold)) * 0.55f;

        /// <summary>0..1 audio duck / low-pass amount applied to non-essential layers.</summary>
        public float AudioDucking => MathUtil.Clamp01(Value * 0.5f);

        /// <summary>Recoil control penalty: suppressed shots climb more.</summary>
        public float RecoilMultiplier => 1f + 0.55f * Value;
    }
}
