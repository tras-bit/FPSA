using System.Collections.Generic;
using Kordon.Core.Loadout;
using Kordon.Game.Match;
using UnityEngine;
using UnityEngine.UI;

namespace Kordon.Game.Ui
{
    /// <summary>
    /// Loadout preparation. The magazine plan is a first-class decision here, not
    /// an afterthought: you pick how many magazines and of which type before the
    /// match, the kit is sealed when the round starts, and nothing can be changed
    /// after that. Everything on this screen is available to every player from the
    /// first match - there are no unlocks that change performance.
    /// </summary>
    public sealed class PrepScreen : UiScreenBase
    {
        public override string Name => "prep";

        private static readonly string[] Primaries = { "vk74", "tr30", "vs9", "dmr14", "sr338", "k870" };
        private static readonly string[] Secondaries = { "p9" };
        private static readonly string[] Armors = { "light", "standard", "heavy" };
        private static readonly string[] Grenades = { "frag_f1", "smoke_rdg", "flash_zarya" };
        private static readonly int[] MagazineCounts = { 3, 4, 5, 6, 7, 8 };
        private static readonly SightKind[] Sights =
        {
            SightKind.IronSight, SightKind.RedDot, SightKind.Holographic, SightKind.Scope4x,
        };

        private int _primary;
        private int _secondary;
        private int _armor;
        private int _grenade;
        private int _primaryMagCount;
        private int _secondaryMagCount;
        private int _primarySight;
        private string _primaryMagId = string.Empty;

        private Text _primaryLabel;
        private Text _secondaryLabel;
        private Text _armorLabel;
        private Button _grenadeButton;
        private Text _planLabel;
        private Text _summaryLabel;
        private Button _sightButton;
        private Text _warningLabel;

        private const float PanelWidth = 1180f;
        private const float PanelHeight = 880f;

        protected override void Build()
        {
            Image background = UiBuilder.Panel(Root, "Background",
                new Color(0.035f, 0.042f, 0.048f, 1f), new Vector2(0.5f, 0.5f), Vector2.zero,
                new Vector2(4000f, 4000f));
            background.raycastTarget = true;

            Image panel = UiBuilder.Panel(Root, "Panel", UiBuilder.Ink, new Vector2(0.5f, 0.5f),
                Vector2.zero, new Vector2(PanelWidth, PanelHeight));

            UiStack stack = new UiStack(panel.transform, "Stack", PanelWidth - 64f, PanelHeight,
                paddingTop: 22f, paddingBottom: 22f, spacing: 7f);

            Text title = stack.Text("KIT - PREPARE FOR DEPLOYMENT", 32, UiBuilder.Paper, 42f,
                TextAnchor.MiddleCenter);
            if (title != null)
            {
                title.fontStyle = FontStyle.Bold;
            }

            stack.Text("The kit locks when the round starts. Magazines are chosen here, once.",
                16, UiBuilder.Dim, 22f, TextAnchor.MiddleCenter);

            stack.Section("primary");
            BuildWeaponRow(stack, "Primary", CyclePrimary, CycleSight, out _primaryLabel,
                out _sightButton);
            stack.Section("secondary");
            BuildWeaponRow(stack, "Secondary", CycleSecondary, null, out _secondaryLabel, out _);

            stack.Section("magazines");
            _planLabel = stack.Text(string.Empty, 19, UiBuilder.Paper, 26f);
            stack.RawButtons(46f, new (string, UnityEngine.Events.UnityAction)[]
            {
                ("- MAG", () => CyclePrimaryMags(-1)),
                ("+ MAG", () => CyclePrimaryMags(1)),
                ("MAG TYPE", () => CycleMagType()),
                ("+ PISTOL MAGS", () => CycleSecondaryMags(1)),
            }, 15);

            stack.Section("equipment");
            _armorLabel = stack.Text(string.Empty, 17, UiBuilder.Dim, 22f);
            stack.Pair("CHANGE PLATES", () => CycleArmor(), "GRENADE: FRAG x2",
                () => CycleGrenade(), 46f, out _, out _grenadeButton, 12f, 16);
            stack.Space(2f);
            stack.RawButtons(44f, new (string, UnityEngine.Events.UnityAction)[]
            {
                ("PRESET: ASSAULT", () => ApplyPreset(0)),
                ("PRESET: BREACHER", () => ApplyPreset(1)),
                ("PRESET: MARKSMAN", () => ApplyPreset(2)),
                ("PROFILES", () => CycleControlProfile()),
            }, 14);

            _summaryLabel = stack.Text(string.Empty, 17, UiBuilder.Dim, 76f,
                TextAnchor.MiddleCenter);
            _warningLabel = stack.Text(string.Empty, 16, UiBuilder.Danger, 26f,
                TextAnchor.MiddleCenter);

            stack.Space(2f);
            RectTransform footer = stack.RawRow(56f);
            if (footer != null)
            {
                RectTransform deploy = UiBuilder.Node(footer, "Deploy");
                deploy.anchorMin = new Vector2(0f, 0f);
                deploy.anchorMax = new Vector2(1f, 1f);
                deploy.offsetMin = Vector2.zero;
                deploy.offsetMax = new Vector2(-220f, 0f);
                UiBuilder.ButtonIn(footer, "LOCK KIT AND DEPLOY TO PRACTICE",
                    () => LockInPractice(), deploy, 18);

                RectTransform back = UiBuilder.Node(footer, "Back");
                back.anchorMin = new Vector2(1f, 0f);
                back.anchorMax = new Vector2(1f, 1f);
                back.pivot = new Vector2(1f, 0.5f);
                back.offsetMin = Vector2.zero;
                back.offsetMax = Vector2.zero;
                back.sizeDelta = new Vector2(200f, 0f);
                UiBuilder.ButtonIn(footer, "BACK", () => Back(), back, 17);
            }

            SelectDefaults();
            Refresh();
        }

        /// <summary>Weapon line: stepper on the left, optional sight button on the right.</summary>
        private static void BuildWeaponRow(UiStack stack, string caption, System.Action<int> step,
            UnityEngine.Events.UnityAction sightAction, out Text valueLabel, out Button sightButton)
        {
            sightButton = null;
            valueLabel = null;

            RectTransform row = stack.RawRow(46f);
            if (row == null)
            {
                return;
            }

            RectTransform left = UiBuilder.Node(row, "Left");
            left.anchorMin = new Vector2(0f, 0f);
            left.anchorMax = new Vector2(0f, 1f);
            left.pivot = new Vector2(0f, 0.5f);
            left.offsetMin = Vector2.zero;
            left.offsetMax = Vector2.zero;
            left.sizeDelta = new Vector2(54f, 0f);
            UiBuilder.ButtonIn(row, "<", () => step(-1), left, 20);

            RectTransform right = UiBuilder.Node(row, "Right");
            right.anchorMin = new Vector2(0f, 0f);
            right.anchorMax = new Vector2(0f, 1f);
            right.pivot = new Vector2(0f, 0.5f);
            right.offsetMin = Vector2.zero;
            right.offsetMax = Vector2.zero;
            right.sizeDelta = new Vector2(54f, 0f);
            right.anchoredPosition = new Vector2(62f, 0f);
            UiBuilder.ButtonIn(row, ">", () => step(1), right, 20);

            valueLabel = UiBuilder.Label(row, "Value", string.Empty, 22, UiBuilder.Paper,
                TextAnchor.MiddleLeft);
            valueLabel.rectTransform.anchorMin = new Vector2(0f, 0f);
            valueLabel.rectTransform.anchorMax = new Vector2(1f, 1f);
            valueLabel.rectTransform.offsetMin = new Vector2(134f, 0f);
            valueLabel.rectTransform.offsetMax = new Vector2(-280f, 0f);

            RectTransform sight = UiBuilder.Node(row, "Sight");
            sight.anchorMin = new Vector2(1f, 0f);
            sight.anchorMax = new Vector2(1f, 1f);
            sight.pivot = new Vector2(1f, 0.5f);
            sight.offsetMin = Vector2.zero;
            sight.offsetMax = Vector2.zero;
            sight.sizeDelta = new Vector2(260f, 0f);
            if (sightAction != null)
            {
                sightButton = UiBuilder.ButtonIn(row, "SIGHT: IRON", sightAction, sight, 16);
            }
            else
            {
                sight.gameObject.SetActive(false);
            }
        }

        // ------------------------------------------------------------ selections

        private void SelectDefaults()
        {
            LoadoutSelection current = Flow.Loadout;
            _primary = IndexOf(Primaries, current.PrimaryWeaponId);
            _secondary = IndexOf(Secondaries, current.SecondaryWeaponId);
            _armor = IndexOf(Armors, current.ArmorKitId);
            _grenade = IndexOf(Grenades, current.GrenadeId);
            _primarySight = IndexOf(Sights, current.PrimarySight);
            _primaryMagCount = Mathf.Clamp(current.PrimaryMagazines.Count > 0
                ? current.PrimaryMagazines[0].Count
                : 6, 1, 8);
            _secondaryMagCount = current.SecondaryMagazines.Count > 0
                ? current.SecondaryMagazines[0].Count
                : 2;
            _primaryMagId = current.PrimaryMagazines.Count > 0
                ? current.PrimaryMagazines[0].MagazineId
                : string.Empty;
        }

        private static int IndexOf<T>(T[] values, T value)
        {
            for (int i = 0; i < values.Length; i++)
            {
                if (EqualityComparer<T>.Default.Equals(values[i], value))
                {
                    return i;
                }
            }

            return 0;
        }

        private WeaponDefinition PrimaryWeapon =>
            Flow.Database.Definitions.Weapons.TryGetValue(Primaries[_primary],
                out WeaponDefinition weapon) ? weapon : null;

        private List<MagazineDefinition> PrimaryMagazines()
        {
            List<MagazineDefinition> magazines = new List<MagazineDefinition>();
            WeaponDefinition weapon = PrimaryWeapon;
            if (weapon == null)
            {
                return magazines;
            }

            foreach (MagazineDefinition magazine in
                     Flow.Database.Definitions.MagazinesForWeapon(weapon))
            {
                magazines.Add(magazine);
            }

            return magazines;
        }

        private void CyclePrimary(int direction)
        {
            _primary = Wrap(_primary + direction, Primaries.Length);
            _primaryMagId = string.Empty;
            Refresh();
        }

        private void CycleSecondary(int direction)
        {
            _secondary = Wrap(_secondary + direction, Secondaries.Length);
            Refresh();
        }

        private void CycleArmor()
        {
            _armor = Wrap(_armor + 1, Armors.Length);
            Refresh();
        }

        private void CycleGrenade()
        {
            _grenade = Wrap(_grenade + 1, Grenades.Length);
            Refresh();
        }

        private void CycleSight()
        {
            _primarySight = Wrap(_primarySight + 1, Sights.Length);
            Refresh();
        }

        private void CyclePrimaryMags(int direction)
        {
            _primaryMagCount = Mathf.Clamp(_primaryMagCount + direction * 1, 1, 8);
            Refresh();
        }

        private void CycleSecondaryMags(int direction)
        {
            _secondaryMagCount = Mathf.Clamp(_secondaryMagCount + direction, 1, 4);
            Refresh();
        }

        private void CycleMagType()
        {
            List<MagazineDefinition> magazines = PrimaryMagazines();
            if (magazines.Count == 0)
            {
                return;
            }

            int index = 0;
            for (int i = 0; i < magazines.Count; i++)
            {
                if (magazines[i].Id == _primaryMagId)
                {
                    index = i;
                    break;
                }
            }

            _primaryMagId = magazines[(index + 1) % magazines.Count].Id;
            Refresh();
        }

        private static int Wrap(int value, int length) => ((value % length) + length) % length;

        /// <summary>Kit screen caption: "FRAG x2", "SMOKE x2", "FLASH x2".</summary>
        private static string GrenadeName(int index)
        {
            switch (Grenades[Wrap(index, Grenades.Length)])
            {
                case "frag_f1": return "FRAG x2";
                case "smoke_rdg": return "SMOKE x2";
                case "flash_zarya": return "FLASH x2";
                default: return Grenades[Wrap(index, Grenades.Length)].ToUpperInvariant() + " x2";
            }
        }

        // --------------------------------------------------------------- refresh

        private void Refresh()
        {
            WeaponDefinition weapon = PrimaryWeapon;
            _primaryLabel.text = weapon != null
                ? weapon.DisplayName + "   (" + weapon.Caliber + ", " +
                  Mathf.RoundToInt(weapon.RoundsPerMinute) + " rpm)"
                : Primaries[_primary];

            _secondaryLabel.text = Secondaries[_secondary].ToUpperInvariant() + "  sidearm";
            if (_grenadeButton != null)
            {
                UiBuilder.SetButtonText(_grenadeButton, "GRENADE: " + GrenadeName(_grenade));
            }

            if (_sightButton != null)
            {
                UiBuilder.SetButtonText(_sightButton,
                    "SIGHT: " + Sights[_primarySight].ToString().ToUpperInvariant());
            }

            LoadoutSelection selection = BuildSelection();
            List<MagazineDefinition> magazines = PrimaryMagazines();
            string magName = "none";
            int capacity = 0;
            foreach (MagazineDefinition magazine in magazines)
            {
                if (magazine.Id == selection.PrimaryMagazines[0].MagazineId)
                {
                    magName = magazine.DisplayName;
                    capacity = magazine.Capacity;
                }
            }

            _planLabel.text = string.Format(
                "{0} x {1}  =  {2} rounds carried", _primaryMagCount, magName,
                _primaryMagCount * capacity);
            _armorLabel.text = Armors[_armor].ToUpperInvariant() + "  plates";
            _summaryLabel.text = string.Format(
                "Primary {0} with {1}\nSecondary {2} with {3} magazines\nGrenade: {4}  |  Plates: {5}\nNo resupply during a round unless the round ends.",
                weapon != null ? weapon.DisplayName : "?",
                selection.PrimaryMagazines.Count > 0 ? selection.PrimaryMagazines[0].Count : 0,
                Secondaries[_secondary].ToUpperInvariant(), _secondaryMagCount,
                Grenades[_grenade], Armors[_armor]);

            LoadoutValidationResult validation =
                LoadoutValidator.Validate(selection, Flow.Database.Definitions);
            _warningLabel.text = validation.IsValid
                ? string.Empty
                : "kit rejected: " + string.Join("; ", validation.Errors.ToArray());
            Flow.Loadout = selection;
        }

        private LoadoutSelection BuildSelection()
        {
            LoadoutSelection selection = Flow.Loadout.Clone();
            selection.PrimaryWeaponId = Primaries[_primary];
            selection.SecondaryWeaponId = Secondaries[_secondary];
            selection.PrimarySight = Sights[_primarySight];
            selection.SecondarySight = Sights[0];
            selection.ArmorKitId = Armors[_armor];
            selection.GrenadeId = Grenades[_grenade];
            selection.GrenadeCount = 2;

            selection.PrimaryMagazines = new List<MagazinePlanEntry>
            {
                new MagazinePlanEntry(ResolvePrimaryMagazine(), _primaryMagCount),
            };
            selection.SecondaryMagazines = new List<MagazinePlanEntry>
            {
                new MagazinePlanEntry(ResolveSecondaryMagazine(), _secondaryMagCount),
            };
            return selection;
        }

        private string ResolvePrimaryMagazine()
        {
            List<MagazineDefinition> magazines = PrimaryMagazines();
            if (magazines.Count == 0)
            {
                return "mag_545_30_ps";
            }

            foreach (MagazineDefinition magazine in magazines)
            {
                if (magazine.Id == _primaryMagId)
                {
                    return magazine.Id;
                }
            }

            _primaryMagId = magazines[0].Id;
            return _primaryMagId;
        }

        private string ResolveSecondaryMagazine()
        {
            WeaponDefinition weapon = Flow.Database.Definitions.Weapons.ContainsKey(
                Secondaries[_secondary])
                ? Flow.Database.Definitions.Weapons[Secondaries[_secondary]]
                : null;
            if (weapon == null)
            {
                return "mag_9x19_17_ap";
            }

            List<MagazineDefinition> magazines =
                Flow.Database.Definitions.MagazinesForWeapon(weapon);
            return magazines.Count > 0 ? magazines[0].Id : "mag_9x19_17_ap";
        }

        /// <summary>
        /// Three ready made kits. They are starting points, not classes: every
        /// weapon, magazine and plate here is available to everybody from the first
        /// match, and nothing about them is locked behind progression.
        /// </summary>
        private void ApplyPreset(int preset)
        {
            switch (preset)
            {
                case 0: // assault
                    _primary = IndexOf(Primaries, "vk74");
                    _secondary = 0;
                    _primarySight = IndexOf(Sights, SightKind.RedDot);
                    _primaryMagCount = 6;
                    _armor = IndexOf(Armors, "standard");
                    _grenade = IndexOf(Grenades, "frag_f1");
                    break;
                case 1: // breacher
                    _primary = IndexOf(Primaries, "k870");
                    _secondary = 0;
                    _primarySight = IndexOf(Sights, SightKind.IronSight);
                    _primaryMagCount = 4;
                    _armor = IndexOf(Armors, "heavy");
                    _grenade = IndexOf(Grenades, "flash_zarya");
                    break;
                default: // marksman
                    _primary = IndexOf(Primaries, "dmr14");
                    _secondary = 0;
                    _primarySight = IndexOf(Sights, SightKind.Scope4x);
                    _primaryMagCount = 5;
                    _armor = IndexOf(Armors, "light");
                    _grenade = IndexOf(Grenades, "smoke_rdg");
                    break;
            }

            _primaryMagId = string.Empty;
            Refresh();
        }

        /// <summary>Swaps between the two shipped control profiles (default and tactical).</summary>
        private void CycleControlProfile()
        {
            Kordon.Game.Input.KordonInput.Rebind(
                Kordon.Game.Input.KordonInput.Bindings.ProfileName == "Tactical"
                    ? Kordon.Core.Settings.KeyBindings.CreateDefault()
                    : Kordon.Core.Settings.KeyBindings.CreateTactical());
            _warningLabel.text = "control profile: " + Kordon.Game.Input.KordonInput.Bindings.ProfileName;
        }

        private void LockInPractice()
        {
            LoadoutValidationResult validation =
                LoadoutValidator.Validate(Flow.Loadout, Flow.Database.Definitions);
            if (!validation.IsValid)
            {
                _warningLabel.text = "kit rejected: " +
                                     string.Join("; ", validation.Errors.ToArray());
                return;
            }

            Flow.StartPractice();
        }
    }
}
