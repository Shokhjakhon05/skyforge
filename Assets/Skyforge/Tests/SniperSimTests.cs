using NUnit.Framework;
using Skyforge;
using Sniper;
using UnityEngine;

namespace Skyforge.Tests
{
    public class SniperSimTests
    {
        static MissionDef Mission(params ActorDef[] actors)
        {
            var mission = new MissionDef { Id = "t", Perch = new Vector3(0f, 20f, -150f), Rounds = 10, Par = 30f };
            mission.Actors.AddRange(actors);
            return mission;
        }

        static ActorDef Man(string id, Role role, Vector3 at) => new ActorDef { Id = id, Model = "swat", Role = role, Path = new[] { at } };

        /// <summary>The direction that lands a round on a point, found the way a player finds it: by correcting for the drop.</summary>
        static Vector3 AimAt(SniperSim sim, Vector3 point)
        {
            Vector3 from = sim.Mission.Perch;
            float time = (point - from).magnitude / sim.Rifle.MuzzleSpeed;
            Vector3 lead = point + Vector3.up * (0.5f * SniperSim.Gravity * time * time) - sim.Mission.Wind * (0.5f * SniperSim.WindPush * time * time);
            return (lead - from).normalized;
        }

        static void Run(SniperSim sim, float seconds)
        {
            for (float t = 0f; t < seconds && sim.Outcome == Outcome.Running; t += 0.02f) sim.Step(0.02f);
        }

        [Test]
        public void AHeadIsFatalAndABodyTakesWhatTheRifleGives()
        {
            var sim = new SniperSim(Mission(Man("a", Role.Target, new Vector3(0f, 0f, 0f)), Man("b", Role.Target, new Vector3(8f, 0f, 0f))), null, Armory.Get("m24"));
            Assert.IsTrue(sim.Fire(sim.Mission.Perch, AimAt(sim, new Vector3(0f, SniperSim.HeadHeight, 0f))));
            Run(sim, 1f);
            Assert.AreEqual(Mood.Dead, sim.Actor(0).Mood);
            Assert.IsTrue(sim.Actor(0).Headshot);
            Assert.AreEqual(1, sim.Headshots);
            Assert.AreEqual(Outcome.Running, sim.Outcome);
            Run(sim, 1.5f);
            Assert.IsTrue(sim.Fire(sim.Mission.Perch, AimAt(sim, sim.Actor(1).Position + new Vector3(0f, 1.2f, 0f))));
            Run(sim, 1f);
            Assert.AreEqual(Mood.Dead, sim.Actor(1).Mood);
            Assert.IsFalse(sim.Actor(1).Headshot);
            Assert.AreEqual(Outcome.Won, sim.Outcome);
            MissionResult result = sim.Grade();
            Assert.IsTrue(result.Won);
            Assert.IsFalse(result.SecondMet, "one of the two was not a headshot");
            Assert.Greater(result.Cash, 0);
        }

        [Test]
        public void ARoundDropsOnItsWaySoAStraightLineFallsShort()
        {
            var mission = Mission(Man("a", Role.Target, new Vector3(0f, 0f, 150f)));
            var sim = new SniperSim(mission, null, Armory.Get("m24"));
            Vector3 head = new Vector3(0f, SniperSim.HeadHeight, 150f);
            sim.Fire(mission.Perch, (head - mission.Perch).normalized);
            Run(sim, 1.5f);
            Assert.AreNotEqual(true, sim.Actor(0).Headshot, "300 m without holding over is not a headshot");
        }

        [Test]
        public void AWallStopsARoundAndOnlyAPiercingOneGoesThroughAThinOne()
        {
            var location = new LocationDef();
            location.Pieces.Add(new Piece { Model = "wall", At = new Vector3(0f, 0f, -6f), Solid = new Vector3(6f, 3f, 0.3f), Thin = true });
            MissionDef mission = Mission(Man("a", Role.Target, Vector3.zero));
            mission.Perch = new Vector3(0f, 1.4f, -150f);
            var plain = new SniperSim(mission, location, Armory.Get("awm"));
            plain.Fire(mission.Perch, AimAt(plain, new Vector3(0f, 1.2f, 0f)));
            Run(plain, 1f);
            Assert.AreEqual(Mood.Calm == plain.Actor(0).Mood || plain.Actor(0).Mood == Mood.Alarmed, true, "the wall should have stopped it");
            Assert.AreEqual(0, plain.Kills);
            var heavy = new SniperSim(mission, location, Armory.Get("m82"));
            heavy.Fire(mission.Perch, AimAt(heavy, new Vector3(0f, 1.2f, 0f)));
            Run(heavy, 1f);
            Assert.AreEqual(1, heavy.Kills);
        }

        [Test]
        public void ALoudRifleSendsTheTargetRunningAndLosingHimLosesTheMission()
        {
            ActorDef boss = Man("boss", Role.Target, new Vector3(0f, 0f, 0f));
            boss.Escape = new Vector3(20f, 0f, 0f);
            var sim = new SniperSim(Mission(boss, Man("g", Role.Guard, new Vector3(4f, 0f, 2f))), null, Armory.Get("m24"));
            sim.Fire(sim.Mission.Perch, new Vector3(0.4f, 0.3f, 1f).normalized);
            Run(sim, 12f);
            Assert.IsTrue(sim.Alarm);
            Assert.AreEqual(Outcome.Lost, sim.Outcome);
            Assert.AreEqual(LostWhy.Escaped, sim.Why);
        }

        [Test]
        public void ASuppressedRifleLeavesTheSquareCalmWhenNobodySeesTheFall()
        {
            var sim = new SniperSim(Mission(Man("a", Role.Target, Vector3.zero), Man("far", Role.Target, new Vector3(60f, 0f, 30f))), null, Armory.Get("vss"));
            sim.Fire(sim.Mission.Perch, AimAt(sim, new Vector3(0f, SniperSim.HeadHeight, 0f)));
            Run(sim, 4f);
            Assert.AreEqual(Mood.Dead, sim.Actor(0).Mood);
            Assert.AreEqual(Mood.Calm, sim.Actor(1).Mood);
            Assert.IsFalse(sim.Alarm);
        }

        [Test]
        public void ACivilianHitEndsItAndAGuardLeftAliveShootsBack()
        {
            var sim = new SniperSim(Mission(Man("t", Role.Target, new Vector3(30f, 0f, 0f)), Man("c", Role.Civilian, Vector3.zero)), null, Armory.Get("m24"));
            sim.Fire(sim.Mission.Perch, AimAt(sim, new Vector3(0f, 1.2f, 0f)));
            Run(sim, 1f);
            Assert.AreEqual(LostWhy.Civilian, sim.Why);

            var fight = new SniperSim(Mission(Man("t", Role.Target, new Vector3(30f, 0f, 0f)), Man("g", Role.Guard, new Vector3(0f, 0f, 0f))), null, Armory.Get("m24"), 3);
            fight.Fire(fight.Mission.Perch, new Vector3(-0.5f, 0.3f, 1f).normalized);
            Run(fight, 90f);
            Assert.AreEqual(Outcome.Lost, fight.Outcome);
            Assert.AreEqual(LostWhy.PlayerDown, fight.Why);
        }

        [Test]
        public void TheMagazineRunsDryReloadsAndTheLastRoundSpentLoses()
        {
            MissionDef mission = Mission(Man("t", Role.Target, new Vector3(0f, 0f, 0f)));
            mission.Rounds = 6;
            var sim = new SniperSim(mission, null, Armory.Get("vss"));
            Assert.AreEqual(6, sim.InMagazine);
            for (int i = 0; i < 6; i++)
            {
                Assert.IsTrue(sim.Fire(mission.Perch, Vector3.up), "round " + i);
                Run(sim, 1.2f);
            }
            Run(sim, 5f);
            Assert.AreEqual(Outcome.Lost, sim.Outcome);
            Assert.AreEqual(LostWhy.OutOfRounds, sim.Why);
        }

        [Test]
        public void EveryRifleCostsMoreThanTheLastAndUpgradesAddUp()
        {
            for (int i = 1; i < Armory.All.Count; i++)
            {
                Assert.Greater(Armory.All[i].Price, Armory.All[i - 1].Price);
                Assert.GreaterOrEqual(Armory.All[i].MissionsRequired, Armory.All[i - 1].MissionsRequired);
            }
            RifleDef b = Armory.Get("m24"), fitted = Armory.Fitted(b, 5, 5, 5, 5, 5);
            Assert.Greater(fitted.Zoom, b.Zoom);
            Assert.Greater(fitted.Damage, b.Damage);
            Assert.Less(fitted.BoltSeconds, b.BoltSeconds);
            Assert.AreEqual(0, Armory.UpgradePrice(b, Armory.MaxLevel));
            Assert.Greater(Armory.UpgradePrice(b, 3), Armory.UpgradePrice(b, 0));
        }

        [Test]
        public void EveryContractHasItsTextsItsTargetsAndCanBeWonByAGoodShot()
        {
            foreach (string language in Loc.Languages)
            {
                Loc.SetLanguage(language);
                foreach (MissionDef mission in Campaign.Missions)
                {
                    Assert.AreNotEqual(mission.TitleKey, Loc.T(mission.TitleKey), language);
                    Assert.AreNotEqual(mission.BriefKey, Loc.T(mission.BriefKey), language);
                    foreach (ActorDef actor in mission.Actors) Assert.AreNotEqual(actor.NameKey, Loc.T(actor.NameKey), language + " " + actor.Id);
                }
            }
            Loc.SetLanguage("en");
            int order = 0;
            foreach (MissionDef mission in Campaign.Missions)
            {
                Assert.AreEqual(++order, mission.Order, mission.Id);
                // A marksman who never misses a head: every target, nearest danger first, as fast as the bolt allows.
                var sim = new SniperSim(mission, Campaign.Location(mission.LocationId), Armory.Get("m24"));
                var before = new Vector3[sim.ActorCount];
                for (int i = 0; i < sim.ActorCount; i++) before[i] = sim.Actor(i).Position;
                for (float t = 0f; t < 120f && sim.Outcome == Outcome.Running; t += 0.02f)
                {
                    if (sim.CanFire && t > 0.1f)
                    {
                        int pick = -1;
                        for (int i = 0; i < sim.ActorCount; i++)
                        {
                            Role role = sim.Def(i).Role;
                            if (sim.Actor(i).Mood == Mood.Dead || sim.Actor(i).Mood == Mood.Gone) continue;
                            if (role != Role.Target && !(role == Role.Guard && sim.Actor(i).Mood == Mood.Alarmed)) continue;
                            if (pick < 0 || role == Role.Target && sim.Def(pick).Role != Role.Target) pick = i;
                        }
                        if (pick >= 0)
                        {
                            // Where he will be when the round gets there, not where he is.
                            ActorState a = sim.Actor(pick);
                            Vector3 velocity = (a.Position - before[pick]) / 0.02f;
                            Vector3 head = a.Position + new Vector3(0f, SniperSim.HeadHeight, 0f);
                            float flight = (head - mission.Perch).magnitude / sim.Rifle.MuzzleSpeed;
                            sim.Fire(mission.Perch, AimAt(sim, head + velocity * flight));
                        }
                    }
                    for (int i = 0; i < sim.ActorCount; i++) before[i] = sim.Actor(i).Position;
                    sim.Step(0.02f);
                }
                Assert.AreEqual(Outcome.Won, sim.Outcome, mission.Id + " lost: " + sim.Why + " after " + sim.Elapsed + " s, shots " + sim.Shots + ", kills " + sim.Kills);
            }
        }

        [Test]
        public void ADrumOfFuelTakesTheMenBesideItAndWakesTheRest()
        {
            MissionDef mission = Mission(Man("a", Role.Target, new Vector3(2f, 0f, 0f)), Man("b", Role.Target, new Vector3(-3f, 0f, 1f)), Man("far", Role.Guard, new Vector3(40f, 0f, 0f)));
            mission.Perch = new Vector3(0f, 12f, -150f);
            var sim = new SniperSim(mission, null, Armory.Get("vss"));
            sim.AddCover(new Cover { Centre = new Vector3(0f, 0.57f, 0f), Size = new Vector3(0.7f, 1.14f, 0.7f), Explosive = true, Tag = 3 });
            sim.Fire(mission.Perch, AimAt(sim, new Vector3(0f, 0.6f, 0f)));
            bool blast = false;
            for (float t = 0f; t < 3f; t += 0.02f)
            {
                sim.Events.Clear();
                sim.Step(0.02f);
                foreach (SimEvent e in sim.Events)
                    if (e.Type == SimEventType.Explosion && e.Actor == 3) blast = true;
            }
            Assert.IsTrue(blast);
            Assert.AreEqual(2, sim.Kills);
            Assert.AreEqual(Outcome.Won, sim.Outcome);
            Assert.IsTrue(sim.Alarm, "even a quiet rifle cannot make a drum of fuel quiet");
        }

        [Test]
        public void AHuntCanBeMadeInEveryPlaceAndGrowsWithItsLevel()
        {
            foreach (LocationDef place in Campaign.Locations)
            {
                MissionDef small = Hunt.Make(place.Id, 1, 5), large = Hunt.Make(place.Id, 12, 5);
                Assert.IsNotNull(small, place.Id);
                int few = 0, many = 0;
                foreach (ActorDef actor in small.Actors) if (actor.Role == Role.Target) few++;
                foreach (ActorDef actor in large.Actors) if (actor.Role == Role.Target) many++;
                Assert.GreaterOrEqual(few, 3, place.Id);
                Assert.Greater(many, few, place.Id);
                Assert.Greater(large.Reward, small.Reward);
                Assert.GreaterOrEqual(small.Rounds, few + 2);
                var sim = new SniperSim(large, place, Armory.Get("m24"));
                for (float t = 0f; t < 5f; t += 0.02f) sim.Step(0.02f);
                Assert.AreEqual(Outcome.Running, sim.Outcome, place.Id + " ended by itself: " + sim.Why);
            }
            var ids = new System.Collections.Generic.HashSet<string>();
            foreach (Feat feat in Feats.All) Assert.IsTrue(ids.Add(feat.Id), feat.Id);
        }

        /// <summary>A shooter who leads a moving mark and never misses a head, on any mission.</summary>
        static SniperSim Marksman(MissionDef mission, string rifle = "m24")
        {
            var sim = new SniperSim(mission, Campaign.Location(mission.LocationId), Armory.Get(rifle));
            var before = new Vector3[sim.ActorCount];
            for (int i = 0; i < sim.ActorCount; i++) before[i] = sim.HeadPoint(i);
            for (float t = 0f; t < 200f && sim.Outcome == Outcome.Running; t += 0.02f)
            {
                if (sim.CanFire && t > 0.1f)
                {
                    // The one nearest to doing harm first: among wolves, the one closest to the end of its path.
                    int pick = -1;
                    float least = float.MaxValue;
                    for (int i = 0; i < sim.ActorCount; i++)
                    {
                        if (sim.Def(i).Role != Role.Target || sim.Actor(i).Mood == Mood.Dead || sim.Actor(i).Mood == Mood.Gone) continue;
                        Vector3[] path = sim.Def(i).Path;
                        float left = sim.Def(i).Assassin ? (path[path.Length - 1] - sim.Actor(i).Position).magnitude : i;
                        if (left < least) { least = left; pick = i; }
                    }
                    if (pick >= 0)
                    {
                        Vector3 head = sim.HeadPoint(pick), velocity = (head - before[pick]) / 0.02f;
                        float flight = (head - mission.Perch).magnitude / sim.Rifle.MuzzleSpeed;
                        sim.Fire(mission.Perch, AimAt(sim, head + velocity * flight));
                    }
                }
                for (int i = 0; i < sim.ActorCount; i++) before[i] = sim.HeadPoint(i);
                sim.Step(0.02f);
            }
            return sim;
        }

        [Test]
        public void EveryLevelOfTheRangeCanBeCleared()
        {
            for (int level = 1; level <= 16; level++)
            {
                MissionDef range = Range.Make(level, level * 3);
                int boards = 0;
                foreach (ActorDef actor in range.Actors)
                {
                    Assert.AreEqual(Kind.Board, actor.Kind);
                    if (actor.Role == Role.Target) boards++;
                    foreach (Vector3 point in actor.Path) Assert.Less(Mathf.Abs(point.x), 34f, "level " + level + " board outside the banks");
                }
                Assert.GreaterOrEqual(boards, 4);
                Assert.Greater(range.Rounds, boards);
                SniperSim sim = Marksman(range);
                Assert.AreEqual(Outcome.Won, sim.Outcome, "range " + level + " lost: " + sim.Why + " after " + sim.Elapsed + " s, shots " + sim.Shots + ", kills " + sim.Kills);
                Assert.IsFalse(sim.Alarm, "steel does not take fright");
            }
        }

        [Test]
        public void ABlueBoardEndsTheRun()
        {
            MissionDef range = Range.Make(7, 1);
            ActorDef friend = range.Actors.Find(actor => actor.Role == Role.Civilian);
            Assert.IsNotNull(friend);
            var sim = new SniperSim(range, Campaign.Location(range.LocationId), Armory.Get("m24"));
            sim.Fire(range.Perch, AimAt(sim, friend.Path[0] + new Vector3(0f, 1.2f, 0f)));
            for (float t = 0f; t < 3f; t += 0.02f) sim.Step(0.02f);
            Assert.AreEqual(Outcome.Lost, sim.Outcome);
            Assert.AreEqual(LostWhy.Civilian, sim.Why);
        }

        [Test]
        public void EveryHuntInTheReserveCanBeWon()
        {
            foreach (string rifle in new[] { "m24", "vss" })
            for (int level = 1; level <= 14; level++)
            {
                MissionDef hunt = Safari.Make(level, level * 5);
                foreach (ActorDef actor in hunt.Actors) Assert.AreEqual(Kind.Beast, actor.Kind);
                SniperSim sim = Marksman(hunt, rifle);
                Assert.AreEqual(Outcome.Won, sim.Outcome, rifle + " hunt " + level + " lost: " + sim.Why + " after " + sim.Elapsed + " s, shots " + sim.Shots + ", kills " + sim.Kills);
            }
        }

        [Test]
        public void AnAnimalIsHitWhereItStandsAndTheHerdRunsFromTheShot()
        {
            MissionDef hunt = Safari.Make(1, 2);
            var sim = new SniperSim(hunt, Campaign.Location(hunt.LocationId), Armory.Get("m24"));
            int stag = hunt.Actors.FindIndex(actor => actor.Role == Role.Target), hind = hunt.Actors.FindIndex(actor => actor.Role == Role.Civilian);
            Vector3 start = sim.Actor(hind).Position;
            // Over its back: a miss, and a loud one.
            sim.Fire(hunt.Perch, AimAt(sim, sim.Actor(stag).Position + new Vector3(0f, 4f, 0f)));
            for (float t = 0f; t < 4f; t += 0.02f) sim.Step(0.02f);
            Assert.AreEqual(0, sim.Kills);
            Assert.AreEqual(Mood.Alarmed, sim.Actor(hind).Mood);
            Assert.Greater((sim.Actor(hind).Position - start).magnitude, 5f, "the hind bolts");
            Assert.AreEqual("Gallop", sim.Actor(hind).Clip);
            // A round through the middle of the body of one that has not moved yet.
            var still = new SniperSim(Safari.Make(1, 2), Campaign.Location(hunt.LocationId), Armory.Get("m24"));
            still.Fire(hunt.Perch, AimAt(still, still.Actor(stag).Position + new Vector3(0f, hunt.Actors[stag].Shoulder - hunt.Actors[stag].Girth, 0f)));
            for (float t = 0f; t < 2f && still.Kills == 0; t += 0.02f) still.Step(0.02f);
            Assert.AreEqual(1, still.Kills);
        }

        /// <summary>
        /// Not a test of anything: when SNIPER_DUMP names a file, the places and contracts are written
        /// there as JSON, for the tool that draws them on the laptop without Unity.
        /// </summary>
        [Test]
        public void TheLayoutCanBeWrittenOutForThePreview()
        {
            string path = System.Environment.GetEnvironmentVariable("SNIPER_DUMP");
            if (string.IsNullOrEmpty(path)) return;
            var ci = System.Globalization.CultureInfo.InvariantCulture;
            string V(Vector3 v) => "[" + v.x.ToString(ci) + "," + v.y.ToString(ci) + "," + v.z.ToString(ci) + "]";
            var b = new System.Text.StringBuilder("{\"locations\":[");
            bool firstPlace = true;
            foreach (LocationDef l in Campaign.Locations)
            {
                b.Append(firstPlace ? "" : ",").Append("{\"id\":\"" + l.Id + "\",\"ground\":\"" + l.Ground + "\",\"light\":\"" + l.Light + "\",\"water\":" + l.WaterZ.ToString(ci) + ",\"menuEye\":" + V(l.MenuEye) + ",\"menuLook\":" + V(l.MenuLook) + ",\"pieces\":[");
                firstPlace = false;
                for (int i = 0; i < l.Pieces.Count; i++)
                {
                    Piece piece = l.Pieces[i];
                    b.Append(i == 0 ? "" : ",").Append("{\"m\":\"" + piece.Model + "\",\"at\":" + V(piece.At) + ",\"yaw\":" + piece.YawDeg.ToString(ci) + ",\"h\":" + piece.Height.ToString(ci) + ",\"scale\":" + piece.Scale.ToString(ci) + ",\"tint\":\"" + (piece.Tint ?? "") + "\",\"solid\":" + V(piece.Solid) + ",\"open\":" + (piece.Open ? "true" : "false") + "}");
                }
                b.Append("]}");
            }
            b.Append("],\"missions\":[");
            for (int m = 0; m < Campaign.Missions.Count; m++)
            {
                MissionDef mission = Campaign.Missions[m];
                b.Append(m == 0 ? "" : ",").Append("{\"id\":\"" + mission.Id + "\",\"place\":\"" + mission.LocationId + "\",\"perch\":" + V(mission.Perch) + ",\"look\":" + V(mission.LookAt) + ",\"actors\":[");
                for (int a = 0; a < mission.Actors.Count; a++)
                {
                    ActorDef actor = mission.Actors[a];
                    b.Append(a == 0 ? "" : ",").Append("{\"role\":\"" + actor.Role + "\",\"path\":[");
                    for (int k = 0; k < actor.Path.Length; k++) b.Append(k == 0 ? "" : ",").Append(V(actor.Path[k]));
                    b.Append("]" + (actor.Escape.HasValue ? ",\"escape\":" + V(actor.Escape.Value) : "") + "}");
                }
                b.Append("]}");
            }
            b.Append("]}");
            System.IO.File.WriteAllText(path, b.ToString());
        }
    }
}
