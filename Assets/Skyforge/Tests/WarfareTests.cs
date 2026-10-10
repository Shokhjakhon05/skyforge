using System.Collections.Generic;
using NUnit.Framework;
using Skyforge.Core;
using UnityEngine;

namespace Skyforge.Tests
{
    /// <summary>The second half of the fight: reactions, pursuers, mounted parts, friendly guns and the newer weapons.</summary>
    public class WarfareTests
    {
        const float Dt = 1f / 60f;

        static TargetDefinition Target(string kind, float x, float z, bool friendly = false) => new TargetDefinition
        {
            Id = kind + x + "_" + z, Kind = TargetKinds.Get(kind), Position = new Vector3(x, 0f, z), Friendly = friendly,
        };

        static MissionDefinition Mission(int wanted, params TargetDefinition[] targets)
        {
            var mission = new MissionDefinition { Id = "test", TimeGoalSeconds = 100f, BasePosition = new Vector3(0f, 0f, -500f) };
            mission.Targets.AddRange(targets);
            var objective = new ObjectiveDefinition { Type = ObjectiveType.Destroy, TextKey = "objective.destroy" };
            for (int i = 0; i < wanted; i++) objective.TargetIndices.Add(i);
            mission.Objectives.Add(objective);
            return mission;
        }

        static DroneSpec Spec() => new DroneSpec { SignalRange = 5000f };

        static FlightState Hover(float x, float y, float z, float yawDeg = 0f) =>
            new FlightState { Position = new Vector3(x, y, z), YawDeg = yawDeg };

        static void Run(MissionRun run, FlightState state, float seconds, bool rocket = false, bool bomb = false)
        {
            for (float t = 0f; t < seconds && run.Status == MissionStatus.InProgress; t += Dt)
                run.Step(state, SimEvent.None, 0.5f, 0f, Dt, rocket, default, 1f, bomb);
        }

        [Test]
        public void ATruckDrivesOffFromANearMissOnlyWhenTheEnemyReacts()
        {
            foreach (bool reactive in new[] { false, true })
            {
                MissionDefinition mission = Mission(2, Target("truck", 0f, 100f), Target("truck", 30f, 100f));
                mission.Reactive = reactive;
                var hard = Spec();
                hard.RocketDamage = 100f;
                var run = new MissionRun(mission, hard);
                Vector3 before = run.Target(1).Position;
                Run(run, Hover(0f, 30f, 0f), 0.2f, true);
                Run(run, Hover(0f, 30f, 0f), 6f);
                Assert.IsFalse(run.Target(0).Alive);
                float moved = Vector3.Distance(before, run.Target(1).Position);
                if (reactive) Assert.Greater(moved, 15f);
                else Assert.AreEqual(0f, moved, 0.01f);
            }
        }

        [Test]
        public void InfantryThatHasGoneToGroundIsHarderToKill()
        {
            MissionDefinition mission = Mission(1, Target("squad", 0f, 100f), Target("truck", 30f, 100f));
            mission.Reactive = true;
            var spec = Spec();
            spec.RocketDamage = 20f;
            var run = new MissionRun(mission, spec);
            // The first rocket hurts it and sends it to cover; the second, landing in cover, does less.
            Run(run, Hover(0f, 30f, 0f), 0.1f, true);
            Run(run, Hover(0f, 30f, 0f), 2f);
            float afterFirst = run.Target(0).Health;
            Run(run, Hover(0f, 30f, 0f), 0.1f, true);
            Run(run, Hover(0f, 30f, 0f), 2f);
            float first = TargetKinds.Get("squad").Health - afterFirst, second = afterFirst - run.Target(0).Health;
            Assert.Greater(first, 0f);
            Assert.Less(second, first * 0.6f);
        }

        [Test]
        public void AHunterComesAfterTheDroneAndShootsIt()
        {
            TargetDefinition hunter = Target("hunter", 0f, 300f);
            hunter.Altitude = 40f;
            var run = new MissionRun(Mission(1, hunter), Spec());
            FlightState state = Hover(0f, 40f, 0f, 180f);
            Run(run, state, 0.5f);
            float start = Vector3.Distance(run.Target(0).Position, state.Position);
            Run(run, state, 20f);
            Assert.Less(Vector3.Distance(run.Target(0).Position, state.Position), start - 150f);
            Run(run, state, 15f);
            Assert.Less(run.Hull, 1f);
        }

        [Test]
        public void FlaresShakeOffAPursuerAndAJammerSilencesEveryGun()
        {
            foreach (Ability ability in new[] { Ability.Flares, Ability.Jammer })
            {
                TargetDefinition hunter = Target("hunter", 0f, 30f);
                hunter.Altitude = 40f;
                var spec = Spec();
                spec.Ability = ability;
                var run = new MissionRun(Mission(1, hunter, Target("aagun", 60f, 0f)), spec);
                FlightState state = Hover(0f, 40f, 0f, 180f);
                Run(run, state, 6f);
                Assert.IsTrue(run.UnderFire, ability.ToString());
                float hull = run.Hull;
                run.UseAbility();
                Run(run, state, 0.2f);
                float after = run.Hull;
                Run(run, state, 3f);
                if (ability == Ability.Jammer) Assert.AreEqual(after, run.Hull, 0.0001f, "the jammer blinds every gun");
                else Assert.Less(run.Hull, hull, "flares do nothing about an ordinary gun");
                Assert.AreEqual(spec.AbilityCharges - 1, run.AbilityCharges);
            }
        }

        [Test]
        public void AShipIsHardToHurtUntilItsTurretsAreGone()
        {
            TargetDefinition ship = Target("warship", 0f, 100f);
            var turret = new TargetDefinition { Id = "turret", Kind = TargetKinds.Get("ship_turret"), Parent = 0, Offset = new Vector3(0f, 7f, 0f) };
            var spec = Spec();
            spec.Bombs = 50;
            var shielded = new MissionRun(Mission(1, ship, turret), spec);
            var bare = new MissionRun(Mission(1, Target("warship", 0f, 100f)), spec);
            foreach (MissionRun run in new[] { shielded, bare })
            {
                Run(run, Hover(0f, 40f, 100f), 0.1f, false, true);
                Run(run, Hover(0f, 40f, 100f), 5f);
            }
            float kept = shielded.Target(0).Health, lost = TargetKinds.Get("warship").Health - bare.Target(0).Health;
            Assert.Greater(lost, 100f);
            Assert.Greater(kept, TargetKinds.Get("warship").Health - lost * 0.5f);
            // The turret rides the ship and went with the same bomb.
            Assert.IsFalse(shielded.Target(1).Alive);
        }

        [Test]
        public void FriendlyGunsKillWhatComesIntoTheirReach()
        {
            var run = new MissionRun(Mission(1, Target("jeep", 0f, 100f), Target("turret", 30f, 100f, true)), Spec());
            Run(run, Hover(0f, 30f, -400f), 20f);
            Assert.IsFalse(run.Target(0).Alive);
            Assert.AreEqual(1, run.KillsBy(Weapon.Ally));
            Assert.AreEqual(0, run.Streak, "an ally's kill is not the player's streak");
        }

        [Test]
        public void TheCannonFiresByItselfAtWhatIsLockedAndRunsDry()
        {
            var spec = Spec();
            spec.Rockets = 0;
            spec.Cannon = 60f;
            spec.CannonSeconds = 3f;
            var run = new MissionRun(Mission(2, Target("truck", 0f, 60f), Target("tank_heavy", 0f, 80f)), spec);
            Run(run, Hover(0f, 20f, 0f), 2f);
            Assert.IsFalse(run.Target(0).Alive);
            Assert.AreEqual(1, run.KillsBy(Weapon.Cannon));
            Run(run, Hover(0f, 20f, 0f), 5f);
            Assert.AreEqual(0f, run.CannonSeconds, 0.001f);
            Assert.IsTrue(run.Target(1).Alive);
        }

        [Test]
        public void AClusterBombReachesWhatAPlainBombDoesNot()
        {
            foreach (bool cluster in new[] { false, true })
            {
                var spec = Spec();
                spec.Cluster = cluster;
                var run = new MissionRun(Mission(2, Target("truck", 0f, 100f), Target("truck", 26f, 100f)), spec);
                Run(run, Hover(0f, 40f, 100f), 0.1f, false, true);
                Run(run, Hover(0f, 40f, 100f), 6f);
                Assert.IsFalse(run.Target(0).Alive);
                float full = TargetKinds.Get("truck").Health;
                if (cluster) Assert.Less(run.Target(1).Health, full, "bomblets reach the second truck");
                else Assert.AreEqual(full, run.Target(1).Health, "a plain bomb does not");
            }
        }

        [Test]
        public void AGuidedMissileGoesWhereItIsSteered()
        {
            var spec = Spec();
            spec.Ability = Ability.GuidedMissile;
            // The target is off to the right: left alone the missile flies past it, steered it does not.
            foreach (bool steered in new[] { false, true })
            {
                var run = new MissionRun(Mission(1, Target("bunker", 120f, 200f)), spec);
                FlightState state = Hover(0f, 40f, 0f);
                run.UseAbility();
                Run(run, state, 0.1f);
                Assert.IsTrue(run.GuidedActive);
                for (float t = 0f; t < 16f && run.GuidedActive; t += Dt)
                {
                    if (steered)
                    {
                        Vector3 to = run.Target(0).Position + Vector3.up - run.GuidedPosition;
                        Vector3 dir = run.GuidedDirection;
                        float turn = Mathf.DeltaAngle(Mathf.Atan2(dir.x, dir.z) * Mathf.Rad2Deg, Mathf.Atan2(to.x, to.z) * Mathf.Rad2Deg);
                        float pitch = Mathf.Asin(Mathf.Clamp(to.normalized.y, -1f, 1f)) - Mathf.Asin(Mathf.Clamp(dir.y, -1f, 1f));
                        run.Guide(new Vector2(Mathf.Clamp(turn / 10f, -1f, 1f), Mathf.Clamp(pitch * 6f, -1f, 1f)));
                    }
                    run.Step(state, SimEvent.None, 0.5f, 0f, Dt);
                }
                Assert.IsFalse(run.GuidedActive);
                Assert.AreEqual(!steered, run.Target(0).Alive);
                if (steered) Assert.AreEqual(1, run.KillsBy(Weapon.Guided));
            }
        }

        [Test]
        public void AStreakOfKillsEarnsSupportAndCallingItSpendsTheStreak()
        {
            var targets = new List<TargetDefinition>();
            for (int i = 0; i < 3; i++) targets.Add(Target("truck", (i - 1) * 6f, 100f));
            // The second group is behind the drone, out of the way until it turns round.
            targets.Add(Target("tank_heavy", 0f, -400f));
            targets.Add(Target("truck", 8f, -400f));
            var spec = Spec();
            spec.Rockets = 40;
            spec.RocketDamage = 100f;
            var run = new MissionRun(Mission(5, targets.ToArray()), spec);
            Assert.AreEqual(0, run.SupportTier);
            FlightState state = Hover(0f, 30f, 0f);
            for (int i = 0; i < 200 && run.Kills < 3; i++) Run(run, state, 0.2f, true);
            Assert.AreEqual(3, run.Kills);
            Assert.AreEqual(1, run.SupportTier);

            state = Hover(0f, 30f, -300f, 180f);
            Run(run, state, 0.3f);
            Assert.GreaterOrEqual(run.LockedTarget, 3);
            run.CallSupport();
            Run(run, state, 0.1f);
            Assert.AreEqual(0, run.Streak);
            Assert.Greater(run.Strikes.Count, 0);
            Run(run, state, 8f);
            Assert.IsFalse(run.Target(4).Alive, "the truck beside the tank is inside the barrage");
            Assert.Greater(run.KillsBy(Weapon.Support), 0);
        }

        [Test]
        public void ASpareAircraftTakesOverWhenTheFirstIsShotDown()
        {
            MissionDefinition mission = Mission(1, Target("aagun", 0f, 60f));
            mission.Spares = 1;
            var run = new MissionRun(mission, Spec());
            FlightState state = Hover(0f, 30f, 0f, 180f);
            Run(run, state, 60f);
            Assert.AreEqual(MissionStatus.InProgress, run.Status);
            Assert.IsTrue(run.NeedsRespawn);
            Assert.AreEqual(0, run.SparesLeft);
            run.Respawned();
            Assert.AreEqual(1f, run.Hull);
            Run(run, state, 60f);
            Assert.AreEqual(MissionStatus.Failed, run.Status);
            Assert.AreEqual(FailReason.ShotDown, run.FailReason);
        }

        [Test]
        public void ALaterWaveStaysOutOfSightAndOutOfReachUntilItsObjective()
        {
            var mission = new MissionDefinition { Id = "waves", TimeGoalSeconds = 100f, BasePosition = new Vector3(0f, 0f, -500f) };
            mission.Targets.Add(Target("truck", 0f, 100f));
            TargetDefinition late = Target("aagun", 10f, 100f);
            late.StartObjective = 2;
            mission.Targets.Add(late);
            mission.Objectives.Add(new ObjectiveDefinition { Type = ObjectiveType.TakeOff });
            var first = new ObjectiveDefinition { Type = ObjectiveType.Destroy, TextKey = "objective.destroy" };
            first.TargetIndices.Add(0);
            var second = new ObjectiveDefinition { Type = ObjectiveType.Destroy, TextKey = "objective.destroy" };
            second.TargetIndices.Add(1);
            mission.Objectives.Add(first);
            mission.Objectives.Add(second);

            var hard = Spec();
            hard.RocketDamage = 100f;
            var run = new MissionRun(mission, hard);
            FlightState state = Hover(0f, 30f, 0f);
            run.Step(state, SimEvent.TookOff, 0.5f, 0f, Dt);
            Run(run, state, 3f);
            Assert.IsTrue(run.Target(1).Dormant);
            Assert.AreEqual(1f, run.Hull, "a gun that is not there yet does not shoot");
            Run(run, state, 0.2f, true);
            Run(run, state, 4f);
            Assert.IsFalse(run.Target(0).Alive);
            Assert.IsTrue(run.Target(1).Alive, "the blast did not reach the wave that had not come yet");
            Assert.AreEqual(2, run.ObjectiveIndex);
            Run(run, state, 0.5f);
            Assert.IsFalse(run.Target(1).Dormant);
            Assert.AreEqual(0f, run.Target(1).Position.y, 0.5f);
        }

        [Test]
        public void VeteranIsTheSameMissionMadeHarderUnderItsOwnName()
        {
            RegionDefinition region = RegionCatalog.All[0];
            Island island = Island.Generate(region.Seed, region.Biome);
            MissionDefinition contract = Operations.Contract(island, region.Id, 3, 5, OperationKind.Strike);
            MissionDefinition veteran = Operations.Veteran(contract);
            Assert.AreEqual("vet:" + contract.Id, veteran.Id);
            Assert.AreEqual(contract.Targets.Count, veteran.Targets.Count);
            Assert.Greater(veteran.HealthScale, 1.4f);
            Assert.Greater(veteran.BaseReward, contract.BaseReward);
            Assert.Less(veteran.TimeGoalSeconds, contract.TimeGoalSeconds + 0.01f);
            var plain = new MissionRun(contract, Spec(), island.Map);
            var hard = new MissionRun(veteran, Spec(), island.Map);
            Assert.Greater(hard.Target(0).Health, plain.Target(0).Health / contract.HealthScale * 1.4f);
        }
    }

    /// <summary>The career: rank, season, tasks, mastery, cases, the base, the armoury and the war over the map.</summary>
    public class CareerTests
    {
        static SaveData Fresh() => new SaveData();

        [Test]
        public void RanksClimbWithExperienceAndPayOnce()
        {
            Assert.AreEqual(1, Career.Rank(0));
            Assert.AreEqual(2, Career.Rank(Career.XpFor(2)));
            Assert.AreEqual(Career.Ranks, Career.Rank(int.MaxValue / 2));
            for (int rank = 2; rank <= Career.Ranks; rank++) Assert.Greater(Career.XpFor(rank), Career.XpFor(rank - 1));

            SaveData save = Fresh();
            var gain = new CareerGain();
            Career.AddXp(save, Career.XpFor(4), gain);
            Assert.AreEqual(4, gain.RankAfter);
            Assert.AreEqual(150 * (2 + 3 + 4), gain.Rewards.Credits);
            var again = new CareerGain();
            Career.AddXp(save, 1, again);
            Assert.IsTrue(again.Rewards.Empty);
            foreach (string language in Loc.Languages)
            {
                Loc.SetLanguage(language);
                for (int rank = 1; rank <= Career.Ranks; rank++) Assert.IsFalse(Career.RankName(rank).StartsWith("rank."), language + " " + rank);
            }
            Loc.SetLanguage("en");
        }

        [Test]
        public void PrestigeHandsTheRankBackForAStar()
        {
            SaveData save = Fresh();
            Assert.IsFalse(Career.CanPrestige(save));
            Career.AddXp(save, Career.XpFor(Career.Ranks), new CareerGain());
            Assert.IsTrue(Career.CanPrestige(save));
            int level = Career.ContractLevel(save);
            Reward reward = Career.TakePrestige(save);
            Assert.AreEqual("prestige1", reward.Emblem);
            Assert.AreEqual(1, Career.Rank(save.Career.Xp));
            Assert.AreEqual(1, save.Career.Prestige);
            Assert.Contains("prestige1", save.Career.Emblems);
            Assert.Less(Career.ContractLevel(save), level);
            Assert.Greater(Career.ContractLevel(save), 1);
        }

        [Test]
        public void AFlightIsBookedIntoRankSeasonTasksAndMastery()
        {
            SaveData save = Fresh();
            const string today = "2026-10-10";
            var report = new MissionReport { Mode = "contract", Drone = "sparrow", Region = "arvelia", Succeeded = true, Stars = 3, Kills = 30, Rockets = 20, Bombs = 10, Armour = 5, Level = 4 };
            CareerGain gain = Career.Apply(save, report, today);
            Assert.AreEqual(Career.XpOf(report), gain.Xp);
            Assert.AreEqual(gain.Xp, save.Career.Xp);
            Assert.AreEqual(gain.Xp, save.Career.SeasonXp);
            Assert.AreEqual("2026-10", save.Career.Season);
            Assert.AreEqual(Tasks.PerDay, save.Career.Daily.Count);
            Assert.AreEqual(Tasks.PerWeek, save.Career.Weekly.Count);
            Assert.AreEqual(1, save.Career.ContractsDone);
            Assert.AreEqual(30, Mastery.Kills(save, "sparrow"));
            Assert.AreEqual(1, Mastery.Level(30));
            Assert.Greater(save.Credits, 0, "the first mastery level pays");
            // The firing range counts for nothing.
            int xp = save.Career.Xp;
            Career.Apply(save, new MissionReport { Mode = "range", Drone = "sparrow", Succeeded = true, Kills = 9 }, today);
            Assert.AreEqual(xp, save.Career.Xp);
            Assert.AreEqual(30, Mastery.Kills(save, "sparrow"));
        }

        [Test]
        public void TasksAreTheSameForADayDifferAcrossDaysAndPayOnce()
        {
            for (int day = 1; day <= 28; day++)
            {
                string key = "2026-10-" + day.ToString("00");
                List<TaskDefinition> tasks = Tasks.For(key, false), again = Tasks.For(key, false);
                Assert.AreEqual(Tasks.PerDay, tasks.Count);
                var stats = new HashSet<string>();
                for (int i = 0; i < tasks.Count; i++)
                {
                    Assert.AreEqual(tasks[i].Id, again[i].Id);
                    Assert.IsTrue(stats.Add(tasks[i].Stat), key + " repeats " + tasks[i].Stat);
                    foreach (string language in Loc.Languages)
                    {
                        Loc.SetLanguage(language);
                        Assert.IsFalse(tasks[i].Text.StartsWith("task."), tasks[i].Stat + " in " + language);
                    }
                }
            }
            Loc.SetLanguage("en");
            Assert.AreEqual(Tasks.PerWeek, Tasks.For(Tasks.Week("2026-10-10"), true).Count);
            Assert.AreNotEqual(Tasks.Week("2026-10-10"), Tasks.Week("2026-10-20"));

            SaveData save = Fresh();
            Tasks.Refresh(save, "2026-10-10");
            TaskDefinition task = Tasks.For("2026-10-10", false)[0];
            Assert.IsNull(Tasks.Claim(save, task, new CareerGain()));
            var report = new MissionReport { Succeeded = true, Stars = 3, Kills = 99, Rockets = 99, Bombs = 99, Armour = 99, Waves = 99, Mode = "contract" };
            Assert.Greater(Tasks.Count(save, report).Count, 0);
            int credits = save.Credits;
            Assert.IsNotNull(Tasks.Claim(save, task, new CareerGain()));
            Assert.AreEqual(credits + task.Reward.Credits, save.Credits);
            Assert.IsNull(Tasks.Claim(save, task, new CareerGain()));
            // The next day brings new tasks.
            Tasks.Refresh(save, "2026-10-11");
            Assert.IsNull(Tasks.State(save, task));
        }

        [Test]
        public void ASeasonLastsAMonthAndPaysEveryTierOnce()
        {
            SaveData save = Fresh();
            Seasons.Roll(save, "2026-10-10");
            save.Career.SeasonXp = Seasons.XpPerTier * 3 + 10;
            Assert.AreEqual(3, Seasons.Tier(save));
            Assert.AreEqual(3, Seasons.Unclaimed(save));
            Assert.IsNotNull(Seasons.Claim(save));
            Assert.IsNull(Seasons.Claim(save));
            Assert.AreEqual(22, Seasons.DaysLeft("2026-10-10"));

            // The last tier carries the season's skin, and a new month starts from nothing.
            save.Career.SeasonXp = Seasons.XpPerTier * Seasons.Tiers;
            Seasons.Roll(save, "2026-11-01");
            Assert.Contains(Seasons.Skin("2026-10"), save.Career.Skins, "what was earned and not collected is paid when the season ends");
            Assert.AreEqual(0, save.Career.SeasonXp);
            Assert.AreEqual("2026-11", save.Career.Season);
            for (int month = 1; month <= 12; month++) Assert.IsNotNull(Skins.Get(Seasons.Skin("2026-" + month.ToString("00"))));
        }

        [Test]
        public void CaseOddsAddUpAndCasesPayWhatTheySay()
        {
            for (int tier = 0; tier < CaseRules.Tiers; tier++)
            {
                int sum = 0;
                foreach (int chance in CaseRules.Odds[tier]) sum += chance;
                Assert.AreEqual(100, sum, "tier " + tier);
            }
            SaveData save = Fresh();
            Assert.IsNull(CaseRules.Open(save, 0, new System.Random(1)));
            Assert.IsFalse(CaseRules.Buy(save, 0));
            save.Credits = 10000000;
            save.Parts = 100000;
            var random = new System.Random(7);
            var seen = new HashSet<int>();
            for (int i = 0; i < 400; i++)
            {
                int tier = i % CaseRules.Tiers;
                Assert.IsTrue(CaseRules.Buy(save, tier));
                int credits = save.Credits, parts = save.Parts, skins = save.Career.Skins.Count;
                CaseDrop drop = CaseRules.Open(save, tier, random);
                Assert.IsNotNull(drop);
                Assert.AreEqual(credits + drop.Credits, save.Credits);
                Assert.AreEqual(parts + drop.Parts, save.Parts);
                if (drop.Skin != null)
                {
                    seen.Add(drop.Rarity);
                    Assert.IsFalse(Skins.Get(drop.Skin).Special, "earned skins do not drop");
                    Assert.AreEqual(drop.Duplicate ? skins : skins + 1, save.Career.Skins.Count);
                    Assert.AreEqual(drop.Duplicate, drop.Credits > 0);
                }
                Assert.IsTrue(drop.Credits > 0 || drop.Parts > 0 || drop.Skin != null);
            }
            Assert.AreEqual(4, seen.Count, "every rarity turned up in 400 cases");
            Assert.AreEqual(0, save.Career.Cases[0] + save.Career.Cases[1] + save.Career.Cases[2]);
        }

        [Test]
        public void SkinsAreWornOnlyIfOwnedAndEveryOneHasAName()
        {
            SaveData save = Fresh();
            Assert.IsFalse(Skins.Wear(save, "sparrow", "gold"));
            save.Career.Skins.Add("gold");
            Assert.IsTrue(Skins.Wear(save, "sparrow", "gold"));
            Assert.AreEqual("gold", Skins.Worn(save, "sparrow").Id);
            Assert.IsNull(Skins.Worn(save, "kestrel"));
            Assert.IsTrue(Skins.Wear(save, "sparrow", null));
            Assert.IsNull(Skins.Worn(save, "sparrow"));
            foreach (string language in Loc.Languages)
            {
                Loc.SetLanguage(language);
                foreach (SkinDefinition skin in Skins.All) Assert.IsTrue(Loc.Has("skin." + skin.Id), skin.Id);
                for (int i = 0; i < 4; i++) Assert.IsTrue(Loc.Has("rarity." + i));
                for (int i = 0; i < CaseRules.Tiers; i++) Assert.IsTrue(Loc.Has("case." + i));
                foreach (string id in BaseRules.Ids) Assert.IsTrue(Loc.Has("base." + id + ".name") && Loc.Has("base." + id + ".note"), id);
                foreach (string id in ArmouryRules.Ids) Assert.IsTrue(Loc.Has("armoury." + id + ".name") && Loc.Has("armoury." + id + ".note"), id);
                for (int kind = 0; kind < Operations.KindCount; kind++)
                {
                    string key = "op." + ((OperationKind)kind).ToString().ToLowerInvariant();
                    Assert.IsTrue(Loc.Has(key + ".name") && Loc.Has(key + ".brief"), key);
                }
                for (int i = 0; i < 12; i++) Assert.IsTrue(Loc.Has("codename.a." + i) && Loc.Has("codename.b." + i));
            }
            Loc.SetLanguage("en");
        }

        [Test]
        public void TheBaseAndTheArmouryChangeWhatFlies()
        {
            SaveData save = Fresh();
            Assert.IsFalse(BaseRules.Upgrade(save, BaseRules.Hangar));
            save.Credits = 1000000;
            save.Parts = 1000;
            DroneSpec plain = ArmouryRules.Outfit(new DroneSpec(), save);
            Assert.AreEqual(0, BaseRules.Spares(save));
            for (int track = 0; track < BaseRules.Tracks; track++)
            for (int level = 0; level < BaseRules.MaxLevel; level++)
                Assert.IsTrue(BaseRules.Upgrade(save, track));
            for (int track = 0; track < ArmouryRules.Tracks; track++)
            for (int level = 0; level < BaseRules.MaxLevel; level++)
                Assert.IsTrue(ArmouryRules.Upgrade(save, track));
            Assert.IsFalse(BaseRules.Upgrade(save, BaseRules.Hangar), "five levels and no more");
            Assert.AreEqual(2, BaseRules.Spares(save));
            Assert.AreEqual(4, BaseRules.Turrets(save));

            DroneSpec fitted = ArmouryRules.Outfit(new DroneSpec(), save);
            Assert.Greater(fitted.Ordnance, plain.Ordnance);
            Assert.Greater(fitted.LockRange, plain.LockRange);
            Assert.Greater(fitted.Rockets, plain.Rockets);
            Assert.Greater(fitted.SignalRange, plain.SignalRange);
            Assert.Less(fitted.DamageScale, plain.DamageScale);
            // A one-way drone stays what it is.
            var wasp = DroneCatalog.Get("wasp").Spec;
            Assert.AreEqual(0, ArmouryRules.Outfit(wasp, save).Rockets);
        }

        [Test]
        public void HeldSectorsPayEachDayAndTheEnemyTakesSomeBack()
        {
            SaveData save = Fresh();
            Assert.IsNull(War.Refresh(save, "2026-10-01"));
            for (int i = 0; i < War.SectorsPerRegion; i++) Assert.IsTrue(War.Capture(save, "arvelia", i));
            Assert.IsFalse(War.Capture(save, "arvelia", 0));
            Assert.AreEqual(12, War.HeldIn(save, "arvelia"));
            Assert.IsNull(War.Refresh(save, "2026-10-01"), "the same day twice does nothing");

            int credits = save.Credits, lost = 0;
            for (int day = 2; day <= 20; day++)
            {
                WarNews news = War.Refresh(save, "2026-10-" + day.ToString("00"));
                if (War.HeldIn(save, "arvelia") == 0) break;
                Assert.IsNotNull(news);
                Assert.Greater(news.Income, 0);
                lost += news.Lost.Count;
            }
            Assert.Greater(save.Credits, credits);
            Assert.Greater(lost, 0, "nineteen days without a single counter-attack");
            Assert.AreEqual(12 - lost, War.HeldIn(save, "arvelia"));
            // A long absence pays for three days at most.
            SaveData away = Fresh();
            War.Refresh(away, "2026-01-01");
            War.Capture(away, "kharad", 3);
            WarNews late = War.Refresh(away, "2026-06-01");
            Assert.AreEqual(War.IncomePerSector * War.MaxDaysPaid, late.Income);
        }

        [Test]
        public void TheCareerSurvivesSavingAndAnOlderSaveGetsOne()
        {
            SaveData save = Fresh();
            save.Career.Xp = 1234;
            save.Career.Skins.Add("gold");
            save.Career.Base[BaseRules.Hangar] = 3;
            War.Capture(save, "arvelia", 4);
            var loaded = JsonUtility.FromJson<SaveData>(JsonUtility.ToJson(save));
            Assert.AreEqual(1234, loaded.Career.Xp);
            Assert.Contains("gold", loaded.Career.Skins);
            Assert.AreEqual(3, loaded.Career.Base[BaseRules.Hangar]);
            Assert.IsTrue(War.Held(loaded, "arvelia", 4));
        }
    }
}
