using System.Collections.Generic;
using UnityEngine;

namespace BusJam
{
    /// <summary>
    /// Generates skin pattern textures in code (no art files). Each pattern is drawn in the skin's accent colour on a
    /// fully TRANSPARENT background, so when it's laid over a vehicle the body's gameplay match-colour shows through
    /// every gap — the colour stays readable. Cached per (pattern, colour).
    /// </summary>
    public static class SkinTextureFactory
    {
        const int S = 128;
        static readonly Dictionary<string, Texture2D> cache = new Dictionary<string, Texture2D>();

        public static Texture2D Get(SkinPattern p, Color accent)
        {
            string key = (int)p + "_" + ColorUtility.ToHtmlStringRGB(accent);
            if (cache.TryGetValue(key, out var t) && t != null) return t;
            t = Build(p, accent);
            cache[key] = t;
            return t;
        }

        // A white radial ray-burst (fades out toward the edge) for the chest-reveal effect; tint it via the renderer.
        static Texture2D _rays;
        public static Texture2D Rays()
        {
            if (_rays != null) return _rays;
            var tex = new Texture2D(S, S, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
            var px = new Color32[S * S];
            const int spokes = 12;
            for (int y = 0; y < S; y++)
                for (int x = 0; x < S; x++)
                {
                    float dx = (x + 0.5f) / S - 0.5f, dy = (y + 0.5f) / S - 0.5f;
                    float r = Mathf.Sqrt(dx * dx + dy * dy);
                    float ang = Mathf.Atan2(dy, dx) / (2f * Mathf.PI) + 0.5f;
                    bool on = Frac(ang * spokes) < 0.5f;
                    byte alpha = (on && r < 0.5f) ? (byte)(Mathf.Clamp01(1f - r * 1.7f) * 255f) : (byte)0;
                    px[y * S + x] = new Color32(255, 255, 255, alpha);
                }
            tex.SetPixels32(px); tex.Apply(false, false);
            _rays = tex;
            return tex;
        }

        static Texture2D Build(SkinPattern p, Color accent)
        {
            var tex = new Texture2D(S, S, TextureFormat.RGBA32, false)
            { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
            var px = new Color32[S * S];
            var on  = new Color32((byte)(accent.r * 255), (byte)(accent.g * 255), (byte)(accent.b * 255), 255);
            var off = new Color32(on.r, on.g, on.b, 0); // same RGB, alpha 0 -> no colour bleed at edges
            for (int y = 0; y < S; y++)
                for (int x = 0; x < S; x++)
                {
                    float u = (x + 0.5f) / S, v = (y + 0.5f) / S;
                    px[y * S + x] = PatternOn(p, u, v) ? on : off;
                }
            tex.SetPixels32(px);
            tex.Apply(false, false);
            return tex;
        }

        // u = across the vehicle width, v = along its length. true => draw the accent here.
        static bool PatternOn(SkinPattern p, float u, float v)
        {
            switch (p)
            {
                case SkinPattern.Stripes: return Mathf.Abs(u - 0.36f) < 0.06f || Mathf.Abs(u - 0.64f) < 0.06f;
                case SkinPattern.Checker: return ((int)(u * 6) + (int)(v * 6)) % 2 == 0;
                case SkinPattern.Dots:    { float cu = Frac(u * 5) - 0.5f, cv = Frac(v * 5) - 0.5f; return cu * cu + cv * cv < 0.085f; }
                case SkinPattern.Hazard:  return Frac((u + v) * 4f) < 0.5f;
                case SkinPattern.Halves:  return v > 0.52f;
                case SkinPattern.Grid:    return Frac(u * 6) < 0.12f || Frac(v * 6) < 0.12f;
                case SkinPattern.Camo:    { float f = Mathf.Sin(u * 8.5f) * Mathf.Cos(v * 7.3f) + Mathf.Sin((u + v) * 11.7f) * 0.6f + Mathf.Sin((u - v) * 9.1f) * 0.4f; return f > 0.25f; }
                case SkinPattern.Flames:  { float h = 0.30f + 0.26f * Mathf.Abs(Mathf.Sin(u * 12.5f)) + 0.06f * Mathf.Sin(u * 31f); return v < h; }
                case SkinPattern.Star:    { float du = u - 0.5f, dv = v - 0.5f; float r = Mathf.Sqrt(du * du + dv * dv); float ang = Mathf.Atan2(dv, du); return r < 0.18f + 0.11f * Mathf.Cos(5f * ang); }
                case SkinPattern.Carbon:  return ((int)(u * 14) + (int)(v * 14)) % 2 == 0;
                case SkinPattern.Bolt:    { float bx = 0.5f + 0.16f * (Mathf.PingPong(v * 4f, 1f) - 0.5f) * 2f; return Mathf.Abs(u - bx) < 0.055f; }
                default: return false;
            }
        }

        static float Frac(float x) => x - Mathf.Floor(x);
    }
}
