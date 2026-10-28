using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Kordon.Core.Progression;
using Kordon.Core.Utility;
using Kordon.Core.Voice;
using Xunit;

namespace Kordon.Core.Tests
{
    /// <summary>
    /// Local proximity voice (with a distinct team radio channel) and the progression
    /// rules: unlocks are cosmetic only, every tool stays available on equal terms, and
    /// nothing in the catalogue can carry a gameplay value.
    /// </summary>
    public class VoiceProgressionTests
    {
        private static readonly KordonVec3 Origin = new KordonVec3(0f, 0f, 0f);

        private static KordonVec3 At(float metres) => new KordonVec3(metres, 0f, 0f);

        private static VoiceProximityModel Model() => new VoiceProximityModel();

        [Fact]
        public void LocalVoice_IsFullInsideTheInnerRadius_AndFallsOffToSilence()
        {
            VoiceProximityModel model = Model();
            Assert.Equal(6f, model.InnerRadiusM, 3);
            Assert.Equal(28f, model.OuterRadiusM, 3);
            Assert.Equal(model.OuterRadiusM, model.LocalHearRadiusM, 3);

            VoiceAudibility close = model.Evaluate(VoiceChannel.LocalProximity, Origin, At(2f), true);
            Assert.True(close.Audible);
            Assert.Equal(1f, close.Gain, 3);
            Assert.Equal(2f, close.DistanceM, 3);
            Assert.False(close.Occluded);

            Assert.Equal(1f, model.Evaluate(VoiceChannel.LocalProximity, Origin, At(6f), true).Gain, 3);

            // Half way between the inner and outer radius is half volume.
            VoiceAudibility mid = model.Evaluate(VoiceChannel.LocalProximity, Origin, At(17f), true);
            Assert.Equal(0.5f, mid.Gain, 3);
            Assert.True(mid.Audible);

            VoiceAudibility far = model.Evaluate(VoiceChannel.LocalProximity, Origin, At(27.9f), true);
            Assert.True(far.Gain > 0f);
            Assert.False(far.Audible, "voice at the edge of the radius is below the audible threshold");

            VoiceAudibility outside = model.Evaluate(VoiceChannel.LocalProximity, Origin, At(28f), true);
            Assert.False(outside.Audible);
            Assert.Equal(0f, outside.Gain, 3);
            Assert.False(model.Evaluate(VoiceChannel.LocalProximity, Origin, At(500f), true).Audible);

            // Monotonic fall off: no sudden louder spot further away.
            float previous = float.MaxValue;
            for (float distance = 0f; distance <= 27f; distance += 1f)
            {
                float gain = model.Evaluate(VoiceChannel.LocalProximity, Origin, At(distance), true).Gain;
                Assert.True(gain <= previous + 0.0001f, $"gain rose at {distance} m");
                previous = gain;
            }
        }

        [Fact]
        public void Occlusion_CutsLocalVoice_ButNeverToAbsoluteSilence()
        {
            VoiceProximityModel model = Model();
            float open = model.Evaluate(VoiceChannel.LocalProximity, Origin, At(10f), true).Gain;
            Assert.True(open > 0.5f);

            VoiceAudibility noLineOfSight =
                model.Evaluate(VoiceChannel.LocalProximity, Origin, At(10f), false);
            Assert.True(noLineOfSight.Occluded);
            Assert.Equal(open * model.OccludedGain, noLineOfSight.Gain, 3);
            Assert.True(noLineOfSight.Gain < open);

            VoiceAudibility oneWall =
                model.Evaluate(VoiceChannel.LocalProximity, Origin, At(10f), true, 1);
            Assert.True(oneWall.Occluded);
            Assert.Equal(open * model.OccludedGain * 0.75f, oneWall.Gain, 3);

            VoiceAudibility twoWalls =
                model.Evaluate(VoiceChannel.LocalProximity, Origin, At(10f), true, 2);
            Assert.Equal(open * model.OccludedGain * 0.5f, twoWalls.Gain, 3);

            // Heavily occluded speech stays faintly audible instead of vanishing.
            VoiceAudibility fiveWalls =
                model.Evaluate(VoiceChannel.LocalProximity, Origin, At(10f), true, 5);
            Assert.Equal(open * model.OccludedGain * 0.15f, fiveWalls.Gain, 3);
            Assert.True(fiveWalls.Gain > 0f);
            Assert.True(fiveWalls.Gain < twoWalls.Gain);
        }

        [Fact]
        public void TeamRadio_ReachesEverybody_AndIsKeptVisuallyAndAudiblyDistinct()
        {
            VoiceProximityModel model = Model();

            VoiceAudibility radio = model.Evaluate(VoiceChannel.TeamRadio, Origin, At(500f), false, 4);
            Assert.True(radio.Audible);
            Assert.Equal(VoiceChannel.TeamRadio, radio.Channel);
            Assert.Equal(model.RadioGain, radio.Gain, 3);
            Assert.False(radio.Occluded, "radio audio is not distance or occlusion limited");
            Assert.Equal(500f, radio.DistanceM, 3);

            VoiceAudibility local = model.Evaluate(VoiceChannel.LocalProximity, Origin, At(3f), true);
            Assert.Equal(VoiceChannel.LocalProximity, local.Channel);
            Assert.True(local.Gain > radio.Gain, "nearby local speech is louder than filtered radio");

            // The UI shows a different label per channel, so the team channel is never
            // mistaken for somebody standing next to you.
            Assert.Equal("LOCAL", VoiceProximityModel.ChannelLabel(VoiceChannel.LocalProximity));
            Assert.Equal("RADIO", VoiceProximityModel.ChannelLabel(VoiceChannel.TeamRadio));
            Assert.Equal("MUTED", VoiceProximityModel.ChannelLabel(VoiceChannel.None));
            Assert.NotEqual(VoiceProximityModel.ChannelLabel(VoiceChannel.LocalProximity),
                VoiceProximityModel.ChannelLabel(VoiceChannel.TeamRadio));
        }

        [Fact]
        public void AMutedChannelProducesNothing_AndEvaluationIsDeterministic()
        {
            VoiceProximityModel model = Model();
            VoiceAudibility muted = model.Evaluate(VoiceChannel.None, Origin, At(1f), true);
            Assert.False(muted.Audible);
            Assert.Equal(0f, muted.Gain, 3);

            for (int i = 0; i < 25; i++)
            {
                VoiceAudibility a = model.Evaluate(VoiceChannel.LocalProximity, Origin, At(11f), true, 1);
                VoiceAudibility b = Model().Evaluate(VoiceChannel.LocalProximity, Origin, At(11f), true, 1);
                Assert.Equal(a.Gain, b.Gain, 5);
                Assert.Equal(a.Audible, b.Audible);
                Assert.Equal(a.Occluded, b.Occluded);
                Assert.Equal(a.DistanceM, b.DistanceM, 5);
            }
        }

        [Fact]
        public void TheShippedVoiceTransport_IsAnHonestIntegrationPoint()
        {
            // Voice needs an external provider account or API key, which the project must
            // never ship or ask for. The bundled transport therefore reports "unavailable"
            // and every call is a safe no-op instead of a fake implementation.
            IVoiceTransport transport = new UnconfiguredVoiceTransport();
            Assert.False(transport.IsAvailable);
            Assert.Equal("none", transport.ProviderName);
            Assert.Contains("not connected", transport.StatusMessage);
            Assert.Contains("account", transport.StatusMessage);
            Assert.Contains("radio text", transport.StatusMessage);
            Assert.False(transport.Initialize("{ \"provider\": \"vivox\", \"apiKey\": \"\" }"));

            transport.SetLocalCaptureEnabled(true);
            transport.SetChannel(VoiceChannel.TeamRadio);
            transport.SendAudio(new float[480], 480, 48000, VoiceChannel.LocalProximity);
            transport.SetListenerGain(7ul, 0.5f);
            transport.Shutdown();
            Assert.False(transport.IsAvailable);
        }

        [Fact]
        public void CosmeticItems_CannotCarryGameplayValuesByConstruction()
        {
            Assert.False(CosmeticItem.AffectsGameplay);

            foreach (FieldInfo field in typeof(CosmeticItem).GetFields(
                         BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance |
                         BindingFlags.Static))
            {
                Type type = field.FieldType;
                bool allowed = type == typeof(string) || type == typeof(bool) ||
                               type == typeof(CosmeticCategory) || type.IsEnum;
                Assert.True(allowed,
                    $"CosmeticItem.{field.Name} has type {type.Name}: cosmetics must stay non numeric");
            }

            Assert.Empty(typeof(CosmeticItem).GetProperties(
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance));

            // No purchase language anywhere in the catalogue: unlocks are earned, not bought.
            foreach (CosmeticItem item in TestData.Database.Cosmetics.Items)
            {
                string requirement = item.UnlockRequirement.ToLowerInvariant();
                foreach (string forbidden in new[]
                         {
                             "buy", "purchase", "price", "premium", "currency", "dlc",
                             "microtransaction", "usd", "eur", "$", "pay"
                         })
                {
                    Assert.DoesNotContain(forbidden, requirement);
                    Assert.DoesNotContain(forbidden, item.Description.ToLowerInvariant());
                }
            }
        }

        [Fact]
        public void Progression_UnlocksCosmeticsOnly_AndNeverChangesTheTools()
        {
            CosmeticCatalog catalog = TestData.Database.Cosmetics;
            ProgressionProfile profile = new ProgressionProfile { PlayerName = "OPERATOR" };

            Assert.Empty(profile.UnlockedCosmeticIds);
            Assert.True(profile.Unlock("finish_worn_black"));
            Assert.False(profile.Unlock("finish_worn_black"), "unlocking twice must not duplicate");
            Assert.True(profile.HasUnlocked("finish_worn_black"));
            Assert.False(profile.HasUnlocked("flourish_quiet_line"));

            List<string> baseline = catalog.UnlockedFor(profile).Select(i => i.Id).ToList();
            Assert.Contains("finish_factory_green", baseline);
            Assert.DoesNotContain("flourish_quiet_line", baseline);

            // Playing more matches, winning, finishing the co-op op or the tutorial does
            // not by itself hand out anything: unlocks are recorded explicitly.
            profile.MatchesPlayed = 250;
            profile.MatchesWon = 120;
            profile.CoopOperationsCompleted = 40;
            profile.TutorialCompleted = true;
            Assert.Equal(baseline, catalog.UnlockedFor(profile).Select(i => i.Id).ToList());

            Assert.True(profile.Unlock("flourish_quiet_line"));
            Assert.Contains("flourish_quiet_line",
                catalog.UnlockedFor(profile).Select(i => i.Id).ToList());

            // The weapon roster is identical for a brand new player and a veteran.
            Assert.Equal(7, TestData.Database.Definitions.Weapons.Count);
            Assert.Equal(17, TestData.Database.Definitions.Magazines.Count);
            Assert.Equal(3, TestData.Database.Definitions.ArmorKits.Count);
            Assert.Equal(3, TestData.Database.Definitions.Grenades.Count);
        }

        [Fact]
        public void EquippingKeepsOneCosmeticPerCategory()
        {
            ProgressionProfile profile = new ProgressionProfile { PlayerName = "OPERATOR" };
            Assert.Equal(string.Empty, profile.EquippedId(CosmeticCategory.WeaponFinish));

            profile.Unlock("finish_worn_black");
            profile.Equip(CosmeticCategory.WeaponFinish, "finish_worn_black");
            Assert.Equal("finish_worn_black", profile.EquippedId(CosmeticCategory.WeaponFinish));

            profile.Unlock("finish_kiln_soot");
            profile.Equip(CosmeticCategory.WeaponFinish, "finish_kiln_soot");
            Assert.Equal("finish_kiln_soot", profile.EquippedId(CosmeticCategory.WeaponFinish));
            Assert.Single(profile.Equipped);

            profile.Equip(CosmeticCategory.SightReticleColor, "reticle_cyan");
            profile.Equip(CosmeticCategory.OperatorPatch, "patch_zavod_crew");
            Assert.Equal(3, profile.Equipped.Count);
            Assert.Equal("reticle_cyan", profile.EquippedId(CosmeticCategory.SightReticleColor));
            Assert.Equal("patch_zavod_crew", profile.EquippedId(CosmeticCategory.OperatorPatch));
            Assert.Equal(string.Empty, profile.EquippedId(CosmeticCategory.HelmetCamo));
        }

        [Fact]
        public void TheKillFeedFlourish_IsTheOnlyRewardForWinningAFreeForAll()
        {
            CosmeticCatalog catalog = TestData.Database.Cosmetics;
            CosmeticItem flourish = catalog.Get("flourish_quiet_line");
            Assert.NotNull(flourish);
            Assert.Equal(CosmeticCategory.KillFeedFlourish, flourish.Category);
            Assert.False(flourish.AvailableByDefault);
            Assert.Equal("reach-50-eliminations-in-one-match", flourish.UnlockRequirement);

            // Reaching the 50 elimination win condition rewards a cosmetic, not a weapon.
            Assert.Equal(50, TestData.Database.MatchRules.KillTarget);
            Assert.DoesNotContain(catalog.Items,
                i => i.Category == CosmeticCategory.None);
        }
    }
}
