using System.Collections.Generic;
using UnityEngine;

namespace Skyforge.Core
{
    /// <summary>A strike that has been called and is on its way.</summary>
    public struct PendingStrike
    {
        public Vector3 Position;
        public float Seconds, Damage, Radius;
        /// <summary>The target it is aimed at, or -1 for a point on the ground.</summary>
        public int Target;
    }

    /// <summary>
    /// The second half of the fight: an enemy that reacts and hunts, things mounted on other
    /// things, friendly guns, and the weapons beyond rockets and bombs. The plain campaign uses
    /// little of it; the generated operations and the veteran campaign turn it on.
    /// </summary>
    public sealed partial class MissionRun
    {
        public const float JamSeconds = 8f, FlareSeconds = 6f;
        public const float GuidedSpeed = 52f, GuidedSeconds = 14f, GuidedDamage = 330f, GuidedBlast = 11f;
        /// <summary>Kills in a row that earn the first, second and third tier of support.</summary>
        public static readonly int[] SupportStreak = { 3, 5, 7 };
        /// <summary>How long a frightened vehicle keeps driving, and how fast.</summary>
        const float FleeSeconds = 7f, FleeSpeed = 7f;
        const float CoverSeconds = 9f;

        readonly List<PendingStrike> _strikes = new List<PendingStrike>();
        readonly int[] _killsBy = new int[8];
        Weapon _weapon;
        float _ordnance = 1f;
        float _jamLeft, _flareLeft, _cannonTick;
        bool _abilityAsked, _supportAsked;
        Vector2 _guide;

        /// <summary>Seconds of cannon fire left.</summary>
        public float CannonSeconds { get; private set; }
        public int AbilityCharges { get; private set; }
        /// <summary>Kills since support was last called.</summary>
        public int Streak { get; private set; }
        /// <summary>Spare aircraft still standing by.</summary>
        public int SparesLeft { get; private set; }
        public bool Jamming => _jamLeft > 0f;
        public bool FlaresBurning => _flareLeft > 0f;
        public IReadOnlyList<PendingStrike> Strikes => _strikes;
        /// <summary>The cannon is firing on this step.</summary>
        public bool CannonFiring { get; private set; }
        /// <summary>Kills made with air-to-air relevance: hostile aircraft brought down.</summary>
        public int AirKills { get; private set; }
        public int ArmourKills { get; private set; }

        public int KillsBy(Weapon weapon) => _killsBy[(int)weapon];

        /// <summary>The tier of support the streak has earned: 0 for none, up to 3.</summary>
        public int SupportTier
        {
            get
            {
                int tier = 0;
                for (int i = 0; i < SupportStreak.Length; i++)
                    if (Streak >= SupportStreak[i]) tier = i + 1;
                return tier;
            }
        }

        /// <summary>A hand-flown missile is in the air: the camera rides it.</summary>
        public bool GuidedActive => GuidedIndex() >= 0;
        public Vector3 GuidedPosition => GuidedIndex() >= 0 ? _projectiles[GuidedIndex()].Position : Vector3.zero;
        public Vector3 GuidedDirection => GuidedIndex() >= 0 ? _projectiles[GuidedIndex()].Direction : Vector3.forward;

        int GuidedIndex()
        {
            for (int i = 0; i < _projectiles.Count; i++)
                if (_projectiles[i].Type == ProjectileType.Guided) return i;
            return -1;
        }

        /// <summary>Multiplier on rocket and bomb damage from the armoury's upgrades; set once before the first step.</summary>
        public float Ordnance
        {
            get => _ordnance;
            set => _ordnance = Mathf.Max(0.1f, value);
        }

        /// <summary>The third button: whatever special equipment the aircraft carries.</summary>
        public void UseAbility() => _abilityAsked = true;

        /// <summary>Calls in the support the streak has earned, on the locked target.</summary>
        public void CallSupport() => _supportAsked = true;

        /// <summary>Stick input for the hand-flown missile: x turns it, y pitches it.</summary>
        public void Guide(Vector2 input) => _guide = Vector2.ClampMagnitude(input, 1f);

        void StartWarfare()
        {
            SparesLeft = Kamikaze ? 0 : Mathf.Max(0, Definition.Spares);
            Streak = 0;
        }

        void RearmWarfare()
        {
            bool armed = IsCombat && !Kamikaze;
            CannonSeconds = armed && _spec.Cannon > 0f ? _spec.CannonSeconds : 0f;
            AbilityCharges = armed && _spec.Ability != Ability.None ? _spec.AbilityCharges : 0;
        }

        // ---------------------------------------------------------------- how the enemy behaves

        /// <summary>Guns that cannot see the drone right now: all of them under jamming, the missile batteries and pursuers under flares.</summary>
        bool Blinded(TargetKind kind)
        {
            if (_jamLeft > 0f) return true;
            return _flareLeft > 0f && (kind.MinAltitude > 0f || kind.Pursues);
        }

        /// <summary>How much of a blow gets through: infantry in cover and a hull behind its turrets take less.</summary>
        float Protection(int index)
        {
            float share = 1f;
            if (_targets[index].Cover > 0f) share *= 0.4f;
            // A ship or a train's engine is hard to hurt while the guns mounted on it still stand.
            for (int i = 0; i < _targets.Length; i++)
                if (Definition.Targets[i].Parent == index && _targets[i].Alive) return share * 0.2f;
            return share;
        }

        /// <summary>A blast frightens what is near it, in missions where the enemy reacts.</summary>
        void Alarm(Vector3 point, float radius)
        {
            if (!Definition.Reactive) return;
            float reach = radius + 55f;
            for (int i = 0; i < _targets.Length; i++)
            {
                TargetDefinition definition = Definition.Targets[i];
                if (!_targets[i].Alive || definition.Friendly || definition.Parent >= 0) continue;
                if (HorizontalDistance(point, _targets[i].Position) > reach) continue;
                TargetKind kind = definition.Kind;
                bool first = !_targets[i].Alarmed;
                _targets[i].Alarmed = true;
                if (kind.TakesCover) _targets[i].Cover = CoverSeconds;
                if (!first || definition.Path.Count >= 2) continue;
                if (!kind.Scatters && !kind.Relocates) continue;

                // Away from the blast; if that way is the sea or a cliff, it stays where it is.
                Vector3 away = _targets[i].Position - point;
                away.y = 0f;
                if (away.sqrMagnitude < 1f) away = new Vector3(Mathf.Sin(i * 2.4f), 0f, Mathf.Cos(i * 2.4f));
                away.Normalize();
                _targets[i].Flee = away;
                _targets[i].FleeLeft = kind.Relocates ? FleeSeconds * 0.6f : FleeSeconds;
                _events.Add(new CombatEvent { Type = CombatEventType.Scattered, Position = _targets[i].Position, Target = i });
            }
        }

        void UpdateBehaviours(FlightState state, float dt)
        {
            for (int i = 0; i < _targets.Length; i++)
            {
                if (!_targets[i].Alive) continue;
                TargetDefinition definition = Definition.Targets[i];
                TargetKind kind = definition.Kind;
                if (ObjectiveIndex >= definition.StartObjective) _targets[i].Clock += dt;
                if (_targets[i].Cover > 0f) _targets[i].Cover -= dt;

                if (definition.Parent >= 0)
                {
                    Ride(i, definition);
                    continue;
                }
                if (kind.Pursues)
                {
                    if (ObjectiveIndex >= definition.StartObjective && _targets[i].Clock >= definition.Delay && !NeedsRespawn)
                        Pursue(i, kind, state, dt);
                    continue;
                }
                if (_targets[i].FleeLeft > 0f) Flee(i, dt);
            }
        }

        /// <summary>A mounted target sits on its parent and goes down with it.</summary>
        void Ride(int index, TargetDefinition definition)
        {
            TargetState parent = _targets[definition.Parent];
            if (!parent.Alive)
            {
                _targets[index].Alive = false;
                _targets[index].Health = 0f;
                _targets[index].Firing = false;
                return;
            }
            Vector3 forward = parent.Forward.sqrMagnitude > 0.01f ? parent.Forward.normalized : Vector3.forward;
            var right = new Vector3(forward.z, 0f, -forward.x);
            _targets[index].Position = parent.Position + right * definition.Offset.x + Vector3.up * definition.Offset.y + forward * definition.Offset.z;
            _targets[index].Forward = forward;
        }

        /// <summary>An enemy aircraft closes on the drone and then holds off at gun range, never below the treetops.</summary>
        void Pursue(int index, TargetKind kind, FlightState state, float dt)
        {
            Vector3 position = _targets[index].Position;
            Vector3 want = state.Position + Vector3.up * 5f;
            Vector3 to = want - position;
            float distance = to.magnitude;
            float hold = kind.GunRange * 0.55f;
            Vector3 move = Vector3.zero;
            if (distance > hold + 4f) move = to / distance;
            else if (distance < hold - 6f) move = -to / Mathf.Max(0.01f, distance);
            // It circles rather than hangs still, which makes it a moving target.
            var orbit = new Vector3(-to.z, 0f, to.x);
            if (orbit.sqrMagnitude > 0.01f) move += orbit.normalized * 0.45f;

            position += Vector3.ClampMagnitude(move, 1f) * kind.PursuitSpeed * dt;
            float floor = Mathf.Max(_map.Height(position.x, position.z), Island.WaterLevel) + 14f;
            if (position.y < floor) position.y = floor;
            _targets[index].Position = position;
            var facing = new Vector3(to.x, 0f, to.z);
            if (facing.sqrMagnitude > 0.01f) _targets[index].Forward = facing.normalized;
        }

        void Flee(int index, float dt)
        {
            _targets[index].FleeLeft -= dt;
            Vector3 next = _targets[index].Position + _targets[index].Flee * FleeSpeed * dt;
            float ground = _map.Height(next.x, next.z);
            // It will not drive into the sea or up a wall.
            if (ground < Island.WaterLevel + 1.5f || _map.Steepness(next.x, next.z) > 0.45f)
            {
                _targets[index].FleeLeft = 0f;
                return;
            }
            next.y = ground;
            _targets[index].Position = next;
            _targets[index].Forward = _targets[index].Flee;
        }

        // ---------------------------------------------------------------- friendly guns, cannon, abilities, support

        /// <summary>Returns false when the mission ended in this step.</summary>
        bool UpdateWarfare(FlightState state, float dt, bool tracer)
        {
            if (_jamLeft > 0f)
            {
                _jamLeft -= dt;
                for (int i = 0; i < _targets.Length; i++) _targets[i].Aim = 0f;
            }
            if (_flareLeft > 0f) _flareLeft -= dt;
            if (Definition.FreeAmmo)
            {
                Rockets = Mathf.Max(Rockets, _spec.Rockets);
                Bombs = Mathf.Max(Bombs, _spec.Bombs);
                if (_spec.Cannon > 0f) CannonSeconds = _spec.CannonSeconds;
                if (_spec.Ability != Ability.None) AbilityCharges = Mathf.Max(AbilityCharges, 1);
            }

            AlliesFire(dt, tracer);
            if (Status != MissionStatus.InProgress) return false;
            FireCannon(state, dt);
            if (Status != MissionStatus.InProgress) return false;

            if (_abilityAsked) TriggerAbility(state);
            if (_supportAsked) TriggerSupport(state);
            _abilityAsked = _supportAsked = false;

            for (int i = _strikes.Count - 1; i >= 0; i--)
            {
                PendingStrike strike = _strikes[i];
                strike.Seconds -= dt;
                if (strike.Seconds > 0f)
                {
                    _strikes[i] = strike;
                    continue;
                }
                _strikes.RemoveAt(i);
                _weapon = Weapon.Support;
                // A strike aimed at a target follows it to where it is now.
                bool live = strike.Target >= 0 && _targets[strike.Target].Alive;
                Vector3 at = live ? _targets[strike.Target].Position : strike.Position;
                Explode(at, strike.Damage, strike.Radius, live ? strike.Target : -1, false);
                if (Status != MissionStatus.InProgress) return false;
            }
            return true;
        }

        /// <summary>Tanks, infantry and turrets on the player's side shoot the nearest hostile they can reach.</summary>
        void AlliesFire(float dt, bool tracer)
        {
            for (int i = 0; i < _targets.Length; i++)
            {
                TargetDefinition definition = Definition.Targets[i];
                if (!_targets[i].Alive || !definition.Friendly || definition.Kind.AllyGun <= 0f) continue;
                int victim = -1;
                float best = definition.Kind.AllyRange;
                for (int j = 0; j < _targets.Length; j++)
                {
                    if (!_targets[j].Alive || Definition.Targets[j].Friendly) continue;
                    if (ObjectiveIndex < Definition.Targets[j].StartObjective) continue;
                    float distance = Vector3.Distance(_targets[i].Position, _targets[j].Position);
                    if (distance >= best) continue;
                    best = distance;
                    victim = j;
                }
                if (victim < 0) continue;
                _targets[i].Firing = true;
                if (tracer)
                    _events.Add(new CombatEvent { Type = CombatEventType.AllyFire, Position = _targets[i].Position + Vector3.up * 2f, Target = victim });
                _weapon = Weapon.Ally;
                Damage(victim, definition.Kind.AllyGun * dt, false);
                if (Status != MissionStatus.InProgress) return;
            }
        }

        /// <summary>The cannon needs no button: it fires by itself while something is locked inside its reach.</summary>
        void FireCannon(FlightState state, float dt)
        {
            CannonFiring = false;
            if (_spec.Cannon <= 0f || CannonSeconds <= 0f || LockedTarget < 0 || state.Grounded || NeedsRespawn) return;
            if (!_targets[LockedTarget].Alive) return;
            if (Vector3.Distance(state.Position, _targets[LockedTarget].Position) > _spec.CannonRange) return;
            CannonFiring = true;
            CannonSeconds = Mathf.Max(0f, CannonSeconds - dt);
            _cannonTick += dt;
            if (_cannonTick >= 0.12f)
            {
                _cannonTick = 0f;
                _events.Add(new CombatEvent { Type = CombatEventType.CannonFire, Position = state.Position, Target = LockedTarget });
            }
            _weapon = Weapon.Cannon;
            // Shells do little to concrete and ship plate, like rockets.
            float share = Definition.Targets[LockedTarget].Kind.Structure ? 0.3f : 1f;
            Damage(LockedTarget, _spec.Cannon * share * _ordnance * dt, true);
        }

        void TriggerAbility(FlightState state)
        {
            if (AbilityCharges <= 0 || state.Grounded || NeedsRespawn || !IsCombat) return;
            switch (_spec.Ability)
            {
                case Ability.Jammer:
                    AbilityCharges--;
                    _jamLeft = JamSeconds;
                    _events.Add(new CombatEvent { Type = CombatEventType.JammerOn, Position = state.Position, Target = -1 });
                    break;
                case Ability.Flares:
                    AbilityCharges--;
                    _flareLeft = FlareSeconds;
                    for (int i = 0; i < _targets.Length; i++)
                        if (Blinded(Definition.Targets[i].Kind)) _targets[i].Aim = 0f;
                    _events.Add(new CombatEvent { Type = CombatEventType.FlaresOut, Position = state.Position, Target = -1 });
                    break;
                case Ability.GuidedMissile:
                    if (GuidedActive) return;
                    AbilityCharges--;
                    float yaw = state.YawDeg * Mathf.Deg2Rad;
                    var heading = new Vector3(Mathf.Sin(yaw), -0.12f, Mathf.Cos(yaw)).normalized;
                    _projectiles.Add(new Projectile
                    {
                        Type = ProjectileType.Guided, Position = state.Position + Vector3.down * 0.4f + heading * 1.2f,
                        Direction = heading, Target = -1, Damage = GuidedDamage * _ordnance,
                    });
                    _events.Add(new CombatEvent { Type = CombatEventType.GuidedLaunched, Position = state.Position, Target = -1 });
                    break;
            }
        }

        /// <summary>Moves the hand-flown missile one step. Returns true when it has gone off.</summary>
        bool FlyGuided(ref Projectile p, float dt)
        {
            // The stick turns it and pitches it; left alone it flies straight and sinks a little.
            float yaw = Mathf.Atan2(p.Direction.x, p.Direction.z) + _guide.x * 1.25f * dt;
            float pitch = Mathf.Asin(Mathf.Clamp(p.Direction.y, -1f, 1f)) + _guide.y * 0.9f * dt;
            pitch = Mathf.Clamp(pitch, -1.2f, 0.9f);
            p.Direction = new Vector3(Mathf.Sin(yaw) * Mathf.Cos(pitch), Mathf.Sin(pitch), Mathf.Cos(yaw) * Mathf.Cos(pitch));
            p.Position += p.Direction * GuidedSpeed * dt;

            bool hit = p.Elapsed >= GuidedSeconds ||
                       p.Position.y <= Mathf.Max(_map.Height(p.Position.x, p.Position.z), Island.WaterLevel) + 0.3f;
            int direct = -1;
            for (int i = 0; i < _targets.Length && direct < 0; i++)
            {
                if (!_targets[i].Alive || Definition.Targets[i].Friendly) continue;
                TargetKind kind = Definition.Targets[i].Kind;
                if (p.Position.y > _targets[i].Position.y + kind.Height + 1.5f || p.Position.y < _targets[i].Position.y - 2f) continue;
                if (HorizontalDistance(p.Position, _targets[i].Position) <= kind.Radius + 0.8f) direct = i;
            }
            if (!hit && direct < 0) return false;
            _weapon = Weapon.Guided;
            Explode(p.Position, p.Damage, GuidedBlast, direct, false);
            return true;
        }

        /// <summary>A cluster bomb: bomblets land in a ring around the main burst.</summary>
        void Scatter(Vector3 centre, float damage)
        {
            for (int i = 0; i < 6 && Status == MissionStatus.InProgress; i++)
            {
                float angle = i * Mathf.PI / 3f + 0.4f;
                Vector3 at = centre + new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * 15f;
                at.y = Mathf.Max(_map.Height(at.x, at.z), Island.WaterLevel);
                _strikes.Add(new PendingStrike { Position = at, Seconds = 0.15f + i * 0.08f, Damage = damage * 0.35f, Radius = 9f, Target = -1 });
            }
        }

        void TriggerSupport(FlightState state)
        {
            int tier = SupportTier;
            if (tier <= 0 || NeedsRespawn) return;
            // On what is locked; failing that, on the nearest thing the objective wants gone.
            int aim = LockedTarget;
            ObjectiveDefinition objective = CurrentObjective;
            if (aim < 0 && objective != null && objective.Type == ObjectiveType.Destroy) aim = NearestTarget(objective, state.Position);
            if (aim < 0) return;
            Streak = 0;
            Vector3 centre = _targets[aim].Position;
            _events.Add(new CombatEvent { Type = CombatEventType.SupportCalled, Position = centre, Target = aim, Size = tier });

            if (tier == 1)
            {
                // Artillery: five shells walked around the point.
                for (int i = 0; i < 5; i++)
                {
                    float angle = i * 2.4f;
                    Vector3 at = centre + new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * (i == 0 ? 0f : 11f);
                    _strikes.Add(new PendingStrike { Position = at, Seconds = 2.6f + i * 0.45f, Damage = 170f, Radius = 12f, Target = i == 0 ? aim : -1 });
                }
            }
            else if (tier == 2)
            {
                // An air strike: a stick of bombs laid along the line of approach.
                Vector3 line = centre - state.Position;
                line.y = 0f;
                line = line.sqrMagnitude > 1f ? line.normalized : Vector3.forward;
                for (int i = 0; i < 8; i++)
                {
                    Vector3 at = centre + line * ((i - 3.5f) * 11f);
                    _strikes.Add(new PendingStrike { Position = at, Seconds = 3f + i * 0.16f, Damage = 240f, Radius = 14f, Target = i == 3 ? aim : -1 });
                }
            }
            else
            {
                // A swarm: one strike drone each for the five nearest hostiles.
                var taken = new List<int>();
                for (int n = 0; n < 5; n++)
                {
                    int pick = -1;
                    float best = float.MaxValue;
                    for (int i = 0; i < _targets.Length; i++)
                    {
                        if (!_targets[i].Alive || Definition.Targets[i].Friendly || taken.Contains(i)) continue;
                        float distance = Vector3.Distance(centre, _targets[i].Position);
                        if (distance >= best) continue;
                        best = distance;
                        pick = i;
                    }
                    if (pick < 0) break;
                    taken.Add(pick);
                    _strikes.Add(new PendingStrike { Position = _targets[pick].Position, Seconds = 2.4f + n * 0.5f, Damage = 380f, Radius = 10f, Target = pick });
                }
            }
        }

        /// <summary>
        /// The aircraft is destroyed. With a spare standing by the fight goes on from the pad;
        /// without one the mission is lost. Returns false when the mission ended.
        /// </summary>
        bool AircraftDown(Vector3 position)
        {
            if (SparesLeft <= 0)
            {
                Fail(FailReason.ShotDown);
                return false;
            }
            SparesLeft--;
            Streak = 0;
            NeedsRespawn = true;
            _events.Add(new CombatEvent { Type = CombatEventType.SpareUsed, Position = position, Target = -1, Size = BombBlast });
            return true;
        }

        void CountKill(int index, bool byPlayer)
        {
            TargetKind kind = Definition.Targets[index].Kind;
            _killsBy[(int)_weapon]++;
            if (_weapon != Weapon.Ally) Streak++;
            if (kind.Pursues || Definition.Targets[index].Altitude > 0f) AirKills++;
            if (kind.Id.StartsWith("tank") || kind.Id == "apc") ArmourKills++;
        }
    }
}
