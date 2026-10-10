using System;
using System.Collections.Generic;
using UnityEngine;

namespace Skyforge.Core
{
    [Serializable]
    public sealed class TaskState
    {
        public string Id;
        public int Progress;
        public bool Claimed;
    }

    [Serializable]
    public sealed class MasteryRecord
    {
        public string Drone;
        public int Kills;
    }

    /// <summary>Everything about the career that is saved: rank, season, tasks, collection, base, the war.</summary>
    [Serializable]
    public sealed class CareerData
    {
        public int Xp;
        public int Prestige;
        /// <summary>The highest rank whose reward has been paid in this prestige.</summary>
        public int RankPaid = 1;

        public string Season = "";
        public int SeasonXp;
        public int SeasonPaid;

        public string TaskDay = "", TaskWeek = "";
        public List<TaskState> Daily = new List<TaskState>();
        public List<TaskState> Weekly = new List<TaskState>();

        public List<MasteryRecord> Mastery = new List<MasteryRecord>();

        public List<string> Skins = new List<string>();
        /// <summary>"drone=skin" for every aircraft that wears one.</summary>
        public List<string> Worn = new List<string>();
        public List<string> Emblems = new List<string> { "star" };
        public string Emblem = "star";
        /// <summary>Unopened cases, by tier.</summary>
        public int[] Cases = new int[CaseRules.Tiers];

        public int[] Base = new int[BaseRules.Tracks];
        public int[] Armoury = new int[ArmouryRules.Tracks];

        /// <summary>Sectors held, as "region:index".</summary>
        public List<string> Sectors = new List<string>();
        public string WarDate = "";

        public int SurvivalBest;
        public int TowerFloor;
        public int ContractsDone;
        public List<string> Story = new List<string>();
        public bool Haptics = true;

        public void Repair()
        {
            if (Daily == null) Daily = new List<TaskState>();
            if (Weekly == null) Weekly = new List<TaskState>();
            if (Mastery == null) Mastery = new List<MasteryRecord>();
            if (Skins == null) Skins = new List<string>();
            if (Worn == null) Worn = new List<string>();
            if (Emblems == null || Emblems.Count == 0) Emblems = new List<string> { "star" };
            if (string.IsNullOrEmpty(Emblem)) Emblem = "star";
            if (Cases == null || Cases.Length != CaseRules.Tiers) Cases = new int[CaseRules.Tiers];
            if (Base == null || Base.Length != BaseRules.Tracks) Base = new int[BaseRules.Tracks];
            if (Armoury == null || Armoury.Length != ArmouryRules.Tracks) Armoury = new int[ArmouryRules.Tracks];
            if (Sectors == null) Sectors = new List<string>();
            if (Story == null) Story = new List<string>();
            Season = Season ?? "";
            TaskDay = TaskDay ?? "";
            TaskWeek = TaskWeek ?? "";
            WarDate = WarDate ?? "";
            RankPaid = Mathf.Clamp(RankPaid, 1, Career.Ranks);
            Xp = Mathf.Max(0, Xp);
        }
    }

    /// <summary>Something handed to the player: any mix of currency, cases and collectables.</summary>
    public sealed class Reward
    {
        public int Credits, Parts, Xp;
        public int[] Cases = new int[CaseRules.Tiers];
        public string Skin, Emblem;

        public bool Empty => Credits == 0 && Parts == 0 && Xp == 0 && Skin == null && Emblem == null && Cases[0] + Cases[1] + Cases[2] == 0;

        public void Add(Reward other)
        {
            Credits += other.Credits;
            Parts += other.Parts;
            Xp += other.Xp;
            for (int i = 0; i < Cases.Length; i++) Cases[i] += other.Cases[i];
            Skin = other.Skin ?? Skin;
            Emblem = other.Emblem ?? Emblem;
        }

        /// <summary>Pays everything but the experience, which the caller routes through the rank.</summary>
        public void Grant(SaveData save)
        {
            save.Credits += Credits;
            save.Stats.CreditsEarned += Credits;
            save.Parts += Parts;
            for (int i = 0; i < Cases.Length; i++) save.Career.Cases[i] += Cases[i];
            if (Skin != null && !save.Career.Skins.Contains(Skin)) save.Career.Skins.Add(Skin);
            if (Emblem != null && !save.Career.Emblems.Contains(Emblem)) save.Career.Emblems.Add(Emblem);
        }

        /// <summary>The reward in words: "+300 credits, +2 parts, elite case".</summary>
        public string Describe()
        {
            var parts = new List<string>();
            if (Credits > 0) parts.Add(Loc.T("reward.credits", Credits));
            if (Parts > 0) parts.Add(Loc.T("reward.parts", Parts));
            if (Xp > 0) parts.Add(Loc.T("reward.xp", Xp));
            for (int i = 0; i < Cases.Length; i++)
                if (Cases[i] > 0) parts.Add(Loc.T("case." + i) + (Cases[i] > 1 ? " ×" + Cases[i] : ""));
            if (Skin != null) parts.Add(Loc.T("reward.skin", Loc.T("skin." + Skin)));
            if (Emblem != null) parts.Add(Loc.T("reward.emblem"));
            return string.Join(", ", parts);
        }
    }

    /// <summary>What one flight amounted to, in the numbers the career counts.</summary>
    public sealed class MissionReport
    {
        public string Mode = "campaign", Drone = "", Region = "";
        public bool Succeeded;
        public int Stars, Kills, Rockets, Bombs, Cannon, Support, Air, Armour, Waves, Level, Sector = -1;

        public static MissionReport From(MissionRun run, MissionDefinition mission, MissionOutcome outcome, string drone)
        {
            return new MissionReport
            {
                Mode = mission.Mode, Drone = drone, Region = mission.Region, Succeeded = outcome.Succeeded, Stars = outcome.Stars,
                Kills = run.Kills - run.KillsBy(Weapon.Ally),
                Rockets = run.KillsBy(Weapon.Rocket), Bombs = run.KillsBy(Weapon.Bomb), Cannon = run.KillsBy(Weapon.Cannon),
                Support = run.KillsBy(Weapon.Support), Air = run.AirKills, Armour = run.ArmourKills,
                Waves = mission.Mode == "survival" ? Mathf.Max(0, run.ObjectiveIndex - 1) : 0, Level = mission.Level,
            };
        }
    }

    /// <summary>What a report earned, for the result screen.</summary>
    public sealed class CareerGain
    {
        public int Xp, RankBefore, RankAfter;
        public Reward Rewards = new Reward();
        public List<string> News = new List<string>();
    }

    /// <summary>Experience, ranks and prestige.</summary>
    public static class Career
    {
        public const int Ranks = 30, MaxPrestige = 5;

        /// <summary>Experience needed to go from a rank to the next.</summary>
        public static int Step(int rank) => 300 + 140 * (Mathf.Clamp(rank, 1, Ranks) - 1);

        /// <summary>Total experience at which a rank is reached.</summary>
        public static int XpFor(int rank)
        {
            int total = 0;
            for (int r = 1; r < Mathf.Clamp(rank, 1, Ranks); r++) total += Step(r);
            return total;
        }

        public static int Rank(int xp)
        {
            int rank = 1;
            while (rank < Ranks && xp >= XpFor(rank + 1)) rank++;
            return rank;
        }

        /// <summary>Ten names, three grades each: "Captain II".</summary>
        public static string RankName(int rank)
        {
            rank = Mathf.Clamp(rank, 1, Ranks);
            string[] grades = { "I", "II", "III" };
            return Loc.T("rank." + (rank - 1) / 3) + " " + grades[(rank - 1) % 3];
        }

        /// <summary>The level at which the generator builds contracts for this player.</summary>
        public static int ContractLevel(SaveData save) =>
            Mathf.Clamp(Rank(save.Career.Xp) + save.Career.Prestige * 4, 1, Operations.MaxLevel);

        public static int XpOf(MissionReport report)
        {
            if (report.Mode == "range") return 0;
            int kills = Mathf.Min(report.Kills, 25);
            if (report.Mode == "survival") return 20 + report.Waves * 45 + kills * 2;
            if (!report.Succeeded) return 10 + kills * 3;
            float xp = 60 + 25 * report.Stars + 6 * kills + report.Level * 8;
            if (report.Mode == "veteran") xp *= 1.4f;
            if (report.Mode == "raid") xp += 80;
            return Mathf.RoundToInt(xp);
        }

        public static Reward RankReward(int rank)
        {
            var reward = new Reward { Credits = 150 * rank };
            if (rank % 2 == 0) reward.Cases[0] = 1;
            if (rank % 5 == 0)
            {
                reward.Cases[1] = 1;
                reward.Parts = 5;
            }
            if (rank % 10 == 0) reward.Cases[2] = 1;
            if (rank % 5 == 0) reward.Emblem = Emblems.All[Mathf.Clamp(rank / 5, 1, Emblems.All.Length - 1)];
            return reward;
        }

        /// <summary>
        /// Books one flight: experience, rank rewards, season, tasks, mastery, records and the
        /// war. Everything it pays is already in the save when it returns.
        /// </summary>
        public static CareerGain Apply(SaveData save, MissionReport report, string today)
        {
            CareerData career = save.Career;
            var gain = new CareerGain { RankBefore = Rank(career.Xp), Xp = XpOf(report) };

            Tasks.Refresh(save, today);
            Seasons.Roll(save, today);
            gain.News.AddRange(Tasks.Count(save, report));
            string mastery = Mastery.Count(save, report, gain.Rewards);
            if (mastery != null) gain.News.Add(mastery);

            if (report.Mode == "survival" && report.Waves > career.SurvivalBest)
            {
                career.SurvivalBest = report.Waves;
                gain.News.Add(Loc.T("news.survival", report.Waves));
            }
            if (report.Succeeded && (report.Mode == "contract" || report.Mode == "sector" || report.Mode == "raid")) career.ContractsDone++;
            if (report.Succeeded && report.Mode == "sector" && report.Sector >= 0 && War.Capture(save, report.Region, report.Sector))
                gain.News.Add(Loc.T("news.sector"));

            AddXp(save, gain.Xp, gain);
            gain.Rewards.Grant(save);
            return gain;
        }

        /// <summary>Adds experience to the rank and the season and collects whatever ranks that passes.</summary>
        public static void AddXp(SaveData save, int xp, CareerGain gain)
        {
            CareerData career = save.Career;
            career.Xp += Mathf.Max(0, xp);
            career.SeasonXp += Mathf.Max(0, xp);
            int rank = Rank(career.Xp);
            gain.RankAfter = rank;
            while (career.RankPaid < rank)
            {
                career.RankPaid++;
                gain.Rewards.Add(RankReward(career.RankPaid));
                gain.News.Add(Loc.T("news.rank", RankName(career.RankPaid)));
            }
        }

        public static bool CanPrestige(SaveData save) => Rank(save.Career.Xp) >= Ranks && save.Career.Prestige < MaxPrestige;

        /// <summary>Hands the rank back for a prestige star: the climb starts again, harder contracts, a legendary case.</summary>
        public static Reward TakePrestige(SaveData save)
        {
            if (!CanPrestige(save)) return null;
            CareerData career = save.Career;
            career.Prestige++;
            career.Xp = 0;
            career.RankPaid = 1;
            var reward = new Reward { Emblem = "prestige" + career.Prestige };
            reward.Cases[2] = 1;
            reward.Grant(save);
            return reward;
        }
    }

    /// <summary>A month-long track of thirty rewards, earned with the same experience as the rank.</summary>
    public static class Seasons
    {
        public const int Tiers = 30, XpPerTier = 350;

        public static string Id(string today) => today != null && today.Length >= 7 ? today.Substring(0, 7) : "";

        /// <summary>Starts a new season when the month has changed; unclaimed tiers of the old one are paid out first.</summary>
        public static void Roll(SaveData save, string today)
        {
            string id = Id(today);
            if (save.Career.Season == id) return;
            if (!string.IsNullOrEmpty(save.Career.Season)) Claim(save);
            save.Career.Season = id;
            save.Career.SeasonXp = 0;
            save.Career.SeasonPaid = 0;
        }

        public static int Tier(SaveData save) => Mathf.Clamp(save.Career.SeasonXp / XpPerTier, 0, Tiers);

        public static int DaysLeft(string today)
        {
            if (!DateTime.TryParse(today, out DateTime date)) return 0;
            return DateTime.DaysInMonth(date.Year, date.Month) - date.Day + 1;
        }

        /// <summary>The skin that belongs to a season: twelve of them, one per month of the year.</summary>
        public static string Skin(string season)
        {
            int month = season != null && season.Length >= 7 && int.TryParse(season.Substring(5, 2), out int m) ? m : 1;
            return Skins.Seasonal[(month - 1) % Skins.Seasonal.Length];
        }

        public static Reward TierReward(int tier, string season)
        {
            var reward = new Reward();
            if (tier % 3 == 1) reward.Credits = 200 + 20 * tier;
            else if (tier % 3 == 2) reward.Parts = 2 + tier / 5;
            else reward.Cases[tier % 15 == 0 ? 2 : tier % 6 == 0 ? 1 : 0] = 1;
            if (tier == Tiers) reward.Skin = Skin(season);
            if (tier == 15) reward.Emblem = "season";
            return reward;
        }

        public static int Unclaimed(SaveData save) => Mathf.Max(0, Tier(save) - save.Career.SeasonPaid);

        /// <summary>Pays every tier reached and not yet paid. Null when there is nothing to pay.</summary>
        public static Reward Claim(SaveData save)
        {
            int tier = Tier(save);
            if (save.Career.SeasonPaid >= tier) return null;
            var total = new Reward();
            while (save.Career.SeasonPaid < tier)
            {
                save.Career.SeasonPaid++;
                total.Add(TierReward(save.Career.SeasonPaid, save.Career.Season));
            }
            total.Grant(save);
            return total;
        }
    }

    public sealed class TaskDefinition
    {
        public string Id, Stat;
        public int Goal;
        public bool Weekly;
        public Reward Reward;

        public string Text => Loc.T("task." + Stat, Goal);
    }

    /// <summary>Three tasks a day and three a week, picked by the date so everyone has the same ones.</summary>
    public static class Tasks
    {
        public const int PerDay = 3, PerWeek = 3;
        // Stat and how much of it a day's task asks for; a week's asks for six times that.
        static readonly string[] Stats = { "kills", "rockets", "bombs", "armour", "missions", "stars", "contracts", "waves", "air", "support" };
        static readonly int[] Goals = { 8, 5, 3, 3, 2, 4, 1, 4, 2, 2 };
        /// <summary>The last two need a player who is some way in; a day's tasks leave them out.</summary>
        const int DailyStats = 8;

        public static string Week(string today)
        {
            if (!DateTime.TryParse(today, out DateTime date)) return "";
            return date.Year + "-w" + ((date.DayOfYear - 1) / 7 + 1).ToString("00");
        }

        static int Hash(string text)
        {
            int hash = 17;
            foreach (char c in text) hash = unchecked(hash * 31 + c);
            return hash & 0x7fffffff;
        }

        public static List<TaskDefinition> For(string key, bool weekly)
        {
            var list = new List<TaskDefinition>();
            int pool = weekly ? Stats.Length : DailyStats, hash = Hash(key), count = weekly ? PerWeek : PerDay;
            int first = hash % pool, stride = 1 + (hash / 7) % (pool - 1);
            // Any stride walks distinct entries while the pool has more entries than tasks and the stride divides it badly;
            // a repeat is simply stepped past.
            var used = new List<int>();
            for (int i = 0; used.Count < count; i++)
            {
                int index = (first + i * stride + (i >= pool ? i / pool : 0)) % pool;
                if (used.Contains(index)) continue;
                used.Add(index);
                var reward = new Reward { Xp = weekly ? 600 : 120, Credits = weekly ? 500 : 120 };
                if (weekly) reward.Cases[used.Count == 1 ? 1 : 0] = 1;
                list.Add(new TaskDefinition
                {
                    Id = (weekly ? "w:" : "d:") + key + ":" + Stats[index], Stat = Stats[index],
                    Goal = Goals[index] * (weekly ? 6 : 1), Weekly = weekly, Reward = reward,
                });
            }
            return list;
        }

        /// <summary>Puts today's and this week's tasks in the save, dropping the old ones.</summary>
        public static void Refresh(SaveData save, string today)
        {
            CareerData career = save.Career;
            if (career.TaskDay != today)
            {
                career.TaskDay = today;
                career.Daily.Clear();
                foreach (TaskDefinition task in For(today, false)) career.Daily.Add(new TaskState { Id = task.Id });
            }
            string week = Week(today);
            if (career.TaskWeek != week)
            {
                career.TaskWeek = week;
                career.Weekly.Clear();
                foreach (TaskDefinition task in For(week, true)) career.Weekly.Add(new TaskState { Id = task.Id });
            }
        }

        static int Amount(string stat, MissionReport report)
        {
            switch (stat)
            {
                case "kills": return report.Kills;
                case "rockets": return report.Rockets;
                case "bombs": return report.Bombs;
                case "armour": return report.Armour;
                case "air": return report.Air;
                case "support": return report.Support;
                case "waves": return report.Waves;
                case "missions": return report.Succeeded ? 1 : 0;
                case "stars": return report.Succeeded ? report.Stars : 0;
                case "contracts": return report.Succeeded && (report.Mode == "contract" || report.Mode == "sector" || report.Mode == "raid") ? 1 : 0;
                default: return 0;
            }
        }

        /// <summary>Adds a flight to the tasks. Returns a line for each task it finished.</summary>
        public static List<string> Count(SaveData save, MissionReport report)
        {
            var done = new List<string>();
            if (report.Mode == "range") return done;
            Tally(save.Career.Daily, For(save.Career.TaskDay, false), report, done);
            Tally(save.Career.Weekly, For(save.Career.TaskWeek, true), report, done);
            return done;
        }

        static void Tally(List<TaskState> states, List<TaskDefinition> tasks, MissionReport report, List<string> done)
        {
            foreach (TaskDefinition task in tasks)
            {
                TaskState state = states.Find(s => s.Id == task.Id);
                if (state == null || state.Progress >= task.Goal) continue;
                state.Progress = Mathf.Min(task.Goal, state.Progress + Amount(task.Stat, report));
                if (state.Progress >= task.Goal) done.Add(Loc.T("news.task", task.Text));
            }
        }

        /// <summary>Pays a finished task once. Null if it is not finished or already paid.</summary>
        public static Reward Claim(SaveData save, TaskDefinition task, CareerGain gain)
        {
            List<TaskState> states = task.Weekly ? save.Career.Weekly : save.Career.Daily;
            TaskState state = states.Find(s => s.Id == task.Id);
            if (state == null || state.Claimed || state.Progress < task.Goal) return null;
            state.Claimed = true;
            task.Reward.Grant(save);
            Career.AddXp(save, task.Reward.Xp, gain);
            return task.Reward;
        }

        public static TaskState State(SaveData save, TaskDefinition task) =>
            (task.Weekly ? save.Career.Weekly : save.Career.Daily).Find(s => s.Id == task.Id);
    }

    /// <summary>Kills with each aircraft earn it badges.</summary>
    public static class Mastery
    {
        public static readonly int[] Levels = { 25, 100, 300, 1000 };

        public static int Kills(SaveData save, string drone)
        {
            MasteryRecord record = save.Career.Mastery.Find(m => m.Drone == drone);
            return record != null ? record.Kills : 0;
        }

        public static int Level(int kills)
        {
            int level = 0;
            foreach (int goal in Levels)
                if (kills >= goal) level++;
            return level;
        }

        /// <summary>Returns a line of news when the flight took the aircraft up a level, and adds that level's reward.</summary>
        public static string Count(SaveData save, MissionReport report, Reward rewards)
        {
            if (report.Mode == "range" || report.Kills <= 0 || string.IsNullOrEmpty(report.Drone)) return null;
            MasteryRecord record = save.Career.Mastery.Find(m => m.Drone == report.Drone);
            if (record == null)
            {
                record = new MasteryRecord { Drone = report.Drone };
                save.Career.Mastery.Add(record);
            }
            int before = Level(record.Kills);
            record.Kills += report.Kills;
            int after = Level(record.Kills);
            if (after <= before) return null;
            rewards.Credits += 300 * after;
            if (after >= 3) rewards.Skin = "ace";
            return Loc.T("news.mastery", Loc.T("drone." + report.Drone + ".name"), after);
        }
    }

    public sealed class SkinDefinition
    {
        public string Id;
        /// <summary>0 common, 1 rare, 2 epic, 3 legendary.</summary>
        public int Rarity;
        public Color Body;
        /// <summary>Epic and legendary paint glows faintly in this colour; black for none.</summary>
        public Color Glow;
        /// <summary>Earned, never dropped by a case.</summary>
        public bool Special;
    }

    public static class Skins
    {
        public static readonly string[] Seasonal =
        {
            "season_frost", "season_ember", "season_jade", "season_frost", "season_ember", "season_jade",
            "season_frost", "season_ember", "season_jade", "season_frost", "season_ember", "season_jade",
        };

        static readonly List<SkinDefinition> List = new List<SkinDefinition>
        {
            new SkinDefinition { Id = "sand", Rarity = 0, Body = new Color(0.72f, 0.62f, 0.44f) },
            new SkinDefinition { Id = "olive", Rarity = 0, Body = new Color(0.32f, 0.38f, 0.22f) },
            new SkinDefinition { Id = "navy", Rarity = 0, Body = new Color(0.16f, 0.24f, 0.38f) },
            new SkinDefinition { Id = "snow", Rarity = 0, Body = new Color(0.88f, 0.90f, 0.92f) },
            new SkinDefinition { Id = "crimson", Rarity = 1, Body = new Color(0.62f, 0.10f, 0.10f) },
            new SkinDefinition { Id = "cobalt", Rarity = 1, Body = new Color(0.10f, 0.30f, 0.78f) },
            new SkinDefinition { Id = "tiger", Rarity = 1, Body = new Color(0.90f, 0.48f, 0.08f) },
            new SkinDefinition { Id = "midnight", Rarity = 1, Body = new Color(0.14f, 0.08f, 0.24f) },
            new SkinDefinition { Id = "toxic", Rarity = 2, Body = new Color(0.20f, 0.30f, 0.12f), Glow = new Color(0.25f, 0.9f, 0.1f) },
            new SkinDefinition { Id = "arctic", Rarity = 2, Body = new Color(0.80f, 0.90f, 0.95f), Glow = new Color(0.1f, 0.7f, 1f) },
            new SkinDefinition { Id = "ember", Rarity = 2, Body = new Color(0.22f, 0.08f, 0.06f), Glow = new Color(1f, 0.35f, 0.05f) },
            new SkinDefinition { Id = "gold", Rarity = 3, Body = new Color(0.92f, 0.70f, 0.20f), Glow = new Color(0.9f, 0.6f, 0.1f) },
            new SkinDefinition { Id = "obsidian", Rarity = 3, Body = new Color(0.05f, 0.05f, 0.06f), Glow = new Color(0.1f, 0.85f, 1f) },
            new SkinDefinition { Id = "phantom", Rarity = 3, Body = new Color(0.78f, 0.74f, 0.86f), Glow = new Color(0.7f, 0.3f, 1f) },
            new SkinDefinition { Id = "ace", Rarity = 2, Body = new Color(0.66f, 0.68f, 0.72f), Glow = new Color(1f, 0.8f, 0.3f), Special = true },
            new SkinDefinition { Id = "season_frost", Rarity = 3, Body = new Color(0.30f, 0.55f, 0.75f), Glow = new Color(0.4f, 0.9f, 1f), Special = true },
            new SkinDefinition { Id = "season_ember", Rarity = 3, Body = new Color(0.45f, 0.12f, 0.08f), Glow = new Color(1f, 0.5f, 0.1f), Special = true },
            new SkinDefinition { Id = "season_jade", Rarity = 3, Body = new Color(0.10f, 0.42f, 0.30f), Glow = new Color(0.3f, 1f, 0.6f), Special = true },
        };

        public static IReadOnlyList<SkinDefinition> All => List;

        public static SkinDefinition Get(string id) => List.Find(skin => skin.Id == id);

        /// <summary>The skin an aircraft wears, or null for its factory paint.</summary>
        public static SkinDefinition Worn(SaveData save, string drone)
        {
            string prefix = drone + "=";
            foreach (string entry in save.Career.Worn)
                if (entry.StartsWith(prefix)) return Get(entry.Substring(prefix.Length));
            return null;
        }

        /// <summary>Puts an owned skin on an aircraft; null or empty takes it back to factory paint.</summary>
        public static bool Wear(SaveData save, string drone, string skin)
        {
            if (!string.IsNullOrEmpty(skin) && !save.Career.Skins.Contains(skin)) return false;
            string prefix = drone + "=";
            save.Career.Worn.RemoveAll(entry => entry.StartsWith(prefix));
            if (!string.IsNullOrEmpty(skin)) save.Career.Worn.Add(prefix + skin);
            return true;
        }
    }

    public static class Emblems
    {
        /// <summary>The first is everyone's; the rest come with ranks, the season and prestige.</summary>
        public static readonly string[] All =
        {
            "star", "chevron", "delta", "crosshair", "diamond", "wings", "crown", "season",
            "prestige1", "prestige2", "prestige3", "prestige4", "prestige5",
        };
    }

    public sealed class CaseDrop
    {
        public int Credits, Parts;
        public string Skin;
        /// <summary>The skin was already owned and was paid out in credits instead.</summary>
        public bool Duplicate;
        public int Rarity = -1;
    }

    /// <summary>Cases: what is in them and how likely each thing is. The odds are shown to the player as they are here.</summary>
    public static class CaseRules
    {
        public const int Tiers = 3;
        public static readonly int[] CreditPrice = { 600, 1800, 3000 };
        public static readonly int[] PartPrice = { 0, 0, 20 };
        /// <summary>Percent chances per tier: credits, parts, then a skin of each rarity.</summary>
        public static readonly int[][] Odds =
        {
            new[] { 55, 20, 18, 6, 1, 0 },
            new[] { 30, 20, 20, 20, 8, 2 },
            new[] { 20, 15, 0, 25, 28, 12 },
        };
        static readonly int[][] CreditRange = { new[] { 150, 400 }, new[] { 400, 900 }, new[] { 900, 1600 } };
        static readonly int[][] PartRange = { new[] { 2, 5 }, new[] { 4, 8 }, new[] { 8, 14 } };
        public static readonly int[] DuplicateCredits = { 200, 500, 1200, 3000 };

        public static bool Buy(SaveData save, int tier)
        {
            if (tier < 0 || tier >= Tiers || save.Credits < CreditPrice[tier] || save.Parts < PartPrice[tier]) return false;
            save.Credits -= CreditPrice[tier];
            save.Parts -= PartPrice[tier];
            save.Career.Cases[tier]++;
            return true;
        }

        /// <summary>Opens one case of a tier, if there is one. Null if there is none.</summary>
        public static CaseDrop Open(SaveData save, int tier, System.Random random)
        {
            if (tier < 0 || tier >= Tiers || save.Career.Cases[tier] <= 0) return null;
            save.Career.Cases[tier]--;
            var drop = new CaseDrop();
            int roll = random.Next(0, 100), slot = 0, sum = 0;
            for (; slot < Odds[tier].Length - 1; slot++)
            {
                sum += Odds[tier][slot];
                if (roll < sum) break;
            }
            if (slot == 0)
            {
                drop.Credits = random.Next(CreditRange[tier][0], CreditRange[tier][1] + 1) / 10 * 10;
            }
            else if (slot == 1)
            {
                drop.Parts = random.Next(PartRange[tier][0], PartRange[tier][1] + 1);
            }
            else
            {
                drop.Rarity = slot - 2;
                var pool = new List<SkinDefinition>();
                foreach (SkinDefinition skin in Skins.All)
                    if (skin.Rarity == drop.Rarity && !skin.Special) pool.Add(skin);
                SkinDefinition pick = pool[random.Next(0, pool.Count)];
                drop.Skin = pick.Id;
                if (save.Career.Skins.Contains(pick.Id))
                {
                    drop.Duplicate = true;
                    drop.Credits = DuplicateCredits[drop.Rarity];
                }
                else
                {
                    save.Career.Skins.Add(pick.Id);
                }
            }
            save.Credits += drop.Credits;
            save.Stats.CreditsEarned += drop.Credits;
            save.Parts += drop.Parts;
            return drop;
        }
    }

    /// <summary>The airfield: six things to build up, each of which changes how the fights go.</summary>
    public static class BaseRules
    {
        public const int Tracks = 6, MaxLevel = 5;
        public const int Hangar = 0, Workshop = 1, Tower = 2, Depot = 3, Defences = 4, Treasury = 5;
        public static readonly string[] Ids = { "hangar", "workshop", "tower", "depot", "defences", "treasury" };

        public static void Cost(int track, int level, out int credits, out int parts)
        {
            credits = 500 + 450 * level + 120 * track;
            parts = level >= 2 ? (level - 1) * 3 : 0;
        }

        public static bool Upgrade(SaveData save, int track) => Buy(save, save.Career.Base, track);

        internal static bool Buy(SaveData save, int[] levels, int track, bool armoury = false)
        {
            if (track < 0 || track >= levels.Length || levels[track] >= MaxLevel) return false;
            int credits, parts;
            if (armoury) ArmouryRules.Cost(track, levels[track], out credits, out parts);
            else Cost(track, levels[track], out credits, out parts);
            if (save.Credits < credits || save.Parts < parts) return false;
            save.Credits -= credits;
            save.Parts -= parts;
            levels[track]++;
            save.Stats.UpgradesBought++;
            return true;
        }

        /// <summary>Spare aircraft the hangar keeps ready: one at level 2, two at level 4.</summary>
        public static int Spares(SaveData save) => save.Career.Base[Hangar] / 2;
        /// <summary>Gun emplacements that stand with the outpost in survival.</summary>
        public static int Turrets(SaveData save) => Mathf.Min(4, save.Career.Base[Defences]);
    }

    /// <summary>Weapon development, shared by every aircraft.</summary>
    public static class ArmouryRules
    {
        public const int Tracks = 4;
        public const int Warheads = 0, Seekers = 1, Cannon = 2, Countermeasures = 3;
        public static readonly string[] Ids = { "warheads", "seekers", "cannon", "countermeasures" };

        public static void Cost(int track, int level, out int credits, out int parts)
        {
            credits = 600 + 500 * level;
            parts = level >= 1 ? level * 2 : 0;
        }

        public static bool Upgrade(SaveData save, int track) => BaseRules.Buy(save, save.Career.Armoury, track, true);

        /// <summary>The aircraft as the base and the armoury send it out.</summary>
        public static DroneSpec Outfit(DroneSpec spec, SaveData save)
        {
            DroneSpec fitted = spec.Clone();
            int[] arms = save.Career.Armoury, bases = save.Career.Base;
            fitted.Ordnance = 1f + 0.06f * arms[Warheads];
            fitted.LockRange *= 1f + 0.05f * arms[Seekers];
            fitted.Cannon *= 1f + 0.08f * arms[Cannon];
            fitted.CannonSeconds *= 1f + 0.10f * arms[Cannon];
            if (fitted.Ability != Ability.None) fitted.AbilityCharges += arms[Countermeasures] / 2;
            fitted.DamageScale *= 1f - 0.04f * bases[BaseRules.Workshop];
            fitted.SignalRange *= 1f + 0.06f * bases[BaseRules.Tower];
            if (fitted.OneWay <= 0)
            {
                if (fitted.Rockets > 0) fitted.Rockets += bases[BaseRules.Depot];
                if (fitted.Bombs > 0) fitted.Bombs += bases[BaseRules.Depot] / 2;
            }
            return fitted;
        }
    }

    public sealed class WarNews
    {
        public int Income;
        public List<string> Lost = new List<string>();
    }

    /// <summary>
    /// The war over the map. Every region has twelve sectors, one around each inland point. A
    /// sector is taken by winning an operation there; held sectors pay each day, and each day the
    /// enemy may take one back.
    /// </summary>
    public static class War
    {
        public const int SectorsPerRegion = 12, IncomePerSector = 25, MaxDaysPaid = 3;

        static string Key(string region, int sector) => region + ":" + sector;

        public static bool Held(SaveData save, string region, int sector) => save.Career.Sectors.Contains(Key(region, sector));

        public static int HeldIn(SaveData save, string region)
        {
            int count = 0;
            for (int i = 0; i < SectorsPerRegion; i++)
                if (Held(save, region, i)) count++;
            return count;
        }

        /// <summary>Returns true if the sector was not held before.</summary>
        public static bool Capture(SaveData save, string region, int sector)
        {
            if (sector < 0 || sector >= SectorsPerRegion || Held(save, region, sector)) return false;
            save.Career.Sectors.Add(Key(region, sector));
            return true;
        }

        /// <summary>
        /// Brings the war up to today: pays for the days that have passed and lets the enemy
        /// counter-attack once a day in each region. Null if nothing happened.
        /// </summary>
        public static WarNews Refresh(SaveData save, string today)
        {
            CareerData career = save.Career;
            if (career.WarDate == today) return null;
            int days = 1;
            if (DateTime.TryParse(career.WarDate, out DateTime last) && DateTime.TryParse(today, out DateTime now))
                days = Mathf.Clamp((int)(now - last).TotalDays, 1, MaxDaysPaid);
            bool first = string.IsNullOrEmpty(career.WarDate);
            career.WarDate = today;
            if (first || career.Sectors.Count == 0) return null;

            var news = new WarNews();
            news.Income = Mathf.RoundToInt(career.Sectors.Count * IncomePerSector * days * (1f + 0.2f * career.Base[BaseRules.Treasury]));
            save.Credits += news.Income;
            save.Stats.CreditsEarned += news.Income;

            // One counter-attack per region per day; the base's defences beat off some of them.
            int holdPercent = 40 + 12 * career.Base[BaseRules.Defences];
            for (int day = 0; day < days; day++)
            {
                foreach (RegionDefinition region in RegionCatalog.All)
                {
                    var held = new List<int>();
                    for (int i = 0; i < SectorsPerRegion; i++)
                        if (Held(save, region.Id, i)) held.Add(i);
                    if (held.Count == 0) continue;
                    int hash = 17;
                    foreach (char c in today + region.Id + day) hash = unchecked(hash * 31 + c);
                    hash &= 0x7fffffff;
                    if (hash % 100 < holdPercent) continue;
                    int lost = held[(hash / 100) % held.Count];
                    career.Sectors.Remove(Key(region.Id, lost));
                    news.Lost.Add(Key(region.Id, lost));
                }
            }
            return news;
        }
    }
}

namespace Skyforge.Core
{
    /// <summary>The story is told in transmissions between operations; each waits for this many campaign missions.</summary>
    public static class Story
    {
        public static readonly int[] Beats = { 0, 1, 3, 6, 10, 15, 24, 36, 50, 70, 90 };
    }
}
