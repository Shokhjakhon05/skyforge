using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace Sniper
{
    [Serializable]
    public sealed class RifleRecord
    {
        public string Id;
        public int Scope, Barrel, Stock, Magazine, Bolt;
    }

    [Serializable]
    public sealed class MissionRecord
    {
        public string Id;
        public int Stars;
        public float Best;
    }

    /// <summary>Everything the player has earned, kept in one file on the phone.</summary>
    [Serializable]
    public sealed class SniperSave
    {
        public int Cash;
        public string Rifle = "m24";
        public string Language = "";
        public bool Muted;
        public int Kills, Headshots;
        /// <summary>The last day the game was opened, the days in a row so far, and the day the daily contract was last paid.</summary>
        public string DayStamp = "", DailyDone = "";
        public int Streak;
        /// <summary>The career in numbers.</summary>
        public int Shots, Blasts, Hunts, HuntLevel = 1, Finished;
        public float LongestShot;
        public List<string> Feats = new List<string>();
        /// <summary>"Pro": no names over heads and no range in the glass, for half as much pay again.</summary>
        public bool Pro;
        public float Sensitivity = 1f;
        public bool Gyro;
        public List<RifleRecord> Rifles = new List<RifleRecord>();
        public List<MissionRecord> Missions = new List<MissionRecord>();

        public RifleRecord Owned(string id)
        {
            foreach (RifleRecord record in Rifles)
                if (record.Id == id) return record;
            return null;
        }

        public MissionRecord Record(string id)
        {
            foreach (MissionRecord record in Missions)
                if (record.Id == id) return record;
            return null;
        }

        public int Completed
        {
            get
            {
                int done = 0;
                foreach (MissionRecord record in Missions)
                    if (record.Stars > 0) done++;
                return done;
            }
        }

        public int Stars
        {
            get
            {
                int stars = 0;
                foreach (MissionRecord record in Missions) stars += record.Stars;
                return stars;
            }
        }

        /// <summary>The rifle in hand with everything fitted to it.</summary>
        public RifleDef Fitted()
        {
            RifleRecord r = Owned(Rifle) ?? Owned("m24");
            RifleDef b = Armory.Get(r != null ? r.Id : "m24");
            return r == null ? b : Armory.Fitted(b, r.Scope, r.Barrel, r.Stock, r.Magazine, r.Bolt);
        }

        public static string FilePath => Path.Combine(Application.persistentDataPath, "sniper.json");

        public static SniperSave Load()
        {
            SniperSave save = null;
            try
            {
                if (File.Exists(FilePath)) save = JsonUtility.FromJson<SniperSave>(File.ReadAllText(FilePath));
            }
            catch (Exception e)
            {
                Debug.LogWarning("[Sniper] Save unreadable: " + e.Message);
            }
            if (save == null) save = new SniperSave();
            if (save.Rifles == null) save.Rifles = new List<RifleRecord>();
            if (save.Missions == null) save.Missions = new List<MissionRecord>();
            if (save.Feats == null) save.Feats = new List<string>();
            if (save.Sensitivity < 0.3f || save.Sensitivity > 3f) save.Sensitivity = 1f;
            if (save.HuntLevel < 1) save.HuntLevel = 1;
            if (save.Owned("m24") == null) save.Rifles.Add(new RifleRecord { Id = "m24" });
            if (save.Owned(save.Rifle) == null) save.Rifle = "m24";
            return save;
        }

        public void Store()
        {
            try
            {
                string temp = FilePath + ".tmp";
                File.WriteAllText(temp, JsonUtility.ToJson(this));
                if (File.Exists(FilePath)) File.Delete(FilePath);
                File.Move(temp, FilePath);
            }
            catch (Exception e)
            {
                Debug.LogError("[Sniper] Save failed: " + e.Message);
            }
        }
    }
}
