using UnityEngine;
using UnityEngine.UI;
using UnityEditor;
using UnityEditor.SceneManagement;
using BusJam;

/// <summary>
/// Bakes the TUTORIAL BACKGROUND PANEL (the strip that sits behind the tutorial instruction text) into the
/// open scene as a real, editable GameObject. Style its Image — sprite / colour / size / position — in the
/// Inspector. At play time <see cref="TutorialCoach"/> adopts it via the <see cref="TutorialPanelMarker"/>
/// tag and draws the tutorial text on top, showing/hiding it automatically during the level 1 / 5 / 10
/// coaching. The panel self-hides at play start, so it only appears during a tutorial regardless of how you
/// leave it. Re-running clears and re-creates it.
///
/// Open SampleScene (the gameplay scene) before running. Menu:
///   Tools ▸ 300Mind UI ▸ Bake Tutorial Panel (into open scene)
/// </summary>
public static class TutorialPanelBaker
{
    [MenuItem("Tools/300Mind UI/Bake Tutorial Panel (into open scene)")]
    static void Bake()
    {
        var scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
        foreach (var existing in scene.GetRootGameObjects())
            if (existing.name == "TutorialPanel_Baked") Object.DestroyImmediate(existing);

        // Host canvas — its scaler MUST match TutorialCoach's (1080x1920, match 0.5) so the panel keeps its
        // layout when the coach reparents it onto its own overlay at runtime.
        var rootGo = new GameObject("TutorialPanel_Baked");
        var canvas = rootGo.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 60; // edit-time preview only; the coach moves the panel to its own overlay at play
        var scaler = rootGo.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1080, 1920);
        scaler.matchWidthOrHeight = 0.5f;
        // No GraphicRaycaster on purpose — the coach's overlay has none, so taps pass through to the board.

        // The panel you style. Tagged so the coach finds + adopts it.
        var go = new GameObject("TutorialBanner", typeof(RectTransform), typeof(Image), typeof(TutorialPanelMarker));
        go.transform.SetParent(rootGo.transform, false);
        var rt = (RectTransform)go.transform;
        rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0f);
        rt.pivot = new Vector2(0.5f, 0f);
        rt.anchoredPosition = new Vector2(0, 380); // same spot as the built-in banner (above the jokers, below the jam)
        rt.sizeDelta = new Vector2(1000, 170);
        var img = go.GetComponent<Image>();
        img.color = new Color(0.06f, 0.08f, 0.13f, 0.86f); // placeholder look — drop in your own sprite/colour
        img.raycastTarget = false;

        Undo.RegisterCreatedObjectUndo(rootGo, "Bake Tutorial Panel");
        EditorSceneManager.MarkSceneDirty(scene);
        Selection.activeGameObject = go;
        Debug.Log("[TutorialPanelBaker] Created 'TutorialBanner' (under TutorialPanel_Baked). Style its Image in the " +
                  "Inspector, then SAVE the scene (Ctrl+S). It self-hides at play; the tutorial shows it automatically.");
    }
}
