using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using Skyforge.Core;
using UnityEngine;

namespace Skyforge.Tests
{
    /// <summary>
    /// Flies every mission of the campaign with a simple autopilot, in the real flight simulation
    /// and mission rules, with the starter drone. Nobody has played most of these missions by hand;
    /// this is what shows that each one can be finished at all: that the battery lasts, the signal
    /// reaches, the radars can be slipped and the wind can be flown in.
    /// The autopilot knows where things are, which a player does not, so it says nothing about how
    /// hard a mission is to work out. Its times are printed to the log to judge the time goals by.
    /// </summary>
    public class PlaythroughTests
    {
        const float Dt = 1f / 60f;
        const float TimeLimit = 900f;
        /// <summary>Set SKYFORGE_TRACE to a mission id to log that flight twice a second.</summary>
        static readonly string Trace = System.Environment.GetEnvironmentVariable("SKYFORGE_TRACE");

        sealed class Pilot
        {
            readonly Island _island;
            readonly MissionDefinition _mission;
            readonly FlightSim _sim;
            readonly MissionRun _run;
            readonly Weather _weather;
            readonly float _zoom;
            float _time;
            float _noLock;

            public MissionRun Run => _run;
            public float Time => _time;
            public string Where => _sim.State.Position + " pad " + _island.Spawn;

            public Pilot(Island island, MissionDefinition mission, DroneSpec spec)
            {
                _island = island;
                _mission = mission;
                if (mission.StrikeDrones > 0) spec = spec.AsStrikeDrone();
                _sim = new FlightSim(island, spec.WithPayload(mission.Payload));
                _run = new MissionRun(mission, _sim.Spec, island.Map);
                _weather = WeatherCatalog.Get(mission.WeatherId);
                _zoom = spec.MaxZoom;
            }

            public void Fly()
            {
                while (_run.Status == MissionStatus.InProgress && _time < TimeLimit)
                {
                    _sim.Wind = _weather.WindAt(_time);
                    if (_run.NeedsRespawn)
                    {
                        // The next strike drone is on the pad.
                        _sim.Reset();
                        _run.Respawned();
                    }
                    FlightInput input = Decide(out bool action, out bool bomb);
                    SimEvent simEvent = _sim.Step(input, Dt);
                    FlightState state = _sim.State;
                    float load = state.Grounded
                        ? 0f
                        : Mathf.Clamp01(0.45f + state.TiltDeg.magnitude / _sim.Spec.MaxTiltDeg * 0.35f + state.Velocity.y / _sim.Spec.MaxClimb * 0.3f);
                    _run.Step(state, simEvent, load, _sim.LastTouchdownSpeed, Dt, action, _sim.Wind, _zoom, bomb);
                    _time += Dt;
                    if (Trace == _mission.Id && Mathf.FloorToInt(_time * 2f) != Mathf.FloorToInt((_time - Dt) * 2f))
                    {
                        string targets = "";
                        for (int i = 0; i < _run.TargetCount; i++)
                            targets += $" {_mission.Targets[i].Id}:{_run.Target(i).Health:0}@{Vector3.Distance(state.Position, _run.Target(i).Position):0}{(_run.Target(i).Firing ? "!" : "")}";
                        Debug.Log($"trace {_time:0.0} pos {state.Position} agl {state.Position.y - Ground(state.Position.x, state.Position.z):0} lock {_run.LockedTarget} r{_run.Rockets} b{_run.Bombs} hull {_run.Hull:0.00} step {_run.ObjectiveIndex}{targets}");
                    }
                }
            }

            float Ground(float x, float z) => Mathf.Max(_island.Map.Height(x, z), Island.WaterLevel);

            /// <summary>Inside a radar's circle, and for a margin around it, the pilot stays under its floor.</summary>
            bool UnderRadar(Vector3 position, float margin)
            {
                foreach (ZoneDefinition zone in _mission.Zones)
                {
                    float dx = position.x - zone.Position.x, dz = position.z - zone.Position.z;
                    if (Mathf.Sqrt(dx * dx + dz * dz) < zone.Radius + margin) return true;
                }
                return false;
            }

            /// <summary>
            /// Where to head for: the goal itself, or, if a radar's circle lies across the straight
            /// line to it, the next point on a ring that passes outside every radar. This is the
            /// route a player takes when the ground is too rough to fly under the radar.
            /// </summary>
            Vector3 Around(Vector3 position, Vector3 goal)
            {
                if (_mission.Zones.Count == 0 || UnderRadar(goal, 30f) || UnderRadar(position, 20f)) return goal;

                bool blocked = false;
                float ring = 0f;
                foreach (ZoneDefinition zone in _mission.Zones)
                {
                    var centre = new Vector2(zone.Position.x, zone.Position.z);
                    ring = Mathf.Max(ring, centre.magnitude + zone.Radius + 45f);
                    var a = new Vector2(position.x, position.z);
                    var b = new Vector2(goal.x, goal.z);
                    Vector2 along = b - a;
                    float t = along.sqrMagnitude > 0.01f ? Mathf.Clamp01(Vector2.Dot(centre - a, along) / along.sqrMagnitude) : 0f;
                    if ((a + along * t - centre).magnitude < zone.Radius + 30f) blocked = true;
                }
                if (!blocked) return goal;

                float here = Mathf.Atan2(position.z, position.x) * Mathf.Rad2Deg;
                float there = Mathf.Atan2(goal.z, goal.x) * Mathf.Rad2Deg;
                float turn = Mathf.Clamp(Mathf.DeltaAngle(here, there), -25f, 25f);
                float next = (here + turn) * Mathf.Deg2Rad;
                return new Vector3(Mathf.Cos(next) * ring, goal.y, Mathf.Sin(next) * ring);
            }

            FlightInput Decide(out bool action, out bool bomb)
            {
                action = false;
                bomb = false;
                FlightState state = _sim.State;
                ObjectiveDefinition objective = _run.CurrentObjective;
                if (objective == null) return default;
                Vector3 position = state.Position;
                Vector3 target = _run.TargetPosition(objective);
                Vector3 via = Around(position, target);
                bool around = via != target;
                // Start descending well before the circle, because the drone sinks slowly. A strike
                // needs height, so there the pilot climbs as soon as it is clear of the circle itself;
                // and a pilot going around the radars has no reason to come down at all.
                bool low = UnderRadar(position, objective.Type == ObjectiveType.Strike || around ? 12f : 130f);
                // Under a radar: just above the tallest trees, well under the radar's floor.
                float cruise = low ? 11f : 30f;

                switch (objective.Type)
                {
                    case ObjectiveType.TakeOff:
                        return new FlightInput { Throttle = 1f };

                    case ObjectiveType.Land:
                    {
                        float away = Flat(position, target);
                        if (around) return Steer(via, via.y + 30f, via, false, cruise);
                        if (away > 2.5f) return Steer(target, Ground(target.x, target.z) + (low ? 11f : 20f), target, false, cruise);
                        // Over the spot: hold it and sink gently.
                        FlightInput hold = Steer(target, position.y, target, true, cruise);
                        hold.Throttle = -0.35f;
                        return hold;
                    }

                    case ObjectiveType.Reach:
                    case ObjectiveType.Find:
                    case ObjectiveType.Scan:
                        if (around) return Steer(via, via.y, via, false, cruise);
                        return Steer(target, target.y, target, Flat(position, target) < 60f, cruise);

                    case ObjectiveType.Observe:
                    case ObjectiveType.Photo:
                    case ObjectiveType.Mark:
                    {
                        if (objective.Type == ObjectiveType.Mark) target = NextThreat(objective, position);
                        // Stand off a little and above, looking at it.
                        Vector3 back = position - target;
                        back.y = 0f;
                        back = back.sqrMagnitude < 1f ? Vector3.back : back.normalized;
                        Vector3 post = target + back * 22f;
                        float height = Ground(target.x, target.z) + (low ? 11f : 22f);
                        action = objective.Type == ObjectiveType.Photo && _run.TargetFramed;
                        return Steer(post, height, target, Flat(position, post) < 40f, cruise);
                    }

                    case ObjectiveType.Follow:
                    {
                        Vector3 back = position - target;
                        back.y = 0f;
                        back = back.sqrMagnitude < 1f ? Vector3.back : back.normalized;
                        float middle = (objective.MinDistance + objective.MaxDistance) * 0.5f;
                        return Steer(target + back * middle, Ground(position.x, position.z) + 26f, target, false, cruise);
                    }

                    case ObjectiveType.Destroy:
                        return Attack(objective, state, low, cruise, out action, out bomb);

                    case ObjectiveType.Strike:
                    {
                        // Aim at where the target will be when the munition lands, then null out the predicted miss.
                        Vector3 impact = _run.PredictImpact(state, out float fall);
                        Vector3 aim = target;
                        if (!string.IsNullOrEmpty(objective.ActorId) && _run.TryGetActor(objective.ActorId, out ActorState actor) && !actor.Arrived)
                        {
                            foreach (ActorDefinition definition in _mission.Actors)
                                if (definition.Id == objective.ActorId) aim += actor.Forward * definition.Speed * fall;
                        }
                        Vector3 miss = aim - impact;
                        miss.y = 0f;
                        float height = Ground(aim.x, aim.z) + objective.MinAltitude + 12f;
                        float above = position.y - Ground(position.x, position.z);
                        action = miss.magnitude < objective.Radius * 0.35f && above > objective.MinAltitude + 2f && _run.ShotsLeft > 0 && !_run.Shot.Active;
                        return Steer(position + miss, low ? Ground(position.x, position.z) + 11f : height, aim, true, cruise);
                    }

                    default:
                        // Arrive: wait where we are.
                        return Steer(position, Ground(position.x, position.z) + cruise, position + Vector3.forward, true, cruise);
                }
            }

            /// <summary>
            /// How a careful player fights: guns first, from as far as a rocket reaches; bombs for
            /// what rockets barely scratch; back to the pad when the racks are empty.
            /// </summary>
            FlightInput Attack(ObjectiveDefinition objective, FlightState state, bool low, float cruise, out bool action, out bool bomb)
            {
                action = false;
                bomb = false;
                Vector3 position = state.Position;
                if (state.Grounded) return new FlightInput { Throttle = 1f };

                int index = _run.NearestTarget(objective, position);
                if (index < 0) return default;

                if (_run.Kamikaze)
                {
                    // Cruise over, then put the drone into the target.
                    TargetState mark = _run.Target(index);
                    float away = Flat(position, mark.Position);
                    if (away > 70f) return Steer(mark.Position, Ground(mark.Position.x, mark.Position.z) + 24f, mark.Position, false, 24f);
                    // Down a slope that ends in the target, aiming through it so the drone does not slow.
                    Vector3 through = mark.Position - position;
                    through.y = 0f;
                    through = through.sqrMagnitude > 0.01f ? through.normalized : Vector3.forward;
                    FlightInput dive = Steer(mark.Position + through * 25f, position.y, mark.Position, true, 24f);
                    float slope = mark.Position.y + 1.5f + Mathf.Max(0f, away - 8f) * 0.45f;
                    if (away > 15f) slope = Mathf.Max(slope, Ground(position.x, position.z) + 3f);
                    dive.Throttle = Mathf.Clamp((slope - position.y) * 0.6f, -1f, 1f);
                    return dive;
                }

                // Anything that shoots near the target goes first, a missile battery before a gun.
                Vector3 centre = _run.Target(index).Position;
                bool running = _mission.Targets[index].Path.Count >= 2 && _mission.Targets[index].Attacks < 0;
                float bestScore = float.MaxValue;
                bool sams = false, guarded = false;
                for (int i = 0; i < _run.TargetCount; i++)
                {
                    TargetDefinition definition = _mission.Targets[i];
                    TargetState guard = _run.Target(i);
                    if (!guard.Alive || definition.Friendly || !definition.Kind.Armed) continue;
                    if (Flat(guard.Position, centre) > 190f) continue;
                    if (definition.Kind.MinAltitude > 0f) sams = true;
                    guarded = true;
                    // Concrete last: it needs a bombing run, and that is flown when the guns are quiet.
                    float score = Flat(guard.Position, position) - (definition.Kind.MinAltitude > 0f ? 1000f : 0f) -
                                  definition.Kind.GunRange + (definition.Kind.Structure ? 500f : 0f);
                    if (score >= bestScore || _run.Rockets <= 0 || running) continue;
                    bestScore = score;
                    index = i;
                }

                TargetState target = _run.Target(index);
                TargetKind kind = _mission.Targets[index].Kind;
                bool useBombs = _run.Bombs > 0 && (kind.Structure || _run.Rockets <= 0);
                if (_run.Rockets <= 0 && (guarded || !useBombs))
                {
                    // Out of everything: home to rearm.
                    Vector3 pad = _mission.BasePosition;
                    Vector3 round = Around(position, pad);
                    if (round != pad) return Steer(round, round.y + 30f, round, false, cruise);
                    if (Flat(position, pad) > 2.5f) return Steer(pad, Ground(pad.x, pad.z) + (low ? 11f : 20f), pad, false, cruise);
                    FlightInput hold = Steer(pad, position.y, pad, true, cruise);
                    hold.Throttle = -0.35f;
                    return hold;
                }

                // Under a missile battery's floor while one is alive.
                float height = sams ? 22f : 30f;
                if (low) height = 11f;
                if (useBombs)
                {
                    Vector3 impact = _run.PredictImpact(state, out float fall);
                    Vector3 aim = target.Position;
                    if (_mission.Targets[index].Path.Count >= 2 && !target.Arrived) aim += target.Forward * _mission.Targets[index].Speed * fall;
                    Vector3 miss = aim - impact;
                    miss.y = 0f;
                    bomb = miss.magnitude < Mathf.Max(2f, kind.Radius * 0.45f) && position.y - Ground(position.x, position.z) > 12f;
                    // Keeping pace with a mover takes a standing lead, or the drone trails it for ever.
                    Vector3 pace = _mission.Targets[index].Path.Count >= 2 && !target.Arrived
                        ? target.Forward * _mission.Targets[index].Speed * 2.86f
                        : Vector3.zero;
                    return Steer(position + miss + pace, Ground(aim.x, aim.z) + height + 6f, aim, true, cruise);
                }

                // Stand off at rocket range. If a hill hides the target, climb for a view of it; under a
                // missile battery's floor there is no climbing, so there the pilot closes in instead.
                float standoff = _sim.Spec.LockRange * 0.85f;
                if (sams || low) standoff = Mathf.Max(45f, standoff - _noLock * 8f);
                else height += Mathf.Min(70f, _noLock * 6f);
                // The range is measured along the slant, so height eats into the distance over the ground.
                float reach = _sim.Spec.LockRange * 0.9f;
                float drop = Ground(target.Position.x, target.Position.z) + height - target.Position.y;
                standoff = Mathf.Min(standoff, Mathf.Sqrt(Mathf.Max(900f, reach * reach - drop * drop)));
                Vector3 back = position - target.Position;
                back.y = 0f;
                back = back.sqrMagnitude < 1f ? Vector3.back : back.normalized;
                Vector3 post = target.Position + back * standoff;
                bool locked = _run.LockedTarget == index;
                if (Flat(position, post) < 20f && !locked) _noLock += Dt;
                else if (locked) _noLock = Mathf.Max(0f, _noLock - Dt * 0.5f);
                // No second rocket at something the first one is about to finish.
                float coming = 0f;
                foreach (Projectile projectile in _run.Projectiles)
                    if (projectile.Type == ProjectileType.Rocket && projectile.Target == index) coming += projectile.Damage;
                action = locked && coming < target.Health;
                Vector3 via = Around(position, post);
                if (via != post) return Steer(via, via.y, via, false, cruise);
                return Steer(post, Ground(target.Position.x, target.Position.z) + height, target.Position, false, Mathf.Min(cruise, height));
            }

            Vector3 NextThreat(ObjectiveDefinition objective, Vector3 position)
            {
                for (int i = 0; i < objective.Targets.Count; i++)
                    if (!_run.IsMarked(i)) return objective.Targets[i];
                return position;
            }

            static float Flat(Vector3 a, Vector3 b)
            {
                float dx = a.x - b.x, dz = a.z - b.z;
                return Mathf.Sqrt(dx * dx + dz * dz);
            }

            /// <summary>
            /// Stick positions that take the drone towards a point at a height, nose towards
            /// <paramref name="lookAt"/>. Unless told the height is exact, it also clears the
            /// terrain ahead by the cruise margin.
            /// </summary>
            FlightInput Steer(Vector3 goal, float height, Vector3 lookAt, bool exactHeight, float cruise)
            {
                FlightState state = _sim.State;
                Vector3 position = state.Position;
                var to = new Vector3(goal.x - position.x, 0f, goal.z - position.z);
                float distance = to.magnitude;
                Vector3 direction = distance > 0.01f ? to / distance : Vector3.zero;

                float yaw = state.YawDeg * Mathf.Deg2Rad;
                var forward = new Vector3(Mathf.Sin(yaw), 0f, Mathf.Cos(yaw));
                var right = new Vector3(Mathf.Cos(yaw), 0f, -Mathf.Sin(yaw));
                float speed = Mathf.Clamp01(distance * 0.35f / _sim.Spec.MaxSpeed);
                var move = new Vector2(Vector3.Dot(direction, right), Vector3.Dot(direction, forward)) * speed;

                var look = new Vector3(lookAt.x - position.x, 0f, lookAt.z - position.z);
                float turn = 0f;
                if (look.sqrMagnitude > 4f)
                {
                    float bearing = Mathf.Atan2(look.x, look.z) * Mathf.Rad2Deg;
                    turn = Mathf.Clamp(Mathf.DeltaAngle(state.YawDeg, bearing) / 25f, -1f, 1f);
                }

                // The highest ground under the drone and a little way ahead of it.
                float reach = cruise < 20f ? 8f : 45f;
                float ground = Ground(position.x, position.z);
                ground = Mathf.Max(ground, Ground(position.x + direction.x * reach * 0.5f, position.z + direction.z * reach * 0.5f));
                ground = Mathf.Max(ground, Ground(position.x + direction.x * reach, position.z + direction.z * reach));

                float wanted;
                if (exactHeight) wanted = Mathf.Max(height, ground + 6f);
                else if (cruise < 20f) wanted = ground + cruise;
                else wanted = Mathf.Max(height, ground + cruise);

                // What any pilot does: climb first, then go. Below the wanted height the drone
                // slows down, and well below it, it stops and climbs. Under a radar it creeps.
                float lacking = wanted - position.y;
                move *= Mathf.Clamp01(1f - (lacking - 3f) / 10f);
                if (cruise < 20f) move *= 0.4f;

                float throttle = Mathf.Clamp(lacking * 0.5f, cruise < 20f ? -1f : -0.6f, 1f);
                return new FlightInput { Move = move, Yaw = turn, Throttle = throttle };
            }
        }

        static void PlayRegion(RegionDefinition region)
        {
            Island island = Island.Generate(region.Seed, region.Biome);
            DroneSpec spec = DroneCatalog.Get(DroneCatalog.Starter).Spec;
            string folder = Path.Combine(Application.dataPath, "Skyforge/Resources/Missions", region.Id);
            var problems = new List<string>();
            var missions = new List<MissionDefinition>();
            foreach (string file in Directory.GetFiles(folder, "*.json"))
            {
                MissionDefinition mission = MissionBuilder.Parse(File.ReadAllText(file), island, new List<string>());
                if (mission != null) missions.Add(mission);
            }
            missions.Sort((a, b) => a.Order.CompareTo(b.Order));

            foreach (MissionDefinition mission in missions)
            {
                var pilot = new Pilot(island, mission, spec);
                pilot.Fly();
                MissionRun run = pilot.Run;
                MissionOutcome outcome = MissionOutcome.From(run);
                string line = $"[Skyforge] playthrough {mission.Id}: {run.Status} {run.FailReason}, " +
                              $"time {pilot.Time:0}/{mission.TimeGoalSeconds:0} s, stars {outcome.Stars}, " +
                              $"battery {run.Battery * 100f:0}%, hull {run.Hull * 100f:0}%, step {run.ObjectiveIndex}/{mission.Objectives.Count}";
                Debug.Log(line);
                if (run.Status != MissionStatus.Succeeded) problems.Add(line + " at " + pilot.Where);
            }
            Assert.IsEmpty(problems, string.Join("\n", problems));
        }

        [Test]
        public void ArveliaCanBeFlownThrough() => PlayRegion(RegionCatalog.Get("arvelia"));

        [Test]
        public void KharadCanBeFlownThrough() => PlayRegion(RegionCatalog.Get("kharad"));

        [Test]
        public void NordheimCanBeFlownThrough() => PlayRegion(RegionCatalog.Get("nordheim"));
    }
}
