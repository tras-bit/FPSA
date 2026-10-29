using Kordon.Core.Loadout;
using Kordon.Game.Audio;
using Kordon.Game.Rendering;
using UnityEngine;

namespace Kordon.Game.Weapons
{
    /// <summary>
    /// One growing, fading, unlit volume: the building block of the explosion flash,
    /// the dust ring and the smoke cloud.  It uses the project's own
    /// Kordon/GrenadeFx shader, so no imported texture or effect package is needed.
    /// </summary>
    public sealed class GrenadeEffectVolume : MonoBehaviour
    {
        public float Lifetime = 1f;
        public float StartScale = 0.4f;
        public float EndScale = 3f;
        public Color StartColor = Color.white;
        public Color EndColor = new Color(1f, 1f, 1f, 0f);
        public float RiseSpeed;
        public float DriftSpeed;

        private float _age;
        private float _seed;
        private Material _material;
        private Transform _transform;

        public static GrenadeEffectVolume Create(Transform parent, Vector3 position,
            float startScale, float endScale, Color startColor, Color endColor, float lifetime,
            float riseSpeed = 0f, float driftSpeed = 0f, Shader shader = null)
        {
            GameObject go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            go.name = "GrenadeFx";
            Collider collider = go.GetComponent<Collider>();
            if (collider != null)
            {
                Object.Destroy(collider);
            }

            if (parent != null)
            {
                go.transform.SetParent(parent, false);
            }

            go.transform.position = position;
            go.transform.localScale = Vector3.one * startScale;

            Renderer renderer = go.GetComponent<Renderer>();
            Material material = null;
            if (shader != null && renderer != null)
            {
                material = new Material(shader);
                material.color = startColor;
                renderer.sharedMaterial = material;
            }

            GrenadeEffectVolume volume = go.AddComponent<GrenadeEffectVolume>();
            volume.Lifetime = Mathf.Max(0.05f, lifetime);
            volume.StartScale = startScale;
            volume.EndScale = endScale;
            volume.StartColor = startColor;
            volume.EndColor = endColor;
            volume.RiseSpeed = riseSpeed;
            volume.DriftSpeed = driftSpeed;
            volume._material = material;
            volume._transform = go.transform;
            volume._seed = Mathf.Abs(position.x * 0.37f + position.z * 0.73f) % 6.28f;
            return volume;
        }

        private void Update()
        {
            float dt = Mathf.Min(Time.deltaTime, 0.1f);
            _age += dt;
            float t = Mathf.Clamp01(_age / Lifetime);

            if (_transform != null)
            {
                float scale = Mathf.Lerp(StartScale, EndScale, t);
                _transform.localScale = Vector3.one * scale;
                Vector3 drift = new Vector3(Mathf.Sin(_seed + _age * 1.7f) * DriftSpeed,
                                            RiseSpeed,
                                            Mathf.Cos(_seed + _age * 1.3f) * DriftSpeed);
                _transform.position += drift * dt;
            }

            if (_material != null)
            {
                _material.color = Color.Lerp(StartColor, EndColor, t);
            }

            if (t >= 1f)
            {
                Destroy(gameObject);
            }
        }
    }

    /// <summary>A point light that fades out and removes itself (explosion / muzzle flash).</summary>
    public sealed class GrenadeFlashLight : MonoBehaviour
    {
        public float Lifetime = 0.18f;
        public float PeakIntensity = 6f;

        private float _age;
        private Light _light;

        public static void Create(Transform parent, Vector3 position, Color color, float range,
            float peakIntensity, float lifetime)
        {
            GameObject go = new GameObject("GrenadeLight");
            if (parent != null)
            {
                go.transform.SetParent(parent, false);
            }

            go.transform.position = position;
            Light light = go.AddComponent<Light>();
            light.color = color;
            light.range = range;
            light.intensity = peakIntensity;

            GrenadeFlashLight fade = go.AddComponent<GrenadeFlashLight>();
            fade.Lifetime = Mathf.Max(0.05f, lifetime);
            fade.PeakIntensity = peakIntensity;
            fade._light = light;
        }

        private void Update()
        {
            _age += Mathf.Min(Time.deltaTime, 0.1f);
            float t = Mathf.Clamp01(_age / Lifetime);
            if (_light != null)
            {
                _light.intensity = PeakIntensity * (1f - t) * (1f - t);
            }

            if (t >= 1f)
            {
                Destroy(gameObject);
            }
        }
    }

    /// <summary>
    /// Grenade presentation: what each type looks and sounds like when it goes off.
    /// The damage is never decided here - this only renders the result the rules
    /// already produced, so a client can never "look" like it hurt someone.
    /// </summary>
    public static class GrenadeEffects
    {
        private static Shader _shader;
        private static bool _shaderProbed;

        private static Shader FxShader
        {
            get
            {
                if (!_shaderProbed)
                {
                    _shaderProbed = true;
                    // Resources first: a material-less shader would be stripped from
                    // the player build and the effects would silently disappear.
                    _shader = ShaderLibrary.GrenadeFx();
                }

                return _shader;
            }
        }

        public static void Detonate(Transform parent, GrenadeDefinition definition, Vector3 position,
            AudioDirector audio)
        {
            if (definition == null)
            {
                return;
            }

            switch (definition.Kind)
            {
                case GrenadeKind.Fragmentation:
                    Explosion(parent, position, definition.DamageRadiusM, audio);
                    break;
                case GrenadeKind.Smoke:
                    Smoke(parent, position, definition.EffectRadiusM,
                        definition.EffectDurationSeconds, audio);
                    break;
                case GrenadeKind.Flashbang:
                    Flash(parent, position, definition.EffectRadiusM, audio);
                    break;
            }
        }

        /// <summary>Fragmentation: hot core, dust ring, debris kicks and a smoke plume.</summary>
        private static void Explosion(Transform parent, Vector3 position, float radius,
            AudioDirector audio)
        {
            float scale = Mathf.Clamp(radius * 0.18f, 1.4f, 3.2f);

            GrenadeEffectVolume.Create(parent, position, 0.35f * scale, 1.15f * scale,
                new Color(1f, 0.86f, 0.55f, 1f), new Color(1f, 0.35f, 0.06f, 0f), 0.16f,
                0f, 0f, FxShader);

            for (int i = 0; i < 3; i++)
            {
                Vector3 offset = new Vector3(
                    Mathf.Cos(i * 2.094f) * 0.55f,
                    0.05f,
                    Mathf.Sin(i * 2.094f) * 0.55f);
                GrenadeEffectVolume.Create(parent, position + offset, 0.5f * scale,
                    1.9f * scale, new Color(0.42f, 0.38f, 0.32f, 0.5f),
                    new Color(0.30f, 0.28f, 0.24f, 0f), 1.35f, 0.25f, 0.22f, FxShader);
            }

            GrenadeEffectVolume.Create(parent, position + Vector3.up * 0.4f, 0.7f * scale,
                2.4f * scale, new Color(0.18f, 0.18f, 0.17f, 0.45f),
                new Color(0.14f, 0.14f, 0.13f, 0f), 2.6f, 0.55f, 0.12f, FxShader);

            for (int i = 0; i < 6; i++)
            {
                float angle = i * 1.047f;
                Vector3 offset = new Vector3(Mathf.Cos(angle) * 0.25f, 0.12f,
                    Mathf.Sin(angle) * 0.25f);
                GrenadeEffectVolume.Create(parent, position + offset, 0.09f, 0.22f,
                    new Color(1f, 0.78f, 0.4f, 0.95f), new Color(0.6f, 0.2f, 0.05f, 0f),
                    0.35f, 0f, 0.02f, FxShader);
            }

            GrenadeFlashLight.Create(parent, position + Vector3.up * 0.3f,
                new Color(1f, 0.72f, 0.38f), Mathf.Max(6f, radius * 1.4f), 9f, 0.22f);
            audio?.PlayExplosion(position, GrenadeKind.Fragmentation);
        }

        /// <summary>Smoke: a slow, dense, long lived cloud that the AI also cannot see through.</summary>
        private static void Smoke(Transform parent, Vector3 position, float radius,
            float seconds, AudioDirector audio)
        {
            float lifetime = Mathf.Clamp(seconds, 3f, 40f);
            float scale = Mathf.Clamp(radius * 0.55f, 2f, 6f);

            for (int i = 0; i < 7; i++)
            {
                float angle = i * 0.897f;
                float spread = (0.35f + (i % 3) * 0.28f) * scale;
                Vector3 offset = new Vector3(Mathf.Cos(angle) * spread, 0.35f + (i % 2) * 0.5f,
                    Mathf.Sin(angle) * spread);
                GrenadeEffectVolume.Create(parent, position + offset, 0.6f * scale,
                    scale, new Color(0.74f, 0.75f, 0.73f, 0.42f),
                    new Color(0.66f, 0.67f, 0.66f, 0.16f), lifetime, 0.22f, 0.1f, FxShader);
            }

            // The cloud is a real occlusion volume, not a picture: it is registered in
            // the shared smoke field so the AI and the server cannot see through it.
            SmokeScreen.Add(position + Vector3.up * 0.4f, radius * 0.85f, lifetime);
            audio?.PlayExplosion(position, GrenadeKind.Smoke);
        }

        /// <summary>Flash: a very short white bloom plus a hard light spike.</summary>
        private static void Flash(Transform parent, Vector3 position, float radius,
            AudioDirector audio)
        {
            float scale = Mathf.Clamp(radius * 0.25f, 1.5f, 3f);
            GrenadeEffectVolume.Create(parent, position, 0.4f * scale, 1.2f * scale,
                new Color(1f, 1f, 1f, 1f), new Color(1f, 1f, 1f, 0f), 0.22f, 0f, 0f, FxShader);
            GrenadeFlashLight.Create(parent, position + Vector3.up * 0.3f, Color.white,
                Mathf.Max(8f, radius * 1.6f), 14f, 0.3f);
            audio?.PlayExplosion(position, GrenadeKind.Flashbang);
        }
    }
}
