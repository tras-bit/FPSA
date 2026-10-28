using System.Collections.Generic;
using Kordon.Core.Combat;
using Kordon.Core.Data;
using Kordon.Core.Utility;
using Xunit;

namespace Kordon.Core.Tests
{
    /// <summary>
    /// Hit zones, per zone armour, material and angle penetration, lethality of vital
    /// zones, damage falloff and suppression.  Every request is resolved with
    /// <c>random: null</c> unless the test is explicitly about a random roll, so the
    /// numbers below are the exact server side results.
    /// </summary>
    public class DamagePenetrationTests
    {
        private static readonly DefinitionLibrary Library = TestData.Database.Definitions;
        private static readonly DamageModel Model = new DamageModel();

        private static BulletDefinition Bullet(string id)
        {
            BulletDefinition bullet = Library.GetBullet(id);
            Assert.NotNull(bullet);
            return bullet;
        }

        private static MaterialDefinition Material(string id)
        {
            MaterialDefinition material = Library.GetMaterialById(id);
            Assert.NotNull(material);
            return material;
        }

        private static ArmorState Armor(string kitId) => new ArmorState(Library.GetArmorKit(kitId));

        private static DamageResult Hit(string bulletId, HitZone zone, float distanceM, ArmorState armor = null,
            MaterialDefinition cover = null, float coverThicknessMm = 0f, float angleDeg = 0f,
            DeterministicRandom random = null)
        {
            return Model.Resolve(new DamageRequest
            {
                Bullet = Bullet(bulletId),
                Zone = zone,
                Armor = armor,
                DistanceM = distanceM,
                AngleFromNormalDeg = angleDeg,
                InterposedMaterial = cover,
                InterposedThicknessMm = coverThicknessMm,
                Random = random
            });
        }

        [Fact]
        public void ArmorClassThresholdsAndBluntFactors_AreTheDocumentedLadder()
        {
            Assert.Equal(new[] { 0f, 8f, 16f, 25f, 35f, 48f, 62f }, PenetrationSolver.ArmorClassThresholds);
            Assert.Equal(new[] { 1f, 0.45f, 0.32f, 0.22f, 0.16f, 0.12f, 0.09f },
                PenetrationSolver.BluntDamageFactors);
            Assert.Equal(25f, PenetrationSolver.ArmorThreshold(3));
            Assert.Equal(0.22f, PenetrationSolver.BluntFactor(3));
        }

        [Fact]
        public void PenetratingHeadAndNeckHits_AreLethal()
        {
            DamageResult head = Hit("545x39_ps", HitZone.Head, 10f);
            Assert.True(head.Accepted);
            Assert.True(head.Fatal);
            Assert.Equal(132f, head.Damage, 1);

            DamageResult neck = Hit("9x19_pst", HitZone.Neck, 6f);
            Assert.True(neck.Fatal);
            Assert.Equal(26f * 3.4f, neck.Damage, 1);
        }

        [Fact]
        public void LimbsAndGear_AreNeverInstantlyLethal()
        {
            DamageResult leg = Hit("762x54r_lps", HitZone.LeftLeg, 20f);
            Assert.False(leg.Fatal);
            Assert.Equal(62f * 0.68f, leg.Damage, 1);
            Assert.True(leg.MobilityPenalty > 0f);

            DamageResult gear = Hit("338_lapua", HitZone.Gear, 20f);
            Assert.False(gear.Fatal);
            Assert.Equal(96f * 0.25f, gear.Damage, 1);
        }

        [Fact]
        public void ZoneMultipliers_FollowTheDocumentedTable()
        {
            Assert.Equal(4.0f, Model.RuleFor(HitZone.Head).DamageMultiplier, 3);
            Assert.Equal(3.4f, Model.RuleFor(HitZone.Neck).DamageMultiplier, 3);
            Assert.Equal(1.0f, Model.RuleFor(HitZone.Thorax).DamageMultiplier, 3);
            Assert.Equal(1.15f, Model.RuleFor(HitZone.Abdomen).DamageMultiplier, 3);
            Assert.Equal(1.05f, Model.RuleFor(HitZone.Pelvis).DamageMultiplier, 3);
            Assert.Equal(0.72f, Model.RuleFor(HitZone.LeftArm).DamageMultiplier, 3);
            Assert.Equal(0.68f, Model.RuleFor(HitZone.RightLeg).DamageMultiplier, 3);
            Assert.Equal(0.25f, Model.RuleFor(HitZone.Gear).DamageMultiplier, 3);
        }

        [Fact]
        public void ClassThreePlate_StopsAPistolRoundAndOnlyBruises()
        {
            DamageResult result = Hit("9x19_pst", HitZone.Thorax, 5f, Armor("standard"));
            Assert.True(result.Accepted);
            Assert.True(result.BlockedByArmor);
            Assert.False(result.PenetratedArmor);
            Assert.False(result.Fatal);
            Assert.Equal(26f * 0.22f, result.Damage, 1);
            Assert.Equal(6f + 11f * 0.9f, result.ArmorDurabilityDamage, 1);
        }

        [Fact]
        public void Helmet_SavesTheOperatorFromAPistolHeadShot()
        {
            DamageResult unarmored = Hit("9x19_pst", HitZone.Head, 5f);
            Assert.True(unarmored.Fatal);

            DamageResult helmeted = Hit("9x19_pst", HitZone.Head, 5f, Armor("heavy"));
            Assert.False(helmeted.Fatal);
            Assert.True(helmeted.BlockedByArmor);
            Assert.Equal(26f * 4.0f * 0.22f, helmeted.Damage, 1);
        }

        [Fact]
        public void RifleRound_DefeatsClassThreeButLosesEnergy()
        {
            DamageResult result = Hit("762x54r_lps", HitZone.Thorax, 50f, Armor("standard"));
            Assert.True(result.PenetratedArmor);
            Assert.False(result.BlockedByArmor);
            Assert.False(result.Fatal);
            Assert.Equal(4f + (43f - 25f) * 0.6f, result.ArmorDurabilityDamage, 1);
            Assert.Equal(62f * (1f - 0.35f * (25f / 43f)), result.Damage, 1);
            Assert.True(result.Damage > 26f * 0.22f);
        }

        [Fact]
        public void AngleOfIncidence_DecidesWhetherARoundGetsThrough()
        {
            // 7.62x39 BP has 45 penetration: enough for class three (25) head on, not at 60 degrees.
            DamageResult square = Hit("762x39_bp", HitZone.Thorax, 20f, Armor("standard"), angleDeg: 0f);
            Assert.True(square.PenetratedArmor);

            DamageResult angled = Hit("762x39_bp", HitZone.Thorax, 20f, Armor("standard"), angleDeg: 60f);
            Assert.True(angled.BlockedByArmor);
            Assert.True(angled.Damage < square.Damage);
            Assert.Equal(45f * (float)System.Math.Pow(0.5, PenetrationSolver.AngleExponent),
                angled.Penetration.PenetrationMargin + PenetrationSolver.ArmorThreshold(3), 1);
        }

        [Fact]
        public void CoverResistance_ScalesWithMaterialAndThickness()
        {
            MaterialDefinition wood = Material("wood");
            Assert.Equal(20f * 1.2f, PenetrationSolver.CoverResistance(wood, 25f), 2);
            Assert.Equal(20f * 1.2f * 2f, PenetrationSolver.CoverResistance(wood, 50f), 2);
            Assert.Equal(20f * 4.5f, PenetrationSolver.CoverResistance(Material("concrete"), 200f), 2);
        }

        [Fact]
        public void WoodenCrate_DegradesRifleRoundsAndStopsPistolRounds()
        {
            MaterialDefinition wood = Material("wood");

            DamageResult rifle = Hit("545x39_ps", HitZone.Thorax, 10f, cover: wood, coverThicknessMm: 25f);
            Assert.True(rifle.Penetration.Penetrated);
            Assert.Equal(33f * 0.8f * (1f - 0.45f * (24f / 28f)), rifle.Damage, 1);
            Assert.True(rifle.Damage < 33f);

            DamageResult pistol = Hit("9x19_pst", HitZone.Thorax, 10f, cover: wood, coverThicknessMm: 25f);
            Assert.False(pistol.Penetration.Penetrated);
            Assert.Equal(0f, pistol.Damage, 3);
        }

        [Fact]
        public void ConcreteWall_StopsEverythingAtFullThicknessButNotAtHalf()
        {
            MaterialDefinition concrete = Material("concrete");

            DamageResult fullWall = Hit("338_lapua", HitZone.Thorax, 20f, cover: concrete, coverThicknessMm: 200f);
            Assert.False(fullWall.Penetration.Penetrated);
            Assert.Equal(0f, fullWall.Damage, 3);

            DamageResult halfWall = Hit("338_lapua", HitZone.Thorax, 20f, cover: concrete, coverThicknessMm: 100f);
            Assert.True(halfWall.Penetration.Penetrated);
            Assert.True(halfWall.Damage > 0f);
            Assert.True(halfWall.Damage < 96f);
        }

        [Fact]
        public void DamageFallsOffWithDistance_AndNeverBelowTheFloor()
        {
            BulletDefinition bullet = Bullet("545x39_ps");
            Assert.Equal(1f, bullet.DamageMultiplierAtDistance(10f), 3);
            Assert.Equal(1f, bullet.DamageMultiplierAtDistance(35f), 3);
            Assert.Equal(0.9f, bullet.DamageMultiplierAtDistance(70f), 3);
            Assert.Equal(0.45f, bullet.DamageMultiplierAtDistance(210f), 3);
            Assert.Equal(0.45f, bullet.DamageMultiplierAtDistance(500f), 3);

            float previous = float.MaxValue;
            for (float distance = 5f; distance <= 400f; distance += 5f)
            {
                float multiplier = bullet.DamageMultiplierAtDistance(distance);
                Assert.True(multiplier <= previous + 1e-4f, $"falloff increased at {distance} m");
                Assert.InRange(multiplier, 0.45f, 1f);
                previous = multiplier;
            }

            Assert.Equal(33f, Hit("545x39_ps", HitZone.Thorax, 10f).Damage, 1);
            Assert.Equal(33f * 0.45f, Hit("545x39_ps", HitZone.Thorax, 300f).Damage, 1);
        }

        [Fact]
        public void ShotgunLosesItsBiteFast()
        {
            float close = Hit("12ga_buckshot", HitZone.Thorax, 5f).Damage;
            float far = Hit("12ga_buckshot", HitZone.Thorax, 40f).Damage;
            Assert.Equal(17f, close, 1);
            Assert.True(far < close * 0.5f);
        }

        [Fact]
        public void WornOutArmor_StopsProtecting()
        {
            ArmorState armor = Armor("standard");
            Assert.Equal(3, armor.ArmorClass(HitZone.Thorax));
            Assert.False(armor.IsExhausted(HitZone.Thorax));

            DamageResult first = Hit("9x19_pst", HitZone.Thorax, 5f, armor);
            Assert.True(first.BlockedByArmor);
            armor.ApplyDurabilityDamage(HitZone.Thorax, first.ArmorDurabilityDamage * 10f);
            Assert.True(armor.IsExhausted(HitZone.Thorax));

            DamageResult after = Hit("9x19_pst", HitZone.Thorax, 5f, armor);
            Assert.False(after.BlockedByArmor);
            Assert.True(after.Damage > first.Damage);
            Assert.Equal(26f, after.Damage, 1);
        }

        [Fact]
        public void RicochetRolls_AreDeterministicForAGivenSeed()
        {
            int CountRicochets(ulong seed)
            {
                DeterministicRandom random = new DeterministicRandom(seed);
                int ricochets = 0;
                for (int i = 0; i < 200; i++)
                {
                    PenetrationResult result = PenetrationSolver.Solve(new PenetrationQuery
                    {
                        Bullet = Bullet("545x39_ps"),
                        Material = Material("steel"),
                        ThicknessMm = 12f,
                        AngleFromNormalDeg = 75f,
                        TravelDistanceM = 30f
                    }, random);

                    if (result.Ricochet)
                    {
                        ricochets++;
                    }
                }

                return ricochets;
            }

            Assert.Equal(CountRicochets(42UL), CountRicochets(42UL));
            Assert.InRange(CountRicochets(42UL), 90, 195);
            Assert.True(PenetrationSolver.RicochetChance(Material("steel"), 75f) >
                        PenetrationSolver.RicochetChance(Material("steel"), 10f));
            Assert.Equal(0f, PenetrationSolver.RicochetChance(Material("flesh"), 80f), 3);
        }

        [Fact]
        public void Vitals_DieAtZeroHealthInLethalMode()
        {
            PlayerVitals vitals = new PlayerVitals(VitalsMode.Lethal);
            Assert.Equal(100f, vitals.Health, 3);

            DamageResult legHit = Hit("545x39_ps", HitZone.LeftLeg, 10f);
            Assert.False(vitals.ApplyDamage(legHit));
            Assert.Equal(100f - 33f * 0.68f, vitals.Health, 1);
            Assert.False(vitals.IsDead);

            // A head hit from a full power rifle round is lethal, which is what puts
            // an operator down in co-op mode.
            DamageResult kill = Hit("762x54r_lps", HitZone.Head, 10f);
            vitals.ApplyDamage(kill);
            vitals.ApplyDamage(kill);
            Assert.True(vitals.IsDead);
            Assert.Equal(0f, vitals.Health, 3);
            Assert.Equal(HitZone.Thorax, vitals.FatalZone);

            // A dead operator absorbs nothing further.
            Assert.False(vitals.ApplyDamage(kill));
        }

        [Fact]
        public void Vitals_DownAndReviveInCoopMode()
        {
            PlayerVitals vitals = new PlayerVitals(VitalsMode.DownAndRevive);
            // A head hit from a full power rifle round is lethal, which is what puts
            // an operator down in co-op mode.
            DamageResult kill = Hit("762x54r_lps", HitZone.Head, 10f);
            Assert.True(vitals.ApplyDamage(kill));
            Assert.True(vitals.IsDowned);
            Assert.False(vitals.IsDead);
            Assert.True(vitals.IsIncapacitated);
            Assert.True(vitals.BleedDamagePerSecond >= 1.2f);

            Assert.True(vitals.Revive());
            Assert.False(vitals.IsDowned);
            Assert.Equal(100f * PlayerVitals.ReviveHealthFraction, vitals.Health, 2);
            Assert.Equal(0f, vitals.BleedDamagePerSecond, 3);
            Assert.False(vitals.Revive());
        }

        [Fact]
        public void DownedOperatorBleedsOut_WhenNobodyPicksThemUp()
        {
            PlayerVitals vitals = new PlayerVitals(VitalsMode.DownAndRevive);
            vitals.ApplyDamage(Hit("762x54r_lps", HitZone.Head, 10f));
            Assert.True(vitals.IsDowned);

            for (int i = 0; i < 60 && !vitals.IsDead; i++)
            {
                vitals.Tick(1f);
            }

            Assert.True(vitals.IsDead);
            Assert.Equal(DeathCause.BleedOut, vitals.LastCause);
        }

        [Fact]
        public void LimbDamage_ReducesMobilityAndAimStability()
        {
            PlayerVitals vitals = new PlayerVitals(VitalsMode.Lethal);
            Assert.Equal(1f, vitals.MobilityMultiplier, 3);
            Assert.Equal(1f, vitals.AimStabilityMultiplier, 3);

            vitals.ApplyDamage(Hit("545x39_ps", HitZone.LeftLeg, 10f));
            Assert.True(vitals.MobilityMultiplier < 1f);

            vitals.ApplyDamage(Hit("9x19_pst", HitZone.RightArm, 5f));
            Assert.True(vitals.AimStabilityMultiplier < 1f);
            Assert.True(vitals.ZoneDamage(HitZone.RightArm) > 0f);
            Assert.False(vitals.IsDead);
        }

        [Fact]
        public void WoundPenalties_HaveHardFloorsSoAnOperatorIsNeverFrozen()
        {
            PlayerVitals vitals = new PlayerVitals(VitalsMode.Lethal);
            DamageResult probe = new DamageResult { Accepted = true, Damage = 1f, SuppressionAdded = 0f };
            HitZone[] zones =
            {
                HitZone.LeftLeg, HitZone.RightLeg, HitZone.Pelvis,
                HitZone.LeftArm, HitZone.RightArm, HitZone.Thorax
            };

            for (int i = 0; i < 400; i++)
            {
                foreach (HitZone zone in zones)
                {
                    probe.Zone = zone;
                    vitals.ApplyDamage(probe);
                }

                if (vitals.Health < 40f)
                {
                    vitals.Heal(60f);
                }
            }

            Assert.False(vitals.IsDead);
            Assert.Equal(0.35f, vitals.MobilityMultiplier, 3);
            Assert.Equal(0.40f, vitals.AimStabilityMultiplier, 3);
        }

        [Fact]
        public void Suppression_IsReadableCappedAndDecays()
        {
            SuppressionModel suppression = new SuppressionModel();
            Assert.Equal(0f, suppression.Value, 3);

            suppression.Add(0.6f);
            suppression.Add(0.9f);
            Assert.Equal(1f, suppression.Value, 3);
            Assert.Equal(0.55f, suppression.VisualIntensity, 3);
            Assert.Equal(0.5f, suppression.AudioDucking, 3);
            Assert.Equal(2.6f, suppression.SwayMultiplier, 3);
            Assert.Equal(1.55f, suppression.RecoilMultiplier, 3);

            suppression.Tick(1f);
            Assert.Equal(1f - 0.42f, suppression.Value, 3);
            suppression.Tick(10f);
            Assert.Equal(0f, suppression.Value, 3);
            Assert.Equal(0f, suppression.VisualIntensity, 3);

            // Below the visual threshold nothing is drawn: suppression must stay fair.
            suppression.Add(0.2f);
            Assert.Equal(0f, suppression.VisualIntensity, 3);
            Assert.True(suppression.SwayMultiplier > 1f);
        }

        [Fact]
        public void NearMisses_SuppressWithoutDamaging()
        {
            Assert.Equal(0.67f, DamageModel.SuppressionFromNearMiss(0f), 2);
            Assert.Equal(0f, DamageModel.SuppressionFromNearMiss(DamageModel.SuppressionRadiusM), 3);
            Assert.True(DamageModel.SuppressionFromNearMiss(1f) >
                        DamageModel.SuppressionFromNearMiss(3f));
            Assert.Equal(0.448f, DamageModel.SuppressionFromHit(33f), 3);
        }

        [Fact]
        public void InvalidRequests_AreRejectedInsteadOfDealt()
        {
            Assert.Equal("no-bullet", Model.Resolve(new DamageRequest { Zone = HitZone.Thorax }).RejectReason);
            Assert.Equal("invalid-zone",
                Model.Resolve(new DamageRequest { Bullet = Bullet("545x39_ps"), Zone = HitZone.None }).RejectReason);
            Assert.Equal("invalid-distance",
                Model.Resolve(new DamageRequest
                {
                    Bullet = Bullet("545x39_ps"),
                    Zone = HitZone.Thorax,
                    DistanceM = -1f
                }).RejectReason);
        }

        [Fact]
        public void Bleeding_ContinuesAfterTheHitUntilItIsStopped()
        {
            PlayerVitals vitals = new PlayerVitals(VitalsMode.Lethal);
            DeterministicRandom random = new DeterministicRandom(11);
            bool bled = false;
            for (int i = 0; i < 40 && !bled; i++)
            {
                DamageResult result = Hit("762x54r_lps", HitZone.Abdomen, 30f, random: random);
                vitals.ApplyDamage(result);
                bled = result.BleedDamagePerSecond > 0f;
                if (!bled)
                {
                    vitals.Reset();
                }
            }

            Assert.True(bled);
            Assert.True(vitals.BleedDamagePerSecond > 0f);
            Assert.InRange(vitals.BleedDamagePerSecond, 0.35f, 4.5f);

            float before = vitals.Health;
            vitals.Tick(1f);
            Assert.True(vitals.Health < before);
            vitals.StopBleeding();
            float after = vitals.Health;
            vitals.Tick(1f);
            Assert.Equal(after, vitals.Health, 3);
        }

        [Fact]
        public void ShippedMaterials_CoverEverySurfaceTheMapUses()
        {
            List<string> required = new List<string>
            {
                "flesh", "cloth", "glass", "wood", "drywall", "sand", "gravel",
                "concrete", "brick", "sheetmetal", "steel", "hardened_steel", "rubber", "water"
            };

            foreach (string id in required)
            {
                Assert.True(Library.GetMaterialById(id) != null, $"missing material '{id}'");
            }

            // The noise multiplier feeds footstep audibility: metal and grating must be louder than sand.
            Assert.True(Material("sheetmetal").NoiseMultiplier > Material("sand").NoiseMultiplier);
            Assert.True(Material("water").NoiseMultiplier > 1f);
        }
    }
}
