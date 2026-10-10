using Skyforge.Core;
using UnityEngine;

namespace Skyforge.UI
{
    /// <summary>
    /// The region as a staff map: dark water, land shaded by height with contour lines, the roads
    /// and the shelled villages, under a grid. Drawn once when a region is loaded.
    /// </summary>
    public static class TacticalMap
    {
        const int Size = 256;

        public static Texture2D Build(Island island)
        {
            HeightMap map = island.Map;
            var pixels = new Color32[Size * Size];
            var water = new Color(0.020f, 0.060f, 0.095f);
            var shore = new Color(0.05f, 0.20f, 0.26f);
            var low = new Color(0.06f, 0.16f, 0.17f);
            var high = new Color(0.20f, 0.33f, 0.32f);
            var line = new Color(0.30f, 0.86f, 1f);
            float span = map.HalfSize * 2f;

            for (int y = 0; y < Size; y++)
            for (int x = 0; x < Size; x++)
            {
                float wx = ((x + 0.5f) / Size - 0.5f) * span, wz = ((y + 0.5f) / Size - 0.5f) * span;
                float height = map.Height(wx, wz);
                Color color;
                if (height < Island.WaterLevel)
                {
                    color = Color.Lerp(shore, water, Mathf.Clamp01(-height / 6f));
                }
                else
                {
                    color = Color.Lerp(low, high, Mathf.Clamp01(height / 150f));
                    // A contour every twenty metres: where the height crosses a multiple within this pixel.
                    float step = span / Size;
                    float next = map.Height(wx + step, wz), up = map.Height(wx, wz + step);
                    if (Mathf.Floor(height / 20f) != Mathf.Floor(next / 20f) || Mathf.Floor(height / 20f) != Mathf.Floor(up / 20f))
                        color = Color.Lerp(color, line, 0.30f);
                    // And the waterline, drawn harder.
                    if (next < Island.WaterLevel || up < Island.WaterLevel) color = Color.Lerp(color, line, 0.75f);
                }
                if (x % 32 == 0 || y % 32 == 0) color = Color.Lerp(color, line, 0.10f);
                pixels[y * Size + x] = color;
            }

            void Dot(Vector3 world, int radius, Color color, float strength)
            {
                int cx = Mathf.RoundToInt((world.x / span + 0.5f) * Size), cy = Mathf.RoundToInt((world.z / span + 0.5f) * Size);
                for (int dy = -radius; dy <= radius; dy++)
                for (int dx = -radius; dx <= radius; dx++)
                {
                    int px = cx + dx, py = cy + dy;
                    if (px < 0 || py < 0 || px >= Size || py >= Size || dx * dx + dy * dy > radius * radius) continue;
                    pixels[py * Size + px] = Color.Lerp(pixels[py * Size + px], color, strength);
                }
            }

            var road = new Color(0.62f, 0.60f, 0.50f);
            foreach (RoadSpec stretch in island.Roads)
            {
                float length = Vector3.Distance(stretch.From, stretch.To);
                int steps = Mathf.Max(1, Mathf.CeilToInt(length / (span / Size) * 1.5f));
                for (int i = 0; i <= steps; i++) Dot(Vector3.Lerp(stretch.From, stretch.To, i / (float)steps), 0, road, 0.6f);
            }
            foreach (Vector3 village in island.Villages) Dot(village, 3, new Color(0.75f, 0.70f, 0.60f), 0.55f);

            var texture = new Texture2D(Size, Size, TextureFormat.RGBA32, false)
            {
                name = "TacticalMap", wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear,
            };
            texture.SetPixels32(pixels);
            texture.Apply(false, true);
            return texture;
        }
    }
}
