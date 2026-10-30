using System;
using System.Collections.Generic;

namespace Kordon.Core.Rules
{
    /// <summary>What a player earns and when. All values are the server's, never the client's.</summary>
    [Serializable]
    public sealed class EconomyRules
    {
        /// <summary>Cash for an elimination, before the weapon multiplier.</summary>
        public int KillReward = 300;

        /// <summary>Cash for the side that took the round.</summary>
        public int RoundWinReward = 3250;

        /// <summary>Cash for planting the charge, paid on the plant, win or lose.</summary>
        public int PlantReward = 300;

        /// <summary>Cash for the defender who cut the wire.</summary>
        public int DefuseReward = 300;

        /// <summary>The whole attacking side is paid for a successful detonation.</summary>
        public int TeamDetonationReward = 800;

        /// <summary>
        /// Losing side consolation ladder, indexed by consecutive losses (0 based, clamped
        /// at the last entry).  The ladder is what makes a lost round recoverable: without
        /// it the losing side never buys a rifle again and the match is decided early.
        /// </summary>
        public int[] LossBonusLadder = { 1400, 1900, 2400, 2900, 3400 };

        /// <summary>What the losing side gets for a round lost by the clock running out.</summary>
        public int TimeExpiredBonus = 0;

        public int RewardForLoss(int consecutiveLosses)
        {
            if (consecutiveLosses < 0)
            {
                consecutiveLosses = 0;
            }

            if (LossBonusLadder == null || LossBonusLadder.Length == 0)
            {
                return 0;
            }

            int index = Math.Min(consecutiveLosses, LossBonusLadder.Length - 1);
            return LossBonusLadder[index];
        }

        public string Validate()
        {
            if (KillReward < 0 || RoundWinReward < 0 || PlantReward < 0 || DefuseReward < 0)
            {
                return "economy-negative-reward";
            }

            if (LossBonusLadder == null || LossBonusLadder.Length == 0)
            {
                return "economy-empty-loss-ladder";
            }

            for (int i = 0; i < LossBonusLadder.Length; i++)
            {
                if (LossBonusLadder[i] < 0)
                {
                    return "economy-negative-loss-bonus";
                }

                if (i > 0 && LossBonusLadder[i] < LossBonusLadder[i - 1])
                {
                    // The ladder must not go down as the losing streak grows.
                    return "economy-loss-ladder-must-climb";
                }
            }

            return null;
        }
    }

    /// <summary>
    /// One player's cash. Every mutation goes through the controller, so the client can
    /// never pay itself: the client only ever sends an intent ("I want to buy this").
    /// </summary>
    public sealed class PlayerEconomy
    {
        public ulong ClientId { get; }
        public int Money { get; private set; }
        public int SpentThisRound { get; private set; }
        public int EarnedThisRound { get; private set; }
        public int ConsecutiveLosses { get; private set; }

        public PlayerEconomy(ulong clientId, int startingMoney)
        {
            ClientId = clientId;
            Money = Math.Max(0, startingMoney);
        }

        public bool CanAfford(int price) => price >= 0 && Money >= price;

        public bool TrySpend(int price)
        {
            if (!CanAfford(price))
            {
                return false;
            }

            Money -= price;
            SpentThisRound += price;
            return true;
        }

        public void Award(int amount, int ceiling)
        {
            if (amount <= 0)
            {
                return;
            }

            int room = ceiling - Money;
            if (room <= 0)
            {
                return;
            }

            int paid = Math.Min(amount, room);
            Money += paid;
            EarnedThisRound += paid;
        }

        public void RecordWin()
        {
            ConsecutiveLosses = 0;
        }

        public void RecordLoss()
        {
            ConsecutiveLosses++;
        }

        public void BeginRound()
        {
            SpentThisRound = 0;
            EarnedThisRound = 0;
        }
    }

    /// <summary>Round end money, computed in one place so the HUD and the server agree.</summary>
    public sealed class RoundPayout
    {
        public Team WinningTeam;
        public RoundEndReason Reason;
        public int WinnerReward;
        public int LoserReward;

        public readonly List<KeyValuePair<ulong, int>> Individual = new List<KeyValuePair<ulong, int>>();
    }
}
