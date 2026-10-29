using System;
using System.Collections.Generic;
using Kordon.Core.Ammo;
using Kordon.Core.Combat;
using Kordon.Core.Data;
using Kordon.Core.Loadout;
using Kordon.Core.Movement;
using Kordon.Core.Rules;
using Kordon.Core.Utility;
using Kordon.Game.Camera;
using Kordon.Game.Maps;
using Kordon.Game.Player;
using Kordon.Game.Ui;
using Kordon.Game.Weapons;
using UnityEngine;

namespace Kordon.Game.Match
{
    /// <summary>
    /// Offline practice on a real map, driven by the very same Core
    /// FfaMatchController the networked match uses (50 kill target, respawn timer,
    /// spawn protection, sealed loadout).  It exists so a player can learn the
    /// magazine system, inspect flow and recoil without a server.  It is clearly
    /// labelled as practice in the HUD; bots join it only once they exist, and they
    /// are always named as bots - practice never stands in for networked FFA.
    /// </summary>
    public sealed class PracticeMatch : MonoBehaviour
    {
        public const ulong LocalClientId = 0x1001UL;

        public FfaMatchController Match { get; private set; }
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
        public event Action<NoiseEvent> WorldNoise;

        private GameDatabase _database;
        private float _start;
        private float _scoreboardTimer;
        private bool _finished;
        private int _spawnCursor;
        private readonly List<ScoreRow> _rows = new List<ScoreRow>();

        public void Begin(GameDatabase database, string mapId, LoadoutSelection loadout,
            Transform parent)
        {
            _database = database;
            MapId = mapId;
            LocalLoadout = loadout ?? DefaultLoadout(database.Definitions);

            MatchRulesConfig rules = database.MatchRules.Clone();
            rules.AllowBots = false;
            rules.MinPlayersToStartLive = 1;
            rules.LoadoutPhaseSeconds = 45f;
            Match = new FfaMatchController(rules, new DeterministicRandom(0x51E5D5EEDUL));

            MapLayout layout = MapLayout.FromJson(ContentPaths.Read(ContentPaths.MapLayout(mapId)));
            Map = MapBuilder.Build(layout, KitManifestData.Load(), parent);

            _start = Time.time;
            BuildLocalPlayer(parent, layout);
            Match.TryJoin(LocalClientId, PlayerProfile.Name, false, 0f);
            Match.BeginLoadoutPhase(0f);
            LoadoutLockResult locked = Match.SubmitLoadout(LocalClientId, LocalLoadout,
                database.Definitions, 0f);
            if (!locked.Accepted)
            {
                Notice?.Invoke("kit rejected: " + locked.Reason);
            }

            Match.BeginCountdown(0f);
            Match.BeginLive(0f);
            PlaceAtSpawn(true);
        }

        private void BuildLocalPlayer(Transform parent, MapLayout layout)
        {
            LocalOperator op = LocalOperatorFactory.Build(parent, this, _database.Definitions,
                LocalLoadout, 0x51E5D5EEDUL, false, layout, false);
            Player = op.Body;
            Rig = op.Rig;
            Weapons = op.Weapons;
            Hud = op.Hud;
            Player.NoiseEmitted += noise => WorldNoise?.Invoke(noise);
            Weapons.PlayerHit += OnLocalHit;
            op.Grenades.BlastResolved += OnBlastResolved;
            Hud.SetObjective("PRACTICE - no server, no bots. Learn the kit, the map and the magazine rules.");
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

        /// <summary>
        /// Offline blast feedback and bookkeeping.  A grenade that kills you must put
        /// you on the normal respawn timer - an uncredited death, because nobody else
        /// scored it.
        /// </summary>
        private void OnBlastResolved(PlayerController target, BlastResult blast)
        {
            if (target == Player)
            {
                Hud?.DamageFlash();
                Hud?.SetHealth(Player.Vitals.Health / PlayerVitals.MaxHealth);
                if (Player.IsDead &&
                    Match.RegisterUncreditedDeath(LocalClientId, DeathCause.Explosion, Now))
                {
                    Hud?.Toast("caught in your own blast");
                }

                return;
            }

            Hud?.HitMarker();
        }

        private void PlaceAtSpawn(bool initial)
        {
            int wanted = NextSpawnIndex();
            RespawnResult respawn = Match.RequestRespawn(LocalClientId, wanted, Now);
            int index = respawn.IsAccepted ? respawn.SpawnPointIndex : wanted;
            if (Map.SpawnPoints.Count == 0)
            {
                Player.Respawn(Vector3.zero, 0f);
                return;
            }

            Transform spawn = Map.SpawnPoints[index % Map.SpawnPoints.Count];
            Player.Respawn(spawn.position, -spawn.rotation.eulerAngles.y);
            if (!initial)
            {
                Weapons.Loadout?.OnRespawnResupply();
            }
        }

        private int NextSpawnIndex()
        {
            _spawnCursor = (_spawnCursor + 1) % Mathf.Max(1, Map.SpawnPoints.Count);
            return _spawnCursor;
        }

        private void Update()
        {
            if (Match == null)
            {
                return;
            }

            float now = Now;
            Match.Tick(now);

            if (Match.Phase == MatchPhase.Live)
            {
                if (Player.IsDead && Match.IsRespawnReady(LocalClientId, now))
                {
                    PlaceAtSpawn(false);
                }

                UpdateHud(now);
            }

            if (Match.Phase == MatchPhase.Finished && !_finished)
            {
                _finished = true;
                MatchFinished?.Invoke();
            }
        }

        private void UpdateHud(float now)
        {
            PlayerMatchState local = Match.GetPlayer(LocalClientId);
            float respawnIn = Match.RespawnCountdownFor(LocalClientId, now);
            Hud.SetMatchLine(string.Format("PRACTICE   {0} kills   (target {1})   {2} s to respawn",
                local != null ? local.Kills : 0, Match.Config.KillTarget,
                Player.IsDead ? Mathf.CeilToInt(respawnIn).ToString() : "0"));
            Hud.SetHealth(Player.Vitals.Health / PlayerVitals.MaxHealth);

            _scoreboardTimer -= Time.deltaTime;
            if (_scoreboardTimer > 0f)
            {
                return;
            }

            _scoreboardTimer = 1f;
            _rows.Clear();
            foreach (ScoreboardRow row in Match.BuildScoreboard())
            {
                _rows.Add(new ScoreRow
                {
                    Name = row.DisplayName,
                    Kills = row.Kills,
                    Deaths = row.Deaths,
                    Ping = row.PingMs,
                    IsLocal = row.ClientId == LocalClientId,
                    IsBot = row.IsBot,
                });
            }

            Hud.SetScoreboard(_rows);
        }

        // -------------------------------------------------------------- loadout

        /// <summary>Balanced default kit: one rifle, one pistol, seven magazines, standard plates.</summary>
        public static LoadoutSelection DefaultLoadout(DefinitionLibrary library)
        {
            LoadoutSelection selection = new LoadoutSelection
            {
                PrimaryWeaponId = "vk74",
                PrimarySight = SightKind.RedDot,
                SecondaryWeaponId = "p9",
                SecondarySight = SightKind.IronSight,
                GrenadeId = "frag_f1",
                GrenadeCount = 2,
                ArmorKitId = "standard",
            };
            selection.PrimaryMagazines.Add(new MagazinePlanEntry("mag_545_30_ps", 6));
            selection.SecondaryMagazines.Add(new MagazinePlanEntry("mag_9x19_17_ap", 2));
            return ClampToLibrary(selection, library);
        }

        /// <summary>Drops any plan entry whose magazine does not fit the chosen weapon.</summary>
        public static LoadoutSelection ClampToLibrary(LoadoutSelection selection,
            DefinitionLibrary library)
        {
            LoadoutSelection result = selection.Clone();
            result.PrimaryMagazines = FilterPlanned(result.PrimaryMagazines, result.PrimaryWeaponId,
                library);
            result.SecondaryMagazines = FilterPlanned(result.SecondaryMagazines,
                result.SecondaryWeaponId, library);
            return result;
        }

        private static List<MagazinePlanEntry> FilterPlanned(List<MagazinePlanEntry> plan,
            string weaponId, DefinitionLibrary library)
        {
            List<MagazinePlanEntry> filtered = new List<MagazinePlanEntry>();
            WeaponDefinition weapon = string.IsNullOrEmpty(weaponId)
                ? null
                : library.Weapons.TryGetValue(weaponId, out WeaponDefinition found) ? found : null;
            if (weapon == null)
            {
                return filtered;
            }

            foreach (MagazinePlanEntry entry in plan)
            {
                if (entry.Count <= 0 ||
                    !library.Magazines.TryGetValue(entry.MagazineId, out MagazineDefinition magazine))
                {
                    continue;
                }

                if (magazine.IsCompatibleWith(weapon))
                {
                    filtered.Add(entry);
                }
            }

            if (filtered.Count == 0)
            {
                List<MagazineDefinition> compatible = library.MagazinesForWeapon(weapon);
                if (compatible.Count > 0)
                {
                    filtered.Add(new MagazinePlanEntry(compatible[0].Id, 4));
                }
            }

            return filtered;
        }

        public void Stop()
        {
            Match?.Abort(Now, "practice-ended");
            if (Map != null)
            {
                Destroy(Map.gameObject);
            }
        }
    }
}
