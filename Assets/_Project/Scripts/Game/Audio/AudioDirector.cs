using System.Collections.Generic;
using Kordon.Core.Ammo;
using Kordon.Core.Movement;
using Kordon.Game.Maps;
using Kordon.Game.Player;
using Kordon.Game.Settings;
using UnityEngine;

namespace Kordon.Game.Audio
{
    /// <summary>
    /// All sound in one place.  Every clip is procedural (Tools/audio), loaded from
    /// Resources, and positioned in the world: footsteps follow the surface you are
    /// walking on, gunfire changes voice with distance and with the indoor/outdoor
    /// zone the listener stands in, and the bodycam layer (breathing, heartbeat,
    /// radio) rides on top of the mix.
    ///
    /// The rules say no silent movement, so the director listens to the same noise
    /// events the AI and the network use: what a hostile can hear is exactly what
    /// the player hears.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class AudioDirector : MonoBehaviour
    {
        private const int PoolSize = 28;

        private readonly Dictionary<string, AudioClip> _clips = new Dictionary<string, AudioClip>();
        private readonly List<AudioSource> _pool = new List<AudioSource>();
        private readonly List<AudioLowPassFilter> _filters = new List<AudioLowPassFilter>();
        private int _cursor;

        public MapLayout Layout { get; set; }
        public PlayerController Listener { get; set; }
        public float EffectsVolume = 0.85f;

        private AudioSource _ambience;
        private AudioSource _bodycam;
        private float _breathTimer;
        private bool _wasIndoor;

        private void Awake()
        {
            for (int i = 0; i < PoolSize; i++)
            {
                GameObject go = new GameObject("Voice" + i);
                go.transform.SetParent(transform, false);
                AudioSource source = go.AddComponent<AudioSource>();
                source.playOnAwake = false;
                source.spatialBlend = 1f;
                source.minDistance = 2.5f;
                source.maxDistance = 120f;
                source.rolloffMode = AudioRolloffMode.Linear;
                _pool.Add(source);
                _filters.Add(go.AddComponent<AudioLowPassFilter>());
            }

            _ambience = CreateLoopSource("Ambience", 0f);
            _bodycam = CreateLoopSource("Bodycam", 0f);
        }

        private AudioSource CreateLoopSource(string name, float spatialBlend)
        {
            GameObject go = new GameObject(name);
            go.transform.SetParent(transform, false);
            AudioSource source = go.AddComponent<AudioSource>();
            source.loop = true;
            source.playOnAwake = false;
            source.spatialBlend = spatialBlend;
            source.volume = 0f;
            return source;
        }

        // ------------------------------------------------------------------ api

        /// <summary>Gunshot at a world position, voiced by weapon class and distance.</summary>
        public void PlayShot(Vector3 position, string weaponId, bool suppressed)
        {
            string cls = WeaponClassFor(weaponId);
            float distance = ListenerDistance(position);
            bool far = distance > 34f;
            string clipName = "fire_" + cls + "_" + (far ? "far" : Shuffle(1, 3));
            AudioClip clip = Load(clipName);
            if (clip == null)
            {
                return;
            }

            float volume = suppressed ? 0.35f : Mathf.Clamp01(1.35f - distance / 110f);
            PlayAt(clip, position, volume * EffectsVolume, 1f - Random.Range(0f, 0.03f) +
                   (far ? -0.12f : 0f), far ? 1400f : 20000f);
        }

        /// <summary>A footstep on an authored surface, quieter when crouched or unarmoured.</summary>
        public void PlayFootstep(Vector3 position, string surface, Gait gait, float gearMassKg)
        {
            string kind = SurfaceBank(surface);
            AudioClip clip = Load("step_" + kind + "_" + Shuffle(1, 3));
            if (clip == null)
            {
                return;
            }

            float stance = gait == Gait.CrouchWalk ? 0.45f : gait == Gait.ProneCrawl ? 0.25f : 1f;
            float gear = Mathf.Clamp(0.75f + gearMassKg / 40f, 0.75f, 1.15f);
            PlayAt(clip, position, 0.55f * stance * gear * EffectsVolume,
                0.94f + Random.Range(0f, 0.12f), 20000f);
        }

        public void PlayLanding(Vector3 position, float fallHeight)
        {
            AudioClip clip = Load("land_" + Shuffle(1, 2));
            PlayAt(clip, position, Mathf.Clamp01(0.4f + fallHeight * 0.15f) * EffectsVolume,
                1f, 20000f);
        }

        /// <summary>The real magazine beats: the sound is always the state machine's sound.</summary>
        public void PlayAmmoEvent(Vector3 position, AmmoEventKind kind)
        {
            string clipName;
            switch (kind)
            {
                case AmmoEventKind.MagazineOut: clipName = "mag_out"; break;
                case AmmoEventKind.MagazineIn: clipName = "mag_in"; break;
                case AmmoEventKind.BoltRelease: clipName = "bolt_release"; break;
                case AmmoEventKind.ChamberRound: clipName = "charge_handle"; break;
                case AmmoEventKind.DryFireClick: clipName = "dry_click"; break;
                case AmmoEventKind.InspectStart:
                case AmmoEventKind.InspectReadable:
                case AmmoEventKind.InspectEnd: clipName = "inspect_rustle"; break;
                case AmmoEventKind.FumbleRecovered: clipName = "charge_handle"; break;
                case AmmoEventKind.GrenadeThrow: clipName = "grenade_pin"; break;
                default: return;
            }

            PlayAt(Load(clipName), position, 0.7f * EffectsVolume, 1f, 20000f);
        }

        public void PlayHit(Vector3 position, bool armor)
        {
            AudioClip clip = Load(armor ? "hit_armor_" + Shuffle(1, 2) : "hit_flesh_" + Shuffle(1, 2));
            PlayAt(clip, position, 0.9f * EffectsVolume, 1f, 20000f);
        }

        public void PlayImpact(Vector3 position, string surface)
        {
            string kind = SurfaceBank(surface) == "metal" ? "impact_metal" : "impact_concrete";
            PlayAt(Load(kind), position, 0.6f * EffectsVolume, 1f, 20000f);
        }

        public void PlaySuppressionWhizz(Vector3 position) =>
            PlayAt(Load("whizz_" + Shuffle(1, 2)), position, 0.75f * EffectsVolume, 1f, 20000f);

        public void PlayUiCue(string clipName, float volume = 0.6f) =>
            Play2D(Load(clipName), volume * EffectsVolume);

        public void PlayBodycamBoot() => Play2D(Load("bodycam_beep_double"), 0.5f * EffectsVolume);

        public void PlayObjectiveChime() => Play2D(Load("objective_chime"), 0.45f * EffectsVolume);

        public void PlayExtractionSiren(Vector3 position) =>
            PlayAt(Load("extraction_siren"), position, 0.8f * EffectsVolume, 1f, 20000f);

        public void PlayRadio() => Play2D(Load("radio_static"), 0.35f * EffectsVolume);

        // -------------------------------------------------------------- per frame

        private void Update()
        {
            if (Listener == null)
            {
                return;
            }

            UpdateAmbience();
            UpdateBodycamLayer();

            EffectsVolume = SettingsStore.Load().EffectsVolume;
        }

        private void UpdateAmbience()
        {
            bool indoor = IsIndoor(Listener.transform.position);
            if (indoor != _wasIndoor)
            {
                _wasIndoor = indoor;
                _ambience.clip = Load(indoor ? "indoor_hum_loop" : "wind_loop");
                _ambience.volume = indoor ? 0.16f : 0.22f;
                if (_ambience.clip != null)
                {
                    _ambience.Play();
                }
            }

            if (_ambience.clip == null && !_ambience.isPlaying)
            {
                _ambience.clip = Load("wind_loop");
                _ambience.volume = 0.2f;
                if (_ambience.clip != null)
                {
                    _ambience.Play();
                }
            }
        }

        private void UpdateBodycamLayer()
        {
            float breath = Listener.Stamina.BreathIntensity;
            _breathTimer -= Time.deltaTime;
            if (breath > 0.45f && _breathTimer <= 0f)
            {
                _breathTimer = Mathf.Lerp(3.6f, 1.5f, breath);
                Play2D(Load(_breathTimer < 2.3f ? "breath_out" : "breath_in"),
                    0.32f * breath * EffectsVolume);
            }

            if (Listener.Vitals.Health / Core.Combat.PlayerVitals.MaxHealth < 0.35f)
            {
                float interval = 1.15f;
                if (_breathTimer <= 0f)
                {
                    _breathTimer = interval;
                    Play2D(Load("heartbeat"), 0.4f * EffectsVolume);
                }
            }
        }

        /// <summary>True inside any authored indoor audio zone (drives reverb and the ambience bed).</summary>
        public bool IsIndoor(Vector3 worldPosition)
        {
            if (Layout == null)
            {
                return false;
            }

            float x, y;
            ToMap(worldPosition, out x, out y);
            foreach (MapAudioZone zone in Layout.AudioZones)
            {
                if (zone.Kind == "Indoor" && zone.Region.Contains(x, y) &&
                    worldPosition.y <= zone.Height + 0.5f)
                {
                    return true;
                }
            }

            return false;
        }

        // --------------------------------------------------------------- helpers

        private static void ToMap(Vector3 world, out float x, out float y)
        {
            x = world.x;
            y = world.z;
        }

        private float ListenerDistance(Vector3 position) =>
            Listener == null ? 0f : Vector3.Distance(Listener.transform.position, position);

        private int Shuffle(int min, int max) => Random.Range(min, max + 1);

        private void PlayAt(AudioClip clip, Vector3 position, float volume, float pitch,
            float cutoff)
        {
            if (clip == null || volume <= 0.001f)
            {
                return;
            }

            AudioSource source = _pool[_cursor];
            AudioLowPassFilter filter = _filters[_cursor];
            _cursor = (_cursor + 1) % _pool.Count;

            source.transform.position = position;
            source.clip = clip;
            source.volume = Mathf.Clamp01(volume);
            source.pitch = Mathf.Clamp(pitch, 0.7f, 1.3f);
            filter.cutoffFrequency = cutoff;
            source.Play();
        }

        private void Play2D(AudioClip clip, float volume)
        {
            if (clip == null)
            {
                return;
            }

            AudioSource source = _pool[_cursor];
            _cursor = (_cursor + 1) % _pool.Count;
            source.transform.position = Listener != null ? Listener.transform.position : Vector3.zero;
            source.spatialBlend = 0f;
            source.clip = clip;
            source.volume = Mathf.Clamp01(volume);
            source.pitch = 1f;
            source.Play();
            source.spatialBlend = 1f;
        }

        private AudioClip Load(string name)
        {
            if (string.IsNullOrEmpty(name))
            {
                return null;
            }

            if (_clips.TryGetValue(name, out AudioClip cached))
            {
                return cached;
            }

            AudioClip clip = Resources.Load<AudioClip>("Kordon/Audio/" + name);
            _clips[name] = clip;
            return clip;
        }

        /// <summary>Authored surface id to the closest footstep bank.</summary>
        public static string SurfaceBank(string surface)
        {
            if (string.IsNullOrEmpty(surface))
            {
                return "concrete";
            }

            switch (surface)
            {
                case "sand": return "sand";
                case "gravel": return "gravel";
                case "wood_crate": return "wood";
                case "grating":
                case "sheet_metal":
                case "hardened_steel":
                case "steel_dark":
                case "steel_painted":
                case "steel_rust": return "metal";
                case "glass": return "concrete";
                case "rubber": return "sand";
                default: return "concrete";
            }
        }

        public static string WeaponClassFor(string weaponId)
        {
            switch (weaponId)
            {
                case "vk74": return "rifle_545";
                case "tr30": return "rifle_762";
                case "vs9": return "smg_9";
                case "dmr14": return "dmr_54r";
                case "sr338": return "sniper_338";
                case "k870": return "shotgun_12";
                case "p9": return "pistol_9";
                default: return "rifle_545";
            }
        }
    }
}
