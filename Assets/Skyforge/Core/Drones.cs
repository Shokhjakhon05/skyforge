using System.Collections.Generic;
using UnityEngine;

namespace Skyforge.Core
{
    /// <summary>How a drone is put together visually; the model is built from these numbers.</summary>
    public sealed class DroneLook
    {
        /// <summary>Number of rotor arms: 4, 6 or 8. A winged drone has two.</summary>
        public int Arms = 4;
        public float Scale = 1f;
        /// <summary>Body length relative to the starter drone.</summary>
        public float BodyLength = 1f;
        public float ArmReach = 1f;
        /// <summary>Palette indices, see the palette in the game assembly.</summary>
        public int BodyColor, AccentColor = 1;
        public bool Wing;
        public bool Floats;
        public bool Dish;
        public bool BigCamera;
        public bool Rack;
    }

    public sealed class DroneDefinition
    {
        public string Id;
        public int Price;
        /// <summary>Campaign stars needed before the drone can be bought.</summary>
        public int StarsRequired;
        public DroneSpec Spec;
        public DroneLook Look = new DroneLook();

        public string NameKey => "drone." + Id + ".name";
        public string RoleKey => "drone." + Id + ".role";
        public string NoteKey => "drone." + Id + ".note";
    }

    /// <summary>The fleet. Every drone differs in how it flies and what it is good for, not only in numbers.</summary>
    public static class DroneCatalog
    {
        public const string Starter = "sparrow";

        // Palette indices, duplicated here because the palette lives with the rendering code.
        const int Graphite = 0, Orange = 1, LightGrey = 2, DarkGrey = 3, Red = 4, Green = 5, LeafDark = 7, White = 10, Lens = 11;

        static readonly List<DroneDefinition> List = Build();

        public static IReadOnlyList<DroneDefinition> All => List;

        public static DroneDefinition Get(string id)
        {
            foreach (DroneDefinition drone in List)
                if (drone.Id == id) return drone;
            return List[0];
        }

        public static int IndexOf(string id)
        {
            for (int i = 0; i < List.Count; i++)
                if (List[i].Id == id) return i;
            return 0;
        }

        static DroneDefinition Make(string id, int price, int stars, DroneLook look, System.Action<DroneSpec> tune)
        {
            var spec = new DroneSpec { Id = id };
            tune(spec);
            return new DroneDefinition { Id = id, Price = price, StarsRequired = stars, Spec = spec, Look = look };
        }

        static List<DroneDefinition> Build()
        {
            return new List<DroneDefinition>
            {
                Make(Starter, 0, 0, new DroneLook { BodyColor = Graphite, AccentColor = Orange }, s => { s.MaxZoom = 2f; }),

                // Scout: sees far and reaches far, breaks easily.
                Make("kestrel", 900, 0, new DroneLook { Scale = 0.9f, BodyColor = LightGrey, AccentColor = Green, BigCamera = true }, s =>
                {
                    s.MaxSpeed = 17f; s.TiltResponse = 7.5f; s.BatterySeconds = 420f; s.SignalRange = 1300f;
                    s.OpticsRange = 1.6f; s.MaxZoom = 8f; s.DamageScale = 1.5f;
                }),

                // Racer: fast and sharp, drinks the battery.
                Make("viper", 1400, 0, new DroneLook { Scale = 0.85f, BodyLength = 1.2f, ArmReach = 0.9f, BodyColor = Red, AccentColor = Graphite }, s =>
                {
                    s.MaxSpeed = 27f; s.MaxTiltDeg = 42f; s.TiltResponse = 9f; s.MaxClimb = 9f; s.MaxDescent = 7f;
                    s.YawRateDeg = 140f; s.BatterySeconds = 300f; s.SignalRange = 800f; s.MaxZoom = 2f;
                }),

                // Hauler: shrugs off cargo and knocks, turns like a barge.
                Make("mule", 1600, 4, new DroneLook { Arms = 6, Scale = 1.25f, BodyColor = DarkGrey, AccentColor = Orange, Rack = true }, s =>
                {
                    s.MaxSpeed = 13f; s.TiltResponse = 4.5f; s.MaxClimb = 5f; s.YawRateDeg = 80f;
                    s.BatterySeconds = 560f; s.DamageScale = 0.6f; s.PayloadEase = 0.4f; s.MaxZoom = 2f;
                }),

                // Stealth: hard to notice, in no hurry.
                Make("ghost", 2400, 8, new DroneLook { Scale = 0.95f, BodyColor = Lens, AccentColor = DarkGrey }, s =>
                {
                    s.MaxSpeed = 13f; s.TiltResponse = 6.5f; s.BatterySeconds = 500f; s.SignalRange = 850f;
                    s.DetectScale = 0.45f;
                }),

                // Mountain: holds its line in wind, climbs hard.
                Make("ibex", 2800, 10, new DroneLook { Scale = 1.1f, ArmReach = 1.15f, BodyColor = LeafDark, AccentColor = LightGrey }, s =>
                {
                    s.MaxSpeed = 17f; s.MaxClimb = 9f; s.BatterySeconds = 520f; s.SignalRange = 1000f;
                    s.WindFactor = 0.4f;
                }),

                // Maritime: weather does not drain it and it can set down on water.
                Make("petrel", 3000, 12, new DroneLook { BodyColor = White, AccentColor = Orange, Floats = true }, s =>
                {
                    s.BatterySeconds = 500f; s.SignalRange = 1100f; s.WeatherProof = true; s.WaterLanding = true;
                }),

                // Night recon: fog and darkness do not shorten its sight.
                Make("owl", 3600, 15, new DroneLook { BodyColor = Graphite, AccentColor = Green, BigCamera = true }, s =>
                {
                    s.MaxSpeed = 15f; s.BatterySeconds = 520f; s.OpticsRange = 1.3f; s.Thermal = true; s.MaxZoom = 8f;
                }),

                // Signal specialist: huge range and it shrinks radar coverage, poor optics.
                Make("relay", 4200, 18, new DroneLook { Arms = 6, BodyColor = LightGrey, AccentColor = Red, Dish = true }, s =>
                {
                    s.MaxSpeed = 15f; s.BatterySeconds = 520f; s.SignalRange = 1900f; s.JamScale = 0.65f;
                    s.OpticsRange = 0.8f; s.MaxZoom = 2f;
                }),

                // Long range wing: enormous endurance and reach, cannot hover.
                Make("albatross", 5200, 21, new DroneLook { Arms = 2, Scale = 1.3f, BodyLength = 1.5f, BodyColor = White, AccentColor = DarkGrey, Wing = true }, s =>
                {
                    s.MaxSpeed = 30f; s.MinSpeed = 9f; s.TiltResponse = 3.5f; s.YawRateDeg = 55f; s.MaxClimb = 7f;
                    s.BatterySeconds = 1100f; s.SignalRange = 2400f;
                }),

                // Strike: munition lands closer to the aim, but the drone is easy to notice.
                Make("talon", 6500, 24, new DroneLook { Scale = 1.15f, BodyLength = 1.2f, BodyColor = DarkGrey, AccentColor = Red, Rack = true }, s =>
                {
                    s.MaxSpeed = 19f; s.BatterySeconds = 420f; s.StrikeBonus = 4f; s.WindCarry = 0.2f;
                    s.DetectScale = 1.4f;
                }),

                // Prototype: good at everything, priced accordingly.
                Make("aegis", 15000, 30, new DroneLook { Arms = 8, Scale = 1.1f, BodyColor = White, AccentColor = Lens, BigCamera = true }, s =>
                {
                    s.MaxSpeed = 24f; s.TiltResponse = 9f; s.MaxClimb = 9f; s.BatterySeconds = 700f; s.SignalRange = 1600f;
                    s.OpticsRange = 1.4f; s.MaxZoom = 8f; s.DetectScale = 0.7f; s.WindFactor = 0.6f;
                }),
            };
        }
    }

    public enum UpgradeTrack
    {
        Motors,
        Battery,
        Radio,
        Optics,
        Frame,
    }

    public sealed class ModuleDefinition
    {
        public string Id;
        public int Price;
        public string NameKey => "module." + Id + ".name";
        public string NoteKey => "module." + Id + ".note";
    }

    /// <summary>
    /// Turns a drone, its upgrade levels and its modules into the spec that actually flies.
    /// Every upgrade and module gives something and takes something.
    /// </summary>
    public static class Loadout
    {
        public const int MaxLevel = 5;
        public const int Tracks = 5;
        public const int ModuleSlots = 2;
        /// <summary>Upgrade levels from this one up also cost parts.</summary>
        public const int PartsFromLevel = 4;

        public static readonly ModuleDefinition[] Modules =
        {
            new ModuleDefinition { Id = "thermal", Price = 1200 },
            new ModuleDefinition { Id = "amplifier", Price = 900 },
            new ModuleDefinition { Id = "decoy", Price = 1500 },
            new ModuleDefinition { Id = "gear", Price = 1100 },
            new ModuleDefinition { Id = "scanner", Price = 1000 },
            new ModuleDefinition { Id = "powercell", Price = 1300 },
        };

        public static ModuleDefinition Module(string id)
        {
            foreach (ModuleDefinition module in Modules)
                if (module.Id == id) return module;
            return null;
        }

        /// <param name="levels">Level 0..5 per <see cref="UpgradeTrack"/>; null means no upgrades.</param>
        public static DroneSpec Apply(DroneDefinition drone, int[] levels, IList<string> modules)
        {
            DroneSpec spec = drone.Spec.Clone();

            int Level(UpgradeTrack track)
            {
                int index = (int)track;
                return levels != null && index < levels.Length ? Mathf.Clamp(levels[index], 0, MaxLevel) : 0;
            }

            int motors = Level(UpgradeTrack.Motors);
            spec.MaxSpeed *= 1f + 0.05f * motors;
            spec.TiltResponse *= 1f + 0.06f * motors;
            spec.MaxClimb *= 1f + 0.05f * motors;
            spec.BatterySeconds *= 1f - 0.03f * motors;

            int battery = Level(UpgradeTrack.Battery);
            spec.BatterySeconds *= 1f + 0.09f * battery;
            spec.TiltResponse *= 1f - 0.03f * battery;

            int radio = Level(UpgradeTrack.Radio);
            spec.SignalRange *= 1f + 0.09f * radio;
            spec.DetectScale *= 1f + 0.03f * radio;

            int optics = Level(UpgradeTrack.Optics);
            spec.OpticsRange *= 1f + 0.09f * optics;
            spec.BatterySeconds *= 1f - 0.02f * optics;

            int frame = Level(UpgradeTrack.Frame);
            spec.DamageScale *= 1f - 0.10f * frame;
            spec.MaxSpeed *= 1f - 0.02f * frame;

            if (modules != null)
            {
                foreach (string module in modules)
                {
                    switch (module)
                    {
                        case "thermal":
                            spec.Thermal = true;
                            spec.BatterySeconds *= 0.95f;
                            break;
                        case "amplifier":
                            spec.SignalRange *= 1.25f;
                            spec.DetectScale *= 1.1f;
                            break;
                        case "decoy":
                            spec.DetectScale *= 0.7f;
                            spec.MaxSpeed *= 0.96f;
                            break;
                        case "gear":
                            spec.SafeTouchdownSpeed *= 1.7f;
                            spec.DamageScale *= 0.6f;
                            spec.TiltResponse *= 0.95f;
                            break;
                        case "scanner":
                            spec.ScanSpeed *= 1.4f;
                            spec.BatterySeconds *= 0.95f;
                            break;
                        case "powercell":
                            spec.BatterySeconds *= 1.25f;
                            spec.MaxSpeed *= 0.93f;
                            break;
                    }
                }
            }
            return spec;
        }

        /// <summary>Price of the next level on a track.</summary>
        /// <param name="currentLevel">The level the drone has now, 0..4.</param>
        public static void UpgradeCost(DroneDefinition drone, int currentLevel, out int credits, out int parts)
        {
            int next = currentLevel + 1;
            credits = Mathf.RoundToInt((drone.Price * 0.12f + 120f) * next / 10f) * 10;
            parts = next >= PartsFromLevel ? (next - PartsFromLevel + 1) * 2 : 0;
        }
    }
}
