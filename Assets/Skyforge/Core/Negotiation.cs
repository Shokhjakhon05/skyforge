using System.Collections.Generic;
using UnityEngine;

namespace Skyforge.Core
{
    /// <summary>One thing the player can say in a negotiation, and what it changes in the mission that follows.</summary>
    public sealed class NegotiationChoice
    {
        public string TextKey;
        /// <summary>What the other side answers; shown before the mission starts.</summary>
        public string ReplyKey;
        public int Cost;
        /// <summary>Radars switched off in the mission, counted from the first.</summary>
        public int RadarsRemoved;
        public float RewardScale = 1f;
        /// <summary>Weather to fly in instead of the mission's own, or null.</summary>
        public string Weather;
    }

    /// <summary>
    /// A conversation before the last mission of a region's major operation. The player picks one
    /// line; the choice is remembered and shapes that mission every time it is flown.
    /// </summary>
    public sealed class Negotiation
    {
        public string MissionId;
        public string SpeakerKey;
        public string TextKey;
        public List<NegotiationChoice> Choices = new List<NegotiationChoice>();
    }

    public static class Negotiations
    {
        static readonly List<Negotiation> List = new List<Negotiation>
        {
            Make("broken_arrow", "arvelia"),
            Make("kh_broken_arrow", "kharad"),
            Make("nh_broken_arrow", "nordheim"),
            Make("ts_broken_arrow", "tessara"),
            Make("vk_broken_arrow", "vulkar"),
            Make("rd_broken_arrow", "redrock"),
        };

        /// <summary>
        /// Every negotiation offers the same three kinds of deal: pay for help, take the job as it
        /// is, or raise the stakes for a bigger reward. The words differ per region.
        /// </summary>
        static Negotiation Make(string missionId, string region)
        {
            string key = "talk." + region;
            var negotiation = new Negotiation { MissionId = missionId, SpeakerKey = key + ".speaker", TextKey = key + ".text" };
            negotiation.Choices.Add(new NegotiationChoice
            {
                TextKey = key + ".pay", ReplyKey = key + ".pay.reply", Cost = 300, RadarsRemoved = 1,
            });
            negotiation.Choices.Add(new NegotiationChoice { TextKey = key + ".plain", ReplyKey = key + ".plain.reply" });
            negotiation.Choices.Add(new NegotiationChoice
            {
                TextKey = key + ".risk", ReplyKey = key + ".risk.reply", RewardScale = 1.5f, Weather = "storm",
            });
            return negotiation;
        }

        public static IReadOnlyList<Negotiation> All => List;

        public static Negotiation For(string missionId)
        {
            foreach (Negotiation negotiation in List)
                if (negotiation.MissionId == missionId) return negotiation;
            return null;
        }

        /// <summary>The choice the player made for a mission, or -1 if the talk has not happened.</summary>
        public static int Chosen(SaveData save, string missionId)
        {
            string prefix = missionId + "=";
            foreach (string entry in save.Choices)
                if (entry.StartsWith(prefix) && int.TryParse(entry.Substring(prefix.Length), out int choice)) return choice;
            return -1;
        }

        public static bool CanAfford(SaveData save, NegotiationChoice choice) => save.Credits >= choice.Cost;

        /// <summary>Records the choice and pays its price. Returns false if it was already made or cannot be paid.</summary>
        public static bool Choose(SaveData save, Negotiation negotiation, int index)
        {
            if (index < 0 || index >= negotiation.Choices.Count) return false;
            if (Chosen(save, negotiation.MissionId) >= 0) return false;
            NegotiationChoice choice = negotiation.Choices[index];
            if (!CanAfford(save, choice)) return false;
            save.Credits -= choice.Cost;
            save.Choices.Add(negotiation.MissionId + "=" + index);
            return true;
        }

        /// <summary>The mission as the player's choice left it. The original definition is not touched.</summary>
        public static MissionDefinition Apply(MissionDefinition mission, SaveData save)
        {
            Negotiation negotiation = For(mission.Id);
            int index = negotiation == null ? -1 : Chosen(save, mission.Id);
            if (index < 0 || index >= negotiation.Choices.Count) return mission;

            NegotiationChoice choice = negotiation.Choices[index];
            MissionDefinition changed = mission.Copy();
            for (int i = 0; i < choice.RadarsRemoved && changed.Zones.Count > 0; i++) changed.Zones.RemoveAt(0);
            changed.BaseReward = Mathf.RoundToInt(mission.BaseReward * choice.RewardScale);
            changed.StarReward = Mathf.RoundToInt(mission.StarReward * choice.RewardScale);
            if (!string.IsNullOrEmpty(choice.Weather)) changed.WeatherId = choice.Weather;
            return changed;
        }
    }
}
