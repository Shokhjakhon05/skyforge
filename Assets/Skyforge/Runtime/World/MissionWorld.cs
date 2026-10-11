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
            public int order;
            public int baseReward;
        }

        static readonly Dictionary<string, List<KeyValuePair<string, int>>> SummaryCache =
            new Dictionary<string, List<KeyValuePair<string, int>>>();

        /// <summary>Id and base reward of a region's missions in campaign order, without building its island.</summary>
        public static List<KeyValuePair<string, int>> Summaries(RegionDefinition region)
        {
            if (SummaryCache.TryGetValue(region.Id, out List<KeyValuePair<string, int>> summaries)) return summaries;
            var parsed = new List<IdOnly>();
            foreach (TextAsset file in Resources.LoadAll<TextAsset>("Missions/" + region.Id))
            {
                var entry = JsonUtility.FromJson<IdOnly>(file.text);
                if (entry != null && !string.IsNullOrEmpty(entry.id)) parsed.Add(entry);
            }
            parsed.Sort((a, b) => a.order.CompareTo(b.order));
            summaries = new List<KeyValuePair<string, int>>();
            foreach (IdOnly entry in parsed) summaries.Add(new KeyValuePair<string, int>(entry.id, entry.baseReward));
            SummaryCache[region.Id] = summaries;
            return summaries;
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

        /// <summary>One target in the world: its model, the part that tracks the drone, and whether it has burnt yet.</summary>
        sealed class TargetView
        {
            public Transform Root, Head, Rotor;
            public Renderer[] Renderers;
            public Model Model;
            public bool Dead;
        }

        readonly Effects _effects;
        readonly List<TargetView> _targets = new List<TargetView>();
        readonly List<Transform> _rockets = new List<Transform>(), _bombs = new List<Transform>();
        float _trailClock;

        /// <summary>Which baked model a kind of target is drawn with; kinds not listed are built from solids.</summary>
        static readonly Dictionary<string, string> TargetModels = new Dictionary<string, string>
        {
            { "truck", "truck" }, { "truck_cargo", "truck_cargo" }, { "truck_army", "truck_army" }, { "truck_fuel", "truck_fuel" },
            { "staff_car", "jeep" }, { "jeep", "jeep" }, { "humvee", "humvee" }, { "apc", "apc" },
            { "tank_light", "tank_light" }, { "tank", "tank" }, { "tank_heavy", "tank_heavy" },
            { "aagun", "aagun" }, { "gatling", "gatling" }, { "sam", "sam" },
            { "heli", "heli" }, { "heli_transport", "heli_transport" }, { "gunship", "heli" },
            { "boat", "boat" }, { "warship", "warship" }, { "ally_truck", "truck_army" },
            { "hunter", "drone_heavy" }, { "fighter", "drone_fighter" }, { "ally_tank", "tank" }, { "turret", "aagun" },
            { "launcher", "sam" }, { "ship_turret", "gatling" },
        };

        /// <summary>Models that stand in for something of another size.</summary>
        static readonly Dictionary<string, float> TargetScales = new Dictionary<string, float>
        {
            { "hunter", 1.5f }, { "fighter", 2.6f }, { "launcher", 1.7f }, { "ship_turret", 0.8f },
        };

        struct Debris
        {
            public Transform Body;
            public Vector3 Velocity, Spin;
            public float Left;
        }

        readonly List<Debris> _debris = new List<Debris>();
        static readonly int EmissionId = Shader.PropertyToID("_Emission");
        MaterialPropertyBlock _heat;
        bool _thermal;

        public MissionWorld(Transform parent, HeightMap map, Effects effects)
        {
            _parent = parent;
            _map = map;
            _effects = effects;
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
            foreach (PropDefinition prop in mission.Props)
            {
                if (prop.Kind == "sandbags" || prop.Kind == "barrels" || prop.Kind == "wreck") SpawnDressing(prop.Kind, prop.Position, prop.Id.Length * 37f);
                else PropModels.Add(props, prop.Kind, prop.Position);
            }
            SpawnTargets(mission);
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
            _targets.Clear();
            _rockets.Clear();
            _bombs.Clear();
            _debris.Clear();
            _thermal = false;
            _effects.ClearBattle();
            _mission = null;
        }

        // ---------------------------------------------------------------- targets

        void SpawnTargets(MissionDefinition mission)
        {
            for (int i = 0; i < mission.Targets.Count; i++)
            {
                TargetDefinition definition = mission.Targets[i];
                var root = new GameObject("Target_" + definition.Id);
                root.transform.SetParent(_parent, false);
                _spawned.Add(root);
                var view = new TargetView { Root = root.transform };

                if (TargetModels.TryGetValue(definition.Kind.Id, out string modelName)) view.Model = ModelLibrary.Get(modelName);
                if (view.Model != null)
                {
                    var parts = new Dictionary<string, Transform>();
                    // The commander's car is the pale one in the column.
                    string id = definition.Kind.Id;
                    Material paint = id == "staff_car" ? view.Model.Tinted(new Color(0.80f, 0.80f, 0.76f))
                        : definition.Friendly ? view.Model.Tinted(new Color(0.22f, 0.36f, 0.50f))
                        : id == "hunter" || id == "fighter" ? view.Model.Tinted(new Color(0.16f, 0.15f, 0.15f))
                        : view.Model.Plain;
                    Transform body = view.Model.Spawn(root.transform, paint, true, parts);
                    if (TargetScales.TryGetValue(id, out float scale)) body.localScale = Vector3.one * scale;
                    parts.TryGetValue("head", out view.Head);
                }
                else
                {
                    var builder = new MeshBuilder();
                    PropModels.Add(builder, definition.Kind.Id, Vector3.zero);
                    var body = new GameObject("Body");
                    body.transform.SetParent(root.transform, false);
                    body.AddComponent<MeshFilter>().sharedMesh = builder.Build("Target");
                    var renderer = body.AddComponent<MeshRenderer>();
                    renderer.sharedMaterial = GameMaterials.PaletteLit;
                }
                // A helicopter in the air has its rotor turning; the blades in the model are the parked ones.
                if (definition.Altitude > 0f && view.Model != null)
                {
                    var blades = new MeshBuilder();
                    float span = view.Model.Bounds.size.z * 0.95f;
                    blades.Box(Vector3.zero, new Vector3(span, 0.06f, 0.45f), Palette.Graphite);
                    blades.Box(Vector3.zero, new Vector3(0.45f, 0.06f, span), Palette.Graphite);
                    view.Rotor = Spawn("Rotor", blades.Build("Rotor"), GameMaterials.PaletteLit, false).transform;
                    view.Rotor.SetParent(root.transform, false);
                    view.Rotor.localPosition = new Vector3(0f, view.Model.Bounds.max.y + 0.15f, view.Model.Bounds.center.z * 0.3f);
                }
                view.Renderers = root.GetComponentsInChildren<Renderer>();
                _targets.Add(view);

                // Dug-in positions sit behind sandbags.
                string kind = definition.Kind.Id;
                if (definition.Path.Count < 2 && (kind == "aagun" || kind == "gatling" || kind == "sam" || kind == "artillery" || kind == "squad"))
                {
                    float reach = definition.Kind.Radius + 2.2f;
                    SpawnDressing("sandbags", OnGround(definition.Position + new Vector3(0f, 0f, reach)), 0f);
                    SpawnDressing("sandbags", OnGround(definition.Position + new Vector3(reach * 0.87f, 0f, -reach * 0.5f)), 120f);
                    SpawnDressing("sandbags", OnGround(definition.Position + new Vector3(-reach * 0.87f, 0f, -reach * 0.5f)), 240f);
                }
                // What is ours is ringed in green on the ground.
                if (definition.Friendly)
                {
                    Transform ring = Spawn("OwnRing", _thinRingMesh, _done, false).transform;
                    ring.SetPositionAndRotation(OnGround(definition.Position) + Vector3.up * 0.6f, Quaternion.Euler(90f, 0f, 0f));
                    ring.localScale = Vector3.one * (definition.Kind.Radius + 6f);
                }
            }
        }

        Vector3 OnGround(Vector3 position)
        {
            position.y = Mathf.Max(_map.Height(position.x, position.z), Island.WaterLevel);
            return position;
        }

        /// <summary>Things a mission stands around its targets that are models rather than solids.</summary>
        void SpawnDressing(string kind, Vector3 position, float yawDeg)
        {
            if (kind == "barrels")
            {
                Model barrel = ModelLibrary.Get("barrel");
                if (barrel == null) return;
                for (int i = 0; i < 5; i++)
                {
                    Transform one = barrel.Spawn(_parent, barrel.Plain, true);
                    one.position = OnGround(position + new Vector3((i % 3 - 1) * 1.25f, 0f, (i / 3) * 1.3f - 0.6f));
                    _spawned.Add(one.gameObject);
                }
                return;
            }
            Model model = ModelLibrary.Get(kind == "wreck" ? "truck" : "sandbags");
            if (model == null) return;
            Transform body = model.Spawn(_parent, kind == "wreck" ? model.Burnt : model.Plain, true);
            Vector3 normal = _map.Normal(position.x, position.z);
            Vector3 forward = Vector3.ProjectOnPlane(Quaternion.Euler(0f, yawDeg, 0f) * Vector3.forward, normal).normalized;
            body.SetPositionAndRotation(position, Quaternion.LookRotation(forward, normal));
            _spawned.Add(body.gameObject);
        }

        void UpdateTargets(MissionRun run, FlightState drone, float deltaTime)
        {
            for (int i = 0; i < _targets.Count; i++)
            {
                TargetView view = _targets[i];
                TargetState state = run.Target(i);
                TargetDefinition definition = _mission.Targets[i];
                bool afloat = _map.Height(state.Position.x, state.Position.z) < Island.WaterLevel;
                Vector3 normal = definition.Altitude > 0f || afloat ? Vector3.up : _map.Normal(state.Position.x, state.Position.z);
                Vector3 forward = Vector3.ProjectOnPlane(state.Forward, normal);
                if (forward.sqrMagnitude < 0.01f) forward = Vector3.forward;

                if (!state.Alive && !view.Dead)
                {
                    // Burnt out where it stood: soot for paint, and it never stands quite straight again.
                    view.Dead = true;
                    bool structure = view.Model == null;
                    foreach (Renderer renderer in view.Renderers)
                    {
                        renderer.sharedMaterial = view.Model != null ? view.Model.Burnt : GameMaterials.PaletteBurnt;
                        renderer.SetPropertyBlock(null);
                    }
                    // A tank's turret is blown clean off; a building throws slabs of itself around.
                    string dead = definition.Kind.Id;
                    if (dead.StartsWith("tank") || dead == "ally_tank" || dead == "apc")
                        Throw(view.Root.position + Vector3.up * 2f, new Vector3(2.2f, 0.8f, 2.6f), 1, 9f);
                    else if (definition.Kind.Structure || dead == "depot" || dead.StartsWith("train"))
                        Throw(view.Root.position + Vector3.up * 2f, new Vector3(1.6f, 0.5f, 2.2f), 5, 11f);
                    view.Root.localScale = structure ? new Vector3(1.04f, 0.42f, 1.04f) : new Vector3(1f, 0.9f, 1f);
                    if (afloat || definition.Altitude > 0f)
                    {
                        // A boat settles low in the water; an aircraft comes down.
                        view.Root.position = OnGround(state.Position) + Vector3.down * (afloat ? definition.Kind.Height * 0.25f : 0f);
                        view.Root.rotation = Quaternion.LookRotation(forward.normalized, Vector3.up) * Quaternion.Euler(6f, 0f, 14f);
                    }
                    else
                    {
                        view.Root.rotation = Quaternion.LookRotation(forward.normalized, normal) * Quaternion.Euler(0f, 0f, structure ? 0f : 6f);
                    }
                    _effects.Burn(view.Root.position + Vector3.up * 1.2f, Mathf.Clamp(definition.Kind.Radius * 0.55f, 1.6f, 5f));
                }
                if (view.Dead) continue;
                if (view.Rotor != null) view.Rotor.Rotate(0f, 900f * deltaTime, 0f, Space.Self);

                view.Root.SetPositionAndRotation(state.Position, Quaternion.LookRotation(forward.normalized, normal));

                // Guns follow the drone round; firing kicks the barrel back.
                if (view.Head != null && definition.Kind.Armed)
                {
                    Vector3 to = drone.Position - state.Position;
                    Vector3 local = Quaternion.Inverse(view.Root.rotation) * to;
                    if (new Vector2(local.x, local.z).sqrMagnitude > 4f && to.magnitude < 420f)
                    {
                        float yaw = Mathf.Atan2(local.x, local.z) * Mathf.Rad2Deg;
                        float turn = 1f - Mathf.Exp(-3f * deltaTime);
                        view.Head.localRotation = Quaternion.Slerp(view.Head.localRotation, Quaternion.Euler(0f, yaw, 0f), turn);
                    }
                }
            }
        }

        /// <summary>Where a target's gun is, for tracers and muzzle flashes.</summary>
        public Vector3 Muzzle(int target)
        {
            TargetView view = _targets[target];
            return view.Root.position + Vector3.up * Mathf.Min(3f, _mission.Targets[target].Kind.Height * 0.8f);
        }

        void UpdateProjectiles(MissionRun run, float deltaTime)
        {
            int rockets = 0, bombs = 0;
            _trailClock -= deltaTime;
            bool trail = _trailClock <= 0f;
            if (trail) _trailClock = 0.035f;

            foreach (Projectile projectile in run.Projectiles)
            {
                bool rocket = projectile.Type == ProjectileType.Rocket;
                List<Transform> pool = rocket ? _rockets : _bombs;
                int index = rocket ? rockets++ : bombs++;
                if (index >= pool.Count) pool.Add(SpawnMunition(rocket));
                Transform view = pool[index];
                if (!view.gameObject.activeSelf) view.gameObject.SetActive(true);
                Vector3 direction = rocket ? projectile.Direction : Vector3.down + (projectile.Impact - projectile.From).normalized * 0.4f;
                if (direction.sqrMagnitude < 0.01f) direction = Vector3.down;
                view.SetPositionAndRotation(projectile.Position, Quaternion.LookRotation(direction.normalized));
                // The first puffs would sit on the drone itself, so the trail starts a moment into the flight.
                if (rocket && trail && projectile.Elapsed > 0.12f) _effects.Trail(projectile.Position - direction.normalized * 0.8f);
            }
            for (int i = rockets; i < _rockets.Count; i++)
                if (_rockets[i].gameObject.activeSelf) _rockets[i].gameObject.SetActive(false);
            for (int i = bombs; i < _bombs.Count; i++)
                if (_bombs[i].gameObject.activeSelf) _bombs[i].gameObject.SetActive(false);
        }

        Transform SpawnMunition(bool rocket)
        {
            Model model = ModelLibrary.Get("missile");
            Transform view;
            if (model != null)
            {
                view = model.Spawn(_parent, model.Plain, false);
                view.localScale = rocket ? new Vector3(0.7f, 0.7f, 1f) : new Vector3(1.3f, 1.3f, 0.8f);
            }
            else
            {
                view = Spawn(rocket ? "Rocket" : "Bomb", _ballMesh, _danger, false).transform;
                view.localScale = new Vector3(0.3f, 0.3f, 1.2f);
            }
            _spawned.Add(view.gameObject);
            return view;
        }

        /// <summary>Follows a running mission: moves actors, recolours rings, flies the munition.</summary>
        /// <summary>Tosses blackened pieces up and out from a kill; they tumble, land and lie there.</summary>
        void Throw(Vector3 from, Vector3 size, int count, float force)
        {
            for (int i = 0; i < count; i++)
            {
                var builder = new MeshBuilder();
                float shrink = count == 1 ? 1f : Random.Range(0.5f, 1f);
                builder.Box(Vector3.zero, size * shrink, Palette.Soot);
                Transform piece = Spawn("Debris", builder.Build("Debris"), GameMaterials.PaletteBurnt, true).transform;
                piece.position = from;
                Vector2 out2 = Random.insideUnitCircle.normalized;
                _debris.Add(new Debris
                {
                    Body = piece, Velocity = new Vector3(out2.x, 0f, out2.y) * Random.Range(force * 0.3f, force * 0.7f) + Vector3.up * Random.Range(force * 0.8f, force * 1.3f),
                    Spin = Random.insideUnitSphere * 260f, Left = 6f,
                });
            }
        }

        void UpdateDebris(float deltaTime)
        {
            for (int i = _debris.Count - 1; i >= 0; i--)
            {
                Debris piece = _debris[i];
                if (piece.Body == null || piece.Left <= 0f)
                {
                    _debris.RemoveAt(i);
                    continue;
                }
                piece.Left -= deltaTime;
                piece.Velocity.y -= 17f * deltaTime;
                Vector3 next = piece.Body.position + piece.Velocity * deltaTime;
                float ground = Mathf.Max(_map.Height(next.x, next.z), Island.WaterLevel) + 0.3f;
                if (next.y <= ground)
                {
                    // Down: it stays where it fell.
                    next.y = ground;
                    piece.Body.position = next;
                    _debris.RemoveAt(i);
                    continue;
                }
                piece.Body.position = next;
                piece.Body.Rotate(piece.Spin * deltaTime, Space.World);
                _debris[i] = piece;
            }
        }

        /// <summary>
        /// The thermal sight: everything with a warm engine or a pulse glows white, the dead and
        /// the landscape stay dark. Used by the gun camera at night.
        /// </summary>
        public void SetThermal(bool on)
        {
            if (on == _thermal) return;
            _thermal = on;
            if (_heat == null) _heat = new MaterialPropertyBlock();
            _heat.SetColor(EmissionId, on ? new Color(1.5f, 1.4f, 1.2f) : Color.black);
            foreach (TargetView view in _targets)
            {
                if (view.Dead || view.Renderers == null) continue;
                foreach (Renderer renderer in view.Renderers)
                    if (renderer != null) renderer.SetPropertyBlock(on ? _heat : null);
            }
        }

        public void Update(MissionRun run, FlightState drone, float deltaTime)
        {
            if (_mission == null) return;
            UpdateDebris(deltaTime);

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

            UpdateTargets(run, drone, deltaTime);
            UpdateProjectiles(run, deltaTime);

            ObjectiveDefinition current = run.CurrentObjective;
            bool marking = current != null && current.Type == ObjectiveType.Mark;
            for (int i = 0; i < _threatRings.Count; i++)
                _threatRings[i].sharedMaterial = !marking || run.IsMarked(i) ? _done : _danger;

            // Strike: a ring on the ground shows where a release would land right now.
            bool aiming = current != null && current.Type == ObjectiveType.Strike && run.ShotsLeft > 0 &&
                          !run.Shot.Active && run.Status == MissionStatus.InProgress;
            // With bombs on the racks the same ring shows where one would fall.
            bool bombing = run.Bombs > 0 && !drone.Grounded && run.Status == MissionStatus.InProgress;
            if (_aimRing.gameObject.activeSelf != (aiming || bombing)) _aimRing.gameObject.SetActive(aiming || bombing);
            if (aiming || bombing)
            {
                Vector3 impact = run.PredictImpact(drone, out _);
                _aimRing.position = impact + Vector3.up * 1.4f;
                _aimRing.localScale = Vector3.one * (aiming ? Mathf.Max(3f, current.Radius) : 4f);
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

    /// <summary>The region's hidden caches: slowly turning crates that vanish when collected.</summary>
    public sealed class CachesView
    {
        readonly Transform[] _crates = new Transform[Caches.PerRegion];

        public CachesView(Island island, Transform parent, System.Func<int, bool> isFound)
        {
            var builder = new MeshBuilder();
            builder.Box(Vector3.zero, new Vector3(1.6f, 1.6f, 1.6f), Palette.White);
            builder.Box(Vector3.zero, new Vector3(1.9f, 0.3f, 1.9f), Palette.White);
            Mesh mesh = builder.Build("Cache");
            Material material = GameMaterials.Unlit(new Color(1f, 0.78f, 0.20f));

            for (int i = 0; i < _crates.Length; i++)
            {
                var crate = new GameObject("Cache" + i);
                crate.transform.SetParent(parent, false);
                crate.transform.position = Caches.Position(island, i);
                crate.AddComponent<MeshFilter>().sharedMesh = mesh;
                var renderer = crate.AddComponent<MeshRenderer>();
                renderer.sharedMaterial = material;
                renderer.shadowCastingMode = ShadowCastingMode.Off;
                crate.SetActive(!isFound(i));
                _crates[i] = crate.transform;
            }
        }

        public void Hide(int index) => _crates[index].gameObject.SetActive(false);

        public void Update(float deltaTime)
        {
            foreach (Transform crate in _crates)
                if (crate != null && crate.gameObject.activeSelf) crate.Rotate(0f, 50f * deltaTime, 0f, Space.World);
        }
    }

    /// <summary>Simple models for the things missions are about, built from boxes and cylinders.</summary>
    public static class PropModels
    {
        /// <summary>A ridge tent: two sloping sides and two triangular ends.</summary>
        static void Ridge(MeshBuilder b, Vector3 at, float width, float length, float height, int color)
        {
            float w = width * 0.5f, l = length * 0.5f;
            Vector3 a = at + new Vector3(-w, 0f, -l), c = at + new Vector3(w, 0f, -l);
            Vector3 d = at + new Vector3(w, 0f, l), e = at + new Vector3(-w, 0f, l);
            Vector3 front = at + new Vector3(0f, height, -l), back = at + new Vector3(0f, height, l);
            b.Quad(a, front, back, e, new Vector3(-1f, 1f, 0f), color);
            b.Quad(c, front, back, d, new Vector3(1f, 1f, 0f), color);
            b.Triangle(a, c, front, Vector3.back, color);
            b.Triangle(e, d, back, Vector3.forward, color);
        }

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
                case "pilot":
                    // One man beside what is left of his parachute.
                    b.Box(at + new Vector3(0f, 0.45f, 0f), new Vector3(0.42f, 0.9f, 0.3f), Palette.Orange);
                    b.Box(at + new Vector3(0f, 1.25f, 0f), new Vector3(0.52f, 0.7f, 0.34f), Palette.Orange);
                    b.Box(at + new Vector3(0f, 1.78f, 0f), new Vector3(0.3f, 0.3f, 0.3f), Palette.White);
                    b.Cylinder(at + new Vector3(2.2f, 0f, 1.2f), 2.0f, 0.4f, 0.5f, 8, Palette.White);
                    b.Cylinder(at + new Vector3(2.2f, 0.5f, 1.2f), 0.4f, 0f, 0.3f, 8, Palette.Orange);
                    break;
                case "officer":
                case "sentry":
                {
                    // One man: an officer in a peaked cap, a sentry with his rifle.
                    bool officer = kind == "officer";
                    b.Box(at + new Vector3(0f, 0.45f, 0f), new Vector3(0.42f, 0.9f, 0.3f), officer ? Palette.Graphite : Palette.Olive);
                    b.Box(at + new Vector3(0f, 1.25f, 0f), new Vector3(0.54f, 0.7f, 0.34f), officer ? Palette.DarkGrey : Palette.Khaki);
                    b.Box(at + new Vector3(0f, 1.78f, 0f), new Vector3(0.3f, 0.3f, 0.3f), Palette.Sandbag);
                    b.Box(at + new Vector3(0f, 1.97f, 0.03f), new Vector3(0.4f, 0.12f, 0.46f), officer ? Palette.Red : Palette.Olive);
                    if (!officer) b.Box(at + new Vector3(0.22f, 1.3f, 0.3f), new Vector3(0.08f, 0.1f, 0.95f), Palette.Graphite);
                    break;
                }
                case "dummy":
                    // A plywood board with rings painted on it.
                    b.Box(at + new Vector3(0f, 1.8f, 0f), new Vector3(3.4f, 3.4f, 0.2f), Palette.White);
                    b.Box(at + new Vector3(0f, 1.8f, 0.12f), new Vector3(2.2f, 2.2f, 0.06f), Palette.Red);
                    b.Box(at + new Vector3(0f, 1.8f, 0.16f), new Vector3(1.0f, 1.0f, 0.06f), Palette.White);
                    b.Box(at + new Vector3(-1.3f, 0.1f, 0f), new Vector3(0.2f, 0.2f, 1.6f), Palette.Trunk);
                    b.Box(at + new Vector3(1.3f, 0.1f, 0f), new Vector3(0.2f, 0.2f, 1.6f), Palette.Trunk);
                    break;
                case "train_engine":
                    b.Box(at + new Vector3(0f, 0.7f, 0f), new Vector3(3.2f, 0.8f, 11f), Palette.Graphite);
                    b.Box(at + new Vector3(0f, 2.3f, -0.6f), new Vector3(3.0f, 2.6f, 8.6f), Palette.Olive);
                    b.Box(at + new Vector3(0f, 3.4f, -3.6f), new Vector3(3.1f, 1.5f, 2.6f), Palette.DarkGrey);
                    b.Cylinder(at + new Vector3(0f, 3.6f, 2.4f), 0.5f, 0.6f, 1.2f, 8, Palette.Soot);
                    b.Box(at + new Vector3(0f, 1.5f, 5.2f), new Vector3(2.8f, 1.2f, 0.8f), Palette.Rust);
                    for (int i = 0; i < 4; i++)
                    {
                        b.Box(at + new Vector3(-1.5f, 0.5f, -3.6f + i * 2.4f), new Vector3(0.3f, 1f, 1f), Palette.Soot);
                        b.Box(at + new Vector3(1.5f, 0.5f, -3.6f + i * 2.4f), new Vector3(0.3f, 1f, 1f), Palette.Soot);
                    }
                    break;
                case "train_car":
                case "train_gun":
                    b.Box(at + new Vector3(0f, 0.7f, 0f), new Vector3(3.2f, 0.8f, 11f), Palette.Graphite);
                    b.Box(at + new Vector3(0f, 2.2f, 0f), new Vector3(3.0f, 2.4f, 10f), kind == "train_gun" ? Palette.DarkGrey : Palette.Rust);
                    for (int i = 0; i < 4; i++)
                    {
                        b.Box(at + new Vector3(-1.5f, 0.5f, -3.6f + i * 2.4f), new Vector3(0.3f, 1f, 1f), Palette.Soot);
                        b.Box(at + new Vector3(1.5f, 0.5f, -3.6f + i * 2.4f), new Vector3(0.3f, 1f, 1f), Palette.Soot);
                    }
                    if (kind == "train_gun")
                    {
                        b.Cylinder(at + new Vector3(0f, 3.4f, 0f), 1.5f, 1.3f, 1.1f, 8, Palette.Olive);
                        b.Box(at + new Vector3(0.4f, 4.4f, 1.6f), new Vector3(0.22f, 0.22f, 3.4f), Quaternion.Euler(-30f, 0f, 0f), Palette.Graphite);
                        b.Box(at + new Vector3(-0.4f, 4.4f, 1.6f), new Vector3(0.22f, 0.22f, 3.4f), Quaternion.Euler(-30f, 0f, 0f), Palette.Graphite);
                    }
                    else
                    {
                        b.Box(at + new Vector3(0f, 3.5f, 0f), new Vector3(2.6f, 0.2f, 9.4f), Palette.Soot);
                        b.Box(at + new Vector3(1.52f, 2.2f, 0f), new Vector3(0.06f, 1.2f, 1.2f), Palette.Yellow);
                    }
                    break;
                case "bridge":
                    // A deck on three piers, with a truss along each side.
                    b.Box(at + new Vector3(0f, 4.6f, 0f), new Vector3(7f, 0.9f, 34f), Palette.Concrete);
                    for (int i = -1; i <= 1; i++) b.Box(at + new Vector3(0f, 1.6f, i * 12f), new Vector3(4.6f, 5.4f, 2.4f), Palette.Rubble);
                    foreach (float side in new[] { -3.3f, 3.3f })
                    {
                        b.Box(at + new Vector3(side, 7.6f, 0f), new Vector3(0.4f, 0.4f, 34f), Palette.Rust);
                        for (int i = -4; i <= 4; i++) b.Box(at + new Vector3(side, 6.2f, i * 4f), new Vector3(0.3f, 2.6f, 0.3f), Palette.Rust);
                    }
                    break;
                case "platform":
                    // A deck on four legs out of the sea, a derrick, a flare stack and tanks.
                    foreach (float x in new[] { -8f, 8f })
                    foreach (float z in new[] { -8f, 8f })
                        b.Cylinder(at + new Vector3(x, -6f, z), 1.2f, 1.2f, 16f, 8, Palette.Yellow);
                    b.Box(at + new Vector3(0f, 10.6f, 0f), new Vector3(22f, 1.4f, 22f), Palette.DarkGrey);
                    b.Box(at + new Vector3(-5f, 13.4f, -4f), new Vector3(8f, 4.2f, 9f), Palette.White);
                    b.Cylinder(at + new Vector3(5f, 11.3f, 5f), 2.6f, 2.6f, 3.4f, 10, Palette.Rust);
                    b.Cylinder(at + new Vector3(6f, 11.3f, -5f), 2.2f, 2.2f, 3.0f, 10, Palette.Orange);
                    b.Cylinder(at + new Vector3(0f, 11.3f, 2f), 1.6f, 0.3f, 15f, 4, Palette.Graphite);
                    b.Box(at + new Vector3(-10f, 14f, 9f), new Vector3(0.4f, 7f, 0.4f), Palette.Graphite);
                    b.Box(at + new Vector3(-10f, 17.6f, 9f), new Vector3(0.8f, 0.5f, 0.8f), Palette.Red);
                    break;
                case "squad":
                case "ally_squad":
                    // Five riflemen in a loose group.
                    for (int i = 0; i < 5; i++)
                    {
                        float angle = i * 2.4f;
                        Vector3 foot = at + new Vector3(Mathf.Cos(angle) * (1.2f + i * 0.45f), 0f, Mathf.Sin(angle) * (1.2f + i * 0.45f));
                        b.Box(foot + new Vector3(0f, 0.45f, 0f), new Vector3(0.42f, 0.9f, 0.3f), Palette.Olive);
                        b.Box(foot + new Vector3(0f, 1.25f, 0f), new Vector3(0.52f, 0.7f, 0.34f), Palette.Khaki);
                        b.Box(foot + new Vector3(0f, 1.78f, 0f), new Vector3(0.3f, 0.3f, 0.3f), Palette.Sandbag);
                        b.Box(foot + new Vector3(0f, 1.95f, 0f), new Vector3(0.36f, 0.14f, 0.38f), Palette.Olive);
                        b.Box(foot + new Vector3(0.2f, 1.3f, 0.35f), new Vector3(0.08f, 0.1f, 0.9f), Palette.Graphite);
                    }
                    break;
                case "artillery":
                {
                    Quaternion raised = Quaternion.Euler(-28f, 0f, 0f);
                    b.Box(at + new Vector3(0f, 1.2f, 0.2f), new Vector3(2.2f, 1.5f, 0.2f), Palette.Olive);
                    b.Box(at + new Vector3(0f, 1.1f, 0f) + raised * new Vector3(0f, 0f, 2.4f), new Vector3(0.32f, 0.32f, 5.2f), raised, Palette.Olive);
                    b.Box(at + new Vector3(0f, 1.1f, 0f) + raised * new Vector3(0f, 0f, 5.1f), new Vector3(0.42f, 0.42f, 0.5f), raised, Palette.Graphite);
                    foreach (float side in new[] { -1f, 1f })
                    {
                        b.Box(at + new Vector3(side * 1.15f, 0.7f, 0f), new Vector3(0.35f, 1.4f, 1.4f), Palette.Graphite);
                        Quaternion spread = Quaternion.Euler(0f, side * 18f, 0f);
                        b.Box(at + new Vector3(side * 0.9f, 0.35f, -2.2f), new Vector3(0.25f, 0.3f, 4f), spread, Palette.Olive);
                    }
                    break;
                }
                case "bunker":
                    b.Box(at + new Vector3(0f, 0.9f, 0f), new Vector3(8f, 3.8f, 7f), Palette.Concrete);
                    b.Box(at + new Vector3(0f, 2.95f, 0f), new Vector3(9f, 0.5f, 8f), Palette.Rubble);
                    b.Box(at + new Vector3(0f, 1.9f, 3.52f), new Vector3(4.6f, 0.5f, 0.1f), Palette.Lens);
                    b.Box(at + new Vector3(4.02f, 1.9f, 0f), new Vector3(0.1f, 0.5f, 3.6f), Palette.Lens);
                    b.Box(at + new Vector3(-4.02f, 1.9f, 0f), new Vector3(0.1f, 0.5f, 3.6f), Palette.Lens);
                    for (int i = 0; i < 6; i++)
                        b.Box(at + new Vector3(-3.5f + i * 1.4f, 0.4f, 4.3f), new Vector3(1.3f, 0.8f, 0.9f), Palette.Sandbag);
                    b.Box(at + new Vector3(0f, 3.4f, -1f), new Vector3(5f, 0.3f, 4f), Palette.Olive);
                    break;
                case "hq":
                    // The command building: two storeys, a mast on the roof and their flag on it.
                    b.Box(at + new Vector3(0f, 2.5f, 0f), new Vector3(15f, 8f, 10f), Palette.Plaster);
                    b.Box(at + new Vector3(0f, 6.7f, 0f), new Vector3(15.8f, 0.5f, 10.8f), Palette.Concrete);
                    b.Box(at + new Vector3(3.5f, 7.8f, -1.5f), new Vector3(5f, 1.8f, 4f), Palette.Concrete);
                    for (int floor = 0; floor < 2; floor++)
                    for (int i = 0; i < 5; i++)
                    {
                        float x = -5.6f + i * 2.8f, y = 1.9f + floor * 2.9f;
                        b.Box(at + new Vector3(x, y, 5.02f), new Vector3(1.4f, 1.3f, 0.1f), Palette.Lens);
                        b.Box(at + new Vector3(x, y, -5.02f), new Vector3(1.4f, 1.3f, 0.1f), Palette.Lens);
                    }
                    b.Box(at + new Vector3(0f, 1.1f, 5.05f), new Vector3(1.8f, 2.4f, 0.14f), Palette.Trunk);
                    b.Cylinder(at + new Vector3(-5f, 6.9f, 2f), 0.12f, 0.08f, 6f, 6, Palette.LightGrey);
                    b.Box(at + new Vector3(-3.9f, 12.2f, 2f), new Vector3(2.2f, 1.2f, 0.08f), Palette.Red);
                    b.Cylinder(at + new Vector3(5.5f, 8.7f, -1.5f), 0.5f, 1.4f, 0.8f, 10, Palette.LightGrey);
                    for (int i = 0; i < 8; i++)
                        b.Box(at + new Vector3(-5.6f + i * 1.6f, 0.4f, 6.4f), new Vector3(1.5f, 0.8f, 0.9f), Palette.Sandbag);
                    break;
                case "depot":
                    // Ammunition under a net: crates, shells and drums.
                    for (int i = 0; i < 9; i++)
                    {
                        var spot = new Vector3((i % 3 - 1) * 2.6f, 0f, (i / 3 - 1) * 2.4f);
                        b.Box(at + spot + new Vector3(0f, 0.7f, 0f), new Vector3(2.2f, 1.4f, 1.8f), i % 2 == 0 ? Palette.Olive : Palette.Trunk);
                        if (i % 3 != 1) b.Box(at + spot + new Vector3(0.1f, 1.9f, 0f), new Vector3(1.8f, 1f, 1.5f), Palette.Olive);
                    }
                    for (int i = 0; i < 4; i++)
                        b.Cylinder(at + new Vector3(5.2f, 0f, -2.2f + i * 1.5f), 0.55f, 0.55f, 1.4f, 8, i % 2 == 0 ? Palette.Red : Palette.DarkGrey);
                    foreach (float x in new[] { -4.6f, 4.2f })
                    foreach (float z in new[] { -4.2f, 4.2f })
                        b.Cylinder(at + new Vector3(x, 0f, z), 0.1f, 0.1f, 3.6f, 5, Palette.Trunk);
                    b.Box(at + new Vector3(-0.2f, 3.6f, 0f), new Vector3(9.4f, 0.12f, 9f), Palette.LeafDark);
                    break;
                case "barrels":
                    for (int i = 0; i < 7; i++)
                        b.Cylinder(at + new Vector3((i % 3 - 1) * 1.3f, 0f, (i / 3 - 1) * 1.3f), 0.55f, 0.55f, 1.4f, 8, i % 2 == 0 ? Palette.Red : Palette.Rust);
                    break;
                case "tent":
                    Ridge(b, at + new Vector3(-2.6f, 0f, 0f), 4.2f, 6.5f, 2.6f, Palette.Olive);
                    Ridge(b, at + new Vector3(2.8f, 0f, 0.8f), 4.2f, 6.5f, 2.6f, Palette.Khaki);
                    b.Box(at + new Vector3(0.2f, 0.5f, -4.4f), new Vector3(1.2f, 1f, 1.2f), Palette.Trunk);
                    break;
                case "outpost":
                    // Ours: a sandbagged yard, a watchtower, a tent and the flag.
                    for (int i = 0; i < 16; i++)
                    {
                        float angle = i * Mathf.PI / 8f;
                        if (i == 4) continue;
                        Vector3 spot = at + new Vector3(Mathf.Cos(angle) * 7.5f, 0.45f, Mathf.Sin(angle) * 7.5f);
                        b.Box(spot, new Vector3(2.9f, 0.9f, 1f), Quaternion.Euler(0f, 90f - angle * Mathf.Rad2Deg, 0f), Palette.Sandbag);
                    }
                    foreach (float x in new[] { -1.2f, 1.2f })
                    foreach (float z in new[] { -1.2f, 1.2f })
                        b.Cylinder(at + new Vector3(-3f + x, 0f, -2f + z), 0.12f, 0.12f, 6f, 5, Palette.Trunk);
                    b.Box(at + new Vector3(-3f, 5f, -2f), new Vector3(3.2f, 0.2f, 3.2f), Palette.Trunk);
                    b.Box(at + new Vector3(-3f, 5.6f, -2f), new Vector3(3.2f, 1f, 3.2f), Palette.Sandbag);
                    b.Cylinder(at + new Vector3(-3f, 6.9f, -2f), 2.6f, 0f, 1.2f, 4, Palette.Olive);
                    Ridge(b, at + new Vector3(2.8f, 0f, 1.5f), 4f, 5.5f, 2.4f, Palette.Olive);
                    b.Cylinder(at + new Vector3(3f, 0f, -3.5f), 0.1f, 0.07f, 7.5f, 6, Palette.LightGrey);
                    b.Box(at + new Vector3(4.1f, 6.8f, -3.5f), new Vector3(2.2f, 1.2f, 0.08f), Palette.Green);
                    b.Box(at + new Vector3(0.5f, 0.6f, -4f), new Vector3(1.4f, 1.2f, 1.4f), Palette.Trunk);
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
