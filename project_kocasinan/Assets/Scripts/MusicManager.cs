using UnityEngine;

namespace BusJam
{
    /// <summary>
    /// Background music. Plays ONE looping track at a time, chosen by context:
    ///   • the main menu calls PlayMenu()            -> SoundCatalog.menuMusic
    ///   • each level calls PlayTheme(themeName)      -> SoundCatalog's track for that theme
    /// Tracks + volumes are all set in Resources/SoundCatalog.asset (per-theme list). If a slot is empty it falls
    /// back to a sensible default track. One persistent instance across scenes; honors the Settings "Music" toggle.
    ///
    /// Music is its own AudioSource (separate from Sfx), so it layers under the SFX rather than mixing with them.
    /// </summary>
    public class MusicManager : MonoBehaviour
    {
        static MusicManager instance;
        AudioSource src;
        SoundCatalog cat;
        AudioClip current;
        float curVol = 0.5f;
        bool paused;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot() => Ensure();

        public static void Ensure()
        {
            if (instance != null) return;
            var go = new GameObject("MusicManager");
            instance = go.AddComponent<MusicManager>();
            DontDestroyOnLoad(go);
        }

        // ---- public API (static so callers don't worry about lifetime) ----
        public static void PlayMenu()              { Ensure(); instance.PlayMenuInternal(); }
        public static void PlayTheme(string theme) { Ensure(); instance.PlayThemeInternal(theme); }

        void Awake()
        {
            if (instance != null && instance != this) { Destroy(gameObject); return; }
            instance = this;
            DontDestroyOnLoad(gameObject);

            ApplyIosAudioSession(); // iOS: play through the silent switch (see helper) — no-op elsewhere

            src = gameObject.AddComponent<AudioSource>();
            src.playOnAwake = false;
            src.loop = true;            // one track loops until we switch context
            src.spatialBlend = 0f;

            cat = Resources.Load<SoundCatalog>("SoundCatalog");
        }

        // Re-assert the iOS audio session when we come back to the foreground: interruptions (phone call, Siri, alarm)
        // can deactivate/reset it, which would otherwise leave music dead until the next launch.
        void OnApplicationPause(bool paused) { if (!paused) ApplyIosAudioSession(); }

#if UNITY_IOS && !UNITY_EDITOR
        [System.Runtime.InteropServices.DllImport("__Internal")]
        static extern void _BusJamSetAudioSessionPlayback();
#endif

        // iOS only: force the AVAudioSession category to .Playback so music/SFX are NOT silenced by the hardware
        // silent/mute switch (Android has no such switch — that's why music played there but not on iOS). Compiles to
        // nothing on Android / in the editor. Native side: Assets/Plugins/iOS/BusJamAudioSession.mm.
        static void ApplyIosAudioSession()
        {
#if UNITY_IOS && !UNITY_EDITOR
            try { _BusJamSetAudioSessionPlayback(); } catch { /* audio must never break startup */ }
#endif
        }

        void PlayMenuInternal()
        {
            AudioClip clip = cat != null ? cat.menuMusic : null;
            if (clip == null) clip = SoundCatalog.DefaultMenuTrack();
            Switch(clip, cat != null ? cat.menuMusicVolume : 0.5f);
        }

        void PlayThemeInternal(string theme)
        {
            AudioClip clip = null; float vol = 0.5f;
            if (cat != null) cat.TryGetThemeMusic(theme, out clip, out vol);
            if (clip == null) clip = SoundCatalog.DefaultThemeTrack(theme); // fallback if the slot is empty
            Switch(clip, vol);
        }

        // Switch to a track (no-op if it's already the one playing, just refreshes volume).
        void Switch(AudioClip clip, float vol)
        {
            curVol = Mathf.Clamp01(vol);
            if (clip == null) { src.Stop(); current = null; return; }
            if (clip == current)
            {
                src.volume = curVol;
                return;
            }
            current = clip;
            src.clip = clip;
            src.volume = curVol;
            paused = false;
            if (SaveSystem.Music) src.Play();
        }

        void Update()
        {
            if (current == null) return;
            if (!SaveSystem.Music)                                   // music turned off -> pause, hold position
            {
                if (src.isPlaying) { src.Pause(); paused = true; }
                return;
            }
            if (paused) { src.UnPause(); paused = false; }
            else if (!src.isPlaying) src.Play();                     // (re)start if it was stopped
            if (src.volume != curVol) src.volume = curVol;           // live-apply volume tweaks from the catalog
        }
    }
}
