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
        public System.Action OnMenu, OnRecolor, OnSwap, OnHeli;
        // Settings panel navigation + win-reward claiming.
        public System.Action OnHome, OnReplay;
        public System.Action<int> OnClaimReward; // grants the gold amount, then advances

        Font font;
        Sprite knob;
        GameObject hudPanel, settingsPanel, successPanel;
        Text hudCoins, hudLevel, hudTheme, comboText, hudPeopleLeft;

        // Level-gated joker buttons (RECOLOR / SWAP / HELI) + their gating metadata.
        Button[] jokerBtns;
        int[] jokerUnlock, jokerCost;
        string[] jokerName;
        Color[] jokerColor;

        public void Build(int recolorCost, int swapCost, int heliCost, int j1Lvl, int j2Lvl, int j3Lvl)
        {
            font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            // Runtime-generated circle (no built-in "UI/Skin/Knob.psd" dependency,
            // which is unavailable in Unity 6 players and logs a sprite error).
            knob = UISprites.Circle();

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

            BuildHud(canvasGo.transform, recolorCost, swapCost, heliCost, j1Lvl, j2Lvl, j3Lvl);
            BuildSettingsPanel(canvasGo.transform);
            BuildSuccessPanel(canvasGo.transform);
            ShowHud();
        }

        void BuildHud(Transform parent, int recolorCost, int swapCost, int heliCost, int j1Lvl, int j2Lvl, int j3Lvl)
        {
            hudPanel = Panel(parent, "Hud", new Color(0, 0, 0, 0));
            hudPanel.GetComponent<Image>().raycastTarget = false;
            // NOTE: the old dark top "strip" bar was intentionally removed for a clean top HUD.

            // LEVEL indicator: TOP-LEFT corner, round/circular badge.
            var levelBadge = Image(hudPanel.transform, new Color(0.30f, 0.35f, 0.45f, 0.95f));
            levelBadge.sprite = knob; levelBadge.raycastTarget = false;
            Anchor(levelBadge.rectTransform, new Vector2(0, 1), new Vector2(0, 1), new Vector2(100, -100), new Vector2(150, 150));
            hudLevel = TopLabel(hudPanel.transform, "1", new Vector2(0, 1), new Vector2(100, -100), new Vector2(150, 70), 48, Color.white, TextAnchor.MiddleCenter);
            hudTheme = TopLabel(hudPanel.transform, "", new Vector2(0, 1), new Vector2(100, -185), new Vector2(260, 36), 24, new Color(0.8f, 0.85f, 0.95f), TextAnchor.MiddleCenter);

            // PEOPLE-LEFT booth: LEFT margin beside the people queue / bus stops, so the player
            // easily sees how many passengers remain. Person-icon badge with the remaining total
            // below it. (Screen-space over a fixed camera — nudge the -500/-600 Y to taste.)
            var peopleBadge = Image(hudPanel.transform, new Color(0.30f, 0.55f, 0.85f, 0.95f));
            peopleBadge.sprite = knob; peopleBadge.raycastTarget = false;
            Anchor(peopleBadge.rectTransform, new Vector2(0, 1), new Vector2(0, 1), new Vector2(105, -500), new Vector2(120, 120));
            var peopleIcon = Image(hudPanel.transform, Color.white);
            peopleIcon.sprite = UISprites.Person(); peopleIcon.raycastTarget = false;
            Anchor(peopleIcon.rectTransform, new Vector2(0, 1), new Vector2(0, 1), new Vector2(105, -494), new Vector2(70, 70));
            hudPeopleLeft = TopLabel(hudPanel.transform, "0", new Vector2(0, 1), new Vector2(105, -606), new Vector2(160, 70), 50, Color.white, TextAnchor.MiddleCenter);

            // SETTINGS button: TOP-RIGHT corner.
            var settingsBtn = Button(hudPanel.transform, "⚙", new Vector2(-90, -100), new Vector2(120, 120),
                new Color(0.30f, 0.35f, 0.45f), () => ShowSettings(), 60);
            var sbRt = settingsBtn.GetComponent<RectTransform>();
            sbRt.anchorMin = sbRt.anchorMax = new Vector2(1, 1);
            sbRt.anchoredPosition = new Vector2(-90, -100);

            // (no timer — T7 removed the countdown; loss is parking-deadlock only.)

            // COIN indicator: directly BELOW the Settings button.
            var coinDot = Image(hudPanel.transform, Palette.Gold);
            coinDot.sprite = knob; coinDot.raycastTarget = false;
            Anchor(coinDot.rectTransform, new Vector2(1, 1), new Vector2(1, 1), new Vector2(-160, -215), new Vector2(50, 50));
            hudCoins = TopLabel(hudPanel.transform, "0", new Vector2(1, 1), new Vector2(-60, -215), new Vector2(170, 56), 40, Palette.Gold, TextAnchor.MiddleRight);

            comboText = Label(hudPanel.transform, "", new Vector2(0, 360), new Vector2(900, 100), 70, Palette.Gold);
            comboText.gameObject.SetActive(false);

            // Level-gated joker buttons across the bottom: RECOLOR (Lv5) / SWAP (Lv10) / HELI (Lv15).
            var recolorCol = new Color(0.80f, 0.45f, 0.85f);
            var swapCol = new Color(0.35f, 0.56f, 0.88f);
            var heliCol = new Color(0.42f, 0.72f, 0.55f);
            var recolor = Button(hudPanel.transform, "", new Vector2(-340, 130), new Vector2(280, 150), recolorCol, () => OnRecolor?.Invoke(), 34);
            var swap = Button(hudPanel.transform, "", new Vector2(0, 130), new Vector2(280, 150), swapCol, () => OnSwap?.Invoke(), 34);
            var heli = Button(hudPanel.transform, "", new Vector2(340, 130), new Vector2(280, 150), heliCol, () => OnHeli?.Invoke(), 34);
            AnchorBottom(recolor); AnchorBottom(swap); AnchorBottom(heli);

            jokerBtns = new[] { recolor, swap, heli };
            jokerUnlock = new[] { j1Lvl, j2Lvl, j3Lvl };
            jokerCost = new[] { recolorCost, swapCost, heliCost };
            jokerName = new[] { "RECOLOR", "SWAP", "HELI" };
            jokerColor = new[] { recolorCol, swapCol, heliCol };
            RefreshJokerLocks();
        }

        /// <summary>Grey out + label "Lv N" any joker not yet unlocked by SaveSystem.Level; restore the
        /// rest to NAME + cost. Call when the player level may have changed (e.g. each StartLevel).</summary>
        public void RefreshJokerLocks()
        {
            if (jokerBtns == null) return;
            int lvl = SaveSystem.Level;
            var locked = new Color(0.24f, 0.26f, 0.31f, 1f);
            for (int i = 0; i < jokerBtns.Length; i++)
            {
                bool open = lvl >= jokerUnlock[i];
                var b = jokerBtns[i];
                b.interactable = open;
                var img = b.GetComponent<Image>();
                if (img != null) img.color = open ? jokerColor[i] : locked;
                var label = b.GetComponentInChildren<Text>();
                if (label != null) label.text = open ? $"{jokerName[i]}\n{jokerCost[i]}" : $"{jokerName[i]}\nLv {jokerUnlock[i]}";
            }
        }

        // ---- Settings panel (800 x 600) -------------------------------------
        void BuildSettingsPanel(Transform parent)
        {
            settingsPanel = Panel(parent, "SettingsScreen", new Color(0, 0, 0, 0.6f));

            var card = Image(settingsPanel.transform, new Color(0.14f, 0.16f, 0.22f, 1f));
            Center(card.rectTransform, new Vector2(800, 600));

            Label(card.transform, "SETTINGS", new Vector2(0, 220), new Vector2(700, 90), 60, Color.white);

            SettingRow(card.transform, "SOUND", 90, SaveSystem.Sound, ToggleSound);
            SettingRow(card.transform, "MUSIC", -20, SaveSystem.Music, ToggleMusic);
            SettingRow(card.transform, "VIBRATION", -130, SaveSystem.Vibration, ToggleVibration);

            Button(card.transform, "CLOSE", new Vector2(0, -250), new Vector2(300, 110),
                new Color(0.45f, 0.50f, 0.60f), () => HideSettings(), 40);

            // Slightly BELOW the settings card: HOME and REPLAY.
            Button(settingsPanel.transform, "HOME", new Vector2(-180, -400), new Vector2(320, 120),
                new Color(0.42f, 0.72f, 0.42f), () => { HideSettings(); OnHome?.Invoke(); }, 42);
            Button(settingsPanel.transform, "REPLAY", new Vector2(180, -400), new Vector2(320, 120),
                new Color(0.88f, 0.62f, 0.32f), () => { HideSettings(); OnReplay?.Invoke(); }, 42);

            settingsPanel.SetActive(false);
        }

        // One labelled on/off toggle row; returns the state Text so it can update.
        Text SettingRow(Transform parent, string name, float y, bool on, System.Action<bool> onChange)
        {
            Label(parent, name, new Vector2(-200, y), new Vector2(360, 70), 40, Color.white, TextAnchor.MiddleLeft);
            Text state = null;
            var btn = Button(parent, "", new Vector2(220, y), new Vector2(220, 90),
                new Color(0.22f, 0.24f, 0.32f), null, 0);
            state = Label(btn.transform, on ? "ON" : "OFF", Vector2.zero, new Vector2(220, 90), 40,
                on ? new Color(0.5f, 0.9f, 0.55f) : new Color(0.9f, 0.5f, 0.5f));
            btn.onClick.AddListener(() =>
            {
                bool now = state.text != "ON";
                state.text = now ? "ON" : "OFF";
                state.color = now ? new Color(0.5f, 0.9f, 0.55f) : new Color(0.9f, 0.5f, 0.5f);
                onChange?.Invoke(now);
            });
            return state;
        }

        // ---- Success / achievement panel (800 x 1000) -----------------------
        void BuildSuccessPanel(Transform parent)
        {
            successPanel = Panel(parent, "SuccessScreen", new Color(0, 0, 0, 0.65f));

            var card = Image(successPanel.transform, new Color(0.14f, 0.18f, 0.16f, 1f));
            Center(card.rectTransform, new Vector2(800, 1000));

            // Plain by request — no decorative art yet, just the "GOOD" text.
            Label(card.transform, "GOOD", new Vector2(0, 250), new Vector2(700, 160), 120, Palette.Gold);

            Button(card.transform, "CLAIM\n+20", new Vector2(0, -120), new Vector2(560, 160),
                new Color(0.42f, 0.72f, 0.42f), () => ClaimReward(20), 46);
            Button(card.transform, "WATCH AD  x2\n+40", new Vector2(0, -320), new Vector2(560, 160),
                new Color(0.88f, 0.62f, 0.32f), () => WatchAdReward(40), 40);

            successPanel.SetActive(false);
        }

        void ClaimReward(int amount)
        {
            successPanel.SetActive(false);
            OnClaimReward?.Invoke(amount);
        }

        void WatchAdReward(int amount)
        {
            // TODO: integrate a real rewarded-ad SDK here. Grant the doubled reward
            //       only from the ad's success/reward callback. For now we grant it
            //       directly and proceed, leaving the ad link to be added later.
            successPanel.SetActive(false);
            OnClaimReward?.Invoke(amount);
        }

        // ---- Settings behaviour (persist + drive real audio/vibration) ------
        void ToggleSound(bool on)     { SaveSystem.Sound = on; }
        void ToggleMusic(bool on)     { SaveSystem.Music = on; }
        void ToggleVibration(bool on)
        {
            SaveSystem.Vibration = on;
            if (on) Vibrate(); // immediate feedback so the player feels it engage
        }

        public static void Vibrate()
        {
            if (!SaveSystem.Vibration) return;
#if UNITY_ANDROID || UNITY_IOS
            Handheld.Vibrate();
#endif
        }

        // ---- API ------------------------------------------------------------
        // Only the in-game HUD is managed here. Win/Lose/Menu screens live in the
        // project's own canvases, which react to BusJamGame's gameplay events.
        public void ShowHud() { Toggle(hudPanel, true); }
        public void HideHud() { Toggle(hudPanel, false); }

        public void ShowSettings() { Toggle(settingsPanel, true); }
        public void HideSettings() { Toggle(settingsPanel, false); }

        public void ShowSuccess() { Toggle(successPanel, true); }
        public void HideSuccess() { Toggle(successPanel, false); }

        public void SetCoins(int c) { if (hudCoins) hudCoins.text = c.ToString(); }
        public void SetLevel(int l) { if (hudLevel) hudLevel.text = l.ToString(); }
        public void SetTheme(string t) { if (hudTheme) hudTheme.text = t; }
        public void SetPeopleLeft(int n) { if (hudPeopleLeft) hudPeopleLeft.text = n.ToString(); }

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

            if (fontSize > 0)
            {
                var label = Label(img.transform, text, Vector2.zero, size, fontSize, Color.white);
                Stretch(label.rectTransform, Vector2.zero, Vector2.one);
                label.rectTransform.sizeDelta = Vector2.zero;
            }
            return btn;
        }

        void Stretch(RectTransform rt, Vector2 min, Vector2 max) { rt.anchorMin = min; rt.anchorMax = max; rt.offsetMin = Vector2.zero; rt.offsetMax = Vector2.zero; }
        void Anchor(RectTransform rt, Vector2 min, Vector2 max, Vector2 pos, Vector2 size) { rt.anchorMin = min; rt.anchorMax = max; rt.pivot = new Vector2(0.5f, 0.5f); rt.anchoredPosition = pos; rt.sizeDelta = size; }
        void Center(RectTransform rt, Vector2 size) { rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f); rt.anchoredPosition = Vector2.zero; rt.sizeDelta = size; }

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
