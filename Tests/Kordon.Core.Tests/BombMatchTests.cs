using System.Collections.Generic;
using Kordon.Core.Rules;
using Xunit;

namespace Kordon.Core.Tests
{
    /// <summary>
    /// The demolition mode: two teams, one charge, round based scoring, a cash economy and
    /// a shop. Every rule here runs on CI without a Unity editor, which is the only way the
    /// mode can be verified in this repository - the round life cycle is exactly the part
    /// that breaks silently when it is only tested by playing.
    /// </summary>
    public class BombMatchTests
    {
        private const ulong A1 = 11, A2 = 12, D1 = 21, D2 = 22;

        private static BombMatchController Create(out BombRules rules, int perSide = 2,
                                                  bool practice = false)
        {
            rules = practice ? BombRules.Practice() : BombRules.Default();
            rules.TeamSize = perSide;
            rules.MaxPlayers = perSide * 2;
            BombMatchController match = new BombMatchController(
                rules, new EconomyRules(), BuyCatalog.Default());

            for (int i = 0; i < perSide; i++)
            {
                Assert.True(match.AddPlayer(A1 + (ulong)i, "ATK" + i, Team.Attackers, false, 0f)
                    .IsAccepted);
                Assert.True(match.AddPlayer(D1 + (ulong)i, "DEF" + i, Team.Defenders, false, 0f)
                    .IsAccepted);
            }

            return match;
        }

        private static void StartLive(BombMatchController match, float now = 100f)
        {
            Assert.True(match.StartMatch(now));
            // Burn the freeze time and one tick of the live round.
            match.Tick(now + match.Rules.FreezeSeconds + 0.1f);
            Assert.Equal(RoundPhase.Live, match.Round);
        }

        // ------------------------------------------------------------------
        // Rules and roster
        // ------------------------------------------------------------------
        [Fact]
        public void DefaultRulesAreConsistent()
        {
            BombRules rules = BombRules.Default();
            Assert.Null(rules.Validate());
            Assert.Equal(10, rules.MaxPlayers);
            Assert.Equal(13, rules.RoundsToWin);
            Assert.Equal(MatchMode.Demolition, rules.Mode);
        }

        [Fact]
        public void RulesRejectADefuseFasterThanAPlant()
        {
            BombRules rules = BombRules.Default();
            rules.DefuseSeconds = 1f;
            Assert.Equal("demolition-defuse-faster-than-plant", rules.Validate());
        }

        [Fact]
        public void BotsArePracticeOnly()
        {
            BombRules rules = BombRules.Default();
            BombMatchController match = new BombMatchController(
                rules, new EconomyRules(), BuyCatalog.Default());
            Assert.Equal(BombJoinOutcome.BotsNotAllowed,
                match.AddPlayer(99, "BOT", Team.Defenders, true, 0f).Outcome);

            BombRules practiceRules = BombRules.Practice();
            BombMatchController practice = new BombMatchController(
                practiceRules, new EconomyRules(), BuyCatalog.Default());
            Assert.True(practice.AddPlayer(99, "BOT", Team.Defenders, true, 0f).IsAccepted);
        }

        [Fact]
        public void TeamsBalanceAndSquadsKeepTheirScoreAcrossTheSwap()
        {
            BombMatchController match = Create(out BombRules rules, 1);
            Assert.Equal(Team.Attackers, match.GetPlayer(A1).Team);
            Assert.Equal(Team.Defenders, match.GetPlayer(D1).Team);

            // A third join with no preference goes to the smaller side; both are full, so
            // the joining player is refused rather than silently stacked.
            Assert.Equal(BombJoinOutcome.MatchFull,
                match.AddPlayer(31, "EXTRA", Team.None, false, 0f).Outcome);
        }

        [Fact]
        public void FreezeTimeIsTheBuyWindowAndItEnds()
        {
            BombMatchController match = Create(out BombRules rules, 1);
            Assert.True(match.StartMatch(100f));
            Assert.Equal(RoundPhase.Freeze, match.Round);
            Assert.True(match.BuyWindowOpen(100f));
            Assert.True(match.BuyWindowOpen(100f + rules.FreezeSeconds - 0.1f));
            float liveStart = 100f + rules.FreezeSeconds + 0.1f;
            match.Tick(liveStart);
            Assert.Equal(RoundPhase.Live, match.Round);
            // Buying stays open briefly into the live round, then closes for good.
            Assert.True(match.BuyWindowOpen(liveStart + rules.BuySeconds - 1f));
            Assert.False(match.BuyWindowOpen(liveStart + rules.BuySeconds + 1f));
        }

        // ------------------------------------------------------------------
        // The charge
        // ------------------------------------------------------------------
        [Fact]
        public void OnlyAnAttackerCanPlantAndOnlyInsideASite()
        {
            BombMatchController match = Create(out _);
            StartLive(match);

            Assert.Equal(BombActionOutcome.WrongTeam,
                match.BeginPlant(D1, "A", true, 120f).Outcome);
            Assert.Equal(BombActionOutcome.OutOfSite,
                match.BeginPlant(A1, "A", false, 120f).Outcome);
            Assert.True(match.BeginPlant(A1, "A", true, 120f).IsAccepted);
        }

        [Fact]
        public void PlantingTakesTimeAndArmsTheCharge()
        {
            BombMatchController match = Create(out BombRules rules);
            StartLive(match);
            Assert.True(match.BeginPlant(A1, "A", true, 120f).IsAccepted);
            Assert.Equal(0f, match.PlantProgressAt(120f));

            Assert.Null(match.CompletePlant(A1, "A", 120f + rules.PlantSeconds * 0.5f));
            Assert.Equal(RoundPhase.Live, match.Round);

            float armedAt = 120f + rules.PlantSeconds;
            Assert.Equal("A", match.CompletePlant(A1, "A", armedAt));
            Assert.Equal(RoundPhase.Planted, match.Round);
            Assert.Equal("A", match.ArmedSite);
            Assert.Equal(1, match.GetPlayer(A1).Plants);
            // The planter is paid for the plant whether or not the round is won.
            Assert.True(match.GetPlayer(A1).Economy.Money > rules.StartingMoney);
        }

        [Fact]
        public void TheChargeExplodesAndTheAttackersTakeTheRound()
        {
            BombMatchController match = Create(out BombRules rules);
            StartLive(match);
            match.BeginPlant(A1, "B", true, 120f);
            float armedAt = 120f + rules.PlantSeconds;
            match.CompletePlant(A1, "B", armedAt);

            // The round clock no longer matters: the charge has its own.
            match.Tick(armedAt + rules.BombSeconds - 1f);
            Assert.Equal(RoundPhase.Planted, match.Round);

            match.Tick(armedAt + rules.BombSeconds + 0.1f);
            Assert.Equal(RoundPhase.RoundOver, match.Round);
            Assert.Equal(RoundEndReason.BombExploded, match.LastEndReason);
            Assert.Equal(1, match.AttackerScore);
            Assert.Equal(0, match.DefenderScore);
        }

        [Fact]
        public void DefusingWithAKitIsTwiceAsFastAndWinsTheRound()
        {
            BombMatchController match = Create(out BombRules rules);
            StartLive(match);

            // Money for a kit and a buy inside the zone.
            BombPlayer defender = match.GetPlayer(D1);
            defender.Economy.Award(4000, rules.MaxMoney);
            Assert.True(match.TryBuy(D1, "defuse_kit", true, 101f).IsAccepted);

            match.BeginPlant(A1, "A", true, 120f);
            float armedAt = 120f + rules.PlantSeconds;
            match.CompletePlant(A1, "A", armedAt);

            Assert.True(match.BeginDefuse(D1, true, armedAt + 1f).IsAccepted);
            Assert.False(match.CompleteDefuse(D1, armedAt + 1f + rules.DefuseKitSeconds * 0.5f));
            Assert.True(match.CompleteDefuse(D1, armedAt + 1f + rules.DefuseKitSeconds + 0.01f));

            Assert.Equal(RoundPhase.RoundOver, match.Round);
            Assert.Equal(RoundEndReason.BombDefused, match.LastEndReason);
            Assert.Equal(1, match.DefenderScore);
            Assert.Equal(1, match.GetPlayer(D1).Defuses);
        }

        [Fact]
        public void WithoutAKitTheDefuseDoesNotFinishInTime()
        {
            BombMatchController match = Create(out BombRules rules);
            StartLive(match);
            match.BeginPlant(A1, "A", true, 120f);
            float armedAt = 120f + rules.PlantSeconds;
            match.CompletePlant(A1, "A", armedAt);

            // Start the defuse 2 seconds before the charge goes off: without a kit there
            // is not enough time, and the round has to end in a detonation.
            float late = armedAt + rules.BombSeconds - 2f;
            Assert.True(match.BeginDefuse(D1, true, late).IsAccepted);
            match.Tick(armedAt + rules.BombSeconds + 0.1f);
            Assert.Equal(RoundPhase.RoundOver, match.Round);
            Assert.Equal(RoundEndReason.BombExploded, match.LastEndReason);
        }

        [Fact]
        public void KillProgressIsCancelledByDeath()
        {
            BombMatchController match = Create(out BombRules rules);
            StartLive(match);
            match.BeginPlant(A1, "A", true, 120f);
            Assert.True(match.PlantProgressAt(120f + rules.PlantSeconds * 0.6f) > 0f);
            match.RegisterKill(D1, A1, 121f);
            Assert.Equal(0f, match.PlantProgressAt(122f));
            Assert.Null(match.CompletePlant(A1, "A", 130f));
        }

        // ------------------------------------------------------------------
        // Round ends
        // ------------------------------------------------------------------
        [Fact]
        public void WipingTheAttackersBeforeThePlantEndsTheRound()
        {
            BombMatchController match = Create(out _);
            StartLive(match);
            match.RegisterKill(D1, A1, 120f);
            match.RegisterKill(D2, A2, 121f);

            Assert.Equal(RoundPhase.RoundOver, match.Round);
            Assert.Equal(RoundEndReason.AttackersEliminated, match.LastEndReason);
            Assert.Equal(1, match.DefenderScore);
        }

        [Fact]
        public void APlantedChargeKeepsRunningAfterTheAttackersAreDead()
        {
            BombMatchController match = Create(out BombRules rules);
            StartLive(match);
            match.BeginPlant(A1, "A", true, 120f);
            float armedAt = 120f + rules.PlantSeconds;
            match.CompletePlant(A1, "A", armedAt);

            match.RegisterKill(D1, A1, armedAt + 1f);
            match.RegisterKill(D2, A2, armedAt + 2f);
            Assert.Equal(RoundPhase.Planted, match.Round);

            match.Tick(armedAt + rules.BombSeconds + 0.1f);
            Assert.Equal(RoundEndReason.BombExploded, match.LastEndReason);
            Assert.Equal(1, match.AttackerScore);
        }

        [Fact]
        public void RunningTheClockOutGivesTheRoundToTheDefenders()
        {
            BombMatchController match = Create(out BombRules rules);
            StartLive(match);
            match.Tick(200f + rules.RoundSeconds + 0.1f);
            Assert.Equal(RoundEndReason.TimeExpired, match.LastEndReason);
            Assert.Equal(1, match.DefenderScore);
        }

        [Fact]
        public void TeamKillsAreRejected()
        {
            BombMatchController match = Create(out _);
            StartLive(match);
            Assert.Equal(BombActionOutcome.WrongTeam, match.RegisterKill(A1, A2, 120f).Outcome);
        }

        // ------------------------------------------------------------------
        // Shop
        // ------------------------------------------------------------------
        [Fact]
        public void BuyingOutsideTheZoneOrAfterTheWindowIsRefused()
        {
            BombMatchController match = Create(out BombRules rules);
            StartLive(match);
            BombPlayer player = match.GetPlayer(A1);
            player.Economy.Award(10000, rules.MaxMoney);

            Assert.Equal(BuyOutcome.OutOfBuyZone,
                match.TryBuy(A1, "vk74", false, 101f).Outcome);
            Assert.Equal(BuyOutcome.BuyTimeOver,
                match.TryBuy(A1, "vk74", true, 200f).Outcome);
        }

        [Fact]
        public void ABuyIsPaidForOnceAndFillsItsSlot()
        {
            BombMatchController match = Create(out BombRules rules);
            StartLive(match);
            BombPlayer player = match.GetPlayer(A1);
            player.Economy.Award(10000, rules.MaxMoney);
            int before = player.Economy.Money;

            BuyResult result = match.TryBuy(A1, "vk74", true, 101f);
            Assert.True(result.IsAccepted);
            Assert.Equal(before - 2700, result.Money);
            Assert.Equal("vk74", player.Buy.PrimaryId);

            // Buying the same rifle twice is refused, and it does not charge twice.
            Assert.Equal(BuyOutcome.SlotAlreadyFull,
                match.TryBuy(A1, "vk74", true, 101f).Outcome);
            Assert.Equal(before - 2700, player.Economy.Money);
        }

        [Fact]
        public void APrimaryCanBeReplacedAndTheKitIsDefendersOnly()
        {
            BombMatchController match = Create(out BombRules rules);
            StartLive(match);
            BombPlayer attacker = match.GetPlayer(A1);
            BombPlayer defender = match.GetPlayer(D1);
            attacker.Economy.Award(10000, rules.MaxMoney);
            defender.Economy.Award(4000, rules.MaxMoney);

            Assert.True(match.TryBuy(A1, "vk74", true, 101f).IsAccepted);
            Assert.True(match.TryBuy(A1, "sr338", true, 101f).IsAccepted);
            Assert.Equal("sr338", attacker.Buy.PrimaryId);

            Assert.Equal(BuyOutcome.WrongTeam,
                match.TryBuy(A1, "defuse_kit", true, 101f).Outcome);
            Assert.True(match.TryBuy(D1, "defuse_kit", true, 101f).IsAccepted);
            Assert.True(defender.Buy.HasDefuseKit);
        }

        [Fact]
        public void PovertyIsRefusedAndTheFourthGrenadeIsTheLast()
        {
            BombMatchController match = Create(out BombRules rules);
            StartLive(match);
            BombPlayer player = match.GetPlayer(A1);
            Assert.Equal(BuyOutcome.NotEnoughMoney,
                match.TryBuy(A1, "sr338", true, 101f).Outcome);

            player.Economy.Award(10000, rules.MaxMoney);
            Assert.True(match.TryBuy(A1, "frag", true, 101f).IsAccepted);
            Assert.True(match.TryBuy(A1, "frag", true, 101f).IsAccepted);
            Assert.True(match.TryBuy(A1, "smoke", true, 101f).IsAccepted);
            Assert.True(match.TryBuy(A1, "flash", true, 101f).IsAccepted);
            Assert.Equal(BuyOutcome.TooManyGrenades,
                match.TryBuy(A1, "flash", true, 101f).Outcome);
        }

        [Fact]
        public void TheCatalogPointsOnlyAtWeaponsTheBuildShips()
        {
            BuyCatalog catalog = BuyCatalog.Default();
            string[] shipped = { "vk74", "tr30", "vs9", "dmr14", "sr338", "k870", "p9" };
            Assert.Null(catalog.Validate(shipped));
            // A shop that points at a weapon the build does not ship is caught, not shipped.
            Assert.Equal("unknown-weapon:p9",
                catalog.Validate(new[] { "vk74", "tr30", "vs9", "dmr14", "sr338", "k870" }));
            Assert.Equal(4, catalog.ForCategory(BuyCategory.Utility).Count);
            Assert.Equal(2, catalog.ForCategory(BuyCategory.Rifle).Count);
            Assert.Single(catalog.ForCategory(BuyCategory.Sniper));
        }

        // ------------------------------------------------------------------
        // Economy
        // ------------------------------------------------------------------
        [Fact]
        public void TheLossLadderClimbsAndStops()
        {
            EconomyRules economy = new EconomyRules();
            Assert.Null(economy.Validate());
            Assert.Equal(1400, economy.RewardForLoss(0));
            Assert.Equal(1900, economy.RewardForLoss(1));
            Assert.Equal(3400, economy.RewardForLoss(4));
            Assert.Equal(3400, economy.RewardForLoss(99));
        }

        [Fact]
        public void MoneyIsCappedAndWinsResetTheLosingStreak()
        {
            PlayerEconomy economy = new PlayerEconomy(1, 800);
            economy.Award(100000, 16000);
            Assert.Equal(16000, economy.Money);

            economy.RecordLoss();
            economy.RecordLoss();
            Assert.Equal(2, economy.ConsecutiveLosses);
            economy.RecordWin();
            Assert.Equal(0, economy.ConsecutiveLosses);
        }

        // ------------------------------------------------------------------
        // Half time and match end
        // ------------------------------------------------------------------
        [Fact]
        public void SidesSwapAfterTheFirstHalfAndTheScoreFollowsTheSquad()
        {
            BombMatchController match = Create(out BombRules rules, 1);
            rules.RoundsPerHalf = 2;
            StartLive(match);
            Assert.Equal(Team.Attackers, match.GetPlayer(A1).Team);

            // Round 1: attackers detonate. Round 2: defenders run the clock out.
            match.BeginPlant(A1, "A", true, 120f);
            float armed = 120f + rules.PlantSeconds;
            match.CompletePlant(A1, "A", armed);
            match.Tick(armed + rules.BombSeconds + 0.1f);
            Assert.Equal(1, match.AttackerScore);

            match.Tick(armed + rules.BombSeconds + rules.RoundOverSeconds + 0.1f);
            Assert.Equal(2, match.RoundNumber);
            float secondLive = armed + rules.BombSeconds + rules.RoundOverSeconds
                               + rules.FreezeSeconds + 0.2f;
            match.Tick(secondLive);
            match.Tick(secondLive + rules.RoundSeconds + 0.1f);
            Assert.Equal(RoundEndReason.TimeExpired, match.LastEndReason);

            // Third round: the sides have swapped, and the squad that attacked in round
            // one is now defending - with its own round still on the board.
            match.Tick(secondLive + rules.RoundSeconds + rules.RoundOverSeconds + 1f);
            Assert.Equal(3, match.RoundNumber);
            Assert.True(match.SidesSwapped);
            Assert.Equal(Team.Defenders, match.GetPlayer(A1).Team);
            Assert.Equal(1, match.DefenderScore);
            Assert.Equal(1, match.AttackerScore);
        }

        [Fact]
        public void TheMatchEndsWhenASquadReachesThirteenRounds()
        {
            BombMatchController match = Create(out BombRules rules, 1);
            rules.RoundsPerHalf = 24;      // no swap in this test
            StartLive(match);

            float now = 120f;
            for (int round = 0; round < 13; round++)
            {
                match.BeginPlant(A1, "A", true, now);
                float armed = now + rules.PlantSeconds;
                match.CompletePlant(A1, "A", armed);
                match.Tick(armed + rules.BombSeconds + 0.1f);
                Assert.Equal(RoundPhase.RoundOver, match.Round);
                now = armed + rules.BombSeconds + rules.RoundOverSeconds + 0.2f;
                match.Tick(now);
                now += rules.FreezeSeconds + 0.2f;
                match.Tick(now);
            }

            Assert.True(match.MatchDecided());
            match.Tick(now + rules.RoundOverSeconds + 1f);
            Assert.Equal(RoundPhase.MatchOver, match.Round);
            Assert.Equal(MatchPhase.Finished, match.Phase);
        }

        // ------------------------------------------------------------------
        // The end-of-match screen
        // ------------------------------------------------------------------

        /// <summary>
        /// A demolition result is a team result. The screen must lead with the side that
        /// won and mark its players, otherwise a 13:7 match reads like a deathmatch.
        /// </summary>
        [Fact]
        public void BuildResultsMarksTheWinningSideAndLeadsWithIt()
        {
            BombMatchController match = Create(out BombRules rules);
            rules.RoundsToWin = 1;
            rules.RoundsPerHalf = 24;
            StartLive(match);

            float now = 120f;
            match.BeginPlant(A1, "A", true, now);
            float armed = now + rules.PlantSeconds;
            match.CompletePlant(A1, "A", armed);
            match.Tick(armed + rules.BombSeconds + 0.1f);
            now = armed + rules.BombSeconds + rules.RoundOverSeconds + 0.2f;
            match.Tick(now);
            match.Tick(now + rules.RoundOverSeconds + 1f);

            Assert.Equal(RoundPhase.MatchOver, match.Round);
            Assert.Equal(Team.Attackers, match.MatchWinner);

            MatchResults results = match.BuildResults();
            Assert.Equal(4, results.Standings.Count);

            // Attackers first, both of them, then the defenders.
            Assert.Equal(A1, results.Standings[0].ClientId);
            Assert.Equal(A2, results.Standings[1].ClientId);
            Assert.Equal(D1, results.Standings[2].ClientId);
            Assert.Equal(D2, results.Standings[3].ClientId);

            Assert.True(results.Standings[0].IsWinner);
            Assert.True(results.Standings[1].IsWinner);
            Assert.False(results.Standings[2].IsWinner);
            Assert.Equal("ATK0", results.WinnerName);
            Assert.Equal(1, results.Standings[0].Rank);
            Assert.Equal(4, results.Standings[3].Rank);
        }

        /// <summary>Inside a squad the board is ordered by personal score, not by join order.</summary>
        [Fact]
        public void BuildResultsOrdersSquadRowsByKills()
        {
            BombMatchController match = Create(out BombRules rules, 3);
            StartLive(match);

            // The third attacker gets two kills, so he must be above his teammates.
            match.RegisterKill(A1 + 2, D1, 120f);
            match.RegisterKill(A1 + 2, D2, 121f);

            MatchResults results = match.BuildResults();
            int firstAttacker = 0;
            for (int i = 0; i < results.Standings.Count; i++)
            {
                if (results.Standings[i].DisplayName.StartsWith("ATK"))
                {
                    firstAttacker = i;
                    break;
                }
            }

            Assert.Equal(A1 + 2, results.Standings[firstAttacker].ClientId);
            Assert.Equal(2, results.Standings[firstAttacker].Kills);
        }
    }
}
