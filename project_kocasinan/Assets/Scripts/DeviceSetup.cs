using UnityEngine;

namespace BusJam
{
    /// <summary>
    /// One-time device setup that runs at APP LAUNCH (before any scene loads) via [RuntimeInitializeOnLoadMethod], so
    /// the loading screen, the main menu AND gameplay all share the same orientation + frame-rate policy. Self-runs —
    /// no scene or Inspector wiring. Delete this file to remove it.
    ///
    ///   • Locks PORTRAIT.
    ///   • DYNAMIC frame rate by device tier (vSync OFF so the target is honoured; mobile otherwise caps at 30):
    ///       – high-end phone  → up to the panel's refresh, max 120 (a 60/90 Hz panel naturally runs at its own rate);
    ///       – every other phone (and unknown) → 60 (steady + battery-friendly).
    ///     targetFrameRate is a CEILING, not a guarantee — the 60-fps FLOOR comes from keeping per-frame + per-level
    ///     cost low, not from this number.
    ///   • Trims render resolution on high-DPI / low-RAM phones so even weak GPUs can hold the target (fill rate is the
    ///     #1 reason a modern phone misses its target; the UI still fills the screen, only the pixel count drops).
    /// </summary>
    public static class DeviceSetup
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Boot()
        {
            Screen.orientation = ScreenOrientation.Portrait;
            ApplyFrameRate();
            TrimResolution();
        }

        /// <summary>
        /// Set vSync + targetFrameRate for this device. Public + idempotent so gameplay can re-assert it after a scene
        /// load (Unity occasionally resets targetFrameRate). High-end → min(120, panel Hz); others → 60.
        /// </summary>
        public static void ApplyFrameRate()
        {
            QualitySettings.vSyncCount = 0; // targetFrameRate is ignored while vSync is on
            int cap = HighEndDevice() ? 120 : 60;
            Application.targetFrameRate = Mathf.Min(cap, MaxPanelHz());
        }

        /// <summary>Editor / desktop / console = high-end. On mobile, classify by RAM + core count (a good cross-SoC
        /// proxy for Android + iOS tiers). Tunable — bump the thresholds if mid devices feel pushed.</summary>
        public static bool HighEndDevice()
        {
            if (!Application.isMobilePlatform) return true;
            return SystemInfo.systemMemorySize >= 5500   // ~6 GB+ RAM
                && SystemInfo.processorCount   >= 7;      // 8-core class SoC
        }

        // Highest refresh the panel advertises (current mode + every supported mode — some 120 Hz panels report 60 as
        // "current" until a high rate is requested). Clamped to [60, 240]; 60 if the platform reports nothing useful.
        static int MaxPanelHz()
        {
            int hz = 60;
            var cur = Screen.currentResolution.refreshRateRatio;
            if (cur.denominator != 0) hz = Mathf.Max(hz, Mathf.RoundToInt((float)cur.value));
            var modes = Screen.resolutions;
            if (modes != null)
                foreach (var m in modes)
                    if (m.refreshRateRatio.denominator != 0)
                        hz = Mathf.Max(hz, Mathf.RoundToInt((float)m.refreshRateRatio.value));
            return Mathf.Clamp(hz, 60, 240);
        }

        static void TrimResolution()
        {
            if (!Application.isMobilePlatform) return;
            // Cap the SHORT side to 1080 on every phone (a low-poly cartoon game looks crisp there); low-RAM phones
            // trim a touch more. This is the single biggest target-fps win on high-DPI panels — and it matters MORE at
            // 120 fps (twice the frames must fit the GPU budget). No effect on 1080p-or-lower phones or the editor.
            float scale = 1f;
            int shortSide = Mathf.Min(Screen.width, Screen.height);
            if (shortSide > 1080) scale = 1080f / shortSide;
            if (SystemInfo.systemMemorySize < 3072) scale *= 0.85f;
            if (scale < 0.999f)
            {
                int w = Mathf.RoundToInt(Screen.width * scale);
                int h = Mathf.RoundToInt(Screen.height * scale);
                if (w > 0 && h > 0) Screen.SetResolution(w, h, true);
            }
        }
    }
}
