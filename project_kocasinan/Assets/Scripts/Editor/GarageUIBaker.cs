using UnityEngine;
using UnityEngine.UI;
using UnityEditor;
using UnityEditor.SceneManagement;

namespace BusJam
{
    /// <summary>
    /// "BusJam ▸ Bake Garage Panels" — bakes the Garage + Vehicles (wardrobe) WINDOW CHROME into the open scene as
    /// real, Inspector-editable GameObjects (so you can reposition / restyle them in the Hierarchy WITHOUT entering
    /// Play mode), and tags them with an <see cref="InGameGarage"/> marker. At runtime <see cref="GameUI"/> adopts
    /// the baked panels instead of building them in code — same adopt-or-build pattern as the HUD / Settings bakes.
    ///
    /// Run it once. Re-running replaces the previous bake. The chest / vehicle cards INSIDE the scroll area are still
    /// generated at runtime, so the baked "Content" object stays empty by design. SAVE the scene (Ctrl+S) afterwards.
    /// </summary>
    public static class GarageUIBaker
    {
        [MenuItem("BusJam/Bake Garage Panels")]
        static void Bake()
        {
            // replace any previous bake so re-running is idempotent
            var old = Object.FindFirstObjectByType<InGameGarage>(FindObjectsInactive.Include);
            if (old != null) Undo.DestroyObjectImmediate(old.gameObject);

            // dedicated overlay canvas (matches GameUI's UICanvas: 1080x1920, match-width). sortingOrder 30 puts the
            // garage above the HUD (0); the wardrobe panel keeps its own overrideSorting 80 so it layers above the
            // garage, and the chest-reveal modal (sort 85) above that. Tweak this number if your HUD sits higher.
            var canvasGo = new GameObject("InGameGarageCanvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster), typeof(InGameGarage));
            Undo.RegisterCreatedObjectUndo(canvasGo, "Bake Garage Panels");
            var canvas = canvasGo.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 30;
            var sc = canvasGo.GetComponent<CanvasScaler>();
            sc.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            sc.referenceResolution = new Vector2(1080, 1920);
            sc.matchWidthOrHeight = 0f;
            var marker = canvasGo.GetComponent<InGameGarage>();

            // temp GameUI (Awake does NOT run in edit mode) drives the SAME chrome builders the runtime uses, so the
            // baked panels are byte-for-byte identical to the code-built ones. The panels parent under the canvas,
            // NOT under this temp object, so destroying it afterwards leaves the baked tree intact.
            var tmpGo = new GameObject("~GarageBakeTmp", typeof(GameUI));
            try
            {
                var ui = tmpGo.GetComponent<GameUI>();
                var gref = ui.EditorBakeGarage(canvasGo.transform);
                var vref = ui.EditorBakeVehicles(canvasGo.transform);

                marker.garageRoot    = gref.panel; marker.garageContent   = gref.content; marker.garageGold   = gref.gold; marker.garageClose   = gref.close;
                marker.vehiclesRoot  = vref.panel; marker.vehiclesContent = vref.content; marker.vehiclesGold = vref.gold; marker.vehiclesClose = vref.close;

                // baked INACTIVE so they don't cover the editor view; tick a panel active in the Hierarchy to edit it
                if (gref.panel) gref.panel.SetActive(false);
                if (vref.panel) vref.panel.SetActive(false);
            }
            finally
            {
                Object.DestroyImmediate(tmpGo);
            }

            EditorUtility.SetDirty(marker);
            EditorSceneManager.MarkSceneDirty(canvasGo.scene);
            Selection.activeGameObject = canvasGo;
            EditorGUIUtility.PingObject(canvasGo);
            Debug.Log("[GarageUIBaker] Baked 'InGameGarageCanvas' (Garage + Vehicles panels) into the scene. " +
                      "Organize them in the Hierarchy (tick a panel active to edit, untick when done), then SAVE the scene (Ctrl+S). " +
                      "GameUI adopts them automatically at runtime.");
        }
    }
}
