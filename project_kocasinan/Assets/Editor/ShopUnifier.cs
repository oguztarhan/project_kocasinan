using UnityEngine;
using UnityEngine.EventSystems;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine.SceneManagement;
using Ridebury;

/// <summary>
/// ONE-SHOT migration from the old two-shop setup (a shop baked into the main-menu scene +
/// a second one baked into the gameplay scene) to THE shop: a single prefab,
/// <c>Assets/Resources/UI/ShopPanel.prefab</c>, spawned at runtime by both scenes.
///
/// It lifts the in-game shop — the look that was kept — out of the gameplay scene into the
/// prefab, then removes BOTH baked shops from BOTH scenes so only the prefab is left.
/// Safe to re-run: if the prefab already exists it just cleans the scenes.
///
/// Menu:  Tools ▸ 300Mind UI ▸ Unify Shop (migrate the two baked shops)
/// </summary>
public static class ShopUnifier
{
    public const string PrefabName = "ShopPanel";
    public const string PrefabFolder = "Assets/Resources/UI";
    public const string PrefabPath = PrefabFolder + "/" + PrefabName + ".prefab";

    const string GameScenePath = "Assets/Scenes/SampleScene.unity";
    const string MenuScenePath = "Assets/Scenes/MainMenu.unity";

    /// <summary>Write <paramref name="go"/> to the shop prefab path, creating the folder if needed.</summary>
    public static void SavePrefab(GameObject go)
    {
        if (!AssetDatabase.IsValidFolder("Assets/Resources")) AssetDatabase.CreateFolder("Assets", "Resources");
        if (!AssetDatabase.IsValidFolder(PrefabFolder)) AssetDatabase.CreateFolder("Assets/Resources", "UI");
        PrefabUtility.SaveAsPrefabAsset(go, PrefabPath);
        AssetDatabase.SaveAssets();
    }

    [MenuItem("Tools/300Mind UI/Unify Shop (migrate the two baked shops)")]
    static void Unify()
    {
        if (!EditorUtility.DisplayDialog("Unify the shop?",
            "Moves the in-game shop into " + PrefabPath + " and DELETES both baked shops from " +
            "MainMenu.unity and SampleScene.unity.\n\nBoth scenes are saved. Commit or stash first if you want a way back.",
            "Unify", "Cancel")) return;

        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
        UnifyNow();
    }

    /// <summary>The migration itself, with no dialogs — callable from a script/CI. Unsaved
    /// scene changes are LOST, so the menu item above offers to save them first.</summary>
    public static void UnifyNow()
    {
        string reopen = SceneManager.GetActiveScene().path;

        // ---- 1) Gameplay scene: lift its shop into the prefab, then remove it ----
        var game = EditorSceneManager.OpenScene(GameScenePath, OpenSceneMode.Single);
        var source = FindShopRoot(game);
        if (source == null && !System.IO.File.Exists(PrefabPath))
        {
            Debug.LogError("[ShopUnifier] No baked shop found in " + GameScenePath + " and no prefab at " + PrefabPath +
                           ". Run 'Tools ▸ 300Mind UI ▸ Bake Shop Prefab' first.");
            return;
        }
        if (source != null)
        {
            if (!System.IO.File.Exists(PrefabPath)) SavePrefab(ToPrefabRoot(source));
            else Debug.Log("[ShopUnifier] " + PrefabPath + " already exists — keeping it and only cleaning the scenes.");
            Object.DestroyImmediate(source);
        }
        RemoveLeftovers(game);
        EditorSceneManager.MarkSceneDirty(game);
        EditorSceneManager.SaveScene(game);

        // ---- 2) Menu scene: drop its own shop panel + any stray shop canvas ----
        var menu = EditorSceneManager.OpenScene(MenuScenePath, OpenSceneMode.Single);
        RemoveLeftovers(menu);
        foreach (var ctrl in Object.FindObjectsByType<MenuController>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            ctrl.shopPanel = null;                   // assigned at runtime from the prefab now
            EditorUtility.SetDirty(ctrl);
        }
        EditorSceneManager.MarkSceneDirty(menu);
        EditorSceneManager.SaveScene(menu);

        if (!string.IsNullOrEmpty(reopen)) EditorSceneManager.OpenScene(reopen, OpenSceneMode.Single);
        Debug.Log("[ShopUnifier] Done. ONE shop now: " + PrefabPath + " — the menu and the game both spawn it at runtime.");
    }

    // The baked shop canvas in a scene: the InGameShop marker, else a root named "InGameShop_Baked".
    static GameObject FindShopRoot(Scene scene)
    {
        foreach (var go in scene.GetRootGameObjects())
        {
            var marker = go.GetComponentInChildren<InGameShop>(true);
            if (marker != null) return marker.gameObject;
        }
        foreach (var go in scene.GetRootGameObjects())
            if (go.name == "InGameShop_Baked") return go;
        return null;
    }

    // Turn the baked canvas into the prefab root: ShopUI instead of the old InGameShop marker,
    // no EventSystem riding along (each scene has its own), and on top of every other canvas.
    static GameObject ToPrefabRoot(GameObject go)
    {
        GameObject panel = null;
        var old = go.GetComponent<InGameShop>();
        if (old != null)
        {
            panel = old.panel;
            Object.DestroyImmediate(old);
        }
        if (panel == null)
        {
            var t = go.transform.Find("Panel_GameShop") ?? go.transform.Find("Panel_Shop");
            if (t != null) panel = t.gameObject;
        }

        foreach (var es in go.GetComponentsInChildren<EventSystem>(true))
            if (es.gameObject != go) Object.DestroyImmediate(es.gameObject);

        var canvas = go.GetComponent<Canvas>();
        if (canvas != null) canvas.sortingOrder = 200;

        var shop = go.GetComponent<ShopUI>();
        if (shop == null) shop = go.AddComponent<ShopUI>();
        shop.panel = panel;
        if (panel != null) panel.SetActive(false);

        go.name = PrefabName;
        return go;
    }

    // Delete anything left of the old two-shop setup in this scene.
    static void RemoveLeftovers(Scene scene)
    {
        foreach (var go in scene.GetRootGameObjects())
        {
            if (go == null) continue;
            foreach (var marker in go.GetComponentsInChildren<InGameShop>(true))
                if (marker != null) Object.DestroyImmediate(marker.gameObject);
            if (go == null) continue;   // the marker WAS this root
            if (go.name == "InGameShop_Baked") { Object.DestroyImmediate(go); continue; }
            foreach (var t in go.GetComponentsInChildren<Transform>(true))
                if (t != null && t.name == "Panel_Shop") { Object.DestroyImmediate(t.gameObject); break; }
        }
    }
}
