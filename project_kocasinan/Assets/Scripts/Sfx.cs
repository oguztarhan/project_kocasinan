using UnityEngine;

namespace BusJam
{
    /// <summary>Procedurally synthesized sound effects (no audio asset files needed).</summary>
    public class Sfx : MonoBehaviour
    {
        AudioSource src;
        AudioClip board, coin, deploy, error, win, lose, click;

        void Awake()
        {
            src = gameObject.AddComponent<AudioSource>();
            src.playOnAwake = false;
            src.spatialBlend = 0f;

            board  = Blip("board", 0.12f, 520f, 880f, 0.35f);
            coin   = Arp("coin", new[] { 880f, 1175f, 1568f }, 0.05f, 0.28f);
            deploy = Blip("deploy", 0.18f, 300f, 150f, 0.3f, noise: 0.25f);
            error  = Blip("error", 0.18f, 180f, 120f, 0.4f);
            win    = Arp("win", new[] { 523f, 659f, 784f, 1046f }, 0.1f, 0.4f);
            lose   = Arp("lose", new[] { 440f, 392f, 311f }, 0.12f, 0.4f);
            click  = Blip("click", 0.06f, 660f, 660f, 0.25f);
        }

        public void Board()  => Play(board);
        public void Coin()   => Play(coin);
        public void Deploy() => Play(deploy);
        public void Error()  => Play(error);
        public void Win()    => Play(win);
        public void Lose()   => Play(lose);
        public void Click()  => Play(click);

        void Play(AudioClip c)
        {
            if (c != null && SaveSystem.Sound) src.PlayOneShot(c);
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
            var clip = AudioClip.Create(name, n, 1, Rate, false);
            clip.SetData(data, 0);
            return clip;
        }
    }
}
