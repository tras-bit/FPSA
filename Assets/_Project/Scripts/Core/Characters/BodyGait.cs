using System;
using Kordon.Core.Utility;

namespace Kordon.Core.Characters
{
    /// <summary>
    /// One frame of body animation, in degrees, about the joint axes the exported
    /// operator hierarchy uses.
    ///
    /// Positive means "swings forward" for every field except <see cref="BobMeters"/>
    /// and <see cref="RollDegrees"/>, so the Unity adapter (OperatorBodyRig) only has
    /// to know one convention and can turn it into local rotations in one place.
    /// </summary>
    public struct GaitPose
    {
        public float ThighLeftDegrees;
        public float ThighRightDegrees;
        public float KneeLeftDegrees;
        public float KneeRightDegrees;
        public float UpperArmLeftDegrees;
        public float UpperArmRightDegrees;
        public float ElbowLeftDegrees;
        public float ElbowRightDegrees;

        /// <summary>How far the hips rise and fall within a step.</summary>
        public float BobMeters;

        /// <summary>Side to side weight shift, in degrees of roll.</summary>
        public float RollDegrees;

        /// <summary>Breathing, 0..1, for a chest that is not a statue when it stands.</summary>
        public float Breath01;
    }

    /// <summary>
    /// The walk and run cycle of an operator body, as pure maths.
    ///
    /// The bodies exported by Tools/blender/build_operators.py are rigid parts parented
    /// to hip, knee, shoulder and elbow empties, so a body animates by turning those
    /// eight transforms.  The maths that decides how far each one turns lives here, in
    /// the engine free core, so it is covered by `dotnet test` instead of only being
    /// eyeballed in an editor that this project cannot run.
    ///
    /// Speed, and therefore cadence, comes from the movement of the body itself: the
    /// remote view and the co-op hostiles both move their transform, and the rig reads
    /// the resulting velocity.  A body that stands still therefore stands still - no
    /// marching on the spot - and a sprinting body takes long strides instead of
    /// twitching its legs faster and faster.
    /// </summary>
    [Serializable]
    public sealed class BodyGait
    {
        /// <summary>Ground covered by one full cycle (two steps) when walking.</summary>
        public float WalkStrideMeters = 1.12f;

        /// <summary>Ground covered by one full cycle at full sprint.</summary>
        public float SprintStrideMeters = 2.05f;

        /// <summary>Speed at which the cycle is fully at its sprint pose.</summary>
        public float SprintSpeedMps = 6.2f;

        public float MaxThighDegrees = 27f;
        public float MaxKneeDegrees = 62f;
        public float MaxArmDegrees = 18f;
        public float MaxElbowDegrees = 26f;
        public float BobMetersPerStep = 0.028f;
        public float MaxRollDegrees = 2.6f;

        /// <summary>Below this the body is standing, not shuffling.</summary>
        public float StandSpeedThreshold = 0.25f;

        /// <summary>Seconds for a body to fall over once it is killed.</summary>
        public float FallDurationSeconds = 0.85f;

        /// <summary>Cycle position in radians, always kept inside one turn.</summary>
        public float Phase { get; private set; }

        /// <summary>0..1 blend towards the sprint pose, from the current speed.</summary>
        public float SpeedBlend01 { get; private set; }

        public void Reset()
        {
            Phase = 0f;
            SpeedBlend01 = 0f;
        }

        /// <summary>
        /// Advance the cycle by one frame of horizontal speed.
        ///
        /// Cadence follows speed / stride, which is what makes a walk read as a walk:
        /// the stride grows with speed up to the sprint length, so the feet keep
        /// landing at a believable rate instead of spinning.
        /// </summary>
        public void Advance(float horizontalSpeedMps, float dt)
        {
            float speed = MathUtil.Clamp(horizontalSpeedMps, 0f, 20f);
            float dtSeconds = MathUtil.Clamp(dt, 0f, 0.1f);
            SpeedBlend01 = MathUtil.Clamp01(speed / Math.Max(0.1f, SprintSpeedMps));
            if (speed <= StandSpeedThreshold)
            {
                // Settle the pose rather than freezing it mid stride: a body that
                // stopped with one leg forward looks broken from ten metres away.
                Phase = MathUtil.MoveTowards(Phase, MathUtil.Round(Phase / MathUtil.Pi) * MathUtil.Pi,
                    6f * dtSeconds);
                return;
            }

            float stride = MathUtil.Lerp(WalkStrideMeters, SprintStrideMeters, SpeedBlend01);
            Phase += (speed / Math.Max(0.2f, stride)) * MathUtil.TwoPi * dtSeconds;
            Phase = MathUtil.Repeat(Phase, MathUtil.TwoPi);
        }

        /// <summary>The pose for the current phase at the given speed.</summary>
        public GaitPose Sample(float horizontalSpeedMps, float crouch01 = 0f, float breath01 = 0f)
        {
            float speed = MathUtil.Clamp(horizontalSpeedMps, 0f, 20f);
            float activity = MathUtil.Clamp01(speed / Math.Max(0.1f, SprintSpeedMps));
            if (speed <= StandSpeedThreshold)
            {
                activity = 0f;
            }

            float crouch = MathUtil.Clamp01(crouch01);
            float swing = MathUtil.Sin(Phase);
            float thigh = MaxThighDegrees * activity * (1f - crouch * 0.45f);
            float knee = MaxKneeDegrees * activity * (1f - crouch * 0.35f);
            float arm = MaxArmDegrees * activity;

            GaitPose pose = new GaitPose();
            pose.ThighLeftDegrees = thigh * swing;
            pose.ThighRightDegrees = -thigh * swing;

            // The knee folds while its leg travels forward and straightens as the
            // foot plants: sin(phase + pi/2) peaks a quarter cycle after the swing,
            // so the bend never lands on the planted leg.
            float bendLeft = MathUtil.Clamp01(MathUtil.Sin(Phase + MathUtil.HalfPi));
            float bendRight = MathUtil.Clamp01(MathUtil.Sin(Phase + MathUtil.HalfPi + MathUtil.Pi));
            pose.KneeLeftDegrees = knee * bendLeft + crouch * 26f;
            pose.KneeRightDegrees = knee * bendRight + crouch * 26f;

            // Arms counter swing the legs, at a little over half the amplitude: a
            // soldier carrying a rifle does not march.
            pose.UpperArmLeftDegrees = -arm * swing * 0.6f;
            pose.UpperArmRightDegrees = arm * swing * 0.6f;
            pose.ElbowLeftDegrees = MaxElbowDegrees * activity * 0.5f * (0.5f + 0.5f * bendLeft);
            pose.ElbowRightDegrees = MaxElbowDegrees * activity * 0.5f * (0.5f + 0.5f * bendRight);

            // Two bobs per cycle, one per foot plant.
            pose.BobMeters = BobMetersPerStep * activity * (0.5f - 0.5f * MathUtil.Cos(Phase * 2f));
            pose.RollDegrees = MaxRollDegrees * activity * MathUtil.Sin(Phase);
            pose.Breath01 = MathUtil.Clamp01(breath01) * (1f - activity * 0.5f);
            return pose;
        }

        /// <summary>
        /// Rotation of the whole body once it has been killed, in degrees of forward
        /// pitch and 0..1 of the fall.  A body that disappears on death is a bug the
        /// player can see; a body that drops is the shot reading as a kill.
        /// </summary>
        public void Fall(float secondsSinceDeath, out float pitchDegrees, out float dropMeters)
        {
            float t = MathUtil.Clamp01(secondsSinceDeath / Math.Max(0.05f, FallDurationSeconds));
            // Ease out with a slight overshoot so the body lands instead of floating.
            float eased = 1f - (1f - t) * (1f - t) * (1f - t);
            pitchDegrees = 92f * eased;
            dropMeters = 0.06f * eased + 0.10f * MathUtil.Clamp01((t - 0.82f) / 0.18f);
        }
    }
}
