using Skyforge.Core;
using UnityEngine;
using UnityEngine.Rendering;

namespace Skyforge.World
{
    /// <summary>The colours a biome's ground is painted with.</summary>
    public sealed class TerrainPaint
    {
        public Color SandWet = new Color(0.55f, 0.50f, 0.36f);
        public Color Sand = new Color(0.80f, 0.74f, 0.54f);
        public Color Ground = new Color(0.36f, 0.52f, 0.24f);
        public Color GroundDry = new Color(0.50f, 0.54f, 0.28f);
        public Color Rock = new Color(0.52f, 0.49f, 0.44f);
        public Color Snow = new Color(0.93f, 0.95f, 0.97f);
        /// <summary>Heights from which bare rock and snow take over.</summary>
        public float RockFrom = 80f, SnowFrom = 118f;

        static readonly TerrainPaint Temperate = new TerrainPaint();
        static readonly TerrainPaint Desert = new TerrainPaint
        {
            SandWet = new Color(0.62f, 0.52f, 0.36f), Sand = new Color(0.88f, 0.78f, 0.56f),
            Ground = new Color(0.82f, 0.68f, 0.44f), GroundDry = new Color(0.72f, 0.55f, 0.36f),
            Rock = new Color(0.62f, 0.40f, 0.28f), RockFrom = 55f, SnowFrom = 9999f,
        };
        static readonly TerrainPaint Snowy = new TerrainPaint
        {
            SandWet = new Color(0.46f, 0.48f, 0.50f), Sand = new Color(0.66f, 0.69f, 0.72f),
            Ground = new Color(0.86f, 0.89f, 0.93f), GroundDry = new Color(0.76f, 0.80f, 0.86f),
            Rock = new Color(0.40f, 0.41f, 0.45f), RockFrom = 140f, SnowFrom = 40f,
        };

        static readonly TerrainPaint Tropical = new TerrainPaint
        {
            SandWet = new Color(0.66f, 0.60f, 0.42f), Sand = new Color(0.90f, 0.84f, 0.62f),
            Ground = new Color(0.24f, 0.50f, 0.20f), GroundDry = new Color(0.36f, 0.56f, 0.24f),
            Rock = new Color(0.44f, 0.44f, 0.38f), RockFrom = 70f, SnowFrom = 9999f,
        };
        static readonly TerrainPaint Volcanic = new TerrainPaint
        {
            SandWet = new Color(0.10f, 0.10f, 0.11f), Sand = new Color(0.17f, 0.16f, 0.16f),
            Ground = new Color(0.25f, 0.23f, 0.21f), GroundDry = new Color(0.33f, 0.27f, 0.22f),
            Rock = new Color(0.21f, 0.18f, 0.17f), Snow = new Color(0.50f, 0.17f, 0.08f), RockFrom = 50f, SnowFrom = 200f,
        };
        static readonly TerrainPaint Tableland = new TerrainPaint
        {
            SandWet = new Color(0.58f, 0.42f, 0.30f), Sand = new Color(0.82f, 0.62f, 0.42f),
            Ground = new Color(0.76f, 0.47f, 0.30f), GroundDry = new Color(0.68f, 0.38f, 0.24f),
            Rock = new Color(0.56f, 0.28f, 0.19f), RockFrom = 60f, SnowFrom = 9999f,
        };

        static readonly TerrainPaint City = new TerrainPaint
        {
            SandWet = new Color(0.36f, 0.36f, 0.35f), Sand = new Color(0.50f, 0.49f, 0.46f),
            Ground = new Color(0.38f, 0.40f, 0.36f), GroundDry = new Color(0.44f, 0.43f, 0.40f),
            Rock = new Color(0.42f, 0.42f, 0.42f), RockFrom = 60f, SnowFrom = 9999f,
        };

        public static TerrainPaint For(Biome biome)
        {
            if (biome == Biome.Urban) return City;
            if (biome == Biome.Tropic) return Tropical;
            if (biome == Biome.Ash) return Volcanic;
            if (biome == Biome.Mesa) return Tableland;
            if (biome == Biome.Desert) return Desert;
            if (biome == Biome.Snow) return Snowy;
            return Temperate;
        }
    }

    /// <summary>Turns an <see cref="Island"/> into things to look at: terrain, sea, trees, pad, sky.</summary>
    public static class IslandView
    {
        /// <summary>Builds the scenery and returns the sea material, which the weather recolours.</summary>
        public static Material Build(Island island, Transform parent)
        {
            Material sea = GameMaterials.Water;
            CreateObject("Terrain", parent, BuildTerrainMesh(island.Map),
                GameMaterials.Surface(BuildTerrainTexture(island, TerrainPaint.For(island.Biome)),
                    island.Biome == Biome.Desert || island.Biome == Biome.Mesa ? GameMaterials.Matter.Sand
                    : island.Biome == Biome.Snow ? GameMaterials.Matter.Snow : GameMaterials.Matter.Ground), true);
            CreateObject("Sea", parent, BuildSeaMesh(), sea, false);
            CreateObject("Trees", parent, BuildTreesMesh(island), GameMaterials.PaletteLit, true);
            CreateObject("Pad", parent, BuildPadMesh(island), GameMaterials.PaletteLit, false);
            return sea;
        }

        static void CreateObject(string name, Transform parent, Mesh mesh, Material material, bool castShadows)
        {
            var obj = new GameObject(name);
            obj.transform.SetParent(parent, false);
            obj.AddComponent<MeshFilter>().sharedMesh = mesh;
            var renderer = obj.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = castShadows ? ShadowCastingMode.On : ShadowCastingMode.Off;
        }

        static Mesh BuildTerrainMesh(HeightMap map)
        {
            int n = map.Resolution;
            var vertices = new Vector3[n * n];
            var uvs = new Vector2[n * n];
            for (int iz = 0; iz < n; iz++)
            for (int ix = 0; ix < n; ix++)
            {
                vertices[iz * n + ix] = new Vector3(map.NodeCoordinate(ix), map[ix, iz], map.NodeCoordinate(iz));
                uvs[iz * n + ix] = new Vector2(ix / (float)(n - 1), iz / (float)(n - 1));
            }

            var triangles = new int[(n - 1) * (n - 1) * 6];
            int t = 0;
            for (int iz = 0; iz < n - 1; iz++)
            for (int ix = 0; ix < n - 1; ix++)
            {
                int v00 = iz * n + ix, v10 = v00 + 1, v01 = v00 + n, v11 = v01 + 1;
                triangles[t++] = v00; triangles[t++] = v01; triangles[t++] = v11;
                triangles[t++] = v00; triangles[t++] = v11; triangles[t++] = v10;
            }

            var mesh = new Mesh { name = "Terrain", indexFormat = IndexFormat.UInt32 };
            mesh.vertices = vertices;
            mesh.uv = uvs;
            mesh.triangles = triangles;
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            mesh.UploadMeshData(true);
            return mesh;
        }

        /// <summary>Paints sand, grass, rock and snow from height and steepness.</summary>
        static Texture2D BuildTerrainTexture(Island island, TerrainPaint paint)
        {
            HeightMap map = island.Map;
            const int size = 1024;
            Color sandWet = paint.SandWet, sand = paint.Sand, grass = paint.Ground, grassDry = paint.GroundDry;
            Color rock = paint.Rock, snow = paint.Snow;
            var seabed = new Color(0.10f, 0.26f, 0.32f);

            var pixels = new Color32[size * size];
            for (int py = 0; py < size; py++)
            for (int px = 0; px < size; px++)
            {
                float x = -map.HalfSize + (px + 0.5f) / size * map.Size;
                float z = -map.HalfSize + (py + 0.5f) / size * map.Size;
                float height = map.Height(x, z);
                float steep = map.Steepness(x, z);
                float patch = Mathf.PerlinNoise(x * 0.02f + 31f, z * 0.02f + 77f);
                float grain = Mathf.PerlinNoise(x * 0.31f, z * 0.31f);

                Color color;
                if (height < -0.5f)
                {
                    color = Color.Lerp(sandWet, seabed, Mathf.InverseLerp(-0.5f, -6f, height));
                }
                else
                {
                    color = Color.Lerp(grass, grassDry, patch);
                    color = Color.Lerp(sand, color, Mathf.InverseLerp(1.5f, 4.5f, height));
                    color = Color.Lerp(sandWet, color, Mathf.InverseLerp(-0.5f, 0.8f, height));
                    color = Color.Lerp(color, rock, Mathf.InverseLerp(0.30f, 0.46f, steep) * 0.85f);
                    color = Color.Lerp(color, rock, Mathf.InverseLerp(paint.RockFrom, paint.RockFrom + 25f, height + patch * 12f));
                    color = Color.Lerp(color, snow,
                        Mathf.InverseLerp(paint.SnowFrom, paint.SnowFrom + 14f, height + patch * 10f) * Mathf.InverseLerp(0.45f, 0.25f, steep));
                }
                color *= 0.90f + grain * 0.20f;
                color.a = 1f;
                pixels[py * size + px] = color;
            }
            PaintWar(island, pixels, size, paint);

            var texture = new Texture2D(size, size, TextureFormat.RGBA32, true)
            {
                name = "TerrainColor",
                wrapMode = TextureWrapMode.Clamp,
                anisoLevel = 4,
            };
            texture.SetPixels32(pixels);
            texture.Apply(true, true);
            return texture;
        }

        /// <summary>
        /// Dirt roads, the trampled grey ground of the shelled villages, and the black of craters,
        /// painted over the terrain colours. Each is gathered into a mask first, so that overlaps
        /// do not darken twice.
        /// </summary>
        static void PaintWar(Island island, Color32[] pixels, int size, TerrainPaint paint)
        {
            HeightMap map = island.Map;
            float metres = map.Size / size;
            var road = new float[size * size];
            var burn = new float[size * size];
            var rim = new float[size * size];

            void Stamp(float[] mask, float x, float z, float radius, float soft)
            {
                int cx = Mathf.RoundToInt((x + map.HalfSize) / metres - 0.5f), cy = Mathf.RoundToInt((z + map.HalfSize) / metres - 0.5f);
                int reach = Mathf.CeilToInt((radius + soft) / metres);
                for (int py = Mathf.Max(0, cy - reach); py <= Mathf.Min(size - 1, cy + reach); py++)
                for (int px = Mathf.Max(0, cx - reach); px <= Mathf.Min(size - 1, cx + reach); px++)
                {
                    float dx = (px - cx) * metres, dz = (py - cy) * metres;
                    float weight = Mathf.InverseLerp(radius + soft, radius, Mathf.Sqrt(dx * dx + dz * dz));
                    int index = py * size + px;
                    if (weight > mask[index]) mask[index] = weight;
                }
            }

            foreach (RoadSpec segment in island.Roads)
            {
                Vector3 path = segment.To - segment.From;
                int steps = Mathf.Max(1, Mathf.CeilToInt(path.magnitude / metres));
                for (int i = 0; i <= steps; i++)
                {
                    Vector3 point = segment.From + path * (i / (float)steps);
                    // A track wanders a little; it is not laid with a ruler.
                    float sway = (Mathf.PerlinNoise(point.x * 0.01f + 5f, point.z * 0.01f + 9f) - 0.5f) * 14f;
                    Vector3 side = new Vector3(-path.z, 0f, path.x).normalized * sway * Mathf.Sin(Mathf.PI * i / steps);
                    Stamp(road, point.x + side.x, point.z + side.z, 1.8f, 2.6f);
                }
            }
            foreach (Vector3 village in island.Villages) Stamp(road, village.x, village.z, 34f, 55f);
            foreach (RuinSpec ruin in island.Ruins) Stamp(burn, ruin.Base.x, ruin.Base.z, ruin.Width * 0.6f, 7f);
            foreach (WreckSpec wreck in island.Wrecks) Stamp(burn, wreck.Base.x, wreck.Base.z, 3.5f, 5f);
            foreach (CraterSpec crater in island.Craters)
            {
                Stamp(burn, crater.X, crater.Z, crater.Radius * 0.75f, crater.Radius * 0.7f);
                Stamp(rim, crater.X, crater.Z, crater.Radius * 1.5f, crater.Radius * 0.8f);
            }

            Color dirt = Color.Lerp(paint.GroundDry, new Color(0.34f, 0.28f, 0.21f), 0.65f);
            Color thrown = Color.Lerp(paint.Sand, new Color(0.50f, 0.42f, 0.32f), 0.6f);
            var soot = new Color(0.085f, 0.08f, 0.075f);
            for (int py = 0; py < size; py++)
            for (int px = 0; px < size; px++)
            {
                int index = py * size + px;
                if (road[index] <= 0f && burn[index] <= 0f && rim[index] <= 0f) continue;
                float x = -map.HalfSize + (px + 0.5f) * metres, z = -map.HalfSize + (py + 0.5f) * metres;
                if (map.Height(x, z) < 0.6f) continue;
                float grain = 0.88f + Mathf.PerlinNoise(x * 0.45f + 3f, z * 0.45f + 8f) * 0.24f;
                Color color = pixels[index];
                color = Color.Lerp(color, dirt * grain, road[index] * 0.6f);
                // Earth thrown out of a crater lies in a paler ring around the black of it.
                color = Color.Lerp(color, thrown * grain, rim[index] * (1f - burn[index]) * 0.7f);
                color = Color.Lerp(color, soot * grain, burn[index] * 0.85f);
                color.a = 1f;
                pixels[index] = color;
            }
        }

        static Mesh BuildSeaMesh()
        {
            const float half = 4000f;
            var mesh = new Mesh { name = "Sea" };
            mesh.vertices = new[]
            {
                new Vector3(-half, Island.WaterLevel, -half), new Vector3(-half, Island.WaterLevel, half),
                new Vector3(half, Island.WaterLevel, half), new Vector3(half, Island.WaterLevel, -half),
            };
            mesh.normals = new[] { Vector3.up, Vector3.up, Vector3.up, Vector3.up };
            mesh.uv = new[] { Vector2.zero, Vector2.up, Vector2.one, Vector2.right };
            mesh.triangles = new[] { 0, 1, 2, 0, 2, 3 };
            mesh.RecalculateBounds();
            return mesh;
        }

        static Mesh BuildTreesMesh(Island island)
        {
            var builder = new MeshBuilder();
            for (int i = 0; i < island.Trees.Count; i++)
            {
                TreeSpec tree = island.Trees[i];
                if (tree.Burnt)
                {
                    // A black pole and two stubs of branch.
                    float pole = tree.Height * 0.75f;
                    builder.Cylinder(tree.Base - Vector3.up * 0.5f, tree.Radius * 0.15f, tree.Radius * 0.05f, pole + 0.5f, 5, Palette.Soot);
                    Quaternion lean = Quaternion.Euler(0f, i * 73f, 52f);
                    builder.Box(tree.Base + Vector3.up * pole * 0.55f + lean * Vector3.up * 0.9f, new Vector3(0.14f, 1.9f, 0.14f), lean, Palette.Soot);
                    lean = Quaternion.Euler(0f, i * 73f + 160f, 44f);
                    builder.Box(tree.Base + Vector3.up * pole * 0.72f + lean * Vector3.up * 0.7f, new Vector3(0.11f, 1.4f, 0.11f), lean, Palette.Soot);
                    continue;
                }
                int leaf = i % 3 == 0 || island.Biome != Biome.Temperate ? Palette.LeafDark : Palette.Leaf;
                float trunk = tree.Height * 0.25f;
                builder.Cylinder(tree.Base - Vector3.up * 0.5f, tree.Radius * 0.16f, tree.Radius * 0.14f,
                    trunk + 0.5f, 5, Palette.Trunk);
                builder.Cylinder(tree.Base + Vector3.up * trunk, tree.Radius, 0f, tree.Height * 0.5f, 6, leaf);
                builder.Cylinder(tree.Base + Vector3.up * (trunk + tree.Height * 0.3f), tree.Radius * 0.72f, 0f,
                    tree.Height * 0.45f, 6, leaf);
            }
            return builder.Build("Trees");
        }

        static Mesh BuildPadMesh(Island island)
        {
            var builder = new MeshBuilder();
            Vector3 center = island.Spawn;
            // The top sits a few centimetres above the terrain the drone rests on.
            builder.Cylinder(center - Vector3.up * 1.5f, 7f, 7f, 1.52f, 24, Palette.Concrete);
            builder.Cylinder(center + Vector3.up * 0.02f, 5.2f, 5.2f, 0.02f, 24, Palette.Orange);
            builder.Cylinder(center + Vector3.up * 0.02f, 4.6f, 4.6f, 0.03f, 24, Palette.DarkGrey);
            // "H" mark.
            builder.Box(center + new Vector3(-1.3f, 0.06f, 0f), new Vector3(0.6f, 0.02f, 4.2f), Palette.White);
            builder.Box(center + new Vector3(1.3f, 0.06f, 0f), new Vector3(0.6f, 0.02f, 4.2f), Palette.White);
            builder.Box(center + new Vector3(0f, 0.06f, 0f), new Vector3(2.6f, 0.02f, 0.6f), Palette.White);
            return builder.Build("Pad");
        }
    }
}
