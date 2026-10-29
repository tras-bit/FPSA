using System.Collections.Generic;
using Kordon.Core.Ammo;
using Kordon.Core.Combat;
using Kordon.Core.Data;
using Kordon.Core.Rules;
using Kordon.Core.Loadout;
using Kordon.Core.Utility;
using Xunit;

namespace Kordon.Core.Tests
{
    /// <summary>
    /// Thrown equipment: deterministic flight physics and the blast rules.  These are
    /// the same rules the server runs, so what the tests assert here is what the
    /// authoritative match enforces.
    /// </summary>
    public class GrenadeCombatTests
    {
        private static readonly DefinitionLibrary Library = TestData.Database.Definitions;
        private static readonly GrenadeDefinition Frag = Library.GetGrenade("frag_f1");
        private static readonly GrenadeDefinition Smoke = Library.GetGrenade("smoke_rdg");
        private static readonly GrenadeDefinition Flash = Library.GetGrenade("flash_zarya");

        private static KordonVec3 Right => KordonVec3.Right;

        [Fact]
        public void Fragmentation_FallsOffWithDistance_AndStopsAtTheDamageRadius()
        {
            BlastModel model = new BlastModel();

            BlastResult close = model.Resolve(BlastRequest.Exposed(Frag, 0.5f));
            BlastResult lethalEdge = model.Resolve(BlastRequest.Exposed(Frag, Frag.LethalRadiusM));
            BlastResult mid = model.Resolve(BlastRequest.Exposed(Frag, Frag.DamageRadiusM * 0.67f));
            BlastResult edge = model.Resolve(BlastRequest.Exposed(Frag, Frag.DamageRadiusM * 0.95f));
            BlastResult outside = model.Resolve(BlastRequest.Exposed(Frag, Frag.DamageRadiusM + 0.5f));

            // Inside the lethal radius the fragmentation pattern is saturated.
            Assert.Equal(Frag.DamageAtCenter, close.Damage, 3);
            Assert.Equal(Frag.DamageAtCenter, lethalEdge.Damage, 3);

            // Past it damage falls away quadratically and stops at the damage radius.
            Assert.True(mid.Damage < lethalEdge.Damage);
            Assert.True(edge.Damage < mid.Damage);
            Assert.True(edge.Damage > 0f);
            Assert.Equal(0f, outside.Damage);
            Assert.True(close.Accepted);

            float previous = float.MaxValue;
            for (float d = Frag.LethalRadiusM; d <= Frag.DamageRadiusM; d += 0.5f)
            {
                float damage = model.Resolve(BlastRequest.Exposed(Frag, d)).Damage;
                Assert.True(damage <= previous + 0.001f, "blast damage must never rise with distance");
                previous = damage;
            }
        }

        [Fact]
        public void Fragmentation_ThroughACoveredLine_HurtsNobody_ButStillSuppresses()
        {
            BlastModel model = new BlastModel();
            BlastResult result = model.Resolve(new BlastRequest
            {
                Grenade = Frag,
                DistanceM = 2f,
                HasLineOfSight = false,
                CoverFactor = 1f,
            });

            Assert.True(result.Accepted);
            Assert.True(result.DamageBlocked);
            Assert.Equal(0f, result.Damage);
            Assert.False(result.Fatal);
            Assert.True(result.Suppression > 0f);
            Assert.True(result.Suppression < model.Resolve(BlastRequest.Exposed(Frag, 2f)).Suppression);

            // A blocked blast is not a damage event at all: vitals must ignore it.
            Assert.False(result.ToDamageResult().Accepted);
        }

        [Fact]
        public void Fragmentation_PartialCover_ReducesDamageButNeverToZero()
        {
            BlastModel model = new BlastModel();
            float open = model.Resolve(BlastRequest.Exposed(Frag, 5f)).Damage;
            float half = model.Resolve(new BlastRequest
            {
                Grenade = Frag,
                DistanceM = 5f,
                HasLineOfSight = true,
                CoverFactor = 0.5f,
            }).Damage;

            Assert.True(half < open);
            Assert.True(half > 0f);
        }

        [Fact]
        public void Fragmentation_ArmourSoftensFragments_WithoutBecomingImmunity()
        {
            BlastModel model = new BlastModel();
            float bare = model.Resolve(BlastRequest.Exposed(Frag, 3f)).Damage;
            float plated = model.Resolve(new BlastRequest
            {
                Grenade = Frag,
                DistanceM = 3f,
                HasLineOfSight = true,
                ArmorClass = 6f,
            }).Damage;

            Assert.True(plated < bare);
            Assert.True(plated >= bare * (1f - BlastModel.MaxArmorReduction) - 0.001f);
            Assert.True(plated > 0f);
        }

        [Fact]
        public void CentreHit_IsLethal_AndKillsAHealthyOperator()
        {
            BlastModel model = new BlastModel();
            BlastResult direct = model.Resolve(BlastRequest.Exposed(Frag, 0.5f));
            Assert.True(direct.Fatal);

            PlayerVitals vitals = new PlayerVitals();
            Assert.True(vitals.ApplyDamage(direct.ToDamageResult(), DeathCause.Explosion));
            Assert.True(vitals.IsDead);
            Assert.Equal(DeathCause.Explosion, vitals.LastCause);
            Assert.Equal(0f, vitals.Health);
        }

        [Fact]
        public void FarEdgeOfTheBlast_IsSurvivable()
        {
            BlastModel model = new BlastModel();
            BlastResult edge = model.Resolve(BlastRequest.Exposed(Frag, Frag.DamageRadiusM * 0.9f));
            Assert.False(edge.Fatal);

            PlayerVitals vitals = new PlayerVitals();
            vitals.ApplyDamage(edge.ToDamageResult(), DeathCause.Explosion);
            Assert.False(vitals.IsDead);
            Assert.True(vitals.Health > 0f);
        }

        [Fact]
        public void Flashbang_BlindsOnlyWhatItCanSee_AndScalesWithDistance()
        {
            BlastModel model = new BlastModel();

            BlastResult near = model.Resolve(BlastRequest.Exposed(Flash, 1f));
            BlastResult far = model.Resolve(BlastRequest.Exposed(Flash, Flash.EffectRadiusM * 0.9f));
            BlastResult wall = model.Resolve(new BlastRequest
            {
                Grenade = Flash,
                DistanceM = 1f,
                HasLineOfSight = false,
                CoverFactor = 1f,
            });
            BlastResult outside = model.Resolve(BlastRequest.Exposed(Flash, Flash.EffectRadiusM + 1f));

            Assert.True(near.Blinded);
            Assert.True(far.Blinded);
            Assert.True(near.BlindSeconds > far.BlindSeconds);
            Assert.True(far.BlindSeconds >= 0.35f);
            Assert.False(wall.Blinded);
            Assert.False(outside.Blinded);
            Assert.Equal(0f, near.Damage);
        }

        [Fact]
        public void Smoke_BlocksVisionForItsDuration_AndDealsNoDamage()
        {
            BlastModel model = new BlastModel();
            BlastResult result = model.Resolve(BlastRequest.Exposed(Smoke, 2f));

            Assert.True(result.BlocksVision);
            Assert.Equal(0f, result.Damage);
            Assert.Equal(Smoke.EffectRadiusM, result.EffectRadiusM, 3);
            Assert.Equal(Smoke.EffectDurationSeconds, result.EffectSeconds, 3);
            Assert.False(result.Blinded);
        }

        [Fact]
        public void BlastWithoutADefinition_IsRejectedInsteadOfDefaulting()
        {
            BlastModel model = new BlastModel();
            BlastResult none = model.Resolve(new BlastRequest { Grenade = null, DistanceM = 1f });
            Assert.False(none.Accepted);
            Assert.False(none.ToDamageResult().Accepted);
        }

        [Fact]
        public void BlastResolution_IsDeterministic()
        {
            BlastModel model = new BlastModel();
            BlastRequest request = new BlastRequest
            {
                Grenade = Frag,
                DistanceM = 4.25f,
                HasLineOfSight = true,
                CoverFactor = 0.25f,
                ArmorClass = 3f,
            };

            BlastResult first = model.Resolve(request);
            BlastResult second = model.Resolve(request);
            Assert.Equal(first.Damage, second.Damage);
            Assert.Equal(first.Suppression, second.Suppression);
            Assert.Equal(first.Fatal, second.Fatal);
        }

        [Fact]
        public void CookingBurnsTheFuze_ButNeverBelowTheSafeMinimum()
        {
            Assert.Equal(GrenadeTrajectory.CookedFuse(Frag, 0f), Frag.FuseSeconds, 3);
            Assert.Equal(GrenadeTrajectory.CookedFuse(Frag, 1f), Frag.FuseSeconds - 1f, 3);

            // Cooking all the way down clamps at the grenade's own cookable minimum.
            Assert.Equal(Frag.CookableMinFuse, GrenadeTrajectory.CookedFuse(Frag, 99f), 3);

            // Smoke is not cookable at all: the full fuze is always used.
            Assert.Equal(Smoke.FuseSeconds, GrenadeTrajectory.CookedFuse(Smoke, 5f), 3);
        }

        [Fact]
        public void ThrowVelocity_IsBoundedAndAlwaysHasAnUpwardBias()
        {
            KordonVec3 flat = GrenadeTrajectory.LaunchVelocity(14f, Right);
            Assert.Equal(14f, flat.Magnitude, 2);
            Assert.True(flat.Y > 0f);

            // A cheat cannot ask for a faster throw than the model allows.
            KordonVec3 absurd = GrenadeTrajectory.LaunchVelocity(400f, Right);
            Assert.Equal(GrenadeTrajectory.MaxThrowSpeedMS, absurd.Magnitude, 2);
        }

        [Fact]
        public void Flight_FallsUnderGravity_AndKeepsTheHorizontalArc()
        {
            GrenadeFlightState state = GrenadeTrajectory.Launch(Frag, KordonVec3.Zero, Right, 0f);
            float startY = state.Position.Y;
            float startSpeed = state.Speed;

            for (int i = 0; i < 12; i++)
            {
                GrenadeTrajectory.Integrate(ref state, GrenadeTrajectory.FixedStep);
            }

            Assert.True(state.Position.X > 0f);
            Assert.True(state.Position.Y > startY);
            Assert.True(state.Velocity.Y < GrenadeTrajectory.LaunchVelocity(Frag.ThrowSpeed, Right).Y);
            Assert.True(state.FuseRemaining < Frag.FuseSeconds);
            Assert.True(state.Age > 0f);
            Assert.True(state.Speed > 0f && state.Speed <= startSpeed + 0.01f);

            for (int i = 0; i < 120; i++)
            {
                GrenadeTrajectory.Integrate(ref state, GrenadeTrajectory.FixedStep);
            }

            Assert.True(state.Position.Y < startY, "a thrown grenade must come down");
        }

        [Fact]
        public void Impact_BouncesWithLessEnergy_AndSettlesOnTheFloor()
        {
            GrenadeFlightState state = new GrenadeFlightState
            {
                Position = new KordonVec3(0f, 0.1f, 0f),
                Velocity = new KordonVec3(6f, -9f, 0f),
                FuseRemaining = 2f,
            };

            bool bounced = GrenadeTrajectory.ResolveImpact(ref state, KordonVec3.Up, out float impactSpeed);
            Assert.True(bounced);
            Assert.True(impactSpeed > 0f);
            Assert.True(state.Velocity.Y > 0f, "the grenade must come back up a little");
            Assert.True(state.Velocity.Y < impactSpeed, "bounce must lose energy");
            Assert.True(state.Velocity.X < 6f, "friction must slow the slide");
            Assert.False(state.Resting);

            // A slow, near-horizontal contact ends the flight instead of bouncing forever.
            state.Velocity = new KordonVec3(0.2f, -0.3f, 0f);
            Assert.False(GrenadeTrajectory.ResolveImpact(ref state, KordonVec3.Up, out _));
            Assert.True(state.Resting);
            Assert.Equal(0f, state.Speed, 3);
        }

        [Fact]
        public void FuzeKeepsBurningWhileResting_AndDetonates()
        {
            GrenadeFlightState state = new GrenadeFlightState
            {
                Position = KordonVec3.Zero,
                FuseRemaining = 0.5f,
                Resting = true,
            };

            KordonVec3 detonation;
            Assert.False(GrenadeTrajectory.TryDetonate(ref state, 0.25f, out detonation));
            Assert.True(state.FuseRemaining > 0f);

            Assert.True(GrenadeTrajectory.TryDetonate(ref state, 0.5f, out detonation));
            Assert.True(state.FuseExpired);
            Assert.Equal(0f, state.FuseRemaining, 3);
        }

        [Fact]
        public void FlightPlausibility_RejectsImpossibleStates()
        {
            GrenadeFlightState good = GrenadeTrajectory.Launch(Frag, KordonVec3.Zero, Right, 0f);
            Assert.True(GrenadeTrajectory.IsPlausible(good));

            GrenadeFlightState rocket = good;
            rocket.Velocity = Right * 90f;
            Assert.False(GrenadeTrajectory.IsPlausible(rocket));

            GrenadeFlightState nan = good;
            nan.Position = new KordonVec3(float.NaN, 1f, 0f);
            Assert.False(GrenadeTrajectory.IsPlausible(nan));

            GrenadeFlightState underground = good;
            underground.Position = new KordonVec3(0f, -500f, 0f);
            Assert.False(GrenadeTrajectory.IsPlausible(underground));
        }

        [Fact]
        public void AverageArmorClass_ReadsTheTorsoPlatesOfAKit()
        {
            ArmorState light = new ArmorState(ArmorKitDefinition.Light());
            ArmorState heavy = new ArmorState(ArmorKitDefinition.Heavy());

            Assert.True(BlastModel.AverageArmorClass(heavy) > BlastModel.AverageArmorClass(light));
            Assert.True(BlastModel.AverageArmorClass(light) > 0f);
            Assert.Equal(0f, BlastModel.AverageArmorClass(null));

            BlastModel model = new BlastModel();
            float bare = model.Resolve(BlastRequest.Exposed(Frag, 3f)).Damage;
            float heavyPlated = model.Resolve(new BlastRequest
            {
                Grenade = Frag,
                DistanceM = 3f,
                HasLineOfSight = true,
                ArmorClass = BlastModel.AverageArmorClass(heavy),
            }).Damage;
            Assert.True(heavyPlated < bare);
        }
        [Fact]
        public void ThrowingConsumesTheKit_AndRespawnReissuesExactlyTheChosenCount()
        {
            LoadoutSelection selection = TestLoadouts.ValidRifleLoadout();
            LoadoutKit kit = new LoadoutKit(selection, Library, null, new DeterministicRandom(7));
            kit.Seal();

            Assert.Equal(2, kit.GrenadesRemaining);
            Assert.True(kit.TrySwitchSlot(ActiveSlot.Grenade, 10f));
            Assert.True(kit.TryThrowGrenade(10.5f));
            Assert.Equal(1, kit.GrenadesRemaining);

            // The kit still has a grenade, so the slot stays selected.
            Assert.Equal(ActiveSlot.Grenade, kit.Active);
            Assert.True(kit.TryThrowGrenade(10.6f));
            Assert.Equal(0, kit.GrenadesRemaining);

            // Empty hands fall back to a weapon: no invisible grenade slot.
            Assert.NotEqual(ActiveSlot.Grenade, kit.Active);
            Assert.False(kit.TryThrowGrenade(10.7f));

            Assert.True(kit.OnRespawnResupply());
            Assert.Equal(2, kit.GrenadesRemaining);
        }

        [Fact]
        public void ResupplyRuleNeverWithinRound_RefusesToReissueGrenades()
        {
            LoadoutKitOptions options = new LoadoutKitOptions
            {
                Resupply = ResupplyRule.NeverWithinRound,
            };
            LoadoutKit kit = new LoadoutKit(TestLoadouts.ValidRifleLoadout(), Library, options,
                new DeterministicRandom(9));
            kit.Seal();
            kit.TrySwitchSlot(ActiveSlot.Grenade, 1f);
            kit.TryThrowGrenade(1.5f);

            Assert.False(kit.OnRespawnResupply());
            Assert.Equal(1, kit.GrenadesRemaining);
        }

        [Fact]
        public void OwnGrenadeDeath_IsUncreditedButStartsTheRespawnTimer()
        {
            MatchRulesConfig config = MatchRulesConfig.DefaultFreeForAll();

            // One operator is enough for this rule check (practice uses the same switch).
            config.MinPlayersToStartLive = 1;
            FfaMatchController match = new FfaMatchController(config, new DeterministicRandom(77));
            Assert.True(match.TryJoin(4242UL, "OP-SELF", false, 0f).IsAccepted);
            match.BeginLoadoutPhase(0f);
            match.SubmitLoadout(4242UL, TestLoadouts.ValidRifleLoadout(), Library, 0f);
            Assert.True(match.BeginCountdown(0f));
            Assert.True(match.BeginLive(0f));
            match.InitialSpawn(_ => 0, config.SpawnProtectionSeconds + 1f);
            match.Tick(config.SpawnProtectionSeconds + 1f);

            float deathAt = config.SpawnProtectionSeconds + 2f;
            Assert.True(match.RegisterUncreditedDeath(4242UL, DeathCause.Explosion, deathAt));

            PlayerMatchState state = match.GetPlayer(4242UL);
            Assert.NotNull(state);
            Assert.False(state.IsAlive);
            Assert.Equal(0, state.Kills);
            Assert.Equal(1, state.Deaths);

            Assert.False(match.IsRespawnReady(4242UL, deathAt + 0.1f));
            Assert.True(match.IsRespawnReady(4242UL, deathAt + config.RespawnDelaySeconds + 0.1f));
        }
    }
}
