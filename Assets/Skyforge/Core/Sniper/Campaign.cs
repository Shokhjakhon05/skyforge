using System.Collections.Generic;
using UnityEngine;

namespace Sniper
{
    /// <summary>The places and the contracts, laid out by hand.</summary>
    public static class Campaign
    {
        static List<LocationDef> _locations;
        static List<MissionDef> _missions;

        public static List<LocationDef> Locations => _locations ?? (_locations = BuildLocations());
        public static List<MissionDef> Missions => _missions ?? (_missions = BuildMissions());

        static List<LocationDef> _extra;
        /// <summary>Places no contract is set in: the range and the reserve.</summary>
        public static List<LocationDef> Extra => _extra ?? (_extra = BuildExtra());

        public static LocationDef Location(string id)
        {
            foreach (LocationDef location in Locations)
                if (location.Id == id) return location;
            foreach (LocationDef location in Extra)
                if (location.Id == id) return location;
            return Locations[0];
        }

        public static MissionDef Mission(string id)
        {
            foreach (MissionDef mission in Missions)
                if (mission.Id == id) return mission;
            return null;
        }

        static Vector3 V(float x, float z) => new Vector3(x, 0f, z);

        static void Put(LocationDef l, string model, float x, float z, float yaw = 0f, float height = 0f, bool open = false, bool thin = false, float y = 0f)
        {
            l.Pieces.Add(new Piece { Model = model, At = new Vector3(x, y, z), YawDeg = yaw, Height = height, Open = open, Thin = thin });
        }

        /// <summary>A drum of fuel that a round sets off.</summary>
        static void Boom(LocationDef l, float x, float z, string model = "barrel", float height = 0f)
        {
            l.Pieces.Add(new Piece { Model = model, At = new Vector3(x, 0f, z), YawDeg = x * 37f, Height = height, Explosive = true });
        }

        /// <summary>A plain painted block: a stripe, a kerb, a platform.</summary>
        static void Slab(LocationDef l, float x, float z, float sx, float sy, float sz, string tint, float yaw = 0f, float y = 0f, bool open = true)
        {
            l.Pieces.Add(new Piece { Model = "box", At = new Vector3(x, y, z), YawDeg = yaw, Solid = new Vector3(sx, sy, sz), Tint = tint, Open = open });
        }

        /// <summary>A model at a plain multiple of its own size.</summary>
        static void Scaled(LocationDef l, string model, float x, float z, float yaw, float scale, bool open = true)
        {
            l.Pieces.Add(new Piece { Model = model, At = new Vector3(x, 0f, z), YawDeg = yaw, Scale = scale, Open = open });
        }

        /// <summary>The same thing repeated along a line, end to end.</summary>
        static void Row(LocationDef l, string model, float x, float z, float stepX, float stepZ, int count, float yaw = 0f, float height = 0f, bool open = false, bool thin = false)
        {
            for (int i = 0; i < count; i++) Put(l, model, x + stepX * i, z + stepZ * i, yaw, height, open, thin);
        }

        static List<LocationDef> BuildExtra()
        {
            var list = new List<LocationDef>();

            // The range: five lanes on grass between earth banks, a board every fifty metres saying how far.
            var range = new LocationDef { Id = "range", NameKey = "loc.range", Ground = "grass", Light = "day", HalfSize = 260f, Ambience = "amb_wind", AmbienceVolume = 0.2f };
            Slab(range, 0f, -122f, 56f, 0.12f, 9f, "9a9a94");
            for (int lane = 0; lane <= 5; lane++) Slab(range, -25f + lane * 10f, 40f, 0.18f, 0.03f, 320f, "e8e8e0");
            for (int mark = 1; mark <= 6; mark++)
            {
                float z = Range.Line.z + mark * 50f;
                Slab(range, 0f, z, 50f, 0.03f, 0.25f, "e8e8e0");
                Slab(range, -28.5f, z, 2.2f, 1.5f, 0.12f, mark % 2 == 0 ? "f2c230" : "e8e8e0", 0f, 0.9f);
                Slab(range, -28.5f, z, 0.14f, 0.9f, 0.14f, "4a4a4a");
                Slab(range, 28.5f, z, 2.2f, 1.5f, 0.12f, mark % 2 == 0 ? "f2c230" : "e8e8e0", 0f, 0.9f);
                Slab(range, 28.5f, z, 0.14f, 0.9f, 0.14f, "4a4a4a");
            }
            // Banks: one behind the last board, one down each side. A round that misses stops in earth.
            Slab(range, 0f, 205f, 96f, 9f, 10f, "6a5a3e", 0f, 0f, false);
            Slab(range, -38f, 40f, 6f, 3.5f, 330f, "6a5a3e", 0f, 0f, false);
            Slab(range, 38f, 40f, 6f, 3.5f, 330f, "6a5a3e", 0f, 0f, false);
            Put(range, "shed", -22f, -140f, 0f, 6f, true);
            Put(range, "lookout", 24f, -134f, 0f, 9f, true);
            Put(range, "truck", 12f, -140f, 100f, 0f, true);
            Put(range, "pickup", 4f, -142f, 80f, 0f, true);
            Row(range, "crate", -8f, -118.5f, 1.3f, 0f, 3, 0f, 0.9f, true);
            Row(range, "barrel", 8f, -118.5f, 1.1f, 0f, 2, 0f, 1.1f, true);
            for (int i = 0; i < 46; i++)
            {
                float side = i % 2 == 0 ? -1f : 1f;
                Put(range, "tree", side * (52f + (i * 37) % 60), -150f + i * 8.4f + (i * 13) % 7, i * 53f, 8f + (i * 7) % 6, true);
            }
            for (int i = 0; i < 14; i++) Put(range, "tree", -120f + i * 18f + (i * 11) % 9, 232f + (i * 17) % 30, i * 71f, 10f + (i * 5) % 7, true);
            list.Add(range);

            // The reserve: a meadow among trees, a stream of logs and a keeper's hut, seen from a high seat at the wood's edge.
            var wild = new LocationDef { Id = "wild", NameKey = "loc.wild", Ground = "grass", Light = "day", HalfSize = 260f, Ambience = "amb_wind", AmbienceVolume = 0.25f };
            Put(wild, "lookout", 0f, -135f, 0f, 8.4f, true);
            Put(wild, "hut", 60f, -40f, 200f, 4.2f);
            Put(wild, "cabin", -84f, 64f, 120f, 5.5f);
            Put(wild, "well", 52f, -32f, 0f, 2.2f);
            Row(wild, "fence_wood", 44f, -52f, 3f, 0f, 8, 0f, 1.3f, false, true);
            for (int i = 0; i < 7; i++) Put(wild, "logs", -70f + i * 23f + (i * 13) % 11, -22f + (i * 29) % 110, i * 47f, 1.1f);
            for (int i = 0; i < 9; i++)
            {
                float x = -66f + (i * 61) % 130, z = -10f + (i * 43) % 100;
                Slab(wild, x, z, 2.2f + i % 3, 1f + (i % 2) * 0.6f, 1.8f + (i * 7) % 3, "77787a", i * 33f, 0f, false);
            }
            for (int i = 0; i < 260; i++)
            {
                // A ring of wood round the meadow, thinning inward; nothing where the animals graze.
                float angle = i * 2.399963f, away = 78f + (i * 29) % 150;
                float x = Mathf.Sin(angle) * away, z = 30f + Mathf.Cos(angle) * away * 0.95f;
                // Nothing between the high seat and the meadow: the whole of it is in view from there.
                if (z < 70f && Mathf.Abs(x) < 95f) continue;
                Put(wild, "tree", x, z, i * 67f, 8f + (i * 11) % 8, true);
            }
            // The wood closes in behind the seat and down both flanks.
            for (int i = 0; i < 40; i++)
            {
                float side = i % 2 == 0 ? -1f : 1f;
                Put(wild, "tree", side * (100f + (i * 37) % 70), -150f + i * 6.5f + (i * 13) % 9, i * 59f, 9f + (i * 7) % 7, true);
            }
            for (int i = 0; i < 12; i++) Put(wild, "tree", -110f + i * 20f + (i * 11) % 8, -150f - (i * 17) % 30, i * 43f, 10f + i % 5, true);
            list.Add(wild);
            return list;
        }

        static List<LocationDef> BuildLocations()
        {
            var list = new List<LocationDef>();

            // The docks: a container yard on a quay, sheds either side, a ship alongside and the city across the water.
            var docks = new LocationDef
            {
                Id = "docks", NameKey = "loc.docks", Ground = "concrete", Light = "day", WaterZ = 50f, HalfSize = 260f,
                MenuEye = new Vector3(26f, 4.5f, -24f), MenuLook = new Vector3(-4f, 3.5f, 14f),
            };
            string[] boxes = { "container_red", "container_green" };
            for (int block = 0; block < 3; block++)
            for (int row = 0; row < 4; row++)
            for (int i = 0; i < 6; i++)
            {
                if ((block * 2 + row + i) % 7 == 0) continue;
                float x = -62f + block * 44f + i * 6.1f, z = 25f + row * 2.9f;
                int high = 1 + (block * 7 + row * 3 + i * 5) % 3;
                for (int level = 0; level < high; level++) Put(docks, boxes[(block + row + i + level) % 2], x, z, 0f, 0f, false, false, level * 2.62f);
            }
            // Walls of boxes down both sides of the yard.
            for (int i = 0; i < 11; i++)
            {
                float z = -22f + i * 6.1f;
                for (int level = 0; level < 1 + (i * 3) % 3; level++)
                {
                    Put(docks, boxes[(i + level) % 2], -80f, z, 90f, 0f, false, false, level * 2.62f);
                    Put(docks, boxes[(i + level + 1) % 2], 80f, z, 90f, 0f, false, false, level * 2.62f);
                }
            }
            for (int i = 0; i < 4; i++) Put(docks, "container_plain", -46f + (i % 2) * 4.8f, -4f - (i / 2) * 2.6f, 4f);
            for (int i = 0; i < 3; i++) Put(docks, boxes[i % 2], 36f, -8f + i * 2.9f, 90f);
            Put(docks, "container_red", 36.2f, -5f, 90f, 0f, false, false, 2.62f);
            Put(docks, "warehouse", -108f, 8f, 0f, 14f);
            Put(docks, "warehouse", 108f, 12f, 180f, 14f);
            Put(docks, "shed", -104f, -46f, 90f, 16f);
            Put(docks, "block", 104f, -50f, -90f, 19f);
            Put(docks, "crane", -40f, 44f, 20f, 44f, true);
            Put(docks, "crane", 38f, 44f, -30f, 40f, true);
            Put(docks, "cargo", 6f, 76f, 90f, 30f, false, false, -1.4f);
            Put(docks, "tug", -96f, 74f, 60f, 9f, true, false, -1f);
            Put(docks, "watertower", -62f, -52f, 0f, 13f, true);
            Row(docks, "tank", 58f, -54f, 4.2f, 0f, 4, 0f, 4.5f);
            // The city across the water, far enough to be haze.
            string[] towers = { "sky_a", "kay_b", "sky_b", "kay_c", "kay_f", "sky_a", "kay_a", "sky_b", "kay_d", "sky_a", "kay_b", "sky_b", "kay_f" };
            for (int i = 0; i < towers.Length; i++)
                Put(docks, towers[i], -420f + i * 70f, 470f + (i % 3) * 40f, i * 37f, 70f + (i * 29) % 80, true, false, -1f);
            Put(docks, "truck", -8f, 6f, 25f);
            Put(docks, "truck", 14f, 12f, -160f);
            Put(docks, "pickup", 22f, -6f, 70f);
            Put(docks, "armored", 54f, -16f, 200f);
            Row(docks, "suv", -34f, -22f, 4.4f, 0f, 1, -8f);
            Put(docks, "car", -29f, -22f, 5f);
            Put(docks, "sports", -24f, -22.4f, -3f);
            Put(docks, "police", 26f, -22f, 172f);
            Put(docks, "tower", 84f, -31f, 0f, 10f);
            Put(docks, "tower", -84f, -31f, 0f, 10f);
            Row(docks, "fence_metal", -80f, -30f, 3.56f, 0f, 19, 0f, 0f, false, true);
            Row(docks, "fence_metal", 14f, -30f, 3.56f, 0f, 19, 0f, 0f, false, true);
            Row(docks, "barrier", -6f, -30f, 2.4f, 0f, 6, 0f, 0.9f);
            Row(docks, "light", -66f, -26f, 22f, 0f, 7, 0f, 0f, true);
            Row(docks, "light", -55f, 21f, 22f, 0f, 6, 180f, 0f, true);
            Boom(docks, -4f, -2f);
            Boom(docks, -2.6f, -1.4f);
            Boom(docks, 10f, 9f);
            foreach (Vector3 p in new[] { V(6f, 2f), V(26f, 6f), V(27.2f, 6.6f), V(-14f, 16f), V(-30f, 4f), V(48f, 18f), V(49f, 19.2f), V(-58f, 14f), V(-57f, 15.4f), V(62f, -6f) })
                Put(docks, "barrel", p.x, p.z, p.x * 40f);
            foreach (Vector3 p in new[] { V(2f, 4f), V(3.2f, 4.2f), V(2.5f, 5.3f), V(-12f, -6f), V(18f, 18f), V(19.1f, 18.3f), V(-26f, 12f), V(-52f, 4f), V(-51f, 5.2f), V(60f, 10f), V(61.2f, 10.4f), V(40f, 20f) })
                Put(docks, "crate", p.x, p.z, p.z * 25f, 1.1f);
            Put(docks, "crate", 2.6f, 4.4f, 20f, 1.1f, false, false, 1.1f);
            Put(docks, "crate", 60.5f, 10.2f, 12f, 1.1f, false, false, 1.1f);
            foreach (Vector3 p in new[] { V(8f, -4f), V(-18f, 2f), V(30f, 14f), V(-40f, 18f), V(56f, 2f), V(-64f, -10f), V(66f, 16f) }) Put(docks, "pallet", p.x, p.z, p.x * 17f);
            Put(docks, "dumpster", -38f, -18f, 0f, 1.6f);
            Put(docks, "dumpster", 50f, -2f, 90f, 1.6f);
            Put(docks, "dumpster", -70f, 16f, 90f, 1.6f);
            docks.Traffic.Add(new Traffic { Model = "truck", From = new Vector3(-170f, 0f, -40f), To = new Vector3(170f, 0f, -40f), Speed = 8f });
            docks.Traffic.Add(new Traffic { Model = "pickup", From = new Vector3(170f, 0f, -45f), To = new Vector3(-170f, 0f, -45f), Speed = 11f, Offset = 0.4f });
            docks.Traffic.Add(new Traffic { Model = "tug", From = new Vector3(-260f, -1f, 130f), To = new Vector3(260f, -1f, 130f), Speed = 3.5f, Height = 9f, Offset = 0.3f });
            docks.Crowd.Add(Walker("worker", V(-70f, -12f), V(-66f, 14f)));
            docks.Crowd.Add(Walker("farmer", V(68f, 14f), V(70f, -14f)));
            docks.Ambience = "amb_town";
            docks.AmbienceVolume = 0.18f;
            list.Add(docks);

            // Downtown: one long street under a wall of house fronts, with the towers of the centre behind them.
            var town = new LocationDef
            {
                Id = "town", NameKey = "loc.town", Ground = "asphalt", Light = "dusk", HalfSize = 260f,
                MenuEye = new Vector3(-34f, 4f, -18f), MenuLook = new Vector3(6f, 6f, 20f),
            };
            string[] fronts = { "block", "kay_b", "shed", "kay_c", "kay_f", "block", "kay_a", "shed", "kay_d", "kay_b", "block" };
            float[] tall = { 21f, 20f, 18f, 23f, 20f, 19f, 17f, 20f, 16f, 22f, 20f };
            for (int i = 0; i < fronts.Length; i++) Put(town, fronts[i], -100f + i * 20f, 34f, 180f, tall[i]);
            for (int i = 0; i < 9; i++) Put(town, i % 3 == 0 ? "sky_a" : i % 3 == 1 ? "kay_c" : "sky_b", -108f + i * 27f, 66f, i * 90f, 44f + (i * 17) % 30, true);
            for (int i = 0; i < 7; i++) Put(town, i % 2 == 0 ? "sky_b" : "sky_a", -150f + i * 50f, 120f, 0f, 70f + (i * 23) % 40, true);
            for (int i = 0; i < 4; i++)
            {
                Put(town, fronts[(i + 2) % fronts.Length], -124f, -30f + i * 21f, 90f, tall[(i + 3) % tall.Length]);
                Put(town, fronts[(i + 5) % fronts.Length], 124f, -30f + i * 21f, -90f, tall[(i + 1) % tall.Length]);
            }
            Row(town, "light", -90f, 20f, 20f, 0f, 10, 180f, 0f, true);
            Row(town, "light", -80f, -24f, 20f, 0f, 9, 0f, 0f, true);
            Row(town, "tree", -98f, 22.5f, 28f, 0f, 8, 0f, 7.5f, true);
            Row(town, "bench", -70f, 21f, 28f, 0f, 6, 180f, 0.9f);
            Put(town, "traffic", -42f, 18f, 90f, 0f, true);
            Put(town, "traffic", 46f, 18f, 90f, 0f, true);
            string[] parked = { "suv", "car", "sports", "pickup", "car", "police", "suv", "sports", "truck", "car" };
            for (int i = 0; i < parked.Length; i++)
            {
                if (i == 4) continue;
                Put(town, parked[i], -84f + i * 18.5f, 15.5f, 90f + (i % 2) * 180f);
            }
            Put(town, "truck", -58f, -18f, 80f);
            Put(town, "armored", 62f, -19f, -95f);
            Put(town, "suv", 20f, -20f, 90f);
            Put(town, "car", -18f, -20.5f, -90f);
            Row(town, "barrier", -10f, -27f, 2.4f, 0f, 9, 0f, 0.9f);
            Put(town, "dumpster", -72f, 24f, 0f, 1.6f);
            Put(town, "dumpster", 34f, 24.5f, 0f, 1.6f);
            Put(town, "dumpster", 90f, 24f, 0f, 1.6f);
            Boom(town, -12f, 21.5f);
            Boom(town, 30f, 21.5f);
            foreach (Vector3 p in new[] { V(-30f, 23f), V(-29f, 24f), V(8f, 24f), V(54f, 23.4f), V(-88f, 10f) }) Put(town, "barrel", p.x, p.z, p.x * 31f);
            foreach (Vector3 p in new[] { V(-50f, 24f), V(-49f, 24.8f), V(70f, 24f), V(16f, 23.6f) }) Put(town, "crate", p.x, p.z, p.x * 13f, 1.1f);
            Row(town, "fence_wood", -108f, -34f, 5.9f, 0f, 16, 0f, 1.4f, false, true);
            Row(town, "fence_wood", 20f, -34f, 5.9f, 0f, 16, 0f, 1.4f, false, true);
            string[] driving = { "car", "suv", "sports", "pickup", "police", "car" };
            for (int i = 0; i < driving.Length; i++)
            {
                bool east = i % 2 == 0;
                town.Traffic.Add(new Traffic
                {
                    Model = driving[i], From = new Vector3(east ? -160f : 160f, 0f, east ? -9f : -13.5f), To = new Vector3(east ? 160f : -160f, 0f, east ? -9f : -13.5f),
                    Speed = 9f + (i * 7) % 5, Offset = i * 0.17f,
                });
            }
            float[] strollers = { -96f, -52f, -8f, 38f, 82f };
            string[] people = { "casual", "worker", "farmer", "adventurer", "casual" };
            for (int i = 0; i < strollers.Length; i++)
                town.Crowd.Add(Walker(people[i], V(strollers[i], 26.5f), V(strollers[i] + (i % 2 == 0 ? 34f : -30f), 27.5f)));
            town.Ambience = "amb_town";
            town.AmbienceVolume = 0.5f;
            list.Add(town);

            // The compound: a walled yard in the sand, a tower on every corner.
            var fort = new LocationDef
            {
                Id = "fort", NameKey = "loc.fort", Ground = "sand", Light = "day", HalfSize = 300f,
                MenuEye = new Vector3(-58f, 7f, -30f), MenuLook = new Vector3(0f, 4f, 24f),
            };
            const float wall = 3.2f;
            for (float x = -40f; x <= 40.1f; x += wall)
            {
                if (Mathf.Abs(x) > 5f) Put(fort, "wall", x, -12f, 0f, wall, false);
                Put(fort, "wall", x, 62f, 0f, wall, false);
            }
            for (float z = -12f + wall; z < 62f; z += wall)
            {
                Put(fort, "wall", -40f, z, 90f, wall, false);
                Put(fort, "wall", 40f, z, 90f, wall, false);
            }
            foreach (Vector3 corner in new[] { V(-41f, -13f), V(41f, -13f), V(-41f, 63f), V(41f, 63f) }) Put(fort, "tower", corner.x, corner.z, 0f, 11f);
            Put(fort, "block", 0f, 46f, 180f, 15f);
            Put(fort, "kay_e", -26f, 44f, 180f, 9f);
            Put(fort, "kay_a", 26f, 42f, 180f, 12f);
            Put(fort, "shed", -28f, 14f, 90f, 9f);
            Put(fort, "watertower", 33f, 54f, 0f, 13f, true);
            Row(fort, "tank", 22f, 20f, 4.2f, 0f, 3, 0f, 4.5f);
            Put(fort, "armored", -10f, 4f, 200f);
            Put(fort, "armored", 14f, 8f, 150f);
            Put(fort, "pickup", 2f, -24f, 20f);
            Put(fort, "truck", 28f, -2f, 100f);
            Put(fort, "suv", -4f, 22f, 60f);
            foreach (Vector3 p in new[] { V(-56f, -20f), V(-62f, 8f), V(-52f, 40f), V(56f, -18f), V(60f, 20f), V(52f, 48f), V(-20f, -30f), V(24f, -34f), V(-70f, 70f), V(74f, 76f), V(0f, 84f), V(-30f, 90f), V(36f, 96f) })
                Put(fort, "palm", p.x, p.z, p.x * 9f, 6.5f + Mathf.Abs(p.z) % 3f, true);
            Boom(fort, -14f, 20f);
            Boom(fort, -13f, 21.2f);
            Boom(fort, 4f, 2f);
            Boom(fort, 12f, 32f);
            foreach (Vector3 p in new[] { V(8f, 30f), V(30f, 10f), V(31f, 11.2f), V(-32f, 30f) }) Put(fort, "barrel", p.x, p.z, p.x * 31f);
            foreach (Vector3 p in new[] { V(-6f, 30f), V(-5f, 31f), V(16f, 24f), V(-22f, 2f), V(-21f, 3f), V(34f, 30f) }) Put(fort, "crate", p.x, p.z, p.x * 13f, 1.1f);
            Put(fort, "crate", -5.5f, 30.5f, 30f, 1.1f, false, false, 1.1f);
            Row(fort, "barrier", -4f, -16f, 2.6f, 0f, 4, 0f, 0.9f);
            Row(fort, "fence_metal", -70f, -44f, 3.56f, 0f, 12, 0f, 0f, false, true);
            Row(fort, "fence_metal", 30f, -44f, 3.56f, 0f, 12, 0f, 0f, false, true);
            fort.Traffic.Add(new Traffic { Model = "pickup", From = new Vector3(-190f, 0f, -52f), To = new Vector3(190f, 0f, -52f), Speed = 10f, Offset = 0.2f });
            fort.Ambience = "amb_wind";
            fort.AmbienceVolume = 0.5f;
            list.Add(fort);

            // The airfield at night: an apron of parked aircraft under lamps, the runway behind it, hangars beyond.
            var air = new LocationDef
            {
                Id = "air", NameKey = "loc.air", Ground = "asphalt", Light = "night", HalfSize = 300f,
                MenuEye = new Vector3(-40f, 5f, -22f), MenuLook = new Vector3(6f, 3f, 16f), Ambience = "amb_wind", AmbienceVolume = 0.3f,
            };
            Slab(air, 0f, 46f, 520f, 0.04f, 0.5f, "e8e8e8");
            Slab(air, 0f, 78f, 520f, 0.04f, 0.5f, "e8e8e8");
            for (int i = -8; i <= 8; i++) Slab(air, i * 30f, 62f, 14f, 0.04f, 0.6f, "e8e8e8");
            for (int i = -4; i <= 4; i++)
            {
                Slab(air, -230f, 62f + i * 3.2f, 16f, 0.04f, 1.4f, "e8e8e8");
                Slab(air, 230f, 62f + i * 3.2f, 16f, 0.04f, 1.4f, "e8e8e8");
            }
            for (int i = -12; i <= 12; i++)
            {
                Slab(air, i * 20f, 44.5f, 0.35f, 0.35f, 0.35f, "ffd070ff");
                Slab(air, i * 20f, 79.5f, 0.35f, 0.35f, 0.35f, "ffd070ff");
            }
            Slab(air, 0f, 30f, 180f, 0.04f, 0.35f, "d8b830");
            Put(air, "warehouse", -78f, 118f, 0f, 15f);
            Put(air, "warehouse", -26f, 120f, 0f, 16f);
            Put(air, "warehouse", 26f, 120f, 0f, 16f);
            Put(air, "warehouse", 78f, 118f, 0f, 15f);
            Put(air, "block", 150f, 104f, 180f, 18f);
            Put(air, "shed", -152f, 104f, 180f, 16f);
            Put(air, "depot_a", -92f, 2f, 90f, 8f);
            Put(air, "depot_b", 94f, 0f, -90f, 8f);
            Put(air, "tvtower", 112f, 34f, 0f, 30f, true);
            Put(air, "lookout", -112f, 28f, 0f, 13f);
            Put(air, "radar", 118f, 14f, 0f, 9f, true);
            Put(air, "plane_big", -120f, 62f, 90f, 13f);
            Put(air, "plane_small", -46f, 16f, 160f, 3.8f);
            Put(air, "plane_prop", -16f, 20f, 200f, 3.6f);
            Put(air, "jet", 30f, 18f, 180f, 5f);
            Put(air, "heli", 62f, 10f, 30f, 4.2f);
            Put(air, "truck", -66f, 24f, 80f);
            Put(air, "pickup", 8f, 2f, 110f);
            Put(air, "suv", 46f, -4f, 20f);
            Put(air, "armored", -30f, -6f, 190f);
            Row(air, "gastank", -84f, 26f, 2.6f, 0f, 4, 0f, 3.6f);
            Boom(air, -40f, 10f);
            Boom(air, -38.8f, 10.8f);
            Boom(air, 22f, 10f);
            Boom(air, 56f, 14f);
            foreach (Vector3 p in new[] { V(-22f, 12f), V(-21f, 13f), V(40f, 8f), V(70f, 18f), V(0f, 12f), V(1.2f, 12.4f), V(-58f, 6f) }) Put(air, "crate", p.x, p.z, p.x * 13f, 1.1f);
            foreach (Vector3 p in new[] { V(14f, 8f), V(-8f, 26f), V(52f, 22f) }) Put(air, "pallet", p.x, p.z, p.x * 17f);
            Row(air, "light", -78f, -8f, 26f, 0f, 7, 0f, 0f, true);
            Row(air, "light", -66f, 31f, 44f, 0f, 4, 180f, 0f, true);
            Row(air, "fence_metal", -110f, -24f, 3.56f, 0f, 62, 0f, 0f, false, true);
            Row(air, "barrier", -8f, -20f, 2.6f, 0f, 6, 0f, 0.9f);
            air.Traffic.Add(new Traffic { Model = "pickup", From = new Vector3(-220f, 0f, 36f), To = new Vector3(220f, 0f, 36f), Speed = 9f, Offset = 0.1f });
            air.Traffic.Add(new Traffic { Model = "plane_small", From = new Vector3(-280f, 0f, 62f), To = new Vector3(280f, 0f, 62f), Speed = 14f, Height = 3.8f, Offset = 0.55f });
            list.Add(air);

            // The station: a square, two platforms, a train standing and another passing behind it.
            var rail = new LocationDef
            {
                Id = "rail", NameKey = "loc.rail", Ground = "concrete", Light = "overcast", HalfSize = 300f,
                MenuEye = new Vector3(-36f, 4f, -8f), MenuLook = new Vector3(4f, 3f, 30f), Ambience = "amb_town", AmbienceVolume = 0.3f,
            };
            foreach (float z in new[] { 20f, 40f, 47f })
                for (int i = -10; i <= 10; i++) Scaled(rail, "track", i * 24.8f, z, 0f, 1.38f);
            Slab(rail, 0f, 12f, 170f, 0.3f, 8f, "8f8f8a", 0f, 0f, true);
            Slab(rail, 0f, 15.8f, 170f, 0.31f, 0.35f, "d8b830");
            Slab(rail, 0f, 30f, 170f, 0.3f, 7f, "8f8f8a", 0f, 0f, true);
            Slab(rail, 0f, 26.7f, 170f, 0.31f, 0.35f, "d8b830");
            Slab(rail, 0f, 33.3f, 170f, 0.31f, 0.35f, "d8b830");
            // The train at the far platform, and wagons in the siding beyond.
            Put(rail, "loco", -34f, 40f, 0f, 4.4f);
            for (int i = 0; i < 4; i++) Put(rail, "carriage", -22f + i * 11.4f, 40f, 0f, 4f);
            Put(rail, "cargo_loco", 96f, 47f, 180f, 4.2f);
            for (int i = 0; i < 3; i++) Put(rail, i == 1 ? "cargo_box" : "cargo_wagon", 110f + i * 14.4f, 47f, 0f, 4f);
            Put(rail, "station", 0f, 66f, 180f, 13f);
            Put(rail, "shed", -60f, 68f, 180f, 15f);
            Put(rail, "block", 58f, 70f, 180f, 17f);
            Put(rail, "warehouse", -124f, 30f, 90f, 12f);
            Put(rail, "depot_a", 104f, 14f, -90f, 8f);
            string[] beyond = { "kay_b", "sky_b", "kay_c", "sky_a", "kay_f", "sky_b", "kay_a", "kay_d" };
            for (int i = 0; i < beyond.Length; i++) Put(rail, beyond[i], -140f + i * 40f, 110f + (i % 2) * 16f, i * 90f, 30f + (i * 13) % 26, true);
            Row(rail, "light", -72f, 9f, 24f, 0f, 7, 0f, 0f, true);
            Row(rail, "light", -60f, 30f, 30f, 0f, 5, 0f, 0f, true);
            Row(rail, "bench", -50f, 9.5f, 25f, 0f, 5, 0f, 0.9f);
            Row(rail, "bench", -38f, 30f, 38f, 0f, 3, 0f, 0.9f);
            string[] taxis = { "car", "suv", "police", "sports", "car", "pickup" };
            for (int i = 0; i < taxis.Length; i++) Put(rail, taxis[i], -62f + i * 24f, -14f, 90f + (i % 2) * 180f);
            Put(rail, "truck", -92f, -6f, 30f);
            Put(rail, "armored", 86f, -10f, 200f);
            Row(rail, "barrier", -12f, -24f, 2.6f, 0f, 9, 0f, 0.9f);
            Row(rail, "fence_metal", -110f, 51f, 3.56f, 0f, 62, 0f, 0f, false, true);
            Boom(rail, -30f, 3f);
            Boom(rail, 44f, 4f);
            Boom(rail, 12f, 28.6f);
            foreach (Vector3 p in new[] { V(-56f, 4f), V(-55f, 5f), V(20f, 4f), V(66f, 6f), V(-12f, 3f) }) Put(rail, "crate", p.x, p.z, p.x * 13f, 1.1f);
            Put(rail, "dumpster", -78f, 4f, 0f, 1.6f);
            Put(rail, "dumpster", 76f, 3f, 0f, 1.6f);
            rail.Traffic.Add(new Traffic { Model = "cargo_loco", From = new Vector3(-320f, 0f, 53.5f), To = new Vector3(320f, 0f, 53.5f), Speed = 12f, Height = 4.2f, Offset = 0.2f });
            rail.Traffic.Add(new Traffic { Model = "cargo_wagon", From = new Vector3(-334.6f, 0f, 53.5f), To = new Vector3(305.4f, 0f, 53.5f), Speed = 12f, Height = 4f, Offset = 0.2f });
            rail.Traffic.Add(new Traffic { Model = "cargo_box", From = new Vector3(-349f, 0f, 53.5f), To = new Vector3(291f, 0f, 53.5f), Speed = 12f, Height = 4f, Offset = 0.2f });
            rail.Traffic.Add(new Traffic { Model = "car", From = new Vector3(-200f, 0f, -30f), To = new Vector3(200f, 0f, -30f), Speed = 10f, Offset = 0.6f });
            rail.Crowd.Add(Walker("casual", new Vector3(-60f, 0.3f, 13f), new Vector3(-20f, 0.3f, 14f)));
            rail.Crowd.Add(Walker("worker", new Vector3(64f, 0.3f, 12f), new Vector3(30f, 0.3f, 13.5f)));
            rail.Crowd.Add(Walker("farmer", new Vector3(-10f, 0.3f, 31f), new Vector3(40f, 0.3f, 30f)));
            list.Add(rail);

            // A village in the snow: a square with a well, houses round it, a church behind, pines all about.
            var snow = new LocationDef
            {
                Id = "snow", NameKey = "loc.snow", Ground = "snow", Light = "day", HalfSize = 300f,
                MenuEye = new Vector3(-30f, 4f, -16f), MenuLook = new Vector3(4f, 4f, 30f), Ambience = "amb_wind", AmbienceVolume = 0.6f,
            };
            Put(snow, "cabin", -36f, 30f, 150f, 7f);
            Put(snow, "cottage", -12f, 42f, 180f, 7.5f);
            Put(snow, "house", 16f, 40f, 180f, 8.5f);
            Put(snow, "hut", 38f, 28f, 210f, 6f);
            Put(snow, "cabin", 52f, 4f, -90f, 7f);
            Put(snow, "cottage", -56f, 2f, 90f, 7f);
            Put(snow, "hut", -64f, 32f, 120f, 6f);
            Put(snow, "house", 66f, 34f, 230f, 8f);
            Put(snow, "church", 2f, 76f, 180f, 20f);
            Put(snow, "sawmill", 74f, 60f, 200f, 6f);
            Put(snow, "well", 0f, 12f, 0f, 2.8f);
            Scaled(snow, "logs", -24f, 14f, 30f, 2.6f, false);
            Scaled(snow, "logs", 28f, 12f, 100f, 2.6f, false);
            Scaled(snow, "logs", 60f, 48f, 10f, 3f, false);
            Scaled(snow, "logs", -44f, 16f, 80f, 2.6f, false);
            for (int i = 0; i < 9; i++) Put(snow, "woodwall", -70f + i * 4.6f, -14f, 0f, 1.6f, false, true);
            for (int i = 0; i < 9; i++) Put(snow, "woodwall", 30f + i * 4.6f, -14f, 0f, 1.6f, false, true);
            Put(snow, "pickup", -14f, 2f, 40f);
            Put(snow, "armored", 24f, -6f, 160f);
            Put(snow, "suv", 44f, 16f, 250f);
            Put(snow, "truck", -44f, -4f, 100f);
            Boom(snow, -10f, 24f);
            Boom(snow, -8.8f, 24.8f);
            Boom(snow, 30f, 20f);
            foreach (Vector3 p in new[] { V(6f, 22f), V(7f, 23f), V(-28f, 4f), V(36f, 2f), V(-4f, 30f) }) Put(snow, "crate", p.x, p.z, p.x * 13f, 1.1f);
            Put(snow, "tower", -78f, -16f, 0f, 10f);
            Put(snow, "tower", 78f, -16f, 0f, 10f);
            // Pines: a ring far enough to close the horizon, and a few among the houses.
            for (int i = 0; i < 46; i++)
            {
                float angle = i * 0.137f * 6.2832f, ring = 96f + (i * 37) % 70;
                float x = Mathf.Cos(angle) * ring * 1.5f, z = 40f + Mathf.Sin(angle) * ring;
                if (z < -30f) continue;
                Put(snow, i % 5 == 0 ? "birch_snow" : i % 7 == 0 ? "dead_snow" : "pine_snow", x, z, i * 50f, i % 5 == 0 ? 9f : i % 7 == 0 ? 6f : 9f + (i * 13) % 6, true);
            }
            foreach (Vector3 p in new[] { V(-22f, 50f), V(30f, 52f), V(-48f, 44f), V(50f, 46f), V(-70f, 14f), V(72f, 12f), V(-6f, 58f) }) Put(snow, "pine_snow", p.x, p.z, p.x * 7f, 8f + Mathf.Abs(p.x) % 4f, true);
            snow.Traffic.Add(new Traffic { Model = "pickup", From = new Vector3(-220f, 0f, -34f), To = new Vector3(220f, 0f, -34f), Speed = 8f, Offset = 0.7f });
            snow.Crowd.Add(Walker("farmer", V(-46f, 22f), V(-30f, 8f)));
            snow.Crowd.Add(Walker("worker", V(54f, 22f), V(40f, 8f)));
            list.Add(snow);
            return list;
        }

        static ActorDef Walker(string model, Vector3 from, Vector3 to)
        {
            return new ActorDef { Model = model, Role = Role.Civilian, NameKey = "name.passer", Path = new[] { from, to }, Wait = 4f };
        }

        static ActorDef Man(string id, string model, Role role, string name, params Vector3[] path) =>
            new ActorDef { Id = id, Model = model, Role = role, NameKey = name, Path = path };

        static List<MissionDef> BuildMissions()
        {
            var list = new List<MissionDef>();

            // 1. One man by a truck, who will not be there for long.
            var m = new MissionDef
            {
                Id = "docks1", LocationId = "docks", TitleKey = "mission.docks1.title", BriefKey = "mission.docks1.brief", Order = 1,
                Perch = new Vector3(-6f, 26f, -150f), LookAt = new Vector3(-4f, 1.4f, 4f), Rounds = 8, Reward = 400, Par = 25f, Second = "headshots",
            };
            ActorDef dealer = Man("dealer", "punk", Role.Target, "name.dealer", V(-4f, 2f));
            dealer.YawDeg = 200f;
            dealer.Bounty = 250;
            // Far enough that a beginner who misses has three or four more rounds at a running man.
            dealer.Escape = V(62f, 18f);
            dealer.LeavesAt = 50f;
            m.Actors.Add(dealer);
            ActorDef docker = Man("docker", "worker", Role.Civilian, "name.worker", V(20f, 16f), V(4f, 18f), V(-12f, 14f));
            docker.Wait = 4f;
            m.Actors.Add(docker);
            list.Add(m);

            // 2. Two men talking. The second runs for his car the moment the first falls.
            m = new MissionDef
            {
                Id = "docks2", LocationId = "docks", TitleKey = "mission.docks2.title", BriefKey = "mission.docks2.brief", Order = 2,
                Perch = new Vector3(18f, 24f, -140f), LookAt = new Vector3(8f, 1.4f, 0f), Rounds = 8, Reward = 600, Par = 20f, Second = "headshots",
            };
            ActorDef buyer = Man("buyer", "suit", Role.Target, "name.buyer", V(7f, -1f));
            buyer.YawDeg = 90f;
            buyer.Idle = "Interact";
            buyer.Bounty = 300;
            buyer.Escape = V(44f, -10f);
            ActorDef seller = Man("seller", "hoodie", Role.Target, "name.seller", V(8.6f, -1f));
            seller.YawDeg = -90f;
            seller.Bounty = 300;
            seller.Escape = V(-44f, -12f);
            m.Actors.Add(buyer);
            m.Actors.Add(seller);
            ActorDef guard = Man("guard", "swat", Role.Guard, "name.guard", V(12f, 4f), V(0f, 6f));
            guard.Idle = "Idle_Gun";
            guard.Wait = 5f;
            m.Actors.Add(guard);
            list.Add(m);

            // 3. The boss walks his quay with two guards; the dockers must not be touched.
            m = new MissionDef
            {
                Id = "docks3", LocationId = "docks", TitleKey = "mission.docks3.title", BriefKey = "mission.docks3.brief", Order = 3,
                Perch = new Vector3(-30f, 30f, -160f), LookAt = new Vector3(0f, 1.4f, 10f), Rounds = 10, Reward = 900, Par = 40f, Second = "headshots",
                Wind = new Vector3(2.5f, 0f, 0f),
            };
            ActorDef boss = Man("boss", "suit", Role.Target, "name.boss", V(-16f, 10f), V(24f, 10f));
            boss.Wait = 6f;
            boss.Bounty = 600;
            boss.Escape = V(58f, 22f);
            m.Actors.Add(boss);
            ActorDef left = Man("g1", "swat", Role.Guard, "name.guard", V(-18f, 12f), V(22f, 12f));
            left.Wait = 6f;
            left.Idle = "Idle_Gun";
            ActorDef right = Man("g2", "swat", Role.Guard, "name.guard", V(-14f, 8f), V(26f, 8f));
            right.Wait = 6f;
            right.Idle = "Idle_Gun";
            m.Actors.Add(left);
            m.Actors.Add(right);
            m.Actors.Add(Man("w1", "worker", Role.Civilian, "name.worker", V(-30f, 2f), V(-10f, -8f)));
            m.Actors.Add(Man("w2", "casual", Role.Civilian, "name.worker", V(30f, 16f), V(34f, 2f)));
            ActorDef watch = Man("g3", "swat", Role.Guard, "name.guard", V(40f, -14f));
            watch.Idle = "Idle_Gun";
            watch.YawDeg = 200f;
            m.Actors.Add(watch);
            list.Add(m);

            // 4. Three sentries far apart. Nobody is to hear of the others.
            m = new MissionDef
            {
                Id = "docks4", LocationId = "docks", TitleKey = "mission.docks4.title", BriefKey = "mission.docks4.brief", Order = 4,
                Perch = new Vector3(40f, 28f, -150f), LookAt = new Vector3(10f, 1.4f, 0f), Rounds = 6, Reward = 1100, Par = 35f, Second = "clean",
            };
            Vector3[] posts = { V(-36f, -14f), V(6f, -12f), V(44f, 6f) };
            for (int i = 0; i < posts.Length; i++)
            {
                ActorDef sentry = Man("s" + i, "swat", Role.Target, "name.sentry", posts[i], posts[i] + new Vector3(i == 1 ? 8f : 0f, 0f, i == 1 ? 0f : 9f));
                sentry.Idle = "Idle_Gun";
                sentry.Wait = 5f + i;
                sentry.Bounty = 200;
                m.Actors.Add(sentry);
            }
            list.Add(m);

            // 5. A witness waits for his ride. Two men are walking towards him, and they are not his ride.
            m = new MissionDef
            {
                Id = "docks5", LocationId = "docks", TitleKey = "mission.docks5.title", BriefKey = "mission.docks5.brief", Order = 5,
                Perch = new Vector3(-10f, 26f, -145f), LookAt = new Vector3(0f, 1.4f, -6f), Rounds = 8, Reward = 1400, Par = 22f, Second = "headshots",
                Wind = new Vector3(-2f, 0f, 0f),
            };
            ActorDef witness = Man("witness", "casual", Role.Ally, "name.witness", V(0f, -8f));
            witness.Idle = "Idle";
            m.Actors.Add(witness);
            ActorDef first = Man("k1", "punk", Role.Target, "name.killer", V(-44f, 14f), V(-24f, 0f), V(-2f, -7f));
            first.Wait = 0.5f;
            first.Assassin = true;
            first.Bounty = 350;
            ActorDef second = Man("k2", "hoodie", Role.Target, "name.killer", V(48f, 16f), V(30f, 2f), V(2f, -7f));
            second.Wait = 0.5f;
            second.Assassin = true;
            second.Bounty = 350;
            m.Actors.Add(first);
            m.Actors.Add(second);
            m.Actors.Add(Man("w1", "worker", Role.Civilian, "name.worker", V(-20f, 16f), V(14f, 18f)));
            list.Add(m);

            // 6. The accountant leaves in forty seconds, and he knows the way to his car.
            m = new MissionDef
            {
                Id = "docks6", LocationId = "docks", TitleKey = "mission.docks6.title", BriefKey = "mission.docks6.brief", Order = 6,
                Perch = new Vector3(60f, 30f, -155f), LookAt = new Vector3(20f, 1.4f, 4f), Rounds = 8, Reward = 1700, Par = 30f, Second = "headshots", Seconds = 60f,
                Wind = new Vector3(3.5f, 0f, 0f),
            };
            ActorDef accountant = Man("acc", "suit", Role.Target, "name.accountant", V(-28f, 10f), V(-6f, 12f));
            accountant.Wait = 4f;
            accountant.Bounty = 800;
            accountant.Escape = V(22f, -4f);
            accountant.LeavesAt = 34f;
            m.Actors.Add(accountant);
            foreach (Vector3 p in new[] { V(-30f, 8f), V(-4f, 14f), V(20f, -8f) })
            {
                ActorDef body = Man("b" + m.Actors.Count, "swat", Role.Guard, "name.guard", p);
                body.Idle = "Idle_Gun";
                body.YawDeg = 180f;
                m.Actors.Add(body);
            }
            m.Actors.Add(Man("w1", "worker", Role.Civilian, "name.worker", V(8f, 4f), V(30f, 12f)));
            m.Actors.Add(Man("w2", "farmer", Role.Civilian, "name.worker", V(-14f, -4f), V(-34f, -10f)));
            list.Add(m);

            AddTown(list);
            AddFort(list);
            AddAir(list);
            AddRail(list);
            AddSnow(list);
            // The people of each place are in every contract there.
            foreach (MissionDef mission in list)
            {
                int n = 0;
                foreach (ActorDef walker in Location(mission.LocationId).Crowd)
                    mission.Actors.Add(new ActorDef { Id = "crowd" + n++, Model = walker.Model, Role = Role.Civilian, NameKey = walker.NameKey, Path = walker.Path, Wait = walker.Wait });
            }
            return list;
        }

        static MissionDef New(List<MissionDef> list, string place, Vector3 perch, Vector3 look, int rounds, int reward, float par, string second)
        {
            int order = list.Count + 1;
            int number = 1;
            foreach (MissionDef other in list)
                if (other.LocationId == place) number++;
            var mission = new MissionDef
            {
                Id = place + number, LocationId = place, TitleKey = "mission." + place + number + ".title", BriefKey = "mission." + place + number + ".brief", Order = order,
                Perch = perch, LookAt = look, Rounds = rounds, Reward = reward, Par = par, Second = second,
            };
            list.Add(mission);
            return mission;
        }

        static ActorDef Target(MissionDef m, string model, string name, int bounty, params Vector3[] path)
        {
            ActorDef actor = Man("t" + m.Actors.Count, model, Role.Target, name, path);
            actor.Bounty = bounty;
            m.Actors.Add(actor);
            return actor;
        }

        static ActorDef Guard(MissionDef m, params Vector3[] path)
        {
            ActorDef actor = Man("g" + m.Actors.Count, "swat", Role.Guard, "name.guard", path);
            actor.Idle = "Idle_Gun";
            actor.Wait = 5f;
            m.Actors.Add(actor);
            return actor;
        }

        static ActorDef Passer(MissionDef m, string model, params Vector3[] path)
        {
            ActorDef actor = Man("c" + m.Actors.Count, model, Role.Civilian, "name.passer", path);
            actor.Wait = 3f;
            m.Actors.Add(actor);
            return actor;
        }

        static void AddTown(List<MissionDef> list)
        {
            // 7. A courier on a bench with a case.
            MissionDef m = New(list, "town", new Vector3(-20f, 30f, -150f), new Vector3(-14f, 1.4f, 20f), 8, 1900, 25f, "headshots");
            ActorDef courier = Target(m, "hoodie", "name.courier", 500, V(-14f, 19f));
            courier.YawDeg = 180f;
            courier.Escape = V(-60f, 12f);
            courier.LeavesAt = 45f;
            Passer(m, "casual", V(-60f, 18f), V(30f, 18f));
            Passer(m, "worker", V(50f, 19f), V(-30f, 19f));
            Passer(m, "farmer", V(10f, 8f), V(60f, 6f));

            // 8. Two lookouts and the man they look out for.
            m = New(list, "town", new Vector3(30f, 32f, -150f), new Vector3(10f, 1.4f, 14f), 9, 2200, 35f, "clean");
            ActorDef fixer = Target(m, "suit", "name.fixer", 700, V(4f, 18f), V(26f, 18f));
            fixer.Wait = 5f;
            fixer.Escape = V(90f, 12f);
            Target(m, "punk", "name.lookout", 250, V(-22f, 12f)).YawDeg = 160f;
            Target(m, "punk", "name.lookout", 250, V(52f, 10f)).YawDeg = 200f;
            Passer(m, "casual", V(-70f, 19f), V(-30f, 19f));
            Passer(m, "worker", V(70f, 6f), V(40f, 2f));

            // 9. A crosswind down the street and a man who never stands still.
            m = New(list, "town", new Vector3(-50f, 28f, -155f), new Vector3(0f, 1.4f, 10f), 10, 2500, 40f, "headshots");
            m.Wind = new Vector3(4f, 0f, 0f);
            ActorDef walker = Target(m, "suit", "name.banker", 900, V(-50f, 8f), V(50f, 8f));
            walker.Wait = 1.5f;
            walker.Escape = V(-100f, 4f);
            Guard(m, V(-52f, 10f), V(48f, 10f)).Wait = 1.5f;
            Guard(m, V(-48f, 6f), V(52f, 6f)).Wait = 1.5f;
            Passer(m, "casual", V(-20f, 19f), V(40f, 19f));
            Passer(m, "farmer", V(60f, 19f), V(10f, 19f));

            // 10. Four of them on the pavement, and the street full of people.
            m = New(list, "town", new Vector3(10f, 34f, -150f), new Vector3(0f, 1.4f, 16f), 9, 2800, 30f, "clean");
            float[] gang = { -38f, -12f, 16f, 44f };
            for (int i = 0; i < gang.Length; i++)
            {
                ActorDef thug = Target(m, i % 2 == 0 ? "punk" : "hoodie", "name.thug", 300, V(gang[i], 19f), V(gang[i] + 6f, 17f));
                thug.Wait = 4f + i;
                thug.Escape = V(gang[i] < 0f ? -100f : 100f, 14f);
            }
            Passer(m, "casual", V(-60f, 20f), V(-20f, 20f));
            Passer(m, "worker", V(0f, 20f), V(30f, 20f));
            Passer(m, "farmer", V(60f, 18f), V(26f, 6f));
            Passer(m, "adventurer", V(-28f, 4f), V(20f, 0f));

            // 11. The informer crosses the square. Three men mean to stop him.
            m = New(list, "town", new Vector3(-10f, 30f, -150f), new Vector3(0f, 1.4f, 4f), 9, 3200, 26f, "headshots");
            m.Wind = new Vector3(-3f, 0f, 0f);
            ActorDef informer = Man("ally", "casual", Role.Ally, "name.informer", V(0f, 2f));
            m.Actors.Add(informer);
            foreach (Vector3 from in new[] { V(-70f, 18f), V(74f, 16f), V(8f, 22f) })
            {
                ActorDef hunter = Target(m, from.x < 0f ? "punk" : "hoodie", "name.killer", 400, from, V(from.x * 0.4f, 10f), V(Mathf.Sign(from.x) * 2f, 3f));
                hunter.Wait = 0.4f;
                hunter.Assassin = true;
            }
            Passer(m, "worker", V(-40f, 19f), V(-10f, 19f));

            // 12. The man who runs the street, his guard, and one minute.
            m = New(list, "town", new Vector3(60f, 36f, -160f), new Vector3(20f, 1.4f, 14f), 10, 3800, 40f, "headshots");
            m.Seconds = 75f;
            m.Wind = new Vector3(2f, 0f, 0f);
            ActorDef king = Target(m, "suit", "name.kingpin", 1500, V(20f, 19f), V(-6f, 19f));
            king.Wait = 6f;
            king.Escape = V(62f, -22f);
            king.LeavesAt = 40f;
            king.Health = 220f;
            Guard(m, V(24f, 17f), V(-2f, 17f)).Wait = 6f;
            Guard(m, V(16f, 21f), V(-10f, 21f)).Wait = 6f;
            Guard(m, V(66f, -23f)).YawDeg = 180f;
            Guard(m, V(-40f, 12f)).YawDeg = 180f;
            Passer(m, "casual", V(-70f, 19f), V(-40f, 19f));
            Passer(m, "farmer", V(80f, 19f), V(50f, 19f));
        }

        static void AddAir(List<MissionDef> list)
        {
            // 19. A pilot checks his aircraft.
            MissionDef m = New(list, "air", new Vector3(-30f, 28f, -140f), new Vector3(-16f, 1.4f, 12f), 8, 9000, 25f, "headshots");
            ActorDef pilot = Target(m, "adventurer", "name.pilot", 900, V(-20f, 12f), V(-12f, 14f));
            pilot.Wait = 6f;
            pilot.Escape = V(-16f, 19f);
            pilot.LeavesAt = 50f;
            Passer(m, "worker", V(-60f, 8f), V(-52f, 20f));

            // 20. Crates change hands under the lamp.
            m = New(list, "air", new Vector3(20f, 30f, -145f), new Vector3(0f, 1.4f, 8f), 9, 9600, 30f, "clean");
            Target(m, "suit", "name.smuggler", 800, V(-2f, 9f)).YawDeg = 90f;
            ActorDef other = Target(m, "hoodie", "name.buyer", 600, V(3.5f, 9f));
            other.YawDeg = -90f;
            other.Escape = V(46f, -2f);
            Guard(m, V(12f, 4f), V(-10f, 4f));
            Guard(m, V(-26f, -2f)).YawDeg = 180f;

            // 21. Three of them between the aircraft; one round into the right drum does for two.
            m = New(list, "air", new Vector3(-60f, 32f, -150f), new Vector3(-30f, 1.4f, 10f), 8, 10400, 30f, "clean");
            Target(m, "swat", "name.sentry", 400, V(-42f, 12.5f)).Idle = "Idle_Gun";
            Target(m, "swat", "name.sentry", 400, V(-36f, 8f)).Idle = "Idle_Gun";
            ActorDef third = Target(m, "swat", "name.sentry", 400, V(24f, 13f), V(34f, 6f));
            third.Idle = "Idle_Gun";
            third.Wait = 4f;
            Passer(m, "worker", V(60f, 20f), V(68f, 6f));

            // 22. Our man waits by the helicopter; they are coming for him out of the dark.
            m = New(list, "air", new Vector3(50f, 30f, -140f), new Vector3(58f, 1.4f, 6f), 9, 11200, 26f, "headshots");
            m.Wind = new Vector3(-2.5f, 0f, 0f);
            m.Actors.Add(Man("ally", "casual", Role.Ally, "name.agent", V(58f, 4f)));
            foreach (Vector3 from in new[] { V(10f, 24f), V(100f, 20f), V(40f, 30f) })
            {
                ActorDef hunter = Target(m, "punk", "name.killer", 500, from, V((from.x + 58f) * 0.5f, 14f), V(58f + Mathf.Sign(from.x - 58f) * 2f, 5f));
                hunter.Wait = 0.4f;
                hunter.Assassin = true;
            }

            // 23. The general boards in forty seconds.
            m = New(list, "air", new Vector3(0f, 34f, -150f), new Vector3(20f, 1.4f, 10f), 10, 12400, 35f, "headshots");
            m.Seconds = 70f;
            m.Wind = new Vector3(3f, 0f, 0f);
            ActorDef general = Target(m, "suit", "name.general", 2500, V(4f, 6f), V(20f, 10f));
            general.Wait = 5f;
            general.Health = 220f;
            general.Escape = V(30f, 15f);
            general.LeavesAt = 36f;
            Guard(m, V(2f, 8f), V(18f, 12f));
            Guard(m, V(6f, 4f), V(22f, 8f));
            Guard(m, V(44f, 4f)).YawDeg = 180f;
            Guard(m, V(-24f, 6f)).YawDeg = 180f;

            // 24. Everyone on the apron.
            m = New(list, "air", new Vector3(-10f, 36f, -150f), new Vector3(0f, 1.4f, 10f), 14, 14000, 60f, "headshots");
            m.Wind = new Vector3(-2f, 0f, 0f);
            foreach (Vector3 post in new[] { V(-44f, 6f), V(-18f, 26f), V(2f, 4f), V(26f, 24f), V(48f, 8f), V(70f, 22f) })
            {
                ActorDef man = Target(m, "swat", "name.sentry", 350, post, post + new Vector3(6f, 0f, -3f));
                man.Idle = "Idle_Gun";
                man.Wait = 5f;
            }
        }

        static Vector3 P(float x, float z) => new Vector3(x, 0.3f, z);

        static void AddRail(List<MissionDef> list)
        {
            // 25. A man with a suitcase on platform one.
            MissionDef m = New(list, "rail", new Vector3(-20f, 30f, -140f), new Vector3(-10f, 1.6f, 12f), 8, 15000, 25f, "headshots");
            ActorDef traveller = Target(m, "suit", "name.courier", 1200, P(-10f, 12f), P(8f, 13f));
            traveller.Wait = 7f;
            traveller.Escape = P(60f, 12f);
            traveller.LeavesAt = 48f;
            Passer(m, "casual", P(30f, 11f), P(-40f, 11f));

            // 26. Two on the far platform, with the near one in the way of nothing.
            m = New(list, "rail", new Vector3(30f, 34f, -145f), new Vector3(10f, 1.6f, 30f), 8, 16000, 30f, "clean");
            m.Wind = new Vector3(2f, 0f, 0f);
            Target(m, "hoodie", "name.lookout", 600, P(-6f, 30f)).YawDeg = 180f;
            ActorDef broker = Target(m, "suit", "name.fixer", 1200, P(20f, 30f), P(34f, 31f));
            broker.Wait = 5f;
            broker.Escape = P(80f, 30f);
            Guard(m, P(8f, 12f), P(28f, 13f));

            // 27. The meeting in the square.
            m = New(list, "rail", new Vector3(-50f, 30f, -150f), new Vector3(-20f, 1.4f, 0f), 9, 17000, 28f, "headshots");
            Target(m, "punk", "name.thug", 500, V(-26f, 0f)).YawDeg = 90f;
            Target(m, "hoodie", "name.thug", 500, V(-22f, 0f)).YawDeg = -90f;
            ActorDef third = Target(m, "suit", "name.banker", 1400, V(-24f, -3f));
            third.Escape = V(-62f, -12f);
            Guard(m, V(-40f, 2f), V(-8f, 2f));
            Passer(m, "worker", V(30f, -4f), V(60f, 0f));

            // 28. The witness waits for the evening train.
            m = New(list, "rail", new Vector3(10f, 32f, -140f), new Vector3(0f, 1.6f, 12f), 9, 18000, 26f, "headshots");
            m.Wind = new Vector3(-3f, 0f, 0f);
            m.Actors.Add(Man("ally", "casual", Role.Ally, "name.witness", P(0f, 12f)));
            foreach (float from in new[] { -70f, 72f, -40f })
            {
                ActorDef hunter = Target(m, from < -50f ? "punk" : "hoodie", "name.killer", 600, P(from, 13f), P(from * 0.5f, 12.5f), P(Mathf.Sign(from) * 2f, 12.2f));
                hunter.Wait = 0.4f;
                hunter.Assassin = true;
            }

            // 29. Six guards down both platforms.
            m = New(list, "rail", new Vector3(-30f, 36f, -150f), new Vector3(0f, 1.6f, 20f), 12, 19500, 50f, "clean");
            foreach (float x in new[] { -50f, -10f, 36f })
            {
                ActorDef near = Target(m, "swat", "name.sentry", 400, P(x, 11f), P(x + 12f, 13f));
                near.Idle = "Idle_Gun";
                near.Wait = 4f;
                ActorDef far = Target(m, "swat", "name.sentry", 400, P(x + 20f, 30f), P(x + 8f, 31f));
                far.Idle = "Idle_Gun";
                far.Wait = 5f;
            }

            // 30. The minister's train leaves in a minute.
            m = New(list, "rail", new Vector3(40f, 36f, -150f), new Vector3(10f, 1.6f, 30f), 10, 21000, 40f, "headshots");
            m.Seconds = 75f;
            m.Wind = new Vector3(3.5f, 0f, 0f);
            ActorDef minister = Target(m, "suit", "name.minister", 3500, P(30f, 30f), P(6f, 31f));
            minister.Wait = 6f;
            minister.Health = 220f;
            minister.Escape = P(-12f, 33f);
            minister.LeavesAt = 40f;
            Guard(m, P(34f, 29f), P(10f, 30f));
            Guard(m, P(26f, 32f), P(2f, 32.5f));
            Guard(m, P(-20f, 12f)).YawDeg = 180f;
            Guard(m, P(50f, 12f)).YawDeg = 180f;
        }

        static void AddSnow(List<MissionDef> list)
        {
            // 31. The contact at the well.
            MissionDef m = New(list, "snow", new Vector3(-10f, 30f, -140f), new Vector3(0f, 1.4f, 10f), 8, 22000, 25f, "headshots");
            m.Wind = new Vector3(4f, 0f, 0f);
            ActorDef contact = Target(m, "farmer", "name.contact", 1500, V(3f, 10f));
            contact.YawDeg = 200f;
            contact.Escape = V(-32f, 24f);
            contact.LeavesAt = 45f;
            Guard(m, V(-20f, 6f), V(16f, 4f));

            // 32. A patrol between the houses.
            m = New(list, "snow", new Vector3(40f, 32f, -150f), new Vector3(0f, 1.4f, 16f), 9, 23500, 35f, "clean");
            m.Wind = new Vector3(-4.5f, 0f, 0f);
            foreach (Vector3 post in new[] { V(-40f, 14f), V(-6f, 28f), V(28f, 24f), V(44f, 8f) })
            {
                ActorDef man = Target(m, "swat", "name.sentry", 500, post, post + new Vector3(8f, 0f, -4f));
                man.Idle = "Idle_Gun";
                man.Wait = 4f;
            }

            // 33. The drums behind the pickup.
            m = New(list, "snow", new Vector3(-40f, 30f, -145f), new Vector3(-10f, 1.4f, 22f), 6, 25000, 20f, "clean");
            m.Wind = new Vector3(3f, 0f, 0f);
            Target(m, "punk", "name.thug", 600, V(-12f, 22f)).YawDeg = 90f;
            Target(m, "hoodie", "name.thug", 600, V(-7f, 23f)).YawDeg = -90f;
            ActorDef boss = Target(m, "suit", "name.smuggler", 1800, V(-10f, 20.5f));
            boss.Escape = V(-36f, 24f);
            Guard(m, V(20f, 8f)).YawDeg = 180f;

            // 34. The prisoner in the square.
            m = New(list, "snow", new Vector3(10f, 32f, -140f), new Vector3(0f, 1.4f, 4f), 9, 26500, 26f, "headshots");
            m.Wind = new Vector3(-5f, 0f, 0f);
            m.Actors.Add(Man("ally", "casual", Role.Ally, "name.prisoner", V(0f, 2f)));
            foreach (Vector3 from in new[] { V(-34f, 24f), V(36f, 22f), V(4f, 34f) })
            {
                ActorDef jailer = Target(m, "swat", "name.jailer", 700, from, V(from.x * 0.5f, 12f), V(Mathf.Sign(from.x) * 1.6f, 3f));
                jailer.Idle = "Idle_Gun";
                jailer.Wait = 0.5f;
                jailer.Assassin = true;
            }

            // 35. The commander leaves his headquarters.
            m = New(list, "snow", new Vector3(-30f, 36f, -150f), new Vector3(10f, 1.4f, 24f), 10, 28000, 40f, "headshots");
            m.Seconds = 75f;
            m.Wind = new Vector3(5f, 0f, 0f);
            ActorDef commander = Target(m, "suit", "name.commander", 4000, V(16f, 32f), V(4f, 18f));
            commander.Wait = 5f;
            commander.Health = 220f;
            commander.Escape = V(22f, -4f);
            commander.LeavesAt = 38f;
            Guard(m, V(20f, 30f), V(8f, 16f));
            Guard(m, V(12f, 30f), V(0f, 16f));
            Guard(m, V(26f, -2f)).YawDeg = 180f;
            Guard(m, V(-30f, 10f)).YawDeg = 180f;

            // 36. The last contract.
            m = New(list, "snow", new Vector3(0f, 40f, -150f), new Vector3(0f, 1.4f, 16f), 16, 32000, 70f, "headshots");
            m.Wind = new Vector3(-4f, 0f, 0f);
            ActorDef last = Target(m, "suit", "name.warlord", 6000, V(0f, 34f), V(10f, 16f), V(-10f, 16f));
            last.Wait = 4f;
            last.Health = 220f;
            last.Escape = V(2f, 60f);
            foreach (Vector3 post in new[] { V(-44f, 10f), V(-20f, 30f), V(24f, 30f), V(46f, 12f), V(-6f, 2f), V(14f, 4f) })
            {
                ActorDef man = Target(m, "swat", "name.sentry", 450, post, post + new Vector3(6f, 0f, 3f));
                man.Idle = "Idle_Gun";
                man.Wait = 5f;
            }
        }

        static void AddFort(List<MissionDef> list)
        {
            // 13. Two sentries on the gate.
            MissionDef m = New(list, "fort", new Vector3(0f, 34f, -128f), new Vector3(0f, 1.4f, -8f), 8, 4200, 20f, "headshots");
            Target(m, "swat", "name.sentry", 300, V(-3.5f, -15f)).Idle = "Idle_Gun";
            Target(m, "swat", "name.sentry", 300, V(3.5f, -15f)).Idle = "Idle_Gun";
            Guard(m, V(-10f, 10f), V(10f, 14f));

            // 14. The chemist comes out for air.
            m = New(list, "fort", new Vector3(-40f, 38f, -128f), new Vector3(-10f, 1.4f, 20f), 8, 4600, 30f, "silent");
            ActorDef chemist = Target(m, "worker", "name.chemist", 900, V(-20f, 22f), V(-8f, 14f));
            chemist.Wait = 6f;
            chemist.Escape = V(-26f, 36f);
            Guard(m, V(12f, 6f), V(20f, 28f));
            Guard(m, V(-30f, 4f)).YawDeg = 180f;
            Guard(m, V(30f, 36f)).YawDeg = 200f;

            // 15. Five men on the wall walk, a long way off.
            m = New(list, "fort", new Vector3(50f, 40f, -148f), new Vector3(0f, 1.4f, 10f), 10, 5000, 45f, "clean");
            m.Wind = new Vector3(-3f, 0f, 0f);
            foreach (Vector3 post in new[] { V(-30f, -4f), V(-12f, 24f), V(8f, 6f), V(24f, 30f), V(32f, -4f) })
            {
                ActorDef sentry = Target(m, "swat", "name.sentry", 250, post, post + new Vector3(6f, 0f, 4f));
                sentry.Idle = "Idle_Gun";
                sentry.Wait = 4f;
            }

            // 16. The prisoner is walked across the yard. He must reach the gate; the men with him must not.
            m = New(list, "fort", new Vector3(-20f, 36f, -133f), new Vector3(0f, 1.4f, 10f), 9, 5600, 30f, "headshots");
            ActorDef prisoner = Man("ally", "farmer", Role.Ally, "name.prisoner", V(0f, 14f));
            m.Actors.Add(prisoner);
            foreach (Vector3 from in new[] { V(-28f, 30f), V(26f, 28f), V(-4f, 36f) })
            {
                ActorDef jailer = Target(m, "swat", "name.jailer", 450, from, V(from.x * 0.5f, 22f), V(Mathf.Sign(from.x) * 1.5f, 15f));
                jailer.Idle = "Idle_Gun";
                jailer.Wait = 0.6f;
                jailer.Assassin = true;
            }
            Guard(m, V(30f, 0f)).YawDeg = 180f;

            // 17. The convoy is leaving. The colonel must not be in it.
            m = New(list, "fort", new Vector3(30f, 38f, -138f), new Vector3(0f, 1.4f, 10f), 10, 6200, 35f, "headshots");
            m.Seconds = 70f;
            m.Wind = new Vector3(4f, 0f, 0f);
            ActorDef colonel = Target(m, "suit", "name.colonel", 1800, V(2f, 36f), V(-6f, 20f));
            colonel.Wait = 5f;
            colonel.Escape = V(-10f, 6f);
            colonel.LeavesAt = 30f;
            colonel.Health = 220f;
            Guard(m, V(-2f, 34f), V(-10f, 18f));
            Guard(m, V(6f, 34f), V(-2f, 18f));
            Guard(m, V(-14f, 2f)).YawDeg = 180f;
            Guard(m, V(18f, 6f)).YawDeg = 180f;

            // 18. Everyone in the yard.
            m = New(list, "fort", new Vector3(0f, 42f, -148f), new Vector3(0f, 1.4f, 14f), 14, 8000, 60f, "headshots");
            m.Wind = new Vector3(-2.5f, 0f, 0f);
            ActorDef warlord = Target(m, "suit", "name.warlord", 3000, V(0f, 34f), V(8f, 16f), V(-8f, 16f));
            warlord.Wait = 4f;
            warlord.Health = 220f;
            warlord.Escape = V(0f, 40f);
            foreach (Vector3 post in new[] { V(-24f, 6f), V(22f, 2f), V(-10f, 28f), V(16f, 30f), V(0f, -15f) })
            {
                ActorDef soldier = Target(m, "swat", "name.sentry", 300, post, post + new Vector3(5f, 0f, 3f));
                soldier.Idle = "Idle_Gun";
                soldier.Wait = 5f;
            }
        }
    }
}
