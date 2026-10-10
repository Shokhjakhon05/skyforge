using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.Rendering;

namespace Skyforge.World
{
    /// <summary>
    /// One baked 3D model: a body and the parts that move on their own (rotors, a turret head),
    /// all painted from one small palette. The files are written by tools/gen_models.py.
    /// </summary>
    public sealed class Model
    {
        public string Name;
        public Mesh[] Meshes;
        public string[] PartNames;
        /// <summary>Where each part sits relative to the model's origin; its mesh is centred on this point.</summary>
        public Vector3[] Pivots;
        public Color32[] Palette;
        public Bounds Bounds;

        Material _plain, _burnt;

        /// <summary>The model in its own colours.</summary>
        public Material Plain => _plain != null ? _plain : _plain = Paint(Palette);

        /// <summary>The same model as a burnt-out wreck.</summary>
        public Material Burnt
        {
            get
            {
                if (_burnt != null) return _burnt;
                var colors = new Color32[Palette.Length];
                for (int i = 0; i < colors.Length; i++)
                {
                    // Soot with a memory of the paint, and rust where the paint was bright.
                    Color original = Palette[i];
                    float light = original.grayscale;
                    var soot = new Color(0.10f + light * 0.10f, 0.09f + light * 0.08f, 0.08f + light * 0.07f);
                    var rust = new Color(0.30f, 0.16f, 0.09f);
                    colors[i] = Color.Lerp(soot, rust, i % 3 == 0 ? 0.35f : 0.08f);
                }
                return _burnt = Paint(colors, GameMaterials.Matter.Burnt);
            }
        }

        /// <summary>
        /// The model with its greys, blacks and whites repainted in a body colour; anything that
        /// already had a colour of its own (lights, markings) keeps it.
        /// </summary>
        public Material Tinted(Color body)
        {
            var colors = new Color32[Palette.Length];
            for (int i = 0; i < colors.Length; i++)
            {
                Color original = Palette[i];
                Color.RGBToHSV(original, out _, out float saturation, out float value);
                colors[i] = saturation > 0.35f ? original : (Color)(body * Mathf.Lerp(0.6f, 1.25f, value));
                colors[i].a = 255;
            }
            return Paint(colors);
        }

        static Material Paint(Color32[] colors, GameMaterials.Matter matter = GameMaterials.Matter.Metal)
        {
            var texture = new Texture2D(colors.Length, 1, TextureFormat.RGBA32, false)
            {
                name = "ModelPalette", filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp,
            };
            texture.SetPixels32(colors);
            texture.Apply(false, true);
            return GameMaterials.Surface(texture, matter);
        }

        public int PartIndex(string name)
        {
            for (int i = 0; i < PartNames.Length; i++)
                if (PartNames[i] == name) return i;
            return -1;
        }

        /// <summary>
        /// Puts the model into the scene under a parent: one object for the body, one child per
        /// moving part. Returns the body; <paramref name="parts"/> receives every part by name.
        /// </summary>
        public Transform Spawn(Transform parent, Material material, bool castShadows, Dictionary<string, Transform> parts = null)
        {
            Transform body = null;
            for (int i = 0; i < Meshes.Length; i++)
            {
                var obj = new GameObject(i == 0 ? Name : Name + "_" + PartNames[i]);
                obj.transform.SetParent(i == 0 ? parent : body, false);
                obj.transform.localPosition = Pivots[i];
                obj.AddComponent<MeshFilter>().sharedMesh = Meshes[i];
                var renderer = obj.AddComponent<MeshRenderer>();
                renderer.sharedMaterial = material;
                renderer.shadowCastingMode = castShadows ? ShadowCastingMode.On : ShadowCastingMode.Off;
                if (i == 0) body = obj.transform;
                if (parts != null) parts[PartNames[i]] = obj.transform;
            }
            return body;
        }
    }

    /// <summary>Loads baked models from Resources/Models once and hands them out by name.</summary>
    public static class ModelLibrary
    {
        static readonly Dictionary<string, Model> Cache = new Dictionary<string, Model>();

        /// <summary>The model, or null if there is no such file or it cannot be read.</summary>
        public static Model Get(string name)
        {
            if (string.IsNullOrEmpty(name)) return null;
            if (Cache.TryGetValue(name, out Model cached)) return cached;
            Model model = null;
            var asset = Resources.Load<TextAsset>("Models/" + name);
            if (asset != null)
            {
                try
                {
                    model = Parse(name, asset.bytes);
                }
                catch (System.Exception e)
                {
                    Debug.LogError("[Skyforge] Model " + name + " is unreadable: " + e.Message);
                }
            }
            else
            {
                Debug.LogError("[Skyforge] Missing model " + name);
            }
            Cache[name] = model;
            return model;
        }

        /// <summary>
        /// File layout, little endian: "SFM1"; palette count (u16) and RGB bytes; part count (u16);
        /// per part a name (length byte + ASCII), pivot (3 floats), vertex and index counts (u32),
        /// vertices (position 3 floats, normal 3 signed bytes, palette index byte), indices (u32).
        /// </summary>
        static Model Parse(string name, byte[] data)
        {
            using (var reader = new BinaryReader(new MemoryStream(data)))
            {
                if (reader.ReadByte() != 'S' || reader.ReadByte() != 'F' || reader.ReadByte() != 'M' || reader.ReadByte() != '1')
                    throw new InvalidDataException("not a model file");

                int colors = reader.ReadUInt16();
                var palette = new Color32[colors];
                for (int i = 0; i < colors; i++) palette[i] = new Color32(reader.ReadByte(), reader.ReadByte(), reader.ReadByte(), 255);

                int parts = reader.ReadUInt16();
                var model = new Model
                {
                    Name = name, Palette = palette, Meshes = new Mesh[parts], PartNames = new string[parts], Pivots = new Vector3[parts],
                };
                bool first = true;
                for (int p = 0; p < parts; p++)
                {
                    int length = reader.ReadByte();
                    model.PartNames[p] = System.Text.Encoding.ASCII.GetString(reader.ReadBytes(length));
                    model.Pivots[p] = new Vector3(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle());
                    int vertexCount = (int)reader.ReadUInt32(), indexCount = (int)reader.ReadUInt32();

                    var vertices = new Vector3[vertexCount];
                    var normals = new Vector3[vertexCount];
                    var uvs = new Vector2[vertexCount];
                    for (int v = 0; v < vertexCount; v++)
                    {
                        vertices[v] = new Vector3(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle());
                        normals[v] = new Vector3(reader.ReadSByte() / 127f, reader.ReadSByte() / 127f, reader.ReadSByte() / 127f);
                        uvs[v] = new Vector2((reader.ReadByte() + 0.5f) / colors, 0.5f);
                    }
                    var indices = new int[indexCount];
                    for (int i = 0; i < indexCount; i++) indices[i] = (int)reader.ReadUInt32();

                    var mesh = new Mesh { name = name + "_" + model.PartNames[p] };
                    if (vertexCount > 65000) mesh.indexFormat = IndexFormat.UInt32;
                    mesh.vertices = vertices;
                    mesh.normals = normals;
                    mesh.uv = uvs;
                    mesh.triangles = indices;
                    mesh.RecalculateBounds();
                    mesh.UploadMeshData(true);
                    model.Meshes[p] = mesh;

                    var bounds = new Bounds(mesh.bounds.center + model.Pivots[p], mesh.bounds.size);
                    if (first) model.Bounds = bounds;
                    else model.Bounds.Encapsulate(bounds);
                    first = false;
                }
                return model;
            }
        }
    }
}
