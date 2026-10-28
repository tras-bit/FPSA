using System;
using System.Collections.Generic;
using Kordon.Core.Settings;
using Kordon.Game.Input;
using Kordon.Game.Match;
using Kordon.Game.Settings;
using UnityEngine;
using UnityEngine.UI;

namespace Kordon.Game.Ui
{
    /// <summary>
    /// Settings and controls.  Everything here is a real setting, including full
    /// key rebinding: press a row, then press the key.  Bodycam processing and the
    /// lack of a crosshair are not listed because they are not options.
    /// </summary>
    public sealed class SettingsScreen : UiScreenBase
    {
        public override string Name => "settings";

        private static readonly InputAction[] Rebindable =
        {
            InputAction.MoveForward, InputAction.MoveBack, InputAction.MoveLeft,
            InputAction.MoveRight, InputAction.Jump, InputAction.Sprint, InputAction.Crouch,
            InputAction.Prone, InputAction.LeanLeft, InputAction.LeanRight,
            InputAction.Reload, InputAction.Interact, InputAction.Scoreboard,
            InputAction.Pause, InputAction.VoiceLocal, InputAction.VoiceTeam,
        };

        private readonly List<Text> _rows = new List<Text>();
        private InputAction? _capturing;
        private Text _captureHint;
        private Text _sensitivityLabel;
        private Text _volumeLabel;
        private Text _qualityLabel;
        private Slider _sensitivity;
        private Slider _master;
        private Slider _effects;

        protected override void Build()
        {
            Image background = UiBuilder.Panel(Root, "Background",
                new Color(0.035f, 0.042f, 0.048f, 1f), new Vector2(0.5f, 0.5f), Vector2.zero,
                new Vector2(4000f, 4000f));
            background.raycastTarget = true;

            Image left = UiBuilder.Panel(Root, "AudioVideo", UiBuilder.Ink,
                new Vector2(0.5f, 0.5f), new Vector2(-330f, 0f), new Vector2(600f, 860f));
            Image right = UiBuilder.Panel(Root, "Controls", UiBuilder.Ink,
                new Vector2(0.5f, 0.5f), new Vector2(330f, 0f), new Vector2(600f, 860f));

            Text title = UiBuilder.LabelAt(left.transform, "Title", "SETTINGS", 32, UiBuilder.Paper,
                new Vector2(0.5f, 1f), new Vector2(0f, -42f), new Vector2(520f, 40f),
                TextAnchor.MiddleCenter);
            title.fontStyle = FontStyle.Bold;

            _sensitivityLabel = UiBuilder.LabelAt(left.transform, "SensitivityLabel", "",
                18, UiBuilder.Paper, new Vector2(0.5f, 1f), new Vector2(0f, -100f),
                new Vector2(520f, 24f), TextAnchor.MiddleLeft);
            _sensitivity = UiBuilder.Slider(left.transform, new Vector2(0f, 326f),
                new Vector2(520f, 18f), 0.4f, 8f, Flow.Settings.MouseSensitivity, value =>
                {
                    Flow.Settings.MouseSensitivity = value;
                    UpdateLabels();
                });

            _volumeLabel = UiBuilder.LabelAt(left.transform, "VolumeLabel", "", 18,
                UiBuilder.Paper, new Vector2(0.5f, 1f), new Vector2(0f, -172f),
                new Vector2(520f, 24f), TextAnchor.MiddleLeft);
            _master = UiBuilder.Slider(left.transform, new Vector2(0f, 254f),
                new Vector2(520f, 18f), 0f, 1f, Flow.Settings.MasterVolume, value =>
                {
                    Flow.Settings.MasterVolume = value;
                    UpdateLabels();
                });

            UiBuilder.LabelAt(left.transform, "EffectsLabel", "effects volume", 18, UiBuilder.Paper,
                new Vector2(0.5f, 1f), new Vector2(0f, -232f), new Vector2(520f, 24f),
                TextAnchor.MiddleLeft);
            _effects = UiBuilder.Slider(left.transform, new Vector2(0f, 194f),
                new Vector2(520f, 18f), 0f, 1f, Flow.Settings.EffectsVolume, value =>
                {
                    Flow.Settings.EffectsVolume = value;
                    UpdateLabels();
                });

            UiBuilder.Button(left.transform, "SUBTITLES: ON", () => ToggleSubtitles(),
                new Vector2(0f, 110f), new Vector2(520f, 48f), 18);
            _qualityLabel = UiBuilder.LabelAt(left.transform, "QualityLabel", "", 18,
                UiBuilder.Paper, new Vector2(0.5f, 1f), new Vector2(0f, -300f),
                new Vector2(520f, 24f), TextAnchor.MiddleLeft);
            UiBuilder.Button(left.transform, "QUALITY", () => CycleQuality(),
                new Vector2(-140f, 30f), new Vector2(240f, 46f), 17);
            UiBuilder.Button(left.transform, "FULLSCREEN", () => ToggleFullscreen(),
                new Vector2(140f, 30f), new Vector2(240f, 46f), 17);
            UiBuilder.Button(left.transform, "1920 x 1080", () => CycleResolution(),
                new Vector2(0f, -26f), new Vector2(520f, 44f), 16);
            UiBuilder.Button(left.transform, "APPLY AND SAVE", () => SaveAndApply(),
                new Vector2(0f, -100f), new Vector2(520f, 52f), 18);
            UiBuilder.LabelAt(left.transform, "BodycamNote",
                "The bodycam feed is always on and cannot be disabled.\nThere is no crosshair; aim with your sights.",
                16, UiBuilder.Dim, new Vector2(0.5f, 0f), new Vector2(0f, 96f),
                new Vector2(520f, 60f), TextAnchor.MiddleCenter);

            Text controlsTitle = UiBuilder.LabelAt(right.transform, "Title", "CONTROLS", 32,
                UiBuilder.Paper, new Vector2(0.5f, 1f), new Vector2(0f, -42f),
                new Vector2(520f, 40f), TextAnchor.MiddleCenter);
            controlsTitle.fontStyle = FontStyle.Bold;
            UiBuilder.LabelAt(right.transform, "Hint",
                "click a row, then press the new key (Esc cancels)", 16, UiBuilder.Dim,
                new Vector2(0.5f, 1f), new Vector2(0f, -78f), new Vector2(520f, 22f),
                TextAnchor.MiddleCenter);

            for (int i = 0; i < Rebindable.Length; i++)
            {
                InputAction action = Rebindable[i];
                float y = 330f - i * 44f;
                Text row = UiBuilder.LabelAt(right.transform, "Row" + i, "", 18, UiBuilder.Paper,
                    new Vector2(0.5f, 0.5f), new Vector2(0f, y), new Vector2(520f, 32f),
                    TextAnchor.MiddleLeft);
                _rows.Add(row);
                int index = i;
                Button button = UiBuilder.Button(right.transform, "", () => BeginCapture(index),
                    new Vector2(0f, y), new Vector2(520f, 32f), 1);
                Image image = button.GetComponent<Image>();
                image.color = new Color(0f, 0f, 0f, 0f);
                Text label = button.GetComponentInChildren<Text>();
                label.gameObject.SetActive(false);
            }

            _captureHint = UiBuilder.LabelAt(right.transform, "CaptureHint", "", 18,
                UiBuilder.Accent, new Vector2(0.5f, 0f), new Vector2(0f, 60f),
                new Vector2(520f, 28f), TextAnchor.MiddleCenter);

            UiBuilder.Button(right.transform, "RESET TO DEFAULTS", () => ResetBindings(),
                new Vector2(-120f, -330f), new Vector2(240f, 46f), 16);
            UiBuilder.Button(right.transform, "BACK", () => Back(),
                new Vector2(150f, -330f), new Vector2(200f, 46f), 16);

            UpdateLabels();
            UpdateRows();
        }

        public override void OnShow()
        {
            UpdateLabels();
            UpdateRows();
            _capturing = null;
            _captureHint.text = string.Empty;
        }

        private void UpdateLabels()
        {
            _sensitivityLabel.text = "look sensitivity: " +
                                     Flow.Settings.MouseSensitivity.ToString("0.00");
            _volumeLabel.text = "master volume: " +
                                Mathf.RoundToInt(Flow.Settings.MasterVolume * 100f) + "%";
            _qualityLabel.text = "quality: " + SettingsStore.QualityName(Flow.Settings.Quality) +
                                 "     fullscreen: " + (Flow.Settings.Fullscreen ? "on" : "off") +
                                 "\nresolution: " + Flow.Settings.ResolutionWidth + " x " +
                                 Flow.Settings.ResolutionHeight;
            AudioListener.volume = Flow.Settings.MasterVolume;
        }

        private void UpdateRows()
        {
            for (int i = 0; i < Rebindable.Length && i < _rows.Count; i++)
            {
                InputAction action = Rebindable[i];
                _rows[i].text = ActionName(action) + "     " +
                                KordonInput.Bindings.GetKeyName(action).ToUpperInvariant();
            }
        }

        private static string ActionName(InputAction action)
        {
            switch (action)
            {
                case InputAction.MoveForward: return "move forward";
                case InputAction.MoveBack: return "move back";
                case InputAction.MoveLeft: return "move left";
                case InputAction.MoveRight: return "move right";
                case InputAction.Jump: return "jump";
                case InputAction.Sprint: return "sprint";
                case InputAction.Crouch: return "crouch";
                case InputAction.Prone: return "prone";
                case InputAction.LeanLeft: return "lean left";
                case InputAction.LeanRight: return "lean right";
                case InputAction.Reload: return "reload / inspect";
                case InputAction.Interact: return "use / interact";
                case InputAction.Scoreboard: return "scoreboard";
                case InputAction.VoiceLocal: return "voice (proximity)";
                case InputAction.VoiceTeam: return "voice (squad radio)";
                case InputAction.Pause: return "pause";
                default: return action.ToString();
            }
        }

        private void BeginCapture(int index)
        {
            _capturing = Rebindable[index];
            _captureHint.text = "press a key for " + ActionName(Rebindable[index]);
        }

        private void Update()
        {
            if (_capturing == null)
            {
                return;
            }

            if (UnityEngine.Input.GetKeyDown(KeyCode.Escape))
            {
                _capturing = null;
                _captureHint.text = string.Empty;
                return;
            }

            foreach (KeyCode key in Enum.GetValues(typeof(KeyCode)))
            {
                if (key == KeyCode.None || !UnityEngine.Input.GetKeyDown(key))
                {
                    continue;
                }

                KeyBindings bindings = KordonInput.Bindings;
                bindings.Rebind(_capturing.Value, (int)key, out List<InputAction> displaced);
                KordonInput.Rebind(bindings);
                if (displaced != null && displaced.Count > 0)
                {
                    _captureHint.text = ActionName(displaced[0]) + " was unbound";
                }
                _capturing = null;
                _captureHint.text = "bound to " + key;
                UpdateRows();
                return;
            }
        }

        private void ResetBindings()
        {
            KeyBindings bindings = KeyBindings.CreateDefault();
            KordonInput.Rebind(bindings);
            UpdateRows();
            _captureHint.text = "defaults restored";
        }

        private void ToggleSubtitles()
        {
            Flow.Settings.SubtitlesEnabled = !Flow.Settings.SubtitlesEnabled;
            SaveAndApply();
        }

        private void CycleQuality()
        {
            Flow.Settings.Quality = (QualityPreset)(((int)Flow.Settings.Quality + 1) % 4);
            SaveAndApply();
        }

        private void ToggleFullscreen()
        {
            Flow.Settings.Fullscreen = !Flow.Settings.Fullscreen;
            SaveAndApply();
        }

        private void CycleResolution()
        {
            if (Flow.Settings.ResolutionWidth == 1920)
            {
                Flow.Settings.ResolutionWidth = 2560;
                Flow.Settings.ResolutionHeight = 1440;
            }
            else if (Flow.Settings.ResolutionWidth == 2560)
            {
                Flow.Settings.ResolutionWidth = 1280;
                Flow.Settings.ResolutionHeight = 720;
            }
            else
            {
                Flow.Settings.ResolutionWidth = 1920;
                Flow.Settings.ResolutionHeight = 1080;
            }

            SaveAndApply();
        }

        private void SaveAndApply()
        {
            SettingsStore.Apply(Flow.Settings);
            SettingsStore.Save(Flow.Settings);
            UpdateLabels();
        }
    }
}
