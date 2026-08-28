using UnityEngine;
using UnityEngine.UI;
using UnityEngine.SceneManagement;
using Ridebury;

/// <summary>
/// Behaviour driver for the SCENE-AUTHORED main menu produced by the editor tool
/// "Tools ▸ 300Mind UI ▸ Bake Main Menu". The bake step creates the visual objects
/// and assigns the references below, so you can freely edit every element's colour,
/// size, position and font in the Inspector — this script only handles the logic.
///
/// While any pop-up panel (Daily / Shop / …) is open, the home-only elements
/// (gold counter, settings, no-ads, PLAY) are hidden; the bottom nav stays visible.
/// </summary>
public class MenuController : MonoBehaviour
{
    [Header("Currency")]
    [SerializeField] public Text coinText;

    [Header("Pop-up panels")]
    [SerializeField] public GameObject dailyPanel;
    [SerializeField] public GameObject shopPanel;
    [SerializeField] public GameObject profilePanel;
    [SerializeField] public GameObject settingsPanel;
    [SerializeField] public GameObject removeAdsPanel;
    [SerializeField] public GameObject adRewardPanel;
    [SerializeField] public GameObject languagePanel;

    [Header("Bottom-nav selected highlights (orange backing)")]
    [SerializeField] public GameObject navDailySel;
    [SerializeField] public GameObject navHomeSel;
    [SerializeField] public GameObject navShopSel;

    [Header("Scene")]
    [SerializeField] public string gameSceneName = "SampleScene";

    [Header("Social media links (paste your URLs here)")]
    [SerializeField] public string facebookUrl  = "https://facebook.com/";
    [SerializeField] public string xUrl         = "https://x.com/";
    [SerializeField] public string instagramUrl = "https://instagram.com/";
    [SerializeField] public string tiktokUrl    = "https://tiktok.com/";

    [Header("Website — just paste your URL here")]
    [SerializeField] public string websiteUrl   = "";   // e.g. "https://yourgame.com"

    // Home-only elements (found by name in the baked hierarchy); hidden while a panel is open.
    GameObject[] homeOnly;
    // Bottom-nav buttons (Nav_Daily / Nav_Home / Nav_Shop); hidden while the SHOP is open (player exits via the ✕).
    GameObject[] navButtons;
    // THE shop (shared with the game scene) — see ShopUI.
    ShopUI shop;

    void Start()
    {
        AdManager.Ensure(); // create the ad singleton in the menu too, so the "watch ad → gold" button works here
        EnsureGarageButton(); // the baked scene has no Btn_Garage -> build it from Btn_Play (same orange style)
        homeOnly = new[]
        {
            FindByName("Coin_Bar"),    // gold counter
            FindByName("Btn_Settings"),// settings gear
            FindByName("Btn_NoAds"),   // no-ads icon
            FindByName("Btn_AdReward"),// watch-ad-for-gold
            FindByName("Btn_Play"),    // PLAY button
            FindByName("Btn_Garage"),  // GARAGE button (skins / chests)
        };
        navButtons = new[] { FindByName("Nav_Daily"), FindByName("Nav_Home"), FindByName("Nav_Shop") };
        CloseAll();
        Refresh();
        SetupShop();           // spawn + hook THE shop (the shared ShopUI prefab, same one the game scene uses)
        WireRemoveAdsPanel();  // the dedicated Remove-Ads popup's offer graphics have NO listeners -> wire them to IAP
        EnsureLanguageUi();    // repair stale/missing prefab references and always wire the visible language button
        EnsureSettingsClose(); // (Settings pop-up) add a red ✕ close button (top-right), wired to ShowHome
        ApplyCutKitIcons();    // the menu prefab still holds OLD atlas sprites — repoint them at the cut kit
        // Start menu music now ONLY if we're not in the launch splash — on first boot the BootSplash starts it at
        // the LOADING screen (not on the Intake logo). When returning here from gameplay there's no splash, so play.
        if (Object.FindAnyObjectByType<BootSplash>() == null)
            MusicManager.PlayMenu(); // main-menu background music (track 1 by default)
        // (#2) Menu background is owned by the self-spawning MenuBackground.cs (animated coast-sunset), which
        // disables the static "Background" object itself. We deliberately DON'T override it here anymore — that
        // was a second background fighting the animated one. One source of truth now.
        Localizer.LocalizeScene(); // translate all baked menu text to the saved language
        // If the "did you like the game?" reminder has already fired, the player opened the app having been asked —
        // so follow up with the actual prompt here in the menu. No-op when nothing is pending. (see RateUs)
        RateUs.MaybeShowFromNotification();
    }

    void Update() { Refresh(); }

    public void Refresh() { if (coinText) coinText.text = SaveSystem.Coins.ToString(); }

    // Makes the Settings pop-up's close button work. The baked panel has a red "Close" ✕ that is only an
    // Image (NO Button component), so tapping it did nothing. Find it and wire it to ShowHome — adding a
    // Button if missing. Also removes the redundant runtime square an earlier build added underneath it.
    // Falls back to building a ✕ on the card if no baked "Close" exists. Idempotent; safe every Start.
    void EnsureSettingsClose()
    {
        if (settingsPanel == null) return;

        // Drop the redundant red square a previous version created at runtime, if it's there.
        var stale = FindInPanel(settingsPanel.transform, "CloseBtn_Runtime");
        if (stale != null) Destroy(stale.gameObject);

        // Wire the baked red "Close" ✕ (the button the player actually sees and taps).
        var closeT = FindInPanel(settingsPanel.transform, "Close");
        if (closeT != null)
        {
            var cimg = closeT.GetComponent<Image>();
            if (cimg != null) cimg.raycastTarget = true;
            var cbtn = closeT.GetComponent<Button>();
            if (cbtn == null) cbtn = closeT.gameObject.AddComponent<Button>();
            if (cimg != null) cbtn.targetGraphic = cimg;
            cbtn.interactable = true;
            cbtn.onClick.RemoveListener(ShowHome); // avoid stacking duplicates across Starts
            cbtn.onClick.AddListener(ShowHome);
            return;
        }

        // Fallback: no baked close button -> build a red ✕ on the card (old behaviour).
        Transform card = settingsPanel.transform.Find("Panel");
        if (card == null && settingsPanel.transform.childCount > 0) card = settingsPanel.transform.GetChild(0);
        if (card == null) card = settingsPanel.transform;
        if (card.Find("CloseBtn_Runtime") != null) return;
        var go = new GameObject("CloseBtn_Runtime", typeof(RectTransform), typeof(Image), typeof(Button));
        go.transform.SetParent(card, false);
        go.transform.SetAsLastSibling(); // render on top of the card content
        var rt = (RectTransform)go.transform;
        rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(1f, 1f);
        rt.anchoredPosition = new Vector2(-30f, -30f);
        rt.sizeDelta = new Vector2(110f, 110f);
        go.GetComponent<Image>().color = new Color(0.86f, 0.27f, 0.27f, 1f); // red
        var xGo = new GameObject("X", typeof(RectTransform), typeof(Text));
        xGo.transform.SetParent(go.transform, false);
        var xrt = (RectTransform)xGo.transform;
        xrt.anchorMin = Vector2.zero; xrt.anchorMax = Vector2.one; xrt.offsetMin = Vector2.zero; xrt.offsetMax = Vector2.zero;
        var t = xGo.GetComponent<Text>();
        t.font = GameFont.UGUI;
        t.text = "X"; t.fontSize = 60; t.alignment = TextAnchor.MiddleCenter; t.color = Color.white; t.raycastTarget = false;
        go.GetComponent<Button>().onClick.AddListener(ShowHome);
    }

    void Set(GameObject g, bool on) { if (g) g.SetActive(on); }

    void HidePanels()
    {
        Set(dailyPanel, false); Set(shopPanel, false); Set(profilePanel, false);
        Set(settingsPanel, false); Set(removeAdsPanel, false); Set(adRewardPanel, false); Set(languagePanel, false);
    }

    void SetHomeOnly(bool on)
    {
        if (homeOnly == null) return;
        foreach (var g in homeOnly) if (g) g.SetActive(on);
    }

    void Sel(GameObject g)
    {
        Set(navDailySel, g == navDailySel);
        Set(navHomeSel,  g == navHomeSel);
        Set(navShopSel,  g == navShopSel);
    }

    // Show/hide the whole bottom nav (Daily/Home/Shop). Hidden while the SHOP is open so the ✕ is the only way out.
    void SetNav(bool on) { if (navButtons == null) return; foreach (var g in navButtons) if (g) g.SetActive(on); }

    // The menu is an authored prefab (Resources/UI/MenuUI), so its icons are serialized sprite refs
    // from the old 300Mind atlas — changing UIKit's accessors alone does NOT move them. Repoint the
    // ones that were redrawn here, at Start, so the menu matches the code-built screens (garage,
    // in-game HUD) without anyone re-baking or hand-assigning anything in the Inspector.
    void ApplyCutKitIcons()
    {
        SetIcon(FindByName("Coin_Bar"),   "Coin_Icon", UIKit.Coin());
        SetIcon(FindByName("Nav_Daily"),  "Icon",      UIKit.NavDaily());
        SetIcon(FindByName("Nav_Home"),   "Icon",      UIKit.NavHome());
        SetIcon(FindByName("Nav_Shop"),   "Icon",      UIKit.NavShop());
        // The unselected backing plate ("Bg") behind each nav icon — the selected one ("Sel") stays
        // orange so the current tab still reads.
        foreach (var nav in new[] { "Nav_Daily", "Nav_Home", "Nav_Shop" })
            SetPlate(FindByName(nav), "Bg", UIKit.BtnGrey());
        SetSelf(FindByName("Btn_Settings"),            UIKit.Gear());
        SetSelf(FindByName("Btn_AdReward"),            UIKit.AdReward());
        SetSelf(FindByName("Btn_Play"),                UIKit.PlayBtn());
        SetSelf(FindByName("Btn_Garage"),              UIKit.BtnOrange());

        // Every pop-up closes with a child named "Close" — give them all the new round red badge, the
        // same one the code-built garage / shop use, so the menu never mixes two generations of it.
        var x = UIKit.CloseX();
        if (x != null)
            foreach (var t in GetComponentsInChildren<Transform>(true))
                if (t.name == "Close") Paint(t.GetComponent<Image>(), x);
    }

    void SetIcon(GameObject parent, string child, Sprite sprite)
    {
        if (parent == null) return;
        var t = parent.transform.Find(child);
        Paint(t ? t.GetComponent<Image>() : null, sprite);
    }

    void SetSelf(GameObject go, Sprite sprite) { if (go != null) Paint(go.GetComponent<Image>(), sprite); }

    // A backing plate fills its rect, so unlike an icon its aspect is deliberately NOT preserved.
    void SetPlate(GameObject parent, string child, Sprite sprite)
    {
        if (parent == null || sprite == null) return;
        var t = parent.transform.Find(child);
        var img = t ? t.GetComponent<Image>() : null;
        if (img == null) return;
        img.sprite = sprite; img.color = Color.white; img.preserveAspect = false;
    }

    // Untinted, un-squashed. No-ops on a missing image or sprite, so a partial menu bake is harmless.
    static void Paint(Image img, Sprite sprite)
    {
        if (img == null || sprite == null) return;
        img.sprite = sprite; img.color = Color.white; img.preserveAspect = true;
    }

    GameObject FindByName(string n)
    {
        foreach (var t in GetComponentsInChildren<Transform>(true))
            if (t.name == n) return t.gameObject;
        return null;
    }

    // Depth-first search (including inactive) for a descendant by name, scoped to one panel.
    static Transform FindInPanel(Transform root, string n)
    {
        foreach (var t in root.GetComponentsInChildren<Transform>(true))
            if (t != root && t.name == n) return t;
        return null;
    }

    // Open a panel: hide panels + home-only elements, show this panel, set nav.
    void Open(GameObject panel, GameObject navSel)
    {
        HidePanels();
        Set(panel, true);
        SetHomeOnly(false);   // hide gold/settings/no-ads/PLAY while the panel is open
        Sel(navSel);
        Localizer.LocalizeScene(); // panel is active now -> its text is found, tagged and translated to the current language
    }

    // ---- Button hooks (wired by the baker as persistent OnClick events) ----
    public void CloseAll()      { HidePanels(); SetHomeOnly(true); SetNav(true); Sel(navHomeSel); }
    public void ShowHome()      { CloseAll(); }
    public void OpenDaily()     { Open(dailyPanel, navDailySel); }
    public void OpenShop()      { HidePanels(); SetHomeOnly(false); Sel(null); SetNav(false); var s = Shop(); if (s != null) s.Open(); else Set(shopPanel, true); } // nav hidden; the ✕ is the way out
    public void OpenProfile()   { Open(profilePanel, null); }
    public void OpenSettings()  { Open(settingsPanel, null); }
    public void OpenRemoveAds() { Open(removeAdsPanel, null); RefreshRemoveAdsPanelPrices(); } // pull REAL localized prices on every open (at Start IAP wasn't ready yet, so the baked $ labels stayed forever)
    public void OpenAdReward()  { Open(adRewardPanel, null); }
    // Language pop-up: overlay it on top (don't hide the settings panel behind it).
    public void OpenLanguage()
    {
        // (#1) Same language panel the in-game scene uses (LanguageSelector + 9 options). Find the baked popup if the
        // reference was lost, and bring it to the FRONT so it isn't hidden behind the open Settings panel.
        if (languagePanel == null)
        {
            var ls = FindFirstObjectByType<LanguageSelector>(FindObjectsInactive.Include);
            if (ls != null) languagePanel = ls.panelRoot != null ? ls.panelRoot : ls.gameObject;
        }
        if (languagePanel)
        {
            languagePanel.transform.SetAsLastSibling(); // render on TOP of the open Settings panel
            languagePanel.SetActive(true);
            Localizer.LocalizeScene();
        }
    }

    void EnsureLanguageUi()
    {
        if (languagePanel == null)
        {
            var panel = FindByName("Panel_Language");
            if (panel != null) languagePanel = panel;
        }
        if (settingsPanel == null) settingsPanel = FindByName("Panel_Settings");
        if (settingsPanel == null) return;

        Transform trigger = FindInPanel(settingsPanel.transform, "Language")
                         ?? FindInPanel(settingsPanel.transform, "Btn_Language")
                         ?? FindInPanel(settingsPanel.transform, "Btn_Empty1");
        if (trigger == null) return;
        var button = trigger.GetComponent<Button>();
        if (button == null) button = trigger.gameObject.AddComponent<Button>();
        var graphic = trigger.GetComponent<Graphic>();
        if (graphic != null) { graphic.raycastTarget = true; button.targetGraphic = graphic; }
        button.interactable = true;
        button.onClick.RemoveListener(OpenLanguage);
        button.onClick.AddListener(OpenLanguage);
    }

    // Social media buttons: open the pasted link in the device browser.
    public void OpenFacebook()  { OpenUrl(facebookUrl); }
    public void OpenX()         { OpenUrl(xUrl); }
    public void OpenInstagram() { OpenUrl(instagramUrl); }
    public void OpenTikTok()    { OpenUrl(tiktokUrl); }
    public void OpenWebsite()   { OpenUrl(websiteUrl); }
    static void OpenUrl(string url) { if (!string.IsNullOrWhiteSpace(url)) Application.OpenURL(url); }

    // Watch-ad reward: show a rewarded ad; grant 10 gold ONLY when it completes (skip/close -> nothing).
    public void WatchAdReward()
    {
        var ad = AdManager.Instance;
        if (ad != null)
            ad.ShowRewarded("menucoins", onReward: () => { SaveSystem.AddCoins(10); Refresh(); }, onClosedNoReward: null);
        else { SaveSystem.AddCoins(10); Refresh(); } // AdManager yoksa (olmaması lazım) eski davranış
    }

    public void Play() { SceneManager.LoadScene(gameSceneName); }

    // Main-menu GARAGE button: load the game scene and open the garage straight away (closing it returns to the menu).
    public void OpenGarage() { GameUI.OpenGarageOnLoad = true; SceneManager.LoadScene(gameSceneName); } // direct load — no transition effect (user preference)

    // The baked menu has no garage button, so build one at runtime by CLONING Btn_Play — that inherits the exact
    // orange sprite/style of the other menu buttons — then shrink it, park it under PLAY, relabel it GARAGE and
    // rewire it to OpenGarage. Idempotent (skips if a baked/earlier Btn_Garage exists). While the player has not
    // yet done the garage tutorial (PlayerPrefs flag set by the in-game garage tutorial), the button PULSES to
    // draw their eye to it.
    void EnsureGarageButton()
    {
        if (FindByName("Btn_Garage") != null) return;
        var play = FindByName("Btn_Play");
        if (play == null) return;

        var go = Instantiate(play, play.transform.parent);
        go.name = "Btn_Garage";
        var prt = (RectTransform)play.transform;
        var grt = (RectTransform)go.transform;
        grt.localScale = Vector3.one * 0.72f;                                  // smaller than PLAY (secondary action)
        grt.anchoredPosition = prt.anchoredPosition                            // parked just below PLAY
                             + new Vector2(0, -(prt.sizeDelta.y * 0.5f + prt.sizeDelta.y * 0.72f * 0.5f + 26f));

        // Strip any CLONED LocalizedText tags first (they carry Btn_Play's "PLAY" key and would re-translate the
        // label straight back to PLAY on the LocalizeScene pass). Immediate, so the same-frame pass can re-tag the
        // label fresh with its new "GARAGE" key.
        foreach (var lt in go.GetComponentsInChildren<LocalizedText>(true)) DestroyImmediate(lt);
        foreach (var t in go.GetComponentsInChildren<Text>(true)) t.text = "GARAGE"; // Localizer translates on scene pass

        var btn = go.GetComponent<Button>();
        if (btn == null) btn = go.GetComponentInChildren<Button>(true);
        if (btn != null)
        {
            // mute the CLONED persistent OnClick (it still points at Play) and wire the garage instead
            for (int i = 0; i < btn.onClick.GetPersistentEventCount(); i++)
                btn.onClick.SetPersistentListenerState(i, UnityEngine.Events.UnityEventCallState.Off);
            btn.onClick.AddListener(OpenGarage);
        }

        // (No pulse here: the tutorial highlight belongs to the IN-GAME garage button only.)
    }

    // Spend 100 gold (joker purchase). Returns silently if not enough.
    public void BuyFor100() { if (SaveSystem.TrySpend(100)) Refresh(); }

    // THE shop — one hierarchy, one wiring path, shared with the game scene: the ShopUI prefab
    // (Resources/UI/ShopPanel, baked by "Tools ▸ 300Mind UI ▸ Bake Shop Prefab"). ShopUI wires every
    // button itself (coin packs -> real IAP products, no-ads/banner bars, per-joker prices, restore,
    // tap handling), so the menu shop and the in-game shop can never drift apart. The menu only tells
    // it what to do with the menu's own chrome while it is open.
    // The shop, re-hooked if needed: a domain reload (editing a script while in Play) drops the
    // delegates set in SetupShop, and the ✕ would then leave the menu without its bottom nav.
    ShopUI Shop()
    {
        if (shop == null || shop.onClosed == null) SetupShop();
        return shop;
    }

    void SetupShop()
    {
        shop = ShopUI.Ensure();
        if (shop == null) return;
        shopPanel = shop.panel;                  // HidePanels()/CloseAll() still hide it like any other pop-up
        shop.onClosed = CloseAll;                // the ✕ / backdrop -> back to the home screen (nav + home-only elements)
        shop.onCoinsChanged = Refresh;           // repaint the gold counter after a joker purchase
    }

    // The dedicated Remove-Ads popup (Panel_RemoveAds, opened by the top-right no-ads icon) shows its two offers as
    // plain Image graphics ("Image" / "Image 2") with NO Button, so tapping them did nothing. Make each offer ONE
    // clickable button and route it to the right Google Play product: the offer that advertises the "+200" gold
    // bonus = remove_ads_plus (BuyRemoveAdsPlus); the other = remove_ads (BuyRemoveAds).
    void WireRemoveAdsPanel()
    {
        if (removeAdsPanel == null) return;
        foreach (var t in removeAdsPanel.GetComponentsInChildren<Transform>(true))
        {
            System.Action onBuy;
            if (t.name == "Image 3" || t.name == "Offer_RemoveBanner")
            {
                // NEW banner-only offer -> buy remove_banner (turns off ONLY the banner). The user may have dropped in
                // just an Image, so make the WHOLE graphic clickable (add a Button if it has none) as well as wiring any
                // inner button, so it fires however they built it.
                onBuy = BuyRemoveBanner;
                var root = t.GetComponent<Button>();
                if (root == null && t.GetComponent<Graphic>() != null) root = t.gameObject.AddComponent<Button>();
                if (root != null) { root.onClick = new Button.ButtonClickedEvent(); root.onClick.AddListener(() => onBuy()); }
            }
            else if (t.name == "Image" || t.name == "Image 2" || t.name == "Offer_RemoveAds" || t.name == "Offer_RemoveAdsPlus")
            {
                // Which product? the "+200" gold bonus marks the remove_ads_plus tier; the other is plain remove_ads.
                bool isPlus = t.name == "Offer_RemoveAdsPlus";
                foreach (var txt in t.GetComponentsInChildren<Text>(true))
                    if (txt.text != null && txt.text.Contains("200")) { isPlus = true; break; }
                onBuy = isPlus ? (System.Action)BuyRemoveAdsPlus : BuyRemoveAds;
            }
            else continue;

            // Wire the small green Button(s) INSIDE this offer to the purchase. The big offer graphic root is not
            // wired here (for Image 3 it was made clickable above); the orange remove_ads graphics stay non-clickable.
            foreach (var green in t.GetComponentsInChildren<Button>(true))
            {
                if (green.transform == t) continue;                 // never the big offer root itself
                green.onClick = new Button.ButtonClickedEvent();
                green.onClick.AddListener(() => onBuy());
            }
        }

        var restore = FindInPanel(removeAdsPanel.transform, "RestorePurchases");
        if (restore != null)
        {
            var button = restore.GetComponent<Button>();
            if (button == null) button = restore.gameObject.AddComponent<Button>();
            var graphic = restore.GetComponent<Graphic>();
            if (graphic != null) button.targetGraphic = graphic;
            button.onClick = new Button.ButtonClickedEvent();
            button.onClick.AddListener(RestorePurchases);
        }
    }

    // Write the REAL localized store price onto one Remove-Ads-popup offer graphic. ONLY texts that already look like
    // a price ("$" / "₺" / "TL") are replaced — the "Banner" caption and the "+200" bonus label are never touched.
    // When the store price isn't available (editor / IAP still starting) the baked label is left as-is.
    void SetOfferPrice(Transform offer, string productId)
    {
        string real = null;
#if !UNITY_EDITOR
        real = IAPManager.Instance != null ? IAPManager.Instance.Price(productId) : null;
#endif
        if (string.IsNullOrEmpty(real)) return;
        foreach (var txt in offer.GetComponentsInChildren<Text>(true))
            if (txt.text != null && (txt.text.Contains("$") || txt.text.Contains("₺") || txt.text.Contains("TL")))
                txt.text = real;
        foreach (var tmp in offer.GetComponentsInChildren<TMPro.TMP_Text>(true))
            if (tmp.text != null && (tmp.text.Contains("$") || tmp.text.Contains("₺") || tmp.text.Contains("TL")))
                tmp.text = real;
    }

    // The dedicated REKLAMLARI KALDIR popup: put the real localized prices on its three offers. Called on every
    // popup OPEN (WireRemoveAdsPanel at Start only wires the BUY buttons; it ran before IAP was ready, which is why
    // the baked "$" prices never localized). The "+200" check EXCLUDES price-looking texts so a real price that
    // happens to contain "200" (e.g. ₺200,99) can never flip an offer's product.
    void RefreshRemoveAdsPanelPrices()
    {
        if (removeAdsPanel == null) return;
        foreach (var t in removeAdsPanel.GetComponentsInChildren<Transform>(true))
        {
            if (t.name == "Image 3" || t.name == "Offer_RemoveBanner") SetOfferPrice(t, IAPManager.RemoveBanner);
            else if (t.name == "Image" || t.name == "Image 2" || t.name == "Offer_RemoveAds" || t.name == "Offer_RemoveAdsPlus")
            {
                bool isPlus = t.name == "Offer_RemoveAdsPlus";
                foreach (var txt in t.GetComponentsInChildren<Text>(true))
                    if (txt.text != null && !txt.text.Contains("$") && !txt.text.Contains("₺") && !txt.text.Contains("TL")
                        && txt.text.Contains("200")) { isPlus = true; break; }
                SetOfferPrice(t, isPlus ? IAPManager.RemoveAdsPlus : IAPManager.RemoveAds);
            }
        }
    }

    // No-ads + restore for the menu's Remove-Ads panel — wire these to its buttons in the Inspector. The
    // entitlement and the "plus" one-time bonus are applied inside IAPManager; Restore re-grants after a
    // reinstall (a Google Play policy requirement).
    public void BuyRemoveAds()     { IAPManager.Instance?.Buy(IAPManager.RemoveAds); }
    public void BuyRemoveAdsPlus() { IAPManager.Instance?.Buy(IAPManager.RemoveAdsPlus); }
    public void BuyRemoveBanner()  { IAPManager.Instance?.Buy(IAPManager.RemoveBanner); }
    public void RestorePurchases() { IAPManager.Instance?.Restore(); }
}
