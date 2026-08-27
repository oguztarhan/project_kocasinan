using UnityEngine;

namespace Ridebury
{
    /// <summary>
    /// Procedurally synthesized sound effects, overridable by Resources/SoundCatalog.asset.
    /// ONE persistent instance for the whole game (DontDestroyOnLoad) with a SINGLE AudioSource that
    /// is stopped before every play — so SFX can NEVER overlap/mix; only the latest sound is heard.
    /// </summary>
    public class Sfx : MonoBehaviour
    {
        public static Sfx Instance { get; private set; }

        AudioSource src;     // one-shot SFX voice (stop-before-play -> these never overlap each other)
        AudioSource engine;  // SEPARATE looping voice for the "vehicle moving" vroom (a continuous layer)
        AudioSource heliSrc; // SEPARATE looping voice for the helicopter-joker rotor (owns the audio while flying)
        AudioClip board, coin, error, win, lose, click, crash, honk, screech, deploy, heli;
        AudioClip[] chest; // 4 rarity-graded chest-open fanfares (index = won car rarity 0..3)
        int heliVoices;      // # of helicopters in flight; the rotor loop owns the move-audio while > 0 (one ending heli can't cut another's audio)
        float master = 1f;                                   // master multiplier (catalog)
        // per-clip volumes (catalog), each 0..1
        float vBoard = 1f, vCoin = 1f, vError = 1f, vWin = 1f, vLose = 1f, vClick = 1f, vCrash = 1f, vHonk = 1f, vScreech = 1f, vDeploy = 1f, vHeli = 0.6f, vChest = 0.85f;

        /// <summary>Get the single Sfx, creating it if no scene has made one yet.</summary>
        public static Sfx Ensure()
        {
            if (Instance == null) new GameObject("Sfx").AddComponent<Sfx>(); // Awake wires Instance + DontDestroyOnLoad
            return Instance;
        }

        void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; } // enforce the singleton
            Instance = this;
            DontDestroyOnLoad(gameObject);

            src = gameObject.AddComponent<AudioSource>();
            src.playOnAwake = false;
            src.spatialBlend = 0f;

            engine = gameObject.AddComponent<AudioSource>();
            engine.playOnAwake = false;
            engine.spatialBlend = 0f;
            engine.loop = true;   // the vroom loops for as long as a vehicle is moving

            heliSrc = gameObject.AddComponent<AudioSource>();
            heliSrc.playOnAwake = false;
            heliSrc.spatialBlend = 0f;
            heliSrc.loop = true;  // rotor loops for the whole helicopter-joker flight

            // Your own clips (Resources/SoundCatalog.asset) OVERRIDE the built-ins; empty slots fall back.
            var cat = Resources.Load<SoundCatalog>("SoundCatalog");
            if (cat != null)
            {
                master   = Mathf.Clamp01(cat.volume);
                vBoard   = Mathf.Clamp01(cat.passengerBoardsBusVolume);
                vCoin    = Mathf.Clamp01(cat.coinRewardVolume);
                vError   = Mathf.Clamp01(cat.invalidActionErrorVolume);
                vWin     = Mathf.Clamp01(cat.levelCompleteVolume);
                vLose    = Mathf.Clamp01(cat.levelFailedVolume);
                vClick   = Mathf.Clamp01(cat.uiButtonClickVolume);
                vCrash   = Mathf.Clamp01(cat.vehicleBlockedCrashVolume);
                vHonk    = Mathf.Clamp01(cat.vehicleArrivesAtStopVolume);
                vScreech = Mathf.Clamp01(cat.fullBusDrivesAwayVolume);
                vDeploy  = Mathf.Clamp01(cat.vehicleSlidesOutVolume);
                vHeli    = Mathf.Clamp01(cat.helicopterVolume);
                vChest   = Mathf.Clamp01(cat.chestVolume);
            }

            // If the catalog slot is empty we randomly pick one of the 3 generated helicopter_0N clips per flight
            // (see Helicopter), so `heli` stays null here and only holds a user-assigned override.
            heli = cat ? cat.helicopter : null;

            board   = Pick(cat ? cat.passengerBoardsBus    : null, Blip("board", 0.12f, 520f, 880f, 0.35f));
            coin    = Pick(cat ? cat.coinReward            : null, Arp("coin", new[] { 880f, 1175f, 1568f }, 0.05f, 0.28f));
            error   = Pick(cat ? cat.invalidActionError    : null, Blip("error", 0.18f, 180f, 120f, 0.4f));
            win     = Pick(cat ? cat.levelComplete         : null, Arp("win", new[] { 523f, 659f, 784f, 1046f }, 0.1f, 0.4f));
            lose    = Pick(cat ? cat.levelFailed           : null, Arp("lose", new[] { 440f, 392f, 311f }, 0.12f, 0.4f));
            click   = Pick(cat ? cat.uiButtonClick         : null, Blip("click", 0.06f, 660f, 660f, 0.25f));
            crash   = Pick(cat ? cat.vehicleBlockedCrash   : null, BuildCrash());
            honk    = Pick(cat ? cat.vehicleArrivesAtStop  : null, BuildHonk());
            screech = Pick(cat ? cat.fullBusDrivesAway     : null, BuildScreech());
            deploy  = cat ? cat.vehicleSlidesOutOfJam : null; // no built-in (the drum was removed) — silent unless you add a clip

            // rarity-graded chest-open fanfares: a catalog clip per rarity OVERRIDES the built-in procedural one
            chest = new AudioClip[4];
            for (int r = 0; r < 4; r++) chest[r] = Pick(cat ? cat.ChestClip(r) : null, BuildChestFanfare(r));
        }

        static AudioClip Pick(AudioClip custom, AudioClip builtin) => custom != null ? custom : builtin;

        public void Board()   => Play(board,   vBoard);
        public void Coin()    => Play(coin,    vCoin);
        public void Error()   => Play(error,   vError);
        public void Win()     => Play(win,     vWin);
        public void Lose()    => Play(lose,    vLose);
        public void Click()   => Play(click,   vClick);
        public void Crash()   => Play(crash,   vCrash);
        public void Honk()    => Play(honk,    vHonk);
        public void Screech() => Play(screech, vScreech); // bus drives away — volume via catalog
        public void Chest(int rarity) => Play(chest != null ? chest[Mathf.Clamp(rarity, 0, 3)] : null, vChest); // rarity-graded chest open (0 Common .. 3 Legendary)

        /// <summary>Turn the looping engine (vroom) on/off. Called every frame from movement detection:
        /// on while ANY vehicle is moving, off the instant they all stop. Silent if no clip / sound is off.</summary>
        public void SetEngine(bool on)
        {
            if (engine == null) return;
            if (heliVoices > 0) { if (engine.isPlaying) engine.Stop(); return; } // a rotor loop owns the move-audio
            if (on && deploy != null && SaveSystem.Sound)
            {
                engine.volume = Mathf.Clamp01(master * vDeploy); // low volume via catalog
                if (!engine.isPlaying) { engine.clip = deploy; engine.Play(); }
            }
            else if (engine.isPlaying) engine.Stop();              // stop immediately when movement stops
        }

        /// <summary>Ref-counted start/stop for the looping helicopter-joker rotor — `on` when a chopper takes off,
        /// `!on` when it leaves. The loop plays while ANY chopper is up (so an exiting one re-tapped into a new
        /// one keeps a single continuous rotor) and mutes the vroom meanwhile (you hear the chopper, not the carried
        /// car's engine). Empty catalog slot -> a random one of the 3 built-in helicopter_0N clips, so lifts vary.</summary>
        public void Helicopter(bool on)
        {
            if (on) heliVoices++;
            else if (heliVoices > 0) heliVoices--;
            if (heliSrc == null) return;

            if (heliVoices > 0 && SaveSystem.Sound)
            {
                if (!heliSrc.isPlaying) // first chopper up -> start the loop (later overlapping ones just keep it going)
                {
                    AudioClip clip = heli != null ? heli : Resources.Load<AudioClip>("Sounds/helicopter_0" + Random.Range(1, 4));
                    if (clip == null) return;
                    heliSrc.clip = clip;
                    heliSrc.volume = Mathf.Clamp01(master * vHeli);
                    heliSrc.Play();
                }
                if (engine != null && engine.isPlaying) engine.Stop(); // hand the move-audio over to the rotor
            }
            else if (heliSrc.isPlaying) heliSrc.Stop();
        }

        /// <summary>Hard-reset the rotor (level teardown): drop the voice count and stop the loop, so two choppers
        /// caught mid-flight by a rebuild can't leave it droning.</summary>
        public void StopAllHelicopter()
        {
            heliVoices = 0;
            if (heliSrc != null && heliSrc.isPlaying) heliSrc.Stop();
        }

        void Play(AudioClip c, float vol = 1f)
        {
            if (c == null || !SaveSystem.Sound) return;
            src.Stop();                                 // kill whatever is playing first...
            src.clip = c;
            src.volume = Mathf.Clamp01(master * vol);   // master × per-clip volume
            src.Play();                                 // ...so it's strictly ONE sound at a time (never mixed/overlapping)
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

        // CHEST OPEN fanfare, grander the rarer (0 Common .. 3 Legendary): a rising bell arpeggio that resolves into a
        // held chord, with a sparkle shimmer on Epic+ and a low boom under the Legendary — so the player HEARS what
        // they won the instant the chest pops open. Each rarity is a different length + voicing, so they're unmistakable.
        AudioClip BuildChestFanfare(int rarity)
        {
            rarity = Mathf.Clamp(rarity, 0, 3);
            float[] scale = { 523.25f, 659.25f, 783.99f, 1046.5f, 1318.5f, 1568.0f }; // C5 E5 G5 C6 E6 G6
            int notes     = new[] { 2, 3, 4, 6 }[rarity];
            float noteDur = new[] { 0.085f, 0.085f, 0.090f, 0.095f }[rarity];
            float ringDur = new[] { 0.22f, 0.30f, 0.45f, 0.70f }[rarity];
            int nArp = (int)(Rate * noteDur), nRing = (int)(Rate * ringDur);
            int total = nArp * notes + nRing;
            var data = new float[total];
            const float vol = 0.42f;

            // rising bell arpeggio (fundamental + octave + 12th harmonics -> a bright chime)
            for (int k = 0; k < notes; k++)
            {
                float f = scale[k];
                for (int i = 0; i < nArp; i++)
                {
                    float t = (float)i / nArp, tau = i / (float)Rate, env = Mathf.Exp(-3.2f * t);
                    float s = Mathf.Sin(2f * Mathf.PI * f * tau)
                            + 0.45f * Mathf.Sin(2f * Mathf.PI * f * 2f * tau)
                            + 0.22f * Mathf.Sin(2f * Mathf.PI * f * 3f * tau);
                    data[k * nArp + i] += (s / 1.7f) * env * vol;
                }
            }

            // a resolved chord held under the ring-out (root + major third + fifth + octave)
            int baseI = notes * nArp;
            float root = scale[0];
            float[] chord = { root, root * 1.26f, root * 1.5f, root * 2f };
            for (int i = 0; i < nRing; i++)
            {
                float t = (float)i / nRing, tau = i / (float)Rate;
                float env = Mathf.Exp(-2.0f * t) * Mathf.Min(1f, (1f - t) / 0.12f); // rings out, click-free tail
                float s = 0f;
                for (int c = 0; c < chord.Length; c++) s += Mathf.Sin(2f * Mathf.PI * chord[c] * tau);
                data[baseI + i] += (s / chord.Length) * env * vol;
            }

            // sparkle shimmer over the ring-out (Epic+) — random high twinkles
            if (rarity >= 2)
            {
                int sparkles = rarity == 3 ? 16 : 7;
                for (int sp = 0; sp < sparkles; sp++)
                {
                    int start = baseI + (int)(nRing * (0.05f + 0.8f * Random.value));
                    float sf = 2200f + 3200f * Random.value;
                    int slen = (int)(Rate * 0.06f);
                    for (int i = 0; i < slen && start + i < total; i++)
                    {
                        float t = (float)i / slen;
                        data[start + i] += Mathf.Sin(2f * Mathf.PI * sf * (i / (float)Rate)) * Mathf.Exp(-7f * t) * 0.12f;
                    }
                }
            }

            // a low boom under the Legendary open for grandeur
            if (rarity == 3)
            {
                int blen = (int)(Rate * 0.22f);
                for (int i = 0; i < blen && i < total; i++)
                {
                    float t = (float)i / blen;
                    float fb = Mathf.Lerp(120f, 55f, t);
                    data[i] += Mathf.Sin(2f * Mathf.PI * fb * (i / (float)Rate)) * Mathf.Exp(-5f * t) * 0.35f;
                }
            }

            for (int i = 0; i < total; i++) data[i] = Mathf.Clamp(data[i], -1f, 1f);
            return Clip("chest" + rarity, data);
        }
    }
}
