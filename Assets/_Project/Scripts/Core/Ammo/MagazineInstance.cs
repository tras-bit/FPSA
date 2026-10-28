using System;
using Kordon.Core.Loadout;

namespace Kordon.Core.Ammo
{
    /// <summary>
    /// One physical magazine with a live round count.  Instances are created from the
    /// locked pre-round plan and never change type or count afterwards.
    /// </summary>
    [Serializable]
    public sealed class MagazineInstance
    {
        public string DefinitionId;
        public int Capacity;
        public int Rounds;

        public MagazineInstance()
        {
        }

        public MagazineInstance(MagazineDefinition definition, int rounds)
        {
            DefinitionId = definition?.Id ?? string.Empty;
            Capacity = definition?.Capacity ?? 0;
            Rounds = Math.Max(0, Math.Min(rounds, Capacity));
        }

        public MagazineInstance(string definitionId, int capacity, int rounds)
        {
            DefinitionId = definitionId ?? string.Empty;
            Capacity = capacity;
            Rounds = Math.Max(0, Math.Min(rounds, capacity));
        }

        public bool IsEmpty => Rounds <= 0;
        public bool IsFull => Rounds >= Capacity;

        public MagazineUiState UiState
        {
            get
            {
                if (Rounds <= 0)
                {
                    return MagazineUiState.Empty;
                }

                return Rounds >= Capacity ? MagazineUiState.Full : MagazineUiState.Partial;
            }
        }

        /// <summary>Take one round out. Returns false when the magazine is empty.</summary>
        public bool ConsumeOne()
        {
            if (Rounds <= 0)
            {
                return false;
            }

            Rounds--;
            return true;
        }

        public void Refill() => Rounds = Capacity;

        public MagazineInstance Clone() => new MagazineInstance(DefinitionId, Capacity, Rounds);

        public override string ToString() => $"{DefinitionId} {Rounds}/{Capacity} ({UiState})";
    }
}
