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

            // The docks: a quay of stacked containers between two sheds, a ship alongside.
            var docks = new LocationDef { Id = "docks", NameKey = "loc.docks", Ground = "concrete", Light = "day", MenuEye = new Vector3(24f, 5f, -26f), MenuLook = new Vector3(-2f, 2.5f, 10f) };
            string[] boxes = { "container_red", "container_green", "container_plain" };
            for (int row = 0; row < 3; row++)
            for (int i = 0; i < 7; i++)
            {
                if ((row * 7 + i) % 5 == 3) continue;
                float x = -24f + i * 6.4f, z = 26f + row * 3.4f;
                Put(docks, boxes[(row + i) % 3], x, z);
                if ((row + i) % 2 == 0) Put(docks, boxes[(row + i + 1) % 3], x + 0.3f, z, 0f, 0f, false, false, 2.62f);
            }
            for (int i = 0; i < 4; i++) Put(docks, boxes[i % 3], -46f + (i % 2) * 6.3f, -4f - (i / 2) * 3.2f, 4f);
            for (int i = 0; i < 3; i++) Put(docks, boxes[(i + 1) % 3], 36f, -8f + i * 3.3f, 90f);
            Put(docks, "container_red", 36.2f, -4.7f, 90f, 0f, false, false, 2.62f);
            Put(docks, "shed", -52f, 34f, 90f, 17f);
            Put(docks, "shed", 58f, 30f, -90f, 19f);
            Put(docks, "block", 70f, -12f, 180f, 20f);
            Put(docks, "ship", 4f, 72f, 0f, 30f);
            Put(docks, "truck", -8f, 6f, 25f);
            Put(docks, "truck", 14f, 12f, -160f);
            Put(docks, "pickup", 22f, -6f, 70f);
            Put(docks, "suv", -20f, -10f, -30f);
            Put(docks, "tower", 44f, 14f, 0f, 10f);
            Put(docks, "tower", -34f, 16f, 0f, 10f);
            Row(docks, "fence_metal", -64f, -26f, 3.56f, 0f, 14, 0f, 0f, false, true);
            Row(docks, "fence_metal", 18f, -26f, 3.56f, 0f, 14, 0f, 0f, false, true);
            Row(docks, "light", -40f, -22f, 20f, 0f, 5, 0f, 0f, true);
            Row(docks, "light", -30f, 20f, 22f, 0f, 4, 180f, 0f, true);
            foreach (Vector3 p in new[] { V(-4f, -2f), V(-2.6f, -1.4f), V(6f, 2f), V(26f, 6f), V(27.2f, 6.6f), V(-14f, 16f), V(-30f, 4f) })
                Put(docks, "barrel", p.x, p.z, p.x * 40f);
            foreach (Vector3 p in new[] { V(2f, 4f), V(3.2f, 4.2f), V(2.5f, 5.3f), V(-12f, -6f), V(18f, 18f), V(19.1f, 18.3f), V(-26f, 12f) })
                Put(docks, "crate", p.x, p.z, p.z * 25f, 1.1f);
            Put(docks, "crate", 2.6f, 4.4f, 20f, 1.1f, false, false, 1.1f);
            foreach (Vector3 p in new[] { V(8f, -4f), V(-18f, 2f), V(30f, 14f) }) Put(docks, "pallet", p.x, p.z, p.x * 17f);
            Put(docks, "dumpster", -38f, -18f, 0f, 1.6f);
            Put(docks, "dumpster", 50f, -2f, 90f, 1.6f);
            Row(docks, "barrier", -10f, -18f, 2.2f, 0f, 6, 0f, 0.9f);
            list.Add(docks);
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
            return list;
        }
    }
}
