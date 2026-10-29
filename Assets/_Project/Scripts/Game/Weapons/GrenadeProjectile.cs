using System;
using Kordon.Core.Combat;
using Kordon.Core.Loadout;
using Kordon.Core.Utility;
using Kordon.Game.Audio;
using Kordon.Game.Maps;
using UnityEngine;

namespace Kordon.Game.Weapons
{
    /// <summary>
    /// The only physical projectile in KORDON.  Flight is stepped through the
    /// deterministic <see cref="GrenadeTrajectory"/> model at a fixed rate, collision
    /// is a short ray sweep against the map, and the fuze burns whether the grenade is
    /// flying, rolling or lying still.  The same component runs on the authoritative
    /// server, so offline play and an online match share one set of physics.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class GrenadeProjectile : MonoBehaviour
    {
        public const float RadiusM = 0.07f;

        /// <summary>Server assigned id; clients use it to retire their visual copy.</summary>
        public int Id { get; private set; }

        public GrenadeDefinition Definition { get; private set; }

        /// <summary>Visual copies never decide damage; the server owns the blast.</summary>
        public bool VisualOnly { get; private set; }

        public GrenadeFlightState State => _state;

        public float FuseRemaining => _state.FuseRemaining;

        /// <summary>Raised once, in the same frame the fuze burns out.</summary>
        public event Action<GrenadeProjectile, Vector3> Detonated;

        private GrenadeFlightState _state;
        private float _accumulator;
        private float _bounceLock;
        private float _spin;
        private AudioDirector _audio;
        private bool _detonated;

        /// <summary>Throw from an operator's hand: arc, bounce, timed fuze.</summary>
        public static GrenadeProjectile Spawn(Transform parent, GrenadeDefinition definition,
            Vector3 origin, Vector3 direction, float cookSeconds, bool withVisual,
            AudioDirector audio, int id = 0)
        {
            if (definition == null)
            {
                return null;
            }

            GrenadeFlightState state = GrenadeTrajectory.Launch(definition, MapSpace.ToCore(origin),
                MapSpace.ToCore(direction), cookSeconds);
            return SpawnWithState(parent, definition, state, withVisual, audio, id);
        }

        /// <summary>Replay an already computed flight (server broadcast, reconnect catch-up).</summary>
        public static GrenadeProjectile SpawnWithState(Transform parent, GrenadeDefinition definition,
            GrenadeFlightState state, bool withVisual, AudioDirector audio, int id = 0)
        {
            if (definition == null)
            {
                return null;
            }

            GameObject go = new GameObject("Grenade_" + definition.Id);
            if (parent != null)
            {
                go.transform.SetParent(parent, false);
            }

            go.transform.position = MapSpace.ToUnity(state.Position);
            GrenadeProjectile projectile = go.AddComponent<GrenadeProjectile>();
            projectile.Id = id;
            projectile.Definition = definition;
            projectile.VisualOnly = !withVisual;
            projectile._state = state;
            projectile._audio = audio;
            projectile._spin = Mathf.Abs(state.Position.X * 3.1f + state.Position.Z);
            if (withVisual)
            {
                projectile.BuildVisual();
            }

            return projectile;
        }

        /// <summary>Authored model for each type of thrown equipment.</summary>
        public static string ModelPath(GrenadeKind kind)
        {
            switch (kind)
            {
                case GrenadeKind.Smoke: return "Kordon/Weapons/grenade_smoke";
                case GrenadeKind.Flashbang: return "Kordon/Weapons/grenade_flash";
                default: return "Kordon/Weapons/grenade_frag";
            }
        }

        private void BuildVisual()
        {
            GrenadeKind kind = Definition != null ? Definition.Kind : GrenadeKind.Fragmentation;
            GameObject prefab = Resources.Load<GameObject>(ModelPath(kind));
            if (prefab != null)
            {
                GameObject model = Instantiate(prefab, transform, false);
                model.name = "Body";
                SurfaceMaterialLibrary.Apply(model);
                return;
            }

            // Fallback so a missing model never hides the grenade in flight.
            GameObject body = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            body.name = "Body";
            body.transform.SetParent(transform, false);
            body.transform.localScale = new Vector3(RadiusM * 2f, RadiusM * 2.25f, RadiusM * 2f);

            Collider ownCollider = body.GetComponent<Collider>();
            if (ownCollider != null)
            {
                Destroy(ownCollider);
            }

            Renderer renderer = body.GetComponent<Renderer>();
            Material material = kind == GrenadeKind.Smoke
                ? SurfaceMaterialLibrary.ForSurface("rubber")
                : SurfaceMaterialLibrary.ForSurface("steel_dark");
            if (renderer != null && material != null)
            {
                renderer.sharedMaterial = material;
            }
        }

        private void Update()
        {
            if (_detonated)
            {
                return;
            }

            float dt = Mathf.Min(Time.deltaTime, 0.05f);
            _accumulator += dt;
            _bounceLock = Mathf.Max(0f, _bounceLock - dt);

            // Fixed step so the flight is frame rate independent on every machine.
            int guard = 0;
            while (_accumulator >= GrenadeTrajectory.FixedStep && guard++ < 12)
            {
                _accumulator -= GrenadeTrajectory.FixedStep;
                Step(GrenadeTrajectory.FixedStep);
                if (_detonated)
                {
                    return;
                }
            }

            transform.position = MapSpace.ToUnity(_state.Position);
            if (!_state.Resting)
            {
                _spin += dt * 340f * Mathf.Clamp01(_state.Speed / 8f);
                transform.rotation = Quaternion.Euler(_spin, _spin * 0.7f, _spin * 0.35f);
            }
        }

        private void Step(float dt)
        {
            KordonVec3 from = _state.Position;
            GrenadeTrajectory.Integrate(ref _state, dt);
            KordonVec3 delta = _state.Position - from;
            float distance = delta.Magnitude;

            if (distance > 0.00001f)
            {
                Vector3 unityFrom = MapSpace.ToUnity(from);
                Vector3 unityDelta = MapSpace.ToUnity(delta);
                Vector3 direction = unityDelta / distance;

                RaycastHit hit;
                if (Physics.Raycast(unityFrom, direction, out hit, distance + RadiusM))
                {
                    // Back the grenade off the surface by its own radius so it never
                    // ends up embedded in a wall.
                    _state.Position = MapSpace.ToCore(hit.point - direction * RadiusM);
                    bool bounced = GrenadeTrajectory.ResolveImpact(ref _state,
                        MapSpace.ToCore(hit.normal), out float impactSpeed);
                    if (!VisualOnly && bounced && _bounceLock <= 0f && impactSpeed > 0.8f)
                    {
                        _audio?.PlayGrenadeBounce(MapSpace.ToUnity(_state.Position), impactSpeed);
                        _bounceLock = 0.15f;
                    }
                }
            }

            if (_state.Position.Y < -25f)
            {
                // Fell out of the world: retire instead of simulating forever.
                _detonated = true;
                Destroy(gameObject);
                return;
            }

            if (_state.FuseExpired)
            {
                Detonate();
            }
        }

        private void Detonate()
        {
            if (_detonated)
            {
                return;
            }

            _detonated = true;
            Vector3 position = MapSpace.ToUnity(_state.Position);
            transform.position = position;
            Detonated?.Invoke(this, position);
            Destroy(gameObject);
        }

        /// <summary>Retire a visual copy without a blast (its server explosion already resolved).</summary>
        public void Retire()
        {
            _detonated = true;
            Destroy(gameObject);
        }
    }
}
