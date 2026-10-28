using System;
using System.Collections.Generic;
using Kordon.Core.Serialization;
using Kordon.Core.Settings;

namespace Kordon.Core.Data
{
    public enum TutorialStepKind
    {
        Info = 0,
        PerformInput = 1,
        FireWeapon = 2,
        Reload = 3,
        InspectMagazine = 4,
        MoveToMarker = 5,
        ChangeStance = 6,
        Sprint = 7,
        Lean = 8,
        Complete = 9
    }

    /// <summary>One step of the in-game tutorial.</summary>
    [Serializable]
    public sealed class TutorialStep
    {
        public string Id = "step_move";
        public TutorialStepKind Kind = TutorialStepKind.Info;
        public string Title = string.Empty;
        public string Body = string.Empty;

        /// <summary>Which binding the player has to press (shown with its current key name).</summary>
        public InputAction RequiredAction = InputAction.None;

        /// <summary>How many times the action must be performed.</summary>
        public int RequiredCount = 1;

        /// <summary>Navigation marker for "move to" steps.</summary>
        public string MarkerId = string.Empty;

        public int Progress { get; set; }
        public bool IsComplete { get; set; }

        public bool IsSatisfied => Progress >= RequiredCount;
    }

    /// <summary>
    /// The tutorial that teaches the rules that matter: no crosshair, tap R to reload,
    /// hold R to inspect a magazine, magazine states, sprint cost, leaning, spawn
    /// protection and the 50 elimination objective.
    /// </summary>
    [Serializable]
    public sealed class TutorialScript
    {
        public string Id = "kordon_basics";
        public string Name = "Basic Training";
        public List<TutorialStep> Steps = new List<TutorialStep>();

        public int CurrentStepIndex { get; private set; }
        public bool IsFinished => CurrentStepIndex >= Steps.Count;
        public TutorialStep Current => IsFinished ? null : Steps[CurrentStepIndex];

        /// <summary>Feed a player event; returns true when a step completed with this event.</summary>
        public bool Notify(TutorialStepKind kind, InputAction action, string markerId)
        {
            TutorialStep step = Current;
            if (step == null)
            {
                return false;
            }

            if (step.Kind != kind)
            {
                return false;
            }

            if (step.RequiredAction != InputAction.None && step.RequiredAction != action)
            {
                return false;
            }

            if (!string.IsNullOrEmpty(step.MarkerId) && step.MarkerId != markerId)
            {
                return false;
            }

            if (step.Kind == TutorialStepKind.Info)
            {
                step.IsComplete = true;
                CurrentStepIndex++;
                return true;
            }

            step.Progress++;
            if (step.IsSatisfied)
            {
                step.IsComplete = true;
                CurrentStepIndex++;
                return true;
            }

            return false;
        }

        public void Skip() => CurrentStepIndex = Steps.Count;

        public void Reset()
        {
            CurrentStepIndex = 0;
            foreach (TutorialStep step in Steps)
            {
                step.Progress = 0;
                step.IsComplete = false;
            }
        }

        public static TutorialScript FromJson(string json)
        {
            TutorialScript script = new TutorialScript();
            if (string.IsNullOrEmpty(json))
            {
                return script;
            }

            Json root = Json.Parse(json);
            script.Id = root["id"].AsString(script.Id);
            script.Name = root["name"].AsString(script.Name);
            foreach (Json item in root["steps"].Items())
            {
                script.Steps.Add(new TutorialStep
                {
                    Id = item["id"].AsString(),
                    Kind = item["kind"].AsEnum(TutorialStepKind.Info),
                    Title = item["title"].AsString(string.Empty),
                    Body = item["body"].AsString(string.Empty),
                    RequiredAction = item["requiredAction"].AsEnum(InputAction.None),
                    RequiredCount = item["requiredCount"].AsInt(1),
                    MarkerId = item["markerId"].AsString(string.Empty)
                });
            }

            return script;
        }
    }
}
