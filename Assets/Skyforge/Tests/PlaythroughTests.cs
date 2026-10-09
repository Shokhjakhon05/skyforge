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

        sealed class Pilot
        {
            readonly Island _island;
            readonly MissionDefinition _mission;
            readonly FlightSim _sim;
            readonly MissionRun _run;
            readonly Weather _weather;
            readonly float _zoom;
            float _time;

            public MissionRun Run => _run;
            public float Time => _time;

            public Pilot(Island island, MissionDefinition mission, DroneSpec spec)
            {
                _island = island;
                _mission = mission;
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
                    FlightInput input = Decide(out bool action);
                    SimEvent simEvent = _sim.Step(input, Dt);
                    FlightState state = _sim.State;
                    float load = state.Grounded
                        ? 0f
                        : Mathf.Clamp01(0.45f + state.TiltDeg.magnitude / _sim.Spec.MaxTiltDeg * 0.35f + state.Velocity.y / _sim.Spec.MaxClimb * 0.3f);
                    _run.Step(state, simEvent, load, _sim.LastTouchdownSpeed, Dt, action, _sim.Wind, _zoom);
                    _time += Dt;
                }
            }

            float Ground(float x, float z) => Mathf.Max(_island.Map.Height(x, z), Island.WaterLevel);

            /// <summary>Inside a radar's circle the pilot stays under its floor.</summary>
            bool UnderRadar(Vector3 position)
            {
                foreach (ZoneDefinition zone in _mission.Zones)
                {
                    float dx = position.x - zone.Position.x, dz = position.z - zone.Position.z;
                    if (Mathf.Sqrt(dx * dx + dz * dz) < zone.Radius + 25f) return true;
                }
                return false;
            }

            FlightInput Decide(out bool action)
            {
                action = false;
                FlightState state = _sim.State;
                ObjectiveDefinition objective = _run.CurrentObjective;
                if (objective == null) return default;
                Vector3 position = state.Position;
                Vector3 target = _run.TargetPosition(objective);
                bool low = UnderRadar(position);
                float cruise = low ? 14f : 30f;

                switch (objective.Type)
                {
                    case ObjectiveType.TakeOff:
                        return new FlightInput { Throttle = 1f };

                    case ObjectiveType.Land:
                    {
                        float away = Flat(position, target);
                        if (away > 2.5f) return Steer(target, Ground(target.x, target.z) + (low ? 14f : 20f), target, false, cruise);
                        // Over the spot: hold it and sink gently.
                        FlightInput hold = Steer(target, position.y, target, true, cruise);
                        hold.Throttle = -0.35f;
                        return hold;
                    }

                    case ObjectiveType.Reach:
                    case ObjectiveType.Find:
                    case ObjectiveType.Scan:
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
                        float height = Ground(target.x, target.z) + (low ? 13f : 22f);
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
                        return Steer(position + miss, low ? Ground(position.x, position.z) + 14f : height, aim, true, cruise);
                    }

                    default:
                        // Arrive: wait where we are.
                        return Steer(position, Ground(position.x, position.z) + cruise, position + Vector3.forward, true, cruise);
                }
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

                float wanted = height;
                if (!exactHeight)
                {
                    float reach = cruise < 20f ? 12f : 45f;
                    float ground = Ground(position.x, position.z);
                    ground = Mathf.Max(ground, Ground(position.x + direction.x * reach * 0.5f, position.z + direction.z * reach * 0.5f));
                    ground = Mathf.Max(ground, Ground(position.x + direction.x * reach, position.z + direction.z * reach));
                    wanted = cruise < 20f ? ground + cruise : Mathf.Max(height, ground + cruise);
                }
                float throttle = Mathf.Clamp((wanted - position.y) * 0.5f, -0.6f, 1f);
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
                if (run.Status != MissionStatus.Succeeded) problems.Add(line);
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
