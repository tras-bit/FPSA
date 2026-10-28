using System;
using System.Collections.Generic;
using Kordon.Core.Serialization;

namespace Kordon.Core.Data
{
    /// <summary>What triggers a subtitle line.</summary>
    public enum SubtitleTrigger
    {
        None = 0,
        MissionStart = 1,
        ObjectiveStarted = 2,
        ObjectiveCompleted = 3,
        AlarmRaised = 4,
        ExtractionCalled = 5,
        ExtractionArrived = 6,
        OperatorDowned = 7,
        EnemyContact = 8,
        RadioMessage = 9,
        MatchCountdown = 10,
        MatchEnd = 11
    }

    /// <summary>One subtitle line. Accessibility requirement: every voiced/announced event is captioned.</summary>
    [Serializable]
    public sealed class SubtitleLine
    {
        public string Id = "coop_briefing_1";
        public SubtitleTrigger Trigger = SubtitleTrigger.RadioMessage;
        public string Speaker = "KOM CONTROL";
        public string Text = string.Empty;
        public string ObjectiveId = string.Empty;
        public float DurationSeconds = 4f;
        public string AudioClipId = string.Empty;
    }

    /// <summary>Caption database, loaded from Data/Definitions/subtitles.json.</summary>
    public sealed class SubtitleLibrary
    {
        private readonly List<SubtitleLine> _lines = new List<SubtitleLine>();

        public IReadOnlyList<SubtitleLine> Lines => _lines;

        public void Add(SubtitleLine line) => _lines.Add(line);

        public List<SubtitleLine> ForTrigger(SubtitleTrigger trigger, string objectiveId = null)
        {
            List<SubtitleLine> result = new List<SubtitleLine>();
            foreach (SubtitleLine line in _lines)
            {
                if (line.Trigger != trigger)
                {
                    continue;
                }

                if (!string.IsNullOrEmpty(objectiveId) && !string.IsNullOrEmpty(line.ObjectiveId) &&
                    line.ObjectiveId != objectiveId)
                {
                    continue;
                }

                result.Add(line);
            }

            return result;
        }

        public static SubtitleLibrary FromJson(string json)
        {
            SubtitleLibrary library = new SubtitleLibrary();
            if (string.IsNullOrEmpty(json))
            {
                return library;
            }

            Json root = Json.Parse(json);
            foreach (Json item in root["subtitles"].Items())
            {
                library.Add(new SubtitleLine
                {
                    Id = item["id"].AsString(),
                    Trigger = item["trigger"].AsEnum(SubtitleTrigger.RadioMessage),
                    Speaker = item["speaker"].AsString(string.Empty),
                    Text = item["text"].AsString(string.Empty),
                    ObjectiveId = item["objectiveId"].AsString(string.Empty),
                    DurationSeconds = item["durationSeconds"].AsFloat(4f),
                    AudioClipId = item["audioClipId"].AsString(string.Empty)
                });
            }

            return library;
        }
    }
}
