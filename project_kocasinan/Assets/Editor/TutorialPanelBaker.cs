using UnityEngine;
using UnityEngine.UI;
using UnityEditor;
using UnityEditor.SceneManagement;
using BusJam;

/// <summary>
/// Bakes the TUTORIAL banner — its BACKGROUND PANEL and its TEXT — into the open scene as real, editable
/// GameObjects, so you can adjust both in the Inspector (sprite / colour / size / position / font size /
/// alignment). At play time <see cref="TutorialCoach"/> adopts the panel via the <see cref="TutorialPanelMarker"/>
/// tag, REUSES your text child (it only fills in the wording for each tutorial step — your styling is kept),
/// and shows/hides the whole thing during the level 1 / 5 / 10 coaching. It self-hides at play start, so it
/// only appears during a tutorial.
///
/// Idempotent: re-running PRESERVES whatever already exists and only adds what's missing, so you never lose
/// your styling. To start clean, delete "TutorialPanel_Baked" in the Hierarchy and run it again.
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

        // --- Host canvas (find or create). Its scaler MUST match TutorialCoach's so the layout is kept when
        //     the coach reparents the panel onto its own overlay at runtime. ---
        GameObject rootGo = null;
        foreach (var go in scene.GetRootGameObjects())
            if (go.name == "TutorialPanel_Baked") { rootGo = go; break; }
        if (rootGo == null)
        {
            rootGo = new GameObject("TutorialPanel_Baked");
            var canvas = rootGo.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 60; // edit-time preview only; the coach moves the panel to its own overlay at play
            var scaler = rootGo.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1080, 1920);
            scaler.matchWidthOrHeight = 0f;   // match WIDTH (portrait): fits the screen width on any aspect
            // No GraphicRaycaster on purpose — the coach's overlay has none, so taps pass through to the board.
            Undo.RegisterCreatedObjectUndo(rootGo, "Bake Tutorial Panel");
        }

        // --- Background panel (find or create). Tagged so the coach finds + adopts it. ---
        Transform bannerT = rootGo.transform.Find("TutorialBanner");
        GameObject banner;
        if (bannerT == null)
        {
            banner = new GameObject("TutorialBanner", typeof(RectTransform), typeof(Image), typeof(TutorialPanelMarker));
            banner.transform.SetParent(rootGo.transform, false);
            var rt = (RectTransform)banner.transform;
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0f);
            rt.pivot = new Vector2(0.5f, 0f);
            rt.anchoredPosition = new Vector2(0, 380); // same spot as the built-in banner (above the jokers, below the jam)
            rt.sizeDelta = new Vector2(1000, 170);
            var img = banner.GetComponent<Image>();
            img.color = new Color(0.06f, 0.08f, 0.13f, 0.86f); // placeholder look — drop in your own sprite/colour
            img.raycastTarget = false;
            Undo.RegisterCreatedObjectUndo(banner, "Bake Tutorial Panel");
        }
        else
        {
            banner = bannerT.gameObject;
            if (banner.GetComponent<TutorialPanelMarker>() == null) banner.AddComponent<TutorialPanelMarker>(); // keep the tag
        }

        // --- Tutorial TEXT child (add ONLY if missing, so an existing styled text is never overwritten). The
        //     coach reuses this Text and just sets the wording per step; everything below is yours to tweak. ---
        var txt = banner.GetComponentInChildren<Text>(true);
        if (txt == null)
        {
            var txtGo = new GameObject("TutorialText", typeof(RectTransform));
            txtGo.transform.SetParent(banner.transform, false);
            var trt = (RectTransform)txtGo.transform;
            trt.anchorMin = Vector2.zero; trt.anchorMax = Vector2.one;                        // fill the panel...
            trt.offsetMin = new Vector2(34, 18); trt.offsetMax = new Vector2(-34, -18);       // ...with a little padding
            txt = txtGo.AddComponent<Text>();
            txt.font = GameFont.UGUI;                       // Matcha Cih (GlobalFontApplier keeps every text on it)
            txt.text = "Tutorial text goes here";           // placeholder — the coach replaces it with each step's wording
            txt.fontSize = 46;
            txt.color = Color.white;
            txt.alignment = TextAnchor.MiddleCenter;
            txt.horizontalOverflow = HorizontalWrapMode.Wrap;
            txt.verticalOverflow = VerticalWrapMode.Overflow;
            txt.raycastTarget = false;
            var sh = txtGo.AddComponent<Shadow>();
            sh.effectColor = new Color(0, 0, 0, 0.5f);
            sh.effectDistance = new Vector2(2, -2);
            Undo.RegisterCreatedObjectUndo(txtGo, "Bake Tutorial Panel");
        }

        EditorSceneManager.MarkSceneDirty(scene);
        Selection.activeGameObject = txt.gameObject;
        Debug.Log("[TutorialPanelBaker] Tutorial banner ready under 'TutorialPanel_Baked': 'TutorialBanner' (background) " +
                  "+ 'TutorialText' (text). Adjust both in the Inspector, then SAVE the scene (Ctrl+S). Re-running keeps " +
                  "your styling. At play, the coach shows them during the tutorial and fills in the wording per step.");
    }
}
