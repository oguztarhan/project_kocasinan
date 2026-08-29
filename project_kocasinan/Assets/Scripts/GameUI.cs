using System.Collections;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;

namespace Ridebury
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
        public System.Action OnColorBlindToggle; // Settings COLOR BLIND toggle -> RideburyGame.ApplyColorBlindMode
        public System.Action<int> OnClaimReward;
        public System.Action OnContinueAd, OnContinuePay, OnContinueDeclined;
        public System.Action<int> OnFreeCoins; // +coins rewarded button -> RideburyGame grants coins & fires CoinsChanged

        static readonly Color White = Color.white;
        static readonly Color Gold  = new Color(1f, 0.85f, 0.30f);
        static readonly Color Dark  = new Color(0.16f, 0.20f, 0.30f);
        static readonly Color Dim   = new Color(0, 0, 0, 0.6f);
        static readonly Color OnCol = new Color(0.35f, 0.85f, 0.40f);
        static readonly Color OffCol= new Color(0.65f, 0.65f, 0.70f);
        // The cut kit's rows/cards are CREAM. White lettering is invisible on them, so light surfaces
        // get inked text instead.
        static readonly Color Ink     = new Color(0.28f, 0.16f, 0.05f);  // headline on a cream surface
        static readonly Color InkSoft = new Color(0.44f, 0.31f, 0.18f);  // secondary line on a cream surface

        Font title, num;
        Transform root;
        GameObject hudPanel, settingsPanel, successPanel, continuePanel, failedPanel, shopPanel;
        ShopUI shop;   // THE shop (shared with the main menu) — see ShopUI
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
        // Booster RAIL (was a row across the bottom-centre, sitting on the busiest part of the jam and right on top
        // of the banner). A column hugging the RIGHT edge keeps the whole board width free for dragging and stays in
        // the thumb zone. It grazes the right escape lane, but away-arrow vehicles clear that band in well under a
        // second. Mirror any change here in the baked HUD (SampleScene ▸ Hud ▸ Joker_*) — that is the shipped path.
        const float RailX      = -95f;                   // button centre, in from the right edge
        const float RailBottom = BannerReservePx + 80f;  // lowest button, clear of the adaptive banner
        const float RailStep   = 170f;                   // centre-to-centre gap
        const float RailSize   = 150f;                   // button edge (~54pt on a 393pt-wide phone; Apple's floor is 44pt)
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
            var shopCanvas = ShopUI.Instance != null ? ShopUI.Instance.GetComponent<Canvas>() : null;
            var panelsCanvas = InGamePanels.Instance != null ? InGamePanels.Instance.GetComponent<Canvas>() : null;
            var hudCanvas = InGameHud.Instance != null ? InGameHud.Instance.GetComponent<Canvas>() : null;
            var garageCanvas = InGameGarage.Instance != null ? InGameGarage.Instance.GetComponent<Canvas>() : null;
            foreach (var c in Object.FindObjectsByType<Canvas>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
            {
                if (c == null) continue;
                if (c.transform.root == transform.root) continue; // ours
                if (shopCanvas != null && c == shopCanvas) continue; // the shared shop canvas
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

            ApplyCutKitToHud(h); // the HUD prefab still holds OLD atlas sprites -> repoint them first

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

        // The in-game HUD is an authored prefab (Resources/UI/HudPanel), so its icons are serialized
        // sprite refs from the old 300Mind atlas — repointing UIKit alone does NOT move them. Swap the
        // redrawn ones here, on adoption, so the HUD matches the menu and the code-built garage without
        // anyone re-baking the prefab or assigning sprites by hand.
        void ApplyCutKitToHud(InGameHud h)
        {
            if (h == null) return;
            if (h.coinButton)
            {
                var ci = FindDeep(h.coinButton.transform, "Coin_Icon");
                PaintIcon(ci ? ci.GetComponent<Image>() : null, UIKit.Coin());
            }
            if (h.gearButton) PaintIcon(h.gearButton.GetComponent<Image>(), UIKit.Gear());
            if (h.recolor != null) { PaintIcon(h.recolor.icon, UIKit.JokerRecolor()); PaintBg(h.recolor.background, UIKit.BtnGrey()); }
            if (h.swap    != null) { PaintIcon(h.swap.icon,    UIKit.JokerSwap());    PaintBg(h.swap.background,    UIKit.BtnGrey()); }
            if (h.heli    != null) { PaintIcon(h.heli.icon,    UIKit.JokerHeli());    PaintBg(h.heli.background,    UIKit.BtnGrey()); }
        }

        // Untinted, un-squashed. No-ops on a missing image or sprite, so a partial bake is harmless.
        // (The joker code fades `icon.color` to show "out of stock", so only the alpha is preserved.)
        static void PaintIcon(Image img, Sprite sprite)
        {
            if (img == null || sprite == null) return;
            img.sprite = sprite;
            img.color = new Color(1f, 1f, 1f, img.color.a);
            img.preserveAspect = true;
        }

        // Same, for a BACKING plate: it has to fill its rect, so aspect is deliberately not preserved.
        static void PaintBg(Image img, Sprite sprite)
        {
            if (img == null || sprite == null) return;
            img.sprite = sprite;
            img.color = new Color(1f, 1f, 1f, img.color.a);
            img.preserveAspect = false;
            UIKit.Slice(img, img.rectTransform.rect.size);
        }

        Joker AdoptJoker(HudJoker hj, int cost, int unlock, int kind, System.Action use)
        {
            if (hj == null) return new Joker();
            return MakeJoker(hj.button, hj.background, hj.icon, hj.lockGo, hj.counterGo, hj.counterText, cost, unlock, kind, use);
        }

        // Builds a Joker record + wires the button: when you OWN one, pressing uses it
        // (RideburyGame consumes a charge); when out of stock, pressing opens the buy panel.
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

            // LEVEL badge: TOP-LEFT, rounded blue-purple button (atlas1_25), white text. Shifted right of the gear,
            // which now owns the corner itself (see the corner swap on the coin bar below).
            var badge = Img(hudPanel.transform, UIKit.A(25), new Color(0.45f, 0.40f, 0.85f));
            Place(badge.rectTransform, new Vector2(0, 1), new Vector2(0, 1), new Vector2(250, -110), new Vector2(170, 170));
            badge.raycastTarget = false;
            Label(badge.transform, "LEVEL", num, new Vector2(0, 42), new Vector2(160, 36), 24, White);
            hudLevel = Label(badge.transform, "1", title, new Vector2(0, -16), new Vector2(160, 90), 64, White);
            levelBadgeGo = badge.gameObject; // (#6) hidden while the shop is open
            hudTheme = Label(hudPanel.transform, "", num, new Vector2(250, -210), new Vector2(260, 36), 22, new Color(0.85f, 0.9f, 1f));
            hudTheme.rectTransform.anchorMin = hudTheme.rectTransform.anchorMax = new Vector2(0, 1);
            hudTheme.rectTransform.anchoredPosition = new Vector2(250, -210);

            // COIN display: TOP-RIGHT (atlas1_20 bar), opens the in-game shop. Currency-right / settings-left is the
            // mirror of the stock template corner assignment; it also frees the top-centre strip for the bonus timer.
            var coinBtn = Btn(hudPanel.transform, UIKit.CoinBar(), Dark, new Vector2(1, 1), new Vector2(-170, -110), new Vector2(300, 96), ShowShop);
            coinBarGo = coinBtn.gameObject; // (#6) hidden while the garage is open (the garage shows its own gold)
            var ci = Img(coinBtn.transform, UIKit.Coin(), Gold); ci.raycastTarget = false; ci.preserveAspect = true;
            Place(ci.rectTransform, new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(42, 0), new Vector2(74, 74));
            hudCoins = Label(coinBtn.transform, "0", num, new Vector2(35, 0), new Vector2(180, 60), 44, White);

            // SETTINGS gear: TOP-LEFT corner, smaller (it is the least-pressed button on the screen).
            gearGo = Btn(hudPanel.transform, UIKit.Gear(), new Color(0.7f, 0.72f, 0.78f), new Vector2(0, 1), new Vector2(86, -110), new Vector2(96, 96), ShowSettings).gameObject; // (#6)

            // (#1) The watch-ad / +coins button was removed from the in-game HUD per request.

            // (People-left count now lives ONLY on the neon world sign by the first bus stop — HUD chip removed.)

            comboText = Label(hudPanel.transform, "", title, new Vector2(0, 360), new Vector2(900, 100), 70, Gold);
            comboText.gameObject.SetActive(false);

            // 3 jokers in a right-edge rail (atlas1_25 buttons + atlas1_34 count badges), stacked bottom-up in
            // unlock order so the one the player gets first is the one nearest the thumb.
            jRecolor = JokerButton(RailBottom,                 UIKit.JokerRecolor(), recolorCost, j1Lvl, 0, () => OnRecolor?.Invoke());
            jSwap    = JokerButton(RailBottom + RailStep,      UIKit.JokerSwap(),    swapCost,    j2Lvl, 1, () => OnSwap?.Invoke());
            jHeli    = JokerButton(RailBottom + RailStep * 2f, UIKit.JokerHeli(),    heliCost,    j3Lvl, 2, () => OnHeli?.Invoke());
            RefreshJokers();
            AddGarageButton(hudPanel.transform);
            BuildBonusCountdown();
        }

        Joker JokerButton(float y, Sprite icon, int cost, int unlock, int kind, System.Action use)
        {
            var btn = Btn(hudPanel.transform, UIKit.BtnGrey(), new Color(0.45f, 0.40f, 0.85f), new Vector2(1, 0), new Vector2(RailX, y), new Vector2(RailSize, RailSize), null);
            var bg = btn.GetComponent<Image>();
            var ico = Img(btn.transform, icon, White); ico.raycastTarget = false; ico.preserveAspect = true; // cut joker art is square
            Center(ico.rectTransform, new Vector2(93, 93));
            var lk = Img(btn.transform, null, new Color(0, 0, 0, 0.55f)); lk.raycastTarget = false;
            Center(lk.rectTransform, new Vector2(RailSize, RailSize));
            Label(lk.transform, "LV " + unlock, num, Vector2.zero, new Vector2(142, 50), 34, White);
            var cb = Img(btn.transform, UIKit.A(34), new Color(0.95f, 0.78f, 0.20f)); cb.raycastTarget = false;
            Place(cb.rectTransform, new Vector2(1, 1), new Vector2(1, 1), new Vector2(-3, -3), new Vector2(60, 60));
            var ct = Label(cb.transform, "0", num, Vector2.zero, new Vector2(60, 42), 32, White);
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
            if (j.bg)
            {
                if (faded)
                {
                    j.bg.sprite = UIKit.PassiveState();
                    j.bg.color = Color.white;
                    j.bg.preserveAspect = true;
                }
                else j.bg.color = j.bgColor;
            }
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
            // Restore Purchases now lives in the SHOP (ShopUI.AddRestoreRow), not in Settings. LEVELS was removed per
            // request; COLOR BLIND is now a Hierarchy button you add to the Settings panel yourself (wired by name).
            WireColorBlindButton(settingsPanel != null ? settingsPanel.transform : null);
            AddPrivacyOptionsButton(settingsPanel != null ? settingsPanel.transform : null);
            // DEBUG: the LEVELS jump button now lives HERE in Settings (off the play screen / out of screenshots),
            // shown only while the "Ridebury ▸ LEVELS Test Button" editor toggle is ON. Device builds never see it.
            if (LevelSelect.DebugLevels) AddLevelsButton(settingsPanel != null ? settingsPanel.transform : null);
        }

        // Google's EU consent policy: users who were shown the UMP consent form (EEA/UK) must ALWAYS have an entry
        // point to change their ad-privacy choices. This floats a small button at the BOTTOM of whichever Settings
        // panel is in use (baked or code-built — same float pattern as the notifications toggle, so it can't collide
        // with authored layout) and shows itself ONLY while UMP reports the requirement, so everyone else never sees it.
        void AddPrivacyOptionsButton(Transform panel)
        {
            if (panel == null) return;
            var btn = Btn(panel, UIKit.PriceBtnA(), new Color(0.45f, 0.50f, 0.60f), new Vector2(0.5f, 0f), new Vector2(0, 90), new Vector2(560, 84),
                          () => AdManager.Instance?.ShowPrivacyOptions());
            Label(btn.transform, Loc.T("Privacy options"), num, Vector2.zero, new Vector2(540, 54), 30, White);
            btn.gameObject.AddComponent<PrivacyOptionsVisibility>(); // shows/hides with the live UMP requirement
        }

        // Keeps the Privacy-options button visible ONLY while UMP requires it. Uses a CanvasGroup (not SetActive, which
        // would stop its own Update) and only runs while the Settings panel is open — effectively free.
        class PrivacyOptionsVisibility : MonoBehaviour
        {
            CanvasGroup cg;
            void Awake() { cg = gameObject.AddComponent<CanvasGroup>(); Apply(); }
            void OnEnable() { Apply(); }
            void Update() { Apply(); } // the UMP status lands asynchronously after boot -> keep it fresh while visible
            void Apply()
            {
                bool req = AdManager.Instance != null && AdManager.Instance.PrivacyOptionsRequired;
                if (cg == null) return;
                cg.alpha = req ? 1f : 0f;
                cg.interactable = cg.blocksRaycasts = req;
            }
        }

        // Wire a COLOR BLIND on/off button that YOU add to the Settings panel in the Hierarchy — name the object
        // "ColorBlind" (or "Btn_ColorBlind"). Tapping flips SaveSystem.ColorBlind and rebuilds the board in the new
        // palette (OnColorBlindToggle). Your own label/graphics are left untouched (design + localize them freely). If
        // the button has a child named "Check" (or "On" / "Tick"), it's shown ONLY while the mode is ON — a ready-made
        // state indicator. No-op if no such button exists yet, so it's safe until you add it.
        void WireColorBlindButton(Transform settingsRoot)
        {
            if (settingsRoot == null) return;
            var t = FindDeep(settingsRoot, "ColorBlind") ?? FindDeep(settingsRoot, "Btn_ColorBlind") ?? FindDeep(settingsRoot, "Colorblind");
            if (t == null) return;
            var btn = t.GetComponent<Button>(); if (btn == null) btn = t.gameObject.AddComponent<Button>();
            var check = FindDeep(t, "Check") ?? FindDeep(t, "On") ?? FindDeep(t, "Tick");
            void Refresh() { if (check != null) check.gameObject.SetActive(SaveSystem.ColorBlind); }
            Refresh();
            btn.onClick.AddListener(() => { SaveSystem.ColorBlind = !SaveSystem.ColorBlind; Refresh(); OnColorBlindToggle?.Invoke(); });
        }

        // (DEBUG, currently NOT wired) Settings → LEVELS: opens the level-select map to jump to any level — used to reach
        // the every-10th bonus levels without grinding to them. Call it from SetupSettings to switch it on, and flip
        // LevelSelect.debugUnlockAll to true alongside it (otherwise the map only shows levels you have actually reached).
        // Both are OFF for release: together they let a first-time player skip straight to level 100.
        // RideburyGame wires OnLevels to levelSelect.Open(); the lambda invokes it at CLICK time, so wiring order is free.
        // Placement: its OWN top-most canvas + raycaster. A plain child of the settings card does NOT receive taps (the
        // card's own overrideSorting canvas swallows them) — this is the placement proven to work on-device.
        void AddLevelsButton(Transform panel)
        {
            if (panel == null) return;
            var holder = new GameObject("LevelsBtn", typeof(RectTransform), typeof(Canvas), typeof(GraphicRaycaster));
            holder.transform.SetParent(panel, false);
            var hrt = holder.GetComponent<RectTransform>();
            hrt.anchorMin = Vector2.zero; hrt.anchorMax = Vector2.one; hrt.offsetMin = Vector2.zero; hrt.offsetMax = Vector2.zero;
            var cv = holder.GetComponent<Canvas>(); cv.overrideSorting = true; cv.sortingOrder = 500; // above the settings card

            var btn = Btn(holder.transform, UIKit.PriceBtnA(), new Color(0.42f, 0.40f, 0.85f), new Vector2(0.5f, 0f), new Vector2(0, 200), new Vector2(540, 110),
                          () => { HideSettings(); OnLevels?.Invoke(); }); // close Settings, then open the level map (LevelSelect pauses the game)
            Label(btn.transform, "LEVELS", title, Vector2.zero, new Vector2(540, 70), 34, White); // raw key -> Localizer translates it (LEVELS is in Loc.Table)
        }

        // (#1/#2) Wire the in-game Settings "Language" button. The baked button is named "Language" (the old code
        // looked for "Card/Btn_Empty", which doesn't exist, so it was never wired).
        void WireLanguageButton(Transform settingsRoot)
        {
            var t = FindDeep(settingsRoot, "Language") ?? FindDeep(settingsRoot, "Btn_Language") ?? FindDeep(settingsRoot, "Btn_Empty");
            if (t == null) return;
            var btn = t.GetComponent<Button>() ?? t.GetComponentInParent<Button>();
            if (btn == null) return;

            var lang = InGamePanels.Instance != null ? InGamePanels.Instance.language : null;
            // Older GamePanels prefabs shipped with the popup present but the serialized marker reference empty.
            // Resolve it by name as a safe fallback so the settings button can never silently become a no-op.
            if (lang == null && InGamePanels.Instance != null)
            {
                var found = FindDeep(InGamePanels.Instance.transform, "Panel_Language") ??
                            FindDeep(InGamePanels.Instance.transform, "LanguagePanel");
                if (found != null) lang = found.gameObject;
            }
            if (lang == null)
            {
                var selector = Object.FindFirstObjectByType<LanguageSelector>(FindObjectsInactive.Include);
                if (selector != null) lang = selector.gameObject;
            }
            if (lang != null)
            {
                btn.onClick.AddListener(() => lang.SetActive(true)); // (#1) open the popup
                foreach (var b in lang.GetComponentsInChildren<InGamePanelButton>(true)) // let its close button dismiss it
                {
                    var cb = b.GetComponent<Button>();
                    if (cb != null && b.action == InGamePanelButton.Act.Close) cb.onClick.AddListener(() => lang.SetActive(false));
                }
            }

            // FIX: the "Language" word wasn't rendering. Its baked label is a black legacy Text (same as the working
            // "Color Blind" label, so colour is NOT the issue) but it sits in a small fixed box (200x50) with
            // Wrap+Truncate overflow inside a non-uniformly SCALED button, which clipped the text to nothing. So force
            // the label ROBUST: fill the whole button, centre it, and NEVER wrap/clip (Overflow) — then it always
            // renders. Also set the "Language" key so LocalizeScene translates it (and re-translates on a lang change).
            var tmp = btn.GetComponentInChildren<TMPro.TMP_Text>(true);
            if (tmp != null) tmp.gameObject.SetActive(false); // hide any baked TMP label; the visible one is the legacy Text
            var lbl = btn.GetComponentInChildren<Text>(true);
            if (lbl == null) lbl = Label(btn.transform, "Language", title, Vector2.zero, new Vector2(440, 90), 40, Color.black);
            lbl.gameObject.SetActive(true);
            lbl.text = "Language";                                    // Loc key -> translated by LocalizeScene
            lbl.alignment = TextAnchor.MiddleCenter;
            lbl.horizontalOverflow = HorizontalWrapMode.Overflow;     // never wrap
            lbl.verticalOverflow   = VerticalWrapMode.Overflow;       // never truncate/clip -> the word always shows
            var lrt = lbl.rectTransform;
            lrt.localScale = Vector3.one;
            lrt.anchorMin = Vector2.zero; lrt.anchorMax = Vector2.one; lrt.offsetMin = Vector2.zero; lrt.offsetMax = Vector2.zero;
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
        // ONE shop for the whole game: the shared ShopUI prefab (Resources/UI/ShopPanel),
        // also used by the main menu. GameUI only opens/closes it and reacts to what the
        // player buys — all shop wiring (IAP products, prices, jokers, restore) is in ShopUI.
        void SetupShop()
        {
            IAPManager.OnChanged -= OnIapChanged; IAPManager.OnChanged += OnIapChanged; // repaint HUD counters when a purchase resolves

            shop = ShopUI.Ensure();
            if (shop == null) return;
            shopPanel = shop.panel;                                  // MakeExclusive / AnyPanelOpen still track it
            shop.onOpened = () => SetHudChromeVisible(false);        // (#6) hide gear/level/ad/jokers
            shop.onClosed = () => SetHudChromeVisible(true);         // restore them when it closes
            shop.onCoinsChanged = () => { SetCoins(SaveSystem.Coins); RefreshJokers(); };
        }

        // First descendant (inactive included) whose GameObject is named `name`, else null.
        static Transform FindDeep(Transform root, string name)
        {
            foreach (var t in root.GetComponentsInChildren<Transform>(true))
                if (t.name == name) return t;
            return null;
        }

        // IAPManager fires OnChanged after a verified purchase / restore / first init -> repaint the live counters.
        void OnIapChanged()
        {
            SetCoins(SaveSystem.Coins);
            RefreshJokers();
            if (shop != null) shop.RefreshPrices();   // shop amounts + localized prices
        }

        void OnDestroy() { IAPManager.OnChanged -= OnIapChanged; }

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
        // The HUD gear is the PAUSE button: gameplay (vehicles, traffic, bonus clock) freezes while the Settings
        // panel is up and resumes the instant it closes. Every close path funnels through HideSettings (RedClose /
        // baked Close / Home / Replay / LEVELS), so the game can never stay stuck at timeScale 0 — and LevelSelect
        // re-pauses itself right after the LEVELS path resumes here.
        public void ShowSettings() { Toggle(settingsPanel, true); Time.timeScale = 0f; }
        public void HideSettings() { Toggle(settingsPanel, false); Time.timeScale = 1f; }
        public void ShowShop() { var s = Shop(); if (s != null) s.Open(); }   // chrome is hidden by the onOpened hook (#6)
        public void HideShop() { var s = Shop(); if (s != null) s.Close(); }

        // The shop, re-hooked if needed: a domain reload (editing a script while in Play) drops
        // the delegates below, and the coin tap would then open a shop that never restores the HUD.
        ShopUI Shop()
        {
            if (shop == null || shop.onOpened == null) SetupShop();
            return shop;
        }

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
        public void ShowContinue() { MakeExclusive(continuePanel); Toggle(continuePanel, true); }
        public void SetContinuePrice(int cost) { if (continuePrice) continuePrice.text = cost.ToString(); }
        public void HideContinue() { Toggle(continuePanel, false); }
        public void ShowFailed() { MakeExclusive(failedPanel); Toggle(failedPanel, true); }
        public void HideFailed() { Toggle(failedPanel, false); }
        public void ShowSuccess(int stars, int reward)
        {
            MakeExclusive(successPanel);                            // level-complete panel must be the ONLY thing on screen
            if (successReward) successReward.text = "+" + reward;   // show the actual coin reward (economy rework)
            Toggle(successPanel, true);
        }
        public void HideSuccess() { Toggle(successPanel, false); }

        // SINGLE RULE — a level-resolution panel (Success / Continue / Failed / a bonus chest) must be the ONLY thing on
        // screen. Force every OTHER overlay, popup and screen shut BEFORE that panel is shown, so a screen the player
        // opened mid-boarding (Garage, wardrobe, shop, settings, a chest reveal, …) can never remain layered behind it.
        // This is the one place that enforces exclusivity; the terminal Show* methods above + the bonus flow call it —
        // NOT a one-off patch on any single screen. `keep` is the panel we're about to show (never closed).
        void MakeExclusive(GameObject keep)
        {
            GameObject[] overlays =
            {
                garagePanel, vehiclesPanel, revealPanel,     // Garage screen + its sub-screens (wardrobe, chest reveal)
                shopPanel, settingsPanel, jokerBuyPanel,     // other popups
                chestWonPanel, stopBarPanel,                 // bonus-reward panels
                continuePanel, failedPanel, successPanel,    // the OTHER terminal panels (mutually exclusive)
            };
            foreach (var go in overlays)
                if (go != null && go != keep && go.activeSelf) go.SetActive(false);
            if (jokerBuyPanels != null)
                foreach (var go in jokerBuyPanels)
                    if (go != null && go != keep && go.activeSelf) go.SetActive(false);

            // ShowGarage hides the coin bar, suppresses the bonus timer, and may flag "opened from the menu". Closing the
            // garage with the raw SetActive above skips HideGarage, so those side-effects would otherwise persist (an
            // invisible coin bar + suppressed bonus timer on the NEXT level). Undo them here — unless the garage/wardrobe
            // is itself the panel being kept.
            if (keep != garagePanel && keep != vehiclesPanel)
            {
                if (coinBarGo) coinBarGo.SetActive(true);
                hideBonusTimer = false;
                garageFromMenu = false;
            }
        }

        // True while any modal pop-up (settings / shop / continue / failed / success) is open. RideburyGame uses
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
            // The waiting-passenger queue renders at the deepest world band (PeopleZ), which projects to the TOP of the
            // screen (~y700) — a timer there sat right on the queue and hid which colours were coming. Placed at the
            // very TOP-CENTER, ABOVE the passengers: TOP-anchored so it stays above them on every aspect (on tall
            // phones the queue drops toward centre; the top-anchored timer stays put). The coin bar owns this strip,
            // so it's hidden whenever the timer shows (SetBonus*/HideBonusCountdown below) — the shop isn't needed
            // mid-bonus, exactly like the garage already hides it.
            bonusCountdown = Label(hudPanel.transform, "", num, Vector2.zero, new Vector2(420, 130), 96, White);
            var brt = bonusCountdown.rectTransform;
            brt.anchorMin = brt.anchorMax = new Vector2(0.5f, 1f); // top-centre
            brt.anchoredPosition = new Vector2(0, -210);          // just below the notch, above the passenger queue
            bonusCountdown.gameObject.SetActive(false);
            // NOTE: the red/green traffic light is now a real in-world prop on both road sides (RideburyGame.BuildTrafficLights),
            // not a HUD widget — so the player reads stop/go straight off the road.
        }
        // TimeAttack count-UP stopwatch (reuses the bonus label): shows elapsed m:ss coloured by PACE, so the player
        // sees the chest they're earning — green (<25s = Gold) -> orange (<45s = Silver) -> red (Bronze).
        public void SetBonusStopwatch(float elapsed)
        {
            if (!bonusCountdown) return;
            if (hideBonusTimer) { if (bonusCountdown.gameObject.activeSelf) bonusCountdown.gameObject.SetActive(false); return; } // (#2) suppressed while the garage is open
            if (!bonusCountdown.gameObject.activeSelf) bonusCountdown.gameObject.SetActive(true);
            if (coinBarGo && coinBarGo.activeSelf) coinBarGo.SetActive(false); // timer owns the top-centre strip -> hide the coin bar under it
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
            if (coinBarGo && coinBarGo.activeSelf) coinBarGo.SetActive(false); // timer owns the top-centre strip -> hide the coin bar under it
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
            // Timer gone -> give the top-centre strip back to the coin bar (normal levels / level end). The garage's
            // own suppression path (hideBonusTimer) leaves the coin bar alone, since the garage manages it separately.
            if (coinBarGo && !coinBarGo.activeSelf && !hideBonusTimer) coinBarGo.SetActive(true);
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

        // 9-slice an image drawn from the cut kit. Those sprites are authored ~1024px wide, so their
        // borders are far larger than the rects we draw them in and a raw Sliced image renders as mush;
        // pixelsPerUnitMultiplier scales the border down until a border pair takes at most 80% of
        // `approxSize`. Pass the size the element ends up at (layout-driven rows: content width + the
        // LayoutElement height). No border authored -> left stretched, exactly as before.
        public Image Sliced(Image img, Vector2 approxSize) { UIKit.Slice(img, approxSize); return img; }

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
            var bi = b.GetComponent<Image>(); if (bi) bi.preserveAspect = true; // the cut ✕ is a round badge — never squash it
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
