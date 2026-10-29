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
    ///
    /// Both columns are laid out by <see cref="UiStack"/>, top to bottom: the previous
    /// version mixed top, centre and bottom anchors with hand written offsets, so the
    /// slider labels sat underneath the slider tracks, the quality readout collided
    /// with the subtitles button, and the last control rows were hidden behind the
    /// footer buttons.
    /// </summary>
    public sealed class SettingsScreen : UiScreenBase
    {
        public override string Name => "settings";

        // Panel heights are derived from the content stacks, not guessed: the columns
        // are 547 px and 759 px of rows respectively, so a fixed 940 left a dead band
        // at the bottom of both panels.
        private const float LeftPanelHeight = 700f;
        private const float RightPanelHeight = 820f;
        private const float LeftWidth = 600f;
        private const float RightWidth = 860f;

        private static readonly InputAction[] Rebindable =
        {
            InputAction.MoveForward, InputAction.MoveBack, InputAction.MoveLeft,
            InputAction.MoveRight, InputAction.Jump, InputAction.Sprint, InputAction.Crouch,
            InputAction.Prone, InputAction.LeanLeft, InputAction.LeanRight,
            InputAction.Reload, InputAction.Interact, InputAction.Scoreboard,
            InputAction.Pause, InputAction.VoiceLocal, InputAction.VoiceTeam,
        };

        private readonly Text[] _rowActions = new Text[Rebindable.Length];
        private readonly Text[] _rowKeys = new Text[Rebindable.Length];

        private InputAction? _capturing;
        private Text _captureHint;
        private Text _sensitivityValue;
        private Text _volumeValue;
        private Text _effectsValue;
        private Button _subtitlesButton;
        private Button _qualityButton;
        private Button _fullscreenButton;
        private Button _resolutionButton;

        protected override void Build()
        {
            Image background = UiBuilder.Panel(Root, "Background",
                new Color(0.035f, 0.042f, 0.048f, 1f), new Vector2(0.5f, 0.5f), Vector2.zero,
                new Vector2(4000f, 4000f));
            background.raycastTarget = true;

            Image left = UiBuilder.Panel(Root, "AudioVideo", UiBuilder.Ink,
                new Vector2(0.5f, 0.5f), new Vector2(-450f, 0f),
                new Vector2(LeftWidth, LeftPanelHeight));
            Image right = UiBuilder.Panel(Root, "Controls", UiBuilder.Ink,
                new Vector2(0.5f, 0.5f), new Vector2(300f, 0f),
                new Vector2(RightWidth, RightPanelHeight));

            BuildAudioVideo(left.transform);
            BuildControls(right.transform);
            UpdateLabels();
            UpdateRows();
        }

        // ------------------------------------------------------------- left column

        private void BuildAudioVideo(Transform panel)
        {
            UiStack stack = new UiStack(panel, "Stack", LeftWidth - 56f, LeftPanelHeight,
                paddingTop: 24f, paddingBottom: 24f, spacing: 8f);

            Text title = stack.Text("SETTINGS", 34, UiBuilder.Paper, 44f, TextAnchor.MiddleCenter);
            if (title != null)
            {
                title.fontStyle = FontStyle.Bold;
            }

            stack.Section("input");
            Slider sensitivity = stack.SliderRow("Look sensitivity", 0.4f, 8f,
                Flow.Settings.MouseSensitivity, value =>
                {
                    Flow.Settings.MouseSensitivity = value;
                    UpdateLabels();
                }, out _sensitivityValue);
            if (sensitivity != null)
            {
                sensitivity.wholeNumbers = false;
            }

            stack.Space(4f);
            stack.Section("audio");
            Slider master = stack.SliderRow("Master volume", 0f, 1f,
                Flow.Settings.MasterVolume, value =>
                {
                    Flow.Settings.MasterVolume = value;
                    UpdateLabels();
                }, out _volumeValue);

            Slider effects = stack.SliderRow("Effects volume", 0f, 1f,
                Flow.Settings.EffectsVolume, value =>
                {
                    Flow.Settings.EffectsVolume = value;
                    UpdateLabels();
                }, out _effectsValue);
            if (master != null)
            {
                master.wholeNumbers = false;
            }

            if (effects != null)
            {
                effects.wholeNumbers = false;
            }

            stack.Space(4f);
            _subtitlesButton = stack.Button("SUBTITLES: ON", () => ToggleSubtitles(), 44f, 17);

            stack.Space(4f);
            stack.Section("video");
            stack.Pair("QUALITY: HIGH", () => CycleQuality(), "FULLSCREEN: ON",
                () => ToggleFullscreen(), 44f, out _qualityButton, out _fullscreenButton);
            _resolutionButton = stack.Button("1920 x 1080", () => CycleResolution(), 44f, 17);

            stack.Space(14f);
            stack.Button("APPLY AND SAVE", () => SaveAndApply(), 52f, 19);

            // The note sits on the bottom edge of the panel, whatever is above it.
            if (stack.Spare > 70f)
            {
                stack.Space(stack.Spare - 70f);
            }

            stack.Text("The bodycam feed is always on and cannot be disabled.\n" +
                       "There is no crosshair; aim with your sights.",
                15, UiBuilder.Dim, 62f, TextAnchor.MiddleCenter);
        }

        // ------------------------------------------------------------ right column

        private void BuildControls(Transform panel)
        {
            UiStack stack = new UiStack(panel, "Stack", RightWidth - 56f, RightPanelHeight,
                paddingTop: 24f, paddingBottom: 24f, spacing: 5f);

            Text title = stack.Text("CONTROLS", 34, UiBuilder.Paper, 44f, TextAnchor.MiddleCenter);
            if (title != null)
            {
                title.fontStyle = FontStyle.Bold;
            }

            stack.Text("Click a row, then press the new key.  Esc cancels.", 15,
                UiBuilder.Dim, 22f, TextAnchor.MiddleCenter);

            for (int i = 0; i < Rebindable.Length; i++)
            {
                int index = i;
                Button row = stack.RowButton(() => BeginCapture(index), 33f,
                    out Text action, out Text key);
                if (row != null)
                {
                    _rowActions[i] = action;
                    _rowKeys[i] = key;
                }
            }

            stack.Space(4f);
            _captureHint = stack.Text(string.Empty, 16, UiBuilder.Accent, 24f,
                TextAnchor.MiddleCenter);
            stack.Pair("RESET TO DEFAULTS", () => ResetBindings(), "BACK", () => Back(), 46f,
                12f, 16);
        }

        public override void OnShow()
        {
            UpdateLabels();
            UpdateRows();
            _capturing = null;
            if (_captureHint != null)
            {
                _captureHint.text = string.Empty;
            }
        }

        private void UpdateLabels()
        {
            if (_sensitivityValue != null)
            {
                _sensitivityValue.text = Flow.Settings.MouseSensitivity.ToString("0.00");
            }

            if (_volumeValue != null)
            {
                _volumeValue.text = Mathf.RoundToInt(Flow.Settings.MasterVolume * 100f) + "%";
            }

            if (_effectsValue != null)
            {
                _effectsValue.text = Mathf.RoundToInt(Flow.Settings.EffectsVolume * 100f) + "%";
            }

            if (_subtitlesButton != null)
            {
                UiBuilder.SetButtonText(_subtitlesButton,
                    "SUBTITLES: " + (Flow.Settings.SubtitlesEnabled ? "ON" : "OFF"));
            }

            if (_qualityButton != null)
            {
                UiBuilder.SetButtonText(_qualityButton,
                    "QUALITY: " + SettingsStore.QualityName(Flow.Settings.Quality).ToUpperInvariant());
            }

            if (_fullscreenButton != null)
            {
                UiBuilder.SetButtonText(_fullscreenButton,
                    "FULLSCREEN: " + (Flow.Settings.Fullscreen ? "ON" : "OFF"));
            }

            if (_resolutionButton != null)
            {
                UiBuilder.SetButtonText(_resolutionButton,
                    Flow.Settings.ResolutionWidth + " x " + Flow.Settings.ResolutionHeight);
            }

            AudioListener.volume = Flow.Settings.MasterVolume;
        }

        private void UpdateRows()
        {
            for (int i = 0; i < Rebindable.Length; i++)
            {
                InputAction action = Rebindable[i];
                if (_rowActions[i] != null)
                {
                    _rowActions[i].text = ActionName(action);
                }

                if (_rowKeys[i] != null)
                {
                    _rowKeys[i].text = KordonInput.Bindings.GetKeyName(action).ToUpperInvariant();
                }
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
            if (_captureHint != null)
            {
                _captureHint.text = "press a key for " + ActionName(Rebindable[index]);
            }
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
                string note = displaced != null && displaced.Count > 0
                    ? ActionName(displaced[0]) + " was unbound"
                    : "bound to " + key;
                _capturing = null;
                _captureHint.text = note;
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
