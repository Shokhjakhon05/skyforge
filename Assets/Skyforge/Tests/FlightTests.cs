using NUnit.Framework;
using Skyforge.Core;
using UnityEngine;

namespace Skyforge.Tests
{
    public class FlightModelTests
    {
        const float Dt = 1f / 120f;

        static FlightState Fly(float seconds, float dt, System.Func<float, FlightInput> input,
            FlightMode mode = FlightMode.Assist, Vector3 wind = default, FlightState start = default)
        {
            var spec = DroneSpec.Sparrow();
            FlightState state = start;
            int steps = Mathf.RoundToInt(seconds / dt);
            for (int i = 0; i < steps; i++)
                FlightModel.Step(ref state, input(i * dt), spec, mode, wind, dt);
            return state;
        }

        static FlightInput Forward(float amount) => new FlightInput { Move = new Vector2(0f, amount) };

        static float HorizontalSpeed(FlightState s) => new Vector2(s.Velocity.x, s.Velocity.z).magnitude;

        [Test]
        public void NoInput_HoldsPosition()
        {
            FlightState end = Fly(5f, Dt, _ => default);
            Assert.Less(end.Position.magnitude, 0.01f);
        }

        [Test]
        public void FullStick_ReachesTopSpeedAndDoesNotExceedIt()
        {
            var spec = DroneSpec.Sparrow();
            FlightState state = default;
            float fastest = 0f;
            for (int i = 0; i < 15 * 120; i++)
            {
                FlightModel.Step(ref state, Forward(1f), spec, FlightMode.Assist, Vector3.zero, Dt);
                fastest = Mathf.Max(fastest, HorizontalSpeed(state));
            }
            Assert.Greater(HorizontalSpeed(state), spec.MaxSpeed * 0.97f);
            Assert.Less(fastest, spec.MaxSpeed * 1.01f);
            Assert.Greater(state.Position.z, 100f, "forward stick moves along +Z at zero yaw");
        }

        [Test]
        public void Assist_BrakesWhenStickReleased()
        {
            FlightState end = Fly(9f, Dt, t => t < 6f ? Forward(1f) : default);
            Assert.Less(HorizontalSpeed(end), 0.5f);
        }

        [Test]
        public void Sport_CoastsWhenStickReleased()
        {
            FlightState end = Fly(7f, Dt, t => t < 6f ? Forward(1f) : default, FlightMode.Sport);
            Assert.Greater(HorizontalSpeed(end), 6f);
        }

        [Test]
        public void Assist_HoldsPositionInWind()
        {
            FlightState end = Fly(10f, Dt, _ => default, FlightMode.Assist, new Vector3(6f, 0f, 0f));
            Assert.Less(end.Position.magnitude, 1f);
        }

        [Test]
        public void Sport_DriftsInWind()
        {
            FlightState end = Fly(10f, Dt, _ => default, FlightMode.Sport, new Vector3(6f, 0f, 0f));
            Assert.Greater(end.Position.x, 10f);
        }

        [Test]
        public void SameFlightAtDifferentStepSizes()
        {
            System.Func<float, FlightInput> input = t => t < 3f ? Forward(1f) : Forward(-0.5f);
            FlightState fine = Fly(5f, 1f / 120f, input);
            FlightState coarse = Fly(5f, 1f / 30f, input);
            Assert.Less(Vector3.Distance(fine.Position, coarse.Position), 1.5f);
        }

        [Test]
        public void Throttle_ClimbsAndDescendsAtRatedSpeed()
        {
            var spec = DroneSpec.Sparrow();
            FlightState up = Fly(4f, Dt, _ => new FlightInput { Throttle = 1f });
            FlightState down = Fly(4f, Dt, _ => new FlightInput { Throttle = -1f });
            Assert.AreEqual(spec.MaxClimb, up.Velocity.y, 0.05f);
            Assert.AreEqual(-spec.MaxDescent, down.Velocity.y, 0.05f);
        }

        [Test]
        public void Yaw_TurnsRightAndForwardFollowsHeading()
        {
            FlightState turned = Fly(0.9f, Dt, _ => new FlightInput { Yaw = 1f });
            Assert.Greater(turned.YawDeg, 60f);
            Assert.Less(turned.YawDeg, 110f);

            var east = new FlightState { YawDeg = 90f };
            FlightState end = Fly(4f, Dt, _ => Forward(1f), FlightMode.Assist, default, east);
            Assert.Greater(end.Position.x, 20f);
            Assert.Less(Mathf.Abs(end.Position.z), 0.5f);
        }
    }

    public class IslandTests
    {
        [Test]
        public void HeightMap_InterpolatesBetweenNodes()
        {
            var map = new HeightMap(3, 20f);
            map[0, 0] = 0f; map[1, 0] = 10f;
            map[0, 1] = 0f; map[1, 1] = 10f;
            Assert.AreEqual(0f, map.Height(-10f, -10f), 1e-4f);
            Assert.AreEqual(10f, map.Height(0f, -10f), 1e-4f);
            Assert.AreEqual(5f, map.Height(-5f, -5f), 1e-4f);
            Assert.AreEqual(0f, map.Height(-500f, -500f), 1e-4f, "outside the grid uses the edge");
        }

        [Test]
        public void Island_IsLandInTheMiddleAndSeaAtTheEdge()
        {
            Island island = Island.Generate(7);
            float half = island.Map.HalfSize;
            Assert.Greater(island.Map.Height(0f, 0f), Island.WaterLevel);
            Assert.Less(island.Map.Height(half, 0f), Island.WaterLevel);
            Assert.Less(island.Map.Height(-half, -half), Island.WaterLevel);
        }

        [Test]
        public void Island_SpawnPadIsFlatDryAndClear()
        {
            Island island = Island.Generate(7);
            Vector3 spawn = island.Spawn;
            Assert.Greater(spawn.y, Island.WaterLevel + 1f);
            for (int i = 0; i < 8; i++)
            {
                float angle = i * Mathf.PI / 4f;
                float x = spawn.x + Mathf.Cos(angle) * 20f, z = spawn.z + Mathf.Sin(angle) * 20f;
                Assert.AreEqual(spawn.y, island.Map.Height(x, z), 0.2f);
            }
            Assert.IsFalse(island.Obstacles.Hits(spawn + Vector3.up, 5f));
        }

        [Test]
        public void Island_SameSeedGivesSameIsland()
        {
            Island a = Island.Generate(11), b = Island.Generate(11);
            Assert.AreEqual(a.Map.Height(123f, -45f), b.Map.Height(123f, -45f));
            Assert.AreEqual(a.Trees.Count, b.Trees.Count);
            Assert.Greater(a.Trees.Count, 200);
        }

        [Test]
        public void Island_RingsAreAboveGroundAndClearOfTrees()
        {
            Island island = Island.Generate(7);
            Assert.AreEqual(10, island.Rings.Count);
            foreach (Vector3 ring in island.Rings)
            {
                float ground = Mathf.Max(island.Map.Height(ring.x, ring.z), Island.WaterLevel);
                Assert.Greater(ring.y - ground, Island.RingRadius + 3f);
                Assert.IsFalse(island.Obstacles.Hits(ring, 1f));
            }
        }

        [Test]
        public void Obstacles_HitInsideAndMissAboveAndBeside()
        {
            var field = new ObstacleField();
            field.Add(new Obstacle { X = 100f, Z = -40f, Radius = 2f, Top = 10f });
            Assert.IsTrue(field.Hits(new Vector3(101f, 5f, -40f), 0.5f));
            Assert.IsFalse(field.Hits(new Vector3(101f, 11f, -40f), 0.5f));
            Assert.IsFalse(field.Hits(new Vector3(104f, 5f, -40f), 0.5f));
        }
    }

    public class FlightSimTests
    {
        const float Dt = 1f / 120f;

        static SimEvent Run(FlightSim sim, float seconds, FlightInput input, SimEvent stopAt = SimEvent.None)
        {
            SimEvent last = SimEvent.None;
            for (int i = 0; i < Mathf.RoundToInt(seconds / Dt); i++)
            {
                SimEvent e = sim.Step(input, Dt);
                if (e != SimEvent.None) last = e;
                if (stopAt != SimEvent.None && e == stopAt) break;
            }
            return last;
        }

        [Test]
        public void StaysOnPadUntilThrottleIsRaised()
        {
            var sim = new FlightSim(Island.Generate(7), DroneSpec.Sparrow());
            Vector3 start = sim.State.Position;
            Run(sim, 2f, new FlightInput { Move = new Vector2(1f, 1f) });
            Assert.IsTrue(sim.State.Grounded);
            Assert.AreEqual(start, sim.State.Position);

            SimEvent e = Run(sim, 2f, new FlightInput { Throttle = 1f });
            Assert.AreEqual(SimEvent.TookOff, e);
            Assert.IsFalse(sim.State.Grounded);
            Assert.Greater(sim.AltitudeAboveGround, 5f);
        }

        [Test]
        public void GentleDescentLands()
        {
            var sim = new FlightSim(Island.Generate(7), DroneSpec.Sparrow());
            Run(sim, 2f, new FlightInput { Throttle = 1f });
            SimEvent e = Run(sim, 30f, new FlightInput { Throttle = -0.5f }, SimEvent.Landed);
            Assert.AreEqual(SimEvent.Landed, e);
            Assert.IsTrue(sim.State.Grounded);
        }

        [Test]
        public void FastDescentCrashes()
        {
            var sim = new FlightSim(Island.Generate(7), DroneSpec.Sparrow());
            Run(sim, 4f, new FlightInput { Throttle = 1f });
            SimEvent e = Run(sim, 30f, new FlightInput { Throttle = -1f }, SimEvent.Crashed);
            Assert.AreEqual(SimEvent.Crashed, e);
        }

        [Test]
        public void ResetReturnsToThePad()
        {
            var sim = new FlightSim(Island.Generate(7), DroneSpec.Sparrow());
            Vector3 start = sim.State.Position;
            Run(sim, 5f, new FlightInput { Throttle = 1f, Move = new Vector2(0f, 1f) });
            sim.Reset();
            Assert.AreEqual(start, sim.State.Position);
            Assert.IsTrue(sim.State.Grounded);
        }
    }
}
