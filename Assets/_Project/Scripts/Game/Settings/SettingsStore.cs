using Kordon.Core.Settings;
using UnityEngine;

namespace Kordon.Game.Settings
{
    /// <summary>
    /// Persists and applies the Core settings object. Kordon.Core owns every setting
    /// (sensitivity, volumes, quality, FOV, subtitles, key bindings) because the
    /// server validates the same rules; this is only the storage and the Unity side
    /// application of them.
    ///
    /// Deliberately missing: a bodycam toggle and a crosshair switch. Both are fixed
    /// design decisions in Kordon.Core and are not settings at all.
    /// </summary>
    public static class SettingsStore
    {
        private const string Key = "kordon.settings.v2";

        public static GameSettings Load()
        {
            string json = PlayerPrefs.GetString(Key, string.Empty);
            GameSettings settings = string.IsNullOrEmpty(json)
                ? new GameSettings()
                : GameSettings.FromJson(json);
            settings.ValidateAndClamp();
            return settings;
        }

        public static void Save(GameSettings settings)
        {
            if (settings == null)
            {
                return;
            }

            settings.ValidateAndClamp();
            PlayerPrefs.SetString(Key, settings.ToJson());
            PlayerPrefs.Save();
        }

        /// <summary>Pushes the settings that Unity itself owns.</summary>
        public static void Apply(GameSettings settings)
        {
            if (settings == null)
            {
                return;
            }

            Screen.SetResolution(settings.ResolutionWidth, settings.ResolutionHeight,
                settings.Fullscreen);
            UnityEngine.QualitySettings.SetQualityLevel(
                Mathf.Clamp((int)settings.Quality, 0, 5), true);
            AudioListener.volume = Mathf.Clamp01(settings.MasterVolume);
        }

        public static string QualityName(QualityPreset preset)
        {
            switch (preset)
            {
                case QualityPreset.Low: return "low";
                case QualityPreset.Medium: return "medium";
                case QualityPreset.High: return "high";
                default: return "ultra";
            }
        }
    }
}
