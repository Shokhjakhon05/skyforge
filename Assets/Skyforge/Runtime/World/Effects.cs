using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Skyforge.World
{
    /// <summary>
    /// Fire, smoke, blasts and tracers, drawn as low-poly blobs that grow, drift and shrink away.
    /// Everything comes out of fixed pools, so a firefight allocates nothing.
    /// </summary>
    public sealed class Effects
    {
        const int PuffCount = 320, StreakCount = 20, ScorchCount = 28;
        /// <summary>Only this many of the island's fires burn live at once; the rest show a standing plume.</summary>
        const int LiveFires = 6;

        struct Puff
        {
            public bool Active, Glow;
            public Vector3 Position, Velocity;
            public float Age, Life, SizeFrom, SizeTo, Gravity, Drag, Spin;
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
            public bool Live;
            /// <summary>The standing column shown while the fire is too far away to burn live.</summary>
            public GameObject Plume;
            public bool Scenery;
        }

        readonly Transform _root;
        readonly Puff[] _puffs = new Puff[PuffCount];
        readonly Transform[] _puffTransforms = new Transform[PuffCount];
        readonly MeshRenderer[] _puffRenderers = new MeshRenderer[PuffCount];
        readonly Streak[] _streaks = new Streak[StreakCount];
        readonly Transform[] _streakTransforms = new Transform[StreakCount];
        readonly Transform[] _scorches = new Transform[ScorchCount];
        readonly List<Fire> _fires = new List<Fire>();
        readonly MaterialPropertyBlock _block = new MaterialPropertyBlock();
        readonly Material _glow, _matte, _scorchMaterial;
        readonly Mesh _blob, _plumeMesh;
        readonly System.Random _random = new System.Random(5);
        static readonly int BaseColor = Shader.PropertyToID("_BaseColor");
        int _nextPuff, _nextStreak, _nextScorch;
        float _sortClock;

        /// <summary>Smoke leans with it.</summary>
        public Vector3 Wind;

        public Effects(Transform parent)
        {
            _root = new GameObject("Effects").transform;
            _root.SetParent(parent, false);
            _glow = GameMaterials.Unlit(Color.white);
            _matte = GameMaterials.Lit(Color.white, 0f);
            _scorchMaterial = GameMaterials.Lit(new Color(0.07f, 0.065f, 0.06f), 0f);
            _blob = BuildBlob();
            _plumeMesh = BuildPlume();

            for (int i = 0; i < PuffCount; i++)
            {
                var obj = new GameObject("Puff");
                obj.transform.SetParent(_root, false);
                obj.AddComponent<MeshFilter>().sharedMesh = _blob;
                var renderer = obj.AddComponent<MeshRenderer>();
                renderer.sharedMaterial = _matte;
                renderer.shadowCastingMode = ShadowCastingMode.Off;
                renderer.receiveShadows = false;
                renderer.enabled = false;
                _puffTransforms[i] = obj.transform;
                _puffRenderers[i] = renderer;
            }

            var streak = new MeshBuilder();
            streak.Box(Vector3.zero, new Vector3(0.14f, 0.14f, 1f), Palette.White);
            Mesh streakMesh = streak.Build("Streak");
            Material tracer = GameMaterials.Unlit(new Color(1f, 0.85f, 0.35f));
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

        // An icosahedron: twelve points, twenty faces.
        static readonly float Golden = (1f + Mathf.Sqrt(5f)) * 0.5f;
        static readonly Vector3[] BlobPoints =
        {
            new Vector3(-1, Golden, 0), new Vector3(1, Golden, 0), new Vector3(-1, -Golden, 0), new Vector3(1, -Golden, 0),
            new Vector3(0, -1, Golden), new Vector3(0, 1, Golden), new Vector3(0, -1, -Golden), new Vector3(0, 1, -Golden),
            new Vector3(Golden, 0, -1), new Vector3(Golden, 0, 1), new Vector3(-Golden, 0, -1), new Vector3(-Golden, 0, 1),
        };
        static readonly int[] BlobFaces =
        {
            0, 11, 5, 0, 5, 1, 0, 1, 7, 0, 7, 10, 0, 10, 11, 1, 5, 9, 5, 11, 4, 11, 10, 2, 10, 7, 6, 7, 1, 8,
            3, 9, 4, 3, 4, 2, 3, 2, 6, 3, 6, 8, 3, 8, 9, 4, 9, 5, 2, 4, 11, 6, 2, 10, 8, 6, 7, 9, 8, 1,
        };

        /// <summary>Adds a faceted ball to a mesh: the shape every puff of smoke and fire is made of.</summary>
        static void AddBlob(MeshBuilder builder, Vector3 centre, Vector3 radii, Quaternion turn, int color)
        {
            float unit = 1f / BlobPoints[0].magnitude;
            for (int i = 0; i < BlobFaces.Length; i += 3)
            {
                Vector3 a = turn * Vector3.Scale(BlobPoints[BlobFaces[i]] * unit, radii);
                Vector3 b = turn * Vector3.Scale(BlobPoints[BlobFaces[i + 1]] * unit, radii);
                Vector3 c = turn * Vector3.Scale(BlobPoints[BlobFaces[i + 2]] * unit, radii);
                builder.Triangle(centre + a, centre + b, centre + c, a + b + c, color);
            }
        }

        /// <summary>A faceted ball one metre across.</summary>
        static Mesh BuildBlob()
        {
            var builder = new MeshBuilder();
            AddBlob(builder, Vector3.zero, Vector3.one * 0.5f, Quaternion.identity, Palette.White);
            return builder.Build("Blob");
        }

        /// <summary>
        /// A column of smoke seen from far away: billows that widen and pale as they climb and
        /// lean downwind, over fifty metres tall. Black at the fire, grey where it thins out.
        /// </summary>
        static Mesh BuildPlume()
        {
            var builder = new MeshBuilder();
            const int billows = 12;
            for (int i = 0; i < billows; i++)
            {
                float share = i / (float)(billows - 1);
                // Not a neat stack: each billow sits a little off the line and is its own size.
                float wobble = Mathf.Sin(i * 2.4f) * (1.2f + share * 3f);
                var centre = new Vector3(Mathf.Pow(share, 1.6f) * 24f + wobble, 2.5f + share * 52f, Mathf.Cos(i * 1.7f) * (1f + share * 3.5f));
                float radius = Mathf.Lerp(1.9f, 8.5f, share) * (0.85f + 0.3f * Mathf.Abs(Mathf.Sin(i * 5.1f)));
                int color = share < 0.25f ? Palette.Soot : share < 0.5f ? Palette.Graphite : share < 0.8f ? Palette.DarkGrey : Palette.Concrete;
                AddBlob(builder, centre, new Vector3(radius, radius * 0.8f, radius), Quaternion.Euler(i * 37f, i * 71f, 0f), color);
            }
            return builder.Build("Plume");
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
            puff.Age = 0f;
            puff.Spin = Range(-90f, 90f);
            _puffs[_nextPuff] = puff;
            _puffTransforms[_nextPuff].rotation = Quaternion.Euler(Range(0f, 360f), Range(0f, 360f), 0f);
            _puffRenderers[_nextPuff].sharedMaterial = puff.Glow ? _glow : _matte;
            _nextPuff = (_nextPuff + 1) % PuffCount;
        }

        // ---------------------------------------------------------------- what the game asks for

        /// <summary>A blast on the spot. <paramref name="radius"/> is how far it reaches, in metres.</summary>
        public void Explosion(Vector3 at, float radius, bool scorch = true)
        {
            float r = Mathf.Clamp(radius, 3f, 26f);
            Emit(new Puff
            {
                Glow = true, Position = at + Vector3.up * r * 0.15f, Life = 0.18f, SizeFrom = r * 0.3f, SizeTo = r * 1.25f,
                From = new Color(1f, 0.97f, 0.80f), To = new Color(1f, 0.62f, 0.15f),
            });

            int fireballs = 5 + Mathf.RoundToInt(r * 0.4f);
            for (int i = 0; i < fireballs; i++)
            {
                Vector3 offset = Scatter(r * 0.3f, r * 0.25f);
                Emit(new Puff
                {
                    Glow = true, Position = at + offset, Velocity = offset.normalized * r * 0.9f + Vector3.up * Range(r * 0.3f, r * 0.9f),
                    Life = Range(0.45f, 0.95f), SizeFrom = r * 0.22f, SizeTo = r * 0.5f, Drag = 3f,
                    From = new Color(1f, 0.80f, 0.25f), To = new Color(0.55f, 0.10f, 0.04f),
                });
            }

            int smoke = 6 + Mathf.RoundToInt(r * 0.6f);
            for (int i = 0; i < smoke; i++)
            {
                Vector3 offset = Scatter(r * 0.35f, r * 0.3f);
                Emit(new Puff
                {
                    Position = at + offset, Velocity = new Vector3(offset.x, 0f, offset.z) * 0.5f + Vector3.up * Range(2.5f, 6.5f),
                    Life = Range(2.6f, 4.8f), SizeFrom = r * 0.28f, SizeTo = r * 0.75f, Drag = 1.1f, Gravity = 0.6f,
                    From = new Color(0.10f, 0.09f, 0.085f), To = new Color(0.34f, 0.33f, 0.32f),
                });
            }

            for (int i = 0; i < 7; i++)
            {
                Vector3 offset = Scatter(1f, 0f);
                Emit(new Puff
                {
                    Position = at + Vector3.up * 0.5f, Velocity = offset.normalized * Range(r * 0.6f, r * 1.6f) + Vector3.up * Range(r * 0.7f, r * 1.7f),
                    Life = Range(0.9f, 1.6f), SizeFrom = Range(0.25f, 0.6f), SizeTo = 0.2f, Gravity = -16f,
                    From = new Color(0.13f, 0.12f, 0.11f), To = new Color(0.10f, 0.09f, 0.08f),
                });
            }

            for (int i = 0; i < 6; i++)
            {
                float angle = (i + Range(0f, 1f)) * Mathf.PI / 3f;
                var outward = new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle));
                Emit(new Puff
                {
                    Position = at + outward * r * 0.2f + Vector3.up * 0.4f, Velocity = outward * r * 1.3f + Vector3.up * 0.8f,
                    Life = Range(1.0f, 1.5f), SizeFrom = r * 0.14f, SizeTo = r * 0.42f, Drag = 2.2f,
                    From = new Color(0.50f, 0.44f, 0.36f), To = new Color(0.62f, 0.58f, 0.52f),
                });
            }

            if (!scorch) return;
            Transform mark = _scorches[_nextScorch];
            _nextScorch = (_nextScorch + 1) % ScorchCount;
            mark.position = at + Vector3.up * 0.07f;
            mark.localScale = new Vector3(r * 0.55f, 1f, r * 0.55f);
            mark.gameObject.SetActive(true);
        }

        /// <summary>A puff of exhaust behind a rocket in flight.</summary>
        public void Trail(Vector3 at)
        {
            Emit(new Puff
            {
                Position = at, Velocity = Scatter(0.3f, 0.3f), Life = 0.55f, SizeFrom = 0.16f, SizeTo = 0.6f,
                From = new Color(0.78f, 0.76f, 0.72f), To = new Color(0.50f, 0.50f, 0.51f),
            });
        }

        public void MuzzleFlash(Vector3 at, float size = 1.2f)
        {
            Emit(new Puff
            {
                Glow = true, Position = at, Life = 0.09f, SizeFrom = size * 0.6f, SizeTo = size * 1.4f,
                From = new Color(1f, 0.95f, 0.7f), To = new Color(1f, 0.6f, 0.2f),
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
            _fires.Add(new Fire { Position = at, Size = size, Left = seconds });
        }

        /// <summary>A fire that belongs to the island, not to a mission: it survives <see cref="ClearBattle"/>.</summary>
        public void BurnForEver(Vector3 at, float size, Transform parent)
        {
            Transform plume = Create("Plume", _plumeMesh, GameMaterials.PaletteLit);
            plume.SetParent(parent, true);
            plume.position = at;
            plume.rotation = Quaternion.Euler(0f, Range(0f, 360f), 0f);
            plume.localScale = Vector3.one * Mathf.Lerp(0.8f, 1.3f, (float)_random.NextDouble());
            _fires.Add(new Fire { Position = at, Size = size, Left = -1f, Plume = plume.gameObject, Scenery = true });
        }

        /// <summary>Puts out what a mission set alight and clears the air; the island's own fires stay.</summary>
        public void ClearBattle()
        {
            _fires.RemoveAll(fire => !fire.Scenery);
            for (int i = 0; i < PuffCount; i++)
            {
                _puffs[i].Active = false;
                _puffRenderers[i].enabled = false;
            }
            for (int i = 0; i < StreakCount; i++)
            {
                _streaks[i].Active = false;
                _streakTransforms[i].gameObject.SetActive(false);
            }
            foreach (Transform scorch in _scorches) scorch.gameObject.SetActive(false);
        }

        /// <summary>Forgets the island's fires too: the region is being replaced.</summary>
        public void ClearAll()
        {
            ClearBattle();
            _fires.Clear();
        }

        // ---------------------------------------------------------------- every frame

        public void Update(float deltaTime, Vector3 camera)
        {
            UpdateFires(deltaTime, camera);

            for (int i = 0; i < PuffCount; i++)
            {
                if (!_puffs[i].Active) continue;
                ref Puff puff = ref _puffs[i];
                puff.Age += deltaTime;
                if (puff.Age >= puff.Life)
                {
                    puff.Active = false;
                    _puffRenderers[i].enabled = false;
                    continue;
                }
                float share = puff.Age / puff.Life;
                puff.Velocity.y += puff.Gravity * deltaTime;
                puff.Velocity *= Mathf.Exp(-puff.Drag * deltaTime);
                puff.Position += (puff.Velocity + (puff.Glow ? Vector3.zero : Wind * 0.6f)) * deltaTime;

                // Grows quickly, lingers, then shrinks to nothing: no transparency needed.
                float grow = 1f - (1f - share) * (1f - share);
                float fade = 1f - Mathf.InverseLerp(0.72f, 1f, share);
                float size = Mathf.Lerp(puff.SizeFrom, puff.SizeTo, grow) * fade;
                Transform transform = _puffTransforms[i];
                transform.position = puff.Position;
                transform.localScale = new Vector3(size, size, size);
                transform.Rotate(0f, puff.Spin * deltaTime, 0f, Space.World);
                _block.SetColor(BaseColor, Color.Lerp(puff.From, puff.To, share));
                _puffRenderers[i].SetPropertyBlock(_block);
                _puffRenderers[i].enabled = true;
            }

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

        void UpdateFires(float deltaTime, Vector3 camera)
        {
            // Now and then, decide which fires are close enough to burn live.
            _sortClock -= deltaTime;
            if (_sortClock <= 0f)
            {
                _sortClock = 1f;
                _fires.Sort((a, b) => (a.Position - camera).sqrMagnitude.CompareTo((b.Position - camera).sqrMagnitude));
                for (int i = 0; i < _fires.Count; i++)
                {
                    Fire fire = _fires[i];
                    fire.Live = i < LiveFires && (fire.Position - camera).sqrMagnitude < 420f * 420f;
                    if (fire.Plume != null && fire.Plume.activeSelf == fire.Live) fire.Plume.SetActive(!fire.Live);
                }
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
                if (!fire.Live) continue;

                fire.FlameClock -= deltaTime;
                if (fire.FlameClock <= 0f)
                {
                    fire.FlameClock = 0.11f;
                    Emit(new Puff
                    {
                        Glow = true, Position = fire.Position + Scatter(fire.Size * 0.45f, fire.Size * 0.3f),
                        Velocity = Vector3.up * Range(1.5f, 3.5f), Life = Range(0.4f, 0.75f),
                        SizeFrom = fire.Size * 0.55f, SizeTo = fire.Size * 0.2f,
                        From = new Color(1f, 0.78f, 0.22f), To = new Color(0.85f, 0.22f, 0.05f),
                    });
                }
                fire.SmokeClock -= deltaTime;
                if (fire.SmokeClock <= 0f)
                {
                    fire.SmokeClock = fire.Scenery ? 0.5f : 0.35f;
                    Emit(new Puff
                    {
                        Position = fire.Position + Scatter(fire.Size * 0.3f, 0.5f) + Vector3.up * fire.Size * 0.6f,
                        Velocity = Vector3.up * Range(3.5f, 5.5f), Life = Range(6f, 9f),
                        SizeFrom = fire.Size * 0.6f, SizeTo = fire.Size * 2.6f, Drag = 0.12f,
                        From = new Color(0.09f, 0.085f, 0.08f), To = new Color(0.30f, 0.29f, 0.29f),
                    });
                }
            }
        }
    }
}
