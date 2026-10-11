using System.Collections.Generic;
using UnityEngine;

namespace Skyforge.Core
{
    /// <summary>Upright cylinder standing on the ground: a tree, a tower, a house.</summary>
    public struct Obstacle
    {
        public float X;
        public float Z;
        public float Radius;
        public float Top;
        /// <summary>A tree: an aircraft breaks on it, a tank drives through it.</summary>
        public bool Soft;
    }

    /// <summary>Obstacles in a coarse grid, so a collision check looks at a handful of them.</summary>
    public sealed class ObstacleField
    {
        const float CellSize = 32f;
        readonly Dictionary<long, List<Obstacle>> _cells = new Dictionary<long, List<Obstacle>>();
        float _largestRadius;

        public int Count { get; private set; }

        public void Add(Obstacle obstacle)
        {
            long key = Key(Cell(obstacle.X), Cell(obstacle.Z));
            if (!_cells.TryGetValue(key, out var list))
                _cells[key] = list = new List<Obstacle>();
            list.Add(obstacle);
            _largestRadius = Mathf.Max(_largestRadius, obstacle.Radius);
            Count++;
        }

        public bool Hits(Vector3 position, float radius, bool solidOnly = false)
        {
            float reach = radius + _largestRadius;
            int minX = Cell(position.x - reach), maxX = Cell(position.x + reach);
            int minZ = Cell(position.z - reach), maxZ = Cell(position.z + reach);
            for (int cz = minZ; cz <= maxZ; cz++)
            for (int cx = minX; cx <= maxX; cx++)
            {
                if (!_cells.TryGetValue(Key(cx, cz), out var list)) continue;
                for (int i = 0; i < list.Count; i++)
                {
                    Obstacle o = list[i];
                    if (position.y - radius > o.Top || (solidOnly && o.Soft)) continue;
                    float dx = position.x - o.X, dz = position.z - o.Z;
                    float limit = o.Radius + radius;
                    if (dx * dx + dz * dz < limit * limit) return true;
                }
            }
            return false;
        }

        static int Cell(float value) => Mathf.FloorToInt(value / CellSize);
        static long Key(int cx, int cz) => ((long)cx << 32) ^ (uint)cz;
    }
}
