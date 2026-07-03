using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace BusJam
{
    /// <summary>
    /// Prefab-clone POOL for the expensive runtime models: high-poly vehicles, skinned queue characters, env decor
    /// and particle FX. Runtime Instantiate of these (and Destroy of their whole hierarchies) is THE frame spike on
    /// weak phones — worst when new vehicles/people appear and on level transitions. The pool keeps released clones
    /// parented under a hidden DontDestroyOnLoad root and hands them back reset-to-fresh, so gameplay code builds on
    /// them exactly as if they came from Instantiate:
    ///
    ///   • Get(prefab, parent)   — pooled clone (falls back to Instantiate on a miss). Materials restored to the
    ///                             prefab originals, transform reset, Animator rebound, particles cleared.
    ///   • Release(model)        — reclaim ONE clone (instead of Destroy).
    ///   • ReleaseAllUnder(root) — reclaim every pooled clone beneath root; call BEFORE Destroy(root) on teardown,
    ///                             so a level swap recycles ~all its models instead of destroying + re-instantiating.
    ///   • ReleaseAfter(go, t)   — timed reclaim for one-shot FX (replaces Destroy(go, t)).
    ///   • Prewarm(prefab, n)    — pre-instantiate into the pool (loading screen), so first use never instantiates.
    ///
    /// Reissue-as-fresh invariants (why reuse is safe):
    ///   – original sharedMaterials captured at clone time are restored on Get -> the build-time tint paths (which
    ///     read the instance's CURRENT materials) behave exactly like a fresh Instantiate;
    ///   – the "ToonEdge" outline duplicates OutlineAll adds under model parts are DETACHED+destroyed on release, so
    ///     renderer order/count matches the prefab again (the recolor paths map prefab->instance renderers by index);
    ///   – mesh trims (VehiclePreview.TrimBase) are idempotent + cached, so they stay applied and cost a dict hit.
    /// </summary>
    public class ModelPool : MonoBehaviour
    {
        /// <summary>Marker carried by every pooled clone: its pool key + the pristine material set to restore.</summary>
        public class PooledModel : MonoBehaviour
        {
            public int key;                 // source prefab instance id
            public Renderer[] rends;        // renderers as cloned (prefab order — ToonEdge dups are stripped on release)
            public Material[][] origMats;   // their sharedMaterials at clone time (prefab originals)
            public bool inPool;             // guards double-release (timed FX release after a teardown reclaim)
            public int generation;          // bumped on every reissue: a STALE timed release (scheduled for a previous
                                            // life of this clone) must never reclaim its CURRENT life mid-use
        }

        const int MaxPerPrefab = 48;        // memory bound: beyond this, released clones are destroyed (a bonus board is ~32 vehicles)

        static ModelPool inst;
        readonly Dictionary<int, Stack<PooledModel>> free = new Dictionary<int, Stack<PooledModel>>();

        static ModelPool Inst
        {
            get
            {
                if (inst == null)
                {
                    var go = new GameObject("~ModelPool");
                    DontDestroyOnLoad(go);
                    inst = go.AddComponent<ModelPool>();
                }
                return inst;
            }
        }

        /// <summary>A clone of `prefab` under `parent`, reset to factory state. Never null for a non-null prefab.</summary>
        public static GameObject Get(GameObject prefab, Transform parent)
        {
            if (prefab == null) return null;
            var p = Inst;
            if (p.free.TryGetValue(prefab.GetInstanceID(), out var stack))
                while (stack.Count > 0)
                {
                    var pm = stack.Pop();
                    if (pm == null) continue;          // externally destroyed -> drop the stale entry
                    p.Reissue(pm, parent);
                    return pm.gameObject;
                }
            return p.CreateNew(prefab, parent);
        }

        /// <summary>Reclaim one clone (use instead of Destroy). Non-pooled objects are just destroyed.</summary>
        public static void Release(GameObject model)
        {
            if (model == null) return;
            var pm = model.GetComponent<PooledModel>();
            if (pm == null) { Destroy(model); return; }
            Inst.Take(pm);
        }

        /// <summary>Reclaim every pooled clone beneath `root`. Call BEFORE Destroy(root).</summary>
        public static void ReleaseAllUnder(Transform root)
        {
            if (root == null || inst == null) return;  // no pool yet -> nothing was ever pooled
            var pms = root.GetComponentsInChildren<PooledModel>(true);
            for (int i = 0; i < pms.Length; i++)
                if (pms[i] != null && !pms[i].inPool) inst.Take(pms[i]);
        }

        /// <summary>Timed reclaim for one-shot FX — the pooled replacement for Destroy(go, delay).</summary>
        public static void ReleaseAfter(GameObject model, float delay)
        {
            if (model == null) return;
            var pm = model.GetComponent<PooledModel>();
            Inst.StartCoroutine(Inst.ReleaseAfterCo(model, delay, pm != null ? pm.generation : 0));
        }

        /// <summary>Pre-instantiate `count` clones into the pool (loading screen), so first use is a pop, not an Instantiate.</summary>
        public static void Prewarm(GameObject prefab, int count)
        {
            if (prefab == null || count <= 0) return;
            var p = Inst;
            int key = prefab.GetInstanceID();
            if (!p.free.TryGetValue(key, out var stack)) p.free[key] = stack = new Stack<PooledModel>();
            for (int i = 0; i < count && stack.Count < MaxPerPrefab; i++)
            {
                var go = p.CreateNew(prefab, p.transform);
                var pm = go.GetComponent<PooledModel>();
                pm.inPool = true;
                go.SetActive(false);
                stack.Push(pm);
            }
        }

        // ---------------- internals ----------------

        GameObject CreateNew(GameObject prefab, Transform parent)
        {
            var go = Instantiate(prefab, parent, false);
            var pm = go.AddComponent<PooledModel>();
            pm.key = prefab.GetInstanceID();
            pm.rends = go.GetComponentsInChildren<Renderer>(true);
            pm.origMats = new Material[pm.rends.Length][];
            for (int i = 0; i < pm.rends.Length; i++)
                pm.origMats[i] = pm.rends[i].sharedMaterials; // getter returns a copy -> pristine snapshot
            return go;
        }

        void Reissue(PooledModel pm, Transform parent)
        {
            pm.inPool = false;
            pm.generation++; // invalidate any timed release still pending from this clone's PREVIOUS life
            var t = pm.transform;
            t.SetParent(parent, false);
            t.localPosition = Vector3.zero; t.localRotation = Quaternion.identity; t.localScale = Vector3.one;
            for (int i = 0; i < pm.rends.Length; i++)
                if (pm.rends[i] != null) pm.rends[i].sharedMaterials = pm.origMats[i]; // factory materials -> tint paths behave like first build
            var anim = pm.GetComponentInChildren<Animator>(true);
            if (anim != null) { anim.enabled = true; anim.Rebind(); anim.Update(0f); } // fresh pose/state (a crowd build may re-freeze it)
            pm.gameObject.SetActive(true);
        }

        void Take(PooledModel pm)
        {
            if (pm == null || pm.inPool) return;
            pm.inPool = true;
            // Strip the ToonEdge outline dups OutlineAll added under the model. DETACH first (Destroy is deferred to
            // end-of-frame, but teardown->rebuild runs in ONE frame — a still-attached dying dup would shift the
            // renderer index mapping the recolor paths rely on).
            var kids = pm.GetComponentsInChildren<Transform>(true);
            for (int i = 0; i < kids.Length; i++)
                if (kids[i] != null && kids[i].name == "ToonEdge")
                {
                    kids[i].SetParent(null, false);
                    Destroy(kids[i].gameObject);
                }
            foreach (var ps in pm.GetComponentsInChildren<ParticleSystem>(true))
                ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear); // FX come back empty, not mid-puff
            pm.transform.SetParent(transform, false);
            pm.gameObject.SetActive(false);
            if (!free.TryGetValue(pm.key, out var stack)) free[pm.key] = stack = new Stack<PooledModel>();
            if (stack.Count >= MaxPerPrefab) { Destroy(pm.gameObject); return; }
            stack.Push(pm);
        }

        IEnumerator ReleaseAfterCo(GameObject model, float delay, int generation)
        {
            yield return new WaitForSeconds(delay);
            if (model == null) yield break;
            var pm = model.GetComponent<PooledModel>();
            if (pm == null) { Destroy(model); yield break; }
            // Reclaim ONLY the life this timer was armed for: if a teardown already pooled the clone AND a new level
            // reissued it meanwhile, generation moved on — this stale timer must not steal the new owner's instance.
            if (!pm.inPool && pm.generation == generation) Take(pm);
        }
    }
}
