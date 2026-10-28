using System.Collections.Generic;
using Kordon.Core.Ammo;
using Kordon.Core.AntiCheat;
using Kordon.Core.Ballistics;
using Kordon.Core.Combat;
using Kordon.Core.Data;
using Kordon.Core.Movement;
using Kordon.Core.Rules;
using Kordon.Core.Utility;
using Xunit;

namespace Kordon.Core.Tests
{
    /// <summary>
    /// Basic, transparent server side anti-cheat: impossible fire rate, impossible
    /// movement speed, teleports, forged score, illegal state transitions, impossible
    /// view angles and resupply exploits.  Everything is deterministic and logged.
    /// </summary>
    public class AntiCheatTests
    {
        private static readonly DefinitionLibrary Library = TestData.Database.Definitions;

        [Fact]
        public void ImpossibleFireRate_IsRejected()
        {
            WeaponDefinition rifle = Library.GetWeapon("vk74");
            Assert.Equal(0.1f, rifle.SecondsPerShot, 4);

            Assert.True(ServerValidators.ValidateFireRate(rifle, FireMode.Auto, 0.1f).Allowed);
            Assert.True(ServerValidators.ValidateFireRate(rifle, FireMode.Auto, 1f).Allowed);

            ValidationVerdict cheat = ServerValidators.ValidateFireRate(rifle, FireMode.Auto, 0.02f);
            Assert.False(cheat.Allowed);
            Assert.Equal(ViolationKind.ImpossibleFireRate, cheat.Kind);
            Assert.True(cheat.Severity > 0f);
            Assert.Contains("weapon minimum", cheat.Detail);

            // Semi automatic fire may never beat the cyclic rate either.
            Assert.False(ServerValidators.ValidateFireRate(rifle, FireMode.Semi, 0.05f).Allowed);
            Assert.True(ServerValidators.ValidateFireRate(rifle, FireMode.Semi, 0.095f).Allowed);

            // Every weapon in the roster is protected by its own rate.
            foreach (WeaponDefinition weapon in Library.Weapons.Values)
            {
                Assert.False(ServerValidators
                    .ValidateFireRate(weapon, weapon.DefaultFireMode, weapon.SecondsPerShot * 0.25f).Allowed);
                Assert.True(ServerValidators
                    .ValidateFireRate(weapon, weapon.DefaultFireMode, weapon.SecondsPerShot).Allowed);
            }

            Assert.True(ServerValidators.ValidateFireRate(null, FireMode.Auto, 0f).Allowed);
        }

        [Fact]
        public void ImpossibleMovementSpeedAndTeleports_AreRejected()
        {
            float sprint = MovementProfile.SprintSpeed;
            float limit = sprint * ServerValidators.SpeedTolerance;

            Assert.True(ServerValidators.ValidateMovement(limit * 0.5f, 1f, sprint, Stance.Stand, true).Allowed);
            Assert.False(ServerValidators.ValidateMovement(limit * 1.5f, 1f, sprint, Stance.Stand, true).Allowed);

            ValidationVerdict speed = ServerValidators.ValidateMovement(6f, 1f, sprint, Stance.Stand, true);
            Assert.False(speed.Allowed);
            Assert.Equal(ViolationKind.ImpossibleMovementSpeed, speed.Kind);

            ValidationVerdict teleport = ServerValidators.ValidateMovement(40f, 1f, sprint, Stance.Stand, false);
            Assert.False(teleport.Allowed);
            Assert.Equal(ViolationKind.Teleport, teleport.Kind);
            Assert.Equal(3f, teleport.Severity, 3);

            ValidationVerdict clock = ServerValidators.ValidateMovement(1f, 0f, sprint, Stance.Stand, false);
            Assert.False(clock.Allowed);
            Assert.Equal(ViolationKind.ClockManipulation, clock.Kind);

            // A crouching operator is held to the crouch speed, not the sprint speed.
            Assert.False(ServerValidators
                .ValidateMovement(MovementProfile.CrouchSpeed * 2f, 1f, MovementProfile.CrouchSpeed,
                    Stance.Crouch, false).Allowed);
            Assert.True(ServerValidators
                .ValidateMovement(MovementProfile.CrouchSpeed, 1f, MovementProfile.CrouchSpeed,
                    Stance.Crouch, false).Allowed);
        }

        [Fact]
        public void ClientScoreSubmission_IsAlwaysRejected()
        {
            ValidationVerdict verdict = ServerValidators.ValidateScoreSubmission(1234UL);
            Assert.False(verdict.Allowed);
            Assert.Equal(ViolationKind.ScoreSpoofing, verdict.Kind);
            Assert.Equal(3f, verdict.Severity, 3);
            Assert.Contains("1234", verdict.Detail);
        }

        [Fact]
        public void IllegalStateTransitions_AreRejected()
        {
            string[][] illegal =
            {
                new[] { "dead", "firing" },
                new[] { "dead", "reloading" },
                new[] { "dead", "inspecting" },
                new[] { "inspecting", "firing" },
                new[] { "reloading", "firing" },
                new[] { "fumbling", "firing" },
                new[] { "spawn-protected", "damaging-others" },
                new[] { "sealed-loadout", "changing-loadout" }
            };

            foreach (string[] pair in illegal)
            {
                ValidationVerdict verdict = ServerValidators.ValidateStateTransition(pair[0], pair[1]);
                Assert.False(verdict.Allowed, $"{pair[0]}->{pair[1]} should be illegal");
                Assert.Equal(ViolationKind.InvalidState, verdict.Kind);
            }

            Assert.True(ServerValidators.ValidateStateTransition("idle", "firing").Allowed);
            Assert.True(ServerValidators.ValidateStateTransition("reloading", "idle").Allowed);
            Assert.True(ServerValidators.ValidateStateTransition("inspecting", "returning").Allowed);
            Assert.True(ServerValidators.ValidateStateTransition("dead", "respawning").Allowed);
            Assert.True(ServerValidators.ValidateStateTransition(null, "firing").Allowed);
        }

        [Fact]
        public void ImpossibleViewAngles_AreRejected()
        {
            Assert.True(ServerValidators.ValidateViewAngles(0f).Allowed);
            Assert.True(ServerValidators.ValidateViewAngles(89f).Allowed);
            Assert.True(ServerValidators.ValidateViewAngles(-90f).Allowed);
            Assert.False(ServerValidators.ValidateViewAngles(120f).Allowed);
            Assert.False(ServerValidators.ValidateViewAngles(-179f).Allowed);
            Assert.False(ServerValidators.ValidateViewAngles(float.NaN).Allowed);
            Assert.False(ServerValidators.ValidateViewAngles(float.PositiveInfinity).Allowed);
            Assert.Equal(ViolationKind.ImpossibleViewAngle,
                ServerValidators.ValidateViewAngles(400f).Kind);
        }

        [Fact]
        public void ResupplyExploits_AreRejected()
        {
            Assert.False(ServerValidators.ValidateResupplyRequest(ResupplyRule.OnRespawnOnly, true).Allowed);
            Assert.False(ServerValidators.ValidateResupplyRequest(ResupplyRule.NeverWithinRound, true).Allowed);
            Assert.True(ServerValidators.ValidateResupplyRequest(ResupplyRule.OnRespawnOnly, false).Allowed);
            Assert.False(ServerValidators.ValidateResupplyRequest(ResupplyRule.NeverWithinRound, false).Allowed);
            Assert.Equal(ViolationKind.ResupplyExploit,
                ServerValidators.ValidateResupplyRequest(ResupplyRule.OnRespawnOnly, true).Kind);
        }

        [Fact]
        public void RateLimiter_StopsRpcFlooding()
        {
            RateLimiter limiter = new RateLimiter();
            Assert.Equal(12, limiter.DefaultMaxPerWindow);
            Assert.Equal(1f, limiter.WindowSeconds, 3);

            for (int i = 0; i < 12; i++)
            {
                Assert.True(limiter.Allow("client-1", 10f + i * 0.01f));
            }

            Assert.False(limiter.Allow("client-1", 10.2f));
            Assert.Equal(12, limiter.Count("client-1"));

            // Another client is unaffected.
            Assert.True(limiter.Allow("client-2", 10.3f));

            // Once the window slides, the flood can resume (but is still counted).
            Assert.True(limiter.Allow("client-1", 12f));

            limiter.Reset("client-1");
            Assert.Equal(0, limiter.Count("client-1"));
            Assert.Equal(1, limiter.Count("client-2"));
            limiter.Reset();
            Assert.Equal(0, limiter.Count("client-2"));

            RateLimiter custom = new RateLimiter();
            Assert.True(custom.Allow("respawn", 0f, 2, 5f));
            Assert.True(custom.Allow("respawn", 1f, 2, 5f));
            Assert.False(custom.Allow("respawn", 2f, 2, 5f));
            Assert.True(custom.Allow("respawn", 6f, 2, 5f));
        }

        [Fact]
        public void ViolationLog_AccumulatesSeverityAndDecidesKicks()
        {
            ViolationLog log = new ViolationLog();
            Assert.Equal(6f, ViolationLog.KickSeverity, 3);
            Assert.Equal(0f, log.SeverityFor(9UL), 3);
            Assert.False(log.ShouldKick(9UL));

            log.Record(new Violation(ViolationKind.ImpossibleFireRate, 9UL, "0.01s", 1f, 2f));
            Assert.Equal(2f, log.SeverityFor(9UL), 3);
            Assert.Equal(1, log.CountFor(9UL));
            Assert.False(log.ShouldKick(9UL));

            log.Record(new Violation(ViolationKind.ImpossibleMovementSpeed, 9UL, "9 m/s", 2f, 3f));
            log.Record(new Violation(ViolationKind.ScoreSpoofing, 9UL, "score", 3f, 3f));
            Assert.Equal(8f, log.SeverityFor(9UL), 3);
            Assert.True(log.ShouldKick(9UL));
            Assert.Equal(0f, log.SeverityFor(10UL), 3);
            Assert.False(log.ShouldKick(10UL));

            for (int i = 0; i < ViolationLog.MaxEntries + 100; i++)
            {
                log.Record(new Violation(ViolationKind.RpcFlood, 11UL, "flood", i, 0.1f));
            }

            Assert.Equal(ViolationLog.MaxEntries, log.Entries.Count);
            log.Clear();
            Assert.Empty(log.Entries);
            Assert.Equal(0f, log.SeverityFor(9UL), 3);
        }

        [Fact]
        public void ServerScore_StaysAuthoritativeAgainstAForgedClient()
        {
            // The controller only ever accepts kills it resolved itself.
            MatchRulesConfig config = MatchRulesConfig.DefaultFreeForAll();
            FfaMatchController match = new FfaMatchController(config, new DeterministicRandom(5));
            match.TryJoin(1UL, "HONEST", false, 0f);
            match.TryJoin(2UL, "CHEATER", false, 0f);
            match.BeginLoadoutPhase(0f);
            match.SubmitLoadout(1UL, TestLoadouts.ValidRifleLoadout(), Library, 1f);
            match.SubmitLoadout(2UL, TestLoadouts.ValidRifleLoadout(), Library, 1f);
            match.BeginCountdown(1f);
            match.BeginLive(10f);
            match.InitialSpawn(_ => 0, 10f);

            ViolationLog log = new ViolationLog();

            // 1. The cheater tries to hand the server a score.
            ValidationVerdict spoof = ServerValidators.ValidateScoreSubmission(2UL);
            Assert.False(spoof.Allowed);
            log.Record(new Violation(spoof.Kind, 2UL, spoof.Detail, 11f, spoof.Severity));

            // 2. The cheater claims an impossible fire rate.
            WeaponDefinition rifle = Library.GetWeapon("vs9");
            ValidationVerdict rate = ServerValidators.ValidateFireRate(rifle, FireMode.Auto, 0.005f);
            Assert.False(rate.Allowed);
            log.Record(new Violation(rate.Kind, 2UL, rate.Detail, 11f, rate.Severity));

            // 3. The cheater claims a teleport.
            ValidationVerdict teleport = ServerValidators.ValidateMovement(50f, 0.1f,
                MovementProfile.SprintSpeed, Stance.Stand, true);
            Assert.False(teleport.Allowed);
            log.Record(new Violation(teleport.Kind, 2UL, teleport.Detail, 11f, teleport.Severity));

            Assert.True(log.ShouldKick(2UL));
            Assert.False(log.ShouldKick(1UL));

            // Meanwhile the honest player scores through the real damage pipeline.
            DamageRegistrationResult damage = match.RegisterDamage(1UL, 2UL, 50f, HitZone.Thorax, 12f);
            Assert.True(damage.IsAccepted);
            Assert.False(damage.VictimDied);
            Assert.Equal(50f, match.GetPlayer(1UL).DamageDealt, 1);

            KillRegistrationResult kill = match.RegisterKill(1UL, 2UL, "vk74", HitZone.Thorax, 42f, 13f);
            Assert.True(kill.IsAccepted);
            Assert.Equal(1, match.GetPlayer(1UL).Kills);
            Assert.Equal(1, match.GetPlayer(1UL).DamageDealt / 50f, 3);
            Assert.Equal(0, match.GetPlayer(2UL).Kills);
            Assert.False(match.GetPlayer(2UL).IsAlive);

            // The server decides when somebody dies: a second "kill" on the same body
            // is refused instead of being counted twice.
            Assert.Equal(ScoreActionOutcome.VictimAlreadyDead,
                match.RegisterKill(1UL, 2UL, "vk74", HitZone.Thorax, 42f, 14f).Outcome);

            // A lethal hit resolves the death inside the damage registration itself.
            DamageRegistrationResult lethal = match.RegisterDamage(2UL, 1UL, 140f, HitZone.Head, 15f);
            Assert.Equal(ScoreActionOutcome.ActorDisconnected, lethal.Outcome);
            DamageRegistrationResult honestLethal = match.RegisterDamage(1UL, 2UL, 140f, HitZone.Thorax, 15f);
            Assert.Equal(ScoreActionOutcome.VictimAlreadyDead, honestLethal.Outcome);
        }

        [Fact]
        public void AntiCheatThresholds_AreTheSameOnEveryServer()
        {
            Assert.Equal(1.18f, ServerValidators.SpeedTolerance, 3);
            Assert.Equal(6.5f, ServerValidators.TeleportDistanceM, 3);
            Assert.Equal(91f, ServerValidators.MaxAbsPitchDeg, 3);
            Assert.Equal(24f, MatchRulesConfig.DefaultFreeForAll().MaxKillsPerMinute, 3);
            Assert.Equal(0.55f, MatchRulesConfig.DefaultFreeForAll().MinSecondsBetweenKills, 3);
            Assert.Equal(900f, FfaMatchController.MaxPlausibleKillDistanceM, 3);
        }

        [Fact]
        public void KillDistanceAndRateChecks_CoverTheWholeRoster()
        {
            MatchRulesConfig config = MatchRulesConfig.DefaultFreeForAll();
            FfaMatchController match = new FfaMatchController(config, new DeterministicRandom(6));
            List<ulong> ids = new List<ulong>();
            for (int i = 0; i < 6; i++)
            {
                ulong id = (ulong)(100 + i);
                match.TryJoin(id, $"OP{i}", false, 0f);
                ids.Add(id);
            }

            match.BeginLoadoutPhase(0f);
            foreach (ulong id in ids)
            {
                match.SubmitLoadout(id, TestLoadouts.ValidRifleLoadout(), Library, 1f);
            }

            match.BeginCountdown(1f);
            match.BeginLive(10f);
            match.InitialSpawn(_ => 0, 10f);

            // A disconnected actor cannot score.
            match.MarkDisconnected(ids[1], 11f);
            KillRegistrationResult disconnected = match.RegisterKill(ids[1], ids[2], "vk74", HitZone.Thorax, 10f, 12f);
            Assert.Equal(ScoreActionOutcome.ActorDisconnected, disconnected.Outcome);

            // An unknown victim cannot be used to farm score.
            KillRegistrationResult ghost = match.RegisterKill(ids[0], 999UL, "vk74", HitZone.Thorax, 10f, 13f);
            Assert.Equal(ScoreActionOutcome.UnknownVictim, ghost.Outcome);

            // Negative and absurd distances are both refused.
            Assert.Equal(ScoreActionOutcome.ImpossibleDistance,
                match.RegisterKill(ids[0], ids[3], "sr338", HitZone.Head, -5f, 14f).Outcome);
            Assert.Equal(ScoreActionOutcome.ImpossibleDistance,
                match.RegisterKill(ids[0], ids[3], "sr338", HitZone.Head, 5000f, 15f).Outcome);

            Assert.Equal(0, match.GetPlayer(ids[0]).Kills);
            Assert.NotEmpty(match.Rejections);
        }
    }
}
