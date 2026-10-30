using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering.HighDefinition;

namespace DuskYard.EditorTools
{
    /// <summary>
    /// HDRP materials. Values are physically motivated (albedo in the 0.04-0.8 range, emission in nits), so the
    /// scene behaves under physical light units and auto exposure.
    /// </summary>
    public static partial class DuskYardBuilder
    {
        const int UVBaseUV0 = 0, UVBasePlanar = 4, UVBaseTriplanar = 5;

        static void CreateMaterials()
        {
            ConfigureThirdPartyTextures();
            Texture2D T(string n) => Load<Texture2D>($"{ThirdPartyTex}/{n}");
            Texture2D G(string n) => Load<Texture2D>($"{TexDir}/{n}.png");

            // ------------------------------------------------------------ environment
            Lit("M_Cobblestone", "Environment", new Color(0.82f, 0.8f, 0.78f), T("cobblestones_d.png"), T("cobblestones_n.png"), null,
                smoothness: 0.42f, metallic: 0f, normalScale: 1.2f, uvBase: UVBasePlanar, worldScale: 0.55f);
            Lit("M_Puddle", "Environment", new Color(0.03f, 0.027f, 0.022f), null, null, null,
                smoothness: 0.93f, metallic: 0f, uvBase: UVBasePlanar, worldScale: 0.55f);
            Lit("M_Bricks", "Environment", new Color(0.78f, 0.74f, 0.7f), T("Bricks_BC.tif"), T("Bricks_N.tif"), T("Bricks_M.tif"),
                smoothness: 0.55f, metallic: 0f, uvBase: UVBaseTriplanar, worldScale: 0.6f);
            Lit("M_BricksDark", "Environment", new Color(0.5f, 0.47f, 0.45f), T("Bricks_BC.tif"), T("Bricks_N.tif"), T("Bricks_M.tif"),
                smoothness: 0.5f, metallic: 0f, uvBase: UVBaseTriplanar, worldScale: 0.6f);
            Lit("M_WoodPlanks", "Environment", new Color(0.62f, 0.55f, 0.48f), T("woodflooring_basecolor.png"), T("woodflooring_normal.png"), null,
                smoothness: 0.28f, metallic: 0f, uvBase: UVBaseTriplanar, worldScale: 0.6f);
            Lit("M_WoodDark", "Environment", new Color(0.32f, 0.26f, 0.21f), T("woodflooring_basecolor.png"), T("woodflooring_normal.png"), null,
                smoothness: 0.25f, metallic: 0f, uvBase: UVBaseTriplanar, worldScale: 1.2f);
            Lit("M_RoofShingles", "Environment", new Color(0.24f, 0.2f, 0.18f), T("woodflooring_basecolor.png"), T("woodflooring_normal.png"), null,
                smoothness: 0.2f, metallic: 0f, uvBase: UVBaseTriplanar, worldScale: 0.9f);
            Lit("M_Rock", "Environment", new Color(0.7f, 0.68f, 0.65f), T("Rock_BC.png"), null, null,
                smoothness: 0.3f, metallic: 0f, uvBase: UVBaseTriplanar, worldScale: 0.5f);
            Lit("M_Chest", "Environment", Color.white, T("Chest_BC.png"), T("Chest_N.png"), T("Chest_M.png"), smoothness: 1f, metallic: 1f);
            Lit("M_StrawBale", "Environment", new Color(0.85f, 0.8f, 0.75f), G("T_Straw_BaseColor"), G("T_Straw_Normal"), null,
                smoothness: 0.18f, metallic: 0f, uvBase: UVBaseTriplanar, worldScale: 1.4f);
            Lit("M_IronDark", "Environment", new Color(0.22f, 0.2f, 0.19f), null, null, null, smoothness: 0.38f, metallic: 0.9f);
            var coals = Lit("M_EmberCoals", "Environment", new Color(0.05f, 0.04f, 0.035f), null, null, null, smoothness: 0.3f, metallic: 0f);
            HDMaterial.SetUseEmissiveIntensity(coals, true);
            HDMaterial.SetEmissiveColor(coals, new Color(1f, 0.32f, 0.08f));
            HDMaterial.SetEmissiveIntensity(coals, 1400f, EmissiveIntensityUnit.Nits);
            HDMaterial.ValidateMaterial(coals);

            // ------------------------------------------------------------ characters / weapon
            Lit("M_Steel", "Characters", new Color(0.56f, 0.57f, 0.58f), null, null, null, smoothness: 0.74f, metallic: 1f);
            Lit("M_Leather", "Characters", new Color(0.24f, 0.15f, 0.09f), null, T("Leather_N.tif"), null, smoothness: 0.35f, metallic: 0f, uvBase: UVBaseTriplanar, worldScale: 4f);
            Lit("M_Tunic", "Characters", new Color(0.30f, 0.07f, 0.05f), null, T("Leather_N.tif"), null, smoothness: 0.18f, metallic: 0f, normalScale: 0.4f, uvBase: UVBaseTriplanar, worldScale: 3f);
            Lit("M_Trousers", "Characters", new Color(0.12f, 0.11f, 0.1f), null, null, null, smoothness: 0.15f, metallic: 0f);
            Lit("M_Skin", "Characters", new Color(0.6f, 0.43f, 0.34f), null, null, null, smoothness: 0.42f, metallic: 0f);
            Lit("M_Hair", "Characters", new Color(0.09f, 0.06f, 0.045f), null, null, null, smoothness: 0.3f, metallic: 0f);

            // ------------------------------------------------------------ Shader Graph presets (SG_DissolveEmissiveLit)
            var sg = Load<Shader>($"{ShaderDir}/SG_DissolveEmissiveLit.shadergraph");
            var noise = G("T_Noise_GrimeDissolve");

            // Preset A - "Straw Dummy": no base emission, heavy grime, warm hit flash
            SgPreset("M_SG_Straw_Dummy", sg, m =>
            {
                m.SetTexture("_BaseMap", G("T_Straw_BaseColor"));
                m.SetTexture("_NormalMap", G("T_Straw_Normal"));
                m.SetColor("_BaseColor", new Color(0.85f, 0.83f, 0.78f));
                m.SetFloat("_NormalStrength", 1.2f);
                m.SetFloat("_Smoothness", 0.2f);
                m.SetTexture("_NoiseMap", noise);
                m.SetFloat("_TriplanarTiling", 1.6f);
                m.SetColor("_GrimeColor", new Color(0.32f, 0.27f, 0.2f));
                m.SetFloat("_GrimeAmount", 0.6f);
                m.SetFloat("_EmissionIntensity", 0f);
                m.SetColor("_HitFlashColor", new Color(1f, 0.7f, 0.42f) * 90f);
                m.SetColor("_DissolveEdgeColor", new Color(1f, 0.35f, 0.08f) * 1500f);
            });

            // Preset B - "Ember Sigil": carved ward plaque, pulsing ember glyph, burn-out / re-forming via dissolve
            SgPreset("M_SG_Ember_Sigil", sg, m =>
            {
                m.SetTexture("_BaseMap", T("woodflooring_basecolor.png"));
                m.SetTexture("_NormalMap", T("woodflooring_normal.png"));
                m.SetColor("_BaseColor", new Color(0.36f, 0.28f, 0.22f));
                m.SetFloat("_Smoothness", 0.25f);
                m.SetTexture("_NoiseMap", noise);
                m.SetFloat("_TriplanarTiling", 3f);
                m.SetColor("_GrimeColor", new Color(0.12f, 0.1f, 0.08f));
                m.SetFloat("_GrimeAmount", 0.45f);
                m.SetTexture("_EmissionMask", G("T_Sigil_Mask"));
                m.SetColor("_EmissionColor", new Color(1f, 0.38f, 0.09f));
                m.SetFloat("_EmissionIntensity", 650f);
                m.SetFloat("_PulseSpeed", 2.4f);
                m.SetFloat("_PulseAmount", 0.35f);
                m.SetFloat("_DissolveEdgeWidth", 0.12f);
                m.SetColor("_DissolveEdgeColor", new Color(1f, 0.42f, 0.1f) * 1400f);
                m.SetColor("_HitFlashColor", Color.black);
            });

            // Preset C - "Waystone Runes": environment stone, cold faint runes, moss grime, geometry-clipping dissolve off
            SgPreset("M_SG_Waystone_Runes", sg, m =>
            {
                m.SetTexture("_BaseMap", T("Rock_BC.png"));
                m.SetColor("_BaseColor", new Color(0.62f, 0.62f, 0.6f));
                m.SetFloat("_Smoothness", 0.32f);
                m.SetTexture("_NoiseMap", noise);
                m.SetFloat("_TriplanarTiling", 0.9f);
                m.SetColor("_GrimeColor", new Color(0.17f, 0.21f, 0.11f));
                m.SetFloat("_GrimeAmount", 0.75f);
                m.SetTexture("_EmissionMask", G("T_Rune_Mask"));
                m.SetColor("_EmissionColor", new Color(0.3f, 0.68f, 1f));
                m.SetFloat("_EmissionIntensity", 90f);
                m.SetFloat("_PulseSpeed", 0.7f);
                m.SetFloat("_PulseAmount", 0.55f);
                m.SetColor("_HitFlashColor", new Color(0.6f, 0.85f, 1f) * 400f);
            });

            // ------------------------------------------------------------ particles
            var add = Load<Shader>($"{ShaderDir}/SG_ParticleAdditive.shadergraph");
            var alpha = Load<Shader>($"{ShaderDir}/SG_ParticleAlpha.shadergraph");
            Particle("M_FX_ContactFlash", add, G("T_FX_Flare"), new Color(1f, 0.82f, 0.62f), 1600f, 0.05f);
            Particle("M_FX_Glint", add, G("T_FX_Flare"), new Color(0.85f, 0.92f, 1f), 1100f, 0.02f);
            Particle("M_FX_Spark", add, G("T_FX_SoftDot"), new Color(1f, 0.6f, 0.25f), 3000f, 0.02f);
            Particle("M_FX_Trail", add, G("T_FX_Trail"), new Color(1f, 0.9f, 0.78f), 260f, 0.05f);
            Particle("M_FX_Flame", add, G("T_FX_Flame"), new Color(1f, 0.45f, 0.13f), 2200f, 0.1f);
            Particle("M_FX_Ember", add, G("T_FX_SoftDot"), new Color(1f, 0.4f, 0.1f), 2500f, 0.02f);
            Particle("M_FX_StrawFiber", alpha, G("T_FX_Fiber"), new Color(0.78f, 0.64f, 0.38f), 110f, 0.02f);
            Particle("M_FX_Dust", alpha, G("T_FX_Dust"), new Color(0.55f, 0.48f, 0.38f), 70f, 0.3f);
            Particle("M_FX_StoneDust", alpha, G("T_FX_Dust"), new Color(0.6f, 0.58f, 0.55f), 70f, 0.3f);
            Particle("M_FX_StoneChip", alpha, G("T_FX_SoftDot"), new Color(0.3f, 0.29f, 0.28f), 60f, 0.02f);
            Particle("M_FX_Smoke", alpha, G("T_FX_Dust"), new Color(0.16f, 0.15f, 0.14f), 45f, 0.4f);

            // ------------------------------------------------------------ decals
            Decal("M_Decal_StrawGash", G("T_Decal_Gash"), G("T_Decal_Gash_Normal"), Color.white);
            Decal("M_Decal_WoodGash", G("T_Decal_Gash"), G("T_Decal_Gash_Normal"), new Color(0.75f, 0.62f, 0.5f));
            Decal("M_Decal_StoneScuff", G("T_Decal_Scuff"), null, Color.white);
        }

        static void ConfigureThirdPartyTextures()
        {
            foreach (var guid in AssetDatabase.FindAssets("t:Texture2D", new[] { ThirdPartyTex }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                var imp = (TextureImporter)AssetImporter.GetAtPath(path);
                string file = System.IO.Path.GetFileNameWithoutExtension(path).ToLowerInvariant();
                bool normal = file.EndsWith("_n") || file.Contains("normal");
                bool linear = file.EndsWith("_m") || file.EndsWith("_h") || file.Contains("output");
                var type = normal ? TextureImporterType.NormalMap : TextureImporterType.Default;
                bool srgb = !normal && !linear;
                if (imp.textureType == type && imp.sRGBTexture == srgb && imp.maxTextureSize == 2048) continue;
                imp.textureType = type;
                imp.sRGBTexture = srgb;
                imp.maxTextureSize = 2048;
                imp.anisoLevel = 4;
                imp.SaveAndReimport();
            }
        }

        static Material GetOrCreateMaterial(string path, Shader shader)
        {
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m) { m.shader = shader; return m; }
            m = new Material(shader);
            AssetDatabase.CreateAsset(m, path);
            return m;
        }

        public static Material Lit(string name, string folder, Color color, Texture2D baseMap, Texture2D normal, Texture2D mask,
            float smoothness, float metallic, float normalScale = 1f, int uvBase = UVBaseUV0, float worldScale = 1f)
        {
            var m = GetOrCreateMaterial($"{MatDir}/{folder}/{name}.mat", Shader.Find("HDRP/Lit"));
            m.SetColor("_BaseColor", color);
            m.SetTexture("_BaseColorMap", baseMap);
            m.SetTexture("_NormalMap", normal);
            m.SetFloat("_NormalScale", normalScale);
            m.SetTexture("_MaskMap", mask);
            m.SetFloat("_Metallic", metallic);
            m.SetFloat("_Smoothness", smoothness);
            if (mask)
            {
                m.SetFloat("_SmoothnessRemapMin", 0f);
                m.SetFloat("_SmoothnessRemapMax", smoothness);
                m.SetFloat("_MetallicRemapMax", metallic);
            }
            m.SetFloat("_UVBase", uvBase);
            m.SetFloat("_TexWorldScale", worldScale);
            HDMaterial.ValidateMaterial(m);
            EditorUtility.SetDirty(m);
            return m;
        }

        static void SgPreset(string name, Shader sg, System.Action<Material> setup)
        {
            var m = GetOrCreateMaterial($"{MatDir}/ShaderGraphPresets/{name}.mat", sg);
            setup(m);
            HDMaterial.ValidateMaterial(m);
            EditorUtility.SetDirty(m);
        }

        static void Particle(string name, Shader shader, Texture2D tex, Color tint, float nits, float softDistance)
        {
            var m = GetOrCreateMaterial($"{MatDir}/VFX/{name}.mat", shader);
            m.SetTexture("_MainTex", tex);
            m.SetColor("_TintColor", tint);
            m.SetFloat("_Intensity", nits);
            m.SetFloat("_SoftDistance", softDistance);
            HDMaterial.ValidateMaterial(m);
            EditorUtility.SetDirty(m);
        }

        static void Decal(string name, Texture2D baseMap, Texture2D normal, Color color)
        {
            var m = GetOrCreateMaterial($"{MatDir}/Decals/{name}.mat", Shader.Find("HDRP/Decal"));
            m.SetTexture("_BaseColorMap", baseMap);
            m.SetColor("_BaseColor", color);
            m.SetTexture("_NormalMap", normal);
            m.SetFloat("_AffectAlbedo", 1f);
            m.SetFloat("_AffectNormal", normal ? 1f : 0f);
            m.SetFloat("_AffectMetal", 0f);
            m.SetFloat("_AffectSmoothness", 1f);
            m.SetFloat("_NormalBlendSrc", 1f); // normal opacity from albedo alpha
            m.SetFloat("_DecalBlend", 1f);
            m.enableInstancing = true;
            HDMaterial.ValidateMaterial(m);
            EditorUtility.SetDirty(m);
        }
    }
}
