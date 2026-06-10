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

    [Header("Bottom-nav selected highlights (orange backing)")]
    [SerializeField] public GameObject navDailySel;
    [SerializeField] public GameObject navHomeSel;
    [SerializeField] public GameObject navShopSel;

    [Header("Scene")]
    [SerializeField] public string gameSceneName = "SampleScene";

    // Home-only elements (found by name in the baked hierarchy); hidden while a panel is open.
    GameObject[] homeOnly;

    void Start()
    {
        homeOnly = new[]
        {
            FindByName("Coin_Bar"),    // gold counter
            FindByName("Btn_Settings"),// settings gear
            FindByName("Btn_NoAds"),   // no-ads icon
            FindByName("Btn_Play"),    // PLAY button
        };
        CloseAll();
        Refresh();
    }

    void Update() { Refresh(); }

    public void Refresh() { if (coinText) coinText.text = SaveSystem.Coins.ToString(); }

    void Set(GameObject g, bool on) { if (g) g.SetActive(on); }

    void HidePanels()
    {
        Set(dailyPanel, false); Set(shopPanel, false); Set(profilePanel, false);
        Set(settingsPanel, false); Set(removeAdsPanel, false);
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
    }

    // ---- Button hooks (wired by the baker as persistent OnClick events) ----
    public void CloseAll()      { HidePanels(); SetHomeOnly(true); Sel(navHomeSel); }
    public void ShowHome()      { CloseAll(); }
    public void OpenDaily()     { Open(dailyPanel, navDailySel); }
    public void OpenShop()      { Open(shopPanel, navShopSel); }
    public void OpenProfile()   { Open(profilePanel, null); }
    public void OpenSettings()  { Open(settingsPanel, null); }
    public void OpenRemoveAds() { Open(removeAdsPanel, null); }

    public void Play() { SceneManager.LoadScene(gameSceneName); }

    // Spend 100 gold (joker purchase). Returns silently if not enough.
    public void BuyFor100() { if (SaveSystem.TrySpend(100)) Refresh(); }

    // Currency cheats / store buttons can call these directly from the Inspector.
    public void AddCoins100()  { SaveSystem.AddCoins(100);  Refresh(); }
    public void AddCoins500()  { SaveSystem.AddCoins(500);  Refresh(); }
    public void AddCoins1000() { SaveSystem.AddCoins(1000); Refresh(); }
}
