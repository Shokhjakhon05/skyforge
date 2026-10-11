using System.Collections.Generic;
using UnityEngine;

namespace Skyforge.Core
{
    public struct TreeSpec
    {
        public Vector3 Base;
        public float Height;
        public float Radius;
        /// <summary>Caught in the fighting: a bare black trunk.</summary>
        public bool Burnt;
    }

    /// <summary>What is left of a building: jagged walls around a rubble floor.</summary>
    public struct RuinSpec
    {
        public Vector3 Base;
        public float YawDeg;
        public float Width, Depth, Height;
        public int Seed;
    }

    /// <summary>A burnt-out vehicle left where it was hit.</summary>
    public struct WreckSpec
    {
        public Vector3 Base;
        public float YawDeg;
        public int Kind;
        public bool Burning;
    }

    /// <summary>A ready-made model standing as scenery: a house, a palm, a rock, a container.</summary>
    public struct PropSpec
    {
        public Vector3 Base;
        public float YawDeg, Scale;
        /// <summary>Index into <see cref="Island.PropModels"/>.</summary>
        public int Model;
    }

    public struct CraterSpec
    {
        public float X, Z, Radius;
    }

    public struct RoadSpec
    {
        public Vector3 From, To;
    }

    /// <summary>
    /// One generated location: terrain heights, where things stand, where the drone starts and
    /// the ring course. Pure data, built the same way for a given seed.
    /// </summary>
    public sealed class Island
    {
        public const float WaterLevel = 0f;
        public const float RingRadius = 7f;
        public const float BeaconHeight = 12f;

        public HeightMap Map { get; private set; }
        public ObstacleField Obstacles { get; private set; }
        public List<TreeSpec> Trees { get; private set; }
        public List<Vector3> Rings { get; private set; }
        /// <summary>Ground positions of the weather beacons, in the order a mission visits them.</summary>
        public List<Vector3> Beacons { get; private set; }
        /// <summary>
        /// Named places that mission files refer to: pad, beacon0..2, ring0..9, poi0..11 (level
        /// spots inland), coast0..7 (on the shore), sea0..3 (open water) and summit.
        /// </summary>
        public Dictionary<string, Vector3> Anchors { get; private set; }

        // The war: what the fighting has left on the island. Ruins are obstacles; the rest is scenery.
        public List<RuinSpec> Ruins { get; private set; }
        public List<WreckSpec> Wrecks { get; private set; }
        /// <summary>Towns, camps and rocks built from models. The tall ones are obstacles too.</summary>
        public List<PropSpec> Props { get; private set; }

        public static readonly string[] PropModels =
        {
            "b_small1", "b_small2", "b_small3", "b_small4", "b_large3", "b_large5", "warehouse", "fortress",
            "b_large1", "b_large2", "b_large4", "apartment",
            "palm1", "palm2", "palm3", "rock1", "rock2", "rock3", "rock4",
            "container1", "container2", "barrier1", "barrier2", "wall", "trash", "crate", "tent",
            "guardtower", "radiotower", "watertower", "sandbags", "barrel",
        };
        const int PropLowCount = 8, PropTall = 8, PropTallCount = 4, PropPalm = 12, PropRock = 15,
            PropContainer = 19, PropBarrier = 21, PropWall = 23, PropTrash = 24, PropCrate = 25, PropTent = 26,
            PropGuard = 27, PropRadio = 28, PropWater = 29, PropSandbags = 30, PropBarrel = 31;
        /// <summary>Height in metres of each model at scale 1, as tools/gen_models.py bakes them.</summary>
        static readonly float[] PropHeights =
        {
            7f, 6.5f, 8.5f, 8f, 9.4f, 9.4f, 7f, 8f, 13.5f, 12.5f, 13f, 14f,
            7.5f, 8.5f, 9f, 5f, 4f, 7f, 3.5f, 2.6f, 2.6f, 1.1f, 1.2f, 2.4f, 2.2f, 1.3f, 3.2f, 8.5f, 9.4f, 9f, 1.2f, 1.1f,
        };
        public List<CraterSpec> Craters { get; private set; }
        public List<RoadSpec> Roads { get; private set; }
        /// <summary>Centres of the shelled villages.</summary>
        public List<Vector3> Villages { get; private set; }
        /// <summary>Where smoke still rises: burning wrecks and smouldering ruins.</summary>
        public List<Vector3> Fires { get; private set; }

        /// <summary>No ruin is taller than this, so a drone that clears the trees clears the villages too.</summary>
        public const float RuinMaxHeight = 9.5f;
        /// <summary>Ruins keep this far from the middle of a village, where missions put their targets.</summary>
        public const float VillageSquare = 30f;
        public Vector3 Spawn { get; private set; }
        public float SpawnYawDeg { get; private set; }

        public Biome Biome { get; private set; }

        public static Island Generate(int seed, int resolution = 257, float size = 1536f) =>
            Generate(seed, Biome.Temperate, resolution, size);

        public static Island Generate(int seed, Biome biome, int resolution = 257, float size = 1536f)
        {
            var island = new Island
            {
                Biome = biome,
                Map = new HeightMap(resolution, size),
                Obstacles = new ObstacleField(),
                Trees = new List<TreeSpec>(),
                Rings = new List<Vector3>(),
                Beacons = new List<Vector3>(),
                Anchors = new Dictionary<string, Vector3>(),
                Ruins = new List<RuinSpec>(),
                Wrecks = new List<WreckSpec>(),
                Props = new List<PropSpec>(),
                Craters = new List<CraterSpec>(),
                Roads = new List<RoadSpec>(),
                Villages = new List<Vector3>(),
                Fires = new List<Vector3>(),
            };
            var random = new System.Random(seed);
            float offsetX = (float)random.NextDouble() * 1000f;
            float offsetZ = (float)random.NextDouble() * 1000f;

            island.BuildHeights(offsetX, offsetZ);
            island.PlaceSpawn();
            island.PlaceBeacons();
            island.PlaceAnchors();
            island.PlaceWar(new System.Random(seed * 31 + 7));
            island.PlaceTrees(random, offsetX, offsetZ);
            island.PlaceRings();
            // Last and from its own dice, so that everything placed before it stays where it was.
            island.PlaceTowns(new System.Random(seed * 53 + 11));
            island.Anchors["pad"] = island.Spawn;
            for (int i = 0; i < island.Beacons.Count; i++) island.Anchors["beacon" + i] = island.Beacons[i];
            for (int i = 0; i < island.Rings.Count; i++) island.Anchors["ring" + i] = island.Rings[i];
            return island;
        }

        void BuildHeights(float offsetX, float offsetZ)
        {
            HeightMap map = Map;
            for (int iz = 0; iz < map.Resolution; iz++)
            for (int ix = 0; ix < map.Resolution; ix++)
            {
                float x = map.NodeCoordinate(ix), z = map.NodeCoordinate(iz);
                float distance = Mathf.Sqrt(x * x + z * z) / map.HalfSize;
                float land = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.95f, 0.30f, distance));

                float hills = Fbm(x * 0.0022f + offsetX, z * 0.0022f + offsetZ, 5);
                float ridge = 1f - Mathf.Abs(2f * Fbm(x * 0.0035f + offsetX + 50f, z * 0.0035f + offsetZ + 50f, 3) - 1f);
                ridge *= ridge;

                float rise = 8f + hills * Biome.Hills + ridge * hills * Biome.Ridges;
                if (Biome.Cone > 0f)
                {
                    // A volcano in the middle, with the top of the cone knocked in.
                    float flank = Mathf.Clamp01(1f - distance / 0.19f);
                    rise += Biome.Cone * flank * flank * (3f - 2f * flank);
                    float crater = Mathf.Clamp01(1f - distance / 0.045f);
                    rise -= Biome.Cone * 0.28f * crater * crater;
                }
                if (Biome.Terrace > 0f)
                {
                    // Tablelands: flat steps with a short steep riser between them.
                    float level = rise / Biome.Terrace;
                    float step = Mathf.Floor(level), part = level - step;
                    float stepped = (step + Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.72f, 1f, part))) * Biome.Terrace;
                    rise = Mathf.Lerp(rise, stepped, 0.85f);
                }
                if (Biome.Channels > 0f)
                {
                    // The sea reaches in wherever a slow noise runs low, leaving islands between the channels.
                    float mask = Fbm(x * 0.0017f + offsetX + 300f, z * 0.0017f + offsetZ + 300f, 3);
                    land *= Mathf.Lerp(1f, Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.38f, 0.52f, mask)), Biome.Channels);
                }

                map[ix, iz] = land * rise - (1f - land) * 14f;
            }
        }

        /// <summary>Levels a pad for the start, on the southern side of the island.</summary>
        void PlaceSpawn()
        {
            HeightMap map = Map;
            float x = 0f, z = -0.5f * map.HalfSize;
            // The pad sits at the height of the land around it. Capping it lower put it at the
            // bottom of a pit in mountainous regions, with a wall to out-climb on every take-off.
            float pad = Mathf.Max(map.Height(x, z), 4f);
            const float inner = 30f, outer = 75f;

            for (int iz = 0; iz < map.Resolution; iz++)
            for (int ix = 0; ix < map.Resolution; ix++)
            {
                float dx = map.NodeCoordinate(ix) - x, dz = map.NodeCoordinate(iz) - z;
                float distance = Mathf.Sqrt(dx * dx + dz * dz);
                if (distance >= outer) continue;
                float blend = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(outer, inner, distance));
                map[ix, iz] = Mathf.Lerp(map[ix, iz], pad, blend);
            }

            Spawn = new Vector3(x, pad, z);
            SpawnYawDeg = 0f;
        }

        void PlaceTrees(System.Random random, float offsetX, float offsetZ)
        {
            HeightMap map = Map;
            float span = map.HalfSize * 0.92f;
            for (int attempt = 0; attempt < 9000 && Trees.Count < Biome.MaxTrees; attempt++)
            {
                float x = ((float)random.NextDouble() * 2f - 1f) * span;
                float z = ((float)random.NextDouble() * 2f - 1f) * span;
                float scale = (0.75f + (float)random.NextDouble() * 0.6f) * Biome.TreeScale;

                float ground = map.Height(x, z);
                if (ground < 2.5f || ground > Biome.TreeLine) continue;
                if (map.Steepness(x, z) > 0.22f) continue;
                if (Mathf.PerlinNoise(x * 0.006f + offsetX + 200f, z * 0.006f + offsetZ + 200f) < 1f - Biome.TreeCover) continue;
                float sx = x - Spawn.x, sz = z - Spawn.z;
                if (sx * sx + sz * sz < 45f * 45f) continue;
                if (NearBeacon(x, z, 18f) || NearAnchor(x, z, 16f)) continue;
                if (NearRuin(x, z, 4f) || NearRoad(x, z, 7f)) continue;

                float height = 8f * scale;
                float radius = 1.9f * scale;
                bool burnt = NearVillage(x, z, 95f) && random.NextDouble() < 0.65;
                Trees.Add(new TreeSpec { Base = new Vector3(x, ground, z), Height = height, Radius = radius, Burnt = burnt });
                Obstacles.Add(new Obstacle { X = x, Z = z, Radius = radius * 0.6f, Top = ground + height * 0.9f, Soft = true });
            }
        }

        /// <summary>Three masts spread around the island, each on dry and fairly level ground.</summary>
        void PlaceBeacons()
        {
            float startAngle = Mathf.Atan2(Spawn.z, Spawn.x);
            float[] turns = { 70f, 180f, 290f };
            foreach (float turn in turns)
            {
                float angle = startAngle + turn * Mathf.Deg2Rad;
                Vector3 best = FlatSpot(Mathf.Cos(angle) * Map.HalfSize * 0.38f, Mathf.Sin(angle) * Map.HalfSize * 0.38f);
                Beacons.Add(best);
                Obstacles.Add(new Obstacle { X = best.x, Z = best.z, Radius = 0.9f, Top = best.y + BeaconHeight });
            }
        }

        /// <summary>The flattest dry point within about 70 m of a position.</summary>
        Vector3 FlatSpot(float centerX, float centerZ)
        {
            Vector3 best = new Vector3(centerX, Map.Height(centerX, centerZ), centerZ);
            float bestScore = float.MaxValue;
            for (int ring = 0; ring <= 6; ring++)
            for (int step = 0; step < 8; step++)
            {
                float offset = ring * 12f, around = step * Mathf.PI / 4f;
                float x = centerX + Mathf.Cos(around) * offset;
                float z = centerZ + Mathf.Sin(around) * offset;
                float ground = Map.Height(x, z);
                float score = Map.Steepness(x, z) * 100f + offset * 0.05f + (ground < 4f ? 1000f : 0f);
                if (score >= bestScore) continue;
                bestScore = score;
                best = new Vector3(x, ground, z);
            }
            return best;
        }

        void PlaceAnchors()
        {
            float startAngle = Mathf.Atan2(Spawn.z, Spawn.x);
            float half = Map.HalfSize;

            float[] reach = { 0.20f, 0.32f, 0.44f };
            for (int i = 0; i < 12; i++)
            {
                float angle = startAngle + (i * 30f + 15f) * Mathf.Deg2Rad;
                float distance = half * reach[i % 3];
                Anchors["poi" + i] = FlatSpot(Mathf.Cos(angle) * distance, Mathf.Sin(angle) * distance);
            }

            for (int i = 0; i < 8; i++)
            {
                float angle = startAngle + (i * 45f + 22.5f) * Mathf.Deg2Rad;
                var direction = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));
                // Walk outwards until the ground dips to the beach, and stay one step short of it.
                // Where channels cut the land the walk may begin in water: find dry ground first,
                // looking outwards and then back towards the middle.
                float begin = half * 0.3f;
                if (Map.Height(direction.x * begin, direction.y * begin) < 3f)
                {
                    float found = -1f;
                    for (float distance = begin; distance < half * 0.9f && found < 0f; distance += 6f)
                        if (Map.Height(direction.x * distance, direction.y * distance) >= 4f) found = distance;
                    for (float distance = begin; distance > 0f && found < 0f; distance -= 6f)
                        if (Map.Height(direction.x * distance, direction.y * distance) >= 4f) found = distance;
                    if (found >= 0f) begin = found;
                }
                Vector2 shore = direction * begin;
                for (float distance = begin; distance < half; distance += 6f)
                {
                    Vector2 point = direction * distance;
                    if (Map.Height(point.x, point.y) < 3f) break;
                    shore = point;
                }
                Anchors["coast" + i] = new Vector3(shore.x, Map.Height(shore.x, shore.y), shore.y);
            }

            for (int i = 0; i < 4; i++)
            {
                float angle = startAngle + (i * 90f + 45f) * Mathf.Deg2Rad;
                var direction = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));
                Vector2 water = direction * half * 0.98f;
                for (float distance = half * 0.3f; distance < half; distance += 6f)
                {
                    Vector2 point = direction * distance;
                    if (Map.Height(point.x, point.y) > -6f) continue;
                    water = point;
                    break;
                }
                Anchors["sea" + i] = new Vector3(water.x, WaterLevel, water.y);
            }

            Vector3 summit = Vector3.zero;
            for (int iz = 0; iz < Map.Resolution; iz++)
            for (int ix = 0; ix < Map.Resolution; ix++)
            {
                if (Map[ix, iz] <= summit.y) continue;
                summit = new Vector3(Map.NodeCoordinate(ix), Map[ix, iz], Map.NodeCoordinate(iz));
            }
            Anchors["summit"] = summit;
        }

        // ---------------------------------------------------------------- the war

        /// <summary>
        /// Shelled villages around most of the inland anchors, wrecks and craters among them, and
        /// dirt roads joining it all up. The middle of each village stays open: that is where
        /// missions park their targets.
        /// </summary>
        void PlaceWar(System.Random random)
        {
            float Next(float min, float max) => min + (float)random.NextDouble() * (max - min);

            for (int i = 0; i < 12; i++)
            {
                Vector3 centre = Anchors["poi" + i];
                bool village = random.NextDouble() < Biome.Ruined;
                // The road net: each inland point to the next one round, and out to the shore.
                Roads.Add(new RoadSpec { From = centre, To = Anchors["poi" + (i + 1) % 12] });
                if (i % 3 == 2) Roads.Add(new RoadSpec { From = centre, To = Anchors["coast" + ((i + 1) / 3 * 2) % 8] });
                if (!village) continue;
                Villages.Add(centre);

                int wanted = random.Next(5, 9);
                for (int attempt = 0; attempt < 40 && wanted > 0; attempt++)
                {
                    float angle = Next(0f, 2f * Mathf.PI), reach = Next(VillageSquare + 6f, 72f);
                    float x = centre.x + Mathf.Cos(angle) * reach, z = centre.z + Mathf.Sin(angle) * reach;
                    float ground = Map.Height(x, z);
                    float width = Next(7f, 12f), depth = Next(6f, 9f);
                    if (ground < 3.5f || Map.Steepness(x, z) > 0.2f) continue;
                    if (TooCloseForRuin(x, z, width)) continue;
                    var ruin = new RuinSpec
                    {
                        Base = new Vector3(x, ground, z), YawDeg = Next(0f, 180f), Width = width, Depth = depth,
                        Height = Next(4.5f, RuinMaxHeight), Seed = random.Next(),
                    };
                    Ruins.Add(ruin);
                    AddRuinObstacles(ruin);
                    if (random.NextDouble() < 0.1) Fires.Add(ruin.Base);
                    wanted--;
                }

                // A city: high-rises on a loose grid around the crossroads, well clear of its middle and its roads.
                int towers = Biome.Towers;
                for (int attempt = 0; attempt < 60 && towers > 0; attempt++)
                {
                    float gx = Mathf.Round(Next(-5f, 5f)) * 26f, gz = Mathf.Round(Next(-5f, 5f)) * 26f;
                    float away = Mathf.Sqrt(gx * gx + gz * gz);
                    if (away < 62f || away > 140f) continue;
                    float x = centre.x + gx, z = centre.z + gz;
                    float ground = Map.Height(x, z);
                    float side = Next(12f, 17f);
                    if (ground < 4f || Map.Steepness(x, z) > 0.16f) continue;
                    if (TooCloseForRuin(x, z, side + 6f) || NearRoad(x, z, 12f) || NearAnchor(x, z, 50f)) continue;
                    // The two points where missions put their radars are flown at rooftop height: no high-rises there.
                    if (Flat(x, z, Anchors["poi3"]) < 210f || Flat(x, z, Anchors["poi6"]) < 210f) continue;
                    var tower = new RuinSpec
                    {
                        Base = new Vector3(x, ground, z), YawDeg = 0f, Width = side, Depth = side,
                        Height = Next(12.5f, Biome.TowerHeight), Seed = random.Next(),
                    };
                    Ruins.Add(tower);
                    AddRuinObstacles(tower);
                    if (random.NextDouble() < 0.08) Fires.Add(tower.Base + Vector3.up * tower.Height);
                    towers--;
                }

                int wrecks = random.Next(1, 4);
                for (int w = 0; w < wrecks; w++)
                {
                    float angle = Next(0f, 2f * Mathf.PI), reach = Next(VillageSquare - 4f, 60f);
                    float x = centre.x + Mathf.Cos(angle) * reach, z = centre.z + Mathf.Sin(angle) * reach;
                    float ground = Map.Height(x, z);
                    if (ground < 3f || Map.Steepness(x, z) > 0.25f || NearRuin(x, z, 5f)) continue;
                    var wreck = new WreckSpec
                    {
                        Base = new Vector3(x, ground, z), YawDeg = Next(0f, 360f), Kind = random.Next(0, 6),
                        Burning = random.NextDouble() < 0.35,
                    };
                    Wrecks.Add(wreck);
                    if (wreck.Burning) Fires.Add(wreck.Base);
                }

                int craters = random.Next(8, 15);
                for (int c = 0; c < craters; c++)
                {
                    float angle = Next(0f, 2f * Mathf.PI), reach = Next(8f, 105f);
                    Craters.Add(new CraterSpec
                    {
                        X = centre.x + Mathf.Cos(angle) * reach, Z = centre.z + Mathf.Sin(angle) * reach, Radius = Next(2.5f, 6f),
                    });
                }
            }

            // The front has passed over the open ground too.
            for (int c = 0; c < 70; c++)
            {
                float x = Next(-1f, 1f) * Map.HalfSize * 0.8f, z = Next(-1f, 1f) * Map.HalfSize * 0.8f;
                if (Map.Height(x, z) < 2.5f) continue;
                Craters.Add(new CraterSpec { X = x, Z = z, Radius = Next(2.5f, 5f) });
            }
            Roads.Add(new RoadSpec { From = Spawn, To = Anchors["poi0"] });
            Roads.Add(new RoadSpec { From = Spawn, To = Anchors["poi11"] });
        }

        // ---------------------------------------------------------------- towns

        void AddProp(int model, float x, float z, float yaw, float scale, float obstacle)
        {
            float ground = Map.Height(x, z);
            Props.Add(new PropSpec { Base = new Vector3(x, ground, z), YawDeg = yaw, Scale = scale, Model = model });
            if (obstacle > 0f)
                Obstacles.Add(new Obstacle { X = x, Z = z, Radius = obstacle, Top = ground + PropHeights[model] * scale * 0.95f, Soft = model >= PropPalm && model < PropRock });
        }

        bool NearProp(float x, float z, float margin)
        {
            for (int i = Props.Count - 1; i >= 0; i--)
            {
                float dx = x - Props[i].Base.x, dz = z - Props[i].Base.z;
                if (dx * dx + dz * dz < margin * margin) return true;
            }
            return false;
        }

        bool TreeAt(float x, float z, float margin)
        {
            foreach (TreeSpec tree in Trees)
            {
                float dx = x - tree.Base.x, dz = z - tree.Base.z;
                if (dx * dx + dz * dz < margin * margin) return true;
            }
            return false;
        }

        /// <summary>
        /// Streets of houses around every village, yards full of containers and barriers, a camp by
        /// the pad, palms where it is warm and boulders on the slopes. Houses stand on a grid turned
        /// with the village, clear of its square and its roads, and no taller than a ruin, so what
        /// could be flown before can still be flown.
        /// </summary>
        void PlaceTowns(System.Random random)
        {
            float Next(float min, float max) => min + (float)random.NextDouble() * (max - min);
            bool warm = Biome.Id == "desert" || Biome.Id == "tropic" || Biome.Id == "mesa" || Biome.Id == "urban";
            bool city = Biome.Towers > 0;

            foreach (Vector3 centre in Villages)
            {
                float turn = Next(0f, 90f);
                float rad = turn * Mathf.Deg2Rad, cos = Mathf.Cos(rad), sin = Mathf.Sin(rad);
                const float step = 19f;
                int houses = 0, wanted = city ? 20 : random.Next(9, 15);
                for (int attempt = 0; attempt < 140 && houses < wanted; attempt++)
                {
                    int gx = random.Next(-5, 6), gz = random.Next(-5, 6);
                    float lx = gx * step, lz = gz * step, away = Mathf.Sqrt(lx * lx + lz * lz);
                    if (away < VillageSquare + 9f || away > 104f) continue;
                    float x = centre.x + lx * cos - lz * sin, z = centre.z + lx * sin + lz * cos;
                    float ground = Map.Height(x, z);
                    if (ground < 3.5f || Map.Steepness(x, z) > 0.2f) continue;
                    if (TooCloseForRuin(x, z, 6f) || NearRoad(x, z, 9f) || NearProp(x, z, 15f) || TreeAt(x, z, 6f)) continue;
                    // Every house is kept under the height of a ruin: missions are flown at rooftop height.
                    int model = random.Next(PropLowCount + (city ? PropTallCount : 1));
                    float scale = Next(0.95f, 1.08f) * Mathf.Min(1f, (RuinMaxHeight - 0.4f) / PropHeights[model]);
                    AddProp(model, x, z, turn + random.Next(4) * 90f, scale, model >= PropTall ? 4.2f : 4.8f);
                    houses++;

                    // What stands in the yard: a container or two, a wall, rubbish, a palm.
                    int yard = random.Next(1, 4);
                    for (int y = 0; y < yard; y++)
                    {
                        float angle = Next(0f, 2f * Mathf.PI), reach = Next(7.5f, 10.5f);
                        float px = x + Mathf.Cos(angle) * reach, pz = z + Mathf.Sin(angle) * reach;
                        if (Map.Height(px, pz) < 3f || Map.Steepness(px, pz) > 0.22f || NearRoad(px, pz, 4f)) continue;
                        if (NearAnchor(px, pz, VillageSquare - 6f) || NearProp(px, pz, 4f) || NearRuin(px, pz, 2f)) continue;
                        int pick = random.Next(8);
                        if (pick < 2) AddProp(PropContainer + random.Next(2), px, pz, turn + random.Next(2) * 90f, 1f, 0f);
                        else if (pick == 2) AddProp(PropWall, px, pz, turn + random.Next(2) * 90f, 1f, 0f);
                        else if (pick == 3) AddProp(PropTrash, px, pz, Next(0f, 360f), 1f, 0f);
                        else if (pick == 4) AddProp(PropCrate, px, pz, Next(0f, 360f), 1f, 0f);
                        else if (pick == 5) AddProp(PropBarrel, px, pz, 0f, 1f, 0f);
                        else if (warm) AddProp(PropPalm + random.Next(3), px, pz, Next(0f, 360f), Next(0.8f, 1.1f), 0.5f);
                        else AddProp(PropSandbags, px, pz, Next(0f, 360f), 1f, 0f);
                    }
                }

                // Road blocks on the way in, and one thing that marks the place from afar.
                for (int b = 0; b < 10; b++)
                {
                    float angle = Next(0f, 2f * Mathf.PI), reach = Next(VillageSquare + 2f, 70f);
                    float x = centre.x + Mathf.Cos(angle) * reach, z = centre.z + Mathf.Sin(angle) * reach;
                    float road = RoadDistance(x, z, 9f);
                    if (road < 4.5f || road >= 9f || Map.Height(x, z) < 3f || NearProp(x, z, 3.5f) || NearRuin(x, z, 2f) || NearPoint(x, z, 26f)) continue;
                    AddProp(PropBarrier + random.Next(2), x, z, Next(0f, 180f), 1f, 0f);
                }
                for (int attempt = 0; attempt < 12; attempt++)
                {
                    float angle = Next(0f, 2f * Mathf.PI), reach = Next(VillageSquare + 12f, 80f);
                    float x = centre.x + Mathf.Cos(angle) * reach, z = centre.z + Mathf.Sin(angle) * reach;
                    if (Map.Height(x, z) < 3.5f || Map.Steepness(x, z) > 0.18f) continue;
                    if (TooCloseForRuin(x, z, 4f) || NearRoad(x, z, 8f) || NearProp(x, z, 9f) || TreeAt(x, z, 4f)) continue;
                    AddProp(new[] { PropWater, PropRadio, PropGuard }[random.Next(3)], x, z, turn, 1f, 1.8f);
                    break;
                }
            }

            // The camp by the pad: tents, stores and a fence of concrete, all low and well off the pad itself.
            for (int i = 0; i < 26; i++)
            {
                float angle = Next(0f, 2f * Mathf.PI), reach = Next(24f, 58f);
                float x = Spawn.x + Mathf.Cos(angle) * reach, z = Spawn.z + Mathf.Sin(angle) * reach;
                if (Map.Height(x, z) < 2.5f || Map.Steepness(x, z) > 0.2f || NearRoad(x, z, 5f)) continue;
                if (NearProp(x, z, 7f) || TreeAt(x, z, 3f) || NearBeacon(x, z, 10f) || NearPoint(x, z, 26f)) continue;
                int pick = i % 6;
                if (pick == 0) AddProp(PropTent, x, z, Next(0f, 360f), 1f, 0f);
                else if (pick == 1) AddProp(PropContainer + random.Next(2), x, z, Next(0f, 180f), 1f, 0f);
                else if (pick == 2) AddProp(PropBarrier + random.Next(2), x, z, angle * Mathf.Rad2Deg, 1f, 0f);
                else if (pick == 3) AddProp(PropCrate, x, z, Next(0f, 360f), 1f, 0f);
                else if (pick == 4) AddProp(PropSandbags, x, z, -angle * Mathf.Rad2Deg + 90f, 1f, 0f);
                else AddProp(PropBarrel, x, z, 0f, 1f, 0f);
            }

            // Round the pad itself: stores stacked within a few steps of it, nothing an aircraft could clip.
            for (int i = 0; i < 16; i++)
            {
                float angle = i * Mathf.PI / 8f + Next(-0.12f, 0.12f), reach = Next(9.5f, 19f);
                float x = Spawn.x + Mathf.Cos(angle) * reach, z = Spawn.z + Mathf.Sin(angle) * reach;
                if (Map.Height(x, z) < 2.5f || Map.Steepness(x, z) > 0.2f || NearRoad(x, z, 3.5f) || NearProp(x, z, 2.6f)) continue;
                int pick = i % 4;
                AddProp(pick == 0 ? PropCrate : pick == 1 ? PropBarrel : pick == 2 ? PropSandbags : PropBarrier + random.Next(2),
                    x, z, pick == 2 ? -angle * Mathf.Rad2Deg + 90f : Next(0f, 360f), pick == 0 ? Next(0.9f, 1.5f) : 1f, 0f);
            }
            // And a shed for the aircraft behind it, where the approach does not pass.
            {
                foreach (float side in new[] { -24f, 24f })
                {
                    float away = (SpawnYawDeg + 180f + side) * Mathf.Deg2Rad;
                    float x = Spawn.x + Mathf.Sin(away) * 36f, z = Spawn.z + Mathf.Cos(away) * 36f;
                    if (Map.Height(x, z) < 3f || Map.Steepness(x, z) > 0.2f || NearRoad(x, z, 8f) || NearProp(x, z, 10f) || NearPoint(x, z, 26f)) continue;
                    AddProp(side < 0f ? 6 : PropGuard, x, z, SpawnYawDeg, 1f, side < 0f ? 5f : 1.8f);
                }
            }

            // Boulders on the slopes and along the shore.
            int rocks = Biome.Id == "urban" ? 50 : 150;
            for (int attempt = 0; attempt < 2400 && rocks > 0; attempt++)
            {
                float x = Next(-1f, 1f) * Map.HalfSize * 0.86f, z = Next(-1f, 1f) * Map.HalfSize * 0.86f;
                float ground = Map.Height(x, z), steep = Map.Steepness(x, z);
                if (ground < 1.2f || steep < 0.1f || steep > 0.75f) continue;
                if (Flat(x, z, Spawn) < 70f || NearAnchor(x, z, 34f) || NearBeacon(x, z, 26f) || NearRoad(x, z, 8f)) continue;
                if (NearRuin(x, z, 6f) || NearProp(x, z, 9f) || TreeAt(x, z, 3f)) continue;
                int model = PropRock + random.Next(4);
                float scale = Next(0.7f, 1.7f);
                AddProp(model, x, z, Next(0f, 360f), scale, PropHeights[model] * scale * 0.34f);
                rocks--;
            }
        }

        static float Flat(float x, float z, Vector3 to) => Mathf.Sqrt((x - to.x) * (x - to.x) + (z - to.z) * (z - to.z));

        bool TooCloseForRuin(float x, float z, float size)
        {
            float sx = x - Spawn.x, sz = z - Spawn.z;
            if (sx * sx + sz * sz < 80f * 80f) return true;
            if (NearBeacon(x, z, 24f) || NearAnchor(x, z, VillageSquare)) return true;
            return NearRuin(x, z, size + 5f);
        }

        /// <summary>A ruin's footprint as two upright cylinders along its longer side.</summary>
        void AddRuinObstacles(RuinSpec ruin)
        {
            float yaw = ruin.YawDeg * Mathf.Deg2Rad;
            var along = new Vector3(Mathf.Cos(yaw), 0f, -Mathf.Sin(yaw));
            float radius = ruin.Depth * 0.5f;
            float offset = Mathf.Max(0f, ruin.Width * 0.5f - radius);
            foreach (float side in new[] { -1f, 1f })
            {
                Vector3 centre = ruin.Base + along * (side * offset);
                Obstacles.Add(new Obstacle { X = centre.x, Z = centre.z, Radius = radius, Top = ruin.Base.y + ruin.Height });
            }
        }

        bool NearRuin(float x, float z, float margin)
        {
            foreach (RuinSpec ruin in Ruins)
            {
                float dx = x - ruin.Base.x, dz = z - ruin.Base.z;
                float limit = ruin.Width * 0.5f + margin;
                if (dx * dx + dz * dz < limit * limit) return true;
            }
            return false;
        }

        bool NearVillage(float x, float z, float distance)
        {
            foreach (Vector3 village in Villages)
            {
                float dx = x - village.x, dz = z - village.z;
                if (dx * dx + dz * dz < distance * distance) return true;
            }
            return false;
        }

        /// <summary>Distance from a point to the nearest road, at most <paramref name="limit"/>.</summary>
        public float RoadDistance(float x, float z, float limit)
        {
            float best = limit;
            foreach (RoadSpec road in Roads)
            {
                float ax = road.From.x, az = road.From.z, bx = road.To.x - ax, bz = road.To.z - az;
                float length = bx * bx + bz * bz;
                float t = length > 0.01f ? Mathf.Clamp01(((x - ax) * bx + (z - az) * bz) / length) : 0f;
                float dx = x - (ax + bx * t), dz = z - (az + bz * t);
                float distance = Mathf.Sqrt(dx * dx + dz * dz);
                if (distance < best) best = distance;
            }
            return best;
        }

        bool NearRoad(float x, float z, float distance) => RoadDistance(x, z, distance) < distance;

        bool NearAnchor(float x, float z, float distance)
        {
            foreach (Vector3 anchor in Anchors.Values)
            {
                float dx = x - anchor.x, dz = z - anchor.z;
                if (dx * dx + dz * dz < distance * distance) return true;
            }
            return false;
        }

        /// <summary>Near one of the inland points where missions put their targets.</summary>
        bool NearPoint(float x, float z, float distance)
        {
            for (int i = 0; i < 12; i++)
                if (Flat(x, z, Anchors["poi" + i]) < distance) return true;
            return false;
        }

        bool NearBeacon(float x, float z, float distance)
        {
            foreach (Vector3 beacon in Beacons)
            {
                float dx = x - beacon.x, dz = z - beacon.z;
                if (dx * dx + dz * dz < distance * distance) return true;
            }
            return false;
        }

        /// <summary>A loop of rings around the island that starts and ends near the pad.</summary>
        void PlaceRings()
        {
            const int count = 10;
            float startAngle = Mathf.Atan2(Spawn.z, Spawn.x);
            for (int i = 0; i < count; i++)
            {
                float angle = startAngle + (i + 0.6f) * (2f * Mathf.PI / count);
                float distance = Map.HalfSize * (0.42f + 0.06f * Mathf.Sin(i * 2.1f));
                float x = Mathf.Cos(angle) * distance, z = Mathf.Sin(angle) * distance;
                float ground = Mathf.Max(Map.Height(x, z), WaterLevel);
                Rings.Add(new Vector3(x, ground + 18f + 6f * Mathf.Sin(i * 1.7f), z));
            }
        }

        static float Fbm(float x, float z, int octaves)
        {
            float sum = 0f, amplitude = 0.5f, total = 0f;
            for (int i = 0; i < octaves; i++)
            {
                sum += Mathf.PerlinNoise(x, z) * amplitude;
                total += amplitude;
                amplitude *= 0.5f;
                x = x * 2f + 17.3f;
                z = z * 2f + 9.1f;
            }
            return Mathf.Clamp01(sum / total);
        }
    }
}
