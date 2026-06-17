using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.SceneManagement;

namespace BusJam
{
    /// <summary>
    /// App-launch sequence — recreated in Unity from the HTML mockups:
    ///   1) INTAKE ENTERTAINMENT splash (logo + sweeping speedometer gauge) ~2.2s
    ///   2) BUS JAM "loading" screen (synthwave backdrop + colourful title + progress bar) ~3s
    ///   3) fade away to reveal the existing MainMenu scene underneath.
    ///
    /// SELF-SPAWNS via [RuntimeInitializeOnLoadMethod] on the FIRST app launch into MainMenu — no scene
    /// or Inspector wiring (same pattern as FpsCounter). It only overlays + fades + destroys, so it can
    /// never break gameplay; to remove the whole thing, just delete this one file. Runs on UNSCALED time.
    /// </summary>
    public class BootSplash : MonoBehaviour
    {
        static bool shown;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot()
        {
            if (shown) return;
            // Only the real launch into the menu — NOT when a dev presses Play in the gameplay scene.
            if (SceneManager.GetActiveScene().name != "MainMenu") return;
            shown = true;
            var go = new GameObject("BootSplash");
            DontDestroyOnLoad(go);
            go.AddComponent<BootSplash>();
        }

        // ---- palette (from the mockups) ----
        static Color Hex(string h) { ColorUtility.TryParseHtmlString(h, out var c); return c; }
        static readonly Color Amber = Hex("#FFC14D"), Coral = Hex("#FF6B4A"), Ink = Hex("#F6F8FF");
        static readonly Color[] LetterCols = {
            Hex("#FF4D6D"), Hex("#FF9E2C"), Hex("#FFD23F"), Hex("#3DDC84"),
            Hex("#22C3E6"), Hex("#5B8DEF"), Hex("#A66BFF"), Hex("#FF6FB5"),
        };
        static readonly Color[] CarCols = {
            Hex("#FF5B5B"), Hex("#4D9DFF"), Hex("#FFC14D"), Hex("#34D399"),
            Hex("#FF8A3C"), Hex("#A855F7"), Hex("#F472B6"), Hex("#22D3EE"),
        };

        Font font;
        RectTransform root;
        Canvas canvas;

        void Start()
        {
            font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");

            var cgo = new GameObject("BootCanvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            cgo.transform.SetParent(transform, false);
            canvas = cgo.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 32767;                 // above EVERYTHING (incl. the FpsCounter at 32760)
            var sc = cgo.GetComponent<CanvasScaler>();
            sc.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            sc.referenceResolution = new Vector2(1080, 1920);
            sc.matchWidthOrHeight = 0.5f;
            root = cgo.GetComponent<RectTransform>();

            StartCoroutine(Run());
            StartCoroutine(HardTimeout());
        }

        // Safety net: tear the overlay down by 8s no matter what, so it can NEVER trap the player on the loading
        // screen. (When Run() finishes it Destroys this GameObject, which also stops this coroutine.)
        IEnumerator HardTimeout()
        {
            yield return Wait(8f);
            Destroy(gameObject);
        }

        void Update()
        {
            // Survive any foreign canvas-hiding (e.g. GameUI.DisableOldCanvases): keep our overlay visible until we
            // deliberately fade + Destroy it at the end, so it can never end up half-shown / "stuck".
            if (canvas != null && !canvas.gameObject.activeSelf) canvas.gameObject.SetActive(true);
        }

        IEnumerator Run()
        {
            var splash = BuildSplash(out var gaugeTex, out var gaugeBuf, out var gaugeBase);
            var load = BuildLoading();
            load.alpha = 0f; load.gameObject.SetActive(false);

            // ---- 1) INTAKE splash: fade up, sweep the gauge, hold ----
            yield return Fade(splash, 0f, 1f, 0.35f);
            float gd = 1.9f, acc = 1f;               // redraw the gauge texture at ~25fps (NOT every frame) so the
            for (float t = 0; t < gd; t += Time.unscaledDeltaTime)   // managed-buffer churn + Apply() upload stays cheap on low-end mobile
            {
                acc += Time.unscaledDeltaTime;
                if (acc >= 0.04f)
                {
                    acc = 0f;
                    float e = EaseOutCubic(Mathf.Clamp01(t / gd));
                    DrawGauge(gaugeBuf, gaugeBase, gaugeTex.width, e);
                    gaugeTex.SetPixels(gaugeBuf); gaugeTex.Apply(false);
                }
                yield return null;
            }
            DrawGauge(gaugeBuf, gaugeBase, gaugeTex.width, 1f);
            gaugeTex.SetPixels(gaugeBuf); gaugeTex.Apply(false);
            yield return Wait(0.5f);

            // ---- crossfade splash -> loading ----
            load.gameObject.SetActive(true);
            StartCoroutine(Fade(load, 0f, 1f, 0.45f));
            yield return Fade(splash, 1f, 0f, 0.45f);
            splash.gameObject.SetActive(false);

            // ---- 2) loading bar 0..100 ----
            float ld = 2.7f, prog = 0f;
            while (prog < 100f)
            {
                prog = Mathf.Min(100f, prog + (100f / ld) * Time.unscaledDeltaTime);
                SetProgress(prog);
                yield return null;
            }
            SetProgress(100f);
            yield return Wait(0.4f);

            // ---- 3) fade the loading panel away -> reveal the MainMenu underneath, then self-destruct ----
            load.blocksRaycasts = false;             // stop intercepting taps immediately so the menu is live
            yield return Fade(load, 1f, 0f, 0.45f);  // fade the panel itself (no CanvasGroup added to the Canvas root)
            Destroy(gameObject);
        }

        // =====================================================================================
        //  SPLASH  (Intake Entertainment)
        // =====================================================================================
        CanvasGroup BuildSplash(out Texture2D gaugeTex, out Color[] gaugeBuf, out Color[] gaugeBase)
        {
            var go = Panel("Splash");
            var g = go.gameObject.AddComponent<CanvasGroup>();

            // radial dark background
            var bg = Img(go, "BG", RadialSprite(256, Hex("#15131F"), Hex("#0A0B17")));
            Stretch(bg.rectTransform);

            // speedometer gauge (upper third) — drawn into a Texture2D as it sweeps. The track+ticks are baked ONCE
            // into gaugeBase; per frame DrawGauge only copies that + adds the fill/needle, so the sweep stays cheap.
            int GS = 256;
            gaugeTex = new Texture2D(GS, GS, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
            gaugeBuf = new Color[GS * GS];
            gaugeBase = new Color[GS * GS];
            BuildGaugeBase(gaugeBase, GS);
            DrawGauge(gaugeBuf, gaugeBase, GS, 0f);
            gaugeTex.SetPixels(gaugeBuf); gaugeTex.Apply(false);
            var gaugeImg = Img(go, "Gauge", Sprite.Create(gaugeTex, new Rect(0, 0, GS, GS), new Vector2(0.5f, 0.5f), 100, 0, SpriteMeshType.FullRect));
            Place(gaugeImg.rectTransform, 0, 430, 620, 620);

            // wordmark block (rises in). Uses the ORIGINAL Intake SVG (Assets/Resources/IntakeLogo.svg) once the
            // Vector Graphics package has imported it as a Sprite; until then it falls back to a built-from-parts
            // version so the splash still shows. No recreation once the SVG is present — it's the real artwork.
            var word = Panel("Word", go);
            Place(word, 0, -80, 1080, 460);
            var wg = word.gameObject.AddComponent<CanvasGroup>();

            var svgLogo = Resources.Load<Sprite>("IntakeLogo");
            if (svgLogo != null)
            {
                var logo = Img(word, "Logo", svgLogo);
                float lw = 840f;                                  // preserve the SVG's 326x150 aspect
                Place(logo.rectTransform, 0, 0, lw, lw * 150f / 326f);
            }
            else
            {
                var tri = Img(word, "Tri", TriangleLogoSprite(220));
                Place(tri.rectTransform, 0, 150, 190, 190);
                var name = Txt(word, "INTAKE", 132, Ink, FontStyle.Bold, TextAnchor.MiddleCenter);
                Place(name.rectTransform, 0, -40, 1000, 170);
                Outline(name, new Color(0, 0, 0, 0.35f), 3);
                var ent = Txt(word, "E N T E R T A I N M E N T", 40, Amber, FontStyle.Bold, TextAnchor.MiddleCenter);
                Place(ent.rectTransform, 0, -150, 1000, 60);
                var rule = Img(word, "Rule", GradH(new[] { new Color(1, 0.42f, 0.29f, 0), Amber, new Color(1, 0.42f, 0.29f, 0) }));
                Place(rule.rectTransform, 0, -210, 620, 6);
            }

            // a subtle rise-in on the wordmark
            StartCoroutine(RiseIn(word, wg));

            // vignette on top
            var vig = Img(go, "Vignette", VignetteSprite(256));
            Stretch(vig.rectTransform); vig.raycastTarget = false;

            return g;
        }

        IEnumerator RiseIn(RectTransform rt, CanvasGroup cg)
        {
            cg.alpha = 0f;
            Vector2 to = rt.anchoredPosition, from = to + new Vector2(0, -34);
            for (float t = 0; t < 0.8f; t += Time.unscaledDeltaTime)
            {
                float e = EaseOutCubic(Mathf.Clamp01(t / 0.8f));
                cg.alpha = e; rt.anchoredPosition = Vector2.LerpUnclamped(from, to, e);
                yield return null;
            }
            cg.alpha = 1f; rt.anchoredPosition = to;
        }

        // 270deg speedometer. Static track+ticks+redline are baked ONCE into baseBuf; per-frame DrawGauge copies it
        // and adds only the fill/needle/hub/warn-dot, so the sweep animation stays cheap on mobile.
        static float GcA(float u) => 225f * Mathf.Deg2Rad - u * 270f * Mathf.Deg2Rad; // CW over the top; arc opens at the bottom
        static void GBand(Color[] buf, int S, float u, float rA, float rB, Color col)
        {
            float a = GcA(u), dx = Mathf.Cos(a), dy = Mathf.Sin(a), cx = S * 0.5f, cy = S * 0.46f;
            for (float r = rA; r <= rB; r += 0.6f) Dot(buf, S, cx + dx * r, cy + dy * r, 1.6f, col);
        }
        static void BuildGaugeBase(Color[] buf, int S)
        {
            for (int i = 0; i < buf.Length; i++) buf[i] = new Color(0, 0, 0, 0);
            float rOut = S * 0.40f, rIn = rOut - S * 0.035f;
            for (float u = 0; u <= 1f; u += 0.0016f)
                GBand(buf, S, u, rIn, rOut, u > 0.82f ? new Color(1f, 0.27f, 0.27f, 0.5f) : new Color(1, 1, 1, 0.10f));
            for (int i = 0; i <= 9; i++)
                GBand(buf, S, i / 9f, rOut - S * 0.055f, rOut - S * 0.012f, i >= 8 ? new Color(1f, 0.35f, 0.35f, 0.85f) : new Color(1f, 0.76f, 0.30f, 0.7f));
        }
        static void DrawGauge(Color[] buf, Color[] baseBuf, int S, float prog)
        {
            System.Array.Copy(baseBuf, buf, buf.Length);
            float rOut = S * 0.40f, rIn = rOut - S * 0.035f, cx = S * 0.5f, cy = S * 0.46f;
            for (float u = 0; u <= prog; u += 0.0016f) GBand(buf, S, u, rIn, rOut, Color.Lerp(Amber, Coral, u));
            float na = GcA(prog);
            for (float r = -rOut * 0.16f; r <= rOut * 0.92f; r += 0.6f) Dot(buf, S, cx + Mathf.Cos(na) * r, cy + Mathf.Sin(na) * r, 2.4f, Hex("#FF8A3C"));
            FillCircle(buf, S, cx, cy, S * 0.022f, Amber);
            Color wd = prog > 0.8f ? new Color(1f, 0.23f, 0.23f, 1f) : new Color(1, 1, 1, 0.2f);
            FillCircle(buf, S, cx, cy - rOut - S * 0.055f, S * 0.016f, wd);
        }

        // =====================================================================================
        //  LOADING  (Bus Jam Traffic Rush)
        // =====================================================================================
        Image progFill; Text pctLabel, loadLabel; float barW;

        CanvasGroup BuildLoading()
        {
            var go = Panel("Loading");
            var g = go.gameObject.AddComponent<CanvasGroup>();

            // EXACT 1:1 port of loadingscreenfinal.html's <canvas> scene — drawn into a Texture2D each frame and
            // shown full-screen. (The title + progress bar below are the HTML/CSS overlay, kept as Unity UI on top.)
            var sceneGo = new GameObject("Scene", typeof(RectTransform), typeof(RawImage));
            sceneGo.transform.SetParent(go, false);
            Stretch(sceneGo.GetComponent<RectTransform>());
            var rimg = sceneGo.GetComponent<RawImage>(); rimg.raycastTarget = false;
            int sh = Mathf.Clamp(Screen.height, 1000, 1600);                                                    // render near-native res so it isn't pixelated
            int sw = Mathf.Clamp(Mathf.RoundToInt(sh * (float)Screen.width / Mathf.Max(1, Screen.height)), 480, 900);
            go.gameObject.AddComponent<BootCanvasScene>().Setup(rimg, sw, sh);

            // ---------- BUS JAM / TRAFFIC RUSH title ----------
            TitleRow(go, "BUS JAM", 720, 122, 0);
            TitleRow(go, "TRAFFIC RUSH", 572, 134, 7);

            // ---------- progress bar (bottom) ----------
            barW = 720f;
            var bar = Img(go, "Bar", RoundRectSprite(330, 24, 12)); bar.color = new Color(0, 0, 0, 0.4f);
            Place(bar.rectTransform, 0, -890, barW, 28);
            progFill = Img(bar.rectTransform, "Fill", GradH(new[] { Amber, Coral }));
            var fr = progFill.rectTransform;
            fr.anchorMin = fr.anchorMax = new Vector2(0, 0.5f); fr.pivot = new Vector2(0, 0.5f);
            fr.anchoredPosition = Vector2.zero; fr.sizeDelta = new Vector2(0, 24);
            loadLabel = Txt(go, "LOADING", 30, Amber, FontStyle.Bold, TextAnchor.MiddleLeft);
            Place(loadLabel.rectTransform, -330, -846, 400, 44);
            pctLabel = Txt(go, "0%", 30, Ink, FontStyle.Bold, TextAnchor.MiddleRight);
            Place(pctLabel.rectTransform, 330, -846, 200, 44);
            go.gameObject.AddComponent<BootDots>().label = loadLabel;

            return g;
        }

        void TitleRow(RectTransform parent, string s, float y, float size, int colOffset)
        {
            float total = s.Length * size * 0.62f;
            float x = -total * 0.5f + size * 0.31f;
            for (int i = 0; i < s.Length; i++)
            {
                char c = s[i];
                if (c == ' ') { x += size * 0.5f; continue; }
                var t = Txt(parent, c.ToString(), (int)size, LetterCols[(i + colOffset) % LetterCols.Length], FontStyle.Bold, TextAnchor.MiddleCenter);
                Outline(t, Hex("#241544"), 3);
                Place(t.rectTransform, x, y, size * 1.1f, size * 1.3f);
                var bob = t.gameObject.AddComponent<BootBob>(); bob.phase = i * 0.5f;
                x += size * 0.62f;
            }
        }

        void SetProgress(float p)
        {
            if (progFill != null) progFill.rectTransform.sizeDelta = new Vector2(barW * p / 100f, 22);
            if (pctLabel != null) pctLabel.text = Mathf.RoundToInt(p) + "%";
        }

        // =====================================================================================
        //  small UI helpers
        // =====================================================================================
        RectTransform Panel(string name, RectTransform parent = null)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent != null ? parent : root, false);
            var rt = go.GetComponent<RectTransform>();
            Stretch(rt);
            return rt;
        }

        Image Img(RectTransform parent, string name, Sprite sp)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image));
            go.transform.SetParent(parent, false);
            var im = go.GetComponent<Image>();
            im.sprite = sp; im.raycastTarget = false;
            if (sp == null) im.color = Color.white;
            return im;
        }

        Text Txt(RectTransform parent, string s, int size, Color col, FontStyle style, TextAnchor anchor)
        {
            var go = new GameObject("Txt", typeof(RectTransform), typeof(Text));
            go.transform.SetParent(parent, false);
            var t = go.GetComponent<Text>();
            t.font = font; t.text = s; t.fontSize = size; t.color = col; t.fontStyle = style;
            t.alignment = anchor; t.horizontalOverflow = HorizontalWrapMode.Overflow;
            t.verticalOverflow = VerticalWrapMode.Overflow; t.raycastTarget = false;
            return t;
        }

        static void Outline(Graphic g, Color c, int dist)
        {
            var o = g.gameObject.AddComponent<Outline>();
            o.effectColor = c; o.effectDistance = new Vector2(dist, -dist);
        }

        static void Stretch(RectTransform rt)
        {
            rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero; rt.offsetMax = Vector2.zero;
        }
        static void Anchor(RectTransform rt, Vector2 mn, Vector2 mx) { rt.anchorMin = mn; rt.anchorMax = mx; }
        static void Zero(RectTransform rt) { rt.offsetMin = Vector2.zero; rt.offsetMax = Vector2.zero; }
        // center-anchored placement in 1080x1920 reference px
        static void Place(RectTransform rt, float x, float y, float w, float h)
        {
            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = new Vector2(x, y); rt.sizeDelta = new Vector2(w, h);
        }

        IEnumerator Fade(CanvasGroup g, float a, float b, float dur)
        {
            for (float t = 0; t < dur; t += Time.unscaledDeltaTime) { g.alpha = Mathf.Lerp(a, b, t / dur); yield return null; }
            g.alpha = b;
        }
        IEnumerator Wait(float s) { for (float t = 0; t < s; t += Time.unscaledDeltaTime) yield return null; }
        static float EaseOutCubic(float t) => 1f - Mathf.Pow(1f - t, 3f);

        // =====================================================================================
        //  procedural sprites (generated once, at boot)
        // =====================================================================================
        static Sprite FromTex(Texture2D t) =>
            Sprite.Create(t, new Rect(0, 0, t.width, t.height), new Vector2(0.5f, 0.5f), 100, 0, SpriteMeshType.FullRect);

        static Sprite GradV(Color[] stops) => GradLinear(stops, true);
        static Sprite GradH(Color[] stops) => GradLinear(stops, false);
        static Sprite GradLinear(Color[] stops, bool vertical)
        {
            int N = 128;
            var t = new Texture2D(vertical ? 1 : N, vertical ? N : 1, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
            for (int i = 0; i < N; i++)
            {
                float f = i / (N - 1f);                 // texture coord 0..1 (y=0 bottom for vertical)
                float s = vertical ? 1f - f : f;        // map so stops[0] is TOP (vertical) / LEFT (horizontal)
                Color c = EvalStops(stops, s);
                if (vertical) t.SetPixel(0, i, c); else t.SetPixel(i, 0, c);
            }
            t.Apply(false);
            return FromTex(t);
        }
        static Color EvalStops(Color[] stops, float s)
        {
            s = Mathf.Clamp01(s); float fi = s * (stops.Length - 1); int i = Mathf.FloorToInt(fi);
            if (i >= stops.Length - 1) return stops[stops.Length - 1];
            return Color.Lerp(stops[i], stops[i + 1], fi - i);
        }

        static Sprite RadialSprite(int S, Color inner, Color outer)
        {
            var t = new Texture2D(S, S, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
            var buf = new Color[S * S]; float c = (S - 1) * 0.5f;
            for (int y = 0; y < S; y++)
                for (int x = 0; x < S; x++)
                {
                    float d = Mathf.Sqrt((x - c) * (x - c) + (y - c) * (y - c)) / (c);
                    buf[y * S + x] = Color.Lerp(inner, outer, Mathf.Clamp01(d));
                }
            t.SetPixels(buf); t.Apply(false); return FromTex(t);
        }

        static Sprite VignetteSprite(int S)
        {
            var t = new Texture2D(S, S, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
            var buf = new Color[S * S]; float c = (S - 1) * 0.5f;
            for (int y = 0; y < S; y++)
                for (int x = 0; x < S; x++)
                {
                    float d = Mathf.Sqrt((x - c) * (x - c) + (y - c) * (y - c)) / c;
                    float a = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.62f, 1.05f, d)) * 0.58f;
                    buf[y * S + x] = new Color(0, 0, 0, a);
                }
            t.SetPixels(buf); t.Apply(false); return FromTex(t);
        }

        static Sprite RoundRectSprite(int W, int H, int r)
        {
            var t = new Texture2D(W, H, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
            var buf = new Color[W * H];
            for (int y = 0; y < H; y++)
                for (int x = 0; x < W; x++)
                {
                    bool inside = true;
                    if (x < r || x > W - 1 - r)
                        if (y < r || y > H - 1 - r)
                        {
                            int ccx = x < r ? r : W - 1 - r, ccy = y < r ? r : H - 1 - r;
                            inside = (x - ccx) * (x - ccx) + (y - ccy) * (y - ccy) <= r * r;
                        }
                    buf[y * W + x] = inside ? Color.white : new Color(1, 1, 1, 0);
                }
            t.SetPixels(buf); t.Apply(false); return FromTex(t);
        }

        // up-pointing triangle with the amber->coral gradient and a right-pointing play cutout
        static Sprite TriangleLogoSprite(int S)
        {
            var t = new Texture2D(S, S, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
            var buf = new Color[S * S];
            Vector2 apex = new Vector2(S * 0.5f, S * 0.92f), bl = new Vector2(S * 0.10f, S * 0.10f), br = new Vector2(S * 0.90f, S * 0.10f);
            // play cutout (points right), in texture space
            Vector2 p0 = new Vector2(S * 0.40f, S * 0.62f), p1 = new Vector2(S * 0.40f, S * 0.30f), p2 = new Vector2(S * 0.64f, S * 0.46f);
            for (int y = 0; y < S; y++)
                for (int x = 0; x < S; x++)
                {
                    var p = new Vector2(x + 0.5f, y + 0.5f);
                    Color col = new Color(0, 0, 0, 0);
                    if (InTri(p, apex, bl, br) && !InTri(p, p0, p1, p2))
                        col = Color.Lerp(Coral, Amber, y / (S - 1f)); // amber up top, coral at base
                    buf[y * S + x] = col;
                }
            t.SetPixels(buf); t.Apply(false); return FromTex(t);
        }

        static Sprite PalmSprite(int S)
        {
            var t = new Texture2D(S, S, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
            var buf = new Color[S * S];
            Color col = Hex("#14202c");
            float bx = S * 0.5f;
            for (int y = 0; y < (int)(S * 0.7f); y++) // trunk
                for (int x = (int)(bx - S * 0.03f); x <= (int)(bx + S * 0.03f); x++)
                    if (x >= 0 && x < S) buf[y * S + Mathf.Clamp(x, 0, S - 1)] = col;
            float tx = bx, ty = S * 0.7f; // fronds
            for (int a = 0; a < 6; a++)
            {
                float ang = Mathf.PI * 0.5f + (a - 2.5f) * 0.5f;
                for (float r = 0; r < S * 0.42f; r += 0.6f)
                    Dot(buf, S, tx + Mathf.Cos(ang) * r, ty + Mathf.Sin(ang) * r * 0.7f, 2.2f, col);
            }
            t.SetPixels(buf); t.Apply(false); return FromTex(t);
        }

        // ---- raster primitives into a Color[] buffer ----
        static void Dot(Color[] buf, int S, float px, float py, float rad, Color col)
        {
            int x0 = Mathf.Max(0, (int)(px - rad)), x1 = Mathf.Min(S - 1, (int)(px + rad));
            int y0 = Mathf.Max(0, (int)(py - rad)), y1 = Mathf.Min(S - 1, (int)(py + rad));
            for (int y = y0; y <= y1; y++)
                for (int x = x0; x <= x1; x++)
                    if ((x - px) * (x - px) + (y - py) * (y - py) <= rad * rad) Blend(buf, S, x, y, col);
        }
        static void FillCircle(Color[] buf, int S, float px, float py, float rad, Color col) => Dot(buf, S, px, py, rad, col);
        static void Blend(Color[] buf, int S, int x, int y, Color c)
        {
            if (x < 0 || x >= S || y < 0 || y >= S) return;
            int i = y * S + x; var d = buf[i]; float a = c.a + d.a * (1 - c.a);
            buf[i] = a < 1e-4f ? new Color(0, 0, 0, 0)
                : new Color((c.r * c.a + d.r * d.a * (1 - c.a)) / a, (c.g * c.a + d.g * d.a * (1 - c.a)) / a, (c.b * c.a + d.b * d.a * (1 - c.a)) / a, a);
        }
        static bool InTri(Vector2 p, Vector2 a, Vector2 b, Vector2 c)
        {
            float d1 = Sign(p, a, b), d2 = Sign(p, b, c), d3 = Sign(p, c, a);
            bool neg = (d1 < 0) || (d2 < 0) || (d3 < 0), pos = (d1 > 0) || (d2 > 0) || (d3 > 0);
            return !(neg && pos);
        }
        static float Sign(Vector2 p, Vector2 a, Vector2 b) => (p.x - b.x) * (a.y - b.y) - (a.x - b.x) * (p.y - b.y);
    }

    // self-animating helpers (attached to letters / cars / the loading label)
    public class BootBob : MonoBehaviour
    {
        public float amp = 7f, speed = 3.0f, phase;
        RectTransform rt; float baseY;
        void Start() { rt = (RectTransform)transform; baseY = rt.anchoredPosition.y; }
        void Update() { var p = rt.anchoredPosition; p.y = baseY + Mathf.Sin(Time.unscaledTime * speed + phase) * amp; rt.anchoredPosition = p; }
    }

    public class BootSlide : MonoBehaviour
    {
        public float speed, minX, maxX;
        RectTransform rt;
        void Start() { rt = (RectTransform)transform; }
        void Update() { var p = rt.anchoredPosition; p.x += speed * Time.unscaledDeltaTime; if (p.x > maxX) p.x = minX; rt.anchoredPosition = p; }
    }

    public class BootDots : MonoBehaviour
    {
        public UnityEngine.UI.Text label; float t; int n;
        void Update()
        {
            t += Time.unscaledDeltaTime; if (t < 0.4f || label == null) return; t = 0f;
            n = (n + 1) % 4; label.text = "LOADING" + new string('.', n);
        }
    }

    // Replicates the HTML loading-screen traffic: 3 lanes of cars driving right, modulated by a 6.2s traffic-light
    // cycle (GO 3.4s -> AMBER 0.5s -> STOP), clamped so cars never overlap, wrapping off the right edge back to the
    // left. Also colours the three traffic-light lamps to match the cycle.
    public class BootTraffic : MonoBehaviour
    {
        public class Car { public RectTransform rt; public float w; }
        public class Lane { public float speed, osc, phase; public System.Collections.Generic.List<Car> cars; }
        public System.Collections.Generic.List<Lane> lanes;
        public Image lampR, lampA, lampG;
        static readonly Color Off = new Color(1, 1, 1, 0.10f);
        static Color H(string s) { ColorUtility.TryParseHtmlString(s, out var c); return c; }
        void Update()
        {
            float t = Time.unscaledTime, dt = Time.unscaledDeltaTime, cyc = t % 6.2f;
            bool go = cyc < 3.4f, amb = cyc >= 3.4f && cyc < 3.9f;
            if (lanes != null)
                foreach (var L in lanes)
                {
                    if (L.cars == null || L.cars.Count == 0) continue;
                    L.cars.Sort((a, b) => a.rt.anchoredPosition.x.CompareTo(b.rt.anchoredPosition.x));
                    float sp = L.speed * (go ? (0.7f + 0.5f * Mathf.Max(0f, Mathf.Sin(t * L.osc + L.phase))) : 0.05f) * dt;
                    for (int i = L.cars.Count - 1; i >= 0; i--)
                    {
                        var c = L.cars[i]; var p = c.rt.anchoredPosition; float tg = p.x + sp;
                        if (i < L.cars.Count - 1) { var a = L.cars[i + 1]; tg = Mathf.Min(tg, a.rt.anchoredPosition.x - (c.w * 0.5f + a.w * 0.5f + 30f)); }
                        if (tg > p.x) { p.x = tg; c.rt.anchoredPosition = p; }
                    }
                    var rr = L.cars[L.cars.Count - 1];
                    if (rr.rt.anchoredPosition.x - rr.w * 0.5f > 600f)
                    {
                        var lf = L.cars[0]; var rp = rr.rt.anchoredPosition;
                        rp.x = lf.rt.anchoredPosition.x - (rr.w * 0.5f + lf.w * 0.5f + 130f); rr.rt.anchoredPosition = rp;
                    }
                }
            if (lampR != null) lampR.color = (!go && !amb) ? H("#FF3B3B") : Off;
            if (lampA != null) lampA.color = amb ? H("#FFC14D") : Off;
            if (lampG != null) lampG.color = go ? H("#39E08B") : Off;
        }
    }
}
