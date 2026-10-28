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
        private int _secondarySight;
        private string _primaryMagId = string.Empty;

        private Text _primaryLabel;
        private Text _secondaryLabel;
        private Text _armorLabel;
        private Text _grenadeLabel;
        private Text _planLabel;
        private Text _summaryLabel;
        private Text _sightLabel;
        private Button _sightButton;
        private Text _secondarySightLabel;
        private Text _warningLabel;

        protected override void Build()
        {
            Image background = UiBuilder.Panel(Root, "Background",
                new Color(0.035f, 0.042f, 0.048f, 1f), new Vector2(0.5f, 0.5f), Vector2.zero,
                new Vector2(4000f, 4000f));
            background.raycastTarget = true;

            Image panel = UiBuilder.Panel(Root, "Panel", UiBuilder.Ink, new Vector2(0.5f, 0.5f),
                Vector2.zero, new Vector2(1180f, 860f));

            Text title = UiBuilder.LabelAt(panel.transform, "Title", "KIT PREPARATION", 36,
                UiBuilder.Paper, new Vector2(0.5f, 1f), new Vector2(0f, -46f),
                new Vector2(1000f, 44f), TextAnchor.MiddleCenter);
            title.fontStyle = FontStyle.Bold;
            UiBuilder.LabelAt(panel.transform, "Sub",
                "The kit locks when the round starts. Magazines are chosen here, once.",
                17, UiBuilder.Dim, new Vector2(0.5f, 1f), new Vector2(0f, -84f),
                new Vector2(1000f, 24f), TextAnchor.MiddleCenter);

            UiBuilder.LabelAt(panel.transform, "PrimaryHeader", "PRIMARY", 20, UiBuilder.Accent,
                new Vector2(0.5f, 0.5f), new Vector2(-470f, 354f), new Vector2(300f, 26f),
                TextAnchor.MiddleLeft);
            _primaryLabel = UiBuilder.LabelAt(panel.transform, "PrimaryValue", "", 24,
                UiBuilder.Paper, new Vector2(0.5f, 0.5f), new Vector2(-120f, 354f),
                new Vector2(560f, 34f), TextAnchor.MiddleLeft);
            UiBuilder.Button(panel.transform, "<", () => CyclePrimary(-1),
                new Vector2(-420f, 314f), new Vector2(54f, 44f), 20);
            UiBuilder.Button(panel.transform, ">", () => CyclePrimary(1),
                new Vector2(-70f, 314f), new Vector2(54f, 44f), 20);
            Button sightButton = UiBuilder.Button(panel.transform, "SIGHT: IRON",
                () => CycleSight(), new Vector2(200f, 314f), new Vector2(260f, 44f), 17);
            _sightButton = sightButton;
            _sightLabel = sightButton.GetComponentInChildren<Text>();

            UiBuilder.LabelAt(panel.transform, "SecondaryHeader", "SECONDARY", 20,
                UiBuilder.Accent, new Vector2(0.5f, 0.5f), new Vector2(-470f, 269f),
                new Vector2(300f, 26f), TextAnchor.MiddleLeft);
            _secondaryLabel = UiBuilder.LabelAt(panel.transform, "SecondaryValue", "", 24,
                UiBuilder.Paper, new Vector2(0.5f, 0.5f), new Vector2(-120f, 269f),
                new Vector2(560f, 34f), TextAnchor.MiddleLeft);
            UiBuilder.Button(panel.transform, "<", () => CycleSecondary(-1),
                new Vector2(-420f, 229f), new Vector2(54f, 44f), 20);
            UiBuilder.Button(panel.transform, ">", () => CycleSecondary(1),
                new Vector2(-70f, 229f), new Vector2(54f, 44f), 20);

            // Magazine plan.
            UiBuilder.LabelAt(panel.transform, "PlanHeader", "MAGAZINE PLAN", 20, UiBuilder.Accent,
                new Vector2(0.5f, 0.5f), new Vector2(-470f, 150f), new Vector2(300f, 26f),
                TextAnchor.MiddleLeft);
            _planLabel = UiBuilder.LabelAt(panel.transform, "Plan", "", 20, UiBuilder.Paper,
                new Vector2(0.5f, 0.5f), new Vector2(-200f, 106f), new Vector2(660f, 30f),
                TextAnchor.MiddleLeft);
            UiBuilder.Button(panel.transform, "FEWER MAGAZINES", () => CyclePrimaryMags(-1),
                new Vector2(-330f, 60f), new Vector2(220f, 46f), 16);
            UiBuilder.Button(panel.transform, "MORE MAGAZINES", () => CyclePrimaryMags(1),
                new Vector2(-90f, 60f), new Vector2(220f, 46f), 16);
            UiBuilder.Button(panel.transform, "MAG TYPE", () => CycleMagType(),
                new Vector2(150f, 60f), new Vector2(180f, 46f), 16);
            UiBuilder.Button(panel.transform, "PISTOL MAGAZINES", () => CycleSecondaryMags(1),
                new Vector2(370f, 60f), new Vector2(240f, 46f), 16);

            // Armour and grenades.
            UiBuilder.LabelAt(panel.transform, "ArmorHeader", "PLATES", 20, UiBuilder.Accent,
                new Vector2(0.5f, 0.5f), new Vector2(-470f, -10f), new Vector2(300f, 26f),
                TextAnchor.MiddleLeft);
            _armorLabel = UiBuilder.LabelAt(panel.transform, "Armor", "", 20, UiBuilder.Paper,
                new Vector2(0.5f, 0.5f), new Vector2(-200f, -46f), new Vector2(660f, 30f),
                TextAnchor.MiddleLeft);
            UiBuilder.Button(panel.transform, "CHANGE PLATES", () => CycleArmor(),
                new Vector2(-330f, -92f), new Vector2(220f, 46f), 16);
            UiBuilder.Button(panel.transform, "GRENADE: FRAG x2", () => CycleGrenade(),
                new Vector2(-90f, -92f), new Vector2(240f, 46f), 16);

            _summaryLabel = UiBuilder.LabelAt(panel.transform, "Summary", "", 18, UiBuilder.Dim,
                new Vector2(0.5f, 0.5f), new Vector2(0f, -168f), new Vector2(1000f, 90f),
                TextAnchor.UpperCenter);

            _warningLabel = UiBuilder.LabelAt(panel.transform, "Warning", "", 18,
                UiBuilder.Danger, new Vector2(0.5f, 0f), new Vector2(0f, 150f),
                new Vector2(1000f, 40f), TextAnchor.MiddleCenter);

            UiBuilder.Button(panel.transform, "LOCK KIT AND DEPLOY TO PRACTICE",
                () => LockInPractice(), new Vector2(-230f, -320f), new Vector2(460f, 56f), 18);
            UiBuilder.Button(panel.transform, "BACK", () => Back(),
                new Vector2(280f, -320f), new Vector2(200f, 56f), 18);

            SelectDefaults();
            Refresh();
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

        // --------------------------------------------------------------- refresh

        private void Refresh()
        {
            WeaponDefinition weapon = PrimaryWeapon;
            _primaryLabel.text = weapon != null
                ? weapon.DisplayName + "   (" + weapon.Caliber + ", " +
                  Mathf.RoundToInt(weapon.RoundsPerMinute) + " rpm)"
                : Primaries[_primary];

            _secondaryLabel.text = Secondaries[_secondary].ToUpperInvariant() + "  sidearm";
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
