using UnityEngine;
using UnityEngine.UI;
using UnityEngine.SceneManagement;
using BusJam;

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

    void Start()
    {
        AdManager.Ensure(); // create the ad singleton in the menu too, so the "watch ad → gold" button works here
        homeOnly = new[]
        {
            FindByName("Coin_Bar"),    // gold counter
            FindByName("Btn_Settings"),// settings gear
            FindByName("Btn_NoAds"),   // no-ads icon
            FindByName("Btn_AdReward"),// watch-ad-for-gold
            FindByName("Btn_Play"),    // PLAY button
            FindByName("Btn_Garage"),  // GARAGE button (skins / chests)
        };
        CloseAll();
        Refresh();
        WireShop();            // the baked menu shop's coin/joker buttons have NO listeners in this scene -> wire them now
        WireRemoveAdsPanel();  // the dedicated Remove-Ads popup's offer graphics have NO listeners -> wire them to IAP
        EnsureSettingsClose(); // (Settings pop-up) add a red ✕ close button (top-right), wired to ShowHome
        // Start menu music now ONLY if we're not in the launch splash — on first boot the BootSplash starts it at
        // the LOADING screen (not on the Intake logo). When returning here from gameplay there's no splash, so play.
        if (Object.FindAnyObjectByType<BootSplash>() == null)
            MusicManager.PlayMenu(); // main-menu background music (track 1 by default)
        // (#2) Menu background is owned by the self-spawning MenuBackground.cs (animated coast-sunset), which
        // disables the static "Background" object itself. We deliberately DON'T override it here anymore — that
        // was a second background fighting the animated one. One source of truth now.
        Localizer.LocalizeScene(); // translate all baked menu text to the saved language
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
    public void CloseAll()      { HidePanels(); SetHomeOnly(true); Sel(navHomeSel); }
    public void ShowHome()      { CloseAll(); }
    public void OpenDaily()     { Open(dailyPanel, navDailySel); }
    public void OpenShop()      { Open(shopPanel, navShopSel); }
    public void OpenProfile()   { Open(profilePanel, null); }
    public void OpenSettings()  { Open(settingsPanel, null); }
    public void OpenRemoveAds() { Open(removeAdsPanel, null); }
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
    public void OpenGarage() { GameUI.OpenGarageOnLoad = true; SceneManager.LoadScene(gameSceneName); }

    // Spend 100 gold (joker purchase). Returns silently if not enough.
    public void BuyFor100() { if (SaveSystem.TrySpend(100)) Refresh(); }

    // Wire the baked menu shop. Unlike the in-game shop (which GameUI.WireSceneShop hooks up), the menu baker
    // leaves the coin packs and the no-ads price button WITHOUT any purchase listener, so taps did nothing
    // ("purchases don't work in the main menu shop"). We wire them here:
    //   • coin packs  -> real Google Play IAP (matched by the baked "Pack_<amount>" card name)
    //   • no-ads bars -> ONLY the green price button buys; tapping the orange bar does nothing
    // Joker bars already carry a baked BuyFor100 listener and Close carries CloseAll, so those are left alone.
    // We also make the shop card block taps so tapping a package can't fall through to the dim backdrop (close).
    void WireShop()
    {
        if (shopPanel == null) return;

        // (a) Any InGameShopButton-tagged buttons (kept for forward-compat; today's menu bake has none).
        foreach (var b in shopPanel.GetComponentsInChildren<InGameShopButton>(true))
        {
            var btn = b.GetComponent<Button>();
            if (btn == null) continue;
            switch (b.action)
            {
                case InGameShopButton.Act.GrantCoins:
                    int amt = b.amount;
                    btn.onClick.AddListener(() => BuyCoinsPack(amt));
                    break;
                case InGameShopButton.Act.SpendJoker:
                    btn.onClick.AddListener(() => { if (SaveSystem.TrySpend(100)) Refresh(); });
                    break;
            }
        }

        // (b) Coin packs by name: each card is "Pack_<amount>" with an unwired child "Buy" button.
        foreach (var t in shopPanel.GetComponentsInChildren<Transform>(true))
        {
            if (t == null || !t.name.StartsWith("Pack_")) continue;
            if (t.GetComponent<InGameShopButton>() != null) continue;            // already wired in (a)
            if (!int.TryParse(t.name.Substring(5), out int coins)) continue;
            var buyT = FindInPanel(t, "Buy");
            var buy = buyT != null ? buyT.GetComponent<Button>() : null;
            if (buy == null) continue;
            buy.onClick.AddListener(() => BuyCoinsPack(coins));
        }

        // (c) No-ads bars: wire ONLY the green price button so tapping the orange bar does nothing.
        WireMenuPromo("RemoveAds", BuyRemoveAds);
        WireMenuPromo("RemoveAds (1)", BuyRemoveAdsPlus);

        // (c2) Joker bars: charge the correct per-joker price (Recolor 75 / Swap 50 / Heli 100) AND grant the joker.
        // The baker wired all three to BuyFor100 (spend 100, grant nothing) -> override here. Handles duplicates.
        foreach (var t in shopPanel.GetComponentsInChildren<Transform>(true))
        {
            int jkind = JokerBarKind(t.name);
            if (jkind >= 0) WireMenuJoker(t, jkind);
        }

        // (c3) The baked menu shop has TWO coin-pack sets (12 cards). Keep only the canonical 6 that match the in-game
        // shop (200/500/1300/2500/4000/5500), once each; HIDE the extra standard set + duplicates. Non-destructive
        // (SetActive false) — the cards stay in the scene, just hidden at runtime.
        int[] keepPacks = { 200, 500, 1300, 2500, 4000, 5500 };
        var seenPacks = new System.Collections.Generic.List<int>();
        foreach (var t in shopPanel.GetComponentsInChildren<Transform>(true))
        {
            if (t == null || !t.name.StartsWith("Pack_") || !int.TryParse(t.name.Substring(5), out int packCoins)) continue;
            bool showPack = System.Array.IndexOf(keepPacks, packCoins) >= 0 && !seenPacks.Contains(packCoins);
            if (showPack) seenPacks.Add(packCoins);
            t.gameObject.SetActive(showPack);
        }

        // (d) Only the empty black backdrop (and the Home/Daily nav) close the shop. Force every background/card/
        // row image to catch taps so tapping a package (the red/orange cards) can't fall through to the close-
        // backdrop. Buttons and the icons/labels parented under them are left alone so they still work.
        BlockShopBackgroundTaps(shopPanel.transform);
    }

    // (Shop close) Make every non-button background/card/row image in the shop a raycast target, so a tap on a
    // package can't pass through to the dim backdrop that closes the shop. A button graphic — and an icon/label
    // parented DIRECTLY under a button — is skipped so its taps still reach the button.
    void BlockShopBackgroundTaps(Transform shopRoot)
    {
        foreach (var img in shopRoot.GetComponentsInChildren<Image>(true))
        {
            if (img.transform == shopRoot) continue;
            var p = img.transform.parent;
            if (p != null && p != shopRoot && p.GetComponent<Button>() != null && img.GetComponent<Button>() == null)
                continue;
            img.raycastTarget = true;
        }

        // THE REAL FIX: a click on a card with NO handler bubbles up to the first ancestor handler — the dim
        // backdrop's CloseAll Button — closing the shop (raycastTarget on the card does NOT stop the bubble). Put a
        // no-op click consumer on each scroll Viewport and on the Card so taps inside the shop are swallowed there
        // and never bubble to the backdrop. Drags still scroll (the ScrollRect handles those separately).
        foreach (var sr in shopRoot.GetComponentsInChildren<ScrollRect>(true))
        {
            var vp = sr.viewport != null ? sr.viewport : sr.transform.Find("Viewport") as RectTransform;
            if (vp != null) AddClickConsumer(vp.gameObject);
        }
        var cardT = FindInPanel(shopRoot, "Card");
        if (cardT != null) AddClickConsumer(cardT.gameObject);
    }

    // Swallow a click that bubbled up to this object so it can't reach the dim backdrop's CloseAll above it. A
    // Button with no onClick listeners consumes the click and does nothing else; a near-invisible raycast image is
    // added if the object has no graphic. Does NOT block ScrollRect dragging.
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

    // Wire ONLY the green price button ("PriceBg" child) of a no-ads bar to its purchase, leaving the orange bar
    // background as a plain tap-blocker (so tapping it neither buys nor closes the shop). Safe if row/child absent.
    void WireMenuPromo(string rowName, System.Action onBuy)
    {
        if (shopPanel == null) return;
        var row = FindInPanel(shopPanel.transform, rowName);
        if (row == null) return;
        var rowImg = row.GetComponent<Image>();
        if (rowImg != null) rowImg.raycastTarget = true;
        var rowBtn = row.GetComponent<Button>();
        if (rowBtn != null) rowBtn.onClick = new Button.ButtonClickedEvent();  // the whole bar must never buy
        var priceT = FindInPanel(row, "PriceBg");
        var target = priceT != null ? priceT : row;                           // fallback: keep buying working
        var pImg = target.GetComponent<Image>();
        if (pImg != null) pImg.raycastTarget = true;
        var pBtn = target.GetComponent<Button>();
        if (pBtn == null) pBtn = target.gameObject.AddComponent<Button>();
        if (pImg != null) pBtn.targetGraphic = pImg;
        pBtn.onClick = new Button.ButtonClickedEvent();
        pBtn.onClick.AddListener(() => onBuy());
    }

    // A shop joker bar -> joker kind by row name ("Bar_Shuffle"=Recolor 0, "Bar_Swap"=Swap 1, "Bar_Heli"=Heli 2).
    static int JokerBarKind(string name)
    {
        if (string.IsNullOrEmpty(name) || !name.StartsWith("Bar_")) return -1;
        var n = name.ToLowerInvariant();
        if (n.Contains("heli")) return 2;
        if (n.Contains("swap")) return 1;
        if (n.Contains("shuffle") || n.Contains("recolor")) return 0;
        return -1;
    }

    // Charge the correct per-joker price (Recolor 75 / Swap 50 / Heli 100 from GameConfig) and GRANT the joker. The
    // menu baker had wired every joker bar to BuyFor100 (spend 100, grant nothing); this replaces that and updates
    // the price label. The granted joker is stored in SaveSystem and shows on the in-game HUD.
    void WireMenuJoker(Transform row, int kind)
    {
        var buyT = FindInPanel(row, "Buy");
        var buy = buyT != null ? buyT.GetComponent<Button>() : null;
        if (buy == null) return;
        int cost = kind == 1 ? GameConfig.SwapCost : kind == 2 ? GameConfig.HeliCost : GameConfig.RecolorCost;
        var priceT = FindInPanel(buy.transform, "Price");
        var priceTxt = priceT != null ? priceT.GetComponent<Text>() : null;
        if (priceTxt != null) priceTxt.text = cost.ToString();
        buy.onClick = new Button.ButtonClickedEvent();                     // drop the baked BuyFor100
        buy.onClick.AddListener(() => { if (SaveSystem.TrySpend(cost)) { SaveSystem.AddFreeJoker(kind, 1); Refresh(); } });
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
            if (t.name != "Image" && t.name != "Image 2") continue;

            // Which product? the "+200" gold bonus marks the remove_ads_plus tier; the other is plain remove_ads.
            bool isPlus = false;
            foreach (var txt in t.GetComponentsInChildren<Text>(true))
                if (txt.text != null && txt.text.Contains("200")) { isPlus = true; break; }
            System.Action onBuy = isPlus ? (System.Action)BuyRemoveAdsPlus : BuyRemoveAds;

            // Wire ONLY the small green Button(s) inside this offer to the purchase. Nothing else is touched — the
            // big orange offer graphic has no Button so it stays non-clickable on its own.
            foreach (var green in t.GetComponentsInChildren<Button>(true))
            {
                if (green.transform == t) continue;                 // never the big orange offer root itself
                green.onClick = new Button.ButtonClickedEvent();
                green.onClick.AddListener(() => onBuy());
            }
        }
    }

    // A coin-pack button -> the matching consumable IAP. Coins are added by IAPManager on a verified purchase;
    // Update()'s Refresh() repaints the counter next frame.
    void BuyCoinsPack(int coins)
    {
        var id = IAPManager.ProductForCoins(coins);
        if (id != null) IAPManager.Instance?.Buy(id);
        else Debug.LogWarning("[Menu] no IAP product for " + coins + " coins");
    }

    // No-ads + restore for the menu's Remove-Ads panel — wire these to its buttons in the Inspector. The
    // entitlement and the "plus" one-time bonus are applied inside IAPManager; Restore re-grants after a
    // reinstall (a Google Play policy requirement).
    public void BuyRemoveAds()     { IAPManager.Instance?.Buy(IAPManager.RemoveAds); }
    public void BuyRemoveAdsPlus() { IAPManager.Instance?.Buy(IAPManager.RemoveAdsPlus); }
    public void RestorePurchases() { IAPManager.Instance?.Restore(); }
}
