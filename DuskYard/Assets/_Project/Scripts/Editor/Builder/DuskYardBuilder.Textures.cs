using System;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace DuskYard.EditorTools
{
    /// <summary>Procedural textures: tileable noise, straw, sigil/rune masks, particle sprites, decals.</summary>
    public static partial class DuskYardBuilder
    {
        enum TexKind { Color, Linear, Normal, Sprite, Decal }

        static void GenerateTextures()
        {
            // ---------------------------------------------------------------- tileable surfaces / masks
            Save("T_Noise_GrimeDissolve", 256, 256, TexKind.Linear, (u, v) =>
            {
                float r = Fbm(u, v, 4, 5, 11);
                float g = Fbm(u, v, 8, 4, 23);
                float b = ValueNoise(u * 32, v * 32, 32, 7);
                return new Color(r, g, b, 1f);
            });

            Func<float, float, float> strawHeight = (u, v) =>
            {
                float fibers = ValueNoise(u * 96f, v * 3f, 96, 3) * 0.65f + ValueNoise(u * 210f, v * 6f, 210, 5) * 0.35f;
                float clumps = Fbm(u, v, 3, 3, 9);
                return Mathf.Clamp01(fibers * 0.8f + clumps * 0.3f - 0.05f);
            };
            Save("T_Straw_BaseColor", 512, 512, TexKind.Color, (u, v) =>
            {
                float h = strawHeight(u, v);
                float tint = Fbm(u, v, 2, 3, 41);
                Color dark = new Color(0.30f, 0.22f, 0.10f), light = new Color(0.78f, 0.64f, 0.36f), old = new Color(0.52f, 0.47f, 0.36f);
                Color c = Color.Lerp(dark, light, SmoothEdge(0.15f, 0.85f, h));
                return Color.Lerp(c, old * (0.6f + 0.6f * h), SmoothEdge(0.45f, 0.75f, tint) * 0.6f);
            });
            SaveNormalFromHeight("T_Straw_Normal", 512, 512, strawHeight, 3.0f);

            Save("T_Sigil_Mask", 512, 512, TexKind.Linear, (u, v) =>
            {
                Vector2 p = new Vector2(u - 0.5f, v - 0.5f);
                float r = p.magnitude;
                float m = Band(r, 0.405f, 0.44f, 0.006f);
                m = Mathf.Max(m, Band(r, 0.30f, 0.315f, 0.005f));
                for (int i = 0; i < 3; i++)
                {
                    float a0 = Mathf.PI * 0.5f + i * Mathf.PI * 2f / 3f, a1 = a0 + Mathf.PI * 2f / 3f;
                    Vector2 s0 = new Vector2(Mathf.Cos(a0), Mathf.Sin(a0)) * 0.30f, s1 = new Vector2(Mathf.Cos(a1), Mathf.Sin(a1)) * 0.30f;
                    m = Mathf.Max(m, Stroke(p, s0, s1, 0.011f));
                }
                m = Mathf.Max(m, Stroke(p, new Vector2(0, -0.30f), new Vector2(0, 0.18f), 0.012f));
                m = Mathf.Max(m, Stroke(p, new Vector2(-0.09f, 0.06f), new Vector2(0.09f, 0.06f), 0.010f));
                for (int i = 0; i < 12; i++)
                {
                    float a = i * Mathf.PI / 6f + 0.26f;
                    Vector2 d = new Vector2(Mathf.Cos(a), Mathf.Sin(a));
                    m = Mathf.Max(m, Stroke(p, d * 0.445f, d * (i % 3 == 0 ? 0.49f : 0.47f), 0.007f));
                }
                float burn = 0.7f + 0.3f * Fbm(u, v, 12, 3, 77);
                return Gray(Mathf.Clamp01(m * burn));
            });

            Save("T_Rune_Mask", 256, 512, TexKind.Linear, (u, v) =>
            {
                // a column of five carved glyphs, each built from strokes between points of a 3x3 lattice
                const int glyphs = 5;
                float cell = (1f - 0.12f) / glyphs;
                float gv = (v - 0.06f) / cell;
                int gi = Mathf.FloorToInt(gv);
                if (gi < 0 || gi >= glyphs || u < 0.3f || u > 0.7f) return Gray(0f);
                Vector2 p = new Vector2((u - 0.3f) / 0.4f, gv - gi);
                var rng = new System.Random(1234 + gi * 97);
                float m = 0f;
                Vector2 Lattice(int k) => new Vector2(0.18f + (k % 3) * 0.32f, 0.12f + (k / 3) * 0.38f);
                m = Mathf.Max(m, Stroke(p, Lattice(1), Lattice(7), 0.045f)); // stave
                for (int s = 0; s < 3; s++)
                {
                    int a = rng.Next(9), b = rng.Next(9);
                    if (a == b) b = (b + 4) % 9;
                    m = Mathf.Max(m, Stroke(p, Lattice(a), Lattice(b), 0.04f));
                }
                return Gray(m * (0.8f + 0.2f * Fbm(u, v, 16, 2, 5)));
            });

            // ---------------------------------------------------------------- particle sprites (white, tinted in material / vertex color)
            Save("T_FX_SoftDot", 64, 64, TexKind.Sprite, (u, v) =>
            {
                float r2 = Sq(u - 0.5f) + Sq(v - 0.5f);
                return new Color(1, 1, 1, Mathf.Exp(-r2 * 28f) * Mathf.Clamp01(1f - Mathf.Sqrt(r2) * 2f) * 1.05f);
            });
            Save("T_FX_Flare", 128, 128, TexKind.Sprite, (u, v) =>
            {
                float x = u - 0.5f, y = v - 0.5f, r2 = x * x + y * y;
                float core = Mathf.Exp(-r2 * 120f);
                float halo = Mathf.Exp(-r2 * 18f) * 0.35f;
                float streakH = Mathf.Exp(-y * y * 4000f) * Mathf.Exp(-x * x * 9f);
                float streakV = Mathf.Exp(-x * x * 4000f) * Mathf.Exp(-y * y * 9f);
                float a = Mathf.Max(core, halo, (streakH + streakV) * 0.8f) * Mathf.Clamp01(1f - Mathf.Sqrt(r2) * 2f);
                return new Color(1, 1, 1, Mathf.Clamp01(a));
            });
            Save("T_FX_Dust", 128, 128, TexKind.Sprite, (u, v) =>
            {
                float r = Mathf.Sqrt(Sq(u - 0.5f) + Sq(v - 0.5f)) * 2f;
                float n = Fbm(u, v, 4, 4, 91);
                float a = SmoothEdge(1f, 0.35f, r + (n - 0.5f) * 0.55f) * (0.55f + 0.45f * n);
                return new Color(1, 1, 1, Mathf.Clamp01(a));
            });
            Save("T_FX_Fiber", 64, 64, TexKind.Sprite, (u, v) =>
            {
                float bend = Mathf.Sin(u * Mathf.PI) * 0.06f;
                float d = Mathf.Abs(v - 0.5f - bend);
                float taper = SmoothEdge(0f, 0.15f, u) * SmoothEdge(1f, 0.8f, u);
                return new Color(1, 1, 1, Mathf.Exp(-d * d * 1400f) * taper);
            });
            Save("T_FX_Trail", 256, 32, TexKind.Sprite, (u, v) =>
            {
                float along = Mathf.Pow(1f - u, 1.6f);
                float across = Mathf.Exp(-Sq((v - 0.5f) / 0.2f));
                float edge = Mathf.Exp(-Sq((v - 0.5f) / 0.06f)) * 0.6f;
                return new Color(1, 1, 1, Mathf.Clamp01(along * (across * 0.55f + edge)));
            });
            Save("T_FX_Flame", 128, 128, TexKind.Sprite, (u, v) =>
            {
                float y = Mathf.Clamp01((v - 0.08f) / 0.85f);
                float w = 0.23f * Mathf.Pow(Mathf.Sin(Mathf.PI * Mathf.Min(1f, y * 1.25f + 0.05f)), 0.7f) * (1f - y * 0.55f);
                float d = Mathf.Abs(u - 0.5f + Mathf.Sin(y * 6f) * 0.015f * y);
                float a = SmoothEdge(w, w * 0.35f, d) * SmoothEdge(0f, 0.08f, y) * SmoothEdge(1f, 0.7f, y);
                return new Color(1, 1, 1, Mathf.Clamp01(a));
            });

            // ---------------------------------------------------------------- decals
            Func<float, float, float> gashDepth = (u, v) =>
            {
                float wob = (ValueNoise(u * 9f, 0.5f, 64, 4) - 0.5f) * 0.12f;
                float w = 0.30f * Mathf.Pow(Mathf.Sin(Mathf.PI * u), 0.6f) + 0.001f;
                float fray = (ValueNoise(u * 64f, v * 6f, 64, 8) - 0.5f) * 0.35f;
                float d = Mathf.Abs(v - 0.5f - wob) / w + fray;
                return Mathf.Clamp01(1f - d);
            };
            Save("T_Decal_Gash", 256, 128, TexKind.Decal, (u, v) =>
            {
                float g = gashDepth(u, v);
                Color core = new Color(0.07f, 0.05f, 0.03f), rim = new Color(0.62f, 0.52f, 0.32f);
                Color c = Color.Lerp(rim, core, SmoothEdge(0.15f, 0.65f, g));
                float a = SmoothEdge(0f, 0.12f, g);
                return new Color(c.r, c.g, c.b, a);
            });
            SaveNormalFromHeight("T_Decal_Gash_Normal", 256, 128, (u, v) => -gashDepth(u, v), 4f, clamp: true);
            Save("T_Decal_Scuff", 256, 128, TexKind.Decal, (u, v) =>
            {
                float taper = Mathf.Pow(Mathf.Sin(Mathf.PI * u), 0.8f);
                float lines = Mathf.Max(0f, ValueNoise(u * 4f, v * 40f, 40, 31) - 0.35f) * 1.6f;
                float band = Mathf.Exp(-Sq((v - 0.5f) / 0.22f));
                float chips = SmoothEdge(0.62f, 0.75f, Fbm(u, v, 10, 3, 55)) * band;
                float a = Mathf.Clamp01((lines * band * taper) * 0.9f + chips * 0.7f);
                Color c = Color.Lerp(new Color(0.72f, 0.69f, 0.64f), new Color(0.2f, 0.18f, 0.16f), chips);
                return new Color(c.r, c.g, c.b, a);
            });
        }

        // -------------------------------------------------------------------------------- texture IO
        static void Save(string name, int w, int h, TexKind kind, Func<float, float, Color> f)
        {
            var tex = new Texture2D(w, h, TextureFormat.RGBA32, false, kind != TexKind.Color && kind != TexKind.Sprite && kind != TexKind.Decal);
            var px = new Color[w * h];
            for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
                px[y * w + x] = f((x + 0.5f) / w, (y + 0.5f) / h);
            tex.SetPixels(px);
            WritePng(name, tex, kind);
        }

        static void SaveNormalFromHeight(string name, int w, int h, Func<float, float, float> height, float strength, bool clamp = false)
        {
            var hs = new float[w * h];
            for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
                hs[y * w + x] = height((x + 0.5f) / w, (y + 0.5f) / h);
            float H(int x, int y)
            {
                if (clamp) { x = Mathf.Clamp(x, 0, w - 1); y = Mathf.Clamp(y, 0, h - 1); }
                else { x = (x + w) % w; y = (y + h) % h; }
                return hs[y * w + x];
            }
            var tex = new Texture2D(w, h, TextureFormat.RGBA32, false, true);
            var px = new Color[w * h];
            for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                float dx = (H(x + 1, y) - H(x - 1, y)) * strength;
                float dy = (H(x, y + 1) - H(x, y - 1)) * strength;
                Vector3 n = new Vector3(-dx, -dy, 1f).normalized;
                px[y * w + x] = new Color(n.x * 0.5f + 0.5f, n.y * 0.5f + 0.5f, n.z * 0.5f + 0.5f, 1f);
            }
            tex.SetPixels(px);
            WritePng(name, tex, TexKind.Normal, clamp);
        }

        static void WritePng(string name, Texture2D tex, TexKind kind, bool clampNormal = false)
        {
            string path = $"{TexDir}/{name}.png";
            File.WriteAllBytes(path, tex.EncodeToPNG());
            UnityEngine.Object.DestroyImmediate(tex);
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
            var imp = (TextureImporter)AssetImporter.GetAtPath(path);
            imp.textureType = kind == TexKind.Normal ? TextureImporterType.NormalMap : TextureImporterType.Default;
            imp.sRGBTexture = kind == TexKind.Color || kind == TexKind.Sprite || kind == TexKind.Decal;
            imp.alphaSource = kind == TexKind.Sprite || kind == TexKind.Decal ? TextureImporterAlphaSource.FromInput : TextureImporterAlphaSource.None;
            imp.alphaIsTransparency = kind == TexKind.Sprite || kind == TexKind.Decal;
            imp.wrapMode = kind == TexKind.Sprite || kind == TexKind.Decal || clampNormal ? TextureWrapMode.Clamp : TextureWrapMode.Repeat;
            imp.mipmapEnabled = true;
            imp.anisoLevel = kind == TexKind.Color || kind == TexKind.Normal ? 4 : 1;
            imp.textureCompression = TextureImporterCompression.CompressedHQ;
            imp.SaveAndReimport();
        }

        // -------------------------------------------------------------------------------- noise / shapes
        static float Sq(float x) => x * x;

        /// <summary>GLSL-style smoothstep(edge0, edge1, x). Note: Mathf.SmoothStep is an interpolation, not an edge function.</summary>
        static float SmoothEdge(float edge0, float edge1, float x)
        {
            float t = Mathf.Clamp01((x - edge0) / (edge1 - edge0));
            return t * t * (3f - 2f * t);
        }
        static Color Gray(float g) => new Color(g, g, g, 1f);

        static float Hash(int x, int y, int seed)
        {
            unchecked
            {
                uint h = (uint)(x * 374761393 + y * 668265263 + seed * 2147483647);
                h = (h ^ (h >> 13)) * 1274126177u;
                h ^= h >> 16;
                return (h & 0xFFFFFF) / 16777215f;
            }
        }

        /// <summary>Value noise that tiles with the given integer period.</summary>
        static float ValueNoise(float x, float y, int period, int seed)
        {
            int xi = Mathf.FloorToInt(x), yi = Mathf.FloorToInt(y);
            float xf = x - xi, yf = y - yi;
            int Wrap(int a) => ((a % period) + period) % period;
            float a00 = Hash(Wrap(xi), Wrap(yi), seed), a10 = Hash(Wrap(xi + 1), Wrap(yi), seed);
            float a01 = Hash(Wrap(xi), Wrap(yi + 1), seed), a11 = Hash(Wrap(xi + 1), Wrap(yi + 1), seed);
            float sx = xf * xf * (3f - 2f * xf), sy = yf * yf * (3f - 2f * yf);
            return Mathf.Lerp(Mathf.Lerp(a00, a10, sx), Mathf.Lerp(a01, a11, sx), sy);
        }

        static float Fbm(float u, float v, int baseFreq, int octaves, int seed)
        {
            float sum = 0f, amp = 0.5f, norm = 0f;
            int f = baseFreq;
            for (int o = 0; o < octaves; o++)
            {
                sum += ValueNoise(u * f, v * f, f, seed + o * 31) * amp;
                norm += amp;
                amp *= 0.5f;
                f *= 2;
            }
            return sum / norm;
        }

        static float Band(float r, float r0, float r1, float aa) => SmoothEdge(r0 - aa, r0 + aa, r) * (1f - SmoothEdge(r1 - aa, r1 + aa, r));

        static float Stroke(Vector2 p, Vector2 a, Vector2 b, float width)
        {
            Vector2 pa = p - a, ba = b - a;
            float t = Mathf.Clamp01(Vector2.Dot(pa, ba) / Mathf.Max(ba.sqrMagnitude, 1e-6f));
            float d = (pa - ba * t).magnitude;
            return 1f - SmoothEdge(width * 0.6f, width * 1.15f, d);
        }
    }
}
