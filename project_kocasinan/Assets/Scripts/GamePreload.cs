using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace BusJam
{
    /// <summary>
    /// Warms the HEAVY gameplay assets while the boot loading screen / main menu is showing, so the first PLAY doesn't
    /// stall loading them (that was the ~20s freeze). It async-loads the vehicle catalog — which pulls in every vehicle
    /// prefab and its mesh + texture tree — and HOLDS a reference to it so UnloadUnusedAssets (fired by the menu->game
    /// scene swap) can't drop them. Then BusJamGame's Resources.Load returns the already-loaded copy and just instantiates.
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
        static Object pinned;                                  // keep-alive ref so the loaded tree survives the scene swap

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
            // Loading the catalog pulls in its whole serialized dependency tree: every vehicle prefab + their meshes
            // and textures (the bulk of the first-Play cost). Async so the loading-screen animation keeps running.
            var req = Resources.LoadAsync<VehicleSetCatalog>("VehicleSetCatalog");
            while (req != null && !req.isDone) { Progress = req.progress; yield return null; }
            pinned = req != null ? req.asset : null;   // hold it -> the LoadScene unload can't drop the warmed assets
            Progress = 1f; Done = true;
        }
    }
}
