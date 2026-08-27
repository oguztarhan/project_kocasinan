using UnityEngine;

namespace Ridebury
{
    /// <summary>
    /// Ambient life for a queue/crowd character. There is no rig and no animation clips — this drives the
    /// six imported blend shapes plus the model transform, so it costs almost nothing and behaves the same
    /// on all 20 characters.
    ///
    ///   IdleF / IdleB  — both arms swung fore/aft about the shoulder. Blended sinusoidally this is the
    ///                    idle sway; without it the figures read as mannequins no matter how much the
    ///                    body breathes, because nothing on the silhouette moves.
    ///   Blink          — quick eye close, with occasional double-blinks.
    ///   Laugh/Mad/Surprise — expressions, ambient or triggered by gameplay.
    ///
    /// Breathing is a volume-preserving squash/stretch on the transform. It works because every character's
    /// origin sits at its feet: scaling Y about that pivot keeps the feet planted and moves only the top.
    ///
    /// Every rate and phase is randomised per instance so a queue never pulses in unison.
    ///
    /// Init() MUST be called after the spawner has set the model's transform — it captures that as the rest
    /// pose, and it is also what re-arms state when a pooled clone is reissued.
    /// </summary>
    public class CharacterLife : MonoBehaviour
    {
        public enum Mood { Neutral, Laugh, Mad, Surprise }

        [Header("Idle feel")]
        public static float BreathAmp = 0.045f;   // trunk squash/stretch, fraction of height
        public static float SwingAmp  = 1.00f;    // 1 = full authored arm swing (17 degrees)

        SkinnedMeshRenderer smr;
        int iBlink = -1, iLaugh = -1, iMad = -1, iSurprise = -1, iIdleF = -1, iIdleB = -1;

        Vector3 baseScale, basePos;
        Quaternion baseRot;
        float phase, breathRate, swingRate, swayRate, breathAmp, swingAmp;

        float blinkAt, blinkT = -1f;
        Mood mood = Mood.Neutral;
        float moodW, moodHold, moodAt;
        bool expressive = true;

        // last written weights — a blend-shape write forces a re-skin, so only touch what changed
        float wBlink = -1f, wMood = -1f, wSwing = -2f;
        Mood wMoodKind = Mood.Neutral;

        /// <param name="expressiveMoods">false = breathe/blink/sway only (background crowd, low-end).</param>
        public void Init(bool expressiveMoods = true)
        {
            smr = GetComponentInChildren<SkinnedMeshRenderer>(true);
            var mesh = smr != null ? smr.sharedMesh : null;
            if (mesh != null)
            {
                iBlink    = mesh.GetBlendShapeIndex("Blink");
                iLaugh    = mesh.GetBlendShapeIndex("Laugh");
                iMad      = mesh.GetBlendShapeIndex("Mad");
                iSurprise = mesh.GetBlendShapeIndex("Surprise");
                iIdleF    = mesh.GetBlendShapeIndex("IdleF");
                iIdleB    = mesh.GetBlendShapeIndex("IdleB");
            }

            baseScale  = transform.localScale;
            basePos    = transform.localPosition;
            baseRot    = transform.localRotation;
            phase      = Random.value * Mathf.PI * 2f;
            breathRate = Random.Range(0.70f, 1.05f);
            swingRate  = Random.Range(0.30f, 0.55f);   // slower than the breath, so it never looks metronomic
            swayRate   = Random.Range(0.22f, 0.42f);
            breathAmp  = BreathAmp * Random.Range(0.80f, 1.20f);
            swingAmp   = SwingAmp  * Random.Range(0.55f, 1.00f);
            expressive = expressiveMoods;

            blinkT = -1f; blinkAt = Time.time + Random.Range(0.8f, 5f);
            mood = Mood.Neutral; moodW = 0f; moodHold = 0f;
            moodAt = Time.time + Random.Range(5f, 14f);
            wBlink = wMood = -1f; wSwing = -2f; wMoodKind = Mood.Neutral;
            ApplyShapes(0f, Mood.Neutral, 0f, 0f);
        }

        /// <summary>Play an expression for `seconds` (laugh on boarding, mad on a dead end…).</summary>
        public void Express(Mood m, float seconds = 1.2f)
        {
            if (m == Mood.Neutral) { moodHold = 0f; return; }
            mood = m;
            moodHold = seconds;
            moodAt = Time.time + seconds + Random.Range(5f, 14f);
        }

        void Update()
        {
            float t = Time.time;

            // ---- breathing: volume-preserving squash/stretch about the feet, plus a small settle ----
            float b = Mathf.Sin(t * breathRate + phase);
            float uy = 1f + breathAmp * b;
            float ux = 1f - breathAmp * 0.62f * b;
            transform.localScale = new Vector3(baseScale.x * ux, baseScale.y * uy, baseScale.z * ux);
            transform.localPosition = basePos + new Vector3(0f, -breathAmp * 0.35f * b * baseScale.y, 0f);

            // ---- weight shift: a slow lean so a row of them never looks like a row of posts ----
            float sway = Mathf.Sin(t * swayRate + phase * 1.7f) * 2.6f;
            transform.localRotation = baseRot * Quaternion.Euler(0f, 0f, sway);

            // ---- arm swing ----
            float swing = Mathf.Sin(t * swingRate + phase * 0.6f) * swingAmp;

            // ---- blink ----
            if (blinkT < 0f && t >= blinkAt) blinkT = 0f;
            float blink = 0f;
            if (blinkT >= 0f)
            {
                blinkT += Time.deltaTime;
                const float dur = 0.14f;
                blink = Mathf.Clamp01(blinkT < dur * 0.5f ? blinkT / (dur * 0.5f)
                                                          : 1f - (blinkT - dur * 0.5f) / (dur * 0.5f));
                if (blinkT >= dur)
                {
                    blinkT = -1f;
                    blinkAt = t + (Random.value < 0.18f ? 0.22f : Random.Range(2.5f, 7f));
                }
            }

            // ---- moods ----
            if (expressive)
            {
                if (moodHold <= 0f && t >= moodAt)
                {
                    float r = Random.value;
                    mood = r < 0.55f ? Mood.Laugh : (r < 0.80f ? Mood.Surprise : Mood.Mad);
                    moodHold = Random.Range(0.7f, 1.6f);
                }
                if (moodHold > 0f) moodHold -= Time.deltaTime;
                moodW = Mathf.MoveTowards(moodW, moodHold > 0f ? 1f : 0f, Time.deltaTime * 4.5f);
                if (moodW <= 0f && moodHold <= 0f && t >= moodAt) moodAt = t + Random.Range(5f, 14f);
            }
            else moodW = 0f;

            ApplyShapes(blink, mood, moodW, swing);
        }

        void ApplyShapes(float blink, Mood m, float w, float swing)
        {
            if (smr == null) return;

            if (!Mathf.Approximately(blink, wBlink))
            {
                if (iBlink >= 0) smr.SetBlendShapeWeight(iBlink, blink * 100f);
                wBlink = blink;
            }
            if (Mathf.Abs(swing - wSwing) > 0.004f)
            {
                // one shape per direction, so the weights stay in the safe 0..100 range
                if (iIdleF >= 0) smr.SetBlendShapeWeight(iIdleF, Mathf.Max(0f,  swing) * 100f);
                if (iIdleB >= 0) smr.SetBlendShapeWeight(iIdleB, Mathf.Max(0f, -swing) * 100f);
                wSwing = swing;
            }
            if (m != wMoodKind || !Mathf.Approximately(w, wMood))
            {
                if (wMoodKind != m) SetMood(wMoodKind, 0f);
                SetMood(m, w * 100f);
                wMoodKind = m; wMood = w;
            }
        }

        void SetMood(Mood m, float weight)
        {
            int idx = m == Mood.Laugh ? iLaugh : m == Mood.Mad ? iMad : m == Mood.Surprise ? iSurprise : -1;
            if (idx >= 0) smr.SetBlendShapeWeight(idx, weight);
        }
    }
}
