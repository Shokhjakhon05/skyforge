using System;
using System.Collections.Generic;
using UnityEngine;

namespace Skyforge.Core
{
    public enum DailyModifier
    {
        /// <summary>The mission's time goal becomes a deadline.</summary>
        AgainstTheClock,
        /// <summary>The battery starts at 70%.</summary>
        LowBattery,
        /// <summary>Any damage to the drone and the operation does not count.</summary>
        NotAScratch,
    }

    /// <summary>One of the day's three operations: a mission the player has already beaten, with a twist.</summary>
    public sealed class DailyOp
    {
        public string RegionId;
        public string MissionId;
        public DailyModifier Modifier;
        public int Credits;
        public int Parts;

        public string ModifierKey => "daily.modifier." + Modifier;
    }

    /// <summary>
    /// Daily operations and the login streak. Everything is local and driven by the device's date:
    /// it survives restarts and time-zone changes, and makes no claim to stop someone who changes
    /// the clock. Skipping a day never takes anything away.
    /// </summary>
    public static class Daily
    {
        public const int OpsPerDay = 3;
        public static readonly int[] StreakCredits = { 60, 80, 100, 130, 160, 200, 300 };
        public static readonly int[] StreakParts = { 0, 0, 1, 0, 0, 1, 3 };

        public static string DateKey(DateTime date) => date.ToString("yyyy-MM-dd");

        /// <summary>
        /// The date the game treats as "today". If the device clock has gone backwards since the
        /// last session, today stays where it was until the clock catches up, so the same day's
        /// rewards cannot be collected again by turning the date back.
        /// </summary>
        public static string Today(SaveData save, DateTime now)
        {
            string key = DateKey(now);
            if (string.CompareOrdinal(key, save.LastDate) < 0) return save.LastDate;
            save.LastDate = key;
            return key;
        }

        /// <summary>
        /// The day's operations, the same for a given date and set of beaten missions. With fewer
        /// than three missions beaten there are fewer operations.
        /// </summary>
        /// <param name="beaten">Region id and mission id of every mission the player has completed.</param>
        /// <param name="rewards">Base reward of each mission in <paramref name="beaten"/>.</param>
        public static List<DailyOp> Ops(string today, IList<KeyValuePair<string, string>> beaten, IList<int> rewards)
        {
            var ops = new List<DailyOp>();
            if (beaten.Count == 0) return ops;

            var random = new System.Random(StableHash(today));
            var indices = new List<int>();
            for (int i = 0; i < beaten.Count; i++) indices.Add(i);
            int modifiers = Enum.GetValues(typeof(DailyModifier)).Length;

            for (int slot = 0; slot < OpsPerDay && indices.Count > 0; slot++)
            {
                int pick = random.Next(indices.Count);
                int index = indices[pick];
                indices.RemoveAt(pick);
                ops.Add(new DailyOp
                {
                    RegionId = beaten[index].Key,
                    MissionId = beaten[index].Value,
                    Modifier = (DailyModifier)((slot + random.Next(modifiers)) % modifiers),
                    Credits = Mathf.RoundToInt(rewards[index] * 0.6f / 10f) * 10,
                    Parts = slot == OpsPerDay - 1 ? 2 : 1,
                });
            }
            return ops;
        }

        /// <summary>string.GetHashCode differs between runs and platforms; the day's operations must not.</summary>
        static int StableHash(string text)
        {
            unchecked
            {
                int hash = 23;
                foreach (char c in text) hash = hash * 31 + c;
                return hash;
            }
        }

        /// <summary>The mission as the modifier leaves it.</summary>
        public static MissionDefinition Apply(MissionDefinition mission, DailyModifier modifier)
        {
            MissionDefinition changed = mission.Copy();
            if (modifier == DailyModifier.LowBattery) changed.BatteryStart = Mathf.Min(changed.BatteryStart, 0.7f);
            return changed;
        }

        /// <summary>Whether a finished attempt satisfies the day's twist.</summary>
        public static bool Satisfied(MissionOutcome outcome, DailyModifier modifier)
        {
            if (!outcome.Succeeded) return false;
            switch (modifier)
            {
                case DailyModifier.AgainstTheClock: return outcome.TimeGoalMet;
                case DailyModifier.NotAScratch: return outcome.NoDamage;
                default: return true;
            }
        }

        public static bool IsDone(SaveData save, string today, int slot)
        {
            return save.DailyDate == today && save.DailyDone.Contains(slot);
        }

        /// <summary>Pays for a completed daily operation, once per day and slot.</summary>
        public static bool Complete(SaveData save, string today, int slot, DailyOp op)
        {
            if (save.DailyDate != today)
            {
                save.DailyDate = today;
                save.DailyDone.Clear();
            }
            if (save.DailyDone.Contains(slot)) return false;
            save.DailyDone.Add(slot);
            save.Credits += op.Credits;
            save.Parts += op.Parts;
            save.Stats.DailyDone++;
            return true;
        }

        public static bool CanClaimStreak(SaveData save, string today) => save.StreakDate != today;

        /// <summary>The streak day (1..7) the next claim would pay for.</summary>
        public static int NextStreakDay(SaveData save, string today)
        {
            if (string.IsNullOrEmpty(save.StreakDate)) return 1;
            int gap = DaysApart(save.StreakDate, today);
            if (gap <= 0) return Mathf.Clamp(save.StreakDay, 1, StreakCredits.Length);
            // The day after: one step up. Each further day missed costs one step, never the whole streak.
            int day = save.StreakDay + 1 - (gap - 1) * 2;
            if (day > StreakCredits.Length) day = 1;
            return Mathf.Clamp(day, 1, StreakCredits.Length);
        }

        /// <summary>Claims today's login reward. Returns the streak day paid, or 0 if already claimed.</summary>
        public static int ClaimStreak(SaveData save, string today)
        {
            if (!CanClaimStreak(save, today)) return 0;
            int day = NextStreakDay(save, today);
            save.StreakDay = day;
            save.StreakDate = today;
            save.Credits += StreakCredits[day - 1];
            save.Parts += StreakParts[day - 1];
            save.Stats.BestStreak = Mathf.Max(save.Stats.BestStreak, day);
            return day;
        }

        /// <summary>Whole days from one "yyyy-MM-dd" date to another; a large number if either is unreadable.</summary>
        public static int DaysApart(string from, string to)
        {
            if (!DateTime.TryParseExact(from, "yyyy-MM-dd", null, System.Globalization.DateTimeStyles.None, out DateTime a)) return 99;
            if (!DateTime.TryParseExact(to, "yyyy-MM-dd", null, System.Globalization.DateTimeStyles.None, out DateTime b)) return 99;
            return (int)Math.Round((b - a).TotalDays);
        }
    }

    /// <summary>Counters kept for achievements.</summary>
    [Serializable]
    public sealed class PlayerStats
    {
        public int Flights;
        public int Crashes;
        public float DistanceMeters;
        public int Laps;
        public int DailyDone;
        public int BestStreak;
        public int CreditsEarned;
        public int CachesFound;
        public int UpgradesBought;
        /// <summary>Hostile targets destroyed, over the whole career.</summary>
        public int Kills;
    }

    public sealed class Achievement
    {
        public string Id;
        /// <summary>Which number it watches; several tiers share one kind.</summary>
        public string Kind;
        public int Goal;
        public int Parts;

        public string NameKey => "achievement." + Kind;
    }

    /// <summary>
    /// Forty achievements in tiers. Each watches one number that the save already holds, so none
    /// can be missed or lost; reaching a goal pays parts once.
    /// </summary>
    public static class Achievements
    {
        static readonly List<Achievement> List = Build();

        public static IReadOnlyList<Achievement> All => List;

        static List<Achievement> Build()
        {
            var list = new List<Achievement>();
            void Tiers(string kind, params int[] goals)
            {
                for (int i = 0; i < goals.Length; i++)
                    list.Add(new Achievement { Id = kind + (i + 1), Kind = kind, Goal = goals[i], Parts = 1 + i });
            }
            Tiers("missions", 1, 5, 15, 30, 45);
            Tiers("stars", 10, 30, 60, 100, 135);
            Tiers("perfect", 3, 10, 25, 45);
            Tiers("drones", 2, 4, 8, 12);
            Tiers("upgrades", 3, 10, 25);
            Tiers("modules", 1, 3, 6);
            Tiers("regions", 2, 3);
            Tiers("daily", 1, 7, 30);
            Tiers("streak", 3, 7);
            Tiers("distance", 10, 50, 200);
            Tiers("laps", 1, 10);
            Tiers("caches", 3, 9);
            Tiers("flights", 10, 100);
            return list;
        }

        /// <summary>The number an achievement of this kind is watching.</summary>
        public static int Value(SaveData save, string kind)
        {
            switch (kind)
            {
                case "missions":
                    int done = 0;
                    foreach (MissionRecord record in save.Missions)
                        if (record.Completions > 0) done++;
                    return done;
                case "stars": return Garage.TotalStars(save);
                case "perfect":
                    int perfect = 0;
                    foreach (MissionRecord record in save.Missions)
                        if (record.Stars >= 3) perfect++;
                    return perfect;
                case "drones": return save.Drones.Count;
                case "upgrades": return save.Stats.UpgradesBought;
                case "modules": return save.OwnedModules.Count;
                case "regions":
                    int open = 0, stars = Garage.TotalStars(save);
                    foreach (RegionDefinition region in RegionCatalog.All)
                        if (stars >= region.StarsRequired) open++;
                    return open;
                case "daily": return save.Stats.DailyDone;
                case "streak": return save.Stats.BestStreak;
                case "distance": return Mathf.FloorToInt(save.Stats.DistanceMeters / 1000f);
                case "laps": return save.Stats.Laps;
                case "caches": return save.Stats.CachesFound;
                case "flights": return save.Stats.Flights;
                default: return 0;
            }
        }

        public static bool IsEarned(SaveData save, Achievement achievement) => save.Achievements.Contains(achievement.Id);

        /// <summary>Awards everything newly reached and returns it, so the game can announce it.</summary>
        public static List<Achievement> Collect(SaveData save)
        {
            var earned = new List<Achievement>();
            foreach (Achievement achievement in List)
            {
                if (IsEarned(save, achievement)) continue;
                if (Value(save, achievement.Kind) < achievement.Goal) continue;
                save.Achievements.Add(achievement.Id);
                save.Parts += achievement.Parts;
                earned.Add(achievement);
            }
            return earned;
        }
    }

    /// <summary>Hidden caches: three per region, found by flying up to them.</summary>
    public static class Caches
    {
        public const int PerRegion = 3;
        public const float PickupRadius = 9f;
        public const int PartsEach = 2;
        static readonly string[] Anchors = { "summit", "coast4", "poi5" };

        /// <summary>Where a region's cache floats.</summary>
        public static Vector3 Position(Island island, int index)
        {
            return island.Anchors[Anchors[index]] + Vector3.up * 6f;
        }

        public static string Key(string regionId, int index) => regionId + ":" + index;

        public static bool IsFound(SaveData save, string regionId, int index) => save.Caches.Contains(Key(regionId, index));

        /// <summary>Collects a cache the drone has reached. Returns its index, or -1.</summary>
        public static int TryCollect(SaveData save, string regionId, Island island, Vector3 drone)
        {
            for (int i = 0; i < PerRegion; i++)
            {
                if (IsFound(save, regionId, i)) continue;
                if (Vector3.Distance(drone, Position(island, i)) > PickupRadius) continue;
                save.Caches.Add(Key(regionId, i));
                save.Parts += PartsEach;
                save.Stats.CachesFound++;
                return i;
            }
            return -1;
        }
    }
}
