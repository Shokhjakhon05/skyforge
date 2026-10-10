using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace Skyforge.Core
{
    [Serializable]
    public sealed class MissionRecord
    {
        public string Id;
        public int Stars;
        public float BestTime;
        public int Completions;
    }

    /// <summary>A drone the player owns, with what has been done to it.</summary>
    [Serializable]
    public sealed class DroneRecord
    {
        public string Id;
        /// <summary>Level 0..5 per <see cref="UpgradeTrack"/>.</summary>
        public int[] Upgrades = new int[Loadout.Tracks];
        /// <summary>Module id per slot; an empty string is an empty slot.</summary>
        public string[] Modules = new string[Loadout.ModuleSlots];

        public static DroneRecord New(string id)
        {
            var record = new DroneRecord { Id = id };
            for (int i = 0; i < record.Modules.Length; i++) record.Modules[i] = "";
            return record;
        }
    }

    /// <summary>Everything about the player that survives closing the game.</summary>
    [Serializable]
    public sealed class SaveData
    {
        public const int CurrentVersion = 7;

        public int Version = CurrentVersion;
        public int Credits;
        public List<MissionRecord> Missions = new List<MissionRecord>();
        public bool SticksSwapped;
        public int FlightModeIndex;
        public int CameraViewIndex;
        public float RingBest;

        // Added in version 2.
        /// <summary>0 = pick automatically, 1..4 = Low..Ultra.</summary>
        public int Quality;
        public bool ShowFps;
        public bool Muted;
        /// <summary>Weather chosen for free flight.</summary>
        public string FreeFlightWeather = WeatherCatalog.Clear;

        // Added in version 3.
        /// <summary>Second currency: earned with stars, spent on the top upgrade levels.</summary>
        public int Parts;
        public List<DroneRecord> Drones = new List<DroneRecord> { DroneRecord.New(DroneCatalog.Starter) };
        public string SelectedDrone = DroneCatalog.Starter;
        public List<string> OwnedModules = new List<string>();

        // Added in version 4.
        /// <summary>The region the player is deployed to.</summary>
        public string Region = "arvelia";
        /// <summary>Negotiation outcomes, as "missionId=choiceIndex".</summary>
        public List<string> Choices = new List<string>();

        // Added in version 5.
        /// <summary>The latest date the game has seen, "yyyy-MM-dd"; see <see cref="Daily.Today"/>.</summary>
        public string LastDate = "";
        /// <summary>The day the entries in <see cref="DailyDone"/> belong to.</summary>
        public string DailyDate = "";
        public List<int> DailyDone = new List<int>();
        public string StreakDate = "";
        public int StreakDay;
        public PlayerStats Stats = new PlayerStats();
        public List<string> Achievements = new List<string>();
        public List<string> Caches = new List<string>();
        /// <summary>Language code chosen in settings; empty means follow the device.</summary>
        public string Language = "";

        // Added in version 6.
        /// <summary>The player bought "no ads".</summary>
        public bool NoAds;
        /// <summary>One-time products already delivered.</summary>
        public List<string> Purchases = new List<string>();
        /// <summary>The day <see cref="AdCratesToday"/> counts for.</summary>
        public string AdDate = "";
        public int AdCratesToday;
        /// <summary>How many times the game has been started.</summary>
        public int Sessions;
        /// <summary>The date of the first launch, "yyyy-MM-dd".</summary>
        public string FirstDate = "";

        // Added in version 7.
        /// <summary>Rank, season, tasks, collection, base and the war over the map.</summary>
        public CareerData Career = new CareerData();

        public DroneRecord FindDrone(string id)
        {
            foreach (DroneRecord drone in Drones)
                if (drone.Id == id) return drone;
            return null;
        }

        public MissionRecord Find(string id)
        {
            foreach (MissionRecord record in Missions)
                if (record.Id == id) return record;
            return null;
        }

        public MissionRecord GetOrAdd(string id)
        {
            MissionRecord record = Find(id);
            if (record != null) return record;
            record = new MissionRecord { Id = id };
            Missions.Add(record);
            return record;
        }

        public bool IsValid()
        {
            if (Version < 1 || Version > CurrentVersion) return false;
            if (Credits < 0 || Missions == null) return false;
            if (Quality < 0 || Quality > 4) return false;
            if (Parts < 0 || Drones == null || OwnedModules == null) return false;
            foreach (DroneRecord drone in Drones)
            {
                if (drone == null || string.IsNullOrEmpty(drone.Id)) return false;
                if (drone.Upgrades == null || drone.Upgrades.Length != Loadout.Tracks) return false;
                if (drone.Modules == null || drone.Modules.Length != Loadout.ModuleSlots) return false;
                foreach (int level in drone.Upgrades)
                    if (level < 0 || level > Loadout.MaxLevel) return false;
            }
            if (FindDrone(SelectedDrone) == null) return false;
            foreach (MissionRecord record in Missions)
            {
                if (record == null || string.IsNullOrEmpty(record.Id)) return false;
                if (record.Stars < 0 || record.Stars > 3 || record.Completions < 0) return false;
            }
            return true;
        }
    }

    public enum LoadResult
    {
        /// <summary>No save existed.</summary>
        Fresh,
        Loaded,
        /// <summary>The main file was unreadable; the previous good copy was used.</summary>
        RecoveredFromBackup,
        /// <summary>Nothing readable was left; the broken file was kept aside and a new save started.</summary>
        ResetAfterCorruption,
    }

    /// <summary>
    /// Reads and writes the save file. A new save is written next to the old one and read back
    /// before it replaces it, and the previous good file is kept as a backup.
    /// </summary>
    public sealed class SaveStore
    {
        readonly string _path;

        public SaveStore(string path)
        {
            _path = path;
        }

        string BackupPath => _path + ".bak";
        string TempPath => _path + ".tmp";

        public SaveData Load(out LoadResult result)
        {
            bool mainExists = File.Exists(_path);
            SaveData data = TryRead(_path);
            if (data != null)
            {
                result = LoadResult.Loaded;
                return data;
            }

            data = TryRead(BackupPath);
            if (data != null)
            {
                result = LoadResult.RecoveredFromBackup;
                return data;
            }

            if (mainExists)
            {
                // Keep the unreadable file: it is the only evidence of what went wrong.
                TryCopy(_path, _path + ".corrupt");
                result = LoadResult.ResetAfterCorruption;
            }
            else
            {
                result = LoadResult.Fresh;
            }
            return new SaveData();
        }

        /// <summary>Returns false, leaving the existing save untouched, if the data could not be stored.</summary>
        public bool Save(SaveData data)
        {
            if (data == null || !data.IsValid()) return false;
            try
            {
                string directory = Path.GetDirectoryName(_path);
                if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);

                File.WriteAllText(TempPath, JsonUtility.ToJson(data));
                if (TryRead(TempPath) == null) return false;

                // Only a readable main file is worth keeping as the backup.
                if (TryRead(_path) != null) File.Copy(_path, BackupPath, true);
                File.Copy(TempPath, _path, true);
                File.Delete(TempPath);
                return true;
            }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException)
            {
                Debug.LogError("[Skyforge] Save failed: " + e.Message);
                return false;
            }
        }

        static SaveData TryRead(string path)
        {
            try
            {
                if (!File.Exists(path)) return null;
                string text = File.ReadAllText(path);
                if (string.IsNullOrWhiteSpace(text)) return null;
                var data = JsonUtility.FromJson<SaveData>(text);
                if (data == null) return null;
                Migrate(data);
                return data.IsValid() ? data : null;
            }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException || e is ArgumentException)
            {
                return null;
            }
        }

        /// <summary>Upgrades a save from an older schema, one version at a time.</summary>
        static void Migrate(SaveData data)
        {
            if (data.Missions == null) data.Missions = new List<MissionRecord>();
            if (data.Version == 1)
            {
                // Version 2 added the settings below; a version 1 file has none of them.
                data.Quality = 0;
                data.ShowFps = false;
                data.Muted = false;
                data.FreeFlightWeather = WeatherCatalog.Clear;
                data.Version = 2;
            }
            if (string.IsNullOrEmpty(data.FreeFlightWeather)) data.FreeFlightWeather = WeatherCatalog.Clear;
            if (data.Version == 2)
            {
                // Version 3 added the hangar. The player keeps the starter drone and receives the
                // parts their stars would have earned.
                data.Drones = new List<DroneRecord> { DroneRecord.New(DroneCatalog.Starter) };
                data.SelectedDrone = DroneCatalog.Starter;
                data.OwnedModules = new List<string>();
                data.Parts = 0;
                foreach (MissionRecord record in data.Missions) data.Parts += Mathf.Max(0, record.Stars) * Progress.PartsPerStar;
                data.Version = 3;
            }
            if (data.Version == 3)
            {
                // Version 4 added regions; everything played so far happened in the first one.
                data.Region = RegionCatalog.All[0].Id;
                data.Choices = new List<string>();
                data.Version = 4;
            }
            if (data.Version == 4)
            {
                // Version 5 added daily operations, the streak, achievements and caches: all start empty.
                data.LastDate = "";
                data.DailyDate = "";
                data.DailyDone = new List<int>();
                data.StreakDate = "";
                data.StreakDay = 0;
                data.Stats = new PlayerStats();
                data.Achievements = new List<string>();
                data.Caches = new List<string>();
                data.Version = 5;
            }
            if (data.Version == 5)
            {
                // Version 6 added advertising and shop state. A player who already has progress is
                // not in their first session, so the "no interstitials at first" rule is behind them.
                data.NoAds = false;
                data.Purchases = new List<string>();
                data.AdDate = "";
                data.AdCratesToday = 0;
                data.Sessions = data.Missions.Count > 0 ? 2 : 0;
                data.FirstDate = "";
                data.Version = 6;
            }
            if (data.Version == 6)
            {
                // Version 7 added the career. Stars already earned count towards the rank.
                data.Career = new CareerData();
                int stars = 0;
                foreach (MissionRecord record in data.Missions) stars += Mathf.Max(0, record.Stars);
                data.Career.Xp = stars * 60;
                data.Career.RankPaid = Skyforge.Core.Career.Rank(data.Career.Xp);
                data.Version = 7;
            }
            Repair(data);
        }

        /// <summary>Fills in anything a hand-edited or partly written file left out, without touching progress.</summary>
        static void Repair(SaveData data)
        {
            if (data.Career == null) data.Career = new CareerData();
            data.Career.Repair();
            if (data.OwnedModules == null) data.OwnedModules = new List<string>();
            if (data.Choices == null) data.Choices = new List<string>();
            if (data.DailyDone == null) data.DailyDone = new List<int>();
            if (data.Achievements == null) data.Achievements = new List<string>();
            if (data.Caches == null) data.Caches = new List<string>();
            if (data.Stats == null) data.Stats = new PlayerStats();
            if (data.LastDate == null) data.LastDate = "";
            if (data.DailyDate == null) data.DailyDate = "";
            if (data.StreakDate == null) data.StreakDate = "";
            if (data.Language == null) data.Language = "";
            if (data.Purchases == null) data.Purchases = new List<string>();
            if (data.AdDate == null) data.AdDate = "";
            if (data.FirstDate == null) data.FirstDate = "";
            data.AdCratesToday = Mathf.Clamp(data.AdCratesToday, 0, AdRules.FreeCratesPerDay);
            data.Sessions = Mathf.Max(0, data.Sessions);
            data.StreakDay = Mathf.Clamp(data.StreakDay, 0, Daily.StreakCredits.Length);
            if (RegionCatalog.Get(data.Region).Id != data.Region) data.Region = RegionCatalog.All[0].Id;
            if (data.Drones == null) data.Drones = new List<DroneRecord>();
            data.Drones.RemoveAll(drone => drone == null || string.IsNullOrEmpty(drone.Id));
            foreach (DroneRecord drone in data.Drones)
            {
                if (drone.Upgrades == null || drone.Upgrades.Length != Loadout.Tracks)
                {
                    var levels = new int[Loadout.Tracks];
                    if (drone.Upgrades != null) Array.Copy(drone.Upgrades, levels, Math.Min(levels.Length, drone.Upgrades.Length));
                    drone.Upgrades = levels;
                }
                if (drone.Modules == null || drone.Modules.Length != Loadout.ModuleSlots)
                    drone.Modules = new string[Loadout.ModuleSlots];
                for (int i = 0; i < drone.Modules.Length; i++)
                    if (drone.Modules[i] == null) drone.Modules[i] = "";
            }
            if (data.FindDrone(DroneCatalog.Starter) == null) data.Drones.Insert(0, DroneRecord.New(DroneCatalog.Starter));
            if (data.FindDrone(data.SelectedDrone) == null) data.SelectedDrone = DroneCatalog.Starter;
        }

        static void TryCopy(string from, string to)
        {
            try
            {
                File.Copy(from, to, true);
            }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException)
            {
            }
        }
    }

    /// <summary>Turns mission outcomes into lasting progress.</summary>
    public static class Progress
    {
        /// <summary>Share of the base reward paid for replaying a mission without doing better.</summary>
        public const float ReplayShare = 0.1f;
        /// <summary>Parts paid for each star the first time it is earned.</summary>
        public const int PartsPerStar = 1;

        /// <summary>
        /// Records an outcome and pays for it, once. The base reward is paid on the first
        /// completion and each star is paid the first time it is earned; a replay that earns
        /// nothing new pays a small flat amount.
        /// </summary>
        public static int Grant(SaveData save, MissionDefinition mission, MissionOutcome outcome)
        {
            if (outcome.Granted || !outcome.Succeeded) return 0;
            outcome.Granted = true;

            MissionRecord record = save.GetOrAdd(mission.Id);
            int credits = 0;
            if (record.Completions == 0) credits += mission.BaseReward;
            int newStars = Mathf.Max(0, outcome.Stars - record.Stars);
            credits += newStars * mission.StarReward;
            save.Parts += newStars * PartsPerStar;
            outcome.PartsGranted = newStars * PartsPerStar;
            if (credits == 0) credits = Mathf.RoundToInt(mission.BaseReward * ReplayShare);

            record.Completions++;
            record.Stars = Mathf.Max(record.Stars, outcome.Stars);
            if (record.BestTime <= 0f || outcome.Time < record.BestTime) record.BestTime = outcome.Time;

            save.Credits += credits;
            save.Stats.CreditsEarned += credits;
            outcome.CreditsGranted = credits;
            return credits;
        }
    }
}
