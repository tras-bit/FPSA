using System;
using System.Collections.Generic;
using System.Globalization;
using Kordon.Core.Utility;

namespace Kordon.Core.Coop
{
    public enum MissionPhase
    {
        None = 0,
        Briefing = 1,
        Infiltration = 2,
        ObjectivesActive = 3,
        ExtractionCalled = 4,
        ExtractionBoarding = 5,
        Completed = 6,
        Failed = 7
    }

    public enum MissionFailReason
    {
        None = 0,
        AllOperatorsDown = 1,
        CriticalObjectiveFailed = 2,
        TimeLimitExpired = 3,
        ExtractionMissed = 4,
        Abandoned = 5
    }

    /// <summary>Live state of one objective.</summary>
    [Serializable]
    public sealed class ObjectiveState
    {
        public string Id;
        public ObjectiveStatus Status = ObjectiveStatus.Locked;
        public int Progress;
        public float HoldTimer;
        public bool IsDiscovered;
        public string HolderClientId;

        public float ProgressFraction(int required) =>
            required <= 0 ? 1f : MathUtil.Clamp01((float)Progress / required);
    }

    /// <summary>One operator inside the operation.</summary>
    [Serializable]
    public sealed class OperatorState
    {
        public ulong ClientId;
        public string DisplayName = string.Empty;
        public bool IsConnected = true;
        public bool IsDowned;
        public bool IsDead;
        public bool IsExtracted;
        public bool AcknowledgedBriefing;
        public float DownedAt = float.NaN;
        public int RevivesGiven;
        public int HostilesRemoved;
        public string CarryingObjectiveId = string.Empty;

        public bool IsIncapacitated => IsDowned || IsDead;
        public bool IsFighting => IsConnected && !IsIncapacitated && !IsExtracted;
    }

    /// <summary>Snapshot the HUD/objective panel binds to.</summary>
    [Serializable]
    public sealed class MissionStatus
    {
        public MissionPhase Phase;
        public string MissionName = string.Empty;
        public string CurrentObjectiveTitle = string.Empty;
        public string CurrentObjectiveDescription = string.Empty;
        public List<ObjectiveState> Objectives = new List<ObjectiveState>();
        public List<OperatorState> Operators = new List<OperatorState>();
        public float MissionTimeRemaining;
        public float ExtractionEtaSeconds = -1f;
        public float BoardingTimeRemaining = -1f;
        public bool AlarmRaised;
        public MissionFailReason FailReason;
        public int HostilesRemainingBudget;
        public string SubtitleLine = string.Empty;
    }

    /// <summary>
    /// Server authoritative co-op operation runner.  Drives briefing, chained
    /// objectives, downed/revive handling, the alarm state and the extraction window.
    /// Two to four operators; the operation is completed when the team extracts.
    /// </summary>
    [Serializable]
    public sealed class MissionController
    {
        private readonly Dictionary<string, ObjectiveState> _objectives =
            new Dictionary<string, ObjectiveState>(StringComparer.Ordinal);
        private readonly Dictionary<ulong, OperatorState> _operators = new Dictionary<ulong, OperatorState>();
        private readonly List<string> _objectiveOrder = new List<string>();
        private readonly List<string> _log = new List<string>();

        public MissionDefinition Definition { get; }
        public MissionPhase Phase { get; private set; } = MissionPhase.None;
        public float Now { get; private set; }
        public float StartedAt { get; private set; } = float.NaN;
        public float ExtractionCalledAt { get; private set; } = float.NaN;
        public float BoardingStartedAt { get; private set; } = float.NaN;
        public bool AlarmRaised { get; private set; }
        public MissionFailReason FailReason { get; private set; } = MissionFailReason.None;
        public int HostilesRemovedTotal { get; private set; }
        public int HostilesSpawnedTotal { get; private set; }
        public string CurrentSubtitle { get; private set; } = string.Empty;

        public IReadOnlyList<string> Log => _log;
        public IReadOnlyCollection<OperatorState> Operators => _operators.Values;

        public MissionController(MissionDefinition definition)
        {
            Definition = definition ?? throw new ArgumentNullException(nameof(definition));
            foreach (ObjectiveDefinition objective in Definition.Objectives)
            {
                _objectives[objective.Id] = new ObjectiveState { Id = objective.Id };
                _objectiveOrder.Add(objective.Id);
            }
        }

        public OperatorState GetOperator(ulong clientId) =>
            _operators.TryGetValue(clientId, out OperatorState op) ? op : null;

        public ObjectiveState GetObjective(string id) =>
            id != null && _objectives.TryGetValue(id, out ObjectiveState state) ? state : null;

        public int ActiveOperatorCount
        {
            get
            {
                int count = 0;
                foreach (OperatorState op in _operators.Values)
                {
                    if (op.IsFighting)
                    {
                        count++;
                    }
                }

                return count;
            }
        }

        // ------------------------------------------------------------ lifecycle

        public bool AddOperator(ulong clientId, string displayName, float now)
        {
            if (_operators.ContainsKey(clientId))
            {
                return false;
            }

            if (_operators.Count >= Definition.MaxPlayers)
            {
                return false;
            }

            _operators[clientId] = new OperatorState { ClientId = clientId, DisplayName = displayName };
            Now = now;
            return true;
        }

        public bool RemoveOperator(ulong clientId, float now)
        {
            if (!_operators.Remove(clientId))
            {
                return false;
            }

            Now = now;
            EvaluateTeamState(now);
            return true;
        }

        public bool BeginBriefing(float now)
        {
            if (Phase != MissionPhase.None)
            {
                return false;
            }

            if (_operators.Count < Definition.MinPlayers)
            {
                AddLog($"briefing-rejected: need {Definition.MinPlayers} operators, have {_operators.Count}");
                return false;
            }

            Phase = MissionPhase.Briefing;
            StartedAt = now;
            Now = now;
            CurrentSubtitle = Definition.BriefingTitle;
            AddLog("briefing-started");
            return true;
        }

        public bool AcknowledgeBriefing(ulong clientId, float now)
        {
            OperatorState op = GetOperator(clientId);
            if (op == null || Phase != MissionPhase.Briefing)
            {
                return false;
            }

            op.AcknowledgedBriefing = true;
            Now = now;
            foreach (OperatorState other in _operators.Values)
            {
                if (!other.AcknowledgedBriefing)
                {
                    return false;
                }
            }

            return BeginInfiltration(now);
        }

        public bool BeginInfiltration(float now)
        {
            if (Phase != MissionPhase.Briefing && Phase != MissionPhase.None)
            {
                return false;
            }

            Phase = MissionPhase.Infiltration;
            if (float.IsNaN(StartedAt))
            {
                StartedAt = now;
            }

            Now = now;
            ActivateObjectives(now);
            Phase = MissionPhase.ObjectivesActive;
            AddLog("infiltration-started");
            return true;
        }

        private void ActivateObjectives(float now)
        {
            foreach (string id in _objectiveOrder)
            {
                ObjectiveDefinition definition = Definition.GetObjective(id);
                ObjectiveState state = _objectives[id];
                if (state.Status != ObjectiveStatus.Locked)
                {
                    continue;
                }

                if (AllDependenciesCompleted(definition))
                {
                    state.Status = ObjectiveStatus.Active;
                    CurrentSubtitle = definition.Title;
                }
            }
        }

        private bool AllDependenciesCompleted(ObjectiveDefinition definition)
        {
            if (definition == null)
            {
                return true;
            }

            foreach (string dependency in definition.DependsOn)
            {
                if (!_objectives.TryGetValue(dependency, out ObjectiveState state) ||
                    state.Status != ObjectiveStatus.Completed)
                {
                    return false;
                }
            }

            return true;
        }

        // ------------------------------------------------------------ objectives

        /// <summary>Add progress to an objective. Returns true when it completed with this call.</summary>
        public bool ProgressObjective(string objectiveId, int amount, ulong clientId, float now)
        {
            if (Phase != MissionPhase.ObjectivesActive && Phase != MissionPhase.ExtractionCalled)
            {
                return false;
            }

            ObjectiveDefinition definition = Definition.GetObjective(objectiveId);
            ObjectiveState state = GetObjective(objectiveId);
            if (definition == null || state == null)
            {
                AddLog($"unknown-objective:{objectiveId}");
                return false;
            }

            if (state.Status == ObjectiveStatus.Locked && AllDependenciesCompleted(definition))
            {
                state.Status = ObjectiveStatus.Active;
            }

            if (state.Status != ObjectiveStatus.Active && state.Status != ObjectiveStatus.InProgress)
            {
                return false;
            }

            if (amount <= 0)
            {
                return false;
            }

            state.Progress = Math.Min(definition.RequiredProgress, state.Progress + amount);
            state.Status = ObjectiveStatus.InProgress;

            if (definition.Kind == ObjectiveKind.Eliminate)
            {
                OperatorState op = GetOperator(clientId);
                if (op != null)
                {
                    op.HostilesRemoved++;
                }

                HostilesRemovedTotal++;
            }

            if (definition.Kind == ObjectiveKind.Retrieve)
            {
                OperatorState op = GetOperator(clientId);
                if (op != null)
                {
                    op.CarryingObjectiveId = objectiveId;
                }
            }

            if (state.Progress >= definition.RequiredProgress)
            {
                return CompleteObjective(objectiveId, now);
            }

            return false;
        }

        public bool CompleteObjective(string objectiveId, float now)
        {
            ObjectiveDefinition definition = Definition.GetObjective(objectiveId);
            ObjectiveState state = GetObjective(objectiveId);
            if (definition == null || state == null)
            {
                return false;
            }

            if (state.Status == ObjectiveStatus.Completed)
            {
                return false;
            }

            state.Status = ObjectiveStatus.Completed;
            state.Progress = definition.RequiredProgress;
            Now = now;
            AddLog($"objective-completed:{objectiveId}");
            ActivateObjectives(now);

            ObjectiveDefinition next = FirstActiveObjective();
            if (next != null)
            {
                CurrentSubtitle = next.Title;
            }

            if (definition.UnlocksExtraction && ExtractionAvailable())
            {
                CurrentSubtitle = "Extraction available";
            }

            return true;
        }

        public bool FailObjective(string objectiveId, float now)
        {
            ObjectiveDefinition definition = Definition.GetObjective(objectiveId);
            ObjectiveState state = GetObjective(objectiveId);
            if (definition == null || state == null)
            {
                return false;
            }

            state.Status = ObjectiveStatus.Failed;
            AddLog($"objective-failed:{objectiveId}");
            if (definition.IsCritical)
            {
                Fail(MissionFailReason.CriticalObjectiveFailed, now);
            }

            // True means "the objective is now failed"; whether that also ends the
            // operation depends on whether the objective was critical.
            return true;
        }

        public ObjectiveDefinition FirstActiveObjective()
        {
            foreach (string id in _objectiveOrder)
            {
                ObjectiveState state = _objectives[id];
                if (state.Status == ObjectiveStatus.Active || state.Status == ObjectiveStatus.InProgress)
                {
                    return Definition.GetObjective(id);
                }
            }

            return null;
        }

        /// <summary>Every non-optional objective that gates the extraction is done.</summary>
        public bool ExtractionAvailable()
        {
            List<string> unlockers = Definition.ExtractionUnlockIds();
            if (unlockers.Count == 0)
            {
                foreach (ObjectiveDefinition objective in Definition.Objectives)
                {
                    if (objective.IsOptional || objective.Kind == ObjectiveKind.Extract)
                    {
                        continue;
                    }

                    if (GetObjective(objective.Id).Status != ObjectiveStatus.Completed)
                    {
                        return false;
                    }
                }

                return true;
            }

            foreach (string id in unlockers)
            {
                if (GetObjective(id).Status != ObjectiveStatus.Completed)
                {
                    return false;
                }
            }

            return true;
        }

        // ------------------------------------------------------------ extraction

        public bool CallExtraction(float now)
        {
            if (Phase != MissionPhase.ObjectivesActive)
            {
                return false;
            }

            if (!ExtractionAvailable())
            {
                AddLog("extraction-denied: objectives incomplete");
                return false;
            }

            Phase = MissionPhase.ExtractionCalled;
            ExtractionCalledAt = now;
            Now = now;
            RaiseAlarm(now, "extraction-inbound");
            CurrentSubtitle = "Extraction inbound";
            AddLog("extraction-called");
            return true;
        }

        /// <summary>Called when the extraction vehicle arrives on the pad.</summary>
        public bool BeginBoarding(float now)
        {
            if (Phase != MissionPhase.ExtractionCalled)
            {
                return false;
            }

            Phase = MissionPhase.ExtractionBoarding;
            BoardingStartedAt = now;
            Now = now;
            CurrentSubtitle = "Board the extraction";
            AddLog("boarding-started");
            return true;
        }

        public bool BoardOperator(ulong clientId, float now)
        {
            OperatorState op = GetOperator(clientId);
            if (op == null || op.IsDead || op.IsExtracted)
            {
                return false;
            }

            if (Phase != MissionPhase.ExtractionBoarding && Phase != MissionPhase.ExtractionCalled)
            {
                return false;
            }

            op.IsExtracted = true;
            op.IsDowned = false;
            Now = now;
            AddLog($"extracted:{clientId}");

            foreach (OperatorState other in _operators.Values)
            {
                if (!other.IsExtracted && !other.IsDead && other.IsConnected)
                {
                    return false;
                }
            }

            return Complete(now);
        }

        public bool Complete(float now)
        {
            if (Phase == MissionPhase.Completed || Phase == MissionPhase.Failed)
            {
                return false;
            }

            // The extraction itself is the last objective: completing the mission closes it
            // out so the debrief and the results screen show a finished chain.
            foreach (ObjectiveDefinition objective in Definition.Objectives)
            {
                if (objective.Kind != ObjectiveKind.Extract)
                {
                    continue;
                }

                ObjectiveState state = GetObjective(objective.Id);
                if (state != null && state.Status != ObjectiveStatus.Completed)
                {
                    state.Status = ObjectiveStatus.Completed;
                    state.Progress = Math.Max(state.Progress, objective.RequiredProgress);
                }
            }

            Phase = MissionPhase.Completed;
            Now = now;
            CurrentSubtitle = Definition.DebriefText;
            AddLog("mission-completed");
            return true;
        }

        public void Fail(MissionFailReason reason, float now)
        {
            if (Phase == MissionPhase.Completed || Phase == MissionPhase.Failed)
            {
                return;
            }

            Phase = MissionPhase.Failed;
            FailReason = reason;
            Now = now;
            AddLog($"mission-failed:{reason}");
        }

        // ------------------------------------------------------------ operators

        public bool DownOperator(ulong clientId, float now)
        {
            OperatorState op = GetOperator(clientId);
            if (op == null || op.IsIncapacitated)
            {
                return false;
            }

            if (!Definition.DownAndRevive)
            {
                op.IsDead = true;
            }
            else
            {
                op.IsDowned = true;
                op.DownedAt = now;
            }

            Now = now;
            AddLog($"downed:{clientId}");
            EvaluateTeamState(now);
            return true;
        }

        public bool KillOperator(ulong clientId, float now)
        {
            OperatorState op = GetOperator(clientId);
            if (op == null || op.IsDead)
            {
                return false;
            }

            op.IsDowned = false;
            op.IsDead = true;
            Now = now;
            AddLog($"killed:{clientId}");
            EvaluateTeamState(now);
            return true;
        }

        public bool ReviveOperator(ulong targetId, ulong byClientId, float now)
        {
            OperatorState target = GetOperator(targetId);
            OperatorState medic = GetOperator(byClientId);
            if (target == null || !target.IsDowned || medic == null || !medic.IsFighting)
            {
                return false;
            }

            target.IsDowned = false;
            target.DownedAt = float.NaN;
            medic.RevivesGiven++;
            Now = now;
            AddLog($"revived:{targetId}-by-{byClientId}");
            return true;
        }

        public void NoteHostileSpawned(int count) => HostilesSpawnedTotal += Math.Max(0, count);

        public void RaiseAlarm(float now, string reason)
        {
            if (AlarmRaised)
            {
                return;
            }

            AlarmRaised = true;
            Now = now;
            AddLog($"alarm-raised:{reason}");
        }

        private void EvaluateTeamState(float now)
        {
            if (Phase == MissionPhase.Completed || Phase == MissionPhase.Failed || Phase == MissionPhase.None)
            {
                return;
            }

            if (_operators.Count == 0)
            {
                // Everybody left: the operation is abandoned rather than left running.
                Fail(MissionFailReason.Abandoned, now);
                return;
            }

            bool anybodyActive = false;
            foreach (OperatorState op in _operators.Values)
            {
                if (op.IsFighting || op.IsExtracted)
                {
                    anybodyActive = true;
                    break;
                }
            }

            if (!anybodyActive)
            {
                Fail(MissionFailReason.AllOperatorsDown, now);
            }
        }

        // ------------------------------------------------------------ ticking

        public void Tick(float now)
        {
            if (now < Now)
            {
                AddLog("clock-went-backwards");
                return;
            }

            Now = now;
            if (Phase == MissionPhase.None || Phase == MissionPhase.Completed || Phase == MissionPhase.Failed)
            {
                return;
            }

            // Downed operators bleed out.
            foreach (OperatorState op in _operators.Values)
            {
                if (op.IsDowned && !float.IsNaN(op.DownedAt) &&
                    now - op.DownedAt >= Definition.DownedBleedOutSeconds)
                {
                    op.IsDowned = false;
                    op.IsDead = true;
                    AddLog($"bled-out:{op.ClientId}");
                }
            }

            EvaluateTeamState(now);

            // Hold objectives are advanced by TickHoldObjective(), which the server calls
            // once per tick with the occupancy test result for the hold volume.

            // Time limit.
            if (Definition.TimeLimitSeconds > 0f && !float.IsNaN(StartedAt) &&
                now - StartedAt >= Definition.TimeLimitSeconds)
            {
                Fail(MissionFailReason.TimeLimitExpired, now);
                return;
            }

            // Extraction windows.
            if (Phase == MissionPhase.ExtractionCalled && !float.IsNaN(ExtractionCalledAt) &&
                now - ExtractionCalledAt >= Definition.ExtractionWaitSeconds)
            {
                BeginBoarding(now);
            }

            if (Phase == MissionPhase.ExtractionBoarding && !float.IsNaN(BoardingStartedAt) &&
                now - BoardingStartedAt >= Definition.ExtractionBoardSeconds)
            {
                Fail(MissionFailReason.ExtractionMissed, now);
            }
        }

        /// <summary>Advance a Hold objective by one server tick while an operator is inside the area.</summary>
        public void TickHoldObjective(string objectiveId, bool operatorInside, float deltaTime, float now)
        {
            ObjectiveDefinition definition = Definition.GetObjective(objectiveId);
            ObjectiveState state = GetObjective(objectiveId);
            if (definition == null || state == null || definition.Kind != ObjectiveKind.Hold)
            {
                return;
            }

            if (state.Status != ObjectiveStatus.Active && state.Status != ObjectiveStatus.InProgress)
            {
                return;
            }

            if (!operatorInside)
            {
                state.HoldTimer = Math.Max(0f, state.HoldTimer - deltaTime * 0.5f);
                return;
            }

            state.HoldTimer += deltaTime;
            state.Status = ObjectiveStatus.InProgress;
            state.Progress = (int)Math.Floor(state.HoldTimer);
            if (state.HoldTimer >= definition.HoldSeconds)
            {
                CompleteObjective(objectiveId, now);
            }
        }

        public float MissionTimeRemaining => Definition.TimeLimitSeconds <= 0f || float.IsNaN(StartedAt)
            ? -1f
            : Math.Max(0f, Definition.TimeLimitSeconds - (Now - StartedAt));

        public float ExtractionEta => Phase != MissionPhase.ExtractionCalled || float.IsNaN(ExtractionCalledAt)
            ? -1f
            : Math.Max(0f, Definition.ExtractionWaitSeconds - (Now - ExtractionCalledAt));

        public float BoardingTimeRemaining =>
            Phase != MissionPhase.ExtractionBoarding || float.IsNaN(BoardingStartedAt)
                ? -1f
                : Math.Max(0f, Definition.ExtractionBoardSeconds - (Now - BoardingStartedAt));

        public MissionStatus BuildStatus()
        {
            MissionStatus status = new MissionStatus
            {
                Phase = Phase,
                MissionName = Definition.Name,
                AlarmRaised = AlarmRaised,
                FailReason = FailReason,
                MissionTimeRemaining = MissionTimeRemaining,
                ExtractionEtaSeconds = ExtractionEta,
                BoardingTimeRemaining = BoardingTimeRemaining,
                HostilesRemainingBudget = Math.Max(0, Definition.TotalHostileBudget - HostilesRemovedTotal),
                SubtitleLine = CurrentSubtitle
            };

            ObjectiveDefinition current = FirstActiveObjective();
            if (current != null)
            {
                status.CurrentObjectiveTitle = current.Title;
                status.CurrentObjectiveDescription = current.Description;
            }

            foreach (string id in _objectiveOrder)
            {
                status.Objectives.Add(_objectives[id]);
            }

            foreach (OperatorState op in _operators.Values)
            {
                status.Operators.Add(op);
            }

            return status;
        }

        private void AddLog(string message)
        {
            _log.Add(string.Format(CultureInfo.InvariantCulture, "[{0:F1}] {1}", Now, message));
            while (_log.Count > 256)
            {
                _log.RemoveAt(0);
            }
        }
    }
}
