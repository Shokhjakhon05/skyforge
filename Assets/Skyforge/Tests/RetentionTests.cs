using System;
using System.Collections.Generic;
using NUnit.Framework;
using Skyforge.Core;
using UnityEngine;

namespace Skyforge.Tests
{
    public class DailyTests
    {
        static List<KeyValuePair<string, string>> Beaten(int count)
        {
            var list = new List<KeyValuePair<string, string>>();
            for (int i = 0; i < count; i++) list.Add(new KeyValuePair<string, string>("arvelia", "m" + i));
            return list;
        }

        static List<int> Rewards(int count)
        {
            var list = new List<int>();
            for (int i = 0; i < count; i++) list.Add(150 + i * 30);
            return list;
        }

        [Test]
        public void SameDayGivesTheSameOperationsAndAnotherDayDiffers()
        {
            List<DailyOp> a = Daily.Ops("2026-10-10", Beaten(10), Rewards(10));
            List<DailyOp> b = Daily.Ops("2026-10-10", Beaten(10), Rewards(10));
            Assert.AreEqual(Daily.OpsPerDay, a.Count);
            for (int i = 0; i < a.Count; i++)
            {
                Assert.AreEqual(a[i].MissionId, b[i].MissionId);
                Assert.AreEqual(a[i].Modifier, b[i].Modifier);
                Assert.Greater(a[i].Credits, 0);
                Assert.Greater(a[i].Parts, 0);
            }

            var firstPicks = new HashSet<string>();
            for (int day = 10; day < 24; day++)
                firstPicks.Add(Daily.Ops("2026-10-" + day, Beaten(10), Rewards(10))[0].MissionId);
            Assert.Greater(firstPicks.Count, 3, "two weeks of days should not all open with the same mission");
        }

        [Test]
        public void OperationsOfOneDayAreDifferentMissions()
        {
            for (int day = 1; day <= 28; day++)
            {
                var seen = new HashSet<string>();
                foreach (DailyOp op in Daily.Ops("2026-02-" + day.ToString("00"), Beaten(6), Rewards(6)))
                    Assert.IsTrue(seen.Add(op.MissionId), "a mission repeats on day " + day);
            }
        }

        [Test]
        public void FewBeatenMissionsMeanFewerOperations()
        {
            Assert.AreEqual(0, Daily.Ops("2026-10-10", Beaten(0), Rewards(0)).Count);
            Assert.AreEqual(2, Daily.Ops("2026-10-10", Beaten(2), Rewards(2)).Count);
        }

        [Test]
        public void EveryModifierHasItsTextAndRule()
        {
            foreach (DailyModifier modifier in Enum.GetValues(typeof(DailyModifier)))
                Assert.IsTrue(Loc.Has("daily.modifier." + modifier), modifier.ToString());

            var slow = new MissionOutcome { Succeeded = true, TimeGoalMet = false, NoDamage = true };
            var dented = new MissionOutcome { Succeeded = true, TimeGoalMet = true, NoDamage = false };
            var failed = new MissionOutcome { Succeeded = false, TimeGoalMet = true, NoDamage = true };
            Assert.IsFalse(Daily.Satisfied(slow, DailyModifier.AgainstTheClock));
            Assert.IsTrue(Daily.Satisfied(slow, DailyModifier.NotAScratch));
            Assert.IsFalse(Daily.Satisfied(dented, DailyModifier.NotAScratch));
            Assert.IsTrue(Daily.Satisfied(dented, DailyModifier.LowBattery));
            Assert.IsFalse(Daily.Satisfied(failed, DailyModifier.LowBattery));

            var mission = new MissionDefinition { Id = "m" };
            Assert.AreEqual(0.7f, Daily.Apply(mission, DailyModifier.LowBattery).BatteryStart);
            Assert.AreEqual(1f, mission.BatteryStart, "the original mission is untouched");
        }

        [Test]
        public void ADailyOperationPaysOncePerDay()
        {
            var save = new SaveData();
            var op = new DailyOp { Credits = 100, Parts = 1 };
            Assert.IsTrue(Daily.Complete(save, "2026-10-10", 0, op));
            Assert.IsFalse(Daily.Complete(save, "2026-10-10", 0, op));
            Assert.IsTrue(Daily.Complete(save, "2026-10-10", 1, op));
            Assert.AreEqual(200, save.Credits);
            Assert.IsTrue(Daily.IsDone(save, "2026-10-10", 0));
            Assert.IsFalse(Daily.IsDone(save, "2026-10-11", 0), "a new day starts clean");
            Assert.IsTrue(Daily.Complete(save, "2026-10-11", 0, op));
            Assert.AreEqual(300, save.Credits);
            Assert.AreEqual(3, save.Stats.DailyDone);
            Assert.IsTrue(save.IsValid());
        }

        [Test]
        public void TurningTheClockBackDoesNotBringYesterdayBack()
        {
            var save = new SaveData();
            Assert.AreEqual("2026-10-10", Daily.Today(save, new DateTime(2026, 10, 10, 23, 30, 0)));
            Assert.AreEqual("2026-10-11", Daily.Today(save, new DateTime(2026, 10, 11, 0, 5, 0)));
            Assert.AreEqual("2026-10-11", Daily.Today(save, new DateTime(2026, 10, 9, 12, 0, 0)), "the clock went back");
            Assert.AreEqual("2026-10-12", Daily.Today(save, new DateTime(2026, 10, 12, 8, 0, 0)));
        }

        [Test]
        public void StreakClimbsDayByDayAndWrapsAfterSeven()
        {
            var save = new SaveData();
            int credits = 0;
            for (int day = 1; day <= 8; day++)
            {
                string date = "2026-10-" + day.ToString("00");
                Assert.IsTrue(Daily.CanClaimStreak(save, date));
                int paid = Daily.ClaimStreak(save, date);
                Assert.AreEqual(day <= 7 ? day : 1, paid);
                credits += Daily.StreakCredits[paid - 1];
                Assert.AreEqual(0, Daily.ClaimStreak(save, date), "once a day");
            }
            Assert.AreEqual(credits, save.Credits);
            Assert.AreEqual(7, save.Stats.BestStreak);
        }

        [Test]
        public void MissingADayStepsTheStreakBackInsteadOfResettingIt()
        {
            var save = new SaveData();
            for (int day = 1; day <= 5; day++) Daily.ClaimStreak(save, "2026-10-0" + day);
            Assert.AreEqual(5, save.StreakDay);

            // Back the day after tomorrow: one day was missed.
            Assert.AreEqual(4, Daily.ClaimStreak(save, "2026-10-07"));
            // A long absence still never goes below the first day.
            Assert.AreEqual(1, Daily.ClaimStreak(save, "2026-11-20"));
        }
    }

    public class AchievementTests
    {
        [Test]
        public void FortyAchievementsWithUniqueIdsAndTexts()
        {
            var ids = new HashSet<string>();
            foreach (Achievement achievement in Achievements.All)
            {
                Assert.IsTrue(ids.Add(achievement.Id), achievement.Id);
                Assert.IsTrue(Loc.Has(achievement.NameKey), achievement.NameKey);
                Assert.Greater(achievement.Goal, 0);
                Assert.Greater(achievement.Parts, 0);
            }
            Assert.AreEqual(40, ids.Count);
        }

        [Test]
        public void EveryAchievementCanBeReachedWithTheContentThatExists()
        {
            var limits = new Dictionary<string, int>
            {
                { "missions", 45 }, { "stars", 135 }, { "perfect", 45 }, { "drones", DroneCatalog.All.Count },
                { "upgrades", DroneCatalog.All.Count * Loadout.Tracks * Loadout.MaxLevel }, { "modules", Loadout.Modules.Length },
                { "regions", RegionCatalog.All.Count }, { "caches", RegionCatalog.All.Count * Caches.PerRegion },
                { "streak", Daily.StreakCredits.Length },
            };
            foreach (Achievement achievement in Achievements.All)
                if (limits.TryGetValue(achievement.Kind, out int limit))
                    Assert.LessOrEqual(achievement.Goal, limit, achievement.Id);
        }

        [Test]
        public void AFreshSaveHasEarnedNothingAndProgressPaysOnce()
        {
            var save = new SaveData();
            Assert.IsEmpty(Achievements.Collect(save));

            save.GetOrAdd("first_contact").Completions = 1;
            save.GetOrAdd("first_contact").Stars = 3;
            List<Achievement> earned = Achievements.Collect(save);
            Assert.IsTrue(earned.Exists(a => a.Id == "missions1"));
            Assert.IsFalse(earned.Exists(a => a.Id == "missions2"));
            int parts = save.Parts;
            Assert.Greater(parts, 0);

            Assert.IsEmpty(Achievements.Collect(save), "nothing is paid twice");
            Assert.AreEqual(parts, save.Parts);
            Assert.IsTrue(save.IsValid());
        }

        [Test]
        public void CountersFeedTheirAchievements()
        {
            var save = new SaveData();
            save.Stats.DistanceMeters = 51000f;
            save.Stats.Laps = 1;
            save.Stats.Flights = 10;
            Garage.BuyModule(new SaveData { Credits = 99999 }, "decoy");
            List<Achievement> earned = Achievements.Collect(save);
            Assert.IsTrue(earned.Exists(a => a.Id == "distance1"));
            Assert.IsTrue(earned.Exists(a => a.Id == "distance2"));
            Assert.IsFalse(earned.Exists(a => a.Id == "distance3"));
            Assert.IsTrue(earned.Exists(a => a.Id == "laps1"));
            Assert.IsTrue(earned.Exists(a => a.Id == "flights1"));
        }

        [Test]
        public void UpgradesAreCounted()
        {
            var save = new SaveData { Credits = 100000, Parts = 50 };
            DroneDefinition sparrow = DroneCatalog.Get(DroneCatalog.Starter);
            for (int i = 0; i < 3; i++) Garage.Upgrade(save, sparrow, UpgradeTrack.Motors);
            Assert.AreEqual(3, save.Stats.UpgradesBought);
            Assert.AreEqual(3, Achievements.Value(save, "upgrades"));
        }
    }

    public class CacheTests
    {
        [Test]
        public void EveryRegionHasThreeReachableCaches()
        {
            foreach (RegionDefinition region in RegionCatalog.All)
            {
                Island island = Island.Generate(region.Seed, region.Biome);
                for (int i = 0; i < Caches.PerRegion; i++)
                {
                    Vector3 position = Caches.Position(island, i);
                    float ground = Mathf.Max(island.Map.Height(position.x, position.z), Island.WaterLevel);
                    Assert.Greater(position.y - ground, 3f, region.Id + " cache " + i);
                    Assert.Less(position.y, FlightSim.Ceiling - 10f, region.Id + " cache " + i);
                    Assert.IsFalse(island.Obstacles.Hits(position, 1f), region.Id + " cache " + i);
                }
            }
        }

        [Test]
        public void ACacheIsCollectedOnceWhenTheDroneReachesIt()
        {
            Island island = Island.Generate(7);
            var save = new SaveData();
            Vector3 cache = Caches.Position(island, 1);

            Assert.AreEqual(-1, Caches.TryCollect(save, "arvelia", island, cache + Vector3.right * 50f));
            Assert.AreEqual(1, Caches.TryCollect(save, "arvelia", island, cache + Vector3.right * 4f));
            Assert.AreEqual(Caches.PartsEach, save.Parts);
            Assert.AreEqual(-1, Caches.TryCollect(save, "arvelia", island, cache), "already taken");
            Assert.AreEqual(Caches.PartsEach, save.Parts);
            Assert.IsTrue(Caches.IsFound(save, "arvelia", 1));
            Assert.IsFalse(Caches.IsFound(save, "kharad", 1), "each region has its own");
            Assert.AreEqual(1, save.Stats.CachesFound);
        }
    }
}
