using System;
using UnityEngine;

namespace Skyforge.Core
{
    /// <summary>
    /// Square grid of terrain heights centred on the world origin. The terrain mesh and the
    /// collision checks both read it, so what the player sees is what the drone hits.
    /// </summary>
    public sealed class HeightMap
    {
        public readonly int Resolution;
        public readonly float Size;
        readonly float[] _heights;

        public HeightMap(int resolution, float size)
        {
            if (resolution < 2) throw new ArgumentOutOfRangeException(nameof(resolution));
            Resolution = resolution;
            Size = size;
            _heights = new float[resolution * resolution];
        }

        public float CellSize => Size / (Resolution - 1);
        public float HalfSize => Size * 0.5f;

        public float this[int ix, int iz]
        {
            get => _heights[iz * Resolution + ix];
            set => _heights[iz * Resolution + ix] = value;
        }

        /// <summary>World position of a grid node on one axis.</summary>
        public float NodeCoordinate(int index) => -HalfSize + index * CellSize;

        /// <summary>Bilinear height at a world position; positions outside the grid use the edge.</summary>
        public float Height(float x, float z)
        {
            float gx = Mathf.Clamp((x + HalfSize) / CellSize, 0f, Resolution - 1);
            float gz = Mathf.Clamp((z + HalfSize) / CellSize, 0f, Resolution - 1);
            int ix = Mathf.Min((int)gx, Resolution - 2);
            int iz = Mathf.Min((int)gz, Resolution - 2);
            float tx = gx - ix;
            float tz = gz - iz;
            float near = Mathf.Lerp(this[ix, iz], this[ix + 1, iz], tx);
            float far = Mathf.Lerp(this[ix, iz + 1], this[ix + 1, iz + 1], tx);
            return Mathf.Lerp(near, far, tz);
        }

        public Vector3 Normal(float x, float z)
        {
            float d = CellSize;
            float dx = Height(x + d, z) - Height(x - d, z);
            float dz = Height(x, z + d) - Height(x, z - d);
            return new Vector3(-dx, 2f * d, -dz).normalized;
        }

        /// <summary>0 on flat ground, 1 on a vertical wall.</summary>
        public float Steepness(float x, float z) => 1f - Normal(x, z).y;
    }
}
