using System;
using System.Collections.Generic;
using Kordon.Core.Serialization;

namespace Kordon.Core.Settings
{
    public enum QualityPreset
    {
        Low = 0,
        Medium = 1,
        High = 2,
        Ultra = 3
    }

    /// <summary>How the magazine strip encodes full / partial / empty.</summary>
    public enum MagazineIndicatorMode
    {
        /// <summary>White, yellow, red blocks (default).</summary>
        ColorOnly = 0,

        /// <summary>Colour plus a distinct pattern per state, for colour blind players.</summary>
        ColorAndPattern = 1
    }

    public enum VoiceChannelPreference
    {
        LocalProximity = 0,
        TeamRadio = 1,
        Muted = 2
    }

    /// <summary>
    /// Persisted player settings.  Two design constraints are encoded as constants and
    /// deliberately have no setter, because they are fixed product decisions:
    ///
    ///   * <see cref="BodycamEffectsAlwaysOn"/> - the body camera treatment is always
    ///     active and cannot be switched off or weakened from the menu;
    ///   * <see cref="CrosshairEnabled"/> - there is no on-screen crosshair, aiming is
    ///     done through iron sights or an optic.
    /// </summary>
    [Serializable]
    public sealed class GameSettings
    {
        public const bool BodycamEffectsAlwaysOn = true;
        public const bool CrosshairEnabled = false;

        public const float MinFieldOfView = 55f;
        public const float MaxFieldOfView = 100f;
        public const float DefaultFieldOfView = 74f;

        // ---- mouse
        public float MouseSensitivity = 1.0f;
        public float AimSensitivityMultiplier = 0.62f;
        public bool InvertVerticalLook = false;
        public bool MouseSmoothing = false;

        // ---- video
        public QualityPreset Quality = QualityPreset.High;
        public int ResolutionWidth = 1920;
        public int ResolutionHeight = 1080;
        public bool Fullscreen = true;
        public bool VSync = true;
        public float FieldOfView = DefaultFieldOfView;
        public float RenderScale = 1f;
        public float ViewDistanceMultiplier = 1f;

        // ---- audio
        public float MasterVolume = 0.9f;
        public float EffectsVolume = 1f;
        public float WeaponVolume = 1f;
        public float AmbienceVolume = 0.85f;
        public float VoiceVolume = 1f;
        public float UiVolume = 0.7f;
        public bool VoiceActivation = false;
        public float VoiceActivationThreshold = 0.06f;
        public VoiceChannelPreference DefaultVoiceChannel = VoiceChannelPreference.LocalProximity;
        public bool LoudnessNormalization = true;

        // ---- accessibility
        public bool SubtitlesEnabled = true;
        public float SubtitleScale = 1f;
        public bool SubtitlesShowSpeaker = true;
        public MagazineIndicatorMode MagazineIndicators = MagazineIndicatorMode.ColorOnly;
        public bool ShowPing = true;
        public bool ShowKillFeed = true;
        public bool ReducedUiMotion = false;

        // ---- gameplay / control style
        public bool ToggleSprint = false;
        public bool ToggleCrouch = false;
        public LeanInputMode LeanMode = LeanInputMode.Hold;
        public bool HoldToAim = true;
        public bool HitmarkerSound = true;
        public bool ShowHitDirection = true;

        /// <summary>Named control profiles, at least one of which is active.</summary>
        public List<KeyBindings> Profiles = new List<KeyBindings>();
        public string ActiveProfileName = "Default";

        public GameSettings()
        {
            Profiles.Add(KeyBindings.CreateDefault());
            Profiles.Add(KeyBindings.CreateTactical());
        }

        public KeyBindings ActiveProfile
        {
            get
            {
                foreach (KeyBindings profile in Profiles)
                {
                    if (profile.ProfileName == ActiveProfileName)
                    {
                        return profile;
                    }
                }

                if (Profiles.Count == 0)
                {
                    Profiles.Add(KeyBindings.CreateDefault());
                }

                ActiveProfileName = Profiles[0].ProfileName;
                return Profiles[0];
            }
        }

        public int GetKey(InputAction action) => ActiveProfile.GetKey(action);

        public bool TryRebind(InputAction action, int keyCode, out List<InputAction> displaced) =>
            ActiveProfile.Rebind(action, keyCode, out displaced);

        public KeyBindings AddProfile(string name)
        {
            KeyBindings bindings = KeyBindings.CreateDefault(name);
            Profiles.Add(bindings);
            ActiveProfileName = name;
            return bindings;
        }

        /// <summary>Clamp every value into its legal range. Called on load and on save.</summary>
        public List<string> ValidateAndClamp()
        {
            List<string> notes = new List<string>();

            MouseSensitivity = Utility.MathUtil.Clamp(MouseSensitivity, 0.05f, 8f);
            AimSensitivityMultiplier = Utility.MathUtil.Clamp(AimSensitivityMultiplier, 0.1f, 1.5f);
            FieldOfView = Utility.MathUtil.Clamp(FieldOfView, MinFieldOfView, MaxFieldOfView);
            RenderScale = Utility.MathUtil.Clamp(RenderScale, 0.6f, 2f);
            ViewDistanceMultiplier = Utility.MathUtil.Clamp(ViewDistanceMultiplier, 0.5f, 2f);

            MasterVolume = Utility.MathUtil.Clamp01(MasterVolume);
            EffectsVolume = Utility.MathUtil.Clamp01(EffectsVolume);
            WeaponVolume = Utility.MathUtil.Clamp01(WeaponVolume);
            AmbienceVolume = Utility.MathUtil.Clamp01(AmbienceVolume);
            VoiceVolume = Utility.MathUtil.Clamp01(VoiceVolume);
            UiVolume = Utility.MathUtil.Clamp01(UiVolume);
            VoiceActivationThreshold = Utility.MathUtil.Clamp01(VoiceActivationThreshold);
            SubtitleScale = Utility.MathUtil.Clamp(SubtitleScale, 0.7f, 2f);

            if (ResolutionWidth < 640 || ResolutionHeight < 480)
            {
                ResolutionWidth = 1920;
                ResolutionHeight = 1080;
                notes.Add("resolution-reset-to-1080p");
            }

            List<string> bindingProblems = ActiveProfile.ValidateRequiredBindings();
            if (bindingProblems.Count > 0)
            {
                notes.AddRange(bindingProblems);
                Profiles.Remove(ActiveProfile);
                Profiles.Insert(0, KeyBindings.CreateDefault());
                ActiveProfileName = "Default";
            }

            return notes;
        }

        public string ToJson()
        {
            Dictionary<string, object> root = JsonObject.New()
                .With("schemaVersion", 1)
                .With("mouseSensitivity", MouseSensitivity)
                .With("aimSensitivityMultiplier", AimSensitivityMultiplier)
                .With("invertVerticalLook", InvertVerticalLook)
                .With("mouseSmoothing", MouseSmoothing)
                .With("quality", Quality.ToString())
                .With("resolutionWidth", ResolutionWidth)
                .With("resolutionHeight", ResolutionHeight)
                .With("fullscreen", Fullscreen)
                .With("vsync", VSync)
                .With("fieldOfView", FieldOfView)
                .With("renderScale", RenderScale)
                .With("viewDistanceMultiplier", ViewDistanceMultiplier)
                .With("masterVolume", MasterVolume)
                .With("effectsVolume", EffectsVolume)
                .With("weaponVolume", WeaponVolume)
                .With("ambienceVolume", AmbienceVolume)
                .With("voiceVolume", VoiceVolume)
                .With("uiVolume", UiVolume)
                .With("voiceActivation", VoiceActivation)
                .With("voiceActivationThreshold", VoiceActivationThreshold)
                .With("defaultVoiceChannel", DefaultVoiceChannel.ToString())
                .With("loudnessNormalization", LoudnessNormalization)
                .With("subtitlesEnabled", SubtitlesEnabled)
                .With("subtitleScale", SubtitleScale)
                .With("subtitlesShowSpeaker", SubtitlesShowSpeaker)
                .With("magazineIndicators", MagazineIndicators.ToString())
                .With("showPing", ShowPing)
                .With("showKillFeed", ShowKillFeed)
                .With("reducedUiMotion", ReducedUiMotion)
                .With("toggleSprint", ToggleSprint)
                .With("toggleCrouch", ToggleCrouch)
                .With("leanMode", LeanMode.ToString())
                .With("holdToAim", HoldToAim)
                .With("hitmarkerSound", HitmarkerSound)
                .With("showHitDirection", ShowHitDirection)
                .With("activeProfile", ActiveProfileName)
                .With("bodycamEffectsAlwaysOn", BodycamEffectsAlwaysOn)
                .With("crosshairEnabled", CrosshairEnabled);

            List<object> profiles = new List<object>();
            foreach (KeyBindings profile in Profiles)
            {
                profiles.Add(Json.Parse(profile.ToJson()).Raw);
            }

            root["profiles"] = profiles;
            return MiniJson.Write(root, true);
        }

        public static GameSettings FromJson(string json)
        {
            GameSettings settings = new GameSettings();
            if (string.IsNullOrEmpty(json))
            {
                return settings;
            }

            Json root = Json.Parse(json);
            settings.MouseSensitivity = root["mouseSensitivity"].AsFloat(settings.MouseSensitivity);
            settings.AimSensitivityMultiplier =
                root["aimSensitivityMultiplier"].AsFloat(settings.AimSensitivityMultiplier);
            settings.InvertVerticalLook = root["invertVerticalLook"].AsBool(settings.InvertVerticalLook);
            settings.MouseSmoothing = root["mouseSmoothing"].AsBool(settings.MouseSmoothing);
            settings.Quality = root["quality"].AsEnum(settings.Quality);
            settings.ResolutionWidth = root["resolutionWidth"].AsInt(settings.ResolutionWidth);
            settings.ResolutionHeight = root["resolutionHeight"].AsInt(settings.ResolutionHeight);
            settings.Fullscreen = root["fullscreen"].AsBool(settings.Fullscreen);
            settings.VSync = root["vsync"].AsBool(settings.VSync);
            settings.FieldOfView = root["fieldOfView"].AsFloat(settings.FieldOfView);
            settings.RenderScale = root["renderScale"].AsFloat(settings.RenderScale);
            settings.ViewDistanceMultiplier = root["viewDistanceMultiplier"].AsFloat(settings.ViewDistanceMultiplier);
            settings.MasterVolume = root["masterVolume"].AsFloat(settings.MasterVolume);
            settings.EffectsVolume = root["effectsVolume"].AsFloat(settings.EffectsVolume);
            settings.WeaponVolume = root["weaponVolume"].AsFloat(settings.WeaponVolume);
            settings.AmbienceVolume = root["ambienceVolume"].AsFloat(settings.AmbienceVolume);
            settings.VoiceVolume = root["voiceVolume"].AsFloat(settings.VoiceVolume);
            settings.UiVolume = root["uiVolume"].AsFloat(settings.UiVolume);
            settings.VoiceActivation = root["voiceActivation"].AsBool(settings.VoiceActivation);
            settings.VoiceActivationThreshold =
                root["voiceActivationThreshold"].AsFloat(settings.VoiceActivationThreshold);
            settings.DefaultVoiceChannel = root["defaultVoiceChannel"].AsEnum(settings.DefaultVoiceChannel);
            settings.LoudnessNormalization = root["loudnessNormalization"].AsBool(settings.LoudnessNormalization);
            settings.SubtitlesEnabled = root["subtitlesEnabled"].AsBool(settings.SubtitlesEnabled);
            settings.SubtitleScale = root["subtitleScale"].AsFloat(settings.SubtitleScale);
            settings.SubtitlesShowSpeaker = root["subtitlesShowSpeaker"].AsBool(settings.SubtitlesShowSpeaker);
            settings.MagazineIndicators = root["magazineIndicators"].AsEnum(settings.MagazineIndicators);
            settings.ShowPing = root["showPing"].AsBool(settings.ShowPing);
            settings.ShowKillFeed = root["showKillFeed"].AsBool(settings.ShowKillFeed);
            settings.ReducedUiMotion = root["reducedUiMotion"].AsBool(settings.ReducedUiMotion);
            settings.ToggleSprint = root["toggleSprint"].AsBool(settings.ToggleSprint);
            settings.ToggleCrouch = root["toggleCrouch"].AsBool(settings.ToggleCrouch);
            settings.LeanMode = root["leanMode"].AsEnum(settings.LeanMode);
            settings.HoldToAim = root["holdToAim"].AsBool(settings.HoldToAim);
            settings.HitmarkerSound = root["hitmarkerSound"].AsBool(settings.HitmarkerSound);
            settings.ShowHitDirection = root["showHitDirection"].AsBool(settings.ShowHitDirection);

            Json profiles = root["profiles"];
            if (!profiles.IsNull && profiles.Count > 0)
            {
                settings.Profiles.Clear();
                foreach (Json profile in profiles.Items())
                {
                    settings.Profiles.Add(KeyBindings.FromJson(MiniJson.Write(profile.Raw, false)));
                }
            }

            settings.ActiveProfileName = root["activeProfile"].AsString(settings.ActiveProfileName);
            settings.ValidateAndClamp();
            return settings;
        }
    }
}
