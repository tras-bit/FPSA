using System;
using UnityEngine;

namespace Kordon.Game.Match
{
    /// <summary>
    /// Local player identity: a display name the player can change in the menu and
    /// nothing else.  No accounts, no services, no personal data - a name is sent to
    /// a server only for its scoreboard.
    /// </summary>
    public static class PlayerProfile
    {
        private const string NameKey = "kordon.profile.name";
        private const string PresetKey = "kordon.profile.loadout";

        public static string Name
        {
            get
            {
                string stored = PlayerPrefs.GetString(NameKey, string.Empty);
                return string.IsNullOrEmpty(stored) ? GenerateName() : stored;
            }
            set
            {
                string cleaned = Sanitize(value);
                if (cleaned.Length > 0)
                {
                    PlayerPrefs.SetString(NameKey, cleaned);
                    PlayerPrefs.Save();
                }
            }
        }

        /// <summary>Loadout preset slot chosen on the prep screen, persisted between matches.</summary>
        public static string LastPreset
        {
            get => PlayerPrefs.GetString(PresetKey, "assault");
            set => PlayerPrefs.SetString(PresetKey, value);
        }

        public static string GenerateName()
        {
            int hash = Math.Abs(Environment.MachineName.GetHashCode() ^ DateTime.UtcNow.Ticks.GetHashCode());
            return "Operator-" + (hash % 9000 + 1000);
        }

        public static string Sanitize(string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return string.Empty;
            }

            string result = value.Trim();
            if (result.Length > 20)
            {
                result = result.Substring(0, 20);
            }

            // Keep it printable and free of markup characters the HUD would render.
            char[] buffer = result.ToCharArray();
            for (int i = 0; i < buffer.Length; i++)
            {
                char c = buffer[i];
                if (char.IsControl(c) || c == '<' || c == '>' || c == '|' || c == ';')
                {
                    buffer[i] = '_';
                }
            }

            return new string(buffer);
        }
    }
}
