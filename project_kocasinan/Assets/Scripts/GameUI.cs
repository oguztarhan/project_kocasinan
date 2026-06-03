using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;

namespace BusJam
{
    /// <summary>Runtime-built portrait uGUI front-end (menu, HUD, win, lose).</summary>
    public class GameUI : MonoBehaviour
    {
        public System.Action OnPlay, OnNext, OnRetry, OnMenu, OnSkip, OnSwap, OnAddTime, OnToggleSound;

        Font font;
        GameObject menuPanel, hudPanel, winPanel, losePanel;
        Text menuCoins, menuBest, hudCoins, hudLevel, hudTimer, hudTheme, winEarned, loseReason, comboText;
        Image[] winStars;
        Text soundLabel;

        readonly Color PanelBg = new Color(0.05f, 0.07f, 0.12f, 0.74f);
        readonly Color Card    = new Color(0.13f, 0.16f, 0.24f, 0.98f);

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

            BuildMenu(canvasGo.transform);
            BuildHud(canvasGo.transform, skipCost, swapCost, timeCost);
            BuildWin(canvasGo.transform);
            BuildLose(canvasGo.transform);
            ShowMenu();
        }

        void BuildMenu(Transform parent)
        {
            menuPanel = Panel(parent, "Menu", PanelBg);
            Label(menuPanel.transform, "BUS JAM\nRUSH", new Vector2(0, 520), new Vector2(900, 280), 110, Color.white);
            Label(menuPanel.transform, "Move buses. Board in order. Beat the clock!", new Vector2(0, 320), new Vector2(960, 60), 34, new Color(0.82f, 0.87f, 0.96f));

            menuCoins = Label(menuPanel.transform, "", new Vector2(0, 170), new Vector2(700, 60), 46, Palette.Gold);
            menuBest  = Label(menuPanel.transform, "", new Vector2(0, 100), new Vector2(700, 46), 30, new Color(0.78f, 0.82f, 0.92f));

            Button(menuPanel.transform, "PLAY", new Vector2(0, -120), new Vector2(440, 150),
                new Color(0.27f, 0.78f, 0.38f), () => OnPlay?.Invoke(), 60);
            var soundBtn = Button(menuPanel.transform, "SOUND: ON", new Vector2(0, -330), new Vector2(340, 90),
                new Color(0.32f, 0.42f, 0.62f), () => OnToggleSound?.Invoke(), 32);
            soundLabel = soundBtn.GetComponentInChildren<Text>();
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

        void BuildWin(Transform parent)
        {
            winPanel = Panel(parent, "Win", PanelBg);
            var card = Image(winPanel.transform, Card);
            Anchor(card.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(860, 760));

            Label(winPanel.transform, "LEVEL\nCOMPLETE!", new Vector2(0, 230), new Vector2(800, 200), 72, new Color(0.45f, 0.92f, 0.55f));

            winStars = new Image[3];
            for (int i = 0; i < 3; i++)
            {
                var s = Image(winPanel.transform, new Color(0.3f, 0.32f, 0.38f));
                Anchor(s.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2((i - 1) * 150, 40), new Vector2(110, 110));
                s.transform.localRotation = Quaternion.Euler(0, 0, 45);
                winStars[i] = s;
            }

            winEarned = Label(winPanel.transform, "+0 coins", new Vector2(0, -100), new Vector2(760, 70), 48, Palette.Gold);
            Button(winPanel.transform, "NEXT", new Vector2(0, -240), new Vector2(440, 140),
                new Color(0.27f, 0.72f, 0.92f), () => OnNext?.Invoke(), 56);
            Button(winPanel.transform, "MENU", new Vector2(0, -390), new Vector2(300, 100),
                new Color(0.4f, 0.45f, 0.55f), () => OnMenu?.Invoke(), 36);
        }

        void BuildLose(Transform parent)
        {
            losePanel = Panel(parent, "Lose", PanelBg);
            var card = Image(losePanel.transform, Card);
            Anchor(card.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(860, 660));

            Label(losePanel.transform, "GAME OVER", new Vector2(0, 200), new Vector2(800, 100), 70, new Color(0.96f, 0.42f, 0.42f));
            loseReason = Label(losePanel.transform, "", new Vector2(0, 70), new Vector2(760, 70), 36, new Color(0.85f, 0.88f, 0.95f));
            Button(losePanel.transform, "RETRY", new Vector2(0, -90), new Vector2(440, 140),
                new Color(0.92f, 0.57f, 0.22f), () => OnRetry?.Invoke(), 56);
            Button(losePanel.transform, "MENU", new Vector2(0, -240), new Vector2(300, 100),
                new Color(0.4f, 0.45f, 0.55f), () => OnMenu?.Invoke(), 36);
        }

        // ---- API ------------------------------------------------------------
        public void ShowMenu() { Toggle(menuPanel, true); Toggle(hudPanel, false); Toggle(winPanel, false); Toggle(losePanel, false); }
        public void ShowHud()  { Toggle(menuPanel, false); Toggle(hudPanel, true); Toggle(winPanel, false); Toggle(losePanel, false); }

        public void ShowWin(int coinsEarned, int stars)
        {
            Toggle(hudPanel, false); Toggle(winPanel, true);
            winEarned.text = $"+{coinsEarned} coins";
            for (int i = 0; i < winStars.Length; i++)
                winStars[i].color = i < stars ? Palette.Gold : new Color(0.3f, 0.32f, 0.38f);
        }

        public void ShowLose(string reason) { Toggle(hudPanel, false); Toggle(losePanel, true); loseReason.text = reason; }

        public void SetCoins(int c) { if (hudCoins) hudCoins.text = c.ToString(); if (menuCoins) menuCoins.text = "Coins: " + c; }
        public void SetMenuInfo(int best) { if (menuBest) menuBest.text = $"Best level: {best}"; }
        public void SetLevel(int l) { if (hudLevel) hudLevel.text = "Level " + l; }
        public void SetTheme(string t) { if (hudTheme) hudTheme.text = t; }
        public void SetSound(bool on) { if (soundLabel) soundLabel.text = on ? "SOUND: ON" : "SOUND: OFF"; }

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
