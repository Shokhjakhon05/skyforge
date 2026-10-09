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

    /// <summary>Everything about the player that survives closing the game.</summary>
    [Serializable]
    public sealed class SaveData
    {
        public const int CurrentVersion = 2;

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
            if (outcome.Stars > record.Stars) credits += (outcome.Stars - record.Stars) * mission.StarReward;
            if (credits == 0) credits = Mathf.RoundToInt(mission.BaseReward * ReplayShare);

            record.Completions++;
            record.Stars = Mathf.Max(record.Stars, outcome.Stars);
            if (record.BestTime <= 0f || outcome.Time < record.BestTime) record.BestTime = outcome.Time;

            save.Credits += credits;
            outcome.CreditsGranted = credits;
            return credits;
        }
    }
}
