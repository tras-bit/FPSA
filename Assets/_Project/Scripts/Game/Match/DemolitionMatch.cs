using System;
using System.Collections.Generic;
using Kordon.Core.Ammo;
using Kordon.Core.Combat;
using Kordon.Core.Data;
using Kordon.Core.Loadout;
using Kordon.Core.Rules;
using Kordon.Core.Settings;
using Kordon.Game.Camera;
using Kordon.Game.Input;
using Kordon.Game.Maps;
using Kordon.Game.Player;
using Kordon.Game.Ui;
using Kordon.Game.Weapons;
using UnityEngine;

namespace Kordon.Game.Match
{
    /// <summary>
    /// The demolition mode, played offline against labelled bots so the whole loop can be
    /// walked: freeze time, buying, a live round, planting, defusing, detonation, round
    /// score, side swap, match end.
    ///
    /// The rules live in Kordon.Core.Rules.BombMatchController and are covered by the test
    /// suite. This component is deliberately thin: it owns the clock, the geometry queries
    /// (which site am I standing on, am I in a buy zone), input and the HUD, and it forwards
    /// every decision to the controller. Anything that could change the outcome of a round
    /// is in Core, not here.
    ///
    /// What is verified: the controller's rules and the map data run in CI. What is not:
    /// how this feels with a mouse in hand - that needs the editor and a play test.
    /// </summary>
    public sealed class DemolitionMatch : MonoBehaviour
    {
        public const ulong LocalClientId = 0x2001UL;
        private const ulong FirstBotId = 0x2100UL;

        public BombMatchController Match { get; private set; }
        public DemolitionMapData MapData { get; private set; }
        public BuiltMap Map { get; private set; }
        public PlayerController Player { get; private set; }
        public WeaponController Weapons { get; private set; }
        public BodycamRig Rig { get; private set; }
        public HudScreen Hud { get; private set; }
        public DemolitionBots Bots { get; private set; }
        public LoadoutSelection LocalLoadout { get; private set; }
        public string MapId { get; private set; } = "ffa_zavod7";
        public float Now => Time.time - _start;

        public event Action MatchFinished;
        public event Action<string> Notice;

        private GameDatabase _database;
        private float _start;
        private bool _finished;
        private bool _buyMenuOpen;
        private int _spawnCursor;
        private float _plantHeld;
        private string _plantSite;
        private float _defuseHeld;
        private readonly List<ulong> _bots = new List<ulong>();
        private readonly Dictionary<ulong, GameObject> _botBodies =
            new Dictionary<ulong, GameObject>();
        private readonly List<ScoreRow> _scoreRows = new List<ScoreRow>();
        private float _scoreboardTimer;
        private string _appliedArmorId;
        private int _localSpawnRound = -1;

        /// <summary>Everything the HUD and the buy screen need, refreshed once per tick.</summary>
        public BombRoundSnapshot Round => Match != null ? Match.Snapshot(Now) : default;

        public Team LocalTeam =>
            Match != null && Match.GetPlayer(LocalClientId) != null
                ? Match.GetPlayer(LocalClientId).Team
                : Team.None;

        public PlayerEconomy LocalEconomy =>
            Match != null && Match.GetPlayer(LocalClientId) != null
                ? Match.GetPlayer(LocalClientId).Economy
                : null;

        public BuyState LocalBuy =>
            Match != null && Match.GetPlayer(LocalClientId) != null
                ? Match.GetPlayer(LocalClientId).Buy
                : null;

        public bool BuyMenuOpen => _buyMenuOpen;

        public GameDatabase Database => _database;

        public void CloseBuyMenu()
        {
            _buyMenuOpen = false;
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
        }

        /// <summary>End-of-match standings for the results screen.</summary>
        public MatchResults BuildResults() => Match != null ? Match.BuildResults() : new MatchResults();

        public void Stop()
        {
            if (Map != null)
            {
                Destroy(Map.gameObject);
            }
        }

        public void Begin(GameDatabase database, string mapId, LoadoutSelection loadout,
                          Transform parent, int botsPerSide = 4)
        {
            _database = database;
            MapId = mapId;
            LocalLoadout = loadout ?? PracticeMatch.DefaultLoadout(database.Definitions);

            BombRules rules = BombRules.Practice();
            rules.MaxPlayers = rules.TeamSize * 2;
            Match = new BombMatchController(rules, new EconomyRules(), BuyCatalog.Default());

            MapData = DemolitionMapData.Parse(
                ContentPaths.Read(ContentPaths.MapLayout(mapId)), out string mapError);
            if (MapData == null)
            {
                Notice?.Invoke("demolition layout refused: " + mapError);
                MapData = new DemolitionMapData();
            }

            MapLayout layout = MapLayout.FromJson(ContentPaths.Read(ContentPaths.MapLayout(mapId)));
            Map = MapBuilder.Build(layout, KitManifestData.Load(), parent);

            _start = Time.time;

            Team localTeam = Team.Attackers;
            Match.AddPlayer(LocalClientId, PlayerProfile.Name, localTeam, false, 0f);

            // Bots fill both sides and are labelled as bots in the scoreboard, exactly as
            // the practice rules require.
            for (int i = 0; i < botsPerSide; i++)
            {
                ulong attacker = FirstBotId + (ulong)i;
                Match.AddPlayer(attacker, "BOT-" + (i + 1), Team.Attackers, true, 0f);
                _bots.Add(attacker);

                ulong defender = FirstBotId + 100UL + (ulong)i;
                Match.AddPlayer(defender, "BOT-" + (i + 1) + "D", Team.Defenders, true, 0f);
                _bots.Add(defender);
            }

            BuildLocalPlayer(parent, layout);
            Match.StartMatch(0f);
            PlaceAtTeamSpawn(true);

            Bots = parent.gameObject.AddComponent<DemolitionBots>();
            Bots.Attach(this);

            Hud?.SetObjective(
                "DEMOLITION - plant the charge at A or B. Buy in the shelter before the round.");
        }


        private void BuildLocalPlayer(Transform parent, MapLayout layout)
        {
            LocalOperator op = LocalOperatorFactory.Build(parent, this, _database.Definitions,
                LocalLoadout, 0x51E5D5EEDUL, false, layout, false);
            Player = op.Body;
            Rig = op.Rig;
            Weapons = op.Weapons;
            Hud = op.Hud;
            Player.NoiseEmitted += _ => { };
            Weapons.PlayerHit += OnLocalHit;
        }

        /// <summary>
        /// A bot's round landed on the local player. The Core controller owns the kill, so
        /// the body and the controller are told in that order: vitals for the ragdoll and
        /// the camera, then RegisterKill for score, economy and the round's aliveness.
        /// </summary>
        public bool ReceiveBotHit(ulong shooterId, string shooterName, DamageResult result)
        {
            if (Player == null || Player.IsDead)
            {
                return false;
            }

            bool died = Player.Vitals.ApplyDamage(result, DeathCause.Gunfire);
            Hud?.DamageFlash();
            Hud?.SetHealth(Player.Vitals.Health / PlayerVitals.MaxHealth);

            if (died)
            {
                Match.RegisterKill(shooterId, LocalClientId, Now);
                Hud?.Toast(shooterName + " eliminated you");
            }

            return true;
        }

        private void OnLocalHit(PlayerController target, DamageResult result)
        {
            if (target == Player)
            {
                Hud?.DamageFlash();
                Hud?.SetHealth(Player.Vitals.Health / PlayerVitals.MaxHealth);
            }
            else
            {
                Hud?.HitMarker();
            }
        }

        // ------------------------------------------------------------------
        // Round loop
        // ------------------------------------------------------------------
        private void Update()
        {
            if (Match == null)
            {
                return;
            }

            float now = Now;
            Match.Tick(now);
            HandleBuyInput(now);
            HandleObjectiveInput(now);
            UpdateAliveState(now);
            UpdateHud(now);

            if (Match.Round == RoundPhase.MatchOver && !_finished)
            {
                _finished = true;
                Hud?.Toast("match complete");
                MatchFinished?.Invoke();
            }
        }

        private void HandleBuyInput(float now)
        {
            if (Weapons != null)
            {
                Weapons.InputLocked = _buyMenuOpen;
            }

            // B opens the shop, but only when the server would accept a purchase at all -
            // opening a menu that refuses every click is worse than not opening it.
            if (KordonInput.Pressed(InputAction.BuyMenu))
            {
                if (Match.BuyWindowOpen(now) || _buyMenuOpen)
                {
                    _buyMenuOpen = !_buyMenuOpen;
                }
                else
                {
                    Hud?.Toast("buy time is over");
                }
            }

            if (_buyMenuOpen && UnityEngine.Input.GetKeyDown(KeyCode.Escape))
            {
                _buyMenuOpen = false;
                Cursor.lockState = CursorLockMode.Locked;
                Cursor.visible = false;
            }
            else if (_buyMenuOpen)
            {
                // A shop that steals the mouse has to say so, and the body has to stop
                // aiming while it is open.
                Cursor.lockState = CursorLockMode.None;
                Cursor.visible = true;
            }
        }

        /// <summary>Buy an item by id. Called by the shop screen; the server decides.</summary>
        public BuyResult Buy(string itemId)
        {
            BuyResult result = Match.TryBuy(LocalClientId, itemId, InBuyZone(),
                Now);
            if (result.IsAccepted)
            {
                Hud?.Toast("bought " + itemId);
                ApplyLoadout(result.OwnedPrimary);
                ApplyArmor();
            }
            else
            {
                Hud?.Toast(Describe(result.Outcome));
            }

            return result;
        }

        private void ApplyLoadout(string primaryId)
        {
            if (Weapons == null || string.IsNullOrEmpty(primaryId))
            {
                return;
            }

            // The bought weapon is what the hands hold: the viewmodel is re-equipped, it is
            // not a HUD flag. A purchase that only changed a label would be a lie.
            LoadoutSelection selection = LocalLoadout.Clone();
            selection.PrimaryWeaponId = primaryId;
            selection.PrimaryMagazines.Clear();
            selection = PracticeMatch.ClampToLibrary(selection, _database.Definitions);
            LocalLoadout = selection;
            Weapons.Equip(selection, _database.Definitions, 0xB0BUL);
        }

        /// <summary>
        /// Armour is not a HUD flag either: the plate carrier the player paid for is the one
        /// the bot rounds are resolved against, so buying it changes the damage they take.
        /// </summary>
        private void ApplyArmor()
        {
            BuyState buy = LocalBuy;
            if (buy == null || Player == null || string.IsNullOrEmpty(buy.ArmorId))
            {
                return;
            }

            // The shop's two tiers map onto the authored kits: the light rig is the cheap
            // vest, the plate carrier with the helmet is the standard kit.
            string kitId = buy.ArmorId == "vest_helmet" ? "standard" : "light";
            var kit = _database.Definitions.GetArmorKit(kitId);
            if (kit == null)
            {
                return;
            }

            Player.Armor = new ArmorState(kit);
            _appliedArmorId = buy.ArmorId;
        }

        private static string Describe(BuyOutcome outcome)
        {
            switch (outcome)
            {
                case BuyOutcome.NotEnoughMoney: return "not enough money";
                case BuyOutcome.BuyTimeOver: return "buy time is over";
                case BuyOutcome.OutOfBuyZone: return "return to the buy zone";
                case BuyOutcome.WrongTeam: return "your side cannot buy that";
                case BuyOutcome.SlotAlreadyFull: return "you already carry that";
                case BuyOutcome.TooManyGrenades: return "no more grenades";
                case BuyOutcome.RoundNotRunning: return "no round is running";
                default: return "purchase refused";
            }
        }

        private void HandleObjectiveInput(float now)
        {
            // Planting and defusing both use the interact key; the round rules decide
            // which of the two is even possible right now.
            bool interact = KordonInput.Held(InputAction.Interact);

            // ---- plant ------------------------------------------------------
            if (LocalTeam == Team.Attackers && Match.Round == RoundPhase.Live)
            {
                string site = BombMatchController.SiteAt(MapData.Sites,
                    Player.transform.position.x, Player.transform.position.z);

                if (interact && !Player.IsDead && site != null)
                {
                    if (_plantSite != site)
                    {
                        _plantSite = site;
                        Match.BeginPlant(LocalClientId, site, true, now);
                    }

                    string armed = Match.CompletePlant(LocalClientId, site, now);
                    if (armed != null)
                    {
                        Hud?.Toast("charge armed at " + armed);
                        _plantHeld = 0f;
                        _plantSite = null;
                    }
                    else
                    {
                        _plantHeld = Match.PlantProgressAt(now);
                    }
                }
                else if (_plantSite != null)
                {
                    Match.CancelPlant(LocalClientId);
                    _plantSite = null;
                    _plantHeld = 0f;
                }
            }

            // ---- defuse -----------------------------------------------------
            if (LocalTeam == Team.Defenders && Match.Round == RoundPhase.Planted)
            {
                bool onBomb = OnCharge();

                if (interact && !Player.IsDead && onBomb)
                {
                    if (_defuseHeld == 0f)
                    {
                        Match.BeginDefuse(LocalClientId, true, now);
                    }

                    _defuseHeld = Match.DefuseProgressAt(now);
                    if (Match.CompleteDefuse(LocalClientId, now))
                    {
                        Hud?.Toast("charge defused");
                        _defuseHeld = 0f;
                    }
                }
                else if (_defuseHeld > 0f)
                {
                    Match.CancelDefuse(LocalClientId);
                    _defuseHeld = 0f;
                }
            }
        }

        /// <summary>Standing within reach of an armed charge.</summary>
        private bool OnCharge()
        {
            if (Match.ArmedSite == null || MapData == null)
            {
                return false;
            }

            BombSiteVolume? site = MapData.Site(Match.ArmedSite);
            if (!site.HasValue)
            {
                return false;
            }

            Vector3 position = Player.transform.position;
            float dx = position.x - site.Value.CentreX;
            float dz = position.z - site.Value.CentreY;
            return dx * dx + dz * dz <= 2.5f * 2.5f;
        }

        public bool InBuyZone()
        {
            Vector3 position = Player.transform.position;
            return BombMatchController.InBuyZone(MapData.BuyZones, LocalTeam,
                position.x, position.z);
        }

        private void UpdateAliveState(float now)
        {
            BombPlayer local = Match.GetPlayer(LocalClientId);
            if (local == null)
            {
                return;
            }

            if (!local.Alive && !Player.IsDead)
            {
                // The controller decided the death; the body follows. Respawn happens at the
                // top of the next round, which is what makes a demolition round terminal.
                Hud?.Toast("eliminated - waiting for the next round");
            }

            // Every round starts from the team's shelter, not from wherever the last one
            // ended: standing in a bombsite during buy time would be a free forward spawn.
            if (Match.Round == RoundPhase.Freeze && _localSpawnRound != Round.RoundNumber)
            {
                _localSpawnRound = Round.RoundNumber;
                if (local.Alive)
                {
                    PlaceAtTeamSpawn(false);
                }
            }
        }

        // ------------------------------------------------------------------
        // Spawning
        // ------------------------------------------------------------------
        private void PlaceAtTeamSpawn(bool initial)
        {
            if (Map.SpawnPoints.Count == 0)
            {
                Player.Respawn(Vector3.zero, 0f);
                return;
            }

            // The map has a spawn point per authored id, and the layout names which of them
            // belong to which side. Asking the layout first is what keeps an attacker from
            // being placed in a defender shelter.
            IReadOnlyList<string> ids = MapData.SpawnsFor(LocalTeam);
            Transform spawn = null;
            if (ids.Count > 0)
            {
                _spawnCursor = (_spawnCursor + 1) % ids.Count;
                string wanted = "Spawn_" + ids[_spawnCursor];
                foreach (Transform point in Map.SpawnPoints)
                {
                    if (point != null && point.name == wanted)
                    {
                        spawn = point;
                        break;
                    }
                }
            }

            if (spawn == null)
            {
                _spawnCursor = (_spawnCursor + 1) % Map.SpawnPoints.Count;
                spawn = Map.SpawnPoints[_spawnCursor % Map.SpawnPoints.Count];
            }

            Player.Respawn(spawn.position, -spawn.rotation.eulerAngles.y);
            // Respawn puts an empty vest on the body, so the armour the shop recorded has
            // to be worn again rather than left behind on the previous life.
            _appliedArmorId = null;
            ApplyArmor();
            if (!initial)
            {
                Weapons.Loadout?.OnRespawnResupply();
            }
        }

        // ------------------------------------------------------------------
        // HUD
        // ------------------------------------------------------------------
        private void UpdateHud(float now)
        {
            if (Hud == null || Match == null)
            {
                return;
            }

            BombRoundSnapshot snapshot = Match.Snapshot(now);
            BombPlayer local = Match.GetPlayer(LocalClientId);
            string phase = snapshot.Phase == RoundPhase.Freeze
                ? "BUY " + Mathf.CeilToInt(snapshot.SecondsLeft)
                : snapshot.Phase == RoundPhase.Planted
                    ? "CHARGE " + Mathf.CeilToInt(snapshot.BombSecondsLeft)
                    : Mathf.CeilToInt(snapshot.SecondsLeft).ToString();

            Hud.SetMatchLine(string.Format(
                "{0}  {1}  {2}  |  {3}:{4}  |  ${5}  |  {6} alive",
                local != null && local.Team == Team.Attackers ? "ATTACK" : "DEFEND",
                phase,
                "R" + snapshot.RoundNumber,
                snapshot.AttackerScore, snapshot.DefenderScore,
                LocalEconomy != null ? LocalEconomy.Money : 0,
                Match.AliveOf(LocalTeam)));

            Hud.SetHealth(Player.Vitals.Health / PlayerVitals.MaxHealth);

            // A new round starts with the kit the shop recorded; the plates have to be put
            // back on the body, not just remembered by the controller.
            if (local != null && local.Buy != null && local.Buy.ArmorId != _appliedArmorId)
            {
                ApplyArmor();
            }

            _scoreboardTimer -= Time.deltaTime;
            if (_scoreboardTimer <= 0f)
            {
                _scoreboardTimer = 1f;
                RefreshScoreboard(snapshot);
            }

            if (_plantHeld > 0f)
            {
                Hud.SetObjective("PLANTING " + Mathf.RoundToInt(_plantHeld * 100f) + "%");
            }
            else if (_defuseHeld > 0f)
            {
                Hud.SetObjective("DEFUSING " + Mathf.RoundToInt(_defuseHeld * 100f) + "%");
            }
            else if (Match.Round == RoundPhase.RoundOver)
            {
                Hud.SetObjective(DescribeEnd(Match.LastEndReason));
            }
        }

        /// <summary>
        /// The hold-Tab board for this mode: two blocks, attackers then defenders, with the
        /// round score in the caption so a player can read the match at a glance.
        /// </summary>
        private void RefreshScoreboard(BombRoundSnapshot snapshot)
        {
            if (Hud == null)
            {
                return;
            }

            _scoreRows.Clear();
            for (int side = 0; side < 2; side++)
            {
                Team team = side == 0 ? Team.Attackers : Team.Defenders;
                foreach (BombPlayer player in Match.Players)
                {
                    if (player.Team != team)
                    {
                        continue;
                    }

                    _scoreRows.Add(new ScoreRow
                    {
                        Name = (team == Team.Attackers ? "[A] " : "[D] ") + player.DisplayName,
                        Kills = player.Kills,
                        Deaths = player.Deaths,
                        Ping = 0,
                        IsLocal = player.ClientId == LocalClientId,
                        IsBot = player.IsBot
                    });
                }
            }

            Hud.SetScoreboardTitle(string.Format("DEMOLITION  {0}:{1}  ROUND {2}",
                snapshot.AttackerScore, snapshot.DefenderScore, snapshot.RoundNumber));
            Hud.SetScoreboard(_scoreRows);
        }

        private static string DescribeEnd(RoundEndReason reason)
        {
            switch (reason)
            {
                case RoundEndReason.BombExploded: return "CHARGE DETONATED";
                case RoundEndReason.BombDefused: return "CHARGE DEFUSED";
                case RoundEndReason.AttackersEliminated: return "ATTACKERS ELIMINATED";
                case RoundEndReason.DefendersEliminated: return "DEFENDERS ELIMINATED";
                case RoundEndReason.TimeExpired: return "TIME EXPIRED";
                default: return string.Empty;
            }
        }
    }
}
