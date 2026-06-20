using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace BusJam
{
    /// <summary>
    /// A port of the &lt;canvas&gt; in loadingscreenfinal.html — the synthwave beach + traffic scene — rendered as
    /// TWO layers so it stays crisp AND cheap:
    ///   • STATIC backdrop (sky/sun/skyline/clouds/ocean/beach/palms/road/light-pole) is rasterised ONCE at full
    ///     device resolution into `staticTex` and shown on a full-screen RawImage. Never re-uploaded => HD, no cost.
    ///   • DYNAMIC layer (the moving cars + the 3 traffic-light lamps) is drawn every frame into a small `dynTex`
    ///     that only covers the road band, overlaid on top. A fraction of the screen => cheap per-frame upload.
    /// Both are textures stretched to their rects, so everything lives in one self-consistent pixel space and lines
    /// up at any aspect ratio. UNSCALED time. Set up + driven entirely by BootSplash; delete with it.
    /// </summary>
    public class BootCanvasScene : MonoBehaviour
    {
        // The road band (canvas coords, y DOWN: 0 = top of screen) that the dynamic layer covers.
        const float BandTopF = 0.40f, BandBotF = 0.96f;

        RawImage staticImg, dynImg;
        Texture2D staticTex, dynTex;
        Color[] statik, dyn;
        int Ws, Hs;                 // static (HD) dimensions
        int Wd, Hd;                 // dynamic (road-band) dimensions
        float ds, bandTopPx;        // dynamic scale (Wd/Ws) + band top in static px
        float horizon, oceanB, roadTop, roadBot, sunX, sunY, sunR;
        float lastT, acc;

        class Car { public float x, y, w, h; public bool bus; public Color col; }
        class Lane { public List<Car> cars; public float y, h, speed, osc, phase; }
        List<Lane> lanes;

        static readonly Color[] CARS = { C("#FF5B5B"), C("#4D9DFF"), C("#FFC14D"), C("#34D399"), C("#FF8A3C"), C("#A855F7"), C("#F472B6"), C("#22D3EE") };
        static readonly Color BUS = C("#FF6B4A");
        static Color C(string h) { ColorUtility.TryParseHtmlString(h, out var c); return c; }

        public void Setup(RawImage background)
        {
            staticImg = background;

            // ---- HD static backdrop: render at (near) the device's real pixel width so it isn't upscaled/soft on
            // high-DPI phones; match the device aspect so the stretch never distorts; cap for memory. ----
            Ws = Mathf.Clamp(Screen.width, 1080, 1440);
            Hs = Mathf.Clamp(Mathf.RoundToInt(Ws * (float)Screen.height / Mathf.Max(1, Screen.width)), 1300, 2880);
            horizon = Hs * 0.40f; oceanB = Hs * 0.52f; roadTop = Hs * 0.56f; roadBot = Hs * 0.93f;
            sunX = Ws * 0.62f; sunY = horizon - Ws * 0.04f; sunR = Ws * 0.15f;

            statik = new Color[Ws * Hs];
            BuildStatic();
            staticTex = new Texture2D(Ws, Hs, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
            staticTex.SetPixels(statik); staticTex.Apply(false);
            staticImg.texture = staticTex;
            statik = null;          // backdrop is uploaded — free the big managed buffer

            // ---- dynamic road-band layer (small, redrawn per frame), anchored to the band by screen fraction ----
            bandTopPx = BandTopF * Hs;
            Wd = Mathf.Clamp(Mathf.RoundToInt(Ws * 0.85f), 720, 1120);
            ds = Wd / (float)Ws;
            Hd = Mathf.Max(1, Mathf.RoundToInt((BandBotF - BandTopF) * Hs * ds));
            dyn = new Color[Wd * Hd];
            dynTex = new Texture2D(Wd, Hd, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };

            var dgo = new GameObject("RoadDynamic", typeof(RectTransform), typeof(RawImage));
            dgo.transform.SetParent(staticImg.rectTransform, false);
            var drt = dgo.GetComponent<RectTransform>();
            drt.anchorMin = new Vector2(0f, 1f - BandBotF);     // UGUI y is UP, so flip the canvas-down band
            drt.anchorMax = new Vector2(1f, 1f - BandTopF);
            drt.offsetMin = Vector2.zero; drt.offsetMax = Vector2.zero;
            dynImg = dgo.GetComponent<RawImage>(); dynImg.raycastTarget = false; dynImg.texture = dynTex;

            InitMoving();
            lastT = Time.unscaledTime;
            DrawDynamic(0f); ApplyDyn();
        }

        void Update()
        {
            acc += Time.unscaledDeltaTime;
            if (acc < 0.033f) return;                 // ~30fps redraw of the (small) dynamic layer
            float t = Time.unscaledTime, dt = t - lastT; lastT = t; acc = 0f;
            DrawDynamic(dt); ApplyDyn();
        }

        void ApplyDyn() { dynTex.SetPixels(dyn); dynTex.Apply(false); }

        // ================= STATIC backdrop (built once, full HD) =================
        void BuildStatic()
        {
            for (int i = 0; i < statik.Length; i++) statik[i] = new Color(0, 0, 0, 1);
            Sky(); Sun(); Skyline(); Clouds(); Ocean(); Beach(); Palms(); Road(); PoleStatic();
        }

        void Sky()
        {
            float[] ps = { 0f, 0.4f, 0.72f, 1f };
            Color[] cs = { C("#3a2a82"), C("#a3429a"), C("#FF6F8E"), C("#FFC169") };
            for (int cy = 0; cy <= horizon + 2; cy++)
            {
                Color c = EvalStops(cs, ps, Mathf.Clamp01(cy / horizon));
                for (int cx = 0; cx < Ws; cx++) SetS(cx, cy, c);
            }
        }

        void Sun()
        {
            RadialS(sunX, sunY, sunR * 2.6f, new Color(1f, 0.78f, 0.47f, 0.7f));    // glow
            float[] ps = { 0f, 0.55f, 1f };
            Color[] cs = { C("#FFE773"), C("#FF9E5A"), C("#FF5E86") };
            int y0 = (int)(sunY - sunR), y1 = (int)(sunY + sunR);
            for (int cy = y0; cy <= y1; cy++)
                for (int cx = (int)(sunX - sunR); cx <= sunX + sunR; cx++)
                {
                    float dx = cx - sunX, dy = cy - sunY;
                    if (dx * dx + dy * dy <= sunR * sunR) SetS(cx, cy, EvalStops(cs, ps, Mathf.Clamp01((cy - y0) / (2f * sunR))));
                }
            Color sl = C("#a3429a");                                                // scanlines
            for (int i = 0; i < 5; i++) FillRectS((int)(sunX - sunR), (int)(sunY + sunR * 0.2f + i * sunR * 0.16f), (int)(sunR * 2), Mathf.Max(1, (int)(sunR * 0.05f)), sl);
        }

        void Skyline()
        {
            Color b = C("#5e2a63"), w = new Color(1f, 0.70f, 0.59f, 0.25f);
            float bx = 0; int seed = 7;
            while (bx < Ws)
            {
                seed = (seed * 1103515245 + 12345) & 0x7fffffff; float r1 = (seed % 1000) / 1000f;
                seed = (seed * 1103515245 + 12345) & 0x7fffffff; float r2 = (seed % 1000) / 1000f;
                float bw = (16 + r1 * 30) * (Ws / 380f), bh = (20 + r2 * 60) * (Ws / 380f);
                FillRectS((int)bx, (int)(horizon - bh), (int)bw, (int)bh, b);
                if (r1 > 0.5f) FillRectS((int)(bx + bw * 0.3f), (int)(horizon - bh * 0.7f), Mathf.Max(1, (int)(2 * Ws / 380f)), (int)(bh * 0.5f), w);
                seed = (seed * 1103515245 + 12345) & 0x7fffffff;
                bx += bw + (2 + (seed % 1000) / 1000f * 8) * (Ws / 380f);
            }
        }

        void Clouds()
        {
            int seed = 999;
            for (int i = 0; i < 5; i++)
            {
                seed = (seed * 1103515245 + 12345) & 0x7fffffff; float fx = (seed % 1000) / 1000f;
                float cx = fx * Ws, cy = Hs * (0.07f + ((i * 53) % 16) / 100f), s = 0.7f + ((i * 71) % 70) / 100f;
                float r = 26 * (Ws / 380f) * s;
                Color body = new Color(1f, 0.70f, 0.67f, 0.55f), top = new Color(1f, 0.88f, 0.75f, 0.4f);
                DiscS(cx, cy, r, body); DiscS(cx + r * 0.9f, cy + r * 0.1f, r * 0.75f, body); DiscS(cx - r * 0.9f, cy + r * 0.15f, r * 0.65f, body);
                DiscS(cx, cy - r * 0.3f, r * 0.7f, top);
            }
        }

        void Ocean()
        {
            for (int cy = (int)horizon; cy < oceanB; cy++)
            {
                Color c = Color.Lerp(C("#2ea6c4"), C("#134e6b"), (cy - horizon) / (oceanB - horizon));
                for (int cx = 0; cx < Ws; cx++) SetS(cx, cy, c);
            }
            for (float yy = horizon; yy < oceanB; yy += Mathf.Max(1, Hs * 0.009f))     // sun reflection
            {
                float f = (yy - horizon) / (oceanB - horizon);
                float ww = sunR * 1.7f * (0.35f + 0.65f * (1 - f));
                FillRectS((int)(sunX - ww / 2), (int)yy, (int)ww, Mathf.Max(1, (int)(Hs * 0.004f)), new Color(1f, 0.82f, 0.48f, 0.3f * (1 - f)));
            }
            for (int i = 0; i < 4; i++)                                                // wave lines
            {
                int yy = (int)(horizon + (oceanB - horizon) * (0.25f + i * 0.2f));
                FillRectS(0, yy, Ws, Mathf.Max(1, (int)(Hs * 0.0015f)), new Color(1, 1, 1, 0.22f));
            }
        }

        void Beach()
        {
            for (int cy = (int)oceanB; cy <= roadTop + 2; cy++)
            {
                Color c = Color.Lerp(C("#f0dcab"), C("#d8c08a"), (cy - oceanB) / (roadTop - oceanB));
                for (int cx = 0; cx < Ws; cx++) SetS(cx, cy, c);
            }
        }

        void Palms()
        {
            Color col = C("#14202c");
            float x = Ws * 0.07f; int n = 0;
            while (x < Ws)
            {
                float s = (n % 2 == 1 ? 1.35f : 0.95f) + ((n * 53) % 20) / 100f;
                Palm(x, roadTop - 2, s, col);
                x += Ws * (0.17f + ((n * 37) % 10) / 100f); n++;
            }
        }

        void Palm(float x, float by, float s, Color col)
        {
            float u = Ws / 380f, lw = 6 * u * s;
            float tx = x + 2 * u * s, ty = by - 98 * u * s;
            for (float p = 0; p <= 1f; p += 0.02f)                                     // trunk (quadratic)
            {
                float qx = Mathf.Lerp(Mathf.Lerp(x, x + 8 * u * s, p), Mathf.Lerp(x + 8 * u * s, tx, p), p);
                float qy = Mathf.Lerp(Mathf.Lerp(by, by - 52 * u * s, p), Mathf.Lerp(by - 52 * u * s, ty, p), p);
                DiscS(qx, qy, lw * 0.5f, col);
            }
            for (int a = 0; a < 7; a++)                                                // fronds
            {
                float ang = -Mathf.PI / 2 + (a - 3) * 0.46f;
                for (float p = 0; p <= 1f; p += 0.03f)
                {
                    float ex = Mathf.Lerp(Mathf.Lerp(tx, tx + Mathf.Cos(ang) * 34 * u * s, p), Mathf.Lerp(tx + Mathf.Cos(ang) * 34 * u * s, tx + Mathf.Cos(ang) * 66 * u * s, p), p);
                    float ey = Mathf.Lerp(Mathf.Lerp(ty, ty + Mathf.Sin(ang) * 14 * u * s, p), Mathf.Lerp(ty + Mathf.Sin(ang) * 14 * u * s, ty + Mathf.Sin(ang) * 34 * u * s, p), p);
                    DiscS(ex, ey, 2.5f * u * s, col);
                }
            }
            DiscS(tx, ty, 6 * u * s, col);
        }

        void Road()
        {
            FillRectS(0, (int)roadTop, Ws, (int)(roadBot - roadTop), C("#2a2238"));
            FillRectS(0, (int)roadTop, Ws, Mathf.Max(1, (int)(Hs * 0.0045f)), C("#1c1729"));
            FillRectS(0, (int)(roadBot - Hs * 0.0045f), Ws, Mathf.Max(1, (int)(Hs * 0.0045f)), C("#1c1729"));
            float[] ys = { Hs * 0.635f, Hs * 0.755f, Hs * 0.865f };                    // dashed lane lines
            Color d = new Color(1f, 0.86f, 0.59f, 0.45f);
            for (int i = 1; i < 3; i++)
            {
                int ly = (int)((ys[i - 1] + ys[i]) / 2);
                int dash = Mathf.Max(6, (int)(Ws * 0.058f)), gap = Mathf.Max(6, (int)(Ws * 0.053f));
                for (int x = 0; x < Ws; x += dash + gap) FillRectS(x, ly, dash, Mathf.Max(1, (int)(Hs * 0.002f)), d);
            }
        }

        // traffic-light POLE is static (only the lamps change), drawn into the HD backdrop
        void PoleStatic()
        {
            float px = Ws * 0.9f, py = roadTop - 92 * (Hs / 720f), u = Ws / 380f;
            RoundRectS(px - 13 * u, py, 26 * u, 72 * u, 7 * u, C("#0c0d15"));
            FillRectS((int)(px - 3 * u), (int)(py + 72 * u), (int)(6 * u), (int)(roadTop - py - 72 * u), C("#1a1c26"));
        }

        // ================= DYNAMIC layer (cars + lamps, per frame) =================
        void InitMoving()
        {
            float h = Mathf.Max(20, Mathf.Min(Ws, Hs) * 0.056f);
            float[] ys = { Hs * 0.635f, Hs * 0.755f, Hs * 0.865f };
            float[] hs = { h * 0.92f, h * 1.0f, h * 1.12f };
            float[] sp = { Ws * 0.0017f * 60f, Ws * 0.002f * 60f, Ws * 0.0023f * 60f };
            float[] os = { 0.9f, 1.1f, 0.8f }, ph = { 0f, 1.7f, 3.2f };
            lanes = new List<Lane>();
            int seed = 12345;
            for (int L = 0; L < 3; L++)
            {
                var lane = new Lane { y = ys[L], h = hs[L], speed = sp[L], osc = os[L], phase = ph[L], cars = new List<Car>() };
                float x = -Ws * 0.35f;
                while (x < Ws * 1.15f)
                {
                    seed = (seed * 1103515245 + 12345) & 0x7fffffff; float rr = (seed % 1000) / 1000f;
                    bool bus = rr < 0.2f; float cw = bus ? lane.h * 3.1f : lane.h * 2.0f;
                    seed = (seed * 1103515245 + 12345) & 0x7fffffff;
                    var car = new Car { y = lane.y, w = cw, h = lane.h, bus = bus, col = bus ? BUS : CARS[seed % CARS.Length], x = x + cw / 2 };
                    lane.cars.Add(car);
                    seed = (seed * 1103515245 + 12345) & 0x7fffffff;
                    x += cw + (lane.h * 1.1f + (seed % 1000) / 1000f * lane.h * 2.2f);
                }
                lanes.Add(lane);
            }
        }

        void DrawDynamic(float dt)
        {
            System.Array.Clear(dyn, 0, dyn.Length);   // transparent — static backdrop shows through
            float t = Time.unscaledTime;
            float cyc = t % 6.2f; bool go = cyc < 3.4f, amb = cyc >= 3.4f && cyc < 3.9f;

            foreach (var L in lanes)
            {
                UpdateLane(L, go, t, dt);
                L.cars.Sort((a, b) => a.x.CompareTo(b.x));
                // skip cars fully off-screen (incl. their head/taillight glow) so we don't iterate dead bounding boxes
                foreach (var c in L.cars) { if (c.x + c.w * 1.3f < 0f || c.x - c.w * 0.7f > Ws) continue; DrawCar(c); }
            }
            Light(go, amb);
        }

        void UpdateLane(Lane L, bool go, float t, float dt)
        {
            if (L.cars.Count == 0) return;
            L.cars.Sort((a, b) => a.x.CompareTo(b.x));
            float sp = L.speed * (go ? (0.7f + 0.5f * Mathf.Max(0f, Mathf.Sin(t * L.osc + L.phase))) : 0.04f) * dt;
            for (int i = L.cars.Count - 1; i >= 0; i--)
            {
                var c = L.cars[i]; float tg = c.x + sp;
                if (i < L.cars.Count - 1) { var a = L.cars[i + 1]; tg = Mathf.Min(tg, a.x - (c.w / 2 + a.w / 2 + L.h * 0.55f)); }
                if (tg > c.x) c.x = tg;
            }
            var r = L.cars[L.cars.Count - 1];
            if (r.x - r.w / 2 > Ws + 24 * (Ws / 380f)) { var l = L.cars[0]; r.x = l.x - (r.w / 2 + l.w / 2 + L.h * 1.0f + ((int)(t * 53) % 100) / 100f * L.h * 2.5f); }
        }

        void DrawCar(Car c)
        {
            float x = c.x, y = c.y, w = c.w, h = c.h;
            EllipseD(x, y + h * 0.55f, w * 0.52f, h * 0.2f, new Color(0, 0, 0, 0.3f));            // shadow
            RoundRectD(x - w / 2, y - h / 2, w, h, h * 0.3f, c.col);                              // body
            Color dark = C("#13203e");
            if (c.bus) for (int k = 0; k < 6; k++) RoundRectD(x - w / 2 + w * 0.07f + k * w * 0.15f, y - h * 0.26f, w * 0.11f, h * 0.32f, 3, dark);
            else { RoundRectD(x - w * 0.24f, y - h * 0.66f, w * 0.52f, h * 0.5f, h * 0.22f, c.col); RoundRectD(x - w * 0.2f, y - h * 0.58f, w * 0.44f, h * 0.36f, 4, dark); }
            RoundRectD(x - w / 2, y - h / 2, w, h * 0.16f, h * 0.3f, new Color(1, 1, 1, 0.14f));   // highlight
            DiscD(x - w * 0.3f, y + h * 0.5f, h * 0.27f, C("#0a0a10")); DiscD(x + w * 0.3f, y + h * 0.5f, h * 0.27f, C("#0a0a10")); // wheels
            DiscD(x - w * 0.3f, y + h * 0.5f, h * 0.12f, C("#262833")); DiscD(x + w * 0.3f, y + h * 0.5f, h * 0.12f, C("#262833")); // hubs
            float hx = x + w * 0.5f, hy = y + h * 0.12f;
            RadialD(hx + w * 0.25f, hy, w * 0.55f, new Color(1f, 0.88f, 0.59f, 0.4f));             // headlight glow
            DiscD(hx, hy, h * 0.1f, C("#FFEBB4"));                                                 // headlight
            DiscD(x - w * 0.5f, hy, h * 0.09f, C("#FF3B3B"));                                      // taillight
            RadialD(x - w * 0.5f, hy, h * 0.17f, new Color(1f, 0.23f, 0.23f, 0.5f));               // taillight glow
        }

        void Light(bool go, bool amb)
        {
            float px = Ws * 0.9f, py = roadTop - 92 * (Hs / 720f), u = Ws / 380f;
            Lamp(px, py + 15 * u, !go && !amb, C("#FF3B3B"), u);
            Lamp(px, py + 36 * u, amb, C("#FFC14D"), u);
            Lamp(px, py + 57 * u, go, C("#39E08B"), u);
        }

        void Lamp(float cx, float cy, bool on, Color col, float u)
        {
            DiscD(cx, cy, 8 * u, on ? col : new Color(1, 1, 1, 0.10f));
            if (on) RadialD(cx, cy, 16 * u, new Color(col.r, col.g, col.b, 0.6f));
        }

        // ================= raster core (any buffer; canvas coords: y DOWN, flipped on write) =================
        Color EvalStops(Color[] cs, float[] ps, float s)
        {
            s = Mathf.Clamp01(s);
            for (int i = 0; i < ps.Length - 1; i++) if (s <= ps[i + 1]) return Color.Lerp(cs[i], cs[i + 1], Mathf.InverseLerp(ps[i], ps[i + 1], s));
            return cs[cs.Length - 1];
        }

        void Put(Color[] buf, int bw, int bh, int cx, int cy, Color c)
        {
            if (cx < 0 || cx >= bw || cy < 0 || cy >= bh || c.a <= 0f) return;
            int idx = (bh - 1 - cy) * bw + cx;        // flip y (texture is bottom-up)
            if (c.a >= 1f) { buf[idx] = c; return; }
            var d = buf[idx]; float a = c.a + d.a * (1 - c.a);
            buf[idx] = a <= 1e-4f ? new Color(0, 0, 0, 0)
                : new Color((c.r * c.a + d.r * d.a * (1 - c.a)) / a, (c.g * c.a + d.g * d.a * (1 - c.a)) / a, (c.b * c.a + d.b * d.a * (1 - c.a)) / a, a);
        }
        void FillRect(Color[] buf, int bw, int bh, int x, int y, int w, int h, Color c) { for (int j = y; j < y + h; j++) for (int i = x; i < x + w; i++) Put(buf, bw, bh, i, j, c); }
        void Disc(Color[] buf, int bw, int bh, float cx, float cy, float r, Color c)
        { for (int j = (int)(cy - r); j <= cy + r; j++) for (int i = (int)(cx - r); i <= cx + r; i++) { float dx = i - cx, dy = j - cy; if (dx * dx + dy * dy <= r * r) Put(buf, bw, bh, i, j, c); } }
        void Radial(Color[] buf, int bw, int bh, float cx, float cy, float r, Color c)
        { for (int j = (int)(cy - r); j <= cy + r; j++) for (int i = (int)(cx - r); i <= cx + r; i++) { float dd = Mathf.Sqrt((i - cx) * (i - cx) + (j - cy) * (j - cy)); if (dd < r) { var cc = c; cc.a = c.a * (1 - dd / r); Put(buf, bw, bh, i, j, cc); } } }
        void Ellipse(Color[] buf, int bw, int bh, float cx, float cy, float rx, float ry, Color c)
        { for (int j = (int)(cy - ry); j <= cy + ry; j++) for (int i = (int)(cx - rx); i <= cx + rx; i++) { float dx = (i - cx) / Mathf.Max(0.01f, rx), dy = (j - cy) / Mathf.Max(0.01f, ry); if (dx * dx + dy * dy <= 1f) Put(buf, bw, bh, i, j, c); } }
        void RoundRect(Color[] buf, int bw, int bh, float x, float y, float w, float h, float r, Color c)
        {
            r = Mathf.Min(r, Mathf.Min(w, h) * 0.5f);
            for (int j = (int)y; j < y + h; j++) for (int i = (int)x; i < x + w; i++)
            {
                float dx = Mathf.Max(0, Mathf.Max(x + r - i, i - (x + w - 1 - r)));
                float dy = Mathf.Max(0, Mathf.Max(y + r - j, j - (y + h - 1 - r)));
                if (dx * dx + dy * dy <= r * r) Put(buf, bw, bh, i, j, c);
            }
        }

        // ---- static wrappers (identity coords -> statik @ Ws,Hs) ----
        void SetS(int cx, int cy, Color c) => Put(statik, Ws, Hs, cx, cy, c);
        void FillRectS(int x, int y, int w, int h, Color c) => FillRect(statik, Ws, Hs, x, y, w, h, c);
        void DiscS(float cx, float cy, float r, Color c) => Disc(statik, Ws, Hs, cx, cy, r, c);
        void RadialS(float cx, float cy, float r, Color c) => Radial(statik, Ws, Hs, cx, cy, r, c);
        void RoundRectS(float x, float y, float w, float h, float r, Color c) => RoundRect(statik, Ws, Hs, x, y, w, h, r, c);

        // ---- dynamic wrappers (static-canvas coords -> band-local @ Wd,Hd) ----
        float MX(float cx) => cx * ds;
        float MY(float cy) => (cy - bandTopPx) * ds;
        void FillRectD(float x, float y, float w, float h, Color c) => FillRect(dyn, Wd, Hd, (int)MX(x), (int)MY(y), (int)(w * ds), (int)(h * ds), c);
        void DiscD(float cx, float cy, float r, Color c) => Disc(dyn, Wd, Hd, MX(cx), MY(cy), r * ds, c);
        void RadialD(float cx, float cy, float r, Color c) => Radial(dyn, Wd, Hd, MX(cx), MY(cy), r * ds, c);
        void EllipseD(float cx, float cy, float rx, float ry, Color c) => Ellipse(dyn, Wd, Hd, MX(cx), MY(cy), rx * ds, ry * ds, c);
        void RoundRectD(float x, float y, float w, float h, float r, Color c) => RoundRect(dyn, Wd, Hd, MX(x), MY(y), w * ds, h * ds, r * ds, c);
    }
}
