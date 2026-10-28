using System;

namespace Kordon.Core.Settings
{
    /// <summary>
    /// Every bindable action in the game.  Bindings are stored as integers matching
    /// UnityEngine.KeyCode so the settings file can be written and read without the
    /// engine, and remapped from the options menu.
    /// </summary>
    public enum InputAction
    {
        None = 0,

        MoveForward = 1,
        MoveBack = 2,
        MoveLeft = 3,
        MoveRight = 4,
        Sprint = 5,
        Crouch = 6,
        Prone = 7,
        Jump = 8,
        LeanLeft = 9,
        LeanRight = 10,
        ToggleSprint = 11,
        ToggleCrouch = 12,

        Fire = 20,
        Aim = 21,
        Reload = 22,
        InspectMagazine = 23,
        FireModeSwitch = 24,
        Grenade = 25,
        SlotPrimary = 26,
        SlotSecondary = 27,
        SlotGrenade = 28,

        Interact = 40,
        Revive = 41,
        PlantCharge = 42,

        Scoreboard = 60,
        Pause = 61,
        VoiceLocal = 62,
        VoiceTeam = 63,
        PushToTalk = 64,
        ToggleSubtitles = 65,
        Ping = 66
    }

    /// <summary>How leaning is held.</summary>
    public enum LeanInputMode
    {
        Hold = 0,
        Toggle = 1
    }

    /// <summary>Display names and parsing for key codes, engine independent.</summary>
    public static class KeyNames
    {
        public const int KeyCodeNone = 0;
        public const int KeyCodeTab = 9;
        public const int KeyCodeReturn = 13;
        public const int KeyCodeEscape = 27;
        public const int KeyCodeSpace = 32;
        public const int KeyCodeAlpha0 = 48;
        public const int KeyCodeA = 97;
        // Unity KeyCode values (UnityEngine.KeyCode): 303 RightShift, 304 LeftShift,
        // 305 RightControl, 306 LeftControl, 307 RightAlt, 308 LeftAlt.
        public const int KeyCodeRightShift = 303;
        public const int KeyCodeLeftShift = 304;
        public const int KeyCodeRightControl = 305;
        public const int KeyCodeLeftControl = 306;
        public const int KeyCodeRightAlt = 307;
        public const int KeyCodeLeftAlt = 308;
        public const int KeyCodeMouse0 = 323;
        public const int KeyCodeMouse1 = 324;
        public const int KeyCodeMouse2 = 325;
        public const int KeyCodeMouse3 = 326;
        public const int KeyCodeMouse4 = 327;

        public static string Display(int keyCode)
        {
            if (keyCode == KeyCodeNone)
            {
                return "Unbound";
            }

            switch (keyCode)
            {
                case KeyCodeTab: return "Tab";
                case KeyCodeReturn: return "Enter";
                case KeyCodeEscape: return "Esc";
                case KeyCodeSpace: return "Space";
                case KeyCodeLeftShift: return "L-Shift";
                case KeyCodeRightShift: return "R-Shift";
                case KeyCodeLeftControl: return "L-Ctrl";
                case KeyCodeRightControl: return "R-Ctrl";
                case KeyCodeLeftAlt: return "L-Alt";
                case KeyCodeRightAlt: return "R-Alt";
                case KeyCodeMouse0: return "LMB";
                case KeyCodeMouse1: return "RMB";
                case KeyCodeMouse2: return "MMB";
                case KeyCodeMouse3: return "Mouse 4";
                case KeyCodeMouse4: return "Mouse 5";
            }

            if (keyCode >= KeyCodeAlpha0 && keyCode <= KeyCodeAlpha0 + 9)
            {
                return ((char)('0' + (keyCode - KeyCodeAlpha0))).ToString();
            }

            if (keyCode >= KeyCodeA && keyCode <= KeyCodeA + 25)
            {
                return ((char)('A' + (keyCode - KeyCodeA))).ToString();
            }

            if (keyCode >= 282 && keyCode <= 296)
            {
                return "F" + (keyCode - 281);
            }

            return "Key" + keyCode.ToString(System.Globalization.CultureInfo.InvariantCulture);
        }

        public static int Parse(string displayName)
        {
            if (string.IsNullOrEmpty(displayName))
            {
                return KeyCodeNone;
            }

            string value = displayName.Trim();
            switch (value.ToLowerInvariant())
            {
                case "unbound":
                case "none": return KeyCodeNone;
                case "tab": return KeyCodeTab;
                case "enter": return KeyCodeReturn;
                case "esc":
                case "escape": return KeyCodeEscape;
                case "space": return KeyCodeSpace;
                case "l-shift":
                case "leftshift": return KeyCodeLeftShift;
                case "r-shift":
                case "rightshift": return KeyCodeRightShift;
                case "l-ctrl":
                case "leftcontrol": return KeyCodeLeftControl;
                case "r-ctrl":
                case "rightcontrol": return KeyCodeRightControl;
                case "l-alt":
                case "leftalt": return KeyCodeLeftAlt;
                case "r-alt":
                case "rightalt": return KeyCodeRightAlt;
                case "lmb":
                case "mouse0": return KeyCodeMouse0;
                case "rmb":
                case "mouse1": return KeyCodeMouse1;
                case "mmb":
                case "mouse2": return KeyCodeMouse2;
                case "mouse 4":
                case "mouse3": return KeyCodeMouse3;
                case "mouse 5":
                case "mouse4": return KeyCodeMouse4;
            }

            if (value.Length == 1)
            {
                char c = char.ToUpperInvariant(value[0]);
                if (c >= 'A' && c <= 'Z')
                {
                    return KeyCodeA + (c - 'A');
                }

                if (c >= '0' && c <= '9')
                {
                    return KeyCodeAlpha0 + (c - '0');
                }
            }

            if (value.StartsWith("F", StringComparison.OrdinalIgnoreCase) &&
                int.TryParse(value.Substring(1), out int fkey) && fkey >= 1 && fkey <= 15)
            {
                return 281 + fkey;
            }

            if (value.StartsWith("Key", StringComparison.OrdinalIgnoreCase) &&
                int.TryParse(value.Substring(3), out int named))
            {
                return named;
            }

            if (int.TryParse(value, out int raw))
            {
                return raw;
            }

            return KeyCodeNone;
        }
    }
}
