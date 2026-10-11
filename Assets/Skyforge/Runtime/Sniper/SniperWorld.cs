using System.Collections.Generic;
using Skyforge;
using UnityEngine;
using UnityEngine.Rendering;

namespace Sniper
{
    /// <summary>A piece of scenery made into a prefab: how large the model is as it was made.</summary>
    public sealed class Prop : MonoBehaviour
    {
        public Vector3 Size = Vector3.one;
    }

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
            BuildGround(location);
            foreach (Piece piece in location.Pieces) Place(piece);
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
            var mesh = new Mesh { name = "Ground" };
            const float repeat = 5f;
            mesh.SetVertices(new List<Vector3> { new Vector3(-half, 0f, -half), new Vector3(-half, 0f, half), new Vector3(half, 0f, half), new Vector3(half, 0f, -half) });
            mesh.SetNormals(new List<Vector3> { Vector3.up, Vector3.up, Vector3.up, Vector3.up });
            mesh.SetTangents(new List<Vector4> { new Vector4(1f, 0f, 0f, -1f), new Vector4(1f, 0f, 0f, -1f), new Vector4(1f, 0f, 0f, -1f), new Vector4(1f, 0f, 0f, -1f) });
            float r = 2f * half / repeat;
            mesh.SetUVs(0, new List<Vector2> { new Vector2(0f, 0f), new Vector2(0f, r), new Vector2(r, r), new Vector2(r, 0f) });
            mesh.SetTriangles(new[] { 0, 1, 2, 0, 2, 3 }, 0);
            mesh.RecalculateBounds();
            var ground = new GameObject("Ground");
            ground.transform.SetParent(_root, false);
            ground.AddComponent<MeshFilter>().sharedMesh = mesh;
            var renderer = ground.AddComponent<MeshRenderer>();
            Material material = Resources.Load<Material>("Materials/Ground_" + location.Ground);
            renderer.sharedMaterial = material != null ? material : GameMaterials.Lit(new Color(0.4f, 0.4f, 0.4f));
            renderer.shadowCastingMode = ShadowCastingMode.Off;
        }

        /// <summary>What a model measures as it stands in the scene, and where its middle is: found once per model.</summary>
        static readonly Dictionary<string, Bounds> Measured = new Dictionary<string, Bounds>();

        void Place(Piece piece)
        {
            GameObject prefab = Load("Props", piece.Model);
            if (prefab == null) return;
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
            Vector3 size = bounds.size;
            // Stood on its own base, at its own middle.
            made.transform.localPosition = -new Vector3(bounds.center.x, bounds.min.y, bounds.center.z);
            float scale = piece.Height > 0f && size.y > 0.001f ? piece.Height / size.y : 1f;
            holder.localPosition = piece.At;
            holder.localRotation = Quaternion.Euler(0f, piece.YawDeg, 0f);
            holder.localScale = Vector3.one * scale;
            if (piece.Open) return;
            Vector3 solid = piece.Solid.x > 0f ? piece.Solid : size * scale;
            if (solid.y < 0.35f) return;
            Covers.Add(new Cover { Centre = piece.At + new Vector3(0f, solid.y * 0.5f, 0f), Size = solid, YawDeg = piece.YawDeg, Thin = piece.Thin });
        }

        public void Populate(SniperSim sim)
        {
            for (int i = 0; i < sim.ActorCount; i++)
            {
                ActorDef def = sim.Def(i);
                GameObject prefab = Load("Chars", def.Model);
                Actor actor = null;
                Transform root = new GameObject("Actor " + def.Id).transform;
                root.SetParent(_root, false);
                if (prefab != null)
                {
                    GameObject made = Object.Instantiate(prefab, root);
                    actor = made.GetComponent<Actor>();
                    float height = actor != null ? actor.Height : 1.8f;
                    made.transform.localScale = Vector3.one * (1.8f / Mathf.Max(0.01f, height));
                }
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
                root.localRotation = Quaternion.Euler(0f, state.YawDeg, 0f);
                Actor actor = _actors[i];
                if (actor == null || _shownClips[i] == state.Clip) continue;
                _shownClips[i] = state.Clip;
                if (!actor.Play(state.Clip, state.Loop) && !actor.Play(state.Mood == Mood.Dead ? "Death" : state.Clip == "Run" ? "Run" : "Idle", state.Loop))
                    actor.Play("Idle", true);
                actor.Speed = state.Clip == "Run" ? 1.25f : 1f;
            }
        }

        public Vector3 Head(SniperSim sim, int index) => sim.Actor(index).Position + new Vector3(0f, SniperSim.HeadHeight, 0f);
    }
}
