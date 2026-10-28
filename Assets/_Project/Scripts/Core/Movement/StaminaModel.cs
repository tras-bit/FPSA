using System;
using Kordon.Core.Utility;

namespace Kordon.Core.Movement
{
    /// <summary>
    /// Sprint is a short, expensive resource.  Stamina also feeds breathing, which
    /// feeds weapon sway and aim stability, exactly as the design requires.
    /// </summary>
    [Serializable]
    public sealed class StaminaModel
    {
        public const float Max = 100f;

        public float DrainPerSecondSprinting = 24f;
        public float DrainPerSecondAimingSprint = 28f;
        public float RegenPerSecond = 15f;
        public float RegenDelayAfterSprint = 1.15f;
        public float ExhaustedBelow = 6f;
        public float RecoveredAbove = 32f;

        /// <summary>Extra drain per kilogram of gear above the reference mass.</summary>
        public float ExtraDrainPerKg = 0.55f;
        public float ReferenceGearMassKg = 14f;

        public float Value { get; private set; } = Max;
        public bool IsExhausted { get; private set; }
        public float TimeSinceDrain { get; private set; } = float.MaxValue;
        public float LastSprintDuration { get; private set; }

        private float _currentSprintRun;

        public float Normalized => MathUtil.Clamp01(Value / Max);

        /// <summary>Full stamina allows roughly 4.2 seconds of sprint before exhaustion.</summary>
        public float SprintSecondsFromFull => Max / DrainPerSecondSprinting;

        public void Reset()
        {
            Value = Max;
            IsExhausted = false;
            TimeSinceDrain = float.MaxValue;
            LastSprintDuration = 0f;
            _currentSprintRun = 0f;
        }

        public bool CanSprint => !IsExhausted && Value > ExhaustedBelow;

        public void Tick(float deltaTime, bool sprinting, bool aiming, float gearMassKg)
        {
            if (deltaTime <= 0f)
            {
                return;
            }

            if (sprinting)
            {
                float drain = aiming ? DrainPerSecondAimingSprint : DrainPerSecondSprinting;
                if (gearMassKg > ReferenceGearMassKg)
                {
                    drain += (gearMassKg - ReferenceGearMassKg) * ExtraDrainPerKg;
                }

                Value = Math.Max(0f, Value - drain * deltaTime);
                TimeSinceDrain = 0f;
                _currentSprintRun += deltaTime;
                if (Value <= ExhaustedBelow)
                {
                    IsExhausted = true;
                }

                if (IsExhausted)
                {
                    LastSprintDuration = _currentSprintRun;
                    _currentSprintRun = 0f;
                }
            }
            else
            {
                TimeSinceDrain += deltaTime;
                if (_currentSprintRun > 0.05f)
                {
                    LastSprintDuration = _currentSprintRun;
                    _currentSprintRun = 0f;
                }

                if (TimeSinceDrain >= RegenDelayAfterSprint)
                {
                    Value = Math.Min(Max, Value + RegenPerSecond * deltaTime);
                }

                if (IsExhausted && Value >= RecoveredAbove)
                {
                    IsExhausted = false;
                }
            }
        }

        /// <summary>0..1 breathing intensity: 0 = calm, 1 = gasping.</summary>
        public float BreathIntensity => MathUtil.Clamp01(1f - Normalized) * 0.75f + (IsExhausted ? 0.25f : 0f);
    }
}
