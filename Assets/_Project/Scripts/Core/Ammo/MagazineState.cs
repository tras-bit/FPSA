using System;

namespace Kordon.Core.Ammo
{
    /// <summary>
    /// The only magazine information the HUD is allowed to show permanently:
    /// a colour state per magazine in the pouch.
    /// White = full, yellow = partially spent, red = empty.
    /// </summary>
    public enum MagazineUiState : byte
    {
        Empty = 0,
        Partial = 1,
        Full = 2
    }

    /// <summary>One slot of the bottom-of-screen magazine strip.</summary>
    [Serializable]
    public struct MagazineHudSlot
    {
        public string MagazineId;
        public MagazineUiState State;

        /// <summary>True for the magazine currently inside the weapon.</summary>
        public bool IsChambered;

        /// <summary>Exact round count - only ever filled while the player is inspecting a magazine.</summary>
        public bool ShowsExactCount;
        public int ExactRounds;
        public int Capacity;

        public MagazineHudSlot(string magazineId, MagazineUiState state, bool isChambered)
        {
            MagazineId = magazineId;
            State = state;
            IsChambered = isChambered;
            ShowsExactCount = false;
            ExactRounds = 0;
            Capacity = 0;
        }

        /// <summary>Colour key used by the HUD (white / yellow / red).</summary>
        public string ColorKey
        {
            get
            {
                switch (State)
                {
                    case MagazineUiState.Full: return "full";
                    case MagazineUiState.Partial: return "partial";
                    default: return "empty";
                }
            }
        }
    }

    /// <summary>What the player is currently doing with the magazine well.</summary>
    public enum AmmoInteraction
    {
        Idle = 0,

        /// <summary>R is held but the hold threshold has not been reached yet: tap vs hold not decided.</summary>
        PendingDecision = 1,

        Reloading = 2,
        Inspecting = 3,
        ReturningFromInspect = 4,
        Chambering = 5,
        FumbleRecovery = 6,
        Throwing = 7
    }

    /// <summary>Which kind of reload is running (drives animation, sound and timing).</summary>
    public enum ReloadType
    {
        None = 0,

        /// <summary>Magazine still had rounds: it goes back into the pouch as "partial".</summary>
        Tactical = 1,

        /// <summary>Magazine was empty: bolt release at the end, no round chambered before.</summary>
        Dry = 2
    }

    /// <summary>Animation/sound hooks emitted by the ammo state machine.</summary>
    public enum AmmoEventKind
    {
        None = 0,
        MagazineOut = 1,
        MagazineIn = 2,
        BoltRelease = 3,
        ChamberRound = 4,
        InspectStart = 5,
        InspectReadable = 6,
        InspectEnd = 7,
        DryFireClick = 8,
        FumbleStart = 9,
        FumbleRecovered = 10,
        ReloadComplete = 11,
        GrenadeThrow = 12,
        LastRound = 13,
        OutOfAmmunition = 14
    }

    /// <summary>One queued presentation event; the runtime drains these for animation and audio.</summary>
    [Serializable]
    public struct AmmoEvent
    {
        public AmmoEventKind Kind;
        public float TimeSeconds;
        public string MagazineId;
        public int Rounds;

        public AmmoEvent(AmmoEventKind kind, float timeSeconds, string magazineId = null, int rounds = 0)
        {
            Kind = kind;
            TimeSeconds = timeSeconds;
            MagazineId = magazineId;
            Rounds = rounds;
        }
    }

    /// <summary>Why a trigger pull was refused. Never a silent failure.</summary>
    public enum FireDenial
    {
        None = 0,
        WeaponEmpty = 1,
        Reloading = 2,
        Inspecting = 3,
        Chambering = 4,
        Fumbling = 5,
        RateOfFire = 6,
        Sprinting = 7,
        OutOfGrenades = 8,
        NotAllowedByMatch = 9
    }
}
