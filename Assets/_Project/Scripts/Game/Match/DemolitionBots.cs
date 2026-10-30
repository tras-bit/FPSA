using System;
using System.Collections.Generic;
using Kordon.Core.Combat;
using Kordon.Core.Data;
using Kordon.Core.Rules;
using Kordon.Core.Utility;
using Kordon.Game.Ai;
using Kordon.Game.Maps;
using Kordon.Game.Player;
using Kordon.Game.Ui;
using Kordon.Game.Weapons;
using Kordon.Net;
using UnityEngine;

namespace Kordon.Game.Match
{
    /// <summary>
    /// The five-a-side opposition in the offline demolition mode.
    ///
    /// Every bot is a <see cref="HostileAgent"/> body - the same authored operator rig the
    /// co-op militia uses - so the fight looks like a fight and not like capsules sliding
    /// around.  This component is the *director*: it spawns one body per bot player the
    /// Core <see cref="BombMatchController"/> knows about, feeds each agent its enemies,
    /// turns the agent's resolved rounds into the controller's kills and economy, and lets
    /// the bots take the objective (plant, defuse) so a round can actually finish.
    ///
    /// Honest limits: the bots aim with the agent's cone model, buy nothing, and walk the
    /// authored nav graph - they are not a stand-in for human opponents.  Every body carries
    /// a BOT name in the scoreboard and the controller marks it as a bot.
    /// </summary>
    public sealed class DemolitionBots : MonoBehaviour
    {
        private sealed class Unit
        {
            public ulong Id;
            public string Name;
            public Team Team;
            public GameObject Body;
            public HostileAgent Agent;
            public float Health;
            public bool Dead;
            public int Index;
            public string PlantSite;
            public bool Defusing;
        }

        private const float PlantStandOffM = 2.5f;
        private const float BodyHealth = 100f;

        private DemolitionMatch _session;
        private readonly List<Unit> _units = new List<Unit>();
        private readonly Dictionary<Transform, Unit> _byBody = new Dictionary<Transform, Unit>();
        private DeterministicRandom _random;
        private int _spawnedRound = -1;
        private bool _subscribed;

        public IReadOnlyList<ulong> Bots => _unitIds;
        private readonly List<ulong> _unitIds = new List<ulong>();

        public void Attach(DemolitionMatch session)
        {
            _session = session;
            _random = new DeterministicRandom(0xDEB01UL);
            if (!_subscribed && session.Weapons != null)
            {
                session.Weapons.ShotFired += OnLocalShot;
                _subscribed = true;
            }

            // The agents test line of sight with a marker on the body, so the player's own
            // body needs one or no bot would ever be able to see them.
            if (session.Player != null &&
                session.Player.GetComponentInParent<RemoteTargetMarker>() == null)
            {
                session.Player.gameObject.AddComponent<RemoteTargetMarker>().ClientId =
                    DemolitionMatch.LocalClientId;
            }
        }

        private void OnDestroy()
        {
            if (_subscribed && _session != null && _session.Weapons != null)
            {
                _session.Weapons.ShotFired -= OnLocalShot;
            }
        }

        // ------------------------------------------------------------------
        // Round pacing
        // ------------------------------------------------------------------
        private void Update()
        {
            if (_session == null || _session.Match == null)
            {
                return;
            }

            BombRoundSnapshot snapshot = _session.Round;
            if (snapshot.Phase == RoundPhase.Freeze && snapshot.RoundNumber != _spawnedRound)
            {
                SpawnRound(snapshot);
                return;
            }

            bool live = snapshot.Phase == RoundPhase.Live || snapshot.Phase == RoundPhase.Planted;
            SetAgentsActive(live);

            if (live)
            {
                UpdateObjectives(snapshot);
            }
        }

        private void SpawnRound(BombRoundSnapshot snapshot)
        {
            _spawnedRound = snapshot.RoundNumber;
            ClearBodies();

            foreach (BombPlayer player in _session.Match.Players)
            {
                if (!player.IsBot)
                {
                    continue;
                }

                Unit unit = new Unit
                {
                    Index = _units.Count,
                    Id = player.ClientId,
                    Name = player.DisplayName,
                    Team = player.Team,
                    Health = BodyHealth
                };

                unit.Body = BuildBody(unit);
                if (unit.Body == null)
                {
                    continue;
                }

                _units.Add(unit);
                _unitIds.Add(unit.Id);
                _byBody[unit.Body.transform] = unit;
            }

            // Agents are configured only after every body exists, otherwise the first
            // bot would start with an empty target list and never join the fight.
            foreach (Unit unit in _units)
            {
                ConfigureAgent(unit);
            }

            if (_units.Count > 0)
            {
                _session.Hud?.Toast("round " + snapshot.RoundNumber + " - " +
                                    (_session.LocalTeam == Team.Attackers
                                        ? "attack the sites"
                                        : "hold the sites"));
            }
        }

        /// <summary>
        /// The freeze phase is buy time: nobody walks or shoots. Round over is the same,
        /// so a decided round does not keep costing kills while the player reads the HUD.
        /// </summary>
        private void SetAgentsActive(bool active)
        {
            foreach (Unit unit in _units)
            {
                if (unit.Agent != null && unit.Body != null && !unit.Dead)
                {
                    unit.Agent.enabled = active;
                }
            }
        }

        private void ClearBodies()
        {
            foreach (Unit unit in _units)
            {
                if (unit.Body != null)
                {
                    Destroy(unit.Body);
                }
            }

            _units.Clear();
            _unitIds.Clear();
            _byBody.Clear();
        }

        private GameObject BuildBody(Unit unit)
        {
            // Both sides use the authored operator body; the defender kit is the militia
            // uniform, so a squad reads as a squad from across the yard.
            string resource = unit.Team == Team.Attackers ? "operator" : "militia";
            GameObject prefab = Resources.Load<GameObject>(
                RemoteOperatorView.OperatorResourceRoot + resource);

            GameObject body;
            if (prefab != null)
            {
                body = Instantiate(prefab, transform, false);
                body.name = "DemolitionBot_" + unit.Name;
                SurfaceMaterialLibrary.Apply(body);
            }
            else
            {
                // No art in Resources (a stripped test build): a capsule keeps the mode
                // playable and is reported as a missing asset, not passed off as a model.
                body = GameObject.CreatePrimitive(PrimitiveType.Capsule);
                body.name = "DemolitionBot_" + unit.Name;
                body.transform.SetParent(transform, false);
                Debug.LogWarning("[kordon] operator prefab missing; demolition bot body is a capsule");
            }

            CapsuleCollider hit = body.GetComponent<CapsuleCollider>();
            if (hit == null)
            {
                hit = body.AddComponent<CapsuleCollider>();
            }

            hit.height = 1.74f;
            hit.radius = 0.30f;
            hit.center = new Vector3(0f, 0.87f, 0f);

            RemoteTargetMarker marker = body.AddComponent<RemoteTargetMarker>();
            marker.ClientId = unit.Id;

            HostileAgent agent = body.AddComponent<HostileAgent>();
            agent.DisplayName = unit.Name;
            agent.Graph = _session.Map.Nav;
            agent.Random = new DeterministicRandom(0xB07UL + unit.Id);
            agent.SightRangeM = 52f;
            agent.FieldOfViewDeg = 126f;
            agent.Accuracy01 = unit.Team == Team.Attackers ? 0.46f : 0.52f;
            agent.Damage = unit.Team == Team.Attackers ? 19f : 16f;
            agent.FireIntervalSeconds = unit.Team == Team.Attackers ? 0.24f : 0.3f;
            agent.DamageApplier = (target, result) => OnBotRound(unit, target, result);
            unit.Agent = agent;

            PlaceAtSpawn(unit);
            return body;
        }

        private void PlaceAtSpawn(Unit unit)
        {
            Transform spawn = SpawnFor(unit);
            if (spawn == null)
            {
                return;
            }

            // A little spread so four bodies do not stand inside each other at the same
            // authored point: the spawn ids are per-player, but the fallback is not.
            int index = Mathf.Max(0, unit.Index);
            Vector3 offset = new Vector3((index % 3 - 1) * 1.1f, 0f, (index / 3) * 1.1f);
            unit.Body.transform.position = spawn.position + spawn.rotation * offset;
            unit.Body.transform.rotation = spawn.rotation;
        }

        private Transform SpawnFor(Unit unit)
        {
            IReadOnlyList<string> ids = _session.MapData.SpawnsFor(unit.Team);
            int index = Mathf.Max(0, unit.Index);
            if (ids.Count > 0)
            {
                string wanted = "Spawn_" + ids[index % ids.Count];
                foreach (Transform point in _session.Map.SpawnPoints)
                {
                    if (point != null && point.name == wanted)
                    {
                        return point;
                    }
                }
            }

            return _session.Map.SpawnPoints.Count > 0
                ? _session.Map.SpawnPoints[index % _session.Map.SpawnPoints.Count]
                : null;
        }

        private void ConfigureAgent(Unit unit)
        {
            if (unit.Agent == null || unit.Body == null)
            {
                return;
            }

            List<Transform> enemies = new List<Transform>();
            foreach (Unit other in _units)
            {
                if (other != unit && other.Team != unit.Team && other.Body != null)
                {
                    enemies.Add(other.Body.transform);
                }
            }

            if (_session.Player != null && _session.LocalTeam != unit.Team)
            {
                enemies.Add(_session.Player.transform);
            }

            unit.Agent.Targets = enemies.ToArray();

            // Attackers push a site, defenders sit on one; both keep fighting anyone they
            // meet on the way, so the round has a shape instead of a random walk.
            BombSiteVolume site = PickSite(unit.Team);
            unit.Agent.PatrolAnchor = new Vector3(site.CentreX, 0f, site.CentreY);
            unit.Agent.Configure(_session.Map.Nav, unit.Agent.Targets, unit.Agent.Random,
                _session.Map.Nav != null
                    ? _session.Map.Nav.NearestNode(MapSpace.ToCore(unit.Body.transform.position))
                    : 0);
            PlaceAtSpawn(unit);
        }

        private BombSiteVolume PickSite(Team team)
        {
            IReadOnlyList<BombSiteVolume> sites = _session.MapData.Sites;
            if (sites.Count == 0)
            {
                return new BombSiteVolume { Id = "A", CentreX = 0f, CentreY = 0f };
            }

            int index = team == Team.Attackers ? 0 : 1;
            return sites[index % sites.Count];
        }

        // ------------------------------------------------------------------
        // Damage both ways
        // ------------------------------------------------------------------
        private bool OnBotRound(Unit shooter, Transform target, DamageResult result)
        {
            if (shooter == null || shooter.Dead || _session == null)
            {
                return false;
            }

            if (_session.Player != null && target == _session.Player.transform)
            {
                return _session.ReceiveBotHit(shooter.Id, shooter.Name, result);
            }

            if (target == null || !_byBody.TryGetValue(target, out Unit victim) || victim.Dead)
            {
                return false;
            }

            victim.Health -= result.Damage;
            if (victim.Health <= 0f)
            {
                KillUnit(victim, shooter.Id, shooter.Name);
            }

            return true;
        }

        private void KillUnit(Unit victim, ulong killerId, string killerName)
        {
            if (victim.Dead)
            {
                return;
            }

            victim.Dead = true;
            victim.Health = 0f;

            // The agent's own death path plays the ragdoll and disables the collider; the
            // controller is told separately, because it owns score, economy and aliveness.
            victim.Agent?.ApplyDamage(new DamageResult
            {
                Accepted = true,
                Damage = 1000f,
                Zone = HitZone.Thorax,
                Fatal = true
            });

            if (victim.Body != null)
            {
                _byBody.Remove(victim.Body.transform);
            }

            _session.Match.RegisterKill(killerId, victim.Id, _session.Now);
            _session.Hud?.Toast(killerName + "  >  " + victim.Name);
        }

        /// <summary>The local player's shot, resolved against the bot bodies.</summary>
        private void OnLocalShot(ShotInfo shot)
        {
            if (_session == null || _session.Match == null || _session.Player.IsDead)
            {
                return;
            }

            if (!Physics.Raycast(shot.Origin, shot.Direction, out RaycastHit hit, 400f,
                    ~0, QueryTriggerInteraction.Ignore))
            {
                return;
            }

            Unit victim = UnitAt(hit.collider);
            if (victim == null || victim.Dead)
            {
                return;
            }

            DefinitionLibrary definitions = _session.Database.Definitions;
            WeaponDefinition weapon = definitions.GetWeapon(shot.WeaponId);
            BulletDefinition bullet = definitions.GetBullet(shot.BulletId)
                                      ?? (weapon != null
                                          ? definitions.GetBullet(weapon.DefaultBulletId)
                                          : null);
            if (bullet == null)
            {
                return;
            }

            DamageRequest request = DamageRequest.Direct(bullet, ZoneOf(victim, hit.point),
                new ArmorState(null), Vector3.Distance(shot.Origin, hit.point), _random);
            DamageResult result = new DamageModel().Resolve(request);
            if (!result.Accepted)
            {
                return;
            }

            victim.Health -= result.Damage;
            _session.Hud?.HitMarker();

            if (victim.Health <= 0f)
            {
                KillUnit(victim, DemolitionMatch.LocalClientId, "you");
                _session.Hud?.Toast("eliminated " + victim.Name);
            }
        }

        private Unit UnitAt(Collider collider)
        {
            if (collider == null)
            {
                return null;
            }

            Transform body = collider.transform;
            while (body != null && !_byBody.ContainsKey(body))
            {
                body = body.parent;
            }

            return body != null ? _byBody[body] : null;
        }

        private static HitZone ZoneOf(Unit victim, Vector3 point)
        {
            float height = point.y - victim.Body.transform.position.y;
            return height >= 1.52f ? HitZone.Head
                : height >= 0.95f ? HitZone.Thorax
                : height >= 0.45f ? HitZone.Abdomen
                : HitZone.RightLeg;
        }

        // ------------------------------------------------------------------
        // Objectives
        // ------------------------------------------------------------------
        /// <summary>
        /// Bots take the objective too: an attacker that stands on a site arms the charge,
        /// a defender that reaches an armed charge cuts it. Without this the round would
        /// only ever end on a wipe or the clock.
        /// </summary>
        private void UpdateObjectives(BombRoundSnapshot snapshot)
        {
            float now = _session.Now;
            foreach (Unit unit in _units)
            {
                if (unit.Dead || unit.Body == null || unit.Agent == null || unit.Agent.IsDead)
                {
                    continue;
                }

                Vector3 position = unit.Body.transform.position;

                if (unit.Team == Team.Attackers && snapshot.Phase == RoundPhase.Live)
                {
                    string site = BombMatchController.SiteAt(_session.MapData.Sites,
                        position.x, position.z);
                    if (site == null)
                    {
                        if (unit.PlantSite != null)
                        {
                            _session.Match.CancelPlant(unit.Id);
                            unit.PlantSite = null;
                        }

                        continue;
                    }

                    if (unit.PlantSite != site)
                    {
                        unit.PlantSite = site;
                        _session.Match.BeginPlant(unit.Id, site, true, now);
                    }

                    if (_session.Match.CompletePlant(unit.Id, site, now) != null)
                    {
                        _session.Hud?.Toast(unit.Name + " armed the charge");
                        unit.PlantSite = null;
                    }
                }
                else if (unit.Team == Team.Defenders && snapshot.Phase == RoundPhase.Planted &&
                         NearCharge(position))
                {
                    if (!unit.Defusing)
                    {
                        unit.Defusing = true;
                        _session.Match.BeginDefuse(unit.Id, true, now);
                    }

                    if (_session.Match.CompleteDefuse(unit.Id, now))
                    {
                        _session.Hud?.Toast(unit.Name + " cut the charge");
                        unit.Defusing = false;
                    }
                }
                else if (unit.Defusing)
                {
                    _session.Match.CancelDefuse(unit.Id);
                    unit.Defusing = false;
                }
            }
        }

        private bool NearCharge(Vector3 position)
        {
            if (_session.Match.ArmedSite == null)
            {
                return false;
            }

            BombSiteVolume? site = _session.MapData.Site(_session.Match.ArmedSite);
            if (!site.HasValue)
            {
                return false;
            }

            float dx = position.x - site.Value.CentreX;
            float dz = position.z - site.Value.CentreY;
            return dx * dx + dz * dz <= PlantStandOffM * PlantStandOffM;
        }
    }
}
