using System;
using System.Collections.Generic;
using System.Globalization;
using Kordon.Core.Serialization;

namespace Kordon.Core.Rules
{
    /// <summary>
    /// The demolition half of a map layout: where the sites are, which shelters belong to
    /// which side, and where each side can buy.
    ///
    /// It is parsed in Core rather than in the Unity layer so the data contract is covered
    /// by the test suite: a map that ships a site in the wrong place, a buy zone for a team
    /// that does not exist, or a team spawn that is not in its own zone fails CI instead of
    /// failing in a live match.  Map *geometry* stays in Blender and is audited there; this
    /// is only the numbers the round needs.
    /// </summary>
    public sealed class DemolitionMapData
    {
        public readonly List<BombSiteVolume> Sites = new List<BombSiteVolume>();
        public readonly List<BuyZoneVolume> BuyZones = new List<BuyZoneVolume>();
        public readonly List<string> AttackerSpawns = new List<string>();
        public readonly List<string> DefenderSpawns = new List<string>();
        public readonly List<string> Modes = new List<string>();

        public bool SupportsDemolition => Modes.Contains("Demolition");

        /// <summary>Parses the layout JSON. Returns null and sets <paramref name="error"/> on bad data.</summary>
        public static DemolitionMapData Parse(string json, out string error)
        {
            error = null;
            DemolitionMapData data = new DemolitionMapData();

            Json root;
            try
            {
                root = Json.Parse(json);
            }
            catch (Exception exception)
            {
                error = "layout is not valid JSON: " + exception.Message;
                return null;
            }

            foreach (Json mode in root["modes"].Items())
            {
                data.Modes.Add(mode.AsString(""));
            }

            foreach (Json site in root["bombSites"].Items())
            {
                string id = site["id"].AsString("");
                if (string.IsNullOrEmpty(id))
                {
                    error = "a bomb site has no id";
                    return null;
                }

                data.Sites.Add(new BombSiteVolume
                {
                    Id = id,
                    Name = site["name"].AsString(id),
                    CentreX = site["center"][0].AsFloat(),
                    CentreY = site["center"][1].AsFloat(),
                    Radius = site["radius"].AsFloat(0f)
                });
            }

            foreach (Json zone in root["buyZones"].Items())
            {
                string teamText = zone["team"].AsString("");
                Team team;
                if (string.Equals(teamText, "Attackers", StringComparison.OrdinalIgnoreCase))
                {
                    team = Team.Attackers;
                }
                else if (string.Equals(teamText, "Defenders", StringComparison.OrdinalIgnoreCase))
                {
                    team = Team.Defenders;
                }
                else
                {
                    error = "buy zone " + zone["id"].AsString("?") + " has no valid team";
                    return null;
                }

                data.BuyZones.Add(new BuyZoneVolume
                {
                    Id = zone["id"].AsString(""),
                    Team = team,
                    CentreX = zone["center"][0].AsFloat(),
                    CentreY = zone["center"][1].AsFloat(),
                    Radius = zone["radius"].AsFloat(0f)
                });
            }

            foreach (Json spawn in root["teamSpawns"]["attackers"].Items())
            {
                data.AttackerSpawns.Add(spawn.AsString(""));
            }

            foreach (Json spawn in root["teamSpawns"]["defenders"].Items())
            {
                data.DefenderSpawns.Add(spawn.AsString(""));
            }

            if (data.SupportsDemolition)
            {
                if (data.Sites.Count == 0)
                {
                    error = "the map declares demolition but has no bomb sites";
                    return null;
                }

                if (data.BuyZones.Count == 0)
                {
                    error = "the map declares demolition but has no buy zones";
                    return null;
                }

                if (data.AttackerSpawns.Count == 0 || data.DefenderSpawns.Count == 0)
                {
                    error = "the map declares demolition but assigns no team spawns";
                    return null;
                }
            }

            return data;
        }

        /// <summary>Spawn ids for a side, so the round can place its players.</summary>
        public IReadOnlyList<string> SpawnsFor(Team team) =>
            team == Team.Attackers ? AttackerSpawns : DefenderSpawns;

        public BombSiteVolume? Site(string id)
        {
            for (int i = 0; i < Sites.Count; i++)
            {
                if (string.Equals(Sites[i].Id, id, StringComparison.Ordinal))
                {
                    return Sites[i];
                }
            }

            return null;
        }
    }
}
