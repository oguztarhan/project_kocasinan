using UnityEngine;

namespace BusJam
{
    /// <summary>Procedurally synthesized sound effects (no audio asset files needed).</summary>
    public class Sfx : MonoBehaviour
    {
        AudioSource src;
        AudioClip board, coin, error, win, lose, click, crash, honk, screech, deploy;

        void Awake()
        {
            src = gameObject.AddComponent<AudioSource>();
            src.playOnAwake = false;
            src.spatialBlend = 0f;

            // Your own clips (Resources/SoundCatalog.asset) OVERRIDE the built-ins; empty slots fall back.
            var cat = Resources.Load<SoundCatalog>("SoundCatalog");
            if (cat != null) src.volume = Mathf.Clamp01(cat.volume);

            board   = Pick(cat ? cat.board   : null, Blip("board", 0.12f, 520f, 880f, 0.35f));
            coin    = Pick(cat ? cat.coin    : null, Arp("coin", new[] { 880f, 1175f, 1568f }, 0.05f, 0.28f));
            error   = Pick(cat ? cat.error   : null, Blip("error", 0.18f, 180f, 120f, 0.4f));
            win     = Pick(cat ? cat.win     : null, Arp("win", new[] { 523f, 659f, 784f, 1046f }, 0.1f, 0.4f));
            lose    = Pick(cat ? cat.lose    : null, Arp("lose", new[] { 440f, 392f, 311f }, 0.12f, 0.4f));
            click   = Pick(cat ? cat.click   : null, Blip("click", 0.06f, 660f, 660f, 0.25f));
            crash   = Pick(cat ? cat.crash   : null, BuildCrash());
            honk    = Pick(cat ? cat.honk    : null, BuildHonk());
            screech = Pick(cat ? cat.screech : null, BuildScreech());
            deploy  = cat ? cat.deploy : null; // no built-in (the drum was removed) — silent unless you add a clip
        }

        static AudioClip Pick(AudioClip custom, AudioClip builtin) => custom != null ? custom : builtin;

        public void Board()   => Play(board);
        public void Coin()    => Play(coin);
        public void Deploy()  => Play(deploy); // silent unless a clip is assigned in SoundCatalog
        public void Error()   => Play(error);
        public void Win()     => Play(win);
        public void Lose()    => Play(lose);
        public void Click()   => Play(click);
        public void Crash()   => Play(crash);
        public void Honk()    => Play(honk);
        public void Screech() => Play(screech);

        void Play(AudioClip c)
        {
            if (c == null || !SaveSystem.Sound) return;
            src.Stop();        // kill whatever is playing first...
            src.clip = c;
            src.Play();        // ...so it's strictly ONE sound at a time (never mixed/overlapping)
        }

        const int Rate = 44100;

        AudioClip Blip(string name, float dur, float f0, float f1, float vol, float noise = 0f)
        {
            int n = (int)(Rate * dur);
            var data = new float[n];
            for (int i = 0; i < n; i++)
            {
                float t = (float)i / n;                // 0..1
                float freq = Mathf.Lerp(f0, f1, t);
                float env = Mathf.Exp(-4f * t);
                float s = Mathf.Sin(2f * Mathf.PI * freq * (i / (float)Rate));
                if (noise > 0f) s = Mathf.Lerp(s, Random.value * 2f - 1f, noise);
                data[i] = s * env * vol;
            }
            var clip = AudioClip.Create(name, n, 1, Rate, false);
            clip.SetData(data, 0);
            return clip;
        }

        AudioClip Arp(string name, float[] notes, float noteDur, float vol)
        {
            int per = (int)(Rate * noteDur);
            int n = per * notes.Length;
            var data = new float[n];
            for (int k = 0; k < notes.Length; k++)
            {
                for (int i = 0; i < per; i++)
                {
                    float t = (float)i / per;
                    float env = Mathf.Exp(-3.5f * t);
                    float s = Mathf.Sin(2f * Mathf.PI * notes[k] * (i / (float)Rate));
                    data[k * per + i] = s * env * vol;
                }
            }
            return Clip(name, data);
        }

        AudioClip Clip(string name, float[] data)
        {
            var clip = AudioClip.Create(name, data.Length, 1, Rate, false);
            clip.SetData(data, 0);
            return clip;
        }

        // CRASH (collision): a big noise burst + a low impact thump + a metallic crunch, then a short tail.
        AudioClip BuildCrash()
        {
            float dur = 0.45f;
            int n = (int)(Rate * dur);
            var data = new float[n];
            for (int i = 0; i < n; i++)
            {
                float t = (float)i / n;
                float tau = i / (float)Rate;
                float noise = (Random.value * 2f - 1f) * Mathf.Exp(-8f * t);                                    // the crash body
                float thump = Mathf.Sin(2f * Mathf.PI * Mathf.Lerp(150f, 70f, t) * tau) * Mathf.Exp(-13f * t);  // low impact boom
                float crunch = (Mathf.Sin(2f * Mathf.PI * 850f * tau) + Mathf.Sin(2f * Mathf.PI * 1730f * tau))
                               * 0.5f * Random.value * Mathf.Exp(-6f * t);                                       // metallic crunch
                data[i] = Mathf.Clamp(noise * 0.6f + thump * 0.7f + crunch * 0.45f, -1f, 1f) * 0.48f;
            }
            return Clip("crash", data);
        }

        // One car honk: a held two-tone dyad (with harmonics) — quick attack, flat sustain, quick release.
        AudioClip BuildHonk()
        {
            float dur = 0.34f;
            int n = (int)(Rate * dur);
            var data = new float[n];
            float f0 = 415f, f1 = 522f; // a horn-like dyad
            for (int i = 0; i < n; i++)
            {
                float t = (float)i / n;
                float env = Mathf.Min(1f, t / 0.04f) * Mathf.Min(1f, (1f - t) / 0.12f);
                float tau = i / (float)Rate;
                float s = Mathf.Sin(2f * Mathf.PI * f0 * tau) + Mathf.Sin(2f * Mathf.PI * f1 * tau)
                        + 0.4f * Mathf.Sin(2f * Mathf.PI * f0 * 2f * tau) + 0.3f * Mathf.Sin(2f * Mathf.PI * f1 * 2f * tau);
                data[i] = (s / 2.7f) * env * 0.34f;
            }
            return Clip("honk", data);
        }

        // Tyre DRIFT: a long, wobbling squeal (pitch slides down mid-drift then back up) over
        // tyre-on-tarmac noise — sustained as the bus slides out of the area, then fades into the distance.
        AudioClip BuildScreech()
        {
            float dur = 0.7f;
            int n = (int)(Rate * dur);
            var data = new float[n];
            for (int i = 0; i < n; i++)
            {
                float t = (float)i / n;
                float vibrato = 1f + 0.06f * Mathf.Sin(2f * Mathf.PI * 14f * t);      // tyre wobble
                float bend = Mathf.Lerp(1350f, 950f, Mathf.Sin(t * Mathf.PI));         // pitch dips mid-slide then returns (the drift)
                float freq = bend * vibrato;
                float tone = Mathf.Sin(2f * Mathf.PI * freq * (i / (float)Rate));
                float s = Mathf.Lerp(tone, Random.value * 2f - 1f, 0.42f);             // squeal + tarmac noise
                float attack = Mathf.Min(1f, t / 0.04f);
                float tail = t > 0.9f ? (1f - t) / 0.1f : 1f;                          // click-free end
                float env = attack * Mathf.Exp(-1.7f * t) * tail;                     // sustains, then fades off into the distance
                data[i] = s * env * 0.3f;
            }
            return Clip("screech", data);
        }
    }
}
