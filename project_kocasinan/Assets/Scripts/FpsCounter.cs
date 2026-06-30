using UnityEngine;
using UnityEngine.UI;

namespace BusJam
{
    /// <summary>
    /// On-screen FPS counter (DEV ONLY). HIDDEN by default — set <see cref="Show"/> = true to show the pill while
    /// profiling on-device, false for player / closed-test / release builds. Self-spawns via
    /// [RuntimeInitializeOnLoadMethod] with no scene/Inspector wiring, and nothing references it.
    ///
    /// Shows smoothed FPS top-right, colour-coded (green ≥50 / yellow ≥30 / red below) on its OWN screen-overlay
    /// canvas above everything. Uses UNSCALED time so it reads correctly while paused, and doesn't block taps.
    /// </summary>
    public class FpsCounter : MonoBehaviour
    {
        // OFF for player / closed-test / release builds — the FPS pill must NOT show to testers. Flip to true only
        // for your own on-device perf profiling, then back to false before building for testers.
        const bool Show = false;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void AutoSpawn()
        {
            if (!Show) return; // hidden -> nothing spawned, nothing drawn on screen
            var go = new GameObject("FpsCounter");
            DontDestroyOnLoad(go);
            go.AddComponent<FpsCounter>();
        }

        Text label;
        GameObject canvasGo;
        float smoothDt = 1f / 60f;
        float refresh;

        void Start()
        {
            canvasGo = new GameObject("Canvas", typeof(Canvas), typeof(CanvasScaler));
            canvasGo.transform.SetParent(transform, false);
            var canvas = canvasGo.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 32760; // above all game UI
            var scaler = canvasGo.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1080, 1920);
            scaler.matchWidthOrHeight = 0f;   // match WIDTH (portrait): fits the screen width on any aspect

            // Dark chip in the top-right corner.
            var bgGo = new GameObject("BG", typeof(RectTransform), typeof(Image));
            bgGo.transform.SetParent(canvasGo.transform, false);
            var bgRt = bgGo.GetComponent<RectTransform>();
            bgRt.anchorMin = bgRt.anchorMax = bgRt.pivot = new Vector2(1f, 1f);
            bgRt.anchoredPosition = new Vector2(-12f, -12f);
            bgRt.sizeDelta = new Vector2(132f, 46f);   // compact pill (was 250x78)
            var bg = bgGo.GetComponent<Image>();
            bg.color = new Color(0f, 0f, 0f, 0.55f);
            bg.raycastTarget = false; // never block gameplay taps

            var txtGo = new GameObject("Text", typeof(RectTransform), typeof(Text));
            txtGo.transform.SetParent(bgGo.transform, false);
            var rt = txtGo.GetComponent<RectTransform>();
            rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero; rt.offsetMax = Vector2.zero;
            label = txtGo.GetComponent<Text>();
            label.font = GameFont.UGUI;
            label.fontSize = 22;   // smaller (the global font scaler still multiplies this); pill sized to fit
            label.fontStyle = FontStyle.Bold;
            label.alignment = TextAnchor.MiddleCenter;
            label.raycastTarget = false;
            label.text = "-- fps";
        }

        void Update()
        {
            // GameUI.DisableOldCanvases() switches off every canvas not under its own root when a level
            // builds — re-assert ours so the counter survives into gameplay (self-contained; no GameUI edit).
            if (canvasGo != null && !canvasGo.activeSelf) canvasGo.SetActive(true);

            // Exponential smoothing so the number is readable, not jittery.
            smoothDt += (Time.unscaledDeltaTime - smoothDt) * 0.1f;
            refresh += Time.unscaledDeltaTime;
            if (refresh < 0.2f || label == null) return; // update text ~5×/sec
            refresh = 0f;

            float fps = smoothDt > 1e-5f ? 1f / smoothDt : 0f;
            label.text = $"{fps:0} fps";   // fps only -> keeps the pill small (drop the ms readout)
            label.color = fps >= 50f ? new Color(0.5f, 1f, 0.55f)
                        : fps >= 30f ? new Color(1f, 0.88f, 0.4f)
                        :              new Color(1f, 0.45f, 0.45f);
        }
    }
}
