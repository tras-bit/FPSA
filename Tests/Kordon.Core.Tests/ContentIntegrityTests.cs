using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Kordon.Core.Ammo;
using Kordon.Core.Combat;
using Kordon.Core.Coop;
using Kordon.Core.Data;
using Kordon.Core.Loadout;
using Kordon.Core.Progression;
using Kordon.Core.Rules;
using Kordon.Core.Serialization;
using Kordon.Core.Settings;
using Xunit;

namespace Kordon.Core.Tests
{
    /// <summary>
    /// The shipped content in Assets/StreamingAssets/Data is part of the build: these
    /// tests fail if a definition file drifts away from the design contract (weapon
    /// roles, magazine compatibility, armour trade offs, 20 slots / 50 eliminations,
    /// the co-op chain, cosmetics without gameplay values, tutorial and subtitles).
    /// </summary>
    public class ContentIntegrityTests
    {
        private static GameDatabase Database => TestData.Database;
        private static DefinitionLibrary Library => Database.Definitions;

        private static string Read(string fileName) =>
            File.ReadAllText(Path.Combine(TestData.DefinitionsDirectory, fileName));

        [Fact]
        public void EveryDefinitionFile_LoadsAndIsInternallyConsistent()
        {
            Assert.Empty(Library.ValidateIntegrity());
            Assert.Empty(Database.Validate());

            Assert.Equal(7, Library.Weapons.Count);
            Assert.Equal(17, Library.Magazines.Count);
            Assert.Equal(3, Library.Grenades.Count);
            Assert.Equal(3, Library.ArmorKits.Count);
            Assert.True(Library.Bullets.Count >= 10);

            foreach (MaterialKind kind in Enum.GetValues(typeof(MaterialKind)))
            {
                if (kind == MaterialKind.Unknown)
                {
                    continue;
                }

                MaterialDefinition material = Library.GetMaterial(kind);
                Assert.NotNull(material);
                Assert.Equal(kind, material.Kind);
                Assert.False(string.IsNullOrEmpty(material.Id));
                Assert.False(string.IsNullOrEmpty(material.AudioSurfaceId));
                Assert.Same(material, Library.GetMaterialById(material.Id));
            }
        }

        [Fact]
        public void WeaponRoster_CoversDistinctRolesThatActuallyDiffer()
        {
            WeaponDefinition[] roster = Library.Weapons.Values.ToArray();

            // Several distinct primaries, a pistol, and grenades as throwable equipment.
            Assert.Contains(roster, w => w.Class == WeaponClass.AssaultRifle);
            Assert.Contains(roster, w => w.Class == WeaponClass.BattleRifle);
            Assert.Contains(roster, w => w.Class == WeaponClass.SubMachineGun);
            Assert.Contains(roster, w => w.Class == WeaponClass.MarksmanRifle);
            Assert.Contains(roster, w => w.Class == WeaponClass.SniperRifle);
            Assert.Contains(roster, w => w.Class == WeaponClass.Shotgun);
            Assert.Contains(roster, w => w.Class == WeaponClass.Sidearm);
            Assert.Empty(Library.WeaponsOfClass(WeaponClass.Grenade));
            Assert.Equal(3, Library.Grenades.Count);

            // Nobody is a reskin: tempo, mass and reach all differ.
            Assert.Equal(roster.Length, roster.Select(w => w.RoundsPerMinute).Distinct().Count());
            Assert.Equal(roster.Length, roster.Select(w => w.WeightKg).Distinct().Count());
            Assert.Equal(roster.Length, roster.Select(w => w.MaxEffectiveRangeM).Distinct().Count());
            Assert.Equal(roster.Length, roster.Select(w => w.Class).Distinct().Count());

            WeaponDefinition sniper = Library.GetWeapon("sr338");
            Assert.Equal(45f, sniper.RoundsPerMinute, 3);
            Assert.Equal(700f, sniper.MaxEffectiveRangeM, 3);
            Assert.False(sniper.CanBeSuppressed);
            Assert.DoesNotContain(SightKind.IronSight, sniper.AvailableSights);
            Assert.Equal(SightKind.Scope8x, sniper.DefaultSight);

            WeaponDefinition shotgun = Library.GetWeapon("k870");
            Assert.Equal(8, shotgun.PelletsPerShot);
            Assert.True(shotgun.MaxEffectiveRangeM < 40f);

            WeaponDefinition smg = Library.GetWeapon("vs9");
            Assert.True(smg.RoundsPerMinute > 800f);
            Assert.True(smg.WeightKg < 3f);

            // Every weapon is aimed through a sight that exists on it.
            foreach (WeaponDefinition weapon in roster)
            {
                Assert.NotEmpty(weapon.AvailableSights);
                Assert.Contains(weapon.DefaultSight, weapon.AvailableSights);
                Assert.True(weapon.RoundsPerMinute > 0f);
                Assert.True(weapon.SecondsPerShot > 0f);
                Assert.True(weapon.WeightKg > 0f);
                Assert.True(weapon.ReloadTacticalSeconds > 0f);
                Assert.True(weapon.ReloadDrySeconds >= weapon.ReloadTacticalSeconds);
                Assert.True(weapon.InspectSeconds > 0f);
                Assert.False(string.IsNullOrEmpty(weapon.DisplayName));
                Assert.NotEmpty(weapon.FireModes);
                Assert.Contains(weapon.DefaultFireMode, weapon.FireModes);
            }
        }

        [Fact]
        public void EveryHitscanWeapon_ResolvesAmmunitionForEveryCompatibleMagazine()
        {
            foreach (WeaponDefinition weapon in Library.Weapons.Values)
            {
                if (!weapon.IsHitscan)
                {
                    continue;
                }

                Assert.NotNull(Library.GetBullet(weapon.DefaultBulletId));
                List<MagazineDefinition> magazines = Library.MagazinesForWeapon(weapon);
                Assert.NotEmpty(magazines);
                foreach (MagazineDefinition magazine in magazines)
                {
                    Assert.Equal(weapon.Caliber, magazine.Caliber);
                    Assert.True(magazine.IsCompatibleWith(weapon));
                    BulletDefinition bullet = Library.ResolveBullet(weapon, magazine);
                    Assert.NotNull(bullet);
                    Assert.False(string.IsNullOrEmpty(bullet.Id));
                    Assert.True(bullet.BaseDamage > 0f);
                }
            }

            // The magazine choice is a real pre-round decision, not one option per gun.
            Assert.True(Library.MagazinesForWeapon(Library.GetWeapon("vk74")).Count >= 3);
            Assert.True(Library.MagazinesForWeapon(Library.GetWeapon("tr30")).Count >= 3);
            Assert.True(Library.MagazinesForWeapon(Library.GetWeapon("vs9")).Count >= 3);
            Assert.True(Library.MagazinesForWeapon(Library.GetWeapon("k870")).Count >= 3);
            Assert.True(Library.MagazinesForWeapon(Library.GetWeapon("dmr14")).Count >= 2);
            Assert.True(Library.MagazinesForWeapon(Library.GetWeapon("p9")).Count >= 2);
        }

        [Fact]
        public void Magazines_OnlyFitTheirOwnPlatform()
        {
            WeaponDefinition vk74 = Library.GetWeapon("vk74");
            WeaponDefinition tr30 = Library.GetWeapon("tr30");

            Assert.True(Library.GetMagazine("mag_545_30_ps").IsCompatibleWith(vk74));
            Assert.False(Library.GetMagazine("mag_545_30_ps").IsCompatibleWith(tr30));
            Assert.True(Library.GetMagazine("mag_762_45_drum").IsCompatibleWith(tr30));
            Assert.False(Library.GetMagazine("mag_762_45_drum").IsCompatibleWith(vk74));

            foreach (MagazineDefinition magazine in Library.Magazines.Values)
            {
                Assert.True(magazine.Capacity > 0);
                Assert.NotNull(Library.GetBullet(magazine.AmmoVariantId));
                Assert.True(magazine.LoadedWeightKg > magazine.EmptyWeightKg);
                Assert.True(magazine.ReloadTimeMultiplier > 0f);
                Assert.InRange(magazine.FumbleChance, 0f, 0.25f);
                Assert.NotEmpty(Library.Weapons.Values.Where(magazine.IsCompatibleWith));
                foreach (string weaponId in magazine.AllowedWeaponIds)
                {
                    Assert.NotNull(Library.GetWeapon(weaponId));
                }
            }

            // Different magazine types exist for the same platform (box / extended / drum / tube).
            Assert.True(Library.MagazinesForWeapon(vk74).Select(m => m.Kind).Distinct().Count() >= 2);
            Assert.Contains(Library.MagazinesForWeapon(vk74), m => m.Kind == MagazineKind.Extended);
            Assert.Contains(Library.MagazinesForWeapon(vk74), m => m.AmmoVariantId == "545x39_bp");
            Assert.Contains(Library.MagazinesForWeapon(tr30), m => m.Kind == MagazineKind.Drum);
            Assert.Contains(Library.MagazinesForWeapon(Library.GetWeapon("k870")),
                m => m.Kind == MagazineKind.Tube);
        }

        [Fact]
        public void ArmorKits_TradeProtectionAgainstMobilityAndNoise()
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
            Assert.Equal(1f, light.MobilityMultiplier, 3);

            // Only the heavy kit protects the head, so head shots stay lethal elsewhere.
            Assert.True(heavy.GetZone(HitZone.Head).ArmorClass > 0);
            Assert.Equal(0, light.GetZone(HitZone.Head).ArmorClass);
            Assert.Equal(0, standard.GetZone(HitZone.Head).ArmorClass);
            Assert.True(heavy.GetZone(HitZone.Thorax).ArmorClass > standard.GetZone(HitZone.Thorax).ArmorClass);
            Assert.True(standard.GetZone(HitZone.Thorax).ArmorClass > light.GetZone(HitZone.Thorax).ArmorClass);

            foreach (ArmorKitDefinition kit in Library.ArmorKits.Values)
            {
                Assert.NotEmpty(kit.Zones);
                Assert.True(kit.GetZone(HitZone.Thorax).ArmorClass > 0);
                foreach (ZoneArmor zone in kit.Zones)
                {
                    Assert.InRange(zone.Coverage, 0f, 1f);
                    Assert.True(zone.Durability > 0f);
                    Assert.True(zone.ArmorClass >= 0);
                }
            }
        }

        [Fact]
        public void Grenades_DifferByPurpose_AndOnlyTheFragKills()
        {
            GrenadeDefinition frag = Library.GetGrenade("frag_f1");
            GrenadeDefinition smoke = Library.GetGrenade("smoke_rdg");
            GrenadeDefinition flash = Library.GetGrenade("flash_zarya");

            Assert.Equal(GrenadeKind.Fragmentation, frag.Kind);
            Assert.True(frag.DamageAtCenter > 100f);
            Assert.True(frag.LethalRadiusM > 0f);
            Assert.True(frag.DamageRadiusM > frag.LethalRadiusM);
            Assert.True(frag.SuppressionRadiusM > frag.DamageRadiusM);
            Assert.True(frag.AllowCooking);

            Assert.Equal(GrenadeKind.Smoke, smoke.Kind);
            Assert.Equal(0f, smoke.DamageAtCenter, 3);
            Assert.True(smoke.EffectRadiusM > 0f);
            Assert.True(smoke.EffectDurationSeconds > 10f);
            Assert.False(smoke.AllowCooking);

            Assert.Equal(GrenadeKind.Flashbang, flash.Kind);
            Assert.Equal(0f, flash.DamageAtCenter, 3);
            Assert.True(flash.SuppressionRadiusM > 0f);
            Assert.True(flash.EffectDurationSeconds < smoke.EffectDurationSeconds);

            // Fragmentation damage falls off with distance and reaches zero outside the radius.
            float previous = float.MaxValue;
            for (float distance = 0f; distance <= frag.DamageRadiusM; distance += 1f)
            {
                float damage = frag.DamageAtDistance(distance);
                Assert.True(damage <= previous + 0.001f, "frag damage must fall off with distance");
                previous = damage;
            }

            Assert.Equal(0f, frag.DamageAtDistance(frag.DamageRadiusM + 5f), 3);
        }

        [Fact]
        public void ShippedMatchRules_AreTheTwentySlotFiftyEliminationFreeForAll()
        {
            MatchRulesConfig rules = Database.MatchRules;
            Assert.Equal(MatchMode.FreeForAll, rules.Mode);
            Assert.Equal(20, rules.MaxPlayers);
            Assert.Equal(50, rules.KillTarget);
            Assert.False(rules.AllowBots);
            Assert.False(rules.CreditAssists);
            Assert.Equal(ResupplyRule.OnRespawnOnly, rules.Resupply);
            Assert.Equal(5f, rules.RespawnDelaySeconds, 3);
            Assert.True(rules.SpawnProtectionSeconds > 0f);
            Assert.True(rules.ReconnectGraceSeconds >= 60f);
            Assert.Equal(0f, rules.TimeLimitSeconds, 3);
            Assert.True(string.IsNullOrEmpty(rules.Validate()));

            // The file cannot silently disagree with the code defaults.
            MatchRulesConfig defaults = MatchRulesConfig.DefaultFreeForAll();
            Assert.Equal(defaults.MaxPlayers, rules.MaxPlayers);
            Assert.Equal(defaults.KillTarget, rules.KillTarget);
            Assert.Equal(defaults.AllowBots, rules.AllowBots);
            Assert.Equal(defaults.Resupply, rules.Resupply);
            MatchRulesConfig broken = rules.Clone();
            broken.MaxPlayers = 19;
            Assert.Equal("ffa-requires-20-slots", broken.Validate());

            broken = rules.Clone();
            broken.KillTarget = 49;
            Assert.Equal("ffa-requires-50-kills", broken.Validate());

            broken = rules.Clone();
            broken.KillTarget = 0;
            Assert.Equal("kill-target-must-be-positive", broken.Validate());
        }

        [Fact]
        public void BotsAreOnlyEverAnOfflinePracticePreset()
        {
            MatchRulesConfig practice = MatchRulesConfig.PracticeRange();
            Assert.Equal(MatchMode.PracticeRange, practice.Mode);
            Assert.True(practice.AllowBots);
            Assert.Equal(15, practice.KillTarget);
            Assert.Equal(1, practice.MinPlayersToStartLive);

            // The documented practice preset in match_rules.json matches the code, so the
            // data file cannot quietly turn the released mode into a bot match.
            Json preset = Json.Parse(Read("match_rules.json"))["practicePreset"];
            Assert.Equal("PracticeRange", preset["mode"].AsString());
            Assert.Equal(practice.KillTarget, preset["killTarget"].AsInt(0));
            Assert.True(preset["allowBots"].AsBool(false));
            Assert.Equal(practice.MinPlayersToStartLive, preset["minPlayersToStartLive"].AsInt(0));

            MatchRulesConfig released = MatchRulesParser.FromJson(Read("match_rules.json"));
            Assert.False(released.AllowBots);
            Assert.Equal(MatchMode.FreeForAll, released.Mode);
        }

        [Fact]
        public void ShippedCoopOperation_HasABriefingAChainAndAnExtraction()
        {
            MissionDefinition mission = Database.GetMission("silent_kiln");
            Assert.NotNull(mission);
            Assert.Equal("Operation Silent Kiln", mission.Name);
            Assert.Equal("COOP_SilentKiln", mission.MapSceneName);
            Assert.Equal(2, mission.MinPlayers);
            Assert.Equal(4, mission.MaxPlayers);
            Assert.True(mission.TimeLimitSeconds >= 600f);
            Assert.True(mission.ExtractionWaitSeconds > 0f);
            Assert.True(mission.ExtractionBoardSeconds > 0f);
            Assert.True(mission.DownAndRevive);
            Assert.True(mission.DownedBleedOutSeconds > 0f);
            Assert.True(mission.MaxSimultaneousHostiles > 0);
            Assert.True(mission.TotalHostileBudget > mission.MaxSimultaneousHostiles);
            Assert.False(string.IsNullOrEmpty(mission.BriefingTitle));
            Assert.False(string.IsNullOrEmpty(mission.BriefingText));
            Assert.False(string.IsNullOrEmpty(mission.DebriefText));

            Assert.True(mission.Objectives.Count >= 5);
            Assert.Contains(mission.Objectives, o => o.Kind == ObjectiveKind.Extract);
            Assert.Contains(mission.Objectives, o => o.Kind == ObjectiveKind.Hold);
            Assert.Equal(new List<string> { "hold_kiln_hall" }, mission.ExtractionUnlockIds());

            var ids = new HashSet<string>(StringComparer.Ordinal);
            foreach (ObjectiveDefinition objective in mission.Objectives)
            {
                Assert.True(ids.Add(objective.Id), $"duplicate objective id {objective.Id}");
                Assert.False(string.IsNullOrEmpty(objective.Title));
                Assert.False(string.IsNullOrEmpty(objective.Description));
                Assert.True(objective.RequiredProgress > 0);
                Assert.NotEqual(ObjectiveKind.None, objective.Kind);
                foreach (string dependency in objective.DependsOn)
                {
                    Assert.NotNull(mission.GetObjective(dependency));
                }
            }

            // The chain starts at the insertion point and ends on the extraction pad.
            Assert.Empty(mission.Objectives[0].DependsOn);
            Assert.Equal(ObjectiveKind.Reach, mission.Objectives[0].Kind);
            Assert.Equal(ObjectiveKind.Extract, mission.Objectives[mission.Objectives.Count - 1].Kind);
            Assert.True(mission.GetObjective("hold_kiln_hall").HoldSeconds >= 30f);
            Assert.True(mission.GetObjective("sabotage_fuel_line").RequiredProgress >= 2);
            Assert.True(mission.GetObjective("reach_yard").IsCritical);
            Assert.True(mission.GetObjective("breach_gate").IsCritical);
            Assert.True(mission.GetObjective("extract").IsCritical);
        }

        [Fact]
        public void Cosmetics_NeverCarryGameplayValues()
        {
            CosmeticCatalog catalog = Database.Cosmetics;
            Assert.True(catalog.Items.Count >= 10);
            Assert.Empty(catalog.ValidateNoGameplayEffects(Database.RawCosmeticsJson));
            Assert.False(CosmeticItem.AffectsGameplay);

            // A stats block added by accident must be caught.
            const string sabotaged = @"{ ""cosmetics"": [
                { ""id"": ""finish_cheat"", ""displayName"": ""Cheat"", ""category"": ""WeaponFinish"",
                  ""damage"": 50, ""recoil"": -0.4, ""capacity"": 60 } ] }";
            List<string> problems = new CosmeticCatalog().ValidateNoGameplayEffects(sabotaged);
            Assert.Equal(3, problems.Count);
            Assert.Contains(problems, p => p.Contains("finish_cheat") && p.Contains("'damage'"));
            Assert.Contains(problems, p => p.Contains("'recoil'"));
            Assert.Contains(problems, p => p.Contains("'capacity'"));

            // Every category exists and (except the 50 elimination flourish) has a default.
            CosmeticCategory[] categories =
            {
                CosmeticCategory.WeaponFinish, CosmeticCategory.SightReticleColor,
                CosmeticCategory.OperatorPatch, CosmeticCategory.CallsignPlate,
                CosmeticCategory.HelmetCamo, CosmeticCategory.KillFeedFlourish
            };
            foreach (CosmeticCategory category in categories)
            {
                List<CosmeticItem> items = catalog.Items.Where(i => i.Category == category).ToList();
                Assert.NotEmpty(items);
                if (category != CosmeticCategory.KillFeedFlourish)
                {
                    Assert.Contains(items, i => i.AvailableByDefault);
                }
            }

            ProgressionProfile fresh = new ProgressionProfile { PlayerName = "OPERATOR" };
            Assert.Contains(catalog.UnlockedFor(fresh), i => i.Id == "finish_factory_green");
            Assert.DoesNotContain(catalog.UnlockedFor(fresh), i => i.Id == "flourish_quiet_line");
            Assert.NotNull(catalog.Get("flourish_quiet_line"));
            Assert.Null(catalog.Get("does_not_exist"));
        }

        [Fact]
        public void Tutorial_TeachesTapReloadAndHoldInspectOnTheSameKey()
        {
            TutorialScript tutorial = Database.Tutorial;
            Assert.Equal("kordon_basics", tutorial.Id);
            Assert.True(tutorial.Steps.Count >= 10);

            Assert.Contains(tutorial.Steps, s => s.Kind == TutorialStepKind.MoveToMarker);
            Assert.Contains(tutorial.Steps, s => s.Kind == TutorialStepKind.Sprint);
            Assert.Contains(tutorial.Steps, s => s.Kind == TutorialStepKind.ChangeStance);
            Assert.Contains(tutorial.Steps, s => s.Kind == TutorialStepKind.Lean);
            Assert.Contains(tutorial.Steps, s => s.Kind == TutorialStepKind.FireWeapon);
            Assert.Contains(tutorial.Steps, s => s.Kind == TutorialStepKind.Reload);
            Assert.Contains(tutorial.Steps, s => s.Kind == TutorialStepKind.InspectMagazine);
            Assert.Equal(TutorialStepKind.Complete, tutorial.Steps[tutorial.Steps.Count - 1].Kind);

            TutorialStep reload = tutorial.Steps.First(s => s.Kind == TutorialStepKind.Reload);
            TutorialStep inspect = tutorial.Steps.First(s => s.Kind == TutorialStepKind.InspectMagazine);
            Assert.Equal(InputAction.Reload, reload.RequiredAction);
            Assert.Equal(InputAction.InspectMagazine, inspect.RequiredAction);

            // The tutorial text and the shipped bindings describe the same key.
            KeyBindings bindings = KeyBindings.CreateDefault();
            Assert.Equal(bindings.GetKeyName(InputAction.Reload),
                bindings.GetKeyName(InputAction.InspectMagazine));
            Assert.Contains(bindings.GetKeyName(InputAction.Reload), reload.Title);
            Assert.Contains(bindings.GetKeyName(InputAction.InspectMagazine), inspect.Title);
            Assert.Contains("Tap", reload.Title);
            Assert.Contains("Hold", inspect.Title);

            foreach (TutorialStep step in tutorial.Steps)
            {
                Assert.False(string.IsNullOrEmpty(step.Id));
                Assert.False(string.IsNullOrEmpty(step.Title));
                Assert.False(string.IsNullOrEmpty(step.Body));
                Assert.True(step.RequiredCount > 0);
                Assert.Equal(0, step.Progress);
                Assert.False(step.IsComplete);
            }
        }

        [Fact]
        public void Tutorial_PlaythroughCompletesAndCanBeSkippedOrReplayed()
        {
            TutorialScript run = TutorialScript.FromJson(Read("tutorial.json"));
            Assert.Equal(run.Steps.Count, Database.Tutorial.Steps.Count);
            Assert.False(run.IsFinished);
            Assert.Equal("welcome", run.Current.Id);

            // The wrong input never advances the tutorial.
            Assert.False(run.Notify(TutorialStepKind.FireWeapon, InputAction.Fire, string.Empty));
            Assert.Equal(0, run.CurrentStepIndex);

            while (!run.IsFinished)
            {
                TutorialStep step = run.Current;
                for (int i = 0; i < step.RequiredCount; i++)
                {
                    bool advanced = run.Notify(step.Kind, step.RequiredAction, step.MarkerId);
                    Assert.Equal(i == step.RequiredCount - 1, advanced);
                }

                Assert.True(step.IsComplete);
                Assert.True(step.IsSatisfied || step.Kind == TutorialStepKind.Info);
            }

            Assert.Null(run.Current);
            Assert.False(run.Notify(TutorialStepKind.Info, InputAction.None, string.Empty));

            run.Reset();
            Assert.Equal(0, run.CurrentStepIndex);
            Assert.All(run.Steps, step => Assert.False(step.IsComplete));

            run.Skip();
            Assert.True(run.IsFinished);
        }

        [Fact]
        public void Subtitles_CaptionEveryAnnouncedEvent()
        {
            SubtitleLibrary subtitles = Database.Subtitles;
            Assert.True(subtitles.Lines.Count >= 15);

            SubtitleTrigger[] required =
            {
                SubtitleTrigger.MissionStart, SubtitleTrigger.ObjectiveStarted,
                SubtitleTrigger.ObjectiveCompleted, SubtitleTrigger.AlarmRaised,
                SubtitleTrigger.ExtractionCalled, SubtitleTrigger.ExtractionArrived,
                SubtitleTrigger.OperatorDowned, SubtitleTrigger.EnemyContact,
                SubtitleTrigger.MatchCountdown, SubtitleTrigger.MatchEnd
            };
            foreach (SubtitleTrigger trigger in required)
            {
                Assert.NotEmpty(subtitles.ForTrigger(trigger));
            }

            Assert.NotEmpty(subtitles.ForTrigger(SubtitleTrigger.ObjectiveStarted, "reach_yard"));
            Assert.Empty(subtitles.ForTrigger(SubtitleTrigger.ObjectiveStarted, "objective_that_does_not_exist"));

            var ids = new HashSet<string>(StringComparer.Ordinal);
            foreach (SubtitleLine line in subtitles.Lines)
            {
                Assert.True(ids.Add(line.Id), $"duplicate subtitle id {line.Id}");
                Assert.False(string.IsNullOrEmpty(line.Text));
                Assert.False(string.IsNullOrEmpty(line.Speaker));
                Assert.True(line.DurationSeconds > 0f);
                Assert.NotEqual(SubtitleTrigger.None, line.Trigger);
            }

            // Every co-op objective is captioned when it starts.
            MissionDefinition mission = Database.GetMission("silent_kiln");
            foreach (ObjectiveDefinition objective in mission.Objectives)
            {
                List<SubtitleLine> lines = subtitles.ForTrigger(SubtitleTrigger.ObjectiveStarted, objective.Id);
                Assert.NotEmpty(lines);
                Assert.All(lines, line => Assert.Equal(objective.Id, line.ObjectiveId));
            }
        }

        [Fact]
        public void ShippedDataFiles_AreValidJsonWithoutLoopbackAddressesOrSecrets()
        {
            List<string> files = Directory
                .GetFiles(TestData.DataRoot, "*.json", SearchOption.AllDirectories)
                .OrderBy(f => f, StringComparer.Ordinal)
                .ToList();

            Assert.True(files.Count >= 12, $"expected the shipped data files, found {files.Count}");

            foreach (string file in files)
            {
                string text = File.ReadAllText(file);
                Json parsed = Json.Parse(text);
                Assert.False(parsed.IsNull, $"{file} did not parse");

                string lower = text.ToLowerInvariant();
                Assert.DoesNotContain("localhost", lower);
                Assert.DoesNotContain("127.0.0.1", lower);
                Assert.DoesNotContain("::1", lower);
                Assert.DoesNotContain("apikey", lower.Replace("_", string.Empty));
                Assert.DoesNotContain("password\": \"p", lower);
                Assert.DoesNotContain("todo", lower);
            }

            Assert.Equal(string.Empty, Database.Net.ServerPasswordHash);
        }
    }
}
