using System.Collections.Generic;
using UnityEngine;

namespace Skyforge.Core
{
    /// <summary>What a kind of target can take and what it does to the drone.</summary>
    public sealed class TargetKind
    {
        public string Id;
        public float Health;
        /// <summary>Footprint radius: a munition landing inside it does full damage.</summary>
        public float Radius = 4f;
        public float Height = 3f;
        /// <summary>The drone is shot at inside this distance; zero means unarmed.</summary>
        public float GunRange;
        /// <summary>Share of the hull lost per second under this target's fire.</summary>
        public float GunDamage;
        /// <summary>A missile battery: it only engages a drone flying higher than this above ground.</summary>
        public float MinAltitude;
        /// <summary>Radius of the explosion this target sets off when it dies; zero for none.</summary>
        public float Blast;
        /// <summary>Concrete and steel plate: rockets do little, bombs are the answer.</summary>
        public bool Structure;

        /// <summary>Flies after the drone instead of following a road: an enemy drone or fighter.</summary>
        public bool Pursues;
        public float PursuitSpeed;
        /// <summary>A soft vehicle that drives off when shells start landing near it.</summary>
        public bool Scatters;
        /// <summary>An emplacement that moves to a new position once it has been hit.</summary>
        public bool Relocates;
        /// <summary>Infantry: under fire it goes to ground and is hard to hurt for a while.</summary>
        public bool TakesCover;
        /// <summary>On the player's side and armed: damage per second it does to the nearest hostile in range.</summary>
        public float AllyGun;
        public float AllyRange = 90f;

        public string NameKey => "target." + Id;
        public bool Armed => GunRange > 0f;
    }

    public static class TargetKinds
    {
        static readonly Dictionary<string, TargetKind> Map = Build();

        public static IEnumerable<TargetKind> All => Map.Values;

        public static TargetKind Get(string id) => id != null && Map.TryGetValue(id, out TargetKind kind) ? kind : null;

        static Dictionary<string, TargetKind> Build()
        {
            var list = new[]
            {
                // Soft vehicles.
                new TargetKind { Id = "truck", Health = 60f, Radius = 4f, Scatters = true },
                new TargetKind { Id = "truck_cargo", Health = 60f, Radius = 4.5f, Scatters = true },
                new TargetKind { Id = "truck_army", Health = 70f, Radius = 4.5f, Scatters = true },
                new TargetKind { Id = "truck_fuel", Health = 40f, Radius = 4.5f, Blast = 18f },
                new TargetKind { Id = "staff_car", Health = 60f, Radius = 3f },
                new TargetKind { Id = "jeep", Health = 40f, Radius = 3f, GunRange = 70f, GunDamage = 0.035f },
                new TargetKind { Id = "humvee", Health = 60f, Radius = 3.2f, GunRange = 80f, GunDamage = 0.045f },
                // Armour.
                new TargetKind { Id = "apc", Health = 110f, Radius = 4f, GunRange = 90f, GunDamage = 0.055f },
                new TargetKind { Id = "tank_light", Health = 120f, Radius = 4f, GunRange = 70f, GunDamage = 0.04f },
                new TargetKind { Id = "tank", Health = 170f, Radius = 4.5f, GunRange = 75f, GunDamage = 0.05f },
                new TargetKind { Id = "tank_heavy", Health = 240f, Radius = 5f, GunRange = 80f, GunDamage = 0.06f },
                // Air defence.
                new TargetKind { Id = "aagun", Health = 90f, Radius = 3.5f, GunRange = 125f, GunDamage = 0.10f, Relocates = true },
                new TargetKind { Id = "gatling", Health = 80f, Radius = 3.2f, GunRange = 105f, GunDamage = 0.13f },
                new TargetKind { Id = "sam", Health = 90f, Radius = 3.5f, GunRange = 240f, GunDamage = 0.16f, MinAltitude = 40f },
                new TargetKind { Id = "radar", Health = 70f, Radius = 3f, Height = 8f },
                // Aircraft and boats.
                new TargetKind { Id = "heli", Health = 80f, Radius = 6f },
                new TargetKind { Id = "heli_transport", Health = 70f, Radius = 5.5f },
                new TargetKind { Id = "gunship", Health = 100f, Radius = 6f, GunRange = 110f, GunDamage = 0.08f },
                new TargetKind { Id = "boat", Health = 70f, Radius = 4.5f, GunRange = 75f, GunDamage = 0.045f },
                new TargetKind { Id = "warship", Health = 420f, Radius = 14f, Height = 10f, GunRange = 110f, GunDamage = 0.045f, Structure = true },
                // People and positions.
                new TargetKind { Id = "squad", Health = 30f, Radius = 5f, Height = 2f, GunRange = 55f, GunDamage = 0.03f, TakesCover = true },
                new TargetKind { Id = "artillery", Health = 100f, Radius = 4f },
                new TargetKind { Id = "bunker", Health = 220f, Radius = 5f, GunRange = 80f, GunDamage = 0.055f, Structure = true },
                new TargetKind { Id = "hq", Health = 320f, Radius = 9f, Height = 8f, Structure = true },
                new TargetKind { Id = "depot", Health = 110f, Radius = 7f, Height = 4f, Blast = 22f },
                new TargetKind { Id = "barrels", Health = 25f, Radius = 3f, Height = 2f, Blast = 13f },
                new TargetKind { Id = "tent", Health = 35f, Radius = 4.5f },
                // What the player protects.
                new TargetKind { Id = "outpost", Health = 300f, Radius = 8f, Height = 6f, Structure = true },
                new TargetKind { Id = "ally_truck", Health = 120f, Radius = 4f },
                new TargetKind { Id = "ally_tank", Health = 280f, Radius = 4.5f, AllyGun = 14f, AllyRange = 85f },
                new TargetKind { Id = "ally_squad", Health = 110f, Radius = 5f, Height = 2f, AllyGun = 6f, AllyRange = 60f },
                new TargetKind { Id = "turret", Health = 220f, Radius = 3.5f, AllyGun = 18f, AllyRange = 110f },
                new TargetKind { Id = "pilot", Health = 70f, Radius = 2.5f, Height = 2f },
                // In the air against the player.
                new TargetKind { Id = "hunter", Health = 30f, Radius = 2.2f, Height = 2f, GunRange = 48f, GunDamage = 0.05f, Pursues = true, PursuitSpeed = 13f },
                new TargetKind { Id = "fighter", Health = 70f, Radius = 4f, Height = 3f, GunRange = 85f, GunDamage = 0.07f, Pursues = true, PursuitSpeed = 21f },
                // Big things that come apart.
                new TargetKind { Id = "train_engine", Health = 260f, Radius = 6f, Height = 4f },
                new TargetKind { Id = "train_car", Health = 140f, Radius = 6f, Height = 4f, Blast = 14f },
                new TargetKind { Id = "train_gun", Health = 150f, Radius = 6f, Height = 5f, GunRange = 95f, GunDamage = 0.07f },
                new TargetKind { Id = "launcher", Health = 210f, Radius = 6f, Height = 5f, Blast = 20f },
                new TargetKind { Id = "ship_turret", Health = 90f, Radius = 3f, Height = 3f, GunRange = 120f, GunDamage = 0.07f },
                new TargetKind { Id = "bridge", Health = 380f, Radius = 10f, Height = 6f, Structure = true },
                new TargetKind { Id = "platform", Health = 520f, Radius = 13f, Height = 16f, Blast = 26f, Structure = true },
                new TargetKind { Id = "dummy", Health = 80f, Radius = 3.5f },
            };
            var map = new Dictionary<string, TargetKind>();
            foreach (TargetKind kind in list) map[kind.Id] = kind;
            return map;
        }
    }

    /// <summary>Something that can be destroyed: where it is, how it moves and whose side it is on.</summary>
    public sealed class TargetDefinition
    {
        public string Id;
        public TargetKind Kind;
        public Vector3 Position;
        /// <summary>Two or more points make it a mover; empty means it stands at <see cref="Position"/>.</summary>
        public List<Vector3> Path = new List<Vector3>();
        public float Speed;
        /// <summary>It waits until this objective is the current one.</summary>
        public int StartObjective;
        /// <summary>Seconds it waits after its cue before setting off; spaces out a column.</summary>
        public float Delay;
        /// <summary>At the end of the path it starts again instead of stopping.</summary>
        public bool Loop;
        /// <summary>Metres above ground, for aircraft in flight.</summary>
        public float Altitude;
        /// <summary>The player's side: losing it fails the mission.</summary>
        public bool Friendly;
        /// <summary>Index of the friendly target this one shoots at when it gets near, or -1.</summary>
        public int Attacks = -1;
        /// <summary>Index of the target this one is mounted on (a turret on a ship), or -1. It rides along at <see cref="Offset"/>.</summary>
        public int Parent = -1;
        public Vector3 Offset;
    }

    public struct TargetState
    {
        public Vector3 Position;
        public Vector3 Forward;
        public float Travelled;
        public bool Arrived;
        public float Health;
        public bool Alive;
        /// <summary>Seconds the drone has been inside this target's sights.</summary>
        public float Aim;
        /// <summary>True on the steps this target is firing, at the drone or at an ally.</summary>
        public bool Firing;
        /// <summary>Seconds it has been in the mission: movers and pursuers set off after their delay.</summary>
        public float Clock;
        /// <summary>Driving off after a near miss: which way, and for how many more seconds.</summary>
        public Vector3 Flee;
        public float FleeLeft;
        /// <summary>Seconds of cover left to infantry that has gone to ground.</summary>
        public float Cover;
        public bool Alarmed;
        /// <summary>Not in the fight yet: a later wave waiting for its cue, kept out of sight and out of reach.</summary>
        public bool Dormant;
    }

    public enum ProjectileType
    {
        Rocket,
        Bomb,
        /// <summary>A missile the player flies by hand through its own camera.</summary>
        Guided,
    }

    /// <summary>What did the damage; kills are counted per weapon for tasks and mastery.</summary>
    public enum Weapon
    {
        None,
        Rocket,
        Bomb,
        Cannon,
        Guided,
        Support,
        Ram,
        Ally,
    }

    public struct Projectile
    {
        public ProjectileType Type;
        public Vector3 Position;
        public Vector3 Direction;
        /// <summary>Rocket: the target it homes on.</summary>
        public int Target;
        public Vector3 From, Impact;
        public float Elapsed, Duration;
        public float Damage;
    }

    public enum CombatEventType
    {
        RocketFired,
        BombDropped,
        /// <summary>A munition or a dying target went off; <see cref="CombatEvent.Size"/> is the blast radius.</summary>
        Explosion,
        TargetHit,
        TargetDestroyed,
        /// <summary>The drone is being shot at by <see cref="CombatEvent.Target"/>.</summary>
        IncomingFire,
        /// <summary>A hostile is shooting at an ally.</summary>
        AllyUnderFire,
        Rearmed,
        /// <summary>A strike drone went off; the next one is waiting on the pad if any are left.</summary>
        Detonated,
        /// <summary>The drone's cannon is firing at <see cref="CombatEvent.Target"/>.</summary>
        CannonFire,
        /// <summary>A friendly gun at Position is firing at <see cref="CombatEvent.Target"/>.</summary>
        AllyFire,
        /// <summary>Support has been called; Size is its tier, Position where it will land.</summary>
        SupportCalled,
        JammerOn,
        FlaresOut,
        GuidedLaunched,
        /// <summary>The aircraft was lost and a spare takes its place on the pad.</summary>
        SpareUsed,
        /// <summary>A target has taken fright and is moving off.</summary>
        Scattered,
    }

    public struct CombatEvent
    {
        public CombatEventType Type;
        public Vector3 Position;
        public int Target;
        public float Size;
    }

    public sealed partial class MissionRun
    {
        public const float RocketSpeed = 85f;
        /// <summary>Half-angle of the cone ahead of the nose inside which a target can be locked.</summary>
        public const float LockHalfAngleDeg = 32f;
        /// <summary>Seconds a gunner needs to find the drone before the first rounds arrive.</summary>
        public const float AimSeconds = 1.6f;
        public const float RocketSplash = 6f, BombBlast = 14f;
        /// <summary>A hostile this close to the ally it is after opens fire on it.</summary>
        public const float AttackRange = 70f;
        public const float AttackDamagePerSecond = 5f;
        /// <summary>Landing this close to the operator rearms the drone.</summary>
        public const float RearmRadius = 12f;
        /// <summary>Finishing with at least this much hull earns the third star in a combat mission.</summary>
        public const float CleanHull = 0.5f;

        public const float DormantDepth = 5000f;

        TargetState[] _targets = new TargetState[0];
        readonly List<Projectile> _projectiles = new List<Projectile>();
        readonly List<CombatEvent> _events = new List<CombatEvent>();
        float _fireCooldown, _bombCooldown, _tracerClock;
        Vector3 _dronePosition;

        public int Rockets { get; private set; }
        public int Bombs { get; private set; }
        /// <summary>Index of the target a rocket fired now would go for, or -1.</summary>
        public int LockedTarget { get; private set; } = -1;
        /// <summary>True on the steps when rounds are hitting the drone.</summary>
        public bool UnderFire { get; private set; }
        /// <summary>Strike drones still on the pad, in a mission flown with them.</summary>
        public int DronesLeft { get; private set; }
        /// <summary>The drone is gone and the next one must be put on the pad: call <see cref="Respawned"/>.</summary>
        public bool NeedsRespawn { get; private set; }
        public int Kills { get; private set; }

        public IReadOnlyList<Projectile> Projectiles => _projectiles;
        /// <summary>What happened in the last step, for sound and effects.</summary>
        public IReadOnlyList<CombatEvent> Events => _events;
        public int TargetCount => _targets.Length;
        public TargetState Target(int index) => _targets[index];
        public bool IsCombat => _targets.Length > 0;
        public bool Kamikaze => Definition.StrikeDrones > 0;

/// <summary>
        /// Whether one-way attack drones can fly a mission: apart from taking off, and coming
        /// home at the end, it must ask for nothing but destruction.
        /// </summary>
        public static bool SuitsOneWay(MissionDefinition mission)
        {
            if (mission.Targets.Count == 0) return false;
            int destroy = 0;
            for (int i = 0; i < mission.Objectives.Count; i++)
            {
                ObjectiveType type = mission.Objectives[i].Type;
                if (type == ObjectiveType.Destroy) destroy++;
                else if (type == ObjectiveType.TakeOff) continue;
                else if (type == ObjectiveType.Land && i == mission.Objectives.Count - 1) continue;
                else return false;
            }
            return destroy > 0;
        }

        /// <summary>
        /// The mission as a sortie of <paramref name="airframes"/> one-way drones: the landing at
        /// the end is struck off, since none of them is coming back. Null if they cannot fly it.
        /// </summary>
        public static MissionDefinition AsOneWaySortie(MissionDefinition mission, int airframes)
        {
            if (!SuitsOneWay(mission)) return null;
            MissionDefinition sortie = mission.Copy();
            sortie.StrikeDrones = airframes;
            int last = sortie.Objectives.Count - 1;
            if (sortie.Objectives[last].Type == ObjectiveType.Land) sortie.Objectives.RemoveAt(last);
            return sortie;
        }

        /// <summary>The third star: an intact drone, or in combat one that was not shot to pieces.</summary>
        public bool CleanFinish => Kamikaze ? DronesLeft > 0 : IsCombat ? Hull >= CleanHull : !Damaged;

        void StartCombat()
        {
            List<TargetDefinition> definitions = Definition.Targets;
            _targets = new TargetState[definitions.Count];
            for (int i = 0; i < _targets.Length; i++)
            {
                _targets[i] = PlaceTarget(definitions[i], -definitions[i].Delay * definitions[i].Speed);
                // A later wave waits far below the map until the objective it belongs to begins.
                if (definitions[i].StartObjective > 1 && definitions[i].Parent < 0)
                {
                    _targets[i].Dormant = true;
                    _targets[i].Position.y -= DormantDepth;
                }
                _targets[i].Health = definitions[i].Kind.Health * (definitions[i].Friendly ? 1f : Definition.HealthScale);
                _targets[i].Alive = true;
            }
            DronesLeft = Definition.StrikeDrones;
            StartWarfare();
            Rearm();
        }

        void Rearm()
        {
            // A strike drone is the munition.
            Rockets = Kamikaze || !IsCombat ? 0 : _spec.Rockets;
            Bombs = Kamikaze || !IsCombat ? 0 : _spec.Bombs;
            RearmWarfare();
        }

        /// <summary>The ground crew's part of a stop on the pad: full racks and a fresh battery.</summary>
        void Resupply()
        {
            Rearm();
            Battery = Mathf.Max(Battery, Mathf.Clamp01(Definition.BatteryStart));
        }

        /// <summary>The game has put the next strike drone on the pad.</summary>
        public void Respawned()
        {
            NeedsRespawn = false;
            Battery = Mathf.Clamp01(Definition.BatteryStart);
            Hull = 1f;
            _signalLostFor = 0f;
            for (int i = 0; i < _targets.Length; i++) _targets[i].Aim = 0f;
            if (!Kamikaze) Rearm();
        }

        TargetState PlaceTarget(TargetDefinition definition, float travelled)
        {
            TargetState state = default;
            if (definition.Path.Count >= 2)
            {
                float total = PathLength(definition.Path);
                float along = definition.Loop && total > 1f ? Mathf.Repeat(travelled, total) : travelled;
                ActorState onPath = AlongPath(definition.Path, along);
                state.Position = onPath.Position;
                state.Forward = onPath.Forward;
                state.Arrived = onPath.Arrived && !definition.Loop;
            }
            else
            {
                state.Position = OnGround(definition.Position);
                state.Forward = Vector3.forward;
            }
            state.Travelled = travelled;
            state.Position.y += definition.Altitude;
            return state;
        }

        static float PathLength(List<Vector3> path)
        {
            float total = 0f;
            for (int i = 0; i + 1 < path.Count; i++)
            {
                Vector3 leg = path[i + 1] - path[i];
                leg.y = 0f;
                total += leg.magnitude;
            }
            return total;
        }

        /// <summary>Returns false when the mission ended in this step.</summary>
        bool UpdateCombat(FlightState state, float dt)
        {
            _dronePosition = state.Position;
            if (_targets.Length == 0) return true;

            _fireCooldown = Mathf.Max(0f, _fireCooldown - dt);
            _bombCooldown = Mathf.Max(0f, _bombCooldown - dt);
            _tracerClock += dt;
            bool tracer = _tracerClock >= 0.22f;
            if (tracer) _tracerClock = 0f;

            MoveTargets(dt);
            UpdateBehaviours(state, dt);
            UpdateProjectiles(dt);
            if (Status != MissionStatus.InProgress) return false;
            if (!UpdateWarfare(state, dt, tracer)) return false;
            if (!EnemyFire(state, dt, tracer)) return false;
            if (NeedsRespawn) return true;
            if (!AlliesUnderAttack(dt, tracer)) return false;
            UpdateLock(state);

            if (Kamikaze && !NeedsRespawn && !state.Grounded && TouchesTarget(state.Position))
                return DroneLost(state.Position, true);
            return CheckEscape();
        }

        void MoveTargets(float dt)
        {
            for (int i = 0; i < _targets.Length; i++)
            {
                TargetDefinition definition = Definition.Targets[i];
                if (!_targets[i].Alive || definition.Path.Count < 2 || _targets[i].Arrived) continue;
                if (ObjectiveIndex < definition.StartObjective) continue;
                // A frightened column drives faster.
                float pace = _targets[i].Alarmed && definition.Kind.Scatters ? 1.45f : 1f;
                TargetState moved = PlaceTarget(definition, _targets[i].Travelled + definition.Speed * pace * dt);
                moved.Health = _targets[i].Health;
                moved.Alive = true;
                moved.Aim = _targets[i].Aim;
                moved.Clock = _targets[i].Clock;
                moved.Cover = _targets[i].Cover;
                moved.Alarmed = _targets[i].Alarmed;
                _targets[i] = moved;
            }
        }

        /// <summary>A mover that reaches the end of its road alive has got away.</summary>
        bool CheckEscape()
        {
            ObjectiveDefinition objective = CurrentObjective;
            if (objective == null || objective.Type != ObjectiveType.Destroy) return true;
            foreach (int index in objective.TargetIndices)
            {
                if (!_targets[index].Alive || !_targets[index].Arrived) continue;
                // An attacker that arrives stays and fights; anything else was running.
                if (Definition.Targets[index].Attacks >= 0) continue;
                Fail(FailReason.Escaped);
                return false;
            }
            return true;
        }

        // ---------------------------------------------------------------- the enemy shoots

        bool EnemyFire(FlightState state, float dt, bool tracer)
        {
            UnderFire = false;
            if (NeedsRespawn) return true;
            float damage = 0f;
            float above = HeightAboveGround(state.Position);
            bool parked = state.Grounded && HorizontalDistance(state.Position, Definition.BasePosition) < RearmRadius * 2f;

            for (int i = 0; i < _targets.Length; i++)
            {
                _targets[i].Firing = false;
                TargetDefinition definition = Definition.Targets[i];
                TargetKind kind = definition.Kind;
                if (!_targets[i].Alive || definition.Friendly || !kind.Armed) continue;

                Vector3 muzzle = _targets[i].Position + Vector3.up * 2f;
                bool sees = !parked && !Blinded(kind) &&
                            Vector3.Distance(state.Position, muzzle) <= kind.GunRange * _spec.JamScale &&
                            above >= kind.MinAltitude &&
                            HasLineOfSight(muzzle, state.Position);
                if (!sees)
                {
                    _targets[i].Aim = Mathf.Max(0f, _targets[i].Aim - dt * 2f);
                    continue;
                }

                // A stealthy airframe takes longer to pick out of the sky.
                _targets[i].Aim += dt;
                if (_targets[i].Aim < AimSeconds / Mathf.Max(0.2f, _spec.DetectScale)) continue;
                _targets[i].Firing = true;
                damage += kind.GunDamage * Definition.GunScale;
                if (tracer) _events.Add(new CombatEvent { Type = CombatEventType.IncomingFire, Position = muzzle, Target = i });
            }
            if (damage <= 0f) return true;

            // A drone at speed is a hard target; one that hovers in front of a gun is not.
            float speed = new Vector2(state.Velocity.x, state.Velocity.z).magnitude;
            float evasion = Mathf.Lerp(1f, 0.45f, Mathf.Clamp01(speed / Mathf.Max(1f, _spec.MaxSpeed)));
            UnderFire = true;
            Hull = Mathf.Max(0f, Hull - damage * evasion * _spec.DamageScale * dt);
            if (Hull > 0f) return true;
            if (Kamikaze) return DroneLost(state.Position, false);
            return AircraftDown(state.Position);
        }

        bool AlliesUnderAttack(float dt, bool tracer)
        {
            for (int i = 0; i < _targets.Length; i++)
            {
                int victim = Definition.Targets[i].Attacks;
                if (victim < 0 || !_targets[i].Alive || !_targets[victim].Alive) continue;
                if (HorizontalDistance(_targets[i].Position, _targets[victim].Position) > AttackRange) continue;
                _targets[i].Firing = true;
                if (tracer)
                    _events.Add(new CombatEvent { Type = CombatEventType.AllyUnderFire, Position = _targets[i].Position + Vector3.up * 2f, Target = victim });
                Damage(victim, AttackDamagePerSecond * dt, false);
                if (Status != MissionStatus.InProgress) return false;
            }
            return true;
        }

        // ---------------------------------------------------------------- the drone shoots

        void UpdateLock(FlightState state)
        {
            LockedTarget = -1;
            if ((Rockets <= 0 && CannonSeconds <= 0f) || state.Grounded) return;
            float yaw = state.YawDeg * Mathf.Deg2Rad;
            var heading = new Vector2(Mathf.Sin(yaw), Mathf.Cos(yaw));
            float best = LockHalfAngleDeg;
            for (int i = 0; i < _targets.Length; i++)
            {
                if (!_targets[i].Alive || Definition.Targets[i].Friendly) continue;
                Vector3 aim = _targets[i].Position + Vector3.up * 1.5f;
                Vector3 offset = aim - state.Position;
                if (offset.magnitude > _spec.LockRange) continue;
                var flat = new Vector2(offset.x, offset.z);
                float angle = flat.sqrMagnitude > 1f ? Vector2.Angle(heading, flat) : 0f;
                if (angle >= best || !HasLineOfSight(state.Position, aim)) continue;
                best = angle;
                LockedTarget = i;
            }
        }

        ActionResult FireWeapons(FlightState state, bool rocket, bool bomb)
        {
            if (!IsCombat || Kamikaze || state.Grounded) return ActionResult.None;
            if (rocket && _fireCooldown <= 0f)
            {
                if (Rockets <= 0) return ActionResult.NoAmmo;
                if (LockedTarget < 0) return ActionResult.NoLock;
                Rockets--;
                _fireCooldown = _spec.FireInterval;
                Vector3 to = _targets[LockedTarget].Position + Vector3.up * 1.5f - state.Position;
                _projectiles.Add(new Projectile
                {
                    Type = ProjectileType.Rocket, Position = state.Position + Vector3.down * 0.3f,
                    Direction = to.sqrMagnitude > 0.01f ? to.normalized : Vector3.forward,
                    Target = LockedTarget, Damage = _spec.RocketDamage * _ordnance,
                });
                _events.Add(new CombatEvent { Type = CombatEventType.RocketFired, Position = state.Position, Target = LockedTarget });
                return ActionResult.RocketFired;
            }
            if (bomb && _bombCooldown <= 0f)
            {
                if (Bombs <= 0) return ActionResult.NoAmmo;
                Bombs--;
                _bombCooldown = 0.9f;
                Vector3 impact = Ballistics.Impact(state.Position, state.Velocity, _lastWind, _map, out float fall, _spec.WindCarry);
                _projectiles.Add(new Projectile
                {
                    Type = ProjectileType.Bomb, Position = state.Position, From = state.Position, Impact = impact,
                    Duration = Mathf.Max(0.05f, fall), Damage = _spec.BombDamage * _ordnance, Target = -1,
                });
                _events.Add(new CombatEvent { Type = CombatEventType.BombDropped, Position = state.Position, Target = -1 });
                return ActionResult.BombDropped;
            }
            return ActionResult.None;
        }

        void UpdateProjectiles(float dt)
        {
            for (int i = _projectiles.Count - 1; i >= 0; i--)
            {
                Projectile p = _projectiles[i];
                p.Elapsed += dt;
                bool gone = false;
                if (p.Type == ProjectileType.Guided)
                {
                    gone = FlyGuided(ref p, dt);
                }
                else if (p.Type == ProjectileType.Rocket)
                {
                    _weapon = Weapon.Rocket;
                    Vector3 aim = _targets[p.Target].Position + Vector3.up * 1.5f;
                    Vector3 to = aim - p.Position;
                    float step = RocketSpeed * dt;
                    if (to.magnitude <= step + 0.5f)
                    {
                        Explode(aim, p.Damage, RocketSplash, p.Target, true);
                        gone = true;
                    }
                    else
                    {
                        p.Direction = to.normalized;
                        p.Position += p.Direction * step;
                        // It was fired with the target in sight, so a crest on the way is skimmed, not hit:
                        // the check that granted the lock looks at the ground more coarsely than a rocket flies.
                        float skim = p.Position.y - HeightAboveGround(p.Position) + 1f;
                        if (p.Position.y < skim) p.Position.y = skim;
                        if (p.Elapsed > 6f)
                        {
                            Explode(p.Position, p.Damage, RocketSplash, -1, true);
                            gone = true;
                        }
                    }
                }
                else
                {
                    float t = Mathf.Clamp01(p.Elapsed / p.Duration);
                    Vector3 position = Vector3.Lerp(p.From, p.Impact, t);
                    position.y = Mathf.Lerp(p.From.y, p.Impact.y, t * t);
                    p.Position = position;
                    if (p.Elapsed >= p.Duration)
                    {
                        _weapon = Weapon.Bomb;
                        Explode(p.Impact, p.Damage, BombBlast, -1, false);
                        if (_spec.Cluster) Scatter(p.Impact, p.Damage);
                        gone = true;
                    }
                }
                if (gone) _projectiles.RemoveAt(i);
                else _projectiles[i] = p;
                if (Status != MissionStatus.InProgress) return;
            }
        }

        /// <summary>
        /// A blast at a point. The target hit directly, or any whose footprint the point is in,
        /// takes it in full; others take less the farther their edge is from it.
        /// </summary>
        void Explode(Vector3 point, float damage, float radius, int direct, bool rocket)
        {
            _events.Add(new CombatEvent { Type = CombatEventType.Explosion, Position = point, Target = direct, Size = radius });
            for (int i = 0; i < _targets.Length; i++)
            {
                if (!_targets[i].Alive) continue;
                TargetKind kind = Definition.Targets[i].Kind;
                float share;
                if (i == direct)
                {
                    share = 1f;
                }
                else
                {
                    // An aircraft in flight is above what a ground burst reaches.
                    if (Mathf.Abs(_targets[i].Position.y - point.y) > kind.Height + radius * 0.6f) continue;
                    float edge = HorizontalDistance(point, _targets[i].Position) - kind.Radius;
                    if (edge >= radius) continue;
                    share = edge <= 0f ? 1f : 1f - edge / radius;
                    if (rocket) share *= 0.5f;
                }
                float dealt = damage * share * (rocket && kind.Structure ? 0.4f : 1f);
                Damage(i, dealt, true);
                if (Status != MissionStatus.InProgress) return;
            }
            // Whoever lived through it now knows: they run, move or go to ground.
            Alarm(point, radius);
        }

        void Damage(int index, float amount, bool byPlayer)
        {
            if (amount <= 0f || !_targets[index].Alive) return;
            amount *= Protection(index);
            _targets[index].Health -= amount;
            if (_targets[index].Health > 0f)
            {
                if (byPlayer) _events.Add(new CombatEvent { Type = CombatEventType.TargetHit, Position = _targets[index].Position, Target = index });
                return;
            }

            _targets[index].Health = 0f;
            _targets[index].Alive = false;
            _targets[index].Firing = false;
            TargetDefinition definition = Definition.Targets[index];
            _events.Add(new CombatEvent
            {
                Type = CombatEventType.TargetDestroyed, Position = _targets[index].Position, Target = index,
                Size = Mathf.Max(definition.Kind.Radius * 1.6f, definition.Kind.Blast),
            });
            if (definition.Friendly)
            {
                // A defence turret is expendable; what the mission is about is not.
                if (definition.Kind.Id != "turret") Fail(FailReason.AllyLost);
                return;
            }
            Kills++;
            CountKill(index, byPlayer);

            // Fuel and ammunition take their neighbours with them, and a drone that is too close.
            if (definition.Kind.Blast > 0f)
            {
                Vector3 centre = _targets[index].Position;
                float reach = definition.Kind.Blast;
                float gap = Vector3.Distance(_dronePosition, centre);
                if (gap < reach && !NeedsRespawn && !Kamikaze)
                {
                    Hull = Mathf.Max(0f, Hull - 0.45f * (1f - gap / reach) * _spec.DamageScale);
                    if (Hull <= 0f && !AircraftDown(_dronePosition)) return;
                }
                Explode(centre, 130f, reach, -1, false);
            }
        }

        // ---------------------------------------------------------------- strike drones

        bool TouchesTarget(Vector3 position)
        {
            for (int i = 0; i < _targets.Length; i++)
            {
                if (!_targets[i].Alive || Definition.Targets[i].Friendly) continue;
                TargetKind kind = Definition.Targets[i].Kind;
                if (position.y > _targets[i].Position.y + kind.Height + 1f) continue;
                if (position.y < _targets[i].Position.y - 2f) continue;
                if (HorizontalDistance(position, _targets[i].Position) <= kind.Radius + 0.5f) return true;
            }
            return false;
        }

        /// <summary>
        /// A strike drone is gone: it flew into something, or was shot down. Returns false when
        /// that ended the mission.
        /// </summary>
        bool DroneLost(Vector3 position, bool detonate)
        {
            DronesLeft = Mathf.Max(0, DronesLeft - 1);
            _events.Add(new CombatEvent { Type = CombatEventType.Detonated, Position = position, Target = -1, Size = BombBlast });
            _weapon = Weapon.Ram;
            if (detonate) Explode(position, Mathf.Max(360f, _spec.BombDamage * 1.6f), BombBlast, -1, false);
            if (Status != MissionStatus.InProgress) return false;

            // The blow that finishes the job needs no drone to fly home.
            ObjectiveDefinition objective = CurrentObjective;
            if (objective != null && objective.Type == ObjectiveType.Destroy && Remaining(objective) == 0)
            {
                CompleteObjective();
                if (Status != MissionStatus.InProgress) return false;
            }
            if (DronesLeft <= 0)
            {
                Fail(FailReason.OutOfDrones);
                return false;
            }
            NeedsRespawn = true;
            return true;
        }

        /// <summary>How many targets of a Destroy objective are still standing.</summary>
        public int Remaining(ObjectiveDefinition objective)
        {
            int left = 0;
            foreach (int index in objective.TargetIndices)
                if (_targets[index].Alive) left++;
            return left;
        }

        /// <summary>The standing target of a Destroy objective nearest to a point, or -1.</summary>
        public int NearestTarget(ObjectiveDefinition objective, Vector3 from)
        {
            int nearest = -1;
            float best = float.MaxValue;
            foreach (int index in objective.TargetIndices)
            {
                if (!_targets[index].Alive) continue;
                float distance = Vector3.Distance(from, _targets[index].Position);
                if (distance >= best) continue;
                best = distance;
                nearest = index;
            }
            return nearest;
        }
    }
}
