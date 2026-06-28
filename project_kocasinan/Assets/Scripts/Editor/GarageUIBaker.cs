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

        // "BusJam ▸ Bake Garage Cards" — adds 3 DRAGGABLE slot boxes on the garage panel (chest area, chest-open popup,
        // shard counter). Position/resize them in the Hierarchy; at runtime the cards fill them. Run AFTER "Bake Garage
        // Panels". Re-running only creates MISSING slots (keeps ones you already positioned). SAVE the scene afterwards.
        [MenuItem("BusJam/Bake Garage Cards")]
        static void BakeCards()
        {
            var marker = Object.FindFirstObjectByType<InGameGarage>(FindObjectsInactive.Include);
            if (marker == null || marker.garageRoot == null)
            {
                EditorUtility.DisplayDialog("Bake Garage Cards", "Run 'BusJam ▸ Bake Garage Panels' first (no InGameGarage / garage panel found).", "OK");
                return;
            }
            var parent = marker.garageRoot.transform; // slots live on the garage panel so they show/hide with it

            // NOTE: no chest slot — a free-positioned chest box overlaps the scroll list. Chests stay in the scroll
            // (tune size/spacing/columns on InGameGarage). Only the popup + shard counter get draggable slots.
            if (marker.revealCard == null)
                marker.revealCard = MakeSlot(parent, "Slot_RevealPopup", new Vector2(0, 120), new Vector2(820, 980), new Color(1f, 0.82f, 0.30f, 0.10f));
            if (marker.shardSlot == null)
                marker.shardSlot = MakeSlot(parent, "Slot_ShardCounter", new Vector2(175, 690), new Vector2(300, 88), new Color(0.42f, 0.92f, 1f, 0.16f));

            EditorUtility.SetDirty(marker);
            EditorSceneManager.MarkSceneDirty(marker.gameObject.scene);
            Selection.activeGameObject = marker.revealCard.gameObject;
            EditorGUIUtility.PingObject(marker.revealCard.gameObject);
            Debug.Log("[GarageUIBaker] Baked draggable slots: Slot_RevealPopup (chest-open popup) + Slot_ShardCounter on the garage panel. " +
                      "Tick the garage panel active in the Hierarchy, drag/resize them, untick, then SAVE the scene (Ctrl+S). " +
                      "Chests stay in the scroll list (tune size/spacing/columns on InGameGarage). Press Play to check.");
        }

        static RectTransform MakeSlot(Transform parent, string name, Vector2 pos, Vector2 size, Color tint)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image));
            Undo.RegisterCreatedObjectUndo(go, "Bake Garage Cards");
            go.transform.SetParent(parent, false);
            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = pos; rt.sizeDelta = size;
            var img = go.GetComponent<Image>(); img.color = tint; img.raycastTarget = false; // faint editor guide; hidden at runtime
            return rt;
        }
    }
}
