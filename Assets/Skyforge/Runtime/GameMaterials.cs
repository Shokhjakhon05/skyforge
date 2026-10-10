using Skyforge.World;
using UnityEngine;

namespace Skyforge
{
    /// <summary>
    /// Materials the build script baked into Resources. Runtime code clones these; it never
    /// calls Shader.Find, because a shader nothing references is left out of the build.
    /// </summary>
    public static class GameMaterials
    {
        static Material _lit, _unlit, _palette;

        static Material LitSource => _lit != null ? _lit : _lit = Load("Materials/Lit");
        static Material UnlitSource => _unlit != null ? _unlit : _unlit = Load("Materials/Unlit");

        public static Material Sky => Load("Materials/Sky");

        /// <summary>What a surface is made of: picks the grain, the roughness and how metallic it is.</summary>
        public enum Matter
        {
            Painted,
            Metal,
            Burnt,
            Ground,
            Sand,
            Snow,
        }

        /// <summary>
        /// A surface with grain, bent normals and roughness (see the Surface shader). Falls back to
        /// the plain lit material if that shader did not make it into the build.
        /// </summary>
        public static Material Surface(Texture baseMap, Matter matter)
        {
            bool terrain = matter == Matter.Ground || matter == Matter.Sand || matter == Matter.Snow;
            var source = Resources.Load<Material>(terrain ? "Materials/SurfaceTerrain" : "Materials/Surface");
            if (source == null) return LitTextured(baseMap, matter == Matter.Metal ? 0.4f : 0.1f);

            var material = new Material(source);
            material.SetTexture("_BaseMap", baseMap);
            string detail;
            float tiling, metallic, smoothness, normal = 1f, strength = 0.8f;
            switch (matter)
            {
                case Matter.Metal: detail = "detail_metal"; tiling = 0.9f; metallic = 0.55f; smoothness = 0.50f; strength = 0.55f; break;
                case Matter.Burnt: detail = "detail_rust"; tiling = 0.8f; metallic = 0.25f; smoothness = 0.22f; strength = 0.9f; break;
                case Matter.Ground: detail = "detail_ground"; tiling = 0.16f; metallic = 0f; smoothness = 0.16f; strength = 0.7f; break;
                case Matter.Sand: detail = "detail_sand"; tiling = 0.14f; metallic = 0f; smoothness = 0.20f; strength = 0.75f; break;
                case Matter.Snow: detail = "detail_snow"; tiling = 0.12f; metallic = 0f; smoothness = 0.42f; strength = 0.6f; normal = 0.7f; break;
                default: detail = "detail_concrete"; tiling = 0.35f; metallic = 0f; smoothness = 0.22f; strength = 0.75f; break;
            }
            material.SetTexture("_Detail", Detail(detail));
            material.SetFloat("_Tiling", tiling);
            material.SetFloat("_Metallic", metallic);
            material.SetFloat("_Smoothness", smoothness);
            material.SetFloat("_DetailStrength", strength);
            material.SetFloat("_NormalStrength", normal);
            if (terrain)
            {
                material.SetTexture("_DetailSteep", Detail("detail_rock"));
                material.SetFloat("_SteepBlend", 1f);
                material.SetFloat("_Macro", 0.7f);
            }
            return material;
        }

        static Texture Detail(string name)
        {
            var texture = Resources.Load<Texture2D>("Textures/" + name);
            if (texture == null) Debug.LogError("[Skyforge] Missing texture " + name);
            return texture;
        }

        /// <summary>
        /// Soft sprites for smoke and fire, or null if the particle shader is missing. Additive
        /// particles add light (fire, sparks); the others cover what is behind them (smoke, dust).
        /// </summary>
        public static Material Particle(Texture sprite, bool additive)
        {
            var source = Resources.Load<Material>(additive ? "Materials/ParticleAdd" : "Materials/Particle");
            if (source == null) return null;
            var material = new Material(source);
            material.SetTexture("_MainTex", sprite);
            return material;
        }

        /// <summary>The sea. Falls back to a plain glossy material if the water shader did not make it into the build.</summary>
        public static Material Water
        {
            get
            {
                var water = Resources.Load<Material>("Materials/Water");
                return water != null ? new Material(water) : Lit(new Color(0.06f, 0.25f, 0.38f), 0.82f);
            }
        }

        /// <summary>The world map's surface; a plain textured material stands in if its shader is missing.</summary>
        public static Material Globe(Texture surface)
        {
            var globe = Resources.Load<Material>("Materials/Globe");
            if (globe == null) return LitTextured(surface);
            var material = new Material(globe);
            material.SetTexture("_BaseMap", surface);
            return material;
        }

        public static Material Lit(Color color, float smoothness = 0.1f)
        {
            var material = new Material(LitSource);
            material.SetColor("_BaseColor", color);
            material.SetFloat("_Smoothness", smoothness);
            return material;
        }

        public static Material LitTextured(Texture texture, float smoothness = 0.05f)
        {
            Material material = Lit(Color.white, smoothness);
            material.SetTexture("_BaseMap", texture);
            return material;
        }

        public static Material Unlit(Color color)
        {
            var material = new Material(UnlitSource);
            material.SetColor("_BaseColor", color);
            return material;
        }

        /// <summary>Shared by every mesh made with <see cref="MeshBuilder"/>.</summary>
        public static Material PaletteLit
        {
            get
            {
                if (_palette == null) _palette = Surface(Palette.CreateTexture(), Matter.Painted);
                return _palette;
            }
        }

        static Material _paletteBurnt;

        /// <summary>The shared palette after a fire: what a destroyed building or emplacement is drawn with.</summary>
        public static Material PaletteBurnt
        {
            get
            {
                if (_paletteBurnt == null)
                {
                    Texture2D source = Palette.CreateTexture(color =>
                    {
                        float light = ((Color)color).grayscale;
                        return (Color32)new Color(0.09f + light * 0.16f, 0.085f + light * 0.14f, 0.08f + light * 0.13f);
                    });
                    _paletteBurnt = Surface(source, Matter.Burnt);
                }
                return _paletteBurnt;
            }
        }

        static Material Load(string path)
        {
            var material = Resources.Load<Material>(path);
            if (material == null) Debug.LogError("[Skyforge] Missing material " + path);
            return material;
        }
    }
}
