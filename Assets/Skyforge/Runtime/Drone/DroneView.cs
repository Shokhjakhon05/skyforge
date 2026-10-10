using System.Collections.Generic;
using Skyforge.Core;
using Skyforge.World;
using UnityEngine;

namespace Skyforge.Drone
{
    /// <summary>
    /// The visible drone, assembled from simple solids according to its <see cref="DroneLook"/>.
    /// Drawn larger than a real drone of its class so it reads well on a phone from the chase
    /// camera. Nose points along +Z.
    /// </summary>
    public sealed class DroneView : MonoBehaviour
    {
        /// <summary>How far below the centre the feet are; matches the radius the simulation rests on.</summary>
        const float FootDepth = 0.38f;

        Transform _tilt;
        readonly List<Transform> _rotors = new List<Transform>();
        Renderer[] _renderers;
        float _rotorSpeed;

        public static DroneView Create(Transform parent, DroneLook look)
        {
            var root = new GameObject("Drone");
            root.transform.SetParent(parent, false);
            var view = root.AddComponent<DroneView>();
            view.Build(look);
            return view;
        }

        void Build(DroneLook look)
        {
            _tilt = new GameObject("Tilt").transform;
            _tilt.SetParent(transform, false);

            if (BuildFromModel(look)) return;

            float s = look.Scale;
            float reach = 0.62f * s * look.ArmReach;
            float length = 0.62f * s * look.BodyLength;
            float width = 0.36f * s;
            var body = new MeshBuilder();

            body.Box(Vector3.zero, new Vector3(width, 0.16f * s, length), look.BodyColor);
            body.Box(new Vector3(0f, 0.09f * s, -0.04f * s), new Vector3(width * 0.72f, 0.06f * s, length * 0.65f), look.AccentColor);
            // Camera pod at the nose, tail light at the back.
            float pod = look.BigCamera ? 1.6f : 1f;
            body.Box(new Vector3(0f, -0.02f * s, length * 0.5f + 0.03f * s), new Vector3(0.20f * s * pod, 0.14f * s * pod, 0.10f * s), Palette.DarkGrey);
            body.Box(new Vector3(0f, -0.04f * s, length * 0.5f + 0.09f * s), new Vector3(0.11f * s * pod, 0.09f * s * pod, 0.04f * s), Palette.Lens);
            body.Box(new Vector3(0f, 0.02f * s, -length * 0.5f), new Vector3(width * 0.6f, 0.05f * s, 0.03f * s), Palette.Red);

            if (look.Rack)
                body.Box(new Vector3(0f, -0.16f * s, 0f), new Vector3(width * 0.8f, 0.14f * s, length * 0.6f), Palette.DarkGrey);
            if (look.Dish)
            {
                body.Cylinder(new Vector3(0f, 0.10f * s, -0.05f * s), 0.03f * s, 0.03f * s, 0.16f * s, 6, Palette.LightGrey);
                body.Cylinder(new Vector3(0f, 0.26f * s, -0.05f * s), 0.06f * s, 0.22f * s, 0.07f * s, 10, look.AccentColor);
            }

            var rotorPositions = new List<Vector3>();
            if (look.Wing)
            {
                // A wing with two tractor rotors, a tail boom and a fin.
                body.Box(new Vector3(0f, 0.06f * s, 0.05f * s), new Vector3(2.4f * s, 0.035f * s, 0.34f * s), look.BodyColor);
                body.Box(new Vector3(0f, 0.07f * s, 0.05f * s), new Vector3(1.2f * s, 0.04f * s, 0.20f * s), look.AccentColor);
                body.Box(new Vector3(0f, 0.02f * s, -length * 0.5f - 0.25f * s), new Vector3(0.06f * s, 0.06f * s, 0.55f * s), Palette.DarkGrey);
                body.Box(new Vector3(0f, 0.04f * s, -length * 0.5f - 0.5f * s), new Vector3(0.7f * s, 0.03f * s, 0.16f * s), look.BodyColor);
                body.Box(new Vector3(0f, 0.16f * s, -length * 0.5f - 0.5f * s), new Vector3(0.03f * s, 0.24f * s, 0.16f * s), look.AccentColor);
                foreach (float side in new[] { -1f, 1f })
                {
                    var motor = new Vector3(side * 0.62f * s, 0.06f * s, 0.26f * s);
                    body.Cylinder(motor + Vector3.down * 0.06f * s, 0.06f * s, 0.06f * s, 0.14f * s, 8, Palette.LightGrey);
                    rotorPositions.Add(motor + Vector3.up * 0.10f * s);
                    body.Box(new Vector3(side * 1.15f * s, 0.06f * s, 0.05f * s), new Vector3(0.05f * s, 0.05f * s, 0.05f * s),
                        side > 0f ? Palette.Green : Palette.Red);
                }
                AddLandingGear(body, look, new[] { new Vector3(-0.3f * s, 0f, 0.1f * s), new Vector3(0.3f * s, 0f, 0.1f * s), new Vector3(0f, 0f, -length * 0.5f) });
            }
            else
            {
                int arms = Mathf.Clamp(look.Arms, 3, 8);
                var feet = new List<Vector3>();
                for (int i = 0; i < arms; i++)
                {
                    float angle = (i + 0.5f) * 2f * Mathf.PI / arms;
                    var direction = new Vector3(Mathf.Sin(angle), 0f, Mathf.Cos(angle));
                    Vector3 tip = direction * reach;
                    Quaternion along = Quaternion.LookRotation(direction, Vector3.up);
                    body.Box(tip * 0.55f, new Vector3(0.07f * s, 0.05f * s, reach * 0.95f), along, Palette.DarkGrey);
                    body.Cylinder(tip + Vector3.down * 0.05f * s, 0.075f * s, 0.075f * s, 0.16f * s, 10, Palette.LightGrey);
                    // Navigation lights: green at the front, red at the back.
                    body.Box(tip + new Vector3(0f, -0.07f * s, 0f) + direction * 0.09f * s, new Vector3(0.05f, 0.04f, 0.05f) * s,
                        direction.z > 0f ? Palette.Green : Palette.Red);
                    rotorPositions.Add(tip + Vector3.up * 0.125f * s);
                    feet.Add(tip);
                }
                AddLandingGear(body, look, feet.ToArray());
            }
            AddPart("Body", _tilt, body.Build("DroneBody"));

            float blade = (look.Wing ? 0.42f : 0.62f) * s * Mathf.Lerp(1f, 0.8f, Mathf.InverseLerp(4f, 8f, look.Arms));
            var blades = new MeshBuilder();
            blades.Box(Vector3.zero, new Vector3(blade, 0.012f, 0.06f * s), Palette.LightGrey);
            blades.Box(Vector3.zero, new Vector3(0.06f * s, 0.012f, blade), Palette.LightGrey);
            Mesh bladeMesh = blades.Build("Rotor");
            for (int i = 0; i < rotorPositions.Count; i++)
            {
                Transform rotor = AddPart("Rotor" + i, _tilt, bladeMesh);
                rotor.localPosition = rotorPositions[i];
                _rotors.Add(rotor);
            }

            _renderers = GetComponentsInChildren<Renderer>();
        }

        /// <summary>
        /// The airframe as a baked 3D model, repainted in the drone's own colour, with the parts
        /// named rotor0..N spinning. Returns false if the drone has no model or it failed to load.
        /// </summary>
        /// <summary>The airframe's longest side in metres; the camera stands back by it.</summary>
        public float Length { get; private set; } = 1.25f;

        bool BuildFromModel(DroneLook look)
        {
            Model model = ModelLibrary.Get(look.Model);
            if (model == null) return false;

            var parts = new Dictionary<string, Transform>();
            Transform body = model.Spawn(_tilt, model.Tinted(Palette.Color(look.BodyColor)), true, parts);
            body.localScale = Vector3.one * look.Scale;
            Length = Mathf.Max(model.Bounds.size.z, model.Bounds.size.x) * look.Scale;
            // The model's feet are at the depth the simulation rests on; scaling must not move them.
            body.localPosition = new Vector3(0f, -FootDepth * (1f - look.Scale), 0f);
            for (int i = 0; i < 8; i++)
                if (parts.TryGetValue("rotor" + i, out Transform rotor)) _rotors.Add(rotor);

            // A helicopter's model has its blades frozen in place; a turning disc of blades goes over them.
            if (look.Model == "drone_heli" || look.Model == "drone_gunship" || look.Model == "drone_lifter")
            {
                float span = model.Bounds.size.z * 0.92f;
                var blades = new MeshBuilder();
                blades.Box(Vector3.zero, new Vector3(span, 0.025f, 0.11f), Palette.Graphite);
                blades.Box(Vector3.zero, new Vector3(0.11f, 0.025f, span), Palette.Graphite);
                Transform rotor = AddPart("MainRotor", body, blades.Build("MainRotor"));
                rotor.localPosition = new Vector3(0f, model.Bounds.max.y + 0.03f, model.Bounds.center.z + model.Bounds.size.z * 0.12f);
                _rotors.Add(rotor);
            }
            _renderers = GetComponentsInChildren<Renderer>();
            return true;
        }

        /// <summary>Legs down to the ground, or two floats for a drone that lands on water.</summary>
        static void AddLandingGear(MeshBuilder body, DroneLook look, Vector3[] feet)
        {
            float s = look.Scale;
            if (look.Floats)
            {
                foreach (float side in new[] { -1f, 1f })
                {
                    float x = side * 0.30f * s;
                    body.Box(new Vector3(x, -FootDepth + 0.06f, 0f), new Vector3(0.13f * s, 0.12f, 0.95f * s), Palette.Orange);
                    body.Box(new Vector3(x, (-FootDepth + 0.12f - 0.05f * s) * 0.5f, 0.2f * s), new Vector3(0.03f, FootDepth - 0.12f - 0.05f * s, 0.03f), Palette.DarkGrey);
                    body.Box(new Vector3(x, (-FootDepth + 0.12f - 0.05f * s) * 0.5f, -0.2f * s), new Vector3(0.03f, FootDepth - 0.12f - 0.05f * s, 0.03f), Palette.DarkGrey);
                }
                return;
            }
            foreach (Vector3 foot in feet)
            {
                float top = foot.y - 0.05f * s;
                float height = top + FootDepth;
                if (height <= 0.02f) continue;
                body.Box(new Vector3(foot.x, top - height * 0.5f, foot.z), new Vector3(0.04f, height, 0.04f), Palette.Graphite);
            }
        }

        static Transform AddPart(string name, Transform parent, Mesh mesh)
        {
            var part = new GameObject(name);
            part.transform.SetParent(parent, false);
            part.AddComponent<MeshFilter>().sharedMesh = mesh;
            part.AddComponent<MeshRenderer>().sharedMaterial = GameMaterials.PaletteLit;
            return part.transform;
        }

        public bool Visible
        {
            set
            {
                foreach (Renderer r in _renderers) r.enabled = value;
            }
        }

        /// <param name="throttle">0 idle on the ground .. 1 working hard.</param>
        public void Apply(Vector3 position, float yawDeg, Vector2 tiltDeg, float throttle, float deltaTime)
        {
            transform.SetPositionAndRotation(position, Quaternion.Euler(0f, yawDeg, 0f));
            _tilt.localRotation = Quaternion.Euler(tiltDeg.y, 0f, -tiltDeg.x);

            _rotorSpeed = Mathf.Lerp(_rotorSpeed, Mathf.Lerp(500f, 2600f, throttle), 1f - Mathf.Exp(-6f * deltaTime));
            for (int i = 0; i < _rotors.Count; i++)
            {
                float direction = i % 2 == 0 ? 1f : -1f;
                _rotors[i].Rotate(0f, direction * _rotorSpeed * deltaTime, 0f, Space.Self);
            }
        }
    }

    /// <summary>The ring course: shows which ring is next and notices the drone flying through it.</summary>
    public sealed class RingCourse
    {
        readonly Vector3[] _centers;
        readonly Renderer[] _renderers;
        readonly Material _next, _later;

        bool _visible = true;

        public int NextIndex { get; private set; }
        public int Count => _centers.Length;

        /// <summary>The course is hidden outside free flight.</summary>
        public bool Visible
        {
            set
            {
                _visible = value;
                Refresh();
            }
        }

        public bool Finished => NextIndex >= _centers.Length;
        public Vector3 NextCenter => _centers[Mathf.Min(NextIndex, _centers.Length - 1)];

        public RingCourse(Island island, Transform parent)
        {
            _centers = island.Rings.ToArray();
            _renderers = new Renderer[_centers.Length];
            _next = GameMaterials.Unlit(new Color(1f, 0.62f, 0.10f));
            _later = GameMaterials.Unlit(new Color(0.75f, 0.85f, 0.95f));

            var builder = new MeshBuilder();
            builder.Torus(Island.RingRadius, 0.45f, 28, 6, Palette.White);
            Mesh mesh = builder.Build("Ring");

            for (int i = 0; i < _centers.Length; i++)
            {
                Vector3 before = i == 0 ? island.Spawn : _centers[i - 1];
                Vector3 after = i == _centers.Length - 1 ? island.Spawn : _centers[i + 1];
                Vector3 direction = after - before;
                direction.y = 0f;

                var ring = new GameObject("Ring" + i);
                ring.transform.SetParent(parent, false);
                ring.transform.SetPositionAndRotation(_centers[i], Quaternion.LookRotation(direction.normalized, Vector3.up));
                ring.AddComponent<MeshFilter>().sharedMesh = mesh;
                _renderers[i] = ring.AddComponent<MeshRenderer>();
                _renderers[i].shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            }
            Restart();
        }

        public void Restart()
        {
            NextIndex = 0;
            Refresh();
        }

        /// <summary>Returns true when the drone has just passed the next ring.</summary>
        public bool Check(Vector3 dronePosition)
        {
            if (Finished) return false;
            if (Vector3.Distance(dronePosition, _centers[NextIndex]) > Island.RingRadius * 0.9f) return false;
            NextIndex++;
            Refresh();
            return true;
        }

        void Refresh()
        {
            for (int i = 0; i < _renderers.Length; i++)
            {
                _renderers[i].enabled = _visible && i >= NextIndex;
                _renderers[i].sharedMaterial = i == NextIndex ? _next : _later;
            }
        }
    }
}
