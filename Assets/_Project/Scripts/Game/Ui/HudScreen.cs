using Kordon.Core.Settings;
using System.Collections.Generic;
using Kordon.Core.Ammo;
using Kordon.Core.Loadout;
using Kordon.Game.Input;
using Kordon.Game.Weapons;
using UnityEngine;
using UnityEngine.UI;

namespace Kordon.Game.Ui
{
    /// <summary>One scoreboard row, fed by whoever owns the score (server or practice host).</summary>
    public struct ScoreRow
    {
        public string Name;
        public int Kills;
        public int Deaths;
        public int Ping;
        public bool IsLocal;
        public bool IsBot;
    }

    /// <summary>
    /// The in-match HUD and nothing more than the rules allow: the magazine strip
    /// (white full / yellow partial / red empty), the inspect readout which is the
    /// only place an exact round count may appear, the tap-R / hold-R hint, match
    /// line, objective, subtitles, hit marker and the hold-Tab scoreboard.
    /// There is no crosshair and no permanent ammo counter anywhere in this codebase.
    /// </summary>
    public sealed class HudScreen : MonoBehaviour, IUiScreen
    {
        public WeaponController Weapons;
        public GrenadeThrower Grenades;
        public bool ShowHints = true;

        private Canvas _canvas;
        private RectTransform _strip;
        private Text _inspect;
        private Text _hint;
        private Text _matchLine;
        private Text _objective;
        private Text _subtitle;
        private Text _toast;
        private Text _stateLine;
        private RectTransform _scoreboard;
        private RectTransform _scoreRows;
        private Image _hitMarker;
        private Image _damageFlash;
        private Image _lowHealthEdge;
        private Image _flashOverlay;
        private Text _voiceLine;
        private Text _grenadeLine;

        private readonly List<Image> _slotImages = new List<Image>();
        private readonly List<Text> _slotMarks = new List<Text>();
        private float _hitMarkerAt = -10f;
        private float _toastAt = -10f;
        private float _damageFlashAt = -10f;
        private float _blindUntil = -10f;
        private float _blindStartedAt = -10f;
        private int _scoreRowCount = -1;
        private string _lastCount = "";
        private int _lastScoreVersion = -1;
        private List<ScoreRow> _rows = new List<ScoreRow>();
        private int _scoreVersion;

        public string Name => "hud";
        public RectTransform Root => (RectTransform)_canvas.transform;

        private void Awake()
        {
            _canvas = UiBuilder.CreateCanvas("HUD", 10);
            Build();
        }

        public void OnShow() => Root.gameObject.SetActive(true);
        public void OnHide() => Root.gameObject.SetActive(false);

        private void Build()
        {
            RectTransform root = Root;

            _damageFlash = UiBuilder.Panel(root, "DamageFlash", new Color(0.5f, 0.02f, 0.02f, 0f),
                new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(4000f, 4000f));
            _damageFlash.raycastTarget = false;

            _lowHealthEdge = UiBuilder.Panel(root, "LowHealth", new Color(0.35f, 0f, 0f, 0f),
                new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(4000f, 4000f));
            _lowHealthEdge.raycastTarget = false;

            _flashOverlay = UiBuilder.Panel(root, "FlashBang", new Color(1f, 1f, 1f, 0f),
                new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(4000f, 4000f));
            _flashOverlay.raycastTarget = false;

            _strip = UiBuilder.FullRect(root, "MagazineStrip");
            HorizontalLayoutGroup line = _strip.gameObject.AddComponent<HorizontalLayoutGroup>();
            line.spacing = 7f;
            line.childAlignment = TextAnchor.LowerCenter;
            line.childControlWidth = false;
            line.childControlHeight = false;
            line.childForceExpandWidth = false;
            line.childForceExpandHeight = false;
            _strip.offsetMin = new Vector2(0f, 46f);
            _strip.offsetMax = new Vector2(0f, -920f);

            _inspect = UiBuilder.LabelAt(root, "Inspect", "", 26, UiBuilder.Paper,
                new Vector2(0.5f, 0f), new Vector2(0f, 104f), new Vector2(1000f, 36f),
                TextAnchor.MiddleCenter);

            _hint = UiBuilder.LabelAt(root, "Hint",
                "tap R  reload     hold R  inspect magazine", 18,
                new Color(0.78f, 0.80f, 0.77f, 0.7f), new Vector2(0.5f, 0f),
                new Vector2(0f, 70f), new Vector2(1000f, 28f), TextAnchor.MiddleCenter);

            _stateLine = UiBuilder.LabelAt(root, "State", "", 20, UiBuilder.Warning,
                new Vector2(0.5f, 0.5f), new Vector2(0f, -120f), new Vector2(1200f, 30f),
                TextAnchor.MiddleCenter);

            _matchLine = UiBuilder.LabelAt(root, "MatchLine", "", 24, UiBuilder.Paper,
                new Vector2(0f, 1f), new Vector2(32f, -26f), new Vector2(900f, 34f),
                TextAnchor.UpperLeft);

            _objective = UiBuilder.LabelAt(root, "Objective", "", 20, UiBuilder.Accent,
                new Vector2(0.5f, 1f), new Vector2(0f, -26f), new Vector2(1100f, 30f),
                TextAnchor.UpperCenter);

            _subtitle = UiBuilder.LabelAt(root, "Subtitle", "", 24,
                new Color(0.96f, 0.96f, 0.93f, 0.96f), new Vector2(0.5f, 0f),
                new Vector2(0f, 138f), new Vector2(1500f, 34f), TextAnchor.MiddleCenter);

            _toast = UiBuilder.LabelAt(root, "Toast", "", 30, UiBuilder.Danger,
                new Vector2(0.5f, 0.5f), new Vector2(0f, 180f), new Vector2(1400f, 44f),
                TextAnchor.MiddleCenter);

            _hitMarker = UiBuilder.Panel(root, "HitMarker", new Color(1f, 1f, 1f, 0.85f),
                new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(7f, 7f));
            _hitMarker.raycastTarget = false;

            _voiceLine = UiBuilder.LabelAt(root, "Voice", "VOICE   not configured", 17,
                UiBuilder.Dim, new Vector2(0f, 0f), new Vector2(230f, 28f),
                new Vector2(420f, 24f), TextAnchor.MiddleCenter);

            _grenadeLine = UiBuilder.LabelAt(root, "Grenade", "", 18, UiBuilder.Paper,
                new Vector2(1f, 0f), new Vector2(-250f, 46f), new Vector2(480f, 26f),
                TextAnchor.MiddleRight);

            BuildScoreboard(root);
        }

        private void BuildScoreboard(RectTransform root)
        {
            Image board = UiBuilder.Panel(root, "Scoreboard", UiBuilder.Ink,
                new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(900f, 660f));
            _scoreboard = (RectTransform)board.transform;
            Text title = UiBuilder.LabelAt(_scoreboard, "Title", "FREE FOR ALL - FIRST TO 50",
                24, UiBuilder.Accent, new Vector2(0.5f, 1f), new Vector2(0f, -18f),
                new Vector2(860f, 32f), TextAnchor.MiddleCenter);
            title.fontStyle = FontStyle.Bold;
            UiBuilder.Column(_scoreboard, "Rows", 3f, new Vector2(0.5f, 0.5f),
                new Vector2(0f, -14f), new Vector2(860f, 570f));
            _scoreRows = (RectTransform)_scoreboard.Find("Rows");
            _scoreboard.gameObject.SetActive(false);
        }

        // ------------------------------------------------------------------ feed

        public void SetMatchLine(string line)
        {
            if (line != _lastCount)
            {
                _lastCount = line;
                _matchLine.text = line;
            }
        }

        public void SetObjective(string line)
        {
            _objective.text = line;
            _objective.gameObject.SetActive(!string.IsNullOrEmpty(line));
        }

        public void SetSubtitle(string line)
        {
            _subtitle.text = line;
            _subtitle.gameObject.SetActive(!string.IsNullOrEmpty(line));
        }

        public void SetStateLine(string line) => _stateLine.text = line;

        public void Toast(string message)
        {
            _toast.text = message;
            _toastAt = Time.time;
        }

        public void HitMarker() => _hitMarkerAt = Time.time;

        public void DamageFlash() => _damageFlashAt = Time.time;

        /// <summary>
        /// Flashbang effect: a hard white bloom that fades out over the blind time the
        /// blast rules produced.  It never removes control - you can still move, shoot
        /// and reload - it just takes your eyes for a moment.
        /// </summary>
        public void Blind(float seconds)
        {
            float duration = Mathf.Clamp(seconds, 0.2f, 6f);
            _blindStartedAt = Time.time;
            _blindUntil = Time.time + duration;
        }

        public void SetScoreboard(List<ScoreRow> rows)
        {
            _rows = rows;
            _scoreVersion++;
        }

        /// <summary>Squad channel is drawn in the accent colour, proximity in plain paper.</summary>
        public void SetVoiceState(Kordon.Core.Voice.VoiceChannel channel, bool talking, bool configured)
        {
            if (_voiceLine == null)
            {
                return;
            }

            if (!configured)
            {
                _voiceLine.text = "VOICE   not configured";
                _voiceLine.color = UiBuilder.Dim;
                return;
            }

            string label = channel == Kordon.Core.Voice.VoiceChannel.TeamRadio
                ? "SQUAD RADIO"
                : "PROXIMITY";
            _voiceLine.text = "VOICE   " + label + (talking ? "   transmitting" : string.Empty);
            _voiceLine.color = channel == Kordon.Core.Voice.VoiceChannel.TeamRadio
                ? UiBuilder.Accent
                : UiBuilder.Paper;
        }

        public void SetHealth(float health01)
        {
            float low = Mathf.Clamp01(1f - health01 / 0.35f);
            _lowHealthEdge.color = new Color(0.32f, 0f, 0f, low * 0.35f);
        }

        // ---------------------------------------------------------------- update

        private void Update()
        {
            float now = Time.time;

            if (Weapons != null && Weapons.Loadout != null)
            {
                AmmoHudSnapshot snapshot = Weapons.Loadout.BuildHudSnapshot(now);
                RefreshStrip(snapshot);
                RefreshInspect(snapshot);
                RefreshGrenade(snapshot);
            }

            bool wantScoreboard = KordonInput.Held(InputAction.Scoreboard);
            if (wantScoreboard && _scoreVersion != _lastScoreVersion)
            {
                RefreshScoreboard();
                _lastScoreVersion = _scoreVersion;
            }

            _scoreboard.gameObject.SetActive(wantScoreboard && _rows.Count > 0);
            if (wantScoreboard && _rows.Count == 0)
            {
                _scoreboard.gameObject.SetActive(false);
            }

            _hitMarker.gameObject.SetActive(now - _hitMarkerAt < 0.085f);
            _toast.gameObject.SetActive(now - _toastAt < 2.4f);

            float flash = Mathf.Clamp01(1f - (now - _damageFlashAt) / 0.45f);
            _damageFlash.color = new Color(0.45f, 0.02f, 0.02f, flash * 0.32f);

            if (_flashOverlay != null)
            {
                float blind = now < _blindUntil
                    ? Mathf.Clamp01(1f - (now - _blindStartedAt) /
                        Mathf.Max(0.2f, _blindUntil - _blindStartedAt))
                    : 0f;
                _flashOverlay.color = new Color(1f, 1f, 1f, blind * blind * 0.92f);
            }
        }

        private void RefreshStrip(AmmoHudSnapshot snapshot)
        {
            EnsureSlots(snapshot.Slots.Count);

            for (int i = 0; i < _slotImages.Count; i++)
            {
                bool present = i < snapshot.Slots.Count;
                _slotImages[i].gameObject.SetActive(present);
                _slotMarks[i].gameObject.SetActive(present);
                if (!present)
                {
                    continue;
                }

                MagazineHudSlot slot = snapshot.Slots[i];
                _slotImages[i].color = UiBuilder.MagazineColor(slot.ColorKey);
                _slotMarks[i].text = slot.IsChambered ? "|" : "";
            }
        }

        private void EnsureSlots(int count)
        {
            while (_slotImages.Count < count)
            {
                Image slot = UiBuilder.Panel(_strip, "Mag" + _slotImages.Count,
                    UiBuilder.MagazineColor("empty"), new Vector2(0.5f, 0.5f), Vector2.zero,
                    new Vector2(44f, 15f));
                slot.raycastTarget = false;
                LayoutElement element = slot.gameObject.AddComponent<LayoutElement>();
                element.preferredWidth = 44f;
                element.preferredHeight = 15f;
                _slotImages.Add(slot);
                _slotMarks.Add(UiBuilder.LabelAt(slot.transform, "Mark", "", 13,
                    UiBuilder.Ink, new Vector2(0.5f, 0.5f), Vector2.zero,
                    new Vector2(44f, 15f), TextAnchor.MiddleCenter));
            }
        }

        /// <summary>
        /// Grenade line: the count of carried grenades is equipment, not magazine
        /// ammunition, so it is always visible.  While a grenade is cooked the line
        /// shows the burning fuze instead - a warning you can read without a crosshair.
        /// </summary>
        private void RefreshGrenade(AmmoHudSnapshot snapshot)
        {
            if (_grenadeLine == null)
            {
                return;
            }

            GrenadeDefinition grenade = Weapons != null && Weapons.Loadout != null
                ? Weapons.Loadout.Grenade
                : null;
            if (grenade == null)
            {
                _grenadeLine.text = string.Empty;
                return;
            }

            string name = grenade.DisplayName.ToUpperInvariant();
            if (Grenades != null && Grenades.IsCooking)
            {
                float left = Mathf.Max(0f, grenade.FuseSeconds - Grenades.CookSeconds);
                _grenadeLine.color = left <= grenade.CookableMinFuse + 0.4f
                    ? UiBuilder.Danger
                    : UiBuilder.Warning;
                _grenadeLine.text = name + "   HELD   " + left.ToString("0.0") + " s";
                return;
            }

            int count = snapshot.GrenadesRemaining;
            _grenadeLine.color = count > 0 ? UiBuilder.Paper : UiBuilder.Dim;
            _grenadeLine.text = count > 0
                ? name + "   x" + count + "   3 select"
                : name + "   none left";
        }

        private void RefreshInspect(AmmoHudSnapshot snapshot)
        {
            if (snapshot.MayShowExactCount)
            {
                _inspect.text = snapshot.InspectedMagazineName + "   " +
                                snapshot.InspectedRounds + " / " + snapshot.InspectedCapacity +
                                (snapshot.HasReserveAmmunition
                                    ? "   spares in pouch"
                                    : "   last magazine");
                _hint.gameObject.SetActive(false);
            }
            else
            {
                _inspect.text = "";
                _hint.gameObject.SetActive(ShowHints);
            }
        }

        private void RefreshScoreboard()
        {
            UiBuilder.Clear(_scoreRows);
            foreach (ScoreRow row in _rows)
            {
                HorizontalLayoutGroup line = UiBuilder.Row(_scoreRows, "Row", 10f,
                    new Vector2(840f, 26f));
                Color color = row.IsLocal ? UiBuilder.Accent : UiBuilder.Paper;
                if (row.IsBot)
                {
                    color = UiBuilder.Warning;
                }

                string name = row.IsBot ? row.Name + "  [practice bot]" : row.Name;
                UiBuilder.Cell(line.transform, "Name", name, 420f, 20, color);
                UiBuilder.Cell(line.transform, "Kills", row.Kills.ToString(), 120f, 20, color,
                    TextAnchor.MiddleRight);
                UiBuilder.Cell(line.transform, "Deaths", row.Deaths.ToString(), 120f, 20,
                    UiBuilder.Dim, TextAnchor.MiddleRight);
                UiBuilder.Cell(line.transform, "Ping", row.Ping + " ms", 140f, 18,
                    UiBuilder.Dim, TextAnchor.MiddleRight);
            }
        }
    }
}
