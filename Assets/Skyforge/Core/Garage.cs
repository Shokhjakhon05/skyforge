using System.Collections.Generic;

namespace Skyforge.Core
{
    public enum PurchaseResult
    {
        Ok,
        AlreadyOwned,
        NotEnoughCredits,
        NotEnoughParts,
        NotEnoughStars,
        MaxLevel,
        NotOwned,
        Unknown,
    }

    /// <summary>
    /// Buying and equipping: drones, upgrade levels and modules. All rules about what the player
    /// may buy live here, away from the screens, so they can be tested.
    /// </summary>
    public static class Garage
    {
        public static int TotalStars(SaveData save)
        {
            int stars = 0;
            foreach (MissionRecord record in save.Missions) stars += record.Stars;
            return stars;
        }

        public static bool Owns(SaveData save, string droneId) => save.FindDrone(droneId) != null;

        public static DroneDefinition Selected(SaveData save) => DroneCatalog.Get(save.SelectedDrone);

        /// <summary>The spec the selected drone flies with, upgrades and modules included.</summary>
        public static DroneSpec SelectedSpec(SaveData save) => Spec(save, Selected(save));

        public static DroneSpec Spec(SaveData save, DroneDefinition drone)
        {
            DroneRecord record = save.FindDrone(drone.Id);
            return record == null
                ? Loadout.Apply(drone, null, null)
                : Loadout.Apply(drone, record.Upgrades, record.Modules);
        }

        public static PurchaseResult CanBuyDrone(SaveData save, DroneDefinition drone)
        {
            if (Owns(save, drone.Id)) return PurchaseResult.AlreadyOwned;
            if (TotalStars(save) < drone.StarsRequired) return PurchaseResult.NotEnoughStars;
            if (save.Credits < drone.Price) return PurchaseResult.NotEnoughCredits;
            return PurchaseResult.Ok;
        }

        public static PurchaseResult BuyDrone(SaveData save, DroneDefinition drone)
        {
            PurchaseResult result = CanBuyDrone(save, drone);
            if (result != PurchaseResult.Ok) return result;
            save.Credits -= drone.Price;
            save.Drones.Add(DroneRecord.New(drone.Id));
            save.SelectedDrone = drone.Id;
            return PurchaseResult.Ok;
        }

        public static PurchaseResult Select(SaveData save, string droneId)
        {
            if (!Owns(save, droneId)) return PurchaseResult.NotOwned;
            save.SelectedDrone = droneId;
            return PurchaseResult.Ok;
        }

        public static int Level(SaveData save, string droneId, UpgradeTrack track)
        {
            DroneRecord record = save.FindDrone(droneId);
            return record == null ? 0 : record.Upgrades[(int)track];
        }

        public static PurchaseResult CanUpgrade(SaveData save, DroneDefinition drone, UpgradeTrack track)
        {
            DroneRecord record = save.FindDrone(drone.Id);
            if (record == null) return PurchaseResult.NotOwned;
            int level = record.Upgrades[(int)track];
            if (level >= Loadout.MaxLevel) return PurchaseResult.MaxLevel;
            Loadout.UpgradeCost(drone, level, out int credits, out int parts);
            if (save.Credits < credits) return PurchaseResult.NotEnoughCredits;
            if (save.Parts < parts) return PurchaseResult.NotEnoughParts;
            return PurchaseResult.Ok;
        }

        public static PurchaseResult Upgrade(SaveData save, DroneDefinition drone, UpgradeTrack track)
        {
            PurchaseResult result = CanUpgrade(save, drone, track);
            if (result != PurchaseResult.Ok) return result;
            DroneRecord record = save.FindDrone(drone.Id);
            Loadout.UpgradeCost(drone, record.Upgrades[(int)track], out int credits, out int parts);
            save.Credits -= credits;
            save.Parts -= parts;
            record.Upgrades[(int)track]++;
            return PurchaseResult.Ok;
        }

        public static bool OwnsModule(SaveData save, string moduleId) => save.OwnedModules.Contains(moduleId);

        public static PurchaseResult BuyModule(SaveData save, string moduleId)
        {
            ModuleDefinition module = Loadout.Module(moduleId);
            if (module == null) return PurchaseResult.Unknown;
            if (OwnsModule(save, moduleId)) return PurchaseResult.AlreadyOwned;
            if (save.Credits < module.Price) return PurchaseResult.NotEnoughCredits;
            save.Credits -= module.Price;
            save.OwnedModules.Add(moduleId);
            return PurchaseResult.Ok;
        }

        public static bool IsEquipped(SaveData save, string droneId, string moduleId)
        {
            DroneRecord record = save.FindDrone(droneId);
            return record != null && System.Array.IndexOf(record.Modules, moduleId) >= 0;
        }

        /// <summary>
        /// Fits a module to a drone, or takes it off if it is already fitted. A module is one
        /// physical item: fitting it to a drone removes it from any other. With both slots full,
        /// the module in the first slot makes way.
        /// </summary>
        public static PurchaseResult ToggleModule(SaveData save, string droneId, string moduleId)
        {
            DroneRecord record = save.FindDrone(droneId);
            if (record == null || !OwnsModule(save, moduleId)) return PurchaseResult.NotOwned;

            int fitted = System.Array.IndexOf(record.Modules, moduleId);
            if (fitted >= 0)
            {
                record.Modules[fitted] = "";
                return PurchaseResult.Ok;
            }

            foreach (DroneRecord other in save.Drones)
                for (int i = 0; i < other.Modules.Length; i++)
                    if (other.Modules[i] == moduleId) other.Modules[i] = "";

            int slot = System.Array.FindIndex(record.Modules, string.IsNullOrEmpty);
            if (slot < 0)
            {
                record.Modules[0] = record.Modules[1];
                slot = Loadout.ModuleSlots - 1;
            }
            record.Modules[slot] = moduleId;
            return PurchaseResult.Ok;
        }

        /// <summary>The names of the modules fitted to a drone, without the empty slots.</summary>
        public static List<string> FittedModules(SaveData save, string droneId)
        {
            var fitted = new List<string>();
            DroneRecord record = save.FindDrone(droneId);
            if (record == null) return fitted;
            foreach (string module in record.Modules)
                if (!string.IsNullOrEmpty(module)) fitted.Add(module);
            return fitted;
        }
    }
}
