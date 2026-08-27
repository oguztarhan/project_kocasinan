using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.SceneManagement;

namespace Ridebury
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
        const float BandTopF = 0.59f, BandBotF = 0.97f;   // traffic band buffer; v2 keeps both lanes in its UPPER part (0.685 & 0.745) and leaves the lower road clear
        RawImage staticImg, dynImg;
        Texture2D staticTex, dynTex;
        Sprite shipSprite;                // the boat (HTML ship()) — rasterised once, slid + bobbed by MenuShip
        Color[] statik, dyn;
        int Ws, Hs, Wd, Hd;
        float ds, bandTopPx, DPR, M;
        float lastT, acc;
        GameObject oldBackground;

        // ---------- scene geometry (HTML geo()) ----------
        float horizonY, seaBot, roadTop, roadBot, sunX, sunY, swFeet, panelTop, busXf;

        class Veh { public float x, len; public bool bus; public Color col; }
        class Lane { public float y, S, baseSpd, gap, ph; public List<Veh> V; }
        List<Lane> lanes;

        // ---- HTML v3_family: animated sidewalk crowd (a 2nd dynamic band over the boardwalk) ----
        const float PplTopF = 0.49f, PplBotF = 0.63f;
        RawImage peopleImg; Texture2D peopleTex; Color[] ppl;
        int Wp, Hp; float dsP, bandTopPxP, SPDK;
        class Ped { public float x, y, S, dir, ph, pace, spd; public Color col, skin, hair; }
        class Waiter { public Color col, skin, hair; public float ph, dir; }
        List<Ped> peds; List<Waiter> waiters;
        float dogX, dogDir, dogPh, dogSpd; Color dogCol; bool hasDog;
        float _fx, _fy, _fs, _fdir, _frot, _frotCy; bool _frotOn;   // figure-local -> world transform (mirrors the canvas matrix)
        static readonly Color[] SKN = { Hex("#F2C49B"), Hex("#E7B488"), Hex("#C98D60"), Hex("#9B6A45"), Hex("#F0CBA8") };
        static readonly Color[] HRC = { Hex("#34261B"), Hex("#1C1A19"), Hex("#5A3B22"), Hex("#C9A24B"), Hex("#6E6E6E"), Hex("#2A2A2A") };
        static readonly Color[] CLOTH = { Hex("#FF8FB8"), Hex("#5BA4E8"), Hex("#FFD23F"), Hex("#46AE4A"), Hex("#E8742B"), Hex("#FF6B8A"), Hex("#7AA0F0"), Hex("#C0392B") };
        static readonly Color[] DGC = { Hex("#8B5E3C"), Hex("#C9A24B"), Hex("#3A3A3A"), Hex("#E8E2D6"), Hex("#A0522D") };

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
            sc.matchWidthOrHeight = 0f;   // match WIDTH (portrait): fits the screen width on any aspect
            var rootRT = cgo.GetComponent<RectTransform>();

            Ws = Mathf.Clamp(Screen.width, 720, 1080);
            Hs = Mathf.Clamp(Mathf.RoundToInt(Ws * (float)Screen.height / Mathf.Max(1, Screen.width)), 1000, 2400);
            DPR = Ws / 390f;
            M = Mathf.Min(Ws, Hs);
            horizonY = Hs * 0.38f; seaBot = Hs * 0.52f; roadTop = Hs * 0.62f; roadBot = Hs;   // v1: road + traffic fill the bottom again
            panelTop = Hs * 0.70f; swFeet = Hs * 0.61f; busXf = Ws * 0.70f;                    // the family walks a seafront PROMENADE between sea and road
            sunX = Ws * 0.30f; sunY = Hs * 0.34f;

            statik = new Color[Ws * Hs];
            BuildLanes();
            BuildStatic();

            // Palms live on a SEPARATE foreground layer (their own transparent buffer) so they sit IN FRONT of
            // the ship — the boat sails BEHIND them instead of clipping over them. The *S draw helpers target the
            // `statik` field, so we briefly point it at this buffer, draw the palms, then restore the backdrop.
            var fg = new Color[Ws * Hs];
            var keepBuf = statik; statik = fg; Palms(); statik = keepBuf;
            var fgTex = new Texture2D(Ws, Hs, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
            fgTex.SetPixels(fg); fgTex.Apply(false);

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

            BuildShip(rootRT);      // the boat drifting across the sea (above the static backdrop, below the panel)

            // Foreground palms: ABOVE the ship (so the boat passes behind them), BELOW the dark fade (so it tints
            // them with the rest of the lower scene). Full-stretch, matching the backdrop's placement exactly.
            var pgo = new GameObject("PalmsFg", typeof(RectTransform), typeof(RawImage));
            pgo.transform.SetParent(rootRT, false); Stretch(pgo.GetComponent<RectTransform>());
            var pim = pgo.GetComponent<RawImage>(); pim.raycastTarget = false; pim.texture = fgTex;

            BuildSidewalk(rootRT);  // v1: promenade props (bus stop + planter + bench + bush), over the palms
            SetupPeople();          // the walking family + waiting passengers + dog
            BuildPeopleBand(rootRT);// their own animated band, on the promenade
            BuildVignette(rootRT);  // v1: soft dark vignette in the corners (replaces the old dark panel + bokeh)

            lastT = Time.unscaledTime;
            DrawDynamic(0f); ApplyDyn();
            DrawPeople(0f, 0f); ApplyPpl();
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
            DrawPeople(t, dt); ApplyPpl();
        }

        void ApplyDyn() { dynTex.SetPixels(dyn); dynTex.Apply(false); }

        // =====================================================================================
        //  lanes (HTML mkLane/fillLane/vf)
        // =====================================================================================
        void BuildLanes()
        {
            lanes = new List<Lane>
            {
                MkLane(Hs * 0.685f, M * 0.0014f, 18f * DPR),
                MkLane(Hs * 0.745f, M * 0.0021f, 26f * DPR),  // v2: both lanes pulled up into a tight band; the lower road stays clear for the menu buttons
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
            Sky(); Sun(); SunBloom(); Clouds(); Sea(); Headland(); Haze(); Beach(); Fence(); Road(); RoadSheen(); LaneDash(); WarmOverlay(); // v1 ambiance; palms are a separate fg layer
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
            FillRectS(0, (int)(seaBot + 8 * DPR), Ws, (int)(roadTop - (seaBot + 8 * DPR)), RGBA(0.816f, 0.659f, 0.424f, 0.22f)); // v1 promenade tint
            Color ps = RGBA(0.588f, 0.439f, 0.290f, 0.22f);
            for (float x = -Ws * 0.1f; x <= Ws * 1.1f; x += Ws * 0.06f)
                LineS(x, seaBot + 8 * DPR, x + (x - Ws / 2f) * 0.05f, roadTop, 1.6f * DPR, ps);     // perspective seams toward the road
        }

        void Fence()   // v1: a seafront railing along the promenade edge (replaces the old fence)
        {
            float ry = seaBot + 4f * DPR;
            FillRectS(0, (int)ry, Ws, Mathf.Max(1, (int)(2.4f * DPR)), RGBA(0.965f, 0.925f, 0.871f, 0.85f));            // top rail
            FillRectS(0, (int)(ry + 5 * DPR), Ws, Mathf.Max(1, (int)(1.5f * DPR)), RGBA(0.886f, 0.824f, 0.745f, 0.7f)); // lower rail
            int ph = Mathf.Max(1, (int)(10 * DPR)); Color pc = RGBA(0.925f, 0.878f, 0.808f, 0.8f);
            for (float x = Ws * 0.02f; x < Ws; x += Ws * 0.05f) FillRectS((int)x, (int)ry, Mathf.Max(1, (int)(2 * DPR)), ph, pc); // posts
        }

        // Faithful port of the HTML palm(): 4 palms on the beach, full 8-frond crown, trunk rings, 3 coconuts.
        void Palms()
        {
            float[] pp = { 0.10f, 0.34f, 0.62f, 0.88f };               // HTML pp
            float baseY = seaBot + (roadTop - seaBot) * 0.25f;         // on the beach, not at the road edge
            for (int i = 0; i < pp.Length; i++) Palm(Ws * pp[i], baseY, M * 0.020f);
        }
        void Palm(float x, float baseY, float s)
        {
            // trunk (front + shaded side), full HTML height 5.6*s
            PolyS(new[] { new Vector2(x - 0.5f * s, baseY), new Vector2(x + 0.5f * s, baseY), new Vector2(x + 0.30f * s, baseY - 5.6f * s), new Vector2(x - 0.30f * s, baseY - 5.6f * s) }, PalmTrunk);
            PolyS(new[] { new Vector2(x + 0.06f * s, baseY), new Vector2(x + 0.5f * s, baseY), new Vector2(x + 0.30f * s, baseY - 5.6f * s), new Vector2(x + 0.06f * s, baseY - 5.6f * s) }, Shade(PalmTrunk, 0.80f));
            // 4 trunk rings
            Color ring = Shade(PalmTrunk, 0.7f);
            for (int i = 1; i <= 4; i++)
                FillRectS(Mathf.RoundToInt(x - 0.42f * s), Mathf.RoundToInt(baseY - 1.15f * i * s), Mathf.Max(1, Mathf.RoundToInt(0.84f * s)), Mathf.Max(1, Mathf.RoundToInt(0.12f * s)), ring);
            // crown: 8 fronds rotated about the trunk top (full spread; no sway baked in)
            float tx = x, ty = baseY - 5.6f * s;
            Color[] fr = { Hex("#2f8f3e"), Hex("#3FA45B"), Hex("#46B86A"), Hex("#3FA45B"), Hex("#2f8f3e"), Hex("#46B86A"), Hex("#3FA45B"), Hex("#2f8f3e") };
            for (int i = 0; i < 8; i++)
            {
                float a = -2.40f + i * 0.685f, ca = Mathf.Cos(a), sa = Mathf.Sin(a);
                Vector2 Rf(float px, float py) => new Vector2(tx + px * ca - py * sa, ty + px * sa + py * ca);
                PolyS(new[] { Rf(0, 0), Rf(3.8f * s, -0.6f * s), Rf(4.0f * s, 0.2f * s), Rf(3.4f * s, 0.7f * s) }, fr[i]);
            }
            // 3 coconuts at the crown base
            DiscS(tx, ty, 0.6f * s, Hex("#6f4a2a"));
            DiscS(tx - 0.55f * s, ty + 0.45f * s, 0.42f * s, Hex("#7a5230"));
            DiscS(tx + 0.55f * s, ty + 0.45f * s, 0.42f * s, Hex("#7a5230"));
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

        // ---- v1 ambiance (HTML headland/haze/sun-bloom/road-sheen + a corner vignette) ----
        void SunBloom() { RadialS(sunX, sunY, Hs * 0.17f, RGBA(1f, 0.949f, 0.804f, 0.33f)); }   // extra warm bloom around the sun

        void Headland()   // two distant promontories at the horizon
        {
            PolyS(new[] { new Vector2(-Ws * 0.05f, horizonY + 2 * DPR), new Vector2(Ws * 0.07f, horizonY - Hs * 0.02f), new Vector2(Ws * 0.19f, horizonY - Hs * 0.010f), new Vector2(Ws * 0.30f, horizonY + 2 * DPR) }, RGBA(0.478f, 0.337f, 0.384f, 0.45f));
            PolyS(new[] { new Vector2(Ws * 0.64f, horizonY + 2 * DPR), new Vector2(Ws * 0.77f, horizonY - Hs * 0.028f), new Vector2(Ws * 0.93f, horizonY - Hs * 0.012f), new Vector2(Ws * 1.06f, horizonY + 2 * DPR) }, RGBA(0.439f, 0.314f, 0.369f, 0.42f));
        }

        void Haze()   // warm haze band hugging the horizon
        {
            int y0 = (int)(horizonY - Hs * 0.05f), y1 = (int)(horizonY + Hs * 0.06f);
            for (int y = Mathf.Max(0, y0); y < y1 && y < Hs; y++) { float f = (float)(y - y0) / Mathf.Max(1, y1 - y0); float a = (1f - Mathf.Abs(f - 0.5f) * 2f) * 0.26f; if (a > 0f) FillRectS(0, y, Ws, 1, RGBA(1f, 0.808f, 0.588f, a)); }
        }

        void RoadSheen()   // warm sheen over the road, fading down
        {
            int y0 = (int)roadTop, y1 = (int)roadBot;
            for (int y = Mathf.Max(0, y0); y < y1 && y < Hs; y++) { float f = (float)(y - y0) / Mathf.Max(1, y1 - y0); float a = f < 0.35f ? Mathf.Lerp(0.10f, 0.035f, f / 0.35f) : Mathf.Lerp(0.035f, 0f, (f - 0.35f) / 0.65f); FillRectS(0, y, Ws, 1, RGBA(1f, 0.62f, 0.40f, a)); }
        }

        // Soft dark vignette in the corners (a radial sprite stretched full-screen) — replaces the old dark panel.
        void BuildVignette(RectTransform root)
        {
            int S = 128; var tex = new Texture2D(S, S, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
            var buf = new Color[S * S]; float c = (S - 1) * 0.5f;
            for (int y = 0; y < S; y++) for (int x = 0; x < S; x++) { float d = Mathf.Sqrt((x - c) * (x - c) + (y - c) * (y - c)) / c; float a = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.55f, 1.05f, d)) * 0.34f; buf[y * S + x] = new Color(0.122f, 0.047f, 0.094f, a); }
            tex.SetPixels(buf); tex.Apply(false);
            var go = new GameObject("Vignette", typeof(RectTransform), typeof(Image));
            go.transform.SetParent(root, false); Stretch(go.GetComponent<RectTransform>());
            var im = go.GetComponent<Image>(); im.raycastTarget = false; im.type = Image.Type.Simple;
            im.sprite = Sprite.Create(tex, new Rect(0, 0, S, S), new Vector2(0.5f, 0.5f), 100, 0, SpriteMeshType.FullRect);
        }

        // =====================================================================================
        //  HTML v3_family — the BOARDWALK + its furniture (bus stop, planter, bench, bush), baked into a layer that
        //  sits OVER the dark fade (bright foreground, exactly like the HTML draws them after the panel). Static.
        //  The walking family + dog + waiters are a separate animated band (added later, over this).
        // =====================================================================================
        void BuildSidewalk(RectTransform root)
        {
            var buf = new Color[Ws * Hs];
            var keep = statik; statik = buf;     // *S helpers target `statik`; borrow it for this layer, then restore
            BusStopDraw(busXf, swFeet, M * 0.0016f);                       // v1: props sit on the promenade (no boardwalk)
            Planter(Ws * 0.13f, swFeet, M * 0.0020f, Hex("#FF8FB8"));
            Bench(Ws * 0.31f, swFeet, M * 0.0020f);
            Bush(Ws * 0.50f, swFeet, M * 0.0036f, Hex("#3FA45B"));
            statik = keep;
            var tex = new Texture2D(Ws, Hs, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
            tex.SetPixels(buf); tex.Apply(false);
            var go = new GameObject("PromenadeProps", typeof(RectTransform), typeof(RawImage));
            go.transform.SetParent(root, false); Stretch(go.GetComponent<RectTransform>());
            var im = go.GetComponent<RawImage>(); im.raycastTarget = false; im.texture = tex;
        }

        void Boardwalk()
        {
            float py0 = roadBot + 13f * DPR, py1 = Hs * 0.93f;
            FillRectS(0, (int)roadBot, Ws, Mathf.Max(1, (int)(6 * DPR)), Hex("#D8CBBD"));               // curb (light)
            FillRectS(0, (int)(roadBot + 6f * DPR), Ws, Mathf.Max(1, (int)(7 * DPR)), Hex("#8C7F72"));  // curb (dark)
            FillRectS(0, (int)py0, Ws, (int)(py1 - py0), Hex("#B9A99B"));                               // boardwalk slab
            Color seam = RGBA(0.424f, 0.376f, 0.329f, 0.5f);
            for (float sx = -Ws * 0.1f; sx <= Ws * 1.1f; sx += Ws * 0.07f)
                LineS(sx, py0, sx + (sx - Ws / 2f) * 0.06f, py1, 2f * DPR, seam);                       // perspective seams
            FillRectS(0, (int)py0, Ws, (int)(py1 - py0), RGBA(1f, 0.588f, 0.353f, 0.07f));              // warm wash
        }

        // a thin line in the *S buffer (which has no stroke op), drawn as one quad
        void LineS(float x0, float y0, float x1, float y1, float w, Color col)
        {
            float dx = x1 - x0, dy = y1 - y0, L = Mathf.Sqrt(dx * dx + dy * dy); if (L < 0.001f) return;
            float px = -dy / L * w * 0.5f, py = dx / L * w * 0.5f;
            PolyS(new[] { new Vector2(x0 + px, y0 + py), new Vector2(x1 + px, y1 + py), new Vector2(x1 - px, y1 - py), new Vector2(x0 - px, y0 - py) }, col);
        }

        // HTML busStop(): glass shelter + posts + red roof + bench + a sign post/box, scaled about (cx, feet).
        void BusStopDraw(float cx, float fy, float S)
        {
            void FR(float lx, float ly, float w, float h, Color c) => FillRectS(Mathf.RoundToInt(cx + lx * S), Mathf.RoundToInt(fy + ly * S), Mathf.Max(1, Mathf.RoundToInt(w * S)), Mathf.Max(1, Mathf.RoundToInt(h * S)), c);
            Vector2 P(float lx, float ly) => new Vector2(cx + lx * S, fy + ly * S);
            float w = 120f, h = 116f, post = 6f; Color red = Hex("#E1352B");
            FR(-w / 2 + 8, -h + 8, w - 16, h - 8, RGBA(0.706f, 0.831f, 0.894f, 0.22f));   // glass
            FR(-w / 2 + 8, -h + 8, (w - 16) * 0.4f, h - 8, RGBA(1, 1, 1, 0.10f));         // glass highlight
            FR(-w / 2, -h, post, h, Hex("#6B7077")); FR(w / 2 - post, -h, post, h, Hex("#6B7077")); // posts
            PolyS(new[] { P(-w / 2 - 14, -h), P(w / 2 + 14, -h), P(w / 2 + 20, -h - 15), P(-w / 2 - 8, -h - 15) }, red);                 // roof
            PolyS(new[] { P(-w / 2 - 14, -h), P(w / 2 + 14, -h), P(w / 2 + 14, -h + 6), P(-w / 2 - 14, -h + 6) }, Shade(red, 0.78f));     // roof edge
            FR(-w / 2 + 16, -40, w - 40, 8, Hex("#8a5a34"));                              // bench seat
            FR(-w / 2 + 22, -32, 6, 32, Hex("#5a4326")); FR(w / 2 - 34, -32, 6, 32, Hex("#5a4326")); // bench legs
            FR(-w / 2 - 40, -h - 4, 5, h + 4, Hex("#9aa0a6"));                            // sign pole
            FR(-w / 2 - 62, -h - 32, 52, 30, Hex("#2C6FD0"));                             // sign box
            FR(-w / 2 - 56, -h - 26, 40, 15, Hex("#FFFFFF"));                             // sign panel
            FR(-w / 2 - 53, -h - 23, 8, 7, Hex("#2C6FD0")); FR(-w / 2 - 43, -h - 23, 8, 7, Hex("#2C6FD0")); FR(-w / 2 - 33, -h - 23, 8, 7, Hex("#2C6FD0")); // route bars
            DiscS(cx + (-w / 2 - 50) * S, fy + (-h - 10) * S, 2.3f * S, Hex("#2b2b33")); DiscS(cx + (-w / 2 - 30) * S, fy + (-h - 10) * S, 2.3f * S, Hex("#2b2b33")); // dots
        }

        void Planter(float cx, float fy, float S, Color fl)
        {
            void FR(float lx, float ly, float w, float h, Color c) => FillRectS(Mathf.RoundToInt(cx + lx * S), Mathf.RoundToInt(fy + ly * S), Mathf.Max(1, Mathf.RoundToInt(w * S)), Mathf.Max(1, Mathf.RoundToInt(h * S)), c);
            FR(-16, -12, 32, 12, Hex("#9a8a78"));                                         // box
            DiscS(cx - 8 * S, fy - 16 * S, 5 * S, Hex("#46B86A")); DiscS(cx, fy - 18 * S, 5 * S, Hex("#3FA45B")); DiscS(cx + 8 * S, fy - 16 * S, 5 * S, Hex("#46B86A")); // greenery
            if (fl.a > 0f) { DiscS(cx - 8 * S, fy - 20 * S, 2.4f * S, fl); DiscS(cx + 2 * S, fy - 22 * S, 2.4f * S, fl); DiscS(cx + 9 * S, fy - 19 * S, 2.4f * S, fl); } // flowers
        }

        void Bench(float cx, float fy, float S)
        {
            void FR(float lx, float ly, float w, float h, Color c) => FillRectS(Mathf.RoundToInt(cx + lx * S), Mathf.RoundToInt(fy + ly * S), Mathf.Max(1, Mathf.RoundToInt(w * S)), Mathf.Max(1, Mathf.RoundToInt(h * S)), c);
            FR(-26, -16, 52, 5, Hex("#8a5a34")); FR(-26, -30, 52, 4, Hex("#8a5a34"));     // seat + backrest
            FR(-22, -16, 5, 16, Hex("#5a4326")); FR(17, -16, 5, 16, Hex("#5a4326"));      // legs
        }

        void Bush(float cx, float fy, float s, Color col)
        {
            DiscS(cx, fy - 0.7f * s, 0.95f * s, col);
            DiscS(cx - 0.7f * s, fy - 0.4f * s, 0.6f * s, Shade(col, 0.9f));
            DiscS(cx + 0.7f * s, fy - 0.4f * s, 0.6f * s, Shade(col, 1.06f));
        }

        // HTML's soft dark fade (py0=H*0.55 -> H), as a UGUI gradient Image ON TOP of the traffic so the cars fade
        // into the dark exactly like the HTML (panel drawn after the vehicles).
        void BuildPanel(RectTransform root)
        {
            var go = new GameObject("BottomPanel", typeof(RectTransform), typeof(Image));
            go.transform.SetParent(root, false);
            var im = go.GetComponent<Image>(); im.sprite = PanelSprite(); im.type = Image.Type.Simple; im.raycastTarget = false;
            var rt = im.rectTransform;
            rt.anchorMin = new Vector2(0f, 0f); rt.anchorMax = new Vector2(1f, 0.38f);    // canvas-down [0.62, 1.0] — over the road, behind the buttons
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
        static Color PanelGrad(float f)     // f=0 top (soft) .. f=1 bottom (dark): dims the road so the buttons read clean
        {
            Color s0 = RGBA(0.106f, 0.055f, 0.086f, 0.12f), s1 = RGBA(0.094f, 0.047f, 0.075f, 0.55f), s2 = RGBA(0.078f, 0.039f, 0.063f, 0.86f);
            if (f < 0.5f) return Color.Lerp(s0, s1, f / 0.5f);
            return Color.Lerp(s1, s2, (f - 0.5f) / 0.5f);
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
        //  SHIP (HTML ship()) — a red boat with the INTAKE ENTERTAINMENT banner, drifting across the sea.
        //  Rasterised ONCE into a sprite (cheap); MenuShip slides + bobs it. The brand text rides on top as a
        //  real Text child so it stays crisp at any size. Sits on the sea, above the backdrop, below the panel.
        // =====================================================================================
        void BuildShip(RectTransform root)
        {
            const float hw = 210f, hh = 44f, q = 2f;   // hull width/height (local units), q = px per local unit
            const int margin = 6;
            int bw = (int)(270f * q) + margin * 2;     // local x span [-135, 135] (symmetric -> pivot.x = 0.5)
            int bh = (int)(168f * q) + margin * 2;     // local y span [-138, 30]
            var buf = new Color[bw * bh];              // all (0,0,0,0) -> transparent
            float ox = margin + 135f * q, oy = margin + 138f * q;  // local (0,0) = waterline centre
            Vector2 P(float lx, float ly) => new Vector2(ox + lx * q, oy + ly * q);
            void Pl(Vector2[] p, Color c) => Poly(buf, bw, bh, p, p.Length, c);
            Color hull = Hex("#D63A2E"), white = Hex("#F5F1E8"), cabTop = Hex("#E6ECF1"), win = Hex("#7FD0EE"), mast = Hex("#5a4530"), flag = Hex("#FFC14D");

            Pl(new[] { P(-hw / 2 + 14, 3), P(hw / 2 - 14, 3), P(hw / 2 - 24, 30), P(-hw / 2 + 24, 30) }, RGBA(0.839f, 0.227f, 0.180f, 0.16f)); // hull shadow
            Pl(new[] { P(-hw / 2, -hh), P(hw / 2 - 14, -hh), P(hw / 2 + 30, -hh / 2), P(hw / 2 - 14, 0), P(-hw / 2, 0) }, hull);              // hull
            Pl(new[] { P(-hw / 2, -hh), P(hw / 2 - 12, -hh), P(hw / 2 - 8, -hh * 0.5f), P(-hw / 2, -hh * 0.5f) }, white);                     // white stripe
            Pl(new[] { P(-hw / 2, -hh * 0.30f), P(hw / 2 - 10, -hh * 0.30f), P(hw / 2 + 22, -hh / 2), P(hw / 2 - 10, 0), P(-hw / 2, 0) }, Shade(hull, 0.82f)); // hull shade
            Pl(new[] { P(-hw * 0.34f, -hh - 46), P(hw * 0.16f, -hh - 46), P(hw * 0.20f, -hh), P(-hw * 0.40f, -hh) }, Color.white);           // cabin
            Pl(new[] { P(-hw * 0.34f, -hh - 46), P(hw * 0.16f, -hh - 46), P(hw * 0.16f, -hh - 40), P(-hw * 0.34f, -hh - 40) }, cabTop);      // cabin top
            for (int k = 0; k < 5; k++) { float wx = -hw * 0.30f + k * hw * 0.094f; Pl(new[] { P(wx, -hh - 36), P(wx + hw * 0.052f, -hh - 36), P(wx + hw * 0.052f, -hh - 18), P(wx, -hh - 18) }, win); } // windows
            Pl(new[] { P(0, -hh - 72), P(hw * 0.12f, -hh - 72), P(hw * 0.10f, -hh - 46), P(hw * 0.02f, -hh - 46) }, hull);                   // smokestack
            Pl(new[] { P(hw * 0.01f, -hh - 66), P(hw * 0.11f, -hh - 66), P(hw * 0.11f, -hh - 60), P(hw * 0.01f, -hh - 60) }, white);         // stack band
            float mx = -hw * 0.30f;
            Pl(new[] { P(mx - 1.5f, -hh - 46), P(mx + 1.5f, -hh - 46), P(mx + 1.5f, -hh - 94), P(mx - 1.5f, -hh - 94) }, mast);             // mast
            Pl(new[] { P(mx, -hh - 94), P(mx + 36, -hh - 86), P(mx, -hh - 78) }, flag);                                                     // flag

            var tex = new Texture2D(bw, bh, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
            tex.SetPixels(buf); tex.Apply(false);
            float pivX = ox / bw, pivY = 1f - oy / bh;   // waterline centre, in texture (y-up) fractions
            shipSprite = Sprite.Create(tex, new Rect(0, 0, bw, bh), new Vector2(pivX, pivY), 100f, 0, SpriteMeshType.FullRect);

            const float kScale = 1.6f;   // ref units per local unit -> hull ≈ 31% of the 1080-wide canvas (matches the HTML)
            var sgo = new GameObject("Ship", typeof(RectTransform), typeof(Image));
            sgo.transform.SetParent(root, false);
            var sim = sgo.GetComponent<Image>(); sim.sprite = shipSprite; sim.raycastTarget = false;
            var srt = sim.rectTransform;
            srt.pivot = new Vector2(pivX, pivY);                       // pivot at the waterline -> rocks naturally
            srt.anchorMin = srt.anchorMax = new Vector2(0.32f, 0.533f); // v1 sea line (HTML _shY for the higher sea); palms render IN FRONT so the boat sails behind them; x animated
            srt.sizeDelta = new Vector2((bw / q) * kScale, (bh / q) * kScale);
            srt.anchoredPosition = Vector2.zero;

            // INTAKE ENTERTAINMENT on the hull's white stripe (a child, so it rides + rocks with the ship)
            var tgo = new GameObject("Brand", typeof(RectTransform), typeof(Text));
            tgo.transform.SetParent(srt, false);
            var tt = tgo.GetComponent<Text>();
            tt.font = GameFont.UGUI; tt.text = "INTAKE ENTERTAINMENT"; tt.fontSize = 18; tt.fontStyle = FontStyle.Bold;
            tt.color = Hex("#243046"); tt.alignment = TextAnchor.MiddleCenter; tt.raycastTarget = false;
            tt.horizontalOverflow = HorizontalWrapMode.Overflow; tt.verticalOverflow = VerticalWrapMode.Overflow;
            var trt = tt.rectTransform;
            trt.anchorMin = trt.anchorMax = new Vector2(0.5f, 0.376f); trt.pivot = new Vector2(0.5f, 0.5f);   // centred on the white hull stripe
            trt.sizeDelta = new Vector2(srt.sizeDelta.x * 0.95f, 44f); trt.anchoredPosition = Vector2.zero;

            var sh = sgo.AddComponent<MenuShip>();
            sh.speedF = 0.022f; sh.baseY = 0.533f; sh.bobPx = 6f * DPR;
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

        // =====================================================================================
        //  HTML v3_family — the animated sidewalk crowd: a walking family, waiting passengers and a dog, redrawn each
        //  frame into their own band over the boardwalk. Faithful port of lpPerson/lpDog and the IK limb helpers.
        // =====================================================================================
        void SetupPeople()
        {
            SPDK = 4f * 8f * 6.5f / (2f * Mathf.PI);
            peds = new List<Ped>();
            (float y, float S)[] rws = { (swFeet - 6f * DPR, M * 0.0019f), (swFeet, M * 0.0024f) };
            for (int i = 0; i < 7; i++)                                   // 7 adults, alternating two depth rows
            {
                var rw = rws[i % 2];
                peds.Add(new Ped { x = Rnd() * Ws, y = rw.y, S = rw.S, dir = Rnd() < 0.5f ? 1f : -1f, col = Pick(CLOTH), skin = Pick(SKN), hair = Pick(HRC), ph = Rnd() * 6.28f, pace = 1f, spd = SPDK * rw.S });
            }
            for (int i = 0; i < 2; i++)                                   // 2 children (smaller, a touch slower)
            {
                float sc = M * 0.0024f * 0.68f;
                peds.Add(new Ped { x = Rnd() * Ws, y = swFeet, S = sc, dir = Rnd() < 0.5f ? 1f : -1f, col = Pick(CLOTH), skin = Pick(SKN), hair = Pick(HRC), ph = Rnd() * 6.28f, pace = 0.95f, spd = SPDK * sc * 0.95f });
            }
            waiters = new List<Waiter>();
            for (int i = 0; i < 3; i++)                                   // 3 passengers waiting at the stop (idle)
                waiters.Add(new Waiter { col = Pick(CLOTH), skin = Pick(SKN), hair = Pick(HRC), ph = Rnd() * 6.28f, dir = (i % 2 != 0) ? -1f : 1f });
            hasDog = true;
            dogX = Rnd() * Ws; dogDir = Rnd() < 0.5f ? 1f : -1f; dogCol = Pick(DGC); dogPh = Rnd() * 6.28f; dogSpd = 30f * DPR;
        }
        Color Pick(Color[] a) => a[(int)(Rnd() * a.Length) % a.Length];

        void BuildPeopleBand(RectTransform root)
        {
            bandTopPxP = PplTopF * Hs;
            Wp = Mathf.Clamp(Mathf.RoundToInt(Ws * 0.9f), 600, 1080);
            dsP = Wp / (float)Ws;
            Hp = Mathf.Max(1, Mathf.RoundToInt((PplBotF - PplTopF) * Hs * dsP));
            ppl = new Color[Wp * Hp];
            peopleTex = new Texture2D(Wp, Hp, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
            var go = new GameObject("PeopleDynamic", typeof(RectTransform), typeof(RawImage));
            go.transform.SetParent(root, false);
            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = new Vector2(0f, 1f - PplBotF); rt.anchorMax = new Vector2(1f, 1f - PplTopF);
            rt.offsetMin = Vector2.zero; rt.offsetMax = Vector2.zero;
            peopleImg = go.GetComponent<RawImage>(); peopleImg.raycastTarget = false; peopleImg.texture = peopleTex;
        }
        void ApplyPpl() { peopleTex.SetPixels(ppl); peopleTex.Apply(false); }

        void DrawPeople(float t, float dt)
        {
            System.Array.Clear(ppl, 0, ppl.Length);
            foreach (var p in peds)
            {
                p.x += p.dir * p.spd * dt;
                if (p.x > Ws + 60f * DPR) p.x = -60f * DPR; else if (p.x < -60f * DPR) p.x = Ws + 60f * DPR;
            }
            if (hasDog) { dogX += dogDir * dogSpd * dt; if (dogX > Ws + 60f * DPR) dogX = -60f * DPR; else if (dogX < -60f * DPR) dogX = Ws + 60f * DPR; }

            Color shW = RGBA(0.196f, 0.133f, 0.102f, 0.25f), shP = RGBA(0.196f, 0.133f, 0.102f, 0.20f);
            float wsS = M * 0.0024f;
            for (int i = 0; i < waiters.Count; i++)                       // waiting passengers (idle) at the stop
            {
                var wq = waiters[i];
                float wx = busXf + (i - (waiters.Count - 1) / 2f) * 0.05f * Ws;
                PShadow(wx, swFeet + 2f * DPR, 13f * wsS, 3.4f * wsS, shW);
                Person(wx, swFeet, wsS, wq.col, wq.skin, wq.hair, wq.dir, t, wq.ph, false, 1f);
            }
            var order = new List<Ped>(peds); order.Sort((a, b) => a.y.CompareTo(b.y));
            foreach (var p in order)                                      // walking family (depth-sorted, far first)
            {
                PShadow(p.x, p.y + 2f * DPR, 12f * p.S, 3.2f * p.S, shP);
                Person(p.x, p.y, p.S, p.col, p.skin, p.hair, p.dir, t, p.ph, true, p.pace);
            }
            if (hasDog) { float dS = M * 0.0016f; PShadow(dogX, swFeet + 2f * DPR, 16f * dS, 3f * dS, shP); Dog(dogX, swFeet, dS, dogCol, dogDir, t, dogPh); }
        }

        // ---- figure drawing (person-local coords -> people band) ----
        static Vector2 V(float x, float y) => new Vector2(x, y);
        float MXp(float cx) => cx * dsP;
        float MYp(float cy) => (cy - bandTopPxP) * dsP;
        Vector2 FL(float lx, float ly)   // figure-local -> world; applies optional torso rotation, then scale/flip/translate
        {
            if (_frotOn) { float c = Mathf.Cos(_frot), s = Mathf.Sin(_frot), ry = ly - _frotCy, nx = lx * c - ry * s, ny = lx * s + ry * c + _frotCy; lx = nx; ly = ny; }
            return new Vector2(_fx + lx * _fs * _fdir, _fy + ly * _fs);
        }
        readonly Vector2[] _fpoly = new Vector2[12];
        void PPoly(Vector2[] local, Color col)
        {
            int n = Mathf.Min(local.Length, _fpoly.Length);
            for (int i = 0; i < n; i++) { var w = FL(local[i].x, local[i].y); _fpoly[i] = new Vector2(MXp(w.x), MYp(w.y)); }
            Poly(ppl, Wp, Hp, _fpoly, n, col);
        }
        void PDisc(float lx, float ly, float r, Color col) { var w = FL(lx, ly); Disc(ppl, Wp, Hp, MXp(w.x), MYp(w.y), r * _fs * dsP, col); }
        void PQuad(float x0, float y0, float x1, float y1, float w0, float w1, Color col)
        {
            float dx = x1 - x0, dy = y1 - y0, L = Mathf.Sqrt(dx * dx + dy * dy); if (L < 1e-4f) L = 1f; float px = -dy / L, py = dx / L;
            PPoly(new[] { V(x0 + px * w0, y0 + py * w0), V(x0 - px * w0, y0 - py * w0), V(x1 - px * w1, y1 - py * w1), V(x1 + px * w1, y1 + py * w1) }, col);
        }
        void PFoot(float fx, float fy, float pitch, Color col)
        {
            float c = Mathf.Cos(-pitch), s = Mathf.Sin(-pitch);
            Vector2 R(float lx, float ly) => V(fx + lx * c - ly * s, fy + lx * s + ly * c);
            PPoly(new[] { R(-3, -2.6f), R(8, -2.9f), R(9, 0.4f), R(-3, 1.8f) }, col);
        }
        void PLine(float x0, float y0, float x1, float y1, float w, Color col)
        {
            float dx = x1 - x0, dy = y1 - y0, L = Mathf.Sqrt(dx * dx + dy * dy); if (L < 1e-4f) return; float px = -dy / L * w * 0.5f, py = dx / L * w * 0.5f;
            PPoly(new[] { V(x0 + px, y0 + py), V(x1 + px, y1 + py), V(x1 - px, y1 - py), V(x0 - px, y0 - py) }, col);
        }
        void PShadow(float wx, float wy, float rx, float ry, Color col)
        {
            float bx = MXp(wx), by = MYp(wy), brx = rx * dsP, bry = ry * dsP;
            var pts = new Vector2[12]; for (int i = 0; i < 12; i++) { float a = i / 12f * 6.2832f; pts[i] = new Vector2(bx + Mathf.Cos(a) * brx, by + Mathf.Sin(a) * bry); }
            Poly(ppl, Wp, Hp, pts, 12, col);
        }
        Vector2 IK2(float hx, float hy, float fx, float fy, float l1, float l2, bool fwd)
        {
            float dx = fx - hx, dy = fy - hy, d = Mathf.Sqrt(dx * dx + dy * dy);
            d = Mathf.Max(Mathf.Abs(l1 - l2) + 0.01f, Mathf.Min(l1 + l2 - 0.01f, d));
            float bas = Mathf.Atan2(dy, dx), ca = (l1 * l1 + d * d - l2 * l2) / (2f * l1 * d); ca = Mathf.Clamp(ca, -1f, 1f);
            float A = Mathf.Acos(ca), a1 = bas - A, a2 = bas + A, x1 = hx + Mathf.Cos(a1) * l1, x2 = hx + Mathf.Cos(a2) * l1;
            float ang = fwd ? (x1 >= x2 ? a1 : a2) : (x1 < x2 ? a1 : a2);
            return V(hx + Mathf.Cos(ang) * l1, hy + Mathf.Sin(ang) * l1);
        }
        void LegDraw(float fx, float fy, float pitch, float Lt, float Ls, float hipYd, Color col, Color shoe)
        {
            var k = IK2(0, hipYd, fx, fy, Lt, Ls, true);
            PQuad(0, hipYd, k.x, k.y, 3.6f, 2.9f, col); PQuad(k.x, k.y, fx, fy, 2.9f, 2.1f, col);
            PFoot(fx, fy, pitch, shoe); PDisc(k.x, k.y, 2.1f, Shade(col, 0.86f));
        }
        void ArmDraw(float fwd, float sh, Color col, Color skin)
        {
            float ang = fwd * 0.62f, Lu = 12f, Lf = 11f, ex = Mathf.Sin(ang) * Lu, ey = sh + Mathf.Cos(ang) * Lu, fa = ang + 0.32f, hx = ex + Mathf.Sin(fa) * Lf, hy = ey + Mathf.Cos(fa) * Lf;
            PQuad(0, sh, ex, ey, 2.6f, 2.1f, col); PQuad(ex, ey, hx, hy, 2.1f, 1.7f, col); PDisc(hx, hy, 2.2f, skin);
        }
        void Person(float x, float fy0, float S, Color col, Color skin, Color hair, float dir, float t, float ph, bool walk, float pace)
        {
            if (pace <= 0f) pace = 1f;
            _fx = x; _fy = fy0; _fs = S; _fdir = dir; _frotOn = false;
            float Lt = 14f, Ls = 14f, hipY = -26f, STR = 8f, LIFT = 7f, theta = t * 6.5f * pace + ph, bob, sway;
            if (walk) { bob = -1.5f * (0.5f - 0.5f * Mathf.Cos(2f * theta)); sway = 0.05f * Mathf.Sin(theta); }
            else { bob = Mathf.Sin(t * 1.6f + ph) * 0.7f; sway = 0.03f * Mathf.Sin(t * 1.1f + ph); }
            float hipYd = hipY + bob, sh = hipYd - 22f;
            float thN = theta, thF = theta + Mathf.PI, fxN, fyN, piN, fxF, fyF, piF;
            if (walk)
            {
                float lN = Mathf.Max(0f, -Mathf.Sin(thN)) * LIFT, lF = Mathf.Max(0f, -Mathf.Sin(thF)) * LIFT;
                fxN = Mathf.Cos(thN) * STR; fyN = -lN; piN = lN > 0.4f ? 0.45f : 0f;
                fxF = Mathf.Cos(thF) * STR; fyF = -lF; piF = lF > 0.4f ? 0.45f : 0f;
            }
            else { fxN = 3.6f; fyN = 0f; piN = 0f; fxF = -3.6f; fyF = 0f; piF = 0f; }
            float farFwd = walk ? Mathf.Cos(theta) : sway * 0.5f, nearFwd = walk ? -Mathf.Cos(theta) : -sway * 0.5f;
            ArmDraw(farFwd, sh, Shade(col, 0.55f), Shade(skin, 0.85f));                                      // far arm
            LegDraw(fxF, fyF, piF, Lt, Ls, hipYd, Shade(col, 0.6f), Hex("#23232b"));                         // far leg
            _frotOn = true; _frot = sway; _frotCy = hipYd;                                                   // torso sways about the hips
            PPoly(new[] { V(-5.5f, hipYd), V(5.5f, hipYd), V(8f, hipYd - 24f), V(-8f, hipYd - 24f) }, col);
            PPoly(new[] { V(-5.5f, hipYd), V(0f, hipYd), V(0f, hipYd - 24f), V(-8f, hipYd - 24f) }, Shade(col, 0.82f));
            PPoly(new[] { V(-8f, hipYd - 24f), V(8f, hipYd - 24f), V(6f, hipYd - 29f), V(-6f, hipYd - 29f) }, Shade(col, 1.12f));
            PPoly(new[] { V(-2.6f, hipYd - 24f), V(2.6f, hipYd - 24f), V(2.4f, hipYd - 29f), V(-2.4f, hipYd - 29f) }, Shade(skin, 0.9f));
            PDisc(0f, hipYd - 35f, 6.3f, skin);                                                              // head
            PPoly(new[] { V(-6.5f, hipYd - 35f), V(6.5f, hipYd - 35f), V(5f, hipYd - 42f), V(-5.5f, hipYd - 42f) }, hair);
            PPoly(new[] { V(-6.5f, hipYd - 35f), V(-6.5f, hipYd - 31f), V(-3.5f, hipYd - 32f), V(-4.6f, hipYd - 36f) }, hair);
            _frotOn = false;
            LegDraw(fxN, fyN, piN, Lt, Ls, hipYd, Shade(col, 0.82f), Hex("#2b2b33"));                        // near leg
            ArmDraw(nearFwd, sh, Shade(col, 0.95f), skin);                                                   // near arm
        }
        void Dog(float x, float fy, float S, Color col, float dir, float t, float ph)
        {
            _fx = x; _fy = fy; _fs = S; _fdir = dir; _frotOn = false;
            float th = t * 9f + ph, a = Mathf.Sin(th) * 3f, b = Mathf.Sin(th + Mathf.PI) * 3f;
            DLeg(-10f, b, Shade(col, 0.7f)); DLeg(12f, a, Shade(col, 0.7f));                                 // far legs
            PPoly(new[] { V(-17f, -15f), V(14f, -16f), V(18f, -23f), V(10f, -27f), V(-12f, -26f), V(-19f, -21f) }, col); // body
            PPoly(new[] { V(-17f, -15f), V(14f, -16f), V(14f, -13f), V(-17f, -13f) }, Shade(col, 0.85f));
            PPoly(new[] { V(14f, -23f), V(24f, -25f), V(26f, -17f), V(16f, -15f) }, col);                    // head
            PPoly(new[] { V(24f, -25f), V(28f, -22f), V(26f, -17f) }, Shade(col, 0.9f));                     // snout
            PPoly(new[] { V(16f, -27f), V(20f, -31f), V(22f, -25f) }, Shade(col, 0.8f));                     // ear
            PDisc(23f, -20f, 1.1f, Hex("#1b1b1f"));                                                          // eye
            PLine(-17f, -23f, -23f, -27f + Mathf.Sin(th * 1.5f) * 3f, 2.4f, Shade(col, 0.85f));              // wagging tail
            DLeg(-7f, a, Shade(col, 0.95f)); DLeg(10f, b, Shade(col, 0.95f));                                // near legs
        }
        void DLeg(float hx, float off, Color col)
        {
            float fx = hx + off;
            PQuad(hx, -13f, fx, -1f, 2.3f, 1.9f, col);
            PPoly(new[] { V(fx - 2.5f, -2f), V(fx + 4f, -2f), V(fx + 4.6f, 0.6f), V(fx - 2.5f, 1f) }, Shade(col, 0.8f));
        }

        static void Stretch(RectTransform rt) { rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one; rt.offsetMin = Vector2.zero; rt.offsetMax = Vector2.zero; }
    }

    /// <summary>The boat: drifts right across the sea (anchor fraction, wraps), gently bobs + rocks. Rotates about
    /// its waterline pivot so it reads like a ship on swell. Unscaled time so it animates on the paused menu.</summary>
    public class MenuShip : MonoBehaviour
    {
        public float speedF, baseY, bobPx;
        RectTransform rt;
        void Awake() { rt = (RectTransform)transform; }
        void Update()
        {
            float fx = rt.anchorMin.x + speedF * Time.unscaledDeltaTime;
            if (fx > 1.25f) fx = -0.25f;                                  // sailed off the right -> wrap to the left
            rt.anchorMin = rt.anchorMax = new Vector2(fx, baseY);
            rt.anchoredPosition = new Vector2(0f, Mathf.Sin(Time.unscaledTime * 1.1f) * bobPx);   // bob
            rt.localRotation = Quaternion.Euler(0f, 0f, Mathf.Sin(Time.unscaledTime * 0.9f) * 1.2f); // rock
        }
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
