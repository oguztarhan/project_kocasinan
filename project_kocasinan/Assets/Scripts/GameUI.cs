using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;

namespace BusJam
{
    /// <summary>
    /// Runtime-built portrait uGUI in-game HUD (coins, level, timer, joker buttons).
    /// The Main Menu, Level Complete, and Game Over screens are provided by the
    /// project's own custom canvases/managers, so they are intentionally not built here.
    /// </summary>
    public class GameUI : MonoBehaviour
    {
        // Pause forwards to the project's own navigation; jokers drive gameplay actions.
        public System.Action OnMenu, OnSkip, OnSwap, OnAddTime;

        Font font;
        GameObject hudPanel;
        Text hudCoins, hudLevel, hudTimer, hudTheme, comboText;

        public void Build(int skipCost, int swapCost, int timeCost)
        {
            font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");

            var canvasGo = new GameObject("UICanvas");
            canvasGo.transform.SetParent(transform, false);
            var canvas = canvasGo.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
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
                var module = es.AddComponent<InputSystemUIInputModule>();
                module.AssignDefaultActions();
            }

            BuildHud(canvasGo.transform, skipCost, swapCost, timeCost);
            ShowHud();
        }

        void BuildHud(Transform parent, int skipCost, int swapCost, int timeCost)
        {
            hudPanel = Panel(parent, "Hud", new Color(0, 0, 0, 0));
            hudPanel.GetComponent<Image>().raycastTarget = false;

            var bar = Image(hudPanel.transform, new Color(0.06f, 0.08f, 0.13f, 0.85f));
            bar.raycastTarget = false;
            bar.rectTransform.anchorMin = new Vector2(0, 1); bar.rectTransform.anchorMax = new Vector2(1, 1);
            bar.rectTransform.pivot = new Vector2(0.5f, 1f);
            bar.rectTransform.offsetMin = new Vector2(0, -150); bar.rectTransform.offsetMax = Vector2.zero;

            var coinDot = Image(hudPanel.transform, Palette.Gold);
            Anchor(coinDot.rectTransform, new Vector2(0, 1), new Vector2(0, 1), new Vector2(70, -75), new Vector2(48, 48));
            hudCoins = TopLabel(hudPanel.transform, "0", new Vector2(0, 1), new Vector2(110, -75), new Vector2(280, 60), 42, Palette.Gold, TextAnchor.MiddleLeft);
            hudLevel = TopLabel(hudPanel.transform, "Level 1", new Vector2(0.5f, 1), new Vector2(0, -60), new Vector2(500, 60), 46, Color.white, TextAnchor.MiddleCenter);
            hudTheme = TopLabel(hudPanel.transform, "", new Vector2(0.5f, 1), new Vector2(0, -112), new Vector2(500, 40), 26, new Color(0.8f, 0.85f, 0.95f), TextAnchor.MiddleCenter);
            hudTimer = TopLabel(hudPanel.transform, "0:00", new Vector2(1, 1), new Vector2(-40, -75), new Vector2(260, 60), 46, Color.white, TextAnchor.MiddleRight);

            comboText = Label(hudPanel.transform, "", new Vector2(0, 360), new Vector2(900, 100), 70, Palette.Gold);
            comboText.gameObject.SetActive(false);

            // joker buttons across the bottom
            var skip = Button(hudPanel.transform, $"SKIP\n{skipCost}", new Vector2(-340, 130), new Vector2(280, 150),
                new Color(0.88f, 0.38f, 0.32f), () => OnSkip?.Invoke(), 36);
            var swap = Button(hudPanel.transform, $"SWAP\n{swapCost}", new Vector2(0, 130), new Vector2(280, 150),
                new Color(0.35f, 0.56f, 0.88f), () => OnSwap?.Invoke(), 36);
            var time = Button(hudPanel.transform, $"+TIME\n{timeCost}", new Vector2(340, 130), new Vector2(280, 150),
                new Color(0.42f, 0.72f, 0.42f), () => OnAddTime?.Invoke(), 36);
            AnchorBottom(skip); AnchorBottom(swap); AnchorBottom(time);

            var pause = Button(hudPanel.transform, "II", new Vector2(-70, -75), new Vector2(96, 96),
                new Color(0.3f, 0.35f, 0.45f), () => OnMenu?.Invoke(), 40);
            pause.GetComponent<RectTransform>().anchorMin = pause.GetComponent<RectTransform>().anchorMax = new Vector2(1, 1);
            pause.GetComponent<RectTransform>().anchoredPosition = new Vector2(-70, -75);
        }

        // ---- API ------------------------------------------------------------
        // Only the in-game HUD is managed here. Win/Lose/Menu screens live in the
        // project's own canvases, which react to BusJamGame's gameplay events.
        public void ShowHud() { Toggle(hudPanel, true); }
        public void HideHud() { Toggle(hudPanel, false); }

        public void SetCoins(int c) { if (hudCoins) hudCoins.text = c.ToString(); }
        public void SetLevel(int l) { if (hudLevel) hudLevel.text = "Level " + l; }
        public void SetTheme(string t) { if (hudTheme) hudTheme.text = t; }

        public void SetTimer(float t)
        {
            if (!hudTimer) return;
            t = Mathf.Max(0, t);
            int m = (int)t / 60, s = (int)t % 60;
            hudTimer.text = $"{m}:{s:00}";
            hudTimer.color = t <= 10f ? new Color(1f, 0.4f, 0.4f) : Color.white;
        }

        public void ShowCombo(int combo)
        {
            if (!comboText) return;
            comboText.gameObject.SetActive(true);
            comboText.text = $"COMBO x{combo}!";
            CancelInvoke(nameof(ClearCombo));
            Invoke(nameof(ClearCombo), 0.8f);
        }
        void ClearCombo() { if (comboText) comboText.gameObject.SetActive(false); }

        // ---- Builders -------------------------------------------------------
        GameObject Panel(Transform parent, string name, Color bg)
        {
            var img = Image(parent, bg);
            img.gameObject.name = name;
            Stretch(img.rectTransform, Vector2.zero, Vector2.one);
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

        Text Label(Transform parent, string text, Vector2 pos, Vector2 size, int fontSize, Color color, TextAnchor align = TextAnchor.MiddleCenter)
        {
            var t = MakeText(parent, text, fontSize, color, align);
            var rt = t.rectTransform;
            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = pos; rt.sizeDelta = size;
            return t;
        }

        Text TopLabel(Transform parent, string text, Vector2 anchor, Vector2 pos, Vector2 size, int fontSize, Color color, TextAnchor align)
        {
            var t = MakeText(parent, text, fontSize, color, align);
            var rt = t.rectTransform;
            rt.anchorMin = rt.anchorMax = anchor; rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = pos; rt.sizeDelta = size;
            return t;
        }

        Text MakeText(Transform parent, string text, int fontSize, Color color, TextAnchor align)
        {
            var go = new GameObject("Text", typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var t = go.AddComponent<Text>();
            t.font = font; t.text = text; t.fontSize = fontSize; t.color = color; t.alignment = align;
            t.horizontalOverflow = HorizontalWrapMode.Overflow; t.verticalOverflow = VerticalWrapMode.Overflow;
            t.raycastTarget = false;
            var sh = go.AddComponent<Shadow>(); sh.effectColor = new Color(0, 0, 0, 0.6f); sh.effectDistance = new Vector2(2, -2);
            return t;
        }

        Button Button(Transform parent, string text, Vector2 pos, Vector2 size, Color color, System.Action onClick, int fontSize = 32)
        {
            var img = Image(parent, color);
            var rt = img.rectTransform;
            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = pos; rt.sizeDelta = size;
            var btn = img.gameObject.AddComponent<Button>();
            btn.targetGraphic = img;
            var colors = btn.colors; colors.highlightedColor = color * 1.1f; colors.pressedColor = color * 0.85f; btn.colors = colors;
            if (onClick != null) btn.onClick.AddListener(() => onClick());

            var label = Label(img.transform, text, Vector2.zero, size, fontSize, Color.white);
            Stretch(label.rectTransform, Vector2.zero, Vector2.one);
            label.rectTransform.sizeDelta = Vector2.zero;
            return btn;
        }

        void Stretch(RectTransform rt, Vector2 min, Vector2 max) { rt.anchorMin = min; rt.anchorMax = max; rt.offsetMin = Vector2.zero; rt.offsetMax = Vector2.zero; }
        void Anchor(RectTransform rt, Vector2 min, Vector2 max, Vector2 pos, Vector2 size) { rt.anchorMin = min; rt.anchorMax = max; rt.pivot = new Vector2(0.5f, 0.5f); rt.anchoredPosition = pos; rt.sizeDelta = size; }

        void AnchorBottom(Button b)
        {
            var rt = b.GetComponent<RectTransform>();
            Vector2 p = rt.anchoredPosition;
            rt.anchorMin = new Vector2(0.5f, 0); rt.anchorMax = new Vector2(0.5f, 0); rt.pivot = new Vector2(0.5f, 0);
            rt.anchoredPosition = new Vector2(p.x, 50);
        }

        void Toggle(GameObject go, bool on) { if (go) go.SetActive(on); }
    }
}
