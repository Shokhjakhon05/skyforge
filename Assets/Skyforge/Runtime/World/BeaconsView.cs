using Skyforge.Core;
using UnityEngine;

namespace Skyforge.World
{
    /// <summary>The weather beacon masts, a permanent part of the island.</summary>
    public static class BeaconsView
    {
        public static void Build(Island island, Transform parent)
        {
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
        }
    }
}
