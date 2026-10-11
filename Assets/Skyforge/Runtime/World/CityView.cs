using System.Collections.Generic;
using Skyforge.Core;
using UnityEngine;
using UnityEngine.Rendering;

namespace Skyforge.World
{
    /// <summary>
    /// What makes a town read as a town from the air: roads of real asphalt laid over the tracks
    /// the island drew, and house fronts with rows of windows on the high-rises.
    /// </summary>
    public static class CityView
    {
        /// <summary>A mesh whose texture coordinates are given in full, for photographed surfaces that repeat.</summary>
        sealed class Sheet
        {
            readonly List<Vector3> _vertices = new List<Vector3>();
            readonly List<Vector3> _normals = new List<Vector3>();
            readonly List<Vector2> _uvs = new List<Vector2>();
            readonly List<int> _triangles = new List<int>();

            public int Count => _vertices.Count;

            public void Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector3 normal, Vector2 ua, Vector2 ub, Vector2 uc, Vector2 ud)
            {
                int start = _vertices.Count;
                _vertices.Add(a); _vertices.Add(b); _vertices.Add(c); _vertices.Add(d);
                for (int i = 0; i < 4; i++) _normals.Add(normal);
                _uvs.Add(ua); _uvs.Add(ub); _uvs.Add(uc); _uvs.Add(ud);
                // Wound whichever way faces along the normal.
                bool flip = Vector3.Dot(Vector3.Cross(b - a, c - a), normal) < 0f;
                _triangles.Add(start); _triangles.Add(flip ? start + 2 : start + 1); _triangles.Add(flip ? start + 1 : start + 2);
                _triangles.Add(start); _triangles.Add(flip ? start + 3 : start + 2); _triangles.Add(flip ? start + 2 : start + 3);
            }

            public void Spawn(string name, Transform parent, Material material, bool shadows)
            {
                if (_vertices.Count == 0 || material == null) return;
                var mesh = new Mesh { name = name };
                if (_vertices.Count > 65000) mesh.indexFormat = IndexFormat.UInt32;
                mesh.SetVertices(_vertices);
                mesh.SetNormals(_normals);
                mesh.SetUVs(0, _uvs);
                mesh.SetTriangles(_triangles, 0);
                mesh.RecalculateBounds();
                mesh.UploadMeshData(true);
                var obj = new GameObject(name);
                obj.transform.SetParent(parent, false);
                obj.AddComponent<MeshFilter>().sharedMesh = mesh;
                var renderer = obj.AddComponent<MeshRenderer>();
                renderer.sharedMaterial = material;
                renderer.shadowCastingMode = shadows ? ShadowCastingMode.On : ShadowCastingMode.Off;
            }
        }

        const float RoadWidth = 7.4f, RoadStep = 3f, RoadLift = 0.22f;
        /// <summary>Metres one repeat of a house front covers: six windows across, six floors up.</summary>
        const float FrontAcross = 18f, FrontUp = 18.6f;
        const int Fronts = 4;

        public static void Build(Island island, Transform parent)
        {
            Transform root = new GameObject("City").transform;
            root.SetParent(parent, false);
            BuildRoads(island, root);
            BuildFronts(island, root);
        }

        /// <summary>The same line the ground paint follows, so the asphalt lies in its own dust.</summary>
        static Vector3 RoadPoint(RoadSpec road, float t)
        {
            Vector3 path = road.To - road.From;
            Vector3 point = road.From + path * t;
            float sway = (Mathf.PerlinNoise(point.x * 0.01f + 5f, point.z * 0.01f + 9f) - 0.5f) * 14f;
            Vector3 side = new Vector3(-path.z, 0f, path.x).normalized * sway * Mathf.Sin(Mathf.PI * t);
            return new Vector3(point.x + side.x, 0f, point.z + side.z);
        }

        static void BuildRoads(Island island, Transform root)
        {
            HeightMap map = island.Map;
            var sheets = new[] { new Sheet(), new Sheet() };
            for (int r = 0; r < island.Roads.Count; r++)
            {
                RoadSpec road = island.Roads[r];
                Sheet sheet = sheets[r % 2];
                float length = new Vector2(road.To.x - road.From.x, road.To.z - road.From.z).magnitude;
                int steps = Mathf.Max(2, Mathf.CeilToInt(length / RoadStep));
                bool have = false;
                Vector3 lastLeft = default, lastMid = default, lastRight = default;
                float run = 0f;
                for (int i = 0; i <= steps; i++)
                {
                    float t = i / (float)steps;
                    Vector3 here = RoadPoint(road, t);
                    Vector3 ahead = RoadPoint(road, Mathf.Min(1f, t + 0.5f / steps)) - RoadPoint(road, Mathf.Max(0f, t - 0.5f / steps));
                    if (ahead.sqrMagnitude < 0.0001f) continue;
                    Vector3 right = new Vector3(ahead.z, 0f, -ahead.x).normalized * RoadWidth * 0.5f;
                    Vector3 left = here - right, mid = here, far = here + right;
                    left.y = map.Height(left.x, left.z);
                    mid.y = map.Height(mid.x, mid.z);
                    far.y = map.Height(far.x, far.z);
                    // No road across the water, and none up a cliff.
                    bool usable = Mathf.Min(left.y, Mathf.Min(mid.y, far.y)) > Island.WaterLevel + 0.7f && Mathf.Abs(left.y - far.y) < RoadWidth * 0.55f;
                    // A road is graded: it does not follow every bump across its width.
                    float level = (left.y + mid.y + far.y) / 3f;
                    left.y = Mathf.Max(left.y, Mathf.Lerp(left.y, level, 0.5f)) + RoadLift;
                    far.y = Mathf.Max(far.y, Mathf.Lerp(far.y, level, 0.5f)) + RoadLift;
                    mid.y = Mathf.Max(mid.y, level) + RoadLift;
                    float stride = have ? (mid - lastMid).magnitude : 0f;
                    run += stride;
                    float v = run / RoadWidth;
                    if (have && usable)
                    {
                        float before = (run - stride) / RoadWidth;
                        Vector3 up = Vector3.up;
                        sheet.Quad(lastLeft, left, mid, lastMid, up, new Vector2(0f, before), new Vector2(0f, v), new Vector2(0.5f, v), new Vector2(0.5f, before));
                        sheet.Quad(lastMid, mid, far, lastRight, up, new Vector2(0.5f, before), new Vector2(0.5f, v), new Vector2(1f, v), new Vector2(1f, before));
                    }
                    lastLeft = left; lastMid = mid; lastRight = far;
                    have = usable;
                }
            }
            sheets[0].Spawn("Roads", root, GameMaterials.Mapped("map_road", 0.22f), false);
            sheets[1].Spawn("RoadsWorn", root, GameMaterials.Mapped("map_road_worn", 0.18f), false);
        }

        static void BuildFronts(Island island, Transform root)
        {
            var sheets = new Sheet[Fronts];
            for (int i = 0; i < Fronts; i++) sheets[i] = new Sheet();
            int placed = 0;
            foreach (RuinSpec ruin in island.Ruins)
            {
                if (ruin.Height <= Island.RuinMaxHeight + 0.5f) continue;
                Sheet sheet = sheets[Mathf.Abs(ruin.Seed) % Fronts];
                float half = ruin.Width * 0.5f + 0.07f, bottom = ruin.Base.y - 1.5f, top = ruin.Base.y + ruin.Height;
                // Whole windows only: the picture is stretched a little so no floor is cut in half.
                float across = Mathf.Max(1f, Mathf.Round(ruin.Width / 3f)) / 6f, up = Mathf.Max(1f, Mathf.Round(ruin.Height / 3.1f)) / 6f;
                float below = -1.5f / FrontUp;
                float shift = (Mathf.Abs(ruin.Seed) % 6) / 6f;
                for (int side = 0; side < 4; side++)
                {
                    Vector3 normal = side == 0 ? Vector3.forward : side == 1 ? Vector3.right : side == 2 ? Vector3.back : Vector3.left;
                    Vector3 along = new Vector3(normal.z, 0f, -normal.x);
                    Vector3 centre = new Vector3(ruin.Base.x, 0f, ruin.Base.z) + normal * half;
                    Vector3 a = centre - along * half, b = centre + along * half;
                    sheet.Quad(new Vector3(a.x, bottom, a.z), new Vector3(a.x, top, a.z), new Vector3(b.x, top, b.z), new Vector3(b.x, bottom, b.z), normal,
                        new Vector2(shift, below), new Vector2(shift, up), new Vector2(shift + across, up), new Vector2(shift + across, below));
                }
                placed++;
            }
            if (placed == 0) return;
            for (int i = 0; i < Fronts; i++) sheets[i].Spawn("Fronts" + i, root, GameMaterials.Mapped("map_facade" + (i + 1), 0.3f), false);
        }
    }
}
