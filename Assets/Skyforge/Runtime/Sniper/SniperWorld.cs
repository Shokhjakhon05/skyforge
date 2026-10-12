using System.Collections.Generic;
using Skyforge;
using Skyforge.World;
using UnityEngine;
using UnityEngine.Rendering;

namespace Sniper
{
    /// <summary>The place a mission happens in, built from the location's list of pieces, and the people in it.</summary>
    public sealed class SniperWorld
    {
        readonly Transform _root;
        readonly List<Actor> _actors = new List<Actor>();
        readonly List<Transform> _actorRoots = new List<Transform>();
        readonly List<string> _shownClips = new List<string>();
        public readonly List<Cover> Covers = new List<Cover>();
        static readonly Dictionary<string, GameObject> Prefabs = new Dictionary<string, GameObject>();

        public SniperWorld(Transform parent, LocationDef location)
        {
            _root = new GameObject("World").transform;
            _root.SetParent(parent, false);
            _night = location.Light == "night";
            BuildGround(location);
            foreach (Piece piece in location.Pieces) Place(piece);
            StartTraffic(location);
        }

        public void Destroy()
        {
            if (_root != null) Object.Destroy(_root.gameObject);
        }

        static GameObject Load(string folder, string name)
        {
            string key = folder + "/" + name;
            if (Prefabs.TryGetValue(key, out GameObject cached) && cached != null) return cached;
            var prefab = Resources.Load<GameObject>(key);
            if (prefab == null) Debug.LogWarning("[Sniper] Missing " + key);
            Prefabs[key] = prefab;
            return prefab;
        }

        void BuildGround(LocationDef location)
        {
            float half = location.HalfSize;
            bool shore = location.WaterZ > 0f;
            float far = shore ? location.WaterZ : half;
            Material material = Resources.Load<Material>("Materials/Ground_" + location.Ground);
            Material laid = material != null ? material : GameMaterials.Lit(new Color(0.4f, 0.4f, 0.4f));
            // Laid in slabs: a light reaches only the slabs near it, and no slab has to answer to every lamp on the map.
            const float slab = 65f;
            for (float x = -half; x < half - 0.1f; x += slab)
            for (float z = -half; z < far - 0.1f; z += slab)
            {
                float x1 = Mathf.Min(half, x + slab), z1 = Mathf.Min(far, z + slab);
                Sheet("Ground", new Vector3(x, 0f, z), new Vector3(x, 0f, z1), new Vector3(x1, 0f, z1), new Vector3(x1, 0f, z), Vector3.up, 5f, laid, new Vector2(x, z));
            }
            if (!shore) return;
            // The quay ends in a wall, and the sea lies below it out to the haze.
            Material wall = Resources.Load<Material>("Materials/Ground_asphalt");
            Sheet("Quay", new Vector3(-half, -3f, far), new Vector3(-half, 0f, far), new Vector3(half, 0f, far), new Vector3(half, -3f, far), Vector3.forward, 5f,
                wall != null ? wall : GameMaterials.Lit(new Color(0.3f, 0.3f, 0.3f)));
            float out_ = 900f;
            Material sea = GameMaterials.Water;
            Sheet("Sea", new Vector3(-out_, -1.6f, far - 2f), new Vector3(-out_, -1.6f, out_), new Vector3(out_, -1.6f, out_), new Vector3(out_, -1.6f, far - 2f), Vector3.up, 40f,
                sea != null ? sea : GameMaterials.Lit(new Color(0.08f, 0.2f, 0.3f), 0.8f));
        }

        void Sheet(string name, Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector3 normal, float repeat, Material material, Vector2 origin = default)
        {
            var mesh = new Mesh { name = name };
            mesh.SetVertices(new List<Vector3> { a, b, c, d });
            mesh.SetNormals(new List<Vector3> { normal, normal, normal, normal });
            var tangent = new Vector4(1f, 0f, 0f, -1f);
            mesh.SetTangents(new List<Vector4> { tangent, tangent, tangent, tangent });
            float u = (d - a).magnitude / repeat, v = (b - a).magnitude / repeat;
            // The picture carries on from one slab to the next.
            float u0 = origin.x / repeat, v0 = origin.y / repeat;
            mesh.SetUVs(0, new List<Vector2> { new Vector2(u0, v0), new Vector2(u0, v0 + v), new Vector2(u0 + u, v0 + v), new Vector2(u0 + u, v0) });
            bool flip = Vector3.Dot(Vector3.Cross(b - a, c - a), normal) < 0f;
            mesh.SetTriangles(flip ? new[] { 0, 2, 1, 0, 3, 2 } : new[] { 0, 1, 2, 0, 2, 3 }, 0);
            mesh.RecalculateBounds();
            var sheet = new GameObject(name);
            sheet.transform.SetParent(_root, false);
            sheet.AddComponent<MeshFilter>().sharedMesh = mesh;
            var renderer = sheet.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
        }

        /// <summary>What a model measures as it stands in the scene, and where its middle is: found once per model.</summary>
        static readonly Dictionary<string, Bounds> Measured = new Dictionary<string, Bounds>();

        static readonly Dictionary<string, Material> Paints = new Dictionary<string, Material>();

        /// <summary>A plain painted block: a stripe on a runway, a platform edge, a drift of snow.</summary>
        void Block(Piece piece)
        {
            string tint = string.IsNullOrEmpty(piece.Tint) ? "808080" : piece.Tint;
            if (!Paints.TryGetValue(tint, out Material paint) || paint == null)
            {
                Color colour = Color.gray;
                ColorUtility.TryParseHtmlString("#" + tint, out colour);
                paint = GameMaterials.Lit(colour, 0.12f);
                // Lamps and lit windows are painted with a colour that glows.
                if (tint.EndsWith("ff") && tint.Length == 8)
                {
                    paint.EnableKeyword("_EMISSION");
                    paint.SetColor("_EmissionColor", colour * 2.2f);
                }
                Paints[tint] = paint;
            }
            var builder = new MeshBuilder();
            builder.Box(new Vector3(0f, piece.Solid.y * 0.5f, 0f), piece.Solid, Palette.White);
            var block = new GameObject("Block");
            block.transform.SetParent(_root, false);
            block.transform.localPosition = piece.At;
            block.transform.localRotation = Quaternion.Euler(0f, piece.YawDeg, 0f);
            block.AddComponent<MeshFilter>().sharedMesh = builder.Build("Block");
            var renderer = block.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = paint;
            renderer.shadowCastingMode = piece.Solid.y > 0.4f ? ShadowCastingMode.On : ShadowCastingMode.Off;
            if (piece.Open || piece.Solid.y < 0.35f) return;
            Covers.Add(new Cover { Centre = piece.At + new Vector3(0f, piece.Solid.y * 0.5f, 0f), Size = piece.Solid, YawDeg = piece.YawDeg, Thin = piece.Thin });
        }

        void Place(Piece piece)
        {
            if (piece.Model == "box")
            {
                Block(piece);
                return;
            }
            Transform holder = Stand(piece.Model, piece.Height, out Vector3 size, out float scale, piece.Scale);
            if (holder == null) return;
            if (_night && (piece.Model == "light" || piece.Model == "floodlight")) Lamp(holder, scale);
            holder.localPosition = piece.At;
            holder.localRotation = Quaternion.Euler(0f, piece.YawDeg, 0f);
            if (piece.Open) return;
            Vector3 solid = piece.Solid.x > 0f ? piece.Solid : size * scale;
            if (solid.y < 0.35f) return;
            var cover = new Cover { Centre = piece.At + new Vector3(0f, solid.y * 0.5f, 0f), Size = solid, YawDeg = piece.YawDeg, Thin = piece.Thin };
            if (piece.Explosive)
            {
                cover.Explosive = true;
                cover.Tag = _drums.Count;
                _drums.Add(holder.gameObject);
                // Marked, so the shooter can tell a drum of fuel from a drum of water.
                foreach (Renderer renderer in holder.GetComponentsInChildren<Renderer>()) renderer.sharedMaterial = DrumPaint;
            }
            Covers.Add(cover);
        }

        readonly List<GameObject> _drums = new List<GameObject>();
        static Material _drumPaint;
        static Material DrumPaint => _drumPaint != null ? _drumPaint : _drumPaint = GameMaterials.Lit(new Color(0.78f, 0.12f, 0.08f), 0.3f);

        /// <summary>The drum that went up is gone from the scene.</summary>
        public void Blow(int tag)
        {
            if (tag >= 0 && tag < _drums.Count && _drums[tag] != null) _drums[tag].SetActive(false);
        }

        /// <summary>A model stood on its base at its middle, scaled to a height, under a holder that can be put anywhere.</summary>
        Transform Stand(string model, float height, out Vector3 size, out float scale, float multiplier = 0f)
        {
            size = Vector3.one;
            scale = 1f;
            Piece piece = new Piece { Model = model, Height = height };
            GameObject prefab = Load("Props", piece.Model);
            if (prefab == null) return null;
            Transform holder = new GameObject(piece.Model).transform;
            holder.SetParent(_root, false);
            GameObject made = Object.Instantiate(prefab, holder);
            made.transform.localPosition = Vector3.zero;
            made.transform.localRotation = Quaternion.identity;
            made.transform.localScale = Vector3.one;
            if (!Measured.TryGetValue(piece.Model, out Bounds bounds))
            {
                // Measured here, on the thing as it will be drawn, rather than trusted from the import.
                Renderer[] renderers = made.GetComponentsInChildren<Renderer>();
                bounds = renderers.Length > 0 ? renderers[0].bounds : new Bounds(Vector3.zero, Vector3.one);
                foreach (Renderer renderer in renderers) bounds.Encapsulate(renderer.bounds);
                Measured[piece.Model] = bounds;
                Debug.Log("[Sniper] prop " + piece.Model + " measures " + bounds.size + " about " + bounds.center);
            }
            size = bounds.size;
            // Stood on its own base, at its own middle.
            made.transform.localPosition = -new Vector3(bounds.center.x, bounds.min.y, bounds.center.z);
            scale = multiplier > 0f ? multiplier : piece.Height > 0f && size.y > 0.001f ? piece.Height / size.y : 1f;
            holder.localScale = Vector3.one * scale;
            return holder;
        }

        bool _night;
        int _lamps;

        /// <summary>At night a lamp post lights the ground under it. A phone draws only so many lights, so the count is kept low.</summary>
        void Lamp(Transform holder, float scale)
        {
            if (_lamps >= 14) return;
            _lamps++;
            var lamp = new GameObject("Lamp");
            lamp.transform.SetParent(holder, false);
            lamp.transform.localPosition = new Vector3(0f, 5.2f, 1.4f);
            var light = lamp.AddComponent<Light>();
            light.type = LightType.Point;
            light.color = new Color(1f, 0.86f, 0.62f);
            light.range = 30f / Mathf.Max(0.2f, scale);
            light.intensity = 9f;
            light.shadows = LightShadows.None;
        }

        sealed class Mover
        {
            public Transform Body;
            public Traffic Way;
            public float Along;
        }

        readonly List<Mover> _movers = new List<Mover>();

        void StartTraffic(LocationDef location)
        {
            foreach (Traffic way in location.Traffic)
            {
                Transform body = Stand(way.Model, way.Height, out Vector3 _, out float _);
                if (body == null) continue;
                Vector3 path = way.To - way.From;
                body.localRotation = Quaternion.Euler(0f, Mathf.Atan2(path.x, path.z) * Mathf.Rad2Deg, 0f);
                _movers.Add(new Mover { Body = body, Way = way, Along = Mathf.Repeat(way.Offset, 1f) });
            }
            Move(0f);
        }

        /// <summary>Cars along their street, a boat across the bay: round and round.</summary>
        public void Move(float deltaTime)
        {
            foreach (Mover mover in _movers)
            {
                float length = Mathf.Max(1f, (mover.Way.To - mover.Way.From).magnitude);
                mover.Along = Mathf.Repeat(mover.Along + mover.Way.Speed * deltaTime / length, 1f);
                mover.Body.localPosition = Vector3.Lerp(mover.Way.From, mover.Way.To, mover.Along);
            }
        }

        public void Populate(SniperSim sim)
        {
            for (int i = 0; i < sim.ActorCount; i++)
            {
                ActorDef def = sim.Def(i);
                GameObject prefab = Load("Chars", def.Model);
                Actor actor = null;
                HeldRifle rifle = null;
                Transform root = new GameObject("Actor " + def.Id).transform;
                root.SetParent(_root, false);
                if (prefab != null)
                {
                    GameObject made = Object.Instantiate(prefab, root);
                    actor = made.GetComponent<Actor>();
                    float height = actor != null ? actor.Height : 1.8f;
                    made.transform.localScale = Vector3.one * (1.8f / Mathf.Max(0.01f, height));
                    if (actor != null && actor.RightHand != null && actor.LeftHand != null && def.Idle != null && def.Idle.StartsWith("Idle_Gun")) rifle = Arm(actor, root);
                }
                _rifles.Add(rifle);
                _actors.Add(actor);
                _actorRoots.Add(root);
                _shownClips.Add(null);
            }
            Present(sim);
        }

        /// <summary>Puts every man where the mission has him, doing what it has him doing.</summary>
        public void Present(SniperSim sim)
        {
            for (int i = 0; i < _actorRoots.Count; i++)
            {
                ActorState state = sim.Actor(i);
                Transform root = _actorRoots[i];
                bool shown = state.Mood != Mood.Gone;
                if (root.gameObject.activeSelf != shown) root.gameObject.SetActive(shown);
                if (!shown) continue;
                root.localPosition = state.Position;
                Quaternion facing = Quaternion.Euler(0f, state.YawDeg, 0f);
                root.localRotation = _shownClips[i] == null ? facing : Quaternion.RotateTowards(root.localRotation, facing, 420f * Time.deltaTime);
                Actor actor = _actors[i];
                if (state.Mood == Mood.Dead && _rifles[i] != null && !_rifles[i].Dropped) _rifles[i].Drop(0.45f);
                if (actor == null || _shownClips[i] == state.Clip) continue;
                _shownClips[i] = state.Clip;
                // A model without the clip asked for plays the nearest it has.
                string clip = state.Clip ?? "Idle";
                bool played = actor.Play(clip, state.Loop);
                if (!played && clip.StartsWith("Death")) played = actor.Play("Death", false);
                if (!played && clip.EndsWith("_Gun")) played = actor.Play(clip.Substring(0, clip.Length - 4), state.Loop);
                if (!played && clip == "HitReact") played = actor.Play("HitRecieve", false);
                if (!played && state.Mood != Mood.Dead) actor.Play("Idle", true);
                actor.Speed = clip.StartsWith("Run") && actor.Hips == null ? 1.25f : 1f;
            }
        }

        readonly List<HeldRifle> _rifles = new List<HeldRifle>();

        /// <summary>A rifle for an armed man: it lies along the line from his right hand to his left, as he holds it.</summary>
        HeldRifle Arm(Actor actor, Transform root)
        {
            GameObject prefab = Load("Props", "rifle1");
            if (prefab == null) return null;
            Transform holder = new GameObject("Rifle").transform;
            holder.SetParent(root, false);
            GameObject made = Object.Instantiate(prefab, holder);
            made.transform.localPosition = Vector3.zero;
            made.transform.localRotation = Quaternion.identity;
            made.transform.localScale = Vector3.one;
            Renderer[] renderers = made.GetComponentsInChildren<Renderer>();
            if (renderers.Length == 0) return null;
            Bounds bounds = renderers[0].bounds;
            foreach (Renderer renderer in renderers) bounds.Encapsulate(renderer.bounds);
            // Whichever way the model was drawn, its length is laid along the holder's forward.
            Vector3 size = bounds.size;
            Vector3 along = size.x >= size.y && size.x >= size.z ? Vector3.right : size.z >= size.y ? Vector3.forward : Vector3.up;
            float length = Mathf.Max(0.001f, Mathf.Max(size.x, Mathf.Max(size.y, size.z)));
            Quaternion turn = Quaternion.Inverse(holder.rotation) * Quaternion.FromToRotation(along, Vector3.forward);
            made.transform.localRotation = turn;
            float scale = 0.95f / length;
            made.transform.localScale = Vector3.one * scale;
            Vector3 centre = holder.InverseTransformPoint(bounds.center);
            made.transform.localPosition = -(turn * centre) * scale + new Vector3(0f, 0f, 0.16f);
            foreach (Renderer renderer in renderers) renderer.sharedMaterial = RiflePaint;
            var held = holder.gameObject.AddComponent<HeldRifle>();
            held.Right = actor.RightHand;
            held.Left = actor.LeftHand;
            return held;
        }

        static Material _riflePaint;
        static Material RiflePaint => _riflePaint != null ? _riflePaint : _riflePaint = GameMaterials.Lit(new Color(0.07f, 0.07f, 0.08f), 0.35f);

        public Vector3 Head(SniperSim sim, int index) => sim.Actor(index).Position + new Vector3(0f, SniperSim.HeadHeight, 0f);
    }

    /// <summary>Keeps a rifle in a man's hands whatever his arms are doing, and lets it fall when he does.</summary>
    public sealed class HeldRifle : MonoBehaviour
    {
        public Transform Right, Left;
        public bool Dropped { get; private set; }
        float _fall = -1f;

        public void Drop(float after)
        {
            Dropped = true;
            _fall = after;
        }

        void LateUpdate()
        {
            if (Right == null || Left == null) return;
            if (Dropped)
            {
                if (_fall < 0f) return;
                _fall -= Time.deltaTime;
                if (_fall >= 0f) return;
                // On the ground beside him, flat.
                Vector3 at = transform.position;
                transform.position = new Vector3(at.x, transform.parent != null ? transform.parent.position.y + 0.06f : 0.06f, at.z);
                transform.rotation = Quaternion.Euler(0f, transform.eulerAngles.y, 90f);
                return;
            }
            Vector3 line = Left.position - Right.position;
            if (line.sqrMagnitude < 0.0009f) return;
            transform.position = Right.position;
            transform.rotation = Quaternion.LookRotation(line.normalized, Vector3.up);
        }
    }
}
