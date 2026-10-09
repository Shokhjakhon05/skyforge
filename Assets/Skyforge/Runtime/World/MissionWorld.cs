using System.Collections.Generic;
using Skyforge.Core;
using UnityEngine;
using UnityEngine.Rendering;

namespace Skyforge.World
{
    /// <summary>Loads the mission files that ship with the game and keeps the ones that are valid.</summary>
    public static class MissionCatalog
    {
        [System.Serializable]
        sealed class IdOnly
        {
            public string id;
        }

        static readonly Dictionary<string, List<string>> IdCache = new Dictionary<string, List<string>>();

        /// <summary>The ids of a region's missions, without building its island.</summary>
        public static List<string> Ids(RegionDefinition region)
        {
            if (IdCache.TryGetValue(region.Id, out List<string> ids)) return ids;
            ids = new List<string>();
            foreach (TextAsset file in Resources.LoadAll<TextAsset>("Missions/" + region.Id))
            {
                var parsed = JsonUtility.FromJson<IdOnly>(file.text);
                if (parsed != null && !string.IsNullOrEmpty(parsed.id)) ids.Add(parsed.id);
            }
            IdCache[region.Id] = ids;
            return ids;
        }

        public static List<MissionDefinition> Load(RegionDefinition region, Island island)
        {
            var missions = new List<MissionDefinition>();
            var errors = new List<string>();
            foreach (TextAsset file in Resources.LoadAll<TextAsset>("Missions/" + region.Id))
            {
                MissionDefinition mission = MissionBuilder.Parse(file.text, island, errors);
                if (mission != null) missions.Add(mission);
            }
            // A broken file costs the player that one mission, never the campaign.
            foreach (string error in errors) Debug.LogError("[Skyforge] Mission skipped - " + error);
            missions.Sort((a, b) => a.Order.CompareTo(b.Order));
            return missions;
        }
    }

    /// <summary>
    /// Everything a mission puts into the world: props, moving actors, gates and hover rings,
    /// radar ceilings, threat markers, the strike aiming ring and the munition.
    /// </summary>
    public sealed class MissionWorld
    {
        readonly Transform _parent;
        readonly HeightMap _map;
        readonly Material _current, _later, _done, _danger;
        readonly Mesh _ringMesh, _thinRingMesh, _ballMesh;
        readonly List<GameObject> _spawned = new List<GameObject>();
        readonly List<Renderer> _objectiveRings = new List<Renderer>();
        readonly List<int> _ringObjective = new List<int>();
        readonly List<Renderer> _threatRings = new List<Renderer>();
        readonly List<Transform> _actors = new List<Transform>();
        Transform _aimRing, _munition, _blast;
        float _blastAge = -1f;
        MissionDefinition _mission;

        public MissionWorld(Transform parent, HeightMap map)
        {
            _parent = parent;
            _map = map;
            _current = GameMaterials.Unlit(new Color(1f, 0.62f, 0.10f));
            _later = GameMaterials.Unlit(new Color(0.75f, 0.85f, 0.95f));
            _done = GameMaterials.Unlit(new Color(0.35f, 0.85f, 0.45f));
            _danger = GameMaterials.Unlit(new Color(0.95f, 0.22f, 0.18f));

            var ring = new MeshBuilder();
            ring.Torus(1f, 0.05f, 40, 5, Palette.White);
            _ringMesh = ring.Build("UnitRing");

            // Radar rings are hundreds of metres across; a thinner tube keeps them from becoming a wall.
            var thin = new MeshBuilder();
            thin.Torus(1f, 0.012f, 64, 4, Palette.White);
            _thinRingMesh = thin.Build("ThinUnitRing");

            var ball = new MeshBuilder();
            ball.Cylinder(Vector3.down * 0.5f, 0.5f, 0.5f, 1f, 10, Palette.White);
            _ballMesh = ball.Build("UnitDrum");
        }

        public void Show(MissionDefinition mission)
        {
            Hide();
            _mission = mission;

            var props = new MeshBuilder();
            foreach (PropDefinition prop in mission.Props) PropModels.Add(props, prop.Kind, prop.Position);
            foreach (ZoneDefinition zone in mission.Zones) PropModels.Add(props, "radar", zone.Position);
            if (props.VertexCount > 0) Spawn("Props", props.Build("MissionProps"), GameMaterials.PaletteLit, true);

            foreach (ActorDefinition actor in mission.Actors)
            {
                var model = new MeshBuilder();
                PropModels.Add(model, actor.Kind, Vector3.zero);
                _actors.Add(Spawn("Actor_" + actor.Id, model.Build("Actor"), GameMaterials.PaletteLit, true).transform);
            }

            // A radar's ring hangs at its floor: below it the drone is unseen.
            foreach (ZoneDefinition zone in mission.Zones)
            {
                Transform ceiling = Spawn("RadarCeiling", _thinRingMesh, _danger, false).transform;
                ceiling.SetPositionAndRotation(zone.Position + Vector3.up * zone.Floor, Quaternion.Euler(90f, 0f, 0f));
                ceiling.localScale = Vector3.one * zone.Radius;
            }

            Vector3 previous = mission.BasePosition;
            for (int i = 0; i < mission.Objectives.Count; i++)
            {
                ObjectiveDefinition objective = mission.Objectives[i];
                if (objective.Type == ObjectiveType.Scan)
                {
                    Renderer zone = Spawn("ScanRing", _ringMesh, _later, false);
                    zone.transform.SetPositionAndRotation(objective.Position, Quaternion.Euler(90f, 0f, 0f));
                    zone.transform.localScale = Vector3.one * objective.Radius;
                    Register(zone, i);
                }
                else if (objective.Type == ObjectiveType.Reach && objective.Gate)
                {
                    Vector3 direction = objective.Position - previous;
                    direction.y = 0f;
                    if (direction.sqrMagnitude < 1f) direction = Vector3.forward;
                    Renderer gate = Spawn("Gate", _ringMesh, _later, false);
                    gate.transform.SetPositionAndRotation(objective.Position, Quaternion.LookRotation(direction.normalized, Vector3.up));
                    gate.transform.localScale = Vector3.one * objective.Radius;
                    Register(gate, i);
                }
                else if (objective.Type == ObjectiveType.Mark)
                {
                    foreach (Vector3 threat in objective.Targets)
                    {
                        Renderer mark = Spawn("ThreatRing", _ringMesh, _danger, false);
                        mark.transform.SetPositionAndRotation(threat + Vector3.up * 7f, Quaternion.Euler(90f, 0f, 0f));
                        mark.transform.localScale = Vector3.one * 5f;
                        _threatRings.Add(mark);
                    }
                }
                if (string.IsNullOrEmpty(objective.ActorId) && objective.Type != ObjectiveType.TakeOff &&
                    objective.Type != ObjectiveType.Mark && objective.Type != ObjectiveType.Arrive)
                    previous = objective.Position;
            }

            _aimRing = Spawn("AimRing", _ringMesh, _danger, false).transform;
            _aimRing.rotation = Quaternion.Euler(90f, 0f, 0f);
            _munition = Spawn("Munition", _ballMesh, _danger, false).transform;
            _munition.localScale = new Vector3(0.5f, 0.9f, 0.5f);
            _blast = Spawn("Blast", _ballMesh, _current, false).transform;
            _aimRing.gameObject.SetActive(false);
            _munition.gameObject.SetActive(false);
            _blast.gameObject.SetActive(false);
            _blastAge = -1f;
        }

        public void Hide()
        {
            foreach (GameObject obj in _spawned) Object.Destroy(obj);
            _spawned.Clear();
            _objectiveRings.Clear();
            _ringObjective.Clear();
            _threatRings.Clear();
            _actors.Clear();
            _mission = null;
        }

        /// <summary>Follows a running mission: moves actors, recolours rings, flies the munition.</summary>
        public void Update(MissionRun run, FlightState drone, float deltaTime)
        {
            if (_mission == null) return;

            for (int i = 0; i < _actors.Count; i++)
            {
                ActorState actor = run.Actor(i);
                Vector3 normal = _map.Normal(actor.Position.x, actor.Position.z);
                Vector3 forward = Vector3.ProjectOnPlane(actor.Forward, normal);
                if (forward.sqrMagnitude < 0.01f) forward = actor.Forward;
                _actors[i].SetPositionAndRotation(actor.Position, Quaternion.LookRotation(forward.normalized, normal));
            }

            for (int i = 0; i < _objectiveRings.Count; i++)
            {
                int objective = _ringObjective[i];
                _objectiveRings[i].enabled = objective >= run.ObjectiveIndex || _mission.Objectives[objective].Type == ObjectiveType.Scan;
                _objectiveRings[i].sharedMaterial =
                    objective < run.ObjectiveIndex ? _done : objective == run.ObjectiveIndex ? _current : _later;
            }

            ObjectiveDefinition current = run.CurrentObjective;
            bool marking = current != null && current.Type == ObjectiveType.Mark;
            for (int i = 0; i < _threatRings.Count; i++)
                _threatRings[i].sharedMaterial = !marking || run.IsMarked(i) ? _done : _danger;

            // Strike: a ring on the ground shows where a release would land right now.
            bool aiming = current != null && current.Type == ObjectiveType.Strike && run.ShotsLeft > 0 &&
                          !run.Shot.Active && run.Status == MissionStatus.InProgress;
            if (_aimRing.gameObject.activeSelf != aiming) _aimRing.gameObject.SetActive(aiming);
            if (aiming)
            {
                Vector3 impact = run.PredictImpact(drone, out _);
                _aimRing.position = impact + Vector3.up * 1.4f;
                _aimRing.localScale = Vector3.one * Mathf.Max(3f, current.Radius);
            }

            Shot shot = run.Shot;
            if (_munition.gameObject.activeSelf != shot.Active) _munition.gameObject.SetActive(shot.Active);
            if (shot.Active)
            {
                float t = Mathf.Clamp01(shot.Elapsed / Mathf.Max(0.01f, shot.Duration));
                // Constant speed sideways, accelerating downwards: the path of a dropped object.
                Vector3 position = Vector3.Lerp(shot.From, shot.Impact, t);
                position.y = Mathf.Lerp(shot.From.y, shot.Impact.y, t * t);
                _munition.position = position;
            }
            if (run.ShotLanded)
            {
                _blast.position = shot.Impact;
                _blast.gameObject.SetActive(true);
                _blastAge = 0f;
            }
            if (_blastAge >= 0f)
            {
                _blastAge += deltaTime;
                float size = Mathf.Lerp(2f, 16f, Mathf.Sqrt(Mathf.Clamp01(_blastAge / 0.5f)));
                _blast.localScale = new Vector3(size, size * 0.6f, size);
                if (_blastAge > 0.6f)
                {
                    _blast.gameObject.SetActive(false);
                    _blastAge = -1f;
                }
            }
        }

        void Register(Renderer ring, int objective)
        {
            _objectiveRings.Add(ring);
            _ringObjective.Add(objective);
        }

        Renderer Spawn(string name, Mesh mesh, Material material, bool castShadows)
        {
            var obj = new GameObject(name);
            obj.transform.SetParent(_parent, false);
            obj.AddComponent<MeshFilter>().sharedMesh = mesh;
            var renderer = obj.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = castShadows ? ShadowCastingMode.On : ShadowCastingMode.Off;
            _spawned.Add(obj);
            return renderer;
        }
    }

    /// <summary>Simple models for the things missions are about, built from boxes and cylinders.</summary>
    public static class PropModels
    {
        public static void Add(MeshBuilder b, string kind, Vector3 at)
        {
            switch (kind)
            {
                case "antenna":
                    b.Cylinder(at - Vector3.up, 1.4f, 1.1f, 1.4f, 8, Palette.Concrete);
                    b.Cylinder(at, 0.3f, 0.15f, 11f, 6, Palette.LightGrey);
                    b.Box(at + Vector3.up * 6f, new Vector3(1.4f, 1f, 1.4f), Palette.Orange);
                    b.Box(at + Vector3.up * 9.5f, new Vector3(3f, 0.12f, 0.12f), Palette.DarkGrey);
                    b.Box(at + Vector3.up * 11.2f, new Vector3(0.45f, 0.45f, 0.45f), Palette.Red);
                    break;
                case "crates":
                    b.Box(at + new Vector3(0f, 0.75f, 0f), new Vector3(1.5f, 1.5f, 1.5f), Palette.Trunk);
                    b.Box(at + new Vector3(1.7f, 0.75f, 0.3f), new Vector3(1.5f, 1.5f, 1.5f), Palette.Trunk);
                    b.Box(at + new Vector3(0.8f, 2.2f, 0.1f), new Vector3(1.4f, 1.4f, 1.4f), Palette.Orange);
                    b.Box(at + new Vector3(-1.9f, 0.5f, -0.6f), new Vector3(1f, 1f, 2.2f), Palette.DarkGrey);
                    break;
                case "camp":
                    b.Cylinder(at + new Vector3(-3.5f, 0f, 0f), 2.6f, 0f, 2.6f, 4, Palette.Concrete);
                    b.Cylinder(at + new Vector3(3.5f, 0f, 1.5f), 2.6f, 0f, 2.6f, 4, Palette.LeafDark);
                    b.Cylinder(at + new Vector3(0f, 0f, -4.5f), 2.2f, 0f, 2.2f, 4, Palette.Concrete);
                    b.Box(at + new Vector3(0.5f, 0.5f, 3.5f), new Vector3(1f, 1f, 1f), Palette.Trunk);
                    b.Cylinder(at + new Vector3(0f, 0f, 0f), 0.6f, 0.4f, 0.3f, 6, Palette.Orange);
                    break;
                case "vehicle":
                case "truck":
                case "car":
                    int cab = kind == "vehicle" ? Palette.DarkGrey : kind == "truck" ? Palette.LeafDark : Palette.White;
                    b.Box(at + new Vector3(0f, 1.6f, -0.9f), new Vector3(2.3f, 2.1f, 4.2f), cab);
                    b.Box(at + new Vector3(0f, 1.3f, 2.1f), new Vector3(2.2f, 1.5f, 1.7f), Palette.LightGrey);
                    b.Box(at + new Vector3(0f, 1.75f, 2.5f), new Vector3(1.9f, 0.5f, 0.7f), Palette.Lens);
                    foreach (float z in new[] { -2.2f, 1.9f })
                    foreach (float x in new[] { -1.1f, 1.1f })
                        b.Box(at + new Vector3(x, 0.45f, z), new Vector3(0.4f, 0.9f, 0.9f), Palette.Graphite);
                    break;
                case "boat":
                    b.Box(at + new Vector3(0f, 0.5f, 0f), new Vector3(3f, 1.4f, 9f), Palette.White);
                    b.Box(at + new Vector3(0f, 1.9f, -1.2f), new Vector3(2.2f, 1.5f, 3f), Palette.Orange);
                    b.Cylinder(at + new Vector3(0f, 2.6f, -1.2f), 0.08f, 0.05f, 2.4f, 5, Palette.DarkGrey);
                    break;
                case "transmitter":
                    b.Box(at + new Vector3(0f, 0.6f, 0f), new Vector3(1.6f, 1.2f, 1.1f), Palette.Graphite);
                    b.Cylinder(at + new Vector3(0.4f, 1.2f, 0f), 0.06f, 0.03f, 3.2f, 5, Palette.LightGrey);
                    b.Box(at + new Vector3(-0.4f, 1.3f, 0f), new Vector3(0.25f, 0.25f, 0.25f), Palette.Red);
                    break;
                case "threat":
                    b.Box(at + new Vector3(-2.2f, 0.6f, 0f), new Vector3(2.6f, 1.2f, 0.9f), Palette.Graphite);
                    b.Box(at + new Vector3(2.2f, 0.6f, 0.4f), new Vector3(2.6f, 1.2f, 0.9f), Palette.Graphite);
                    b.Box(at + new Vector3(0f, 1f, -2.4f), new Vector3(2f, 2f, 3.2f), Palette.DarkGrey);
                    b.Box(at + new Vector3(0f, 2.3f, -2.4f), new Vector3(0.5f, 0.5f, 0.5f), Palette.Red);
                    break;
                case "pad":
                    b.Cylinder(at - Vector3.up * 1.2f, 6f, 6f, 1.25f, 20, Palette.Concrete);
                    b.Cylinder(at + Vector3.up * 0.05f, 4.4f, 4.4f, 0.02f, 20, Palette.Orange);
                    b.Cylinder(at + Vector3.up * 0.05f, 3.6f, 3.6f, 0.03f, 20, Palette.White);
                    break;
                case "radar":
                    b.Cylinder(at - Vector3.up, 1.6f, 1.2f, 1.5f, 8, Palette.Concrete);
                    b.Cylinder(at, 0.35f, 0.3f, 7f, 6, Palette.DarkGrey);
                    b.Cylinder(at + Vector3.up * 7f, 0.4f, 2.4f, 1.3f, 10, Palette.Red);
                    break;
            }
        }
    }
}
