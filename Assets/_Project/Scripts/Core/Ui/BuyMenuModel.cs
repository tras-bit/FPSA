using System.Collections.Generic;
using Kordon.Core.Rules;

namespace Kordon.Core.Ui
{
    /// <summary>One line of the buy menu, ready to draw.</summary>
    public struct BuyMenuRow
    {
        public string ItemId;
        public string DisplayName;
        public int Price;
        public bool Affordable;
        public bool Owned;
        public bool BlockedByTeam;
        public BuySlot Slot;
    }

    /// <summary>One shelf of the buy menu.</summary>
    public sealed class BuyMenuSection
    {
        public BuyCategory Category;
        public string Title;
        public readonly List<BuyMenuRow> Rows = new List<BuyMenuRow>();
    }

    /// <summary>
    /// Turns the shop and one player's wallet into exactly what the buy screen draws.
    ///
    /// The decision of *what* is legal stays on the server - this only mirrors it. Doing it
    /// here means the rule "an item you cannot afford is not clickable, and a kit is not
    /// offered to an attacker" is tested once, in Core, instead of being re-implemented in
    /// the screen and drifting from the server's answer.
    /// </summary>
    public static class BuyMenuModel
    {
        private static string TitleFor(BuyCategory category)
        {
            switch (category)
            {
                case BuyCategory.Sidearm: return "SIDEARM";
                case BuyCategory.SubMachineGun: return "SUBMACHINE GUN";
                case BuyCategory.Shotgun: return "SHOTGUN";
                case BuyCategory.Rifle: return "RIFLE";
                case BuyCategory.Marksman: return "MARKSMAN";
                case BuyCategory.Sniper: return "SNIPER";
                case BuyCategory.Armor: return "ARMOUR";
                case BuyCategory.Utility: return "UTILITY";
                default: return "OTHER";
            }
        }

        public static List<BuyMenuSection> Build(BuyCatalog catalog, int money, Team team,
                                                 BuyState owned)
        {
            List<BuyMenuSection> sections = new List<BuyMenuSection>();
            foreach (BuyCategory category in new[]
                     {
                         BuyCategory.Rifle, BuyCategory.Sniper, BuyCategory.Marksman,
                         BuyCategory.SubMachineGun, BuyCategory.Shotgun,
                         BuyCategory.Sidearm, BuyCategory.Armor, BuyCategory.Utility
                     })
            {
                BuyMenuSection section = new BuyMenuSection
                {
                    Category = category,
                    Title = TitleFor(category)
                };

                foreach (BuyItem item in catalog.ForCategory(category))
                {
                    // A shelf item that the player's side may never buy is still shown:
                    // hiding it would leave the defenders wondering why the attackers get
                    // a kit they do not. It is drawn greyed out instead.
                    bool blocked = item.TeamRestriction != Team.None
                                   && item.TeamRestriction != team;
                    section.Rows.Add(new BuyMenuRow
                    {
                        ItemId = item.Id,
                        DisplayName = item.DisplayName,
                        Price = item.Price,
                        Affordable = money >= item.Price,
                        Owned = IsOwned(item, owned),
                        BlockedByTeam = blocked,
                        Slot = item.Slot
                    });
                }

                sections.Add(section);
            }

            return sections;
        }

        public static bool IsOwned(BuyItem item, BuyState owned)
        {
            if (owned == null)
            {
                return false;
            }

            switch (item.Slot)
            {
                case BuySlot.Primary:
                    return owned.PrimaryId == item.WeaponId;
                case BuySlot.Sidearm:
                    return owned.SidearmId == item.WeaponId;
                case BuySlot.Armor:
                    return owned.ArmorId == item.Id;
                case BuySlot.DefuseKit:
                    return owned.HasDefuseKit;
                case BuySlot.Grenade:
                    return owned.Grenades.Contains(item.Id);
                default:
                    return false;
            }
        }

        /// <summary>Can this row be clicked right now? Mirrors the server's refusal reasons.</summary>
        public static bool IsSelectable(BuyMenuRow row) =>
            row.Affordable && !row.Owned && !row.BlockedByTeam;
    }
}
