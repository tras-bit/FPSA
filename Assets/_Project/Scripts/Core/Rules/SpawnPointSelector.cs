using System;
using System.Collections.Generic;
using Kordon.Core.Utility;

namespace Kordon.Core.Rules
{
    /// <summary>One authored spawn location on the map.</summary>
    [Serializable]
    public struct SpawnPoint
    {
        public KordonVec3 Position;
        public KordonVec3 Forward;

        /// <summary>Balanced start zone: the map ships 4 zones so no corner is favoured.</summary>
        public int ZoneIndex;
        public bool IsActive;

        public SpawnPoint(KordonVec3 position, KordonVec3 forward, int zoneIndex)
        {
            Position = position;
            Forward = forward;
            ZoneIndex = zoneIndex;
            IsActive = true;
        }
    }

    /// <summary>
    /// Picks respawn locations on the server.  Two rules matter:
    ///  1. never respawn a player where an enemy is already aiming (maximise the
    ///     minimum distance to living opponents);
    ///  2. keep the four start zones balanced over the whole match so one corner is
    ///     not farmed.
    /// </summary>
    public sealed class SpawnPointSelector
    {
        private readonly List<SpawnPoint> _points = new List<SpawnPoint>();
        private readonly int[] _zoneUsage;
        private readonly DeterministicRandom _random;

        /// <summary>Distance below which a spawn point is considered contested and skipped.</summary>
        public float DangerRadiusM = 14f;

        /// <summary>Bonus weight for using a zone that has been used least.</summary>
        public float ZoneBalanceWeight = 6f;

        public SpawnPointSelector(IEnumerable<SpawnPoint> points, DeterministicRandom random)
        {
            _random = random ?? new DeterministicRandom(0xA11CEUL);
            int maxZone = 0;
            foreach (SpawnPoint point in points)
            {
                _points.Add(point);
                maxZone = Math.Max(maxZone, point.ZoneIndex);
            }

            _zoneUsage = new int[Math.Max(1, maxZone + 1)];
        }

        public int PointCount => _points.Count;

        public SpawnPoint GetPoint(int index) => _points[MathUtil.Clamp(index, 0, Math.Max(0, _points.Count - 1))];

        /// <summary>
        /// Choose the best spawn point index for a player.  <paramref name="enemies"/> is the
        /// list of positions of every living opponent (free-for-all: everybody else).
        /// </summary>
        public int Select(IReadOnlyList<KordonVec3> enemies)
        {
            if (_points.Count == 0)
            {
                return -1;
            }

            int best = -1;
            float bestScore = float.NegativeInfinity;

            for (int i = 0; i < _points.Count; i++)
            {
                SpawnPoint point = _points[i];
                if (!point.IsActive)
                {
                    continue;
                }

                float minDistance = float.MaxValue;
                if (enemies != null)
                {
                    for (int e = 0; e < enemies.Count; e++)
                    {
                        float d = KordonVec3.Distance(point.Position, enemies[e]);
                        if (d < minDistance)
                        {
                            minDistance = d;
                        }
                    }
                }

                if (float.IsPositiveInfinity(minDistance))
                {
                    minDistance = DangerRadiusM * 4f;
                }

                float score = Math.Min(minDistance, DangerRadiusM * 3f);
                if (minDistance < DangerRadiusM)
                {
                    // Contested: strongly discourage, but keep it possible when the map is full.
                    score -= (DangerRadiusM - minDistance) * 3f;
                }

                if (point.ZoneIndex >= 0 && point.ZoneIndex < _zoneUsage.Length)
                {
                    score -= _zoneUsage[point.ZoneIndex] * ZoneBalanceWeight * 0.1f;
                }

                score += _random.Next01() * 0.75f;

                if (score > bestScore)
                {
                    bestScore = score;
                    best = i;
                }
            }

            if (best >= 0)
            {
                int zone = _points[best].ZoneIndex;
                if (zone >= 0 && zone < _zoneUsage.Length)
                {
                    _zoneUsage[zone]++;
                }
            }

            return best;
        }

        /// <summary>Zone usage counters, exposed for the server debug overlay.</summary>
        public int ZoneUsage(int zoneIndex) =>
            zoneIndex >= 0 && zoneIndex < _zoneUsage.Length ? _zoneUsage[zoneIndex] : 0;

        public void ResetZoneUsage()
        {
            for (int i = 0; i < _zoneUsage.Length; i++)
            {
                _zoneUsage[i] = 0;
            }
        }

        /// <summary>Load spawn points from the map's JSON layout data.</summary>
        public static List<SpawnPoint> ParsePoints(Serialization.Json json)
        {
            List<SpawnPoint> points = new List<SpawnPoint>();
            foreach (Serialization.Json item in json.Items())
            {
                KordonVec3 position = new KordonVec3(
                    item["position"]["x"].AsFloat(),
                    item["position"]["y"].AsFloat(),
                    item["position"]["z"].AsFloat());
                KordonVec3 forward = new KordonVec3(
                    item["forward"]["x"].AsFloat(0f),
                    item["forward"]["y"].AsFloat(0f),
                    item["forward"]["z"].AsFloat(1f));
                SpawnPoint point = new SpawnPoint(position, forward, item["zone"].AsInt(0))
                {
                    IsActive = item["active"].AsBool(true)
                };
                points.Add(point);
            }

            return points;
        }
    }
}
