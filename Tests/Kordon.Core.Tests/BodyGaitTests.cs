using Kordon.Core.Characters;
using Kordon.Core.Utility;
using Xunit;

namespace Kordon.Core.Tests
{
    /// <summary>
    /// The body animation is real maths, not a shader trick, so it is tested like the
    /// rest of the rules: a body that stands still must not march, a walking body must
    /// keep its feet on a believable cadence, and a dead body must actually fall.
    /// </summary>
    public sealed class BodyGaitTests
    {
        [Fact]
        public void StandingBodyDoesNotAnimate()
        {
            BodyGait gait = new BodyGait();
            for (int frame = 0; frame < 120; frame++)
            {
                gait.Advance(0f, 1f / 60f);
            }

            GaitPose pose = gait.Sample(0f);
            Assert.Equal(0f, pose.ThighLeftDegrees, 3);
            Assert.Equal(0f, pose.ThighRightDegrees, 3);
            Assert.Equal(0f, pose.KneeLeftDegrees, 3);
            Assert.Equal(0f, pose.UpperArmRightDegrees, 3);
            Assert.True(pose.BobMeters < 1e-4f);
            Assert.Equal(0f, gait.Phase, 3);
        }

        [Fact]
        public void CadenceFollowsSpeedOverStride()
        {
            // A full cycle covers WalkStrideMeters.  At that stride and a speed of
            // one stride per second the phase must have advanced exactly one turn.
            BodyGait gait = new BodyGait();
            gait.Phase = 0f;
            float speed = 1.4f; // speed below SprintSpeed, so blend = speed / sprint

            float blend = MathUtil.Clamp01(speed / gait.SprintSpeedMps);
            float stride = MathUtil.Lerp(gait.WalkStrideMeters, gait.SprintStrideMeters, blend);
            float seconds = stride / speed;

            int frames = (int)(seconds * 240f);
            for (int frame = 0; frame < frames; frame++)
            {
                gait.Advance(speed, 1f / 240f);
            }

            float expected = MathUtil.Repeat((speed / stride) * MathUtil.TwoPi * (frames / 240f),
                MathUtil.TwoPi);
            Assert.InRange(MathUtil.WrapAngle((gait.Phase - expected) * MathUtil.Rad2Deg), -2f, 2f);
        }

        [Fact]
        public void LegsAlternateAndStayWithinTheirJoints()
        {
            BodyGait gait = new BodyGait();
            float minLeft = float.MaxValue;
            float maxLeft = float.MinValue;
            for (int frame = 0; frame < 240; frame++)
            {
                gait.Advance(3.2f, 1f / 60f);
                GaitPose pose = gait.Sample(3.2f);
                Assert.Equal(-pose.ThighLeftDegrees, pose.ThighRightDegrees, 3);
                Assert.InRange(pose.ThighLeftDegrees, -gait.MaxThighDegrees, gait.MaxThighDegrees);
                Assert.InRange(pose.KneeLeftDegrees, 0f, gait.MaxKneeDegrees + 0.001f);
                Assert.InRange(pose.KneeRightDegrees, 0f, gait.MaxKneeDegrees + 0.001f);
                if (pose.ThighLeftDegrees > maxLeft)
                {
                    maxLeft = pose.ThighLeftDegrees;
                }

                if (pose.ThighLeftDegrees < minLeft)
                {
                    minLeft = pose.ThighLeftDegrees;
                }
            }

            // A sprinting body has to actually swing: a pose that never moves is the
            // static mesh this feature replaces.
            Assert.True(maxLeft - minLeft > gait.MaxThighDegrees * 0.85f, "legs barely move");
        }

        [Fact]
        public void CrouchFoldsTheKneesAndShortensTheStride()
        {
            BodyGait gait = new BodyGait();
            gait.Advance(1.6f, 0.4f);
            GaitPose standing = gait.Sample(1.6f, 0f);
            GaitPose crouched = gait.Sample(1.6f, 1f);
            Assert.True(crouched.KneeLeftDegrees > standing.KneeLeftDegrees);
            Assert.True(crouched.KneeRightDegrees > standing.KneeRightDegrees);
            Assert.True(System.Math.Abs(crouched.ThighLeftDegrees) < System.Math.Abs(standing.ThighLeftDegrees));
        }

        [Fact]
        public void KilledBodyFallsOverAndStaysDown()
        {
            BodyGait gait = new BodyGait();
            gait.Fall(0f, out float start, out float dropStart);
            Assert.Equal(0f, start, 3);
            Assert.True(dropStart < 0.01f);

            float previous = start;
            for (float time = 0.1f; time < gait.FallDurationSeconds; time += 0.1f)
            {
                gait.Fall(time, out float pitch, out _);
                Assert.True(pitch >= previous);
                previous = pitch;
            }

            gait.Fall(gait.FallDurationSeconds * 4f, out float settled, out float dropSettled);
            Assert.InRange(settled, 88f, 96f);
            Assert.InRange(dropSettled, 0.1f, 0.25f);
        }
    }
}
