using System.Collections.Generic;
using Kordon.Core.Data;
using Kordon.Core.Loadout;
using Xunit;

namespace Kordon.Core.Tests
{
    /// <summary>
    /// Pre-round kit selection: magazine count/type/size are chosen before the round,
    /// the limits are the same for everybody, and the server rejects anything illegal.
    /// </summary>
    public class LoadoutTests
    {
        private static readonly DefinitionLibrary Library = TestData.Database.Definitions;

        private static LoadoutSelection Base() => TestLoadouts.ValidRifleLoadout();

        [Fact]
        public void ShippedRoster_HasDistinctPrimariesPlusASidearmAndGrenades()
        {
            Assert.Equal(7, Library.Weapons.Count);
            Assert.NotNull(Library.GetWeapon("vk74"));
            Assert.NotNull(Library.GetWeapon("tr30"));
            Assert.NotNull(Library.GetWeapon("vs9"));
            Assert.NotNull(Library.GetWeapon("dmr14"));
            Assert.NotNull(Library.GetWeapon("sr338"));
            Assert.NotNull(Library.GetWeapon("k870"));
            Assert.NotNull(Library.GetWeapon("p9"));

            Assert.Equal(WeaponClass.AssaultRifle, Library.GetWeapon("vk74").Class);
            Assert.Equal(WeaponClass.BattleRifle, Library.GetWeapon("tr30").Class);
            Assert.Equal(WeaponClass.SubMachineGun, Library.GetWeapon("vs9").Class);
            Assert.Equal(WeaponClass.MarksmanRifle, Library.GetWeapon("dmr14").Class);
            Assert.Equal(WeaponClass.SniperRifle, Library.GetWeapon("sr338").Class);
            Assert.Equal(WeaponClass.Shotgun, Library.GetWeapon("k870").Class);
            Assert.Equal(WeaponClass.Sidearm, Library.GetWeapon("p9").Class);

            Assert.Equal(3, Library.Grenades.Count);
            Assert.NotNull(Library.GetGrenade("frag_f1"));
            Assert.NotNull(Library.GetGrenade("smoke_rdg"));
            Assert.NotNull(Library.GetGrenade("flash_zarya"));
            Assert.Equal(3, Library.ArmorKits.Count);
        }

        [Fact]
        public void EveryWeapon_DiffersInRangeTempoWeightAndControl()
        {
            WeaponDefinition smg = Library.GetWeapon("vs9");
            WeaponDefinition rifle = Library.GetWeapon("vk74");
            WeaponDefinition battleRifle = Library.GetWeapon("tr30");
            WeaponDefinition marksman = Library.GetWeapon("dmr14");
            WeaponDefinition sniper = Library.GetWeapon("sr338");
            WeaponDefinition shotgun = Library.GetWeapon("k870");
            WeaponDefinition pistol = Library.GetWeapon("p9");

            // tempo
            Assert.True(smg.RoundsPerMinute > rifle.RoundsPerMinute);
            Assert.True(rifle.RoundsPerMinute > battleRifle.RoundsPerMinute);
            Assert.True(battleRifle.RoundsPerMinute > marksman.RoundsPerMinute);
            Assert.True(marksman.RoundsPerMinute > sniper.RoundsPerMinute);

            // range
            Assert.True(sniper.MaxEffectiveRangeM > marksman.MaxEffectiveRangeM);
            Assert.True(marksman.MaxEffectiveRangeM > rifle.MaxEffectiveRangeM);
            Assert.True(rifle.MaxEffectiveRangeM > smg.MaxEffectiveRangeM);
            Assert.True(smg.MaxEffectiveRangeM > shotgun.MaxEffectiveRangeM);
            Assert.True(shotgun.MaxEffectiveRangeM > pistol.MaxEffectiveRangeM);

            // weight and handling
            Assert.True(sniper.WeightKg > marksman.WeightKg);
            Assert.True(marksman.WeightKg > battleRifle.WeightKg);
            Assert.True(battleRifle.WeightKg > rifle.WeightKg);
            Assert.True(rifle.WeightKg > smg.WeightKg);
            Assert.True(smg.WeightKg > pistol.WeightKg);
            Assert.True(sniper.AimDownSightSeconds > smg.AimDownSightSeconds);
            Assert.True(pistol.AimDownSightSeconds < smg.AimDownSightSeconds);

            // control: the heavier the weapon, the harder the climb
            Assert.True(sniper.Recoil.VerticalBaseDeg > marksman.Recoil.VerticalBaseDeg);
            Assert.True(marksman.Recoil.VerticalBaseDeg > battleRifle.Recoil.VerticalBaseDeg);
            Assert.True(battleRifle.Recoil.VerticalBaseDeg > rifle.Recoil.VerticalBaseDeg);
            Assert.True(rifle.Recoil.VerticalBaseDeg > smg.Recoil.VerticalBaseDeg);
            Assert.True(smg.Spread.HipBaseDeg < shotgun.Spread.HipBaseDeg);
            Assert.True(sniper.Spread.AimingBaseDeg < marksman.Spread.AimingBaseDeg);

            // the shotgun is the only multi-pellet weapon and every weapon is hitscan
            Assert.Equal(8, shotgun.PelletsPerShot);
            Assert.Equal(1, rifle.PelletsPerShot);
            foreach (WeaponDefinition weapon in Library.Weapons.Values)
            {
                Assert.True(weapon.IsHitscan, $"{weapon.Id} must be hitscan");
                Assert.False(weapon.IsThrownProjectile);
            }
        }

        [Fact]
        public void BoltActionAndPumpWeapons_CannotSelectAutomaticFire()
        {
            WeaponDefinition sniper = Library.GetWeapon("sr338");
            Assert.Contains(FireMode.Semi, sniper.FireModes);
            Assert.DoesNotContain(FireMode.Auto, sniper.FireModes);
            Assert.False(sniper.SupportsFireMode(FireMode.Auto));

            WeaponDefinition rifle = Library.GetWeapon("vk74");
            Assert.True(rifle.SupportsFireMode(FireMode.Auto));
            Assert.True(rifle.SupportsFireMode(FireMode.Semi));
        }

        [Fact]
        public void SightsAreOptionsNotUpgrades_AndTheDefaultIsAlwaysAvailable()
        {
            foreach (WeaponDefinition weapon in Library.Weapons.Values)
            {
                Assert.NotEmpty(weapon.AvailableSights);
                Assert.Contains(weapon.DefaultSight, weapon.AvailableSights);
            }

            WeaponDefinition sniper = Library.GetWeapon("sr338");
            Assert.Contains(SightKind.Scope8x, sniper.AvailableSights);
            Assert.DoesNotContain(SightKind.IronSight, sniper.AvailableSights);
            Assert.Equal(8f, WeaponDefinition.MagnificationFor(SightKind.Scope8x), 3);
            Assert.Equal(1f, WeaponDefinition.MagnificationFor(SightKind.IronSight), 3);
        }

        [Fact]
        public void ValidKits_AreAcceptedForEveryWeaponInTheRoster()
        {
            Dictionary<string, string[]> magazines = new Dictionary<string, string[]>
            {
                { "vk74", new[] { "mag_545_30_ps", "mag_545_45_ps", "mag_545_30_bp" } },
                { "tr30", new[] { "mag_762_30_ps", "mag_762_45_drum", "mag_762_30_bp" } },
                { "vs9", new[] { "mag_9x19_20_pst", "mag_9x19_30_pst", "mag_9x19_30_ap" } },
                { "dmr14", new[] { "mag_54r_10_lps", "mag_54r_10_snb" } },
                { "sr338", new[] { "mag_338_5" } },
                { "k870", new[] { "tube_12ga_6_buck", "tube_12ga_6_slug", "tube_12ga_8_buck" } }
            };

            foreach (KeyValuePair<string, string[]> pair in magazines)
            {
                foreach (string magazineId in pair.Value)
                {
                    LoadoutSelection selection = new LoadoutSelection
                    {
                        PrimaryWeaponId = pair.Key,
                        PrimarySight = Library.GetWeapon(pair.Key).DefaultSight,
                        PrimaryMagazines = new List<MagazinePlanEntry> { new MagazinePlanEntry(magazineId, 4) },
                        SecondaryWeaponId = "p9",
                        SecondarySight = SightKind.IronSight,
                        SecondaryMagazines = new List<MagazinePlanEntry>
                        {
                            new MagazinePlanEntry("mag_9x19_15_pst", 2)
                        },
                        GrenadeId = "frag_f1",
                        GrenadeCount = 2,
                        ArmorKitId = "standard"
                    };

                    LoadoutValidationResult result = LoadoutValidator.Validate(selection, Library);
                    Assert.True(result.IsValid, $"{pair.Key} + {magazineId}: {result}");
                    Assert.InRange(result.TotalMassKg, LoadoutRules.MinTotalMassKg, LoadoutRules.MaxTotalMassKg);
                }
            }
        }

        [Fact]
        public void MagazineLimits_AreEnforcedPerSlotAndInTotal()
        {
            LoadoutSelection tooManyPrimary = Base();
            tooManyPrimary.PrimaryMagazines = new List<MagazinePlanEntry> { new MagazinePlanEntry("mag_545_30_ps", 9) };
            Assert.Contains("primary-too-many-magazines",
                string.Join("|", LoadoutValidator.Validate(tooManyPrimary, Library).Errors.ToArray()));

            LoadoutSelection tooManySecondary = Base();
            tooManySecondary.SecondaryMagazines =
                new List<MagazinePlanEntry> { new MagazinePlanEntry("mag_9x19_15_pst", 5) };
            Assert.Contains("secondary-too-many-magazines",
                string.Join("|", LoadoutValidator.Validate(tooManySecondary, Library).Errors.ToArray()));

            LoadoutSelection tooManyTotal = Base();
            tooManyTotal.PrimaryMagazines = new List<MagazinePlanEntry> { new MagazinePlanEntry("mag_545_30_ps", 8) };
            tooManyTotal.SecondaryMagazines =
                new List<MagazinePlanEntry> { new MagazinePlanEntry("mag_9x19_15_pst", 4) };
            Assert.Equal(12, tooManyTotal.TotalMagazines());
            Assert.Contains("too-many-magazines-total",
                string.Join("|", LoadoutValidator.Validate(tooManyTotal, Library).Errors.ToArray()));

            Assert.Equal(8, LoadoutRules.MaxPrimaryMagazines);
            Assert.Equal(4, LoadoutRules.MaxSecondaryMagazines);
            Assert.Equal(10, LoadoutRules.MaxTotalMagazines);
        }

        [Fact]
        public void GrenadeAndArmorRules_AreEnforced()
        {
            LoadoutSelection threeGrenades = Base();
            threeGrenades.GrenadeCount = 3;
            Assert.Contains("grenade-count-out-of-range",
                string.Join("|", LoadoutValidator.Validate(threeGrenades, Library).Errors.ToArray()));

            LoadoutSelection countWithoutType = Base();
            countWithoutType.GrenadeId = string.Empty;
            countWithoutType.GrenadeCount = 2;
            Assert.Contains("grenade-count-without-type",
                string.Join("|", LoadoutValidator.Validate(countWithoutType, Library).Errors.ToArray()));

            LoadoutSelection unknownArmor = Base();
            unknownArmor.ArmorKitId = "powered_exoskeleton";
            Assert.Contains("unknown-armor-kit",
                string.Join("|", LoadoutValidator.Validate(unknownArmor, Library).Errors.ToArray()));
        }

        [Fact]
        public void IncompatibleMagazinesSightsAndSuppressors_AreRejected()
        {
            LoadoutSelection wrongMagazine = Base();
            wrongMagazine.PrimaryMagazines = new List<MagazinePlanEntry> { new MagazinePlanEntry("mag_338_5", 4) };
            Assert.Contains("primary-magazine-incompatible",
                string.Join("|", LoadoutValidator.Validate(wrongMagazine, Library).Errors.ToArray()));

            LoadoutSelection wrongSight = Base();
            wrongSight.PrimarySight = SightKind.Scope8x;
            Assert.Contains("primary-sight-not-available",
                string.Join("|", LoadoutValidator.Validate(wrongSight, Library).Errors.ToArray()));

            LoadoutSelection suppressedSniper = Base();
            suppressedSniper.PrimaryWeaponId = "sr338";
            suppressedSniper.PrimarySight = SightKind.Scope8x;
            suppressedSniper.PrimaryMagazines = new List<MagazinePlanEntry> { new MagazinePlanEntry("mag_338_5", 4) };
            suppressedSniper.PrimarySuppressed = true;
            Assert.Contains("primary-suppressor-not-available",
                string.Join("|", LoadoutValidator.Validate(suppressedSniper, Library).Errors.ToArray()));

            LoadoutSelection pistolWithSmgMagazine = Base();
            pistolWithSmgMagazine.SecondaryMagazines =
                new List<MagazinePlanEntry> { new MagazinePlanEntry("mag_9x19_30_pst", 2) };
            Assert.Contains("secondary-magazine-incompatible",
                string.Join("|", LoadoutValidator.Validate(pistolWithSmgMagazine, Library).Errors.ToArray()));
        }

        [Fact]
        public void MissingOrUnknownParts_AreRejected()
        {
            Assert.Contains("empty-selection",
                string.Join("|", LoadoutValidator.Validate(null, Library).Errors.ToArray()));
            Assert.Contains("no-definition-library",
                string.Join("|", LoadoutValidator.Validate(Base(), null).Errors.ToArray()));

            LoadoutSelection noPrimary = Base();
            noPrimary.PrimaryWeaponId = string.Empty;
            string errors = string.Join("|", LoadoutValidator.Validate(noPrimary, Library).Errors.ToArray());
            Assert.Contains("no-primary-weapon", errors);
            Assert.Contains("primary-missing-weapon", errors);

            LoadoutSelection ghostWeapon = Base();
            ghostWeapon.PrimaryWeaponId = "railgun_x9";
            Assert.Contains("primary-unknown-weapon",
                string.Join("|", LoadoutValidator.Validate(ghostWeapon, Library).Errors.ToArray()));

            LoadoutSelection noMagazines = Base();
            noMagazines.PrimaryMagazines = new List<MagazinePlanEntry>();
            Assert.Contains("primary-no-magazines-planned",
                string.Join("|", LoadoutValidator.Validate(noMagazines, Library).Errors.ToArray()));

            LoadoutSelection zeroCount = Base();
            zeroCount.PrimaryMagazines = new List<MagazinePlanEntry> { new MagazinePlanEntry("mag_545_30_ps", 0) };
            Assert.Contains("primary-magazine-count-must-be-positive",
                string.Join("|", LoadoutValidator.Validate(zeroCount, Library).Errors.ToArray()));
        }

        [Fact]
        public void MassCap_RejectsAnImpossibleKit()
        {
            DefinitionLibrary library = new DefinitionLibrary();
            library.AddBulletsJson("{\"bullets\":[{\"id\":\"b\",\"caliber\":\"x\",\"baseDamage\":30}]}");
            library.AddMagazinesJson("{\"magazines\":[{\"id\":\"m\",\"caliber\":\"x\",\"ammoVariantId\":\"b\"," +
                                     "\"capacity\":30,\"loadedWeightKg\":5,\"emptyWeightKg\":1," +
                                     "\"allowedWeaponIds\":[\"anvil\"]}]}");
            library.AddWeaponsJson("{\"weapons\":[{\"id\":\"anvil\",\"displayName\":\"Anvil\",\"caliber\":\"x\"," +
                                   "\"defaultBulletId\":\"b\",\"weightKg\":30," +
                                   "\"availableSights\":[\"IronSight\"],\"defaultSight\":\"IronSight\"}]}");
            library.AddArmorKitsJson("{\"armorKits\":[{\"id\":\"standard\",\"weightKg\":6.5," +
                                     "\"zones\":[{\"zone\":\"Thorax\",\"armorClass\":3}]}]}");

            LoadoutSelection selection = new LoadoutSelection
            {
                PrimaryWeaponId = "anvil",
                PrimarySight = SightKind.IronSight,
                PrimaryMagazines = new List<MagazinePlanEntry> { new MagazinePlanEntry("m", 1) },
                ArmorKitId = "standard"
            };

            LoadoutValidationResult result = LoadoutValidator.Validate(selection, library);
            Assert.False(result.IsValid);
            Assert.Contains("too-heavy", string.Join("|", result.Errors.ToArray()));
            Assert.Equal(42.5f, result.TotalMassKg, 2);

            LoadoutSelection feather = new LoadoutSelection
            {
                PrimaryWeaponId = "anvil",
                PrimarySight = SightKind.IronSight,
                PrimaryMagazines = new List<MagazinePlanEntry> { new MagazinePlanEntry("m", 1) },
                ArmorKitId = "none"
            };
            Assert.Contains("unknown-armor-kit",
                string.Join("|", LoadoutValidator.Validate(feather, library).Errors.ToArray()));
        }

        [Fact]
        public void ShippedCatalogue_CannotProduceAnOverweightKit()
        {
            // The heaviest legal combination still sits under the cap, so the cap is a
            // tamper guard rather than a balance lever.
            LoadoutSelection heaviest = new LoadoutSelection
            {
                PrimaryWeaponId = "tr30",
                PrimarySight = SightKind.IronSight,
                PrimarySuppressed = true,
                PrimaryMagazines = new List<MagazinePlanEntry> { new MagazinePlanEntry("mag_762_45_drum", 8) },
                SecondaryWeaponId = "p9",
                SecondarySight = SightKind.IronSight,
                SecondarySuppressed = true,
                SecondaryMagazines = new List<MagazinePlanEntry> { new MagazinePlanEntry("mag_9x19_17_ap", 2) },
                GrenadeId = "frag_f1",
                GrenadeCount = 2,
                ArmorKitId = "heavy"
            };

            LoadoutValidationResult result = LoadoutValidator.Validate(heaviest, Library);
            Assert.True(result.IsValid, result.ToString());
            Assert.True(result.TotalMassKg < LoadoutRules.MaxTotalMassKg);
            Assert.Equal(10, result.TotalMagazines);
        }

        [Fact]
        public void LoadoutHash_IsStableOrderIndependentAndChangesWithTheKit()
        {
            LoadoutSelection a = Base();
            LoadoutSelection b = Base();
            Assert.Equal(a.ComputeHash(), b.ComputeHash());
            Assert.Equal(16, a.ComputeHash().Length);

            LoadoutSelection reordered = Base();
            reordered.PrimaryMagazines = new List<MagazinePlanEntry>
            {
                new MagazinePlanEntry("mag_545_30_bp", 1),
                new MagazinePlanEntry("mag_545_30_ps", 4)
            };
            Assert.Equal(a.ComputeHash(), reordered.ComputeHash());

            LoadoutSelection oneMoreMagazine = Base();
            oneMoreMagazine.PrimaryMagazines = new List<MagazinePlanEntry>
            {
                new MagazinePlanEntry("mag_545_30_ps", 5),
                new MagazinePlanEntry("mag_545_30_bp", 1)
            };
            Assert.NotEqual(a.ComputeHash(), oneMoreMagazine.ComputeHash());

            LoadoutSelection otherSight = Base();
            otherSight.PrimarySight = SightKind.Holographic;
            Assert.NotEqual(a.ComputeHash(), otherSight.ComputeHash());

            LoadoutSelection otherArmor = Base();
            otherArmor.ArmorKitId = "heavy";
            Assert.NotEqual(a.ComputeHash(), otherArmor.ComputeHash());

            LoadoutSelection suppressed = Base();
            suppressed.PrimarySuppressed = true;
            Assert.NotEqual(a.ComputeHash(), suppressed.ComputeHash());

            LoadoutSelection cloned = a.Clone();
            cloned.PrimaryMagazines[0] = new MagazinePlanEntry("mag_545_45_ps", 4);
            Assert.NotEqual(a.ComputeHash(), cloned.ComputeHash());
            Assert.Equal(4, a.PrimaryMagazines[0].Count);
        }

        [Fact]
        public void EveryMagazineInTheCatalogue_FitsAtLeastOneWeapon()
        {
            foreach (MagazineDefinition magazine in Library.Magazines.Values)
            {
                bool fits = false;
                foreach (WeaponDefinition weapon in Library.Weapons.Values)
                {
                    if (magazine.IsCompatibleWith(weapon))
                    {
                        fits = true;
                        break;
                    }
                }

                Assert.True(fits, $"{magazine.Id} is not compatible with any weapon");
                Assert.True(magazine.LoadedWeightKg > magazine.EmptyWeightKg);
                Assert.True(magazine.Capacity > 0);
                Assert.NotNull(Library.GetBullet(magazine.AmmoVariantId));
            }

            foreach (WeaponDefinition weapon in Library.Weapons.Values)
            {
                Assert.NotEmpty(Library.MagazinesForWeapon(weapon));
            }
        }

        [Fact]
        public void MagazineVariants_TradeCapacityAgainstHandlingAndMass()
        {
            MagazineDefinition standard = Library.GetMagazine("mag_545_30_ps");
            MagazineDefinition extended = Library.GetMagazine("mag_545_45_ps");
            MagazineDefinition drum = Library.GetMagazine("mag_762_45_drum");
            MagazineDefinition tube = Library.GetMagazine("tube_12ga_8_buck");

            Assert.True(extended.Capacity > standard.Capacity);
            Assert.True(extended.ReloadTimeMultiplier > standard.ReloadTimeMultiplier);
            Assert.True(extended.LoadedWeightKg > standard.LoadedWeightKg);
            Assert.True(extended.HandlingPenalty > standard.HandlingPenalty);
            Assert.True(drum.FumbleChance > standard.FumbleChance);
            Assert.Equal(MagazineKind.Drum, drum.Kind);
            Assert.Equal(MagazineKind.Tube, tube.Kind);
            Assert.True(tube.ReloadTimeMultiplier > 2f);

            // Armour piercing variants trade damage for penetration.
            Assert.True(Library.GetBullet("545x39_bp").PenetrationValue >
                        Library.GetBullet("545x39_ps").PenetrationValue);
            Assert.True(Library.GetBullet("545x39_bp").BaseDamage <
                        Library.GetBullet("545x39_ps").BaseDamage);
            Assert.True(Library.GetBullet("9x19_ap").PenetrationValue >
                        Library.GetBullet("9x19_pst").PenetrationValue);
        }

        [Fact]
        public void ArmorKits_TradeMobilityAndNoiseForProtection()
        {
            ArmorKitDefinition light = Library.GetArmorKit("light");
            ArmorKitDefinition standard = Library.GetArmorKit("standard");
            ArmorKitDefinition heavy = Library.GetArmorKit("heavy");

            Assert.True(light.WeightKg < standard.WeightKg);
            Assert.True(standard.WeightKg < heavy.WeightKg);
            Assert.True(light.MobilityMultiplier > standard.MobilityMultiplier);
            Assert.True(standard.MobilityMultiplier > heavy.MobilityMultiplier);
            Assert.True(light.NoiseMultiplier < standard.NoiseMultiplier);
            Assert.True(standard.NoiseMultiplier < heavy.NoiseMultiplier);
            Assert.Equal(3, standard.GetZone(Combat.HitZone.Thorax).ArmorClass);
            Assert.Equal(4, heavy.GetZone(Combat.HitZone.Thorax).ArmorClass);
            Assert.Equal(3, heavy.GetZone(Combat.HitZone.Head).ArmorClass);
            Assert.Equal(0, light.GetZone(Combat.HitZone.Head).ArmorClass);
            Assert.False(light.HasCoverage(Combat.HitZone.Head));
            Assert.True(heavy.HasCoverage(Combat.HitZone.Head));
        }

        [Fact]
        public void Grenades_AreTheOnlyPhysicalProjectiles()
        {
            foreach (GrenadeDefinition grenade in Library.Grenades.Values)
            {
                Assert.True(grenade.ThrowSpeed > 0f);
                Assert.True(grenade.FuseSeconds > 0f);
                Assert.True(grenade.CookableMinFuse > 0f);
                Assert.True(grenade.CookableMinFuse < grenade.FuseSeconds);
            }

            GrenadeDefinition frag = Library.GetGrenade("frag_f1");
            Assert.Equal(GrenadeKind.Fragmentation, frag.Kind);
            Assert.Equal(115f, frag.DamageAtCenter, 1);
            Assert.Equal(4.5f, frag.LethalRadiusM, 2);
            Assert.Equal(13f, frag.DamageRadiusM, 2);
            Assert.True(frag.LineOfSightRequired);

            GrenadeDefinition smoke = Library.GetGrenade("smoke_rdg");
            Assert.Equal(GrenadeKind.Smoke, smoke.Kind);
            Assert.Equal(0f, smoke.DamageAtCenter, 3);
            Assert.True(smoke.EffectRadiusM > 0f);
            Assert.True(smoke.EffectDurationSeconds > 0f);

            GrenadeDefinition flash = Library.GetGrenade("flash_zarya");
            Assert.Equal(GrenadeKind.Flashbang, flash.Kind);
            Assert.Equal(0f, flash.DamageAtCenter, 3);
            Assert.True(flash.SuppressionRadiusM > 0f);
        }
    }
}
