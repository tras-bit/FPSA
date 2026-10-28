using Kordon.Core.Settings;
using UnityEngine;
using UnityInput = UnityEngine.Input;

namespace Kordon.Game.Input
{
    /// <summary>
    /// One reading point for raw device state.  The bindings themselves live in
    /// Kordon.Core (InputAction + KeyBindings), because the server has to understand
    /// the same actions; this class only turns a Core binding into a Unity key.
    /// </summary>
    public static class KordonInput
    {
        private const string BindingsKey = "kordon.bindings.v2";

        public static KeyBindings Bindings { get; private set; } = LoadBindings();

        public static void Rebind(KeyBindings bindings)
        {
            Bindings = bindings ?? KeyBindings.CreateDefault();
            PlayerPrefs.SetString(BindingsKey, Bindings.ToJson());
            PlayerPrefs.Save();
        }

        public static KeyBindings LoadBindings()
        {
            string json = PlayerPrefs.GetString(BindingsKey, string.Empty);
            return string.IsNullOrEmpty(json) ? KeyBindings.CreateDefault() : KeyBindings.FromJson(json);
        }

        private static KeyCode KeyFor(InputAction action) =>
            (KeyCode)Bindings.GetKey(action);

        public static bool Held(InputAction action) => UnityInput.GetKey(KeyFor(action));
        public static bool Pressed(InputAction action) => UnityInput.GetKeyDown(KeyFor(action));
        public static bool Released(InputAction action) => UnityInput.GetKeyUp(KeyFor(action));

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

        public static Vector2 LookDelta() =>
            new Vector2(UnityInput.GetAxis("Mouse X"), UnityInput.GetAxis("Mouse Y"));
    }
}
