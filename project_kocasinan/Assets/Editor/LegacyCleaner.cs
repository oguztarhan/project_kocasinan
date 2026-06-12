using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;

/// <summary>
/// Editor tool that removes the LEGACY game-over UI from the open scene: the old
/// scene-wired Continue / Failed panels (referenced by the now-unused GameManager) and
/// the GameManager object itself. The Continue / Failed / Success flow is owned by GameUI
/// (code or baked panels), so these scene objects are dead remnants.
///
/// Menu:  Tools ▸ 300Mind UI ▸ Clean Legacy Game-Over UI
/// </summary>
public static class LegacyCleaner
{
    [MenuItem("Tools/300Mind UI/Clean Legacy Game-Over UI")]
    static void Clean()
    {
        int removed = 0;
        var gms = Object.FindObjectsByType<GameManager>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        foreach (var gm in gms)
        {
            var so = new SerializedObject(gm);
            foreach (var prop in new[] { "continuePanel", "failedPanel" })
            {
                var p = so.FindProperty(prop);
                if (p != null && p.objectReferenceValue is GameObject go && go != null)
                {
                    Debug.Log($"[LegacyCleaner] Deleting old panel: {go.name}");
                    Object.DestroyImmediate(go);
                    removed++;
                }
            }
            Debug.Log($"[LegacyCleaner] Deleting legacy GameManager object: {gm.gameObject.name}");
            Object.DestroyImmediate(gm.gameObject);
            removed++;
        }

        if (removed == 0) { Debug.Log("[LegacyCleaner] No legacy game-over UI found in this scene."); return; }
        EditorSceneManager.MarkSceneDirty(UnityEngine.SceneManagement.SceneManager.GetActiveScene());
        Debug.Log($"[LegacyCleaner] Removed {removed} legacy object(s). SAVE the scene (Ctrl+S).");
    }
}
