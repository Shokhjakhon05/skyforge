using Skyforge.Core;
using UnityEngine;

namespace Skyforge.World
{
    /// <summary>The towns, camps and boulders the island placed, put into the scene from the baked models.</summary>
    public static class PropsView
    {
        public static void Build(Island island, Transform parent)
        {
            Transform root = new GameObject("Props").transform;
            root.SetParent(parent, false);
            for (int i = 0; i < island.Props.Count; i++)
            {
                PropSpec prop = island.Props[i];
                string name = Island.PropModels[prop.Model];
                Model model = ModelLibrary.Get(name);
                if (model == null) continue;
                bool house = name.StartsWith("b_") || name == "apartment" || name == "warehouse" || name == "fortress";
                bool rock = name.StartsWith("rock");
                bool big = house || rock || name.StartsWith("palm") || name.EndsWith("tower") || name.StartsWith("container") || name == "tent";
                // One house in three has burnt; the rest only look lived in.
                Transform body = model.Spawn(root, rock ? model.Stone : house && i % 3 == 0 ? model.Scorched : model.Scenery, big);
                float ground = prop.Base.y;
                if (house)
                {
                    // Stood on the lowest corner of its plot, so no side of it hangs in the air.
                    float half = Mathf.Max(model.Bounds.extents.x, model.Bounds.extents.z) * prop.Scale * 0.8f;
                    for (int k = 0; k < 4; k++)
                        ground = Mathf.Min(ground, island.Map.Height(prop.Base.x + (k % 2 == 0 ? half : -half), prop.Base.z + (k < 2 ? half : -half)));
                    ground -= 0.15f;
                }
                else if (rock) ground -= model.Bounds.size.y * prop.Scale * 0.18f;
                else ground -= 0.08f;
                Quaternion turn = Quaternion.Euler(0f, prop.YawDeg, 0f);
                if (!house && !name.EndsWith("tower"))
                {
                    // Small things lie on the slope they stand on.
                    Vector3 normal = island.Map.Normal(prop.Base.x, prop.Base.z);
                    turn = Quaternion.FromToRotation(Vector3.up, Vector3.Slerp(Vector3.up, normal, rock ? 0.4f : 0.9f)) * turn;
                }
                body.SetPositionAndRotation(new Vector3(prop.Base.x, ground, prop.Base.z), turn);
                body.localScale = Vector3.one * prop.Scale;
            }
        }
    }

    /// <summary>What a kill left behind, shown as a crate under a pillar of light that can be seen from the air.</summary>
    public sealed class PickupsView
    {
        sealed class Item
        {
            public int Id;
            public GameObject Root;
            public Transform Crate;
            public MeshRenderer Beam;
            public bool Seen;
        }

        static readonly Color[] Colours =
        {
            new Color(0.25f, 1f, 0.4f, 0.55f), new Color(1f, 0.55f, 0.12f, 0.55f), new Color(1f, 0.85f, 0.25f, 0.55f),
        };

        readonly Transform _root;
        readonly System.Collections.Generic.List<Item> _items = new System.Collections.Generic.List<Item>();
        readonly Material _beam;
        readonly Mesh[] _beamMeshes = new Mesh[3];

        public PickupsView(Transform parent)
        {
            _root = new GameObject("Pickups").transform;
            _root.SetParent(parent, false);
            _beam = GameMaterials.Particle(BeamTexture(), true) ?? GameMaterials.Unlit(Color.white);
            for (int i = 0; i < 3; i++) _beamMeshes[i] = BeamMesh(Colours[i]);
        }

        static Texture2D BeamTexture()
        {
            var texture = new Texture2D(16, 64, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
            var pixels = new Color32[16 * 64];
            for (int y = 0; y < 64; y++)
            for (int x = 0; x < 16; x++)
            {
                float across = 1f - Mathf.Abs(x - 7.5f) / 7.5f, up = 1f - y / 63f;
                pixels[y * 16 + x] = new Color(1f, 1f, 1f, across * across * Mathf.Sqrt(up));
            }
            texture.SetPixels32(pixels);
            texture.Apply();
            return texture;
        }

        /// <summary>Two upright quads crossed, so the pillar has width from every side.</summary>
        static Mesh BeamMesh(Color colour)
        {
            const float half = 2.4f, tall = 46f;
            var vertices = new Vector3[8];
            var uv = new Vector2[8];
            var colors = new Color[8];
            for (int q = 0; q < 2; q++)
            {
                Vector3 side = q == 0 ? Vector3.right : Vector3.forward;
                vertices[q * 4] = -side * half;
                vertices[q * 4 + 1] = side * half;
                vertices[q * 4 + 2] = side * half + Vector3.up * tall;
                vertices[q * 4 + 3] = -side * half + Vector3.up * tall;
                uv[q * 4] = new Vector2(0f, 0f);
                uv[q * 4 + 1] = new Vector2(1f, 0f);
                uv[q * 4 + 2] = new Vector2(1f, 1f);
                uv[q * 4 + 3] = new Vector2(0f, 1f);
            }
            for (int i = 0; i < 8; i++) colors[i] = colour;
            var mesh = new Mesh { name = "Beam" };
            mesh.vertices = vertices;
            mesh.uv = uv;
            mesh.colors = colors;
            mesh.triangles = new[] { 0, 2, 1, 0, 3, 2, 4, 6, 5, 4, 7, 6 };
            mesh.RecalculateBounds();
            return mesh;
        }

        Item Make(Pickup pickup)
        {
            var item = new Item { Id = pickup.Id, Root = new GameObject("Pickup") };
            item.Root.transform.SetParent(_root, false);
            item.Root.transform.position = pickup.Position;
            Model model = ModelLibrary.Get("crate");
            if (model != null)
            {
                item.Crate = model.Spawn(item.Root.transform, model.Tinted(Colours[(int)pickup.Type] * 0.9f), false);
                item.Crate.localScale = Vector3.one * 2.2f;
            }
            var beam = new GameObject("Beam");
            beam.transform.SetParent(item.Root.transform, false);
            beam.AddComponent<MeshFilter>().sharedMesh = _beamMeshes[(int)pickup.Type];
            item.Beam = beam.AddComponent<MeshRenderer>();
            item.Beam.sharedMaterial = _beam;
            item.Beam.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            return item;
        }

        public void Update(MissionRun run)
        {
            foreach (Item item in _items) item.Seen = false;
            if (run != null)
            {
                foreach (Pickup pickup in run.Pickups)
                {
                    Item item = _items.Find(i => i.Id == pickup.Id);
                    if (item == null) _items.Add(item = Make(pickup));
                    item.Seen = true;
                    if (item.Crate != null)
                    {
                        item.Crate.localPosition = Vector3.up * (1.4f + 0.35f * Mathf.Sin(Time.time * 2.4f + pickup.Id));
                        item.Crate.localRotation = Quaternion.Euler(0f, Time.time * 70f + pickup.Id * 40f, 0f);
                    }
                    // The last seconds it blinks: take it or lose it.
                    bool shown = pickup.Left > 6f || Mathf.Repeat(Time.time * 4f, 1f) < 0.6f;
                    if (item.Beam.enabled != shown) item.Beam.enabled = shown;
                }
            }
            for (int i = _items.Count - 1; i >= 0; i--)
            {
                if (_items[i].Seen) continue;
                Object.Destroy(_items[i].Root);
                _items.RemoveAt(i);
            }
        }

        public void Clear() => Update(null);
    }
}
