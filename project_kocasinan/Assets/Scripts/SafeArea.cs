using UnityEngine;

namespace Ridebury
{
    /// <summary>
    /// Keeps edge-anchored UI chrome out of the iPhone sensor housing (notch / Dynamic Island), the rounded screen
    /// corners and the home-indicator strip. Attached at RUNTIME by <see cref="ApplyToChrome"/> — no prefab or scene
    /// edits, so it is reversible by deleting this file plus its three call sites (GameUI.SetupHud,
    /// GameUI.SetupSettings, MenuController.Start).
    ///
    /// WHY THIS IS NEEDED. Every canvas here is ScaleWithScreenSize / reference 1080x1920 / match WIDTH, so one
    /// reference unit is screenWidthPt/1080 points and the canvas simply gets taller or shorter with the aspect
    /// ratio. Unity renders edge-to-edge on iOS, so anything anchored to the top or bottom edge sits at the PHYSICAL
    /// edge. On a 402pt-wide iPhone 16 Pro one unit is 0.372pt, so the ~62pt sensor housing is ~167 units deep — and
    /// the shipped chrome sat inside it: the in-game gear button spans 42..138 units below the top edge and the level
    /// badge 25..195, i.e. its top edge is ~9pt from the glass, inside the corner radius.
    ///
    /// WHAT IT DOES. Shifts the element inward by the safe-area inset on whichever edges it is anchored to. It never
    /// resizes anything and never touches a background: <see cref="ApplyToChrome"/> only picks POINT-anchored children
    /// (anchorMin == anchorMax) that touch an edge, which by construction excludes every full-stretch background and
    /// full-screen dim panel, and centre-anchored cards have no edge to be pushed off.
    ///
    /// In portrait — the only orientation this app allows — the left/right insets are zero on every iPhone and iPad,
    /// so in practice this is a vertical nudge. The horizontal terms are kept for correctness, not because they fire.
    ///
    /// In the Editor Screen.safeArea is the whole screen, so every offset is zero and this is a no-op. It only ever
    /// does anything on a real device.
    /// </summary>
    [DisallowMultipleComponent]
    public class SafeArea : MonoBehaviour
    {
        /// <summary>
        /// Add a <see cref="SafeArea"/> to every DIRECT child of <paramref name="root"/> that is edge-anchored chrome.
        /// Idempotent — re-running it (SetupSettings can run more than once) adds nothing a second time.
        ///
        /// Call this AFTER any code that positions children relative to each other. Each component captures its own
        /// baseline in Awake, so a child positioned from an ALREADY-SHIFTED sibling would bake the inset in twice.
        /// </summary>
        public static void ApplyToChrome(Transform root)
        {
            if (root == null) return;
            for (int i = 0; i < root.childCount; i++)
            {
                if (!(root.GetChild(i) is RectTransform rt)) continue;
                // Stretched in either axis => a background, a full-screen dim panel or a column. Insetting those by
                // moving anchoredPosition is meaningless (it would slide the fill, not shrink it), so leave them be.
                if (rt.anchorMin != rt.anchorMax) continue;
                bool touchesEdge = rt.anchorMin.x == 0f || rt.anchorMin.x == 1f
                                || rt.anchorMin.y == 0f || rt.anchorMin.y == 1f;
                if (!touchesEdge) continue;                       // centre-anchored: nothing can push it off-screen
                if (rt.GetComponent<SafeArea>() == null) rt.gameObject.AddComponent<SafeArea>();
            }
        }

        RectTransform rt;
        Canvas canvas;
        Vector2 baseline;      // the AUTHORED position; every Apply recomputes from this, so it can never compound
        bool ready;

        // What the last Apply was computed against — re-applied when any of it moves (rotation, split view on iPad,
        // and the first frames after boot, where CanvasScaler has not yet published its scaleFactor).
        Rect lastSafe;
        int lastW, lastH;
        float lastScale;

        void Awake()
        {
            rt = (RectTransform)transform;
            canvas = GetComponentInParent<Canvas>();
            baseline = rt.anchoredPosition;
            ready = true;
            Apply();
        }

        void OnEnable() { if (ready) Apply(); }

        void Update()
        {
            if (!ready) return;
            float s = canvas != null ? canvas.scaleFactor : 1f;
            if (Screen.width != lastW || Screen.height != lastH || Screen.safeArea != lastSafe || s != lastScale)
                Apply();
        }

        void Apply()
        {
            if (rt == null) return;
            if (canvas == null) canvas = GetComponentInParent<Canvas>();
            if (canvas == null) return;

            // Screen.safeArea is in PIXELS and so is Screen.width/height; scaleFactor converts pixels -> canvas units.
            float s = canvas.scaleFactor;
            if (s <= 0f) return;                 // scaler has not run yet — Update will come back once it has

            Rect sa = Screen.safeArea;
            lastSafe = sa; lastW = Screen.width; lastH = Screen.height; lastScale = s;

            float top    = (Screen.height - sa.yMax) / s;
            float bottom = sa.yMin / s;
            float left   = sa.xMin / s;
            float right  = (Screen.width - sa.xMax) / s;

            Vector2 p = baseline;
            if (rt.anchorMax.y == 1f) p.y -= top;      // pinned to the top edge -> push DOWN, clear of the housing
            if (rt.anchorMin.y == 0f) p.y += bottom;   // pinned to the bottom  -> push UP, clear of the indicator
            if (rt.anchorMin.x == 0f) p.x += left;
            if (rt.anchorMax.x == 1f) p.x -= right;
            rt.anchoredPosition = p;
        }
    }
}
