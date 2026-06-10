using UnityEngine;
using UnityEngine.UI;
using UnityEngine.SceneManagement;
using BusJam;

/// <summary>
/// Behaviour driver for the SCENE-AUTHORED main menu produced by the editor tool
/// "Tools ▸ 300Mind UI ▸ Bake Main Menu". The bake step creates the visual objects
/// and assigns the references below, so you can freely edit every element's colour,
/// size, position and font in the Inspector — this script only handles the logic
/// (open/close panels, currency text, PLAY). Nothing here generates graphics.
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

    void Start() { CloseAll(); SelHome(); Refresh(); }
    void Update() { Refresh(); }

    public void Refresh() { if (coinText) coinText.text = SaveSystem.Coins.ToString(); }

    void Set(GameObject g, bool on) { if (g) g.SetActive(on); }
    public void CloseAll()
    {
        Set(dailyPanel, false); Set(shopPanel, false); Set(profilePanel, false);
        Set(settingsPanel, false); Set(removeAdsPanel, false);
    }

    void Sel(GameObject g)
    {
        Set(navDailySel, g == navDailySel);
        Set(navHomeSel,  g == navHomeSel);
        Set(navShopSel,  g == navShopSel);
    }
    void SelHome() { Sel(navHomeSel); }

    // ---- Button hooks (wired by the baker as persistent OnClick events) ----
    public void ShowHome()      { CloseAll(); Sel(navHomeSel); }
    public void OpenDaily()     { CloseAll(); Set(dailyPanel, true);   Sel(navDailySel); }
    public void OpenShop()      { CloseAll(); Set(shopPanel, true);    Sel(navShopSel); }
    public void OpenProfile()   { CloseAll(); Set(profilePanel, true); }
    public void OpenSettings()  { CloseAll(); Set(settingsPanel, true); }
    public void OpenRemoveAds() { CloseAll(); Set(removeAdsPanel, true); }

    public void Play() { SceneManager.LoadScene(gameSceneName); }

    // Currency cheats / store buttons can call these directly from the Inspector.
    public void AddCoins100()  { SaveSystem.AddCoins(100);  Refresh(); }
    public void AddCoins500()  { SaveSystem.AddCoins(500);  Refresh(); }
    public void AddCoins1000() { SaveSystem.AddCoins(1000); Refresh(); }
}
