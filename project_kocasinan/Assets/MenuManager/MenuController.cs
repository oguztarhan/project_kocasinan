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

    // Spend 100 gold (joker purchase). Returns silently if not enough.
    public void BuyFor100() { if (SaveSystem.TrySpend(100)) Refresh(); }

    // Currency cheats / store buttons can call these directly from the Inspector.
    public void AddCoins100()  { SaveSystem.AddCoins(100);  Refresh(); }
    public void AddCoins500()  { SaveSystem.AddCoins(500);  Refresh(); }
    public void AddCoins1000() { SaveSystem.AddCoins(1000); Refresh(); }
}
