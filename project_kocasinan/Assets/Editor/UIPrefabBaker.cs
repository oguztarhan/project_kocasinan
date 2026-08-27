using System;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEditor;
using UnityEditor.SceneManagement;
using Ridebury;

/// <summary>
/// Turns every authored UI screen into ONE editable prefab under <c>Assets/Resources/UI/</c> and keeps
/// it that way. The scenes hold no UI any more: <see cref="UIPrefabs"/> spawns the right prefabs when a
/// scene loads, so editing a prefab changes the game everywhere it appears.
///
/// Menu — Tools ▸ 300Mind UI ▸ UI Prefabs:
/// <list type="bullet">
/// <item><b>Bake ALL UI into prefabs</b> — the one-shot migration: lifts the baked UI out of MainMenu.unity
/// and SampleScene.unity into the prefabs and deletes it from both scenes. Safe to re-run (an existing
/// prefab is kept; only the scene copy is removed).</item>
/// <item><b>Open ▸ …</b> — opens one UI prefab in Prefab Mode. This is where you edit the game's UI now.</item>
/// <item><b>Check Out …</b> — drops the prefabs for the open scene back into the Hierarchy, for when you'd
/// rather edit them in the scene (or want to run one of the old bakers by hand).</item>
/// <item><b>Check In …</b> — saves those scene copies back into the prefabs and removes them again.</item>
/// </list>
///
/// The old bakers ("Bake In-Game HUD", "Bake Main Menu", "Bake Garage Panels", …) all run through
/// <see cref="Edit"/>, so they check out, rebuild and check in on their own — you never end up with a
/// second copy of a screen living in a scene.
/// </summary>
public static class UIPrefabBaker
{
    public const string Folder = "Assets/Resources/UI";

    const string GameScenePath = "Assets/Scenes/SampleScene.unity";
    const string MenuScenePath = "Assets/Scenes/MainMenu.unity";

    /// <summary>One UI screen: where it lives as a prefab, and what it is called while it sits in a scene.</summary>
    public class Entry
    {
        public string rootName;     // name the object takes in the Hierarchy (what the old bakers look for)
        public string prefabName;   // asset name under Assets/Resources/UI
        public string scenePath;    // the scene this screen belongs to
        public Type marker;         // component that identifies it even after a rename

        public string Path => Folder + "/" + prefabName + ".prefab";
        public string Resource => "UI/" + prefabName;
    }

    public static readonly Entry Hud = new Entry
    { rootName = UIPrefabs.HudRoot, prefabName = "HudPanel", scenePath = GameScenePath, marker = typeof(InGameHud) };
    public static readonly Entry Panels = new Entry
    { rootName = UIPrefabs.PanelsRoot, prefabName = "GamePanels", scenePath = GameScenePath, marker = typeof(InGamePanels) };
    public static readonly Entry Garage = new Entry
    { rootName = UIPrefabs.GarageRoot, prefabName = "GaragePanel", scenePath = GameScenePath, marker = typeof(InGameGarage) };
    public static readonly Entry Tutorial = new Entry
    { rootName = UIPrefabs.TutorialRoot, prefabName = "TutorialPanel", scenePath = GameScenePath, marker = typeof(TutorialPanelMarker) };
    public static readonly Entry Menu = new Entry
    { rootName = UIPrefabs.MenuRoot, prefabName = "MenuUI", scenePath = MenuScenePath, marker = typeof(MenuController) };

    /// <summary>Every screen this tool owns. (The shop is already a prefab — see ShopUI / GameShopBaker.)</summary>
    public static readonly Entry[] All = { Hud, Panels, Garage, Tutorial, Menu };

    // ================= Migration =================

    [MenuItem("Tools/300Mind UI/UI Prefabs/Bake ALL UI into prefabs (scenes to Resources-UI)")]
    static void BakeAll()
    {
        if (!EditorUtility.DisplayDialog("Move all UI into prefabs?",
            "Saves the baked UI of MainMenu.unity and SampleScene.unity into " + Folder +
            " and DELETES it from both scenes. Both scenes are saved.\n\n" +
            "A prefab that already exists is kept as-is (only the scene copy goes), so this is safe to re-run.\n\n" +
            "Commit or stash first if you want a way back.",
            "Bake", "Cancel")) return;

        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
        BakeAllNow();
    }

    /// <summary>The migration itself, without dialogs — callable from a script or CI.
    /// Unsaved scene changes are LOST, so the menu item above offers to save them first.</summary>
    public static void BakeAllNow()
    {
        string reopen = SceneManager.GetActiveScene().path;
        int saved = 0, removed = 0, missing = 0;

        foreach (var scenePath in new[] { GameScenePath, MenuScenePath })
        {
            var scene = EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Single);
            bool touched = false;

            foreach (var e in All)
            {
                if (e.scenePath != scenePath) continue;
                var go = FindRoot(scene, e);
                if (go == null)
                {
                    if (AssetDatabase.LoadAssetAtPath<GameObject>(e.Path) == null)
                    {
                        missing++;
                        Debug.LogWarning("[UIPrefabBaker] " + e.rootName + " is in neither " + scenePath + " nor " + e.Path +
                                         " — bake it once with its own menu item, then re-run this.");
                    }
                    continue;
                }

                if (AssetDatabase.LoadAssetAtPath<GameObject>(e.Path) == null) { SaveToPrefab(go, e); saved++; }
                else Debug.Log("[UIPrefabBaker] " + e.Path + " already exists — keeping it and only removing the scene copy.");

                UnityEngine.Object.DestroyImmediate(go);
                removed++;
                touched = true;
            }

            if (touched)
            {
                EditorSceneManager.MarkSceneDirty(scene);
                EditorSceneManager.SaveScene(scene);
            }
        }

        AssetDatabase.SaveAssets();
        if (!string.IsNullOrEmpty(reopen)) EditorSceneManager.OpenScene(reopen, OpenSceneMode.Single);
        Debug.Log("[UIPrefabBaker] Done — " + saved + " prefab(s) written, " + removed + " scene copy(ies) removed" +
                  (missing > 0 ? ", " + missing + " screen(s) not found" : "") +
                  ". All UI now lives in " + Folder + " and is spawned at runtime by UIPrefabs.");
    }

    // ================= Check out / check in =================

    /// <summary>Puts <paramref name="e"/> into the open scene as a normal (unpacked) object so it can be
    /// edited there — or hands back the copy that is already in the scene. Null when neither exists.</summary>
    public static GameObject CheckOut(Entry e)
    {
        var scene = SceneManager.GetActiveScene();
        var existing = FindRoot(scene, e);
        if (existing != null) return existing;

        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(e.Path);
        if (prefab == null) return null;

        var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab, scene);
        PrefabUtility.UnpackPrefabInstance(go, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
        go.name = e.rootName;
        Undo.RegisterCreatedObjectUndo(go, "Check out " + e.prefabName);
        EditorSceneManager.MarkSceneDirty(scene);
        return go;
    }

    /// <summary>Saves the open scene's copy of <paramref name="e"/> back into its prefab and removes it from
    /// the scene, so the prefab stays the only copy. False when the scene has no such object.</summary>
    public static bool CheckIn(Entry e)
    {
        var scene = SceneManager.GetActiveScene();
        var go = FindRoot(scene, e);
        if (go == null) return false;

        SaveToPrefab(go, e);
        UnityEngine.Object.DestroyImmediate(go);
        EditorSceneManager.MarkSceneDirty(scene);
        AssetDatabase.SaveAssets();
        return true;
    }

    /// <summary>Runs an old scene-baker against <paramref name="e"/>: checks the prefab out into the scene,
    /// lets <paramref name="bake"/> rebuild/edit it there, then saves it back and clears the scene again.
    /// Aborts (with an offer to switch scenes) when the wrong scene is open.</summary>
    public static void Edit(Entry e, Action bake)
    {
        if (!EnsureScene(e)) return;

        CheckOut(e);                       // no-op when the baker creates the object from scratch
        try { bake(); }
        finally
        {
            if (CheckIn(e))
                Debug.Log("[UIPrefabBaker] Saved " + e.Path + ". Edit it with 'Tools ▸ 300Mind UI ▸ UI Prefabs ▸ Open' " +
                          "(or double-click the asset) — the scene needs no saving.");
            else
                Debug.LogWarning("[UIPrefabBaker] Nothing named " + e.rootName + " in the scene after the bake — " +
                                 e.Path + " was left untouched.");
        }
    }

    /// <summary>Same as <see cref="Edit"/> for a tool that sweeps the WHOLE open scene (a font fixer, say):
    /// checks out every screen belonging to the open scene, runs <paramref name="work"/>, checks them all back in.</summary>
    public static void EditScene(Action work)
    {
        string scenePath = SceneManager.GetActiveScene().path;
        foreach (var e in All) if (e.scenePath == scenePath) CheckOut(e);
        try { work(); }
        finally { foreach (var e in All) if (e.scenePath == scenePath) CheckIn(e); }
    }

    [MenuItem("Tools/300Mind UI/UI Prefabs/Check Out all UI of the open scene")]
    static void CheckOutAll()
    {
        string scenePath = SceneManager.GetActiveScene().path;
        int n = 0;
        foreach (var e in All) if (e.scenePath == scenePath && CheckOut(e) != null) n++;
        Debug.Log(n == 0
            ? "[UIPrefabBaker] Nothing to check out for this scene (open MainMenu or SampleScene)."
            : "[UIPrefabBaker] Checked out " + n + " UI prefab(s) into the scene. Edit them, then run " +
              "'Check In' — do NOT save the scene with them still in it.");
    }

    [MenuItem("Tools/300Mind UI/UI Prefabs/Check In (save scene UI back into the prefabs)")]
    static void CheckInAll()
    {
        string scenePath = SceneManager.GetActiveScene().path;
        int n = 0;
        foreach (var e in All) if (e.scenePath == scenePath && CheckIn(e)) n++;
        if (n > 0) EditorSceneManager.SaveScene(SceneManager.GetActiveScene());
        Debug.Log(n == 0
            ? "[UIPrefabBaker] No checked-out UI found in this scene."
            : "[UIPrefabBaker] Saved " + n + " screen(s) back into " + Folder + " and cleaned the scene.");
    }

    // ---- Open one prefab in Prefab Mode (this is where the UI is edited now) ----
    [MenuItem("Tools/300Mind UI/UI Prefabs/Open/In-Game HUD")]        static void OpenHud() => Open(Hud);
    [MenuItem("Tools/300Mind UI/UI Prefabs/Open/In-Game Panels")]     static void OpenPanels() => Open(Panels);
    [MenuItem("Tools/300Mind UI/UI Prefabs/Open/Garage")]             static void OpenGarage() => Open(Garage);
    [MenuItem("Tools/300Mind UI/UI Prefabs/Open/Tutorial Banner")]    static void OpenTutorial() => Open(Tutorial);
    [MenuItem("Tools/300Mind UI/UI Prefabs/Open/Main Menu")]          static void OpenMenu() => Open(Menu);
    [MenuItem("Tools/300Mind UI/UI Prefabs/Open/Shop")]               static void OpenShop() => OpenPath(ShopUnifier.PrefabPath);

    static void Open(Entry e) => OpenPath(e.Path);

    static void OpenPath(string path)
    {
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
        if (prefab == null)
        {
            EditorUtility.DisplayDialog("UI Prefabs", path + " does not exist yet.\n\nRun 'Bake ALL UI into prefabs' first.", "OK");
            return;
        }
        AssetDatabase.OpenAsset(prefab);
        EditorGUIUtility.PingObject(prefab);
    }

    // ================= Plumbing =================

    // The screen's object in this scene: by name first (what the bakers create), then by its marker
    // component, so a renamed root is still found.
    static GameObject FindRoot(Scene scene, Entry e)
    {
        foreach (var go in scene.GetRootGameObjects())
            if (go != null && go.name == e.rootName) return go;

        if (e.marker != null)
            foreach (var go in scene.GetRootGameObjects())
                if (go != null && go.GetComponentInChildren(e.marker, true) != null) return go;

        return null;
    }

    // Write the object to its prefab path (creating Resources/UI on the way). Any EventSystem riding
    // along is dropped first — every scene has its own, and a second one breaks input.
    static void SaveToPrefab(GameObject go, Entry e)
    {
        EnsureFolder();
        foreach (var es in go.GetComponentsInChildren<EventSystem>(true))
            if (es != null && es.gameObject != go) UnityEngine.Object.DestroyImmediate(es.gameObject);

        go.name = e.rootName;
        PrefabUtility.SaveAsPrefabAsset(go, e.Path);
        AssetDatabase.SaveAssets();
    }

    static void EnsureFolder()
    {
        if (!AssetDatabase.IsValidFolder("Assets/Resources")) AssetDatabase.CreateFolder("Assets", "Resources");
        if (!AssetDatabase.IsValidFolder(Folder)) AssetDatabase.CreateFolder("Assets/Resources", "UI");
    }

    // Makes sure the scene this screen belongs to is the open one; offers to switch when it isn't.
    static bool EnsureScene(Entry e)
    {
        var scene = SceneManager.GetActiveScene();
        if (scene.path == e.scenePath) return true;
        if (FindRoot(scene, e) != null) return true;   // it's here anyway (an old bake) — work on it in place

        if (!EditorUtility.DisplayDialog("Wrong scene open",
            e.prefabName + " belongs to " + System.IO.Path.GetFileName(e.scenePath) + ", but " +
            (string.IsNullOrEmpty(scene.path) ? "an unsaved scene" : System.IO.Path.GetFileName(scene.path)) + " is open.\n\nOpen it now?",
            "Open " + System.IO.Path.GetFileNameWithoutExtension(e.scenePath), "Cancel")) return false;

        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return false;
        EditorSceneManager.OpenScene(e.scenePath, OpenSceneMode.Single);
        return true;
    }
}
