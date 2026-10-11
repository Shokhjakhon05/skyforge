using System.Collections.Generic;
using UnityEngine;

namespace Skyforge.Core
{
    public enum Precipitation
    {
        None,
        Rain,
        Snow,
        Dust,
    }

    /// <summary>
    /// One set of conditions: how the place looks and what it does to the flight. Everything that
    /// affects gameplay is listed here and shown in the briefing; there are no hidden modifiers.
    /// </summary>
    public sealed class Weather
    {
        public string Id;
        public string NameKey;

        // Gameplay.
        /// <summary>Steady wind in m/s and the compass direction it blows towards.</summary>
        public float WindSpeed;
        public float WindHeadingDeg;
        /// <summary>Extra wind that comes and goes, m/s.</summary>
        public float Gust;
        /// <summary>Multiplier on battery drain; cold and rain cost charge.</summary>
        public float BatteryDrain = 1f;
        /// <summary>Multiplier on signal range.</summary>
        public float SignalRange = 1f;
        /// <summary>Multiplier on how far a camera can see; thermal sensors ignore it.</summary>
        public float Visibility = 1f;

        // Look.
        public float SunPitchDeg = 38f;
        public float SunYawDeg = -40f;
        public Color SunColor = new Color(1f, 0.95f, 0.85f);
        public float SunIntensity = 1.35f;
        public Color AmbientSky = new Color(0.52f, 0.62f, 0.78f);
        public Color AmbientEquator = new Color(0.46f, 0.50f, 0.52f);
        public Color AmbientGround = new Color(0.26f, 0.25f, 0.22f);
        public Color Fog = new Color(0.70f, 0.80f, 0.90f);
        public float FogStart = 250f;
        public float FogEnd = 2600f;
        public Color SkyTint = new Color(0.50f, 0.52f, 0.55f);
        public float SkyExposure = 1.25f;
        public float Atmosphere = 0.95f;
        public Color Sea = new Color(0.06f, 0.25f, 0.38f);
        /// <summary>Which photographed sky is overhead, and where the sun stands in that picture.</summary>
        public string Sky = "clear";
        public float SkySunYawDeg = 33f;
        public Color SkyTone = Color.white;
        public float SkyBrightness = 1f;
        /// <summary>A solid cloud deck instead of the blue sky dome: the sky is the colour of the fog.</summary>
        public bool Overcast;
        public Precipitation Precipitation;
        /// <summary>0..1 density of rain, snow or dust.</summary>
        public float PrecipitationAmount;

        public bool HasWind => WindSpeed > 0.5f || Gust > 0.5f;

        /// <summary>Wind at a moment in time: the steady part plus gusts that swing its strength and direction.</summary>
        public Vector3 WindAt(float time)
        {
            if (!HasWind) return Vector3.zero;
            // Perlin noise can stray slightly outside 0..1, hence the clamps.
            float swell = Mathf.Clamp(Mathf.PerlinNoise(time * 0.35f, 3.1f) * 2f - 1f, -1f, 1f);
            float swing = Mathf.Clamp(Mathf.PerlinNoise(7.7f, time * 0.22f) * 2f - 1f, -1f, 1f);
            float speed = Mathf.Max(0f, WindSpeed + Gust * swell);
            float heading = (WindHeadingDeg + swing * 25f) * Mathf.Deg2Rad;
            return new Vector3(Mathf.Sin(heading), 0f, Mathf.Cos(heading)) * speed;
        }
    }

    public static class WeatherCatalog
    {
        public const string Clear = "clear";

        static readonly List<Weather> List = new List<Weather>
        {
            new Weather { Id = Clear, NameKey = "weather.clear" },
            new Weather
            {
                Id = "sunset", NameKey = "weather.sunset",
                Sky = "sunset", SkySunYawDeg = 49f, SkyBrightness = 1.05f,
                SunPitchDeg = 9f, SunYawDeg = -70f, SunColor = new Color(1f, 0.62f, 0.34f), SunIntensity = 1.15f,
                AmbientSky = new Color(0.42f, 0.42f, 0.58f), AmbientEquator = new Color(0.52f, 0.40f, 0.34f),
                AmbientGround = new Color(0.20f, 0.17f, 0.16f),
                Fog = new Color(0.86f, 0.62f, 0.46f), FogStart = 200f, FogEnd = 2200f,
                SkyTint = new Color(0.62f, 0.46f, 0.38f), SkyExposure = 1.15f, Atmosphere = 1.6f,
                Sea = new Color(0.10f, 0.20f, 0.30f),
            },
            new Weather
            {
                Id = "overcast", NameKey = "weather.overcast",
                Sky = "overcast", SkySunYawDeg = 29f, Overcast = true,
                WindSpeed = 3f, WindHeadingDeg = 80f, Gust = 2f,
                SunColor = new Color(0.86f, 0.88f, 0.90f), SunIntensity = 0.7f,
                AmbientSky = new Color(0.56f, 0.59f, 0.63f), AmbientEquator = new Color(0.48f, 0.50f, 0.52f),
                AmbientGround = new Color(0.28f, 0.28f, 0.27f),
                Fog = new Color(0.66f, 0.69f, 0.72f), FogStart = 120f, FogEnd = 1500f,
                SkyTint = new Color(0.60f, 0.62f, 0.64f), SkyExposure = 0.85f, Atmosphere = 0.5f,
                Sea = new Color(0.14f, 0.22f, 0.27f),
            },
            new Weather
            {
                Id = "rain", NameKey = "weather.rain",
                Sky = "overcast", SkySunYawDeg = 29f, SkyBrightness = 0.78f, SkyTone = new Color(0.90f, 0.94f, 1f), Visibility = 0.8f, Overcast = true,
                WindSpeed = 5f, WindHeadingDeg = 110f, Gust = 3f, BatteryDrain = 1.15f, SignalRange = 0.9f,
                SunColor = new Color(0.72f, 0.76f, 0.80f), SunIntensity = 0.5f,
                AmbientSky = new Color(0.46f, 0.50f, 0.55f), AmbientEquator = new Color(0.40f, 0.43f, 0.46f),
                AmbientGround = new Color(0.22f, 0.23f, 0.23f),
                Fog = new Color(0.52f, 0.56f, 0.60f), FogStart = 60f, FogEnd = 800f,
                SkyTint = new Color(0.48f, 0.50f, 0.53f), SkyExposure = 0.6f, Atmosphere = 0.4f,
                Sea = new Color(0.12f, 0.18f, 0.22f),
                Precipitation = Precipitation.Rain, PrecipitationAmount = 0.7f,
            },
            new Weather
            {
                Id = "fog", NameKey = "weather.fog",
                Sky = "fog", SkySunYawDeg = 37f, Visibility = 0.45f, Overcast = true,
                SignalRange = 0.85f,
                SunColor = new Color(0.90f, 0.90f, 0.88f), SunIntensity = 0.6f,
                AmbientSky = new Color(0.62f, 0.64f, 0.66f), AmbientEquator = new Color(0.56f, 0.57f, 0.58f),
                AmbientGround = new Color(0.34f, 0.34f, 0.33f),
                Fog = new Color(0.74f, 0.76f, 0.78f), FogStart = 15f, FogEnd = 260f,
                SkyTint = new Color(0.70f, 0.71f, 0.72f), SkyExposure = 0.9f, Atmosphere = 0.3f,
                Sea = new Color(0.20f, 0.26f, 0.30f),
            },
            new Weather
            {
                Id = "storm", NameKey = "weather.storm",
                Sky = "overcast", SkySunYawDeg = 29f, SkyBrightness = 0.48f, SkyTone = new Color(0.82f, 0.88f, 1f), Visibility = 0.6f, Overcast = true,
                WindSpeed = 9f, WindHeadingDeg = 140f, Gust = 5f, BatteryDrain = 1.3f, SignalRange = 0.75f,
                SunColor = new Color(0.60f, 0.64f, 0.72f), SunIntensity = 0.35f,
                AmbientSky = new Color(0.32f, 0.35f, 0.42f), AmbientEquator = new Color(0.28f, 0.30f, 0.34f),
                AmbientGround = new Color(0.15f, 0.16f, 0.17f),
                Fog = new Color(0.34f, 0.37f, 0.42f), FogStart = 40f, FogEnd = 600f,
                SkyTint = new Color(0.34f, 0.36f, 0.40f), SkyExposure = 0.4f, Atmosphere = 0.4f,
                Sea = new Color(0.08f, 0.12f, 0.16f),
                Precipitation = Precipitation.Rain, PrecipitationAmount = 1f,
            },
            new Weather
            {
                Id = "dust", NameKey = "weather.dust",
                Sky = "fog", SkySunYawDeg = 37f, SkyBrightness = 0.95f, SkyTone = new Color(1f, 0.84f, 0.62f), Visibility = 0.6f, Overcast = true,
                WindSpeed = 6f, WindHeadingDeg = 250f, Gust = 4f, SignalRange = 0.85f,
                SunColor = new Color(1f, 0.82f, 0.58f), SunIntensity = 0.9f,
                AmbientSky = new Color(0.66f, 0.56f, 0.42f), AmbientEquator = new Color(0.60f, 0.50f, 0.38f),
                AmbientGround = new Color(0.34f, 0.28f, 0.20f),
                Fog = new Color(0.78f, 0.64f, 0.44f), FogStart = 30f, FogEnd = 450f,
                SkyTint = new Color(0.74f, 0.60f, 0.42f), SkyExposure = 0.95f, Atmosphere = 1.4f,
                Sea = new Color(0.20f, 0.24f, 0.24f),
                Precipitation = Precipitation.Dust, PrecipitationAmount = 0.6f,
            },
            new Weather
            {
                Id = "snow", NameKey = "weather.snow",
                Sky = "snow", SkySunYawDeg = 43f, SkyBrightness = 1.1f, Visibility = 0.7f, Overcast = true,
                WindSpeed = 4f, WindHeadingDeg = 20f, Gust = 2f, BatteryDrain = 1.35f,
                SunColor = new Color(0.86f, 0.90f, 0.96f), SunIntensity = 0.65f,
                AmbientSky = new Color(0.66f, 0.70f, 0.76f), AmbientEquator = new Color(0.60f, 0.63f, 0.68f),
                AmbientGround = new Color(0.40f, 0.42f, 0.45f),
                Fog = new Color(0.80f, 0.83f, 0.87f), FogStart = 40f, FogEnd = 700f,
                SkyTint = new Color(0.74f, 0.76f, 0.80f), SkyExposure = 0.9f, Atmosphere = 0.35f,
                Sea = new Color(0.16f, 0.22f, 0.28f),
                Precipitation = Precipitation.Snow, PrecipitationAmount = 0.8f,
            },
            new Weather
            {
                Id = "night", NameKey = "weather.night",
                Sky = "night", SkySunYawDeg = 36f, SkyBrightness = 0.20f, SkyTone = new Color(0.62f, 0.74f, 1f), Visibility = 0.55f,
                SunPitchDeg = 24f, SunYawDeg = 30f, SunColor = new Color(0.55f, 0.65f, 0.95f), SunIntensity = 0.34f,
                AmbientSky = new Color(0.10f, 0.13f, 0.22f), AmbientEquator = new Color(0.08f, 0.10f, 0.16f),
                AmbientGround = new Color(0.04f, 0.05f, 0.08f),
                Fog = new Color(0.03f, 0.04f, 0.08f), FogStart = 80f, FogEnd = 1400f,
                SkyTint = new Color(0.10f, 0.12f, 0.22f), SkyExposure = 0.12f, Atmosphere = 0.6f,
                Sea = new Color(0.02f, 0.05f, 0.09f),
            },
            // A night to aim through rather than fly over: the same sky under a moon bright enough to tell a man from his shadow.
            new Weather
            {
                Id = "moonlit", NameKey = "weather.night",
                Sky = "night", SkySunYawDeg = 36f, SkyBrightness = 0.26f, SkyTone = new Color(0.62f, 0.74f, 1f), Visibility = 0.7f,
                SunPitchDeg = 38f, SunYawDeg = 30f, SunColor = new Color(0.62f, 0.72f, 1f), SunIntensity = 0.95f,
                AmbientSky = new Color(0.26f, 0.32f, 0.5f), AmbientEquator = new Color(0.2f, 0.25f, 0.38f),
                AmbientGround = new Color(0.1f, 0.12f, 0.18f),
                Fog = new Color(0.04f, 0.055f, 0.1f), FogStart = 140f, FogEnd = 1600f,
                SkyTint = new Color(0.10f, 0.12f, 0.22f), SkyExposure = 0.14f, Atmosphere = 0.6f,
                Sea = new Color(0.02f, 0.05f, 0.09f),
            },
        };

        public static IReadOnlyList<Weather> All => List;

        /// <summary>Unknown ids fall back to clear weather rather than breaking a mission.</summary>
        public static Weather Get(string id)
        {
            foreach (Weather weather in List)
                if (weather.Id == id) return weather;
            return List[0];
        }

        public static string Next(string id)
        {
            for (int i = 0; i < List.Count; i++)
                if (List[i].Id == id) return List[(i + 1) % List.Count].Id;
            return List[0].Id;
        }
    }
}
