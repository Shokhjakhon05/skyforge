using System.Collections.Generic;
using UnityEngine;

namespace Skyforge.Core
{
    /// <summary>
    /// Where a ground vehicle can go on an island: a coarse grid of drivable cells and the
    /// shortest way between two of them. Used to offer only armoured raids that can be driven,
    /// and by the test driver that drives them.
    /// </summary>
    public sealed class GroundNav
    {
        public const float Cell = 12f;
        readonly Island _island;
        readonly int _size;
        readonly bool[] _open;

        public GroundNav(Island island)
        {
            _island = island;
            _size = Mathf.CeilToInt(island.Map.HalfSize * 2f / Cell);
            _open = new bool[_size * _size];
            for (int z = 0; z < _size; z++)
            for (int x = 0; x < _size; x++)
            {
                Vector2 p = Centre(x, z);
                // A cell is open when its middle and the points around it can all be stood on.
                // Stricter than the vehicle itself, so a road found here has room to spare.
                bool open = true;
                for (int k = 0; open && k < 9; k++)
                    open = FlightSim.Drivable(island, p.x + (k % 3 - 1) * 5f, p.y + (k / 3 - 1) * 5f, FlightSim.GroundMaxSlope * 0.8f);
                _open[z * _size + x] = open;
            }
        }

        Vector2 Centre(int x, int z) => new Vector2((x + 0.5f) * Cell - _island.Map.HalfSize, (z + 0.5f) * Cell - _island.Map.HalfSize);
        int Index(float v) => Mathf.Clamp(Mathf.FloorToInt((v + _island.Map.HalfSize) / Cell), 0, _size - 1);

        /// <summary>The open cell nearest to a point, within a few cells of it; -1 if there is none.</summary>
        int Nearest(Vector3 point)
        {
            int cx = Index(point.x), cz = Index(point.z), best = -1;
            float bestDistance = float.MaxValue;
            for (int dz = -3; dz <= 3; dz++)
            for (int dx = -3; dx <= 3; dx++)
            {
                int x = cx + dx, z = cz + dz;
                if (x < 0 || z < 0 || x >= _size || z >= _size || !_open[z * _size + x]) continue;
                float d = dx * dx + dz * dz;
                if (d >= bestDistance) continue;
                bestDistance = d;
                best = z * _size + x;
            }
            return best;
        }

        /// <summary>Waypoints on the ground from one point to another, or null when no vehicle can make the trip.</summary>
        public List<Vector3> Path(Vector3 from, Vector3 to)
        {
            int start = Nearest(from), goal = Nearest(to);
            if (start < 0 || goal < 0) return null;
            var cost = new float[_open.Length];
            var came = new int[_open.Length];
            for (int i = 0; i < cost.Length; i++) { cost[i] = float.MaxValue; came[i] = -1; }
            var frontier = new SortedSet<(float, int)>();
            cost[start] = 0f;
            frontier.Add((0f, start));
            int gx = goal % _size, gz = goal / _size;
            while (frontier.Count > 0)
            {
                (float _, int current) = frontier.Min;
                frontier.Remove(frontier.Min);
                if (current == goal) break;
                int cx = current % _size, cz = current / _size;
                for (int dz = -1; dz <= 1; dz++)
                for (int dx = -1; dx <= 1; dx++)
                {
                    if (dx == 0 && dz == 0) continue;
                    int x = cx + dx, z = cz + dz;
                    if (x < 0 || z < 0 || x >= _size || z >= _size) continue;
                    int next = z * _size + x;
                    if (!_open[next]) continue;
                    // No cutting a corner between two closed cells.
                    if (dx != 0 && dz != 0 && (!_open[cz * _size + x] || !_open[z * _size + cx])) continue;
                    float step = cost[current] + (dx != 0 && dz != 0 ? 1.414f : 1f);
                    if (step >= cost[next]) continue;
                    if (cost[next] < float.MaxValue) frontier.Remove((cost[next] + Mathf.Sqrt((x - gx) * (x - gx) + (z - gz) * (z - gz)), next));
                    cost[next] = step;
                    came[next] = current;
                    frontier.Add((step + Mathf.Sqrt((x - gx) * (x - gx) + (z - gz) * (z - gz)), next));
                }
            }
            if (came[goal] < 0 && goal != start) return null;
            var path = new List<Vector3>();
            for (int at = goal; at >= 0; at = came[at])
            {
                Vector2 p = Centre(at % _size, at / _size);
                path.Add(new Vector3(p.x, _island.Map.Height(p.x, p.y), p.y));
                if (at == start) break;
            }
            path.Reverse();
            return path;
        }

        public static float Length(List<Vector3> path)
        {
            float total = 0f;
            for (int i = 1; i < path.Count; i++) total += Vector3.Distance(path[i - 1], path[i]);
            return total;
        }

        /// <summary>Whether the ground lets one point see another.</summary>
        public static bool Sees(HeightMap map, Vector3 from, Vector3 to)
        {
            float distance = Vector3.Distance(from, to);
            int steps = Mathf.Max(2, Mathf.CeilToInt(distance / 5f));
            for (int i = 1; i < steps; i++)
            {
                Vector3 p = Vector3.Lerp(from, to, i / (float)steps);
                if (map.Height(p.x, p.z) > p.y - 0.5f) return false;
            }
            return true;
        }
    }
}
