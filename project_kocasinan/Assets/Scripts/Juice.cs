using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace BusJam
{
    /// <summary>Lightweight "juice": scale punches and cube-based particle bursts.
    /// Particles are plain cubes (no shader/material asset dependencies).</summary>
    public static class Juice
    {
        // Reentrant-safe punch state. punchRest = the TRUE rest scale per transform, captured ONCE while a
        // punch sequence is active; punchSeq = a generation token so a newer punch supersedes any in-flight
        // one. Without this, overlapping punches on the SAME transform (e.g. boarders every BoardGap=0.07s
        // vs a 0.22s punch) each snapshot the already-inflated localScale -> the scale COMPOUNDS and the
        // last-finishing coroutine writes an inflated "rest". Always animating from punchRest fixes that.
        static readonly Dictionary<Transform, Vector3> punchRest = new Dictionary<Transform, Vector3>();
        static readonly Dictionary<Transform, int> punchSeq = new Dictionary<Transform, int>();

        public static IEnumerator PunchScale(Transform t, float amount = 0.25f, float dur = 0.22f)
        {
            if (t == null) yield break;
            // Capture the true rest scale ONCE — only when no punch is already active on t.
            if (!punchRest.TryGetValue(t, out Vector3 rest)) { rest = t.localScale; punchRest[t] = rest; }
            int mySeq = (punchSeq.TryGetValue(t, out int s) ? s : 0) + 1;
            punchSeq[t] = mySeq; // become the owner; any older in-flight punch will see a newer seq and bail

            Vector3 peak = rest * (1f + amount);
            float e = 0f;
            while (e < dur)
            {
                if (t == null) { punchRest.Remove(t); punchSeq.Remove(t); yield break; }
                if (!punchSeq.TryGetValue(t, out int cur) || cur != mySeq) yield break; // superseded or evicted
                e += Time.deltaTime;
                float k = Mathf.Sin((e / dur) * Mathf.PI); // up then back (sine ease)
                t.localScale = Vector3.LerpUnclamped(rest, peak, k);
                yield return null;
            }
            // Latest owner finished -> restore the TRUE rest and clear state (so it never drifts).
            if (punchSeq.TryGetValue(t, out int last) && last == mySeq)
            {
                if (t != null) t.localScale = rest;
                punchRest.Remove(t);
                punchSeq.Remove(t);
            }
        }

        /// <summary>Evict a transform's punch state (restoring its rest scale if still alive). Call this
        /// before destroying a punched object so no stale dictionary entry leaks across levels.</summary>
        public static void StopPunch(Transform t)
        {
            if (ReferenceEquals(t, null)) return;                 // genuine null ref -> nothing keyed
            if (t != null && punchRest.TryGetValue(t, out Vector3 rest)) t.localScale = rest; // restore if not destroyed
            punchRest.Remove(t);
            punchSeq.Remove(t);
        }

        /// <summary>Drop ALL punch state. Call on level teardown (after StopAllCoroutines) so entries left
        /// behind by hard-stopped punch coroutines don't leak.</summary>
        public static void ClearAllPunches()
        {
            punchRest.Clear();
            punchSeq.Clear();
        }

        public static void Burst(MonoBehaviour runner, Transform parent, Vector3 pos, Material mat, int count, float power)
        {
            if (runner == null || mat == null) return;
            for (int i = 0; i < count; i++)
            {
                var c = GameObject.CreatePrimitive(PrimitiveType.Cube);
                Object.Destroy(c.GetComponent<Collider>());
                if (parent != null) c.transform.SetParent(parent, true);
                c.transform.position = pos;
                c.transform.localScale = Vector3.one * Random.Range(0.08f, 0.18f);
                c.transform.rotation = Random.rotation;
                c.GetComponent<Renderer>().sharedMaterial = mat;
                Vector3 v = new Vector3(Random.Range(-1f, 1f), Random.Range(0.6f, 1.4f), Random.Range(-1f, 1f)).normalized * power;
                runner.StartCoroutine(Fly(c.transform, v, Random.Range(0.7f, 1.3f)));
            }
        }

        /// <param name="dirX">0 = symmetric spray; +1/-1 = diagonal burst toward that side
        /// (used for the bottom-corner win confetti shooting up across the screen).</param>
        public static void Confetti(MonoBehaviour runner, Transform parent, Vector3 pos, Material[] mats, int count = 40, float dirX = 0f)
        {
            if (runner == null || mats == null || mats.Length == 0) return;
            for (int i = 0; i < count; i++)
            {
                var c = GameObject.CreatePrimitive(PrimitiveType.Cube);
                Object.Destroy(c.GetComponent<Collider>());
                if (parent != null) c.transform.SetParent(parent, true);
                c.transform.position = pos + new Vector3(Random.Range(-0.4f, 0.4f), 0, 0);
                c.transform.localScale = new Vector3(0.16f, 0.16f, 0.04f);
                c.transform.rotation = Random.rotation;
                c.GetComponent<Renderer>().sharedMaterial = mats[Random.Range(0, mats.Length)];
                float vx = dirX == 0f ? Random.Range(-2.5f, 2.5f) : dirX * Random.Range(2.0f, 5.0f);
                Vector3 v = new Vector3(vx, Random.Range(4.5f, 7.5f), Random.Range(-1f, 1f));
                runner.StartCoroutine(Fly(c.transform, v, Random.Range(1.2f, 2.0f)));
            }
        }

        static IEnumerator Fly(Transform t, Vector3 vel, float life)
        {
            float e = 0f;
            Vector3 spin = Random.insideUnitSphere * 360f;
            while (e < life && t != null)
            {
                float dt = Time.deltaTime;
                e += dt;
                vel += Vector3.down * 9.8f * dt;
                t.position += vel * dt;
                t.Rotate(spin * dt);
                yield return null;
            }
            if (t != null) Object.Destroy(t.gameObject);
        }
    }
}
