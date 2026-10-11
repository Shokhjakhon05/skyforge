using System.Collections.Generic;
using UnityEngine;

namespace Skyforge.Core
{
    public enum PickupType
    {
        Repair,
        Ammo,
        Credits,
    }

    /// <summary>What a destroyed enemy left behind: fly or drive through it to take it.</summary>
    public struct Pickup
    {
        public PickupType Type;
        public Vector3 Position;
        public float Left;
        public int Id;
    }

    /// <summary>
    /// The second extension of the fight: rockets fired without a lock, loot left by the enemy,
    /// kills in quick succession, the rifle of a sniper mission and what a ground vehicle needs.
    /// </summary>
    public sealed partial class MissionRun
    {
        public const float PickupSeconds = 45f, PickupRadius = 11f, PickupHeight = 70f;
        public const float PickupRepair = 0.25f;
        public const int PickupCredits = 40;
        /// <summary>Kills this close together in time count as one chain.</summary>
        public const float ChainSeconds = 4.5f;
        public const float BulletSpeed = 340f, BulletDamage = 260f, BulletDrop = 3.2f, BulletWind = 0.55f;
        /// <summary>A rocket fired without a lock goes off this close to anything hostile.</summary>
        const float UnguidedFuse = 3.2f;

        readonly List<Pickup> _pickups = new List<Pickup>();
        System.Random _lootDice;
        int _pickupSerial;
        float _chainLeft;

        /// <summary>Where the sight is looking when nothing is locked: a rocket fired now flies there.</summary>
        public Vector3? FreeAim;
        public IReadOnlyList<Pickup> Pickups => _pickups;
        /// <summary>Credits picked up on the field, paid with the reward if the mission is won.</summary>
        public int Loot { get; private set; }
        public int PickupsTaken { get; private set; }
        /// <summary>Kills in the chain that is running: 2 is a double, 3 a triple and so on.</summary>
        public int Chain { get; private set; }
        public int BestChain { get; private set; }
        public bool Sniper => Definition.Mode == "sniper";
        public int Rounds { get; private set; }
        public int ShotsFired { get; private set; }
        public int ShotsHit { get; private set; }

        /// <summary>A drone on the ground cannot fight; a tank is never anywhere else.</summary>
        bool Parked(FlightState state) => state.Grounded && !_spec.Ground;

        void StartArsenal()
        {
            _lootDice = new System.Random((Definition.Id ?? "").GetHashCode() * 31 + 5);
            if (Sniper) Rounds = Definition.Rounds;
        }

        // ---------------------------------------------------------------- unguided rockets

        bool FireUnguided(FlightState state)
        {
            if (FreeAim == null) return false;
            Vector3 from = state.Position + Vector3.down * 0.3f;
            Vector3 to = FreeAim.Value - from;
            if (to.sqrMagnitude < 4f) return false;
            Rockets--;
            _fireCooldown = _spec.FireInterval;
            _projectiles.Add(new Projectile
            {
                Type = ProjectileType.Rocket, Position = from, Direction = to.normalized, Target = -1,
                Damage = _spec.RocketDamage * _ordnance, Impact = FreeAim.Value,
            });
            _events.Add(new CombatEvent { Type = CombatEventType.RocketFired, Position = state.Position, Target = -1 });
            return true;
        }

        /// <summary>Flies a rocket that has no target; true when it went off.</summary>
        bool FlyUnguided(ref Projectile p, float dt)
        {
            _weapon = Weapon.Rocket;
            int steps = Mathf.Max(1, Mathf.CeilToInt(RocketSpeed * dt / 2f));
            float step = RocketSpeed * dt / steps;
            for (int s = 0; s < steps; s++)
            {
                p.Position += p.Direction * step;
                int near = -1;
                for (int i = 0; i < _targets.Length; i++)
                {
                    if (!_targets[i].Alive || Definition.Targets[i].Friendly) continue;
                    TargetKind kind = Definition.Targets[i].Kind;
                    Vector3 centre = _targets[i].Position + Vector3.up * Mathf.Min(1.5f, kind.Height * 0.5f);
                    if (Vector3.Distance(p.Position, centre) > kind.Radius + UnguidedFuse) continue;
                    near = i;
                    break;
                }
                bool ground = _map != null && HeightAboveGround(p.Position) <= 0.2f;
                if (near < 0 && !ground && p.Position.y > Island.WaterLevel) continue;
                Explode(p.Position, p.Damage, RocketSplash, near, true);
                return true;
            }
            if (p.Elapsed > 7f)
            {
                Explode(p.Position, p.Damage, RocketSplash, -1, true);
                return true;
            }
            return false;
        }

        // ---------------------------------------------------------------- loot and chains

        void AfterKill(int index)
        {
            if (_weapon == Weapon.Ally) return;
            Chain = _chainLeft > 0f ? Chain + 1 : 1;
            _chainLeft = ChainSeconds;
            BestChain = Mathf.Max(BestChain, Chain);
            if (Chain >= 2)
                _events.Add(new CombatEvent { Type = CombatEventType.ChainKill, Position = _targets[index].Position, Target = index, Size = Chain });

            if (Sniper || _lootDice == null) return;
            TargetDefinition definition = Definition.Targets[index];
            Vector3 at = _targets[index].Position;
            float ground = _map != null ? at.y - HeightAboveGround(at) : at.y;
            if (_map != null && ground < Island.WaterLevel + 0.3f) return;
            // Hardware carries more than a man does.
            double chance = definition.Kind.Health >= 100f ? 0.6 : 0.3;
            if (_lootDice.NextDouble() > chance) return;
            double roll = _lootDice.NextDouble();
            PickupType type = Hull < 0.55f && roll < 0.55 ? PickupType.Repair
                : roll < 0.3 ? PickupType.Repair : roll < 0.65 ? PickupType.Ammo : PickupType.Credits;
            var pickup = new Pickup { Type = type, Position = new Vector3(at.x, ground, at.z), Left = PickupSeconds, Id = _pickupSerial++ };
            _pickups.Add(pickup);
            _events.Add(new CombatEvent { Type = CombatEventType.PickupDropped, Position = pickup.Position, Target = pickup.Id, Size = (int)type });
        }

        void UpdateArsenal(FlightState state, float dt)
        {
            if (_chainLeft > 0f)
            {
                _chainLeft -= dt;
                if (_chainLeft <= 0f) Chain = 0;
            }
            for (int i = _pickups.Count - 1; i >= 0; i--)
            {
                Pickup pickup = _pickups[i];
                pickup.Left -= dt;
                bool taken = !NeedsRespawn && HorizontalDistance(state.Position, pickup.Position) <= PickupRadius &&
                    state.Position.y - pickup.Position.y <= PickupHeight;
                if (taken)
                {
                    Take(pickup.Type);
                    PickupsTaken++;
                    _events.Add(new CombatEvent { Type = CombatEventType.PickupTaken, Position = pickup.Position, Target = pickup.Id, Size = (int)pickup.Type });
                }
                if (taken || pickup.Left <= 0f) _pickups.RemoveAt(i);
                else _pickups[i] = pickup;
            }
        }

        void Take(PickupType type)
        {
            switch (type)
            {
                case PickupType.Repair:
                    Hull = Mathf.Min(1f, Hull + PickupRepair);
                    break;
                case PickupType.Ammo:
                    Rockets = Mathf.Min(_spec.Rockets, Rockets + Mathf.Max(1, Mathf.CeilToInt(_spec.Rockets * 0.4f)));
                    Bombs = Mathf.Min(_spec.Bombs, Bombs + Mathf.Max(_spec.Bombs > 0 ? 1 : 0, Mathf.CeilToInt(_spec.Bombs * 0.34f)));
                    if (_spec.Cannon > 0f) CannonSeconds = Mathf.Min(_spec.CannonSeconds, CannonSeconds + _spec.CannonSeconds * 0.4f);
                    break;
                default:
                    Loot += PickupCredits + 10 * Mathf.Max(0, Definition.Level);
                    break;
            }
        }

        // ---------------------------------------------------------------- the rifle

        /// <summary>
        /// One round from the sniper's rifle. It takes time to arrive, sinks a little on the way and
        /// drifts with the wind, so a far or a moving target has to be led. False when the magazine is empty.
        /// </summary>
        public bool SniperFire(Vector3 origin, Vector3 direction)
        {
            if (!Sniper || Status != MissionStatus.InProgress || Rounds <= 0 || _fireCooldown > 0f) return false;
            Rounds--;
            ShotsFired++;
            _fireCooldown = 1.1f;
            _projectiles.Add(new Projectile
            {
                Type = ProjectileType.Bullet, Position = origin, From = origin, Direction = direction.normalized, Target = -1, Damage = BulletDamage,
            });
            _events.Add(new CombatEvent { Type = CombatEventType.RifleShot, Position = origin, Target = -1 });
            return true;
        }

        bool FlyBullet(ref Projectile p, float dt)
        {
            _weapon = Weapon.Rifle;
            Vector3 velocity = p.Direction * BulletSpeed;
            velocity.y -= BulletDrop * p.Elapsed;
            velocity += new Vector3(_lastWind.x, 0f, _lastWind.z) * BulletWind * Mathf.Min(1f, p.Elapsed * 2f);
            int steps = Mathf.Max(1, Mathf.CeilToInt(BulletSpeed * dt / 1.5f));
            Vector3 step = velocity * dt / steps;
            for (int s = 0; s < steps; s++)
            {
                p.Position += step;
                for (int i = 0; i < _targets.Length; i++)
                {
                    if (!_targets[i].Alive || Definition.Targets[i].Friendly) continue;
                    TargetKind kind = Definition.Targets[i].Kind;
                    Vector3 centre = _targets[i].Position + Vector3.up * kind.Height * 0.5f;
                    Vector3 off = p.Position - centre;
                    float reach = Mathf.Max(1.1f, kind.Radius * 0.8f);
                    if (new Vector2(off.x, off.z).magnitude > reach || Mathf.Abs(off.y) > Mathf.Max(1.2f, kind.Height * 0.6f)) continue;
                    ShotsHit++;
                    _events.Add(new CombatEvent { Type = CombatEventType.BulletHit, Position = p.Position, Target = i, Size = 1f });
                    // Steel shrugs off most of a rifle round; a man does not.
                    Damage(i, p.Damage * (kind.Health > 120f ? 0.35f : 1f), true);
                    Alarm(p.Position, 30f);
                    return true;
                }
                if (_map != null && HeightAboveGround(p.Position) <= 0f)
                {
                    _events.Add(new CombatEvent { Type = CombatEventType.BulletHit, Position = p.Position, Target = -1, Size = 0f });
                    Alarm(p.Position, 14f);
                    return true;
                }
            }
            return p.Elapsed > 4f;
        }

        /// <summary>A sniper who is out of rounds with work left has failed.</summary>
        void CheckRounds()
        {
            if (!Sniper || Rounds > 0 || Status != MissionStatus.InProgress) return;
            foreach (Projectile p in _projectiles)
                if (p.Type == ProjectileType.Bullet) return;
            ObjectiveDefinition objective = CurrentObjective;
            if (objective != null && objective.Type == ObjectiveType.Destroy) Fail(FailReason.OutOfAmmo);
        }
    }
}
