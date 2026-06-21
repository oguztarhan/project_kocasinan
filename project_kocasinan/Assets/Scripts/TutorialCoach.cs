using UnityEngine;
using UnityEngine.UI;

namespace BusJam
{
    /// <summary>
    /// Lightweight, self-contained on-screen coach for the early tutorials (#4 level-1; reused by #5/#6).
    /// Builds its OWN overlay canvas with NO GraphicRaycaster, and every graphic has raycastTarget=false,
    /// so taps pass straight through to the game board underneath (BusJamGame reads taps via a physics
    /// raycast + EventSystem.IsPointerOverGameObject, which this must never intercept).
    ///
    /// Driven by BusJamGame: ShowText(...) sets the bottom banner; PointAt(screen)/HidePointer() move a
    /// pulsing "tap here" ring. Build() once, then Show/Hide freely. No scene/baker edits, no assets.
    /// </summary>
    public class TutorialCoach : MonoBehaviour
    {
        Font font;
        GameObject root;        // whole overlay (toggled by Show/Hide)
        GameObject bannerGo;
        Text bannerText;
        RectTransform pointer;
        bool pointerOn;

        public void Build()
        {
            if (root != null) return;
            font = GameFont.UGUI;

            var canvasGo = new GameObject("TutorialCanvas");
            canvasGo.transform.SetParent(transform, false);
            var canvas = canvasGo.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 900; // above the HUD, below LevelSelect (1000) and the success/fail panels we hand off to
            var scaler = canvasGo.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1080, 1920);
            scaler.matchWidthOrHeight = 0f;   // match WIDTH (portrait): fits the screen width on any aspect
            // NO GraphicRaycaster on purpose -> this overlay can never block the game's tap raycast.
            root = canvasGo;

            // ---- bottom instruction banner ----
            // If the user authored a panel in the scene (tagged with TutorialPanelMarker, e.g. via
            // Tools ▸ 300Mind UI ▸ Bake Tutorial Panel), ADOPT it as the banner background so they fully
            // control its look in the Inspector — we only drive the text on top. Otherwise fall back
            // to the built-in dark box (original behaviour, so nothing breaks if no panel exists).
            var marker = FindScenePanel();
            if (marker != null)
            {
                marker.adopted = true;                                    // stand down the panel's play-start self-hide
                bannerGo = marker.gameObject;
                bannerGo.transform.SetParent(canvasGo.transform, false); // adopt into this overlay so Show/Hide drives it
                bannerGo.transform.SetAsFirstSibling();                   // keep it BEHIND the pointer ring
                var mimg = bannerGo.GetComponent<Image>();
                if (mimg != null) mimg.raycastTarget = false;            // never block the board's tap raycast
                bannerText = bannerGo.GetComponentInChildren<Text>(true); // reuse a text the user added, if any
                if (bannerText == null)
                {
                    bannerText = MakeText(bannerGo.transform, "", 46, Color.white, TextAnchor.MiddleCenter);
                    var atrt = bannerText.rectTransform;
                    atrt.anchorMin = Vector2.zero; atrt.anchorMax = Vector2.one;
                    atrt.offsetMin = new Vector2(34, 18); atrt.offsetMax = new Vector2(-34, -18);
                }
            }
            else
            {
                bannerGo = new GameObject("Banner", typeof(RectTransform), typeof(Image));
                bannerGo.transform.SetParent(canvasGo.transform, false);
                var brt = bannerGo.GetComponent<RectTransform>();
                brt.anchorMin = brt.anchorMax = new Vector2(0.5f, 0f);
                brt.pivot = new Vector2(0.5f, 0f);
                brt.anchoredPosition = new Vector2(0, 380);   // lower third, ABOVE the joker row, BELOW the jam
                brt.sizeDelta = new Vector2(1000, 170);
                var bimg = bannerGo.GetComponent<Image>();
                bimg.color = new Color(0.06f, 0.08f, 0.13f, 0.86f);
                bimg.raycastTarget = false;

                bannerText = MakeText(bannerGo.transform, "", 46, Color.white, TextAnchor.MiddleCenter);
                var trt = bannerText.rectTransform;
                trt.anchorMin = Vector2.zero; trt.anchorMax = Vector2.one;
                trt.offsetMin = new Vector2(34, 18); trt.offsetMax = new Vector2(-34, -18);
            }

            // ---- pulsing "tap here" ring ----
            var pgo = new GameObject("Pointer", typeof(RectTransform), typeof(Image));
            pgo.transform.SetParent(canvasGo.transform, false);
            pointer = pgo.GetComponent<RectTransform>();
            pointer.sizeDelta = new Vector2(150, 150);
            var pimg = pgo.GetComponent<Image>();
            pimg.sprite = MakeCircleSprite();
            pimg.color = new Color(1f, 0.92f, 0.30f, 0.9f); // warm yellow
            pimg.raycastTarget = false;

            Hide();
        }

        public void ShowText(string msg)
        {
            if (root == null) Build();
            root.SetActive(true);
            if (bannerGo) bannerGo.SetActive(true);
            if (bannerText) bannerText.text = msg;
        }

        public void PointAt(Vector2 screenPos)
        {
            if (pointer == null) return;
            if (root != null && !root.activeSelf) root.SetActive(true);
            pointerOn = true;
            if (!pointer.gameObject.activeSelf) pointer.gameObject.SetActive(true);
            pointer.position = new Vector3(screenPos.x, screenPos.y, 0f); // overlay canvas -> position is in screen pixels
        }

        public void HidePointer()
        {
            pointerOn = false;
            if (pointer && pointer.gameObject.activeSelf) pointer.gameObject.SetActive(false);
        }

        public void Hide()
        {
            HidePointer();
            if (root) root.SetActive(false);
        }

        void Update()
        {
            if (pointerOn && pointer != null)
            {
                float s = 1f + 0.16f * Mathf.Sin(Time.unscaledTime * 6f);
                pointer.localScale = new Vector3(s, s, 1f);
            }
        }

        // ---- helpers --------------------------------------------------------
        // Find the user's tutorial-background panel in the loaded scene (active OR inactive), skipping
        // prefab/asset instances. Returns null if none exists -> the coach builds its own dark box.
        static TutorialPanelMarker FindScenePanel()
        {
            var all = Resources.FindObjectsOfTypeAll<TutorialPanelMarker>();
            foreach (var m in all)
                if (m != null && m.gameObject.scene.IsValid()) return m;
            return null;
        }

        Text MakeText(Transform parent, string text, int size, Color color, TextAnchor align)
        {
            var go = new GameObject("Text", typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var t = go.AddComponent<Text>();
            t.font = font; t.text = text; t.fontSize = size; t.color = color; t.alignment = align;
            t.horizontalOverflow = HorizontalWrapMode.Wrap; t.verticalOverflow = VerticalWrapMode.Overflow;
            t.raycastTarget = false;
            var sh = go.AddComponent<Shadow>(); sh.effectColor = new Color(0, 0, 0, 0.5f); sh.effectDistance = new Vector2(2, -2);
            return t;
        }

        static Sprite cachedCircle;
        static Sprite MakeCircleSprite()
        {
            if (cachedCircle != null) return cachedCircle;
            int s = 64; float r = s * 0.5f;
            var tex = new Texture2D(s, s, TextureFormat.RGBA32, false);
            var px = new Color[s * s];
            for (int y = 0; y < s; y++)
                for (int x = 0; x < s; x++)
                {
                    float d = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), new Vector2(r, r)) / r;
                    float disc = Mathf.Clamp01(1.1f - d) * 0.45f;                 // soft filled centre
                    float ring = Mathf.Clamp01(1f - Mathf.Abs(d - 0.80f) * 7f);    // brighter rim
                    px[y * s + x] = new Color(1, 1, 1, Mathf.Max(disc, ring));
                }
            tex.SetPixels(px); tex.Apply();
            tex.wrapMode = TextureWrapMode.Clamp;
            cachedCircle = Sprite.Create(tex, new Rect(0, 0, s, s), new Vector2(0.5f, 0.5f), 100f);
            return cachedCircle;
        }
    }
}
