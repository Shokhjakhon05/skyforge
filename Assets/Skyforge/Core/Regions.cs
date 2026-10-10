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
        /// <summary>Above zero the land rises in steps this many metres tall: mesas and cliffs instead of slopes.</summary>
        public float Terrace;
        /// <summary>0..1: how deeply the sea cuts channels through the land, breaking it into islands.</summary>
        public float Channels;
        /// <summary>Height in metres of a volcano standing in the middle; zero for none.</summary>
        public float Cone;
        /// <summary>Tall buildings standing around each village, and how tall they may be: a city.</summary>
        public int Towers;
        public float TowerHeight = 30f;
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
        /// <summary>A scatter of low green islands in shallow water.</summary>
        public static readonly Biome Tropic = new Biome
        {
            Id = "tropic", Hills = 34f, Ridges = 55f, TreeCover = 0.7f, MaxTrees = 2400, TreeLine = 70f, TreeScale = 1.1f, Ruined = 0.8f,
            Channels = 0.85f,
        };
        /// <summary>Black sand and ash around a volcano.</summary>
        public static readonly Biome Ash = new Biome
        {
            Id = "ash", Hills = 42f, Ridges = 120f, TreeCover = 0.2f, MaxTrees = 600, TreeLine = 70f, TreeScale = 0.8f, Ruined = 0.9f,
            Cone = 160f, Weather = "sunset",
        };
        /// <summary>A city on flat ground: blocks of shelled high-rises around every crossroads.</summary>
        public static readonly Biome Urban = new Biome
        {
            Id = "urban", Hills = 14f, Ridges = 12f, TreeCover = 0.14f, MaxTrees = 500, TreeLine = 60f, Ruined = 1f,
            Towers = 9, TowerHeight = 19f, Weather = "overcast",
        };
        /// <summary>Red tablelands cut by cliffs.</summary>
        public static readonly Biome Mesa = new Biome
        {
            Id = "mesa", Hills = 70f, Ridges = 60f, TreeCover = 0.1f, MaxTrees = 400, TreeLine = 80f, TreeScale = 0.7f, Ruined = 1f,
            Terrace = 26f, Weather = "dust",
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
            new RegionDefinition { Id = "kharad", Seed = 23, Biome = Biome.Desert, StarsRequired = 2, Latitude = 14f, Longitude = 48f },
            new RegionDefinition { Id = "nordheim", Seed = 41, Biome = Biome.Snow, StarsRequired = 4, Latitude = 64f, Longitude = 105f },
            new RegionDefinition { Id = "tessara", Seed = 58, Biome = Biome.Tropic, StarsRequired = 7, Latitude = -6f, Longitude = 128f },
            new RegionDefinition { Id = "vulkar", Seed = 79, Biome = Biome.Ash, StarsRequired = 10, Latitude = -38f, Longitude = -72f },
            new RegionDefinition { Id = "redrock", Seed = 97, Biome = Biome.Mesa, StarsRequired = 13, Latitude = 30f, Longitude = -108f },
            new RegionDefinition { Id = "meridian", Seed = 127, Biome = Biome.Urban, StarsRequired = 16, Latitude = 48f, Longitude = 22f },
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
