using UnityEngine;
using UnityEngine.SceneManagement;

namespace Ridebury
{
    /// <summary>
    /// EVERY authored UI screen lives in ONE prefab under <c>Assets/Resources/UI/</c> — never baked
    /// into a scene any more. Open the prefab, edit it in the Inspector, save: both scenes pick the
    /// change up, and there is nothing to keep in sync.
    ///
    /// <list type="bullet">
    /// <item><c>UI/HudPanel</c>      — in-game HUD (coin bar, level badge, gear, jokers, GARAGE button)</item>
    /// <item><c>UI/GamePanels</c>    — Settings / Continue / Failed / Success / joker-buy / language pop-ups</item>
    /// <item><c>UI/GaragePanel</c>   — garage + wardrobe canvas (also carries the InGameGarage style overrides)</item>
    /// <item><c>UI/TutorialPanel</c> — tutorial banner behind the coach text</item>
    /// <item><c>UI/MenuUI</c>        — the whole main menu (MenuController rides on its root)</item>
    /// <item><c>UI/ShopPanel</c>     — THE shop, shared by the menu and the game (see <see cref="ShopUI"/>)</item>
    /// </list>
    ///
    /// This class spawns the right ones for whichever scene just loaded, BEFORE any Start() runs, so
    /// <see cref="GameUI"/> / MenuController adopt them exactly as they used to adopt the scene bakes.
    /// A copy still sitting in the scene (e.g. one checked out with "Tools ▸ 300Mind UI ▸ UI Prefabs ▸
    /// Check Out") always wins, so an in-progress edit is never shadowed by a second copy.
    ///
    /// Bake / migrate with "Tools ▸ 300Mind UI ▸ UI Prefabs ▸ …" (see UIPrefabBaker).
    /// </summary>
    public static class UIPrefabs
    {
        // ---- Resources paths (Assets/Resources/UI/<name>.prefab) ----
        public const string Hud      = "UI/HudPanel";
        public const string Panels   = "UI/GamePanels";
        public const string Garage   = "UI/GaragePanel";
        public const string Tutorial = "UI/TutorialPanel";
        public const string Menu     = "UI/MenuUI";
        public const string Shop     = ShopUI.ResourcePath;

        // ---- Names the spawned instances take. They match the old scene-baked roots, so every
        //      editor baker that looks its target up by name keeps working on a checked-out copy.
        public const string HudRoot      = "InGameHud_Baked";
        public const string PanelsRoot   = "InGamePanels_Baked";
        public const string GarageRoot   = "InGameGarageCanvas";
        public const string TutorialRoot = "TutorialPanel_Baked";
        public const string MenuRoot     = "MenuUI_Baked";

        /// <summary>Main-menu scene name (used when the scene carries no gameplay/menu component to sniff).</summary>
        public const string MenuSceneName = "MainMenu";

        // ---- Auto-spawn ------------------------------------------------------

        // Runs after the first scene's Awake but before any Start, then again for every later
        // scene load — the same window the scene-baked UI used to be alive in.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Install()
        {
            SceneManager.sceneLoaded -= OnSceneLoaded;
            SceneManager.sceneLoaded += OnSceneLoaded;
            SpawnFor(SceneManager.GetActiveScene());   // the first scene: its sceneLoaded already fired
        }

        static void OnSceneLoaded(Scene scene, LoadSceneMode mode) => SpawnFor(scene);

        /// <summary>Spawn the UI prefabs that belong to <paramref name="scene"/>: gameplay UI for a scene
        /// that runs <see cref="RideburyGame"/>, the menu UI for the main-menu scene. Anything else is left alone.</summary>
        public static void SpawnFor(Scene scene)
        {
            if (!scene.IsValid() || !scene.isLoaded) return;
            if (Has<RideburyGame>(scene)) EnsureGameplay();
            else if (scene.name == MenuSceneName || Has<MenuManager>(scene)) EnsureMenu();
        }

        /// <summary>HUD + pop-ups + garage + tutorial banner. Idempotent — safe to call again at any time.</summary>
        public static void EnsureGameplay()
        {
            Ensure<InGameHud>(Hud, HudRoot);
            Ensure<InGamePanels>(Panels, PanelsRoot);
            Ensure<InGameGarage>(Garage, GarageRoot);
            Ensure<TutorialPanelMarker>(Tutorial, TutorialRoot);
        }

        /// <summary>The main menu (MenuController and every panel it drives). Idempotent.</summary>
        public static MenuController EnsureMenu() => Ensure<MenuController>(Menu, MenuRoot);

        /// <summary>The scene's <typeparamref name="T"/> UI: an existing one if the scene already has it
        /// (a checked-out copy), otherwise spawned from <paramref name="resourcePath"/>. Null only when the
        /// prefab is missing — bake it with "Tools ▸ 300Mind UI ▸ UI Prefabs ▸ Bake ALL UI into prefabs".</summary>
        public static T Ensure<T>(string resourcePath, string rootName) where T : Component
        {
            var existing = Object.FindFirstObjectByType<T>(FindObjectsInactive.Include);
            if (existing != null) return existing;

            var prefab = Resources.Load<GameObject>(resourcePath);
            if (prefab == null)
            {
                Debug.LogWarning("[UIPrefabs] Missing Resources/" + resourcePath +
                                 " — run 'Tools ▸ 300Mind UI ▸ UI Prefabs ▸ Bake ALL UI into prefabs'.");
                return null;
            }

            var go = Object.Instantiate(prefab);
            go.name = rootName;                       // no "(Clone)": the editor bakers find it by this name
            return go.GetComponent<T>() ?? go.GetComponentInChildren<T>(true);
        }

        // Does this scene carry a T anywhere (inactive included)? Cheap: roots only, one pass.
        static bool Has<T>(Scene scene) where T : Component
        {
            foreach (var go in scene.GetRootGameObjects())
                if (go != null && go.GetComponentInChildren<T>(true) != null) return true;
            return false;
        }
    }
}
