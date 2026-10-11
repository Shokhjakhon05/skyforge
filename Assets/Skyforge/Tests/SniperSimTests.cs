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
    }
}
