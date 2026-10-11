using System.Collections.Generic;
using UnityEngine;

namespace Sniper
{
    /// <summary>One rifle as it comes out of the case, before anything is fitted to it.</summary>
    public sealed class RifleDef
    {
        public string Id, NameKey;
        public int Price;
        /// <summary>Missions that must be complete before the shop offers it.</summary>
        public int MissionsRequired;
        /// <summary>Damage to a body; a man has 100 health, an armoured one 220. A head is always fatal.</summary>
        public float Damage = 100f;
        /// <summary>Greatest magnification of its scope.</summary>
        public float Zoom = 6f;
        /// <summary>How still it holds, 0..1: the share of the sway that is taken away.</summary>
        public float Stability = 0.2f;
        public int Magazine = 5;
        public float ReloadSeconds = 2.6f, BoltSeconds = 1.1f;
        public float MuzzleSpeed = 760f;
        public bool Suppressed;
        /// <summary>Goes through thin cover (a fence, a car door, a window) and keeps part of its damage.</summary>
        public bool Piercing;
    }

    public enum UpgradeKind { Scope, Barrel, Stock, Magazine, Bolt }

    public static class Armory
    {
        public const int MaxLevel = 5;

        public static readonly List<RifleDef> All = new List<RifleDef>
        {
            new RifleDef { Id = "m24", NameKey = "rifle.m24", Price = 0, Damage = 100f, Zoom = 8f, Stability = 0.15f, Magazine = 5, BoltSeconds = 1.25f },
            new RifleDef { Id = "svd", NameKey = "rifle.svd", Price = 4500, MissionsRequired = 3, Damage = 110f, Zoom = 8f, Stability = 0.2f, Magazine = 10, BoltSeconds = 0.55f, MuzzleSpeed = 800f },
            new RifleDef { Id = "vss", NameKey = "rifle.vss", Price = 12000, MissionsRequired = 7, Damage = 105f, Zoom = 8f, Stability = 0.3f, Magazine = 10, BoltSeconds = 0.5f, MuzzleSpeed = 420f, Suppressed = true },
            new RifleDef { Id = "awm", NameKey = "rifle.awm", Price = 26000, MissionsRequired = 12, Damage = 180f, Zoom = 12f, Stability = 0.35f, Magazine = 5, BoltSeconds = 1.2f, MuzzleSpeed = 900f },
            new RifleDef { Id = "m82", NameKey = "rifle.m82", Price = 55000, MissionsRequired = 18, Damage = 260f, Zoom = 14f, Stability = 0.3f, Magazine = 8, BoltSeconds = 0.8f, MuzzleSpeed = 850f, Piercing = true },
            new RifleDef { Id = "ghost", NameKey = "rifle.ghost", Price = 98000, MissionsRequired = 24, Damage = 240f, Zoom = 16f, Stability = 0.5f, Magazine = 7, BoltSeconds = 0.9f, MuzzleSpeed = 880f, Suppressed = true, Piercing = true },
        };

        public static RifleDef Get(string id)
        {
            foreach (RifleDef rifle in All)
                if (rifle.Id == id) return rifle;
            return All[0];
        }

        /// <summary>What the next level of an upgrade costs on this rifle; zero when there is none left.</summary>
        public static int UpgradePrice(RifleDef rifle, int level)
        {
            if (level >= MaxLevel) return 0;
            return Mathf.RoundToInt((600 + rifle.Price * 0.08f) * (level + 1) * (1f + level * 0.35f) / 50f) * 50;
        }

        /// <summary>The rifle with its fitted parts counted in.</summary>
        public static RifleDef Fitted(RifleDef b, int scope, int barrel, int stock, int magazine, int bolt)
        {
            return new RifleDef
            {
                Id = b.Id, NameKey = b.NameKey, Price = b.Price, MissionsRequired = b.MissionsRequired, Suppressed = b.Suppressed, Piercing = b.Piercing,
                Zoom = b.Zoom + scope * 0.8f,
                Damage = b.Damage * (1f + barrel * 0.08f),
                MuzzleSpeed = b.MuzzleSpeed * (1f + barrel * 0.04f),
                Stability = Mathf.Min(0.92f, b.Stability + stock * 0.1f),
                Magazine = b.Magazine + magazine,
                ReloadSeconds = b.ReloadSeconds * (1f - magazine * 0.05f),
                BoltSeconds = b.BoltSeconds * (1f - bolt * 0.1f),
            };
        }
    }
}
