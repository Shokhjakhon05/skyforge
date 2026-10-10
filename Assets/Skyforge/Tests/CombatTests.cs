using System.Collections.Generic;
using NUnit.Framework;
using Skyforge.Core;
using UnityEngine;

namespace Skyforge.Tests
{
    /// <summary>The rules of a fight, on flat ground with no island: who can hit whom, and what ends a mission.</summary>
    public class CombatTests
    {
        const float Dt = 1f / 60f;

        static TargetDefinition Target(string kind, float x, float z, bool friendly = false) => new TargetDefinition
        {
            Id = kind + x + "_" + z, Kind = TargetKinds.Get(kind), Position = new Vector3(x, 0f, z), Friendly = friendly,
        };

        /// <summary>A mission whose one objective is to destroy the first <paramref name="wanted"/> targets.</summary>
        static MissionDefinition Mission(int wanted, params TargetDefinition[] targets)
        {
            var mission = new MissionDefinition { Id = "test", TimeGoalSeconds = 100f, BasePosition = new Vector3(0f, 0f, -500f) };
            mission.Targets.AddRange(targets);
            var objective = new ObjectiveDefinition { Type = ObjectiveType.Destroy, TextKey = "objective.destroy" };
            for (int i = 0; i < wanted; i++) objective.TargetIndices.Add(i);
            mission.Objectives.Add(objective);
            return mission;
        }

        static DroneSpec Spec() => new DroneSpec { SignalRange = 5000f };

        static FlightState Hover(float x, float y, float z, float yawDeg = 0f) =>
            new FlightState { Position = new Vector3(x, y, z), YawDeg = yawDeg };

        static void Run(MissionRun run, FlightState state, float seconds, bool rocket = false, bool bomb = false)
        {
            for (float t = 0f; t < seconds && run.Status == MissionStatus.InProgress; t += Dt)
                run.Step(state, SimEvent.None, 0.5f, 0f, Dt, rocket, default, 1f, bomb);
        }

        [Test]
        public void EveryKindOfTargetIsNamedInEveryLanguage()
        {
            foreach (TargetKind kind in TargetKinds.All)
            {
                Assert.Greater(kind.Health, 0f, kind.Id);
                Assert.Greater(kind.Radius, 0f, kind.Id);
                foreach (string language in Loc.Languages)
                {
                    Loc.SetLanguage(language);
                    Assert.AreNotEqual(kind.NameKey, Loc.T(kind.NameKey), kind.Id + " has no name in " + language);
                }
            }
            Loc.SetLanguage("en");
        }

        [Test]
        public void RocketsNeedATargetAheadAndDestroyIt()
        {
            var run = new MissionRun(Mission(1, Target("truck", 0f, 100f)), Spec());
            FlightState facingAway = Hover(0f, 30f, 0f, 180f);
            run.Step(facingAway, SimEvent.None, 0.5f, 0f, Dt);
            Assert.AreEqual(-1, run.LockedTarget, "nothing can be locked behind the drone");
            Assert.AreEqual(ActionResult.NoLock, run.Step(facingAway, SimEvent.None, 0.5f, 0f, Dt, true));

            FlightState facing = Hover(0f, 30f, 0f);
            run.Step(facing, SimEvent.None, 0.5f, 0f, Dt);
            Assert.AreEqual(0, run.LockedTarget);
            int before = run.Rockets;
            Assert.AreEqual(ActionResult.RocketFired, run.Step(facing, SimEvent.None, 0.5f, 0f, Dt, true));
            Assert.AreEqual(before - 1, run.Rockets);

            Run(run, facing, 6f, true);
            Assert.AreEqual(MissionStatus.Succeeded, run.Status);
            Assert.AreEqual(1, run.Kills);
        }

        [Test]
        public void ATargetBeyondLockRangeCannotBeLocked()
        {
            var run = new MissionRun(Mission(1, Target("truck", 0f, 400f)), Spec());
            run.Step(Hover(0f, 30f, 0f), SimEvent.None, 0.5f, 0f, Dt);
            Assert.AreEqual(-1, run.LockedTarget);
        }

        [Test]
        public void ConcreteShrugsOffRocketsAndFallsToBombs()
        {
            var run = new MissionRun(Mission(1, Target("bunker", 0f, 100f)), Spec());
            FlightState standoff = Hover(0f, 30f, 0f);
            Run(run, standoff, 0.1f);
            run.Step(standoff, SimEvent.None, 0.5f, 0f, Dt, true);
            Run(run, standoff, 4f);
            float left = run.Target(0).Health;
            Assert.Greater(left, TargetKinds.Get("bunker").Health - 30f, "one rocket barely scratches a bunker");
            Assert.Less(left, TargetKinds.Get("bunker").Health);

            // Straight down from overhead.
            FlightState above = Hover(0f, 45f, 100f);
            Assert.AreEqual(ActionResult.BombDropped, run.Step(above, SimEvent.None, 0.5f, 0f, Dt, false, default, 1f, true));
            Run(run, above, 5f);
            Assert.IsFalse(run.Target(0).Alive);
            Assert.AreEqual(MissionStatus.Succeeded, run.Status);
        }

        [Test]
        public void ABombThatLandsWideDoesLess()
        {
            var near = new MissionRun(Mission(1, Target("tank_heavy", 0f, 100f)), Spec());
            Run(near, Hover(0f, 45f, 100f), 0.1f, false, true);
            Run(near, Hover(0f, 45f, 100f), 5f);
            var wide = new MissionRun(Mission(1, Target("tank_heavy", 0f, 100f)), Spec());
            Run(wide, Hover(13f, 45f, 100f), 0.1f, false, true);
            Run(wide, Hover(13f, 45f, 100f), 5f);
            Assert.Less(near.Target(0).Health, wide.Target(0).Health);
            Assert.Less(wide.Target(0).Health, TargetKinds.Get("tank_heavy").Health, "the edge of the blast still hurts");
        }

        [Test]
        public void GunsNeedTimeToAimAndOnlyReachSoFar()
        {
            var close = new MissionRun(Mission(1, Target("aagun", 0f, 100f)), Spec());
            FlightState inside = Hover(0f, 30f, 0f);
            Run(close, inside, 1f);
            Assert.AreEqual(1f, close.Hull, "the gunner has not found the drone yet");
            Run(close, inside, 4f);
            Assert.Less(close.Hull, 1f);
            Assert.IsTrue(close.UnderFire);

            var far = new MissionRun(Mission(1, Target("aagun", 0f, 100f)), Spec());
            Run(far, Hover(0f, 30f, -100f), 6f);
            Assert.AreEqual(1f, far.Hull, "200 m is out of the gun's reach");
        }

        [Test]
        public void SpeedMakesTheDroneAHarderTarget()
        {
            var hovering = new MissionRun(Mission(1, Target("aagun", 0f, 100f)), Spec());
            Run(hovering, Hover(0f, 30f, 0f), 5f);
            var racing = new MissionRun(Mission(1, Target("aagun", 0f, 100f)), Spec());
            FlightState fast = Hover(0f, 30f, 0f);
            fast.Velocity = new Vector3(16f, 0f, 0f);
            Run(racing, fast, 5f);
            Assert.Greater(racing.Hull, hovering.Hull);
        }

        [Test]
        public void AMissileBatterySeesOnlyWhatFliesHigh()
        {
            var low = new MissionRun(Mission(1, Target("sam", 0f, 150f)), Spec());
            Run(low, Hover(0f, 25f, 0f), 6f);
            Assert.AreEqual(1f, low.Hull);
            var high = new MissionRun(Mission(1, Target("sam", 0f, 150f)), Spec());
            Run(high, Hover(0f, 70f, 0f), 6f);
            Assert.Less(high.Hull, 1f);
        }

        [Test]
        public void BeingShotToPiecesFailsTheMission()
        {
            var run = new MissionRun(Mission(1, Target("gatling", 0f, 60f)), Spec());
            Run(run, Hover(0f, 20f, 0f), 60f);
            Assert.AreEqual(MissionStatus.Failed, run.Status);
            Assert.AreEqual(FailReason.ShotDown, run.FailReason);
        }

        [Test]
        public void AFuelTruckTakesItsNeighbourWithIt()
        {
            var run = new MissionRun(Mission(2, Target("truck_fuel", 0f, 100f), Target("truck", 9f, 100f)), Spec());
            FlightState state = Hover(0f, 30f, 0f);
            Run(run, state, 0.1f);
            Assert.AreEqual(0, run.LockedTarget, "the fuel truck is dead ahead");
            run.Step(state, SimEvent.None, 0.5f, 0f, Dt, true);
            Run(run, state, 5f);
            Assert.AreEqual(MissionStatus.Succeeded, run.Status, "one rocket, two wrecks");
            Assert.AreEqual(2, run.Kills);
        }

        [Test]
        public void StrikeDronesAreSpentOneByOne()
        {
            MissionDefinition mission = Mission(2, Target("tank_heavy", 0f, 100f), Target("bunker", 80f, 100f));
            mission.StrikeDrones = 3;
            var run = new MissionRun(mission, Spec().AsStrikeDrone());
            Assert.IsTrue(run.Kamikaze);
            Assert.AreEqual(0, run.Rockets, "a strike drone carries nothing but itself");

            run.Step(Hover(0f, 2f, 100f), SimEvent.None, 0.5f, 0f, Dt);
            Assert.IsFalse(run.Target(0).Alive);
            Assert.IsTrue(run.NeedsRespawn);
            Assert.AreEqual(2, run.DronesLeft);
            Assert.AreEqual(MissionStatus.InProgress, run.Status);

            run.Respawned();
            run.Step(Hover(80f, 2f, 100f), SimEvent.None, 0.5f, 0f, Dt);
            Assert.AreEqual(MissionStatus.Succeeded, run.Status);
            Assert.IsTrue(run.CleanFinish, "one drone was left over");
        }

        [Test]
        public void WastingEveryStrikeDroneFails()
        {
            MissionDefinition mission = Mission(1, Target("tank_heavy", 0f, 100f));
            mission.StrikeDrones = 2;
            var run = new MissionRun(mission, Spec().AsStrikeDrone());
            run.Step(Hover(300f, 1f, 300f), SimEvent.Crashed, 0.5f, 6f, Dt);
            Assert.IsTrue(run.NeedsRespawn);
            run.Respawned();
            run.Step(Hover(300f, 1f, 300f), SimEvent.Crashed, 0.5f, 6f, Dt);
            Assert.AreEqual(MissionStatus.Failed, run.Status);
            Assert.AreEqual(FailReason.OutOfDrones, run.FailReason);
        }

        [Test]
        public void AMoverThatReachesTheEndOfItsRoadHasEscaped()
        {
            TargetDefinition car = Target("staff_car", 0f, 100f);
            car.Path = new List<Vector3> { new Vector3(0f, 0f, 100f), new Vector3(0f, 0f, 160f) };
            car.Speed = 10f;
            var run = new MissionRun(Mission(1, car), Spec());
            Run(run, Hover(0f, 30f, -300f), 10f);
            Assert.AreEqual(MissionStatus.Failed, run.Status);
            Assert.AreEqual(FailReason.Escaped, run.FailReason);
        }

        [Test]
        public void LosingWhatIsProtectedFails()
        {
            TargetDefinition raider = Target("jeep", 0f, 100f);
            TargetDefinition outpost = Target("outpost", 30f, 100f, true);
            MissionDefinition mission = Mission(1, raider, outpost);
            mission.Targets[0].Attacks = 1;
            var run = new MissionRun(mission, Spec());
            Run(run, Hover(0f, 30f, -400f), 120f);
            Assert.AreEqual(MissionStatus.Failed, run.Status);
            Assert.AreEqual(FailReason.AllyLost, run.FailReason);
        }

        [Test]
        public void LandingOnThePadRearmsAndChargesTheBattery()
        {
            MissionDefinition mission = Mission(1, Target("truck", 0f, 100f));
            mission.BasePosition = Vector3.zero;
            var run = new MissionRun(mission, Spec());
            FlightState state = Hover(0f, 30f, 0f);
            Run(run, state, 0.1f);
            run.Step(state, SimEvent.None, 0.5f, 0f, Dt, true);
            Run(run, state, 0.2f);
            Assert.Less(run.Rockets, Spec().Rockets);
            Run(run, Hover(0f, 30f, -200f), 30f);
            float used = run.Battery;
            Assert.Less(used, 1f);

            FlightState landed = Hover(2f, 0.45f, 1f);
            landed.Grounded = true;
            run.Step(landed, SimEvent.Landed, 0f, 1f, Dt);
            Assert.AreEqual(Spec().Rockets, run.Rockets);
            Assert.AreEqual(1f, run.Battery);
        }

        [Test]
        public void TheThirdStarInAFightIsHalfTheHull()
        {
            var run = new MissionRun(Mission(1, Target("aagun", 0f, 100f)), Spec());
            FlightState state = Hover(0f, 30f, 0f);
            Run(run, state, 3f);
            Assert.Less(run.Hull, 1f);
            Assert.Greater(run.Hull, MissionRun.CleanHull);
            Assert.IsTrue(run.CleanFinish, "scratched paint does not cost the star in combat");
        }

        [Test]
        public void EveryDroneCarriesSomethingToFightWith()
        {
            foreach (DroneDefinition drone in DroneCatalog.All)
            {
                Assert.Greater(drone.Spec.Rockets, 0, drone.Id);
                Assert.Greater(drone.Spec.Rockets * drone.Spec.RocketDamage + drone.Spec.Bombs * drone.Spec.BombDamage, 300f, drone.Id);
                Assert.Greater(drone.Spec.LockRange, 100f, drone.Id);
                Assert.IsFalse(string.IsNullOrEmpty(drone.Look.Model), drone.Id + " has no model");
            }
        }
    }
}
