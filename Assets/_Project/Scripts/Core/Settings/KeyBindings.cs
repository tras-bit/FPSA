using System;
using System.Collections.Generic;
using System.Globalization;
using Kordon.Core.Serialization;

namespace Kordon.Core.Settings
{
    /// <summary>
    /// A named, rebindable control profile.  The game ships three profiles
    /// (Default, Southpaw-style Tactical, Custom) and any action can be remapped from
    /// the options menu; the mapping is persisted as JSON in the player settings file.
    /// </summary>
    [Serializable]
    public sealed class KeyBindings
    {
        private readonly Dictionary<InputAction, int> _map = new Dictionary<InputAction, int>();

        public string ProfileName { get; set; } = "Default";

        public IReadOnlyDictionary<InputAction, int> Map => _map;

        public int GetKey(InputAction action) => _map.TryGetValue(action, out int key) ? key : KeyNames.KeyCodeNone;

        public string GetKeyName(InputAction action) => KeyNames.Display(GetKey(action));

        public void SetKey(InputAction action, int keyCode) => _map[action] = keyCode;

        public void Unbind(InputAction action) => _map[action] = KeyNames.KeyCodeNone;

        /// <summary>Actions already using this key (used to warn about conflicts).</summary>
        public List<InputAction> Conflicts(int keyCode)
        {
            List<InputAction> conflicts = new List<InputAction>();
            if (keyCode == KeyNames.KeyCodeNone)
            {
                return conflicts;
            }

            foreach (KeyValuePair<InputAction, int> pair in _map)
            {
                if (pair.Value == keyCode)
                {
                    conflicts.Add(pair.Key);
                }
            }

            return conflicts;
        }

        /// <summary>Rebind and report what it displaced; the displaced action becomes unbound.</summary>
        public bool Rebind(InputAction action, int keyCode, out List<InputAction> displaced)
        {
            displaced = Conflicts(keyCode);
            foreach (InputAction other in displaced)
            {
                if (other != action)
                {
                    _map[other] = KeyNames.KeyCodeNone;
                }
            }

            _map[action] = keyCode;
            return true;
        }

        /// <summary>Default keyboard and mouse layout required by the design brief.</summary>
        public static KeyBindings CreateDefault(string profileName = "Default")
        {
            KeyBindings bindings = new KeyBindings { ProfileName = profileName };

            // WASD movement, mouse look (view is not bindable: it is always the mouse).
            bindings.SetKey(InputAction.MoveForward, KeyNames.KeyCodeA + ('W' - 'A'));
            bindings.SetKey(InputAction.MoveBack, KeyNames.KeyCodeA + ('S' - 'A'));
            bindings.SetKey(InputAction.MoveLeft, KeyNames.KeyCodeA + ('A' - 'A'));
            bindings.SetKey(InputAction.MoveRight, KeyNames.KeyCodeA + ('D' - 'A'));

            // Shift = short sprint, Ctrl/C = crouch, Q/E = lean.
            bindings.SetKey(InputAction.Sprint, KeyNames.KeyCodeLeftShift);
            bindings.SetKey(InputAction.ToggleSprint, KeyNames.KeyCodeNone);
            bindings.SetKey(InputAction.Crouch, KeyNames.KeyCodeLeftControl);
            bindings.SetKey(InputAction.ToggleCrouch, KeyNames.KeyCodeA + ('C' - 'A'));
            bindings.SetKey(InputAction.Prone, KeyNames.KeyCodeA + ('Z' - 'A'));
            bindings.SetKey(InputAction.Jump, KeyNames.KeyCodeSpace);
            bindings.SetKey(InputAction.LeanLeft, KeyNames.KeyCodeA + ('Q' - 'A'));
            bindings.SetKey(InputAction.LeanRight, KeyNames.KeyCodeA + ('E' - 'A'));

            bindings.SetKey(InputAction.Fire, KeyNames.KeyCodeMouse0);
            bindings.SetKey(InputAction.Aim, KeyNames.KeyCodeMouse1);

            // R is both reload (tap) and magazine inspection (hold); the same physical
            // key drives both, which is why they are bound to one action pair.
            bindings.SetKey(InputAction.Reload, KeyNames.KeyCodeA + ('R' - 'A'));
            bindings.SetKey(InputAction.InspectMagazine, KeyNames.KeyCodeA + ('R' - 'A'));

            bindings.SetKey(InputAction.FireModeSwitch, KeyNames.KeyCodeA + ('B' - 'A'));
            bindings.SetKey(InputAction.Grenade, KeyNames.KeyCodeA + ('G' - 'A'));
            bindings.SetKey(InputAction.SlotPrimary, KeyNames.KeyCodeAlpha0 + 1);
            bindings.SetKey(InputAction.SlotSecondary, KeyNames.KeyCodeAlpha0 + 2);
            bindings.SetKey(InputAction.SlotGrenade, KeyNames.KeyCodeAlpha0 + 3);

            bindings.SetKey(InputAction.Interact, KeyNames.KeyCodeA + ('F' - 'A'));
            bindings.SetKey(InputAction.Revive, KeyNames.KeyCodeA + ('F' - 'A'));
            bindings.SetKey(InputAction.PlantCharge, KeyNames.KeyCodeA + ('F' - 'A'));

            bindings.SetKey(InputAction.Scoreboard, KeyNames.KeyCodeTab);
            bindings.SetKey(InputAction.Pause, KeyNames.KeyCodeEscape);
            bindings.SetKey(InputAction.PushToTalk, KeyNames.KeyCodeA + ('V' - 'A'));
            bindings.SetKey(InputAction.VoiceLocal, KeyNames.KeyCodeA + ('V' - 'A'));
            bindings.SetKey(InputAction.VoiceTeam, KeyNames.KeyCodeA + ('T' - 'A'));
            bindings.SetKey(InputAction.ToggleSubtitles, KeyNames.KeyCodeNone);
            bindings.SetKey(InputAction.Ping, KeyNames.KeyCodeA + ('H' - 'A'));
            return bindings;
        }

        /// <summary>Left handed mouse layout: movement on the arrow side, actions on the left.</summary>
        public static KeyBindings CreateTactical(string profileName = "Tactical")
        {
            KeyBindings bindings = CreateDefault(profileName);
            bindings.SetKey(InputAction.Sprint, KeyNames.KeyCodeRightShift);
            bindings.SetKey(InputAction.Crouch, KeyNames.KeyCodeA + ('C' - 'A'));
            bindings.SetKey(InputAction.ToggleCrouch, KeyNames.KeyCodeLeftControl);
            bindings.SetKey(InputAction.LeanLeft, KeyNames.KeyCodeA + ('Q' - 'A'));
            bindings.SetKey(InputAction.LeanRight, KeyNames.KeyCodeA + ('E' - 'A'));
            bindings.SetKey(InputAction.Reload, KeyNames.KeyCodeA + ('R' - 'A'));
            bindings.SetKey(InputAction.Scoreboard, KeyNames.KeyCodeTab);
            return bindings;
        }

        public string ToJson()
        {
            Dictionary<string, object> root = JsonObject.New().With("profile", ProfileName);
            List<object> entries = new List<object>();
            foreach (InputAction action in Enum.GetValues(typeof(InputAction)))
            {
                if (action == InputAction.None)
                {
                    continue;
                }

                entries.Add(JsonObject.New()
                    .With("action", action.ToString())
                    .With("key", GetKey(action))
                    .With("keyName", KeyNames.Display(GetKey(action))));
            }

            root["bindings"] = entries;
            return MiniJson.Write(root, true);
        }

        public static KeyBindings FromJson(string json)
        {
            KeyBindings bindings = CreateDefault();
            if (string.IsNullOrEmpty(json))
            {
                return bindings;
            }

            Json root = Json.Parse(json);
            bindings.ProfileName = root["profile"].AsString(bindings.ProfileName);
            foreach (Json entry in root["bindings"].Items())
            {
                string actionName = entry["action"].AsString();
                if (Enum.TryParse(actionName, true, out InputAction action) && action != InputAction.None)
                {
                    int key = entry.Has("key") ? entry["key"].AsInt(KeyNames.KeyCodeNone)
                        : KeyNames.Parse(entry["keyName"].AsString());
                    bindings.SetKey(action, key);
                }
            }

            return bindings;
        }

        /// <summary>Sanity check: the actions the design brief requires must be bound.</summary>
        public List<string> ValidateRequiredBindings()
        {
            List<string> problems = new List<string>();
            InputAction[] required =
            {
                InputAction.MoveForward, InputAction.MoveBack, InputAction.MoveLeft, InputAction.MoveRight,
                InputAction.Sprint, InputAction.Crouch, InputAction.Jump, InputAction.LeanLeft,
                InputAction.LeanRight, InputAction.Reload, InputAction.InspectMagazine,
                InputAction.Fire, InputAction.Aim
            };

            foreach (InputAction action in required)
            {
                if (GetKey(action) == KeyNames.KeyCodeNone)
                {
                    problems.Add($"required-action-unbound:{action}");
                }
            }

            // R must be one physical key that drives both reload (tap) and inspect (hold).
            if (GetKey(InputAction.Reload) != GetKey(InputAction.InspectMagazine))
            {
                problems.Add(string.Format(CultureInfo.InvariantCulture,
                    "reload-and-inspect-must-share-a-key (reload={0}, inspect={1})",
                    KeyNames.Display(GetKey(InputAction.Reload)),
                    KeyNames.Display(GetKey(InputAction.InspectMagazine))));
            }

            return problems;
        }
    }
}
