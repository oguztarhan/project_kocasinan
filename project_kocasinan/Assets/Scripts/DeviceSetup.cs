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

            // Low-end phones (< 3 GB RAM — the same heuristic gameplay uses for `lowEnd`) are usually GPU/fill-rate
            // bound at native resolution (e.g. 1080x2400 ≈ 2.6M px). Rendering at ~85% keeps the framebuffer light
            // enough to hold 60 while the screen still fills edge-to-edge. No effect on capable devices or the editor.
            if (Application.isMobilePlatform && SystemInfo.systemMemorySize < 3072)
            {
                const float scale = 0.85f;
                int w = Mathf.RoundToInt(Screen.width * scale);
                int h = Mathf.RoundToInt(Screen.height * scale);
                if (w > 0 && h > 0) Screen.SetResolution(w, h, true);
            }
        }
    }
}
