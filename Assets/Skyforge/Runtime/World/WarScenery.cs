using Skyforge.Core;
using UnityEngine;
using UnityEngine.Rendering;

namespace Skyforge.World
{
    /// <summary>
    /// What the fighting left behind, made visible: shelled houses, burnt-out vehicles, smoke over
    /// the villages. The island decides where; this decides what it looks like.
    /// </summary>
    public static class WarScenery
    {
        /// <summary>Models a wreck can be, by <see cref="WreckSpec.Kind"/>.</summary>
        static readonly string[] WreckModels = { "truck", "tank", "jeep", "humvee", "truck_cargo", "apc" };

        /// <summary>Ruins are built into meshes of this many houses, so that villages out of view are not drawn.</summary>
        const int RuinsPerMesh = 8;

        public static void Build(Island island, Transform parent, Effects effects)
        {
            for (int start = 0; start < island.Ruins.Count; start += RuinsPerMesh)
            {
                var builder = new MeshBuilder();
                for (int i = start; i < island.Ruins.Count && i < start + RuinsPerMesh; i++) AddRuin(builder, island.Ruins[i]);
                Spawn("Ruins", parent, builder.Build("Ruins"), GameMaterials.PaletteLit);
            }

            foreach (WreckSpec wreck in island.Wrecks)
            {
                Model model = ModelLibrary.Get(WreckModels[wreck.Kind % WreckModels.Length]);
                if (model == null) continue;
                Vector3 normal = island.Map.Normal(wreck.Base.x, wreck.Base.z);
                Vector3 forward = Vector3.ProjectOnPlane(Quaternion.Euler(0f, wreck.YawDeg, 0f) * Vector3.forward, normal).normalized;
                Transform body = model.Spawn(parent, model.Burnt, true);
                // Settled on flat tyres, and not quite level.
                body.SetPositionAndRotation(wreck.Base + Vector3.down * 0.22f,
                    Quaternion.LookRotation(forward, normal) * Quaternion.Euler(0f, 0f, wreck.Kind % 2 == 0 ? 5f : -4f));
            }

            foreach (Vector3 fire in island.Fires) effects.BurnForEver(fire + Vector3.up * 0.8f, 2.4f, parent);
        }

        static void Spawn(string name, Transform parent, Mesh mesh, Material material)
        {
            var obj = new GameObject(name);
            obj.transform.SetParent(parent, false);
            obj.AddComponent<MeshFilter>().sharedMesh = mesh;
            var renderer = obj.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = ShadowCastingMode.On;
        }

        /// <summary>
        /// A house after the shelling: walls that stand tall on one side and fall away towards the
        /// corner that was hit, empty window openings, a floor of rubble and what is left of the roof.
        /// </summary>
        /// <summary>A high-rise that has been shelled: floors of dark windows, a bitten-off top, a hole or two.</summary>
        static void AddTower(MeshBuilder b, RuinSpec ruin)
        {
            var random = new System.Random(ruin.Seed);
            float Next(float min, float max) => min + (float)random.NextDouble() * (max - min);
            int wall = random.Next(3) == 0 ? Palette.Plaster : random.Next(2) == 0 ? Palette.Concrete : Palette.Rubble;
            float w = ruin.Width, h = ruin.Height;
            Vector3 foot = ruin.Base + Vector3.down * 1.5f;
            b.Box(foot + Vector3.up * (h + 1.5f) * 0.5f, new Vector3(w, h + 1.5f, w), wall);
            // Window bands on every floor, a hand's breadth proud of the wall so they read from the air.
            for (float y = 2.2f; y < h - 1.2f; y += 3.1f)
            {
                b.Box(ruin.Base + Vector3.up * y, new Vector3(w + 0.16f, 1.25f, w * 0.86f), Palette.Lens);
                b.Box(ruin.Base + Vector3.up * y, new Vector3(w * 0.86f, 1.25f, w + 0.16f), Palette.Lens);
            }
            // The top floors are gone on one corner, and soot runs down from the breach.
            float bite = Next(0.3f, 0.5f) * w, sx = random.Next(2) == 0 ? -1f : 1f, sz = random.Next(2) == 0 ? -1f : 1f;
            b.Box(ruin.Base + new Vector3(sx * (w - bite) * 0.5f, h - bite * 0.4f, sz * (w - bite) * 0.5f), new Vector3(bite + 0.3f, bite * 0.9f, bite + 0.3f), Palette.Soot);
            b.Box(ruin.Base + new Vector3(-sx * w * 0.2f, h + 0.9f, -sz * w * 0.2f), new Vector3(w * 0.34f, 1.8f, w * 0.3f), Palette.DarkGrey);
            b.Box(ruin.Base + new Vector3(sx * (w * 0.5f + 0.1f), h * Next(0.35f, 0.6f), sz * w * 0.12f), new Vector3(0.3f, 3.4f, 3.2f), Palette.Soot);
            // What fell off lies at its feet.
            for (int i = 0; i < 4; i++)
                b.Box(ruin.Base + new Vector3(sx * (w * 0.5f + Next(1f, 4f)), Next(0.2f, 0.7f), sz * Next(-w * 0.4f, w * 0.5f)),
                    new Vector3(Next(1.2f, 2.8f), Next(0.6f, 1.4f), Next(1.2f, 2.8f)), Quaternion.Euler(Next(-18f, 18f), Next(0f, 90f), Next(-18f, 18f)), Palette.Rubble);
        }

        public static void AddRuin(MeshBuilder b, RuinSpec ruin)
        {
            if (ruin.Height > Island.RuinMaxHeight + 0.5f)
            {
                AddTower(b, ruin);
                return;
            }
            var random = new System.Random(ruin.Seed);
            float Next(float min, float max) => min + (float)random.NextDouble() * (max - min);

            Quaternion turn = Quaternion.Euler(0f, ruin.YawDeg, 0f);
            Vector3 At(float x, float y, float z) => ruin.Base + turn * new Vector3(x, y, z);

            int wall = random.Next(3) == 0 ? Palette.Brick : random.Next(2) == 0 ? Palette.Plaster : Palette.Concrete;
            float halfW = ruin.Width * 0.5f, halfD = ruin.Depth * 0.5f;
            const float thick = 0.45f, sunk = 1.6f;
            // The corner the shell came through.
            var hit = new Vector2(random.Next(2) == 0 ? -halfW : halfW, random.Next(2) == 0 ? -halfD : halfD);
            float diagonal = new Vector2(ruin.Width, ruin.Depth).magnitude;

            float HeightAt(float x, float z)
            {
                float far = new Vector2(x - hit.x, z - hit.y).magnitude / diagonal;
                return ruin.Height * Mathf.Clamp(0.12f + far * 1.25f, 0.12f, 1f) * Next(0.82f, 1.08f);
            }

            void Wall(float x0, float z0, float x1, float z1)
            {
                float length = new Vector2(x1 - x0, z1 - z0).magnitude;
                int pieces = Mathf.Max(2, Mathf.RoundToInt(length / 2.3f));
                bool alongX = Mathf.Abs(x1 - x0) > Mathf.Abs(z1 - z0);
                float piece = length / pieces;
                for (int i = 0; i < pieces; i++)
                {
                    float t = (i + 0.5f) / pieces;
                    float x = Mathf.Lerp(x0, x1, t), z = Mathf.Lerp(z0, z1, t);
                    float height = HeightAt(x, z);
                    var size = alongX ? new Vector3(piece + 0.02f, 1f, thick) : new Vector3(thick, 1f, piece + 0.02f);
                    bool window = i % 2 == 1 && height > 2.7f;
                    if (window)
                    {
                        // A sill, an opening, and whatever still hangs above it.
                        size.y = 1.0f + sunk;
                        b.Box(At(x, (1.0f - sunk) * 0.5f, z), size, turn, wall);
                        if (height > 2.9f)
                        {
                            size.y = height - 2.3f;
                            b.Box(At(x, 2.3f + size.y * 0.5f, z), size, turn, wall);
                        }
                    }
                    else
                    {
                        size.y = height + sunk;
                        b.Box(At(x, (height - sunk) * 0.5f, z), size, turn, wall);
                    }
                    // Fire has been at the top of what stands.
                    if (random.Next(3) != 0 && height > 1.5f)
                    {
                        var cap = new Vector3(size.x * 1.02f, 0.4f, size.z * 1.06f);
                        b.Box(At(x, height - 0.1f, z), cap, turn, Palette.Soot);
                    }
                }
            }

            Wall(-halfW, -halfD, halfW, -halfD);
            Wall(-halfW, halfD, halfW, halfD);
            Wall(-halfW, -halfD + thick, -halfW, halfD - thick);
            Wall(halfW, -halfD + thick, halfW, halfD - thick);

            // The floor, under a layer of what came down.
            b.Box(At(0f, 0.12f - sunk * 0.5f, 0f), new Vector3(ruin.Width - 0.5f, 0.3f + sunk, ruin.Depth - 0.5f), turn, Palette.Rubble);
            int heaps = random.Next(6, 11);
            for (int i = 0; i < heaps; i++)
            {
                // Most of the rubble lies where the wall was blown out, some of it outside.
                float x = Mathf.Lerp(hit.x, Next(-halfW, halfW), Next(0f, 0.8f)) + Next(-1.5f, 1.5f);
                float z = Mathf.Lerp(hit.y, Next(-halfD, halfD), Next(0f, 0.8f)) + Next(-1.5f, 1.5f);
                var size = new Vector3(Next(0.6f, 1.9f), Next(0.4f, 1.1f), Next(0.6f, 1.7f));
                Quaternion tilt = turn * Quaternion.Euler(Next(-25f, 25f), Next(0f, 90f), Next(-25f, 25f));
                b.Box(At(x, size.y * 0.3f, z), size, tilt, i % 3 == 0 ? wall : Palette.Rubble);
            }

            // An upper floor that half survived, in the taller houses.
            if (ruin.Height > 6.5f && random.Next(2) == 0)
            {
                float side = hit.x > 0f ? -1f : 1f;
                b.Box(At(side * halfW * 0.5f, 3.3f, 0f), new Vector3(halfW * 0.95f, 0.25f, ruin.Depth - 0.6f), turn, Palette.Concrete);
            }
            // Roof beams, fallen in.
            int beams = random.Next(1, 4);
            for (int i = 0; i < beams; i++)
            {
                float z = Next(-halfD * 0.7f, halfD * 0.7f);
                float top = Mathf.Min(ruin.Height * 0.8f, 5f);
                Quaternion lean = turn * Quaternion.Euler(0f, Next(-15f, 15f), (hit.x > 0f ? 1f : -1f) * Next(18f, 38f));
                b.Box(At(0f, top * 0.45f, z), new Vector3(ruin.Width * 0.85f, 0.22f, 0.22f), lean, Palette.Soot);
            }
        }
    }
}
