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
                if (_palette == null) _palette = LitTextured(Palette.CreateTexture(), 0.25f);
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
                    _paletteBurnt = LitTextured(source, 0.02f);
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
