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

    /// <summary>The range: boards at measured distances, more of them and further each time, then moving, then against the clock.</summary>
    public static class Range
    {
        public const string Id = "range", Place = "range";
        public static readonly Vector3 Line = new Vector3(0f, 3.2f, -120f);

        public static MissionDef Make(int level, int seed)
        {
            var random = new System.Random(seed * 104729 + level * 31 + 5);
            int boards = Mathf.Clamp(4 + level / 2, 4, 10);
            float near = Mathf.Min(60f + level * 6f, 140f), far = Mathf.Min(110f + level * 14f, 300f);
            var range = new MissionDef
            {
                Id = Id, LocationId = Place, TitleKey = "sn.range", BriefKey = level >= 5 ? "sn.range.brief.friends" : level >= 3 ? "sn.range.brief.moving" : "sn.range.brief", Order = 0,
                Perch = Line, LookAt = new Vector3(0f, 1.2f, 0f), Rounds = boards + Mathf.Max(2, 5 - level / 3), Reward = 120 + level * 40,
                Seconds = level < 2 ? 0f : Mathf.Max(25f, 30f + boards * 7f - level * 1.5f), Par = 12f + boards * 4f, Second = "clean",
                Wind = level < 4 ? Vector3.zero : new Vector3(((float)random.NextDouble() * 2f - 1f) * Mathf.Min(7f, level * 0.5f), 0f, 0f),
            };
            int friends = level >= 5 ? Mathf.Min(3, (level - 3) / 2) : 0;
            for (int i = 0; i < boards; i++)
            {
                float x = -20f + (i * 3 % 5) * 10f + ((float)random.NextDouble() * 4f - 2f);
                float distance = boards > 1 ? Mathf.Lerp(near, far, i / (float)(boards - 1)) : near;
                float z = Line.z + distance;
                bool moves = level >= 3 && i % 3 == 2;
                range.Actors.Add(new ActorDef
                {
                    Id = "b" + i, Model = "board", Kind = Kind.Board, Role = Role.Target, NameKey = "name.board", Health = 1f, YawDeg = 180f,
                    Bounty = 10 + Mathf.RoundToInt(distance / 10f) * 5, Pace = Mathf.Min(3.5f, 1.2f + level * 0.12f),
                    Path = moves ? new[] { new Vector3(x - 4f, 0f, z), new Vector3(x + 4f, 0f, z) } : new[] { new Vector3(x, 0f, z) },
                });
                // A board that must not be hit stands half across the one that must.
                if (i < friends)
                    range.Actors.Add(new ActorDef
                    {
                        Id = "f" + i, Model = "board_friend", Kind = Kind.Board, Role = Role.Civilian, NameKey = "name.board.friend", Health = 1f, YawDeg = 180f,
                        Path = new[] { new Vector3(x + (moves ? 5.2f : 0.62f), 0f, z - 0.6f) },
                    });
            }
            return range;
        }
    }

    /// <summary>The reserve: a stag to be taken out of a herd without touching the hinds, or wolves to be stopped before they reach it.</summary>
    public static class Safari
    {
        public const string Id = "safari", Place = "wild";

        static ActorDef Stag(string id, Vector3 at) => new ActorDef
        {
            Id = id, Model = "stag", Kind = Kind.Beast, NameKey = "name.stag", Idle = "Eating", Path = new[] { at },
            Tall = 2.05f, Shoulder = 1.3f, Length = 1.7f, Girth = 0.3f, HeadAt = 1.75f, Pace = 0.8f, Flight = 9f,
        };

        static ActorDef Hind(string id, Vector3 at) => new ActorDef
        {
            Id = id, Model = "deer", Kind = Kind.Beast, NameKey = "name.hind", Idle = "Eating", Path = new[] { at },
            Tall = 1.5f, Shoulder = 1f, Length = 1.35f, Girth = 0.25f, HeadAt = 1.35f, Pace = 0.8f, Flight = 9f,
        };

        static ActorDef Wolf(string id, Vector3 at) => new ActorDef
        {
            Id = id, Model = "wolf", Kind = Kind.Beast, NameKey = "name.wolf", Idle = "Idle", Path = new[] { at },
            Tall = 0.95f, Shoulder = 0.74f, Length = 1.2f, Girth = 0.2f, HeadAt = 0.8f, Pace = 1.5f, Flight = 6.5f,
        };

        public static bool Wolves(int level) => level % 2 == 0;

        public static MissionDef Make(int level, int seed)
        {
            var random = new System.Random(seed * 15485863 + level * 53 + 3);
            float Next() => (float)random.NextDouble();
            bool wolves = Wolves(level);
            var centre = new Vector3(Next() * 50f - 25f, 0f, 10f + Next() * 40f);
            var safari = new MissionDef
            {
                Id = Id, LocationId = Place, TitleKey = "sn.safari", BriefKey = wolves ? "sn.safari.brief.wolves" : "sn.safari.brief.stag", Order = 0,
                Perch = new Vector3(0f, 9.5f, -130f), LookAt = centre + Vector3.up, Reward = 300 + level * 90, Second = "clean",
                Wind = new Vector3((Next() * 2f - 1f) * Mathf.Min(6f, 1f + level * 0.4f), 0f, 0f),
            };
            if (!wolves)
            {
                int stags = Mathf.Clamp(1 + level / 3, 1, 4), hinds = 4 + level % 3;
                for (int i = 0; i < stags + hinds; i++)
                {
                    float angle = (i * 137.5f + Next() * 40f) * Mathf.Deg2Rad, away = 5f + i * 2.6f + Next() * 3f;
                    Vector3 at = centre + new Vector3(Mathf.Sin(angle), 0f, Mathf.Cos(angle)) * away;
                    ActorDef beast = i < stags ? Stag("s" + i, at) : Hind("d" + i, at);
                    beast.Role = i < stags ? Role.Target : Role.Civilian;
                    beast.YawDeg = Next() * 360f;
                    beast.Bounty = i < stags ? 300 + level * 30 : 0;
                    beast.Wait = 5f + Next() * 6f;
                    // Every other one wanders a few steps as it feeds.
                    if (i % 2 == 0) beast.Path = new[] { at, at + new Vector3(Next() * 12f - 6f, 0f, Next() * 12f - 6f) };
                    safari.Actors.Add(beast);
                }
                safari.Rounds = stags * 2 + 1;
                safari.Par = 14f + stags * 8f;
                return safari;
            }
            int pack = Mathf.Clamp(2 + level / 3, 2, 6);
            for (int i = 0; i < 3; i++)
            {
                ActorDef hind = Hind("d" + i, centre + new Vector3(i * 3.2f - 3.2f, 0f, (i % 2) * 2.4f));
                hind.Role = Role.Ally;
                hind.YawDeg = 40f + i * 110f;
                safari.Actors.Add(hind);
            }
            for (int i = 0; i < pack; i++)
            {
                float angle = (i * (360f / pack) + Next() * 30f - 60f) * Mathf.Deg2Rad, away = 70f + Next() * 25f;
                Vector3 from = centre + new Vector3(Mathf.Sin(angle) * away, 0f, Mathf.Abs(Mathf.Cos(angle)) * away * 0.9f + 10f);
                from.x = Mathf.Clamp(from.x, -115f, 115f);
                ActorDef wolf = Wolf("w" + i, from);
                wolf.Role = Role.Target;
                wolf.Assassin = true;
                wolf.Bounty = 200 + level * 20;
                wolf.Wait = 1f + i * 2.5f;
                wolf.Path = new[] { from, centre + new Vector3(Next() * 4f - 2f, 0f, Next() * 4f - 2f) };
                Vector3 line = wolf.Path[1] - from;
                wolf.YawDeg = Mathf.Atan2(line.x, line.z) * Mathf.Rad2Deg;
                safari.Actors.Add(wolf);
            }
            safari.Rounds = pack * 2 + 2;
            safari.Par = 20f + pack * 7f;
            return safari;
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
            new Feat { Id = "range1", Kind = "ranges", Goal = 10, Cash = 3000 },
            new Feat { Id = "safari1", Kind = "safaris", Goal = 10, Cash = 5000 },
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
                case "board": return "sn.look.board";
                case "stag": return "sn.look.stag";
                case "wolf": return "sn.look.wolf";
                default: return "sn.look.casual";
            }
        }
    }
}
