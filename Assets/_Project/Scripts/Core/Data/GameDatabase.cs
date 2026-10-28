using System;
using System.Collections.Generic;
using System.IO;
using Kordon.Core.Config;
using Kordon.Core.Coop;
using Kordon.Core.Nav;
using Kordon.Core.Progression;
using Kordon.Core.Rules;

namespace Kordon.Core.Data
{
    /// <summary>
    /// Everything data driven the game needs, loaded from one directory:
    ///   Data/Definitions/{bullets,materials,magazines,weapons,armor_kits,grenades}.json
    ///   Data/Definitions/{match_rules,missions,cosmetics,tutorial,subtitles}.json
    ///   Data/Nav/*.json
    ///   Config/net_config.json
    ///
    /// Unity reads these from Assets/StreamingAssets (a plain folder in a standalone
    /// build), the dedicated server reads them next to its executable, and the test
    /// harness reads them straight from the repository.  One source of truth, three
    /// consumers.
    /// </summary>
    public sealed class GameDatabase
    {
        public DefinitionLibrary Definitions { get; private set; } = new DefinitionLibrary();
        public MatchRulesConfig MatchRules { get; private set; } = MatchRulesConfig.DefaultFreeForAll();
        public NetConfig Net { get; private set; } = NetConfig.Default();
        public List<MissionDefinition> Missions { get; } = new List<MissionDefinition>();
        public Dictionary<string, NavGraph> NavGraphs { get; } =
            new Dictionary<string, NavGraph>(StringComparer.Ordinal);
        public CosmeticCatalog Cosmetics { get; private set; } = new CosmeticCatalog();
        public TutorialScript Tutorial { get; private set; } = new TutorialScript();
        public SubtitleLibrary Subtitles { get; private set; } = new SubtitleLibrary();
        public string SourceDirectory { get; private set; } = string.Empty;
        public string RawCosmeticsJson { get; private set; } = string.Empty;

        public MissionDefinition GetMission(string id)
        {
            foreach (MissionDefinition mission in Missions)
            {
                if (mission.Id == id)
                {
                    return mission;
                }
            }

            return null;
        }

        public NavGraph GetNavGraph(string id) =>
            id != null && NavGraphs.TryGetValue(id, out NavGraph graph) ? graph : null;

        /// <summary>Load the whole database from a data root directory.</summary>
        public static GameDatabase Load(string dataRoot)
        {
            GameDatabase database = new GameDatabase { SourceDirectory = dataRoot };
            string definitions = Path.Combine(dataRoot, "Definitions");
            string nav = Path.Combine(dataRoot, "Nav");
            string config = Path.Combine(dataRoot, "Config");

            database.Definitions = DefinitionLibrary.FromDirectory(definitions);

            string matchRules = ReadIfExists(Path.Combine(definitions, "match_rules.json"));
            if (!string.IsNullOrEmpty(matchRules))
            {
                database.MatchRules = MatchRulesParser.FromJson(matchRules);
            }

            string netJson = ReadIfExists(Path.Combine(config, "net_config.json"));
            if (!string.IsNullOrEmpty(netJson))
            {
                database.Net = NetConfig.FromJson(netJson);
            }

            string missions = ReadIfExists(Path.Combine(definitions, "missions.json"));
            if (!string.IsNullOrEmpty(missions))
            {
                database.Missions.AddRange(MissionParser.FromJson(missions));
            }

            if (Directory.Exists(nav))
            {
                foreach (string file in Directory.GetFiles(nav, "*.json"))
                {
                    NavGraph graph = NavGraph.FromJson(File.ReadAllText(file));
                    database.NavGraphs[graph.Id] = graph;
                }
            }

            string cosmetics = ReadIfExists(Path.Combine(definitions, "cosmetics.json"));
            if (!string.IsNullOrEmpty(cosmetics))
            {
                database.RawCosmeticsJson = cosmetics;
                database.Cosmetics = CosmeticCatalog.FromJson(cosmetics);
            }

            string tutorial = ReadIfExists(Path.Combine(definitions, "tutorial.json"));
            if (!string.IsNullOrEmpty(tutorial))
            {
                database.Tutorial = TutorialScript.FromJson(tutorial);
            }

            string subtitles = ReadIfExists(Path.Combine(definitions, "subtitles.json"));
            if (!string.IsNullOrEmpty(subtitles))
            {
                database.Subtitles = SubtitleLibrary.FromJson(subtitles);
            }

            return database;
        }

        private static string ReadIfExists(string path) => File.Exists(path) ? File.ReadAllText(path) : null;

        /// <summary>
        /// Full content audit.  CI runs this against the repository, and the editor menu
        /// Tools &gt; Kordon &gt; Validate content runs the same check.
        /// </summary>
        public List<string> Validate()
        {
            List<string> problems = new List<string>();
            problems.AddRange(Definitions.ValidateIntegrity());

            string rulesProblem = MatchRules.Validate();
            if (rulesProblem != null)
            {
                problems.Add("match-rules: " + rulesProblem);
            }

            string serverProblem = Net.ValidateServer();
            if (serverProblem != null)
            {
                problems.Add("net-config: " + serverProblem);
            }

            foreach (ServerRegion region in Net.Regions)
            {
                if (NetConfig.IsLoopback(region.Address))
                {
                    problems.Add($"net-config: region {region.Id} points at a loopback address");
                }
            }

            if (Missions.Count == 0)
            {
                problems.Add("no-missions-defined");
            }

            foreach (MissionDefinition mission in Missions)
            {
                if (mission.MinPlayers < 2)
                {
                    problems.Add($"{mission.Id}: co-op operation must allow at least two players");
                }

                if (mission.Objectives.Count == 0)
                {
                    problems.Add($"{mission.Id}: no objectives");
                }

                bool hasExtraction = false;
                foreach (ObjectiveDefinition objective in mission.Objectives)
                {
                    if (objective.Kind == ObjectiveKind.Extract)
                    {
                        hasExtraction = true;
                    }

                    if (string.IsNullOrEmpty(objective.Title))
                    {
                        problems.Add($"{mission.Id}/{objective.Id}: objective without a title");
                    }

                    foreach (string dependency in objective.DependsOn)
                    {
                        if (mission.GetObjective(dependency) == null)
                        {
                            problems.Add($"{mission.Id}/{objective.Id}: unknown dependency '{dependency}'");
                        }
                    }
                }

                if (!hasExtraction)
                {
                    problems.Add($"{mission.Id}: operation has no extraction objective");
                }
            }

            foreach (KeyValuePair<string, NavGraph> pair in NavGraphs)
            {
                problems.AddRange(pair.Value.Validate());
            }

            problems.AddRange(Cosmetics.ValidateNoGameplayEffects(RawCosmeticsJson));

            if (Tutorial.Steps.Count == 0)
            {
                problems.Add("tutorial-has-no-steps");
            }

            if (Subtitles.Lines.Count == 0)
            {
                problems.Add("no-subtitles-defined");
            }

            return problems;
        }
    }

    /// <summary>Parses match_rules.json.</summary>
    public static class MatchRulesParser
    {
        public static MatchRulesConfig FromJson(string json)
        {
            Serialization.Json root = Serialization.Json.Parse(json);
            MatchRulesConfig config = MatchRulesConfig.DefaultFreeForAll();
            config.Mode = root["mode"].AsEnum(config.Mode);
            config.MaxPlayers = root["maxPlayers"].AsInt(config.MaxPlayers);
            config.KillTarget = root["killTarget"].AsInt(config.KillTarget);
            config.MinPlayersToStartLive = root["minPlayersToStartLive"].AsInt(config.MinPlayersToStartLive);
            config.LoadoutPhaseSeconds = root["loadoutPhaseSeconds"].AsFloat(config.LoadoutPhaseSeconds);
            config.CountdownSeconds = root["countdownSeconds"].AsFloat(config.CountdownSeconds);
            config.TimeLimitSeconds = root["timeLimitSeconds"].AsFloat(config.TimeLimitSeconds);
            config.RespawnDelaySeconds = root["respawnDelaySeconds"].AsFloat(config.RespawnDelaySeconds);
            config.SpawnProtectionSeconds = root["spawnProtectionSeconds"].AsFloat(config.SpawnProtectionSeconds);
            config.ReconnectGraceSeconds = root["reconnectGraceSeconds"].AsFloat(config.ReconnectGraceSeconds);
            config.AllowBots = root["allowBots"].AsBool(config.AllowBots);
            config.MaxBotsInPractice = root["maxBotsInPractice"].AsInt(config.MaxBotsInPractice);
            config.MaxKillsPerMinute = root["maxKillsPerMinute"].AsFloat(config.MaxKillsPerMinute);
            config.MinSecondsBetweenKills = root["minSecondsBetweenKills"].AsFloat(config.MinSecondsBetweenKills);
            config.Resupply = root["resupply"].AsEnum(config.Resupply);
            config.CreditAssists = root["creditAssists"].AsBool(config.CreditAssists);
            return config;
        }
    }

    /// <summary>Parses missions.json.</summary>
    public static class MissionParser
    {
        public static List<MissionDefinition> FromJson(string json)
        {
            List<MissionDefinition> missions = new List<MissionDefinition>();
            Serialization.Json root = Serialization.Json.Parse(json);
            foreach (Serialization.Json item in root["missions"].Items())
            {
                MissionDefinition mission = new MissionDefinition
                {
                    Id = item["id"].AsString(),
                    Name = item["name"].AsString(),
                    MapSceneName = item["mapSceneName"].AsString(),
                    BriefingTitle = item["briefingTitle"].AsString(),
                    BriefingText = item["briefingText"].AsString(),
                    DebriefText = item["debriefText"].AsString(),
                    MinPlayers = item["minPlayers"].AsInt(2),
                    MaxPlayers = item["maxPlayers"].AsInt(4),
                    Difficulty = item["difficulty"].AsEnum(MissionDifficulty.Standard),
                    TimeLimitSeconds = item["timeLimitSeconds"].AsFloat(1500f),
                    ExtractionWaitSeconds = item["extractionWaitSeconds"].AsFloat(75f),
                    ExtractionBoardSeconds = item["extractionBoardSeconds"].AsFloat(40f),
                    MaxSimultaneousHostiles = item["maxSimultaneousHostiles"].AsInt(8),
                    TotalHostileBudget = item["totalHostileBudget"].AsInt(34),
                    AlarmReinforcements = item["alarmReinforcements"].AsInt(10),
                    DownAndRevive = item["downAndRevive"].AsBool(true),
                    DownedBleedOutSeconds = item["downedBleedOutSeconds"].AsFloat(45f)
                };

                foreach (Serialization.Json objective in item["objectives"].Items())
                {
                    ObjectiveDefinition definition = new ObjectiveDefinition
                    {
                        Id = objective["id"].AsString(),
                        Kind = objective["kind"].AsEnum(ObjectiveKind.Reach),
                        Title = objective["title"].AsString(),
                        Description = objective["description"].AsString(),
                        RequiredProgress = objective["requiredProgress"].AsInt(1),
                        LocationId = objective["locationId"].AsString(string.Empty),
                        InteractionRadiusM = objective["interactionRadiusM"].AsFloat(3.5f),
                        HoldSeconds = objective["holdSeconds"].AsFloat(0f),
                        IsOptional = objective["isOptional"].AsBool(false),
                        IsCritical = objective["isCritical"].AsBool(true),
                        UnlocksExtraction = objective["unlocksExtraction"].AsBool(false)
                    };

                    foreach (Serialization.Json dependency in objective["dependsOn"].Items())
                    {
                        definition.DependsOn.Add(dependency.AsString());
                    }

                    mission.Objectives.Add(definition);
                }

                missions.Add(mission);
            }

            return missions;
        }
    }
}
