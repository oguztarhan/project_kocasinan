using UnityEngine;
using UnityEngine.UI;

namespace BusJam
{
    /// <summary>
    /// TEMPORARY on-screen FPS counter. To REMOVE later: just delete this one file — it self-spawns
    /// via [RuntimeInitializeOnLoadMethod] with no scene/Inspector wiring, and nothing references it.
    ///
    /// Shows smoothed FPS + frame time (ms) top-right, color-coded (green ≥50 / yellow ≥30 / red below),
    /// on its OWN screen-overlay canvas above everything. Uses UNSCALED time so it reads correctly while
    /// paused (Time.timeScale = 0), and doesn't block taps (no raycast targets).
    ///
    /// NOTE: under Unity Remote this shows the EDITOR's render rate (usually fine) while the phone screen
    /// looks laggy — that gap IS the Remote video-stream cost, not your game. Real numbers need a build.
    /// </summary>
    public class FpsCounter : MonoBehaviour
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void AutoSpawn()
        {
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
            scaler.matchWidthOrHeight = 0.5f;

            // Dark chip in the top-right corner.
            var bgGo = new GameObject("BG", typeof(RectTransform), typeof(Image));
            bgGo.transform.SetParent(canvasGo.transform, false);
            var bgRt = bgGo.GetComponent<RectTransform>();
            bgRt.anchorMin = bgRt.anchorMax = bgRt.pivot = new Vector2(1f, 1f);
            bgRt.anchoredPosition = new Vector2(-16f, -16f);
            bgRt.sizeDelta = new Vector2(250f, 78f);
            var bg = bgGo.GetComponent<Image>();
            bg.color = new Color(0f, 0f, 0f, 0.55f);
            bg.raycastTarget = false; // never block gameplay taps

            var txtGo = new GameObject("Text", typeof(RectTransform), typeof(Text));
            txtGo.transform.SetParent(bgGo.transform, false);
            var rt = txtGo.GetComponent<RectTransform>();
            rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero; rt.offsetMax = Vector2.zero;
            label = txtGo.GetComponent<Text>();
            label.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            label.fontSize = 40;
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
            label.text = $"{fps:0} fps   {smoothDt * 1000f:0.0} ms";
            label.color = fps >= 50f ? new Color(0.5f, 1f, 0.55f)
                        : fps >= 30f ? new Color(1f, 0.88f, 0.4f)
                        :              new Color(1f, 0.45f, 0.45f);
        }
    }
}
