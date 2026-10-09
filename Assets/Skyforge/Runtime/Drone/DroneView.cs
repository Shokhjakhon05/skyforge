using Skyforge.Core;
using Skyforge.World;
using UnityEngine;

namespace Skyforge.Drone
{
    /// <summary>
    /// The visible quadcopter, assembled from simple solids. Drawn larger than a real drone of
    /// this class so it reads well on a phone from the chase camera. Nose points along +Z.
    /// </summary>
    public sealed class DroneView : MonoBehaviour
    {
        const float ArmReach = 0.62f;
        Transform _tilt;
        readonly Transform[] _rotors = new Transform[4];
        Renderer[] _renderers;
        float _rotorSpeed;

        public static DroneView Create(Transform parent)
        {
            var root = new GameObject("Drone");
            root.transform.SetParent(parent, false);
            var view = root.AddComponent<DroneView>();
            view.Build();
            return view;
        }

        void Build()
        {
            _tilt = new GameObject("Tilt").transform;
            _tilt.SetParent(transform, false);

            var body = new MeshBuilder();
            body.Box(new Vector3(0f, 0f, 0f), new Vector3(0.36f, 0.16f, 0.62f), Palette.Graphite);
            body.Box(new Vector3(0f, 0.09f, -0.04f), new Vector3(0.26f, 0.06f, 0.40f), Palette.Orange);
            body.Box(new Vector3(0f, -0.02f, 0.33f), new Vector3(0.20f, 0.14f, 0.10f), Palette.DarkGrey);
            body.Box(new Vector3(0f, -0.04f, 0.39f), new Vector3(0.11f, 0.09f, 0.04f), Palette.Lens);
            body.Box(new Vector3(0f, 0.02f, -0.32f), new Vector3(0.22f, 0.05f, 0.03f), Palette.Red);

            for (int i = 0; i < 4; i++)
            {
                Vector3 direction = ArmDirection(i);
                Vector3 tip = direction * ArmReach;
                Quaternion along = Quaternion.LookRotation(direction, Vector3.up);
                body.Box(tip * 0.55f, new Vector3(0.07f, 0.05f, ArmReach * 0.95f), along, Palette.DarkGrey);
                body.Cylinder(tip + Vector3.down * 0.05f, 0.075f, 0.075f, 0.16f, 10, Palette.LightGrey);
                // Leg under each motor; its foot is the lowest point of the drone.
                body.Box(tip + Vector3.down * 0.215f, new Vector3(0.04f, 0.33f, 0.04f), Palette.Graphite);
                // Navigation lights: green at the front, red at the back.
                body.Box(tip + new Vector3(0f, -0.07f, 0f) + direction * 0.09f, new Vector3(0.05f, 0.04f, 0.05f),
                    direction.z > 0f ? Palette.Green : Palette.Red);
            }
            AddPart("Body", _tilt, body.Build("DroneBody"));

            var blades = new MeshBuilder();
            blades.Box(Vector3.zero, new Vector3(0.62f, 0.012f, 0.06f), Palette.LightGrey);
            blades.Box(Vector3.zero, new Vector3(0.06f, 0.012f, 0.62f), Palette.LightGrey);
            Mesh bladeMesh = blades.Build("Rotor");
            for (int i = 0; i < 4; i++)
            {
                _rotors[i] = AddPart("Rotor" + i, _tilt, bladeMesh);
                _rotors[i].localPosition = ArmDirection(i) * ArmReach + Vector3.up * 0.125f;
            }

            _renderers = GetComponentsInChildren<Renderer>();
        }

        static Vector3 ArmDirection(int index)
        {
            float x = index % 2 == 0 ? 1f : -1f;
            float z = index < 2 ? 1f : -1f;
            return new Vector3(x, 0f, z).normalized;
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
            for (int i = 0; i < 4; i++)
            {
                float direction = i == 0 || i == 3 ? 1f : -1f;
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

        public int NextIndex { get; private set; }
        public int Count => _centers.Length;
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
                _renderers[i].enabled = i >= NextIndex;
                _renderers[i].sharedMaterial = i == NextIndex ? _next : _later;
            }
        }
    }
}
