using System.Collections.Generic;
using Kordon.Core.Ammo;
using Kordon.Game.Input;
using Kordon.Game.Weapons;
using UnityEngine;
using UnityEngine.UI;

namespace Kordon.Game.Ui
{
    /// <summary>One scoreboard row handed to the HUD by whoever owns the score.</summary>
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
    /// The in-match HUD, and only the HUD the rules allow: the magazine strip
    /// (white full / yellow partial / red empty), the inspect readout that is the
    /// only place an exact round count may appear, the reload/inspect hint, match
    /// line, subtitles, objective line and the hold-Tab scoreboard.  No crosshair,
    /// no permanent ammo counter - those do not exist in this codebase at all.
    /// </summary>
    public sealed class HudScreen : MonoBehaviour
    {
        public WeaponController Weapons;

        private Canvas _canvas;
        private RectTransform _strip;
        private Text _inspect;
        private Text _hint;
        private Text _matchLine;
        private Text _objective;
        private Text _subtitle;
        private Text _toast;
        private RectTransform _scoreboard;
        private RectTransform _scoreRows;
        private Image _hitMarker;

        private readonly List<Image> _slotImages = new List<Image>();
        private readonly List<Text> _slotMarks = new List<Text>();
        private float _hitMarkerTime = -10f;
        private float _toastTime = -10f;
        private string _lastSubtitle = "";
        private string _lastObjective = "";
        private string _lastMatchLine = "";
        private List<ScoreRow> _lastScore;

        public bool Visible
        {
            get => _canvas != null && _canvas.gameObject.activeSelf;
            set
            {
                if (_canvas != null)
                {
                    _canvas.gameObject.SetActive(value);
                }
            }
        }

        private void Awake()
        {
            _canvas = UiBuilder.CreateCanvas("Hud", 10);
            Build();
        }

        private void Build()
        {
            RectTransform root = _canvas.transform as RectTransform;

            // Magazine strip, bottom centre.
            _strip = UiBuilder.Panel(root, "MagazineStrip", new Color(0f, 0f, 0f, 0f),
                new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 46f),
                new Vector2(900f, 40f));
            HorizontalLayoutGroup line = _strip.gameObject.AddComponent<HorizontalLayoutGroup>();
            line.spacing = 6f;
            line.childAlignment = TextAnchor.MiddleCenter;
            line.childForceExpandWidth = false;

            // Inspect readout above the strip: exact counts live here and nowhere else.
            _inspect = UiBuilder.Label(root, "Inspect", "", 26, TextAnchor.MiddleCenter,
                UiBuilder.Paper);
            Place(_inspect.rectTransform, new Vector2(0.5f, 0f), new Vector2(0f, 108f),
                new Vector2(900f, 40f));

            _hint = UiBuilder.Label(root, "Hint", "Tap R - reload      Hold R - inspect magazine",
                18, TextAnchor.MiddleCenter, new Color(0.75f, 0.78f, 0.76f, 0.75f));
            Place(_hint.rectTransform, new Vector2(0.5f, 0f), new Vector2(0f, 148f),
                new Vector2(900f, 28f));

            _matchLine = UiBuilder.Label(root, "MatchLine", "", 22, TextAnchor.UpperLeft,
                UiBuilder.Paper);
            Place(_matchLine.rectTransform, new Vector2(0f, 1f), new Vector2(28f, -24f),
                new Vector2(700f, 32f));

            _objective = UiBuilder.Label(root, "Objective", "", 20, TextAnchor.UpperCenter,
                UiBuilder.Accent);
            Place(_objective.rectTransform, new Vector2(0.5f, 1f), new Vector2(0f, -26f),
                new Vector2(1100f, 30f));

            _subtitle = UiBuilder.Label(root, "Subtitle", "", 24, TextAnchor.LowerCenter,
                new Color(0.95f, 0.95f, 0.92f, 0.95f));
            Place(_subtitle.rectTransform, new Vector2(0.5f, 0f), new Vector2(0f, 190f),
                new Vector2(1400f, 34f));

            _toast = UiBuilder.Label(root, "Toast", "", 26, TextAnchor.MiddleCenter,
                UiBuilder.Danger, FontStyle.Bold);
            Place(_toast.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0f, 140f),
                new Vector2(1200f, 40f));

            _hitMarker = UiBuilder.Panel(root, "HitMarker", new Color(1f, 1f, 1f, 0.9f),
                new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0f, 0f),
                new Vector2(6f, 6f)).GetComponent<Image>();
            _hitMarker.raycastTarget = false;
            _hitMarker.gameObject.SetActive(false);

            BuildScoreboard(root);
        }

        private void BuildScoreboard(RectTransform root)
        {
            _scoreboard = UiBuilder.Panel(root, "Scoreboard", UiBuilder.Ink,
                new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero,
                new Vector2(860f, 640f));
            _scoreboard.gameObject.SetActive(false);

            Text title = UiBuilder.Label(_scoreboard, "Title", "SCOREBOARD", 26,
                TextAnchor.UpperCenter, UiBuilder.Accent, FontStyle.Bold);
            Place(title.rectTransform, new Vector2(0.5f, 1f), new Vector2(0f, -18f),
                new Vector2(800f, 34f));

            _scoreRows = UiBuilder.Column(_scoreboard, "Rows", 4f);
            RectTransform rowsRect = _scoreRows.rectTransform;
            rowsRect.anchorMin = new Vector2(0f, 0f);
            rowsRect.anchorMax = new Vector2(1f, 1f);
            rowsRect.offsetMin = new Vector2(30f, 24f);
            rowsRect.offsetMax = new Vector2(-30f, -60f);
        }

        private static void Place(RectTransform rect, Vector2 anchor, Vector2 position,
            Vector2 size)
        {
            rect.anchorMin = anchor;
            rect.anchorMax = anchor;
            rect.anchoredPosition = position;
            rect.sizeDelta = size;
        }

        // ------------------------------------------------------------------ feed

        public void SetMatchLine(string line)
        {
            if (line == _lastMatchLine)
            {
                return;
            }

            _lastMatchLine = line;
            _matchLine.text = line;
        }

        public void SetObjective(string line)
        {
            if (line == _lastObjective)
            {
                return;
            }

            _lastObjective = line;
            _objective.text = line;
            _objective.gameObject.SetActive(!string.IsNullOrEmpty(line));
        }

        public void SetSubtitle(string line)
        {
            if (line == _lastSubtitle)
            {
                return;
            }

            _lastSubtitle = line;
            _subtitle.text = line;
            _subtitle.gameObject.SetActive(!string.IsNullOrEmpty(line));
        }

        public void Toast(string message)
        {
            _toast.text = message;
            _toastTime = Time.time;
        }

        public void HitMarker() => _hitMarkerTime = Time.time;

        public void SetScoreboard(List<ScoreRow> rows) => _lastScore = rows;

        // ---------------------------------------------------------------- update

        private void Update()
        {
            if (Weapons == null)
            {
                return;
            }

            AmmoHudSnapshot snapshot = Weapons.Loadout != null
                ? Weapons.Loadout.BuildHudSnapshot(Time.time)
                : null;
            if (snapshot != null)
            {
                RefreshStrip(snapshot);
                RefreshInspect(snapshot);
            }

            bool showScore = KordonInput.Held(InputAction.Scoreboard);
            if (showScore && _lastScore != null)
            {
                RefreshScoreboard();
            }

            _scoreboard.gameObject.SetActive(showScore && _lastScore != null);

            float now = Time.time;
            _hitMarker.gameObject.SetActive(now - _hitMarkerTime < 0.09f);
            _toast.gameObject.SetActive(now - _toastTime < 2.5f);
        }

        private void RefreshStrip(AmmoHudSnapshot snapshot)
        {
            while (_slotImages.Count < snapshot.Slots.Count)
            {
                int index = _slotImages.Count;
                RectTransform slot = UiBuilder.Panel(_strip, "Slot" + index,
                    UiBuilder.MagazineEmpty, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                    Vector2.zero, new Vector2(46f, 16f));
                LayoutElement element = slot.gameObject.AddComponent<LayoutElement>();
                element.preferredWidth = 46f;
                element.preferredHeight = 16f;
                _slotImages.Add(slot.GetComponent<Image>());
                _slotMarks.Add(UiBuilder.Label(slot, "Mark", "", 14, TextAnchor.MiddleCenter,
                    UiBuilder.InkSolid));
            }

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

        private void RefreshInspect(AmmoHudSnapshot snapshot)
        {
            if (snapshot.MayShowExactCount)
            {
                string reserve = snapshot.HasReserveAmmunition
                    ? "reserve magazines in pouch"
                    : "no reserve magazines";
                _inspect.text = string.Format("{0}: {1}/{2} - {3}",
                    snapshot.InspectedMagazineName, snapshot.InspectedRounds,
                    snapshot.InspectedCapacity, reserve);
                _hint.gameObject.SetActive(false);
            }
            else
            {
                _inspect.text = "";
                _hint.gameObject.SetActive(snapshot.Interaction == AmmoInteraction.Idle);
            }
        }

        private void RefreshScoreboard()
        {
            while (_scoreRows.childCount < _lastScore.Count)
            {
                // Rows are rebuilt from scratch whenever the count changes; the
                // scoreboard only exists while Tab is held, so churn is rare.
                foreach (Transform child in _scoreRows)
                {
                    Object.Destroy(child.gameObject);
                }

                for (int i = 0; i < _lastScore.Count; i++)
                {
                    RectTransform row = UiBuilder.Row(_scoreRows, "Row" + i,
                        new Vector2(800f, 34f));
                    HorizontalLayoutGroup line = row.gameObject.AddComponent<HorizontalLayoutGroup>();
                    line.spacing = 12f;
                    line.childForceExpandWidth = false;
                    ScoreRow data = _lastScore[i];
                    AddCell(row, data.IsLocal ? "> " + data.Name : data.Name, 380f,
                        data.IsBot ? UiBuilder.MagazinePartial : UiBuilder.Paper);
                    AddCell(row, data.Kills.ToString(), 90f, UiBuilder.Paper);
                    AddCell(row, data.Deaths.ToString(), 90f, UiBuilder.Paper);
                    AddCell(row, data.Ping + " ms", 120f, UiBuilder.Paper);
                }

                break;
            }
        }

        private void AddCell(RectTransform row, string content, float width, Color color)
        {
            Text cell = UiBuilder.Label(row, "Cell", content, 20, TextAnchor.MiddleLeft, color);
            LayoutElement element = cell.gameObject.AddComponent<LayoutElement>();
            element.preferredWidth = width;
        }
    }
}
