using System.Collections.Generic;
using Kordon.Core.Combat;
using Kordon.Core.Loadout;
using Kordon.Core.Rules;
using Kordon.Core.Utility;
using Xunit;

namespace Kordon.Core.Tests
{
    /// <summary>
    /// The main PvP contract: free-for-all, 20 human slots, no teams, server side score,
    /// first operator to personally reach 50 eliminations wins.
    /// </summary>
    public class MatchRulesTests
    {
        private const int KillTarget = 50;

        private static FfaMatchController CreateLiveMatch(int players, out List<ulong> ids, float now = 100f)
        {
            MatchRulesConfig config = MatchRulesConfig.DefaultFreeForAll();
            FfaMatchController match = new FfaMatchController(config, new DeterministicRandom(1234));
            ids = new List<ulong>();
            for (int i = 0; i < players; i++)
            {
                ulong id = (ulong)(1000 + i);
                Assert.True(match.TryJoin(id, $"OP{i:D2}", false, now).IsAccepted);
                ids.Add(id);
            }

            match.BeginLoadoutPhase(now);
            foreach (ulong id in ids)
            {
                Assert.True(match.SubmitLoadout(id, TestLoadouts.ValidRifleLoadout(), TestData.Database.Definitions, now).Accepted);
            }

            Assert.True(match.BeginCountdown(now));
            Assert.True(match.BeginLive(now));
            match.InitialSpawn(_ => 0, now);

            // Initial spawn grants spawn protection; every test in this file is about
            // what happens once the match is really contested, so move the clock past
            // the protection window before handing the match over.
            match.Tick(now + config.SpawnProtectionSeconds + 0.5f);
            return match;
        }

        [Fact]
        public void DefaultRules_AreTwentySlotsAndFiftyKills()
        {
            MatchRulesConfig config = MatchRulesConfig.DefaultFreeForAll();
            Assert.Equal(20, config.MaxPlayers);
            Assert.Equal(KillTarget, config.KillTarget);
            Assert.Equal(MatchMode.FreeForAll, config.Mode);
            Assert.Null(config.Validate());
        }

        [Fact]
        public void ShippedMatchRulesJson_MatchesTheRequiredContract()
        {
            MatchRulesConfig config = TestData.Database.MatchRules;
            Assert.Equal(20, config.MaxPlayers);
            Assert.Equal(KillTarget, config.KillTarget);
            Assert.Equal(MatchMode.FreeForAll, config.Mode);
            Assert.False(config.AllowBots);
            Assert.Null(config.Validate());
        }

        [Fact]
        public void Match_AcceptsExactlyTwentyPlayersAndRefusesTheTwentyFirst()
        {
            FfaMatchController match = CreateLiveMatch(20, out List<ulong> ids);
            Assert.Equal(20, match.Roster.Count);
            Assert.Equal(0, match.FreeSlots);

            JoinResult refused = match.TryJoin(9999UL, "OP21", false, 100f);
            Assert.False(refused.IsAccepted);
            Assert.Equal(JoinOutcome.MatchFull, refused.Outcome);
            Assert.Equal(20, match.Roster.Count);
        }

        [Fact]
        public void FreeForAll_HasNoTeams()
        {
            FfaMatchController match = CreateLiveMatch(4, out _);
            // PlayerMatchState carries no team field at all; ranking is purely individual.
            List<ScoreboardRow> board = match.BuildScoreboard();
            Assert.Equal(4, board.Count);
            Assert.Equal(1, board[0].Rank);
            Assert.Equal(4, board[3].Rank);
        }

        [Fact]
        public void Victory_GoesToTheFirstPlayerToPersonallyReachFiftyKills()
        {
            FfaMatchController match = CreateLiveMatch(3, out List<ulong> ids);
            ulong killer = ids[0];
            ulong victim = ids[1];

            for (int i = 1; i <= KillTarget; i++)
            {
                // Keep the victim alive and available for the next elimination.
                if (!match.GetPlayer(victim).IsAlive)
                {
                    match.GetPlayer(victim).RespawnAvailableAt = match.Now;
                    Assert.True(match.RequestRespawn(victim, 1, match.Now + 0.01f).IsAccepted);
                    // Firing ends spawn protection - the victim shoots back instead of
                    // standing in the open, which is what the test is modelling.
                    match.NotifyShotFired(victim, match.Now + 0.02f);
                }

                KillRegistrationResult result = match.RegisterKill(killer, victim, "vk74", HitZone.Thorax,
                    18f, match.Now + 1f);
                Assert.True(result.IsAccepted, $"kill {i} rejected: {result.Reason}");
                Assert.Equal(i, result.KillerKills);

                if (i < KillTarget)
                {
                    Assert.False(result.MatchEnded);
                    Assert.Equal(MatchPhase.Live, match.Phase);
                }
                else
                {
                    Assert.True(result.MatchEnded);
                    Assert.Equal(MatchEndReason.KillTargetReached, result.EndReason);
                    Assert.Equal(killer, result.WinnerId);
                    Assert.Equal(MatchPhase.Finished, match.Phase);
                }
            }

            MatchResults results = match.BuildResults();
            Assert.Equal(killer, results.WinnerId);
            Assert.Equal(KillTarget, results.WinnerKills);
            Assert.Equal(MatchEndReason.KillTargetReached, results.Reason);
            Assert.Equal(KillTarget, results.Standings[0].Kills);
            Assert.Equal(0, results.Standings[0].KillsToObjective);
        }

        [Fact]
        public void FortyNineKills_DoesNotWinTheMatch()
        {
            FfaMatchController match = CreateLiveMatch(2, out List<ulong> ids);
            for (int i = 0; i < KillTarget - 1; i++)
            {
                PlayerMatchState victim = match.GetPlayer(ids[1]);
                if (!victim.IsAlive)
                {
                    victim.RespawnAvailableAt = match.Now;
                    match.RequestRespawn(ids[1], 1, match.Now + 0.01f);
                    match.NotifyShotFired(ids[1], match.Now + 0.02f);
                }

                match.RegisterKill(ids[0], ids[1], "vk74", HitZone.Head, 12f, match.Now + 1f);
            }

            Assert.Equal(KillTarget - 1, match.GetPlayer(ids[0]).Kills);
            Assert.Equal(MatchPhase.Live, match.Phase);
            Assert.Equal(0UL, match.WinnerId);
            Assert.Equal(1, match.BuildScoreboard()[0].KillsToObjective);
        }

        [Fact]
        public void Kills_AreOnlyEverRegisteredByTheServerAgainstLivePlayers()
        {
            FfaMatchController match = CreateLiveMatch(3, out List<ulong> ids);

            // Not live yet -> rejected.
            MatchRulesConfig config = MatchRulesConfig.DefaultFreeForAll();
            FfaMatchController lobby = new FfaMatchController(config);
            lobby.TryJoin(ids[0], "A", false, 0f);
            lobby.TryJoin(ids[1], "B", false, 0f);
            KillRegistrationResult tooEarly = lobby.RegisterKill(ids[0], ids[1], "vk74", HitZone.Thorax, 10f, 1f);
            Assert.Equal(ScoreActionOutcome.RejectedPhase, tooEarly.Outcome);

            // Killing an already dead player does not score.
            Assert.True(match.RegisterKill(ids[0], ids[1], "vk74", HitZone.Thorax, 10f, match.Now + 1f).IsAccepted);
            KillRegistrationResult secondTime = match.RegisterKill(ids[0], ids[1], "vk74", HitZone.Thorax, 10f,
                match.Now + 2f);
            Assert.Equal(ScoreActionOutcome.VictimAlreadyDead, secondTime.Outcome);
            Assert.Equal(1, match.GetPlayer(ids[0]).Kills);

            // Unknown participants cannot be used to invent score.
            KillRegistrationResult ghost = match.RegisterKill(777UL, ids[2], "vk74", HitZone.Thorax, 10f,
                match.Now + 3f);
            Assert.Equal(ScoreActionOutcome.UnknownKiller, ghost.Outcome);
        }

        [Fact]
        public void Suicide_IncrementsDeathsButCreditsNobody()
        {
            FfaMatchController match = CreateLiveMatch(2, out List<ulong> ids);
            KillRegistrationResult result = match.RegisterKill(ids[0], ids[0], "frag_f1", HitZone.Pelvis, 0f,
                match.Now + 1f);
            Assert.Equal(ScoreActionOutcome.Suicide, result.Outcome);
            Assert.Equal(0, match.GetPlayer(ids[0]).Kills);
            Assert.Equal(1, match.GetPlayer(ids[0]).Deaths);
            Assert.Equal(MatchPhase.Live, match.Phase);
        }

        [Fact]
        public void Respawn_IsDelayedByTheConfiguredTimeAndCannotBeSkipped()
        {
            FfaMatchController match = CreateLiveMatch(2, out List<ulong> ids);
            float now = match.Now;
            Assert.True(match.RegisterKill(ids[0], ids[1], "vk74", HitZone.Thorax, 10f, now + 1f).IsAccepted);

            PlayerMatchState victim = match.GetPlayer(ids[1]);
            Assert.False(victim.IsAlive);
            Assert.Equal(now + 1f + match.Config.RespawnDelaySeconds, victim.RespawnAvailableAt, 3);

            RespawnResult early = match.RequestRespawn(ids[1], 0, now + 2f);
            Assert.False(early.IsAccepted);
            Assert.Equal(RespawnOutcome.TooEarly, early.Outcome);

            float readyAt = now + 1f + match.Config.RespawnDelaySeconds + 0.01f;
            Assert.True(match.IsRespawnReady(ids[1], readyAt));
            RespawnResult ok = match.RequestRespawn(ids[1], 3, readyAt);
            Assert.True(ok.IsAccepted);
            Assert.Equal(3, ok.SpawnPointIndex);
            Assert.True(victim.IsAlive);
            Assert.True(victim.IsInSpawnProtection(readyAt));
            Assert.False(victim.IsInSpawnProtection(readyAt + match.Config.SpawnProtectionSeconds + 0.1f));
        }

        [Fact]
        public void SpawnProtection_BlocksDamageUntilItExpiresOrThePlayerFires()
        {
            FfaMatchController match = CreateLiveMatch(2, out List<ulong> ids);
            float now = match.Now;
            match.RegisterKill(ids[0], ids[1], "vk74", HitZone.Thorax, 10f, now + 1f);
            float respawnAt = now + 1f + match.Config.RespawnDelaySeconds + 0.01f;
            match.RequestRespawn(ids[1], 2, respawnAt);

            DamageRegistrationResult blocked = match.RegisterDamage(ids[0], ids[1], 25f, HitZone.Thorax, respawnAt);
            Assert.False(blocked.IsAccepted);
            Assert.Equal(ScoreActionOutcome.SpawnProtection, blocked.Outcome);

            // Shooting ends the protection immediately.
            match.NotifyShotFired(ids[1], respawnAt + 0.1f);
            DamageRegistrationResult allowed = match.RegisterDamage(ids[0], ids[1], 25f, HitZone.Thorax,
                respawnAt + 0.2f);
            Assert.True(allowed.IsAccepted);
            Assert.Equal(25f, allowed.Damage, 3);
        }

        [Fact]
        public void Disconnect_KeepsTheSlotAndScoreDuringTheGraceWindow()
        {
            FfaMatchController match = CreateLiveMatch(3, out List<ulong> ids);
            ulong leaver = ids[2];
            match.RegisterKill(leaver, ids[0], "vk74", HitZone.Thorax, 20f, match.Now + 1f);
            Assert.Equal(1, match.GetPlayer(leaver).Kills);

            Assert.True(match.MarkDisconnected(leaver, match.Now + 2f));
            Assert.False(match.GetPlayer(leaver).IsConnected);
            Assert.Equal(3, match.Roster.Count);

            // Somebody else cannot take the slot while the grace window is open:
            // 20 slots, three of them held (one by the disconnected operator).
            Assert.Equal(17, match.FreeSlots);

            Assert.True(match.TryReconnect(leaver, match.GetPlayer(leaver).SessionToken, match.Now + 10f));
            Assert.True(match.GetPlayer(leaver).IsConnected);
            Assert.Equal(1, match.GetPlayer(leaver).Kills);
        }

        [Fact]
        public void Reconnect_WithAWrongToken_IsRefused()
        {
            FfaMatchController match = CreateLiveMatch(2, out List<ulong> ids);
            match.MarkDisconnected(ids[1], match.Now + 1f);
            Assert.False(match.TryReconnect(ids[1], "forged-token", match.Now + 2f));
            Assert.False(match.GetPlayer(ids[1]).IsConnected);
        }

        [Fact]
        public void GraceExpiry_FreesTheSlotForAnotherOperator()
        {
            FfaMatchController match = CreateLiveMatch(2, out List<ulong> ids);
            match.MarkDisconnected(ids[1], match.Now);
            match.Tick(match.Now + match.Config.ReconnectGraceSeconds + 1f);
            // The slot came back: 20 slots, one operator still in the match.
            Assert.Equal(19, match.FreeSlots);
            Assert.Null(match.GetPlayer(ids[1]));
            Assert.True(match.TryJoin(4242UL, "REPLACEMENT", false, match.Now + 200f).IsAccepted);
        }

        [Fact]
        public void Leaving_PermanentlyReleasesTheSlot()
        {
            FfaMatchController match = CreateLiveMatch(2, out List<ulong> ids);
            Assert.True(match.TryLeave(ids[0], match.Now));
            Assert.Equal(1, match.Roster.Count);
            Assert.Equal(19, match.FreeSlots);
        }

        [Fact]
        public void Rematch_ResetsScoreAndReopensTheLoadoutScreen()
        {
            FfaMatchController match = CreateLiveMatch(2, out List<ulong> ids);
            for (int i = 0; i < KillTarget; i++)
            {
                PlayerMatchState victim = match.GetPlayer(ids[1]);
                if (!victim.IsAlive)
                {
                    victim.RespawnAvailableAt = match.Now;
                    match.RequestRespawn(ids[1], 1, match.Now + 0.01f);
                    match.NotifyShotFired(ids[1], match.Now + 0.02f);
                }

                match.RegisterKill(ids[0], ids[1], "vk74", HitZone.Thorax, 10f, match.Now + 1f);
            }

            Assert.Equal(MatchPhase.Finished, match.Phase);
            MatchResults before = match.BuildResults();
            Assert.Equal(KillTarget, before.Standings[0].Kills);

            Assert.True(match.TryRematch(match.Now + 30f));
            Assert.Equal(MatchPhase.Loadout, match.Phase);
            Assert.Equal(0, match.GetPlayer(ids[0]).Kills);
            Assert.Equal(0, match.GetPlayer(ids[1]).Kills);
            Assert.Equal(0UL, match.WinnerId);
            Assert.False(match.GetPlayer(ids[0]).LoadoutLocked);
            Assert.Equal(1, match.RematchCount);

            // Kits may be chosen again for the new match.
            Assert.True(match.SubmitLoadout(ids[0], TestLoadouts.ValidSmgLoadout(),
                TestData.Database.Definitions, match.Now + 31f).Accepted);
        }

        [Fact]
        public void Bots_CannotJoinANetworkFreeForAllMatch()
        {
            MatchRulesConfig config = MatchRulesConfig.DefaultFreeForAll();
            Assert.False(config.AllowBots);
            FfaMatchController match = new FfaMatchController(config);
            JoinResult botJoin = match.TryJoin(1UL, "BOT", true, 0f);
            Assert.False(botJoin.IsAccepted);
            Assert.Equal(JoinOutcome.BotsNotAllowed, botJoin.Outcome);
        }

        [Fact]
        public void Bots_AreAllowedInThePracticeRangeAndStayFlagged()
        {
            MatchRulesConfig config = MatchRulesConfig.PracticeRange();
            Assert.True(config.AllowBots);
            FfaMatchController match = new FfaMatchController(config);
            Assert.True(match.TryJoin(1UL, "PLAYER", false, 0f).IsAccepted);
            Assert.True(match.TryJoin(2UL, "RANGE-BOT", true, 0f).IsAccepted);
            Assert.True(match.GetPlayer(2UL).IsBot);
            match.BeginLoadoutPhase(0f);
            match.SubmitLoadout(1UL, TestLoadouts.ValidRifleLoadout(), TestData.Database.Definitions, 1f);
            match.SubmitLoadout(2UL, TestLoadouts.ValidRifleLoadout(), TestData.Database.Definitions, 1f);
            match.BeginCountdown(1f);
            match.BeginLive(10f);
            List<ScoreboardRow> board = match.BuildScoreboard();
            bool anyBotFlagged = false;
            foreach (ScoreboardRow row in board)
            {
                if (row.IsBot)
                {
                    anyBotFlagged = true;
                }
            }

            Assert.True(anyBotFlagged);
        }

        [Fact]
        public void TimeLimit_EndsTheMatchAndCrownTheTopScorer()
        {
            MatchRulesConfig config = MatchRulesConfig.DefaultFreeForAll();
            config.TimeLimitSeconds = 60f;
            FfaMatchController match = new FfaMatchController(config, new DeterministicRandom(7));
            match.TryJoin(1UL, "A", false, 0f);
            match.TryJoin(2UL, "B", false, 0f);
            match.BeginLoadoutPhase(0f);
            match.SubmitLoadout(1UL, TestLoadouts.ValidRifleLoadout(), TestData.Database.Definitions, 1f);
            match.SubmitLoadout(2UL, TestLoadouts.ValidRifleLoadout(), TestData.Database.Definitions, 1f);
            match.BeginCountdown(1f);
            match.BeginLive(10f);
            match.InitialSpawn(_ => 0, 10f);

            match.RegisterKill(2UL, 1UL, "vk74", HitZone.Thorax, 15f, 20f);
            match.GetPlayer(1UL).RespawnAvailableAt = 25f;
            match.RequestRespawn(1UL, 0, 26f);

            match.Tick(80f);
            Assert.Equal(MatchPhase.Finished, match.Phase);
            Assert.Equal(MatchEndReason.TimeLimitReached, match.EndReason);
            Assert.Equal(2UL, match.WinnerId);
        }

        [Fact]
        public void ImpossibleKillDistance_IsRejected()
        {
            FfaMatchController match = CreateLiveMatch(2, out List<ulong> ids);
            KillRegistrationResult result = match.RegisterKill(ids[0], ids[1], "sr338", HitZone.Head,
                FfaMatchController.MaxPlausibleKillDistanceM + 1f, match.Now + 1f);
            Assert.Equal(ScoreActionOutcome.ImpossibleDistance, result.Outcome);
            Assert.Equal(0, match.GetPlayer(ids[0]).Kills);
        }

        [Fact]
        public void KillRateLimit_StopsImpossibleScoreBursts()
        {
            FfaMatchController match = CreateLiveMatch(2, out List<ulong> ids);
            float now = match.Now;
            int accepted = 0;
            for (int i = 0; i < 40; i++)
            {
                PlayerMatchState victim = match.GetPlayer(ids[1]);
                if (!victim.IsAlive)
                {
                    victim.RespawnAvailableAt = now;
                    match.RequestRespawn(ids[1], 0, now + 0.001f);
                    match.NotifyShotFired(ids[1], now + 0.002f);
                }

                // All within the same tenth of a second: physically impossible.
                KillRegistrationResult result = match.RegisterKill(ids[0], ids[1], "vs9", HitZone.Thorax, 5f,
                    now + 0.001f);
                if (result.IsAccepted)
                {
                    accepted++;
                }
                else
                {
                    Assert.Equal(ScoreActionOutcome.RateLimited, result.Outcome);
                }
            }

            Assert.Equal(1, accepted);
        }

        [Fact]
        public void EmptyMatch_AbortsInsteadOfRunningForever()
        {
            MatchRulesConfig config = MatchRulesConfig.DefaultFreeForAll();
            FfaMatchController match = new FfaMatchController(config);
            match.TryJoin(1UL, "A", false, 0f);
            match.BeginLoadoutPhase(0f);
            match.SubmitLoadout(1UL, TestLoadouts.ValidRifleLoadout(), TestData.Database.Definitions, 1f);
            match.BeginCountdown(1f);
            match.BeginLive(10f);
            match.TryLeave(1UL, 11f);
            match.Tick(12f);
            Assert.Equal(MatchPhase.Aborted, match.Phase);
            Assert.Equal(MatchEndReason.AllPlayersLeft, match.EndReason);
        }
    }

    /// <summary>Ready made, always valid loadout selections used across the test suite.</summary>
    public static class TestLoadouts
    {
        public static LoadoutSelection ValidRifleLoadout() => new LoadoutSelection
        {
            PrimaryWeaponId = "vk74",
            PrimarySight = SightKind.IronSight,
            PrimaryMagazines = new List<MagazinePlanEntry>
            {
                new MagazinePlanEntry("mag_545_30_ps", 4),
                new MagazinePlanEntry("mag_545_30_bp", 1)
            },
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

        public static LoadoutSelection ValidSmgLoadout() => new LoadoutSelection
        {
            PrimaryWeaponId = "vs9",
            PrimarySight = SightKind.RedDot,
            PrimaryMagazines = new List<MagazinePlanEntry>
            {
                new MagazinePlanEntry("mag_9x19_30_pst", 5)
            },
            SecondaryWeaponId = "p9",
            SecondarySight = SightKind.IronSight,
            SecondaryMagazines = new List<MagazinePlanEntry>
            {
                new MagazinePlanEntry("mag_9x19_15_pst", 2)
            },
            GrenadeId = "smoke_rdg",
            GrenadeCount = 1,
            ArmorKitId = "light"
        };
    }
}
