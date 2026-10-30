using System;
using System.Collections.Generic;

namespace Kordon.Core.Rules
{
    /// <summary>One player of a demolition match, as the server holds them.</summary>
    public sealed class BombPlayer
    {
        public ulong ClientId;
        public string DisplayName;

        /// <summary>
        /// Which squad the player belongs to: 0 or 1.  The squad is permanent for the
        /// match, the side is not - the sides swap at half time, and the score has to
        /// follow the squad, not the side, or a team would lose its rounds by switching.
        /// </summary>
        public int Squad;

        public Team Team;
        public bool Alive;
        public bool IsBot;
        public int Kills;
        public int Deaths;
        public int Plants;
        public int Defuses;
        public PlayerEconomy Economy;
        public BuyState Buy = new BuyState();

        public bool HasDefuseKit => Buy.HasDefuseKit;
    }

    public enum BombJoinOutcome
    {
        Accepted = 0,
        MatchFull = 1,
        DuplicateSession = 2,
        InvalidName = 3,
        BotsNotAllowed = 4,
        MatchAlreadyStarted = 5,
        WrongPhase = 6
    }

    public struct BombJoinResult
    {
        public BombJoinOutcome Outcome;
        public string Reason;
        public Team Team;
        public bool IsAccepted => Outcome == BombJoinOutcome.Accepted;
    }

    public enum BombActionOutcome
    {
        Accepted = 0,
        UnknownPlayer = 1,
        Dead = 2,
        WrongPhase = 3,
        WrongTeam = 4,
        OutOfSite = 5,
        AlreadyPlanted = 6,
        AlreadyCarrying = 7,
        NotCarrying = 8,
        BombNotPlanted = 9,
        NotOnASite = 10
    }

    public struct BombActionResult
    {
        public BombActionOutcome Outcome;
        public string Reason;
        public bool IsAccepted => Outcome == BombActionOutcome.Accepted;
    }

    /// <summary>One bomb site, as loaded from the map layout JSON.</summary>
    public struct BombSiteVolume
    {
        public string Id;
        public string Name;
        public float CentreX;
        public float CentreY;
        public float Radius;
    }

    /// <summary>One team's buy zone, as loaded from the map layout JSON.</summary>
    public struct BuyZoneVolume
    {
        public string Id;
        public Team Team;
        public float CentreX;
        public float CentreY;
        public float Radius;
    }

    /// <summary>Read-only view the HUD and the network layer replicate.</summary>
    public struct BombRoundSnapshot
    {
        public int RoundNumber;
        public RoundPhase Phase;
        public float SecondsLeft;
        public int AttackerScore;
        public int DefenderScore;
        public bool BombPlanted;
        public string BombSite;
        public float BombSecondsLeft;
        public float PlantProgress;
        public float DefuseProgress;
        public ulong DefusingClient;
        public ulong CarrierClient;
        public RoundEndReason LastEndReason;
    }

    /// <summary>
    /// Server authoritative demolition round.
    ///
    /// Everything a client can trigger is an *intent*: "I am holding the plant key on site
    /// B", "I am buying a rifle", "I shot player 7". The controller decides, the clocks and
    /// the money live here, and nothing is mutated from the client. It is a plain
    /// engine-free class so the whole round life cycle - freeze, plant, defuse, detonation,
    /// elimination, time out, half time, match point - runs in the test suite on CI without
    /// a Unity editor, which is the only way any of this can be verified in this repository.
    /// </summary>
    public sealed class BombMatchController
    {
        private readonly BombRules _rules;
        private readonly EconomyRules _economy;
        private readonly BuyCatalog _catalog;
        private readonly List<BombPlayer> _players = new List<BombPlayer>();
        private readonly Dictionary<ulong, BombPlayer> _byId = new Dictionary<ulong, BombPlayer>();

        /// <summary>
        /// Progress tolerance.  A plant held for exactly PlantSeconds computes as
        /// 3.1999998 / 3.2 in single precision, which is just under one; without this the
        /// charge would refuse to arm at the exact moment it should.
        /// </summary>
        private const float ProgressEpsilon = 0.001f;

        private readonly int[] _squadScore = new int[2];
        private float _roundStartedAt;
        private float _liveStartedAt;
        private float _phaseEndsAt;
        private float _explosionAt;
        private float _roundOverAt;
        private string _armedSite;
        private ulong _carrier;
        private ulong _plantingClient;
        private float _plantStartedAt;
        private float _plantProgress;
        private ulong _defusingClient;
        private float _defuseStartedAt;
        private float _defuseSecondsNeeded;

        public BombMatchController(BombRules rules, EconomyRules economy, BuyCatalog catalog)
        {
            _rules = rules ?? throw new ArgumentNullException(nameof(rules));
            _economy = economy ?? throw new ArgumentNullException(nameof(economy));
            _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));

            string problem = rules.Validate();
            if (problem != null)
            {
                throw new ArgumentException("invalid demolition rules: " + problem, nameof(rules));
            }

            string economyProblem = economy.Validate();
            if (economyProblem != null)
            {
                throw new ArgumentException("invalid economy: " + economyProblem, nameof(economy));
            }
        }

        public BombRules Rules => _rules;
        public EconomyRules Economy => _economy;
        public BuyCatalog Catalog => _catalog;

        public MatchPhase Phase { get; private set; } = MatchPhase.Lobby;
        public RoundPhase Round { get; private set; } = RoundPhase.None;
        public int RoundNumber { get; private set; }

        /// <summary>Rounds won by the squad currently attacking.</summary>
        public int AttackerScore => _squadScore[SquadOn(Team.Attackers)];

        /// <summary>Rounds won by the squad currently defending.</summary>
        public int DefenderScore => _squadScore[SquadOn(Team.Defenders)];
        public bool BombPlanted => Round == RoundPhase.Planted;
        public string ArmedSite => _armedSite;
        public ulong CarrierClient => _carrier;
        public RoundEndReason LastEndReason { get; private set; } = RoundEndReason.None;
        public Team MatchWinner { get; private set; } = Team.None;
        public bool SidesSwapped { get; private set; }
        public IReadOnlyList<BombPlayer> Players => _players;

        /// <summary>Which squad is on the given side right now.</summary>
        public int SquadOn(Team side)
        {
            for (int i = 0; i < _players.Count; i++)
            {
                if (_players[i].Team == side)
                {
                    return _players[i].Squad;
                }
            }

            // Empty side: derive from the swap flag so the score still has an identity.
            if (side == Team.Attackers)
            {
                return SidesSwapped ? 1 : 0;
            }

            return SidesSwapped ? 0 : 1;
        }

        public int AliveOf(Team team)
        {
            int alive = 0;
            for (int i = 0; i < _players.Count; i++)
            {
                if (_players[i].Team == team && _players[i].Alive)
                {
                    alive++;
                }
            }

            return alive;
        }

        // ------------------------------------------------------------------
        // Roster
        // ------------------------------------------------------------------
        public BombJoinResult AddPlayer(ulong clientId, string name, Team preferred,
                                        bool isBot, float now)
        {
            if (Phase != MatchPhase.Lobby && Phase != MatchPhase.Loadout)
            {
                return Reject(BombJoinOutcome.MatchAlreadyStarted, "the match has started");
            }

            if (string.IsNullOrWhiteSpace(name) || name.Length > 20)
            {
                return Reject(BombJoinOutcome.InvalidName, "a name is required");
            }

            if (isBot && !_rules.AllowBots)
            {
                return Reject(BombJoinOutcome.BotsNotAllowed, "bots are practice only");
            }

            for (int i = 0; i < _players.Count; i++)
            {
                if (_players[i].ClientId == clientId)
                {
                    return Reject(BombJoinOutcome.DuplicateSession, "already joined");
                }
            }

            if (_players.Count >= _rules.MaxPlayers)
            {
                return Reject(BombJoinOutcome.MatchFull, "the match is full");
            }

            Team team = ChooseTeam(preferred);
            if (team == Team.None)
            {
                return Reject(BombJoinOutcome.MatchFull, "both sides are full");
            }

            // A new player joins the squad already on that side, so a squad keeps one
            // identity across the half-time swap.
            int squadOfTeam = SquadOn(team);

            BombPlayer player = new BombPlayer
            {
                ClientId = clientId,
                DisplayName = name.Trim(),
                Squad = squadOfTeam,
                Team = team,
                Alive = true,
                IsBot = isBot,
                Economy = new PlayerEconomy(clientId, _rules.StartingMoney)
            };

            // The issue weapon: every operator starts a round with a sidearm, so the
            // loadout is never "nothing" even when the player buys nothing at all.
            player.Buy.SidearmId = "p9";

            _players.Add(player);
            _byId[clientId] = player;

            return new BombJoinResult
            {
                Outcome = BombJoinOutcome.Accepted,
                Team = team,
                Reason = null
            };
        }

        private Team ChooseTeam(Team preferred)
        {
            int attackers = 0;
            int defenders = 0;
            for (int i = 0; i < _players.Count; i++)
            {
                if (_players[i].Team == Team.Attackers)
                {
                    attackers++;
                }
                else if (_players[i].Team == Team.Defenders)
                {
                    defenders++;
                }
            }

            if (preferred == Team.Attackers && attackers < _rules.TeamSize)
            {
                return Team.Attackers;
            }

            if (preferred == Team.Defenders && defenders < _rules.TeamSize)
            {
                return Team.Defenders;
            }

            if (attackers < defenders && attackers < _rules.TeamSize)
            {
                return Team.Attackers;
            }

            if (defenders < _rules.TeamSize)
            {
                return Team.Defenders;
            }

            return attackers < _rules.TeamSize ? Team.Attackers : Team.None;
        }

        private static BombJoinResult Reject(BombJoinOutcome outcome, string reason) =>
            new BombJoinResult { Outcome = outcome, Reason = reason, Team = Team.None };

        public bool RemovePlayer(ulong clientId)
        {
            if (!_byId.TryGetValue(clientId, out BombPlayer player))
            {
                return false;
            }

            _byId.Remove(clientId);
            _players.Remove(player);
            return true;
        }

        public BombPlayer GetPlayer(ulong clientId) =>
            _byId.TryGetValue(clientId, out BombPlayer player) ? player : null;

        // ------------------------------------------------------------------
        // Match and round life cycle
        // ------------------------------------------------------------------
        public bool StartMatch(float now)
        {
            if (Phase != MatchPhase.Lobby || _players.Count == 0)
            {
                return false;
            }

            Phase = MatchPhase.Live;
            RoundNumber = 0;
            StartRound(now);
            return true;
        }

        private void StartRound(float now)
        {
            RoundNumber++;
            Round = RoundPhase.Freeze;
            _roundStartedAt = now;
            _phaseEndsAt = now + _rules.FreezeSeconds;
            _armedSite = null;
            _carrier = 0;
            _plantProgress = 0f;
            _plantingClient = 0;
            _defusingClient = 0;
            _defuseStartedAt = 0f;
            _explosionAt = 0f;
            LastEndReason = RoundEndReason.None;

            // Sides swap once, after the first half. The swap happens between rounds so
            // no player is ever moved while their buy is open.
            if (!SidesSwapped && RoundNumber == _rules.RoundsPerHalf + 1)
            {
                SwapSides();
            }

            for (int i = 0; i < _players.Count; i++)
            {
                BombPlayer player = _players[i];
                player.Alive = true;
                player.Buy.HasDefuseKit = player.Buy.HasDefuseKit && player.Team == Team.Defenders;
                player.Economy.BeginRound();
            }

            _carrier = PickCarrier();
        }

        private void SwapSides()
        {
            for (int i = 0; i < _players.Count; i++)
            {
                BombPlayer player = _players[i];
                player.Team = player.Team == Team.Attackers ? Team.Defenders : Team.Attackers;
                // A kit belongs to a defender; the attacking side cannot keep one.
                if (player.Team != Team.Defenders)
                {
                    player.Buy.HasDefuseKit = false;
                }
            }

            SidesSwapped = true;
        }

        private ulong PickCarrier()
        {
            ulong picked = 0;
            for (int i = 0; i < _players.Count; i++)
            {
                BombPlayer player = _players[i];
                if (player.Team != Team.Attackers)
                {
                    continue;
                }

                // Deterministic: the lowest id carries first. Randomising the carrier is a
                // gameplay decision that belongs to the server, and a deterministic pick
                // keeps the tests honest.
                if (picked == 0 || player.ClientId < picked)
                {
                    picked = player.ClientId;
                }
            }

            return picked;
        }

        /// <summary>Advance every clock. Called by the server at a fixed tick rate.</summary>
        public void Tick(float now)
        {
            if (Phase != MatchPhase.Live)
            {
                return;
            }

            switch (Round)
            {
                case RoundPhase.Freeze:
                    if (now >= _phaseEndsAt)
                    {
                        Round = RoundPhase.Live;
                        // The round clock starts when the freeze ends, not when the round
                        // does: otherwise the buy time would be taken out of the round.
                        _liveStartedAt = now;
                        _phaseEndsAt = now + _rules.RoundSeconds;
                    }

                    break;

                case RoundPhase.Live:
                    if (now >= _phaseEndsAt)
                    {
                        EndRound(Team.Defenders, RoundEndReason.TimeExpired, now);
                    }

                    break;

                case RoundPhase.Planted:
                    if (now >= _explosionAt)
                    {
                        EndRound(Team.Attackers, RoundEndReason.BombExploded, now);
                    }

                    break;

                case RoundPhase.RoundOver:
                    if (now >= _roundOverAt)
                    {
                        if (MatchDecided())
                        {
                            Round = RoundPhase.MatchOver;
                            Phase = MatchPhase.Finished;
                            MatchWinner = SideOfWinningSquad();
                        }
                        else
                        {
                            StartRound(now);
                        }
                    }

                    break;
            }
        }

        public bool MatchDecided() =>
            _squadScore[0] >= _rules.RoundsToWin || _squadScore[1] >= _rules.RoundsToWin;

        /// <summary>The side the winning squad is playing right now.</summary>
        private Team SideOfWinningSquad()
        {
            int winningSquad = _squadScore[0] >= _rules.RoundsToWin ? 0 : 1;
            for (int i = 0; i < _players.Count; i++)
            {
                if (_players[i].Squad == winningSquad)
                {
                    return _players[i].Team;
                }
            }

            return Team.None;
        }

        // ------------------------------------------------------------------
        // The charge
        // ------------------------------------------------------------------
        public BombActionResult BeginPlant(ulong clientId, string siteId, bool inSite, float now)
        {
            if (Phase != MatchPhase.Live || Round != RoundPhase.Live)
            {
                return Fail(BombActionOutcome.WrongPhase, "the charge can only be planted in a live round");
            }

            BombPlayer player = GetPlayer(clientId);
            if (player == null)
            {
                return Fail(BombActionOutcome.UnknownPlayer, "unknown player");
            }

            if (!player.Alive)
            {
                return Fail(BombActionOutcome.Dead, "a dead operator cannot plant");
            }

            if (player.Team != Team.Attackers)
            {
                return Fail(BombActionOutcome.WrongTeam, "only the attacking side carries the charge");
            }

            if (string.IsNullOrEmpty(siteId))
            {
                return Fail(BombActionOutcome.NotOnASite, "no site");
            }

            if (!inSite)
            {
                return Fail(BombActionOutcome.OutOfSite, "outside the site");
            }

            _plantingClient = clientId;
            _plantStartedAt = now;
            _plantProgress = 0f;
            _armedSite = null;
            return new BombActionResult { Outcome = BombActionOutcome.Accepted };
        }

        /// <summary>Plant progress is a function of held time, not of client reports.</summary>
        public float PlantProgressAt(float now)
        {
            if (_plantingClient == 0 || Round != RoundPhase.Live)
            {
                return 0f;
            }

            float held = now - _plantStartedAt;
            return Clamp01(held / _rules.PlantSeconds);
        }

        public void CancelPlant(ulong clientId)
        {
            if (_plantingClient == clientId)
            {
                _plantingClient = 0;
                _plantProgress = 0f;
            }
        }

        /// <summary>
        /// Complete a plant if the key has been held long enough. Returns the armed site id,
        /// or null. The caller passes the site the planter is standing on, so the controller
        /// never has to know map geometry.
        /// </summary>
        public string CompletePlant(ulong clientId, string siteId, float now)
        {
            if (_plantingClient != clientId || Round != RoundPhase.Live)
            {
                return null;
            }

            BombPlayer player = GetPlayer(clientId);
            if (player == null || !player.Alive)
            {
                CancelPlant(clientId);
                return null;
            }

            float progress = PlantProgressAt(now);
            if (progress < 1f - ProgressEpsilon)
            {
                return null;
            }

            _plantingClient = 0;
            _plantProgress = 1f;
            _armedSite = siteId;
            Round = RoundPhase.Planted;
            _explosionAt = now + _rules.BombSeconds;
            _phaseEndsAt = _explosionAt;
            player.Plants++;
            player.Economy.Award(_economy.PlantReward, _rules.MaxMoney);
            _carrier = 0;
            return siteId;
        }

        public BombActionResult BeginDefuse(ulong clientId, bool onBomb, float now)
        {
            if (Round != RoundPhase.Planted)
            {
                return Fail(BombActionOutcome.BombNotPlanted, "there is nothing to defuse");
            }

            BombPlayer player = GetPlayer(clientId);
            if (player == null)
            {
                return Fail(BombActionOutcome.UnknownPlayer, "unknown player");
            }

            if (!player.Alive)
            {
                return Fail(BombActionOutcome.Dead, "a dead operator cannot defuse");
            }

            if (player.Team != Team.Defenders)
            {
                return Fail(BombActionOutcome.WrongTeam, "only the defending side defuses");
            }

            if (!onBomb)
            {
                return Fail(BombActionOutcome.NotOnASite, "not at the charge");
            }

            _defusingClient = clientId;
            _defuseStartedAt = now;
            _defuseSecondsNeeded = player.HasDefuseKit
                ? _rules.DefuseKitSeconds
                : _rules.DefuseSeconds;
            return new BombActionResult { Outcome = BombActionOutcome.Accepted };
        }

        public float DefuseProgressAt(float now)
        {
            if (_defusingClient == 0 || Round != RoundPhase.Planted)
            {
                return 0f;
            }

            return Clamp01((now - _defuseStartedAt) / Math.Max(0.01f, _defuseSecondsNeeded));
        }

        public void CancelDefuse(ulong clientId)
        {
            if (_defusingClient == clientId)
            {
                _defusingClient = 0;
            }
        }

        /// <summary>True when the defuse finished; ends the round in that case.</summary>
        public bool CompleteDefuse(ulong clientId, float now)
        {
            if (_defusingClient != clientId || Round != RoundPhase.Planted)
            {
                return false;
            }

            BombPlayer player = GetPlayer(clientId);
            if (player == null || !player.Alive)
            {
                CancelDefuse(clientId);
                return false;
            }

            if (DefuseProgressAt(now) < 1f - ProgressEpsilon)
            {
                return false;
            }

            _defusingClient = 0;
            player.Defuses++;
            player.Economy.Award(_economy.DefuseReward, _rules.MaxMoney);
            EndRound(Team.Defenders, RoundEndReason.BombDefused, now);
            return true;
        }

        /// <summary>
        /// Which site, if any, a position is standing on.  Map geometry stays in the map
        /// data; the controller only sees the result.
        /// </summary>
        public static string SiteAt(IReadOnlyList<BombSiteVolume> sites, float x, float y)
        {
            for (int i = 0; i < sites.Count; i++)
            {
                BombSiteVolume site = sites[i];
                float dx = x - site.CentreX;
                float dy = y - site.CentreY;
                if (dx * dx + dy * dy <= site.Radius * site.Radius)
                {
                    return site.Id;
                }
            }

            return null;
        }

        /// <summary>Whether a position is inside one of the buy zones of a team.</summary>
        public static bool InBuyZone(IReadOnlyList<BuyZoneVolume> zones, Team team,
                                     float x, float y)
        {
            for (int i = 0; i < zones.Count; i++)
            {
                BuyZoneVolume zone = zones[i];
                if (zone.Team != team)
                {
                    continue;
                }

                float dx = x - zone.CentreX;
                float dy = y - zone.CentreY;
                if (dx * dx + dy * dy <= zone.Radius * zone.Radius)
                {
                    return true;
                }
            }

            return false;
        }

        // ------------------------------------------------------------------
        // Combat
        // ------------------------------------------------------------------
        public BombActionResult RegisterKill(ulong killerId, ulong victimId, float now)
        {
            if (Phase != MatchPhase.Live || (Round != RoundPhase.Live && Round != RoundPhase.Planted))
            {
                return Fail(BombActionOutcome.WrongPhase, "no round is being played");
            }

            BombPlayer killer = GetPlayer(killerId);
            BombPlayer victim = GetPlayer(victimId);
            if (killer == null || victim == null)
            {
                return Fail(BombActionOutcome.UnknownPlayer, "unknown player");
            }

            if (!victim.Alive)
            {
                return Fail(BombActionOutcome.Dead, "the victim is already dead");
            }

            if (killer.Team == victim.Team)
            {
                return Fail(BombActionOutcome.WrongTeam, "team kill");
            }

            victim.Alive = false;
            victim.Deaths++;
            killer.Kills++;
            killer.Economy.Award(_economy.KillReward, _rules.MaxMoney);

            // A dead defuser cannot keep cutting the wire, and a dead carrier cannot plant.
            if (_defusingClient == victimId)
            {
                _defusingClient = 0;
            }

            if (_plantingClient == victimId)
            {
                _plantingClient = 0;
                _plantProgress = 0f;
            }

            if (_carrier == victimId)
            {
                _carrier = PickCarrier();
            }

            // Elimination ends the round - except that a planted charge keeps running after
            // every attacker is dead, which is the whole point of a retake.
            if (AliveOf(Team.Defenders) == 0 && Round != RoundPhase.RoundOver)
            {
                EndRound(Team.Attackers, RoundEndReason.DefendersEliminated, now);
            }
            else if (AliveOf(Team.Attackers) == 0 && Round == RoundPhase.Live)
            {
                EndRound(Team.Defenders, RoundEndReason.AttackersEliminated, now);
            }

            return new BombActionResult { Outcome = BombActionOutcome.Accepted };
        }

        private void EndRound(Team winner, RoundEndReason reason, float now)
        {
            Round = RoundPhase.RoundOver;
            LastEndReason = reason;
            _roundOverAt = now + _rules.RoundOverSeconds;
            _defusingClient = 0;
            _plantingClient = 0;

            _squadScore[SquadOn(winner)]++;

            int winnerReward = _economy.RoundWinReward;
            if (reason == RoundEndReason.BombExploded)
            {
                winnerReward += _economy.TeamDetonationReward;
            }

            for (int i = 0; i < _players.Count; i++)
            {
                BombPlayer player = _players[i];
                bool onWinningSide = player.Team == winner;
                if (onWinningSide)
                {
                    player.Economy.RecordWin();
                    player.Economy.Award(winnerReward, _rules.MaxMoney);
                }
                else
                {
                    player.Economy.RecordLoss();
                    int bonus = _economy.RewardForLoss(player.Economy.ConsecutiveLosses - 1);
                    if (reason == RoundEndReason.TimeExpired)
                    {
                        bonus += _economy.TimeExpiredBonus;
                    }

                    player.Economy.Award(bonus, _rules.MaxMoney);
                }
            }
        }

        // ------------------------------------------------------------------
        // Results
        // ------------------------------------------------------------------

        /// <summary>
        /// The end-of-match screen for this mode.  A demolition result is a *team* result,
        /// so the rows are ordered by squad first and by personal score inside it, which is
        /// how a player reads a 13-7 scoreboard: their side, then their own line.
        /// </summary>
        public MatchResults BuildResults()
        {
            MatchResults results = new MatchResults
            {
                Reason = MatchEndReason.KillTargetReached,
                WinnerId = 0,
                DurationSeconds = 0f
            };

            List<BombPlayer> ordered = new List<BombPlayer>(_players);
            ordered.Sort((a, b) =>
            {
                int squad = Squash(a).CompareTo(Squash(b));
                if (squad != 0)
                {
                    return squad;
                }

                int byKills = b.Kills.CompareTo(a.Kills);
                return byKills != 0 ? byKills : a.ClientId.CompareTo(b.ClientId);
            });

            for (int i = 0; i < ordered.Count; i++)
            {
                BombPlayer player = ordered[i];
                bool winner = MatchWinner != Team.None && player.Team == MatchWinner;
                if (winner && results.WinnerId == 0)
                {
                    results.WinnerId = player.ClientId;
                    results.WinnerName = player.DisplayName;
                    results.WinnerKills = player.Kills;
                }

                results.Standings.Add(new ScoreboardRow
                {
                    ClientId = player.ClientId,
                    DisplayName = player.DisplayName,
                    Kills = player.Kills,
                    Deaths = player.Deaths,
                    Damage = 0,
                    PingMs = 0,
                    IsConnected = true,
                    IsBot = player.IsBot,
                    IsAlive = player.Alive,
                    Rank = i + 1,
                    IsWinner = winner,
                    KillsToObjective = 0
                });

                results.TotalEliminations += player.Kills;
            }

            return results;
        }

        /// <summary>Rows are grouped by side, attackers first, so both teams read in blocks.</summary>
        private static int Squash(BombPlayer player) =>
            player.Team == Team.Attackers ? 0 : 1;

        // ------------------------------------------------------------------
        // Buying
        // ------------------------------------------------------------------
        /// <summary>
        /// Buy one item. The client sends the item id; everything else - price, legality,
        /// whether the buy window is still open, whether the operator is standing in a buy
        /// zone - is decided here.
        /// </summary>
        public BuyResult TryBuy(ulong clientId, string itemId, bool inBuyZone, float now)
        {
            BombPlayer player = GetPlayer(clientId);
            if (player == null)
            {
                return BuyFail(BuyOutcome.RoundNotRunning, itemId, 0, "unknown player");
            }

            BuyResult Base(BuyOutcome outcome, int price) => new BuyResult
            {
                Outcome = outcome,
                ItemId = itemId,
                Price = price,
                Money = player.Economy.Money,
                OwnedPrimary = player.Buy.PrimaryId
            };

            if (Phase != MatchPhase.Live || (Round != RoundPhase.Freeze && Round != RoundPhase.Live))
            {
                return Base(BuyOutcome.RoundNotRunning, 0);
            }

            if (!player.Alive)
            {
                return Base(BuyOutcome.Dead, 0);
            }

            if (!inBuyZone)
            {
                return Base(BuyOutcome.OutOfBuyZone, 0);
            }

            if (!BuyWindowOpen(now))
            {
                return Base(BuyOutcome.BuyTimeOver, 0);
            }

            BuyItem item = _catalog.Find(itemId);
            if (item == null)
            {
                return Base(BuyOutcome.UnknownItem, 0);
            }

            if (item.TeamRestriction != Team.None && item.TeamRestriction != player.Team)
            {
                return Base(BuyOutcome.WrongTeam, item.Price);
            }

            if (item.Slot == BuySlot.Grenade && player.Buy.Grenades.Count >= 4)
            {
                return Base(BuyOutcome.TooManyGrenades, item.Price);
            }

            if (item.Slot == BuySlot.Primary && player.Buy.PrimaryId == item.WeaponId)
            {
                return Base(BuyOutcome.SlotAlreadyFull, item.Price);
            }

            if (item.Slot == BuySlot.Sidearm && player.Buy.SidearmId == item.WeaponId
                && item.Id != "p9_alt")
            {
                return Base(BuyOutcome.SlotAlreadyFull, item.Price);
            }

            if (!player.Economy.CanAfford(item.Price))
            {
                return Base(BuyOutcome.NotEnoughMoney, item.Price);
            }

            if (!player.Economy.TrySpend(item.Price))
            {
                return Base(BuyOutcome.NotEnoughMoney, item.Price);
            }

            switch (item.Slot)
            {
                case BuySlot.Primary:
                    player.Buy.PrimaryId = item.WeaponId;
                    break;
                case BuySlot.Sidearm:
                    player.Buy.SidearmId = item.WeaponId;
                    break;
                case BuySlot.Armor:
                    player.Buy.ArmorId = item.Id;
                    break;
                case BuySlot.DefuseKit:
                    player.Buy.HasDefuseKit = true;
                    break;
                case BuySlot.Grenade:
                    player.Buy.Grenades.Add(item.Id);
                    break;
            }

            return new BuyResult
            {
                Outcome = BuyOutcome.Accepted,
                ItemId = item.Id,
                Price = item.Price,
                Money = player.Economy.Money,
                OwnedPrimary = player.Buy.PrimaryId
            };
        }

        private BuyResult BuyFail(BuyOutcome outcome, string itemId, int price, string reason) =>
            new BuyResult { Outcome = outcome, ItemId = itemId, Price = price };

        /// <summary>Buying is open during freeze time and for BuySeconds into the live round.</summary>
        public bool BuyWindowOpen(float now)
        {
            if (Round == RoundPhase.Freeze)
            {
                return true;
            }

            if (Round != RoundPhase.Live)
            {
                return false;
            }

            return now <= _liveStartedAt + _rules.BuySeconds;
        }

        public BombRoundSnapshot Snapshot(float now) => new BombRoundSnapshot
        {
            RoundNumber = RoundNumber,
            Phase = Round,
            SecondsLeft = Remaining(now),
            AttackerScore = AttackerScore,
            DefenderScore = DefenderScore,
            BombPlanted = Round == RoundPhase.Planted,
            BombSite = _armedSite,
            BombSecondsLeft = Round == RoundPhase.Planted ? Math.Max(0f, _explosionAt - now) : 0f,
            PlantProgress = PlantProgressAt(now),
            DefuseProgress = DefuseProgressAt(now),
            DefusingClient = _defusingClient,
            CarrierClient = _carrier,
            LastEndReason = LastEndReason
        };

        private float Remaining(float now)
        {
            switch (Round)
            {
                case RoundPhase.Freeze:
                case RoundPhase.Live:
                case RoundPhase.Planted:
                    return Math.Max(0f, _phaseEndsAt - now);
                case RoundPhase.RoundOver:
                    return Math.Max(0f, _roundOverAt - now);
                default:
                    return 0f;
            }
        }

        private static BombActionResult Fail(BombActionOutcome outcome, string reason) =>
            new BombActionResult { Outcome = outcome, Reason = reason };

        private static float Clamp01(float value) =>
            value < 0f ? 0f : (value > 1f ? 1f : value);
    }
}
