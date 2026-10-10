using System.Collections.Generic;
using UnityEngine;

namespace Skyforge.Core
{
    /// <summary>The kinds of operation the generator can put together.</summary>
    public enum OperationKind
    {
        /// <summary>An enemy position with its guards.</summary>
        Strike,
        /// <summary>A column on the move that must not get away.</summary>
        Convoy,
        /// <summary>Raiders driving at an outpost of ours.</summary>
        Defend,
        /// <summary>Enemy drones and fighters come up to meet the player.</summary>
        AirPatrol,
        /// <summary>An armoured train: engine, wagons and gun cars.</summary>
        Train,
        /// <summary>A missile launcher driving to its firing point: stop it before it gets there.</summary>
        Launcher,
        /// <summary>Drop the bridge before the column crosses it.</summary>
        Bridge,
        /// <summary>A downed pilot: reach him before the search party does and bring him out.</summary>
        Rescue,
        /// <summary>Armour hunted at night through the thermal sight.</summary>
        NightHunt,
        /// <summary>An oil platform out at sea with its guard boats.</summary>
        Platform,
        /// <summary>A command ship that must lose its turrets before the hull gives.</summary>
        Flagship,
        /// <summary>Our tanks advance on an enemy line; keep them alive.</summary>
        Offensive,
    }

    /// <summary>
    /// Builds operations out of the same parts the campaign's files are made of: anchors of the
    /// island, target kinds, objectives. A seed and a level always give the same operation, so a
    /// contract can be offered, declined and offered again unchanged.
    /// </summary>
    public static class Operations
    {
        public const int SurvivalWaves = 20;
        /// <summary>Levels above this add nothing new; the numbers stop growing.</summary>
        public const int MaxLevel = 40;

        static readonly string[] Soft = { "truck", "truck_cargo", "truck_army", "truck_fuel" };
        static readonly string[] Scouts = { "jeep", "humvee", "apc" };
        static readonly string[] Armour = { "tank_light", "tank", "tank_heavy" };
        static readonly string[] Guns = { "squad", "gatling", "aagun", "bunker" };
        static readonly string[] Weathers = { "clear", "overcast", "sunset", "clear", "rain", "dust", "fog", "storm" };

        public static int KindCount => System.Enum.GetValues(typeof(OperationKind)).Length;

        /// <summary>How much tougher the enemy's hulls and guns are at a level.</summary>
        public static float HealthScale(int level) => Mathf.Min(2.2f, 1f + 0.035f * Mathf.Max(0, level - 1));
        public static float GunScale(int level) => Mathf.Min(1.9f, 1f + 0.028f * Mathf.Max(0, level - 1));
        public static int Reward(int level) => 140 + 55 * Mathf.Clamp(level, 1, MaxLevel);

        sealed class Draft
        {
            public readonly List<TargetData> Targets = new List<TargetData>();
            public readonly List<ObjectiveData> Objectives = new List<ObjectiveData>();
            public readonly List<PropData> Props = new List<PropData>();
            public System.Random Random;
            public int Level;
            public string Weather;
            public float TimeGoal = 240f;
            public string Template = "assault";
            public string ObjectiveText = "objective.op.strike";

            public int Roll(int count) => Random.Next(0, Mathf.Max(1, count));
            public float Spread(float reach) => ((float)Random.NextDouble() * 2f - 1f) * reach;
            public string Pick(string[] list, int strongest) => list[Mathf.Clamp(Roll(Mathf.Min(list.Length, strongest + 1)), 0, list.Length - 1)];

            public TargetData Add(string id, string kind, string anchor, float dx = 0f, float dz = 0f)
            {
                var target = new TargetData { id = id, kind = kind, at = Place(anchor, dx, dz) };
                Targets.Add(target);
                return target;
            }

            public TargetData Mover(string id, string kind, PlaceData[] path, float speed, float delay, int start = 1)
            {
                var target = new TargetData { id = id, kind = kind, path = path, speed = speed, delay = delay, startObjective = start };
                Targets.Add(target);
                return target;
            }

            public TargetData Flyer(string id, string kind, string anchor, float dx, float dz, float delay, int start = 1)
            {
                var target = new TargetData
                {
                    id = id, kind = kind, at = Place(anchor, dx, dz), altitude = 40f, delay = delay, startObjective = start,
                };
                Targets.Add(target);
                return target;
            }
        }

        static PlaceData Place(string anchor, float dx = 0f, float dz = 0f, float dy = 0f) =>
            new PlaceData { anchor = anchor, dx = dx, dy = dy, dz = dz };

        static ObjectiveData Destroy(IEnumerable<TargetData> targets, string text)
        {
            var ids = new List<string>();
            foreach (TargetData target in targets) ids.Add(target.id);
            return new ObjectiveData { type = "destroy", targets = ids.ToArray(), text = text };
        }

        static ObjectiveData TakeOff => new ObjectiveData { type = "takeoff" };
        static ObjectiveData Home => new ObjectiveData { type = "land", at = Place("pad"), radius = 8f };

        /// <summary>Three inland points in a row, the road a column drives.</summary>
        static PlaceData[] Road(int from, bool reverse = false)
        {
            int a = ((from % 12) + 12) % 12, b = (a + 1) % 12, c = (a + 2) % 12;
            return reverse ? new[] { Place("poi" + c), Place("poi" + b), Place("poi" + a) } : new[] { Place("poi" + a), Place("poi" + b), Place("poi" + c) };
        }

        // ---------------------------------------------------------------- contracts

        /// <summary>
        /// A contract: one operation of a kind picked by the seed (or forced), as hard as the level,
        /// on this island. <paramref name="sector"/> pins it to one inland point, for the war over the map.
        /// </summary>
        public static MissionDefinition Contract(Island island, string region, int seed, int level, OperationKind? forced = null,
            int sector = -1, string weather = null)
        {
            level = Mathf.Clamp(level, 1, MaxLevel);
            for (int attempt = 0; attempt < 10; attempt++)
            {
                var random = new System.Random(seed * 7919 + attempt * 104729 + 17);
                // A pinned sector is always fought on the ground, at that point.
                OperationKind kind = forced ?? (sector >= 0
                    ? new[] { OperationKind.Strike, OperationKind.NightHunt, OperationKind.Offensive, OperationKind.Strike }[random.Next(0, 4)]
                    : (OperationKind)random.Next(0, KindCount));
                // The first levels keep to the plain kinds: the others need a player who has learnt to fight.
                if (forced == null && sector < 0 && level < 3 && kind > OperationKind.Defend) kind = (OperationKind)random.Next(0, 3);
                var draft = new Draft { Random = random, Level = level, Weather = weather ?? Weathers[random.Next(0, Weathers.Length)] };
                int place = sector >= 0 ? sector : random.Next(0, 12);
                Fill(draft, kind, place);

                var data = new MissionData
                {
                    id = "op:" + region + ":" + kind + ":" + seed + ":" + level + (sector >= 0 ? ":s" + sector : ""),
                    region = region, order = 1, template = draft.Template, weather = draft.Weather,
                    timeGoal = Mathf.Round(draft.TimeGoal * (1f + 0.012f * level) / 10f) * 10f,
                    baseReward = Reward(level), starReward = Mathf.RoundToInt(Reward(level) * 0.3f / 10f) * 10,
                    targets = draft.Targets.ToArray(), objectives = draft.Objectives.ToArray(), props = draft.Props.ToArray(),
                };
                var errors = new List<string>();
                MissionDefinition mission = MissionBuilder.Build(data, island, errors);
                if (mission == null || !WithinReach(mission)) continue;
                mission.Mode = sector >= 0 ? "sector" : "contract";
                mission.Reactive = true;
                mission.HealthScale = HealthScale(level);
                mission.GunScale = GunScale(level);
                mission.Title = Codename(seed + attempt * 31);
                mission.BriefKey = "op." + kind.ToString().ToLowerInvariant() + ".brief";
                mission.Template = draft.Template;
                mission.Level = level;
                mission.Kind = kind;
                return mission;
            }
            return null;
        }

        /// <summary>
        /// Whether the starter drone's radio reaches everything the operation is about. A contract
        /// that cannot be flown with the aircraft everyone has is not offered.
        /// </summary>
        public static bool WithinReach(MissionDefinition mission)
        {
            float reach = new DroneSpec().SignalRange * 0.82f;
            foreach (TargetDefinition target in mission.Targets)
            {
                if (target.Parent >= 0) continue;
                // A target is fought from a rocket's range, not from on top of it.
                if (Flat(target.Position, mission.BasePosition) - StandOff > reach) return false;
                foreach (Vector3 point in target.Path)
                    if (Flat(point, mission.BasePosition) - StandOff > reach) return false;
            }
            foreach (ObjectiveDefinition objective in mission.Objectives)
            {
                bool placed = objective.Type == ObjectiveType.Land || objective.Type == ObjectiveType.Scan || objective.Type == ObjectiveType.Reach;
                if (placed && Flat(objective.Position, mission.BasePosition) > reach) return false;
            }
            return true;
        }

        const float StandOff = 100f;

        static float Flat(Vector3 a, Vector3 b) => new Vector2(a.x - b.x, a.z - b.z).magnitude;

        /// <summary>Two words that make an operation's name: "Iron Tempest".</summary>
        public static string Codename(int seed)
        {
            int a = Mathf.Abs(seed * 31 + 7) % 12, b = Mathf.Abs(seed * 17 + 3) % 12;
            return Loc.T("codename.a." + a) + " " + Loc.T("codename.b." + b);
        }

        static void Guards(Draft draft, string anchor, int count)
        {
            for (int i = 0; i < count; i++)
            {
                float angle = i * 2.1f + 0.6f;
                string kind = draft.Pick(Guns, 1 + draft.Level / 5);
                draft.Add("guard" + i, kind, anchor, Mathf.Cos(angle) * (20f + i * 3f), Mathf.Sin(angle) * (20f + i * 3f));
            }
        }

        /// <summary>Enemy aircraft that come up once the fighting has started.</summary>
        static List<TargetData> Hunters(Draft draft, string anchor, int count, int start = 1, float firstDelay = 12f)
        {
            var list = new List<TargetData>();
            for (int i = 0; i < count; i++)
            {
                string kind = draft.Level >= 12 && i % 3 == 2 ? "fighter" : "hunter";
                list.Add(draft.Flyer("air" + start + "_" + i, kind, anchor, 60f + i * 25f, 70f - i * 30f, firstDelay + i * 9f, start));
            }
            return list;
        }

        static void Fill(Draft d, OperationKind kind, int place)
        {
            int level = d.Level;
            string poi = "poi" + place;
            var marks = new List<TargetData>();
            d.Objectives.Add(TakeOff);
            switch (kind)
            {
                case OperationKind.Strike:
                {
                    marks.Add(d.Add("mark0", level >= 6 ? "depot" : "truck_fuel", poi, 0f, -10f));
                    marks.Add(d.Add("mark1", d.Pick(Soft, 3), poi, -11f, 4f));
                    marks.Add(d.Add("mark2", level >= 10 ? "hq" : "tent", poi, 14f, 8f));
                    if (level >= 4) marks.Add(d.Add("mark3", d.Pick(Armour, level / 8), poi, -4f, 22f));
                    Guards(d, poi, Mathf.Clamp(1 + level / 4, 1, 5));
                    if (level >= 8) Hunters(d, poi, Mathf.Min(3, level / 8));
                    d.ObjectiveText = "objective.op.strike";
                    d.TimeGoal = 200f;
                    break;
                }
                case OperationKind.Convoy:
                {
                    PlaceData[] road = Road(place);
                    int count = Mathf.Clamp(3 + level / 4, 3, 7);
                    for (int i = 0; i < count; i++)
                    {
                        string k = i == 0 ? d.Pick(Scouts, level / 5) : i == count - 1 && level >= 5 ? d.Pick(Armour, level / 9) : d.Pick(Soft, 3);
                        marks.Add(d.Mover("col" + i, k, road, 4.6f, i * 5f));
                    }
                    if (level >= 9) Hunters(d, poi, 1 + level / 14);
                    d.Template = "convoy";
                    d.ObjectiveText = "objective.destroy_convoy";
                    d.TimeGoal = 230f;
                    break;
                }
                case OperationKind.Defend:
                {
                    TargetData post = d.Add("outpost", "outpost", "pad", 62f, 34f);
                    post.friendly = true;
                    var road = new[] { Place("coast6"), Place("poi10"), Place("pad", 100f, 70f) };
                    int count = Mathf.Clamp(3 + level / 3, 3, 8);
                    for (int i = 0; i < count; i++)
                    {
                        string k = i % 3 == 2 ? d.Pick(Armour, level / 8) : d.Pick(Scouts, 1 + level / 6);
                        TargetData raider = d.Mover("raider" + i, k, road, 6.5f, i * 9f);
                        raider.attacks = "outpost";
                        marks.Add(raider);
                    }
                    d.Template = "defend";
                    d.ObjectiveText = "objective.defend";
                    d.TimeGoal = 240f;
                    break;
                }
                case OperationKind.AirPatrol:
                {
                    marks.AddRange(Hunters(d, poi, Mathf.Clamp(2 + level / 4, 2, 7), 1, 4f));
                    marks.Add(d.Add("radar", "radar", poi));
                    d.Template = "sead";
                    d.ObjectiveText = "objective.op.air";
                    d.TimeGoal = 210f;
                    break;
                }
                case OperationKind.Train:
                {
                    PlaceData[] line = Road(place);
                    float pace = 2.6f + Mathf.Min(2.2f, level * 0.12f);
                    marks.Add(d.Mover("engine", "train_engine", line, pace, 0f));
                    int cars = Mathf.Clamp(2 + level / 5, 2, 6);
                    for (int i = 0; i < cars; i++)
                        {
                        // A short train has one gun car, at the tail; longer ones alternate.
                        bool gun = level < 5 ? i == cars - 1 : i % 2 == 0;
                        marks.Add(d.Mover("car" + i, gun ? "train_gun" : "train_car", line, pace, (i + 1) * 16f / pace));
                    }
                    d.Template = "convoy";
                    d.ObjectiveText = "objective.op.train";
                    d.TimeGoal = 260f;
                    break;
                }
                case OperationKind.Launcher:
                {
                    PlaceData[] road = Road(place, true);
                    marks.Add(d.Mover("launcher", "launcher", road, 3.6f, 0f));
                    d.Mover("escort0", d.Pick(Scouts, 2), road, 3.6f, 4f);
                    if (level >= 5) d.Mover("escort1", "apc", road, 3.6f, 8f);
                    if (level >= 9) d.Mover("escort2", d.Pick(Armour, level / 10), road, 3.6f, 12f);
                    if (level >= 7) Hunters(d, poi, 1 + level / 12);
                    d.Template = "eliminate";
                    d.ObjectiveText = "objective.op.launcher";
                    d.TimeGoal = 200f;
                    break;
                }
                case OperationKind.Bridge:
                {
                    // The bridge stands at the middle point of the column's road: once it is down the column has nowhere to go.
                    PlaceData[] road = Road(place);
                    marks.Add(d.Add("bridge", "bridge", road[1].anchor));
                    Guards(d, road[1].anchor, Mathf.Clamp(1 + level / 6, 1, 4));
                    d.Template = "demolition";
                    d.ObjectiveText = "objective.op.bridge";
                    d.TimeGoal = 220f;
                    break;
                }
                case OperationKind.Rescue:
                {
                    TargetData pilot = d.Add("pilot", "pilot", poi);
                    pilot.friendly = true;
                    PlaceData[] road = Road(place + 2, true);
                    int count = Mathf.Clamp(2 + level / 5, 2, 6);
                    for (int i = 0; i < count; i++)
                    {
                        TargetData hunter = d.Mover("search" + i, d.Pick(Scouts, 1 + level / 8), road, 5.5f, 6f + i * 8f);
                        hunter.attacks = "pilot";
                        marks.Add(hunter);
                    }
                    d.Objectives.Add(Destroy(marks, "objective.op.rescue"));
                    // Then down beside him, and home with him aboard.
                    d.Objectives.Add(new ObjectiveData { type = "land", at = Place(poi, 9f, 0f), radius = 14f, text = "objective.op.pickup" });
                    d.Objectives.Add(TakeOff);
                    d.Objectives.Add(Home);
                    d.Template = "defend";
                    d.TimeGoal = 280f;
                    return;
                }
                case OperationKind.NightHunt:
                {
                    int count = Mathf.Clamp(2 + level / 4, 2, 6);
                    for (int i = 0; i < count; i++)
                        marks.Add(d.Add("tank" + i, d.Pick(Armour, level / 7), poi, d.Spread(26f), d.Spread(26f)));
                    Guards(d, poi, Mathf.Clamp(level / 5, 0, 3));
                    d.Weather = "night";
                    d.Template = "hunt";
                    d.ObjectiveText = "objective.op.nighthunt";
                    d.TimeGoal = 230f;
                    break;
                }
                case OperationKind.Platform:
                {
                    string sea = "sea" + place % 4;
                    marks.Add(d.Add("platform", "platform", sea));
                    int boats = Mathf.Clamp(1 + level / 5, 1, 4);
                    for (int i = 0; i < boats; i++) d.Add("boat" + i, "boat", sea, Mathf.Cos(i * 1.9f) * 38f, Mathf.Sin(i * 1.9f) * 38f);
                    if (level >= 6)
                    {
                        TargetData gun = new TargetData { id = "rig_gun", kind = "ship_turret", parent = "platform", oy = 15f };
                        d.Targets.Add(gun);
                    }
                    d.Template = "naval";
                    d.ObjectiveText = "objective.op.platform";
                    d.TimeGoal = 220f;
                    break;
                }
                case OperationKind.Flagship:
                {
                    string sea = "sea" + place % 4;
                    marks.Add(d.Add("flagship", "warship", sea));
                    int turrets = Mathf.Clamp(2 + level / 6, 2, 4);
                    for (int i = 0; i < turrets; i++)
                    {
                        var turret = new TargetData { id = "turret" + i, kind = "ship_turret", parent = "flagship", oy = 7f, oz = (i - (turrets - 1) * 0.5f) * 12f };
                        d.Targets.Add(turret);
                        marks.Add(turret);
                    }
                    if (level >= 5) d.Add("escort", "boat", sea, 40f, 20f);
                    d.Template = "naval";
                    d.ObjectiveText = "objective.op.flagship";
                    d.TimeGoal = 250f;
                    break;
                }
                case OperationKind.Offensive:
                {
                    // Our armour drives at their line; everything on that line shoots at the leading tank.
                    PlaceData[] road = Road(place + 10);
                    TargetData lead = d.Mover("ally0", "ally_tank", road, 2.6f, 0f);
                    lead.friendly = true;
                    TargetData second = d.Mover("ally1", "ally_tank", road, 2.6f, 6f);
                    second.friendly = true;
                    TargetData infantry = d.Mover("ally2", "ally_squad", road, 2.6f, 11f);
                    infantry.friendly = true;
                    string line = road[2].anchor;
                    int count = Mathf.Clamp(3 + level / 4, 3, 7);
                    for (int i = 0; i < count; i++)
                    {
                        string k = i % 2 == 0 ? d.Pick(Armour, level / 9) : d.Pick(Guns, 1 + level / 6);
                        TargetData foe = d.Add("line" + i, k, line, Mathf.Cos(i * 1.3f) * 24f, Mathf.Sin(i * 1.3f) * 24f);
                        foe.attacks = i % 2 == 0 ? "ally0" : "ally1";
                        marks.Add(foe);
                    }
                    d.Template = "hunt";
                    d.ObjectiveText = "objective.op.offensive";
                    d.TimeGoal = 250f;
                    break;
                }
            }
            d.Objectives.Add(Destroy(marks, d.ObjectiveText));
            d.Objectives.Add(Home);
        }

        // ---------------------------------------------------------------- the other modes

        /// <summary>
        /// The campaign mission as a veteran flies it: tougher hulls, harder guns, an enemy that
        /// reacts, a tighter clock and better pay. It keeps its own stars under its own id.
        /// </summary>
        public static MissionDefinition Veteran(MissionDefinition mission)
        {
            MissionDefinition veteran = mission.Copy();
            veteran.Id = VeteranId(mission.Id);
            veteran.Mode = "veteran";
            veteran.Reactive = true;
            veteran.HealthScale = 1.5f;
            veteran.GunScale = 1.4f;
            veteran.TimeGoalSeconds = Mathf.Round(mission.TimeGoalSeconds * 0.95f / 10f) * 10f;
            veteran.BaseReward = Mathf.RoundToInt(mission.BaseReward * 1.6f / 10f) * 10;
            veteran.StarReward = Mathf.RoundToInt(mission.StarReward * 1.6f / 10f) * 10;
            veteran.Title = mission.TitleText;
            veteran.Brief = mission.BriefText;
            return veteran;
        }

        public static string VeteranId(string missionId) => "vet:" + missionId;

        /// <summary>
        /// Survival: wave after wave drives at the outpost by the pad. The waves are the
        /// objectives, so the number of the objective reached is the score.
        /// </summary>
        /// <param name="turrets">Friendly gun emplacements around the outpost, from the base's defences.</param>
        public static MissionDefinition Survival(Island island, string region, int turrets)
        {
            var d = new Draft { Random = new System.Random(4201), Level = 1, Weather = "sunset" };
            TargetData post = d.Add("outpost", "outpost", "pad", 62f, 34f);
            post.friendly = true;
            for (int i = 0; i < Mathf.Clamp(turrets, 0, 4); i++)
            {
                TargetData gun = d.Add("turret" + i, "turret", "pad", 62f + Mathf.Cos(i * 1.6f) * 22f, 34f + Mathf.Sin(i * 1.6f) * 22f);
                gun.friendly = true;
            }
            var road = new[] { Place("coast6"), Place("poi10"), Place("pad", 100f, 70f) };
            var other = new[] { Place("coast5"), Place("poi9"), Place("pad", 100f, 70f) };
            d.Objectives.Add(TakeOff);
            for (int wave = 1; wave <= SurvivalWaves; wave++)
            {
                d.Level = wave;
                var marks = new List<TargetData>();
                int count = Mathf.Clamp(2 + wave / 3, 2, 6);
                for (int i = 0; i < count; i++)
                {
                    string kind = i % 3 == 2 && wave >= 3 ? d.Pick(Armour, wave / 6) : d.Pick(Scouts, wave / 3);
                    TargetData raider = d.Mover("w" + wave + "_" + i, kind, (wave + i) % 2 == 0 ? road : other, 7f, i * 6f, wave);
                    raider.attacks = "outpost";
                    marks.Add(raider);
                }
                // Every third wave comes with aircraft.
                if (wave % 3 == 0) marks.AddRange(Hunters(d, "pad", Mathf.Clamp(wave / 4, 1, 3), wave, 6f));
                d.Objectives.Add(Destroy(marks, "objective.op.wave"));
            }
            var data = new MissionData
            {
                id = "survival:" + region, region = region, order = 1, template = "defend", weather = d.Weather,
                timeGoal = 3600f, baseReward = 100, starReward = 0,
                targets = d.Targets.ToArray(), objectives = d.Objectives.ToArray(),
            };
            MissionDefinition mission = MissionBuilder.Build(data, island, new List<string>());
            if (mission == null) return null;
            mission.Mode = "survival";
            mission.Reactive = true;
            mission.Title = Loc.T("op.survival.title");
            mission.BriefKey = "op.survival.brief";
            return mission;
        }

        /// <summary>The wave a survival run has reached, from 1, or 0 before take-off.</summary>
        public static int Wave(MissionRun run) => Mathf.Clamp(run.ObjectiveIndex, 0, SurvivalWaves);

        /// <summary>
        /// A raid: fly in, hold over each supply drop long enough to mark it for pick-up, and get
        /// out. The longer it lasts the more aircraft come looking.
        /// </summary>
        public static MissionDefinition Raid(Island island, string region, int seed, int level)
        {
            level = Mathf.Clamp(level, 1, MaxLevel);
            var d = new Draft { Random = new System.Random(seed * 131 + 5), Level = level, Weather = "overcast" };
            int crates = Mathf.Clamp(3 + level / 6, 3, 6);
            int first = d.Roll(12);
            d.Objectives.Add(TakeOff);
            for (int i = 0; i < crates; i++)
            {
                string poi = "poi" + (first + i * 2) % 12;
                d.Objectives.Add(new ObjectiveData { type = "scan", at = Place(poi, 0f, 0f, 16f), radius = 13f, height = 14f, hold = 3f, text = "objective.op.crate" });
                d.Add("watch" + i, d.Pick(Guns, 1 + level / 6), poi, 22f, -14f);
                // Each drop marked wakes more of the hunt.
                Hunters(d, poi, 1 + Mathf.Min(2, level / 10), i + 2, 4f);
            }
            d.Objectives.Add(Home);
            var data = new MissionData
            {
                id = "raid:" + region + ":" + seed + ":" + level, region = region, order = 1, template = "stealth", weather = d.Weather,
                timeGoal = 150f + crates * 70f, baseReward = Reward(level) + crates * 60, starReward = Mathf.RoundToInt(Reward(level) * 0.3f / 10f) * 10,
                targets = d.Targets.ToArray(), objectives = d.Objectives.ToArray(),
            };
            MissionDefinition mission = MissionBuilder.Build(data, island, new List<string>());
            // A drop out of radio reach: try the neighbouring arrangement instead.
            if (mission != null && !WithinReach(mission) && seed < 100000) return Raid(island, region, seed + 100003, level);
            if (mission == null) return null;
            mission.Mode = "raid";
            mission.Reactive = true;
            mission.HealthScale = HealthScale(level);
            mission.GunScale = GunScale(level);
            mission.Title = Loc.T("op.raid.title") + " «" + Codename(seed) + "»";
            mission.BriefKey = "op.raid.brief";
            mission.Level = level;
            return mission;
        }

        /// <summary>The firing range: one of everything to shoot at, racks that never empty, nothing that shoots back.</summary>
        public static MissionDefinition Range(Island island, string region)
        {
            var d = new Draft { Random = new System.Random(1), Level = 1, Weather = "clear" };
            var marks = new List<TargetData>();
            string[] line = { "dummy", "truck", "tank_light", "tank", "tank_heavy", "bunker", "depot", "dummy" };
            for (int i = 0; i < line.Length; i++)
            {
                TargetData target = d.Add("range" + i, line[i], "poi0", (i % 4 - 1.5f) * 22f, (i / 4) * 30f - 10f);
                if (line[i] == "bunker") target.kind = "hq";
                marks.Add(target);
            }
            marks.Add(d.Mover("runner", "truck_army", Road(0), 6f, 0f));
            marks[marks.Count - 1].loop = true;
            d.Objectives.Add(TakeOff);
            d.Objectives.Add(Destroy(marks, "objective.op.range"));
            d.Objectives.Add(Home);
            var data = new MissionData
            {
                id = "range:" + region, region = region, order = 1, template = "assault", weather = "clear",
                timeGoal = 3600f, baseReward = 10, starReward = 0,
                targets = d.Targets.ToArray(), objectives = d.Objectives.ToArray(),
            };
            MissionDefinition mission = MissionBuilder.Build(data, island, new List<string>());
            if (mission == null) return null;
            mission.Mode = "range";
            mission.FreeAmmo = true;
            mission.GunScale = 0f;
            mission.Title = Loc.T("op.range.title");
            mission.BriefKey = "op.range.brief";
            return mission;
        }
    }
}
