using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using Skyforge.Core;
using UnityEngine;

namespace Skyforge.Tests
{
    /// <summary>Checks every mission file that ships with the game, region by region.</summary>
    public class MissionFilesTests
    {
        static string Folder(RegionDefinition region) =>
            Path.Combine(Application.dataPath, "Skyforge/Resources/Missions", region.Id);

        static readonly Dictionary<string, Island> Islands = new Dictionary<string, Island>();

        static Island IslandOf(RegionDefinition region)
        {
            if (!Islands.TryGetValue(region.Id, out Island island))
                Islands[region.Id] = island = Island.Generate(region.Seed, region.Biome);
            return island;
        }

        static List<MissionDefinition> LoadAll(RegionDefinition region, List<string> errors)
        {
            var missions = new List<MissionDefinition>();
            foreach (string file in Directory.GetFiles(Folder(region), "*.json"))
            {
                MissionDefinition mission = MissionBuilder.Parse(File.ReadAllText(file), IslandOf(region), errors);
                if (mission == null) continue;
                Assert.AreEqual(mission.Id, Path.GetFileNameWithoutExtension(file), "file name must match the mission id");
                Assert.AreEqual(region.Id, mission.Region, mission.Id + " is in the wrong folder");
                missions.Add(mission);
            }
            return missions;
        }

        [Test]
        public void EveryMissionFileIsValid()
        {
            var ids = new HashSet<string>();
            foreach (RegionDefinition region in RegionCatalog.All)
            {
                var errors = new List<string>();
                List<MissionDefinition> missions = LoadAll(region, errors);
                Assert.IsEmpty(errors, string.Join("\n", errors));
                Assert.AreEqual(15, missions.Count, region.Id);
                foreach (MissionDefinition mission in missions)
                    Assert.IsTrue(ids.Add(mission.Id), "mission id used twice: " + mission.Id);
            }
            Assert.AreEqual(45, ids.Count);
        }

        [Test]
        public void EveryTemplateHasAMissionAndOrdersAreUnique()
        {
            foreach (RegionDefinition region in RegionCatalog.All)
            {
                var templates = new HashSet<string>();
                var orders = new HashSet<int>();
                foreach (MissionDefinition mission in LoadAll(region, new List<string>()))
                {
                    templates.Add(mission.Template);
                    Assert.IsTrue(orders.Add(mission.Order), region.Id + ": order " + mission.Order + " is used twice");
                }
                foreach (string template in MissionBuilder.Templates)
                    Assert.IsTrue(templates.Contains(template), region.Id + ": no mission uses template " + template);
            }
        }

        [Test]
        public void EveryMissionHasItsTextsInEveryLanguage()
        {
            foreach (RegionDefinition region in RegionCatalog.All)
            {
                Assert.IsTrue(Loc.Has(region.NameKey), region.NameKey);
                Assert.IsTrue(Loc.Has(region.NoteKey), region.NoteKey);
                foreach (MissionDefinition mission in LoadAll(region, new List<string>()))
                {
                    Assert.IsTrue(Loc.Has(mission.TitleKey), mission.TitleKey);
                    Assert.IsTrue(Loc.Has(mission.BriefKey), mission.BriefKey);
                    Assert.IsTrue(Loc.Has("template." + mission.Template), mission.Template);
                    foreach (ObjectiveDefinition objective in mission.Objectives)
                        Assert.IsTrue(Loc.Has(objective.TextKey), mission.Id + ": " + objective.TextKey);
                }
            }
        }

        [Test]
        public void EveryMissionStaysWithinSignalRangeAndOutOfObstacles()
        {
            var spec = DroneSpec.Sparrow();
            foreach (RegionDefinition region in RegionCatalog.All)
            {
                Island island = IslandOf(region);
                foreach (MissionDefinition mission in LoadAll(region, new List<string>()))
                {
                    float range = spec.SignalRange * WeatherCatalog.Get(mission.WeatherId).SignalRange;
                    foreach (ObjectiveDefinition objective in mission.Objectives)
                    {
                        if (objective.Type == ObjectiveType.TakeOff || objective.Type == ObjectiveType.Arrive) continue;
                        if (!string.IsNullOrEmpty(objective.ActorId)) continue;
                        var places = new List<Vector3>(objective.Targets);
                        if (objective.Type != ObjectiveType.Mark) places.Add(objective.Position);
                        foreach (Vector3 place in places)
                        {
                            float distance = Vector3.Distance(place, island.Spawn);
                            Assert.Less(distance, range * 0.95f, mission.Id + ": an objective is beyond the signal");
                        }
                        bool flown = objective.Type == ObjectiveType.Reach || objective.Type == ObjectiveType.Scan;
                        if (!flown) continue;
                        Assert.IsFalse(island.Obstacles.Hits(objective.Position, spec.Radius), mission.Id + ": a waypoint is inside an obstacle");
                        float ground = Mathf.Max(island.Map.Height(objective.Position.x, objective.Position.z), Island.WaterLevel);
                        Assert.Greater(objective.Position.y - ground, 3f, mission.Id + ": a waypoint is in the ground");
                        Assert.Less(objective.Position.y, FlightSim.Ceiling - 20f, mission.Id + ": a waypoint is above the ceiling");
                    }
                }
            }
        }

        [Test]
        public void EveryRegionHasAUsableIsland()
        {
            foreach (RegionDefinition region in RegionCatalog.All)
            {
                Island island = IslandOf(region);
                Assert.Greater(island.Spawn.y, Island.WaterLevel + 1f, region.Id);
                Assert.AreEqual(3, island.Beacons.Count, region.Id);
                Assert.AreEqual(10, island.Rings.Count, region.Id);
                for (int i = 0; i < 12; i++) Assert.Greater(island.Anchors["poi" + i].y, Island.WaterLevel + 3f, region.Id + " poi" + i);
                for (int i = 0; i < 8; i++) Assert.Greater(island.Anchors["coast" + i].y, Island.WaterLevel, region.Id + " coast" + i);
                Assert.Less(island.Anchors["summit"].y, FlightSim.Ceiling - 40f, region.Id + ": the summit must be flyable");
                foreach (Vector3 ring in island.Rings)
                    Assert.Less(ring.y, FlightSim.Ceiling - 20f, region.Id + ": a ring is above the ceiling");
                Assert.IsFalse(island.Obstacles.Hits(island.Spawn + Vector3.up, 5f), region.Id + ": the pad is clear");
            }
        }

        [Test]
        public void RegionsOpenInOrderAndTheFirstIsFree()
        {
            Assert.AreEqual(0, RegionCatalog.All[0].StarsRequired);
            for (int i = 1; i < RegionCatalog.All.Count; i++)
            {
                Assert.Greater(RegionCatalog.All[i].StarsRequired, RegionCatalog.All[i - 1].StarsRequired);
                // A region must be reachable with the stars the earlier regions can give.
                Assert.LessOrEqual(RegionCatalog.All[i].StarsRequired, i * 15 * 3);
            }
        }

        [Test]
        public void BrokenMissionsAreRejectedWithReasons()
        {
            Island island = Island.Generate(7);
            var errors = new List<string>();
            Assert.IsNull(MissionBuilder.Parse("{ not json", island, errors));
            Assert.IsNull(MissionBuilder.Parse(
                "{\"id\":\"x\",\"template\":\"scan\",\"timeGoal\":60,\"baseReward\":10,\"starReward\":5," +
                "\"objectives\":[{\"type\":\"reach\",\"at\":{\"anchor\":\"nowhere\"},\"radius\":5}]}", island, errors));
            Assert.IsNull(MissionBuilder.Parse(
                "{\"id\":\"y\",\"template\":\"dance\",\"timeGoal\":60,\"baseReward\":10,\"starReward\":5," +
                "\"objectives\":[{\"type\":\"teleport\"}]}", island, errors));
            Assert.IsNull(MissionBuilder.Parse(
                "{\"id\":\"z\",\"template\":\"scan\",\"timeGoal\":60,\"baseReward\":10,\"starReward\":5,\"objectives\":[]}",
                island, errors));

            string all = string.Join("\n", errors);
            StringAssert.Contains("not valid JSON", all);
            StringAssert.Contains("unknown anchor 'nowhere'", all);
            StringAssert.Contains("unknown template 'dance'", all);
            StringAssert.Contains("unknown type 'teleport'", all);
            StringAssert.Contains("z: has no objectives", all);
        }

        [Test]
        public void IslandHasEveryAnchorMissionsMayUse()
        {
            Island island = Island.Generate(7);
            Assert.IsTrue(island.Anchors.ContainsKey("pad"));
            Assert.IsTrue(island.Anchors.ContainsKey("summit"));
            for (int i = 0; i < 12; i++) Assert.Greater(island.Anchors["poi" + i].y, Island.WaterLevel + 3f, "poi" + i);
            for (int i = 0; i < 8; i++) Assert.Greater(island.Anchors["coast" + i].y, Island.WaterLevel, "coast" + i);
            for (int i = 0; i < 4; i++)
            {
                Vector3 sea = island.Anchors["sea" + i];
                Assert.Less(island.Map.Height(sea.x, sea.z), Island.WaterLevel, "sea" + i);
            }
            Assert.IsFalse(island.Obstacles.Hits(island.Anchors["poi3"] + Vector3.up, 4f), "anchors are kept clear of trees");
        }
    }

    public class LocTests
    {
        [Test]
        public void EveryStringHasEveryLanguage()
        {
            int languages = -1;
            foreach (KeyValuePair<string, string[]> row in Loc.All)
            {
                if (languages < 0) languages = row.Value.Length;
                Assert.AreEqual(languages, row.Value.Length, row.Key);
                foreach (string text in row.Value) Assert.IsNotNull(text, row.Key);
            }
            Assert.GreaterOrEqual(languages, 2);
        }

        [Test]
        public void PlaceholdersMatchAcrossLanguages()
        {
            foreach (KeyValuePair<string, string[]> row in Loc.All)
                for (int slot = 0; slot < 4; slot++)
                {
                    bool first = row.Value[0].Contains("{" + slot + "}");
                    foreach (string text in row.Value)
                        Assert.AreEqual(first, text.Contains("{" + slot + "}"), row.Key + " slot " + slot);
                }
        }
    }

    /// <summary>The objective types added for the mission engine.</summary>
    public class ObjectiveTests
    {
        const float Dt = 0.1f;

        static MissionDefinition Mission(params ObjectiveDefinition[] objectives)
        {
            var mission = new MissionDefinition { Id = "t", TimeGoalSeconds = 60f, BaseReward = 10 };
            mission.Objectives.AddRange(objectives);
            return mission;
        }

        static FlightState Fly(float x, float y, float z, float yawDeg = 0f) =>
            new FlightState { Position = new Vector3(x, y, z), YawDeg = yawDeg };

        static void Hold(MissionRun run, FlightState state, float seconds, bool action = false)
        {
            for (int i = 0; i < Mathf.RoundToInt(seconds / Dt); i++)
                run.Step(state, SimEvent.None, 0.45f, 0f, Dt, action && i == 0);
        }

        static ActorDefinition Truck(string id, float speed, float detectRange = 0f, int startObjective = 0)
        {
            var actor = new ActorDefinition { Id = id, Kind = "truck", Speed = speed, DetectRange = detectRange, StartObjective = startObjective };
            actor.Path.Add(new Vector3(0f, 0f, 0f));
            actor.Path.Add(new Vector3(0f, 0f, 100f));
            return actor;
        }

        [Test]
        public void Reach_CompletesInsideTheRadiusOnly()
        {
            var run = new MissionRun(Mission(new ObjectiveDefinition { Type = ObjectiveType.Reach, Position = new Vector3(50f, 20f, 0f), Radius = 7f }), DroneSpec.Sparrow());
            Hold(run, Fly(40f, 20f, 0f), 1f);
            Assert.AreEqual(MissionStatus.InProgress, run.Status);
            Hold(run, Fly(45f, 20f, 0f), 0.2f);
            Assert.AreEqual(MissionStatus.Succeeded, run.Status);
        }

        [Test]
        public void Observe_NeedsTheTargetAheadAndInRange()
        {
            var objective = new ObjectiveDefinition { Type = ObjectiveType.Observe, Position = new Vector3(0f, 0f, 60f), Range = 80f, HoldSeconds = 2f };
            var run = new MissionRun(Mission(objective), DroneSpec.Sparrow());

            Hold(run, Fly(0f, 20f, 0f, 90f), 3f);
            Assert.IsFalse(run.TargetFramed, "looking sideways");
            Assert.AreEqual(0f, run.ObjectiveProgress);

            Hold(run, Fly(0f, 20f, -60f), 3f);
            Assert.IsFalse(run.TargetFramed, "too far");

            Hold(run, Fly(0f, 20f, 0f), 1f);
            Assert.IsTrue(run.TargetFramed);
            Assert.AreEqual(MissionStatus.InProgress, run.Status);
            Hold(run, Fly(0f, 20f, 0f), 1.5f);
            Assert.AreEqual(MissionStatus.Succeeded, run.Status);
        }

        [Test]
        public void Observe_ZoomExtendsTheRange()
        {
            var objective = new ObjectiveDefinition { Type = ObjectiveType.Observe, Position = new Vector3(0f, 0f, 150f), Range = 80f, HoldSeconds = 1f };
            var run = new MissionRun(Mission(objective), DroneSpec.Sparrow());
            run.Step(Fly(0f, 20f, 0f), SimEvent.None, 0.4f, 0f, Dt, false, default, 1f);
            Assert.IsFalse(run.TargetFramed);
            run.Step(Fly(0f, 20f, 0f), SimEvent.None, 0.4f, 0f, Dt, false, default, 2f);
            Assert.IsTrue(run.TargetFramed);
        }

        [Test]
        public void Photo_OnlyCountsWhenFramedAndThePressIsReported()
        {
            var objective = new ObjectiveDefinition { Type = ObjectiveType.Photo, Position = new Vector3(0f, 0f, 30f), Range = 45f };
            var run = new MissionRun(Mission(objective), DroneSpec.Sparrow());

            ActionResult away = run.Step(Fly(0f, 10f, 0f, 180f), SimEvent.None, 0.4f, 0f, Dt, true);
            Assert.AreEqual(ActionResult.PhotoMissed, away);
            Assert.AreEqual(MissionStatus.InProgress, run.Status);

            Hold(run, Fly(0f, 10f, 0f), 2f);
            Assert.AreEqual(MissionStatus.InProgress, run.Status, "framing alone is not a photo");

            ActionResult shot = run.Step(Fly(0f, 10f, 0f), SimEvent.None, 0.4f, 0f, Dt, true);
            Assert.AreEqual(ActionResult.PhotoTaken, shot);
            Assert.AreEqual(MissionStatus.Succeeded, run.Status);
        }

        [Test]
        public void Follow_CountsOnlyInsideTheDistanceBand()
        {
            MissionDefinition mission = Mission(new ObjectiveDefinition
            {
                Type = ObjectiveType.Follow, ActorId = "truck", MinDistance = 30f, MaxDistance = 80f, HoldSeconds = 3f,
            });
            mission.Actors.Add(Truck("truck", 0.0001f));
            var run = new MissionRun(mission, DroneSpec.Sparrow());

            Hold(run, Fly(10f, 20f, 0f), 2f);
            Assert.AreEqual(0f, run.ObjectiveProgress, "too close");
            Hold(run, Fly(200f, 20f, 0f), 2f);
            Assert.AreEqual(0f, run.ObjectiveProgress, "too far");
            Hold(run, Fly(50f, 20f, 0f), 2f);
            Assert.Greater(run.ObjectiveProgress, 0.5f);
            Hold(run, Fly(200f, 20f, 0f), 2f);
            Assert.Greater(run.ObjectiveProgress, 0.5f, "tracking time is kept when contact is broken");
            Hold(run, Fly(50f, 20f, 0f), 1.5f);
            Assert.AreEqual(MissionStatus.Succeeded, run.Status);
        }

        [Test]
        public void Actor_DrivesItsPathAndStops()
        {
            MissionDefinition mission = Mission(new ObjectiveDefinition { Type = ObjectiveType.Arrive, ActorId = "truck" });
            mission.Actors.Add(Truck("truck", 10f));
            var run = new MissionRun(mission, DroneSpec.Sparrow());

            Hold(run, Fly(300f, 20f, 0f), 5f);
            Assert.IsTrue(run.TryGetActor("truck", out ActorState halfway));
            Assert.AreEqual(50f, halfway.Position.z, 1.5f);
            Assert.AreEqual(MissionStatus.InProgress, run.Status);

            Hold(run, Fly(300f, 20f, 0f), 6f);
            Assert.AreEqual(MissionStatus.Succeeded, run.Status);
        }

        [Test]
        public void Actor_WaitsForItsStartObjective()
        {
            MissionDefinition mission = Mission(
                new ObjectiveDefinition { Type = ObjectiveType.TakeOff },
                new ObjectiveDefinition { Type = ObjectiveType.Arrive, ActorId = "truck" });
            mission.Actors.Add(Truck("truck", 10f, 0f, 1));
            var run = new MissionRun(mission, DroneSpec.Sparrow());

            var parked = new FlightState { Position = new Vector3(300f, 0f, 0f), Grounded = true };
            for (int i = 0; i < 50; i++) run.Step(parked, SimEvent.None, 0f, 0f, Dt);
            run.TryGetActor("truck", out ActorState waiting);
            Assert.AreEqual(0f, waiting.Travelled);

            Hold(run, Fly(300f, 20f, 0f), 2f);
            run.TryGetActor("truck", out ActorState moving);
            Assert.Greater(moving.Travelled, 10f);
        }

        [Test]
        public void GettingTooCloseToAWatchfulActorBlowsTheMission()
        {
            MissionDefinition mission = Mission(new ObjectiveDefinition
            {
                Type = ObjectiveType.Follow, ActorId = "truck", MinDistance = 30f, MaxDistance = 80f, HoldSeconds = 60f,
            });
            mission.Actors.Add(Truck("truck", 0.0001f, 25f));
            var run = new MissionRun(mission, DroneSpec.Sparrow());

            Hold(run, Fly(10f, 5f, 0f), 1f);
            Assert.IsTrue(run.BeingDetected);
            Assert.Greater(run.Detection, 0.2f);
            Assert.AreEqual(MissionStatus.InProgress, run.Status);

            Hold(run, Fly(60f, 20f, 0f), 10f);
            Assert.AreEqual(0f, run.Detection, "backing off lets the alarm die down");

            Hold(run, Fly(10f, 5f, 0f), MissionRun.DetectSeconds + 0.5f);
            Assert.AreEqual(FailReason.Detected, run.FailReason);
        }

        [Test]
        public void Radar_SeesOnlyAboveItsFloor()
        {
            MissionDefinition mission = Mission(new ObjectiveDefinition { Type = ObjectiveType.Reach, Position = new Vector3(900f, 0f, 0f), Radius = 5f });
            mission.Zones.Add(new ZoneDefinition { Position = Vector3.zero, Radius = 100f, Floor = 20f });
            var run = new MissionRun(mission, DroneSpec.Sparrow());

            Hold(run, Fly(50f, 15f, 0f), 5f);
            Assert.AreEqual(0f, run.Detection, "under the floor");
            Hold(run, Fly(150f, 60f, 0f), 5f);
            Assert.AreEqual(0f, run.Detection, "outside the radius");
            Hold(run, Fly(50f, 30f, 0f), MissionRun.DetectSeconds + 0.5f);
            Assert.AreEqual(FailReason.Detected, run.FailReason);
        }

        [Test]
        public void Find_ReadingGrowsOnTheWayIn()
        {
            var objective = new ObjectiveDefinition { Type = ObjectiveType.Find, Position = new Vector3(0f, 0f, 400f), Radius = 15f, Range = 500f };
            var run = new MissionRun(Mission(objective), DroneSpec.Sparrow());
            float far = run.ScannerReading(new Vector3(0f, 20f, 0f));
            float near = run.ScannerReading(new Vector3(0f, 20f, 300f));
            Assert.Greater(near, far);
            Assert.Greater(far, 0f);

            Hold(run, Fly(0f, 10f, 392f), 0.3f);
            Assert.AreEqual(MissionStatus.Succeeded, run.Status);
        }

        [Test]
        public void Ballistics_FallTimeAndDrift()
        {
            Vector3 impact = Ballistics.Impact(new Vector3(0f, 44.145f, 0f), new Vector3(4f, 0f, 0f), Vector3.zero, null, out float seconds);
            Assert.AreEqual(3f, seconds, 0.01f);
            Assert.AreEqual(12f, impact.x, 0.05f);
            Assert.AreEqual(0f, impact.y);

            Vector3 windy = Ballistics.Impact(new Vector3(0f, 44.145f, 0f), Vector3.zero, new Vector3(0f, 0f, 10f), null, out _);
            Assert.AreEqual(10f * Ballistics.WindCarry * 3f, windy.z, 0.05f);
        }

        [Test]
        public void Strike_HitsWhenReleasedOverTheTarget()
        {
            var objective = new ObjectiveDefinition { Type = ObjectiveType.Strike, Position = new Vector3(0f, 0f, 0f), Radius = 6f, MinAltitude = 40f };
            var run = new MissionRun(Mission(objective), DroneSpec.Sparrow());

            ActionResult low = run.Step(Fly(0f, 20f, 0f), SimEvent.None, 0.4f, 0f, Dt, true);
            Assert.AreEqual(ActionResult.ReleaseRefused, low);
            Assert.AreEqual(1, run.ShotsLeft);

            ActionResult released = run.Step(Fly(2f, 50f, 1f), SimEvent.None, 0.4f, 0f, Dt, true);
            Assert.AreEqual(ActionResult.Released, released);
            Assert.IsTrue(run.Shot.Active);
            Assert.AreEqual(MissionStatus.InProgress, run.Status, "the munition is still falling");

            Hold(run, Fly(2f, 50f, 1f), 4f);
            Assert.AreEqual(MissionStatus.Succeeded, run.Status);
        }

        [Test]
        public void Strike_MissWithTheLastShotFails()
        {
            var objective = new ObjectiveDefinition { Type = ObjectiveType.Strike, Position = new Vector3(0f, 0f, 0f), Radius = 6f, MinAltitude = 40f };
            var run = new MissionRun(Mission(objective), DroneSpec.Sparrow());
            run.Step(Fly(40f, 50f, 0f), SimEvent.None, 0.4f, 0f, Dt, true);
            Hold(run, Fly(40f, 50f, 0f), 4f);
            Assert.AreEqual(FailReason.Missed, run.FailReason);
        }

        [Test]
        public void Strike_WindCarriesTheMunitionAsPredicted()
        {
            var objective = new ObjectiveDefinition { Type = ObjectiveType.Strike, Position = new Vector3(0f, 0f, 0f), Radius = 3f, MinAltitude = 40f };
            var run = new MissionRun(Mission(objective), DroneSpec.Sparrow());
            var wind = new Vector3(8f, 0f, 0f);
            FlightState hover = Fly(0f, 50f, 0f);

            run.Step(hover, SimEvent.None, 0.4f, 0f, Dt, false, wind);
            Vector3 predicted = run.PredictImpact(hover, out _);
            Assert.Greater(predicted.x, 10f, "released straight above, it would be blown past the target");

            // Hover upwind by the predicted drift and it lands on the target.
            FlightState corrected = Fly(-predicted.x, 50f, 0f);
            run.Step(corrected, SimEvent.None, 0.4f, 0f, Dt, true, wind);
            for (int i = 0; i < 40; i++) run.Step(corrected, SimEvent.None, 0.4f, 0f, Dt, false, wind);
            Assert.AreEqual(MissionStatus.Succeeded, run.Status);
        }

        static MissionDefinition EscortMission(float convoySpeed)
        {
            var mark = new ObjectiveDefinition { Type = ObjectiveType.Mark, ActorId = "convoy", Range = 80f, HoldSeconds = 2f };
            mark.Targets.Add(new Vector3(0f, 0f, 60f));
            MissionDefinition mission = Mission(mark, new ObjectiveDefinition { Type = ObjectiveType.Arrive, ActorId = "convoy" });
            mission.Actors.Add(Truck("convoy", convoySpeed));
            return mission;
        }

        [Test]
        public void Mark_ThreatMarkedInTimeLetsTheConvoyThrough()
        {
            var run = new MissionRun(EscortMission(5f), DroneSpec.Sparrow());
            Assert.AreEqual(1, run.ThreatsLeft);
            Hold(run, Fly(0f, 20f, 20f), 2.5f);
            Assert.AreEqual(0, run.ThreatsLeft);
            Assert.AreEqual(1, run.ObjectiveIndex);
            Hold(run, Fly(0f, 20f, 20f), 25f);
            Assert.AreEqual(MissionStatus.Succeeded, run.Status);
        }

        [Test]
        public void Mark_UnmarkedThreatAmbushesTheConvoy()
        {
            var run = new MissionRun(EscortMission(5f), DroneSpec.Sparrow());
            Hold(run, Fly(300f, 20f, 0f, 90f), 20f);
            Assert.AreEqual(MissionStatus.Failed, run.Status);
            Assert.AreEqual(FailReason.Ambushed, run.FailReason);
        }

        [Test]
        public void StartingBatteryAndPayloadComeFromTheMission()
        {
            MissionDefinition mission = Mission(new ObjectiveDefinition { Type = ObjectiveType.TakeOff });
            mission.BatteryStart = 0.6f;
            Assert.AreEqual(0.6f, new MissionRun(mission, DroneSpec.Sparrow()).Battery);

            DroneSpec empty = DroneSpec.Sparrow(), loaded = DroneSpec.Sparrow().WithPayload(1f);
            Assert.Less(loaded.MaxSpeed, empty.MaxSpeed);
            Assert.Less(loaded.TiltResponse, empty.TiltResponse);
            Assert.Less(loaded.MaxClimb, empty.MaxClimb);
            Assert.AreEqual(empty.MaxSpeed, DroneSpec.Sparrow().WithPayload(0f).MaxSpeed);
        }
    }
}
