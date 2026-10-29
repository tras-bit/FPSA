using System.Collections.Generic;
using Kordon.Core.Ammo;
using Kordon.Core.Loadout;
using Kordon.Core.Settings;
using Kordon.Core.Ui;
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
    /// The in-match HUD and nothing more than the rules allow: the loadout card (which
    /// slot is in hand), the two magazine rows, the inspect readout which is the only
    /// place an exact round count may appear, the tap-R / hold-R hint, match line,
    /// objective, subtitles, hit marker and the hold-Tab scoreboard.
    ///
    /// There is no crosshair and no permanent ammo counter anywhere in this codebase.
    /// The whole bottom right block is measured by <see cref="HudLayout"/> in the
    /// engine free core, so the geometry is unit tested instead of eyeballed.
    /// </summary>
    public sealed class HudScreen : MonoBehaviour, IUiScreen
    {
        /// <summary>Bottom right card colours, kept here so the icon textures match.</summary>
        private static readonly Color CardFill = new Color(0.045f, 0.055f, 0.065f, 0.74f);
        private static readonly Color CardBorder = new Color(1f, 1f, 1f, 0.10f);
        private static readonly Color ChipFill = new Color(1f, 1f, 1f, 0.07f);
        private static readonly Color ActiveFill = new Color(0.88f, 0.46f, 0.13f, 0.16f);

        public WeaponController Weapons;
        public GrenadeThrower Grenades;
        public bool ShowHints = true;

        private Canvas _canvas;

        private RectTransform _loadoutCard;
        private readonly Image[] _rowBackdrops = new Image[3];
        private readonly Image[] _rowPip = new Image[3];
        private readonly Image[] _keyChips = new Image[3];
        private readonly Text[] _keyLabels = new Text[3];
        private readonly Text[] _weaponNames = new Text[3];
        private readonly Text[] _weaponStates = new Text[3];

        private RectTransform _strip;
        private RectTransform _sidearmStrip;
        private readonly List<Image> _slotIcons = new List<Image>();
        private readonly List<Image> _sidearmIcons = new List<Image>();
        private AmmoHudSnapshot _secondarySnapshot;

        private Text _inspect;
        private Text _hint;
        private Text _matchLine;
        private Text _objective;
        private Text _subtitle;
        private Text _toast;
        private Text _stateLine;
        private Text _voiceLine;

        private RectTransform _scoreboard;
        private RectTransform _scoreRows;
        private Image _hitMarker;
        private Image _damageFlash;
        private Image _lowHealthEdge;
        private Image _flashOverlay;

        private float _hitMarkerAt = -10f;
        private float _toastAt = -10f;
        private float _damageFlashAt = -10f;
        private float _blindUntil = -10f;
        private float _blindStartedAt = -10f;
        private string _lastCount = string.Empty;
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

        // ----------------------------------------------------------------- build

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

            BuildLoadoutBlock(root);
            BuildScoreboard(root);

            _matchLine = UiBuilder.LabelAt(root, "MatchLine", "", 22, UiBuilder.Paper,
                new Vector2(0f, 1f), new Vector2(30f, -24f), new Vector2(760f, 30f),
                TextAnchor.UpperLeft);

            _objective = UiBuilder.LabelAt(root, "Objective", "", 19, UiBuilder.Accent,
                new Vector2(0.5f, 1f), new Vector2(0f, -24f), new Vector2(900f, 28f),
                TextAnchor.UpperCenter);

            _subtitle = UiBuilder.LabelAt(root, "Subtitle", "", 22,
                new Color(0.96f, 0.96f, 0.93f, 0.96f), new Vector2(0.5f, 0f),
                new Vector2(0f, 96f), new Vector2(1000f, 30f), TextAnchor.MiddleCenter);

            _stateLine = UiBuilder.LabelAt(root, "State", "", 19, UiBuilder.Warning,
                new Vector2(0.5f, 0.5f), new Vector2(0f, -120f), new Vector2(1100f, 28f),
                TextAnchor.MiddleCenter);

            _toast = UiBuilder.LabelAt(root, "Toast", "", 27, UiBuilder.Danger,
                new Vector2(0.5f, 0.5f), new Vector2(0f, 170f), new Vector2(1200f, 40f),
                TextAnchor.MiddleCenter);

            _hitMarker = UiBuilder.Panel(root, "HitMarker", new Color(1f, 1f, 1f, 0.85f),
                new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(7f, 7f));
            _hitMarker.raycastTarget = false;

            _voiceLine = UiBuilder.LabelAt(root, "Voice", "VOICE   not configured", 16,
                UiBuilder.Dim, new Vector2(0f, 0f), new Vector2(200f, 22f),
                new Vector2(360f, 22f), TextAnchor.MiddleLeft);
        }

        /// <summary>
        /// The whole bottom right block, bottom to top: loadout card, primary magazines,
        /// sidearm magazines, inspect readout.  The card states which slot is in hand and
        /// what is in its pouches; the rows show the magazines themselves as silhouettes
        /// (white full, yellow partial, red empty) and never a round count.
        /// </summary>
        private void BuildLoadoutBlock(RectTransform root)
        {
            HudRect card = HudLayout.LoadoutCard();
            _loadoutCard = Panel(root, "LoadoutCard", card, HudIcons.Rounded(CardFill, CardBorder));

            Text title = UiBuilder.Label(_loadoutCard, "Title", "LOADOUT", 12,
                new Color(0.62f, 0.66f, 0.64f, 0.9f), TextAnchor.MiddleLeft);
            RectTransform titleRect = title.rectTransform;
            titleRect.anchorMin = new Vector2(0f, 1f);
            titleRect.anchorMax = new Vector2(0f, 1f);
            titleRect.pivot = new Vector2(0f, 1f);
            titleRect.sizeDelta = new Vector2(160f, 16f);
            titleRect.anchoredPosition = new Vector2(14f, -9f);
            title.fontStyle = FontStyle.Bold;

            Image rule = UiBuilder.Panel(_loadoutCard, "Rule", new Color(1f, 1f, 1f, 0.08f),
                new Vector2(0.5f, 1f), Vector2.zero, new Vector2(HudLayout.ColumnWidth - 24f, 1f));
            rule.rectTransform.anchoredPosition = new Vector2(0f, -24f);
            rule.raycastTarget = false;

            string[] keys = { "1", "2", "3" };
            for (int i = 0; i < 3; i++)
            {
                RectTransform row = UiBuilder.Node(_loadoutCard, "Slot" + i);
                row.anchorMin = new Vector2(0f, 1f);
                row.anchorMax = new Vector2(1f, 1f);
                row.pivot = new Vector2(0.5f, 1f);
                row.sizeDelta = new Vector2(-16f, 26f);
                row.anchoredPosition = new Vector2(0f, -(27f + i * 27f));

                _rowBackdrops[i] = row.gameObject.AddComponent<Image>();
                _rowBackdrops[i].sprite = HudIcons.Rounded(ActiveFill, new Color(0f, 0f, 0f, 0f));
                _rowBackdrops[i].type = Image.Type.Sliced;
                _rowBackdrops[i].raycastTarget = false;

                _rowPip[i] = UiBuilder.Panel(row, "Pip", UiBuilder.Accent,
                    new Vector2(0f, 0.5f), new Vector2(4f, 0f), new Vector2(3f, 16f));
                _rowPip[i].raycastTarget = false;

                RectTransform chip = UiBuilder.Node(row, "Key");
                chip.anchorMin = new Vector2(0f, 0.5f);
                chip.anchorMax = new Vector2(0f, 0.5f);
                chip.pivot = new Vector2(0f, 0.5f);
                chip.sizeDelta = new Vector2(22f, 18f);
                chip.anchoredPosition = new Vector2(11f, 0f);
                _keyChips[i] = chip.gameObject.AddComponent<Image>();
                _keyChips[i].sprite = HudIcons.Rounded(ChipFill, new Color(1f, 1f, 1f, 0.06f));
                _keyChips[i].type = Image.Type.Sliced;
                _keyChips[i].raycastTarget = false;

                _keyLabels[i] = UiBuilder.Label(chip, "Key", keys[i], 12, UiBuilder.Dim,
                    TextAnchor.MiddleCenter);
                Stretch(_keyLabels[i].rectTransform, 0f, 0f);

                _weaponNames[i] = UiBuilder.Label(row, "Name", string.Empty, 16, UiBuilder.Dim,
                    TextAnchor.MiddleLeft);
                RectTransform nameRect = _weaponNames[i].rectTransform;
                nameRect.anchorMin = Vector2.zero;
                nameRect.anchorMax = Vector2.one;
                nameRect.offsetMin = new Vector2(40f, 0f);
                nameRect.offsetMax = new Vector2(-78f, 0f);
                _weaponNames[i].fontStyle = FontStyle.Bold;

                _weaponStates[i] = UiBuilder.Label(row, "State", string.Empty, 14, UiBuilder.Dim,
                    TextAnchor.MiddleRight);
                RectTransform stateRect = _weaponStates[i].rectTransform;
                stateRect.anchorMin = new Vector2(1f, 0f);
                stateRect.anchorMax = new Vector2(1f, 1f);
                stateRect.pivot = new Vector2(1f, 0.5f);
                stateRect.sizeDelta = new Vector2(74f, 0f);
                stateRect.anchoredPosition = new Vector2(-10f, 0f);
            }

            HudRect mags = HudLayout.MagazineRow();
            _strip = Panel(root, "PrimaryMags", mags, null);
            HorizontalLayoutGroup primary = _strip.gameObject.AddComponent<HorizontalLayoutGroup>();
            primary.spacing = HudLayout.MagazineIconGap;
            primary.childAlignment = TextAnchor.MiddleRight;
            primary.childControlWidth = false;
            primary.childControlHeight = false;
            primary.childForceExpandWidth = false;
            primary.childForceExpandHeight = false;
            RowCaption(root, mags, "PRIMARY");

            HudRect sidearm = HudLayout.SidearmRow();
            _sidearmStrip = Panel(root, "SidearmMags", sidearm, null);
            HorizontalLayoutGroup secondary =
                _sidearmStrip.gameObject.AddComponent<HorizontalLayoutGroup>();
            secondary.spacing = HudLayout.MagazineIconGap;
            secondary.childAlignment = TextAnchor.MiddleRight;
            secondary.childControlWidth = false;
            secondary.childControlHeight = false;
            secondary.childForceExpandWidth = false;
            secondary.childForceExpandHeight = false;
            RowCaption(root, sidearm, "SIDEARM");

            HudRect inspect = HudLayout.InspectLine();
            _inspect = UiBuilder.LabelAt(root, "Inspect", string.Empty, 22, UiBuilder.Accent,
                new Vector2(1f, 0f),
                new Vector2(-inspect.Right, inspect.Bottom),
                new Vector2(inspect.Width, inspect.Height), TextAnchor.MiddleRight);

            _hint = UiBuilder.LabelAt(root, "Hint",
                "tap R  reload     hold R  inspect", 16,
                new Color(0.78f, 0.80f, 0.77f, 0.72f), new Vector2(0.5f, 0f),
                new Vector2(0f, 28f), new Vector2(HudLayout.HintHalfWidth * 2f, 24f),
                TextAnchor.MiddleCenter);
        }

        /// <summary>A fixed size, bottom-right anchored panel rectangle.</summary>
        private static RectTransform Panel(RectTransform root, string name, HudRect rect,
            Sprite sprite)
        {
            RectTransform node = UiBuilder.Node(root, name);
            node.anchorMin = new Vector2(1f, 0f);
            node.anchorMax = new Vector2(1f, 0f);
            node.pivot = new Vector2(1f, 0f);
            node.sizeDelta = new Vector2(rect.Width, rect.Height);
            node.anchoredPosition = new Vector2(-rect.Right, rect.Bottom);
            if (sprite == null)
            {
                return node;
            }

            Image image = node.gameObject.AddComponent<Image>();
            image.sprite = sprite;
            image.type = Image.Type.Sliced;
            image.raycastTarget = false;
            return node;
        }

        private void RowCaption(RectTransform root, HudRect rect, string caption)
        {
            Text label = UiBuilder.LabelAt(root, caption + " MAGS", caption + " MAGS",
                11, new Color(0.62f, 0.66f, 0.64f, 0.78f), new Vector2(1f, 0f),
                new Vector2(-rect.Right - rect.Width + 2f, rect.Bottom + 6f),
                new Vector2(160f, 16f), TextAnchor.MiddleLeft);
            label.fontStyle = FontStyle.Bold;
        }

        private static void Stretch(RectTransform rect, float horizontal, float vertical)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = new Vector2(horizontal, vertical);
            rect.offsetMax = new Vector2(-horizontal, -vertical);
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
                LoadoutKit loadout = Weapons.Loadout;
                AmmoHudSnapshot snapshot = loadout.BuildHudSnapshot(now);
                RefreshMagazines(snapshot, _strip, _slotIcons,
                    HudLayout.MagazineIconWidth, HudLayout.MagazineIconHeight);
                RefreshSidearm(now);
                RefreshLoadoutCard(snapshot);
                RefreshInspect(snapshot);
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

        /// <summary>
        /// The card: three slots with their key, name and pouch contents.  The active slot
        /// is the only one in the accent colour, so "which weapon is in hand" is one
        /// glance; the grenade row becomes the live fuze readout while one is cooking.
        /// </summary>
        private void RefreshLoadoutCard(AmmoHudSnapshot snapshot)
        {
            LoadoutKit loadout = Weapons.Loadout;
            string primary = loadout.Primary != null && loadout.Primary.Weapon != null
                ? loadout.Primary.Weapon.DisplayName.ToUpperInvariant()
                : "NO PRIMARY";
            string secondary = loadout.Secondary != null && loadout.Secondary.Weapon != null
                ? loadout.Secondary.Weapon.DisplayName.ToUpperInvariant()
                : "NO SIDEARM";
            string grenade = loadout.Grenade != null &&
                             !string.IsNullOrEmpty(loadout.Grenade.DisplayName)
                ? loadout.Grenade.DisplayName.ToUpperInvariant()
                : "GRENADE";

            string grenadeState = GrenadeReadout(loadout, snapshot, out Color grenadeColour);
            string[] names = { primary, secondary, grenade };
            string[] states = { MagazineCount(snapshot), MagazineCount(_secondarySnapshot), grenadeState };

            for (int i = 0; i < 3; i++)
            {
                bool active = (int)loadout.Active == i;
                Color text = active ? UiBuilder.Accent : UiBuilder.Dim;
                _weaponNames[i].text = names[i];
                _weaponNames[i].color = text;
                _weaponStates[i].text = states[i];
                _weaponStates[i].color = i == 2 ? grenadeColour : text;

                _rowBackdrops[i].enabled = active;
                _rowPip[i].enabled = active;
                _keyChips[i].sprite = HudIcons.Rounded(
                    active ? new Color(0.88f, 0.46f, 0.13f, 0.85f) : ChipFill, CardBorder);
                _keyLabels[i].color = active ? UiBuilder.Ink : UiBuilder.Dim;
            }
        }

        /// <summary>
        /// The grenade row doubles as the fuze readout: the count while they are carried,
        /// and the burning fuze in warning or danger colour while one is cooked.  The
        /// count of grenades is equipment, not ammunition, so it is always on screen.
        /// </summary>
        private string GrenadeReadout(LoadoutKit loadout, AmmoHudSnapshot snapshot,
            out Color colour)
        {
            colour = UiBuilder.Dim;
            if (loadout.Grenade == null)
            {
                return string.Empty;
            }

            if (Grenades != null && Grenades.IsCooking)
            {
                float left = Mathf.Max(0f, loadout.Grenade.FuseSeconds - Grenades.CookSeconds);
                colour = left <= loadout.Grenade.CookableMinFuse + 0.4f
                    ? UiBuilder.Danger
                    : UiBuilder.Warning;
                return left.ToString("0.0") + " s";
            }

            int count = snapshot.GrenadesRemaining;
            colour = count > 0 ? UiBuilder.Paper : UiBuilder.Danger;
            return count > 0 ? "x" + count : "NONE";
        }

        /// <summary>"4 MAG" style pouch summary.  Never a round count: the pouch holds
        /// magazines, and their contents stay unknown until the magazine is checked.</summary>
        private static string MagazineCount(AmmoHudSnapshot snapshot) =>
            snapshot == null || snapshot.Slots == null
                ? string.Empty
                : snapshot.Slots.Count + " MAG";

        private void RefreshSidearm(float now)
        {
            if (Weapons == null || Weapons.Loadout == null || Weapons.Loadout.Secondary == null)
            {
                return;
            }

            _secondarySnapshot = Weapons.Loadout.Secondary.BuildHudSnapshot(now);
            RefreshMagazines(_secondarySnapshot, _sidearmStrip, _sidearmIcons,
                HudLayout.SidearmIconWidth, HudLayout.SidearmIconHeight);
        }

        /// <summary>
        /// Draws one row of magazine silhouettes, right aligned.  The icon carries the
        /// pocket state as a colour (white full, yellow partial, red empty) and a bright
        /// round on top when the magazine is chambered.
        /// </summary>
        private void RefreshMagazines(AmmoHudSnapshot snapshot, RectTransform row,
            List<Image> icons, float width, float height)
        {
            if (row == null || snapshot == null || snapshot.Slots == null)
            {
                return;
            }

            while (icons.Count < snapshot.Slots.Count)
            {
                RectTransform node = UiBuilder.Node(row, "Mag" + icons.Count);
                node.anchorMin = new Vector2(1f, 0.5f);
                node.anchorMax = new Vector2(1f, 0.5f);
                node.pivot = new Vector2(0.5f, 0.5f);
                node.sizeDelta = new Vector2(width, height);
                Image icon = node.gameObject.AddComponent<Image>();
                icon.preserveAspect = true;
                icon.raycastTarget = false;
                LayoutElement element = node.gameObject.AddComponent<LayoutElement>();
                element.preferredWidth = width;
                element.preferredHeight = height;
                icons.Add(icon);
            }

            for (int i = 0; i < icons.Count; i++)
            {
                bool present = i < snapshot.Slots.Count;
                icons[i].gameObject.SetActive(present);
                if (!present)
                {
                    continue;
                }

                MagazineHudSlot slot = snapshot.Slots[i];
                Color colour = UiBuilder.MagazineColor(slot.ColorKey);
                icons[i].sprite = HudIcons.Magazine(colour, slot.IsChambered);
            }
        }

        private void RefreshInspect(AmmoHudSnapshot snapshot)
        {
            if (snapshot.MayShowExactCount)
            {
                _inspect.text = snapshot.InspectedRounds + " / " + snapshot.InspectedCapacity;
                _hint.gameObject.SetActive(false);
            }
            else
            {
                _inspect.text = string.Empty;
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
