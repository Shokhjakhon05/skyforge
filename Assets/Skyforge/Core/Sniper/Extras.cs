using System.Collections.Generic;
using UnityEngine;

namespace Sniper
{
    /// <summary>The contracts that never run out: a place already known, people where its own contracts put them, more of them each time.</summary>
    public static class Hunt
    {
        public const string Id = "hunt";

        public static MissionDef Make(string placeId, int level, int seed)
        {
            var random = new System.Random(seed * 7919 + level * 131 + 17);
            var spots = new List<Vector3>();
            var bases = new List<MissionDef>();
            foreach (MissionDef mission in Campaign.Missions)
            {
                if (mission.LocationId != placeId) continue;
                bases.Add(mission);
                foreach (ActorDef actor in mission.Actors)
                {
                    if (actor.Id != null && actor.Id.StartsWith("crowd")) continue;
                    foreach (Vector3 point in actor.Path)
                    {
                        bool crowded = false;
                        foreach (Vector3 other in spots)
                            if ((other - point).sqrMagnitude < 16f) crowded = true;
                        if (!crowded) spots.Add(point);
                    }
                }
            }
            if (bases.Count == 0 || spots.Count < 3) return null;
            MissionDef basis = bases[random.Next(bases.Count)];
            int targets = Mathf.Min(spots.Count - 1, Mathf.Clamp(3 + level / 2, 3, 9));
            var hunt = new MissionDef
            {
                Id = Id, LocationId = placeId, TitleKey = "sn.hunt.title", BriefKey = "sn.hunt.brief", Order = 0,
                Perch = basis.Perch, LookAt = basis.LookAt, Rounds = targets * 2 + 2, Reward = 600 + level * 220,
                Par = 18f + targets * 7f, Second = level % 2 == 0 ? "headshots" : "clean",
                Wind = new Vector3(((float)random.NextDouble() * 2f - 1f) * Mathf.Min(6f, 1f + level * 0.45f), 0f, 0f),
            };
            // Shuffled, so each hunt stands its people somewhere else.
            for (int i = spots.Count - 1; i > 0; i--)
            {
                int j = random.Next(i + 1);
                Vector3 swap = spots[i];
                spots[i] = spots[j];
                spots[j] = swap;
            }
            string[] looks = { "swat", "punk", "hoodie", "swat" };
            for (int i = 0; i < targets; i++)
            {
                Vector3 from = spots[i], to = from;
                foreach (Vector3 other in spots)
                {
                    float gap = (other - from).magnitude;
                    if (gap > 5f && gap < 22f && Mathf.Abs(other.y - from.y) < 0.05f) to = other;
                }
                string look = looks[random.Next(looks.Length)];
                var man = new ActorDef
                {
                    Id = "h" + i, Model = look, Role = Role.Target, NameKey = look == "swat" ? "name.sentry" : "name.thug",
                    Path = to == from ? new[] { from } : new[] { from, to }, Wait = 3f + (float)random.NextDouble() * 4f,
                    Idle = look == "swat" ? "Idle_Gun" : "Idle", Bounty = 200 + level * 25, YawDeg = (float)random.NextDouble() * 360f,
                    Health = level >= 8 && i == 0 ? 220f : 100f,
                };
                hunt.Actors.Add(man);
            }
            int guards = Mathf.Clamp(level / 3, 0, 3);
            for (int i = 0; i < guards && targets + i < spots.Count; i++)
                hunt.Actors.Add(new ActorDef { Id = "hg" + i, Model = "swat", Role = Role.Guard, NameKey = "name.guard", Path = new[] { spots[targets + i] }, Idle = "Idle_Gun", YawDeg = 180f });
            int n = 0;
            foreach (ActorDef walker in Campaign.Location(placeId).Crowd)
                hunt.Actors.Add(new ActorDef { Id = "crowd" + n++, Model = walker.Model, Role = Role.Civilian, NameKey = walker.NameKey, Path = walker.Path, Wait = walker.Wait });
            return hunt;
        }
    }

    public sealed class Feat
    {
        public string Id, Kind;
        public int Goal, Cash;
    }

    /// <summary>What a career is counted in, and what each count pays the first time it is reached.</summary>
    public static class Feats
    {
        public static readonly List<Feat> All = new List<Feat>
        {
            new Feat { Id = "kills1", Kind = "kills", Goal = 10, Cash = 500 },
            new Feat { Id = "kills2", Kind = "kills", Goal = 60, Cash = 2500 },
            new Feat { Id = "kills3", Kind = "kills", Goal = 250, Cash = 10000 },
            new Feat { Id = "heads1", Kind = "heads", Goal = 25, Cash = 1500 },
            new Feat { Id = "heads2", Kind = "heads", Goal = 120, Cash = 8000 },
            new Feat { Id = "far1", Kind = "far", Goal = 180, Cash = 1500 },
            new Feat { Id = "far2", Kind = "far", Goal = 240, Cash = 5000 },
            new Feat { Id = "stars1", Kind = "stars", Goal = 18, Cash = 2000 },
            new Feat { Id = "stars2", Kind = "stars", Goal = 54, Cash = 8000 },
            new Feat { Id = "stars3", Kind = "stars", Goal = 108, Cash = 30000 },
            new Feat { Id = "blast1", Kind = "blasts", Goal = 5, Cash = 2000 },
            new Feat { Id = "hunt1", Kind = "hunts", Goal = 10, Cash = 6000 },
        };

        /// <summary>The clothes a model wears, as a line of a briefing: the key of the description.</summary>
        public static string LookKey(string model)
        {
            switch (model)
            {
                case "suit": return "sn.look.suit";
                case "punk": return "sn.look.punk";
                case "hoodie": return "sn.look.hoodie";
                case "swat": return "sn.look.swat";
                case "worker": return "sn.look.worker";
                case "farmer": return "sn.look.farmer";
                case "adventurer": return "sn.look.adventurer";
                default: return "sn.look.casual";
            }
        }
    }
}
