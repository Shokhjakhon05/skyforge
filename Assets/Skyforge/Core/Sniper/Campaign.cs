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

        public static LocationDef Location(string id)
        {
            foreach (LocationDef location in Locations)
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

        /// <summary>The same thing repeated along a line, end to end.</summary>
        static void Row(LocationDef l, string model, float x, float z, float stepX, float stepZ, int count, float yaw = 0f, float height = 0f, bool open = false, bool thin = false)
        {
            for (int i = 0; i < count; i++) Put(l, model, x + stepX * i, z + stepZ * i, yaw, height, open, thin);
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
            foreach (Vector3 p in new[] { V(-4f, -2f), V(-2.6f, -1.4f), V(6f, 2f), V(26f, 6f), V(27.2f, 6.6f), V(-14f, 16f), V(-30f, 4f), V(48f, 18f), V(49f, 19.2f), V(-58f, 14f), V(-57f, 15.4f), V(62f, -6f) })
                Put(docks, "barrel", p.x, p.z, p.x * 40f);
            foreach (Vector3 p in new[] { V(2f, 4f), V(3.2f, 4.2f), V(2.5f, 5.3f), V(-12f, -6f), V(18f, 18f), V(19.1f, 18.3f), V(-26f, 12f), V(-52f, 4f), V(-51f, 5.2f), V(60f, 10f), V(61.2f, 10.4f), V(40f, 20f) })
                Put(docks, "crate", p.x, p.z, p.z * 25f, 1.1f);
            Put(docks, "crate", 2.6f, 4.4f, 20f, 1.1f, false, false, 1.1f);
            Put(docks, "crate", 60.5f, 10.2f, 12f, 1.1f, false, false, 1.1f);
            foreach (Vector3 p in new[] { V(8f, -4f), V(-18f, 2f), V(30f, 14f), V(-40f, 18f), V(56f, 2f), V(-64f, -10f), V(66f, 16f) }) Put(docks, "pallet", p.x, p.z, p.x * 17f);
            Put(docks, "dumpster", -38f, -18f, 0f, 1.6f);
            Put(docks, "dumpster", 50f, -2f, 90f, 1.6f);
            Put(docks, "dumpster", -70f, 16f, 90f, 1.6f);
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
            foreach (Vector3 p in new[] { V(-30f, 23f), V(-29f, 24f), V(8f, 24f), V(54f, 23.4f), V(-88f, 10f) }) Put(town, "barrel", p.x, p.z, p.x * 31f);
            foreach (Vector3 p in new[] { V(-50f, 24f), V(-49f, 24.8f), V(70f, 24f), V(16f, 23.6f) }) Put(town, "crate", p.x, p.z, p.x * 13f, 1.1f);
            Row(town, "fence_wood", -108f, -34f, 5.9f, 0f, 16, 0f, 1.4f, false, true);
            Row(town, "fence_wood", 20f, -34f, 5.9f, 0f, 16, 0f, 1.4f, false, true);
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
            foreach (Vector3 p in new[] { V(-14f, 20f), V(-13f, 21.2f), V(8f, 30f), V(30f, 10f), V(31f, 11.2f), V(-32f, 30f), V(4f, 2f) }) Put(fort, "barrel", p.x, p.z, p.x * 31f);
            foreach (Vector3 p in new[] { V(-6f, 30f), V(-5f, 31f), V(16f, 24f), V(-22f, 2f), V(-21f, 3f), V(34f, 30f) }) Put(fort, "crate", p.x, p.z, p.x * 13f, 1.1f);
            Put(fort, "crate", -5.5f, 30.5f, 30f, 1.1f, false, false, 1.1f);
            Row(fort, "barrier", -4f, -16f, 2.6f, 0f, 4, 0f, 0.9f);
            Row(fort, "fence_metal", -70f, -44f, 3.56f, 0f, 12, 0f, 0f, false, true);
            Row(fort, "fence_metal", 30f, -44f, 3.56f, 0f, 12, 0f, 0f, false, true);
            list.Add(fort);
            return list;
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
            dealer.Escape = V(-8f, 8f);
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
            king.Escape = V(62f, -12f);
            king.LeavesAt = 40f;
            king.Health = 220f;
            Guard(m, V(24f, 17f), V(-2f, 17f)).Wait = 6f;
            Guard(m, V(16f, 21f), V(-10f, 21f)).Wait = 6f;
            Guard(m, V(60f, -14f)).YawDeg = 180f;
            Guard(m, V(-40f, 12f)).YawDeg = 180f;
            Passer(m, "casual", V(-70f, 19f), V(-40f, 19f));
            Passer(m, "farmer", V(80f, 19f), V(50f, 19f));
        }

        static void AddFort(List<MissionDef> list)
        {
            // 13. Two sentries on the gate.
            MissionDef m = New(list, "fort", new Vector3(0f, 34f, -170f), new Vector3(0f, 1.4f, -8f), 8, 4200, 20f, "headshots");
            Target(m, "swat", "name.sentry", 300, V(-3.5f, -15f)).Idle = "Idle_Gun";
            Target(m, "swat", "name.sentry", 300, V(3.5f, -15f)).Idle = "Idle_Gun";
            Guard(m, V(-10f, 10f), V(10f, 14f));

            // 14. The chemist comes out for air.
            m = New(list, "fort", new Vector3(-40f, 38f, -170f), new Vector3(-10f, 1.4f, 20f), 8, 4600, 30f, "silent");
            ActorDef chemist = Target(m, "worker", "name.chemist", 900, V(-20f, 22f), V(-8f, 14f));
            chemist.Wait = 6f;
            chemist.Escape = V(-26f, 36f);
            Guard(m, V(12f, 6f), V(20f, 28f));
            Guard(m, V(-30f, 4f)).YawDeg = 180f;
            Guard(m, V(30f, 36f)).YawDeg = 200f;

            // 15. Five men on the wall walk, a long way off.
            m = New(list, "fort", new Vector3(50f, 40f, -190f), new Vector3(0f, 1.4f, 10f), 10, 5000, 45f, "clean");
            m.Wind = new Vector3(-3f, 0f, 0f);
            foreach (Vector3 post in new[] { V(-30f, -4f), V(-12f, 24f), V(8f, 6f), V(24f, 30f), V(32f, -4f) })
            {
                ActorDef sentry = Target(m, "swat", "name.sentry", 250, post, post + new Vector3(6f, 0f, 4f));
                sentry.Idle = "Idle_Gun";
                sentry.Wait = 4f;
            }

            // 16. The prisoner is walked across the yard. He must reach the gate; the men with him must not.
            m = New(list, "fort", new Vector3(-20f, 36f, -175f), new Vector3(0f, 1.4f, 10f), 9, 5600, 30f, "headshots");
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
            m = New(list, "fort", new Vector3(30f, 38f, -180f), new Vector3(0f, 1.4f, 10f), 10, 6200, 35f, "headshots");
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
            m = New(list, "fort", new Vector3(0f, 42f, -190f), new Vector3(0f, 1.4f, 14f), 14, 8000, 60f, "headshots");
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
