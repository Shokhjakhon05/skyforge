using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Skyforge.World
{
    /// <summary>Colours shared by all generated props; a mesh picks one through its UV.</summary>
    public static class Palette
    {
        public const int Graphite = 0, Orange = 1, LightGrey = 2, DarkGrey = 3, Red = 4, Green = 5,
            Leaf = 6, LeafDark = 7, Trunk = 8, Concrete = 9, White = 10, Lens = 11;
        const int Size = 16;

        static readonly Color32[] Colors =
        {
            new Color32(38, 42, 48, 255), new Color32(240, 125, 30, 255), new Color32(200, 205, 210, 255),
            new Color32(70, 76, 84, 255), new Color32(225, 40, 40, 255), new Color32(50, 215, 90, 255),
            new Color32(62, 118, 58, 255), new Color32(40, 88, 50, 255), new Color32(92, 68, 48, 255),
            new Color32(128, 132, 136, 255), new Color32(238, 238, 238, 255), new Color32(18, 22, 30, 255),
            new Color32(255, 0, 255, 255), new Color32(255, 0, 255, 255), new Color32(255, 0, 255, 255),
            new Color32(255, 0, 255, 255),
        };

        public static Vector2 Uv(int index) => new Vector2((index + 0.5f) / Size, 0.5f);

        public static Texture2D CreateTexture()
        {
            var texture = new Texture2D(Size, 1, TextureFormat.RGBA32, false)
            {
                name = "Palette",
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp,
            };
            texture.SetPixels32(Colors);
            texture.Apply(false, true);
            return texture;
        }
    }

    /// <summary>
    /// Builds flat-shaded meshes out of simple solids. Faces are oriented by an outward hint,
    /// so callers never have to think about winding order.
    /// </summary>
    public sealed class MeshBuilder
    {
        readonly List<Vector3> _vertices = new List<Vector3>();
        readonly List<Vector3> _normals = new List<Vector3>();
        readonly List<Vector2> _uvs = new List<Vector2>();
        readonly List<int> _triangles = new List<int>();

        public int VertexCount => _vertices.Count;

        public void Triangle(Vector3 a, Vector3 b, Vector3 c, Vector3 outward, int color)
        {
            Vector3 normal = Vector3.Cross(b - a, c - a);
            if (normal.sqrMagnitude < 1e-12f) return;
            if (Vector3.Dot(normal, outward) < 0f)
            {
                (b, c) = (c, b);
                normal = -normal;
            }
            normal.Normalize();

            int start = _vertices.Count;
            _vertices.Add(a); _vertices.Add(b); _vertices.Add(c);
            _normals.Add(normal); _normals.Add(normal); _normals.Add(normal);
            Vector2 uv = Palette.Uv(color);
            _uvs.Add(uv); _uvs.Add(uv); _uvs.Add(uv);
            _triangles.Add(start); _triangles.Add(start + 1); _triangles.Add(start + 2);
        }

        public void Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector3 outward, int color)
        {
            Triangle(a, b, c, outward, color);
            Triangle(a, c, d, outward, color);
        }

        public void Box(Vector3 center, Vector3 size, Quaternion rotation, int color)
        {
            Vector3 h = size * 0.5f;
            Vector3 P(float x, float y, float z) => center + rotation * new Vector3(x * h.x, y * h.y, z * h.z);
            Vector3 N(float x, float y, float z) => rotation * new Vector3(x, y, z);

            Quad(P(1, -1, -1), P(1, 1, -1), P(1, 1, 1), P(1, -1, 1), N(1, 0, 0), color);
            Quad(P(-1, -1, -1), P(-1, 1, -1), P(-1, 1, 1), P(-1, -1, 1), N(-1, 0, 0), color);
            Quad(P(-1, 1, -1), P(1, 1, -1), P(1, 1, 1), P(-1, 1, 1), N(0, 1, 0), color);
            Quad(P(-1, -1, -1), P(1, -1, -1), P(1, -1, 1), P(-1, -1, 1), N(0, -1, 0), color);
            Quad(P(-1, -1, 1), P(1, -1, 1), P(1, 1, 1), P(-1, 1, 1), N(0, 0, 1), color);
            Quad(P(-1, -1, -1), P(1, -1, -1), P(1, 1, -1), P(-1, 1, -1), N(0, 0, -1), color);
        }

        public void Box(Vector3 center, Vector3 size, int color) => Box(center, size, Quaternion.identity, color);

        /// <summary>Upright cylinder, or a cone when <paramref name="topRadius"/> is zero.</summary>
        public void Cylinder(Vector3 baseCenter, float radius, float topRadius, float height, int sides, int color)
        {
            Vector3 top = baseCenter + Vector3.up * height;
            for (int i = 0; i < sides; i++)
            {
                float a0 = i * 2f * Mathf.PI / sides, a1 = (i + 1) * 2f * Mathf.PI / sides;
                var d0 = new Vector3(Mathf.Cos(a0), 0f, Mathf.Sin(a0));
                var d1 = new Vector3(Mathf.Cos(a1), 0f, Mathf.Sin(a1));
                Vector3 outward = d0 + d1 + Vector3.up * 0.01f;

                Vector3 b0 = baseCenter + d0 * radius, b1 = baseCenter + d1 * radius;
                Vector3 t0 = top + d0 * topRadius, t1 = top + d1 * topRadius;
                Triangle(b0, b1, t1, outward, color);
                if (topRadius > 0f)
                {
                    Triangle(b0, t1, t0, outward, color);
                    Triangle(top, t0, t1, Vector3.up, color);
                }
                Triangle(baseCenter, b0, b1, Vector3.down, color);
            }
        }

        /// <summary>Ring lying in the XY plane, so its axis is Z.</summary>
        public void Torus(float radius, float tube, int segments, int sides, int color)
        {
            for (int i = 0; i < segments; i++)
            for (int j = 0; j < sides; j++)
            {
                Vector3 a = TorusPoint(radius, tube, i, j, segments, sides);
                Vector3 b = TorusPoint(radius, tube, i + 1, j, segments, sides);
                Vector3 c = TorusPoint(radius, tube, i + 1, j + 1, segments, sides);
                Vector3 d = TorusPoint(radius, tube, i, j + 1, segments, sides);
                float mid = (i + 0.5f) * 2f * Mathf.PI / segments;
                var tubeCenter = new Vector3(Mathf.Cos(mid) * radius, Mathf.Sin(mid) * radius, 0f);
                Quad(a, b, c, d, (a + b + c + d) * 0.25f - tubeCenter, color);
            }
        }

        static Vector3 TorusPoint(float radius, float tube, int i, int j, int segments, int sides)
        {
            float u = i * 2f * Mathf.PI / segments, v = j * 2f * Mathf.PI / sides;
            float r = radius + tube * Mathf.Cos(v);
            return new Vector3(Mathf.Cos(u) * r, Mathf.Sin(u) * r, tube * Mathf.Sin(v));
        }

        public Mesh Build(string name)
        {
            var mesh = new Mesh { name = name };
            if (_vertices.Count > 65000) mesh.indexFormat = IndexFormat.UInt32;
            mesh.SetVertices(_vertices);
            mesh.SetNormals(_normals);
            mesh.SetUVs(0, _uvs);
            mesh.SetTriangles(_triangles, 0);
            mesh.RecalculateBounds();
            mesh.UploadMeshData(true);
            return mesh;
        }
    }
}
