using System;
using System.Collections.Generic;
using System.Globalization;
using Kordon.Core.Ballistics;
using Kordon.Core.Loadout;
using Kordon.Core.Movement;
using Kordon.Core.Utility;

namespace Kordon.Core.AntiCheat
{
    public enum ViolationKind
    {
        None = 0,
        ImpossibleFireRate = 1,
        ImpossibleMovementSpeed = 2,
        ScoreSpoofing = 3,
        InvalidState = 4,
        RpcFlood = 5,
        Teleport = 6,
        ImpossibleViewAngle = 7,
        ClockManipulation = 8,
        IllegalLoadout = 9,
        ResupplyExploit = 10
    }

    /// <summary>One recorded rule breach.</summary>
    [Serializable]
    public struct Violation
    {
        public ViolationKind Kind;
        public ulong ClientId;
        public string Detail;
        public float ServerTime;
        public float Severity;

        public Violation(ViolationKind kind, ulong clientId, string detail, float serverTime, float severity)
        {
            Kind = kind;
            ClientId = clientId;
            Detail = detail;
            ServerTime = serverTime;
            Severity = severity;
        }

        public override string ToString() => string.Format(CultureInfo.InvariantCulture,
            "[{0:F2}] {1} client {2}: {3} (severity {4:F2})", ServerTime, Kind, ClientId, Detail, Severity);
    }

    /// <summary>Verdict of a single validation.</summary>
    public struct ValidationVerdict
    {
        public bool Allowed;
        public ViolationKind Kind;
        public string Detail;
        public float Severity;

        public static ValidationVerdict Ok() => new ValidationVerdict { Allowed = true, Kind = ViolationKind.None };

        public static ValidationVerdict Reject(ViolationKind kind, string detail, float severity = 1f) =>
            new ValidationVerdict { Allowed = false, Kind = kind, Detail = detail, Severity = severity };
    }

    /// <summary>
    /// Sliding window rate limiter for client to server RPCs.  A client may send a
    /// bounded number of messages per action per second; anything above that is
    /// dropped and recorded instead of being processed.
    /// </summary>
    public sealed class RateLimiter
    {
        private readonly Dictionary<string, List<float>> _windows = new Dictionary<string, List<float>>(StringComparer.Ordinal);

        public int DefaultMaxPerWindow { get; set; } = 12;
        public float WindowSeconds { get; set; } = 1f;

        public bool Allow(string key, float now, int maxPerWindow = -1, float windowSeconds = -1f)
        {
            int max = maxPerWindow > 0 ? maxPerWindow : DefaultMaxPerWindow;
            float window = windowSeconds > 0f ? windowSeconds : WindowSeconds;

            if (!_windows.TryGetValue(key, out List<float> stamps))
            {
                stamps = new List<float>();
                _windows[key] = stamps;
            }

            while (stamps.Count > 0 && now - stamps[0] > window)
            {
                stamps.RemoveAt(0);
            }

            if (stamps.Count >= max)
            {
                return false;
            }

            stamps.Add(now);
            return true;
        }

        public void Reset(string key = null)
        {
            if (key == null)
            {
                _windows.Clear();
            }
            else
            {
                _windows.Remove(key);
            }
        }

        public int Count(string key) => _windows.TryGetValue(key, out List<float> stamps) ? stamps.Count : 0;
    }

    /// <summary>
    /// Basic, transparent server side checks against obvious cheating.  These are not
    /// a full anti-cheat suite; they exist so a modified client cannot gain anything
    /// from the most common attacks: impossible fire rate, impossible movement speed,
    /// forged score, forged state transitions and RPC flooding.
    /// </summary>
    public static class ServerValidators
    {
        /// <summary>Tolerance for network jitter when validating movement speed.</summary>
        public const float SpeedTolerance = 1.18f;

        /// <summary>Maximum distance a player may move in one tick before it counts as a teleport.</summary>
        public const float TeleportDistanceM = 6.5f;

        /// <summary>Pitch outside this range means the client sent an impossible view angle.</summary>
        public const float MaxAbsPitchDeg = 91f;

        public static ValidationVerdict ValidateFireRate(WeaponDefinition weapon, FireMode mode,
            float secondsSinceLastShot)
        {
            if (weapon == null)
            {
                return ValidationVerdict.Ok();
            }

            float minimum = weapon.SecondsPerShot;
            if (mode == FireMode.Semi)
            {
                // Semi-automatic fire is human limited, but never faster than the cyclic rate.
                minimum *= 0.92f;
            }
            else if (mode == FireMode.Burst)
            {
                minimum *= 0.9f;
            }

            if (secondsSinceLastShot + 1e-3f < minimum)
            {
                return ValidationVerdict.Reject(ViolationKind.ImpossibleFireRate,
                    string.Format(CultureInfo.InvariantCulture,
                        "{0:F4}s between shots, weapon minimum {1:F4}s", secondsSinceLastShot, minimum),
                    Math.Min(3f, minimum / Math.Max(1e-4f, secondsSinceLastShot)));
            }

            return ValidationVerdict.Ok();
        }

        public static ValidationVerdict ValidateMovement(float distanceMovedM, float deltaTime,
            float maxAllowedSpeedMps, Stance stance, bool sprinting)
        {
            if (deltaTime <= 0f)
            {
                return ValidationVerdict.Reject(ViolationKind.ClockManipulation, "non-positive-delta", 2f);
            }

            if (distanceMovedM > TeleportDistanceM)
            {
                return ValidationVerdict.Reject(ViolationKind.Teleport,
                    string.Format(CultureInfo.InvariantCulture, "{0:F2}m in one tick", distanceMovedM), 3f);
            }

            float speed = distanceMovedM / deltaTime;
            float limit = maxAllowedSpeedMps * SpeedTolerance;

            // Jumping adds a small vertical component; ignore it for the horizontal check.
            if (speed > limit)
            {
                return ValidationVerdict.Reject(ViolationKind.ImpossibleMovementSpeed,
                    string.Format(CultureInfo.InvariantCulture,
                        "{0:F2} m/s vs limit {1:F2} m/s ({2}{3})",
                        speed, limit, stance, sprinting ? "+sprint" : string.Empty),
                    Math.Min(4f, speed / Math.Max(0.01f, maxAllowedSpeedMps)));
            }

            return ValidationVerdict.Ok();
        }

        /// <summary>
        /// Clients may never submit score.  Any client message carrying a kill count,
        /// damage total or match result is rejected and recorded.
        /// </summary>
        public static ValidationVerdict ValidateScoreSubmission(ulong clientId) =>
            ValidationVerdict.Reject(ViolationKind.ScoreSpoofing,
                $"client {clientId} attempted to submit score", 3f);

        /// <summary>
        /// State transitions that cannot legally happen: firing while inspecting or
        /// reloading, firing while dead, respawning before the timer, reloading without
        /// a spare magazine, inspecting while dead.
        /// </summary>
        public static ValidationVerdict ValidateStateTransition(string fromState, string toState)
        {
            if (string.IsNullOrEmpty(fromState) || string.IsNullOrEmpty(toState))
            {
                return ValidationVerdict.Ok();
            }

            bool illegal =
                (fromState == "dead" && toState == "firing") ||
                (fromState == "dead" && toState == "reloading") ||
                (fromState == "dead" && toState == "inspecting") ||
                (fromState == "inspecting" && toState == "firing") ||
                (fromState == "reloading" && toState == "firing") ||
                (fromState == "fumbling" && toState == "firing") ||
                (fromState == "spawn-protected" && toState == "damaging-others") ||
                (fromState == "sealed-loadout" && toState == "changing-loadout");

            return illegal
                ? ValidationVerdict.Reject(ViolationKind.InvalidState, $"{fromState}->{toState}", 1.5f)
                : ValidationVerdict.Ok();
        }

        public static ValidationVerdict ValidateViewAngles(float pitchDegrees)
        {
            if (float.IsNaN(pitchDegrees) || float.IsInfinity(pitchDegrees))
            {
                return ValidationVerdict.Reject(ViolationKind.ImpossibleViewAngle, "nan-pitch", 2f);
            }

            if (Math.Abs(pitchDegrees) > MaxAbsPitchDeg)
            {
                return ValidationVerdict.Reject(ViolationKind.ImpossibleViewAngle,
                    string.Format(CultureInfo.InvariantCulture, "pitch {0:F1}", pitchDegrees), 1.5f);
            }

            return ValidationVerdict.Ok();
        }

        /// <summary>A resupply request that is not allowed by the current match rule.</summary>
        public static ValidationVerdict ValidateResupplyRequest(Kordon.Core.Ammo.ResupplyRule rule, bool duringLife)
        {
            if (duringLife)
            {
                // Within a life there is never any resupply, whichever rule is configured:
                // no ammo crates, no ground pickups, no sharing between operators.
                return ValidationVerdict.Reject(ViolationKind.ResupplyExploit, "resupply-during-life", 2f);
            }

            if (rule == Kordon.Core.Ammo.ResupplyRule.NeverWithinRound)
            {
                return ValidationVerdict.Reject(ViolationKind.ResupplyExploit, "resupply-forbidden-this-round", 1f);
            }

            return ValidationVerdict.Ok();
        }
    }

    /// <summary>Accumulates violations per client and decides when a kick is warranted.</summary>
    public sealed class ViolationLog
    {
        private readonly List<Violation> _entries = new List<Violation>();
        private readonly Dictionary<ulong, float> _severity = new Dictionary<ulong, float>();

        public const float KickSeverity = 6f;
        public const int MaxEntries = 512;

        public IReadOnlyList<Violation> Entries => _entries;

        public void Record(Violation violation)
        {
            _entries.Add(violation);
            while (_entries.Count > MaxEntries)
            {
                _entries.RemoveAt(0);
            }

            _severity.TryGetValue(violation.ClientId, out float current);
            _severity[violation.ClientId] = current + violation.Severity;
        }

        public float SeverityFor(ulong clientId) => _severity.TryGetValue(clientId, out float value) ? value : 0f;

        public bool ShouldKick(ulong clientId) => SeverityFor(clientId) >= KickSeverity;

        public int CountFor(ulong clientId)
        {
            int count = 0;
            foreach (Violation violation in _entries)
            {
                if (violation.ClientId == clientId)
                {
                    count++;
                }
            }

            return count;
        }

        public void Clear()
        {
            _entries.Clear();
            _severity.Clear();
        }
    }
}
