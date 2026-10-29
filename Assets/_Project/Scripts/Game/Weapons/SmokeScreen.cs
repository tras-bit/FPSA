using Kordon.Core.Combat;
using Kordon.Game.Maps;
using UnityEngine;

namespace Kordon.Game.Weapons
{
    /// <summary>
    /// The smoke clouds of the current world, in map space, shared by everything that
    /// needs to look through them: the hostile AI, the player's own line of sight and
    /// the server.  A cloud is added where a smoke grenade goes off and simply expires
    /// on its own timer, so a dropped client can never leave permanent smoke behind.
    /// </summary>
    public static class SmokeScreen
    {
        private static readonly SmokeField Field = new SmokeField();

        public static int ActiveClouds
        {
            get
            {
                Prune();
                return Field.Count;
            }
        }

        public static void Add(Vector3 center, float radiusM, float lifetimeSeconds)
        {
            Prune();
            Field.Add(MapSpace.ToCore(center), Mathf.Max(0.1f, radiusM),
                Time.time + Mathf.Max(0.1f, lifetimeSeconds));
        }

        /// <summary>True when a smoke cloud stands between the two world points.</summary>
        public static bool Blocks(Vector3 from, Vector3 to)
        {
            Prune();
            return Field.Count > 0 &&
                   Field.BlocksSegment(MapSpace.ToCore(from), MapSpace.ToCore(to), Time.time);
        }

        public static void Reset() => Field.Clear();

        private static void Prune() => Field.Prune(Time.time);
    }
}
