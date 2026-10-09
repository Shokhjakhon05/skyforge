using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using Skyforge.Core;
using UnityEngine;

namespace Skyforge.Tests
{
    public class DroneCatalogTests
    {
        [Test]
        public void TwelveDronesWithUniqueIdsAndTexts()
        {
            var ids = new HashSet<string>();
            foreach (DroneDefinition drone in DroneCatalog.All)
            {
                Assert.IsTrue(ids.Add(drone.Id), drone.Id);
                Assert.AreEqual(drone.Id, drone.Spec.Id);
                Assert.IsTrue(Loc.Has(drone.NameKey), drone.NameKey);
                Assert.IsTrue(Loc.Has(drone.RoleKey), drone.RoleKey);
                Assert.IsTrue(Loc.Has(drone.NoteKey), drone.NoteKey);
            }
            Assert.AreEqual(12, ids.Count);
            Assert.AreEqual(DroneCatalog.Starter, DroneCatalog.All[0].Id);
            Assert.AreEqual(0, DroneCatalog.All[0].Price);
        }

        [Test]
        public void PricesRiseThroughTheCatalog()
        {
            for (int i = 1; i < DroneCatalog.All.Count; i++)
            {
                Assert.Greater(DroneCatalog.All[i].Price, DroneCatalog.All[i - 1].Price, DroneCatalog.All[i].Id);
                Assert.GreaterOrEqual(DroneCatalog.All[i].StarsRequired, DroneCatalog.All[i - 1].StarsRequired);
            }
        }

        [Test]
        public void EveryDroneHasSaneHandling()
        {
            foreach (DroneDefinition drone in DroneCatalog.All)
            {
                DroneSpec s = drone.Spec;
                Assert.Greater(s.MaxSpeed, 8f, drone.Id);
                Assert.Greater(s.TiltResponse, 2f, drone.Id);
                Assert.Greater(s.BatterySeconds, 200f, drone.Id);
                Assert.Greater(s.SignalRange, 700f, drone.Id);
                Assert.Less(s.MinSpeed, s.MaxSpeed * 0.5f, drone.Id);
                Assert.Contains(Mathf.RoundToInt(s.MaxZoom), new[] { 2, 4, 8 }, drone.Id);
            }
        }

        [Test]
        public void NoDroneIsBestAtEverythingExceptThePrototype()
        {
            // Every drone other than the prototype loses to the starter drone somewhere.
            DroneSpec starter = DroneCatalog.Get(DroneCatalog.Starter).Spec;
            foreach (DroneDefinition drone in DroneCatalog.All)
            {
                if (drone.Id == DroneCatalog.Starter || drone.Id == "aegis") continue;
                DroneSpec s = drone.Spec;
                bool weaker = s.MaxSpeed < starter.MaxSpeed || s.BatterySeconds < starter.BatterySeconds ||
                              s.SignalRange < starter.SignalRange || s.TiltResponse < starter.TiltResponse ||
                              s.DamageScale > starter.DamageScale || s.DetectScale > starter.DetectScale ||
                              s.OpticsRange < starter.OpticsRange || s.MinSpeed > 0f || s.MaxZoom < starter.MaxZoom;
                // Drones that are simply the starter plus one talent pay for it in price instead.
                Assert.IsTrue(weaker || drone.Price >= 2800, drone.Id + " has no drawback");
            }
        }

        [Test]
        public void WingKeepsMovingAndStealthIsNoticedSlower()
        {
            DroneSpec wing = DroneCatalog.Get("albatross").Spec;
            FlightState state = default;
            for (int i = 0; i < 120 * 8; i++)
                FlightModel.Step(ref state, default, wing, FlightMode.Assist, Vector3.zero, 1f / 120f);
            Assert.Greater(new Vector2(state.Velocity.x, state.Velocity.z).magnitude, wing.MinSpeed * 0.9f, "no input, still flying");

            var mission = new MissionDefinition { Id = "t", TimeGoalSeconds = 60f };
            mission.Objectives.Add(new ObjectiveDefinition { Type = ObjectiveType.Reach, Position = new Vector3(900f, 0f, 0f), Radius = 5f });
            mission.Zones.Add(new ZoneDefinition { Position = Vector3.zero, Radius = 100f, Floor = 20f });
            var loud = new MissionRun(mission, DroneCatalog.Get(DroneCatalog.Starter).Spec);
            var quiet = new MissionRun(mission, DroneCatalog.Get("ghost").Spec);
            var seen = new FlightState { Position = new Vector3(50f, 40f, 0f) };
            for (int i = 0; i < 10; i++)
            {
                loud.Step(seen, SimEvent.None, 0.4f, 0f, 0.1f);
                quiet.Step(seen, SimEvent.None, 0.4f, 0f, 0.1f);
            }
            Assert.Less(quiet.Detection, loud.Detection * 0.6f);
        }

        [Test]
        public void WindPushesTheMountainDroneLess()
        {
            var wind = new Vector3(8f, 0f, 0f);
            FlightState plain = default, mountain = default;
            DroneSpec sparrow = DroneCatalog.Get(DroneCatalog.Starter).Spec, ibex = DroneCatalog.Get("ibex").Spec;
            for (int i = 0; i < 120 * 6; i++)
            {
                FlightModel.Step(ref plain, default, sparrow, FlightMode.Sport, wind, 1f / 120f);
                FlightModel.Step(ref mountain, default, ibex, FlightMode.Sport, wind, 1f / 120f);
            }
            Assert.Less(mountain.Position.x, plain.Position.x * 0.6f);
        }

        [Test]
        public void FloatsLandOnWaterAndOthersCrash()
        {
            Island island = Island.Generate(7);
            Vector3 sea = island.Anchors["sea0"];
            foreach (string id in new[] { "petrel", DroneCatalog.Starter })
            {
                var sim = new FlightSim(island, DroneCatalog.Get(id).Spec);
                sim.State = new FlightState { Position = sea + Vector3.up * 6f };
                SimEvent last = SimEvent.None;
                for (int i = 0; i < 120 * 10 && last == SimEvent.None; i++)
                    last = sim.Step(new FlightInput { Throttle = -0.4f }, 1f / 120f);
                Assert.AreEqual(id == "petrel" ? SimEvent.Landed : SimEvent.Crashed, last, id);
            }
        }

        [Test]
        public void FogShortensSightUnlessThermal()
        {
            var mission = new MissionDefinition { Id = "t", WeatherId = "fog", TimeGoalSeconds = 60f };
            var objective = new ObjectiveDefinition { Type = ObjectiveType.Observe, Position = new Vector3(0f, 0f, 70f), Range = 100f, HoldSeconds = 2f };
            mission.Objectives.Add(objective);
            var plain = new MissionRun(mission, DroneCatalog.Get(DroneCatalog.Starter).Spec);
            var thermal = new MissionRun(mission, DroneCatalog.Get("owl").Spec);
            var state = new FlightState { Position = new Vector3(0f, 10f, 0f) };
            plain.Step(state, SimEvent.None, 0.4f, 0f, 0.1f);
            thermal.Step(state, SimEvent.None, 0.4f, 0f, 0.1f);
            Assert.IsFalse(plain.TargetFramed);
            Assert.IsTrue(thermal.TargetFramed);
        }
    }

    public class LoadoutTests
    {
        static readonly DroneDefinition Sparrow = DroneCatalog.Get(DroneCatalog.Starter);

        static int[] Levels(UpgradeTrack track, int level)
        {
            var levels = new int[Loadout.Tracks];
            levels[(int)track] = level;
            return levels;
        }

        [Test]
        public void NoUpgradesLeavesTheSpecAlone()
        {
            DroneSpec spec = Loadout.Apply(Sparrow, null, null);
            Assert.AreEqual(Sparrow.Spec.MaxSpeed, spec.MaxSpeed);
            Assert.AreEqual(Sparrow.Spec.BatterySeconds, spec.BatterySeconds);
            Assert.AreNotSame(Sparrow.Spec, spec, "the catalog entry must never be modified");
        }

        [Test]
        public void EveryUpgradeTrackGivesAndTakes()
        {
            DroneSpec b = Sparrow.Spec;
            DroneSpec motors = Loadout.Apply(Sparrow, Levels(UpgradeTrack.Motors, 5), null);
            Assert.Greater(motors.MaxSpeed, b.MaxSpeed);
            Assert.Less(motors.BatterySeconds, b.BatterySeconds);

            DroneSpec battery = Loadout.Apply(Sparrow, Levels(UpgradeTrack.Battery, 5), null);
            Assert.Greater(battery.BatterySeconds, b.BatterySeconds);
            Assert.Less(battery.TiltResponse, b.TiltResponse);

            DroneSpec radio = Loadout.Apply(Sparrow, Levels(UpgradeTrack.Radio, 5), null);
            Assert.Greater(radio.SignalRange, b.SignalRange);
            Assert.Greater(radio.DetectScale, b.DetectScale);

            DroneSpec optics = Loadout.Apply(Sparrow, Levels(UpgradeTrack.Optics, 5), null);
            Assert.Greater(optics.OpticsRange, b.OpticsRange);
            Assert.Less(optics.BatterySeconds, b.BatterySeconds);

            DroneSpec frame = Loadout.Apply(Sparrow, Levels(UpgradeTrack.Frame, 5), null);
            Assert.Less(frame.DamageScale, b.DamageScale);
            Assert.Less(frame.MaxSpeed, b.MaxSpeed);
            Assert.Greater(frame.DamageScale, 0f);
        }

        [Test]
        public void EveryModuleHasTextsAndChangesTheSpec()
        {
            foreach (ModuleDefinition module in Loadout.Modules)
            {
                Assert.IsTrue(Loc.Has(module.NameKey), module.NameKey);
                Assert.IsTrue(Loc.Has(module.NoteKey), module.NoteKey);
                DroneSpec with = Loadout.Apply(Sparrow, null, new[] { module.Id, "" });
                DroneSpec b = Sparrow.Spec;
                bool changed = with.Thermal != b.Thermal || with.SignalRange != b.SignalRange ||
                               with.DetectScale != b.DetectScale || with.SafeTouchdownSpeed != b.SafeTouchdownSpeed ||
                               with.ScanSpeed != b.ScanSpeed || with.BatterySeconds != b.BatterySeconds;
                Assert.IsTrue(changed, module.Id);
            }
        }

        [Test]
        public void UpgradeCostsRiseAndTopLevelsNeedParts()
        {
            int previous = 0;
            for (int level = 0; level < Loadout.MaxLevel; level++)
            {
                Loadout.UpgradeCost(Sparrow, level, out int credits, out int parts);
                Assert.Greater(credits, previous);
                Assert.AreEqual(level + 1 >= Loadout.PartsFromLevel, parts > 0, "level " + (level + 1));
                previous = credits;
            }
            Loadout.UpgradeCost(DroneCatalog.Get("aegis"), 0, out int dear, out _);
            Loadout.UpgradeCost(Sparrow, 0, out int cheap, out _);
            Assert.Greater(dear, cheap, "a dearer drone is dearer to upgrade");
        }
    }

    public class GarageTests
    {
        static SaveData Rich(int credits = 100000, int parts = 100, int stars = 33)
        {
            var save = new SaveData { Credits = credits, Parts = parts };
            for (int i = 0; i < stars / 3; i++) save.GetOrAdd("m" + i).Stars = 3;
            return save;
        }

        [Test]
        public void NewPlayerOwnsTheStarterDrone()
        {
            var save = new SaveData();
            Assert.IsTrue(save.IsValid());
            Assert.IsTrue(Garage.Owns(save, DroneCatalog.Starter));
            Assert.AreEqual(DroneCatalog.Starter, Garage.Selected(save).Id);
        }

        [Test]
        public void BuyingADroneTakesCreditsOnceAndSelectsIt()
        {
            SaveData save = Rich(1000);
            DroneDefinition kestrel = DroneCatalog.Get("kestrel");
            Assert.AreEqual(PurchaseResult.Ok, Garage.BuyDrone(save, kestrel));
            Assert.AreEqual(1000 - kestrel.Price, save.Credits);
            Assert.AreEqual("kestrel", save.SelectedDrone);
            Assert.AreEqual(PurchaseResult.AlreadyOwned, Garage.BuyDrone(save, kestrel));
            Assert.AreEqual(1000 - kestrel.Price, save.Credits);
            Assert.IsTrue(save.IsValid());
        }

        [Test]
        public void PurchasesAreRefusedWithTheReason()
        {
            Assert.AreEqual(PurchaseResult.NotEnoughCredits, Garage.BuyDrone(Rich(10), DroneCatalog.Get("kestrel")));
            Assert.AreEqual(PurchaseResult.NotEnoughStars, Garage.BuyDrone(Rich(100000, 0, 0), DroneCatalog.Get("ghost")));
            Assert.AreEqual(PurchaseResult.NotOwned, Garage.Select(Rich(), "viper"));
            Assert.AreEqual(PurchaseResult.NotOwned, Garage.Upgrade(Rich(), DroneCatalog.Get("viper"), UpgradeTrack.Motors));
            Assert.AreEqual(PurchaseResult.Unknown, Garage.BuyModule(Rich(), "warp-drive"));
        }

        [Test]
        public void UpgradesGoToFiveAndStop()
        {
            SaveData save = Rich();
            DroneDefinition sparrow = DroneCatalog.Get(DroneCatalog.Starter);
            for (int i = 0; i < Loadout.MaxLevel; i++)
                Assert.AreEqual(PurchaseResult.Ok, Garage.Upgrade(save, sparrow, UpgradeTrack.Battery));
            Assert.AreEqual(5, Garage.Level(save, sparrow.Id, UpgradeTrack.Battery));
            Assert.AreEqual(PurchaseResult.MaxLevel, Garage.Upgrade(save, sparrow, UpgradeTrack.Battery));
            Assert.Less(save.Parts, 100, "the top levels cost parts");
            Assert.Greater(Garage.SelectedSpec(save).BatterySeconds, sparrow.Spec.BatterySeconds);
            Assert.IsTrue(save.IsValid());
        }

        [Test]
        public void UpgradeWithoutPartsIsRefusedAndCostsNothing()
        {
            SaveData save = Rich(100000, 0);
            DroneDefinition sparrow = DroneCatalog.Get(DroneCatalog.Starter);
            for (int i = 0; i < Loadout.PartsFromLevel - 1; i++) Garage.Upgrade(save, sparrow, UpgradeTrack.Frame);
            int credits = save.Credits;
            Assert.AreEqual(PurchaseResult.NotEnoughParts, Garage.Upgrade(save, sparrow, UpgradeTrack.Frame));
            Assert.AreEqual(credits, save.Credits);
            Assert.AreEqual(Loadout.PartsFromLevel - 1, Garage.Level(save, sparrow.Id, UpgradeTrack.Frame));
        }

        [Test]
        public void ModulesAreBoughtOnceAndMoveBetweenDrones()
        {
            SaveData save = Rich();
            Garage.BuyDrone(save, DroneCatalog.Get("kestrel"));
            Assert.AreEqual(PurchaseResult.NotOwned, Garage.ToggleModule(save, "kestrel", "decoy"));
            Assert.AreEqual(PurchaseResult.Ok, Garage.BuyModule(save, "decoy"));
            Assert.AreEqual(PurchaseResult.AlreadyOwned, Garage.BuyModule(save, "decoy"));

            Garage.ToggleModule(save, "kestrel", "decoy");
            Assert.IsTrue(Garage.IsEquipped(save, "kestrel", "decoy"));
            Assert.Less(Garage.SelectedSpec(save).DetectScale, DroneCatalog.Get("kestrel").Spec.DetectScale);

            Garage.ToggleModule(save, DroneCatalog.Starter, "decoy");
            Assert.IsTrue(Garage.IsEquipped(save, DroneCatalog.Starter, "decoy"));
            Assert.IsFalse(Garage.IsEquipped(save, "kestrel", "decoy"), "one module cannot be on two drones");

            Garage.ToggleModule(save, DroneCatalog.Starter, "decoy");
            Assert.IsFalse(Garage.IsEquipped(save, DroneCatalog.Starter, "decoy"));
            Assert.IsTrue(save.IsValid());
        }

        [Test]
        public void ThirdModuleReplacesTheFirst()
        {
            SaveData save = Rich();
            foreach (string id in new[] { "decoy", "thermal", "scanner" })
            {
                Garage.BuyModule(save, id);
                Garage.ToggleModule(save, DroneCatalog.Starter, id);
            }
            Assert.IsFalse(Garage.IsEquipped(save, DroneCatalog.Starter, "decoy"));
            Assert.IsTrue(Garage.IsEquipped(save, DroneCatalog.Starter, "thermal"));
            Assert.IsTrue(Garage.IsEquipped(save, DroneCatalog.Starter, "scanner"));
            Assert.AreEqual(2, Garage.FittedModules(save, DroneCatalog.Starter).Count);
        }

        [Test]
        public void StarsPayPartsOnce()
        {
            var save = new SaveData();
            var mission = new MissionDefinition { Id = "m", BaseReward = 100, StarReward = 40 };
            Progress.Grant(save, mission, new MissionOutcome { Succeeded = true, Stars = 2, Time = 50f });
            Assert.AreEqual(2, save.Parts);
            Progress.Grant(save, mission, new MissionOutcome { Succeeded = true, Stars = 3, Time = 50f });
            Assert.AreEqual(3, save.Parts);
            Progress.Grant(save, mission, new MissionOutcome { Succeeded = true, Stars = 3, Time = 50f });
            Assert.AreEqual(3, save.Parts);
        }

        [Test]
        public void HangarSurvivesSavingAndVersionTwoSavesGetIt()
        {
            string directory = Path.Combine(Path.GetTempPath(), "skyforge-garage-" + System.Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            try
            {
                string path = Path.Combine(directory, "save.json");
                SaveData save = Rich();
                Garage.BuyDrone(save, DroneCatalog.Get("viper"));
                Garage.Upgrade(save, DroneCatalog.Get("viper"), UpgradeTrack.Motors);
                Garage.BuyModule(save, "powercell");
                Garage.ToggleModule(save, "viper", "powercell");
                Assert.IsTrue(new SaveStore(path).Save(save));

                SaveData loaded = new SaveStore(path).Load(out LoadResult result);
                Assert.AreEqual(LoadResult.Loaded, result);
                Assert.AreEqual("viper", loaded.SelectedDrone);
                Assert.AreEqual(1, Garage.Level(loaded, "viper", UpgradeTrack.Motors));
                Assert.IsTrue(Garage.IsEquipped(loaded, "viper", "powercell"));

                File.WriteAllText(path,
                    "{\"Version\":2,\"Credits\":500,\"Missions\":[{\"Id\":\"first_contact\",\"Stars\":2,\"BestTime\":150.0," +
                    "\"Completions\":1}],\"Quality\":3,\"ShowFps\":true,\"Muted\":false,\"FreeFlightWeather\":\"rain\"}");
                SaveData old = new SaveStore(path).Load(out result);
                Assert.AreEqual(LoadResult.Loaded, result);
                Assert.AreEqual(SaveData.CurrentVersion, old.Version);
                Assert.AreEqual(500, old.Credits);
                Assert.AreEqual(3, old.Quality);
                Assert.AreEqual("rain", old.FreeFlightWeather);
                Assert.AreEqual(2, old.Parts, "stars already earned pay their parts");
                Assert.IsTrue(Garage.Owns(old, DroneCatalog.Starter));
            }
            finally
            {
                Directory.Delete(directory, true);
            }
        }
    }
}

namespace Skyforge.Tests
{
    public class NegotiationTests
    {
        static MissionDefinition Finale()
        {
            var mission = new MissionDefinition { Id = "broken_arrow", BaseReward = 600, StarReward = 100, WeatherId = "sunset" };
            mission.Zones.Add(new ZoneDefinition { Radius = 100f, Floor = 20f });
            mission.Zones.Add(new ZoneDefinition { Radius = 100f, Floor = 20f });
            return mission;
        }

        [Test]
        public void EveryRegionEndsWithANegotiationThatHasItsTexts()
        {
            Assert.AreEqual(RegionCatalog.All.Count, Negotiations.All.Count);
            foreach (Negotiation negotiation in Negotiations.All)
            {
                Assert.IsTrue(Loc.Has(negotiation.SpeakerKey), negotiation.SpeakerKey);
                Assert.IsTrue(Loc.Has(negotiation.TextKey), negotiation.TextKey);
                Assert.AreEqual(3, negotiation.Choices.Count);
                foreach (NegotiationChoice choice in negotiation.Choices)
                {
                    Assert.IsTrue(Loc.Has(choice.TextKey), choice.TextKey);
                    Assert.IsTrue(Loc.Has(choice.ReplyKey), choice.ReplyKey);
                    Assert.IsTrue(string.IsNullOrEmpty(choice.Weather) || WeatherCatalog.Get(choice.Weather).Id == choice.Weather);
                }
            }
        }

        [Test]
        public void NoChoiceLeavesTheMissionAlone()
        {
            MissionDefinition mission = Finale();
            Assert.AreSame(mission, Negotiations.Apply(mission, new SaveData()));
            var ordinary = new MissionDefinition { Id = "sprint" };
            Assert.AreSame(ordinary, Negotiations.Apply(ordinary, new SaveData()), "a mission without a negotiation");
        }

        [Test]
        public void PayingRemovesARadarAndCostsCreditsOnce()
        {
            var save = new SaveData { Credits = 500 };
            Negotiation negotiation = Negotiations.For("broken_arrow");
            Assert.IsTrue(Negotiations.Choose(save, negotiation, 0));
            Assert.AreEqual(200, save.Credits);
            Assert.IsFalse(Negotiations.Choose(save, negotiation, 2), "the choice is final");
            Assert.AreEqual(200, save.Credits);
            Assert.AreEqual(0, Negotiations.Chosen(save, "broken_arrow"));

            MissionDefinition original = Finale();
            MissionDefinition changed = Negotiations.Apply(original, save);
            Assert.AreEqual(1, changed.Zones.Count);
            Assert.AreEqual(2, original.Zones.Count, "the catalog definition is untouched");
            Assert.AreEqual(600, changed.BaseReward);
            Assert.IsTrue(save.IsValid());
        }

        [Test]
        public void ChoiceThatCannotBePaidIsRefused()
        {
            var save = new SaveData { Credits = 100 };
            Assert.IsFalse(Negotiations.Choose(save, Negotiations.For("broken_arrow"), 0));
            Assert.AreEqual(100, save.Credits);
            Assert.AreEqual(-1, Negotiations.Chosen(save, "broken_arrow"));
        }

        [Test]
        public void TakingTheRiskRaisesThePayAndChangesTheWeather()
        {
            var save = new SaveData();
            Negotiations.Choose(save, Negotiations.For("broken_arrow"), 2);
            MissionDefinition changed = Negotiations.Apply(Finale(), save);
            Assert.AreEqual(900, changed.BaseReward);
            Assert.AreEqual(150, changed.StarReward);
            Assert.AreEqual("storm", changed.WeatherId);
            Assert.AreEqual(2, changed.Zones.Count);
        }

        [Test]
        public void StormChoiceKeepsEveryFinaleWithinSignalRange()
        {
            // The risky answer swaps the weather for a storm, which shortens the link.
            var spec = DroneSpec.Sparrow();
            float range = spec.SignalRange * WeatherCatalog.Get("storm").SignalRange;
            foreach (RegionDefinition region in RegionCatalog.All)
            {
                Island island = Island.Generate(region.Seed, region.Biome);
                foreach (string anchor in new[] { "poi2", "poi3", "poi4", "poi10" })
                    Assert.Less(Vector3.Distance(island.Anchors[anchor], island.Spawn), range, region.Id + " " + anchor);
            }
        }
    }
}
