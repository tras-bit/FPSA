using System;
using System.Collections.Generic;
using System.Reflection;
using Kordon.Core.Settings;
using Xunit;

namespace Kordon.Core.Tests
{
    /// <summary>
    /// Settings, control profiles and rebinding.  Two product rules are locked as
    /// compile time constants here: the body camera treatment is always on, and there
    /// is no crosshair.
    /// </summary>
    public class SettingsTests
    {
        private const int KeyW = KeyNames.KeyCodeA + ('W' - 'A');
        private const int KeyA = KeyNames.KeyCodeA;
        private const int KeyS = KeyNames.KeyCodeA + ('S' - 'A');
        private const int KeyD = KeyNames.KeyCodeA + ('D' - 'A');
        private const int KeyC = KeyNames.KeyCodeA + ('C' - 'A');
        private const int KeyQ = KeyNames.KeyCodeA + ('Q' - 'A');
        private const int KeyE = KeyNames.KeyCodeA + ('E' - 'A');
        private const int KeyR = KeyNames.KeyCodeA + ('R' - 'A');
        private const int KeyZ = KeyNames.KeyCodeA + ('Z' - 'A');

        [Fact]
        public void DefaultLayout_MatchesTheDesignBrief()
        {
            KeyBindings bindings = KeyBindings.CreateDefault();
            Assert.Equal("Default", bindings.ProfileName);
            Assert.Empty(bindings.ValidateRequiredBindings());

            Assert.Equal(KeyW, bindings.GetKey(InputAction.MoveForward));
            Assert.Equal(KeyS, bindings.GetKey(InputAction.MoveBack));
            Assert.Equal(KeyA, bindings.GetKey(InputAction.MoveLeft));
            Assert.Equal(KeyD, bindings.GetKey(InputAction.MoveRight));

            // Shift = short sprint (hold), Ctrl = crouch, C = crouch toggle, Z = prone.
            Assert.Equal(KeyNames.KeyCodeLeftShift, bindings.GetKey(InputAction.Sprint));
            Assert.Equal(KeyNames.KeyCodeLeftControl, bindings.GetKey(InputAction.Crouch));
            Assert.Equal(KeyC, bindings.GetKey(InputAction.ToggleCrouch));
            Assert.Equal(KeyZ, bindings.GetKey(InputAction.Prone));
            Assert.Equal(KeyNames.KeyCodeSpace, bindings.GetKey(InputAction.Jump));

            // Q / E lean.
            Assert.Equal(KeyQ, bindings.GetKey(InputAction.LeanLeft));
            Assert.Equal(KeyE, bindings.GetKey(InputAction.LeanRight));

            Assert.Equal(KeyNames.KeyCodeMouse0, bindings.GetKey(InputAction.Fire));
            Assert.Equal(KeyNames.KeyCodeMouse1, bindings.GetKey(InputAction.Aim));
            Assert.Equal(KeyNames.KeyCodeTab, bindings.GetKey(InputAction.Scoreboard));
            Assert.Equal(KeyNames.KeyCodeEscape, bindings.GetKey(InputAction.Pause));

            // Local voice and team radio are separate keys so the channels stay distinct.
            Assert.NotEqual(bindings.GetKey(InputAction.VoiceLocal), bindings.GetKey(InputAction.VoiceTeam));
        }

        [Fact]
        public void ReloadAndInspect_ShareOnePhysicalKey()
        {
            KeyBindings bindings = KeyBindings.CreateDefault();
            Assert.Equal(KeyR, bindings.GetKey(InputAction.Reload));
            Assert.Equal(KeyR, bindings.GetKey(InputAction.InspectMagazine));
            Assert.Equal("R", bindings.GetKeyName(InputAction.Reload));

            // Tap = reload, hold = inspect: both must live on the same key or the
            // control scheme in the brief cannot be expressed.
            Assert.Contains(InputAction.Reload, bindings.Conflicts(KeyR));
            Assert.Contains(InputAction.InspectMagazine, bindings.Conflicts(KeyR));

            bindings.SetKey(InputAction.InspectMagazine, KeyNames.KeyCodeA + ('I' - 'A'));
            List<string> problems = bindings.ValidateRequiredBindings();
            Assert.Contains(problems, p => p.StartsWith("reload-and-inspect-must-share-a-key"));
        }

        [Fact]
        public void BodycamEffects_AreAlwaysOn_AndNoSettingCanTurnThemOff()
        {
            Assert.True(GameSettings.BodycamEffectsAlwaysOn);
            Assert.False(GameSettings.CrosshairEnabled);

            FieldInfo bodycam = typeof(GameSettings).GetField(nameof(GameSettings.BodycamEffectsAlwaysOn));
            FieldInfo crosshair = typeof(GameSettings).GetField(nameof(GameSettings.CrosshairEnabled));
            Assert.True(bodycam.IsLiteral, "bodycam flag must be a compile time constant");
            Assert.True(crosshair.IsLiteral, "crosshair flag must be a compile time constant");
            Assert.Equal(true, bodycam.GetRawConstantValue());
            Assert.Equal(false, crosshair.GetRawConstantValue());

            // Nothing else in the settings type may expose a switch for either rule.
            foreach (MemberInfo member in typeof(GameSettings).GetMembers(
                         BindingFlags.Public | BindingFlags.NonPublic |
                         BindingFlags.Instance | BindingFlags.Static))
            {
                string name = member.Name.ToLowerInvariant();
                bool mentionsBodycam = name.Contains("bodycam");
                bool mentionsCrosshair = name.Contains("crosshair");
                if (!mentionsBodycam && !mentionsCrosshair)
                {
                    continue;
                }

                Assert.True(member is FieldInfo field && field.IsLiteral,
                    $"{member.Name} must stay a constant: the bodycam look is permanent and there is no crosshair");
            }
        }

        [Fact]
        public void ASettingsFileCannotDisableTheBodycamOrEnableACrosshair()
        {
            GameSettings settings = new GameSettings();
            string json = settings.ToJson();
            Assert.Contains("bodycamEffectsAlwaysOn", json);
            Assert.Contains("crosshairEnabled", json);

            // A hand edited settings file cannot turn either rule off: the constants are
            // not read from JSON at all.
            const string hostile = @"{
              ""fieldOfView"": 80,
              ""mouseSensitivity"": 1.2,
              ""bodycamEffectsAlwaysOn"": false,
              ""crosshairEnabled"": true,
              ""profiles"": []
            }";
            GameSettings restored = GameSettings.FromJson(hostile);
            Assert.True(GameSettings.BodycamEffectsAlwaysOn);
            Assert.False(GameSettings.CrosshairEnabled);
            Assert.Equal(80f, restored.FieldOfView, 3);
        }

        [Fact]
        public void Rebinding_DisplacesTheOtherActionInsteadOfDoubleBinding()
        {
            GameSettings settings = new GameSettings();
            Assert.True(settings.TryRebind(InputAction.Sprint, KeyNames.KeyCodeLeftControl,
                out List<InputAction> displaced));
            Assert.Contains(InputAction.Crouch, displaced);
            Assert.Equal(KeyNames.KeyCodeNone, settings.GetKey(InputAction.Crouch));
            Assert.Equal(KeyNames.KeyCodeLeftControl, settings.GetKey(InputAction.Sprint));

            List<string> problems = settings.ActiveProfile.ValidateRequiredBindings();
            Assert.Contains(problems, p => p == "required-action-unbound:Crouch");
        }

        [Fact]
        public void ABrokenProfileIsReplacedByTheDefaultOnLoad()
        {
            GameSettings settings = new GameSettings();
            settings.ActiveProfile.Unbind(InputAction.MoveForward);
            settings.ActiveProfile.Unbind(InputAction.Aim);

            List<string> notes = settings.ValidateAndClamp();
            Assert.Contains("required-action-unbound:MoveForward", notes);
            Assert.Contains("required-action-unbound:Aim", notes);
            Assert.Equal("Default", settings.ActiveProfileName);
            Assert.Equal(KeyW, settings.GetKey(InputAction.MoveForward));
            Assert.Equal(KeyNames.KeyCodeMouse1, settings.GetKey(InputAction.Aim));
            Assert.Empty(settings.ActiveProfile.ValidateRequiredBindings());
        }

        [Fact]
        public void ControlProfiles_AreIndependentAndSwitchable()
        {
            GameSettings settings = new GameSettings();
            Assert.Equal(2, settings.Profiles.Count);
            Assert.Equal("Default", settings.ActiveProfileName);
            Assert.Equal("Tactical", settings.Profiles[1].ProfileName);

            KeyBindings southpaw = settings.AddProfile("Southpaw");
            Assert.Equal(3, settings.Profiles.Count);
            Assert.Equal("Southpaw", settings.ActiveProfileName);
            Assert.Same(southpaw, settings.ActiveProfile);

            Assert.True(settings.TryRebind(InputAction.Fire, KeyNames.KeyCodeMouse2, out _));
            Assert.Equal(KeyNames.KeyCodeMouse2, settings.GetKey(InputAction.Fire));

            // The other profiles are untouched.
            settings.ActiveProfileName = "Default";
            Assert.Equal(KeyNames.KeyCodeMouse0, settings.GetKey(InputAction.Fire));
            settings.ActiveProfileName = "Tactical";
            Assert.Equal(KeyNames.KeyCodeMouse0, settings.GetKey(InputAction.Fire));

            // An unknown profile name falls back to the first one instead of leaving
            // the player without controls.
            settings.ActiveProfileName = "DoesNotExist";
            Assert.Equal("Default", settings.ActiveProfile.ProfileName);
            Assert.Equal("Default", settings.ActiveProfileName);
        }

        [Fact]
        public void TacticalProfile_IsACompleteAlternativeLayout()
        {
            KeyBindings tactical = KeyBindings.CreateTactical();
            Assert.Empty(tactical.ValidateRequiredBindings());
            Assert.Equal(KeyNames.KeyCodeRightShift, tactical.GetKey(InputAction.Sprint));
            Assert.Equal(KeyC, tactical.GetKey(InputAction.Crouch));
            Assert.Equal(KeyNames.KeyCodeLeftControl, tactical.GetKey(InputAction.ToggleCrouch));
            Assert.Equal(KeyR, tactical.GetKey(InputAction.InspectMagazine));
        }

        [Fact]
        public void ValuesAreClampedIntoLegalRanges()
        {
            GameSettings settings = new GameSettings
            {
                FieldOfView = 400f,
                MouseSensitivity = 99f,
                AimSensitivityMultiplier = 40f,
                RenderScale = 0.01f,
                ViewDistanceMultiplier = 100f,
                MasterVolume = 7f,
                VoiceVolume = -3f,
                SubtitleScale = 25f,
                ResolutionWidth = 320,
                ResolutionHeight = 200
            };

            List<string> notes = settings.ValidateAndClamp();
            Assert.Contains("resolution-reset-to-1080p", notes);

            Assert.Equal(GameSettings.MaxFieldOfView, settings.FieldOfView, 3);
            Assert.Equal(8f, settings.MouseSensitivity, 3);
            Assert.Equal(1.5f, settings.AimSensitivityMultiplier, 3);
            Assert.Equal(0.6f, settings.RenderScale, 3);
            Assert.Equal(2f, settings.ViewDistanceMultiplier, 3);
            Assert.Equal(1f, settings.MasterVolume, 3);
            Assert.Equal(0f, settings.VoiceVolume, 3);
            Assert.Equal(2f, settings.SubtitleScale, 3);
            Assert.Equal(1920, settings.ResolutionWidth);
            Assert.Equal(1080, settings.ResolutionHeight);

            settings.FieldOfView = 1f;
            settings.ValidateAndClamp();
            Assert.Equal(GameSettings.MinFieldOfView, settings.FieldOfView, 3);
            Assert.Equal(74f, GameSettings.DefaultFieldOfView, 3);
        }

        [Fact]
        public void Settings_RoundTripThroughJson()
        {
            GameSettings settings = new GameSettings
            {
                MouseSensitivity = 1.75f,
                InvertVerticalLook = true,
                Quality = QualityPreset.Ultra,
                ResolutionWidth = 2560,
                ResolutionHeight = 1440,
                Fullscreen = false,
                VSync = false,
                FieldOfView = 88f,
                MasterVolume = 0.5f,
                VoiceActivation = true,
                DefaultVoiceChannel = VoiceChannelPreference.TeamRadio,
                SubtitlesEnabled = false,
                SubtitleScale = 1.4f,
                MagazineIndicators = MagazineIndicatorMode.ColorAndPattern,
                ReducedUiMotion = true,
                ToggleSprint = true,
                ToggleCrouch = true,
                LeanMode = LeanInputMode.Toggle,
                HoldToAim = false
            };
            settings.TryRebind(InputAction.Grenade, KeyNames.KeyCodeMouse4, out _);

            GameSettings restored = GameSettings.FromJson(settings.ToJson());
            Assert.Equal(settings.MouseSensitivity, restored.MouseSensitivity, 3);
            Assert.True(restored.InvertVerticalLook);
            Assert.Equal(QualityPreset.Ultra, restored.Quality);
            Assert.Equal(2560, restored.ResolutionWidth);
            Assert.Equal(1440, restored.ResolutionHeight);
            Assert.False(restored.Fullscreen);
            Assert.False(restored.VSync);
            Assert.Equal(88f, restored.FieldOfView, 3);
            Assert.Equal(0.5f, restored.MasterVolume, 3);
            Assert.True(restored.VoiceActivation);
            Assert.Equal(VoiceChannelPreference.TeamRadio, restored.DefaultVoiceChannel);
            Assert.False(restored.SubtitlesEnabled);
            Assert.Equal(1.4f, restored.SubtitleScale, 3);
            Assert.Equal(MagazineIndicatorMode.ColorAndPattern, restored.MagazineIndicators);
            Assert.True(restored.ReducedUiMotion);
            Assert.True(restored.ToggleSprint);
            Assert.True(restored.ToggleCrouch);
            Assert.Equal(LeanInputMode.Toggle, restored.LeanMode);
            Assert.False(restored.HoldToAim);
            Assert.Equal(2, restored.Profiles.Count);
            Assert.Equal(KeyNames.KeyCodeMouse4, restored.GetKey(InputAction.Grenade));
            Assert.Empty(restored.ValidateAndClamp());
        }

        [Fact]
        public void AccessibilityOptionsDefaultToOn()
        {
            GameSettings settings = new GameSettings();
            Assert.True(settings.SubtitlesEnabled);
            Assert.True(settings.SubtitlesShowSpeaker);
            Assert.True(settings.ShowPing);
            Assert.True(settings.ShowKillFeed);
            Assert.True(settings.HitmarkerSound);
            Assert.True(settings.ShowHitDirection);
            Assert.True(settings.LoudnessNormalization);
            Assert.Equal(MagazineIndicatorMode.ColorOnly, settings.MagazineIndicators);
            // Colour plus pattern is offered for colour blind players.
            Assert.Equal(1, (int)MagazineIndicatorMode.ColorAndPattern);
            Assert.False(settings.VoiceActivation);
        }

        [Fact]
        public void KeyNames_DisplayAndParseRoundTrip()
        {
            int[] codes =
            {
                KeyW, KeyA, KeyS, KeyD, KeyR, KeyQ, KeyE, KeyZ, KeyC,
                KeyNames.KeyCodeTab, KeyNames.KeyCodeReturn, KeyNames.KeyCodeEscape, KeyNames.KeyCodeSpace,
                KeyNames.KeyCodeLeftShift, KeyNames.KeyCodeRightShift,
                KeyNames.KeyCodeLeftControl, KeyNames.KeyCodeRightControl, KeyNames.KeyCodeLeftAlt,
                KeyNames.KeyCodeMouse0, KeyNames.KeyCodeMouse1, KeyNames.KeyCodeMouse2,
                KeyNames.KeyCodeMouse3, KeyNames.KeyCodeMouse4,
                KeyNames.KeyCodeAlpha0, KeyNames.KeyCodeAlpha0 + 5
            };

            foreach (int code in codes)
            {
                string display = KeyNames.Display(code);
                Assert.False(string.IsNullOrEmpty(display));
                Assert.Equal(code, KeyNames.Parse(display));
            }

            Assert.Equal("Unbound", KeyNames.Display(KeyNames.KeyCodeNone));
            Assert.Equal(KeyNames.KeyCodeNone, KeyNames.Parse("Unbound"));
            Assert.Equal(KeyNames.KeyCodeNone, KeyNames.Parse("None"));
            Assert.Equal(KeyNames.KeyCodeNone, KeyNames.Parse(string.Empty));
            Assert.Equal("LMB", KeyNames.Display(KeyNames.KeyCodeMouse0));
            Assert.Equal("RMB", KeyNames.Display(KeyNames.KeyCodeMouse1));
            Assert.Equal("L-Shift", KeyNames.Display(KeyNames.KeyCodeLeftShift));
            Assert.Equal("Esc", KeyNames.Display(KeyNames.KeyCodeEscape));
        }

        [Fact]
        public void Bindings_RoundTripThroughJson()
        {
            KeyBindings bindings = KeyBindings.CreateDefault("Preset A");
            bindings.SetKey(InputAction.Grenade, KeyNames.KeyCodeMouse3);

            KeyBindings restored = KeyBindings.FromJson(bindings.ToJson());
            Assert.Equal("Preset A", restored.ProfileName);
            foreach (InputAction action in Enum.GetValues(typeof(InputAction)))
            {
                Assert.Equal(bindings.GetKey(action), restored.GetKey(action));
            }

            // Unknown or malformed input falls back to the default layout rather than
            // leaving the player with no controls.
            Assert.Equal(KeyW, KeyBindings.FromJson(string.Empty).GetKey(InputAction.MoveForward));
            // An entry without a key unbinds that action; ValidateAndClamp then replaces
            // the whole profile with the default layout.
            KeyBindings partial = KeyBindings.FromJson(
                "{ \"bindings\": [ { \"action\": \"MoveForward\" } ] }");
            Assert.Equal(KeyNames.KeyCodeNone, partial.GetKey(InputAction.MoveForward));
            Assert.Contains("required-action-unbound:MoveForward", partial.ValidateRequiredBindings());
        }
    }
}
