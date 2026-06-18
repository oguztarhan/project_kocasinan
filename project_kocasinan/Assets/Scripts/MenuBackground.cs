using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.SceneManagement;

namespace BusJam
{
    /// <summary>
    /// MAIN-MENU animated background — a faithful port of the user's intake_rush_coast_sunset.html canvas scene
    /// (coastal sunset: sky/sun, drifting sea, beach + railing, palms, a road with two lanes of side-view traffic,
    /// and a dark bottom panel the menu buttons sit on). Rendered as TWO layers:
    ///   • STATIC backdrop (everything except the cars) rasterised ONCE at full device resolution onto a full-screen
    ///     RawImage. Never re-uploaded => HD, no per-frame cost.
    ///   • DYNAMIC band (the two lanes of scrolling cars/buses) redrawn each frame into a small texture over just the
    ///     road, anchored by screen fraction.
    /// The HTML's btn()/gear()/star() helpers are unused (they're where the menu's own buttons go), so there is NO
    /// billboard/title to render here — the menu's Logo/Title/buttons sit on top.
    ///
    /// SELF-SPAWNS whenever the MainMenu scene is active (sortingOrder -100, behind menu UI) and disables the scene's
    /// one static "Background" Image so this shows. Plain scene object (dies on scene change). Delete this file to
    /// remove the whole thing. Runs on UNSCALED time.
    /// </summary>
    public class MenuBackground : MonoBehaviour
    {
        static MenuBackground current;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot()
        {
            SceneManager.sceneLoaded += (scene, mode) => { if (scene.name == "MainMenu") Spawn(); };
            if (SceneManager.GetActiveScene().name == "MainMenu") Spawn();
        }
        static void Spawn()
        {
            if (current != null) return;
            var go = new GameObject("MenuBackground");
            current = go.AddComponent<MenuBackground>();
        }

        // ---------- palette (coast sunset) ----------
        static Color Hex(string h) { ColorUtility.TryParseHtmlString(h, out var c); return c; }
        static Color RGBA(float r, float g, float b, float a) => new Color(r, g, b, a);
        static readonly Color[] COLS = {
            Hex("#FF6B8A"), Hex("#FFB14E"), Hex("#FFD23F"), Hex("#5FD08A"), Hex("#52C7E6"),
            Hex("#7AA0F0"), Hex("#B98BF0"), Hex("#FF8FB8"), Hex("#FF6B5B"), Hex("#42C7B0"),
        };
        static readonly Color SkyTop = Hex("#F2683E"), SkyMid = Hex("#FF9A57"), SkyBot = Hex("#FFD37A");
        static readonly Color SeaTop = Hex("#F4A063"), SeaMid = Hex("#C9774F"), SeaBotC = Hex("#3E7E86");
        static readonly Color SunDisc = Hex("#FFE39A"), CloudCol = Hex("#FFC79A");
        static readonly Color BeachCol = Hex("#E9C98C"), BeachTop = Hex("#F2DCA6"), FencePost = Hex("#8a7a68"), FenceRail = Hex("#9a8a78");
        static readonly Color RoadCol = Hex("#46434f"), RoadEdge = Hex("#5a5666");
        static readonly Color PalmTrunk = Hex("#B5894F"), PalmBase = Hex("#7a5a34");
        static readonly Color[] Fronds = { Hex("#2f8f3e"), Hex("#3FA45B"), Hex("#46B86A"), Hex("#3FA45B"), Hex("#2f8f3e") };
        static readonly Color WinDay = Hex("#CFF0FF"), WinDay2 = Hex("#A9DCF2"), Wheel = Hex("#23262e"), Hub = Hex("#cfd2da"), Headlight = Hex("#FFE9B0"), Taillight = Hex("#FF5B5B");

        // ---------- render targets ----------
        const float BandTopF = 0.52f, BandBotF = 0.71f;   // the road/traffic band (canvas-down) the dynamic layer covers
        RawImage staticImg, dynImg;
        Texture2D staticTex, dynTex;
        Color[] statik, dyn;
        int Ws, Hs, Wd, Hd;
        float ds, bandTopPx, DPR, M;
        float lastT, acc;
        GameObject oldBackground;

        // ---------- scene geometry (HTML geo()) ----------
        float horizonY, seaBot, roadTop, roadBot, sunX, sunY;

        class Veh { public float x, len; public bool bus; public Color col; }
        class Lane { public float y, S, baseSpd, gap, ph; public List<Veh> V; }
        List<Lane> lanes;

        uint rng = 0x1A2B3C4D;
        float Rnd() { rng ^= rng << 13; rng ^= rng >> 17; rng ^= rng << 5; return (rng & 0xFFFFFF) / 16777215f; }

        void Start()
        {
            oldBackground = GameObject.Find("Background");
            if (oldBackground != null) oldBackground.SetActive(false);

            var cgo = new GameObject("MenuBgCanvas", typeof(Canvas), typeof(CanvasScaler));
            cgo.transform.SetParent(transform, false);
            var canvas = cgo.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = -100;
            var sc = cgo.GetComponent<CanvasScaler>();
            sc.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            sc.referenceResolution = new Vector2(1080, 1920);
            sc.matchWidthOrHeight = 0.5f;
            var rootRT = cgo.GetComponent<RectTransform>();

            Ws = Mathf.Clamp(Screen.width, 720, 1080);
            Hs = Mathf.Clamp(Mathf.RoundToInt(Ws * (float)Screen.height / Mathf.Max(1, Screen.width)), 1000, 2400);
            DPR = Ws / 390f;
            M = Mathf.Min(Ws, Hs);
            horizonY = Hs * 0.40f; seaBot = Hs * 0.55f; roadTop = Hs * 0.575f; roadBot = Hs;   // soft variant: road fills to the bottom
            sunX = Ws * 0.30f; sunY = Hs * 0.385f;

            statik = new Color[Ws * Hs];
            BuildLanes();
            BuildStatic();
            staticTex = new Texture2D(Ws, Hs, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
            staticTex.SetPixels(statik); staticTex.Apply(false);
            var sgo = new GameObject("StaticBg", typeof(RectTransform), typeof(RawImage));
            sgo.transform.SetParent(rootRT, false); Stretch(sgo.GetComponent<RectTransform>());
            staticImg = sgo.GetComponent<RawImage>(); staticImg.raycastTarget = false; staticImg.texture = staticTex;
            statik = null;

            bandTopPx = BandTopF * Hs;
            Wd = Mathf.Clamp(Mathf.RoundToInt(Ws * 0.9f), 600, 1080);
            ds = Wd / (float)Ws;
            Hd = Mathf.Max(1, Mathf.RoundToInt((BandBotF - BandTopF) * Hs * ds));
            dyn = new Color[Wd * Hd];
            dynTex = new Texture2D(Wd, Hd, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
            var dgo = new GameObject("RoadDynamic", typeof(RectTransform), typeof(RawImage));
            dgo.transform.SetParent(rootRT, false);
            var drt = dgo.GetComponent<RectTransform>();
            drt.anchorMin = new Vector2(0f, 1f - BandBotF); drt.anchorMax = new Vector2(1f, 1f - BandTopF);
            drt.offsetMin = Vector2.zero; drt.offsetMax = Vector2.zero;
            dynImg = dgo.GetComponent<RawImage>(); dynImg.raycastTarget = false; dynImg.texture = dynTex;

            BuildPanel(rootRT);     // soft dark fade over the lower scene (sits ON TOP of the traffic, like the HTML)
            BuildBokeh(rootRT);     // warm motes drifting up over the dark

            lastT = Time.unscaledTime;
            DrawDynamic(0f); ApplyDyn();
        }

        void OnDestroy()
        {
            if (oldBackground != null) oldBackground.SetActive(true);
            if (current == this) current = null;
        }

        void Update()
        {
            acc += Time.unscaledDeltaTime;
            if (acc < 0.033f) return;
            float t = Time.unscaledTime, dt = t - lastT; lastT = t; acc = 0f;
            DrawDynamic(dt); ApplyDyn();
        }

        void ApplyDyn() { dynTex.SetPixels(dyn); dynTex.Apply(false); }

        // =====================================================================================
        //  lanes (HTML mkLane/fillLane/vf)
        // =====================================================================================
        void BuildLanes()
        {
            lanes = new List<Lane>
            {
                MkLane(Hs * 0.615f, M * 0.0012f, 18f * DPR),
                MkLane(Hs * 0.672f, M * 0.0018f, 26f * DPR),
            };
        }
        Lane MkLane(float y, float S, float baseSpd)
        {
            var L = new Lane { y = y, S = S, baseSpd = baseSpd, gap = 8f * DPR, ph = Rnd() * 6f, V = new List<Veh>() };
            float x = -180f * DPR;
            while (x < Ws + 240f * DPR)
            {
                bool bus = Rnd() < 0.38f;
                float len = (bus ? 150f : 96f) * S;
                L.V.Add(new Veh { x = x, len = len, bus = bus, col = COLS[(int)(Rnd() * COLS.Length) % COLS.Length] });
                x += len + (14f + Rnd() * 28f) * DPR;
            }
            return L;
        }

        // =====================================================================================
        //  STATIC backdrop (HTML loop() minus the cars)
        // =====================================================================================
        void BuildStatic()
        {
            for (int i = 0; i < statik.Length; i++) statik[i] = new Color(0, 0, 0, 1);
            Sky(); Sun(); Clouds(); Sea(); Beach(); Fence(); Palms(); Road(); LaneDash(); WarmOverlay();
            // (the dark bottom fade + bokeh are UGUI layers built in Start, so they sit OVER the moving traffic)
        }

        void Sky()
        {
            for (int y = 0; y < seaBot && y < Hs; y++)
            {
                float f = y / seaBot;
                Color c = f < 0.5f ? Color.Lerp(SkyTop, SkyMid, f / 0.5f) : Color.Lerp(SkyMid, SkyBot, (f - 0.5f) / 0.5f);
                for (int x = 0; x < Ws; x++) SetS(x, y, c);
            }
        }

        void Sun()
        {
            RadialS(sunX, sunY, Hs * 0.26f, RGBA(1f, 0.922f, 0.667f, 0.95f));   // glow (approx of additive)
            DiscS(sunX, sunY, Hs * 0.06f, SunDisc);
        }

        void Clouds()
        {
            for (int i = 0; i < 3; i++)
            {
                float x = Rnd() * Ws, y = Hs * (0.10f + Rnd() * 0.15f), s = (16f + Rnd() * 14f) * DPR;
                Cloud(x, y, s);
            }
        }
        void Cloud(float x, float y, float s)
        {
            PolyS(new[] { new Vector2(x - 3 * s, y), new Vector2(x - 1 * s, y - 1.4f * s), new Vector2(x + 1 * s, y - 1.6f * s), new Vector2(x + 3 * s, y - 0.6f * s), new Vector2(x + 3.4f * s, y + 0.6f * s), new Vector2(x - 3 * s, y + 0.6f * s) }, CloudCol);
            PolyS(new[] { new Vector2(x - 3 * s, y + 0.6f * s), new Vector2(x + 3.4f * s, y + 0.6f * s), new Vector2(x + 3 * s, y - 0.6f * s), new Vector2(x - 1 * s, y - 0.2f * s) }, Shade(CloudCol, 0.92f));
        }

        void Sea()
        {
            for (int y = (int)horizonY; y < seaBot && y < Hs; y++)
            {
                float f = (y - horizonY) / (seaBot - horizonY);
                Color c = f < 0.5f ? Color.Lerp(SeaTop, SeaMid, f / 0.5f) : Color.Lerp(SeaMid, SeaBotC, (f - 0.5f) / 0.5f);
                for (int x = 0; x < Ws; x++) SetS(x, y, c);
            }
            // sun glints on the water (baked at t=0)
            for (int i = 0; i < 10; i++)
            {
                float yy = horizonY + (seaBot - horizonY) * (i + 0.5f) / 10f, wob = Mathf.Sin(i) * 8f * DPR, ww = Hs * 0.03f + i * 5f * DPR;
                FillRectS((int)(sunX - ww + wob), (int)yy, (int)(ww * 2), Mathf.Max(1, (int)(3 * DPR)), RGBA(1f, 0.882f, 0.627f, 0.22f * (1 - i / 12f)));
            }
            // faint wave lines
            for (int i = 0; i < 6; i++)
            {
                float yy = horizonY + (seaBot - horizonY) * (i + 0.7f) / 6f + Mathf.Sin(i) * 2f * DPR;
                FillRectS(0, (int)yy, Ws, Mathf.Max(1, (int)(2 * DPR)), RGBA(1, 1, 1, 0.06f));
            }
        }

        void Beach()
        {
            FillRectS(0, (int)seaBot, Ws, (int)(roadTop - seaBot) + 1, BeachCol);
            FillRectS(0, (int)seaBot, Ws, Mathf.Max(1, (int)(4 * DPR)), BeachTop);
        }

        void Fence()
        {
            FillRectS(0, (int)(seaBot + 5 * DPR), Ws, Mathf.Max(1, (int)(3 * DPR)), FenceRail);
            int postH = (int)(roadTop - (seaBot + 6 * DPR) - 2 * DPR);
            for (float x = 0; x < Ws; x += Ws * 0.07f) FillRectS((int)x, (int)(seaBot + 6 * DPR), Mathf.Max(1, (int)(3 * DPR)), postH, FencePost);
        }

        void Palms()
        {
            float[] pp = { 0.12f, 0.5f, 0.82f };
            for (int i = 0; i < pp.Length; i++) Palm(Ws * pp[i], roadTop, M * 0.016f);
        }
        void Palm(float x, float baseY, float s)
        {
            // trunk
            PolyS(new[] { new Vector2(x - 0.5f * s, baseY), new Vector2(x + 0.5f * s, baseY), new Vector2(x + 0.28f * s, baseY - 5 * s), new Vector2(x - 0.28f * s, baseY - 5 * s) }, PalmTrunk);
            PolyS(new[] { new Vector2(x + 0.05f * s, baseY), new Vector2(x + 0.5f * s, baseY), new Vector2(x + 0.28f * s, baseY - 5 * s), new Vector2(x + 0.06f * s, baseY - 5 * s) }, Shade(PalmTrunk, 0.82f));
            // crown (5 fronds), rotated about the trunk top (no sway baked in)
            float ox = x, oy = baseY - 5 * s;
            for (int i = 0; i < 5; i++)
            {
                float a = -1.15f + i * 0.57f, ca = Mathf.Cos(a), sa = Mathf.Sin(a);
                Vector2 Rot(float lx, float ly) => new Vector2(ox + lx * ca - ly * sa, oy + lx * sa + ly * ca);
                PolyS(new[] { Rot(0, 0), Rot(3.6f * s, -0.5f * s), Rot(3.0f * s, 0.6f * s) }, Fronds[i]);
            }
            DiscS(ox, oy, 0.5f * s, PalmBase);
        }

        void Road()
        {
            FillRectS(0, (int)roadTop, Ws, (int)(roadBot - roadTop), RoadCol);
            FillRectS(0, (int)roadTop, Ws, Mathf.Max(1, (int)(4 * DPR)), RoadEdge);
        }

        void LaneDash()
        {
            int y = (int)((lanes[0].y + lanes[1].y) / 2 + 6 * DPR);
            int dash = Mathf.Max(2, (int)(22 * DPR)), gap = Mathf.Max(2, (int)(16 * DPR)), th = Mathf.Max(1, (int)(3 * DPR));
            for (int x = 0; x < Ws; x += dash + gap) FillRectS(x, y, dash, th, RGBA(1f, 0.922f, 0.784f, 0.7f));
        }

        void WarmOverlay()
        {
            FillRectS(0, 0, Ws, (int)roadBot, RGBA(1f, 0.588f, 0.314f, 0.06f));   // faint warm wash (approx of additive)
        }

        // HTML's soft dark fade (py0=H*0.55 -> H), as a UGUI gradient Image ON TOP of the traffic so the cars fade
        // into the dark exactly like the HTML (panel drawn after the vehicles).
        void BuildPanel(RectTransform root)
        {
            var go = new GameObject("BottomPanel", typeof(RectTransform), typeof(Image));
            go.transform.SetParent(root, false);
            var im = go.GetComponent<Image>(); im.sprite = PanelSprite(); im.type = Image.Type.Simple; im.raycastTarget = false;
            var rt = im.rectTransform;
            rt.anchorMin = new Vector2(0f, 0f); rt.anchorMax = new Vector2(1f, 0.45f);    // canvas-down [0.55, 1.0]
            rt.offsetMin = Vector2.zero; rt.offsetMax = Vector2.zero;
        }
        Sprite PanelSprite()
        {
            int N = 128, Wt = 4; var tex = new Texture2D(Wt, N, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
            var buf = new Color[Wt * N];
            for (int row = 0; row < N; row++) { Color c = PanelGrad(1f - row / (N - 1f)); for (int x = 0; x < Wt; x++) buf[row * Wt + x] = c; }   // row N-1 (top)=f0, row 0 (bottom)=f1
            tex.SetPixels(buf); tex.Apply(false);
            return Sprite.Create(tex, new Rect(0, 0, Wt, N), new Vector2(0.5f, 0.5f), 100, 0, SpriteMeshType.FullRect);
        }
        static Color PanelGrad(float f)     // f=0 top (transparent) .. f=1 bottom (dark) — HTML stops 0/.45/.8/1
        {
            Color s0 = RGBA(0.133f, 0.071f, 0.102f, 0f), s1 = RGBA(0.125f, 0.063f, 0.094f, 0.26f), s2 = RGBA(0.110f, 0.055f, 0.086f, 0.60f), s3 = RGBA(0.086f, 0.043f, 0.071f, 0.90f);
            if (f < 0.45f) return Color.Lerp(s0, s1, f / 0.45f);
            if (f < 0.8f) return Color.Lerp(s1, s2, (f - 0.45f) / 0.35f);
            return Color.Lerp(s2, s3, (f - 0.8f) / 0.2f);
        }

        // warm bokeh motes drifting up over the dark panel (HTML's twinkling additive dots) — UGUI glow sprites
        void BuildBokeh(RectTransform root)
        {
            var glow = GlowSprite();
            for (int i = 0; i < 14; i++)
            {
                var go = new GameObject("Bokeh", typeof(RectTransform), typeof(Image));
                go.transform.SetParent(root, false);
                var im = go.GetComponent<Image>(); im.sprite = glow; im.raycastTarget = false;
                float r = (2f + Rnd() * 4f) * DPR, sz = r * 3.4f;
                var rt = im.rectTransform;
                rt.anchorMin = rt.anchorMax = new Vector2(Rnd(), Rnd() * 0.5f); rt.pivot = new Vector2(0.5f, 0.5f);
                rt.sizeDelta = new Vector2(sz, sz); rt.anchoredPosition = Vector2.zero;
                var bk = go.AddComponent<MenuBokeh>();
                bk.img = im; bk.baseA = 0.10f + Rnd() * 0.16f; bk.ph = Rnd() * 6f; bk.vyF = (6f + Rnd() * 10f) * DPR / Hs;
                im.color = new Color(1f, 0.823f, 0.588f, bk.baseA);
            }
        }
        Sprite GlowSprite()
        {
            int S = 48; var tex = new Texture2D(S, S, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
            var buf = new Color[S * S]; float c = (S - 1) * 0.5f;
            for (int y = 0; y < S; y++) for (int x = 0; x < S; x++) { float d = Mathf.Sqrt((x - c) * (x - c) + (y - c) * (y - c)) / c; float a = Mathf.Clamp01(1f - d); a *= a; buf[y * S + x] = new Color(1, 1, 1, a); }
            tex.SetPixels(buf); tex.Apply(false);
            return Sprite.Create(tex, new Rect(0, 0, S, S), new Vector2(0.5f, 0.5f), 100, 0, SpriteMeshType.FullRect);
        }

        // =====================================================================================
        //  DYNAMIC layer (HTML stepLane + the two side-view vehicle drawers)
        // =====================================================================================
        void DrawDynamic(float dt)
        {
            System.Array.Clear(dyn, 0, dyn.Length);
            float t = Time.unscaledTime;
            foreach (var L in lanes)
            {
                StepLane(L, t, dt);
                foreach (var v in L.V)
                {
                    if (v.x + v.len < -8 || v.x > Ws + 8) continue;
                    if (v.bus) DrawBus(v.x + v.len / 2, L.y, L.S, v.col); else DrawCar(v.x + v.len / 2, L.y, L.S, v.col);
                }
            }
        }

        void StepLane(Lane L, float t, float dt)
        {
            L.V.Sort((a, b) => a.x.CompareTo(b.x));
            float sg = 0.5f + 0.5f * Mathf.Sin(t * 0.6f + L.ph);
            float spd = L.baseSpd * (0.18f + 1.0f * sg);
            for (int i = L.V.Count - 1; i >= 0; i--)
            {
                var v = L.V[i]; float nx = v.x + spd * dt;
                if (i < L.V.Count - 1) { var ah = L.V[i + 1]; nx = Mathf.Min(nx, ah.x - v.len - L.gap); }
                if (nx > v.x) v.x = nx;
            }
            var r = L.V[L.V.Count - 1];
            if (r.x > Ws + 200 * DPR) { var l = L.V[0]; r.x = l.x - r.len - L.gap - Rnd() * 30 * DPR; }
        }

        void DrawBus(float cx, float baseY, float S, Color col)
        {
            const float L = 150f, Hh = 72f;
            Vector2 P(float lx, float ly) => new Vector2(cx + lx * S, baseY + ly * S);
            PolyD(new[] { P(-72, 8), P(-50, 2), P(58, 2), P(72, 9), P(52, 15), P(-54, 15) }, RGBA(0, 0, 0, 0.12f)); // shadow
            PolyD(new[] { P(-L / 2, -12), P(-L / 2, -Hh + 8), P(L / 2 - 16, -Hh + 8), P(L / 2, -Hh + 20), P(L / 2, -12) }, col); // body
            PolyD(new[] { P(-L / 2, -Hh + 8), P(L / 2 - 16, -Hh + 8), P(L / 2 - 16, -Hh + 18), P(-L / 2, -Hh + 18) }, Shade(col, 1.14f)); // roof hl
            PolyD(new[] { P(-L / 2, -24), P(L / 2, -24), P(L / 2, -12), P(-L / 2, -12) }, Shade(col, 0.82f)); // lower band
            PolyD(new[] { P(L / 2 - 16, -Hh + 8), P(L / 2, -Hh + 20), P(L / 2, -24), P(L / 2 - 16, -24) }, Shade(col, 0.9f)); // front slope
            for (int k = 0; k < 4; k++)
            {
                float x = -L / 2 + 14 + k * 32;
                PolyD(new[] { P(x, -Hh + 16), P(x + 24, -Hh + 16), P(x + 22, -Hh + 40), P(x - 2, -Hh + 40) }, WinDay);
                PolyD(new[] { P(x - 2, -Hh + 40), P(x + 22, -Hh + 40), P(x + 24, -Hh + 30), P(x, -Hh + 30) }, WinDay2);
            }
            DiscD(cx - L * 0.3f * S, baseY - 8 * S, 15 * S, Wheel); DiscD(cx + L * 0.3f * S, baseY - 8 * S, 15 * S, Wheel);
            DiscD(cx - L * 0.3f * S, baseY - 8 * S, 6 * S, Hub); DiscD(cx + L * 0.3f * S, baseY - 8 * S, 6 * S, Hub);
            DiscD(cx + (L / 2 - 3) * S, baseY - 30 * S, 6 * S, Headlight);
            DiscD(cx + (-L / 2 + 4) * S, baseY - 30 * S, 4 * S, Taillight);
        }

        void DrawCar(float cx, float baseY, float S, Color col)
        {
            const float L = 96f, Hh = 44f;
            Vector2 P(float lx, float ly) => new Vector2(cx + lx * S, baseY + ly * S);
            PolyD(new[] { P(-50, 6), P(-34, 2), P(40, 2), P(50, 7), P(36, 12), P(-36, 12) }, RGBA(0, 0, 0, 0.12f)); // shadow
            PolyD(new[] { P(-L * 0.28f, -Hh * 0.5f), P(L * 0.34f, -Hh * 0.5f), P(L * 0.22f, -Hh), P(-L * 0.18f, -Hh) }, col); // cabin
            PolyD(new[] { P(-L / 2, -8), P(-L / 2, -Hh * 0.5f), P(L / 2, -Hh * 0.5f), P(L / 2, -8) }, col); // body
            PolyD(new[] { P(-L / 2, -Hh * 0.5f), P(L / 2, -Hh * 0.5f), P(L / 2, -Hh * 0.5f + 5), P(-L / 2, -Hh * 0.5f + 5) }, Shade(col, 1.12f)); // roof hl
            PolyD(new[] { P(-L / 2, -Hh * 0.16f), P(L / 2, -Hh * 0.16f), P(L / 2, -8), P(-L / 2, -8) }, Shade(col, 0.84f)); // lower
            PolyD(new[] { P(-L * 0.16f, -Hh * 0.54f), P(L * 0.24f, -Hh * 0.54f), P(L * 0.14f, -Hh * 0.94f), P(-L * 0.12f, -Hh * 0.94f) }, WinDay); // windshield
            DiscD(cx - L * 0.28f * S, baseY - 6 * S, 12 * S, Wheel); DiscD(cx + L * 0.28f * S, baseY - 6 * S, 12 * S, Wheel);
            DiscD(cx - L * 0.28f * S, baseY - 6 * S, 5 * S, Hub); DiscD(cx + L * 0.28f * S, baseY - 6 * S, 5 * S, Hub);
            DiscD(cx + (L / 2 - 2) * S, baseY - 22 * S, 5 * S, Headlight);
            DiscD(cx + (-L / 2 + 3) * S, baseY - 22 * S, 3.5f * S, Taillight);
        }

        // =====================================================================================
        //  raster core (any buffer; canvas coords y-DOWN, flipped on write)
        // =====================================================================================
        static Color Shade(Color c, float f) => new Color(Mathf.Min(1, c.r * f), Mathf.Min(1, c.g * f), Mathf.Min(1, c.b * f), c.a);

        void Put(Color[] buf, int bw, int bh, int cx, int cy, Color c)
        {
            if (cx < 0 || cx >= bw || cy < 0 || cy >= bh || c.a <= 0f) return;
            int idx = (bh - 1 - cy) * bw + cx;
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
        readonly List<float> _xs = new List<float>(16);
        void Poly(Color[] buf, int bw, int bh, Vector2[] p, int n, Color c)
        {
            if (n < 3) return;
            float minY = 1e9f, maxY = -1e9f;
            for (int i = 0; i < n; i++) { if (p[i].y < minY) minY = p[i].y; if (p[i].y > maxY) maxY = p[i].y; }
            int y0 = Mathf.Max(0, Mathf.FloorToInt(minY)), y1 = Mathf.Min(bh - 1, Mathf.CeilToInt(maxY));
            for (int y = y0; y <= y1; y++)
            {
                float yc = y + 0.5f; _xs.Clear();
                for (int i = 0; i < n; i++)
                {
                    Vector2 a = p[i], b = p[(i + 1) % n];
                    if ((a.y <= yc && b.y > yc) || (b.y <= yc && a.y > yc)) _xs.Add(a.x + (yc - a.y) / (b.y - a.y) * (b.x - a.x));
                }
                _xs.Sort();
                for (int k = 0; k + 1 < _xs.Count; k += 2)
                {
                    int xa = Mathf.Max(0, Mathf.RoundToInt(_xs[k])), xb = Mathf.Min(bw - 1, Mathf.RoundToInt(_xs[k + 1]));
                    for (int x = xa; x <= xb; x++) Put(buf, bw, bh, x, y, c);
                }
            }
        }

        // static wrappers (statik @ Ws,Hs)
        void SetS(int cx, int cy, Color c) => Put(statik, Ws, Hs, cx, cy, c);
        void FillRectS(int x, int y, int w, int h, Color c) => FillRect(statik, Ws, Hs, x, y, w, h, c);
        void DiscS(float cx, float cy, float r, Color c) => Disc(statik, Ws, Hs, cx, cy, r, c);
        void RadialS(float cx, float cy, float r, Color c) => Radial(statik, Ws, Hs, cx, cy, r, c);
        void PolyS(Vector2[] p, Color c) => Poly(statik, Ws, Hs, p, p.Length, c);

        // dynamic wrappers (full-screen coords -> band-local @ Wd,Hd)
        float MX(float cx) => cx * ds;
        float MY(float cy) => (cy - bandTopPx) * ds;
        readonly Vector2[] _scratch = new Vector2[16];
        void DiscD(float cx, float cy, float r, Color c) => Disc(dyn, Wd, Hd, MX(cx), MY(cy), r * ds, c);
        void PolyD(Vector2[] p, Color c)
        {
            int n = Mathf.Min(p.Length, _scratch.Length);
            for (int i = 0; i < n; i++) _scratch[i] = new Vector2(MX(p[i].x), MY(p[i].y));
            Poly(dyn, Wd, Hd, _scratch, n, c);
        }

        static void Stretch(RectTransform rt) { rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one; rt.offsetMin = Vector2.zero; rt.offsetMax = Vector2.zero; }
    }

    /// <summary>A single warm bokeh mote: drifts up (anchor fraction), wraps at the top, twinkles its alpha.</summary>
    public class MenuBokeh : MonoBehaviour
    {
        public Image img; public float vyF, ph, baseA;
        RectTransform rt;
        void Awake() { rt = (RectTransform)transform; }
        void Update()
        {
            float fy = rt.anchorMin.y + vyF * Time.unscaledDeltaTime, fx = rt.anchorMin.x;
            if (fy > 0.58f) { fy = -0.02f; fx = UnityEngine.Random.value; }   // rose past 0.42H -> respawn at the bottom
            rt.anchorMin = rt.anchorMax = new Vector2(fx, fy);
            if (img != null) { float tw = 0.6f + 0.4f * Mathf.Sin(Time.unscaledTime * 1.3f + ph); var col = img.color; col.a = baseA * tw; img.color = col; }
        }
    }
}
