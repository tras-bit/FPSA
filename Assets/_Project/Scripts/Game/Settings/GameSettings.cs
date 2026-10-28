using Kordon.Game.Input;
using UnityEngine;
using UnityEngine.UI;

namespace Kordon.Game.Settings
{
    /// <summary>
    /// Persisted player settings: look sensitivity, volumes, quality, subtitles.
    /// Bodycam processing is deliberately NOT here - it has no setting, by design.
    /// </summary>
    public sealed class GameSettings
    {
        public float Sensitivity = 2.4f;
        public float MasterVolume = 0.9f;
        public float EffectsVolume = 0.85f;
        public float VoiceVolume = 0.8f;
        public bool Subtitles = true;
        public int QualityLevel = 2;
        public bool Fullscreen = true;
        public int ResolutionWidth = 1920;
        public int ResolutionHeight = 1080;

        private const string Key = "kordon.settings.v1";

        public void Apply()
        {
            Screen.SetResolution(ResolutionWidth, ResolutionHeight, Fullscreen);
            QualitySettings.SetLevel(QualityLevel);
        }

        public string Serialize() =>
            string.Format(System.Globalization.CultureInfo.InvariantCulture,
                "{0:F3}|{1:F3}|{2:F3}|{3:F3}|{4}|{5}|{6}|{7}|{8}",
                Sensitivity, MasterVolume, EffectsVolume, VoiceVolume,
                Subtitles ? 1 : 0, QualityLevel, Fullscreen ? 1 : 0,
                ResolutionWidth, ResolutionHeight);

        public void Deserialize(string data)
        {
            if (string.IsNullOrEmpty(data))
            {
                return;
            }

            string[] parts = data.Split('|');
            if (parts.Length < 9)
            {
                return;
            }

            System.Globalization.CultureInfo culture =
                System.Globalization.CultureInfo.InvariantCulture;
            float.TryParse(parts[0], System.Globalization.NumberStyles.Float, culture,
                out Sensitivity);
            float.TryParse(parts[1], System.Globalization.NumberStyles.Float, culture,
                out MasterVolume);
            float.TryParse(parts[2], System.Globalization.NumberStyles.Float, culture,
                out EffectsVolume);
            float.TryParse(parts[3], System.Globalization.NumberStyles.Float, culture,
                out VoiceVolume);
            Subtitles = parts[4] == "1";
            int.TryParse(parts[5], out QualityLevel);
            Fullscreen = parts[6] == "1";
            int.TryParse(parts[7], out ResolutionWidth);
            int.TryParse(parts[8], out ResolutionHeight);
        }

        public void Save() => PlayerPrefs.SetString(Key, Serialize());

        public static GameSettings Load()
        {
            GameSettings settings = new GameSettings();
            settings.Deserialize(PlayerPrefs.GetString(Key, ""));
            return settings;
        }
    }

    /// <summary>Stub-friendly wrapper so settings code never touches QualitySettings directly.</summary>
    public static class QualitySettings
    {
        public static void SetLevel(int level) => UnityEngine.QualitySettings.SetQualityLevel(
            Mathf.Clamp(level, 0, 5), true);
    }
}
