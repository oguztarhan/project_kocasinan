using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

namespace Ridebury.EditorTools
{
    /// <summary>
    /// The UI bench: everything needed to hand-edit the game's UI in the Inspector and trust that what you see is
    /// what ships.
    ///
    /// The screens live as prefabs in Assets/Resources/UI and are spawned at play time by <see cref="UIPrefabs"/>,
    /// so the Hierarchy is empty in the editor and there is nothing to click. "Edit UI in Scene" drops them into the
    /// open scene (and reveals the pop-ups, which are saved inactive); "Put UI Back" applies your edits to the
    /// prefabs and clears the scene again. A checked-out copy also WINS at play time — UIPrefabs adopts an existing
    /// one instead of spawning a second — so you can press Play mid-edit and see the real thing.
    ///
    /// "Bake WYSIWYG" is what makes the Inspector honest. Three things used to change your layout at play time:
    /// GlobalFontApplier replaced every label's FONT with the one in FontConfig (MenuUI alone carried four — Matcha
    /// Cih, Oswald, GROBOLD, Humaroid — and all four became Humaroid on screen), FontConfig.fontScale multiplied every
    /// authored SIZE by 1.575, and labels were sized to the whole button rect even though the art is a rim around a
    /// much smaller face. The bake writes all three into the prefab — shipping font, scaled size, Best Fit within the
    /// box, and a label rect clamped to the button's face — so the prefab renders exactly what the game renders.
    /// Re-run it after adding UI; it is idempotent.
    ///
    /// Note the font is not a free choice: of the four in use only Humaroid and Oswald carry the Turkish letters
    /// (Matcha Cih and GROBOLD have no Ğ ğ İ ı Ş ş), and none of them carry Chinese at all.
    ///
    /// One thing the bake cannot do for you: open the prefab at the DEVICE aspect. The canvas is 1080 wide with
    /// match=width, so its height follows the Game view — 1920 at 9:16, 2340 on a real phone, 608 in a 2560x1440
    /// landscape window (which is why a landscape Game view makes the menu look shattered). Use
    /// "Add 1080x2340 Game View Size" once and pick it.
    /// </summary>
    public static class UIWorkbench
    {
        const string Root = "Ridebury/UI/";

        // Every authored screen, tagged with the scene it belongs to. Same list UIPrefabs spawns from.
        // `menu`: true = only the main menu, false = only gameplay, null = both (the shop is shared).
        static readonly (string path, string rootName, bool? menu)[] Screens =
        {
            ("Assets/Resources/UI/HudPanel.prefab",      UIPrefabs.HudRoot,      false),
            ("Assets/Resources/UI/GamePanels.prefab",    UIPrefabs.PanelsRoot,   false),
            ("Assets/Resources/UI/GaragePanel.prefab",   UIPrefabs.GarageRoot,   false),
            ("Assets/Resources/UI/TutorialPanel.prefab", UIPrefabs.TutorialRoot, false),
            ("Assets/Resources/UI/MenuUI.prefab",        UIPrefabs.MenuRoot,     true),
            ("Assets/Resources/UI/ShopPanel.prefab",     "ShopPanel_Baked",      null),
        };

        /// <summary>True when the open scene is the main menu. A screen checked out into the wrong scene would
        /// render over the game at play time, so check-out follows the scene rather than dropping all six in.</summary>
        static bool InMenuScene()
        {
            var scene = EditorSceneManager.GetActiveScene();
            if (scene.name == UIPrefabs.MenuSceneName) return true;
            foreach (var go in scene.GetRootGameObjects())
                if (go != null && go.GetComponentInChildren<RideburyGame>(true) != null) return false;
            return scene.name == UIPrefabs.MenuSceneName;
        }

        /// <summary>The aspect everything is judged at: 1080 wide (the canvas is match=width, so this never varies)
        /// by the height a 20:9 phone gives. Both the bake and the check resolve layout at this size.</summary>
        static readonly Vector2 DesignSize = new Vector2(1080, 2340);

        /// <summary>How much of a button sprite's 9-slice frame is off-limits to its label. Mirrors
        /// GlobalFontApplier.FramePad — keep the two the same or the bake and the runtime will disagree.</summary>
        const float FramePad = 0.7f;

        const int MinFitSize = 8;

        // ---------------------------------------------------------------- check out / put back

        [MenuItem(Root + "Edit UI in Scene (check out)", priority = 0)]
        static void CheckOut()
        {
            var scene = EditorSceneManager.GetActiveScene();
            bool menu = InMenuScene();
            int added = 0, skipped = 0;
            foreach (var (path, rootName, belongs) in Screens)
            {
                if (belongs.HasValue && belongs.Value != menu) { skipped++; continue; }  // wrong scene for this screen
                if (FindRoot(rootName) != null) continue;                     // already checked out
                var asset = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (asset == null) { Debug.LogWarning($"[UI] missing {path}"); continue; }

                var go = (GameObject)PrefabUtility.InstantiatePrefab(asset, scene);
                go.name = rootName;                                            // the name the game uses
                Undo.RegisterCreatedObjectUndo(go, "Check out UI");
                added++;
            }
            EditorSceneManager.MarkSceneDirty(scene);
            Debug.Log($"[UI] checked out {added} screen(s) into '{scene.name}'" +
                      (skipped > 0 ? $" ({skipped} belong to the {(menu ? "game" : "main menu")} scene — open that scene to edit them)" : "") + ". " +
                      "Edit them in the Hierarchy, then run " +
                      "'Ridebury ▸ UI ▸ Put UI Back' to save your edits into the prefabs.\n" +
                      "Pop-ups are saved inactive so they don't cover the screen — tick one active to work on it " +
                      "(or use 'Reveal All Pop-ups'). Press Play any time: the game adopts this copy.");
        }

        [MenuItem(Root + "Put UI Back (apply + remove)", priority = 1)]
        static void PutBack()
        {
            int applied = 0;
            foreach (var (_, rootName, _) in Screens)
            {
                var go = FindRoot(rootName);
                if (go == null) continue;
                WarnIfPopupsLeftOpen(go);
                if (PrefabUtility.IsPartOfPrefabInstance(go))
                {
                    PrefabUtility.ApplyPrefabInstance(go, InteractionMode.UserAction);
                    applied++;
                }
                else Debug.LogWarning($"[UI] '{rootName}' is not a prefab instance — edits NOT saved. " +
                                      "Drag it onto its prefab in Assets/Resources/UI yourself before deleting it.");
                Object.DestroyImmediate(go);
            }
            EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
            AssetDatabase.SaveAssets();
            Debug.Log($"[UI] applied {applied} screen(s) back to their prefabs and cleared the scene.");
        }

        [MenuItem(Root + "Reveal All Pop-ups (checked-out copies)", priority = 2)]
        static void Reveal()
        {
            int n = 0;
            foreach (var (_, rootName, _) in Screens)
            {
                var go = FindRoot(rootName);
                if (go == null) continue;
                foreach (var rt in go.GetComponentsInChildren<RectTransform>(true))
                {
                    // only the pop-up roots: a direct child of the canvas that fills it
                    if (rt.parent != go.transform || rt.gameObject.activeSelf) continue;
                    Undo.RecordObject(rt.gameObject, "Reveal pop-ups");
                    rt.gameObject.SetActive(true);
                    n++;
                }
            }
            Debug.Log($"[UI] revealed {n} hidden panel(s). They overlap each other by design — hide the ones you are " +
                      "not editing. Re-hide them all before 'Put UI Back', or the game opens holding every pop-up.");
        }

        /// <summary>"Reveal All Pop-ups" leaves panels switched on. Saving them that way makes the game open holding
        /// every pop-up at once, so say so loudly before the edits go back into the prefab.</summary>
        static void WarnIfPopupsLeftOpen(GameObject screen)
        {
            var open = new List<string>();
            foreach (Transform child in screen.transform)
            {
                if (!child.gameObject.activeSelf) continue;
                var rt = child as RectTransform;
                if (rt == null) continue;
                bool fullScreen = rt.anchorMin == Vector2.zero && rt.anchorMax == Vector2.one;
                if (fullScreen && child.name.StartsWith("Panel_")) open.Add(child.name);
            }
            if (open.Count > 0)
                Debug.LogWarning($"[UI] '{screen.name}' is being saved with {open.Count} pop-up(s) left ACTIVE " +
                                 $"({string.Join(", ", open)}). Pop-ups ship hidden — untick them and put the screen " +
                                 "back again, or the game starts with them covering the screen.", screen);
        }

        [MenuItem(Root + "Hide All Pop-ups (checked-out copies)", priority = 3)]
        static void Hide()
        {
            int n = 0;
            foreach (var (_, rootName, _) in Screens)
            {
                var go = FindRoot(rootName);
                if (go == null) continue;
                foreach (Transform child in go.transform)
                {
                    var rt = child as RectTransform;
                    if (rt == null || !child.gameObject.activeSelf) continue;
                    if (rt.anchorMin != Vector2.zero || rt.anchorMax != Vector2.one) continue;  // full-screen only
                    if (!child.name.StartsWith("Panel_") && !child.name.StartsWith("NewUI_")) continue;
                    Undo.RecordObject(child.gameObject, "Hide pop-ups");
                    child.gameObject.SetActive(false);
                    n++;
                }
            }
            EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
            Debug.Log($"[UI] hid {n} pop-up(s). Do this before saving the scene: a pop-up left on becomes a scene " +
                      "override that forces it open in the game — that is how the main menu ended up opening with " +
                      "six panels stacked on it.");
        }

        [MenuItem(Root + "Reset Pop-up Visibility to the Prefab", priority = 4)]
        static void ResetVisibility()
        {
            int n = 0;
            foreach (var (_, rootName, _) in Screens)
            {
                var go = FindRoot(rootName);
                if (go == null) continue;
                foreach (var t in go.GetComponentsInChildren<Transform>(true))
                {
                    var src = PrefabUtility.GetCorrespondingObjectFromSource(t.gameObject);
                    if (src == null || src.activeSelf == t.gameObject.activeSelf) continue;
                    Undo.RecordObject(t.gameObject, "Reset visibility");
                    t.gameObject.SetActive(src.activeSelf);
                    n++;
                }
            }
            EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
            Debug.Log($"[UI] reset {n} object(s) to the on/off state their prefab says. Use this when the scene has " +
                      "drifted from the prefabs and you are not sure what got toggled.");
        }

        [MenuItem(Root + "Revert Scene Overrides to the Prefabs", priority = 5)]
        static void RevertOverrides()
        {
            int n = 0;
            foreach (var (_, rootName, _) in Screens)
            {
                var go = FindRoot(rootName);
                if (go == null || !PrefabUtility.IsPartOfPrefabInstance(go)) continue;
                var instRoot = PrefabUtility.GetOutermostPrefabInstanceRoot(go);

                // Everything INSIDE the screen goes back to the prefab; the instance's own root keeps its name,
                // placement and active state, which is what makes it this scene's copy.
                foreach (var c in go.GetComponentsInChildren<Component>(true))
                {
                    if (c == null || c.gameObject == instRoot || c is Transform && c.gameObject == instRoot) continue;
                    if (!PrefabUtility.IsPartOfPrefabInstance(c)) continue;
                    PrefabUtility.RevertObjectOverride(c, InteractionMode.AutomatedAction);
                    n++;
                }
                foreach (var t in go.GetComponentsInChildren<Transform>(true))
                {
                    if (t.gameObject == instRoot) continue;
                    PrefabUtility.RevertObjectOverride(t.gameObject, InteractionMode.AutomatedAction);
                }
            }
            EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
            Debug.Log($"[UI] reverted overrides on {n} object(s) — the screens in this scene now match their prefabs.\n" +
                      "Scene overrides are how one scene's copy of a screen quietly drifts from another's: the menu's " +
                      "shop had 97 frozen layout values the game's copy did not, and only the menu misbehaved. " +
                      "Run this whenever a screen works in one scene but not the other.");
        }

        // ---------------------------------------------------------------- open

        [MenuItem(Root + "Open Prefab/HUD",             priority = 60)] static void OpenHud()      => Open(0);
        [MenuItem(Root + "Open Prefab/Game Pop-ups",    priority = 61)] static void OpenPanels()   => Open(1);
        [MenuItem(Root + "Open Prefab/Garage + Wardrobe", priority = 62)] static void OpenGarage() => Open(2);
        [MenuItem(Root + "Open Prefab/Tutorial Banner", priority = 63)] static void OpenTutorial() => Open(3);
        [MenuItem(Root + "Open Prefab/Main Menu",       priority = 64)] static void OpenMenu()     => Open(4);
        [MenuItem(Root + "Open Prefab/Shop",            priority = 65)] static void OpenShop()     => Open(5);

        /// <summary>Open one screen in Prefab Mode. The screens are never in a scene until you check them out, so
        /// this is the shortest route to a hierarchy you can click — the garage in particular has no other way in.</summary>
        static void Open(int i)
        {
            var asset = AssetDatabase.LoadAssetAtPath<GameObject>(Screens[i].path);
            if (asset == null) { Debug.LogWarning("[UI] missing " + Screens[i].path); return; }
            AssetDatabase.OpenAsset(asset);
            Debug.Log($"[UI] opened {System.IO.Path.GetFileName(Screens[i].path)}. Its pop-ups are saved OFF — tick one " +
                      "active in the Hierarchy to work on it, and untick it before you leave.");
        }

        // ---------------------------------------------------------------- bake

        [MenuItem(Root + "Bake WYSIWYG into Prefabs", priority = 20)]
        static void Bake()
        {
            float scale = GameFont.UiScale;
            var font = GameFont.UGUI;               // the ONE font the game renders every label in
            var tmpFont = GameFont.TMP;
            var log = new StringBuilder();
            int texts = 0, resized = 0, refonted = 0;

            foreach (var (path, _, _) in Screens)
            {
                var root = PrefabUtility.LoadPrefabContents(path);
                if (root == null) continue;
                try
                {
                    bool first = root.GetComponent<BakedUiText>() == null;
                    if (first) root.AddComponent<BakedUiText>();

                    using (new ResolvedAt(root, DesignSize))
                    {
                        foreach (var t in root.GetComponentsInChildren<Text>(true))
                        {
                            if (Skip(t)) continue;
                            texts++;
                            if (font != null && t.font != font) { t.font = font; refonted++; }
                            if (first) t.fontSize = Mathf.Max(MinFitSize, Mathf.RoundToInt(t.fontSize * scale));
                            t.horizontalOverflow = HorizontalWrapMode.Wrap;
                            t.verticalOverflow = VerticalWrapMode.Truncate;
                            t.resizeTextMinSize = MinFitSize;
                            t.resizeTextMaxSize = Mathf.Max(MinFitSize, t.fontSize);
                            t.resizeTextForBestFit = true;
                            if (ClampToFace(t)) resized++;
                        }
                        foreach (var t in root.GetComponentsInChildren<TMP_Text>(true))
                        {
                            if (Skip(t)) continue;
                            texts++;
                            if (tmpFont != null && t.font != tmpFont) { t.font = tmpFont; refonted++; }
                            if (first) t.fontSize = Mathf.Max(MinFitSize, t.fontSize * scale);
                            t.overflowMode = TextOverflowModes.Truncate;
                            t.fontSizeMin = MinFitSize;
                            t.fontSizeMax = Mathf.Max(MinFitSize, t.fontSize);
                            t.enableAutoSizing = true;
                            if (ClampToFace(t)) resized++;
                        }
                    }

                    PrefabUtility.SaveAsPrefabAsset(root, path);
                    log.AppendLine($"   {System.IO.Path.GetFileName(path)}  {(first ? "sizes baked x" + scale.ToString("0.###") : "sizes already baked")}");
                }
                finally { PrefabUtility.UnloadPrefabContents(root); }
            }

            AssetDatabase.SaveAssets();
            Debug.Log($"[UI] baked {texts} label(s); {resized} label box(es) clamped to their button's face; " +
                      $"{refonted} switched to the shipping font ({(font != null ? font.name : "?")}).\n{log}" +
                      "The Inspector now shows what ships — same font, same size. Open a prefab at 1080x2340 and it " +
                      "matches the game.");
        }

        /// <summary>Clamp a label's box to the face of the button behind it, keeping where the designer put it.
        /// Returns true if anything moved. Buttons whose sprite carries no 9-slice frame are left alone — there is
        /// nothing to measure the rim from, so the authored box stands.</summary>
        static bool ClampToFace(Graphic g)
        {
            var holder = g.transform.parent != null ? g.transform.parent.GetComponent<Image>() : null;
            var sprite = holder != null ? holder.sprite : null;
            if (sprite == null) return false;
            Vector4 b = sprite.border;
            if (b == Vector4.zero || sprite.rect.width < 1f || sprite.rect.height < 1f) return false;

            RectTransform p = holder.rectTransform, l = g.rectTransform;
            Rect pr = p.rect;
            if (pr.width < 4f || pr.height < 4f) return false;

            var face = new Vector2(
                pr.width  * Mathf.Max(0.1f, 1f - FramePad * (b.x + b.z) / sprite.rect.width),
                pr.height * Mathf.Max(0.1f, 1f - FramePad * (b.y + b.w) / sprite.rect.height));

            Vector2 size = l.rect.size;
            Vector2 centre = p.InverseTransformPoint(l.TransformPoint(l.rect.center));

            Vector2 want = Vector2.Min(size, face);
            Vector2 half = face * 0.5f;
            var c = new Vector2(
                Mathf.Clamp(centre.x, pr.center.x - half.x + want.x * 0.5f, pr.center.x + half.x - want.x * 0.5f),
                Mathf.Clamp(centre.y, pr.center.y - half.y + want.y * 0.5f, pr.center.y + half.y - want.y * 0.5f));

            if ((want - size).sqrMagnitude < 0.01f && (c - centre).sqrMagnitude < 0.01f) return false;

            l.anchorMin = l.anchorMax = l.pivot = new Vector2(0.5f, 0.5f);
            l.sizeDelta = want;
            l.anchoredPosition = c - pr.center;
            return true;
        }

        static bool Skip(Graphic g) =>
            g.GetComponent<NoTextFit>() != null || g.GetComponent<ContentSizeFitter>() != null;

        // ---------------------------------------------------------------- check

        [MenuItem(Root + "Check UI Fits", priority = 21)]
        static void Check()
        {
            int problems = 0;
            foreach (var (path, _, _) in Screens)
            {
                var root = PrefabUtility.LoadPrefabContents(path);
                if (root == null) continue;
                try
                {
                    using (new ResolvedAt(root, DesignSize))
                    {
                        string file = System.IO.Path.GetFileName(path);
                        var canvas = new Rect(0, 0, DesignSize.x, DesignSize.y);

                        foreach (var g in root.GetComponentsInChildren<Graphic>(true))
                        {
                            var rt = g.rectTransform;
                            if (rt == root.transform) continue;

                            // outside the screen? (scroll contents legitimately are, so skip anything under a Mask)
                            if (rt.GetComponentInParent<Mask>(true) == null && rt.GetComponentInParent<RectMask2D>(true) == null)
                            {
                                Rect w = ScreenRect(rt, root.transform as RectTransform, DesignSize);
                                if (w.width > 1f && w.height > 1f &&
                                    (w.xMin < -1f || w.yMin < -1f || w.xMax > canvas.xMax + 1f || w.yMax > canvas.yMax + 1f))
                                {
                                    Debug.LogWarning($"[UI] {file}: '{Path(rt)}' falls off screen at {DesignSize.x}x{DesignSize.y} " +
                                                     $"(x {w.xMin:0}..{w.xMax:0}, y {w.yMin:0}..{w.yMax:0}).", g);
                                    problems++;
                                }
                            }

                            // a graphic switched off inside a panel that is on — almost always an accident
                            if (!g.gameObject.activeSelf && rt.parent != null && rt.parent.gameObject.activeInHierarchy
                                && !g.name.StartsWith("Panel_") && !g.name.StartsWith("Check") && !g.name.StartsWith("NewUI_"))
                            {
                                Debug.LogWarning($"[UI] {file}: '{Path(rt)}' is switched OFF inside a panel that is on. " +
                                                 "If it should show (a coin icon, a label), tick it active.", g);
                                problems++;
                            }

                            // a label wider/taller than the face it sits on?
                            var t = g as Text;
                            if (t != null && !Skip(t) && OverflowsFace(t))
                            {
                                Debug.LogWarning($"[UI] {file}: '{Path(rt)}' is bigger than its button's face — " +
                                                 "run 'Bake WYSIWYG into Prefabs' or widen the button.", g);
                                problems++;
                            }
                        }
                    }
                }
                finally { PrefabUtility.UnloadPrefabContents(root); }
            }
            Debug.Log(problems == 0
                ? $"[UI] all screens fit at {DesignSize.x}x{DesignSize.y}."
                : $"[UI] {problems} problem(s) above — click one to select the object.");
        }

        static bool OverflowsFace(Graphic g)
        {
            var holder = g.transform.parent != null ? g.transform.parent.GetComponent<Image>() : null;
            var sprite = holder != null ? holder.sprite : null;
            if (sprite == null || sprite.border == Vector4.zero) return false;
            Vector4 b = sprite.border;
            Rect pr = holder.rectTransform.rect;
            var face = new Vector2(
                pr.width  * Mathf.Max(0.1f, 1f - FramePad * (b.x + b.z) / sprite.rect.width),
                pr.height * Mathf.Max(0.1f, 1f - FramePad * (b.y + b.w) / sprite.rect.height));
            Vector2 s = g.rectTransform.rect.size;
            return s.x > face.x + 1f || s.y > face.y + 1f;
        }

        static Rect ScreenRect(RectTransform rt, RectTransform root, Vector2 size)
        {
            if (root == null) return new Rect();
            Vector2 min = root.InverseTransformPoint(rt.TransformPoint(rt.rect.min));
            Vector2 max = root.InverseTransformPoint(rt.TransformPoint(rt.rect.max));
            Vector2 o = -(Vector2)root.rect.min;   // root local -> a 0..size screen box, whatever its pivot is
            return Rect.MinMaxRect(min.x + o.x, min.y + o.y, max.x + o.x, max.y + o.y);
        }

        /// <summary>A checked-out screen by name. GameObject.Find would miss it — TutorialPanel is saved inactive.</summary>
        static GameObject FindRoot(string name)
        {
            foreach (var go in EditorSceneManager.GetActiveScene().GetRootGameObjects())
                if (go != null && go.name == name) return go;
            return null;
        }

        static string Path(Transform t)
        {
            var s = t.name;
            for (var p = t.parent; p != null; p = p.parent) s = p.name + "/" + s;
            return s;
        }

        // ---------------------------------------------------------------- game view size

        [MenuItem(Root + "Add 1080x2340 Game View Size", priority = 40)]
        static void AddGameViewSize()
        {
            // GameViewSizes is internal, so this goes through reflection and simply reports if Unity moved it.
            try
            {
                var sizesType = typeof(Editor).Assembly.GetType("UnityEditor.GameViewSizes");
                var singleton = typeof(ScriptableSingleton<>).MakeGenericType(sizesType);
                var instance = singleton.GetProperty("instance").GetValue(null);
                var group = sizesType.GetMethod("GetGroup").Invoke(instance, new object[] { 0 }); // Standalone
                var sizeType = typeof(Editor).Assembly.GetType("UnityEditor.GameViewSize");
                var sizeTypeEnum = typeof(Editor).Assembly.GetType("UnityEditor.GameViewSizeType");
                var ctor = sizeType.GetConstructor(new[] { sizeTypeEnum, typeof(int), typeof(int), typeof(string) });
                var size = ctor.Invoke(new object[] { System.Enum.ToObject(sizeTypeEnum, 1), 1080, 2340, "Phone 1080x2340 (20:9)" });
                group.GetType().GetMethod("AddCustomSize").Invoke(group, new[] { size });
                Debug.Log("[UI] added Game view size 'Phone 1080x2340 (20:9)'. Pick it in the Game view dropdown — " +
                          "the prefabs then preview at the shape a real phone has.");
            }
            catch (System.Exception e)
            {
                Debug.LogWarning("[UI] could not add the Game view size automatically (" + e.GetType().Name +
                                 "). Add it by hand: Game view ▸ resolution dropdown ▸ + ▸ Fixed Resolution 1080 x 2340.");
            }
        }

        // ---------------------------------------------------------------- helpers

        /// <summary>Resolves a prefab's layout at a given screen size while the block runs, then puts the root's own
        /// rect back exactly as it was so the bake never writes a canvas size into the asset.</summary>
        sealed class ResolvedAt : System.IDisposable
        {
            readonly RectTransform _rt;
            readonly Vector2 _min, _max, _delta, _pos;

            public ResolvedAt(GameObject root, Vector2 size)
            {
                _rt = root.transform as RectTransform;
                if (_rt == null) return;
                _min = _rt.anchorMin; _max = _rt.anchorMax; _delta = _rt.sizeDelta; _pos = _rt.anchoredPosition;
                _rt.anchorMin = _rt.anchorMax = new Vector2(0.5f, 0.5f);
                _rt.anchoredPosition = Vector2.zero;
                _rt.sizeDelta = size;
                LayoutRebuilder.ForceRebuildLayoutImmediate(_rt);
            }

            public void Dispose()
            {
                if (_rt == null) return;
                _rt.anchorMin = _min; _rt.anchorMax = _max; _rt.sizeDelta = _delta; _rt.anchoredPosition = _pos;
            }
        }
    }
}
