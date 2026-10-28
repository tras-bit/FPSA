namespace Kordon.Core.Movement
{
    /// <summary>Body posture. Drives speed, noise, spread, recoil and camera height.</summary>
    public enum Stance
    {
        Stand = 0,
        Crouch = 1,
        Prone = 2
    }

    /// <summary>How the character is moving right now (used for noise and animation).</summary>
    public enum Gait
    {
        Idle = 0,
        Walk = 1,
        Sprint = 2,
        CrouchWalk = 3,
        ProneCrawl = 4
    }

    /// <summary>Direction the body is leaning towards.</summary>
    public enum LeanDirection
    {
        None = 0,
        Left = -1,
        Right = 1
    }
}
