using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;

namespace Ridebury
{
    /// <summary>
    /// Frame-rate GOVERNOR enforcing the 60-min / 90-max window. Self-boots (no scene wiring); delete to remove.
    ///
    ///   • CEILING: re-asserts DeviceSetup.ApplyFrameRate() on every scene load (vSync off + targetFrameRate = 90 on
    ///     high-end phones, 60 elsewhere — Unity occasionally resets it across scene swaps).
    ///   • FLOOR: adaptive resolution. Watches a smoothed frame time; if the game sits under ~56 fps for 2s, steps
    ///     URP renderScale down one notch (1 -> .85 -> .75 -> .65) — fill rate is the #1 cost on phones, so this
    ///     recovers the 60 floor while gameplay, UI and composition stay IDENTICAL (UI renders at full res). When
    ///     the device holds its target comfortably for 30s, it steps back up. Scene-load spikes are ignored
    ///     (5s settle) so a loading hitch can never trigger a downscale.
    ///
    /// The scale multiplies the URP asset's authored renderScale (captured at boot) and is restored on quit, so the
    /// asset is never left mutated (matters in the editor, where play-mode SO changes persist).
    /// </summary>
    public class PerfGovernor : MonoBehaviour
    {
        const float FloorFps = 60f;                              // hard floor we protect on every device
        static readonly float[] Steps = { 1f, 0.85f, 0.75f, 0.65f }; // renderScale ladder (× the authored base)

        static PerfGovernor inst;
        int step;                 // current ladder index
        float baseScale = 1f;     // URP asset's authored renderScale at boot
        float ema;                // smoothed frame time
        float lowTime, okTime;    // how long we've been under the floor / comfortably at target
        float cooldown;           // min gap between ladder moves
        float settle;             // post-scene-load grace (load spikes must not downscale)

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot()
        {
            if (inst != null) return;
            var go = new GameObject("~PerfGovernor");
            DontDestroyOnLoad(go);
            inst = go.AddComponent<PerfGovernor>();
        }

        void Awake()
        {
            var urp = GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset;
            if (urp != null) baseScale = urp.renderScale;
            ema = 1f / FloorFps;
            settle = 5f;
        }

        void OnEnable()  { SceneManager.sceneLoaded += OnSceneLoaded; }
        void OnDisable() { SceneManager.sceneLoaded -= OnSceneLoaded; }
        void OnSceneLoaded(Scene s, LoadSceneMode m) { DeviceSetup.ApplyFrameRate(); settle = 5f; }

        void Update()
        {
            float dt = Time.unscaledDeltaTime;
            ema = Mathf.Lerp(ema, dt, 0.05f);                    // ~20-frame smoothing: reacts in ~1s, ignores single spikes
            if (settle > 0f) { settle -= dt; lowTime = 0f; return; }
            if (cooldown > 0f) cooldown -= dt;

            float fps = 1f / Mathf.Max(ema, 1e-4f);
            if (fps < FloorFps - 4f)                             // genuinely under the floor (not a 59.x wobble)
            {
                okTime = 0f; lowTime += dt;
                if (lowTime > 2f && cooldown <= 0f && step < Steps.Length - 1)
                {
                    Apply(step + 1);
                    lowTime = 0f; cooldown = 6f;                 // give the new scale time to show its effect
                }
            }
            else
            {
                lowTime = 0f;
                float target = Application.targetFrameRate > 0 ? Application.targetFrameRate : FloorFps;
                if (fps >= target - 3f) okTime += dt; else okTime = 0f;
                if (okTime > 30f && step > 0 && cooldown <= 0f)  // long comfortable stretch -> try one notch back up
                {
                    Apply(step - 1);
                    okTime = 0f; cooldown = 10f;
                }
            }
        }

        void Apply(int s)
        {
            step = s;
            var urp = GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset;
            if (urp != null) urp.renderScale = Mathf.Clamp(baseScale * Steps[s], 0.1f, 2f);
        }

        void OnDestroy()
        {
            // Leave the asset exactly as authored (editor play-mode SO changes would otherwise stick).
            var urp = GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset;
            if (urp != null) urp.renderScale = baseScale;
            if (inst == this) inst = null;
        }
    }
}
