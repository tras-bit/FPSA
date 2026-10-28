using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityInput = UnityEngine.Input;

namespace Kordon.Game.Input
{
    /// <summary>Every bindable action. Movement keys are bindings too, per the accessibility rules.</summary>
    public enum InputAction
    {
        MoveForward,
        MoveBack,
        MoveLeft,
        MoveRight,
        Jump,
        Sprint,
        Crouch,
        Prone,
        LeanLeft,
        LeanRight,
        ReloadInspect,
        Use,
        Scoreboard,
        Pause,
    }

    /// <summary>
    /// Rebindable key map, persisted per player. Stored as a tiny "action:keycode"
    /// string in PlayerPrefs so settings survive between sessions and the settings
    /// screen can list and reassign every row.
    /// </summary>
    public sealed class InputBindings
    {
        private const string StoreKey = "kordon.input.bindings";
        private readonly Dictionary<InputAction, KeyCode> _keys = new Dictionary<InputAction, KeyCode>();

        public InputBindings()
        {
            foreach (InputAction action in System.Enum.GetValues(typeof(InputAction)))
            {
                _keys[action] = DefaultKey(action);
            }
        }

        public static KeyCode DefaultKey(InputAction action)
        {
            switch (action)
            {
                case InputAction.MoveForward: return KeyCode.W;
                case InputAction.MoveBack: return KeyCode.S;
                case InputAction.MoveLeft: return KeyCode.A;
                case InputAction.MoveRight: return KeyCode.D;
                case InputAction.Jump: return KeyCode.Space;
                case InputAction.Sprint: return KeyCode.LeftShift;
                case InputAction.Crouch: return KeyCode.LeftControl;
                case InputAction.Prone: return KeyCode.Z;
                case InputAction.LeanLeft: return KeyCode.Q;
                case InputAction.LeanRight: return KeyCode.E;
                case InputAction.ReloadInspect: return KeyCode.R;
                case InputAction.Use: return KeyCode.F;
                case InputAction.Scoreboard: return KeyCode.Tab;
                case InputAction.Pause: return KeyCode.Escape;
                default: return KeyCode.None;
            }
        }

        public KeyCode this[InputAction action]
        {
            get => _keys.TryGetValue(action, out KeyCode key) ? key : DefaultKey(action);
            set => _keys[action] = value;
        }

        public void Reset(InputAction action) => _keys[action] = DefaultKey(action);

        public string Serialize()
        {
            StringBuilder builder = new StringBuilder();
            foreach (KeyValuePair<InputAction, KeyCode> pair in _keys)
            {
                if (builder.Length > 0)
                {
                    builder.Append(';');
                }

                builder.Append((int)pair.Key).Append(':').Append((int)pair.Value);
            }

            return builder.ToString();
        }

        public void Deserialize(string data)
        {
            if (string.IsNullOrEmpty(data))
            {
                return;
            }

            foreach (string pair in data.Split(';'))
            {
                string[] parts = pair.Split(':');
                if (parts.Length != 2 ||
                    !int.TryParse(parts[0], out int action) ||
                    !int.TryParse(parts[1], out int key))
                {
                    continue;
                }

                if (System.Enum.IsDefined(typeof(InputAction), action))
                {
                    _keys[(InputAction)action] = (KeyCode)key;
                }
            }
        }

        public void Save() => PlayerPrefs.SetString(StoreKey, Serialize());

        public static InputBindings Load()
        {
            InputBindings bindings = new InputBindings();
            bindings.Deserialize(PlayerPrefs.GetString(StoreKey, ""));
            return bindings;
        }
    }

    /// <summary>
    /// One reading point for raw device state. Fire and aim stay on mouse buttons
    /// (the rules document binds them there); everything else honours the binding
    /// table. No UnityEngine.Input calls anywhere else in the game assemblies.
    /// </summary>
    public static class KordonInput
    {
        public static InputBindings Bindings { get; private set; } = InputBindings.Load();

        public static void Rebind(InputBindings bindings)
        {
            Bindings = bindings;
            Bindings.Save();
        }

        public static bool Held(InputAction action) => UnityInput.GetKey(Bindings[action]);
        public static bool Pressed(InputAction action) => UnityInput.GetKeyDown(Bindings[action]);
        public static bool Released(InputAction action) => UnityInput.GetKeyUp(Bindings[action]);

        public static bool FireHeld => UnityInput.GetMouseButton(0);
        public static bool FirePressed => UnityInput.GetMouseButtonDown(0);
        public static bool AimHeld => UnityInput.GetMouseButton(1);

        /// <summary>Signed move axes from the bound keys, length clamped to 1.</summary>
        public static Vector2 MoveAxes()
        {
            float x = (Held(InputAction.MoveRight) ? 1f : 0f) - (Held(InputAction.MoveLeft) ? 1f : 0f);
            float y = (Held(InputAction.MoveForward) ? 1f : 0f) - (Held(InputAction.MoveBack) ? 1f : 0f);
            if (x != 0f && y != 0f)
            {
                x *= 0.7071f;
                y *= 0.7071f;
            }

            return new Vector2(x, y);
        }

        /// <summary>Raw mouse look delta in pixels for this frame.</summary>
        public static Vector2 LookDelta() =>
            new Vector2(UnityInput.GetAxis("Mouse X"), UnityInput.GetAxis("Mouse Y"));
    }
}
