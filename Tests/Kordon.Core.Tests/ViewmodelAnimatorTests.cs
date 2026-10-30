using System;
using Kordon.Core.Weapons;
using Xunit;

namespace Kordon.Core.Tests
{
    /// <summary>
    /// The procedural viewmodel animation: pose blending, sway, recoil, the magazine
    /// timeline and the charging handle.
    ///
    /// This is what the player stares at for the whole match, so it is tested the same
    /// way the rules are: fixed inputs, fixed dt, exact expectations.  "Looks fine in
    /// the editor" is not available here, and a spring that diverges or a magazine that
    /// never seats would be visible in every frame of the game.
    /// </summary>
    public sealed class ViewmodelAnimatorTests
    {
        private const float Dt = 1f / 60f;

        private static ViewmodelInput Input(bool aiming = false, bool sprinting = false,
            float speed = 0f, bool grounded = true) => new ViewmodelInput
        {
            Aiming = aiming,
            Sprinting = sprinting,
            Speed01 = speed,
            Grounded = grounded,
            Breath01 = 0f,
        };

        private static ViewmodelAnimator Animator() =>
            new ViewmodelAnimator(ViewmodelConfig.Default);

        private static void Run(ViewmodelAnimator animator, ViewmodelInput input, int frames)
        {
            for (int i = 0; i < frames; i++)
            {
                animator.Tick(Dt, input);
            }
        }

        [Fact]
        public void HipPoseIsStableAtRest()
        {
            ViewmodelAnimator animator = Animator();
            Run(animator, Input(), 120);

            ViewmodelPose pose = animator.Pose;
            Assert.True(pose.X > 0.10f && pose.X < 0.20f, "hip x out of range: " + pose.X);
            Assert.True(pose.Y > -0.25f && pose.Y < -0.10f, "hip y out of range: " + pose.Y);
            Assert.True(pose.Z > 0.25f && pose.Z < 0.50f, "hip z out of range: " + pose.Z);
            Assert.Equal(ViewmodelState.Ready, pose.State);
        }

        [Fact]
        public void AimingPutsTheSightsOnTheCameraAxis()
        {
            ViewmodelAnimator animator = Animator();
            Run(animator, Input(aiming: true), 120);

            ViewmodelPose pose = animator.Pose;
            Assert.Equal(ViewmodelState.Aiming, pose.State);
            Assert.True(pose.AimBlend > 0.95f, "aim blend did not settle: " + pose.AimBlend);
            Assert.InRange(pose.X, -0.02f, 0.02f);
            // the sight height is what puts the irons in front of the eye
            Assert.InRange(pose.Y, -ViewmodelConfig.Default.SightHeight - 0.02f,
                -ViewmodelConfig.Default.SightHeight + 0.03f);
        }

        [Fact]
        public void AimBlendReturnsToHipWhenAimingStops()
        {
            ViewmodelAnimator animator = Animator();
            Run(animator, Input(aiming: true), 90);
            Assert.True(animator.Pose.AimBlend > 0.9f);

            Run(animator, Input(), 90);
            Assert.True(animator.Pose.AimBlend < 0.1f, "aim blend stuck: " + animator.Pose.AimBlend);
        }

        [Fact]
        public void SprintingOnlyTakesOverWhenNothingElseIsHappening()
        {
            ViewmodelAnimator animator = Animator();
            animator.Notify(ViewmodelEvent.MagazineOut);
            Run(animator, Input(sprinting: true, speed: 1f), 30);
            Assert.Equal(ViewmodelState.ReloadOut, animator.State);

            animator.Notify(ViewmodelEvent.ReloadComplete);
            Run(animator, Input(sprinting: true, speed: 1f), 30);
            Assert.Equal(ViewmodelState.Sprinting, animator.State);

            Run(animator, Input(), 30);
            Assert.Equal(ViewmodelState.Ready, animator.State);
        }

        [Fact]
        public void RecoilKicksBackThenSettles()
        {
            ViewmodelAnimator animator = Animator();
            Run(animator, Input(), 120);
            float restingZ = animator.Pose.Z;
            float restingPitch = animator.Pose.Pitch;

            animator.AddRecoil(1f);
            animator.Tick(Dt, Input());
            Assert.True(animator.Pose.Z > restingZ + 0.005f, "recoil did not push the weapon back");
            Assert.True(animator.Pose.Pitch < restingPitch - 0.5f, "recoil did not lift the muzzle");

            Run(animator, Input(), 60);
            Assert.InRange(animator.Pose.Z, restingZ - 0.01f, restingZ + 0.01f);
            Assert.InRange(animator.Pose.Pitch, restingPitch - 1.5f, restingPitch + 1.5f);
        }

        [Fact]
        public void RecoilNeverDivergesEvenWithAbuse()
        {
            ViewmodelAnimator animator = Animator();
            for (int i = 0; i < 600; i++)
            {
                animator.AddRecoil(1f);
                animator.Tick(0.1f, Input());
                Assert.False(float.IsNaN(animator.Pose.Pitch) || float.IsInfinity(animator.Pose.Pitch));
                Assert.InRange(animator.Pose.Z, 0.0f, 1.2f);
            }
        }

        [Fact]
        public void MagazineDropsOutAndDisappears()
        {
            ViewmodelAnimator animator = Animator();
            Run(animator, Input(), 30);
            Assert.True(animator.Pose.MagazineVisible);
            Assert.Equal(0f, animator.Pose.MagazineY);

            animator.Notify(ViewmodelEvent.MagazineOut);
            // The first beat is the weapon coming round into the reload pose: the
            // magazine is still seated and still in the well.  A tenth of a second in.
            Run(animator, Input(), 6);
            Assert.True(animator.Pose.MagazineVisible, "the magazine vanished instantly");
            Assert.True(animator.Pose.MagazineY > -0.005f,
                "the magazine left the well before the weapon came round: " +
                animator.Pose.MagazineY);

            // Then it goes, and goes fast: a quarter of a second later it is clear.
            Run(animator, Input(), 15);
            Assert.True(animator.Pose.MagazineY < -0.02f,
                "the magazine did not move: " + animator.Pose.MagazineY);

            // 0.7 s of drop in total, so another second is more than enough.
            Run(animator, Input(), 90);
            Assert.False(animator.Pose.MagazineVisible, "the dropped magazine is still drawn");
            Assert.True(animator.MagazineOut);
        }

        [Fact]
        public void FreshMagazineRisesAndSeatsExactly()
        {
            ViewmodelAnimator animator = Animator();
            animator.Notify(ViewmodelEvent.MagazineOut);
            Run(animator, Input(), 90);
            Assert.False(animator.Pose.MagazineVisible);

            animator.Notify(ViewmodelEvent.MagazineIn);
            Run(animator, Input(), 2);
            Assert.True(animator.Pose.MagazineVisible, "the new magazine never appeared");

            // insert is 0.30 s, and the seat needs a moment to settle
            Run(animator, Input(), 60);
            ViewmodelPose pose = animator.Pose;
            Assert.True(pose.MagazineVisible);
            Assert.Equal(0f, pose.MagazineX, 3);
            Assert.Equal(0f, pose.MagazineY, 3);
            Assert.Equal(0f, pose.MagazineZ, 3);
            Assert.Equal(0f, pose.MagazinePitch, 3);
            Assert.Equal(0f, pose.MagazineRoll, 3);
            Assert.False(animator.MagazineOut);
        }

        [Fact]
        public void InspectPullsTheMagazineOutTowardsTheEye()
        {
            ViewmodelAnimator animator = Animator();
            Run(animator, Input(), 30);

            animator.Notify(ViewmodelEvent.InspectStart);
            Run(animator, Input(), 30);

            ViewmodelPose pose = animator.Pose;
            Assert.Equal(ViewmodelState.Inspecting, pose.State);
            Assert.True(pose.MagazineVisible);
            Assert.True(pose.MagazineY < -0.05f, "the magazine is still in the well");
            Assert.True(Math.Abs(pose.MagazineRoll) > 30f, "the magazine was not turned to be read");
            Assert.True(animator.MagazineOut, "the HUD may only show a count while the magazine is out");

            animator.Notify(ViewmodelEvent.InspectEnd);
            Run(animator, Input(), 40);
            Assert.Equal(0f, animator.Pose.MagazineY, 3);
            Assert.False(animator.MagazineOut);
        }

        [Fact]
        public void ChargingHandleRacksBackAndRunsHome()
        {
            ViewmodelAnimator animator = Animator();
            animator.Notify(ViewmodelEvent.BoltRelease);

            float peak = 0f;
            for (int i = 0; i < 30; i++)
            {
                animator.Tick(Dt, Input());
                peak = Math.Max(peak, animator.Pose.BoltTravel);
            }

            Assert.True(peak > 0.95f, "the charging handle never reached the rear: " + peak);
            Run(animator, Input(), 30);
            Assert.True(animator.Pose.BoltTravel < 0.02f, "the charging handle never ran home");
        }

        [Fact]
        public void SwayFollowsTheLookAndReturnsToCentre()
        {
            ViewmodelAnimator animator = Animator();
            Run(animator, Input(), 60);
            float centre = animator.Pose.Yaw;

            ViewmodelInput looking = Input();
            looking.YawRate = 220f;
            Run(animator, looking, 10);
            float swung = animator.Pose.Yaw;
            Assert.True(Math.Abs(swung - centre) > 0.4f,
                "the weapon did not lag behind a fast turn: " + (swung - centre));

            ViewmodelInput still = Input();
            still.YawRate = 0f;
            Run(animator, still, 90);
            Assert.InRange(animator.Pose.Yaw, centre - 0.6f, centre + 0.6f);
        }

        [Fact]
        public void BobFollowsTheFeetAndIsDampedWhenAiming()
        {
            ViewmodelAnimator animator = Animator();
            ViewmodelInput running = Input(speed: 1f);
            Run(animator, running, 90);
            float min = 1f, max = -1f;
            for (int i = 0; i < 120; i++)
            {
                animator.Tick(Dt, running);
                min = Math.Min(min, animator.Pose.X);
                max = Math.Max(max, animator.Pose.X);
            }

            float hipAmplitude = max - min;
            Assert.True(hipAmplitude > 0.015f, "running produced no bob: " + hipAmplitude);

            ViewmodelAnimator aiming = Animator();
            ViewmodelInput runningAimed = Input(aiming: true, speed: 1f);
            Run(aiming, runningAimed, 90);
            min = 1f;
            max = -1f;
            for (int i = 0; i < 120; i++)
            {
                aiming.Tick(Dt, runningAimed);
                min = Math.Min(min, aiming.Pose.X);
                max = Math.Max(max, aiming.Pose.X);
            }

            Assert.True(max - min < hipAmplitude * 0.6f,
                "aiming did not damp the bob: " + (max - min) + " vs " + hipAmplitude);
        }

        [Fact]
        public void LandingDipsTheWeaponThenRecovers()
        {
            ViewmodelAnimator animator = Animator();
            Run(animator, Input(), 90);
            float resting = animator.Pose.Y;

            ViewmodelInput landing = Input();
            landing.LandingImpulse = 1f;
            float dip = 0f;
            float dipPitch = 0f;
            for (int i = 0; i < 20; i++)
            {
                animator.Tick(Dt, i == 0 ? landing : Input());
                dip = Math.Min(dip, animator.Pose.Y - resting);
                dipPitch = Math.Max(dipPitch, animator.Pose.Pitch);
            }

            Assert.True(dip < -0.008f, "landing did not dip the weapon: " + dip);
            Assert.True(dipPitch > 1f, "landing did not lift the muzzle: " + dipPitch);

            Run(animator, Input(), 90);
            Assert.InRange(animator.Pose.Y, resting - 0.01f, resting + 0.01f);
        }

        [Fact]
        public void SameInputsGiveTheSamePose()
        {
            ViewmodelAnimator first = Animator();
            ViewmodelAnimator second = Animator();

            for (int i = 0; i < 200; i++)
            {
                ViewmodelInput input = Input(aiming: i % 40 < 20, speed: (i % 30) / 30f);
                input.YawRate = (i % 17 - 8) * 12f;
                first.Tick(Dt, input);
                second.Tick(Dt, input);
            }

            Assert.Equal(first.Pose.X, second.Pose.X);
            Assert.Equal(first.Pose.Y, second.Pose.Y);
            Assert.Equal(first.Pose.Z, second.Pose.Z);
            Assert.Equal(first.Pose.Pitch, second.Pose.Pitch);
            Assert.Equal(first.Pose.Yaw, second.Pose.Yaw);
        }

        [Fact]
        public void TriggerSnapsBackAndReleases()
        {
            ViewmodelAnimator animator = Animator();
            Run(animator, Input(), 10);
            Assert.True(animator.Pose.TriggerTravel < 0.001f, "the trigger was pulled idle");

            animator.PullTrigger();
            animator.Tick(1f / 240f, Input());
            Assert.True(animator.Pose.TriggerTravel > 0.8f,
                "the trigger did not snap back: " + animator.Pose.TriggerTravel);

            // held for a beat - a pull that returns within the frame it was pulled in is
            // a pull nobody can see
            Run(animator, Input(), 6);
            Assert.True(animator.Pose.TriggerTravel > 0.9f,
                "the trigger let go before it could be seen: " +
                animator.Pose.TriggerTravel);

            // then released on its own
            Run(animator, Input(), 20);
            Assert.True(animator.Pose.TriggerTravel < 0.05f,
                "the trigger never reset: " + animator.Pose.TriggerTravel);
        }

        [Fact]
        public void FiringPullsTheTriggerThroughTheRecoilImpulse()
        {
            ViewmodelAnimator animator = Animator();
            Run(animator, Input(), 10);

            ViewmodelInput shot = Input();
            shot.RecoilImpulse = 0.4f;
            animator.Tick(1f / 120f, shot);
            Assert.True(animator.Pose.TriggerTravel > 0.8f,
                "a shot did not move the trigger: " + animator.Pose.TriggerTravel);
        }

        [Fact]
        public void MagazineSeatIsFeltByTheWeapon()
        {
            // The last fifth of the insert is the seat: the weapon takes a small jolt at
            // the moment the magazine locks, which is what makes the change read.
            ViewmodelAnimator animator = Animator();
            animator.Notify(ViewmodelEvent.MagazineOut);
            Run(animator, Input(), 40);
            animator.Notify(ViewmodelEvent.MagazineIn);

            bool seated = false;
            for (int frame = 0; frame < 120; frame++)
            {
                animator.Tick(1f / 120f, Input());
                // the seat is the last beat: the magazine dips as it locks home
                if (animator.Pose.MagazineY < -0.0008f && animator.Pose.MagazineY > -0.02f)
                {
                    seated = true;
                }
            }

            Assert.True(seated, "the magazine never took the seat");
            Assert.True(animator.Pose.MagazineY > -0.0001f,
                "the magazine did not end up seated: " + animator.Pose.MagazineY);
        }

        [Fact]
        public void ZeroAndLongFramesAreSafe()
        {
            ViewmodelAnimator animator = Animator();
            animator.Tick(0f, Input());
            animator.Tick(-1f, Input());
            animator.Tick(10f, Input());

            ViewmodelPose pose = animator.Pose;
            Assert.False(float.IsNaN(pose.X) || float.IsNaN(pose.Y) || float.IsNaN(pose.Z));
            Assert.InRange(pose.Z, 0f, 1.5f);
        }

        [Fact]
        public void SupportHandStaysOnTheForeEndUntilAReload()
        {
            ViewmodelAnimator animator = Animator();
            Run(animator, Input(), 90);
            Assert.True(animator.Pose.SupportHandBlend < 0.001f,
                "the support hand moved with no reload: " + animator.Pose.SupportHandBlend);

            animator.Notify(ViewmodelEvent.MagazineOut);
            Run(animator, Input(), 6);
            Assert.True(animator.Pose.SupportHandBlend > 0.3f,
                "the support hand did not leave the fore-end: " + animator.Pose.SupportHandBlend);
        }

        [Fact]
        public void SupportHandReturnsToTheForeEndAfterTheReload()
        {
            ViewmodelAnimator animator = Animator();
            animator.Notify(ViewmodelEvent.MagazineOut);
            Run(animator, Input(), 20);
            Assert.True(animator.Pose.SupportHandBlend > 0.8f,
                "the support hand never reached the magazine: " + animator.Pose.SupportHandBlend);

            animator.Notify(ViewmodelEvent.MagazineIn);
            Run(animator, Input(), 20);
            animator.Notify(ViewmodelEvent.ReloadComplete);
            Run(animator, Input(), 90);

            Assert.Equal(ViewmodelState.Ready, animator.State);
            Assert.True(animator.Pose.SupportHandBlend < 0.08f,
                "the support hand did not return: " + animator.Pose.SupportHandBlend);
        }

        [Fact]
        public void FumbleIsVisibleAndRecovers()
        {
            ViewmodelAnimator animator = Animator();
            animator.Notify(ViewmodelEvent.FumbleStart);
            Run(animator, Input(), 20);
            Assert.Equal(ViewmodelState.Fumble, animator.State);

            animator.Notify(ViewmodelEvent.FumbleRecovered);
            Run(animator, Input(), 40);
            Assert.Equal(ViewmodelState.Ready, animator.State);
        }
    }
}
