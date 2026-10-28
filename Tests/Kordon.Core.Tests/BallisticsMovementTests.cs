using System.Collections.Generic;
using Kordon.Core.Ballistics;
using Kordon.Core.Data;
using Kordon.Core.Loadout;
using Kordon.Core.Movement;
using Kordon.Core.Utility;
using Xunit;

namespace Kordon.Core.Tests
{
    /// <summary>
    /// Recoil and spread react to weapon, stance, movement, breathing and wounds;
    /// movement is slow and inertial; stamina is short; nothing is silent.
    /// </summary>
    public class BallisticsMovementTests
    {
        private static readonly DefinitionLibrary Library = TestData.Database.Definitions;
        private static readonly WeaponDefinition Rifle = Library.GetWeapon("vk74");

        private static RecoilShot Shoot(RecoilSolver solver, ShotContext context, float now,
            DeterministicRandom random = null) => solver.Fire(Rifle.Recoil, context, random, now);

        [Fact]
        public void FirstShotIsSoft_AndTheClimbRampsToAPlateau()
        {
            RecoilSolver solver = new RecoilSolver();
            List<float> pitches = new List<float>();
            for (int i = 0; i < 8; i++)
            {
                pitches.Add(Shoot(solver, ShotContext.Calm(), i * 0.1f).PitchDeg);
            }

            RecoilProfile profile = Rifle.Recoil;
            Assert.Equal(profile.VerticalBaseDeg * profile.FirstShotMultiplier * profile.AimingVerticalMultiplier,
                pitches[0], 3);
            Assert.True(pitches[1] > pitches[0]);
            Assert.True(pitches[5] > pitches[1]);
            Assert.Equal(pitches[6], pitches[7], 3);
            Assert.Equal(profile.VerticalBaseDeg * profile.PlateauMultiplier * profile.AimingVerticalMultiplier,
                pitches[7], 3);
            Assert.Equal(8, solver.ShotIndex);
            Assert.True(solver.AccumulatedPitch > 0f);
        }

        [Fact]
        public void AimingTightensRecoilAndSpread_HipFireDoesNeither()
        {
            ShotContext aiming = ShotContext.Calm();
            ShotContext hip = ShotContext.Calm();
            hip.IsAiming = false;

            float aimingPitch = Shoot(new RecoilSolver(), aiming, 0f).PitchDeg;
            float hipPitch = Shoot(new RecoilSolver(), hip, 0f).PitchDeg;
            Assert.True(aimingPitch < hipPitch);
            Assert.Equal(hipPitch / Rifle.Recoil.AimingVerticalMultiplier, aimingPitch, 3);

            SpreadModel spread = new SpreadModel();
            Assert.True(spread.ComputeCone(Rifle.Spread, aiming) < spread.ComputeCone(Rifle.Spread, hip));
            Assert.Equal(Rifle.Spread.AimingBaseDeg, spread.ComputeCone(Rifle.Spread, aiming), 3);
            Assert.Equal(Rifle.Spread.HipBaseDeg, spread.ComputeCone(Rifle.Spread, hip), 3);
        }

        [Fact]
        public void LowerStances_AreSteadier()
        {
            ShotContext stand = ShotContext.Calm();
            ShotContext crouch = ShotContext.Calm();
            crouch.Stance = Stance.Crouch;
            ShotContext prone = ShotContext.Calm();
            prone.Stance = Stance.Prone;

            float standPitch = Shoot(new RecoilSolver(), stand, 0f).PitchDeg;
            float crouchPitch = Shoot(new RecoilSolver(), crouch, 0f).PitchDeg;
            float pronePitch = Shoot(new RecoilSolver(), prone, 0f).PitchDeg;
            Assert.True(pronePitch < crouchPitch);
            Assert.True(crouchPitch < standPitch);
            Assert.Equal(standPitch * Rifle.Recoil.CrouchMultiplier, crouchPitch, 3);
            Assert.Equal(standPitch * Rifle.Recoil.ProneMultiplier, pronePitch, 3);

            SpreadModel spread = new SpreadModel();
            Assert.True(spread.ComputeCone(Rifle.Spread, prone) < spread.ComputeCone(Rifle.Spread, crouch));
            Assert.True(spread.ComputeCone(Rifle.Spread, crouch) < spread.ComputeCone(Rifle.Spread, stand));
        }

        [Fact]
        public void AirborneShots_AreDramaticallyWorse()
        {
            ShotContext grounded = ShotContext.Calm();
            ShotContext airborne = ShotContext.Calm();
            airborne.IsGrounded = false;

            float groundedPitch = Shoot(new RecoilSolver(), grounded, 0f).PitchDeg;
            float airbornePitch = Shoot(new RecoilSolver(), airborne, 0f).PitchDeg;
            Assert.Equal(groundedPitch * Rifle.Recoil.AirborneMultiplier, airbornePitch, 3);
            Assert.True(airbornePitch > groundedPitch * 3f);

            SpreadModel spread = new SpreadModel();
            Assert.Equal(Rifle.Spread.AimingBaseDeg * Rifle.Spread.AirborneMultiplier,
                spread.ComputeCone(Rifle.Spread, airborne), 3);
        }

        [Fact]
        public void LandingPenalty_DecaysOverTheRecoveryWindow()
        {
            float PitchAt(float sinceLanding)
            {
                ShotContext context = ShotContext.Calm();
                context.TimeSinceLanding = sinceLanding;
                return Shoot(new RecoilSolver(), context, 0f).PitchDeg;
            }

            float calm = PitchAt(5f);
            float justLanded = PitchAt(0f);
            float halfWay = PitchAt(Rifle.Recoil.LandingRecoverySeconds * 0.5f);

            Assert.Equal(calm * Rifle.Recoil.LandingPenaltyMultiplier, justLanded, 3);
            Assert.True(justLanded > halfWay);
            Assert.True(halfWay > calm);
            Assert.Equal(calm, PitchAt(Rifle.Recoil.LandingRecoverySeconds), 3);
        }

        [Fact]
        public void MovementAndSprint_DegradeWeaponControl()
        {
            ShotContext calm = ShotContext.Calm();
            ShotContext walking = ShotContext.Calm();
            walking.Speed = MovementProfile.WalkSpeed;
            ShotContext sprinting = ShotContext.Calm();
            sprinting.Speed = MovementProfile.SprintSpeed;

            float calmPitch = Shoot(new RecoilSolver(), calm, 0f).PitchDeg;
            float walkPitch = Shoot(new RecoilSolver(), walking, 0f).PitchDeg;
            float sprintPitch = Shoot(new RecoilSolver(), sprinting, 0f).PitchDeg;
            Assert.True(calmPitch < walkPitch);
            Assert.True(walkPitch < sprintPitch);
            Assert.Equal(calmPitch * Rifle.Recoil.MovementMultiplierAtSprint, sprintPitch, 3);

            SpreadModel spread = new SpreadModel();
            float calmCone = spread.ComputeCone(Rifle.Spread, calm);
            float sprintCone = spread.ComputeCone(Rifle.Spread, sprinting);
            Assert.True(sprintCone > calmCone + Rifle.Spread.SprintAddDeg);
        }

        [Fact]
        public void ExhaustionSuppressionAndWounds_AllMakeTheWeaponWorse()
        {
            ShotContext calm = ShotContext.Calm();
            float calmPitch = Shoot(new RecoilSolver(), calm, 0f).PitchDeg;

            ShotContext exhausted = ShotContext.Calm();
            exhausted.Stamina01 = 0f;
            Assert.Equal(calmPitch * Rifle.Recoil.StaminaExhaustedMultiplier,
                Shoot(new RecoilSolver(), exhausted, 0f).PitchDeg, 3);

            ShotContext suppressed = ShotContext.Calm();
            suppressed.Suppression01 = 1f;
            Assert.Equal(calmPitch * Rifle.Recoil.SuppressionMultiplier,
                Shoot(new RecoilSolver(), suppressed, 0f).PitchDeg, 3);

            ShotContext wounded = ShotContext.Calm();
            wounded.AimStability = 0.5f;
            Assert.Equal(calmPitch * 2f, Shoot(new RecoilSolver(), wounded, 0f).PitchDeg, 3);

            ShotContext crippled = ShotContext.Calm();
            crippled.AimStability = 0.1f;
            Assert.Equal(calmPitch * 2.5f, Shoot(new RecoilSolver(), crippled, 0f).PitchDeg, 3);

            ShotContext leaning = ShotContext.Calm();
            leaning.IsLeaning = true;
            Assert.Equal(calmPitch * Rifle.Recoil.LeanMultiplier,
                Shoot(new RecoilSolver(), leaning, 0f).PitchDeg, 3);

            SpreadModel spread = new SpreadModel();
            Assert.True(spread.ComputeCone(Rifle.Spread, exhausted) > spread.ComputeCone(Rifle.Spread, calm));
            Assert.True(spread.ComputeCone(Rifle.Spread, suppressed) > spread.ComputeCone(Rifle.Spread, calm));
            Assert.Equal(spread.ComputeCone(Rifle.Spread, calm) * 2f,
                spread.ComputeCone(Rifle.Spread, wounded), 3);
        }

        [Fact]
        public void RecoilRecoversAfterAPause_AndTheClimbResets()
        {
            RecoilSolver solver = new RecoilSolver();
            float firstShot = Shoot(solver, ShotContext.Calm(), 0f).PitchDeg;
            for (int i = 1; i < 6; i++)
            {
                Shoot(solver, ShotContext.Calm(), i * 0.1f);
            }

            float accumulated = solver.AccumulatedPitch;
            Assert.True(accumulated > 0f);

            solver.Tick(0.05f, Rifle.Recoil);
            Assert.Equal(accumulated, solver.AccumulatedPitch, 4);

            solver.Tick(0.1f, Rifle.Recoil);
            Assert.True(solver.AccumulatedPitch < accumulated);
            Assert.True(solver.AccumulatedPitch > 0f);

            solver.Tick(1f, Rifle.Recoil);
            Assert.Equal(0f, solver.AccumulatedPitch, 4);
            Assert.Equal(0, solver.ShotIndex);
            Assert.Equal(firstShot, Shoot(solver, ShotContext.Calm(), 5f).PitchDeg, 4);
        }

        [Fact]
        public void RecoilIsDeterministicForAGivenSeed()
        {
            List<RecoilShot> Run(ulong seed)
            {
                RecoilSolver solver = new RecoilSolver();
                DeterministicRandom random = new DeterministicRandom(seed);
                ShotContext context = ShotContext.Calm();
                context.IsAiming = false;
                List<RecoilShot> shots = new List<RecoilShot>();
                for (int i = 0; i < 10; i++)
                {
                    shots.Add(solver.Fire(Rifle.Recoil, context, random, i * 0.1f));
                }

                return shots;
            }

            List<RecoilShot> a = Run(7UL);
            List<RecoilShot> b = Run(7UL);
            List<RecoilShot> c = Run(8UL);
            for (int i = 0; i < a.Count; i++)
            {
                Assert.Equal(a[i].PitchDeg, b[i].PitchDeg, 5);
                Assert.Equal(a[i].YawDeg, b[i].YawDeg, 5);
            }

            bool anyDifferent = false;
            for (int i = 0; i < a.Count; i++)
            {
                if (System.Math.Abs(a[i].YawDeg - c[i].YawDeg) > 1e-5f)
                {
                    anyDifferent = true;
                }
            }

            Assert.True(anyDifferent);
            Assert.All(a, shot => Assert.True(shot.PitchDeg >= 0f));
        }

        [Fact]
        public void SpreadCone_GrowsWithEveryDocumentedFactor()
        {
            SpreadModel spread = new SpreadModel();
            SpreadProfile profile = Rifle.Spread;

            ShotContext hip = ShotContext.Calm();
            hip.IsAiming = false;
            float baseline = spread.ComputeCone(profile, hip);
            Assert.Equal(profile.HipBaseDeg, baseline, 3);

            ShotContext moving = hip;
            moving.Speed = 2.55f;
            Assert.Equal(baseline + 2.55f * profile.MovementAddDegPerMps, spread.ComputeCone(profile, moving), 3);

            ShotContext leaning = hip;
            leaning.IsLeaning = true;
            Assert.Equal(baseline + profile.LeanAddDeg, spread.ComputeCone(profile, leaning), 3);

            ShotContext tired = hip;
            tired.Stamina01 = 0f;
            Assert.Equal(baseline + profile.StaminaExhaustedAddDeg, spread.ComputeCone(profile, tired), 3);

            ShotContext suppressed = hip;
            suppressed.Suppression01 = 1f;
            Assert.Equal(baseline + profile.SuppressionAddDeg, spread.ComputeCone(profile, suppressed), 3);
        }

        [Fact]
        public void Bloom_BuildsUpToACapAndRecoversOverTime()
        {
            SpreadModel spread = new SpreadModel();
            SpreadProfile profile = Rifle.Spread;
            ShotContext hip = ShotContext.Calm();
            hip.IsAiming = false;
            float baseline = spread.ComputeCone(profile, hip);

            for (int i = 0; i < 40; i++)
            {
                spread.AddBloom(profile);
            }

            Assert.Equal(profile.BloomMaxDeg, spread.Bloom, 3);
            Assert.Equal(baseline + profile.BloomMaxDeg, spread.ComputeCone(profile, hip), 3);

            spread.Tick(0.2f, profile);
            Assert.Equal(profile.BloomMaxDeg - profile.BloomRecoveryDegPerSecond * 0.2f, spread.Bloom, 3);

            spread.Tick(5f, profile);
            Assert.Equal(0f, spread.Bloom, 3);
            Assert.Equal(baseline, spread.ComputeCone(profile, hip), 3);
        }

        [Fact]
        public void SampledDirections_StayInsideTheCone()
        {
            DeterministicRandom random = new DeterministicRandom(99);
            KordonVec3 forward = KordonVec3.Forward;
            Assert.Equal(forward, SpreadModel.SampleDirection(forward, 0f, random));

            float maxAngle = 0f;
            for (int i = 0; i < 500; i++)
            {
                KordonVec3 sample = SpreadModel.SampleDirection(forward, 2.6f, random);
                Assert.Equal(1f, sample.Magnitude, 3);
                maxAngle = System.Math.Max(maxAngle, KordonVec3.Angle(forward, sample));
            }

            Assert.True(maxAngle <= 2.6f + 0.001f, $"sample escaped the cone: {maxAngle}");
            Assert.True(maxAngle > 1f, "samples bunched in the middle");
        }

        [Fact]
        public void MovementIsSlowAndInertial()
        {
            MovementModel model = new MovementModel();
            MovementIntent intent = new MovementIntent { Forward = 1f, Stance = Stance.Stand };
            KordonVec3 velocity = KordonVec3.Zero;
            float dt = 1f / 60f;

            MovementStep first = model.Integrate(velocity, KordonVec3.Forward, intent, true, dt);
            Assert.True(first.Velocity.Magnitude < MovementProfile.WalkSpeed);
            Assert.Equal(MovementProfile.GroundAcceleration * dt, first.Velocity.Magnitude, 3);
            Assert.Equal(Gait.Walk, first.Gait);
            Assert.False(first.Sprinting);

            velocity = first.Velocity;
            for (int i = 0; i < 5; i++)
            {
                velocity = model.Integrate(velocity, KordonVec3.Forward, intent, true, dt).Velocity;
            }

            Assert.True(velocity.Magnitude < MovementProfile.WalkSpeed);

            for (int i = 0; i < 60; i++)
            {
                velocity = model.Integrate(velocity, KordonVec3.Forward, intent, true, dt).Velocity;
            }

            Assert.Equal(MovementProfile.WalkSpeed, velocity.Magnitude, 2);
        }

        [Fact]
        public void StanceAimingAndDirection_ChangeTheSpeedCap()
        {
            MovementProfile profile = new MovementProfile();
            Assert.Equal(MovementProfile.WalkSpeed, profile.MaxSpeedFor(Stance.Stand, false, false), 3);
            Assert.Equal(MovementProfile.CrouchSpeed, profile.MaxSpeedFor(Stance.Crouch, false, false), 3);
            Assert.Equal(MovementProfile.ProneSpeed, profile.MaxSpeedFor(Stance.Prone, false, false), 3);
            Assert.Equal(MovementProfile.SprintSpeed, profile.MaxSpeedFor(Stance.Stand, false, true), 3);
            Assert.Equal(MovementProfile.AimingSpeed, profile.MaxSpeedFor(Stance.Stand, true, false), 3);
            Assert.Equal(MovementProfile.AimingSpeed, profile.MaxSpeedFor(Stance.Stand, true, true), 3);

            Assert.True(profile.MaxSpeedFor(Stance.Stand, false, false, gearMassKg: 24f) <
                        profile.MaxSpeedFor(Stance.Stand, false, false, gearMassKg: 10f));
            Assert.True(profile.MaxSpeedFor(Stance.Stand, false, false, armorMobility: 0.9f) <
                        profile.MaxSpeedFor(Stance.Stand, false, false));
            Assert.Equal(MovementProfile.StandEyeHeight, profile.EyeHeightFor(Stance.Stand), 3);
            Assert.Equal(MovementProfile.CrouchEyeHeight, profile.EyeHeightFor(Stance.Crouch), 3);
            Assert.Equal(MovementProfile.ProneEyeHeight, profile.EyeHeightFor(Stance.Prone), 3);
            Assert.Equal(MovementProfile.StandEyeHeight, profile.EyeHeightFor(Stance.Crouch, 0f), 3);
        }

        [Fact]
        public void Sprinting_RequiresIntentStanceStaminaAndNoAiming()
        {
            MovementModel model = new MovementModel();
            float dt = 1f / 60f;

            MovementStep sprint = model.Integrate(KordonVec3.Zero, KordonVec3.Forward,
                new MovementIntent { Forward = 1f, WantsSprint = true, Stance = Stance.Stand }, true, dt);
            Assert.True(sprint.Sprinting);
            Assert.Equal(Gait.Sprint, sprint.Gait);

            MovementStep aiming = model.Integrate(KordonVec3.Zero, KordonVec3.Forward,
                new MovementIntent { Forward = 1f, WantsSprint = true, IsAiming = true, Stance = Stance.Stand },
                true, dt);
            Assert.False(aiming.Sprinting);

            MovementStep crouched = model.Integrate(KordonVec3.Zero, KordonVec3.Forward,
                new MovementIntent { Forward = 1f, WantsSprint = true, Stance = Stance.Crouch }, true, dt);
            Assert.False(crouched.Sprinting);
            Assert.Equal(Gait.CrouchWalk, crouched.Gait);

            MovementStep exhausted = model.Integrate(KordonVec3.Zero, KordonVec3.Forward,
                new MovementIntent { Forward = 1f, WantsSprint = true, Stance = Stance.Stand }, true, dt,
                canSprint: false);
            Assert.False(exhausted.Sprinting);

            MovementStep airborne = model.Integrate(KordonVec3.Zero, KordonVec3.Forward,
                new MovementIntent { Forward = 1f, WantsSprint = true, Stance = Stance.Stand }, false, dt);
            Assert.False(airborne.Sprinting);
        }

        [Fact]
        public void StoppingIsFasterThanStarting_ButNeverInstant()
        {
            MovementModel model = new MovementModel();
            float dt = 1f / 60f;
            MovementIntent intent = new MovementIntent { Forward = 1f, Stance = Stance.Stand };
            KordonVec3 velocity = KordonVec3.Forward * MovementProfile.WalkSpeed;

            MovementStep first = model.Integrate(velocity, KordonVec3.Zero, new MovementIntent(), true, dt);
            Assert.True(first.Velocity.Magnitude < MovementProfile.WalkSpeed);
            Assert.True(first.Velocity.Magnitude > 0f);
            Assert.Equal(MovementProfile.WalkSpeed - MovementProfile.GroundDeceleration * dt,
                first.Velocity.Magnitude, 3);
            Assert.Equal(Gait.Walk, first.Gait);

            int steps = 0;
            while (first.Velocity.Magnitude > 0.05f && steps < 120)
            {
                first = model.Integrate(first.Velocity, KordonVec3.Zero, new MovementIntent(), true, dt);
                steps++;
            }

            Assert.True(steps < 20, $"took {steps} steps to stop");
            Assert.Equal(Gait.Idle, MovementModel.ClassifyGait(Stance.Stand, false, first.Velocity));
        }

        [Fact]
        public void Jumping_GivesWeakAirControlAndRuinsWeaponControl()
        {
            MovementModel model = new MovementModel();
            float dt = 1f / 60f;
            MovementIntent intent = new MovementIntent { Forward = 1f, WantsJump = true, Stance = Stance.Stand };

            MovementStep jump = model.Integrate(KordonVec3.Zero, KordonVec3.Forward, intent, true, dt);
            Assert.True(jump.Jumped);
            Assert.Equal(MovementProfile.JumpUpVelocity, jump.Velocity.Y, 3);
            // Still on the ground this step: control is only ruined once the feet leave it.
            Assert.Equal(1f, jump.WeaponControlPenalty, 3);

            MovementStep air = model.Integrate(jump.Velocity, KordonVec3.Forward,
                new MovementIntent { Forward = 1f, Stance = Stance.Stand }, false, dt);
            Assert.False(air.Jumped);
            Assert.Equal(jump.Velocity.Y - MovementProfile.Gravity * dt, air.Velocity.Y, 3);
            Assert.Equal(MovementProfile.AirborneWeaponControlPenalty, air.WeaponControlPenalty, 3);

            MovementStep grounded = model.Integrate(KordonVec3.Zero, KordonVec3.Forward,
                new MovementIntent { Forward = 1f, Stance = Stance.Stand }, true, dt);
            Assert.True(grounded.WeaponControlPenalty < air.WeaponControlPenalty);

            // Air control is a fraction of ground acceleration.
            float airHorizontal = new KordonVec3(air.Velocity.X, 0f, air.Velocity.Z).Magnitude;
            float jumpHorizontal = new KordonVec3(jump.Velocity.X, 0f, jump.Velocity.Z).Magnitude;
            float groundGain = grounded.Velocity.Magnitude;
            Assert.True(airHorizontal > jumpHorizontal);
            Assert.True(airHorizontal - jumpHorizontal < groundGain);
            Assert.Equal(MovementProfile.GroundAcceleration * MovementProfile.AirControlMultiplier * dt,
                airHorizontal - jumpHorizontal, 2);

            MovementStep crouchJump = model.Integrate(KordonVec3.Zero, KordonVec3.Forward,
                new MovementIntent { Forward = 1f, WantsJump = true, Stance = Stance.Crouch }, true, dt);
            Assert.Equal(MovementProfile.JumpUpVelocity * MovementProfile.CrouchJumpVelocityMultiplier,
                crouchJump.Velocity.Y, 3);
        }

        [Fact]
        public void FallDamage_StartsAfterASafeDropAndKillsFromExtremeHeight()
        {
            Assert.Equal(0f, MovementModel.FallDamage(1f), 3);
            Assert.Equal(0f, MovementModel.FallDamage(3.2f), 3);
            Assert.True(MovementModel.FallDamage(6f) > 0f);
            Assert.True(MovementModel.FallDamage(10f) > MovementModel.FallDamage(6f));
            Assert.Equal(120f, MovementModel.FallDamage(17f), 3);
            Assert.Equal(120f, MovementModel.FallDamage(60f), 3);
        }

        [Fact]
        public void Stamina_IsShortDrainsFasterUnderLoadAndRegensAfterADelay()
        {
            StaminaModel stamina = new StaminaModel();
            Assert.Equal(StaminaModel.Max, stamina.Value, 3);
            Assert.True(stamina.CanSprint);
            Assert.Equal(100f / 24f, stamina.SprintSecondsFromFull, 3);

            stamina.Tick(1f, true, false, 10f);
            Assert.Equal(76f, stamina.Value, 3);

            StaminaModel aiming = new StaminaModel();
            aiming.Tick(1f, true, true, 10f);
            Assert.True(aiming.Value < stamina.Value);

            StaminaModel loaded = new StaminaModel();
            loaded.Tick(1f, true, false, 24f);
            Assert.True(loaded.Value < stamina.Value);

            for (int i = 0; i < 300 && stamina.CanSprint; i++)
            {
                stamina.Tick(0.05f, true, false, 10f);
            }

            Assert.False(stamina.CanSprint);
            Assert.True(stamina.IsExhausted);
            Assert.True(stamina.Value <= stamina.ExhaustedBelow);
            Assert.True(stamina.BreathIntensity > 0.9f);

            // No regeneration during the delay, then 15 per second.
            float exhausted = stamina.Value;
            stamina.Tick(1f, false, false, 10f);
            Assert.Equal(exhausted, stamina.Value, 3);
            stamina.Tick(1f, false, false, 10f);
            Assert.Equal(exhausted + StaminaModel.RegenPerSecond, stamina.Value, 3);

            for (int i = 0; i < 200 && stamina.IsExhausted; i++)
            {
                stamina.Tick(0.2f, false, false, 10f);
            }

            Assert.False(stamina.IsExhausted);
            Assert.True(stamina.Value >= stamina.RecoveredAbove);

            for (int i = 0; i < 400 && stamina.Value < StaminaModel.Max; i++)
            {
                stamina.Tick(0.2f, false, false, 10f);
            }

            Assert.Equal(StaminaModel.Max, stamina.Value, 3);

            stamina.Reset();
            Assert.Equal(StaminaModel.Max, stamina.Value, 3);
            Assert.Equal(0f, stamina.BreathIntensity, 3);
        }

        [Fact]
        public void NothingIsSilent_EveryGaitAndSurfaceMakesNoise()
        {
            Assert.Equal(NoiseModel.SprintRadius, NoiseModel.GaitRadius(Gait.Sprint), 3);
            Assert.Equal(NoiseModel.WalkRadius, NoiseModel.GaitRadius(Gait.Walk), 3);
            Assert.Equal(NoiseModel.CrouchWalkRadius, NoiseModel.GaitRadius(Gait.CrouchWalk), 3);
            Assert.Equal(NoiseModel.ProneCrawlRadius, NoiseModel.GaitRadius(Gait.ProneCrawl), 3);
            Assert.True(NoiseModel.GaitRadius(Gait.Sprint) > NoiseModel.GaitRadius(Gait.Walk));
            Assert.True(NoiseModel.GaitRadius(Gait.Walk) > NoiseModel.GaitRadius(Gait.CrouchWalk));
            Assert.True(NoiseModel.GaitRadius(Gait.CrouchWalk) > NoiseModel.GaitRadius(Gait.ProneCrawl));

            string[] surfaces = { "concrete", "metal", "grating", "gravel", "wood", "glass", "sand", "cloth", "rubber", "water" };
            Gait[] gaits = { Gait.Walk, Gait.Sprint, Gait.CrouchWalk, Gait.ProneCrawl };
            foreach (Gait gait in gaits)
            {
                foreach (string surface in surfaces)
                {
                    float radius = NoiseModel.FootstepRadius(gait, surface, 14f);
                    Assert.True(radius > 0f, $"{gait} on {surface} was silent");
                }
            }

            Assert.Equal(1.4f, NoiseModel.SurfaceMultiplier("metal"), 3);
            Assert.Equal(1.5f, NoiseModel.SurfaceMultiplier("grating"), 3);
            Assert.Equal(0.7f, NoiseModel.SurfaceMultiplier("sand"), 3);
            Assert.Equal(0.5f, NoiseModel.SurfaceMultiplier("cloth"), 3);
            Assert.True(NoiseModel.FootstepRadius(Gait.Walk, "grating", 14f) >
                        NoiseModel.FootstepRadius(Gait.Walk, "sand", 14f));
        }

        [Fact]
        public void HeavyGearAndArmour_MakeTheOperatorLouder()
        {
            float light = NoiseModel.FootstepRadius(Gait.Walk, "concrete", 8f, 1f);
            float standard = NoiseModel.FootstepRadius(Gait.Walk, "concrete", 14f, 1.1f);
            float assault = NoiseModel.FootstepRadius(Gait.Walk, "concrete", 26f, 1.25f);
            Assert.True(light < standard);
            Assert.True(standard < assault);
            Assert.Equal(9.5f, NoiseModel.FootstepRadius(Gait.Walk, "concrete", 14f, 1f), 3);
            Assert.Equal(9.5f * (1f + 12f * 0.035f), NoiseModel.FootstepRadius(Gait.Walk, "concrete", 26f, 1f), 3);
            Assert.True(NoiseModel.WeaponHandlingRadius(20f) > NoiseModel.WeaponHandlingRadius(8f));
            Assert.True(NoiseModel.LandingRadius(3f) > 0f);
            Assert.Equal(0f, NoiseModel.LandingRadius(0.2f), 3);
        }

        [Fact]
        public void GunshotsHandlingAndReloads_AreAudibleAtTheirDocumentedRadii()
        {
            NoiseEvent shot = NoiseModel.MakeGunshot(new KordonVec3(0f, 0f, 0f), false);
            Assert.Equal(NoiseModel.GunshotRadius, shot.RadiusM, 3);
            Assert.Equal(NoiseKind.Gunshot, shot.Kind);
            Assert.False(shot.IsSilent);

            NoiseEvent suppressed = NoiseModel.MakeGunshot(KordonVec3.Zero, true);
            Assert.Equal(NoiseModel.SuppressedGunshotRadius, suppressed.RadiusM, 3);
            Assert.True(suppressed.RadiusM < shot.RadiusM);

            NoiseEvent footstep = NoiseModel.MakeFootstep(Gait.Sprint, "metal", KordonVec3.Zero, 14f, 1f);
            Assert.Equal(NoiseKind.Footstep, footstep.Kind);
            Assert.Equal("metal", footstep.SurfaceId);
            Assert.Equal(NoiseModel.FootstepRadius(Gait.Sprint, "metal", 14f, 1f), footstep.RadiusM, 3);

            NoiseEvent impact = NoiseModel.MakeImpact(new KordonVec3(5f, 0f, 0f), "concrete");
            Assert.Equal(NoiseKind.BulletImpact, impact.Kind);
            Assert.Equal(NoiseModel.ImpactRadius, impact.RadiusM, 3);

            Assert.Equal(1f, footstep.LoudnessAt(KordonVec3.Zero), 3);
            Assert.True(footstep.LoudnessAt(new KordonVec3(0f, 0f, footstep.RadiusM * 0.5f)) < 1f);
            Assert.Equal(0f, footstep.LoudnessAt(new KordonVec3(0f, 0f, footstep.RadiusM + 1f)), 3);
            Assert.True(NoiseEventEqualsSilent(new NoiseEvent { RadiusM = 0f }));
        }

        private static bool NoiseEventEqualsSilent(NoiseEvent noise) => noise.IsSilent;

        [Fact]
        public void FootstepCadence_FollowsSpeedAndGait()
        {
            Assert.True(NoiseModel.FootstepInterval(Gait.Sprint, 4.35f) < NoiseModel.FootstepInterval(Gait.Walk, 2.55f));
            Assert.True(NoiseModel.FootstepInterval(Gait.Walk, 2.55f) > 0f);
            Assert.Equal(float.MaxValue, NoiseModel.FootstepInterval(Gait.Idle, 0f));
        }

        [Fact]
        public void Breathing_FollowsStaminaSuppressionWoundsAndStance()
        {
            BreathModel breath = new BreathModel();
            Assert.Equal(0f, breath.Intensity(Stance.Stand, 1f, 0f, false, 1f), 3);
            Assert.Equal(0.6f, breath.Intensity(Stance.Stand, 0f, 0f, false, 1f), 3);
            Assert.Equal(1f, breath.Intensity(Stance.Stand, 0f, 1f, true, 0.4f), 3);
            Assert.True(breath.Intensity(Stance.Prone, 0f, 1f, true, 0.4f) <
                        breath.Intensity(Stance.Stand, 0f, 1f, true, 0.4f));
            Assert.True(breath.Intensity(Stance.Crouch, 0f, 0f, false, 1f) <
                        breath.Intensity(Stance.Stand, 0f, 0f, false, 1f));

            Assert.Equal(breath.RestBreathRatePerMinute, breath.BreathRatePerMinute(0f), 3);
            Assert.Equal(breath.MaxBreathRatePerMinute, breath.BreathRatePerMinute(1f), 3);
            Assert.False(breath.ShouldPlayBreathing(0.2f));
            Assert.True(breath.ShouldPlayBreathing(0.9f));

            float calmSway = breath.SwayDegrees(Stance.Stand, 0f, true);
            float exhaustedSway = breath.SwayDegrees(Stance.Stand, 1f, true);
            Assert.True(exhaustedSway > calmSway);
            Assert.True(breath.SwayDegrees(Stance.Prone, 0f, true) < calmSway);
            Assert.True(breath.SwayDegrees(Stance.Stand, 0f, false) > calmSway);

            KordonVec3 sway = breath.SampleSway(1.5f, 0.5f, exhaustedSway);
            Assert.True(System.Math.Abs(sway.X) <= exhaustedSway + 1e-3f);
            Assert.True(System.Math.Abs(sway.Y) <= exhaustedSway + 1e-3f);
            Assert.Equal(sway, breath.SampleSway(1.5f, 0.5f, exhaustedSway));
        }

        [Fact]
        public void StaminaDrivesBreathing_WhichDrivesAimStability()
        {
            StaminaModel stamina = new StaminaModel();
            BreathModel breath = new BreathModel();
            Assert.Equal(0f, breath.Intensity(Stance.Stand, stamina.Normalized, 0f, false, 1f), 3);

            for (int i = 0; i < 80; i++)
            {
                stamina.Tick(0.05f, true, false, 16f);
            }

            float intensity = breath.Intensity(Stance.Stand, stamina.Normalized, 0f, false, 1f);
            Assert.True(intensity > 0.4f);
            Assert.True(breath.SwayDegrees(Stance.Stand, intensity, true) >
                        breath.SwayDegrees(Stance.Stand, 0f, true));
        }
    }
}
