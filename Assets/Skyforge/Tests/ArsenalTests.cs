using System.Collections.Generic;
using NUnit.Framework;
using Skyforge.Core;
using UnityEngine;

namespace Skyforge.Tests
{
    /// <summary>Free fire, loot, chains of kills, the vehicles that drive and the sniper's rifle.</summary>
    public class ArsenalTests
    {
        const float Dt = 1f / 60f;

        static TargetDefinition Target(string kind, float x, float z) => new TargetDefinition
        {
            Id = kind + x + "_" + z, Kind = TargetKinds.Get(kind), Position = new Vector3(x, 0f, z),
        };

        static MissionDefinition Mission(string id, params TargetDefinition[] targets)
        {
            var mission = new MissionDefinition { Id = id, TimeGoalSeconds = 100f, BasePosition = new Vector3(0f, 0f, -500f) };
            mission.Targets.AddRange(targets);
            var objective = new ObjectiveDefinition { Type = ObjectiveType.Destroy, TextKey = "objective.destroy" };
            for (int i = 0; i < targets.Length; i++) objective.TargetIndices.Add(i);
            mission.Objectives.Add(objective);
            return mission;
        }

        static FlightState Hover(float x, float y, float z, float yawDeg = 0f) =>
            new FlightState { Position = new Vector3(x, y, z), YawDeg = yawDeg };

        static void Run(MissionRun run, FlightState state, float seconds, bool rocket = false)
        {
            for (float t = 0f; t < seconds && run.Status == MissionStatus.InProgress; t += Dt)
                run.Step(state, SimEvent.None, 0.5f, 0f, Dt, rocket);
        }

        [Test]
        public void ATargetIsLockedFromWellOutsideItsGunRange()
        {
            var run = new MissionRun(Mission("far", Target("tank", 0f, 230f)), new DroneSpec { SignalRange = 5000f });
            Run(run, Hover(0f, 30f, 0f), 0.1f);
            Assert.AreEqual(0, run.LockedTarget);
            Assert.Greater(new DroneSpec().LockRange, TargetKinds.Get("aagun").GunRange * 1.8f);
        }

        [Test]
        public void ARocketFiredWithoutALockFliesWhereTheSightLooks()
        {
            // The truck is behind the drone, so nothing is locked; the sight is put on it by hand.
            var spec = new DroneSpec { SignalRange = 5000f, RocketDamage = 200f };
            var run = new MissionRun(Mission("free", Target("truck", 0f, -120f)), spec);
            Run(run, Hover(0f, 30f, 0f), 0.1f);
            Assert.AreEqual(-1, run.LockedTarget);
            Assert.AreEqual(ActionResult.NoLock, run.Step(Hover(0f, 30f, 0f), SimEvent.None, 0.5f, 0f, Dt, true));
            Run(run, Hover(0f, 30f, 0f), 1f);
            run.FreeAim = new Vector3(0f, 1f, -120f);
            Assert.AreEqual(ActionResult.RocketFired, run.Step(Hover(0f, 30f, 0f), SimEvent.None, 0.5f, 0f, Dt, true));
            Run(run, Hover(0f, 30f, 0f), 4f);
            Assert.IsFalse(run.Target(0).Alive);
        }

        [Test]
        public void KillsLeaveLootAndFlyingThroughItTakesIt()
        {
            var targets = new List<TargetDefinition>();
            for (int i = 0; i < 12; i++) targets.Add(Target("tank", (i - 6) * 40f, 150f));
            var spec = new DroneSpec { SignalRange = 5000f, RocketDamage = 2000f, Rockets = 40, FireInterval = 0.1f };
            var run = new MissionRun(Mission("loot", targets.ToArray()), spec);
            int dropped = 0;
            for (int i = 0; i < 12; i++)
            {
                var state = Hover((i - 6) * 40f, 30f, 0f);
                for (float t = 0f; t < 4f && run.Target(i).Alive; t += Dt)
                {
                    run.Step(state, SimEvent.None, 0.5f, 0f, Dt, run.LockedTarget == i && run.Projectiles.Count == 0);
                    foreach (CombatEvent e in run.Events)
                        if (e.Type == CombatEventType.PickupDropped) dropped++;
                }
                if (i == 10) break;
            }
            Assert.Greater(dropped, 2);
            Assert.AreEqual(dropped, run.Pickups.Count);
            int before = run.Pickups.Count;
            Vector3 at = run.Pickups[0].Position;
            Run(run, Hover(at.x, at.y + 25f, at.z), 0.1f);
            Assert.Less(run.Pickups.Count, before);
            Assert.GreaterOrEqual(run.PickupsTaken, 1);
        }

        [Test]
        public void KillsInQuickSuccessionMakeAChain()
        {
            var spec = new DroneSpec { SignalRange = 5000f, RocketDamage = 2000f, FireInterval = 0.1f };
            var run = new MissionRun(Mission("chain", Target("truck", -6f, 120f), Target("truck", 6f, 120f), Target("truck", 400f, 120f)), spec);
            int best = 0;
            for (float t = 0f; t < 5f; t += Dt)
            {
                run.Step(Hover(0f, 30f, 0f), SimEvent.None, 0.5f, 0f, Dt, run.LockedTarget >= 0 && run.Projectiles.Count == 0);
                foreach (CombatEvent e in run.Events)
                    if (e.Type == CombatEventType.ChainKill) best = Mathf.Max(best, (int)e.Size);
            }
            Assert.AreEqual(2, best);
            Assert.AreEqual(2, run.BestChain);
        }

        [Test]
        public void AVehicleDrivesOverTheGroundAndStopsAtTheWater()
        {
            Island island = Island.Generate(7);
            DroneSpec spec = DroneCatalog.Get(DroneCatalog.StarterVehicle).Spec;
            Assert.IsTrue(spec.Ground);
            var sim = new FlightSim(island, spec);
            Vector3 start = sim.State.Position;
            float farthest = 0f;
            for (float t = 0f; t < 240f; t += Dt)
            {
                SimEvent e = sim.Step(new FlightInput { Move = new Vector2(Mathf.Sin(t * 0.05f) * 0.3f, 1f) }, Dt);
                Assert.AreNotEqual(SimEvent.Crashed, e);
                Vector3 p = sim.State.Position;
                float ground = island.Map.Height(p.x, p.z);
                Assert.Greater(ground, Island.WaterLevel - FlightSim.FordDepth - 0.01f);
                Assert.AreEqual(Mathf.Max(ground, Island.WaterLevel - FlightSim.FordDepth) + spec.Radius, p.y, 0.01f);
                farthest = Mathf.Max(farthest, Vector3.Distance(p, start));
            }
            Assert.Greater(farthest, 60f);
            // Let go of the stick and it comes to rest, which is what a mission calls landed.
            for (float t = 0f; t < 6f; t += Dt) sim.Step(default, Dt);
            Assert.IsTrue(sim.State.Grounded);
        }

        static string LastSeen;

        /// <summary>Drives a raid the way a patient player would: along the road the map gives, stopping to shoot.</summary>
        static MissionRun DriveRaid(Island island, MissionDefinition mission, DroneSpec spec)
        {
            var nav = new GroundNav(island);
            var sim = new FlightSim(island, spec);
            var run = new MissionRun(mission, spec, island.Map);
            List<Vector3> path = null;
            int leg = 0;
            Vector3 goal = Vector3.zero, seenAt = Vector3.zero;
            float stuck = 0f;
            for (float t = 0f; t < 1500f && run.Status == MissionStatus.InProgress; t += Dt)
            {
                ObjectiveDefinition objective = run.CurrentObjective;
                Vector3 wanted = mission.BasePosition;
                bool fight = objective != null && objective.Type == ObjectiveType.Destroy;
                if (objective != null && objective.Type != ObjectiveType.Land)
                {
                    for (int index = 0; index < mission.Targets.Count; index++)
                        if (run.Target(index).Alive) { wanted = run.Target(index).Position; break; }
                }
                // Loot on the way is worth a detour.
                stuck = Vector3.Distance(sim.State.Position, seenAt) < 2f ? stuck + Dt : 0f;
                if (stuck == 0f) seenAt = sim.State.Position;
                if (path == null || Vector3.Distance(goal, wanted) > 40f || stuck > 6f)
                {
                    goal = wanted;
                    path = nav.Path(sim.State.Position, goal) ?? new List<Vector3> { goal };
                    leg = 0;
                    stuck = 0f;
                    seenAt = sim.State.Position + Vector3.up * 50f;
                }
                while (leg < path.Count - 1 && Vector3.Distance(sim.State.Position, path[leg]) < 9f) leg++;
                Vector3 to = path[leg] - sim.State.Position;
                float bearing = Mathf.Atan2(to.x, to.z) * Mathf.Rad2Deg;
                float off = Mathf.DeltaAngle(sim.State.YawDeg, bearing);
                bool arrived = objective != null && objective.Type == ObjectiveType.Land && Vector3.Distance(sim.State.Position, mission.BasePosition) < 12f;
                // With something in the sights it holds still and fires; otherwise it drives on.
                bool engage = fight && run.LockedTarget >= 0;
                if (fight && run.LockedTarget < 0 && Vector3.Distance(sim.State.Position, wanted) < spec.LockRange * 0.8f)
                {
                    Vector3 aim = wanted - sim.State.Position;
                    off = Mathf.DeltaAngle(sim.State.YawDeg, Mathf.Atan2(aim.x, aim.z) * Mathf.Rad2Deg);
                }
                var input = new FlightInput
                {
                    Move = new Vector2(0f, arrived || engage ? 0f : Mathf.Abs(off) > 50f ? 0.25f : 1f),
                    Yaw = Mathf.Clamp(off / 25f, -1f, 1f),
                };
                SimEvent e = sim.Step(input, Dt);
                run.Step(sim.State, e, 0.5f, sim.LastTouchdownSpeed, Dt, engage);
                LastSeen = "at " + sim.State.Position + " to " + wanted + " leg " + leg + "/" + path.Count + " lock " + run.LockedTarget + " grounded " + sim.State.Grounded;
            }
            return run;
        }

        [Test]
        public void AnArmouredRaidIsOfferedOnEveryIslandAndCanBeDriven()
        {
            foreach (RegionDefinition region in RegionCatalog.All)
            {
                Island island = Island.Generate(region.Seed, region.Biome);
                int offered = 0, won = 0;
                for (int seed = 1; seed <= 3; seed++)
                {
                    MissionDefinition mission = Operations.ArmourRaid(island, region.Id, seed, 1);
                    if (mission == null) continue;
                    offered++;
                    Assert.AreEqual("armour", mission.Mode);
                    MissionRun run = DriveRaid(island, mission, DroneCatalog.Get(DroneCatalog.StarterVehicle).Spec);
                    System.Console.WriteLine("[Skyforge] raid " + mission.Id + ": " + run.Status + " " + run.FailReason + ", hull " + Mathf.RoundToInt(run.Hull * 100f) + "%, step " + run.ObjectiveIndex + ", kills " + run.Kills + ", rockets " + run.Rockets + ", " + LastSeen);
                    if (run.Status == MissionStatus.Succeeded) won++;
                }
                Assert.Greater(offered, 0, region.Id + " offers no armoured raid");
                Assert.Greater(won, 0, region.Id + ": no raid could be driven to the end");
            }
        }

        [Test]
        public void ASniperTaskIsOfferedOnEveryIslandAndCanBeShotClean()
        {
            foreach (RegionDefinition region in RegionCatalog.All)
            {
                Island island = Island.Generate(region.Seed, region.Biome);
                foreach (int level in new[] { 1, 9 })
                {
                    MissionDefinition mission = Operations.SniperTask(island, region.Id, 3, level);
                    Assert.IsNotNull(mission, region.Id + " offers no sniper task at level " + level);
                    Assert.Greater(mission.Rounds, mission.Targets.Count);
                    var run = new MissionRun(mission, DroneCatalog.Overwatch.Spec, island.Map);
                    var state = new FlightState { Position = mission.Perch, YawDeg = mission.PerchYawDeg };
                    // A marksman who leads a walking man by where he will be when the round arrives.
                    var before = new Vector3[mission.Targets.Count];
                    var speed = new Vector3[mission.Targets.Count];
                    for (float t = 0f; t < 200f && run.Status == MissionStatus.InProgress; t += Dt)
                    {
                        for (int i = 0; i < before.Length; i++) before[i] = run.Target(i).Position;
                        run.Step(state, SimEvent.None, 0.5f, 0f, Dt);
                        for (int i = 0; i < before.Length; i++) speed[i] = (run.Target(i).Position - before[i]) / Dt;
                        if (run.Projectiles.Count > 0) continue;
                        for (int i = 0; i < mission.Targets.Count; i++)
                        {
                            if (!run.Target(i).Alive) continue;
                            Vector3 aim = run.Target(i).Position + Vector3.up * mission.Targets[i].Kind.Height * 0.5f;
                            float flight = Vector3.Distance(mission.Perch, aim) / MissionRun.BulletSpeed;
                            aim += speed[i] * flight + Vector3.up * 0.5f * MissionRun.BulletDrop * flight * flight;
                            run.SniperFire(mission.Perch, aim - mission.Perch);
                            break;
                        }
                    }
                    System.Console.WriteLine("[Skyforge] sniper " + mission.Id + ": " + run.Status + " " + run.FailReason + ", shots " + run.ShotsFired + ", hits " + run.ShotsHit + " of " + mission.Targets.Count);
                    Assert.AreEqual(MissionStatus.Succeeded, run.Status, mission.Id + " " + run.FailReason);
                }
            }
        }

        [Test]
        public void ASniperWhoWastesHisRoundsFails()
        {
            Island island = Island.Generate(7);
            MissionDefinition mission = Operations.SniperTask(island, "arvelia", 3, 1);
            var run = new MissionRun(mission, DroneCatalog.Overwatch.Spec, island.Map);
            var state = new FlightState { Position = mission.Perch, YawDeg = mission.PerchYawDeg };
            for (float t = 0f; t < 120f && run.Status == MissionStatus.InProgress; t += Dt)
            {
                run.Step(state, SimEvent.None, 0.5f, 0f, Dt);
                run.SniperFire(mission.Perch, Vector3.up);
            }
            Assert.AreEqual(MissionStatus.Failed, run.Status);
            Assert.AreEqual(FailReason.OutOfAmmo, run.FailReason);
        }

        [Test]
        public void EveryIslandHasATownOfModelsThatLeavesTheSquaresOpen()
        {
            foreach (RegionDefinition region in RegionCatalog.All)
            {
                Island island = Island.Generate(region.Seed, region.Biome);
                Assert.Greater(island.Props.Count, 150, region.Id);
                foreach (PropSpec prop in island.Props)
                {
                    Assert.Less(prop.Model, Island.PropModels.Length);
                    for (int i = 0; i < 12; i++)
                    {
                        Vector3 poi = island.Anchors["poi" + i];
                        float away = new Vector2(prop.Base.x - poi.x, prop.Base.z - poi.z).magnitude;
                        Assert.Greater(away, 20f, region.Id + " crowds poi" + i + " with " + Island.PropModels[prop.Model]);
                    }
                }
            }
        }
    }
}
