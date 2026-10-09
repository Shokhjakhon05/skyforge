using System.Collections.Generic;
using Skyforge.Core;
using UnityEngine;
using UnityEngine.Rendering;

namespace Skyforge.World
{
    /// <summary>
    /// The beacon masts, and above each one a ring that outlines where the drone has to hover:
    /// orange for the current target, pale for later ones, green once scanned.
    /// </summary>
    public sealed class BeaconsView
    {
        readonly List<Renderer> _zones = new List<Renderer>();
        readonly List<Transform> _zoneTransforms = new List<Transform>();
        readonly Material _current, _later, _done;
        readonly Mesh _zoneMesh;
        readonly Transform _parent;

        public BeaconsView(Island island, Transform parent)
        {
            _parent = parent;
            _current = GameMaterials.Unlit(new Color(1f, 0.62f, 0.10f));
            _later = GameMaterials.Unlit(new Color(0.75f, 0.85f, 0.95f));
            _done = GameMaterials.Unlit(new Color(0.35f, 0.85f, 0.45f));

            var masts = new MeshBuilder();
            foreach (Vector3 beacon in island.Beacons)
            {
                float h = Island.BeaconHeight;
                masts.Cylinder(beacon - Vector3.up * 1f, 1.6f, 1.3f, 1.5f, 8, Palette.Concrete);
                masts.Cylinder(beacon, 0.35f, 0.18f, h, 6, Palette.LightGrey);
                masts.Box(beacon + Vector3.up * (h * 0.55f), new Vector3(1.5f, 1.1f, 1.5f), Palette.Orange);
                masts.Box(beacon + Vector3.up * (h * 0.85f), new Vector3(3.2f, 0.12f, 0.12f), Palette.DarkGrey);
                masts.Box(beacon + Vector3.up * (h * 0.85f), new Vector3(0.12f, 0.12f, 3.2f), Palette.DarkGrey);
                masts.Box(beacon + Vector3.up * (h + 0.2f), new Vector3(0.5f, 0.5f, 0.5f), Palette.Red);
            }
            var obj = new GameObject("Beacons");
            obj.transform.SetParent(parent, false);
            obj.AddComponent<MeshFilter>().sharedMesh = masts.Build("Beacons");
            obj.AddComponent<MeshRenderer>().sharedMaterial = GameMaterials.PaletteLit;

            var ring = new MeshBuilder();
            ring.Torus(1f, 0.03f, 40, 5, Palette.White);
            _zoneMesh = ring.Build("ScanZone");
        }

        /// <summary>Creates one hover ring per scan objective of the mission.</summary>
        public void Show(MissionDefinition mission)
        {
            Hide();
            foreach (ObjectiveDefinition objective in mission.Objectives)
            {
                if (objective.Type != ObjectiveType.Scan) continue;
                var zone = new GameObject("ScanZone");
                zone.transform.SetParent(_parent, false);
                // The torus is built upright; lay it flat and size it to the scan radius.
                zone.transform.SetPositionAndRotation(objective.Position, Quaternion.Euler(90f, 0f, 0f));
                zone.transform.localScale = Vector3.one * objective.Radius;
                zone.AddComponent<MeshFilter>().sharedMesh = _zoneMesh;
                var renderer = zone.AddComponent<MeshRenderer>();
                renderer.shadowCastingMode = ShadowCastingMode.Off;
                _zones.Add(renderer);
                _zoneTransforms.Add(zone.transform);
            }
        }

        public void Hide()
        {
            foreach (Transform zone in _zoneTransforms) Object.Destroy(zone.gameObject);
            _zones.Clear();
            _zoneTransforms.Clear();
        }

        /// <param name="scansDone">How many scan objectives are already complete.</param>
        public void Refresh(int scansDone)
        {
            for (int i = 0; i < _zones.Count; i++)
                _zones[i].sharedMaterial = i < scansDone ? _done : i == scansDone ? _current : _later;
        }
    }
}
