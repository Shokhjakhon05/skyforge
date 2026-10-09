using System.Collections.Generic;
using UnityEngine;

namespace Skyforge.Core
{
    public struct TreeSpec
    {
        public Vector3 Base;
        public float Height;
        public float Radius;
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
        public Vector3 Spawn { get; private set; }
        public float SpawnYawDeg { get; private set; }

        public static Island Generate(int seed, int resolution = 257, float size = 1536f)
        {
            var island = new Island
            {
                Map = new HeightMap(resolution, size),
                Obstacles = new ObstacleField(),
                Trees = new List<TreeSpec>(),
                Rings = new List<Vector3>(),
                Beacons = new List<Vector3>(),
            };
            var random = new System.Random(seed);
            float offsetX = (float)random.NextDouble() * 1000f;
            float offsetZ = (float)random.NextDouble() * 1000f;

            island.BuildHeights(offsetX, offsetZ);
            island.PlaceSpawn();
            island.PlaceBeacons();
            island.PlaceTrees(random, offsetX, offsetZ);
            island.PlaceRings();
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

                map[ix, iz] = land * (8f + hills * 60f + ridge * hills * 110f) - (1f - land) * 14f;
            }
        }

        /// <summary>Levels a pad for the start, on the southern side of the island.</summary>
        void PlaceSpawn()
        {
            HeightMap map = Map;
            float x = 0f, z = -0.5f * map.HalfSize;
            float pad = Mathf.Clamp(map.Height(x, z), 4f, 40f);
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
            for (int attempt = 0; attempt < 9000 && Trees.Count < 2200; attempt++)
            {
                float x = ((float)random.NextDouble() * 2f - 1f) * span;
                float z = ((float)random.NextDouble() * 2f - 1f) * span;
                float scale = 0.75f + (float)random.NextDouble() * 0.6f;

                float ground = map.Height(x, z);
                if (ground < 2.5f || ground > 95f) continue;
                if (map.Steepness(x, z) > 0.22f) continue;
                if (Mathf.PerlinNoise(x * 0.006f + offsetX + 200f, z * 0.006f + offsetZ + 200f) < 0.45f) continue;
                float sx = x - Spawn.x, sz = z - Spawn.z;
                if (sx * sx + sz * sz < 45f * 45f) continue;
                if (NearBeacon(x, z, 18f)) continue;

                float height = 8f * scale;
                float radius = 1.9f * scale;
                Trees.Add(new TreeSpec { Base = new Vector3(x, ground, z), Height = height, Radius = radius });
                Obstacles.Add(new Obstacle { X = x, Z = z, Radius = radius * 0.6f, Top = ground + height * 0.9f });
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
                Vector3 best = Vector3.zero;
                float bestScore = float.MaxValue;
                // Look around the ideal spot and take the flattest dry point.
                for (int ring = 0; ring <= 6; ring++)
                for (int step = 0; step < 8; step++)
                {
                    float offset = ring * 12f, around = step * Mathf.PI / 4f;
                    float x = Mathf.Cos(angle) * Map.HalfSize * 0.38f + Mathf.Cos(around) * offset;
                    float z = Mathf.Sin(angle) * Map.HalfSize * 0.38f + Mathf.Sin(around) * offset;
                    float ground = Map.Height(x, z);
                    float score = Map.Steepness(x, z) * 100f + offset * 0.05f + (ground < 4f ? 1000f : 0f);
                    if (score >= bestScore) continue;
                    bestScore = score;
                    best = new Vector3(x, ground, z);
                }
                Beacons.Add(best);
                Obstacles.Add(new Obstacle { X = best.x, Z = best.z, Radius = 0.9f, Top = best.y + BeaconHeight });
            }
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
