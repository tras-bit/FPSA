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

            if (local.Alive && Player.IsDead && Match.Round == RoundPhase.Freeze)
            {
                PlaceAtTeamSpawn(false);
            }
        }

        // ------------------------------------------------------------------
        // Spawning
        // ------------------------------------------------------------------
        private void PlaceAtTeamSpawn(bool initial)
        {
            IReadOnlyList<string> ids = MapData.SpawnsFor(LocalTeam);
            if (Map.SpawnPoints.Count == 0)
            {
                Player.Respawn(Vector3.zero, 0f);
                return;
            }

            _spawnCursor = (_spawnCursor + 1) % Mathf.Max(1, Map.SpawnPoints.Count);
            Transform spawn = Map.SpawnPoints[_spawnCursor % Map.SpawnPoints.Count];
            Player.Respawn(spawn.position, -spawn.rotation.eulerAngles.y);
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
