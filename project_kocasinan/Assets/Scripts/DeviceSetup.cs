using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Ridebury
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
            DeviceTier = ClassifyTier();   // decide the graphics tier ONCE, before anything renders
            ApplyQuality();                // set the per-tier render budget on the URP asset + QualitySettings
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
            int cap = HighEndDevice() ? 90 : 60; // 90 fps max on capable phones (60 floor on the rest)
            Application.targetFrameRate = Mathf.Min(cap, MaxPanelHz());
        }

        // ===================== device quality tier =====================
        /// <summary>Cheap phone -> Low, mid phone -> Mid, flagship (or editor/desktop) -> High. Classified ONCE at
        /// launch from RAM + core count (a decent cross-SoC proxy). The WHOLE game reads DeviceTier so every system
        /// agrees on one tier. Tune the two thresholds below freely if a class of device still struggles / has headroom.</summary>
        public enum Tier { Low, Mid, High }
        public static Tier DeviceTier { get; private set; } = Tier.High;

        // TESTING: set this to Tier.Low / Mid / High to FORCE a tier on ANY device (incl. the editor), so you can
        // preview each budget without the target hardware. Leave null for real per-device classification. >>> null for release <<<
        public static Tier? ForceTier = null;

        static Tier ClassifyTier()
        {
            if (ForceTier.HasValue) return ForceTier.Value;           // code override (see above)
#if UNITY_EDITOR
            int menuTier = UnityEditor.EditorPrefs.GetInt(EditorTierKey, -1); // Ridebury ▸ Graphics Tier menu (persists across Play)
            if (menuTier >= 0) return (Tier)menuTier;
#endif
            if (!Application.isMobilePlatform) return Tier.High;       // editor / desktop / console
            int ram = SystemInfo.systemMemorySize, cores = SystemInfo.processorCount;
            if (ram >= 5500 && cores >= 7) return Tier.High;          // ~6 GB+ 8-core flagship
            if (ram >= 3200)               return Tier.Mid;          // ~4 GB mid phone (was the gap: ran full graphics)
            return Tier.Low;                                          // ~2-3 GB budget phone
        }

        // High-end == the top tier. Kept as a method so existing callers (the frame-rate cap) still work unchanged.
        public static bool HighEndDevice() => DeviceTier == Tier.High;

        /// <summary>Apply the per-tier RENDER budget to the URP asset + QualitySettings, ONCE at launch. This is the
        /// STARTING quality; PerfGovernor still adaptively drops renderScale at runtime to defend the 60-fps floor, and
        /// RideburyGame reads DeviceTier to gate the two biggest gameplay-board costs (the inverted-hull TOON OUTLINE,
        /// which draws every vehicle twice, and the shadowmap pass).
        ///   • Low : shadows OFF, MSAA off, renderScale 0.80, 0 pixel lights   (RideburyGame: no outlines, no VFX)
        ///   • Mid : HARD shadows @28 m, MSAA off, renderScale 0.90, 1 light    (RideburyGame: no outlines)
        ///   • High: SOFT shadows @50 m, 2x MSAA, renderScale 1.00, 2 lights    (RideburyGame: outlines on)</summary>
        public static void ApplyQuality()
        {
            QualitySettings.antiAliasing = 0;   // URP owns MSAA via the asset below, not QualitySettings
            switch (DeviceTier)
            {
                case Tier.Low: QualitySettings.pixelLightCount = 0; QualitySettings.shadows = UnityEngine.ShadowQuality.Disable;  break;
                case Tier.Mid: QualitySettings.pixelLightCount = 1; QualitySettings.shadows = UnityEngine.ShadowQuality.HardOnly; break;
                default:       QualitySettings.pixelLightCount = 2; QualitySettings.shadows = UnityEngine.ShadowQuality.All;      break;
            }

            if (GraphicsSettings.currentRenderPipeline is UniversalRenderPipelineAsset urp)
            {
                switch (DeviceTier)
                {
                    // High leaves cascades as authored so the shared asset isn't mutated in the editor (editor == High).
                    case Tier.Low: urp.renderScale = 0.80f; urp.msaaSampleCount = 1; urp.shadowDistance = 0f;  urp.shadowCascadeCount = 1; break;
                    case Tier.Mid: urp.renderScale = 0.90f; urp.msaaSampleCount = 1; urp.shadowDistance = 28f; urp.shadowCascadeCount = 1; break;
                    default:       urp.renderScale = 1.00f; urp.msaaSampleCount = 2; urp.shadowDistance = 50f; break;
                }
            }
        }

#if UNITY_EDITOR
        public const string EditorTierKey = "Ridebury.ForceTier"; // EditorPrefs key shared with the Graphics Tier menu
        // Re-classify (re-reads the menu EditorPrefs) and re-apply live. renderScale / MSAA / shadow distance update
        // instantly; the toon outlines + sun-shadow style apply on the NEXT level build (they're set when a level is
        // built), so reload the gameplay scene / press Retry to see those change.
        public static void EditorApplyTier()
        {
            DeviceTier = ClassifyTier();
            ApplyQuality();
            ApplyFrameRate();
        }
#endif

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
