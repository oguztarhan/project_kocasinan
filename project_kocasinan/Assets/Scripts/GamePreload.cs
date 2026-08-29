using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Ridebury
{
    /// <summary>
    /// Warms the HEAVY gameplay assets while the boot loading screen / main menu is showing, so the first PLAY doesn't
    /// stall loading them (that was the ~20s freeze). It async-loads the vehicle catalog — which pulls in every vehicle
    /// prefab and its mesh + texture tree — and HOLDS a reference to it so UnloadUnusedAssets (fired by the menu->game
    /// scene swap) can't drop them. Then RideburyGame's Resources.Load returns the already-loaded copy and just instantiates.
    ///
    /// Self-spawns at launch into the MainMenu (no scene/Inspector wiring). BootSplash reads Progress/Done to pace its
    /// bar so the menu only appears once everything is warm. Fully safe: if it never runs (or the catalog is missing),
    /// Done flips true immediately and Play loads exactly as before — it can only ever make the first Play faster.
    /// </summary>
    public class GamePreload : MonoBehaviour
    {
        public static float Progress { get; private set; }   // 0..1
        public static bool  Done     { get; private set; }
        static GamePreload inst;
        // Keep-alive refs so the loaded trees survive the scene swap. This must cover EVERY prefab the pool was warmed
        // with, not just the set catalog: ModelPool keys its pool by prefab.GetInstanceID(), so if the menu->game
        // UnloadUnusedAssets drops a catalog, RideburyGame's Resources.Load brings the prefabs back with NEW instance
        // ids — every pooled clone is then unreachable and level 1 re-instantiates the lot (skinned Animator init on
        // ~20 people + the high-poly vehicles) synchronously, which is exactly the cost this prewarm exists to remove.
        static readonly List<Object> pinned = new List<Object>();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot()
        {
            if (inst != null || Done) return;
            // Only warm from the menu launch. (A dev pressing Play directly into the gameplay scene has nothing to warm.)
            if (SceneManager.GetActiveScene().name != "MainMenu") { Done = true; return; }
            var go = new GameObject("~GamePreload");
            DontDestroyOnLoad(go);
            inst = go.AddComponent<GamePreload>();
        }

        IEnumerator Start()
        {
            // 1) Loading the catalog pulls in its whole serialized dependency tree: every vehicle prefab + their meshes
            // and textures (the bulk of the first-Play cost). Async so the loading-screen animation keeps running.
            var req = Resources.LoadAsync<VehicleSetCatalog>("VehicleSetCatalog");
            while (req != null && !req.isDone) { Progress = req.progress * 0.6f; yield return null; }
            if (req != null && req.asset != null) pinned.Add(req.asset); // hold it -> the LoadScene unload can't drop the warmed assets
            Progress = 0.6f;

            // 2) PRE-FILL the model pool while the splash is still up: pre-instantiate the vehicle + character models
            // gameplay will spawn, a couple per frame (keeps the splash animating). StartLevel's board build and the
            // MID-PLAY person streaming then POP from the pool instead of Instantiate'ing — that runtime Instantiate
            // (high-poly meshes + skinned Animator init) was the vehicle-spawn hitch on weak phones.
            bool tight = DeviceSetup.DeviceTier != DeviceSetup.Tier.High; // Low + Mid phones -> smaller prewarm pools
            var jobs = new List<(GameObject prefab, int n)>();
            var vcat = Resources.Load<VehicleCatalog>("VehicleCatalog");
            void AddVehicle(VehicleType t, int n)
            {
                GameObject pf = VehicleWardrobe.EquippedModel(t);          // the model gameplay will actually use
                if (pf == null && vcat != null) pf = vcat.PrefabFor(t);    // catalog default fallback (same rule as CreateBus)
                if (pf != null) jobs.Add((pf, tight ? Mathf.Max(1, n / 2) : n));
            }
            AddVehicle(VehicleType.Car, 10);     // a board is mostly cars
            AddVehicle(VehicleType.Minivan, 6);
            AddVehicle(VehicleType.Bus, 6);
            var pcat = Resources.Load<PeopleCatalog>("PeopleCatalog");
            if (pcat != null && pcat.prefabs != null)
            {
                // With a big cast (20 Ridebury people) one warm clone each is plenty — a board only shows ~10
                // queue + ~21 crowd figures drawn at random from the whole set, so warming 2 of each just
                // doubles boot instantiates for clones that mostly never get used.
                int per = (tight || pcat.prefabs.Length > 10) ? 1 : 2;
                foreach (var pp in pcat.prefabs)
                    if (pp != null) jobs.Add((pp, per));                    // queue + crowd characters
            }

            // Pin the other two catalogs AND every prefab we are about to warm. Without this the scene swap's automatic
            // UnloadUnusedAssets is free to drop them (nothing else references them once this coroutine ends), which
            // silently invalidates every pool key we fill below — see the `pinned` note at the top.
            if (vcat != null) pinned.Add(vcat);
            if (pcat != null) pinned.Add(pcat);
            foreach (var (prefab, _) in jobs) pinned.Add(prefab);

            int target = 0, made = 0;
            foreach (var j in jobs) target += j.n;
            foreach (var (prefab, n) in jobs)
                for (int i = 0; i < n; i++)
                {
                    ModelPool.Prewarm(prefab, 1);
                    made++;
                    Progress = 0.6f + 0.35f * (target > 0 ? made / (float)target : 1f);
                    if ((made & 1) == 0) yield return null;                // 2 instantiates per frame -> no splash stutter
                }

            // 3) NO Shader.WarmupAllShaders() here (removed): it synchronously compiles EVERY variant of every loaded
            // shader on the main thread — on weak GPUs / buggy drivers that block ran for minutes or hung outright,
            // freezing the boot splash on exactly the phones it was meant to help (even BootSplash's HardTimeout can't
            // fire while the main thread is blocked). The pool prewarm above already removes the big first-Play hitch
            // (mesh + Animator init); a first-render shader compile is a one-frame stutter, never a soft-lock.
            yield return null;
            Progress = 1f; Done = true;
        }
    }
}
