using Skyforge.Core;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Skyforge.World
{
    public enum GraphicsTier
    {
        Low = 1,
        Medium = 2,
        High = 3,
        Ultra = 4,
    }

    /// <summary>
    /// Light, sky, fog, sea colour, precipitation and screen effects. A <see cref="Weather"/>
    /// decides how it looks; a <see cref="GraphicsTier"/> decides how much of it the phone draws.
    /// </summary>
    public sealed class SceneLook
    {
        readonly Light _sun;
        readonly Material _sky;
        Material _sea;
        readonly Camera _camera;
        readonly ParticleSystem _particles;
        readonly ParticleSystemRenderer _particleRenderer;
        readonly Material _particleMaterial;
        readonly Volume _volume;
        readonly Bloom _bloom;
        readonly ColorAdjustments _grade;
        readonly System.Collections.Generic.Dictionary<string, Texture2D> _skies =
            new System.Collections.Generic.Dictionary<string, Texture2D>();
        Cubemap _reflection;
        bool _scope;
        Weather _weather;
        GraphicsTier _quality = GraphicsTier.High;

        public GraphicsTier Quality => _quality;

        public SceneLook(Transform parent, Camera camera, Material sea)
        {
            _camera = camera;
            _sea = sea;

            var sunObject = new GameObject("Sun");
            sunObject.transform.SetParent(parent, false);
            _sun = sunObject.AddComponent<Light>();
            _sun.type = LightType.Directional;
            _sun.shadowStrength = 0.8f;
            RenderSettings.sun = _sun;

            _sky = GameMaterials.Sky;
            if (_sky != null) RenderSettings.skybox = _sky;
            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.Linear;

            // Precipitation is emitted from a box that Update keeps above and ahead of the camera.
            var particleObject = new GameObject("Precipitation");
            particleObject.transform.SetParent(parent, false);
            _particles = particleObject.AddComponent<ParticleSystem>();
            _particleRenderer = particleObject.GetComponent<ParticleSystemRenderer>();
            _particleMaterial = GameMaterials.Unlit(Color.white);
            _particleRenderer.sharedMaterial = _particleMaterial;
            _particleRenderer.shadowCastingMode = ShadowCastingMode.Off;
            _particleRenderer.receiveShadows = false;
            ParticleSystem.MainModule main = _particles.main;
            main.loop = true;
            main.playOnAwake = true;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.startSpeed = 0f;
            main.maxParticles = 4000;
            ParticleSystem.ShapeModule shape = _particles.shape;
            shape.shapeType = ParticleSystemShapeType.Box;
            ParticleSystem.VelocityOverLifetimeModule velocity = _particles.velocityOverLifetime;
            velocity.enabled = true;
            velocity.space = ParticleSystemSimulationSpace.World;
            _particles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

            // A touch of contrast, a soft vignette, and bloom on the top tier.
            var volumeObject = new GameObject("ScreenEffects");
            volumeObject.transform.SetParent(parent, false);
            _volume = volumeObject.AddComponent<Volume>();
            _volume.isGlobal = true;
            var profile = ScriptableObject.CreateInstance<VolumeProfile>();
            _grade = profile.Add<ColorAdjustments>(true);
            _grade.contrast.value = 14f;
            _grade.saturation.value = -4f;
            _grade.postExposure.value = 0.12f;
            var tone = profile.Add<Tonemapping>(true);
            tone.mode.value = TonemappingMode.Neutral;
            var vignette = profile.Add<Vignette>(true);
            vignette.intensity.value = 0.24f;
            vignette.smoothness.value = 0.45f;
            _bloom = profile.Add<Bloom>(true);
            _bloom.threshold.value = 1.0f;
            _bloom.intensity.value = 0.55f;
            _bloom.scatter.value = 0.62f;
            _volume.sharedProfile = profile;
        }

        /// <summary>The sea of the region now loaded; the weather recolours it.</summary>
        public void SetSea(Material sea)
        {
            _sea = sea;
            if (_weather != null) Apply(_weather);
        }

        /// <summary>The world map is seen against empty dark space: no fog, no sky, no rain.</summary>
        public void ShowSpace()
        {
            RenderSettings.fog = false;
            _camera.clearFlags = CameraClearFlags.SolidColor;
            _camera.backgroundColor = new Color(0.01f, 0.015f, 0.03f);
            _particles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        }

        /// <summary>Call every frame: keeps the precipitation around wherever the camera is looking.</summary>
        public void Update()
        {
            Transform cam = _camera.transform;
            Vector3 ahead = cam.forward;
            ahead.y = 0f;
            if (ahead.sqrMagnitude > 0.01f) ahead.Normalize();
            _particles.transform.position = cam.position + ahead * 9f;
        }

        /// <summary>Picks a tier from the phone's memory. Ultra is never chosen automatically.</summary>
        public static GraphicsTier Recommend()
        {
            int memory = SystemInfo.systemMemorySize;
            if (memory < 3500) return GraphicsTier.Low;
            // Phones with a mid-range chip often carry 6 GB; they get the middle tier too.
            if (memory < 7200) return GraphicsTier.Medium;
            return GraphicsTier.High;
        }

        public void SetQuality(GraphicsTier quality)
        {
            _quality = quality;
            var pipeline = GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset;
            if (pipeline != null)
            {
                pipeline.renderScale = quality == GraphicsTier.Low ? 0.7f : quality == GraphicsTier.Medium ? 0.85f : 1f;
                pipeline.msaaSampleCount = quality == GraphicsTier.Low ? 1 : quality == GraphicsTier.Ultra ? 4 : 2;
                pipeline.shadowDistance = quality == GraphicsTier.Medium ? 70f : quality == GraphicsTier.Ultra ? 220f : 140f;
            }

            _sun.shadows = quality == GraphicsTier.Low ? LightShadows.None : LightShadows.Soft;

            bool effects = quality >= GraphicsTier.Medium;
            _volume.enabled = effects;
            _bloom.active = quality >= GraphicsTier.High;
            if (pipeline != null) pipeline.supportsHDR = quality >= GraphicsTier.High;
            // The cheapest tier draws surfaces without their grain.
            if (quality == GraphicsTier.Low) Shader.EnableKeyword("SKYFORGE_FLAT");
            else Shader.DisableKeyword("SKYFORGE_FLAT");
            // The middle tier looks at the grain from one side only: a third of the texture reads.
            if (quality == GraphicsTier.Medium) Shader.EnableKeyword("SKYFORGE_LITE");
            else Shader.DisableKeyword("SKYFORGE_LITE");
            UniversalAdditionalCameraData cameraData = _camera.GetUniversalAdditionalCameraData();
            if (cameraData != null) cameraData.renderPostProcessing = effects;

            if (_weather != null) ApplyPrecipitation(_weather);
        }

        public void Apply(Weather weather)
        {
            _weather = weather;
            RenderSettings.fog = true;
            _sun.transform.rotation = Quaternion.Euler(weather.SunPitchDeg, weather.SunYawDeg, 0f);
            _sun.color = weather.SunColor;
            _sun.intensity = weather.SunIntensity;

            RenderSettings.ambientSkyColor = weather.AmbientSky;
            RenderSettings.ambientEquatorColor = weather.AmbientEquator;
            RenderSettings.ambientGroundColor = weather.AmbientGround;
            RenderSettings.fogColor = weather.Fog;
            RenderSettings.fogStartDistance = weather.FogStart;
            RenderSettings.fogEndDistance = weather.FogEnd;

            _camera.clearFlags = CameraClearFlags.Skybox;
            _camera.backgroundColor = weather.Fog;
            if (_sky != null)
            {
                if (_sky.HasProperty("_HorizonColor"))
                {
                    Texture2D picture = SkyPicture(weather.Sky);
                    if (picture != null) _sky.SetTexture("_MainTex", picture);
                    _sky.SetColor("_Tint", weather.SkyTone);
                    _sky.SetFloat("_Exposure", weather.SkyBrightness);
                    _sky.SetColor("_HorizonColor", weather.Fog);
                    // Thick weather swallows more of the sky above the horizon.
                    _sky.SetFloat("_HorizonHeight", Mathf.Lerp(0.34f, 0.07f, Mathf.InverseLerp(260f, 2200f, weather.FogEnd)));
                    // The sun in the photograph goes where the light comes from.
                    _sky.SetFloat("_Rotation", (weather.SunYawDeg + 180f - weather.SkySunYawDeg) * Mathf.Deg2Rad);
                }
                else
                {
                    _sky.SetColor("_SkyTint", weather.SkyTint);
                    _sky.SetColor("_GroundColor", weather.Fog);
                    _sky.SetFloat("_Exposure", weather.SkyExposure);
                    _sky.SetFloat("_AtmosphereThickness", weather.Atmosphere);
                }
            }
            ApplyReflection(weather);
            if (_sea != null)
            {
                if (_sea.HasProperty("_DeepColor"))
                {
                    _sea.SetColor("_DeepColor", weather.Sea);
                    _sea.SetColor("_SkyColor", weather.Fog);
                    _sea.SetFloat("_WaveStrength", 0.30f + Mathf.Clamp01(weather.WindSpeed / 10f) * 0.5f);
                    _sea.SetFloat("_WaveSpeed", 0.6f + weather.WindSpeed * 0.08f);
                }
                else
                {
                    _sea.SetColor("_BaseColor", weather.Sea);
                }
            }
            ApplyPrecipitation(weather);
        }

        Texture2D SkyPicture(string id)
        {
            if (_skies.TryGetValue(id, out Texture2D cached)) return cached;
            var picture = Resources.Load<Texture2D>("Sky/sky_" + id);
            if (picture == null) Debug.LogError("[Skyforge] Missing sky " + id);
            _skies[id] = picture;
            return picture;
        }

        /// <summary>
        /// What shiny things reflect: a small gradient of the weather's own sky, horizon and ground
        /// colours. Nothing is baked in a code-only project, and without this metal reflects black.
        /// </summary>
        void ApplyReflection(Weather weather)
        {
            const int size = 16;
            if (_reflection == null) _reflection = new Cubemap(size, TextureFormat.RGBA32, true) { name = "SkyReflection" };
            Color zenith = weather.AmbientSky * 1.25f, horizon = weather.Fog, ground = weather.AmbientGround * 0.9f;
            var pixels = new Color[size * size];
            for (int face = 0; face < 6; face++)
            {
                for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    float a = (x + 0.5f) / size * 2f - 1f, b = (y + 0.5f) / size * 2f - 1f;
                    // Only the height of the direction matters for a gradient.
                    float up;
                    if (face == (int)CubemapFace.PositiveY) up = 1f / Mathf.Sqrt(1f + a * a + b * b);
                    else if (face == (int)CubemapFace.NegativeY) up = -1f / Mathf.Sqrt(1f + a * a + b * b);
                    else up = -b / Mathf.Sqrt(1f + a * a + b * b);
                    Color color = up >= 0f
                        ? Color.Lerp(horizon, zenith, Mathf.Pow(up, 0.6f))
                        : Color.Lerp(horizon, ground, Mathf.Pow(-up, 0.4f));
                    color.a = 1f;
                    pixels[y * size + x] = color;
                }
                _reflection.SetPixels(pixels, (CubemapFace)face);
            }
            _reflection.Apply(true);
            RenderSettings.defaultReflectionMode = DefaultReflectionMode.Custom;
            RenderSettings.customReflectionTexture = _reflection;
            RenderSettings.reflectionIntensity = 1f;
        }

        /// <summary>
        /// The gun camera: the picture a strike drone's operator sees, drained of colour and hard
        /// in contrast. Only the grade changes; the HUD draws the reticle.
        /// </summary>
        public void SetScope(bool on)
        {
            _scope = on;
            _grade.saturation.value = on ? -100f : -4f;
            _grade.contrast.value = on ? 38f : 14f;
            _grade.postExposure.value = on ? 0.35f : 0.12f;
        }

        public bool Scope => _scope;

        void ApplyPrecipitation(Weather weather)
        {
            if (weather.Precipitation == Precipitation.None || weather.PrecipitationAmount <= 0f)
            {
                _particles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
                return;
            }

            float density = _quality == GraphicsTier.Low ? 0.35f : _quality == GraphicsTier.Medium ? 0.6f : 1f;
            float amount = weather.PrecipitationAmount * density;
            Vector3 wind = weather.WindAt(0f);

            float fall, lifetime, size, rate, stretch;
            Color color;
            Vector3 boxSize, boxOffset;
            switch (weather.Precipitation)
            {
                case Precipitation.Rain:
                    // Thin streaks only a little lighter than the fog: rain is seen as texture, not as lines.
                    fall = 22f; lifetime = 1.1f; size = 0.014f; rate = 3200f; stretch = 0.035f;
                    color = Color.Lerp(weather.Fog, Color.white, 0.35f);
                    boxSize = new Vector3(34f, 1f, 34f); boxOffset = new Vector3(0f, 12f, 0f);
                    break;
                case Precipitation.Snow:
                    fall = 2.6f; lifetime = 6f; size = 0.05f; rate = 900f; stretch = 0f;
                    color = new Color(0.97f, 0.98f, 1f);
                    boxSize = new Vector3(40f, 1f, 40f); boxOffset = new Vector3(0f, 10f, 0f);
                    break;
                default:
                    // Dust: fine specks drifting through the whole volume around the camera.
                    fall = 0.4f; lifetime = 4f; size = 0.022f; rate = 900f; stretch = 0f;
                    color = Color.Lerp(weather.Fog, new Color(0.45f, 0.36f, 0.24f), 0.5f);
                    boxSize = new Vector3(40f, 18f, 40f); boxOffset = new Vector3(0f, 2f, 0f);
                    break;
            }

            ParticleSystem.MainModule main = _particles.main;
            main.startLifetime = lifetime;
            main.startSize = size;
            main.startColor = color;
            ParticleSystem.EmissionModule emission = _particles.emission;
            emission.rateOverTime = rate * amount;
            ParticleSystem.ShapeModule shape = _particles.shape;
            shape.scale = boxSize;
            shape.position = boxOffset;
            ParticleSystem.VelocityOverLifetimeModule velocity = _particles.velocityOverLifetime;
            velocity.x = new ParticleSystem.MinMaxCurve(wind.x);
            velocity.y = new ParticleSystem.MinMaxCurve(-fall);
            velocity.z = new ParticleSystem.MinMaxCurve(wind.z);

            _particleRenderer.renderMode = stretch > 0f
                ? ParticleSystemRenderMode.Stretch
                : ParticleSystemRenderMode.Billboard;
            _particleRenderer.velocityScale = stretch;
            _particleRenderer.lengthScale = 1f;
            _particleMaterial.SetColor("_BaseColor", color);

            if (!_particles.isPlaying) _particles.Play(true);
        }
    }
}
