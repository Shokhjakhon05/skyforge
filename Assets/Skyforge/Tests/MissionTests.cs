using System.IO;
using NUnit.Framework;
using Skyforge.Core;
using UnityEngine;

namespace Skyforge.Tests
{
    public class MissionRunTests
    {
        const float Dt = 0.1f;

        static MissionDefinition TwoStepMission()
        {
            var mission = new MissionDefinition
            {
                Id = "test",
                TimeGoalSeconds = 60f,
                BaseReward = 100,
                StarReward = 40,
                BasePosition = Vector3.zero,
            };
            mission.Objectives.Add(new ObjectiveDefinition { Type = ObjectiveType.TakeOff });
            mission.Objectives.Add(new ObjectiveDefinition
            {
                Type = ObjectiveType.Scan,
                Position = new Vector3(100f, 20f, 0f),
                Radius = 10f,
                HeightWindow = 10f,
                HoldSeconds = 3f,
            });
            mission.Objectives.Add(new ObjectiveDefinition { Type = ObjectiveType.Land, Position = Vector3.zero, Radius = 8f });
            return mission;
        }

        static FlightState At(float x, float y, bool grounded = false) =>
            new FlightState { Position = new Vector3(x, y, 0f), Grounded = grounded };

        static void Hold(MissionRun run, FlightState state, float seconds, float load = 0.45f)
        {
            for (int i = 0; i < Mathf.RoundToInt(seconds / Dt); i++)
                run.Step(state, SimEvent.None, load, 0f, Dt);
        }

        static MissionRun FlyWholeMission(float cruiseSeconds, float touchdownSpeed)
        {
            var run = new MissionRun(TwoStepMission(), DroneSpec.Sparrow());
            Hold(run, At(0f, 5f), cruiseSeconds);
            Hold(run, At(100f, 20f), 3.5f);
            run.Step(At(0f, 0f, true), SimEvent.Landed, 0f, touchdownSpeed, Dt);
            return run;
        }

        [Test]
        public void ClockWaitsForTakeOff()
        {
            var run = new MissionRun(TwoStepMission(), DroneSpec.Sparrow());
            Hold(run, At(0f, 0f, true), 10f);
            Assert.AreEqual(0f, run.Elapsed);
            Assert.AreEqual(1f, run.Battery);
            Assert.AreEqual(0, run.ObjectiveIndex);
        }

        [Test]
        public void ObjectivesCompleteInOrder()
        {
            var run = new MissionRun(TwoStepMission(), DroneSpec.Sparrow());
            Hold(run, At(0f, 5f), 1f);
            Assert.AreEqual(1, run.ObjectiveIndex);

            // Landing before the scan is done does not finish anything.
            run.Step(At(0f, 0f, true), SimEvent.Landed, 0f, 1f, Dt);
            Assert.AreEqual(1, run.ObjectiveIndex);

            Hold(run, At(100f, 20f), 2f);
            Assert.AreEqual(1, run.ObjectiveIndex);
            Assert.Greater(run.ObjectiveProgress, 0.5f);
            Hold(run, At(100f, 20f), 1.5f);
            Assert.AreEqual(2, run.ObjectiveIndex);

            run.Step(At(30f, 0f, true), SimEvent.Landed, 0f, 1f, Dt);
            Assert.AreEqual(MissionStatus.InProgress, run.Status, "landing away from the pad does not count");
            run.Step(At(2f, 0f, true), SimEvent.None, 0f, 0f, Dt);
            Assert.AreEqual(MissionStatus.Succeeded, run.Status);
        }

        [Test]
        public void ScanProgressDrainsOutsideTheZone()
        {
            var run = new MissionRun(TwoStepMission(), DroneSpec.Sparrow());
            Hold(run, At(0f, 5f), 1f);
            Hold(run, At(100f, 20f), 2f);
            float inside = run.ObjectiveProgress;
            Hold(run, At(100f, 45f), 1f);
            Assert.Less(run.ObjectiveProgress, inside);
            Assert.Greater(run.ObjectiveProgress, 0f);
        }

        [Test]
        public void CleanFastRunEarnsThreeStars()
        {
            MissionOutcome outcome = MissionOutcome.From(FlyWholeMission(20f, 1f));
            Assert.IsTrue(outcome.Succeeded);
            Assert.IsTrue(outcome.TimeGoalMet);
            Assert.IsTrue(outcome.NoDamage);
            Assert.AreEqual(3, outcome.Stars);
        }

        [Test]
        public void SlowRunLosesTheTimeStar()
        {
            MissionOutcome outcome = MissionOutcome.From(FlyWholeMission(70f, 1f));
            Assert.IsFalse(outcome.TimeGoalMet);
            Assert.AreEqual(2, outcome.Stars);
        }

        [Test]
        public void HardLandingLosesTheDamageStar()
        {
            MissionRun run = FlyWholeMission(20f, 3f);
            Assert.Less(run.Hull, 1f);
            Assert.Greater(run.Hull, 0f);
            MissionOutcome outcome = MissionOutcome.From(run);
            Assert.IsTrue(outcome.Succeeded);
            Assert.IsFalse(outcome.NoDamage);
            Assert.AreEqual(2, outcome.Stars);
        }

        [Test]
        public void CrashFailsTheMission()
        {
            var run = new MissionRun(TwoStepMission(), DroneSpec.Sparrow());
            Hold(run, At(0f, 5f), 1f);
            run.Step(At(0f, 0f), SimEvent.Crashed, 0f, 9f, Dt);
            Assert.AreEqual(MissionStatus.Failed, run.Status);
            Assert.AreEqual(FailReason.Destroyed, run.FailReason);
            Assert.AreEqual(0, MissionOutcome.From(run).Stars);
        }

        [Test]
        public void BatteryRunsOutAndHarderFlyingDrainsFaster()
        {
            var spec = DroneSpec.Sparrow();
            var calm = new MissionRun(TwoStepMission(), spec);
            var hard = new MissionRun(TwoStepMission(), spec);
            Hold(calm, At(0f, 5f), 60f, 0.2f);
            Hold(hard, At(0f, 5f), 60f, 1f);
            Assert.Less(hard.Battery, calm.Battery);

            Hold(hard, At(0f, 5f), spec.BatterySeconds, 1f);
            Assert.AreEqual(MissionStatus.Failed, hard.Status);
            Assert.AreEqual(FailReason.BatteryEmpty, hard.FailReason);
        }

        [Test]
        public void SignalFadesWithDistanceAndLosingItFails()
        {
            var spec = DroneSpec.Sparrow();
            var run = new MissionRun(TwoStepMission(), spec);
            Hold(run, At(spec.SignalRange * 0.3f, 5f), 1f);
            Assert.AreEqual(1f, run.Signal);
            Hold(run, At(spec.SignalRange * 0.8f, 5f), 1f);
            Assert.Greater(run.Signal, 0f);
            Assert.Less(run.Signal, 1f);

            Hold(run, At(spec.SignalRange * 1.1f, 5f), MissionRun.SignalGraceSeconds * 0.5f);
            Assert.AreEqual(MissionStatus.InProgress, run.Status, "there is a grace period");
            Hold(run, At(spec.SignalRange * 0.5f, 5f), 1f);
            Hold(run, At(spec.SignalRange * 1.1f, 5f), MissionRun.SignalGraceSeconds * 0.8f);
            Assert.AreEqual(MissionStatus.InProgress, run.Status, "coming back resets the countdown");

            Hold(run, At(spec.SignalRange * 1.1f, 5f), MissionRun.SignalGraceSeconds);
            Assert.AreEqual(FailReason.SignalLost, run.FailReason);
        }

        [Test]
        public void FirstContactIsFlyableOnTheGeneratedIsland()
        {
            Island island = Island.Generate(7);
            var spec = DroneSpec.Sparrow();
            MissionDefinition mission = MissionCatalog.FirstContact(island);

            Assert.AreEqual(3, island.Beacons.Count);
            Assert.AreEqual(5, mission.Objectives.Count);
            foreach (Vector3 beacon in island.Beacons)
            {
                Assert.Greater(beacon.y, Island.WaterLevel + 3f);
                Assert.Less(Vector3.Distance(beacon, island.Spawn), spec.SignalRange * 0.9f);
            }
            foreach (ObjectiveDefinition objective in mission.Objectives)
            {
                if (objective.Type != ObjectiveType.Scan) continue;
                Assert.IsFalse(island.Obstacles.Hits(objective.Position, spec.Radius), "the scan point is in free air");
            }
        }
    }

    public class SaveTests
    {
        string _directory;
        string _path;

        [SetUp]
        public void SetUp()
        {
            _directory = Path.Combine(Path.GetTempPath(), "skyforge-test-" + System.Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_directory);
            _path = Path.Combine(_directory, "save.json");
        }

        [TearDown]
        public void TearDown()
        {
            if (Directory.Exists(_directory)) Directory.Delete(_directory, true);
        }

        static SaveData Sample(int credits)
        {
            var data = new SaveData { Credits = credits, SticksSwapped = true, RingBest = 189.5f };
            MissionRecord record = data.GetOrAdd("first_contact");
            record.Stars = 2;
            record.BestTime = 151.25f;
            record.Completions = 3;
            return data;
        }

        [Test]
        public void MissingFileGivesAFreshSave()
        {
            SaveData data = new SaveStore(_path).Load(out LoadResult result);
            Assert.AreEqual(LoadResult.Fresh, result);
            Assert.AreEqual(0, data.Credits);
            Assert.AreEqual(SaveData.CurrentVersion, data.Version);
        }

        [Test]
        public void SavedDataComesBackUnchanged()
        {
            var store = new SaveStore(_path);
            Assert.IsTrue(store.Save(Sample(420)));

            SaveData data = new SaveStore(_path).Load(out LoadResult result);
            Assert.AreEqual(LoadResult.Loaded, result);
            Assert.AreEqual(420, data.Credits);
            Assert.IsTrue(data.SticksSwapped);
            Assert.AreEqual(189.5f, data.RingBest);
            MissionRecord record = data.Find("first_contact");
            Assert.NotNull(record);
            Assert.AreEqual(2, record.Stars);
            Assert.AreEqual(151.25f, record.BestTime);
            Assert.AreEqual(3, record.Completions);
        }

        [Test]
        public void CorruptedFileFallsBackToThePreviousSave()
        {
            var store = new SaveStore(_path);
            store.Save(Sample(100));
            store.Save(Sample(200));
            File.WriteAllText(_path, "{\"Version\":1,\"Credits\":2");

            SaveData data = store.Load(out LoadResult result);
            Assert.AreEqual(LoadResult.RecoveredFromBackup, result);
            Assert.AreEqual(100, data.Credits);
        }

        [Test]
        public void CorruptedFileWithoutBackupStartsOverAndKeepsTheEvidence()
        {
            File.WriteAllText(_path, "not json at all");
            SaveData data = new SaveStore(_path).Load(out LoadResult result);
            Assert.AreEqual(LoadResult.ResetAfterCorruption, result);
            Assert.AreEqual(0, data.Credits);
            Assert.IsTrue(File.Exists(_path + ".corrupt"));
        }

        [Test]
        public void InvalidDataIsNeverWritten()
        {
            var store = new SaveStore(_path);
            store.Save(Sample(100));
            Assert.IsFalse(store.Save(Sample(-5)));
            Assert.AreEqual(100, store.Load(out _).Credits);
        }

        [Test]
        public void VersionOneSaveIsMigrated()
        {
            File.WriteAllText(_path,
                "{\"Version\":1,\"Credits\":250,\"Missions\":[{\"Id\":\"first_contact\",\"Stars\":3," +
                "\"BestTime\":140.5,\"Completions\":2}],\"SticksSwapped\":true,\"FlightModeIndex\":1," +
                "\"CameraViewIndex\":0,\"RingBest\":189.0}");

            SaveData data = new SaveStore(_path).Load(out LoadResult result);
            Assert.AreEqual(LoadResult.Loaded, result);
            Assert.AreEqual(SaveData.CurrentVersion, data.Version);
            Assert.AreEqual(250, data.Credits);
            Assert.AreEqual(3, data.Find("first_contact").Stars);
            Assert.IsTrue(data.SticksSwapped);
            Assert.AreEqual(0, data.Quality);
            Assert.AreEqual(WeatherCatalog.Clear, data.FreeFlightWeather);
        }

        [Test]
        public void SaveFromANewerVersionIsNotAccepted()
        {
            SaveData future = Sample(100);
            future.Version = SaveData.CurrentVersion + 1;
            File.WriteAllText(_path, JsonUtility.ToJson(future));
            new SaveStore(_path).Load(out LoadResult result);
            Assert.AreEqual(LoadResult.ResetAfterCorruption, result);
        }
    }

    public class WeatherTests
    {
        [Test]
        public void EveryWeatherHasAnIdAndSaneNumbers()
        {
            var seen = new System.Collections.Generic.HashSet<string>();
            foreach (Weather weather in WeatherCatalog.All)
            {
                Assert.IsTrue(seen.Add(weather.Id), "duplicate id " + weather.Id);
                Assert.IsFalse(string.IsNullOrEmpty(weather.NameKey));
                Assert.Greater(weather.FogEnd, weather.FogStart);
                Assert.GreaterOrEqual(weather.BatteryDrain, 1f);
                Assert.LessOrEqual(weather.SignalRange, 1f);
                Assert.Greater(weather.SignalRange, 0.5f);
            }
            Assert.AreEqual(9, seen.Count);
        }

        [Test]
        public void UnknownWeatherFallsBackToClear()
        {
            Assert.AreEqual(WeatherCatalog.Clear, WeatherCatalog.Get("no-such-weather").Id);
            Assert.AreEqual(WeatherCatalog.Clear, WeatherCatalog.Get(null).Id);
        }

        [Test]
        public void NextCyclesThroughAllWeather()
        {
            string id = WeatherCatalog.Clear;
            for (int i = 0; i < WeatherCatalog.All.Count; i++) id = WeatherCatalog.Next(id);
            Assert.AreEqual(WeatherCatalog.Clear, id);
        }

        [Test]
        public void WindStaysWithinSteadyPlusGust()
        {
            Weather storm = WeatherCatalog.Get("storm");
            for (float t = 0f; t < 120f; t += 0.37f)
            {
                float speed = storm.WindAt(t).magnitude;
                Assert.LessOrEqual(speed, storm.WindSpeed + storm.Gust + 0.01f);
                Assert.GreaterOrEqual(speed, storm.WindSpeed - storm.Gust - 0.01f);
            }
            Assert.AreEqual(Vector3.zero, WeatherCatalog.Get(WeatherCatalog.Clear).WindAt(5f));
        }

        [Test]
        public void EveryDroneCanMakeHeadwayInEveryWeather()
        {
            var spec = DroneSpec.Sparrow();
            foreach (Weather weather in WeatherCatalog.All)
                Assert.Less(weather.WindSpeed + weather.Gust, spec.MaxSpeed,
                    weather.Id + " would pin the starter drone in place");
        }

        [Test]
        public void BadWeatherDrainsTheBatteryFasterAndShortensTheSignal()
        {
            var spec = DroneSpec.Sparrow();
            var clear = new MissionRun(new MissionDefinition { Id = "a" }, spec);
            var storm = new MissionRun(new MissionDefinition { Id = "b", WeatherId = "storm" }, spec);
            var far = new FlightState { Position = new Vector3(spec.SignalRange * 0.7f, 5f, 0f) };
            for (int i = 0; i < 300; i++)
            {
                clear.Step(far, SimEvent.None, 0.5f, 0f, 0.1f);
                storm.Step(far, SimEvent.None, 0.5f, 0f, 0.1f);
            }
            Assert.Less(storm.Battery, clear.Battery);
            Assert.Less(storm.Signal, clear.Signal);
        }
    }

    public class ProgressTests
    {
        static MissionDefinition Mission() =>
            new MissionDefinition { Id = "m", BaseReward = 100, StarReward = 40 };

        static MissionOutcome Outcome(int stars, float time = 100f) =>
            new MissionOutcome { Succeeded = stars > 0, Stars = stars, Time = time };

        [Test]
        public void FirstCompletionPaysBaseAndStars()
        {
            var save = new SaveData();
            Assert.AreEqual(180, Progress.Grant(save, Mission(), Outcome(2)));
            Assert.AreEqual(180, save.Credits);
            Assert.AreEqual(2, save.Find("m").Stars);
            Assert.AreEqual(1, save.Find("m").Completions);
        }

        [Test]
        public void SameOutcomeIsPaidOnlyOnce()
        {
            var save = new SaveData();
            MissionOutcome outcome = Outcome(3);
            Progress.Grant(save, Mission(), outcome);
            Assert.AreEqual(0, Progress.Grant(save, Mission(), outcome));
            Assert.AreEqual(220, save.Credits);
            Assert.AreEqual(1, save.Find("m").Completions);
        }

        [Test]
        public void ReplayPaysOnlyForNewStars()
        {
            var save = new SaveData();
            Progress.Grant(save, Mission(), Outcome(1));
            Assert.AreEqual(80, Progress.Grant(save, Mission(), Outcome(3)));
            Assert.AreEqual(10, Progress.Grant(save, Mission(), Outcome(2)), "nothing new: flat replay reward");
            Assert.AreEqual(3, save.Find("m").Stars, "stars never go down");
        }

        [Test]
        public void BestTimeKeepsTheFastestRun()
        {
            var save = new SaveData();
            Progress.Grant(save, Mission(), Outcome(1, 120f));
            Progress.Grant(save, Mission(), Outcome(1, 90f));
            Progress.Grant(save, Mission(), Outcome(1, 150f));
            Assert.AreEqual(90f, save.Find("m").BestTime);
        }

        [Test]
        public void FailedMissionPaysNothing()
        {
            var save = new SaveData();
            Assert.AreEqual(0, Progress.Grant(save, Mission(), Outcome(0)));
            Assert.AreEqual(0, save.Credits);
            Assert.IsNull(save.Find("m"));
        }
    }
}
