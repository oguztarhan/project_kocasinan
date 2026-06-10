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
    public class GameUI : MonoBehaviour
    {
        public System.Action OnMenu, OnRecolor, OnSwap, OnHeli;
        public System.Action OnHome, OnReplay;
        public System.Action<int> OnClaimReward;
        public System.Action OnContinueAd, OnContinuePay, OnContinueDeclined;

        static readonly Color White = Color.white;
        static readonly Color Gold  = new Color(1f, 0.85f, 0.30f);
        static readonly Color Dark  = new Color(0.16f, 0.20f, 0.30f);
        static readonly Color Dim   = new Color(0, 0, 0, 0.6f);
        static readonly Color OnCol = new Color(0.35f, 0.85f, 0.40f);
        static readonly Color OffCol= new Color(0.65f, 0.65f, 0.70f);

        Font title, num;
        Transform root;
        GameObject hudPanel, settingsPanel, successPanel, continuePanel, failedPanel, shopPanel;
        Text hudCoins, hudLevel, hudTheme, comboText, hudPeopleLeft, successReward;

        struct Joker { public Button btn; public GameObject lockGo; public int unlock; }
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
            sc.matchWidthOrHeight = 0.5f;
            canvasGo.AddComponent<GraphicRaycaster>();
            root = canvasGo.transform;

            if (EventSystem.current == null)
            {
                var es = new GameObject("EventSystem");
                es.transform.SetParent(transform, false);
                es.AddComponent<EventSystem>();
                es.AddComponent<InputSystemUIInputModule>().AssignDefaultActions();
            }

            BuildHud(recolorCost, swapCost, heliCost, j1Lvl, j2Lvl, j3Lvl);
            BuildSettings();
            SetupShop();
            BuildContinue();
            BuildFailed();
            BuildSuccess();
            ShowHud();
            DisableOldCanvases(); // hide legacy scene canvas (white bg / old coin / texts)
        }

        // Hide every canvas that doesn't belong to this game object's hierarchy
        // (runtime only). LevelSelect/GameUI canvases live under the same root, so
        // they survive; the legacy scene canvas does not.
        void DisableOldCanvases()
        {
            if (!Application.isPlaying) return;
            var shopCanvas = InGameShop.Instance != null ? InGameShop.Instance.GetComponent<Canvas>() : null;
            foreach (var c in Object.FindObjectsByType<Canvas>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
            {
                if (c == null) continue;
                if (c.transform.root == transform.root) continue; // ours
                if (shopCanvas != null && c == shopCanvas) continue; // baked in-game shop
                c.gameObject.SetActive(false);
            }
        }

        // ---- HUD ------------------------------------------------------------
        void BuildHud(int recolorCost, int swapCost, int heliCost, int j1Lvl, int j2Lvl, int j3Lvl)
        {
            hudPanel = Panel("Hud", new Color(0, 0, 0, 0));
            hudPanel.GetComponent<Image>().raycastTarget = false;

            // LEVEL badge: TOP-LEFT, round yellow circle (atlas1_19).
            var badge = Img(hudPanel.transform, UIKit.CircleYellow(), new Color(0.95f, 0.78f, 0.30f));
            Place(badge.rectTransform, new Vector2(0, 1), new Vector2(0, 1), new Vector2(110, -110), new Vector2(170, 170));
            badge.raycastTarget = false;
            Label(badge.transform, "LEVEL", num, new Vector2(0, 42), new Vector2(160, 36), 24, Dark);
            hudLevel = Label(badge.transform, "1", title, new Vector2(0, -16), new Vector2(160, 90), 64, Dark);
            hudTheme = Label(hudPanel.transform, "", num, new Vector2(110, -210), new Vector2(260, 36), 22, new Color(0.85f, 0.9f, 1f));
            hudTheme.rectTransform.anchorMin = hudTheme.rectTransform.anchorMax = new Vector2(0, 1);
            hudTheme.rectTransform.anchoredPosition = new Vector2(110, -210);

            // COIN display: TOP-CENTER (atlas1_20 bar), opens the in-game shop.
            var coinBtn = Btn(hudPanel.transform, UIKit.CoinBar(), Dark, new Vector2(0.5f, 1), new Vector2(0, -100), new Vector2(300, 96), ShowShop);
            var ci = Img(coinBtn.transform, UIKit.Coin(), Gold); ci.raycastTarget = false;
            Place(ci.rectTransform, new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(42, 0), new Vector2(74, 74));
            hudCoins = Label(coinBtn.transform, "0", num, new Vector2(35, 0), new Vector2(180, 60), 44, White);

            // SETTINGS gear: TOP-RIGHT.
            Btn(hudPanel.transform, UIKit.Gear(), new Color(0.7f, 0.72f, 0.78f), new Vector2(1, 1), new Vector2(-90, -100), new Vector2(120, 120), ShowSettings);

            // PEOPLE-LEFT badge: left margin, round green circle (atlas1_18).
            var pBadge = Img(hudPanel.transform, UIKit.CircleGreen(), new Color(0.35f, 0.70f, 0.40f));
            Place(pBadge.rectTransform, new Vector2(0, 1), new Vector2(0, 1), new Vector2(105, -440), new Vector2(140, 140));
            pBadge.raycastTarget = false;
            var pIco = Img(pBadge.transform, UISprites.Person(), White); pIco.raycastTarget = false;
            Center(pIco.rectTransform, new Vector2(66, 66)); pIco.rectTransform.anchoredPosition = new Vector2(0, 16);
            hudPeopleLeft = Label(pBadge.transform, "0", num, new Vector2(0, -38), new Vector2(140, 50), 36, White);

            comboText = Label(hudPanel.transform, "", title, new Vector2(0, 360), new Vector2(900, 100), 70, Gold);
            comboText.gameObject.SetActive(false);

            // 3 jokers across the bottom on atlas1_56/57 boxes: RECOLOR / SWAP / HELI.
            jRecolor = JokerButton(-260, recolorCost.ToString(), UIKit.JokerRecolor(), UIKit.ShopIconBgA(), j1Lvl, () => OnRecolor?.Invoke());
            jSwap    = JokerButton(0,    swapCost.ToString(),    UIKit.JokerSwap(),    UIKit.ShopIconBgB(), j2Lvl, () => OnSwap?.Invoke());
            jHeli    = JokerButton(260,  heliCost.ToString(),    UIKit.JokerHeli(),    UIKit.ShopIconBgA(), j3Lvl, () => OnHeli?.Invoke());
            RefreshJokers();
        }

        Joker JokerButton(float x, string costText, Sprite icon, Sprite boxBg, int unlock, System.Action onClick)
        {
            var btn = Btn(hudPanel.transform, boxBg, new Color(0.30f, 0.45f, 0.70f), new Vector2(0.5f, 0), new Vector2(x, 70), new Vector2(180, 180), onClick);
            var rt = btn.GetComponent<RectTransform>();
            rt.anchorMin = new Vector2(0.5f, 0); rt.anchorMax = new Vector2(0.5f, 0); rt.pivot = new Vector2(0.5f, 0);
            rt.anchoredPosition = new Vector2(x, 70);
            var ico = Img(btn.transform, icon, new Color(0.9f, 0.9f, 1f)); ico.raycastTarget = false;
            Center(ico.rectTransform, new Vector2(110, 110)); ico.rectTransform.anchoredPosition = new Vector2(0, 12);
            Label(btn.transform, costText, num, new Vector2(0, -64), new Vector2(170, 44), 30, White);
            var lk = Img(btn.transform, null, new Color(0, 0, 0, 0.55f)); lk.raycastTarget = false;
            Center(lk.rectTransform, new Vector2(180, 180));
            Label(lk.transform, "LV " + unlock, num, Vector2.zero, new Vector2(170, 60), 34, White);
            return new Joker { btn = btn, lockGo = lk.gameObject, unlock = unlock };
        }

        void RefreshJokers() { SetJoker(jRecolor); SetJoker(jSwap); SetJoker(jHeli); }
        void SetJoker(Joker j)
        {
            if (j.btn == null) return;
            bool unlocked = level >= j.unlock;
            j.btn.interactable = unlocked;
            if (j.lockGo) j.lockGo.SetActive(!unlocked);
        }

        // ---- Settings (atlas2_0 WHITE panel; blue title tile; 18-icon toggles; 36 buttons) ----
        void BuildSettings()
        {
            settingsPanel = Panel("Settings", Dim);

            // Panel background = atlas2_0, tinted WHITE.
            var card = Img(settingsPanel.transform, UIKit.EmptyBoxBlue(), White);
            card.color = White;
            Center(card.rectTransform, new Vector2(820, 1000));

            // Blue TEXT TILE on top of the rectangle, with the title on it.
            var tile = Img(card.transform, UIKit.TitleBarA(), new Color(0.25f, 0.55f, 0.90f));
            tile.color = new Color(0.25f, 0.55f, 0.90f); tile.raycastTarget = false;
            Place(tile.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0, 410), new Vector2(580, 130));
            Label(card.transform, "SETTINGS", title, new Vector2(0, 410), new Vector2(560, 100), 56, White);

            // SOUND / MUSIC: tap to toggle. Button image = atlas1_36, on/off icon = atlas1_18.
            ToggleRow(card.transform, 210, "SOUND", SaveSystem.Sound, v => SaveSystem.Sound = v);
            ToggleRow(card.transform, 60,  "MUSIC", SaveSystem.Music, v => SaveSystem.Music = v);

            // HOME + REPLAY: ALL settings buttons use atlas1_36.
            var home = Btn(card.transform, UIKit.PriceBtnA(), new Color(0.4f, 0.8f, 0.45f), new Vector2(0.5f, 0.5f), new Vector2(-180, -160), new Vector2(310, 115),
                () => { HideSettings(); OnHome?.Invoke(); });
            Label(home.transform, "HOME", title, Vector2.zero, new Vector2(310, 80), 40, White);
            var replay = Btn(card.transform, UIKit.PriceBtnA(), new Color(0.95f, 0.75f, 0.25f), new Vector2(0.5f, 0.5f), new Vector2(180, -160), new Vector2(310, 115),
                () => { HideSettings(); OnReplay?.Invoke(); });
            Label(replay.transform, "REPLAY", title, Vector2.zero, new Vector2(310, 80), 38, White);

            RedClose(card.transform, HideSettings);
            settingsPanel.SetActive(false);
        }

        // Sound/Music row: label + a tap-toggle button (bg = atlas1_36 PriceBtnA,
        // on/off icon = atlas1_18 CircleGreen → green when ON, grey when OFF).
        void ToggleRow(Transform parent, float y, string name, bool initial, System.Action<bool> onChange)
        {
            Label(parent, name, num, new Vector2(-140, y), new Vector2(300, 60), 38, Dark, TextAnchor.MiddleLeft);
            bool[] st = { initial };
            var btn = Btn(parent, UIKit.PriceBtnA(), new Color(0.5f, 0.7f, 0.9f), new Vector2(0.5f, 0.5f), new Vector2(220, y), new Vector2(200, 100), null);
            var ico = Img(btn.transform, UIKit.CircleGreen(), OffCol); ico.raycastTarget = false;
            Center(ico.rectTransform, new Vector2(76, 76));
            ico.color = st[0] ? OnCol : OffCol;
            btn.onClick.AddListener(() =>
            {
                st[0] = !st[0];
                ico.color = st[0] ? OnCol : OffCol;
                onChange?.Invoke(st[0]);
            });
        }

        // ---- Shop setup -----------------------------------------------------
        // Prefer the Inspector-editable scene shop baked via
        // "Tools ▸ 300Mind UI ▸ Bake In-Game Shop"; otherwise build the code shop.
        void SetupShop()
        {
            if (InGameShop.Instance != null && InGameShop.Instance.panel != null)
            {
                shopPanel = InGameShop.Instance.panel;
                WireSceneShop(shopPanel.transform);
                shopPanel.SetActive(false);
            }
            else BuildShop();
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
                        int amt = b.amount;
                        btn.onClick.AddListener(() => { SaveSystem.AddCoins(amt); SetCoins(SaveSystem.Coins); });
                        break;
                    case InGameShopButton.Act.SpendJoker:
                        btn.onClick.AddListener(() => { if (SaveSystem.TrySpend(100)) SetCoins(SaveSystem.Coins); });
                        break;
                    case InGameShopButton.Act.Close:
                        btn.onClick.AddListener(HideShop);
                        break;
                }
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
            Label(adsPrice.transform, "TRY 249,99", num, Vector2.zero, new Vector2(360, 60), 36, White);

            // 2) Gold packs (3-column grid, icons 11,12,13,29,30,31).
            var gridGo = new GameObject("CoinGrid", typeof(RectTransform));
            gridGo.transform.SetParent(ctGo.transform, false);
            var gl = gridGo.AddComponent<GridLayoutGroup>();
            gl.cellSize = new Vector2(275, 360); gl.spacing = new Vector2(15, 20);
            gl.childAlignment = TextAnchor.UpperCenter;
            gl.constraint = GridLayoutGroup.Constraint.FixedColumnCount; gl.constraintCount = 3;
            ShopCoinCard(gridGo.transform, UIKit.ShopCoinA(),     "100",   "$ 100",   100);
            ShopCoinCard(gridGo.transform, UIKit.ShopCoinB(),     "500",   "$ 250",   500);
            ShopCoinCard(gridGo.transform, UIKit.ShopCoinC(),     "1000",  "$ 500",   1000);
            ShopCoinCard(gridGo.transform, UIKit.ShopGold(),      "2000",  "$ 800",   2000);
            ShopCoinCard(gridGo.transform, UIKit.CoinPackSmall(), "5000",  "$ 1200",  5000);
            ShopCoinCard(gridGo.transform, UIKit.CoinPackBig(),   "10000", "$ 2100",  10000);

            // 3) Joker bars (atlas1_44 bg, icon left, buy for 100 gold).
            ShopJokerBar(ctGo.transform, UIKit.JokerRecolor());
            ShopJokerBar(ctGo.transform, UIKit.JokerSwap());
            ShopJokerBar(ctGo.transform, UIKit.JokerHeli());

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
                () => { SaveSystem.AddCoins(coins); SetCoins(SaveSystem.Coins); });
            Label(buy.transform, price, num, Vector2.zero, new Vector2(245, 56), 32, White);
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

            var pay = Btn(card.transform, UIKit.ShopIconBgB(), new Color(0.95f, 0.6f, 0.25f), new Vector2(0.5f, 0.5f), new Vector2(0, -150), new Vector2(580, 160), () => OnContinuePay?.Invoke());
            var payc = Img(pay.transform, UIKit.Coin(), Gold); payc.raycastTarget = false;
            Place(payc.rectTransform, new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(110, 0), new Vector2(70, 70));
            Label(pay.transform, "100", title, new Vector2(40, 0), new Vector2(440, 70), 44, White);

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

        void ClaimReward(int amount) { successPanel.SetActive(false); OnClaimReward?.Invoke(amount); }

        public static void Vibrate()
        {
            if (!SaveSystem.Vibration) return;
#if UNITY_ANDROID || UNITY_IOS
            Handheld.Vibrate();
#endif
        }

        // ---- API ------------------------------------------------------------
        public void ShowHud() { Toggle(hudPanel, true); }
        public void HideHud() { Toggle(hudPanel, false); }
        public void ShowSettings() { Toggle(settingsPanel, true); }
        public void HideSettings() { Toggle(settingsPanel, false); }
        public void ShowShop() { Toggle(shopPanel, true); }
        public void HideShop() { Toggle(shopPanel, false); }
        public void ShowContinue() { Toggle(continuePanel, true); }
        public void HideContinue() { Toggle(continuePanel, false); }
        public void ShowFailed() { Toggle(failedPanel, true); }
        public void HideFailed() { Toggle(failedPanel, false); }
        public void ShowSuccess() { ShowSuccess(3); }
        public void ShowSuccess(int stars) { Toggle(successPanel, true); }
        public void HideSuccess() { Toggle(successPanel, false); }

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

        void RedClose(Transform card, System.Action onClose)
        {
            var b = Btn(card, UIKit.CloseX(), new Color(0.85f, 0.2f, 0.2f), new Vector2(1, 1), new Vector2(-40, -40), new Vector2(96, 96), onClose);
            b.transform.SetAsLastSibling();
        }

        void Place(RectTransform rt, Vector2 min, Vector2 max, Vector2 pos, Vector2 size)
        { rt.anchorMin = min; rt.anchorMax = max; rt.pivot = new Vector2(0.5f, 0.5f); rt.anchoredPosition = pos; rt.sizeDelta = size; }
        void Center(RectTransform rt, Vector2 size)
        { rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f); rt.anchoredPosition = Vector2.zero; rt.sizeDelta = size; }
        void Stretch(RectTransform rt)
        { rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one; rt.offsetMin = Vector2.zero; rt.offsetMax = Vector2.zero; }
        void Toggle(GameObject go, bool on) { if (go) go.SetActive(on); }
    }
}
