using System.Collections;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;

namespace BusJam
{
    /// <summary>
    /// Runtime-built in-game HUD + Settings / Shop / Continue / Failed / Success panels,
    /// styled with the 300Mind "2D Game UI Kit" (sprites via <see cref="UIKit"/>).
    /// All pop-up windows use the kit's big blue panel (atlas 2, sprite 0). The old
    /// scene-authored canvas is disabled at runtime so legacy white backgrounds, the
    /// old coin display and stray texts never show during gameplay.
    /// </summary>
    public partial class GameUI : MonoBehaviour
    {
        public System.Action OnMenu, OnRecolor, OnSwap, OnHeli;
        public System.Action OnHome, OnReplay, OnLevels;
        public System.Action<int> OnClaimReward;
        public System.Action OnContinueAd, OnContinuePay, OnContinueDeclined;
        public System.Action<int> OnFreeCoins; // +coins rewarded button -> BusJamGame grants coins & fires CoinsChanged

        static readonly Color White = Color.white;
        static readonly Color Gold  = new Color(1f, 0.85f, 0.30f);
        static readonly Color Dark  = new Color(0.16f, 0.20f, 0.30f);
        static readonly Color Dim   = new Color(0, 0, 0, 0.6f);
        static readonly Color OnCol = new Color(0.35f, 0.85f, 0.40f);
        static readonly Color OffCol= new Color(0.65f, 0.65f, 0.70f);

        Font title, num;
        Transform root;
        GameObject hudPanel, settingsPanel, successPanel, continuePanel, failedPanel, shopPanel;
        Text hudCoins, hudLevel, hudTheme, comboText, hudPeopleLeft, successReward, continuePrice, bonusCountdown;
        GameObject jokerBuyPanel; Image jokerBuyIcon; Text jokerBuyPrice; int buyKind, buyCost;
        readonly GameObject[] jokerBuyPanels = new GameObject[3]; // baked per-joker buy panels (0/1/2)
        readonly Sprite[] jokerIcons = new Sprite[3];
        readonly int[] jokerCosts = new int[3];
        GameObject gearGo, levelBadgeGo, adFreeBtnGo; // (#6) HUD chrome hidden while the shop is open; the coin bar stays
        GameObject coinBarGo;      // top-center HUD coin bar — hidden while the GARAGE is open (the garage shows its own gold)
        bool hideBonusTimer;       // true while the garage is open -> the bonus countdown suppresses itself (no duplicate over the garage)
        InGameGarage garageCfg;    // scene marker read for per-element image/colour overrides (found incl. inactive, so no need to enable it)

        // Bottom space reserved for the AdMob adaptive banner so the joker row never sits under it (T3). Tunable.
        const float BannerReservePx = 190f;
        const float JokerBaseBottom = 70f;  // original bottom offset of the joker row
        const int   FreeCoinsReward = 50;   // coins granted by the "+coins" rewarded button (T5)

        struct Joker
        {
            public Button btn;
            public Image bg; public Color bgColor;
            public Image icon; public Color iconColor; public Sprite iconSprite;
            public int unlock, kind, cost;
            public GameObject lockGo, counterGo;
            public Text counterText;
        }
        Joker jRecolor, jSwap, jHeli;
        int level = 1;

        public void Build(int recolorCost, int swapCost, int heliCost, int j1Lvl, int j2Lvl, int j3Lvl)
        {
            title = UIKit.Title();
            num   = UIKit.Num();

            var canvasGo = new GameObject("UICanvas");
            canvasGo.transform.SetParent(transform, false);
            var canvas = canvasGo.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            var sc = canvasGo.AddComponent<CanvasScaler>();
            sc.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            sc.referenceResolution = new Vector2(1080, 1920);
            sc.matchWidthOrHeight = 0f;   // match WIDTH: 1080-wide portrait HUD always fits the screen width on any phone aspect
            canvasGo.AddComponent<GraphicRaycaster>();
            root = canvasGo.transform;

            if (EventSystem.current == null)
            {
                var es = new GameObject("EventSystem");
                es.transform.SetParent(transform, false);
                es.AddComponent<EventSystem>();
                es.AddComponent<InputSystemUIInputModule>().AssignDefaultActions();
            }

            SetupHud(recolorCost, swapCost, heliCost, j1Lvl, j2Lvl, j3Lvl);
            SetupSettings();
            SetupShop();
            SetupContinue();
            SetupFailed();
            SetupSuccess();
            SetupJokerBuy();
            garageCfg = Object.FindFirstObjectByType<InGameGarage>(FindObjectsInactive.Include); // read image/colour overrides even if its canvas is inactive
            BuildGarage();
            BuildVehicles(); // vehicle wardrobe ("dolap") panel — opened from a button on the garage
            ShowHud();
            DisableOldCanvases(); // hide legacy scene canvas (white bg / old coin / texts)
            Localizer.LocalizeScene(); // translate all in-game text to the saved language
            if (OpenGarageOnLoad) { OpenGarageOnLoad = false; garageFromMenu = true; ShowGarage(); } // opened from the main-menu Garage button
        }

        // Hide every canvas that doesn't belong to this game object's hierarchy
        // (runtime only). LevelSelect/GameUI canvases live under the same root, so
        // they survive; the legacy scene canvas does not.
        void DisableOldCanvases()
        {
            if (!Application.isPlaying) return;
            var shopCanvas = InGameShop.Instance != null ? InGameShop.Instance.GetComponent<Canvas>() : null;
            var panelsCanvas = InGamePanels.Instance != null ? InGamePanels.Instance.GetComponent<Canvas>() : null;
            var hudCanvas = InGameHud.Instance != null ? InGameHud.Instance.GetComponent<Canvas>() : null;
            var garageCanvas = InGameGarage.Instance != null ? InGameGarage.Instance.GetComponent<Canvas>() : null;
            foreach (var c in Object.FindObjectsByType<Canvas>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
            {
                if (c == null) continue;
                if (c.transform.root == transform.root) continue; // ours
                if (shopCanvas != null && c == shopCanvas) continue; // baked in-game shop
                if (panelsCanvas != null && c == panelsCanvas) continue; // baked settings/continue/failed
                if (hudCanvas != null && c == hudCanvas) continue; // baked HUD
                if (garageCanvas != null && c == garageCanvas) continue; // baked garage + vehicles panels
                c.gameObject.SetActive(false);
            }
        }

        // ---- HUD setup ------------------------------------------------------
        // Adopt the Inspector-editable scene HUD baked via "Tools ▸ 300Mind UI ▸ Bake
        // In-Game HUD"; otherwise build the HUD in code.
        void SetupHud(int recolorCost, int swapCost, int heliCost, int j1Lvl, int j2Lvl, int j3Lvl)
        {
            var h = InGameHud.Instance;
            if (h == null || h.hudRoot == null)
            {
                BuildHud(recolorCost, swapCost, heliCost, j1Lvl, j2Lvl, j3Lvl);
                return;
            }
            hudPanel = h.hudRoot;
            hudCoins = h.coinText;
            hudLevel = h.levelText;
            hudTheme = h.themeText;
            hudPeopleLeft = h.peopleText;
            comboText = h.comboText;
            if (comboText) comboText.gameObject.SetActive(false);
            if (h.peopleIcon) { h.peopleIcon.sprite = UISprites.Person(); h.peopleIcon.color = White; }
            if (h.coinButton) h.coinButton.onClick.AddListener(ShowShop);
            coinBarGo = h.coinButton ? h.coinButton.gameObject : (hudCoins ? hudCoins.transform.parent.gameObject : null); // (#6) hidden while the garage is open
            if (h.gearButton) h.gearButton.onClick.AddListener(ShowSettings);
            gearGo = h.gearButton ? h.gearButton.gameObject : null;                                       // (#6)
            var lb = FindDeep(hudPanel.transform, "Level_Badge"); levelBadgeGo = lb ? lb.gameObject : null; // (#6)

            jRecolor = AdoptJoker(h.recolor, recolorCost, j1Lvl, 0, () => OnRecolor?.Invoke());
            jSwap    = AdoptJoker(h.swap,    swapCost,    j2Lvl, 1, () => OnSwap?.Invoke());
            jHeli    = AdoptJoker(h.heli,    heliCost,    j3Lvl, 2, () => OnHeli?.Invoke());
            // (#4) Baked joker positions are now used EXACTLY as placed in the Hierarchy. The old auto-lift
            // (ReserveBannerSpace: +190px to clear the banner) overrode your manual placement, so it's gone.
            // (#1) The watch-ad / +coins button was removed from the in-game HUD per request.
            RefreshJokers();
            AddGarageButton(hudPanel.transform);
            BuildBonusCountdown();
        }

        Joker AdoptJoker(HudJoker hj, int cost, int unlock, int kind, System.Action use)
        {
            if (hj == null) return new Joker();
            return MakeJoker(hj.button, hj.background, hj.icon, hj.lockGo, hj.counterGo, hj.counterText, cost, unlock, kind, use);
        }

        // Builds a Joker record + wires the button: when you OWN one, pressing uses it
        // (BusJamGame consumes a charge); when out of stock, pressing opens the buy panel.
        Joker MakeJoker(Button btn, Image bg, Image icon, GameObject lockGo, GameObject counterGo, Text counterText, int cost, int unlock, int kind, System.Action use)
        {
            Sprite iconSprite = icon != null ? icon.sprite : null;
            if (kind >= 0 && kind < 3) { jokerIcons[kind] = iconSprite; jokerCosts[kind] = cost; }
            var j = new Joker
            {
                btn = btn,
                bg = bg, bgColor = bg != null ? bg.color : White,
                icon = icon, iconColor = icon != null ? icon.color : White, iconSprite = iconSprite,
                unlock = unlock, kind = kind, cost = cost,
                lockGo = lockGo, counterGo = counterGo, counterText = counterText
            };
            if (btn != null)
                btn.onClick.AddListener(() =>
                {
                    if (SaveSystem.FreeJoker(kind) > 0) use?.Invoke();   // own one -> use it
                    else ShowJokerBuy(kind);                              // out -> buy with gold
                });
            return j;
        }

        // ---- HUD ------------------------------------------------------------
        void BuildHud(int recolorCost, int swapCost, int heliCost, int j1Lvl, int j2Lvl, int j3Lvl)
        {
            hudPanel = Panel("Hud", new Color(0, 0, 0, 0));
            hudPanel.GetComponent<Image>().raycastTarget = false;

            // LEVEL badge: TOP-LEFT, rounded blue-purple button (atlas1_25), white text.
            var badge = Img(hudPanel.transform, UIKit.A(25), new Color(0.45f, 0.40f, 0.85f));
            Place(badge.rectTransform, new Vector2(0, 1), new Vector2(0, 1), new Vector2(110, -110), new Vector2(170, 170));
            badge.raycastTarget = false;
            Label(badge.transform, "LEVEL", num, new Vector2(0, 42), new Vector2(160, 36), 24, White);
            hudLevel = Label(badge.transform, "1", title, new Vector2(0, -16), new Vector2(160, 90), 64, White);
            levelBadgeGo = badge.gameObject; // (#6) hidden while the shop is open
            hudTheme = Label(hudPanel.transform, "", num, new Vector2(110, -210), new Vector2(260, 36), 22, new Color(0.85f, 0.9f, 1f));
            hudTheme.rectTransform.anchorMin = hudTheme.rectTransform.anchorMax = new Vector2(0, 1);
            hudTheme.rectTransform.anchoredPosition = new Vector2(110, -210);

            // COIN display: TOP-CENTER (atlas1_20 bar), opens the in-game shop.
            var coinBtn = Btn(hudPanel.transform, UIKit.CoinBar(), Dark, new Vector2(0.5f, 1), new Vector2(0, -100), new Vector2(300, 96), ShowShop);
            coinBarGo = coinBtn.gameObject; // (#6) hidden while the garage is open (the garage shows its own gold)
            var ci = Img(coinBtn.transform, UIKit.Coin(), Gold); ci.raycastTarget = false;
            Place(ci.rectTransform, new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(42, 0), new Vector2(74, 74));
            hudCoins = Label(coinBtn.transform, "0", num, new Vector2(35, 0), new Vector2(180, 60), 44, White);

            // SETTINGS gear: TOP-RIGHT.
            gearGo = Btn(hudPanel.transform, UIKit.Gear(), new Color(0.7f, 0.72f, 0.78f), new Vector2(1, 1), new Vector2(-90, -100), new Vector2(120, 120), ShowSettings).gameObject; // (#6)

            // (#1) The watch-ad / +coins button was removed from the in-game HUD per request.

            // (People-left count now lives ONLY on the neon world sign by the first bus stop — HUD chip removed.)

            comboText = Label(hudPanel.transform, "", title, new Vector2(0, 360), new Vector2(900, 100), 70, Gold);
            comboText.gameObject.SetActive(false);

            // 3 jokers across the bottom (atlas1_25 buttons + atlas1_34 count badges).
            jRecolor = JokerButton(-260, UIKit.JokerRecolor(), recolorCost, j1Lvl, 0, () => OnRecolor?.Invoke());
            jSwap    = JokerButton(0,    UIKit.JokerSwap(),    swapCost,    j2Lvl, 1, () => OnSwap?.Invoke());
            jHeli    = JokerButton(260,  UIKit.JokerHeli(),    heliCost,    j3Lvl, 2, () => OnHeli?.Invoke());
            RefreshJokers();
            AddGarageButton(hudPanel.transform);
            BuildBonusCountdown();
        }

        Joker JokerButton(float x, Sprite icon, int cost, int unlock, int kind, System.Action use)
        {
            var btn = Btn(hudPanel.transform, UIKit.A(25), new Color(0.45f, 0.40f, 0.85f), new Vector2(0.5f, 0), new Vector2(x, JokerBaseBottom + BannerReservePx), new Vector2(180, 180), null);
            var rt = btn.GetComponent<RectTransform>();
            rt.anchorMin = new Vector2(0.5f, 0); rt.anchorMax = new Vector2(0.5f, 0); rt.pivot = new Vector2(0.5f, 0);
            rt.anchoredPosition = new Vector2(x, JokerBaseBottom + BannerReservePx);
            var bg = btn.GetComponent<Image>();
            var ico = Img(btn.transform, icon, White); ico.raycastTarget = false;
            Center(ico.rectTransform, new Vector2(112, 112));
            var lk = Img(btn.transform, null, new Color(0, 0, 0, 0.55f)); lk.raycastTarget = false;
            Center(lk.rectTransform, new Vector2(180, 180));
            Label(lk.transform, "LV " + unlock, num, Vector2.zero, new Vector2(170, 60), 34, White);
            var cb = Img(btn.transform, UIKit.A(34), new Color(0.95f, 0.78f, 0.20f)); cb.raycastTarget = false;
            Place(cb.rectTransform, new Vector2(1, 1), new Vector2(1, 1), new Vector2(-4, -4), new Vector2(72, 72));
            var ct = Label(cb.transform, "0", num, Vector2.zero, new Vector2(72, 50), 32, White);
            return MakeJoker(btn, bg, ico, lk.gameObject, cb.gameObject, ct, cost, unlock, kind, use);
        }

        // --- T3/T5 helpers ---
        // Lift a baked joker button up by the reserved banner height so the adaptive banner never covers it.
        void ReserveBannerSpace(HudJoker hj)
        {
            if (hj == null || hj.button == null) return;
            var rt = hj.button.GetComponent<RectTransform>();
            if (rt != null) rt.anchoredPosition += new Vector2(0, BannerReservePx);
        }

        // Small "+coins (watch ad)" button by the coin bar: grants FreeCoinsReward via a rewarded ad.
        void BuildFreeCoinsButton(Transform parent)
        {
            if (parent == null) return;
            var btn = Btn(parent, UIKit.PriceBtnA(), new Color(0.28f, 0.72f, 0.38f), new Vector2(0.5f, 1), new Vector2(245, -100), new Vector2(140, 92), () =>
            {
                var ad = AdManager.Instance;
                if (ad != null) ad.ShowRewarded("freecoins", onReward: () => OnFreeCoins?.Invoke(FreeCoinsReward), onClosedNoReward: null);
            });
            var ic = Img(btn.transform, UIKit.Coin(), Gold); ic.raycastTarget = false;
            Place(ic.rectTransform, new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(32, 8), new Vector2(48, 48));
            Label(btn.transform, "+", title, new Vector2(36, 10), new Vector2(80, 56), 44, White);
            Label(btn.transform, "AD", num, new Vector2(0, -28), new Vector2(120, 28), 20, White);
            adFreeBtnGo = btn.gameObject; // (#6) hidden while the shop is open
        }

        void RefreshJokers() { SetJoker(jRecolor); SetJoker(jSwap); SetJoker(jHeli); }
        void SetJoker(Joker j)
        {
            if (j.btn == null) return;
            bool unlocked = level >= j.unlock;
            j.btn.interactable = unlocked;
            if (j.lockGo) j.lockGo.SetActive(!unlocked);
            int owned = SaveSystem.FreeJoker(j.kind);
            bool faded = unlocked && owned <= 0;            // out of stock -> dim it
            if (j.bg)   j.bg.color   = faded ? Dim40(j.bgColor)   : j.bgColor;
            if (j.icon) j.icon.color = faded ? Dim40(j.iconColor) : j.iconColor;
            if (j.counterGo) j.counterGo.SetActive(unlocked);
            if (j.counterText) j.counterText.text = owned.ToString();
        }
        static Color Dim40(Color c) => new Color(c.r, c.g, c.b, c.a * 0.4f);

        // Adopt the Inspector-editable baked joker-buy panel; otherwise build it in code.
        void SetupJokerBuy()
        {
            var p = InGamePanels.Instance;
            if (p != null && p.jokerBuyRecolor != null)
            {
                WireJokerBuy(p.jokerBuyRecolor, p.jokerBuyRecolorBtn, 0);
                WireJokerBuy(p.jokerBuySwap,    p.jokerBuySwapBtn,    1);
                WireJokerBuy(p.jokerBuyHeli,    p.jokerBuyHeliBtn,    2);
            }
            else BuildJokerBuy();
        }

        void WireJokerBuy(GameObject panel, Button buyBtn, int kind)
        {
            if (panel == null) return;
            jokerBuyPanels[kind] = panel;
            int cost = jokerCosts[kind];
            if (buyBtn)
            {
                // the price label was baked as a static "100"; show the real per-joker cost
                var priceT = buyBtn.transform.Find("Price")?.GetComponent<Text>();
                if (priceT == null) { var ts = buyBtn.GetComponentsInChildren<Text>(true); if (ts.Length > 0) priceT = ts[0]; }
                if (priceT != null) priceT.text = cost.ToString();
                buyBtn.onClick.AddListener(() =>
                {
                    if (SaveSystem.TrySpend(cost)) { SaveSystem.AddFreeJoker(kind, 1); SetCoins(SaveSystem.Coins); RefreshJokers(); }
                });
            }
            foreach (var b in panel.GetComponentsInChildren<InGamePanelButton>(true))
            {
                var btn = b.GetComponent<Button>();
                if (btn != null && b.action == InGamePanelButton.Act.Close)
                    btn.onClick.AddListener(() => panel.SetActive(false));
            }
            panel.SetActive(false);
        }

        // Buy one of the current joker for gold. Stays open so you can keep buying.
        void JokerBuyPressed()
        {
            if (SaveSystem.TrySpend(buyCost))
            {
                SaveSystem.AddFreeJoker(buyKind, 1);
                SetCoins(SaveSystem.Coins);
                RefreshJokers();
            }
        }

        // Code fallback for the joker buy popup (used when no baked panel exists).
        void BuildJokerBuy()
        {
            jokerBuyPanel = Panel("JokerBuy", Dim);
            var cv = jokerBuyPanel.AddComponent<Canvas>(); cv.overrideSorting = true; cv.sortingOrder = 70;
            jokerBuyPanel.AddComponent<GraphicRaycaster>();
            var card = Img(jokerBuyPanel.transform, UIKit.EmptyBoxBlue(), White); card.color = White;
            Center(card.rectTransform, new Vector2(620, 700));
            jokerBuyIcon = Img(card.transform, null, White); jokerBuyIcon.raycastTarget = false;
            Place(jokerBuyIcon.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0, 130), new Vector2(230, 230));
            var buy = Btn(card.transform, UIKit.PriceBtnA(), new Color(0.30f, 0.75f, 0.35f), new Vector2(0.5f, 0.5f), new Vector2(0, -150), new Vector2(380, 130), JokerBuyPressed);
            var bc = Img(buy.transform, UIKit.Coin(), Gold); bc.raycastTarget = false;
            Place(bc.rectTransform, new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(46, 0), new Vector2(60, 60));
            jokerBuyPrice = Label(buy.transform, "0", title, new Vector2(30, 0), new Vector2(380, 80), 44, White);
            RedClose(card.transform, () => jokerBuyPanel.SetActive(false));
            jokerBuyPanel.SetActive(false);
        }

        void ShowJokerBuy(int kind)
        {
            if (jokerBuyPanels[kind] != null) { jokerBuyPanels[kind].SetActive(true); return; }
            // Code fallback: the single reusable panel (set its icon + price for this joker).
            if (jokerBuyPanel == null) return;
            buyKind = kind; buyCost = jokerCosts[kind];
            if (jokerBuyIcon) { jokerBuyIcon.sprite = jokerIcons[kind]; jokerBuyIcon.color = White; }
            if (jokerBuyPrice) jokerBuyPrice.text = jokerCosts[kind].ToString();
            jokerBuyPanel.SetActive(true);
        }

        // ---- Settings — same layout as the baked main-menu Settings panel ----
        // Card tinted #A12929; blue title tile; SOUND/MUSIC toggles (atlas1_37 button +
        // crisp logo, faded when OFF); one empty button; HOME + REPLAY below it.
        void BuildSettings()
        {
            settingsPanel = Panel("Settings", Dim);

            var card = Img(settingsPanel.transform, UIKit.EmptyBoxBlue(), White);
            card.color = new Color(0.631f, 0.161f, 0.161f); // #A12929
            Center(card.rectTransform, new Vector2(820, 1050));

            var tile = Img(card.transform, UIKit.TitleBarA(), new Color(0.25f, 0.55f, 0.90f));
            tile.color = new Color(0.25f, 0.55f, 0.90f); tile.raycastTarget = false;
            Place(tile.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0, 430), new Vector2(580, 130));
            Label(card.transform, "SETTINGS", title, new Vector2(0, 430), new Vector2(560, 100), 56, White);

            // SOUND + MUSIC toggles (atlas1_37 button + crisp icon; fades when OFF).
            AudioToggle(card.transform, -160, UIKit.IconSpeaker(), SaveSystem.Sound, v => SaveSystem.Sound = v);
            AudioToggle(card.transform,  160, UIKit.IconNote(),    SaveSystem.Music, v => SaveSystem.Music = v);

            // HOME + REPLAY: ALL settings buttons use atlas1_36.
            var home = Btn(card.transform, UIKit.PriceBtnA(), new Color(0.4f, 0.8f, 0.45f), new Vector2(0.5f, 0.5f), new Vector2(-180, -250), new Vector2(310, 115),
                () => { HideSettings(); OnHome?.Invoke(); });
            Label(home.transform, "HOME", title, Vector2.zero, new Vector2(310, 80), 40, White);
            var replay = Btn(card.transform, UIKit.PriceBtnA(), new Color(0.95f, 0.75f, 0.25f), new Vector2(0.5f, 0.5f), new Vector2(180, -250), new Vector2(310, 115),
                () => { HideSettings(); OnReplay?.Invoke(); });
            Label(replay.transform, "REPLAY", title, Vector2.zero, new Vector2(320, 80), 38, White);

            RedClose(card.transform, HideSettings);
            settingsPanel.SetActive(false);
        }

        // Sound/Music toggle: atlas1_37 button + crisp logo; full colour when ON, faded when OFF.
        void AudioToggle(Transform parent, float x, Sprite logo, bool initial, System.Action<bool> onChange)
        {
            bool[] st = { initial };
            var btn = Btn(parent, UIKit.PriceBtnB(), new Color(0.95f, 0.78f, 0.20f), new Vector2(0.5f, 0.5f), new Vector2(x, 230), new Vector2(220, 150), null);
            btn.transition = Selectable.Transition.None;
            var bg = btn.GetComponent<Image>();
            var ico = Img(btn.transform, logo, White); ico.raycastTarget = false;
            Center(ico.rectTransform, new Vector2(110, 110));
            System.Action apply = () =>
            {
                var c = st[0] ? White : new Color(0.8f, 0.8f, 0.82f, 0.45f);
                bg.color = c; ico.color = c;
            };
            apply();
            btn.onClick.AddListener(() => { st[0] = !st[0]; apply(); onChange?.Invoke(st[0]); });
        }

        // Push-notification on/off. Flips SaveSystem.NotificationsEnabled and re-applies the FCM subscription. Added to
        // whichever Settings panel is in use (baked or code-built); floats near the top so it never overlaps the card.
        void AddNotificationsToggle(Transform panel)
        {
            if (panel == null) return;
            bool[] st = { SaveSystem.NotificationsEnabled };
            var btn = Btn(panel, UIKit.PriceBtnA(), new Color(0.30f, 0.72f, 0.36f), new Vector2(0.5f, 1f), new Vector2(0, -100), new Vector2(540, 96), null);
            var lbl = Label(btn.transform, "", title, Vector2.zero, new Vector2(540, 60), 34, White);
            System.Action apply = () =>
            {
                lbl.text = st[0] ? "NOTIFICATIONS: ON" : "NOTIFICATIONS: OFF";
                var img = btn.GetComponent<Image>();
                if (img) img.color = st[0] ? new Color(0.30f, 0.72f, 0.36f) : new Color(0.55f, 0.55f, 0.60f);
            };
            apply();
            btn.onClick.AddListener(() =>
            {
                st[0] = !st[0];
                SaveSystem.NotificationsEnabled = st[0];
                FirebaseManager.Instance?.ApplyNotificationState();
                apply();
            });
        }

        // On-device notification DELIVERY TEST (diagnostic). Tapping fires a visible notification ~4s later — Android
        // shows it even in the foreground — bypassing the +1h/day-based re-engagement schedule AND the enabled toggles,
        // so the whole chain (permission → channel → icon → delivery) is confirmable on a device in seconds instead of
        // waiting an hour. If nothing appears after tapping, the fault is permission / OEM battery-kill / a Remote Config
        // kill-switch, NOT the notification code. The button's own label reports the result. Added as the LAST child so
        // it draws on top of the (baked) panel content. To drop it before production, delete this method + its one call.
        void AddNotificationTestButton(Transform panel)
        {
            if (panel == null) return;
            var btn = Btn(panel, UIKit.PriceBtnA(), new Color(0.30f, 0.55f, 0.85f), new Vector2(0.5f, 1f), new Vector2(0, -100), new Vector2(540, 96), null);
            var lbl = Label(btn.transform, "TEST NOTIFICATION", title, Vector2.zero, new Vector2(540, 60), 34, White);
            btn.onClick.AddListener(() => { lbl.text = NotificationService.SendTest(); }); // status shown in-place
        }

        // Restore Purchases (a Google Play storefront REQUIREMENT): re-asserts the no-ads entitlement after a reinstall
        // (IAPManager.Restore replays owned non-consumables). Appended as a full-width row at the BOTTOM of the shop
        // list, so it lives in the storefront next to the things it restores. Works for the baked shop AND the code shop.
        void AddShopRestoreRow(Transform content)
        {
            if (content == null) return;
            var row = Img(content, UIKit.ShopBoxA(), new Color(0.30f, 0.55f, 0.85f));
            var le = row.gameObject.AddComponent<LayoutElement>(); le.preferredHeight = 120; le.minHeight = 120;
            var lbl = Label(row.transform, "RESTORE PURCHASES", title, Vector2.zero, new Vector2(640, 70), 36, White);
            var btn = row.gameObject.AddComponent<Button>(); btn.targetGraphic = row;
            btn.onClick.AddListener(() => { IAPManager.Instance?.Restore(); lbl.text = "RESTORED"; });
        }

        // ---- Settings / Continue / Failed setup -----------------------------
        // Prefer the Inspector-editable scene panels baked via
        // "Tools ▸ 300Mind UI ▸ Bake In-Game Panels"; otherwise build them in code.
        void SetupSettings()
        {
            if (InGamePanels.Instance != null && InGamePanels.Instance.settings != null)
            {
                settingsPanel = InGamePanels.Instance.settings;
                WireSettings(settingsPanel.transform);
                WireLanguageButton(settingsPanel.transform); // (#1/#2) open the language popup + fix its label font
                settingsPanel.SetActive(false);
            }
            else BuildSettings(); // fallback (code-built settings)
            // (Removed per request) The in-game settings push-notification on/off toggle was added here via
            // AddNotificationsToggle(...). That call is intentionally gone so the button no longer appears in the
            // in-game Settings panel. The method is left defined (unused) in case it's wanted again later.
            // Restore Purchases now lives in the SHOP (AddShopRestoreRow), not in Settings.

            // On-device notification DELIVERY TEST button (diagnostic). settingsPanel is assigned in BOTH branches above
            // (baked = InGamePanels.settings, code-built = BuildSettings), so this covers both. Fires a visible
            // notification ~4s after tapping to confirm delivery in seconds; remove before production if undesired.
            AddNotificationTestButton(settingsPanel != null ? settingsPanel.transform : null);
        }

        // (#1/#2) Wire the in-game Settings "Language" button. The baked button is named "Language" (the old code
        // looked for "Card/Btn_Empty", which doesn't exist, so it was never wired). We also fix its label: the baked
        // label is a TMP using a different font than the other (legacy-Text) buttons, so we hide it and add a legacy
        // Text that matches the siblings.
        void WireLanguageButton(Transform settingsRoot)
        {
            var t = FindDeep(settingsRoot, "Language") ?? FindDeep(settingsRoot, "Btn_Language") ?? FindDeep(settingsRoot, "Btn_Empty");
            if (t == null) return;
            var btn = t.GetComponent<Button>() ?? t.GetComponentInParent<Button>();
            if (btn == null) return;

            var lang = InGamePanels.Instance != null ? InGamePanels.Instance.language : null;
            if (lang != null)
            {
                btn.onClick.AddListener(() => lang.SetActive(true)); // (#1) open the popup
                foreach (var b in lang.GetComponentsInChildren<InGamePanelButton>(true)) // let its close button dismiss it
                {
                    var cb = b.GetComponent<Button>();
                    if (cb != null && b.action == InGamePanelButton.Act.Close) cb.onClick.AddListener(() => lang.SetActive(false));
                }
            }

            // Swap the odd TMP label for a legacy Text in GROBOLD ("Gro Bold") — the kit's title font, UIKit.Title().
            var tmp = btn.GetComponentInChildren<TMPro.TMP_Text>(true);
            if (tmp != null) tmp.gameObject.SetActive(false);
            if (btn.GetComponentInChildren<Text>(true) == null)
            {
                var lbl = Label(btn.transform, "Language", title, Vector2.zero, new Vector2(440, 90), 40, White); // title = GROBOLD / Gro Bold
                var rt = lbl.rectTransform;
                rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one; rt.offsetMin = Vector2.zero; rt.offsetMax = Vector2.zero;
            }
        }

        // (LEVELS access is the always-visible on-screen button built by LevelSelect itself — no settings button needed.)

        // (Skin debug button removed — skins are deprecated; vehicles come from the garage chests + craft.)

        void WireSettings(Transform root)
        {
            foreach (var b in root.GetComponentsInChildren<InGamePanelButton>(true))
            {
                var btn = b.GetComponent<Button>();
                if (btn == null) continue;
                switch (b.action)
                {
                    case InGamePanelButton.Act.Home:        btn.onClick.AddListener(() => { HideSettings(); OnHome?.Invoke(); }); break;
                    case InGamePanelButton.Act.Replay:      btn.onClick.AddListener(() => { HideSettings(); OnReplay?.Invoke(); }); break;
                    case InGamePanelButton.Act.Close:       btn.onClick.AddListener(HideSettings); break;
                    case InGamePanelButton.Act.ToggleSound: WireAudioToggle(btn, true); break;
                    case InGamePanelButton.Act.ToggleMusic: WireAudioToggle(btn, false); break;
                }
            }
        }

        void SetupContinue()
        {
            if (InGamePanels.Instance != null && InGamePanels.Instance.continuePanel != null)
            {
                continuePanel = InGamePanels.Instance.continuePanel;
                // (Continue screen) Repurpose the pay-150 button as REPLAY: relabel its text child + hide its coin
                // icon child. continuePrice is intentionally left null so SetContinuePrice can't overwrite "REPLAY".
                var payT = continuePanel.transform.Find("Card/Pay");
                if (payT) foreach (Transform ch in payT)
                {
                    var ct = ch.GetComponent<Text>();
                    if (ct) ct.text = Loc.T("REPLAY");
                    else if (ch.GetComponent<Image>()) ch.gameObject.SetActive(false); // hide the coin icon
                }
                foreach (var b in continuePanel.GetComponentsInChildren<InGamePanelButton>(true))
                {
                    var btn = b.GetComponent<Button>();
                    if (btn == null) continue;
                    switch (b.action)
                    {
                        case InGamePanelButton.Act.ContinueAd:  btn.onClick.AddListener(() => OnContinueAd?.Invoke()); break;
                        case InGamePanelButton.Act.ContinuePay: btn.onClick.AddListener(() => { HideContinue(); OnReplay?.Invoke(); }); break; // was pay-150 -> now REPLAY
                        case InGamePanelButton.Act.Close:       btn.onClick.AddListener(() => { HideContinue(); OnContinueDeclined?.Invoke(); }); break;
                    }
                }
                continuePanel.SetActive(false);
            }
            else BuildContinue();
        }

        void SetupFailed()
        {
            if (InGamePanels.Instance != null && InGamePanels.Instance.failed != null)
            {
                failedPanel = InGamePanels.Instance.failed;
                foreach (var b in failedPanel.GetComponentsInChildren<InGamePanelButton>(true))
                {
                    var btn = b.GetComponent<Button>();
                    if (btn == null) continue;
                    switch (b.action)
                    {
                        case InGamePanelButton.Act.Home:   btn.onClick.AddListener(() => { HideFailed(); OnHome?.Invoke(); }); break;
                        case InGamePanelButton.Act.Replay: btn.onClick.AddListener(() => { HideFailed(); OnReplay?.Invoke(); }); break;
                    }
                }
                AddEmoji(failedPanel, false); // (#4) sad face on the failed screen
                failedPanel.SetActive(false);
            }
            else BuildFailed();
        }

        void SetupSuccess()
        {
            if (InGamePanels.Instance != null && InGamePanels.Instance.success != null)
            {
                successPanel = InGamePanels.Instance.success;
                var rl = successPanel.transform.Find("Card/Reward");
                if (rl) successReward = rl.GetComponent<Text>();
                foreach (var b in successPanel.GetComponentsInChildren<InGamePanelButton>(true))
                {
                    var btn = b.GetComponent<Button>();
                    if (btn == null) continue;
                    if (b.action == InGamePanelButton.Act.Claim)
                    {
                        int amt = b.amount;
                        btn.onClick.AddListener(() => ClaimReward(amt));
                    }
                }
                AddEmoji(successPanel, true); // (#4) happy face on the success screen
                successPanel.SetActive(false);
            }
            else BuildSuccess();
        }

        // (#4) Add a procedural emoji (happy/sad face) sticker just above a baked panel's card.
        void AddEmoji(GameObject panel, bool happy)
        {
            if (panel == null) return;
            Transform card = panel.transform;
            foreach (Transform ch in panel.transform) { var im = ch.GetComponent<Image>(); if (im != null && ch.childCount >= 2) { card = ch; break; } }
            var go = new GameObject("Emoji", typeof(RectTransform), typeof(Image));
            go.transform.SetParent(card, false);
            var img = go.GetComponent<Image>();
            img.sprite = MakeEmojiSprite(happy); img.raycastTarget = false; img.preserveAspect = true;
            var rt = img.rectTransform;
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 1f); rt.pivot = new Vector2(0.5f, 0f);
            rt.anchoredPosition = new Vector2(0f, 6f); rt.sizeDelta = new Vector2(160, 160);
        }

        // A round yellow emoji face — smiling (happy) or frowning (sad). Procedural so it renders reliably (no emoji font needed).
        Sprite MakeEmojiSprite(bool happy)
        {
            int S = 128;
            var tex = new Texture2D(S, S, TextureFormat.RGBA32, false) { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
            var px = new Color[S * S];
            float cx = (S - 1) * 0.5f, cy = (S - 1) * 0.5f, r = S * 0.46f;
            Color face = new Color(1f, 0.83f, 0.22f), dark = new Color(0.18f, 0.12f, 0.04f), edge = new Color(0.82f, 0.58f, 0.10f), clear = new Color(0, 0, 0, 0);
            for (int y = 0; y < S; y++)
                for (int x = 0; x < S; x++)
                {
                    float d = Mathf.Sqrt((x - cx) * (x - cx) + (y - cy) * (y - cy));
                    px[y * S + x] = d <= r ? (d > r - S * 0.05f ? edge : face) : clear;
                }
            float ex = S * 0.18f, ey = cy + S * 0.13f, er = S * 0.072f;
            EmojiDisc(px, S, cx - ex, ey, er, dark);
            EmojiDisc(px, S, cx + ex, ey, er, dark);
            float mw = S * 0.30f, mcy = cy - S * 0.10f, amp = S * 0.14f, th = S * 0.05f;
            for (float x = cx - mw; x <= cx + mw; x += 0.5f)
            {
                float t = (x - cx) / mw;
                float yc = happy ? (mcy - amp * (1f - t * t)) : (mcy - amp * t * t);
                for (float y = yc - th; y <= yc + th; y += 0.5f)
                {
                    int xi = Mathf.RoundToInt(x), yi = Mathf.RoundToInt(y);
                    if (xi >= 0 && xi < S && yi >= 0 && yi < S) px[yi * S + xi] = dark;
                }
            }
            tex.SetPixels(px); tex.Apply(false);
            return Sprite.Create(tex, new Rect(0, 0, S, S), new Vector2(0.5f, 0.5f), 100f);
        }
        static void EmojiDisc(Color[] px, int S, float cx, float cy, float r, Color col)
        {
            for (int y = (int)(cy - r); y <= cy + r; y++)
                for (int x = (int)(cx - r); x <= cx + r; x++)
                    if (x >= 0 && x < S && y >= 0 && y < S) { float dx = x - cx, dy = y - cy; if (dx * dx + dy * dy <= r * r) px[y * S + x] = col; }
        }

        // Wire a baked sound/music toggle button: full colour when ON, faded when OFF.
        void WireAudioToggle(Button btn, bool isSound)
        {
            btn.transition = Selectable.Transition.None;
            var bg = btn.GetComponent<Image>();
            var lt = btn.transform.Find("Logo");
            var ico = lt != null ? lt.GetComponent<Image>() : null;
            bool[] st = { isSound ? SaveSystem.Sound : SaveSystem.Music };
            System.Action apply = () =>
            {
                var c = st[0] ? White : new Color(0.8f, 0.8f, 0.82f, 0.45f);
                if (bg) bg.color = c;
                if (ico) ico.color = c;
            };
            apply();
            btn.onClick.AddListener(() =>
            {
                st[0] = !st[0]; apply();
                if (isSound) SaveSystem.Sound = st[0]; else SaveSystem.Music = st[0];
            });
        }

        // ---- Shop setup -----------------------------------------------------
        // Prefer the Inspector-editable scene shop baked via
        // "Tools ▸ 300Mind UI ▸ Bake In-Game Shop"; otherwise build the code shop.
        void SetupShop()
        {
            IAPManager.OnChanged -= OnIapChanged; IAPManager.OnChanged += OnIapChanged; // repaint counters when a purchase resolves
            // (#5/#7) Use the BAKED scene shop even if its canvas was left INACTIVE in the Hierarchy. A disabled
            // InGameShop_Baked never runs Awake, so InGameShop.Instance stays null and the OLD code-built shop
            // (BuildShop) showed instead — that was the "old shop still showing". Find it inactive-inclusive + enable.
            var shop = InGameShop.Instance;
            if (shop == null) shop = Object.FindFirstObjectByType<InGameShop>(FindObjectsInactive.Include);
            if (shop != null)
            {
                if (!shop.gameObject.activeSelf) shop.gameObject.SetActive(true); // runs its Awake -> assigns + hides panel
                var panel = shop.panel;
                if (panel == null)
                {
                    var t = shop.transform.Find("Panel_GameShop");
                    if (t) panel = t.gameObject;
                }
                if (panel != null)
                {
                    shopPanel = panel;
                    WireSceneShop(shopPanel.transform);
                    shopPanel.SetActive(false);
                    return;
                }
            }
            BuildShop(); // genuine fallback only — no baked shop exists in the scene
        }

        // Wire the baked shop's tagged buttons to live actions (the visuals stay in the scene).
        void WireSceneShop(Transform shopRoot)
        {
            foreach (var b in shopRoot.GetComponentsInChildren<InGameShopButton>(true))
            {
                var btn = b.GetComponent<Button>();
                if (btn == null) continue;
                switch (b.action)
                {
                    case InGameShopButton.Act.GrantCoins:
                        break; // coin packs are mapped to the real CoinPacks below (MapShopCoinButtons) so the shown amount,
                               // the price, and the product purchased always agree — no re-bake needed.
                    case InGameShopButton.Act.SpendJoker:
                    {
                        int jkind = JokerBarKind(b.transform.parent != null ? b.transform.parent.name : null);
                        if (jkind < 0) jkind = 0;                                       // unknown row name -> default to Recolor
                        int jcost = JokerShopCost(jkind);                               // Recolor 75 / Swap 50 / Heli 100 (GameConfig)
                        var jp = btn.transform.Find("Price")?.GetComponent<Text>();
                        if (jp != null) jp.text = jcost.ToString();                     // show the real per-joker price (baked label was a flat "100")
                        btn.onClick.AddListener(() => { if (SaveSystem.TrySpend(jcost)) { SaveSystem.AddFreeJoker(jkind, 1); SetCoins(SaveSystem.Coins); RefreshJokers(); } });
                        break;
                    }
                    case InGameShopButton.Act.Close:
                        btn.onClick.AddListener(HideShop);
                        break;
                }
            }

            // The two promo bars are baked as plain "RemoveAds" rows (no InGameShopButton tag
            // and no Button of their own), so wire them here by name to the real IAP products:
            //   "RemoveAds"     -> remove_ads      (ads off)
            //   "RemoveAds (1)" -> remove_ads_plus (ads off + a one-time 200 gold + free Recolor joker)
            // The bonus is granted inside IAPManager.Grant (flag-gated so a restore can't repeat it).
            WirePromoBar(shopRoot, "RemoveAds", RemoveAds);
            WirePromoBar(shopRoot, "RemoveAds (1)", RemoveAdsPlus);

            // Map the 6 baked coin packs onto the real IAP products (relabels each card's amount + price to match) so
            // every coin button buys a valid product, whatever amounts the shop was baked with.
            MapShopCoinButtons(shopRoot, true);

            // Restore Purchases (Google Play storefront requirement): append a row at the bottom of the shop's scroll list.
            var restoreScroll = shopRoot.GetComponentInChildren<ScrollRect>(true);
            AddShopRestoreRow(restoreScroll != null && restoreScroll.content != null ? restoreScroll.content : shopRoot);

            // (Shop close) Only the empty black backdrop (and the red ✕) close the shop. Force every background/
            // card/row image to catch taps so tapping a package (the red/orange cards) can't fall through to the
            // close-backdrop. Buttons and the icons/labels parented under them are left alone so they still work.
            BlockShopBackgroundTaps(shopRoot);
        }

        // (Shop close) Make every non-button background/card/row image inside the shop a raycast target, so a tap
        // on a package can't pass through to the dim backdrop that closes the shop. A button graphic — and an
        // icon/label parented DIRECTLY under a button — is skipped so its taps still reach the button.
        void BlockShopBackgroundTaps(Transform shopRoot)
        {
            // Make cards/rows/backgrounds catch taps so a tap resolves to them, not the backdrop behind.
            foreach (var img in shopRoot.GetComponentsInChildren<Image>(true))
            {
                if (img.transform == shopRoot) continue;                        // backdrop graphic: it closes on black-area taps
                var p = img.transform.parent;
                if (p != null && p != shopRoot && p.GetComponent<Button>() != null && img.GetComponent<Button>() == null)
                    continue;                                                   // a button's icon/label -> keep non-blocking so its taps reach the button
                img.raycastTarget = true;
            }

            // THE REAL FIX: the dim backdrop is an ANCESTOR of every card AND is the tap-to-close Button. In Unity
            // UI a click on a card with NO click handler BUBBLES UP to the first ancestor that has one — the
            // backdrop — so the shop closes (raycastTarget on the card does NOT stop this; the event still bubbles).
            // Put a no-op click "consumer" on each scroll Viewport (covers the whole package list) and on the Card,
            // so a tap inside the shop is swallowed there and never bubbles up to the backdrop. Drags still scroll —
            // the ScrollRect handles those via a separate (drag) event path.
            foreach (var sr in shopRoot.GetComponentsInChildren<ScrollRect>(true))
            {
                var vp = sr.viewport != null ? sr.viewport : sr.transform.Find("Viewport") as RectTransform;
                if (vp != null) AddClickConsumer(vp.gameObject);
            }
            var cardT = FindDeep(shopRoot, "Card");
            if (cardT != null) AddClickConsumer(cardT.gameObject);
        }

        // Swallow a click that bubbled up to this object so it can't reach the dim backdrop's close handler above
        // it. A Button is an IPointerClickHandler; with no onClick listeners it consumes the click and does nothing
        // else. A near-invisible raycast image is added if the object has no graphic (so empty gaps are caught too).
        // Does NOT block ScrollRect dragging (drag is a separate event).
        void AddClickConsumer(GameObject go)
        {
            if (go == null || go.GetComponent<Selectable>() != null) return;
            var g = go.GetComponent<Graphic>();
            if (g == null) { var img = go.AddComponent<Image>(); img.color = new Color(1f, 1f, 1f, 0.004f); g = img; }
            g.raycastTarget = true;
            var btn = go.AddComponent<Button>();
            btn.transition = Selectable.Transition.None;
            btn.targetGraphic = g;
        }

        // Wire a baked promo row to a real purchase, but make ONLY the green price button ("PriceBg")
        // trigger the buy. The orange bar background is left as a plain tap-blocker so tapping it does
        // nothing — it must NOT purchase, and must NOT fall through to the shop's close-backdrop.
        void WirePromoBar(Transform shopRoot, string rowName, System.Action onBuy)
        {
            var row = FindDeep(shopRoot, rowName);
            if (row == null) return;

            // Orange bar background: keep catching taps (so they can't reach the close-backdrop behind
            // the shop), but make sure it never BUYS — clear any whole-bar button listeners.
            var rowImg = row.GetComponent<Image>();
            if (rowImg != null) rowImg.raycastTarget = true;
            var rowBtn = row.GetComponent<Button>();
            if (rowBtn != null) rowBtn.onClick = new Button.ButtonClickedEvent();

            // The purchase button = the green price child. Fall back to the whole row only if no price
            // child is found (so buying still works on an unexpected layout).
            var price = FindDeep(row, "PriceBg");
            var target = price != null ? price : row;
            var pImg = target.GetComponent<Image>();
            if (pImg != null) pImg.raycastTarget = true;
            var pBtn = target.GetComponent<Button>();
            if (pBtn == null) pBtn = target.gameObject.AddComponent<Button>();
            if (pImg != null) pBtn.targetGraphic = pImg;
            pBtn.onClick = new Button.ButtonClickedEvent();
            pBtn.onClick.AddListener(() => onBuy());
        }

        // First descendant (inactive included) whose GameObject is named `name`, else null.
        static Transform FindDeep(Transform root, string name)
        {
            foreach (var t in root.GetComponentsInChildren<Transform>(true))
                if (t.name == name) return t;
            return null;
        }

        // A shop joker bar -> its joker kind by row name ("Bar_Shuffle"=Recolor 0, "Bar_Swap"=Swap 1, "Bar_Heli"=Heli 2).
        static int JokerBarKind(string name)
        {
            if (string.IsNullOrEmpty(name)) return -1;
            var n = name.ToLowerInvariant();
            if (n.Contains("heli")) return 2;
            if (n.Contains("swap")) return 1;
            if (n.Contains("shuffle") || n.Contains("recolor")) return 0;
            return -1;
        }
        // Per-joker shop price from GameConfig (Recolor 75 / Swap 50 / Heli 100) — same source as the HUD joker buy panel.
        static int JokerShopCost(int kind) => kind == 1 ? GameConfig.SwapCost : kind == 2 ? GameConfig.HeliCost : GameConfig.RecolorCost;

        // The two no-ads shop bars -> real Google Play purchases. The entitlement (and the "plus" tier's one-time
        // bonus) is applied in IAPManager.Grant after Google signs the receipt; OnIapChanged then refreshes the HUD.
        void RemoveAds()     { IAPManager.Instance?.Buy(IAPManager.RemoveAds); }
        void RemoveAdsPlus() { IAPManager.Instance?.Buy(IAPManager.RemoveAdsPlus); }

        // A coin-pack button -> the matching consumable IAP. Coins are added by IAPManager on a verified purchase,
        // never here, so a cancelled/failed purchase grants nothing.
        void BuyCoins(int coins)
        {
            var id = IAPManager.ProductForCoins(coins);
            if (id == null) { Debug.LogWarning("[Shop] no IAP product for " + coins + " coins"); return; }
            if (IAPManager.Instance != null) IAPManager.Instance.Buy(id);
            else Debug.LogWarning("[Shop] IAP not ready yet");
        }

        // IAPManager fires OnChanged after a verified purchase / restore / first init -> repaint the live counters.
        void OnIapChanged()
        {
            SetCoins(SaveSystem.Coins);
            RefreshJokers();
            if (shopPanel != null) MapShopCoinButtons(shopPanel.transform, false); // refresh amounts/prices once IAP is ready (no re-wire)
        }

        void OnDestroy() { IAPManager.OnChanged -= OnIapChanged; }

        // Placeholder coin-pack prices shown until the REAL localized Play price loads (in the editor, before IAP
        // initialises, or before the products are Active in Play Console — so the labels are never blank). Index =
        // CoinPacks sorted ascending. On a device with active products, IAPManager.Price overrides these per-region.
        static readonly string[] FallbackPrices = { "$0.99", "$1.99", "$4.99", "$8.99", "$12.00", "$17.99" };

        // Map the baked coin-pack cards ("Pack_<amount>", used by BOTH the in-game and menu shop bakers) onto the REAL
        // IAP CoinPacks, smallest->smallest, so the displayed amount, the price, and the product actually purchased always
        // match IAPManager — whatever amounts the shop was baked with, and with NO re-bake. Change CoinPacks and the shops
        // follow. Card layout (both bakers): a card named "Pack_N" -> child "Amount" (count) + child "Buy" -> child "Price".
        // wireClicks=true on first setup (adds the Buy listener once); false to only refresh labels when IAP initialises.
        public static void MapShopCoinButtons(Transform shopRoot, bool wireClicks)
        {
            if (shopRoot == null) return;
            var cards = new System.Collections.Generic.List<(int baked, Transform card, Button buy)>();
            foreach (var t in shopRoot.GetComponentsInChildren<Transform>(true))
            {
                if (t == null || !t.name.StartsWith("Pack_")) continue;
                if (!int.TryParse(t.name.Substring(5), out int baked)) continue;
                var buyT = t.Find("Buy");
                var buy = buyT != null ? buyT.GetComponent<Button>() : null;
                if (buy != null) cards.Add((baked, t, buy));
            }
            if (cards.Count == 0) return;
            cards.Sort((a, b) => a.baked.CompareTo(b.baked));
            var packs = new System.Collections.Generic.List<(string id, int coins)>(IAPManager.CoinPacks);
            packs.Sort((a, b) => a.coins.CompareTo(b.coins));
            for (int i = 0; i < cards.Count && i < packs.Count; i++)
            {
                var card = cards[i].card; var buy = cards[i].buy; var pack = packs[i];
                var amtT = card.Find("Amount");
                if (amtT != null) { var lt = amtT.GetComponent<Text>(); if (lt != null) lt.text = pack.coins.ToString(); }
                var priceTf = buy.transform.Find("Price");
                if (priceTf != null)
                {
                    var lt = priceTf.GetComponent<Text>();
                    string pr = null;
#if !UNITY_EDITOR
                    pr = IAPManager.Instance != null ? IAPManager.Instance.Price(pack.id) : null; // real localized Play price (device only)
#endif
                    // Editor uses Unity's FAKE store ("$0.01" for everything) — ignore it and show the fixed placeholder.
                    if (string.IsNullOrEmpty(pr) && i < FallbackPrices.Length) pr = FallbackPrices[i];
                    if (lt != null && !string.IsNullOrEmpty(pr)) lt.text = pr;
                }
                if (wireClicks) { var id = pack.id; buy.onClick.AddListener(() => IAPManager.Instance?.Buy(id)); }
            }
        }

        // ---- In-game shop (coin tap) — identical to the main-menu shop -------
        // Code fallback used only when no baked shop exists. Mirrors the baker:
        // dim backdrop + tall card + scrollable list (Remove-Ads → gold grid → jokers).
        void BuildShop()
        {
            shopPanel = Panel("Shop", new Color(0, 0, 0, 0.6f));

            var card = Img(shopPanel.transform, UIKit.PanelTall(), new Color(0.30f, 0.25f, 0.55f));
            Center(card.rectTransform, new Vector2(960, 1500));
            Label(card.transform, "SHOP", title, new Vector2(0, 680), new Vector2(700, 120), 74, White);
            RedClose(card.transform, HideShop);

            // ---- Scroll view ----
            var svGo = new GameObject("ScrollView", typeof(RectTransform));
            svGo.transform.SetParent(card.transform, false);
            var svRt = svGo.GetComponent<RectTransform>();
            svRt.anchorMin = svRt.anchorMax = svRt.pivot = new Vector2(0.5f, 0.5f);
            svRt.anchoredPosition = new Vector2(0, 20); svRt.sizeDelta = new Vector2(880, 1120);
            var scroll = svGo.AddComponent<ScrollRect>();
            scroll.horizontal = false; scroll.vertical = true;
            scroll.movementType = ScrollRect.MovementType.Elastic; scroll.scrollSensitivity = 28;

            var vpGo = new GameObject("Viewport", typeof(RectTransform));
            vpGo.transform.SetParent(svGo.transform, false);
            var vpRt = vpGo.GetComponent<RectTransform>();
            vpRt.anchorMin = Vector2.zero; vpRt.anchorMax = Vector2.one; vpRt.offsetMin = Vector2.zero; vpRt.offsetMax = Vector2.zero;
            var vpImg = vpGo.AddComponent<Image>(); vpImg.color = new Color(1, 1, 1, 0.01f); // catches drags over empty space
            vpGo.AddComponent<RectMask2D>();

            var ctGo = new GameObject("Content", typeof(RectTransform));
            ctGo.transform.SetParent(vpGo.transform, false);
            var ctRt = ctGo.GetComponent<RectTransform>();
            ctRt.anchorMin = new Vector2(0, 1); ctRt.anchorMax = new Vector2(1, 1); ctRt.pivot = new Vector2(0.5f, 1);
            ctRt.anchoredPosition = Vector2.zero; ctRt.sizeDelta = Vector2.zero;
            var vlg = ctGo.AddComponent<VerticalLayoutGroup>();
            vlg.spacing = 24; vlg.padding = new RectOffset(15, 15, 15, 15);
            vlg.childAlignment = TextAnchor.UpperCenter;
            vlg.childControlWidth = true; vlg.childControlHeight = true;
            vlg.childForceExpandWidth = true; vlg.childForceExpandHeight = false;
            var fit = ctGo.AddComponent<ContentSizeFitter>();
            fit.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            fit.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
            scroll.viewport = vpRt; scroll.content = ctRt;

            // 1) Remove-ads bar (atlas1_44 bg, no-ads icon left, price button right).
            var adsRow = Img(ctGo.transform, UIKit.ShopBoxA(), new Color(0.95f, 0.55f, 0.20f));
            var adsLe = adsRow.gameObject.AddComponent<LayoutElement>(); adsLe.preferredHeight = 160; adsLe.minHeight = 160;
            var adsIco = Img(adsRow.transform, UIKit.NoAds(), new Color(0.85f, 0.3f, 0.3f)); adsIco.raycastTarget = false;
            Place(adsIco.rectTransform, new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(95, 0), new Vector2(110, 110));
            var adsPrice = Img(adsRow.transform, UIKit.PriceBtnA(), new Color(0.3f, 0.75f, 0.35f)); adsPrice.raycastTarget = false;
            Place(adsPrice.rectTransform, new Vector2(1, 0.5f), new Vector2(1, 0.5f), new Vector2(-210, 0), new Vector2(360, 110));
            var adsRealPrice = IAPManager.Instance != null ? IAPManager.Instance.Price(IAPManager.RemoveAds) : null;
            Label(adsPrice.transform, string.IsNullOrEmpty(adsRealPrice) ? "TRY 249,99" : adsRealPrice, num, Vector2.zero, new Vector2(360, 60), 36, White);
            var adsBtn = adsRow.gameObject.AddComponent<Button>(); adsBtn.targetGraphic = adsRow; // whole bar buys remove_ads
            adsBtn.onClick.AddListener(RemoveAds);

            // 2) Gold packs (3-column grid, icons 11,12,13,29,30,31).
            var gridGo = new GameObject("CoinGrid", typeof(RectTransform));
            gridGo.transform.SetParent(ctGo.transform, false);
            var gl = gridGo.AddComponent<GridLayoutGroup>();
            gl.cellSize = new Vector2(275, 360); gl.spacing = new Vector2(15, 20);
            gl.childAlignment = TextAnchor.UpperCenter;
            gl.constraint = GridLayoutGroup.Constraint.FixedColumnCount; gl.constraintCount = 3;
            ShopCoinCard(gridGo.transform, UIKit.ShopCoinA(),     "200",   "$0.99",   200);
            ShopCoinCard(gridGo.transform, UIKit.ShopCoinB(),     "500",   "$1.99",   500);
            ShopCoinCard(gridGo.transform, UIKit.ShopCoinC(),     "1300",  "$4.99",   1300);
            ShopCoinCard(gridGo.transform, UIKit.ShopGold(),      "2500",  "$8.99",   2500);
            ShopCoinCard(gridGo.transform, UIKit.CoinPackSmall(), "4000",  "$12.00",  4000);
            ShopCoinCard(gridGo.transform, UIKit.CoinPackBig(),   "5500",  "$17.99",  5500);

            // 3) Joker bars (atlas1_44 bg, icon left, buy for 100 gold).
            ShopJokerBar(ctGo.transform, UIKit.JokerRecolor());
            ShopJokerBar(ctGo.transform, UIKit.JokerSwap());
            ShopJokerBar(ctGo.transform, UIKit.JokerHeli());

            // Restore Purchases (Google Play storefront requirement): last row in the list.
            AddShopRestoreRow(ctGo.transform);

            shopPanel.SetActive(false);
        }

        // One purple coin-pack card: coin icon + amount + green price button (grants coins).
        void ShopCoinCard(Transform parent, Sprite icon, string amount, string price, int coins)
        {
            var card = Img(parent, UIKit.ShopIconBgA(), new Color(0.55f, 0.40f, 0.78f));
            var ico = Img(card.transform, icon, Gold); ico.raycastTarget = false;
            Place(ico.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0, 40), new Vector2(150, 150));
            Label(card.transform, amount, num, new Vector2(0, 132), new Vector2(255, 50), 34, White);
            var buy = Btn(card.transform, UIKit.PriceBtnA(), new Color(0.3f, 0.75f, 0.35f), new Vector2(0.5f, 0), new Vector2(0, 22), new Vector2(245, 92),
                () => BuyCoins(coins)); // real IAP; coins granted by IAPManager on success
            // Real localized Play price on a device; the editor's FAKE store returns "$0.01", so ignore it there and
            // show the fixed `price` placeholder ($0.99 etc.).
            string realPrice = null;
#if !UNITY_EDITOR
            realPrice = IAPManager.Instance != null ? IAPManager.Instance.Price(IAPManager.ProductForCoins(coins)) : null;
#endif
            Label(buy.transform, string.IsNullOrEmpty(realPrice) ? price : realPrice, num, Vector2.zero, new Vector2(245, 56), 32, White);
        }

        // A full-width joker bar: icon on the dark-orange left + a "100 gold" buy button.
        void ShopJokerBar(Transform parent, Sprite icon)
        {
            var row = Img(parent, UIKit.ShopBoxA(), new Color(0.95f, 0.55f, 0.20f));
            var le = row.gameObject.AddComponent<LayoutElement>(); le.preferredHeight = 160; le.minHeight = 160;
            var ico = Img(row.transform, icon, White); ico.raycastTarget = false;
            Place(ico.rectTransform, new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(110, 0), new Vector2(120, 120));
            var buy = Btn(row.transform, UIKit.PriceBtnA(), new Color(0.3f, 0.75f, 0.35f), new Vector2(1, 0.5f), new Vector2(-210, 0), new Vector2(360, 110),
                () => { if (SaveSystem.TrySpend(100)) SetCoins(SaveSystem.Coins); });
            var bc = Img(buy.transform, UIKit.Coin(), Gold); bc.raycastTarget = false;
            Place(bc.rectTransform, new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(45, 0), new Vector2(56, 56));
            Label(buy.transform, "100", num, new Vector2(30, 0), new Vector2(360, 60), 36, White);
        }

        // ---- Continue panel (56/57 buttons; ad icon atlas1_61) ---------------
        void BuildContinue()
        {
            continuePanel = Panel("Continue", Dim);
            var card = Img(continuePanel.transform, UIKit.EmptyBoxBlue(), new Color(0.25f, 0.55f, 0.90f));
            Center(card.rectTransform, new Vector2(820, 1000));
            Label(card.transform, "CONTINUE?", title, new Vector2(0, 360), new Vector2(700, 100), 62, White);

            var ad = Btn(card.transform, UIKit.ShopIconBgA(), new Color(0.3f, 0.75f, 0.35f), new Vector2(0.5f, 0.5f), new Vector2(0, 60), new Vector2(580, 160), () => OnContinueAd?.Invoke());
            var adi = Img(ad.transform, UIKit.WatchAd(), new Color(0.5f, 0.7f, 0.9f)); adi.raycastTarget = false;
            Place(adi.rectTransform, new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(85, 0), new Vector2(95, 95));
            Label(ad.transform, "WATCH AD", title, new Vector2(45, 0), new Vector2(420, 70), 40, White);

            // (Continue screen) REPLAY button in place of the old pay-150-gold continue — restarts the level.
            var replay = Btn(card.transform, UIKit.ShopIconBgB(), new Color(0.30f, 0.62f, 0.92f), new Vector2(0.5f, 0.5f), new Vector2(0, -150), new Vector2(580, 160), () => { HideContinue(); OnReplay?.Invoke(); });
            Label(replay.transform, Loc.T("REPLAY"), title, Vector2.zero, new Vector2(520, 80), 52, White);

            RedClose(card.transform, () => { HideContinue(); OnContinueDeclined?.Invoke(); });
            continuePanel.SetActive(false);
        }

        // ---- Failed panel (title tile atlas1_50; 56/57 buttons) --------------
        void BuildFailed()
        {
            failedPanel = Panel("Failed", Dim);
            var card = Img(failedPanel.transform, UIKit.EmptyBoxBlue(), new Color(0.25f, 0.55f, 0.90f));
            Center(card.rectTransform, new Vector2(820, 1000));

            var tile = Img(card.transform, UIKit.TitleBarB(), new Color(0.85f, 0.2f, 0.2f)); tile.raycastTarget = false;
            Place(tile.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0, 360), new Vector2(560, 150));
            Label(card.transform, "FAIL", title, new Vector2(0, 360), new Vector2(540, 110), 72, White);

            var home = Btn(card.transform, UIKit.ShopIconBgA(), new Color(0.4f, 0.8f, 0.45f), new Vector2(0.5f, 0.5f), new Vector2(-170, -100), new Vector2(300, 170),
                () => { HideFailed(); OnHome?.Invoke(); });
            Label(home.transform, "HOME", title, Vector2.zero, new Vector2(300, 90), 40, White);
            var replay = Btn(card.transform, UIKit.ShopIconBgB(), new Color(0.95f, 0.6f, 0.25f), new Vector2(0.5f, 0.5f), new Vector2(170, -100), new Vector2(300, 170),
                () => { HideFailed(); OnReplay?.Invoke(); });
            Label(replay.transform, "REPLAY", title, Vector2.zero, new Vector2(300, 90), 38, White);
            failedPanel.SetActive(false);
        }

        // ---- Success / achievement (title tile atlas1_53; 56/57 buttons) -----
        void BuildSuccess()
        {
            successPanel = Panel("Success", new Color(0, 0, 0, 0.65f));
            var card = Img(successPanel.transform, UIKit.EmptyBoxBlue(), new Color(0.25f, 0.55f, 0.90f));
            Center(card.rectTransform, new Vector2(820, 1000));

            var tile = Img(card.transform, UIKit.TitleBarC(), new Color(0.30f, 0.65f, 0.95f)); tile.raycastTarget = false;
            Place(tile.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0, 380), new Vector2(660, 150));
            Label(card.transform, "ACHIEVEMENT", title, new Vector2(0, 380), new Vector2(640, 110), 56, White);

            // Reward coin in the center of the box.
            var rc = Img(card.transform, UIKit.Coin(), Gold); rc.raycastTarget = false;
            Center(rc.rectTransform, new Vector2(180, 180)); rc.rectTransform.anchoredPosition = new Vector2(0, 130);
            successReward = Label(card.transform, "+20", title, new Vector2(0, -10), new Vector2(600, 90), 64, Gold);

            var next = Btn(card.transform, UIKit.ShopIconBgA(), new Color(0.3f, 0.75f, 0.35f), new Vector2(0.5f, 0.5f), new Vector2(0, -180), new Vector2(580, 150), () => ClaimReward(20));
            Label(next.transform, "NEXT", title, Vector2.zero, new Vector2(580, 90), 46, White);

            var ad = Btn(card.transform, UIKit.ShopIconBgB(), new Color(0.95f, 0.6f, 0.25f), new Vector2(0.5f, 0.5f), new Vector2(0, -345), new Vector2(580, 145), () => ClaimReward(40));
            var adi = Img(ad.transform, UIKit.WatchAd(), new Color(0.5f, 0.7f, 0.9f)); adi.raycastTarget = false;
            Place(adi.rectTransform, new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(80, 0), new Vector2(85, 85));
            Label(ad.transform, "AD  x2", title, new Vector2(40, 0), new Vector2(440, 70), 40, White);
            successPanel.SetActive(false);
        }

        void ClaimReward(int amount)
        {
            successPanel.SetActive(false);
            // AD ×2 (40) must be EARNED via a rewarded ad; base NEXT (20) is instant. Skip/close/no-ad -> base 20. (T5)
            var ad = AdManager.Instance;
            if (amount >= 40 && ad != null)
                ad.ShowRewarded("doublecoins", onReward: () => OnClaimReward?.Invoke(amount), onClosedNoReward: () => OnClaimReward?.Invoke(20));
            else
                OnClaimReward?.Invoke(amount);
        }

        public static void Vibrate()
        {
            if (!SaveSystem.Vibration) return;
#if UNITY_ANDROID || UNITY_IOS
            Handheld.Vibrate();
#endif
        }

        // ---- API ------------------------------------------------------------
        public void ShowHud() { Toggle(hudPanel, true); SetHudChromeVisible(true); }
        public void HideHud() { Toggle(hudPanel, false); }
        public void ShowSettings() { Toggle(settingsPanel, true); }
        public void HideSettings() { Toggle(settingsPanel, false); }
        public void ShowShop() { SetHudChromeVisible(false); Toggle(shopPanel, true); } // (#6) hide gear/level/ad/jokers
        public void HideShop() { Toggle(shopPanel, false); SetHudChromeVisible(true); } // restore them when it closes

        // (#6) While the shop is open, hide the gear, level badge, +coins(ad) button and the 3 jokers — leaving ONLY
        // the gold/coin bar visible. Restored when the shop closes (or whenever the HUD is (re)shown).
        void SetHudChromeVisible(bool on)
        {
            if (jRecolor.btn) jRecolor.btn.gameObject.SetActive(on);
            if (jSwap.btn)    jSwap.btn.gameObject.SetActive(on);
            if (jHeli.btn)    jHeli.btn.gameObject.SetActive(on);
            if (gearGo)       gearGo.SetActive(on);
            if (levelBadgeGo) levelBadgeGo.SetActive(on);
            if (adFreeBtnGo)  adFreeBtnGo.SetActive(on);
            if (garageBtnGo)  garageBtnGo.SetActive(on); // hide the GARAGE button too while the garage/shop is open
            if (hudTheme)     hudTheme.gameObject.SetActive(on); // theme-name label (top-left) — was left floating over the open garage/shop
        }
        public void ShowContinue() { Toggle(continuePanel, true); }
        public void SetContinuePrice(int cost) { if (continuePrice) continuePrice.text = cost.ToString(); }
        public void HideContinue() { Toggle(continuePanel, false); }
        public void ShowFailed() { Toggle(failedPanel, true); }
        public void HideFailed() { Toggle(failedPanel, false); }
        public void ShowSuccess(int stars, int reward)
        {
            if (successReward) successReward.text = "+" + reward;   // show the actual coin reward (economy rework)
            Toggle(successPanel, true);
        }
        public void HideSuccess() { Toggle(successPanel, false); }

        // True while any modal pop-up (settings / shop / continue / failed / success) is open. BusJamGame uses
        // this to hide the tutorial coach so nothing tutorial-related shows on top of a panel.
        public bool AnyPanelOpen() =>
            IsShown(settingsPanel) || IsShown(shopPanel) || IsShown(continuePanel) || IsShown(failedPanel) || IsShown(successPanel);
        static bool IsShown(GameObject g) => g != null && g.activeInHierarchy;

        public void SetCoins(int c) { if (hudCoins) hudCoins.text = c.ToString(); }
        public void SetLevel(int l) { if (hudLevel) hudLevel.text = l.ToString(); }

        /// <summary>Re-evaluate the joker lock overlays against the player's
        /// progression (SaveSystem.Level), so RECOLOR/SWAP/HELI unlock as it rises —
        /// even when replaying an earlier level.</summary>
        public void RefreshJokerLocks()
        {
            level = SaveSystem.Level;
            RefreshJokers();
        }

        // (#6) Screen position of a joker button, for the unlock-coach pointer. kind: 0=Recolor,1=Swap,2=Heli.
        public Vector2 JokerScreenPos(int kind)
        {
            Joker j = kind == 1 ? jSwap : kind == 2 ? jHeli : jRecolor;
            if (j.btn == null) return new Vector2(Screen.width * 0.5f, Screen.height * 0.12f);
            var canvas = j.btn.GetComponentInParent<Canvas>();
            Camera uiCam = (canvas != null && canvas.renderMode != RenderMode.ScreenSpaceOverlay) ? canvas.worldCamera : null;
            return RectTransformUtility.WorldToScreenPoint(uiCam, j.btn.transform.position);
        }
        public void SetTheme(string t) { if (hudTheme) hudTheme.text = t; }

        public void ShowCombo(int combo)
        {
            if (!comboText) return;
            comboText.gameObject.SetActive(true);
            comboText.text = Loc.Format("COMBO x{0}!", combo);
            CancelInvoke(nameof(ClearCombo));
            Invoke(nameof(ClearCombo), 0.8f);
        }
        void ClearCombo() { if (comboText) comboText.gameObject.SetActive(false); }

        // Bonus-only countdown (top-center mm:ss). Uses the COIN number font (num = Oswald-Bold) so it matches the
        // rest of the HUD numbers, sits a bit higher on screen, and shifts green -> orange -> red as it runs down.
        // Built on BOTH HUD paths; lives on hudPanel, so HideHud auto-hides it at the bonus end.
        void BuildBonusCountdown()
        {
            if (hudPanel == null || bonusCountdown != null) return;
            bonusCountdown = Label(hudPanel.transform, "", num, new Vector2(0, 700), new Vector2(420, 130), 96, White);
            bonusCountdown.gameObject.SetActive(false);
            // NOTE: the red/green traffic light is now a real in-world prop on both road sides (BusJamGame.BuildTrafficLights),
            // not a HUD widget — so the player reads stop/go straight off the road.
        }
        // TimeAttack count-UP stopwatch (reuses the bonus label): shows elapsed m:ss coloured by PACE, so the player
        // sees the chest they're earning — green (<25s = Gold) -> orange (<45s = Silver) -> red (Bronze).
        public void SetBonusStopwatch(float elapsed)
        {
            if (!bonusCountdown) return;
            if (hideBonusTimer) { if (bonusCountdown.gameObject.activeSelf) bonusCountdown.gameObject.SetActive(false); return; } // (#2) suppressed while the garage is open
            if (!bonusCountdown.gameObject.activeSelf) bonusCountdown.gameObject.SetActive(true);
            int s = Mathf.Max(0, Mathf.FloorToInt(elapsed));
            bonusCountdown.text = (s / 60) + ":" + (s % 60).ToString("00");
            Color green = new Color(0.36f, 0.92f, 0.45f), orange = new Color(1f, 0.66f, 0.16f), red = new Color(1f, 0.32f, 0.28f);
            bonusCountdown.color = elapsed < 25f ? green : elapsed < 45f ? Color.Lerp(green, orange, (elapsed - 25f) / 20f) : Color.Lerp(orange, red, Mathf.Clamp01((elapsed - 45f) / 20f));
            bonusCountdown.transform.localScale = Vector3.one; // clear any leftover countdown pulse
        }

        public void SetBonusCountdown(float seconds)
        {
            if (!bonusCountdown) return;
            if (hideBonusTimer) { if (bonusCountdown.gameObject.activeSelf) bonusCountdown.gameObject.SetActive(false); return; } // (#2) suppressed while the garage is open
            if (!bonusCountdown.gameObject.activeSelf) bonusCountdown.gameObject.SetActive(true);
            int s = Mathf.Max(0, Mathf.CeilToInt(seconds));
            bonusCountdown.text = (s / 60) + ":" + (s % 60).ToString("00");

            // Colour ramps down with the clock: green (plenty) -> orange (getting low) -> red (urgent).
            Color green = new Color(0.36f, 0.92f, 0.45f), orange = new Color(1f, 0.66f, 0.16f), red = new Color(1f, 0.32f, 0.28f);
            Color c;
            if      (seconds > 60f) c = green;
            else if (seconds > 30f) c = Color.Lerp(orange, green, (seconds - 30f) / 30f); // 30..60s: orange -> green
            else if (seconds > 10f) c = Color.Lerp(red, orange, (seconds - 10f) / 20f);   // 10..30s: red -> orange
            else                    c = red;
            bonusCountdown.color = c;

            // Heartbeat pulse in the last 10s for urgency (called every frame from the timer tick, so it animates).
            float pulse = seconds <= 10f ? 1f + 0.12f * Mathf.Abs(Mathf.Sin(Time.unscaledTime * 6f)) : 1f;
            bonusCountdown.rectTransform.localScale = Vector3.one * pulse;
        }
        public void HideBonusCountdown()
        {
            if (bonusCountdown) { bonusCountdown.gameObject.SetActive(false); bonusCountdown.rectTransform.localScale = Vector3.one; }
        }

        // Combo reward feedback: a green "+Ns" that floats up just under the timer and fades. Called when the player
        // chains enough crash-free bus sends on a bonus level.
        public void ShowTimeBonus(int sec)
        {
            if (hudPanel == null) return;
            var t = Label(hudPanel.transform, "+" + sec + "s", num, new Vector2(0, 580), new Vector2(320, 80), 60, new Color(0.40f, 1f, 0.50f));
            StartCoroutine(FloatAndFade(t, new Vector2(0, 580), new Vector2(0, 660), 0.9f)); // rise UP toward the timer + fade
        }
        IEnumerator FloatAndFade(Text t, Vector2 from, Vector2 to, float dur)
        {
            float e = 0f; Color c0 = t.color;
            while (t != null && e < dur)
            {
                e += Time.unscaledDeltaTime; float k = Mathf.Clamp01(e / dur);
                t.rectTransform.anchoredPosition = Vector2.Lerp(from, to, k);
                t.color = new Color(c0.r, c0.g, c0.b, 1f - k);
                yield return null;
            }
            if (t != null) Destroy(t.gameObject);
        }

        // ---- Builders -------------------------------------------------------
        GameObject Panel(string name, Color bg)
        {
            var img = Img(root, null, bg);
            img.gameObject.name = name;
            Stretch(img.rectTransform);
            return img.gameObject;
        }

        Image Img(Transform parent, Sprite sprite, Color fallback)
        {
            var go = new GameObject("Img", typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var img = go.AddComponent<Image>();
            if (sprite != null) { img.sprite = sprite; img.color = White; }
            else img.color = fallback;
            return img;
        }

        Text Label(Transform parent, string text, Font font, Vector2 pos, Vector2 size, int fontSize, Color color, TextAnchor align = TextAnchor.MiddleCenter)
        {
            var go = new GameObject("Text", typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var t = go.AddComponent<Text>();
            t.font = font; t.text = text; t.fontSize = fontSize; t.color = color; t.alignment = align;
            t.horizontalOverflow = HorizontalWrapMode.Overflow; t.verticalOverflow = VerticalWrapMode.Overflow;
            t.raycastTarget = false;
            var sh = go.AddComponent<Shadow>(); sh.effectColor = new Color(0, 0, 0, 0.4f); sh.effectDistance = new Vector2(2, -2);
            var rt = t.rectTransform;
            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = pos; rt.sizeDelta = size;
            return t;
        }

        Button Btn(Transform parent, Sprite sprite, Color fallback, Vector2 anchor, Vector2 pos, Vector2 size, System.Action onClick)
        {
            var img = Img(parent, sprite, fallback);
            var rt = img.rectTransform;
            rt.anchorMin = rt.anchorMax = anchor; rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = pos; rt.sizeDelta = size;
            var btn = img.gameObject.AddComponent<Button>();
            btn.targetGraphic = img;
            if (onClick != null) btn.onClick.AddListener(() => onClick());
            return btn;
        }

        // Returns the button so callers that BAKE the chrome (garage/vehicles) can store the ref and wire the action
        // at runtime; pass onClose = null to create it unwired. Existing callers ignore the return value.
        Button RedClose(Transform card, System.Action onClose)
        {
            var b = Btn(card, UIKit.CloseX(), new Color(0.85f, 0.2f, 0.2f), new Vector2(1, 1), new Vector2(-40, -40), new Vector2(96, 96), onClose);
            b.transform.SetAsLastSibling();
            return b;
        }

        void Place(RectTransform rt, Vector2 min, Vector2 max, Vector2 pos, Vector2 size)
        { rt.anchorMin = min; rt.anchorMax = max; rt.pivot = new Vector2(0.5f, 0.5f); rt.anchoredPosition = pos; rt.sizeDelta = size; }
        void Center(RectTransform rt, Vector2 size)
        { rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f); rt.anchoredPosition = Vector2.zero; rt.sizeDelta = size; }
        void Stretch(RectTransform rt)
        { rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one; rt.offsetMin = Vector2.zero; rt.offsetMax = Vector2.zero; }
        void Toggle(GameObject go, bool on) { if (go) { go.SetActive(on); if (on) Localizer.LocalizeScene(); } } // re-localize when shown -> inactive-built panels translate on open
    }
}
