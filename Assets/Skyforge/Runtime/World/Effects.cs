using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Skyforge.World
{
    /// <summary>
    /// Fire, smoke, dust, blasts and tracers. Every puff is a soft sprite that faces the camera,
    /// grows, drifts with the wind and fades; all of them are drawn as two meshes rebuilt each
    /// frame (one that covers, one that glows), out of a fixed pool, so a firefight allocates nothing.
    /// </summary>
    public sealed class Effects
    {
        const int PuffCount = 640, StreakCount = 20, ScorchCount = 28;
        /// <summary>Fires nearer than this burn with flames; farther ones only raise their column of smoke.</summary>
        const float FlameDistance = 380f;

        // Cells of the sprite sheet, four across and two down.
        const int CellGlow = 4, CellRing = 5, CellSpark = 6, CellChunk = 7;

        struct Puff
        {
            public bool Active, Glow, Flat;
            public Vector3 Position, Velocity;
            public float Age, Life, SizeFrom, SizeTo, Gravity, Drag, Spin, Angle, Alpha, FadeIn, FadeFrom;
            public int Cell;
            public Color From, To;
        }

        struct Streak
        {
            public bool Active;
            public Vector3 Position, Direction;
            public float Left, Speed;
        }

        sealed class Fire
        {
            public Vector3 Position;
            public float Size;
            /// <summary>Seconds left; negative burns for ever.</summary>
            public float Left;
            public float FlameClock, SmokeClock;
            public bool Near;
            public bool Scenery;
        }

        readonly Transform _root;
        readonly Puff[] _puffs = new Puff[PuffCount];
        readonly int[] _order = new int[PuffCount];
        readonly float[] _depth = new float[PuffCount];
        readonly Vector3[] _smokeVertices = new Vector3[PuffCount * 4], _glowVertices = new Vector3[PuffCount * 4];
        readonly Color32[] _smokeColors = new Color32[PuffCount * 4], _glowColors = new Color32[PuffCount * 4];
        readonly Vector2[] _smokeUvs = new Vector2[PuffCount * 4], _glowUvs = new Vector2[PuffCount * 4];
        readonly Mesh _smokeMesh, _glowMesh;
        readonly Streak[] _streaks = new Streak[StreakCount];
        readonly Transform[] _streakTransforms = new Transform[StreakCount];
        readonly Transform[] _scorches = new Transform[ScorchCount];
        readonly List<Fire> _fires = new List<Fire>();
        readonly Material _scorchMaterial;
        readonly System.Random _random = new System.Random(5);
        int _nextPuff, _nextStreak, _nextScorch;
        float _sortClock;

        /// <summary>Smoke leans with it.</summary>
        public Vector3 Wind;
        /// <summary>How brightly the smoke is lit: the weather's daylight. Fire makes its own light.</summary>
        public Color Daylight = Color.white;

        public Effects(Transform parent)
        {
            _root = new GameObject("Effects").transform;
            _root.SetParent(parent, false);
            _scorchMaterial = GameMaterials.Lit(new Color(0.07f, 0.065f, 0.06f), 0f);

            Texture2D sheet = BuildSheet();
            Material smoke = GameMaterials.Particle(sheet, false) ?? GameMaterials.Unlit(new Color(0.3f, 0.3f, 0.3f));
            Material glow = GameMaterials.Particle(sheet, true) ?? GameMaterials.Unlit(new Color(1f, 0.7f, 0.3f));
            if (glow.HasProperty("_Glow")) glow.SetFloat("_Glow", 1.8f);
            _smokeMesh = BuildQuads("Smoke", smoke);
            _glowMesh = BuildQuads("Glow", glow);

            var streak = new MeshBuilder();
            streak.Box(Vector3.zero, new Vector3(0.14f, 0.14f, 1f), Palette.White);
            Mesh streakMesh = streak.Build("Streak");
            Material tracer = GameMaterials.Unlit(new Color(2.2f, 1.7f, 0.6f));
            for (int i = 0; i < StreakCount; i++)
            {
                _streakTransforms[i] = Create("Tracer", streakMesh, tracer);
                _streakTransforms[i].gameObject.SetActive(false);
            }

            var disc = new MeshBuilder();
            disc.Cylinder(Vector3.zero, 1f, 1f, 0.04f, 14, Palette.White);
            Mesh discMesh = disc.Build("Scorch");
            for (int i = 0; i < ScorchCount; i++)
            {
                _scorches[i] = Create("Scorch", discMesh, _scorchMaterial);
                _scorches[i].gameObject.SetActive(false);
            }
        }

        Transform Create(string name, Mesh mesh, Material material)
        {
            var obj = new GameObject(name);
            obj.transform.SetParent(_root, false);
            obj.AddComponent<MeshFilter>().sharedMesh = mesh;
            var renderer = obj.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            return obj.transform;
        }

        /// <summary>One mesh of PuffCount quads; the vertices are rewritten every frame.</summary>
        Mesh BuildQuads(string name, Material material)
        {
            var mesh = new Mesh { name = name };
            mesh.MarkDynamic();
            mesh.vertices = new Vector3[PuffCount * 4];
            mesh.colors32 = new Color32[PuffCount * 4];
            mesh.uv = new Vector2[PuffCount * 4];
            var triangles = new int[PuffCount * 6];
            for (int i = 0; i < PuffCount; i++)
            {
                int v = i * 4, t = i * 6;
                triangles[t] = v; triangles[t + 1] = v + 1; triangles[t + 2] = v + 2;
                triangles[t + 3] = v; triangles[t + 4] = v + 2; triangles[t + 5] = v + 3;
            }
            mesh.triangles = triangles;
            // Never culled and never measured: the puffs are all over the island.
            mesh.bounds = new Bounds(Vector3.zero, Vector3.one * 100000f);
            Create(name, mesh, material);
            return mesh;
        }

        /// <summary>
        /// The sprite sheet, drawn here rather than shipped: four billows of smoke, a soft glow,
        /// a shock ring, a spark and a chunk of debris.
        /// </summary>
        static Texture2D BuildSheet()
        {
            const int cell = 128, across = 4, down = 2;
            var pixels = new Color32[cell * across * cell * down];
            for (int index = 0; index < across * down; index++)
            {
                int ox = index % across * cell, oy = index / across * cell;
                float seed = 11.3f + index * 37.7f;
                for (int y = 0; y < cell; y++)
                for (int x = 0; x < cell; x++)
                {
                    float u = (x + 0.5f) / cell * 2f - 1f, v = (y + 0.5f) / cell * 2f - 1f;
                    float r = Mathf.Sqrt(u * u + v * v);
                    float alpha, light = 1f;
                    if (index < CellGlow)
                    {
                        // A billow: a soft ball eaten into by noise, lit a little from above.
                        float n = Mathf.PerlinNoise(seed + u * 1.7f, seed + v * 1.7f) * 0.6f
                                  + Mathf.PerlinNoise(seed * 2f + u * 4.1f, seed * 2f + v * 4.1f) * 0.3f
                                  + Mathf.PerlinNoise(seed * 3f + u * 9f, seed * 3f + v * 9f) * 0.1f;
                        float edge = Mathf.Clamp01(1f - r * (0.92f + (0.5f - n) * 0.9f));
                        alpha = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(edge * 2.4f)) * Mathf.Clamp01(0.45f + n * 0.9f);
                        light = Mathf.Clamp01(0.62f + n * 0.42f + v * 0.16f);
                    }
                    else if (index == CellGlow)
                    {
                        float core = Mathf.Clamp01(1f - r);
                        alpha = core * core;
                    }
                    else if (index == CellRing)
                    {
                        float band = 1f - Mathf.Abs(r - 0.8f) / 0.16f;
                        alpha = Mathf.Clamp01(band) * Mathf.Clamp01(band) * 0.9f;
                    }
                    else if (index == CellSpark)
                    {
                        alpha = Mathf.Clamp01(1f - r * 1.6f);
                        alpha = Mathf.Clamp01(alpha * 3f);
                    }
                    else
                    {
                        // A chunk: a hard-edged, uneven lump.
                        float angle = Mathf.Atan2(v, u);
                        float rim = 0.52f + 0.2f * Mathf.Sin(angle * 3f + 1f) + 0.1f * Mathf.Sin(angle * 5f);
                        alpha = Mathf.Clamp01((rim - r) * 14f);
                        light = 0.75f + 0.25f * Mathf.PerlinNoise(seed + u * 3f, seed + v * 3f);
                    }
                    byte l = (byte)(light * 255f);
                    pixels[(oy + y) * cell * across + ox + x] = new Color32(l, l, l, (byte)(Mathf.Clamp01(alpha) * 255f));
                }
            }
            var texture = new Texture2D(cell * across, cell * down, TextureFormat.RGBA32, true)
            {
                name = "EffectSheet", wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear,
            };
            texture.SetPixels32(pixels);
            texture.Apply(true, true);
            return texture;
        }

        float Range(float min, float max) => min + (float)_random.NextDouble() * (max - min);

        Vector3 Scatter(float horizontal, float vertical)
        {
            float angle = Range(0f, 2f * Mathf.PI);
            float reach = Mathf.Sqrt((float)_random.NextDouble()) * horizontal;
            return new Vector3(Mathf.Cos(angle) * reach, Range(0f, vertical), Mathf.Sin(angle) * reach);
        }

        void Emit(Puff puff)
        {
            puff.Active = true;
            if (puff.Alpha <= 0f) puff.Alpha = puff.Glow ? 1f : 0.82f;
            if (puff.FadeFrom <= 0f) puff.FadeFrom = puff.Glow ? 0.25f : 0.45f;
            if (puff.FadeIn <= 0f) puff.FadeIn = puff.Glow ? 0.02f : 0.08f;
            if (puff.Cell == 0 && !puff.Flat) puff.Cell = _random.Next(0, CellGlow);
            puff.Angle = Range(0f, 2f * Mathf.PI);
            if (puff.Spin == 0f) puff.Spin = Range(-0.5f, 0.5f);
            _puffs[_nextPuff] = puff;
            _nextPuff = (_nextPuff + 1) % PuffCount;
        }

        // ---------------------------------------------------------------- what the game asks for

        /// <summary>A blast on the spot. <paramref name="radius"/> is how far it reaches, in metres.</summary>
        public void Explosion(Vector3 at, float radius, bool scorch = true)
        {
            float r = Mathf.Clamp(radius, 3f, 26f);
            // The flash, and the ring of the shock running out along the ground.
            Emit(new Puff
            {
                Glow = true, Cell = CellGlow, Position = at + Vector3.up * r * 0.2f, Life = 0.14f, SizeFrom = r * 0.7f, SizeTo = r * 2.3f,
                From = new Color(1f, 0.97f, 0.85f), To = new Color(1f, 0.62f, 0.15f),
            });
            Emit(new Puff
            {
                Glow = true, Flat = true, Cell = CellRing, Position = at + Vector3.up * 0.3f, Life = 0.42f, SizeFrom = r * 0.6f,
                SizeTo = r * 4.2f, Alpha = 0.5f, From = new Color(1f, 0.9f, 0.75f), To = new Color(0.8f, 0.7f, 0.6f),
            });

            int fireballs = 7 + Mathf.RoundToInt(r * 0.5f);
            for (int i = 0; i < fireballs; i++)
            {
                Vector3 offset = Scatter(r * 0.3f, r * 0.25f);
                Emit(new Puff
                {
                    Glow = true, Position = at + offset, Velocity = offset.normalized * r * 0.9f + Vector3.up * Range(r * 0.3f, r * 1.0f),
                    Life = Range(0.5f, 1.05f), SizeFrom = r * 0.35f, SizeTo = r * 0.95f, Drag = 3f,
                    From = new Color(1f, 0.78f, 0.28f), To = new Color(0.60f, 0.10f, 0.03f),
                });
            }

            // The black cloud that rolls up out of the fireball.
            int smoke = 8 + Mathf.RoundToInt(r * 0.6f);
            for (int i = 0; i < smoke; i++)
            {
                Vector3 offset = Scatter(r * 0.35f, r * 0.3f);
                Emit(new Puff
                {
                    Position = at + offset, Velocity = new Vector3(offset.x, 0f, offset.z) * 0.5f + Vector3.up * Range(2.5f, 7f),
                    Life = Range(3.2f, 5.6f), SizeFrom = r * 0.45f, SizeTo = r * 1.5f, Drag = 1.0f, Gravity = 0.6f, FadeIn = 0.05f,
                    From = new Color(0.07f, 0.065f, 0.06f), To = new Color(0.30f, 0.29f, 0.28f), Alpha = 0.9f,
                });
            }

            // Debris and sparks thrown out and falling back.
            for (int i = 0; i < 9; i++)
            {
                Vector3 offset = Scatter(1f, 0f);
                Emit(new Puff
                {
                    Cell = CellChunk, Position = at + Vector3.up * 0.5f,
                    Velocity = offset.normalized * Range(r * 0.6f, r * 1.7f) + Vector3.up * Range(r * 0.8f, r * 1.9f),
                    Life = Range(1.0f, 1.8f), SizeFrom = Range(0.3f, 0.8f), SizeTo = 0.3f, Gravity = -17f, Spin = Range(-9f, 9f),
                    Alpha = 1f, FadeFrom = 0.85f, FadeIn = 0.001f,
                    From = new Color(0.12f, 0.11f, 0.10f), To = new Color(0.10f, 0.09f, 0.08f),
                });
            }
            for (int i = 0; i < 12; i++)
            {
                Vector3 offset = Scatter(1f, 0f);
                Emit(new Puff
                {
                    Glow = true, Cell = CellSpark, Position = at + Vector3.up * 0.5f,
                    Velocity = offset.normalized * Range(r * 0.9f, r * 2.4f) + Vector3.up * Range(r * 0.6f, r * 2.2f),
                    Life = Range(0.6f, 1.3f), SizeFrom = 0.45f, SizeTo = 0.15f, Gravity = -14f, Drag = 0.6f, FadeFrom = 0.6f,
                    From = new Color(1f, 0.85f, 0.45f), To = new Color(1f, 0.35f, 0.08f),
                });
            }

            // Dust kicked along the ground.
            for (int i = 0; i < 8; i++)
            {
                float angle = (i + Range(0f, 1f)) * Mathf.PI / 4f;
                var outward = new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle));
                Emit(new Puff
                {
                    Position = at + outward * r * 0.25f + Vector3.up * 0.5f, Velocity = outward * r * 1.4f + Vector3.up * 0.9f,
                    Life = Range(1.4f, 2.2f), SizeFrom = r * 0.3f, SizeTo = r * 0.95f, Drag = 2.2f, Alpha = 0.55f,
                    From = new Color(0.52f, 0.46f, 0.38f), To = new Color(0.62f, 0.58f, 0.52f),
                });
            }

            // A big one stands up as a column of fire before the smoke takes over, and its embers hang in the air.
            if (r > 9f)
            {
                for (int i = 0; i < 7; i++)
                {
                    Emit(new Puff
                    {
                        Glow = true, Position = at + Scatter(r * 0.15f, 0f) + Vector3.up * (i * r * 0.16f),
                        Velocity = Vector3.up * Range(r * 0.7f, r * 1.2f), Life = Range(0.9f, 1.6f), SizeFrom = r * 0.4f,
                        SizeTo = r * 0.75f, Drag = 1.4f, From = new Color(1f, 0.66f, 0.2f), To = new Color(0.45f, 0.07f, 0.02f),
                    });
                }
            }
            for (int i = 0; i < 10; i++)
            {
                Emit(new Puff
                {
                    Glow = true, Cell = CellSpark, Position = at + Scatter(r * 0.6f, r * 0.5f),
                    Velocity = new Vector3(Range(-1f, 1f), Range(1.2f, 3.5f), Range(-1f, 1f)), Life = Range(2f, 4f),
                    SizeFrom = 0.22f, SizeTo = 0.08f, Drag = 0.3f, FadeFrom = 0.5f, FadeIn = 0.3f,
                    From = new Color(1f, 0.7f, 0.3f), To = new Color(1f, 0.3f, 0.05f),
                });
            }

            if (!scorch) return;
            Transform mark = _scorches[_nextScorch];
            _nextScorch = (_nextScorch + 1) % ScorchCount;
            mark.position = at + Vector3.up * 0.07f;
            mark.localScale = new Vector3(r * 0.55f, 1f, r * 0.55f);
            mark.gameObject.SetActive(true);
        }

        /// <summary>A munition into the sea: a white column thrown up and falling back as spray.</summary>
        public void Splash(Vector3 at, float radius)
        {
            float r = Mathf.Clamp(radius, 3f, 20f);
            at.y = Skyforge.Core.Island.WaterLevel;
            Emit(new Puff
            {
                Glow = true, Flat = true, Cell = CellRing, Position = at + Vector3.up * 0.15f, Life = 1.1f, SizeFrom = r * 0.5f,
                SizeTo = r * 3.6f, Alpha = 0.45f, From = Color.white, To = new Color(0.8f, 0.9f, 1f),
            });
            for (int i = 0; i < 12; i++)
            {
                Vector3 offset = Scatter(r * 0.22f, 0f);
                Emit(new Puff
                {
                    Position = at + offset + Vector3.up * (i * 0.3f), Velocity = offset * 0.6f + Vector3.up * Range(r * 1.1f, r * 2.4f),
                    Life = Range(1.1f, 1.9f), SizeFrom = r * 0.25f, SizeTo = r * 0.7f, Gravity = -9f, Drag = 0.5f, Alpha = 0.8f,
                    From = new Color(0.94f, 0.97f, 1f), To = new Color(0.75f, 0.84f, 0.9f),
                });
            }
            for (int i = 0; i < 10; i++)
            {
                float angle = i * Mathf.PI / 5f;
                var outward = new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle));
                Emit(new Puff
                {
                    Position = at + outward * r * 0.3f + Vector3.up * 0.4f, Velocity = outward * r * 0.9f + Vector3.up * r * 0.35f,
                    Life = Range(0.9f, 1.5f), SizeFrom = r * 0.2f, SizeTo = r * 0.6f, Gravity = -6f, Drag = 1.2f, Alpha = 0.6f,
                    From = Color.white, To = new Color(0.8f, 0.88f, 0.94f),
                });
            }
        }

        /// <summary>A building or a big machine coming apart: a wide rolling cloud of dust and a rain of pieces.</summary>
        public void Collapse(Vector3 at, float size)
        {
            float r = Mathf.Clamp(size, 3f, 20f);
            for (int i = 0; i < 14; i++)
            {
                float angle = (i + Range(0f, 1f)) * Mathf.PI / 7f;
                var outward = new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle));
                Emit(new Puff
                {
                    Position = at + outward * r * 0.3f + Vector3.up * Range(0.5f, r * 0.5f),
                    Velocity = outward * Range(r * 0.5f, r * 1.2f) + Vector3.up * Range(0.5f, 2.5f),
                    Life = Range(3.5f, 6f), SizeFrom = r * 0.5f, SizeTo = r * 1.6f, Drag = 1.6f, Alpha = 0.7f,
                    From = new Color(0.46f, 0.43f, 0.39f), To = new Color(0.60f, 0.58f, 0.55f),
                });
            }
            for (int i = 0; i < 12; i++)
            {
                Vector3 offset = Scatter(r * 0.4f, r * 0.5f);
                Emit(new Puff
                {
                    Cell = CellChunk, Position = at + offset,
                    Velocity = new Vector3(offset.x, 0f, offset.z).normalized * Range(2f, r) + Vector3.up * Range(3f, r * 1.2f),
                    Life = Range(1.2f, 2.2f), SizeFrom = Range(0.5f, 1.3f), SizeTo = 0.5f, Gravity = -17f, Spin = Range(-7f, 7f),
                    Alpha = 1f, FadeFrom = 0.9f, FadeIn = 0.001f,
                    From = new Color(0.30f, 0.28f, 0.26f), To = new Color(0.22f, 0.21f, 0.20f),
                });
            }
        }

        /// <summary>A round striking something: a spit of sparks and a little dust.</summary>
        public void Impact(Vector3 at)
        {
            for (int i = 0; i < 4; i++)
            {
                Emit(new Puff
                {
                    Glow = true, Cell = CellSpark, Position = at, Velocity = Scatter(5f, 6f), Life = Range(0.2f, 0.45f),
                    SizeFrom = 0.3f, SizeTo = 0.08f, Gravity = -12f, From = new Color(1f, 0.9f, 0.6f), To = new Color(1f, 0.4f, 0.1f),
                });
            }
            Emit(new Puff
            {
                Position = at, Velocity = Vector3.up * 1.5f, Life = 0.9f, SizeFrom = 0.5f, SizeTo = 2.2f, Alpha = 0.5f,
                From = new Color(0.45f, 0.42f, 0.38f), To = new Color(0.55f, 0.53f, 0.50f),
            });
        }

        /// <summary>Decoy flares thrown out behind an aircraft: bright points that fall away trailing smoke.</summary>
        public void Flares(Vector3 at, Vector3 velocity)
        {
            for (int i = 0; i < 10; i++)
            {
                Vector3 push = velocity * 0.4f + Scatter(9f, 2f) + Vector3.down * Range(2f, 6f);
                Emit(new Puff
                {
                    Glow = true, Cell = CellGlow, Position = at, Velocity = push, Life = Range(1.6f, 2.6f), SizeFrom = 1.6f, SizeTo = 0.8f,
                    Gravity = -5f, Drag = 0.8f, FadeFrom = 0.7f, From = new Color(1f, 0.95f, 0.75f), To = new Color(1f, 0.55f, 0.2f),
                });
                Emit(new Puff
                {
                    Position = at, Velocity = push * 0.7f, Life = Range(1.8f, 2.8f), SizeFrom = 0.5f, SizeTo = 2.4f, Alpha = 0.4f, Gravity = -3f, Drag = 0.8f,
                    From = new Color(0.8f, 0.8f, 0.8f), To = new Color(0.6f, 0.6f, 0.62f),
                });
            }
        }

        /// <summary>Dust blown off the ground, by rotor wash or by wheels.</summary>
        public void Dust(Vector3 at, float size, Vector3 push)
        {
            Emit(new Puff
            {
                Position = at + Scatter(size, 0.3f), Velocity = push + Vector3.up * Range(0.3f, 1.2f), Life = Range(1.2f, 2.2f),
                SizeFrom = size * 0.5f, SizeTo = size * 2.2f, Drag = 1.5f, Alpha = 0.32f,
                From = new Color(0.58f, 0.53f, 0.45f), To = new Color(0.66f, 0.63f, 0.58f),
            });
        }

        /// <summary>The exhaust behind a rocket in flight: a hot point and the white trail it leaves.</summary>
        public void Trail(Vector3 at)
        {
            Emit(new Puff
            {
                Position = at, Velocity = Scatter(0.3f, 0.3f), Life = 1.3f, SizeFrom = 0.3f, SizeTo = 1.5f, Alpha = 0.55f, FadeFrom = 0.2f,
                From = new Color(0.86f, 0.85f, 0.83f), To = new Color(0.62f, 0.62f, 0.63f),
            });
            Emit(new Puff
            {
                Glow = true, Cell = CellGlow, Position = at, Life = 0.08f, SizeFrom = 1.3f, SizeTo = 0.7f,
                From = new Color(1f, 0.85f, 0.5f), To = new Color(1f, 0.5f, 0.15f),
            });
        }

        public void MuzzleFlash(Vector3 at, float size = 1.2f)
        {
            Emit(new Puff
            {
                Glow = true, Cell = CellGlow, Position = at, Life = 0.08f, SizeFrom = size * 1.2f, SizeTo = size * 2.6f,
                From = new Color(1f, 0.95f, 0.7f), To = new Color(1f, 0.6f, 0.2f),
            });
            Emit(new Puff
            {
                Position = at, Velocity = Vector3.up * 0.8f, Life = 0.7f, SizeFrom = size * 0.5f, SizeTo = size * 1.8f, Alpha = 0.35f,
                From = new Color(0.6f, 0.6f, 0.6f), To = new Color(0.7f, 0.7f, 0.7f),
            });
        }

        /// <summary>A bright round on its way from a gun to whatever it is shooting at.</summary>
        public void Tracer(Vector3 from, Vector3 to)
        {
            Vector3 path = to - from;
            float length = path.magnitude;
            if (length < 2f) return;
            _streaks[_nextStreak] = new Streak { Active = true, Position = from, Direction = path / length, Left = length, Speed = 240f };
            _streakTransforms[_nextStreak].gameObject.SetActive(true);
            _nextStreak = (_nextStreak + 1) % StreakCount;
        }

        /// <summary>Something burns here: flames at the base and smoke rising off them.</summary>
        /// <param name="seconds">How long it burns; negative for as long as the mission lasts.</param>
        public void Burn(Vector3 at, float size, float seconds = -1f)
        {
            _fires.Add(new Fire { Position = at, Size = size, Left = seconds, Near = true });
        }

        /// <summary>A fire that belongs to the island, not to a mission: it survives <see cref="ClearBattle"/>.</summary>
        public void BurnForEver(Vector3 at, float size, Transform parent)
        {
            var fire = new Fire { Position = at, Size = size, Left = -1f, Scenery = true };
            _fires.Add(fire);
            Prewarm(fire);
        }

        /// <summary>Stands a fire's column of smoke up at once, as if it had been burning all along.</summary>
        void Prewarm(Fire fire)
        {
            const int count = 9;
            for (int i = 0; i < count; i++) EmitColumn(fire, (i + Range(0f, 0.8f)) / count);
        }

        /// <summary>One billow of a fire's tall column; <paramref name="aged"/> is how far through its life it starts.</summary>
        void EmitColumn(Fire fire, float aged)
        {
            float life = Range(15f, 19f);
            var puff = new Puff
            {
                Position = fire.Position + Scatter(fire.Size * 0.3f, 0.5f) + Vector3.up * fire.Size * 0.6f,
                Velocity = Vector3.up * Range(3.6f, 4.6f), Life = life,
                SizeFrom = fire.Size * 1.1f, SizeTo = fire.Size * 7.5f, Drag = 0.035f, Alpha = 0.8f, FadeFrom = 0.4f, FadeIn = 0.04f,
                From = new Color(0.06f, 0.055f, 0.05f), To = new Color(0.36f, 0.35f, 0.35f),
            };
            if (aged > 0f)
            {
                float age = aged * life;
                puff.Position += (puff.Velocity + Wind * 0.6f) * age * 0.82f;
            }
            Emit(puff);
            if (aged > 0f) _puffs[(_nextPuff + PuffCount - 1) % PuffCount].Age = aged * life;
        }

        /// <summary>Puts out what a mission set alight and clears the air; the island's own fires stay.</summary>
        public void ClearBattle()
        {
            _fires.RemoveAll(fire => !fire.Scenery);
            for (int i = 0; i < PuffCount; i++) _puffs[i].Active = false;
            for (int i = 0; i < StreakCount; i++)
            {
                _streaks[i].Active = false;
                _streakTransforms[i].gameObject.SetActive(false);
            }
            foreach (Transform scorch in _scorches) scorch.gameObject.SetActive(false);
            foreach (Fire fire in _fires) Prewarm(fire);
        }

        /// <summary>Forgets the island's fires too: the region is being replaced.</summary>
        public void ClearAll()
        {
            _fires.Clear();
            ClearBattle();
        }

        // ---------------------------------------------------------------- every frame

        public void Update(float deltaTime, Transform camera)
        {
            Vector3 eye = camera.position;
            UpdateFires(deltaTime, eye);

            int active = 0;
            for (int i = 0; i < PuffCount; i++)
            {
                if (!_puffs[i].Active) continue;
                ref Puff puff = ref _puffs[i];
                puff.Age += deltaTime;
                if (puff.Age >= puff.Life)
                {
                    puff.Active = false;
                    continue;
                }
                puff.Velocity.y += puff.Gravity * deltaTime;
                puff.Velocity *= Mathf.Exp(-puff.Drag * deltaTime);
                puff.Position += (puff.Velocity + (puff.Glow || puff.Cell == CellChunk ? Vector3.zero : Wind * 0.6f)) * deltaTime;
                puff.Angle += puff.Spin * deltaTime;
                _order[active] = i;
                _depth[active] = -(puff.Position - eye).sqrMagnitude;
                active++;
            }
            // Far puffs first, so nearer smoke covers what is behind it.
            System.Array.Sort(_depth, _order, 0, active);

            Vector3 right = camera.right, up = camera.up;
            int smoke = 0, glow = 0;
            for (int n = 0; n < active; n++)
            {
                ref Puff puff = ref _puffs[_order[n]];
                float share = puff.Age / puff.Life;
                float grow = 1f - (1f - share) * (1f - share);
                float size = Mathf.Lerp(puff.SizeFrom, puff.SizeTo, grow) * 0.5f;
                float alpha = puff.Alpha * Mathf.Clamp01(share / puff.FadeIn) * (1f - Mathf.InverseLerp(puff.FadeFrom, 1f, share));
                Color color = Color.Lerp(puff.From, puff.To, share);
                if (!puff.Glow) color *= Daylight;
                color.a = alpha;
                Color32 packed = color;

                float cos = Mathf.Cos(puff.Angle) * size, sin = Mathf.Sin(puff.Angle) * size;
                Vector3 a, b;
                if (puff.Flat)
                {
                    a = new Vector3(cos, 0f, sin);
                    b = new Vector3(-sin, 0f, cos);
                }
                else
                {
                    a = right * cos + up * sin;
                    b = up * cos - right * sin;
                }

                float u0 = (puff.Cell % 4) * 0.25f, v0 = (puff.Cell / 4) * 0.5f;
                Vector3[] vertices = puff.Glow ? _glowVertices : _smokeVertices;
                Color32[] colors = puff.Glow ? _glowColors : _smokeColors;
                Vector2[] uvs = puff.Glow ? _glowUvs : _smokeUvs;
                int v = (puff.Glow ? glow++ : smoke++) * 4;
                vertices[v] = puff.Position - a - b; vertices[v + 1] = puff.Position - a + b;
                vertices[v + 2] = puff.Position + a + b; vertices[v + 3] = puff.Position + a - b;
                colors[v] = colors[v + 1] = colors[v + 2] = colors[v + 3] = packed;
                uvs[v] = new Vector2(u0 + 0.004f, v0 + 0.004f); uvs[v + 1] = new Vector2(u0 + 0.004f, v0 + 0.496f);
                uvs[v + 2] = new Vector2(u0 + 0.246f, v0 + 0.496f); uvs[v + 3] = new Vector2(u0 + 0.246f, v0 + 0.004f);
            }
            Upload(_smokeMesh, _smokeVertices, _smokeColors, _smokeUvs, smoke);
            Upload(_glowMesh, _glowVertices, _glowColors, _glowUvs, glow);

            for (int i = 0; i < StreakCount; i++)
            {
                if (!_streaks[i].Active) continue;
                ref Streak streak = ref _streaks[i];
                float step = streak.Speed * deltaTime;
                streak.Position += streak.Direction * step;
                streak.Left -= step;
                if (streak.Left <= 0f)
                {
                    streak.Active = false;
                    _streakTransforms[i].gameObject.SetActive(false);
                    continue;
                }
                Transform transform = _streakTransforms[i];
                transform.SetPositionAndRotation(streak.Position, Quaternion.LookRotation(streak.Direction));
                transform.localScale = new Vector3(1f, 1f, Mathf.Min(7f, streak.Left + 1f));
            }
        }

        /// <summary>Sends the used quads to the mesh; the unused ones collapse to a point and draw nothing.</summary>
        static void Upload(Mesh mesh, Vector3[] vertices, Color32[] colors, Vector2[] uvs, int used)
        {
            for (int i = used * 4; i < vertices.Length; i++)
            {
                if (vertices[i] == Vector3.zero) break;
                vertices[i] = Vector3.zero;
            }
            mesh.SetVertices(vertices);
            mesh.SetColors(colors);
            mesh.SetUVs(0, uvs);
        }

        void UpdateFires(float deltaTime, Vector3 camera)
        {
            _sortClock -= deltaTime;
            if (_sortClock <= 0f)
            {
                _sortClock = 1f;
                foreach (Fire fire in _fires)
                    fire.Near = (fire.Position - camera).sqrMagnitude < FlameDistance * FlameDistance;
            }

            for (int i = _fires.Count - 1; i >= 0; i--)
            {
                Fire fire = _fires[i];
                if (fire.Left >= 0f)
                {
                    fire.Left -= deltaTime;
                    if (fire.Left <= 0f)
                    {
                        _fires.RemoveAt(i);
                        continue;
                    }
                }

                fire.SmokeClock -= deltaTime;
                if (fire.SmokeClock <= 0f)
                {
                    fire.SmokeClock = fire.Scenery ? 1.9f : 1.1f;
                    EmitColumn(fire, 0f);
                }
                if (!fire.Near) continue;

                fire.FlameClock -= deltaTime;
                if (fire.FlameClock <= 0f)
                {
                    fire.FlameClock = 0.09f;
                    Emit(new Puff
                    {
                        Glow = true, Position = fire.Position + Scatter(fire.Size * 0.45f, fire.Size * 0.3f),
                        Velocity = Vector3.up * Range(1.8f, 4f), Life = Range(0.45f, 0.85f),
                        SizeFrom = fire.Size * 1.1f, SizeTo = fire.Size * 0.45f, FadeFrom = 0.4f, FadeIn = 0.15f,
                        From = new Color(1f, 0.74f, 0.22f), To = new Color(0.85f, 0.20f, 0.04f),
                    });
                }
            }
        }
    }
}
