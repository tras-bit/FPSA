using System;
using System.Collections.Generic;
using System.Globalization;
using Kordon.Core.Combat;
using Kordon.Core.Data;
using Kordon.Core.Loadout;
using Kordon.Core.Utility;

namespace Kordon.Core.Rules
{
    public enum JoinOutcome
    {
        Accepted = 0,
        MatchFull = 1,
        WrongPhase = 2,
        DuplicateSession = 3,
        BotsNotAllowed = 4,
        InvalidName = 5,
        InvalidConfig = 6
    }

    public struct JoinResult
    {
        public JoinOutcome Outcome;
        public string Reason;
        public string SessionToken;
        public int SlotIndex;
        public bool IsAccepted => Outcome == JoinOutcome.Accepted;
    }

    public enum ScoreActionOutcome
    {
        Accepted = 0,
        RejectedPhase = 1,
        UnknownKiller = 2,
        UnknownVictim = 3,
        VictimAlreadyDead = 4,
        Suicide = 5,
        SpawnProtection = 6,
        RateLimited = 7,
        ActorDisconnected = 8,
        ImpossibleDistance = 9,
        InvalidInput = 10
    }

    public struct KillRegistrationResult
    {
        public ScoreActionOutcome Outcome;
        public string Reason;
        public int KillerKills;
        public bool MatchEnded;
        public ulong WinnerId;
        public MatchEndReason EndReason;
        public bool IsAccepted => Outcome == ScoreActionOutcome.Accepted;
    }

    public struct DamageRegistrationResult
    {
        public ScoreActionOutcome Outcome;
        public string Reason;
        public float Damage;
        public bool VictimDied;
        public bool IsAccepted => Outcome == ScoreActionOutcome.Accepted;
    }

    public enum RespawnOutcome
    {
        Accepted = 0,
        UnknownPlayer = 1,
        NotDead = 2,
        TooEarly = 3,
        PhaseNotLive = 4,
        Disconnected = 5,
        NoSpawnPoint = 6
    }

    public struct RespawnResult
    {
        public RespawnOutcome Outcome;
        public string Reason;
        public int SpawnPointIndex;
        public float SpawnProtectionUntil;
        public bool IsAccepted => Outcome == RespawnOutcome.Accepted;
    }

    public struct LoadoutLockResult
    {
        public bool Accepted;
        public string Reason;
        public string Hash;
    }

    /// <summary>One scoreboard row sent to clients.</summary>
    [Serializable]
    public struct ScoreboardRow
    {
        public ulong ClientId;
        public string DisplayName;
        public int Kills;
        public int Deaths;
        public int Damage;
        public int PingMs;
        public bool IsConnected;
        public bool IsBot;
        public bool IsAlive;
        public int Rank;
        public bool IsWinner;
        public int KillsToObjective;
    }

    /// <summary>Final match summary shown on the results screen.</summary>
    [Serializable]
    public sealed class MatchResults
    {
        public MatchEndReason Reason;
        public ulong WinnerId;
        public string WinnerName = string.Empty;
        public int WinnerKills;
        public float DurationSeconds;
        public List<ScoreboardRow> Standings = new List<ScoreboardRow>();
        public List<KillFeedEntry> LastEvents = new List<KillFeedEntry>();
        public int TotalEliminations;
    }

    /// <summary>
    /// Authoritative free-for-all match controller.
    ///
    /// Everything that matters - score, damage, eliminations, respawn timing, loadout
    /// locking, disconnection handling and match end - is decided here, on the server.
    /// Clients only send intents (input, "I think I hit somebody") and render the
    /// replicated state.  There are no teams in this mode: 20 independent operators,
    /// first one to personally reach <see cref="MatchRulesConfig.KillTarget"/>
    /// eliminations wins.
    /// </summary>
    [Serializable]
    public sealed class FfaMatchController
    {
        private readonly Dictionary<ulong, PlayerMatchState> _players = new Dictionary<ulong, PlayerMatchState>();
        private readonly List<PlayerMatchState> _roster = new List<PlayerMatchState>();
        private readonly List<KillFeedEntry> _killFeed = new List<KillFeedEntry>();
        private readonly Dictionary<ulong, List<float>> _killTimestamps = new Dictionary<ulong, List<float>>();
        private readonly List<string> _rejections = new List<string>();
        private readonly DeterministicRandom _random;

        public const int KillFeedCapacity = 40;
        public const int RejectionLogCapacity = 64;
        public const float MaxPlausibleKillDistanceM = 900f;

        public MatchRulesConfig Config { get; }
        public MatchPhase Phase { get; private set; } = MatchPhase.Lobby;
        public float Now { get; private set; }
        public float LiveStartedAt { get; private set; } = float.NaN;
        public float EndedAt { get; private set; } = float.NaN;
        public MatchEndReason EndReason { get; private set; } = MatchEndReason.None;
        public ulong WinnerId { get; private set; }
        public int RematchCount { get; private set; }
        public float LoadoutPhaseStartedAt { get; private set; } = float.NaN;
        public float CountdownStartedAt { get; private set; } = float.NaN;

        public IReadOnlyList<PlayerMatchState> Roster => _roster;
        public IReadOnlyList<KillFeedEntry> KillFeed => _killFeed;
        public IReadOnlyList<string> Rejections => _rejections;

        public int ConnectedHumanCount
        {
            get
            {
                int count = 0;
                foreach (PlayerMatchState player in _roster)
                {
                    if (player.IsConnected && !player.IsBot && !player.LeftPermanently)
                    {
                        count++;
                    }
                }

                return count;
            }
        }

        public int FreeSlots => Math.Max(0, Config.MaxPlayers - _roster.Count);

        public FfaMatchController(MatchRulesConfig config, DeterministicRandom random = null)
        {
            Config = config ?? MatchRulesConfig.DefaultFreeForAll();
            _random = random ?? new DeterministicRandom(0xC0FFEEUL);
        }

        public PlayerMatchState GetPlayer(ulong clientId) =>
            _players.TryGetValue(clientId, out PlayerMatchState state) ? state : null;

        // ------------------------------------------------------------ joining

        public JoinResult TryJoin(ulong clientId, string displayName, bool isBot, float now)
        {
            string configError = Config.Validate();
            if (configError != null)
            {
                return new JoinResult { Outcome = JoinOutcome.InvalidConfig, Reason = configError };
            }

            if (string.IsNullOrEmpty(displayName))
            {
                return new JoinResult { Outcome = JoinOutcome.InvalidName, Reason = "empty-name" };
            }

            if (displayName.Length > 24)
            {
                displayName = displayName.Substring(0, 24);
            }

            if (isBot && !Config.AllowBots)
            {
                return new JoinResult
                {
                    Outcome = JoinOutcome.BotsNotAllowed,
                    Reason = "bots-are-only-allowed-in-the-practice-range"
                };
            }

            if (_players.TryGetValue(clientId, out PlayerMatchState existing))
            {
                if (existing.LeftPermanently)
                {
                    _players.Remove(clientId);
                    _roster.Remove(existing);
                }
                else if (existing.IsConnected)
                {
                    return new JoinResult
                    {
                        Outcome = JoinOutcome.DuplicateSession,
                        Reason = "already-in-match",
                        SessionToken = existing.SessionToken
                    };
                }
                else
                {
                    // Reclaiming an existing slot after a drop.
                    existing.IsConnected = true;
                    existing.DisconnectTime = float.NaN;
                    existing.DisplayName = displayName;
                    return new JoinResult
                    {
                        Outcome = JoinOutcome.Accepted,
                        Reason = "reconnected",
                        SessionToken = existing.SessionToken,
                        SlotIndex = _roster.IndexOf(existing)
                    };
                }
            }

            if (_roster.Count >= Config.MaxPlayers)
            {
                return new JoinResult { Outcome = JoinOutcome.MatchFull, Reason = "match-full" };
            }

            if (Phase.IsTerminal())
            {
                return new JoinResult { Outcome = JoinOutcome.WrongPhase, Reason = "match-already-finished" };
            }

            PlayerMatchState player = new PlayerMatchState
            {
                ClientId = clientId,
                DisplayName = displayName,
                IsBot = isBot,
                IsConnected = true,
                JoinTime = now,
                SessionToken = NewSessionToken(clientId),
                IsAlive = false,
                RespawnAvailableAt = float.MaxValue
            };

            _players[clientId] = player;
            _roster.Add(player);
            return new JoinResult
            {
                Outcome = JoinOutcome.Accepted,
                SessionToken = player.SessionToken,
                SlotIndex = _roster.Count - 1
            };
        }

        private string NewSessionToken(ulong clientId)
        {
            // Random enough to stop another client from claiming a dropped player's slot.
            return string.Format(CultureInfo.InvariantCulture, "{0:x16}-{1:x16}", clientId, _random.NextULong());
        }

        public bool TryLeave(ulong clientId, float now)
        {
            PlayerMatchState player = GetPlayer(clientId);
            if (player == null)
            {
                return false;
            }

            player.IsConnected = false;
            player.LeftPermanently = true;
            player.IsAlive = false;
            player.DisconnectTime = now;
            _players.Remove(clientId);
            _roster.Remove(player);
            CheckAbort(now);
            return true;
        }

        public bool MarkDisconnected(ulong clientId, float now)
        {
            PlayerMatchState player = GetPlayer(clientId);
            if (player == null || !player.IsConnected)
            {
                return false;
            }

            player.IsConnected = false;
            player.IsAlive = false;
            player.DisconnectTime = now;
            player.RespawnAvailableAt = float.MaxValue;
            return true;
        }

        /// <summary>Reclaim a slot inside the reconnect grace window, keeping score and kit.</summary>
        public bool TryReconnect(ulong clientId, string sessionToken, float now)
        {
            PlayerMatchState player = GetPlayer(clientId);
            if (player == null || player.LeftPermanently)
            {
                return false;
            }

            if (player.IsConnected)
            {
                return true;
            }

            if (!player.IsInReconnectGrace(now, Config))
            {
                return false;
            }

            if (!string.IsNullOrEmpty(sessionToken) && sessionToken != player.SessionToken)
            {
                Record($"reconnect-token-mismatch:{clientId}");
                return false;
            }

            player.IsConnected = true;
            player.DisconnectTime = float.NaN;
            player.IsAlive = false;
            if (Phase == MatchPhase.Live)
            {
                player.RespawnAvailableAt = now;
            }

            return true;
        }

        public void SetPing(ulong clientId, int pingMs)
        {
            PlayerMatchState player = GetPlayer(clientId);
            if (player != null)
            {
                player.PingMs = MathUtil.Clamp(pingMs, 0, 4000);
            }
        }

        // ------------------------------------------------------------ phases

        public bool BeginLoadoutPhase(float now)
        {
            if (Phase != MatchPhase.Lobby)
            {
                return false;
            }

            Phase = MatchPhase.Loadout;
            LoadoutPhaseStartedAt = now;
            Now = now;
            return true;
        }

        public LoadoutLockResult SubmitLoadout(ulong clientId, LoadoutSelection selection,
            DefinitionLibrary library, float now)
        {
            PlayerMatchState player = GetPlayer(clientId);
            if (player == null)
            {
                return new LoadoutLockResult { Accepted = false, Reason = "unknown-player" };
            }

            if (!Phase.AllowsLoadoutChanges())
            {
                // The kit is blocked once the match has started.
                return new LoadoutLockResult { Accepted = false, Reason = "loadout-locked" };
            }

            LoadoutValidationResult validation = LoadoutValidator.Validate(selection, library);
            if (!validation.IsValid)
            {
                return new LoadoutLockResult
                {
                    Accepted = false,
                    Reason = string.Join(";", validation.Errors.ToArray())
                };
            }

            player.LoadoutHash = selection.ComputeHash();
            player.SelectedLoadoutJson = MiniJsonSafe(selection);
            player.LoadoutLocked = true;
            Now = now;
            return new LoadoutLockResult { Accepted = true, Hash = player.LoadoutHash };
        }

        private static string MiniJsonSafe(LoadoutSelection selection)
        {
            // Kept as a compact string so the server can hand the kit back on reconnect.
            Dictionary<string, object> map = JsonObject.New()
                .With("primary", selection.PrimaryWeaponId)
                .With("secondary", selection.SecondaryWeaponId)
                .With("armor", selection.ArmorKitId)
                .With("grenade", selection.GrenadeId)
                .With("grenadeCount", selection.GrenadeCount)
                .With("hash", selection.ComputeHash());
            return Serialization.MiniJson.Write(map, false);
        }

        public bool BeginCountdown(float now)
        {
            if (Phase != MatchPhase.Loadout)
            {
                return false;
            }

            int ready = 0;
            foreach (PlayerMatchState player in _roster)
            {
                if (player.IsConnected && player.LoadoutLocked)
                {
                    ready++;
                }
            }

            bool loadoutPhaseExpired = !float.IsNaN(LoadoutPhaseStartedAt) &&
                                       now - LoadoutPhaseStartedAt >= Config.LoadoutPhaseSeconds;
            if (ready < Config.MinPlayersToStartLive && !loadoutPhaseExpired)
            {
                return false;
            }

            Phase = MatchPhase.Countdown;
            CountdownStartedAt = now;
            Now = now;
            return true;
        }

        public bool BeginLive(float now)
        {
            if (Phase != MatchPhase.Countdown)
            {
                return false;
            }

            Phase = MatchPhase.Live;
            LiveStartedAt = now;
            Now = now;
            return true;
        }

        public void Abort(float now, string reason)
        {
            if (Phase.IsTerminal())
            {
                return;
            }

            Phase = MatchPhase.Aborted;
            EndReason = MatchEndReason.ServerAborted;
            EndedAt = now;
            Now = now;
            Record($"aborted:{reason}");
        }

        /// <summary>Advance timers. Call once per server tick with the authoritative clock.</summary>
        public void Tick(float now)
        {
            if (now < Now)
            {
                // A server clock that goes backwards is a bug or a tampering attempt.
                Record("clock-went-backwards");
                return;
            }

            Now = now;

            switch (Phase)
            {
                case MatchPhase.Loadout:
                    if (!float.IsNaN(LoadoutPhaseStartedAt) &&
                        now - LoadoutPhaseStartedAt >= Config.LoadoutPhaseSeconds)
                    {
                        BeginCountdown(now);
                    }

                    break;

                case MatchPhase.Countdown:
                    if (!float.IsNaN(CountdownStartedAt) && now - CountdownStartedAt >= Config.CountdownSeconds)
                    {
                        BeginLive(now);
                    }

                    break;

                case MatchPhase.Live:
                    if (Config.TimeLimitSeconds > 0f && !float.IsNaN(LiveStartedAt) &&
                        now - LiveStartedAt >= Config.TimeLimitSeconds)
                    {
                        FinishByTimeLimit(now);
                    }

                    ExpireReconnectGraces(now);
                    CheckAbort(now);
                    break;
            }
        }

        public float CountdownRemaining => Phase == MatchPhase.Countdown && !float.IsNaN(CountdownStartedAt)
            ? Math.Max(0f, Config.CountdownSeconds - (Now - CountdownStartedAt))
            : 0f;

        public float LoadoutTimeRemaining => Phase == MatchPhase.Loadout && !float.IsNaN(LoadoutPhaseStartedAt)
            ? Math.Max(0f, Config.LoadoutPhaseSeconds - (Now - LoadoutPhaseStartedAt))
            : 0f;

        private void ExpireReconnectGraces(float now)
        {
            for (int i = _roster.Count - 1; i >= 0; i--)
            {
                PlayerMatchState player = _roster[i];
                if (!player.IsConnected && !player.LeftPermanently && !player.IsInReconnectGrace(now, Config))
                {
                    player.LeftPermanently = true;
                    _players.Remove(player.ClientId);
                    _roster.RemoveAt(i);
                    Record($"grace-expired:{player.ClientId}");
                }
            }
        }

        private void CheckAbort(float now)
        {
            if (Phase != MatchPhase.Live && Phase != MatchPhase.Countdown && Phase != MatchPhase.Loadout)
            {
                return;
            }

            if (_roster.Count == 0)
            {
                Phase = MatchPhase.Aborted;
                EndReason = MatchEndReason.AllPlayersLeft;
                EndedAt = now;
            }
        }

        // ------------------------------------------------------------ combat bookkeeping

        public void NotifyShotFired(ulong clientId, float now)
        {
            PlayerMatchState player = GetPlayer(clientId);
            if (player == null || !player.IsAlive)
            {
                return;
            }

            player.ShotsFired++;

            // Firing ends spawn protection immediately: you cannot be protected while attacking.
            if (player.SpawnProtectionUntil > now)
            {
                player.SpawnProtectionUntil = now;
            }
        }

        public void NotifyHitLanded(ulong clientId)
        {
            PlayerMatchState player = GetPlayer(clientId);
            if (player != null)
            {
                player.HitsLanded++;
            }
        }

        public DamageRegistrationResult RegisterDamage(ulong attackerId, ulong victimId, float damage,
            HitZone zone, float now)
        {
            if (Phase != MatchPhase.Live)
            {
                return new DamageRegistrationResult
                {
                    Outcome = ScoreActionOutcome.RejectedPhase,
                    Reason = $"phase-{Phase}"
                };
            }

            if (damage < 0f || float.IsNaN(damage) || float.IsInfinity(damage))
            {
                return new DamageRegistrationResult
                {
                    Outcome = ScoreActionOutcome.InvalidInput,
                    Reason = "invalid-damage"
                };
            }

            PlayerMatchState attacker = GetPlayer(attackerId);
            PlayerMatchState victim = GetPlayer(victimId);
            if (attacker == null || !attacker.IsConnected)
            {
                return new DamageRegistrationResult
                {
                    Outcome = ScoreActionOutcome.ActorDisconnected,
                    Reason = "attacker-not-present"
                };
            }

            if (victim == null || !victim.IsConnected)
            {
                return new DamageRegistrationResult
                {
                    Outcome = ScoreActionOutcome.UnknownVictim,
                    Reason = "victim-not-present"
                };
            }

            if (!victim.IsAlive)
            {
                return new DamageRegistrationResult
                {
                    Outcome = ScoreActionOutcome.VictimAlreadyDead,
                    Reason = "victim-already-dead"
                };
            }

            if (victim.IsInSpawnProtection(now))
            {
                return new DamageRegistrationResult
                {
                    Outcome = ScoreActionOutcome.SpawnProtection,
                    Reason = "victim-in-spawn-protection"
                };
            }

            if (attackerId == victimId)
            {
                // Self damage (falling, own grenade) is allowed but never scores.
                victim.DamageTaken += damage;
                return new DamageRegistrationResult
                {
                    Outcome = ScoreActionOutcome.Accepted,
                    Damage = damage,
                    VictimDied = false,
                    Reason = "self-damage"
                };
            }

            damage = Math.Min(damage, DamageModel.BaseHealth * 3f);
            attacker.DamageDealt += damage;
            victim.DamageTaken += damage;

            bool died = damage >= PlayerVitals.MaxHealth || zone.IsVitalZone() && damage >= 40f;
            if (died)
            {
                victim.IsAlive = false;
                victim.LastDeathCause = DeathCause.Gunfire;
                victim.LastFatalZone = zone;
                victim.LastKilledBy = attackerId;
            }

            return new DamageRegistrationResult
            {
                Outcome = ScoreActionOutcome.Accepted,
                Damage = damage,
                VictimDied = died
            };
        }

        /// <summary>
        /// Register an elimination.  Only the server calls this, only the player who
        /// personally landed the finishing blow is credited, and the win is decided here.
        /// </summary>
        public KillRegistrationResult RegisterKill(ulong killerId, ulong victimId, string weaponId,
            HitZone zone, float distanceM, float now)
        {
            if (Phase != MatchPhase.Live)
            {
                return Reject(ScoreActionOutcome.RejectedPhase, $"phase-{Phase}");
            }

            if (distanceM < 0f || float.IsNaN(distanceM) || distanceM > MaxPlausibleKillDistanceM)
            {
                return Reject(ScoreActionOutcome.ImpossibleDistance,
                    string.Format(CultureInfo.InvariantCulture, "distance-{0:F1}", distanceM));
            }

            PlayerMatchState killer = GetPlayer(killerId);
            PlayerMatchState victim = GetPlayer(victimId);

            if (victim == null)
            {
                return Reject(ScoreActionOutcome.UnknownVictim, "victim-not-present");
            }

            if (!victim.IsAlive)
            {
                return Reject(ScoreActionOutcome.VictimAlreadyDead, "victim-already-dead");
            }

            if (killerId == victimId)
            {
                victim.IsAlive = false;
                victim.Deaths++;
                victim.LastDeathCause = DeathCause.Gunfire;
                victim.LastFatalZone = zone;
                victim.LastKilledBy = victim.ClientId;
                victim.RespawnAvailableAt = now + Config.RespawnDelaySeconds;
                AddKillFeedEntry(victim, victim, weaponId, zone, distanceM, now);
                return new KillRegistrationResult
                {
                    Outcome = ScoreActionOutcome.Suicide,
                    Reason = "self-elimination-scores-nothing",
                    KillerKills = victim.Kills
                };
            }

            if (killer == null)
            {
                return Reject(ScoreActionOutcome.UnknownKiller, "killer-not-present");
            }

            if (!killer.IsConnected)
            {
                return Reject(ScoreActionOutcome.ActorDisconnected, "killer-disconnected");
            }

            if (victim.IsInSpawnProtection(now))
            {
                return Reject(ScoreActionOutcome.SpawnProtection, "victim-in-spawn-protection");
            }

            if (!IsKillRateAllowed(killer, now))
            {
                return Reject(ScoreActionOutcome.RateLimited, "kill-rate-limit");
            }

            killer.Kills++;
            if (zone.IsVitalZone())
            {
                killer.Headshots++;
            }

            killer.LongestKillM = Math.Max(killer.LongestKillM, distanceM);
            NoteKillTime(killer.ClientId, now);

            victim.IsAlive = false;
            victim.Deaths++;
            victim.LastDeathCause = DeathCause.Gunfire;
            victim.LastFatalZone = zone;
            victim.LastKilledBy = killerId;
            victim.RespawnAvailableAt = now + Config.RespawnDelaySeconds;

            AddKillFeedEntry(killer, victim, weaponId, zone, distanceM, now);

            KillRegistrationResult result = new KillRegistrationResult
            {
                Outcome = ScoreActionOutcome.Accepted,
                KillerKills = killer.Kills
            };

            if (killer.Kills >= Config.KillTarget)
            {
                killer.IsWinner = true;
                WinnerId = killer.ClientId;
                EndReason = MatchEndReason.KillTargetReached;
                Phase = MatchPhase.Finished;
                EndedAt = now;
                result.MatchEnded = true;
                result.WinnerId = killer.ClientId;
                result.EndReason = EndReason;
            }

            return result;
        }

        /// <summary>Environment / bleed-out death: nobody is credited.</summary>
        public bool RegisterUncreditedDeath(ulong victimId, DeathCause cause, float now)
        {
            PlayerMatchState victim = GetPlayer(victimId);
            if (victim == null || !victim.IsAlive)
            {
                return false;
            }

            victim.IsAlive = false;
            victim.Deaths++;
            victim.LastDeathCause = cause;
            victim.RespawnAvailableAt = now + Config.RespawnDelaySeconds;
            AddKillFeedEntry(null, victim, string.Empty, HitZone.None, 0f, now);
            return true;
        }

        private bool IsKillRateAllowed(PlayerMatchState killer, float now)
        {
            if (!_killTimestamps.TryGetValue(killer.ClientId, out List<float> stamps))
            {
                return true;
            }

            if (stamps.Count > 0 && now - stamps[stamps.Count - 1] < Config.MinSecondsBetweenKills)
            {
                return false;
            }

            int recent = 0;
            for (int i = stamps.Count - 1; i >= 0; i--)
            {
                if (now - stamps[i] <= 60f)
                {
                    recent++;
                }
                else
                {
                    break;
                }
            }

            return recent < (int)Math.Ceiling(Config.MaxKillsPerMinute);
        }

        private void NoteKillTime(ulong clientId, float now)
        {
            if (!_killTimestamps.TryGetValue(clientId, out List<float> stamps))
            {
                stamps = new List<float>();
                _killTimestamps[clientId] = stamps;
            }

            stamps.Add(now);
            while (stamps.Count > 0 && now - stamps[0] > 120f)
            {
                stamps.RemoveAt(0);
            }
        }

        private void AddKillFeedEntry(PlayerMatchState killer, PlayerMatchState victim, string weaponId,
            HitZone zone, float distanceM, float now)
        {
            KillFeedEntry entry = new KillFeedEntry(
                killer?.ClientId ?? victim.ClientId,
                killer?.DisplayName ?? victim.DisplayName,
                victim.ClientId,
                victim.DisplayName,
                weaponId ?? string.Empty,
                zone,
                distanceM,
                now,
                killer?.IsBot ?? false,
                victim.IsBot);

            _killFeed.Insert(0, entry);
            while (_killFeed.Count > KillFeedCapacity)
            {
                _killFeed.RemoveAt(_killFeed.Count - 1);
            }
        }

        private KillRegistrationResult Reject(ScoreActionOutcome outcome, string reason)
        {
            Record($"{outcome}:{reason}");
            return new KillRegistrationResult { Outcome = outcome, Reason = reason };
        }

        private void Record(string message)
        {
            _rejections.Add($"{Now.ToString("F2", CultureInfo.InvariantCulture)} {message}");
            while (_rejections.Count > RejectionLogCapacity)
            {
                _rejections.RemoveAt(0);
            }
        }

        // ------------------------------------------------------------ respawn

        public RespawnResult RequestRespawn(ulong clientId, int spawnPointIndex, float now)
        {
            PlayerMatchState player = GetPlayer(clientId);
            if (player == null)
            {
                return new RespawnResult { Outcome = RespawnOutcome.UnknownPlayer, Reason = "unknown-player" };
            }

            if (!player.IsConnected)
            {
                return new RespawnResult { Outcome = RespawnOutcome.Disconnected, Reason = "disconnected" };
            }

            if (Phase != MatchPhase.Live)
            {
                return new RespawnResult
                {
                    Outcome = RespawnOutcome.PhaseNotLive,
                    Reason = $"phase-{Phase}"
                };
            }

            if (player.IsAlive)
            {
                return new RespawnResult { Outcome = RespawnOutcome.NotDead, Reason = "already-alive" };
            }

            if (now + 1e-3f < player.RespawnAvailableAt)
            {
                return new RespawnResult
                {
                    Outcome = RespawnOutcome.TooEarly,
                    Reason = string.Format(CultureInfo.InvariantCulture,
                        "{0:F2}s-remaining", player.RespawnAvailableAt - now)
                };
            }

            if (spawnPointIndex < 0)
            {
                return new RespawnResult { Outcome = RespawnOutcome.NoSpawnPoint, Reason = "no-spawn-point" };
            }

            player.IsAlive = true;
            player.SpawnPointIndex = spawnPointIndex;
            player.SpawnProtectionUntil = now + Config.SpawnProtectionSeconds;
            player.RespawnAvailableAt = float.MaxValue;
            return new RespawnResult
            {
                Outcome = RespawnOutcome.Accepted,
                SpawnPointIndex = spawnPointIndex,
                SpawnProtectionUntil = player.SpawnProtectionUntil
            };
        }

        public bool IsRespawnReady(ulong clientId, float now)
        {
            PlayerMatchState player = GetPlayer(clientId);
            return player != null && player.IsConnected && !player.IsAlive &&
                   now >= player.RespawnAvailableAt && Phase == MatchPhase.Live;
        }

        public float RespawnCountdownFor(ulong clientId, float now)
        {
            PlayerMatchState player = GetPlayer(clientId);
            if (player == null || player.IsAlive)
            {
                return 0f;
            }

            return Math.Max(0f, player.RespawnAvailableAt - now);
        }

        // ------------------------------------------------------------ results

        private void FinishByTimeLimit(float now)
        {
            Phase = MatchPhase.Finished;
            EndReason = MatchEndReason.TimeLimitReached;
            EndedAt = now;
            List<PlayerMatchState> sorted = SortedRoster();
            if (sorted.Count > 0)
            {
                sorted[0].IsWinner = true;
                WinnerId = sorted[0].ClientId;
            }
        }

        public List<PlayerMatchState> SortedRoster()
        {
            List<PlayerMatchState> sorted = new List<PlayerMatchState>(_roster);
            sorted.Sort((a, b) => a.CompareTo(b));
            return sorted;
        }

        public List<ScoreboardRow> BuildScoreboard()
        {
            List<ScoreboardRow> rows = new List<ScoreboardRow>();
            List<PlayerMatchState> sorted = SortedRoster();
            for (int i = 0; i < sorted.Count; i++)
            {
                PlayerMatchState player = sorted[i];
                rows.Add(new ScoreboardRow
                {
                    ClientId = player.ClientId,
                    DisplayName = player.DisplayName,
                    Kills = player.Kills,
                    Deaths = player.Deaths,
                    Damage = (int)Math.Round(player.DamageDealt),
                    PingMs = player.PingMs,
                    IsConnected = player.IsConnected,
                    IsBot = player.IsBot,
                    IsAlive = player.IsAlive,
                    Rank = i + 1,
                    IsWinner = player.IsWinner,
                    KillsToObjective = Math.Max(0, Config.KillTarget - player.Kills)
                });
            }

            return rows;
        }

        public MatchResults BuildResults()
        {
            MatchResults results = new MatchResults
            {
                Reason = EndReason,
                WinnerId = WinnerId,
                DurationSeconds = float.IsNaN(LiveStartedAt)
                    ? 0f
                    : (float.IsNaN(EndedAt) ? Now : EndedAt) - LiveStartedAt
            };

            results.Standings = BuildScoreboard();
            results.LastEvents = new List<KillFeedEntry>(_killFeed);

            foreach (PlayerMatchState player in _roster)
            {
                results.TotalEliminations += player.Kills;
                if (player.IsWinner)
                {
                    results.WinnerName = player.DisplayName;
                    results.WinnerKills = player.Kills;
                }
            }

            return results;
        }

        /// <summary>
        /// Rematch: the same roster plays again.  Scores reset, kits become editable
        /// again (the loadout screen reopens) and the phase returns to Loadout.
        /// </summary>
        public bool TryRematch(float now)
        {
            if (Phase != MatchPhase.Finished && Phase != MatchPhase.Aborted)
            {
                return false;
            }

            Phase = MatchPhase.Loadout;
            LoadoutPhaseStartedAt = now;
            CountdownStartedAt = float.NaN;
            LiveStartedAt = float.NaN;
            EndedAt = float.NaN;
            EndReason = MatchEndReason.None;
            WinnerId = 0UL;
            RematchCount++;
            Now = now;

            foreach (PlayerMatchState player in _roster)
            {
                player.Kills = 0;
                player.Deaths = 0;
                player.DamageDealt = 0f;
                player.DamageTaken = 0f;
                player.ShotsFired = 0;
                player.HitsLanded = 0;
                player.Headshots = 0;
                player.LongestKillM = 0f;
                player.IsWinner = false;
                player.IsAlive = false;
                player.LoadoutLocked = false;
                player.LoadoutHash = string.Empty;
                player.RespawnAvailableAt = float.MaxValue;
                player.SpawnProtectionUntil = 0f;
                player.LastDeathCause = DeathCause.None;
            }

            _killFeed.Clear();
            _killTimestamps.Clear();
            return true;
        }

        /// <summary>Spawn every connected player at the start of a live match.</summary>
        public void InitialSpawn(Func<PlayerMatchState, int> spawnPointPicker, float now)
        {
            if (Phase != MatchPhase.Live)
            {
                return;
            }

            foreach (PlayerMatchState player in _roster)
            {
                if (!player.IsConnected || player.IsAlive)
                {
                    continue;
                }

                int index = spawnPointPicker?.Invoke(player) ?? 0;
                player.IsAlive = true;
                player.SpawnPointIndex = index;
                player.RespawnAvailableAt = float.MaxValue;
                player.SpawnProtectionUntil = now + Config.SpawnProtectionSeconds;
            }
        }
    }
}
