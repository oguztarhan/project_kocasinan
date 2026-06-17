using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace BusJam
{
    /// <summary>
    /// A 1:1 port of the &lt;canvas&gt; in loadingscreenfinal.html — the synthwave beach + traffic scene — drawn into
    /// a Texture2D each frame and shown on a full-screen RawImage. The STATIC backdrop (sky/sun/skyline/ocean/beach/
    /// palms/road) is rasterised once into `statik`; every frame we copy that and overlay only the moving bits
    /// (drifting clouds, the cars with the go/stop traffic-light cycle, the light), so it stays cheap. Redraw is
    /// capped at ~30fps. UNSCALED time. Set up + driven entirely by BootSplash; delete with it.
    /// </summary>
    public class BootCanvasScene : MonoBehaviour
    {
        RawImage img; Texture2D tex; Color[] frame, statik; int W, H;
        float horizon, oceanB, roadTop, roadBot, sunX, sunY, sunR;
        float lastT, acc;

        class Car { public float x, y, w, h; public bool bus; public Color col; }
        class Lane { public List<Car> cars; public float y, h, speed, osc, phase; }
        class Cloud { public float x, y, s, sp; }
        List<Lane> lanes; List<Cloud> clouds;

        static readonly Color[] CARS = { C("#FF5B5B"), C("#4D9DFF"), C("#FFC14D"), C("#34D399"), C("#FF8A3C"), C("#A855F7"), C("#F472B6"), C("#22D3EE") };
        static readonly Color BUS = C("#FF6B4A");
        static Color C(string h) { ColorUtility.TryParseHtmlString(h, out var c); return c; }

        public void Setup(RawImage target, int w, int h)
        {
            img = target; W = w; H = h;
            tex = new Texture2D(W, H, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
            frame = new Color[W * H]; statik = new Color[W * H];
            horizon = H * 0.40f; oceanB = H * 0.52f; roadTop = H * 0.56f; roadBot = H * 0.93f;
            sunX = W * 0.62f; sunY = horizon - W * 0.04f; sunR = W * 0.15f;
            BuildStatic();
            InitMoving();
            img.texture = tex;
            lastT = Time.unscaledTime;
            DrawDynamic(0f); Apply();
        }

        void Update()
        {
            acc += Time.unscaledDeltaTime;
            if (acc < 0.042f) return;                 // ~24fps redraw (higher res -> ease the per-frame cost)
            float t = Time.unscaledTime, dt = t - lastT; lastT = t; acc = 0f;
            DrawDynamic(dt); Apply();
        }

        void Apply() { tex.SetPixels(frame); tex.Apply(false); }

        // ================= STATIC backdrop (once) =================
        void BuildStatic()
        {
            for (int i = 0; i < statik.Length; i++) statik[i] = new Color(0, 0, 0, 1);
            Sky(); Sun(); Skyline(); Ocean(); Beach(); Palms(); Road();
        }

        void Sky()
        {
            float[] ps = { 0f, 0.4f, 0.72f, 1f };
            Color[] cs = { C("#3a2a82"), C("#a3429a"), C("#FF6F8E"), C("#FFC169") };
            for (int cy = 0; cy <= horizon + 2; cy++)
            {
                Color c = EvalStops(cs, ps, Mathf.Clamp01(cy / horizon));
                for (int cx = 0; cx < W; cx++) SetS(cx, cy, c);
            }
        }

        void Sun()
        {
            // glow
            RadialS(sunX, sunY, sunR * 2.6f, new Color(1f, 0.78f, 0.47f, 0.7f), statik);
            // disc (vertical gradient)
            float[] ps = { 0f, 0.55f, 1f };
            Color[] cs = { C("#FFE773"), C("#FF9E5A"), C("#FF5E86") };
            int y0 = (int)(sunY - sunR), y1 = (int)(sunY + sunR);
            for (int cy = y0; cy <= y1; cy++)
                for (int cx = (int)(sunX - sunR); cx <= sunX + sunR; cx++)
                {
                    float dx = cx - sunX, dy = cy - sunY;
                    if (dx * dx + dy * dy <= sunR * sunR) SetS(cx, cy, EvalStops(cs, ps, Mathf.Clamp01((cy - y0) / (2f * sunR))));
                }
            // scanlines
            Color sl = C("#a3429a");
            for (int i = 0; i < 5; i++) FillRectS((int)(sunX - sunR), (int)(sunY + sunR * 0.2f + i * sunR * 0.16f), (int)(sunR * 2), Mathf.Max(1, (int)(sunR * 0.05f)), sl);
        }

        void Skyline()
        {
            Color b = C("#5e2a63"), w = new Color(1f, 0.70f, 0.59f, 0.25f);
            float bx = 0; int seed = 7;
            while (bx < W)
            {
                seed = (seed * 1103515245 + 12345) & 0x7fffffff; float r1 = (seed % 1000) / 1000f;
                seed = (seed * 1103515245 + 12345) & 0x7fffffff; float r2 = (seed % 1000) / 1000f;
                float bw = (16 + r1 * 30) * (W / 380f), bh = (20 + r2 * 60) * (W / 380f);
                FillRectS((int)bx, (int)(horizon - bh), (int)bw, (int)bh, b);
                if (r1 > 0.5f) FillRectS((int)(bx + bw * 0.3f), (int)(horizon - bh * 0.7f), Mathf.Max(1, (int)(2 * W / 380f)), (int)(bh * 0.5f), w);
                seed = (seed * 1103515245 + 12345) & 0x7fffffff;
                bx += bw + (2 + (seed % 1000) / 1000f * 8) * (W / 380f);
            }
        }

        void Ocean()
        {
            for (int cy = (int)horizon; cy < oceanB; cy++)
            {
                Color c = Color.Lerp(C("#2ea6c4"), C("#134e6b"), (cy - horizon) / (oceanB - horizon));
                for (int cx = 0; cx < W; cx++) SetS(cx, cy, c);
            }
            // sun reflection
            for (float yy = horizon; yy < oceanB; yy += Mathf.Max(1, H * 0.009f))
            {
                float f = (yy - horizon) / (oceanB - horizon);
                float ww = sunR * 1.7f * (0.35f + 0.65f * (1 - f));
                FillRectS((int)(sunX - ww / 2), (int)yy, (int)ww, Mathf.Max(1, (int)(H * 0.004f)), new Color(1f, 0.82f, 0.48f, 0.3f * (1 - f)));
            }
            // wave lines
            for (int i = 0; i < 4; i++)
            {
                int yy = (int)(horizon + (oceanB - horizon) * (0.25f + i * 0.2f));
                FillRectS(0, yy, W, Mathf.Max(1, (int)(H * 0.0015f)), new Color(1, 1, 1, 0.22f));
            }
        }

        void Beach()
        {
            for (int cy = (int)oceanB; cy <= roadTop + 2; cy++)
            {
                Color c = Color.Lerp(C("#f0dcab"), C("#d8c08a"), (cy - oceanB) / (roadTop - oceanB));
                for (int cx = 0; cx < W; cx++) SetS(cx, cy, c);
            }
        }

        void Palms()
        {
            Color col = C("#14202c");
            float x = W * 0.07f; int n = 0;
            while (x < W)
            {
                float s = (n % 2 == 1 ? 1.35f : 0.95f) + ((n * 53) % 20) / 100f;
                Palm(x, roadTop - 2, s, col);
                x += W * (0.17f + ((n * 37) % 10) / 100f); n++;
            }
        }

        void Palm(float x, float by, float s, Color col)
        {
            float u = W / 380f, lw = 6 * u * s;
            float tx = x + 2 * u * s, ty = by - 98 * u * s;
            // trunk (quadratic curve approximated by points)
            for (float p = 0; p <= 1f; p += 0.02f)
            {
                float qx = Mathf.Lerp(Mathf.Lerp(x, x + 8 * u * s, p), Mathf.Lerp(x + 8 * u * s, tx, p), p);
                float qy = Mathf.Lerp(Mathf.Lerp(by, by - 52 * u * s, p), Mathf.Lerp(by - 52 * u * s, ty, p), p);
                DiscS(qx, qy, lw * 0.5f, col);
            }
            // fronds
            for (int a = 0; a < 7; a++)
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
            FillRectS(0, (int)roadTop, W, (int)(roadBot - roadTop), C("#2a2238"));
            FillRectS(0, (int)roadTop, W, Mathf.Max(1, (int)(H * 0.0045f)), C("#1c1729"));
            FillRectS(0, (int)(roadBot - H * 0.0045f), W, Mathf.Max(1, (int)(H * 0.0045f)), C("#1c1729"));
            // dashed lane lines between the 3 lanes
            float[] ys = { H * 0.635f, H * 0.755f, H * 0.865f };
            Color d = new Color(1f, 0.86f, 0.59f, 0.45f);
            for (int i = 1; i < 3; i++)
            {
                int ly = (int)((ys[i - 1] + ys[i]) / 2);
                int dash = Mathf.Max(6, (int)(W * 0.058f)), gap = Mathf.Max(6, (int)(W * 0.053f));
                for (int x = 0; x < W; x += dash + gap) FillRectS(x, ly, dash, Mathf.Max(1, (int)(H * 0.002f)), d);
            }
        }

        // ================= MOVING parts (per frame) =================
        void InitMoving()
        {
            float h = Mathf.Max(20, Mathf.Min(W, H) * 0.056f);
            float[] ys = { H * 0.635f, H * 0.755f, H * 0.865f };
            float[] hs = { h * 0.92f, h * 1.0f, h * 1.12f };
            float[] sp = { W * 0.0017f * 60f, W * 0.002f * 60f, W * 0.0023f * 60f };
            float[] os = { 0.9f, 1.1f, 0.8f }, ph = { 0f, 1.7f, 3.2f };
            lanes = new List<Lane>();
            int seed = 12345;
            for (int L = 0; L < 3; L++)
            {
                var lane = new Lane { y = ys[L], h = hs[L], speed = sp[L], osc = os[L], phase = ph[L], cars = new List<Car>() };
                float x = -W * 0.35f;
                while (x < W * 1.15f)
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
            clouds = new List<Cloud>();
            for (int i = 0; i < 4; i++)
            {
                seed = (seed * 1103515245 + 12345) & 0x7fffffff;
                clouds.Add(new Cloud { x = (seed % 1000) / 1000f * W, y = H * (0.07f + ((i * 53) % 16) / 100f), s = 0.7f + ((i * 71) % 70) / 100f, sp = (0.1f + ((i * 37) % 25) / 100f) * (W / 380f) * 60f });
            }
        }

        void DrawDynamic(float dt)
        {
            System.Array.Copy(statik, frame, frame.Length);
            float t = Time.unscaledTime;
            float cyc = t % 6.2f; bool go = cyc < 3.4f, amb = cyc >= 3.4f && cyc < 3.9f;

            // clouds
            foreach (var c in clouds)
            {
                c.x += c.sp * dt; if (c.x - 60 * (W / 380f) > W) c.x = -60 * (W / 380f);
                float r = 26 * (W / 380f) * c.s;
                Color body = new Color(1f, 0.70f, 0.67f, 0.55f), top = new Color(1f, 0.88f, 0.75f, 0.4f);
                DiscF(c.x, c.y, r, body); DiscF(c.x + r * 0.9f, c.y + r * 0.1f, r * 0.75f, body); DiscF(c.x - r * 0.9f, c.y + r * 0.15f, r * 0.65f, body);
                DiscF(c.x, c.y - r * 0.3f, r * 0.7f, top);
            }

            // cars (back lanes first so nearer lanes overlap)
            foreach (var L in lanes)
            {
                UpdateLane(L, go, t, dt);
                L.cars.Sort((a, b) => a.x.CompareTo(b.x));
                foreach (var c in L.cars) DrawCar(c);
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
            if (r.x - r.w / 2 > W + 24 * (W / 380f)) { var l = L.cars[0]; r.x = l.x - (r.w / 2 + l.w / 2 + L.h * 1.0f + ((int)(t * 53) % 100) / 100f * L.h * 2.5f); }
        }

        void DrawCar(Car c)
        {
            float x = c.x, y = c.y, w = c.w, h = c.h;
            EllipseF(x, y + h * 0.55f, w * 0.52f, h * 0.2f, new Color(0, 0, 0, 0.3f));            // shadow
            RoundRectF(x - w / 2, y - h / 2, w, h, h * 0.3f, c.col);                              // body
            Color dark = C("#13203e");
            if (c.bus) for (int k = 0; k < 6; k++) RoundRectF(x - w / 2 + w * 0.07f + k * w * 0.15f, y - h * 0.26f, w * 0.11f, h * 0.32f, 3, dark);
            else { RoundRectF(x - w * 0.24f, y - h * 0.66f, w * 0.52f, h * 0.5f, h * 0.22f, c.col); RoundRectF(x - w * 0.2f, y - h * 0.58f, w * 0.44f, h * 0.36f, 4, dark); }
            RoundRectF(x - w / 2, y - h / 2, w, h * 0.16f, h * 0.3f, new Color(1, 1, 1, 0.14f));   // highlight
            DiscF(x - w * 0.3f, y + h * 0.5f, h * 0.27f, C("#0a0a10")); DiscF(x + w * 0.3f, y + h * 0.5f, h * 0.27f, C("#0a0a10")); // wheels
            DiscF(x - w * 0.3f, y + h * 0.5f, h * 0.12f, C("#262833")); DiscF(x + w * 0.3f, y + h * 0.5f, h * 0.12f, C("#262833")); // hubs
            float hx = x + w * 0.5f, hy = y + h * 0.12f;
            RadialF(hx + w * 0.25f, hy, w * 0.55f, new Color(1f, 0.88f, 0.59f, 0.4f));             // headlight glow
            DiscF(hx, hy, h * 0.1f, C("#FFEBB4"));                                                 // headlight
            DiscF(x - w * 0.5f, hy, h * 0.09f, C("#FF3B3B"));                                      // taillight
            RadialF(x - w * 0.5f, hy, h * 0.17f, new Color(1f, 0.23f, 0.23f, 0.5f));               // taillight glow
        }

        void Light(bool go, bool amb)
        {
            float px = W * 0.9f, py = roadTop - 92 * (H / 720f), u = W / 380f;
            RoundRectF(px - 13 * u, py, 26 * u, 72 * u, 7 * u, C("#0c0d15"));
            FillRectF((int)(px - 3 * u), (int)(py + 72 * u), (int)(6 * u), (int)(roadTop - py - 72 * u), C("#1a1c26"));
            Lamp(px, py + 15 * u, !go && !amb, C("#FF3B3B"), u);
            Lamp(px, py + 36 * u, amb, C("#FFC14D"), u);
            Lamp(px, py + 57 * u, go, C("#39E08B"), u);
        }

        void Lamp(float cx, float cy, bool on, Color col, float u)
        {
            DiscF(cx, cy, 8 * u, on ? col : new Color(1, 1, 1, 0.10f));
            if (on) RadialF(cx, cy, 16 * u, new Color(col.r, col.g, col.b, 0.6f));
        }

        // ================= raster helpers (canvas coords: y DOWN, flipped on write) =================
        Color EvalStops(Color[] cs, float[] ps, float s)
        {
            s = Mathf.Clamp01(s);
            for (int i = 0; i < ps.Length - 1; i++) if (s <= ps[i + 1]) return Color.Lerp(cs[i], cs[i + 1], Mathf.InverseLerp(ps[i], ps[i + 1], s));
            return cs[cs.Length - 1];
        }

        void SetS(int cx, int cy, Color c) { Put(statik, cx, cy, c); }
        void FillRectS(int x, int y, int w, int h, Color c) { for (int j = y; j < y + h; j++) for (int i = x; i < x + w; i++) Put(statik, i, j, c); }
        void DiscS(float cx, float cy, float r, Color c) { Disc(statik, cx, cy, r, c); }
        void RadialS(float cx, float cy, float r, Color c, Color[] buf) { Radial(buf, cx, cy, r, c); }

        void FillRectF(int x, int y, int w, int h, Color c) { for (int j = y; j < y + h; j++) for (int i = x; i < x + w; i++) Put(frame, i, j, c); }
        void DiscF(float cx, float cy, float r, Color c) { Disc(frame, cx, cy, r, c); }
        void RadialF(float cx, float cy, float r, Color c) { Radial(frame, cx, cy, r, c); }
        void EllipseF(float cx, float cy, float rx, float ry, Color c)
        {
            for (int j = (int)(cy - ry); j <= cy + ry; j++) for (int i = (int)(cx - rx); i <= cx + rx; i++)
            { float dx = (i - cx) / Mathf.Max(0.01f, rx), dy = (j - cy) / Mathf.Max(0.01f, ry); if (dx * dx + dy * dy <= 1f) Put(frame, i, j, c); }
        }
        void RoundRectF(float x, float y, float w, float h, float r, Color c)
        {
            r = Mathf.Min(r, Mathf.Min(w, h) * 0.5f);
            for (int j = (int)y; j < y + h; j++) for (int i = (int)x; i < x + w; i++)
            {
                float dx = Mathf.Max(0, Mathf.Max(x + r - i, i - (x + w - 1 - r)));
                float dy = Mathf.Max(0, Mathf.Max(y + r - j, j - (y + h - 1 - r)));
                if (dx * dx + dy * dy <= r * r) Put(frame, i, j, c);
            }
        }

        void Disc(Color[] buf, float cx, float cy, float r, Color c)
        {
            for (int j = (int)(cy - r); j <= cy + r; j++) for (int i = (int)(cx - r); i <= cx + r; i++)
            { float dx = i - cx, dy = j - cy; if (dx * dx + dy * dy <= r * r) Put(buf, i, j, c); }
        }
        void Radial(Color[] buf, float cx, float cy, float r, Color c)
        {
            for (int j = (int)(cy - r); j <= cy + r; j++) for (int i = (int)(cx - r); i <= cx + r; i++)
            { float d = Mathf.Sqrt((i - cx) * (i - cx) + (j - cy) * (j - cy)); if (d < r) { var cc = c; cc.a = c.a * (1 - d / r); Put(buf, i, j, cc); } }
        }

        void Put(Color[] buf, int cx, int cy, Color c)
        {
            if (cx < 0 || cx >= W || cy < 0 || cy >= H || c.a <= 0f) return;
            int idx = (H - 1 - cy) * W + cx;          // flip y (texture is bottom-up)
            if (c.a >= 1f) { buf[idx] = c; return; }
            var d = buf[idx]; float a = c.a + d.a * (1 - c.a);
            buf[idx] = a <= 1e-4f ? new Color(0, 0, 0, 0)
                : new Color((c.r * c.a + d.r * d.a * (1 - c.a)) / a, (c.g * c.a + d.g * d.a * (1 - c.a)) / a, (c.b * c.a + d.b * d.a * (1 - c.a)) / a, a);
        }
    }
}
