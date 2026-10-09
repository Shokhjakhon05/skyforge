using System.Collections.Generic;
using UnityEngine;

namespace Skyforge.Core
{
    /// <summary>
    /// When an advertisement may be offered or shown. The SDK that actually shows ads sits behind
    /// an interface in the game; these rules are the same whichever SDK it is, and they are what
    /// keeps advertising from getting in the player's way.
    /// </summary>
    public static class AdRules
    {
        /// <summary>Free part crates a player may earn by watching ads in one day.</summary>
        public const int FreeCratesPerDay = 3;
        public const int PartsPerCrate = 2;
        /// <summary>No interstitial before the player has finished this many missions.</summary>
        public const int MissionsBeforeInterstitials = 4;
        public const float SecondsBetweenInterstitials = 180f;

        /// <summary>
        /// Doubling a mission's reward: once per result, only for a reward that was actually paid.
        /// With "no ads" bought the doubling is simply given.
        /// </summary>
        public static bool CanDouble(MissionOutcome outcome)
        {
            return outcome.Succeeded && outcome.Granted && !outcome.Doubled && outcome.CreditsGranted > 0;
        }

        /// <summary>Pays the doubled reward. Call only after the SDK reports the ad was watched to the end.</summary>
        public static int Double(SaveData save, MissionOutcome outcome)
        {
            if (!CanDouble(outcome)) return 0;
            outcome.Doubled = true;
            save.Credits += outcome.CreditsGranted;
            save.Stats.CreditsEarned += outcome.CreditsGranted;
            return outcome.CreditsGranted;
        }

        public static int CratesLeft(SaveData save, string today)
        {
            return save.AdDate == today ? Mathf.Max(0, FreeCratesPerDay - save.AdCratesToday) : FreeCratesPerDay;
        }

        /// <summary>Pays one free crate of parts, up to the daily cap. Call only after a completed ad.</summary>
        public static int ClaimCrate(SaveData save, string today)
        {
            if (save.AdDate != today)
            {
                save.AdDate = today;
                save.AdCratesToday = 0;
            }
            if (save.AdCratesToday >= FreeCratesPerDay) return 0;
            save.AdCratesToday++;
            save.Parts += PartsPerCrate;
            return PartsPerCrate;
        }

        /// <summary>
        /// An interstitial is allowed only between missions, after a success, once the player is
        /// past the first few missions, not in their first session, and not too soon after the
        /// last one. Never for a player who bought "no ads".
        /// </summary>
        /// <param name="secondsSinceLast">Time since the last interstitial this session; a large number if none.</param>
        public static bool MayShowInterstitial(SaveData save, MissionOutcome outcome, float secondsSinceLast)
        {
            if (save.NoAds) return false;
            if (outcome == null || !outcome.Succeeded) return false;
            if (save.Sessions < 2) return false;
            if (Achievements.Value(save, "missions") < MissionsBeforeInterstitials) return false;
            return secondsSinceLast >= SecondsBetweenInterstitials;
        }
    }

    public sealed class Product
    {
        public string Id;
        /// <summary>Bought once and kept, as opposed to a pack that can be bought again.</summary>
        public bool OneTime;
        public int Credits;
        public int Parts;
        public bool NoAds;
        /// <summary>A drone that comes with the product, or null.</summary>
        public string Drone;

        public string NameKey => "product." + Id + ".name";
        public string NoteKey => "product." + Id + ".note";
    }

    /// <summary>
    /// What the shop sells for real money. Nothing here is needed to finish the campaign, and
    /// nothing is a random box: every product says exactly what it gives.
    /// </summary>
    public static class Shop
    {
        public const string NoAdsId = "no_ads";
        public const string StarterId = "starter_pack";
        /// <summary>The starter pack is offered for this many days after the first launch.</summary>
        public const int StarterDays = 3;

        public static readonly Product[] Products =
        {
            new Product { Id = NoAdsId, OneTime = true, NoAds = true },
            new Product { Id = StarterId, OneTime = true, Credits = 1500, Parts = 5, Drone = "kestrel" },
            new Product { Id = "credits_small", Credits = 2000 },
            new Product { Id = "credits_medium", Credits = 6000, Parts = 5 },
            new Product { Id = "credits_large", Credits = 15000, Parts = 15 },
            new Product { Id = "parts_pack", Parts = 20 },
        };

        public static Product Get(string id)
        {
            foreach (Product product in Products)
                if (product.Id == id) return product;
            return null;
        }

        public static bool Owns(SaveData save, string productId) => save.Purchases.Contains(productId);

        /// <summary>Whether the shop should list a product for this player today.</summary>
        public static bool IsOffered(SaveData save, Product product, string today)
        {
            if (product.OneTime && Owns(save, product.Id)) return false;
            if (product.Id != StarterId) return true;
            if (string.IsNullOrEmpty(save.FirstDate)) return true;
            return Daily.DaysApart(save.FirstDate, today) < StarterDays;
        }

        /// <summary>
        /// Hands over what a product contains. Call only after the store confirms the purchase.
        /// A one-time product is delivered once however many times the store reports it, which is
        /// what makes restoring purchases on a new phone safe.
        /// </summary>
        public static bool Deliver(SaveData save, string productId)
        {
            Product product = Get(productId);
            if (product == null) return false;
            if (product.OneTime)
            {
                if (Owns(save, productId)) return false;
                save.Purchases.Add(productId);
            }
            save.Credits += product.Credits;
            save.Parts += product.Parts;
            if (product.NoAds) save.NoAds = true;
            if (!string.IsNullOrEmpty(product.Drone) && save.FindDrone(product.Drone) == null)
                save.Drones.Add(DroneRecord.New(product.Drone));
            return true;
        }
    }
}
