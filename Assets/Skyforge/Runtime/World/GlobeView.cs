using System.Collections.Generic;
using Skyforge.Core;
using UnityEngine;
using UnityEngine.Rendering;

namespace Skyforge.World
{
    /// <summary>
    /// The world map: a painted globe with a marker for every region. It hangs far above the
    /// island so both can exist at once; the camera simply goes up to it.
    /// </summary>
    public sealed class GlobeView
    {
        public const float Radius = 10f;
        public static readonly Vector3 Center = new Vector3(0f, 4000f, 0f);

        readonly Transform _globe;
        readonly List<Renderer> _markers = new List<Renderer>();
        readonly Material _open, _locked, _selected;
        Quaternion _target;
        bool _settling;

        public GlobeView(Transform parent)
        {
            var obj = new GameObject("Globe");
            obj.transform.SetParent(parent, false);
            obj.transform.position = Center;
            _globe = obj.transform;
            obj.AddComponent<MeshFilter>().sharedMesh = BuildSphere(64, 32);
            var renderer = obj.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = GameMaterials.Globe(BuildSurface());
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;

            _open = GameMaterials.Unlit(Color.white);
            _locked = GameMaterials.Unlit(new Color(0.45f, 0.48f, 0.52f));
            _selected = GameMaterials.Unlit(new Color(1f, 0.62f, 0.10f));

            // Each region is a ring lying on the surface with a pin through its middle.
            var marker = new MeshBuilder();
            marker.Torus(0.55f, 0.05f, 28, 5, Palette.White);
            marker.Box(new Vector3(0f, 0f, 0.45f), new Vector3(0.12f, 0.12f, 0.9f), Palette.White);
            Mesh markerMesh = marker.Build("RegionMarker");

            foreach (RegionDefinition region in RegionCatalog.All)
            {
                Vector3 direction = RegionCatalog.OnGlobe(region.Latitude, region.Longitude);
                var pin = new GameObject("Marker_" + region.Id);
                pin.transform.SetParent(_globe, false);
                pin.transform.localPosition = direction * (Radius + 0.03f);
                // The marker's ring lies in its XY plane and its pin points along Z: aim Z away from the globe.
                pin.transform.localRotation = Quaternion.FromToRotation(Vector3.forward, direction);
                pin.AddComponent<MeshFilter>().sharedMesh = markerMesh;
                var pinRenderer = pin.AddComponent<MeshRenderer>();
                pinRenderer.shadowCastingMode = ShadowCastingMode.Off;
                _markers.Add(pinRenderer);
            }
            _target = _globe.rotation;
        }

        /// <summary>The rotation that brings a region to face a camera looking along +Z.</summary>
        static Quaternion Facing(RegionDefinition region)
        {
            return Quaternion.Euler(-region.Latitude * 0.6f, 0f, 0f) * Quaternion.Euler(0f, 180f - region.Longitude, 0f);
        }

        public void Refresh(int selected, System.Func<RegionDefinition, bool> isLocked)
        {
            for (int i = 0; i < _markers.Count; i++)
            {
                _markers[i].sharedMaterial =
                    i == selected ? _selected : isLocked(RegionCatalog.All[i]) ? _locked : _open;
                _markers[i].transform.localScale = Vector3.one * (i == selected ? 1.35f : 1f);
            }
        }

        /// <summary>Starts turning the globe to show a region.</summary>
        public void Focus(int region, bool instantly)
        {
            _target = Facing(RegionCatalog.All[region]);
            _settling = true;
            if (instantly) _globe.rotation = _target;
        }

        /// <summary>The player's finger turns the globe; this stops any automatic turning.</summary>
        public void Drag(Vector2 degrees)
        {
            _settling = false;
            _globe.rotation = Quaternion.Euler(degrees.y, -degrees.x, 0f) * _globe.rotation;
        }

        public void Update(float deltaTime)
        {
            if (!_settling) return;
            _globe.rotation = Quaternion.Slerp(_globe.rotation, _target, 1f - Mathf.Exp(-5f * deltaTime));
            if (Quaternion.Angle(_globe.rotation, _target) < 0.2f) _settling = false;
        }

        /// <summary>The region whose marker a ray hits, or -1.</summary>
        public int Pick(Ray ray)
        {
            // Ray against the sphere, then the nearest marker to the hit point.
            Vector3 offset = ray.origin - Center;
            float b = Vector3.Dot(offset, ray.direction);
            float c = offset.sqrMagnitude - Radius * Radius;
            float discriminant = b * b - c;
            if (discriminant < 0f) return -1;
            Vector3 hit = ray.origin + ray.direction * (-b - Mathf.Sqrt(discriminant));
            Vector3 local = Quaternion.Inverse(_globe.rotation) * (hit - Center).normalized;

            int best = -1;
            float bestDot = 0.965f;
            for (int i = 0; i < RegionCatalog.All.Count; i++)
            {
                RegionDefinition region = RegionCatalog.All[i];
                float dot = Vector3.Dot(local, RegionCatalog.OnGlobe(region.Latitude, region.Longitude));
                if (dot <= bestDot) continue;
                bestDot = dot;
                best = i;
            }
            return best;
        }

        static Mesh BuildSphere(int segments, int rings)
        {
            var vertices = new List<Vector3>();
            var normals = new List<Vector3>();
            var uvs = new List<Vector2>();
            var triangles = new List<int>();
            for (int r = 0; r <= rings; r++)
            for (int s = 0; s <= segments; s++)
            {
                float u = s / (float)segments, v = r / (float)rings;
                Vector3 direction = RegionCatalog.OnGlobe(v * 180f - 90f, u * 360f - 180f);
                vertices.Add(direction * Radius);
                normals.Add(direction);
                uvs.Add(new Vector2(u, v));
            }
            int row = segments + 1;
            for (int r = 0; r < rings; r++)
            for (int s = 0; s < segments; s++)
            {
                int a = r * row + s, b = a + 1, c = a + row, d = c + 1;
                // Wound so the outside faces out; checked against the normal below.
                AddOutward(triangles, vertices, a, c, d);
                AddOutward(triangles, vertices, a, d, b);
            }
            var mesh = new Mesh { name = "Globe" };
            mesh.SetVertices(vertices);
            mesh.SetNormals(normals);
            mesh.SetUVs(0, uvs);
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateBounds();
            return mesh;
        }

        static void AddOutward(List<int> triangles, List<Vector3> vertices, int a, int b, int c)
        {
            Vector3 normal = Vector3.Cross(vertices[b] - vertices[a], vertices[c] - vertices[a]);
            if (normal.sqrMagnitude < 1e-10f) return;
            if (Vector3.Dot(normal, vertices[a] + vertices[b] + vertices[c]) < 0f) (b, c) = (c, b);
            triangles.Add(a);
            triangles.Add(b);
            triangles.Add(c);
        }

        /// <summary>Paints oceans and continents; each region sits on land coloured by its biome.</summary>
        static Texture2D BuildSurface()
        {
            const int width = 1024, height = 512;
            var deep = new Color(0.03f, 0.09f, 0.20f);
            var shallow = new Color(0.08f, 0.26f, 0.40f);
            var green = new Color(0.25f, 0.42f, 0.22f);
            var sand = new Color(0.70f, 0.58f, 0.36f);
            var ice = new Color(0.90f, 0.93f, 0.96f);
            var rock = new Color(0.36f, 0.34f, 0.30f);

            // Regions get a continent each; a few more fill out the rest of the world.
            var lands = new List<Vector4>();
            foreach (RegionDefinition region in RegionCatalog.All)
            {
                Vector3 d = RegionCatalog.OnGlobe(region.Latitude, region.Longitude);
                lands.Add(new Vector4(d.x, d.y, d.z, 0.42f));
            }
            float[,] extra = { { -25f, -75f, 0.38f }, { -35f, 150f, 0.34f }, { 10f, -130f, 0.30f }, { 55f, -165f, 0.26f }, { -62f, 30f, 0.32f } };
            for (int i = 0; i < extra.GetLength(0); i++)
            {
                Vector3 d = RegionCatalog.OnGlobe(extra[i, 0], extra[i, 1]);
                lands.Add(new Vector4(d.x, d.y, d.z, extra[i, 2]));
            }

            var pixels = new Color32[width * height];
            for (int y = 0; y < height; y++)
            for (int x = 0; x < width; x++)
            {
                float latitude = (y + 0.5f) / height * 180f - 90f;
                Vector3 p = RegionCatalog.OnGlobe(latitude, (x + 0.5f) / width * 360f - 180f);
                // Three flat slices of noise stand in for noise on a sphere: no seam, no pinch at the poles.
                float noise = (Mathf.PerlinNoise(p.x * 3f + 11f, p.y * 3f + 7f) + Mathf.PerlinNoise(p.y * 3f + 23f, p.z * 3f + 19f) +
                               Mathf.PerlinNoise(p.z * 3f + 37f, p.x * 3f + 31f)) / 3f;
                float detail = (Mathf.PerlinNoise(p.x * 11f + 5f, p.z * 11f + 3f) + Mathf.PerlinNoise(p.y * 11f + 9f, p.x * 11f + 2f)) * 0.5f;

                float land = -1f;
                foreach (Vector4 l in lands)
                {
                    float angle = Mathf.Acos(Mathf.Clamp(p.x * l.x + p.y * l.y + p.z * l.z, -1f, 1f));
                    land = Mathf.Max(land, 1f - angle / l.w);
                }
                land += (noise - 0.5f) * 1.1f + (detail - 0.5f) * 0.25f;

                Color color;
                if (land < 0f)
                {
                    color = Color.Lerp(shallow, deep, Mathf.Clamp01(-land * 4f));
                }
                else
                {
                    float cold = Mathf.InverseLerp(48f, 68f, Mathf.Abs(latitude) + (noise - 0.5f) * 20f);
                    float dry = Mathf.InverseLerp(30f, 12f, Mathf.Abs(latitude) + (detail - 0.5f) * 16f);
                    color = Color.Lerp(green, sand, dry);
                    color = Color.Lerp(color, rock, Mathf.Clamp01(land - 0.55f) * 1.5f);
                    color = Color.Lerp(color, ice, cold);
                    color *= 0.85f + detail * 0.3f;
                }
                if (Mathf.Abs(latitude) > 80f) color = Color.Lerp(color, ice, Mathf.InverseLerp(80f, 86f, Mathf.Abs(latitude)));
                color.a = 1f;
                pixels[y * width + x] = color;
            }

            var texture = new Texture2D(width, height, TextureFormat.RGBA32, true)
            {
                name = "GlobeSurface",
                wrapModeU = TextureWrapMode.Repeat,
                wrapModeV = TextureWrapMode.Clamp,
                anisoLevel = 4,
            };
            texture.SetPixels32(pixels);
            texture.Apply(true, true);
            return texture;
        }
    }
}
