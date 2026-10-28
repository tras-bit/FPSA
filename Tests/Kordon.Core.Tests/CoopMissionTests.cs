using System.Collections.Generic;
using System.Reflection;
using Kordon.Core.Coop;
using Kordon.Core.Rules;
using Xunit;

namespace Kordon.Core.Tests
{
    /// <summary>
    /// The shipped co-operative operation: briefing for at least two operators, a chain
    /// of linked objectives, AI opposition budget, hold, extraction and the failure
    /// conditions.  It also guards the rule that co-op never changes the free-for-all.
    /// </summary>
    public class CoopMissionTests
    {
        private const ulong Alpha = 11UL;
        private const ulong Bravo = 22UL;
        private const ulong Charlie = 33UL;

        private static MissionDefinition Shipped()
        {
            MissionDefinition mission = TestData.Database.GetMission("silent_kiln");
            Assert.NotNull(mission);
            return mission;
        }

        private static MissionController TwoOperators(out MissionController controller, float now = 0f)
        {
            controller = new MissionController(Shipped());
            Assert.True(controller.AddOperator(Alpha, "ALPHA", now));
            Assert.True(controller.AddOperator(Bravo, "BRAVO", now));
            return controller;
        }

        private static MissionController LiveMission(float now = 0f)
        {
            MissionController controller = TwoOperators(out _, now);
            Assert.True(controller.BeginBriefing(now));
            controller.AcknowledgeBriefing(Alpha, now + 1f);
            Assert.True(controller.AcknowledgeBriefing(Bravo, now + 2f));
            Assert.Equal(MissionPhase.ObjectivesActive, controller.Phase);
            return controller;
        }

        /// <summary>Walks the whole objective chain up to (but not including) extraction.</summary>
        private static void ClearObjectives(MissionController controller, float start)
        {
            float now = start;
            Assert.True(controller.ProgressObjective("reach_yard", 1, Alpha, now += 5f));
            Assert.True(controller.ProgressObjective("breach_gate", 1, Bravo, now += 20f));
            Assert.False(controller.ProgressObjective("sabotage_fuel_line", 1, Alpha, now += 30f));
            Assert.True(controller.ProgressObjective("sabotage_fuel_line", 1, Bravo, now += 25f));
            Assert.True(controller.ProgressObjective("retrieve_intel", 1, Alpha, now += 40f));

            for (int i = 0; i < 45; i++)
            {
                controller.TickHoldObjective("hold_kiln_hall", true, 1f, now += 1f);
            }

            Assert.Equal(ObjectiveStatus.Completed, controller.GetObjective("hold_kiln_hall").Status);
            Assert.True(controller.ProgressObjective("reach_extraction", 1, Bravo, now += 15f));
        }

        [Fact]
        public void ShippedOperation_MatchesTheDesignContract()
        {
            MissionDefinition mission = Shipped();
            Assert.Equal("Operation Silent Kiln", mission.Name);
            Assert.Equal(2, mission.MinPlayers);
            Assert.Equal(4, mission.MaxPlayers);
            Assert.True(mission.Objectives.Count >= 5);
            Assert.True(mission.TimeLimitSeconds > 0f);
            Assert.True(mission.ExtractionWaitSeconds > 0f);
            Assert.True(mission.ExtractionBoardSeconds > 0f);
            Assert.True(mission.DownedBleedOutSeconds > 0f);
            Assert.True(mission.DownAndRevive);
            Assert.True(mission.TotalHostileBudget > 0);
            Assert.True(mission.MaxSimultaneousHostiles > 0);
            Assert.True(mission.AlarmReinforcements > 0);
            Assert.False(string.IsNullOrEmpty(mission.BriefingText));
            Assert.False(string.IsNullOrEmpty(mission.DebriefText));

            bool hasExtraction = false;
            foreach (ObjectiveDefinition objective in mission.Objectives)
            {
                if (objective.Kind == ObjectiveKind.Extract)
                {
                    hasExtraction = true;
                }

                Assert.False(string.IsNullOrEmpty(objective.Title));
                Assert.False(string.IsNullOrEmpty(objective.Description));
                Assert.True(objective.RequiredProgress > 0);
                Assert.NotNull(mission.GetObjective(objective.Id));
                foreach (string dependency in objective.DependsOn)
                {
                    Assert.NotNull(mission.GetObjective(dependency));
                }
            }

            Assert.True(hasExtraction);
            Assert.Equal(new List<string> { "hold_kiln_hall" }, mission.ExtractionUnlockIds());
        }

        [Fact]
        public void Briefing_NeedsTwoOperatorsAndEveryAcknowledgement()
        {
            MissionController controller = new MissionController(Shipped());
            Assert.True(controller.AddOperator(Alpha, "ALPHA", 0f));
            Assert.False(controller.BeginBriefing(0f));
            Assert.Equal(MissionPhase.None, controller.Phase);
            Assert.Contains(controller.Log, line => line.Contains("briefing-rejected"));

            Assert.True(controller.AddOperator(Bravo, "BRAVO", 1f));
            Assert.True(controller.BeginBriefing(1f));
            Assert.Equal(MissionPhase.Briefing, controller.Phase);
            Assert.Equal(Shipped().BriefingTitle, controller.BuildStatus().SubtitleLine);

            // One acknowledgement is not enough: everybody has to read the briefing.
            Assert.False(controller.AcknowledgeBriefing(Alpha, 2f));
            Assert.Equal(MissionPhase.Briefing, controller.Phase);
            Assert.True(controller.GetOperator(Alpha).AcknowledgedBriefing);
            Assert.False(controller.GetOperator(Bravo).AcknowledgedBriefing);

            Assert.True(controller.AcknowledgeBriefing(Bravo, 3f));
            Assert.Equal(MissionPhase.ObjectivesActive, controller.Phase);
            Assert.Equal(ObjectiveStatus.Active, controller.GetObjective("reach_yard").Status);
            Assert.Equal(ObjectiveStatus.Locked, controller.GetObjective("breach_gate").Status);

            // The operation seats four operators and no more.
            Assert.True(controller.AddOperator(Charlie, "CHARLIE", 4f));
            Assert.True(controller.AddOperator(44UL, "DELTA", 4f));
            Assert.False(controller.AddOperator(55UL, "ECHO", 4f));
            Assert.False(controller.AddOperator(Alpha, "ALPHA-AGAIN", 4f));
        }

        [Fact]
        public void ObjectiveChain_UnlocksOnlyInOrder()
        {
            MissionController controller = LiveMission();

            // Skipping ahead is impossible: dependencies gate every step.
            Assert.False(controller.ProgressObjective("breach_gate", 1, Alpha, 10f));
            Assert.False(controller.ProgressObjective("hold_kiln_hall", 1, Alpha, 10f));
            Assert.False(controller.ProgressObjective("extract", 1, Alpha, 10f));
            Assert.False(controller.ProgressObjective("does_not_exist", 1, Alpha, 10f));
            Assert.False(controller.ProgressObjective("reach_yard", 0, Alpha, 10f));

            Assert.True(controller.ProgressObjective("reach_yard", 1, Alpha, 11f));
            Assert.Equal(ObjectiveStatus.Completed, controller.GetObjective("reach_yard").Status);
            Assert.Equal(ObjectiveStatus.Active, controller.GetObjective("breach_gate").Status);

            Assert.True(controller.ProgressObjective("breach_gate", 1, Bravo, 20f));
            Assert.Equal(ObjectiveStatus.Active, controller.GetObjective("sabotage_fuel_line").Status);
            Assert.Equal(ObjectiveStatus.Active, controller.GetObjective("retrieve_intel").Status);

            // The fuel line needs two charges.
            Assert.False(controller.ProgressObjective("sabotage_fuel_line", 1, Alpha, 30f));
            Assert.Equal(ObjectiveStatus.InProgress, controller.GetObjective("sabotage_fuel_line").Status);
            Assert.Equal(1, controller.GetObjective("sabotage_fuel_line").Progress);
            Assert.True(controller.ProgressObjective("sabotage_fuel_line", 1, Bravo, 40f));
            Assert.Equal(1f,
                controller.GetObjective("sabotage_fuel_line").ProgressFraction(2), 3);
            Assert.Equal(0f, controller.GetObjective("retrieve_intel").ProgressFraction(1), 3);

            // Retrieval marks the carrier.
            Assert.True(controller.ProgressObjective("retrieve_intel", 1, Alpha, 50f));
            Assert.Equal("retrieve_intel", controller.GetOperator(Alpha).CarryingObjectiveId);

            // The hold only unlocks once both are done.
            Assert.Equal(ObjectiveStatus.Active, controller.GetObjective("hold_kiln_hall").Status);
            Assert.Equal(ObjectiveStatus.Locked, controller.GetObjective("reach_extraction").Status);
        }

        [Fact]
        public void HoldObjective_NeedsBodiesInTheRoomAndDecaysWhenTheyLeave()
        {
            MissionController controller = LiveMission();
            controller.ProgressObjective("reach_yard", 1, Alpha, 5f);
            controller.ProgressObjective("breach_gate", 1, Bravo, 10f);
            controller.ProgressObjective("sabotage_fuel_line", 2, Alpha, 20f);
            controller.ProgressObjective("retrieve_intel", 1, Bravo, 30f);

            ObjectiveState hold = controller.GetObjective("hold_kiln_hall");
            Assert.Equal(ObjectiveStatus.Active, hold.Status);

            for (int i = 0; i < 20; i++)
            {
                controller.TickHoldObjective("hold_kiln_hall", true, 1f, 40f + i);
            }

            Assert.Equal(20f, hold.HoldTimer, 3);
            Assert.Equal(20, hold.Progress);
            Assert.Equal(ObjectiveStatus.InProgress, hold.Status);

            // Leaving the hall bleeds the timer at half rate; it never resets to zero instantly.
            controller.TickHoldObjective("hold_kiln_hall", false, 4f, 60f);
            Assert.Equal(18f, hold.HoldTimer, 3);

            for (int i = 0; i < 30; i++)
            {
                controller.TickHoldObjective("hold_kiln_hall", true, 1f, 70f + i);
            }

            Assert.Equal(ObjectiveStatus.Completed, hold.Status);
            Assert.Equal(45f, hold.HoldTimer, 3);
            Assert.True(controller.ExtractionAvailable());

            // A completed hold cannot be run again.
            controller.TickHoldObjective("hold_kiln_hall", true, 5f, 200f);
            Assert.Equal(45f, hold.HoldTimer, 3);
        }

        [Fact]
        public void Extraction_IsCalledWaitsBoardingAndCompletesTheOperation()
        {
            MissionController controller = LiveMission();
            Assert.False(controller.ExtractionAvailable());
            Assert.False(controller.CallExtraction(5f));
            Assert.Equal(MissionPhase.ObjectivesActive, controller.Phase);

            ClearObjectives(controller, 10f);
            Assert.True(controller.ExtractionAvailable());

            float called = 300f;
            Assert.True(controller.CallExtraction(called));
            Assert.Equal(MissionPhase.ExtractionCalled, controller.Phase);
            Assert.True(controller.AlarmRaised);
            Assert.Equal(Shipped().ExtractionWaitSeconds, controller.ExtractionEta, 3);
            Assert.Equal(called + Shipped().ExtractionWaitSeconds,
                controller.BuildStatus().ExtractionEtaSeconds + called, 2);

            // The aircraft arrives on its own schedule.
            controller.Tick(called + Shipped().ExtractionWaitSeconds);
            Assert.Equal(MissionPhase.ExtractionBoarding, controller.Phase);
            Assert.True(controller.BoardingTimeRemaining > 0f);

            // Everybody has to be aboard: one operator alone does not finish the op.
            Assert.False(controller.BoardOperator(Alpha, called + 80f));
            Assert.True(controller.GetOperator(Alpha).IsExtracted);
            Assert.Equal(MissionPhase.ExtractionBoarding, controller.Phase);

            Assert.True(controller.BoardOperator(Bravo, called + 85f));
            Assert.Equal(MissionPhase.Completed, controller.Phase);
            Assert.True(controller.GetOperator(Bravo).IsExtracted);
            Assert.Equal(ObjectiveStatus.Completed, controller.GetObjective("extract").Status);

            MissionStatus status = controller.BuildStatus();
            Assert.Equal(MissionPhase.Completed, status.Phase);
            Assert.Equal(Shipped().DebriefText, status.SubtitleLine);
            Assert.Equal(2, status.Operators.Count);
            Assert.Equal(string.Empty, status.CurrentObjectiveTitle);
            Assert.Contains(controller.Log, line => line.Contains("mission-completed"));
        }

        [Fact]
        public void MissingTheExtraction_FailsTheOperation()
        {
            MissionController controller = LiveMission();
            ClearObjectives(controller, 10f);
            float called = 400f;
            Assert.True(controller.CallExtraction(called));

            controller.Tick(called + Shipped().ExtractionWaitSeconds);
            Assert.Equal(MissionPhase.ExtractionBoarding, controller.Phase);
            Assert.False(controller.BoardOperator(Alpha, called + 80f));
            Assert.True(controller.GetOperator(Alpha).IsExtracted);

            controller.Tick(called + Shipped().ExtractionWaitSeconds + Shipped().ExtractionBoardSeconds);
            Assert.Equal(MissionPhase.Failed, controller.Phase);
            Assert.Equal(MissionFailReason.ExtractionMissed, controller.FailReason);
            Assert.Equal(MissionFailReason.ExtractionMissed, controller.BuildStatus().FailReason);
        }

        [Fact]
        public void WholeTeamDown_FailsTheOperation()
        {
            MissionController controller = LiveMission();
            Assert.True(controller.DownOperator(Alpha, 20f));
            Assert.Equal(MissionPhase.ObjectivesActive, controller.Phase);
            Assert.True(controller.GetOperator(Alpha).IsDowned);
            Assert.False(controller.GetOperator(Alpha).IsFighting);

            Assert.True(controller.DownOperator(Bravo, 21f));
            Assert.Equal(MissionPhase.Failed, controller.Phase);
            Assert.Equal(MissionFailReason.AllOperatorsDown, controller.FailReason);
        }

        [Fact]
        public void TeammatesCanRevive_DownedOperatorsCannot()
        {
            MissionController controller = LiveMission();
            Assert.True(controller.DownOperator(Alpha, 20f));

            // A downed operator cannot pick anybody up.
            Assert.True(controller.DownOperator(Bravo, 21f));
            Assert.False(controller.ReviveOperator(Alpha, Bravo, 22f));

            // Bring Bravo back through the mission controller and let him revive Alpha.
            MissionController second = LiveMission();
            Assert.True(second.DownOperator(Alpha, 20f));
            Assert.False(second.ReviveOperator(Alpha, Alpha, 21f));
            Assert.True(second.ReviveOperator(Alpha, Bravo, 22f));
            Assert.False(second.GetOperator(Alpha).IsDowned);
            Assert.Equal(1, second.GetOperator(Bravo).RevivesGiven);
            Assert.False(second.ReviveOperator(Alpha, Bravo, 23f));
            Assert.Equal(MissionPhase.ObjectivesActive, second.Phase);
        }

        [Fact]
        public void DownedOperatorBleedsOut_WhenNobodyReachesThem()
        {
            MissionController controller = LiveMission();
            Assert.True(controller.DownOperator(Alpha, 100f));
            controller.Tick(100f + Shipped().DownedBleedOutSeconds - 1f);
            Assert.True(controller.GetOperator(Alpha).IsDowned);
            Assert.False(controller.GetOperator(Alpha).IsDead);

            controller.Tick(100f + Shipped().DownedBleedOutSeconds);
            Assert.True(controller.GetOperator(Alpha).IsDead);
            Assert.False(controller.GetOperator(Alpha).IsDowned);
            Assert.Contains(controller.Log, line => line.Contains("bled-out"));

            // One operator still fighting keeps the operation alive.
            Assert.Equal(MissionPhase.ObjectivesActive, controller.Phase);
        }

        [Fact]
        public void CriticalObjectiveFailure_EndsTheMission_OptionalDoesNot()
        {
            MissionController controller = LiveMission();
            controller.ProgressObjective("reach_yard", 1, Alpha, 10f);
            Assert.False(controller.FailObjective("does_not_exist", 11f));
            Assert.True(controller.FailObjective("reach_yard", 12f));
            Assert.Equal(MissionPhase.Failed, controller.Phase);
            Assert.Equal(MissionFailReason.CriticalObjectiveFailed, controller.FailReason);
            Assert.Equal(ObjectiveStatus.Failed, controller.GetObjective("reach_yard").Status);

            MissionController second = LiveMission();
            ClearObjectives(second, 10f);
            Assert.False(second.FailObjective("extract", 500f));
            Assert.NotEqual(MissionPhase.Failed, second.Phase);
        }

        [Fact]
        public void TimeLimit_ExpiresTheOperation()
        {
            MissionController controller = LiveMission(0f);
            controller.Tick(Shipped().TimeLimitSeconds * 0.5f);
            Assert.Equal(MissionPhase.ObjectivesActive, controller.Phase);
            Assert.True(controller.MissionTimeRemaining > 0f);

            controller.Tick(Shipped().TimeLimitSeconds + 1f);
            Assert.Equal(MissionPhase.Failed, controller.Phase);
            Assert.Equal(MissionFailReason.TimeLimitExpired, controller.FailReason);
            Assert.Equal(0f, controller.MissionTimeRemaining, 3);
        }

        [Fact]
        public void AlarmAndHostileBudget_AreTrackedByTheServer()
        {
            MissionController controller = LiveMission();
            Assert.False(controller.AlarmRaised);
            controller.RaiseAlarm(50f, "gate-breach");
            Assert.True(controller.AlarmRaised);
            Assert.Contains(controller.Log, line => line.Contains("alarm-raised:gate-breach"));
            controller.RaiseAlarm(60f, "second");
            Assert.Single(controller.Log, line => line.Contains("alarm-raised"));

            controller.NoteHostileSpawned(6);
            Assert.Equal(6, controller.HostilesSpawnedTotal);
            Assert.Equal(Shipped().TotalHostileBudget, controller.BuildStatus().HostilesRemainingBudget);
        }

        [Fact]
        public void EliminationObjectives_CreditTheOperatorAndTheTeam()
        {
            MissionDefinition mission = new MissionDefinition
            {
                Id = "training_contact",
                Name = "Training Contact",
                MinPlayers = 2,
                MaxPlayers = 4,
                TimeLimitSeconds = 0f,
                TotalHostileBudget = 6,
                Objectives = new List<ObjectiveDefinition>
                {
                    new ObjectiveDefinition
                    {
                        Id = "clear_yard",
                        Kind = ObjectiveKind.Eliminate,
                        Title = "Clear the yard",
                        Description = "Remove six hostiles from the yard.",
                        RequiredProgress = 6
                    },
                    new ObjectiveDefinition
                    {
                        Id = "extract",
                        Kind = ObjectiveKind.Extract,
                        Title = "Extract",
                        Description = "Board the aircraft.",
                        RequiredProgress = 1,
                        DependsOn = new List<string> { "clear_yard" }
                    }
                }
            };

            MissionController controller = new MissionController(mission);
            controller.AddOperator(Alpha, "ALPHA", 0f);
            controller.AddOperator(Bravo, "BRAVO", 0f);
            controller.BeginBriefing(0f);
            controller.AcknowledgeBriefing(Alpha, 1f);
            controller.AcknowledgeBriefing(Bravo, 2f);

            for (int i = 0; i < 4; i++)
            {
                Assert.False(controller.ProgressObjective("clear_yard", 1, Alpha, 10f + i));
            }

            Assert.Equal(4, controller.GetOperator(Alpha).HostilesRemoved);
            Assert.Equal(4, controller.HostilesRemovedTotal);
            Assert.Equal(2, controller.BuildStatus().HostilesRemainingBudget);

            Assert.False(controller.ProgressObjective("clear_yard", 1, Bravo, 20f));
            Assert.Equal(5, controller.HostilesRemovedTotal);
            Assert.True(controller.ProgressObjective("clear_yard", 1, Bravo, 21f));
            Assert.Equal(6, controller.HostilesRemovedTotal);
            Assert.Equal(0, controller.BuildStatus().HostilesRemainingBudget);
            // Bravo removed two hostiles: the one that filled the objective and the
            // one after it (which still counts as a removal, just not as progress).
            Assert.Equal(2, controller.GetOperator(Bravo).HostilesRemoved);
            Assert.Equal(ObjectiveStatus.Active, controller.GetObjective("extract").Status);
        }

        [Fact]
        public void OperatorsCanLeave_AndTheMissionNoticesWhenEverybodyDoes()
        {
            MissionController controller = LiveMission();
            Assert.False(controller.RemoveOperator(Charlie, 10f));
            Assert.True(controller.RemoveOperator(Alpha, 11f));
            Assert.Equal(MissionPhase.ObjectivesActive, controller.Phase);
            Assert.True(controller.RemoveOperator(Bravo, 12f));
            Assert.Equal(MissionPhase.Failed, controller.Phase);
            Assert.Equal(MissionFailReason.Abandoned, controller.FailReason);
        }

        [Fact]
        public void ServerClockGoingBackwards_IsIgnored()
        {
            MissionController controller = LiveMission(100f);
            controller.Tick(120f);
            Assert.Equal(120f, controller.Now, 3);
            controller.Tick(50f);
            Assert.Equal(120f, controller.Now, 3);
            Assert.Contains(controller.Log, line => line.Contains("clock-went-backwards"));
        }

        [Fact]
        public void StatusSnapshot_DrivesTheHudWithoutLeakingRules()
        {
            MissionController controller = LiveMission();
            MissionStatus status = controller.BuildStatus();
            Assert.Equal(MissionPhase.ObjectivesActive, status.Phase);
            Assert.Equal(Shipped().Name, status.MissionName);
            Assert.Equal("Reach the rail spur", status.CurrentObjectiveTitle);
            Assert.False(string.IsNullOrEmpty(status.CurrentObjectiveDescription));
            Assert.Equal(Shipped().Objectives.Count, status.Objectives.Count);
            Assert.Equal(2, status.Operators.Count);
            Assert.Equal(-1f, status.ExtractionEtaSeconds, 3);
            Assert.Equal(-1f, status.BoardingTimeRemaining, 3);
            Assert.True(status.MissionTimeRemaining > 0f);

            controller.ProgressObjective("reach_yard", 1, Alpha, 30f);
            Assert.Equal("Breach the yard gate", controller.BuildStatus().CurrentObjectiveTitle);
        }

        [Fact]
        public void CoopMode_NeverChangesTheFreeForAllRules()
        {
            MatchRulesConfig ffa = TestData.Database.MatchRules;
            Assert.Equal(MatchMode.FreeForAll, ffa.Mode);
            Assert.Equal(20, ffa.MaxPlayers);
            Assert.Equal(50, ffa.KillTarget);
            Assert.False(ffa.AllowBots);
            Assert.Null(ffa.Validate());

            // The co-op controller has no kill target, no score and no winner concept:
            // it only tracks objectives, operators and the extraction.
            MissionController controller = LiveMission();
            Assert.Equal(MissionPhase.ObjectivesActive, controller.Phase);
            Assert.True(controller.Definition.MinPlayers >= 2);
            foreach (PropertyInfo property in typeof(MissionController).GetProperties())
            {
                string name = property.Name.ToLowerInvariant();
                Assert.DoesNotContain("killtarget", name);
                Assert.DoesNotContain("score", name);
                Assert.DoesNotContain("winner", name);
            }
        }
    }
}
