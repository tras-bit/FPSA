using System;
using System.Collections.Generic;

namespace Kordon.Core.Rules
{
    /// <summary>Shop shelves, in the order the buy menu draws them.</summary>
    public enum BuyCategory
    {
        Sidearm = 0,
        SubMachineGun = 1,
        Shotgun = 2,
        Rifle = 3,
        Marksman = 4,
        Sniper = 5,
        Armor = 6,
        Utility = 7
    }

    /// <summary>What kind of slot an item occupies, which is what prevents duplicates.</summary>
    public enum BuySlot
    {
        Primary = 0,
        Sidearm = 1,
        Armor = 2,
        DefuseKit = 3,
        Grenade = 4,

        /// <summary>Consumable that is not carried between rounds (nothing yet, kept explicit).</summary>
        None = 5
    }

    /// <summary>One line of the buy menu.</summary>
    public sealed class BuyItem
    {
        public string Id;
        public string DisplayName;
        public BuyCategory Category;
        public BuySlot Slot;
        public int Price;

        /// <summary>Weapon id from weapons.json, or null for gear and utility.</summary>
        public string WeaponId;

        /// <summary>Only this side may buy it. None means both.</summary>
        public Team TeamRestriction = Team.None;

        public int MaxOwned = 1;

        /// <summary>Order inside its shelf, so the menu never re-orders itself between runs.</summary>
        public int SortOrder;
    }

    /// <summary>Why a purchase failed. The server sends this back verbatim; it is the UI's only truth.</summary>
    public enum BuyOutcome
    {
        Accepted = 0,
        UnknownItem = 1,
        NotEnoughMoney = 2,
        BuyTimeOver = 3,
        OutOfBuyZone = 4,
        WrongTeam = 5,
        SlotAlreadyFull = 6,
        Dead = 7,
        RoundNotRunning = 8,
        TooManyGrenades = 9
    }

    /// <summary>Purchase request result. Money is echoed, never trusted from the client.</summary>
    public struct BuyResult
    {
        public BuyOutcome Outcome;
        public string ItemId;
        public int Price;
        public int Money;
        public string OwnedPrimary;

        public bool IsAccepted => Outcome == BuyOutcome.Accepted;
    }

    /// <summary>What one player currently carries into the round.</summary>
    public sealed class BuyState
    {
        public string PrimaryId;
        public string SidearmId;
        public string ArmorId;
        public bool HasDefuseKit;
        public readonly List<string> Grenades = new List<string>();

        public HeroLoadoutSnapshot Snapshot() => new HeroLoadoutSnapshot
        {
            PrimaryId = PrimaryId,
            SidearmId = SidearmId,
            ArmorId = ArmorId,
            HasDefuseKit = HasDefuseKit,
            Grenades = Grenades.ToArray()
        };
    }

    /// <summary>Serialisable copy handed to the HUD and the viewmodel.</summary>
    public struct HeroLoadoutSnapshot
    {
        public string PrimaryId;
        public string SidearmId;
        public string ArmorId;
        public bool HasDefuseKit;
        public string[] Grenades;
    }

    /// <summary>
    /// The shop: a fixed catalogue, a fixed price list, and one place that decides whether
    /// a purchase is legal. Prices are data, not balance knobs scattered through the code,
    /// and the catalogue is validated so a typo cannot ship a free rifle.
    /// </summary>
    public sealed class BuyCatalog
    {
        public readonly List<BuyItem> Items = new List<BuyItem>();

        public static BuyCatalog Default()
        {
            BuyCatalog catalog = new BuyCatalog();
            catalog.Add("p9", "P-9 Igla", BuyCategory.Sidearm, BuySlot.Sidearm, 0, "p9", 1);
            catalog.Add("p9_alt", "P-9 with suppressor", BuyCategory.Sidearm, BuySlot.Sidearm, 500,
                "p9", 2);
            catalog.Add("vs9", "VS-9 Sherif", BuyCategory.SubMachineGun, BuySlot.Primary, 1250,
                "vs9", 3);
            catalog.Add("k870", "K-870 Zaslona", BuyCategory.Shotgun, BuySlot.Primary, 1100,
                "k870", 4);
            catalog.Add("vk74", "VK-74 Klyuch", BuyCategory.Rifle, BuySlot.Primary, 2700,
                "vk74", 5);
            catalog.Add("tr30", "TR-30 Volkhov", BuyCategory.Rifle, BuySlot.Primary, 3100,
                "tr30", 6);
            catalog.Add("dmr14", "DMR-14 Sokol", BuyCategory.Marksman, BuySlot.Primary, 3600,
                "dmr14", 7);
            catalog.Add("sr338", "SR-338 Taiga", BuyCategory.Sniper, BuySlot.Primary, 4750,
                "sr338", 8);
            catalog.Add("vest", "Armour vest", BuyCategory.Armor, BuySlot.Armor, 650,
                null, 9);
            catalog.Add("vest_helmet", "Vest and helmet", BuyCategory.Armor, BuySlot.Armor, 1000,
                null, 10);
            catalog.Add("defuse_kit", "Defuse kit", BuyCategory.Utility, BuySlot.DefuseKit, 400,
                null, 11, Team.Defenders);
            catalog.Add("frag", "Frag grenade", BuyCategory.Utility, BuySlot.Grenade, 300,
                null, 12);
            catalog.Add("smoke", "Smoke grenade", BuyCategory.Utility, BuySlot.Grenade, 300,
                null, 13);
            catalog.Add("flash", "Flashbang", BuyCategory.Utility, BuySlot.Grenade, 200,
                null, 14);
            return catalog;
        }

        private void Add(string id, string name, BuyCategory category, BuySlot slot, int price,
                         string weaponId, int sortOrder, Team restriction = Team.None)
        {
            Items.Add(new BuyItem
            {
                Id = id,
                DisplayName = name,
                Category = category,
                Slot = slot,
                Price = price,
                WeaponId = weaponId,
                TeamRestriction = restriction,
                SortOrder = sortOrder
            });
        }

        public BuyItem Find(string id)
        {
            for (int i = 0; i < Items.Count; i++)
            {
                if (string.Equals(Items[i].Id, id, StringComparison.Ordinal))
                {
                    return Items[i];
                }
            }

            return null;
        }

        public List<BuyItem> ForCategory(BuyCategory category)
        {
            List<BuyItem> shelf = new List<BuyItem>();
            for (int i = 0; i < Items.Count; i++)
            {
                if (Items[i].Category == category)
                {
                    shelf.Add(Items[i]);
                }
            }

            shelf.Sort((a, b) => a.SortOrder.CompareTo(b.SortOrder));
            return shelf;
        }

        /// <summary>
        /// Validates the catalogue against the weapon list the build actually ships, so a
        /// buy item can never point at a weapon that does not exist.
        /// </summary>
        public string Validate(IEnumerable<string> knownWeaponIds)
        {
            HashSet<string> known = new HashSet<string>(knownWeaponIds, StringComparer.Ordinal);
            HashSet<string> ids = new HashSet<string>(StringComparer.Ordinal);

            for (int i = 0; i < Items.Count; i++)
            {
                BuyItem item = Items[i];
                if (string.IsNullOrEmpty(item.Id) || !ids.Add(item.Id))
                {
                    return "duplicate-or-empty-item-id";
                }

                if (item.Price < 0)
                {
                    return "negative-price:" + item.Id;
                }

                if (string.IsNullOrEmpty(item.DisplayName))
                {
                    return "missing-display-name:" + item.Id;
                }

                if (!string.IsNullOrEmpty(item.WeaponId) && !known.Contains(item.WeaponId))
                {
                    return "unknown-weapon:" + item.Id;
                }

                if (item.Slot == BuySlot.Primary && string.IsNullOrEmpty(item.WeaponId))
                {
                    return "primary-without-weapon:" + item.Id;
                }
            }

            return null;
        }
    }
}
