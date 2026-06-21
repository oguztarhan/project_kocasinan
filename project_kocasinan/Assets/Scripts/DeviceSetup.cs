using UnityEngine;

namespace BusJam
{
    /// <summary>
    /// One-time device setup that runs at APP LAUNCH (before any scene loads), so the loading screen, the main menu
    /// AND gameplay all share the same orientation + frame rate — not just the gameplay scene (BusJamGame used to be
    /// the only place that set these, so the boot/menu ran at the platform default, often 30 fps). Self-runs via
    /// [RuntimeInitializeOnLoadMethod] — no scene or Inspector wiring. Delete this file to remove it.
    ///
    ///   • Locks PORTRAIT.
    ///   • Targets a steady 60 FPS on every device (vSync OFF so the target is honored; mobile otherwise caps at 30).
    ///     Note: this also caps 90/120 Hz phones to 60, which is what we want for a consistent feel + battery.
    ///   • On memory-constrained phones, trims the render resolution a little so even low-end GPUs can hold 60. The
    ///     UI/canvas still fill the whole screen edge-to-edge — only the pixel count drops, rarely noticeable.
    /// </summary>
    public static class DeviceSetup
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Boot()
        {
            Screen.orientation = ScreenOrientation.Portrait;
            QualitySettings.vSyncCount = 0;      // don't gate on the display refresh; let targetFrameRate drive the cap
            Application.targetFrameRate = 60;     // steady 60 on every capable device

            // FILL-RATE is the #1 reason a modern phone misses 60: a 1440p panel is ~4.3M px, and with MSAA + post
            // a mid GPU can't finish a frame in 16 ms, so Android Frame Pacing halves it to a locked 30. A low-poly
            // cartoon game looks crisp at ~1080p, so cap the SHORT side to 1080 on EVERY phone (aspect kept — the UI
            // still fills the screen edge-to-edge, only the pixel count drops). Low-RAM phones trim a touch more.
            // This is the single biggest 60-fps win on high-DPI devices; no effect on 1080p-or-lower phones or editor.
            if (Application.isMobilePlatform)
            {
                float scale = 1f;
                int shortSide = Mathf.Min(Screen.width, Screen.height);
                if (shortSide > 1080) scale = 1080f / shortSide;          // downscale only high-DPI panels
                if (SystemInfo.systemMemorySize < 3072) scale *= 0.85f;   // weak/low-RAM phones go a bit lighter still
                if (scale < 0.999f)
                {
                    int w = Mathf.RoundToInt(Screen.width * scale);
                    int h = Mathf.RoundToInt(Screen.height * scale);
                    if (w > 0 && h > 0) Screen.SetResolution(w, h, true);
                }
            }
        }
    }
}
