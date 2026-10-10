using System.Collections.Generic;
using UnityEngine;

namespace Skyforge.Core
{
    /// <summary>What kind of land a region is; shapes the terrain and decides how it is painted.</summary>
    public sealed class Biome
    {
        public string Id;
        /// <summary>Height in metres added by rolling hills and by ridges.</summary>
        public float Hills = 60f, Ridges = 110f;
        /// <summary>0..1: how much of the suitable ground grows trees.</summary>
        public float TreeCover = 0.55f;
        public int MaxTrees = 2200;
        /// <summary>Trees do not grow above this height.</summary>
        public float TreeLine = 95f;
        public float TreeScale = 1f;
        /// <summary>0..1: the share of inland points where a shelled village stands.</summary>
        public float Ruined = 0.75f;
        /// <summary>Weather shown behind the menu and offered first in free flight.</summary>
        public string Weather = WeatherCatalog.Clear;

        public static readonly Biome Temperate = new Biome { Id = "temperate" };
        public static readonly Biome Desert = new Biome
        {
            Id = "desert", Hills = 38f, Ridges = 150f, TreeCover = 0.12f, MaxTrees = 500, TreeLine = 60f, TreeScale = 0.7f, Ruined = 1f,
        };
        public static readonly Biome Snow = new Biome
        {
            Id = "snow", Hills = 85f, Ridges = 200f, TreeCover = 0.45f, MaxTrees = 1800, TreeLine = 120f, Weather = "snow", Ruined = 0.85f,
        };
    }

    public sealed class RegionDefinition
    {
        public string Id;
        public int Seed;
        public Biome Biome;
        /// <summary>Campaign stars needed before the region opens.</summary>
        public int StarsRequired;
        /// <summary>Where the region sits on the globe, in degrees.</summary>
        public float Latitude, Longitude;

        public string NameKey => "region." + Id + ".name";
        public string NoteKey => "region." + Id + ".note";
    }

    public static class RegionCatalog
    {
        static readonly List<RegionDefinition> List = new List<RegionDefinition>
        {
            new RegionDefinition { Id = "arvelia", Seed = 7, Biome = Biome.Temperate, StarsRequired = 0, Latitude = 38f, Longitude = -20f },
            new RegionDefinition { Id = "kharad", Seed = 23, Biome = Biome.Desert, StarsRequired = 18, Latitude = 14f, Longitude = 48f },
            new RegionDefinition { Id = "nordheim", Seed = 41, Biome = Biome.Snow, StarsRequired = 45, Latitude = 64f, Longitude = 105f },
        };

        public static IReadOnlyList<RegionDefinition> All => List;

        public static RegionDefinition Get(string id)
        {
            foreach (RegionDefinition region in List)
                if (region.Id == id) return region;
            return List[0];
        }

        public static int IndexOf(string id)
        {
            for (int i = 0; i < List.Count; i++)
                if (List[i].Id == id) return i;
            return 0;
        }

        /// <summary>Unit vector from the centre of the globe to a place on its surface.</summary>
        public static Vector3 OnGlobe(float latitude, float longitude)
        {
            float lat = latitude * Mathf.Deg2Rad, lon = longitude * Mathf.Deg2Rad;
            return new Vector3(Mathf.Cos(lat) * Mathf.Sin(lon), Mathf.Sin(lat), Mathf.Cos(lat) * Mathf.Cos(lon));
        }
    }
}
