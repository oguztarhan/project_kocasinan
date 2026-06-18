using System.Collections.Generic;
using System.Globalization;
using UnityEngine;

namespace BusJam
{
    /// <summary>
    /// Rasterises the EXACT Intake Entertainment wordmark (the INTAKE letters with the triangle-A, + ENTERTAINMENT +
    /// the rule) straight from the original SVG path data in intakeentertainment.html — into one crisp HD texture.
    /// NO dependency on the Vector Graphics package or its importer (which kept refusing to produce a usable sprite).
    /// Pure scanline even-odd polygon fill. Built once and cached. If anything throws, Get() returns null and the
    /// splash falls back to its built-from-parts logo, so it can never break.
    /// </summary>
    public static class BootIntakeLogo
    {
        const float VBX = 278f, VBY = 405f, VBW = 326f, VBH = 150f;   // SVG viewBox "278 405 326 150"

        const string INTAKE =
            "M293.80,474.00 L293.80,420.34 L305.04,420.34 L305.04,474.00 L293.80,474.00 Z M350.65,474.00 L327.27,432.67 Q327.96,438.69 327.96,442.35 L327.96,474.00 L317.97,474.00 L317.97,420.34 L330.81,420.34 L354.54,462.01 Q353.85,456.25 353.85,451.53 L353.85,420.34 L363.84,420.34 L363.84,474.00 L350.65,474.00 Z M401.03,429.02 L401.03,474.00 L389.79,474.00 L389.79,429.02 L372.46,429.02 L372.46,420.34 L418.40,420.34 L418.40,429.02 L401.03,429.02 Z " +
            "M522.91,474.00 L503.64,449.36 L497.01,454.43 L497.01,474.00 L485.77,474.00 L485.77,420.34 L497.01,420.34 L497.01,444.68 L521.19,420.34 L534.29,420.34 L511.36,443.03 L536.17,474.00 L522.91,474.00 Z M544.60,474.00 L544.60,420.34 L586.80,420.34 L586.80,429.02 L555.84,429.02 L555.84,442.51 L584.48,442.51 L584.48,451.19 L555.84,451.19 L555.84,465.31 L588.36,465.31 L588.36,474.00 L544.60,474.00 Z";

        const string TRIANGLE = "M449.9,418 L474.1,474 L425.7,474 Z";
        const string CUTOUT = "M442.5,440.2 L442.5,470.5 L464.7,455.3 Z";

        const string ENTERTAINMENT =
            "M289.99,520.00 L289.99,505.55 L301.35,505.55 L301.35,507.89 L293.02,507.89 L293.02,511.52 L300.73,511.52 L300.73,513.86 L293.02,513.86 L293.02,517.66 L301.77,517.66 L301.77,520.00 L289.99,520.00 Z M323.05,520.00 L316.76,508.87 Q316.94,510.49 316.94,511.48 L316.94,520.00 L314.25,520.00 L314.25,505.55 L317.71,505.55 L324.10,516.77 Q323.91,515.22 323.91,513.95 L323.91,505.55 L326.60,505.55 L326.60,520.00 L323.05,520.00 Z M346.21,507.89 L346.21,520.00 L343.19,520.00 L343.19,507.89 L338.52,507.89 L338.52,505.55 L350.89,505.55 L350.89,507.89 L346.21,507.89 Z M362.78,520.00 L362.78,505.55 L374.14,505.55 L374.14,507.89 L365.80,507.89 L365.80,511.52 L373.52,511.52 L373.52,513.86 L365.80,513.86 L365.80,517.66 L374.56,517.66 L374.56,520.00 L362.78,520.00 Z M396.97,520.00 L393.61,514.51 L390.07,514.51 L390.07,520.00 L387.04,520.00 L387.04,505.55 L394.26,505.55 Q396.85,505.55 398.25,506.67 Q399.66,507.78 399.66,509.86 Q399.66,511.38 398.79,512.48 Q397.93,513.58 396.47,513.93 L400.37,520.00 L396.97,520.00 Z M396.61,509.98 Q396.61,507.90 393.94,507.90 L390.07,507.90 L390.07,512.16 L394.02,512.16 Q395.30,512.16 395.95,511.59 Q396.61,511.02 396.61,509.98 Z M419.00,507.89 L419.00,520.00 L415.98,520.00 L415.98,507.89 L411.31,507.89 L411.31,505.55 L423.68,505.55 L423.68,507.89 L419.00,507.89 Z M445.78,520.00 L444.50,516.31 L438.99,516.31 L437.71,520.00 L434.69,520.00 L439.96,505.55 L443.53,505.55 L448.78,520.00 L445.78,520.00 Z M441.74,507.78 L441.68,508.00 Q441.58,508.37 441.43,508.84 Q441.29,509.32 439.67,514.03 L443.82,514.03 L442.40,509.88 L441.96,508.49 L441.74,507.78 Z M461.00,520.00 L461.00,505.55 L464.03,505.55 L464.03,520.00 L461.00,520.00 Z M485.89,520.00 L479.60,508.87 Q479.78,510.49 479.78,511.48 L479.78,520.00 L477.09,520.00 L477.09,505.55 L480.55,505.55 L486.94,516.77 Q486.75,515.22 486.75,513.95 L486.75,505.55 L489.44,505.55 L489.44,520.00 L485.89,520.00 Z M514.53,520.00 L514.53,511.24 Q514.53,510.95 514.53,510.65 Q514.54,510.35 514.63,508.10 Q513.90,510.85 513.55,511.94 L510.95,520.00 L508.80,520.00 L506.19,511.94 L505.09,508.10 Q505.22,510.47 505.22,511.24 L505.22,520.00 L502.53,520.00 L502.53,505.55 L506.58,505.55 L509.16,513.63 L509.39,514.41 L509.88,516.35 L510.53,514.03 L513.18,505.55 L517.21,505.55 L517.21,520.00 L514.53,520.00 Z M530.29,520.00 L530.29,505.55 L541.66,505.55 L541.66,507.89 L533.32,507.89 L533.32,511.52 L541.03,511.52 L541.03,513.86 L533.32,513.86 L533.32,517.66 L542.08,517.66 L542.08,520.00 L530.29,520.00 Z M563.35,520.00 L557.06,508.87 Q557.24,510.49 557.24,511.48 L557.24,520.00 L554.56,520.00 L554.56,505.55 L558.01,505.55 L564.40,516.77 Q564.22,515.22 564.22,513.95 L564.22,505.55 L566.90,505.55 L566.90,520.00 L563.35,520.00 Z M586.51,507.89 L586.51,520.00 L583.49,520.00 L583.49,507.89 L578.82,507.89 L578.82,505.55 L591.19,505.55 L591.19,507.89 L586.51,507.89 Z";

        static Sprite cached;

        public static Sprite Get()
        {
            if (cached != null) return cached;
            try
            {
                const int scale = 5;
                int W = Mathf.RoundToInt(VBW * scale), H = Mathf.RoundToInt(VBH * scale);
                var buf = new Color[W * H];
                Color ink = Hex("#F6F8FF"), amber = Hex("#FFC14D"), coral = Hex("#FF6B4A"), dark = Hex("#0A0B17");

                Fill(buf, W, H, scale, Parse(INTAKE), (x, y) => ink);

                var tri = Tex(Parse(TRIANGLE), scale);
                Rect tb = Bounds(tri);
                Fill(buf, W, H, tri, (x, y) =>
                    Color.Lerp(amber, coral, Mathf.Clamp01(((x - tb.x) / Mathf.Max(1f, tb.width) + (tb.yMax - y) / Mathf.Max(1f, tb.height)) * 0.5f)));

                Fill(buf, W, H, scale, Parse(CUTOUT), (x, y) => dark);
                Fill(buf, W, H, scale, Parse(ENTERTAINMENT), (x, y) => amber);
                DrawRule(buf, W, H, scale, amber);

                var tex = new Texture2D(W, H, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
                tex.SetPixels(buf); tex.Apply(false);
                cached = Sprite.Create(tex, new Rect(0, 0, W, H), new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect);
                return cached;
            }
            catch { return null; }
        }

        static Color Hex(string h) { ColorUtility.TryParseHtmlString(h, out var c); return c; }

        // ---- SVG path -> closed sub-polygons (viewBox coords; Q beziers flattened) ----
        static List<List<Vector2>> Parse(string d)
        {
            var subs = new List<List<Vector2>>();
            List<Vector2> cur = null; Vector2 pos = Vector2.zero;
            int i = 0, n = d.Length;
            float Num()
            {
                while (i < n && (d[i] == ' ' || d[i] == ',' || d[i] == '\n' || d[i] == '\t' || d[i] == '\r')) i++;
                int st = i;
                while (i < n) { char ch = d[i]; if ((ch >= '0' && ch <= '9') || ch == '.' || ch == '-' || ch == '+' || ch == 'e' || ch == 'E') i++; else break; }
                return i > st ? float.Parse(d.Substring(st, i - st), CultureInfo.InvariantCulture) : 0f;
            }
            while (i < n)
            {
                char c = d[i];
                if (c == 'M' || c == 'm') { i++; if (cur != null && cur.Count > 0) subs.Add(cur); cur = new List<Vector2>(); pos = new Vector2(Num(), Num()); cur.Add(pos); }
                else if (c == 'L' || c == 'l') { i++; pos = new Vector2(Num(), Num()); cur.Add(pos); }
                else if (c == 'Q' || c == 'q') { i++; Vector2 ct = new Vector2(Num(), Num()), e = new Vector2(Num(), Num()); for (int s = 1; s <= 12; s++) { float t = s / 12f, u = 1 - t; cur.Add(u * u * pos + 2 * u * t * ct + t * t * e); } pos = e; }
                else if (c == 'Z' || c == 'z') { i++; if (cur != null && cur.Count > 0) { subs.Add(cur); cur = null; } }
                else i++;
            }
            if (cur != null && cur.Count > 0) subs.Add(cur);
            return subs;
        }

        // transform viewBox sub-polygons to texture coords (y-flipped)
        static List<List<Vector2>> Tex(List<List<Vector2>> vb, int scale)
        {
            var outp = new List<List<Vector2>>(vb.Count);
            foreach (var s in vb)
            {
                var t = new List<Vector2>(s.Count);
                foreach (var p in s) t.Add(new Vector2((p.x - VBX) * scale, (VBH - (p.y - VBY)) * scale));
                outp.Add(t);
            }
            return outp;
        }

        static Rect Bounds(List<List<Vector2>> subs)
        {
            float minX = float.MaxValue, minY = float.MaxValue, maxX = float.MinValue, maxY = float.MinValue;
            foreach (var s in subs) foreach (var p in s) { minX = Mathf.Min(minX, p.x); minY = Mathf.Min(minY, p.y); maxX = Mathf.Max(maxX, p.x); maxY = Mathf.Max(maxY, p.y); }
            return new Rect(minX, minY, maxX - minX, maxY - minY);
        }

        static void Fill(Color[] buf, int W, int H, int scale, List<List<Vector2>> vb, System.Func<float, float, Color> colorAt) => Fill(buf, W, H, Tex(vb, scale), colorAt);

        // scanline even-odd polygon fill (sub-polygons already in texture coords)
        static void Fill(Color[] buf, int W, int H, List<List<Vector2>> subs, System.Func<float, float, Color> colorAt)
        {
            float minY = H, maxY = 0;
            foreach (var s in subs) foreach (var p in s) { minY = Mathf.Min(minY, p.y); maxY = Mathf.Max(maxY, p.y); }
            int y0 = Mathf.Max(0, Mathf.FloorToInt(minY)), y1 = Mathf.Min(H - 1, Mathf.CeilToInt(maxY));
            var xs = new List<float>();
            for (int y = y0; y <= y1; y++)
            {
                float yc = y + 0.5f; xs.Clear();
                foreach (var s in subs)
                    for (int i = 0; i < s.Count; i++)
                    {
                        Vector2 a = s[i], b = s[(i + 1) % s.Count];
                        if ((a.y <= yc && b.y > yc) || (b.y <= yc && a.y > yc)) xs.Add(a.x + (yc - a.y) / (b.y - a.y) * (b.x - a.x));
                    }
                xs.Sort();
                for (int k = 0; k + 1 < xs.Count; k += 2)
                {
                    int xa = Mathf.Max(0, Mathf.RoundToInt(xs[k])), xb = Mathf.Min(W - 1, Mathf.RoundToInt(xs[k + 1]));
                    for (int x = xa; x <= xb; x++) { int idx = y * W + x; var c = colorAt(x, y); buf[idx] = c.a >= 1f ? c : Blend(buf[idx], c); }
                }
            }
        }

        static void DrawRule(Color[] buf, int W, int H, int scale, Color amber)
        {
            float x0 = (289f - VBX) * scale, x1 = (289f + 303f - VBX) * scale;
            float yA = (VBH - (544f - VBY)) * scale, yB = (VBH - (547f - VBY)) * scale;
            int ya = Mathf.Max(0, Mathf.FloorToInt(Mathf.Min(yA, yB))), yb = Mathf.Min(H - 1, Mathf.CeilToInt(Mathf.Max(yA, yB)));
            for (int y = ya; y <= yb; y++)
                for (int x = Mathf.Max(0, (int)x0); x <= Mathf.Min(W - 1, (int)x1); x++)
                {
                    float u = (x - x0) / Mathf.Max(1f, x1 - x0);
                    var c = amber; c.a = Mathf.Sin(Mathf.Clamp01(u) * Mathf.PI) * 0.9f;
                    int idx = y * W + x; buf[idx] = Blend(buf[idx], c);
                }
        }

        static Color Blend(Color d, Color c)
        {
            float a = c.a + d.a * (1 - c.a);
            if (a <= 1e-4f) return new Color(0, 0, 0, 0);
            return new Color((c.r * c.a + d.r * d.a * (1 - c.a)) / a, (c.g * c.a + d.g * d.a * (1 - c.a)) / a, (c.b * c.a + d.b * d.a * (1 - c.a)) / a, a);
        }
    }
}
