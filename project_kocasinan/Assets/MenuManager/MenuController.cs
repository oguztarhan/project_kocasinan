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
        };
        CloseAll();
        Refresh();
        // (#2) Menu background is owned by the self-spawning MenuBackground.cs (animated coast-sunset), which
        // disables the static "Background" object itself. We deliberately DON'T override it here anymore — that
        // was a second background fighting the animated one. One source of truth now.
        Localizer.LocalizeScene(); // translate all baked menu text to the saved language
        BuildWebsiteButton();      // (website) runtime-create the WEB button — you only need to paste the URL above
    }

    void Update() { Refresh(); }

    public void Refresh() { if (coinText) coinText.text = SaveSystem.Coins.ToString(); }

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

    // Creates the WEBSITE button at runtime (top-left corner) wired to OpenWebsite, so NO re-bake is needed — you
    // just paste your link into the websiteUrl field above. Tapping it does nothing until a URL is set.
    void BuildWebsiteButton()
    {
        var canvas = GetComponentInChildren<Canvas>();
        if (canvas == null) return;
        var font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        // Borrow a rounded button sprite from an existing baked menu button (BEFORE we create ours, so we don't pick
        // up our own). The old GetBuiltinResource("UI/Skin/UISprite.psd") isn't available in this Unity version.
        Sprite btnSprite = null; Image.Type btnType = Image.Type.Sliced;
        var sample = canvas.GetComponentInChildren<Button>(true);
        if (sample != null) { var si = sample.GetComponent<Image>(); if (si != null) { btnSprite = si.sprite; btnType = si.type; } }
        var go = new GameObject("Btn_Website", typeof(RectTransform), typeof(Image), typeof(Button));
        go.transform.SetParent(canvas.transform, false);
        var rt = (RectTransform)go.transform;
        rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0f, 1f);    // top-left corner
        rt.anchoredPosition = new Vector2(120f, -120f);
        rt.sizeDelta = new Vector2(150f, 150f);
        var img = go.GetComponent<Image>();
        if (btnSprite != null) { img.sprite = btnSprite; img.type = btnType; } // rounded like the rest; otherwise a plain solid rect
        img.color = new Color(0.30f, 0.55f, 0.92f);
        go.GetComponent<Button>().onClick.AddListener(OpenWebsite);
        var lblGo = new GameObject("Label", typeof(RectTransform), typeof(Text));
        lblGo.transform.SetParent(go.transform, false);
        var lrt = (RectTransform)lblGo.transform;
        lrt.anchorMin = Vector2.zero; lrt.anchorMax = Vector2.one; lrt.offsetMin = Vector2.zero; lrt.offsetMax = Vector2.zero;
        var lbl = lblGo.GetComponent<Text>();
        lbl.text = "WEB"; lbl.font = font; lbl.fontSize = 40; lbl.fontStyle = FontStyle.Bold;
        lbl.alignment = TextAnchor.MiddleCenter; lbl.color = Color.white;
    }

    // Watch-ad reward: show a rewarded ad; grant 10 gold ONLY when it completes (skip/close -> nothing).
    public void WatchAdReward()
    {
        var ad = AdManager.Instance;
        if (ad != null)
            ad.ShowRewarded("menucoins", onReward: () => { SaveSystem.AddCoins(10); Refresh(); }, onClosedNoReward: null);
        else { SaveSystem.AddCoins(10); Refresh(); } // AdManager yoksa (olmaması lazım) eski davranış
    }

    public void Play() { SceneManager.LoadScene(gameSceneName); }

    // Spend 100 gold (joker purchase). Returns silently if not enough.
    public void BuyFor100() { if (SaveSystem.TrySpend(100)) Refresh(); }

    // Currency cheats / store buttons can call these directly from the Inspector.
    public void AddCoins100()  { SaveSystem.AddCoins(100);  Refresh(); }
    public void AddCoins500()  { SaveSystem.AddCoins(500);  Refresh(); }
    public void AddCoins1000() { SaveSystem.AddCoins(1000); Refresh(); }
}
