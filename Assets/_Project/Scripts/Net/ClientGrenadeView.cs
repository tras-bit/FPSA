using System.Collections.Generic;
using Kordon.Core.Combat;
using Kordon.Core.Data;
using Kordon.Core.Loadout;
using Kordon.Game.Player;
using Kordon.Game.Weapons;
using UnityEngine;

namespace Kordon.Net
{
    /// <summary>
    /// Client side presentation of thrown equipment.  The flight a client sees is a
    /// visual copy that follows the same Core flight model; it never decides damage.
    /// The server announces each throw with an id and later the blast for exactly that
    /// id, so the puff of smoke and the blast always agree on where the grenade went.
    /// </summary>
    public sealed class ClientGrenadeView : MonoBehaviour
    {
        public LocalOperator Local;
        public DefinitionLibrary Library;

        private readonly Dictionary<int, GrenadeProjectile> _live =
            new Dictionary<int, GrenadeProjectile>();

        public int LiveCount => _live.Count;

        public void Configure(DefinitionLibrary library, LocalOperator local)
        {
            Library = library;
            Local = local;
        }

        private void OnEnable()
        {
            NetworkSessionEvents.GrenadeSpawned += OnSpawned;
            NetworkSessionEvents.GrenadeExploded += OnExploded;
        }

        private void OnDisable()
        {
            NetworkSessionEvents.GrenadeSpawned -= OnSpawned;
            NetworkSessionEvents.GrenadeExploded -= OnExploded;
            Clear();
        }

        public void Clear()
        {
            foreach (GrenadeProjectile projectile in _live.Values)
            {
                if (projectile != null)
                {
                    projectile.Retire();
                }
            }

            _live.Clear();
        }

        private void OnSpawned(int id, Vector3 position, Vector3 velocity, float fuseSeconds,
            string grenadeId)
        {
            GrenadeDefinition definition = Library != null ? Library.GetGrenade(grenadeId) : null;
            if (definition == null)
            {
                return;
            }

            if (_live.TryGetValue(id, out GrenadeProjectile existing) && existing != null)
            {
                existing.Retire();
            }

            GrenadeFlightState state = new GrenadeFlightState
            {
                Position = Kordon.Game.Maps.MapSpace.ToCore(position),
                Velocity = Kordon.Game.Maps.MapSpace.ToCore(velocity),
                FuseRemaining = Mathf.Max(0.05f, fuseSeconds),
            };

            // Visual only: the authoritative explosion arrives as its own message.
            GrenadeProjectile projectile = GrenadeProjectile.SpawnWithState(transform, definition,
                state, true, Local != null ? Local.Audio : null, id);
            if (projectile != null)
            {
                _live[id] = projectile;
            }
        }

        private void OnExploded(int id, Vector3 position, string grenadeId)
        {
            if (_live.TryGetValue(id, out GrenadeProjectile projectile))
            {
                if (projectile != null)
                {
                    projectile.Retire();
                }

                _live.Remove(id);
            }

            GrenadeDefinition definition = Library != null ? Library.GetGrenade(grenadeId) : null;
            GrenadeEffects.Detonate(transform, definition, position,
                Local != null ? Local.Audio : null);

            if (Local == null || definition == null)
            {
                return;
            }

            // Readable feedback: the blast rattles the bodycam and, up close, the HUD.
            float distance = Local.Rig != null
                ? Vector3.Distance(Local.Rig.transform.position, position)
                : 0f;
            if (Local.Rig != null && distance < Mathf.Max(1f, definition.SuppressionRadiusM))
            {
                Local.Rig.SetSuppression(Mathf.Clamp01(
                    1f - distance / Mathf.Max(1f, definition.SuppressionRadiusM)));
            }

            if (Local.Hud != null)
            {
                if (distance < definition.DamageRadiusM + 4f)
                {
                    Local.Hud.DamageFlash();
                }

                if (definition.Kind == GrenadeKind.Flashbang &&
                    distance < definition.EffectRadiusM)
                {
                    Local.Hud.Toast("blinded by a flashbang");
                }
            }
        }
    }
}
