using Skyforge.Core;
using UnityEngine;
using UnityEngine.Rendering;

namespace Skyforge.World
{
    /// <summary>Turns an <see cref="Island"/> into things to look at: terrain, sea, trees, pad, sky.</summary>
    public static class IslandView
    {
        public static readonly Color HorizonColor = new Color(0.70f, 0.80f, 0.90f);

        public static void Build(Island island, Transform parent)
        {
            CreateObject("Terrain", parent, BuildTerrainMesh(island.Map),
                GameMaterials.LitTextured(BuildTerrainTexture(island.Map)), true);
            CreateObject("Sea", parent, BuildSeaMesh(), GameMaterials.Lit(new Color(0.06f, 0.25f, 0.38f), 0.82f), false);
            CreateObject("Trees", parent, BuildTreesMesh(island), GameMaterials.PaletteLit, true);
            CreateObject("Pad", parent, BuildPadMesh(island), GameMaterials.PaletteLit, false);
            SetUpSkyAndLight(parent);
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
        static Texture2D BuildTerrainTexture(HeightMap map)
        {
            const int size = 1024;
            var sandWet = new Color(0.55f, 0.50f, 0.36f);
            var sand = new Color(0.80f, 0.74f, 0.54f);
            var grass = new Color(0.36f, 0.52f, 0.24f);
            var grassDry = new Color(0.50f, 0.54f, 0.28f);
            var rock = new Color(0.42f, 0.40f, 0.38f);
            var snow = new Color(0.93f, 0.95f, 0.97f);
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
                    color = Color.Lerp(color, rock, Mathf.InverseLerp(0.16f, 0.30f, steep));
                    color = Color.Lerp(color, rock, Mathf.InverseLerp(80f, 105f, height + patch * 12f));
                    color = Color.Lerp(color, snow,
                        Mathf.InverseLerp(118f, 132f, height + patch * 10f) * Mathf.InverseLerp(0.45f, 0.25f, steep));
                }
                color *= 0.90f + grain * 0.20f;
                color.a = 1f;
                pixels[py * size + px] = color;
            }

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
                int leaf = i % 3 == 0 ? Palette.LeafDark : Palette.Leaf;
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

        static void SetUpSkyAndLight(Transform parent)
        {
            var sunObject = new GameObject("Sun");
            sunObject.transform.SetParent(parent, false);
            sunObject.transform.rotation = Quaternion.Euler(38f, -40f, 0f);
            var sun = sunObject.AddComponent<Light>();
            sun.type = LightType.Directional;
            sun.color = new Color(1f, 0.95f, 0.85f);
            sun.intensity = 1.35f;
            sun.shadows = LightShadows.Soft;
            sun.shadowStrength = 0.8f;
            RenderSettings.sun = sun;

            Material sky = GameMaterials.Sky;
            if (sky != null)
            {
                sky.SetColor("_GroundColor", HorizonColor);
                RenderSettings.skybox = sky;
            }

            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = new Color(0.52f, 0.62f, 0.78f);
            RenderSettings.ambientEquatorColor = new Color(0.46f, 0.50f, 0.52f);
            RenderSettings.ambientGroundColor = new Color(0.26f, 0.25f, 0.22f);

            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.Linear;
            RenderSettings.fogColor = HorizonColor;
            RenderSettings.fogStartDistance = 250f;
            RenderSettings.fogEndDistance = 2600f;
        }
    }
}
