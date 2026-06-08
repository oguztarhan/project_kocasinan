using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;

namespace BusJam
{
    /// <summary>
    /// Self-contained Level Select screen on its OWN canvas (kept separate from the
    /// HUD-only GameUI). Shows levels 1..N where N = SaveSystem.Level (highest
    /// unlocked); locked levels are shown but not tappable. Tapping a level calls
    /// BusJamGame.LoadLevel. Build() is called by BusJamGame; you can also drive it
    /// from your own UI via Open()/Close()/Toggle().
    /// </summary>
    public class LevelSelect : MonoBehaviour
    {
        BusJamGame game;
        Font font;
        GameObject panel;
        RectTransform content;
        GameObject openButton;
        bool isOpen;

        const int Columns = 4;
        const int LockedPreview = 4;   // how many locked levels to show past your progress
        const int MinShown = 12;

        public void Build(BusJamGame g)
        {
            game = g;
            font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");

            var canvasGo = new GameObject("LevelSelectCanvas");
            canvasGo.transform.SetParent(transform, false);
            var canvas = canvasGo.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 50; // above the HUD
            var scaler = canvasGo.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1080, 1920);
            scaler.matchWidthOrHeight = 0.5f;
            canvasGo.AddComponent<GraphicRaycaster>();

            if (EventSystem.current == null)
            {
                var es = new GameObject("EventSystem");
                es.transform.SetParent(transform, false);
                es.AddComponent<EventSystem>();
                es.AddComponent<InputSystemUIInputModule>().AssignDefaultActions();
            }

            BuildOpenButton(canvasGo.transform);
            BuildPanel(canvasGo.transform);   // created after the open button -> renders on top when shown
            Close();
        }

        // ---- Public API -----------------------------------------------------
        public void Open()
        {
            PopulateGrid();
            if (panel) panel.SetActive(true);
            if (openButton) openButton.SetActive(false);
            Time.timeScale = 0f;             // pause while browsing
            isOpen = true;
        }

        public void Close()
        {
            if (panel) panel.SetActive(false);
            if (openButton) openButton.SetActive(true);
            Time.timeScale = 1f;
            isOpen = false;
        }

        public void Toggle() { if (isOpen) Close(); else Open(); }

        // Safety: never leave the game paused if this is torn down while open.
        void OnDisable() { if (isOpen) Time.timeScale = 1f; }

        void SelectLevel(int n)
        {
            Close();
            if (game != null) game.LoadLevel(n);
        }

        // ---- Build ----------------------------------------------------------
        void BuildOpenButton(Transform parent)
        {
            // Top-left, below the HUD's level badge/theme so it doesn't overlap.
            var btn = Button(parent, "LEVELS", new Color(0.30f, 0.35f, 0.45f), () => Toggle(), 34);
            var rt = btn.GetComponent<RectTransform>();
            rt.anchorMin = rt.anchorMax = new Vector2(0, 1);
            rt.pivot = new Vector2(0, 1);
            rt.anchoredPosition = new Vector2(30, -250);
            rt.sizeDelta = new Vector2(220, 96);
            openButton = btn.gameObject;
        }

        void BuildPanel(Transform parent)
        {
            panel = Panel(parent, "LevelSelectPanel", new Color(0.04f, 0.06f, 0.10f, 0.82f));

            var card = Image(panel.transform, new Color(0.13f, 0.16f, 0.24f, 1f));
            Anchor(card.rectTransform, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(980, 1600));

            Label(card.transform, "SELECT LEVEL", new Vector2(0, 700), new Vector2(820, 90), 58, Color.white);

            var close = Button(card.transform, "X", new Color(0.5f, 0.3f, 0.3f), () => Close(), 46);
            var crt = close.GetComponent<RectTransform>();
            crt.anchorMin = crt.anchorMax = new Vector2(1, 1);
            crt.pivot = new Vector2(1, 1);
            crt.anchoredPosition = new Vector2(-40, -40);
            crt.sizeDelta = new Vector2(96, 96);

            // Scroll view
            var svGo = new GameObject("Scroll", typeof(RectTransform));
            svGo.transform.SetParent(card.transform, false);
            var svRt = svGo.GetComponent<RectTransform>();
            svRt.anchorMin = Vector2.zero; svRt.anchorMax = Vector2.one;
            svRt.offsetMin = new Vector2(40, 60); svRt.offsetMax = new Vector2(-40, -180);
            var scroll = svGo.AddComponent<ScrollRect>();
            scroll.horizontal = false; scroll.vertical = true;
            scroll.movementType = ScrollRect.MovementType.Elastic;
            scroll.scrollSensitivity = 35f;

            var vpGo = new GameObject("Viewport", typeof(RectTransform), typeof(Image), typeof(Mask));
            vpGo.transform.SetParent(svGo.transform, false);
            var vpRt = vpGo.GetComponent<RectTransform>();
            vpRt.anchorMin = Vector2.zero; vpRt.anchorMax = Vector2.one;
            vpRt.offsetMin = Vector2.zero; vpRt.offsetMax = Vector2.zero; vpRt.pivot = new Vector2(0, 1);
            vpGo.GetComponent<Image>().color = new Color(1, 1, 1, 0.01f);
            vpGo.GetComponent<Mask>().showMaskGraphic = false;
            scroll.viewport = vpRt;

            var contentGo = new GameObject("Content", typeof(RectTransform), typeof(GridLayoutGroup), typeof(ContentSizeFitter));
            contentGo.transform.SetParent(vpGo.transform, false);
            content = contentGo.GetComponent<RectTransform>();
            content.anchorMin = new Vector2(0, 1); content.anchorMax = new Vector2(1, 1); content.pivot = new Vector2(0.5f, 1);
            content.offsetMin = Vector2.zero; content.offsetMax = Vector2.zero;
            var grid = contentGo.GetComponent<GridLayoutGroup>();
            grid.cellSize = new Vector2(195, 195);
            grid.spacing = new Vector2(20, 20);
            grid.padding = new RectOffset(15, 15, 15, 15);
            grid.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
            grid.constraintCount = Columns;
            grid.childAlignment = TextAnchor.UpperCenter;
            contentGo.GetComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            scroll.content = content;
        }

        void PopulateGrid()
        {
            if (content == null) return;
            for (int i = content.childCount - 1; i >= 0; i--) Destroy(content.GetChild(i).gameObject);

            int unlocked = Mathf.Max(1, SaveSystem.Level);
            int total = Mathf.Max(unlocked + LockedPreview, MinShown);
            total = Mathf.CeilToInt(total / (float)Columns) * Columns; // fill rows

            for (int n = 1; n <= total; n++)
            {
                bool isUnlocked = n <= unlocked;
                bool isCompleted = n < unlocked;
                AddLevelButton(n, isUnlocked, isCompleted);
            }
        }

        void AddLevelButton(int n, bool unlocked, bool completed)
        {
            var go = new GameObject("Lvl" + n, typeof(RectTransform), typeof(Image));
            go.transform.SetParent(content, false);
            var img = go.GetComponent<Image>();
            img.color = !unlocked ? new Color(0.24f, 0.26f, 0.31f, 1f)
                      : completed  ? new Color(0.30f, 0.70f, 0.42f, 1f)
                                   : new Color(0.30f, 0.55f, 0.92f, 1f);
            var btn = go.AddComponent<Button>();
            btn.targetGraphic = img;
            btn.interactable = unlocked;
            if (unlocked) { int lv = n; btn.onClick.AddListener(() => SelectLevel(lv)); }

            var label = MakeText(go.transform, n.ToString(), 70,
                unlocked ? Color.white : new Color(0.55f, 0.58f, 0.65f), TextAnchor.MiddleCenter);
            Stretch(label.rectTransform);

            if (!unlocked)
            {
                var lk = MakeText(go.transform, "LOCKED", 24, new Color(0.55f, 0.58f, 0.65f), TextAnchor.LowerCenter);
                var rt = lk.rectTransform;
                rt.anchorMin = new Vector2(0, 0); rt.anchorMax = new Vector2(1, 0); rt.pivot = new Vector2(0.5f, 0);
                rt.anchoredPosition = new Vector2(0, 14); rt.sizeDelta = new Vector2(0, 30);
            }
        }

        // ---- uGUI helpers (self-contained) ----------------------------------
        GameObject Panel(Transform parent, string name, Color bg)
        {
            var img = Image(parent, bg);
            img.gameObject.name = name;
            var rt = img.rectTransform;
            rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one; rt.offsetMin = Vector2.zero; rt.offsetMax = Vector2.zero;
            return img.gameObject;
        }

        Image Image(Transform parent, Color color)
        {
            var go = new GameObject("Img", typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var img = go.AddComponent<Image>();
            img.color = color;
            return img;
        }

        Text MakeText(Transform parent, string text, int fontSize, Color color, TextAnchor align)
        {
            var go = new GameObject("Text", typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var t = go.AddComponent<Text>();
            t.font = font; t.text = text; t.fontSize = fontSize; t.color = color; t.alignment = align;
            t.horizontalOverflow = HorizontalWrapMode.Overflow; t.verticalOverflow = VerticalWrapMode.Overflow;
            t.raycastTarget = false;
            var sh = go.AddComponent<Shadow>(); sh.effectColor = new Color(0, 0, 0, 0.5f); sh.effectDistance = new Vector2(2, -2);
            return t;
        }

        Text Label(Transform parent, string text, Vector2 pos, Vector2 size, int fontSize, Color color)
        {
            var t = MakeText(parent, text, fontSize, color, TextAnchor.MiddleCenter);
            var rt = t.rectTransform;
            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = pos; rt.sizeDelta = size;
            return t;
        }

        Button Button(Transform parent, string text, Color color, System.Action onClick, int fontSize)
        {
            var img = Image(parent, color);
            var rt = img.rectTransform;
            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = new Vector2(220, 96);
            var btn = img.gameObject.AddComponent<Button>();
            btn.targetGraphic = img;
            var colors = btn.colors; colors.highlightedColor = color * 1.1f; colors.pressedColor = color * 0.85f; btn.colors = colors;
            if (onClick != null) btn.onClick.AddListener(() => onClick());
            var label = MakeText(img.transform, text, fontSize, Color.white, TextAnchor.MiddleCenter);
            Stretch(label.rectTransform);
            return btn;
        }

        void Stretch(RectTransform rt) { rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one; rt.offsetMin = Vector2.zero; rt.offsetMax = Vector2.zero; }
        void Anchor(RectTransform rt, Vector2 anchor, Vector2 pos, Vector2 size)
        { rt.anchorMin = rt.anchorMax = anchor; rt.pivot = new Vector2(0.5f, 0.5f); rt.anchoredPosition = pos; rt.sizeDelta = size; }
    }
}
