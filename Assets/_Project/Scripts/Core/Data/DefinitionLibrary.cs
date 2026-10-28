using System;
using System.Collections.Generic;
using System.IO;
using Kordon.Core.Ballistics;
using Kordon.Core.Combat;
using Kordon.Core.Loadout;
using Kordon.Core.Serialization;

namespace Kordon.Core.Data
{
    /// <summary>
    /// Loads and indexes every data driven definition of the game
    /// (weapons, magazines, ammunition, armour kits, grenades, materials) from the
    /// JSON files under <c>Assets/_Project/Data/Definitions</c>.
    ///
    /// The JSON files are the single source of truth: the same files are read by the
    /// Unity runtime (from StreamingAssets), by the dedicated server and by the
    /// out-of-editor test harness.
    /// </summary>
    public sealed class DefinitionLibrary
    {
        private readonly Dictionary<string, WeaponDefinition> _weapons =
            new Dictionary<string, WeaponDefinition>(StringComparer.Ordinal);
        private readonly Dictionary<string, MagazineDefinition> _magazines =
            new Dictionary<string, MagazineDefinition>(StringComparer.Ordinal);
        private readonly Dictionary<string, BulletDefinition> _bullets =
            new Dictionary<string, BulletDefinition>(StringComparer.Ordinal);
        private readonly Dictionary<string, ArmorKitDefinition> _armorKits =
            new Dictionary<string, ArmorKitDefinition>(StringComparer.Ordinal);
        private readonly Dictionary<string, GrenadeDefinition> _grenades =
            new Dictionary<string, GrenadeDefinition>(StringComparer.Ordinal);
        private readonly Dictionary<MaterialKind, MaterialDefinition> _materials =
            new Dictionary<MaterialKind, MaterialDefinition>();
        private readonly Dictionary<string, MaterialDefinition> _materialsById =
            new Dictionary<string, MaterialDefinition>(StringComparer.Ordinal);

        public IReadOnlyDictionary<string, WeaponDefinition> Weapons => _weapons;
        public IReadOnlyDictionary<string, MagazineDefinition> Magazines => _magazines;
        public IReadOnlyDictionary<string, BulletDefinition> Bullets => _bullets;
        public IReadOnlyDictionary<string, ArmorKitDefinition> ArmorKits => _armorKits;
        public IReadOnlyDictionary<string, GrenadeDefinition> Grenades => _grenades;

        public WeaponDefinition GetWeapon(string id) =>
            id != null && _weapons.TryGetValue(id, out WeaponDefinition w) ? w : null;

        public MagazineDefinition GetMagazine(string id) =>
            id != null && _magazines.TryGetValue(id, out MagazineDefinition m) ? m : null;

        public BulletDefinition GetBullet(string id) =>
            id != null && _bullets.TryGetValue(id, out BulletDefinition b) ? b : null;

        public ArmorKitDefinition GetArmorKit(string id) =>
            id != null && _armorKits.TryGetValue(id, out ArmorKitDefinition a) ? a : null;

        public GrenadeDefinition GetGrenade(string id) =>
            id != null && _grenades.TryGetValue(id, out GrenadeDefinition g) ? g : null;

        public MaterialDefinition GetMaterial(MaterialKind kind) =>
            _materials.TryGetValue(kind, out MaterialDefinition m) ? m : MaterialDefinition.Default(kind);

        public MaterialDefinition GetMaterialById(string id) =>
            id != null && _materialsById.TryGetValue(id, out MaterialDefinition m) ? m : null;

        /// <summary>Bullet actually fired by a weapon with a given magazine planned in.</summary>
        public BulletDefinition ResolveBullet(WeaponDefinition weapon, MagazineDefinition magazine)
        {
            if (magazine != null)
            {
                BulletDefinition bullet = GetBullet(magazine.AmmoVariantId);
                if (bullet != null)
                {
                    return bullet;
                }
            }

            return weapon != null ? GetBullet(weapon.DefaultBulletId) : null;
        }

        public List<MagazineDefinition> MagazinesForWeapon(WeaponDefinition weapon)
        {
            List<MagazineDefinition> result = new List<MagazineDefinition>();
            if (weapon == null)
            {
                return result;
            }

            foreach (MagazineDefinition magazine in _magazines.Values)
            {
                if (magazine.IsCompatibleWith(weapon))
                {
                    result.Add(magazine);
                }
            }

            result.Sort((a, b) => string.CompareOrdinal(a.Id, b.Id));
            return result;
        }

        public List<WeaponDefinition> WeaponsOfClass(WeaponClass weaponClass)
        {
            List<WeaponDefinition> result = new List<WeaponDefinition>();
            foreach (WeaponDefinition weapon in _weapons.Values)
            {
                if (weapon.Class == weaponClass)
                {
                    result.Add(weapon);
                }
            }

            result.Sort((a, b) => string.CompareOrdinal(a.Id, b.Id));
            return result;
        }

        // ------------------------------------------------------------ loading

        /// <summary>Load every definition file from a directory (works in editor, server and CI).</summary>
        public static DefinitionLibrary FromDirectory(string directory)
        {
            if (!Directory.Exists(directory))
            {
                throw new DirectoryNotFoundException($"definition directory not found: {directory}");
            }

            DefinitionLibrary library = new DefinitionLibrary();
            library.AddBulletsJson(ReadFile(Path.Combine(directory, "bullets.json")));
            library.AddMaterialsJson(ReadFile(Path.Combine(directory, "materials.json")));
            library.AddMagazinesJson(ReadFile(Path.Combine(directory, "magazines.json")));
            library.AddWeaponsJson(ReadFile(Path.Combine(directory, "weapons.json")));
            library.AddArmorKitsJson(ReadFile(Path.Combine(directory, "armor_kits.json")));
            library.AddGrenadesJson(ReadFile(Path.Combine(directory, "grenades.json")));
            return library;
        }

        private static string ReadFile(string path) => File.Exists(path) ? File.ReadAllText(path) : null;

        public void AddWeaponsJson(string json)
        {
            if (string.IsNullOrEmpty(json))
            {
                return;
            }

            Json root = Json.Parse(json);
            Json items = root.Has("weapons") ? root["weapons"] : root;
            foreach (Json item in items.Items())
            {
                WeaponDefinition weapon = ParseWeapon(item);
                _weapons[weapon.Id] = weapon;
            }
        }

        public void AddMagazinesJson(string json)
        {
            if (string.IsNullOrEmpty(json))
            {
                return;
            }

            Json root = Json.Parse(json);
            Json items = root.Has("magazines") ? root["magazines"] : root;
            foreach (Json item in items.Items())
            {
                MagazineDefinition magazine = ParseMagazine(item);
                _magazines[magazine.Id] = magazine;
            }
        }

        public void AddBulletsJson(string json)
        {
            if (string.IsNullOrEmpty(json))
            {
                return;
            }

            Json root = Json.Parse(json);
            Json items = root.Has("bullets") ? root["bullets"] : root;
            foreach (Json item in items.Items())
            {
                BulletDefinition bullet = new BulletDefinition
                {
                    Id = item["id"].AsString(),
                    DisplayName = item["displayName"].AsString(item["id"].AsString()),
                    Caliber = item["caliber"].AsString(),
                    BaseDamage = item["baseDamage"].AsFloat(30f),
                    PenetrationValue = item["penetrationValue"].AsFloat(20f),
                    MuzzleVelocity = item["muzzleVelocity"].AsFloat(800f),
                    MassGrams = item["massGrams"].AsFloat(4f),
                    OptimalRangeM = item["optimalRangeM"].AsFloat(35f),
                    FalloffStartM = item["falloffStartM"].AsFloat(60f),
                    FalloffEndM = item["falloffEndM"].AsFloat(180f),
                    MinDamageMultiplier = item["minDamageMultiplier"].AsFloat(0.45f),
                    FragmentationChance = item["fragmentationChance"].AsFloat(0.1f),
                    FragmentationDamageMultiplier = item["fragmentationDamageMultiplier"].AsFloat(1.3f),
                    IsProjectile = item["isProjectile"].AsBool(false),
                    ProjectileSpeed = item["projectileSpeed"].AsFloat(0f),
                    ProjectileGravity = item["projectileGravity"].AsFloat(0f),
                    BleedChance = item["bleedChance"].AsFloat(0.25f)
                };
                _bullets[bullet.Id] = bullet;
            }
        }

        public void AddArmorKitsJson(string json)
        {
            if (string.IsNullOrEmpty(json))
            {
                return;
            }

            Json root = Json.Parse(json);
            Json items = root.Has("armorKits") ? root["armorKits"] : root;
            foreach (Json item in items.Items())
            {
                ArmorKitDefinition kit = new ArmorKitDefinition
                {
                    Id = item["id"].AsString(),
                    DisplayName = item["displayName"].AsString(),
                    WeightKg = item["weightKg"].AsFloat(5f),
                    MobilityMultiplier = item["mobilityMultiplier"].AsFloat(1f),
                    NoiseMultiplier = item["noiseMultiplier"].AsFloat(1f)
                };
                foreach (Json zone in item["zones"].Items())
                {
                    kit.Zones.Add(new ZoneArmor(
                        zone["zone"].AsEnum(HitZone.Thorax),
                        zone["armorClass"].AsInt(0),
                        zone["coverage"].AsFloat(1f),
                        zone["durability"].AsFloat(50f)));
                }

                _armorKits[kit.Id] = kit;
            }
        }

        public void AddGrenadesJson(string json)
        {
            if (string.IsNullOrEmpty(json))
            {
                return;
            }

            Json root = Json.Parse(json);
            Json items = root.Has("grenades") ? root["grenades"] : root;
            foreach (Json item in items.Items())
            {
                GrenadeDefinition grenade = new GrenadeDefinition
                {
                    Id = item["id"].AsString(),
                    DisplayName = item["displayName"].AsString(),
                    Kind = item["kind"].AsEnum(GrenadeKind.Fragmentation),
                    WeightKg = item["weightKg"].AsFloat(0.5f),
                    ThrowSpeed = item["throwSpeed"].AsFloat(13f),
                    FuseSeconds = item["fuseSeconds"].AsFloat(3.5f),
                    CookableMinFuse = item["cookableMinFuse"].AsFloat(0.8f),
                    DamageAtCenter = item["damageAtCenter"].AsFloat(100f),
                    LethalRadiusM = item["lethalRadiusM"].AsFloat(4f),
                    DamageRadiusM = item["damageRadiusM"].AsFloat(12f),
                    SuppressionRadiusM = item["suppressionRadiusM"].AsFloat(20f),
                    LineOfSightRequired = item["lineOfSightRequired"].AsBool(true),
                    EffectRadiusM = item["effectRadiusM"].AsFloat(6f),
                    EffectDurationSeconds = item["effectDurationSeconds"].AsFloat(20f),
                    AllowCooking = item["allowCooking"].AsBool(true)
                };
                _grenades[grenade.Id] = grenade;
            }
        }

        public void AddMaterialsJson(string json)
        {
            if (string.IsNullOrEmpty(json))
            {
                return;
            }

            Json root = Json.Parse(json);
            Json items = root.Has("materials") ? root["materials"] : root;
            foreach (Json item in items.Items())
            {
                MaterialKind kind = item["kind"].AsEnum(MaterialKind.Concrete);
                MaterialDefinition material = MaterialDefinition.Default(kind);
                material.Id = item["id"].AsString(kind.ToString().ToLowerInvariant());
                material.DisplayName = item["displayName"].AsString(material.Id);
                material.Kind = kind;
                material.PenetrationResistance = item["penetrationResistance"].AsFloat(material.PenetrationResistance);
                material.ReferenceThicknessMm = item["referenceThicknessMm"].AsFloat(material.ReferenceThicknessMm);
                material.BaseRicochetChance = item["baseRicochetChance"].AsFloat(material.BaseRicochetChance);
                material.RicochetAngleThresholdDeg =
                    item["ricochetAngleThresholdDeg"].AsFloat(material.RicochetAngleThresholdDeg);
                material.ResidualDamageFactor = item["residualDamageFactor"].AsFloat(material.ResidualDamageFactor);
                material.Penetrable = item["penetrable"].AsBool(material.Penetrable);
                material.Destructible = item["destructible"].AsBool(material.Destructible);
                material.DestructibleIntegrity = item["destructibleIntegrity"].AsFloat(material.DestructibleIntegrity);
                material.AudioSurfaceId = item["audioSurfaceId"].AsString(material.AudioSurfaceId);
                material.NoiseMultiplier = item["noiseMultiplier"].AsFloat(material.NoiseMultiplier);
                _materials[kind] = material;
                _materialsById[material.Id] = material;
            }
        }

        private static WeaponDefinition ParseWeapon(Json item)
        {
            WeaponDefinition weapon = new WeaponDefinition
            {
                Id = item["id"].AsString(),
                DisplayName = item["displayName"].AsString(),
                Manufacturer = item["manufacturer"].AsString(string.Empty),
                Description = item["description"].AsString(string.Empty),
                Class = item["class"].AsEnum(WeaponClass.AssaultRifle),
                Caliber = item["caliber"].AsString(),
                DefaultBulletId = item["defaultBulletId"].AsString(),
                DefaultFireMode = item["defaultFireMode"].AsEnum(FireMode.Semi),
                BurstCount = item["burstCount"].AsInt(3),
                RoundsPerMinute = item["roundsPerMinute"].AsFloat(600f),
                PelletsPerShot = item["pelletsPerShot"].AsInt(1),
                WeightKg = item["weightKg"].AsFloat(3.5f),
                MobilityMultiplier = item["mobilityMultiplier"].AsFloat(1f),
                AimDownSightSeconds = item["aimDownSightSeconds"].AsFloat(0.3f),
                AimOutSeconds = item["aimOutSeconds"].AsFloat(0.25f),
                SprintToFireSeconds = item["sprintToFireSeconds"].AsFloat(0.4f),
                ReloadTacticalSeconds = item["reloadTacticalSeconds"].AsFloat(2.3f),
                ReloadDrySeconds = item["reloadDrySeconds"].AsFloat(2.9f),
                ChargeSeconds = item["chargeSeconds"].AsFloat(0.5f),
                InspectSeconds = item["inspectSeconds"].AsFloat(1.1f),
                InspectReturnSeconds = item["inspectReturnSeconds"].AsFloat(0.85f),
                MaxEffectiveRangeM = item["maxEffectiveRangeM"].AsFloat(100f),
                MuzzleNoiseDb = item["muzzleNoiseDb"].AsFloat(155f),
                CanBeSuppressed = item["canBeSuppressed"].AsBool(true),
                SwayMultiplier = item["swayMultiplier"].AsFloat(1f),
                DefaultSight = item["defaultSight"].AsEnum(SightKind.IronSight)
            };

            // The JSON is authoritative: without clearing, a bolt action rifle would
            // inherit the default "Semi + Auto" fire modes and iron sights it does not have.
            Json modes = item["fireModes"];
            if (!modes.IsNull)
            {
                weapon.FireModes.Clear();
                foreach (Json mode in modes.Items())
                {
                    weapon.FireModes.Add(mode.AsEnum(FireMode.Semi));
                }
            }

            Json sights = item["availableSights"];
            if (!sights.IsNull)
            {
                weapon.AvailableSights.Clear();
                foreach (Json sight in sights.Items())
                {
                    weapon.AvailableSights.Add(sight.AsEnum(SightKind.IronSight));
                }
            }

            Json recoil = item["recoil"];
            if (!recoil.IsNull)
            {
                weapon.Recoil = new RecoilProfile
                {
                    VerticalBaseDeg = recoil["verticalBaseDeg"].AsFloat(1.1f),
                    HorizontalBaseDeg = recoil["horizontalBaseDeg"].AsFloat(0.3f),
                    VerticalVariance = recoil["verticalVariance"].AsFloat(0.2f),
                    HorizontalVariance = recoil["horizontalVariance"].AsFloat(0.6f),
                    ClimbRampShots = recoil["climbRampShots"].AsFloat(6f),
                    PlateauMultiplier = recoil["plateauMultiplier"].AsFloat(1.5f),
                    FirstShotMultiplier = recoil["firstShotMultiplier"].AsFloat(0.6f),
                    RecoveryDelaySec = recoil["recoveryDelaySec"].AsFloat(0.09f),
                    RecoveryTimeSec = recoil["recoveryTimeSec"].AsFloat(0.22f),
                    AimingVerticalMultiplier = recoil["aimingVerticalMultiplier"].AsFloat(0.6f),
                    AimingHorizontalMultiplier = recoil["aimingHorizontalMultiplier"].AsFloat(0.7f),
                    CrouchMultiplier = recoil["crouchMultiplier"].AsFloat(0.8f),
                    ProneMultiplier = recoil["proneMultiplier"].AsFloat(0.55f),
                    MovementMultiplierAtWalk = recoil["movementMultiplierAtWalk"].AsFloat(1.35f),
                    MovementMultiplierAtSprint = recoil["movementMultiplierAtSprint"].AsFloat(2.1f),
                    AirborneMultiplier = recoil["airborneMultiplier"].AsFloat(3.1f),
                    LandingRecoverySeconds = recoil["landingRecoverySeconds"].AsFloat(0.45f),
                    LandingPenaltyMultiplier = recoil["landingPenaltyMultiplier"].AsFloat(2.2f),
                    LeanMultiplier = recoil["leanMultiplier"].AsFloat(1.18f),
                    StaminaExhaustedMultiplier = recoil["staminaExhaustedMultiplier"].AsFloat(1.45f),
                    SuppressionMultiplier = recoil["suppressionMultiplier"].AsFloat(1.5f),
                    VisualKick = recoil["visualKick"].AsFloat(1f)
                };
            }

            Json spread = item["spread"];
            if (!spread.IsNull)
            {
                weapon.Spread = new SpreadProfile
                {
                    AimingBaseDeg = spread["aimingBaseDeg"].AsFloat(0.08f),
                    HipBaseDeg = spread["hipBaseDeg"].AsFloat(2.6f),
                    MovementAddDegPerMps = spread["movementAddDegPerMps"].AsFloat(0.42f),
                    SprintAddDeg = spread["sprintAddDeg"].AsFloat(3.4f),
                    CrouchMultiplier = spread["crouchMultiplier"].AsFloat(0.8f),
                    ProneMultiplier = spread["proneMultiplier"].AsFloat(0.55f),
                    AirborneMultiplier = spread["airborneMultiplier"].AsFloat(3.2f),
                    LeanAddDeg = spread["leanAddDeg"].AsFloat(0.55f),
                    StaminaExhaustedAddDeg = spread["staminaExhaustedAddDeg"].AsFloat(0.85f),
                    SuppressionAddDeg = spread["suppressionAddDeg"].AsFloat(1.6f),
                    BloomPerShotDeg = spread["bloomPerShotDeg"].AsFloat(0.22f),
                    BloomMaxDeg = spread["bloomMaxDeg"].AsFloat(3.2f),
                    BloomRecoveryDegPerSecond = spread["bloomRecoveryDegPerSecond"].AsFloat(4.5f),
                    AdsSettleTimeSec = spread["adsSettleTimeSec"].AsFloat(0.22f)
                };
            }

            return weapon;
        }

        private static MagazineDefinition ParseMagazine(Json item)
        {
            MagazineDefinition magazine = new MagazineDefinition
            {
                Id = item["id"].AsString(),
                DisplayName = item["displayName"].AsString(),
                Caliber = item["caliber"].AsString(),
                AmmoVariantId = item["ammoVariantId"].AsString(),
                Capacity = item["capacity"].AsInt(30),
                Kind = item["kind"].AsEnum(MagazineKind.Box),
                LoadedWeightKg = item["loadedWeightKg"].AsFloat(0.7f),
                EmptyWeightKg = item["emptyWeightKg"].AsFloat(0.25f),
                ReloadTimeMultiplier = item["reloadTimeMultiplier"].AsFloat(1f),
                HandlingPenalty = item["handlingPenalty"].AsFloat(0f),
                FumbleChance = item["fumbleChance"].AsFloat(0.01f)
            };

            foreach (Json allowed in item["allowedClasses"].Items())
            {
                magazine.AllowedClasses.Add(allowed.AsEnum(WeaponClass.AssaultRifle));
            }

            foreach (Json allowed in item["allowedWeaponIds"].Items())
            {
                magazine.AllowedWeaponIds.Add(allowed.AsString());
            }

            return magazine;
        }

        /// <summary>
        /// Cross reference check used by CI and by the editor menu
        /// (Tools &gt; Kordon &gt; Validate definitions).  Returns a list of problems;
        /// an empty list means the shipped content is self consistent.
        /// </summary>
        public List<string> ValidateIntegrity()
        {
            List<string> errors = new List<string>();
            if (_weapons.Count == 0)
            {
                errors.Add("no-weapons-defined");
            }

            if (_magazines.Count == 0)
            {
                errors.Add("no-magazines-defined");
            }

            foreach (WeaponDefinition weapon in _weapons.Values)
            {
                if (string.IsNullOrEmpty(weapon.Id))
                {
                    errors.Add("weapon-without-id");
                    continue;
                }

                if (weapon.RoundsPerMinute <= 0f)
                {
                    errors.Add($"{weapon.Id}: non-positive rate of fire");
                }

                if (weapon.AvailableSights.Count == 0)
                {
                    errors.Add($"{weapon.Id}: no sights available");
                }

                if (!weapon.AvailableSights.Contains(weapon.DefaultSight))
                {
                    errors.Add($"{weapon.Id}: default sight not in available sights");
                }

                if (weapon.IsHitscan)
                {
                    if (GetBullet(weapon.DefaultBulletId) == null)
                    {
                        errors.Add($"{weapon.Id}: unknown default bullet '{weapon.DefaultBulletId}'");
                    }

                    if (MagazinesForWeapon(weapon).Count == 0)
                    {
                        errors.Add($"{weapon.Id}: no compatible magazine in the library");
                    }
                }
                else if (weapon.IsThrownProjectile && !_grenades.ContainsKey(weapon.Id) &&
                         string.IsNullOrEmpty(weapon.DefaultBulletId))
                {
                    errors.Add($"{weapon.Id}: grenade class without grenade definition");
                }
            }

            foreach (MagazineDefinition magazine in _magazines.Values)
            {
                if (magazine.Capacity <= 0)
                {
                    errors.Add($"{magazine.Id}: non-positive capacity");
                }

                if (GetBullet(magazine.AmmoVariantId) == null)
                {
                    errors.Add($"{magazine.Id}: unknown ammo variant '{magazine.AmmoVariantId}'");
                }

                if (magazine.LoadedWeightKg < magazine.EmptyWeightKg)
                {
                    errors.Add($"{magazine.Id}: loaded weight below empty weight");
                }
            }

            foreach (ArmorKitDefinition kit in _armorKits.Values)
            {
                if (kit.Zones.Count == 0)
                {
                    errors.Add($"{kit.Id}: armour kit without zones");
                }

                foreach (ZoneArmor zone in kit.Zones)
                {
                    if (zone.ArmorClass < 0 || zone.ArmorClass >= PenetrationSolver.ArmorClassThresholds.Length)
                    {
                        errors.Add($"{kit.Id}: armour class out of range for zone {zone.Zone}");
                    }
                }
            }

            return errors;
        }
    }
}
