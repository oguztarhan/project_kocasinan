using System.Collections;
using UnityEngine;

namespace BusJam
{
    /// <summary>Lightweight "juice": scale punches and cube-based particle bursts.
    /// Particles are plain cubes (no shader/material asset dependencies).</summary>
    public static class Juice
    {
        public static IEnumerator PunchScale(Transform t, float amount = 0.25f, float dur = 0.22f)
        {
            if (t == null) yield break;
            Vector3 baseScale = t.localScale;
            Vector3 peak = baseScale * (1f + amount);
            float e = 0f;
            while (e < dur)
            {
                if (t == null) yield break;
                e += Time.deltaTime;
                float k = e / dur;
                // up then back (sine ease)
                float s = Mathf.Sin(k * Mathf.PI);
                t.localScale = Vector3.LerpUnclamped(baseScale, peak, s);
                yield return null;
            }
            if (t != null) t.localScale = baseScale;
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

        public static void Confetti(MonoBehaviour runner, Transform parent, Vector3 pos, Material[] mats, int count = 40)
        {
            if (runner == null || mats == null || mats.Length == 0) return;
            for (int i = 0; i < count; i++)
            {
                var c = GameObject.CreatePrimitive(PrimitiveType.Cube);
                Object.Destroy(c.GetComponent<Collider>());
                if (parent != null) c.transform.SetParent(parent, true);
                c.transform.position = pos + new Vector3(Random.Range(-1.5f, 1.5f), 0, 0);
                c.transform.localScale = new Vector3(0.16f, 0.16f, 0.04f);
                c.transform.rotation = Random.rotation;
                c.GetComponent<Renderer>().sharedMaterial = mats[Random.Range(0, mats.Length)];
                Vector3 v = new Vector3(Random.Range(-2.5f, 2.5f), Random.Range(3f, 6f), Random.Range(-1f, 1f));
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
