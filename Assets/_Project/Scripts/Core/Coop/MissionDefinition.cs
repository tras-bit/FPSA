using System;
using System.Collections.Generic;

namespace Kordon.Core.Coop
{
    /// <summary>What the team has to do.</summary>
    public enum ObjectiveKind
    {
        None = 0,

        /// <summary>Get to a location.</summary>
        Reach = 1,

        /// <summary>Plant charges / sabotage equipment.</summary>
        Sabotage = 2,

        /// <summary>Pick up an item and carry it.</summary>
        Retrieve = 3,

        /// <summary>Hold a location for a duration.</summary>
        Hold = 4,

        /// <summary>Remove a number of hostiles from an area.</summary>
        Eliminate = 5,

        /// <summary>Interact with a device (valve, terminal, generator).</summary>
        Interact = 6,

        /// <summary>Board the extraction vehicle and leave.</summary>
        Extract = 7
    }

    public enum ObjectiveStatus
    {
        Locked = 0,
        Active = 1,
        InProgress = 2,
        Completed = 3,
        Failed = 4
    }

    /// <summary>One objective inside a co-op operation.</summary>
    [Serializable]
    public sealed class ObjectiveDefinition
    {
        public string Id = "obj_reach_yard";
        public ObjectiveKind Kind = ObjectiveKind.Reach;
        public string Title = string.Empty;
        public string Description = string.Empty;

        /// <summary>Units of progress needed (charges to plant, hostiles to remove, seconds to hold).</summary>
        public int RequiredProgress = 1;

        /// <summary>Objectives that must be completed first.</summary>
        public List<string> DependsOn = new List<string>();

        /// <summary>Navigation node or marker id the objective lives at.</summary>
        public string LocationId = string.Empty;

        /// <summary>Radius in metres around LocationId in which progress is possible.</summary>
        public float InteractionRadiusM = 3.5f;

        /// <summary>Seconds a Hold objective must be held.</summary>
        public float HoldSeconds = 0f;

        public bool IsOptional = false;

        /// <summary>Failing this objective fails the whole operation.</summary>
        public bool IsCritical = true;

        /// <summary>Whether completing this objective opens the extraction.</summary>
        public bool UnlocksExtraction = false;

        public ObjectiveDefinition Clone()
        {
            ObjectiveDefinition copy = (ObjectiveDefinition)MemberwiseClone();
            copy.DependsOn = new List<string>(DependsOn);
            return copy;
        }
    }

    public enum MissionDifficulty
    {
        Standard = 0,
        Veteran = 1
    }

    /// <summary>
    /// A complete co-operative operation: briefing, a chain of objectives and an
    /// extraction.  Authored in Assets/_Project/Data/Definitions/missions.json.
    /// </summary>
    [Serializable]
    public sealed class MissionDefinition
    {
        public string Id = "silent_kiln";
        public string Name = "Operation Silent Kiln";
        public string MapSceneName = "COOP_SilentKiln";
        public string BriefingTitle = "Kiln Site 12";
        public string BriefingText = string.Empty;
        public string DebriefText = string.Empty;

        public int MinPlayers = 2;
        public int MaxPlayers = 4;
        public MissionDifficulty Difficulty = MissionDifficulty.Standard;

        /// <summary>Total operation time limit in seconds; 0 means unlimited.</summary>
        public float TimeLimitSeconds = 1500f;

        /// <summary>How long the extraction vehicle waits once it is called in.</summary>
        public float ExtractionWaitSeconds = 75f;

        /// <summary>How long the team has to board once the vehicle arrives.</summary>
        public float ExtractionBoardSeconds = 40f;

        public List<ObjectiveDefinition> Objectives = new List<ObjectiveDefinition>();

        /// <summary>Hostile budget: how many AI can be alive at once.</summary>
        public int MaxSimultaneousHostiles = 8;
        public int TotalHostileBudget = 34;

        /// <summary>Reinforcement wave triggered when the sabotage is discovered.</summary>
        public int AlarmReinforcements = 10;

        /// <summary>Co-op operators are downed and can be revived, not instantly killed.</summary>
        public bool DownAndRevive = true;
        public float DownedBleedOutSeconds = 45f;

        public MissionDefinition Clone()
        {
            MissionDefinition copy = (MissionDefinition)MemberwiseClone();
            copy.Objectives = new List<ObjectiveDefinition>();
            foreach (ObjectiveDefinition objective in Objectives)
            {
                copy.Objectives.Add(objective.Clone());
            }

            return copy;
        }

        public ObjectiveDefinition GetObjective(string id)
        {
            foreach (ObjectiveDefinition objective in Objectives)
            {
                if (objective.Id == id)
                {
                    return objective;
                }
            }

            return null;
        }

        /// <summary>Objectives that unlock the extraction once they (and their prerequisites) are done.</summary>
        public List<string> ExtractionUnlockIds()
        {
            List<string> ids = new List<string>();
            foreach (ObjectiveDefinition objective in Objectives)
            {
                if (objective.UnlocksExtraction)
                {
                    ids.Add(objective.Id);
                }
            }

            return ids;
        }
    }
}
