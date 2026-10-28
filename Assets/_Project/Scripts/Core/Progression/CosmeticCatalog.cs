using System;
using System.Collections.Generic;
using Kordon.Core.Serialization;

namespace Kordon.Core.Progression
{
    public enum CosmeticCategory
    {
        None = 0,
        WeaponFinish = 1,
        SightReticleColor = 2,
        OperatorPatch = 3,
        CallsignPlate = 4,
        HelmetCamo = 5,
        KillFeedFlourish = 6
    }

    /// <summary>
    /// Everything that can be unlocked is cosmetic.  A CosmeticItem carries no damage,
    /// recoil, capacity or mobility values at all - the type system makes a paid or
    /// grinded combat advantage impossible to express.
    /// </summary>
    [Serializable]
    public sealed class CosmeticItem
    {
        public string Id = "finish_worn_black";
        public string DisplayName = "Worn Black";
        public CosmeticCategory Category = CosmeticCategory.WeaponFinish;
        public string Description = string.Empty;

        /// <summary>What the player does to unlock it: play matches, complete the tutorial, finish the co-op op.</summary>
        public string UnlockRequirement = "complete-tutorial";

        /// <summary>True when every player has it from the start.</summary>
        public bool AvailableByDefault = false;

        /// <summary>Never true, and there is no field that could express one.</summary>
        public const bool AffectsGameplay = false;

        public CosmeticItem Clone() => (CosmeticItem)MemberwiseClone();
    }

    /// <summary>Per player unlock state. Purely presentational.</summary>
    [Serializable]
    public sealed class ProgressionProfile
    {
        public string PlayerName = string.Empty;
        public int MatchesPlayed;
        public int MatchesWon;
        public int CoopOperationsCompleted;
        public bool TutorialCompleted;
        public List<string> UnlockedCosmeticIds = new List<string>();
        public Dictionary<CosmeticCategory, string> Equipped = new Dictionary<CosmeticCategory, string>();

        public bool HasUnlocked(string cosmeticId) =>
            UnlockedCosmeticIds.Contains(cosmeticId);

        public bool Unlock(string cosmeticId)
        {
            if (HasUnlocked(cosmeticId))
            {
                return false;
            }

            UnlockedCosmeticIds.Add(cosmeticId);
            return true;
        }

        public void Equip(CosmeticCategory category, string cosmeticId) => Equipped[category] = cosmeticId;

        public string EquippedId(CosmeticCategory category) =>
            Equipped.TryGetValue(category, out string id) ? id : string.Empty;
    }

    /// <summary>The shipped cosmetic catalogue.</summary>
    public sealed class CosmeticCatalog
    {
        private readonly List<CosmeticItem> _items = new List<CosmeticItem>();

        public IReadOnlyList<CosmeticItem> Items => _items;

        public void Add(CosmeticItem item) => _items.Add(item);

        public CosmeticItem Get(string id)
        {
            foreach (CosmeticItem item in _items)
            {
                if (item.Id == id)
                {
                    return item;
                }
            }

            return null;
        }

        public List<CosmeticItem> UnlockedFor(ProgressionProfile profile)
        {
            List<CosmeticItem> result = new List<CosmeticItem>();
            foreach (CosmeticItem item in _items)
            {
                if (item.AvailableByDefault || profile.HasUnlocked(item.Id))
                {
                    result.Add(item);
                }
            }

            return result;
        }

        public static CosmeticCatalog FromJson(string json)
        {
            CosmeticCatalog catalog = new CosmeticCatalog();
            if (string.IsNullOrEmpty(json))
            {
                return catalog;
            }

            Json root = Json.Parse(json);
            foreach (Json item in root["cosmetics"].Items())
            {
                catalog.Add(new CosmeticItem
                {
                    Id = item["id"].AsString(),
                    DisplayName = item["displayName"].AsString(),
                    Category = item["category"].AsEnum(CosmeticCategory.WeaponFinish),
                    Description = item["description"].AsString(string.Empty),
                    UnlockRequirement = item["unlockRequirement"].AsString("default"),
                    AvailableByDefault = item["availableByDefault"].AsBool(false)
                });
            }

            return catalog;
        }

        /// <summary>
        /// Guard used by CI: no cosmetic may ever grant a combat advantage.  Because the
        /// data type has no numeric gameplay fields this is a structural check - it fails
        /// only if somebody adds a stats block to the JSON.
        /// </summary>
        public List<string> ValidateNoGameplayEffects(string rawJson)
        {
            List<string> problems = new List<string>();
            if (string.IsNullOrEmpty(rawJson))
            {
                return problems;
            }

            Json root = Json.Parse(rawJson);
            foreach (Json item in root["cosmetics"].Items())
            {
                string[] forbidden = { "damage", "recoil", "spread", "capacity", "penetration",
                    "mobility", "rateOfFire", "armorClass", "reloadTime" };
                foreach (string key in forbidden)
                {
                    if (item.Has(key))
                    {
                        problems.Add($"{item["id"].AsString()}: cosmetic carries gameplay field '{key}'");
                    }
                }
            }

            return problems;
        }
    }
}
