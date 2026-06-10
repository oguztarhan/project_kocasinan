using System.Collections;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.SceneManagement;
using BusJam; // UIKit, SaveSystem, UISprites

/// <summary>
/// Code-built Main Menu styled with the 300Mind "2D Game UI Kit" (sprites resolved
/// through <see cref="UIKit"/>). Auto-bootstraps in the MainMenu scene (Play mode and
/// an editor preview). Screens: Home (PLAY + no-ads), Daily Rewards, Shop, Profile,
/// Settings, Remove-Ads. The old scene-authored canvas is disabled at runtime so the
/// legacy white background / coin / texts never show behind the new menu.
/// </summary>
public class MenuView : MonoBehaviour
{
    const string GameScene = "SampleScene";

    static readonly Color Dim    = new Color(0, 0, 0, 0.6f);
    static readonly Color White  = Color.white;
    static readonly Color Orange = new Color(1f, 0.62f, 0.15f);
    static readonly Color Gold   = new Color(1f, 0.85f, 0.30f);
    static readonly Color Dark   = new Color(0.16f, 0.20f, 0.30f);
    static readonly Color OnCol  = new Color(0.35f, 0.85f, 0.40f);
    static readonly Color OffCol = new Color(0.65f, 0.65f, 0.70f);

    Font title, num;
    Transform root;
    GameObject homeView, dailyPanel, shopPanel, profilePanel, settingsPanel, removeAdsPanel;
    Text coinText;
    readonly RectTransform[] navRt = new RectTransform[3]; // 0=daily 1=home 2=shop
    readonly Image[] navBg = new Image[3];

    // ---- Bootstrap ------------------------------------------------------
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Bootstrap()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
        SceneManager.sceneLoaded += OnSceneLoaded;
        Ensure();
    }
    static void OnSceneLoaded(Scene s, LoadSceneMode m) => Ensure();
    static void Ensure()
    {
        if (SceneManager.GetActiveScene().name != "MainMenu") return;
        if (FindAnyObjectByType<MenuView>() != null) return;
        new GameObject("MenuView").AddComponent<MenuView>().Build();
    }

#if UNITY_EDITOR
    static MenuView s_preview;
    [UnityEditor.InitializeOnLoadMethod]
    static void EditorBoot()
    {
        UnityEditor.EditorApplication.update -= EditorTick;
        UnityEditor.EditorApplication.update += EditorTick;
        UnityEditor.EditorApplication.playModeStateChanged -= OnPlayMode;
        UnityEditor.EditorApplication.playModeStateChanged += OnPlayMode;
    }
    static void OnPlayMode(UnityEditor.PlayModeStateChange st)
    {
        if (st == UnityEditor.PlayModeStateChange.ExitingEditMode && s_preview != null)
        { DestroyImmediate(s_preview.gameObject); s_preview = null; }
    }
    static void EditorTick()
    {
        if (Application.isPlaying) return;
        if (s_preview != null) return;
        if (SceneManager.GetActiveScene().name != "MainMenu") return;
        var go = new GameObject("MenuView (Preview)") { hideFlags = HideFlags.DontSave };
        var mv = go.AddComponent<MenuView>();
        mv.preview = true;
        s_preview = mv;
        mv.Build();
    }
    bool preview;
#endif

    void Update()
    {
        if (SceneManager.GetActiveScene().name != "MainMenu") { Destroy(gameObject); return; }
        if (coinText) coinText.text = SaveSystem.Coins.ToString();
    }

    // ---- Build ----------------------------------------------------------
    public void Build()
    {
        title = UIKit.Title();
        num   = UIKit.Num();

        var canvasGo = new GameObject("MenuCanvas");
        canvasGo.transform.SetParent(transform, false);
        var canvas = canvasGo.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 500;
        var sc = canvasGo.AddComponent<CanvasScaler>();
        sc.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        sc.referenceResolution = new Vector2(1080, 1920);
        sc.matchWidthOrHeight = 0.5f;
        canvasGo.AddComponent<GraphicRaycaster>();
        root = canvasGo.transform;

        if (Application.isPlaying && EventSystem.current == null)
        {
            var es = new GameObject("EventSystem");
            es.transform.SetParent(transform, false);
            es.AddComponent<EventSystem>();
            es.AddComponent<InputSystemUIInputModule>().AssignDefaultActions();
        }

        BuildHome();
        BuildDaily();
        BuildShop();
        BuildProfile();
        BuildSettings();
        BuildRemoveAds();
        BuildTopBar();
        BuildBottomNav();

        ShowHome();          // all pop-ups hidden; nav starts all-blue (no selection)
        DisableOldCanvases(); // hide the legacy scene canvas (white bg / old coin / texts)

#if UNITY_EDITOR
        if (preview)
            foreach (var t in root.GetComponentsInChildren<Transform>(true))
                t.gameObject.hideFlags = HideFlags.DontSave;
#endif
    }

    // Hide every canvas that doesn't belong to this overlay (runtime only) so the
    // old editor-authored menu (white image, old coin, texts) never shows behind us.
    void DisableOldCanvases()
    {
        if (!Application.isPlaying) return;
        foreach (var c in Object.FindObjectsByType<Canvas>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
        {
            if (c == null) continue;
            if (c.transform.root == transform.root) continue; // ours
            c.gameObject.SetActive(false);
        }
    }

    // ---- Home -----------------------------------------------------------
    void BuildHome()
    {
        homeView = Panel("Home", new Color(0, 0, 0, 0));
        homeView.GetComponent<Image>().raycastTarget = false;

        var play = Btn(homeView.transform, UIKit.PlayBtn(), new Color(0.30f, 0.75f, 0.35f),
            new Vector2(0.5f, 0.5f), new Vector2(0, -120), new Vector2(460, 180), Play);
        Label(play.transform, "PLAY", title, Vector2.zero, new Vector2(460, 120), 70, White);

        Btn(homeView.transform, UIKit.NoAds(), new Color(0.85f, 0.30f, 0.30f),
            new Vector2(1, 1), new Vector2(-110, -360), new Vector2(150, 150), OpenRemoveAds);
    }

    // ---- Top bar --------------------------------------------------------
    void BuildTopBar()
    {
        // Gold counter (left): bar + coin icon + green "+" right next to the coin.
        var coinBtn = Btn(root, UIKit.CoinBar(), Dark, new Vector2(0, 1), new Vector2(200, -95), new Vector2(300, 96), OpenShop);
        var ci = Img(coinBtn.transform, UIKit.Coin(), Gold); ci.raycastTarget = false;
        Place(ci.rectTransform, new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(42, 0), new Vector2(74, 74));
        var cp = Img(coinBtn.transform, UIKit.PlusGreen(), new Color(0.3f, 0.8f, 0.35f)); cp.raycastTarget = false;
        Place(cp.rectTransform, new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(88, -22), new Vector2(46, 46));
        coinText = Label(coinBtn.transform, "0", num, new Vector2(45, 0), new Vector2(170, 60), 44, White, TextAnchor.MiddleCenter);

        // (Center profile button removed by request — it rendered as a yellow circle
        // with a white square. Re-add later with a proper avatar sprite if wanted;
        // OpenProfile() and the profile panel are still in place.)

        // Settings gear (top-right).
        Btn(root, UIKit.Gear(), new Color(0.7f, 0.72f, 0.78f), new Vector2(1, 1), new Vector2(-90, -100), new Vector2(120, 120), OpenSettings);
    }

    // ---- Bottom nav (blue strip; selected = orange + raised, animated) ---
    void BuildBottomNav()
    {
        var strip = Img(root, UIKit.NavStrip(), new Color(0.18f, 0.42f, 0.85f));
        strip.rectTransform.anchorMin = new Vector2(0, 0); strip.rectTransform.anchorMax = new Vector2(1, 0);
        strip.rectTransform.pivot = new Vector2(0.5f, 0);
        strip.rectTransform.offsetMin = Vector2.zero; strip.rectTransform.offsetMax = new Vector2(0, 200);

        NavButton(strip.transform, 0, -340, UIKit.NavDaily(), () => { OpenDaily(); SelectNav(0); });
        NavButton(strip.transform, 1, 0,    UIKit.NavHome(),  () => { ShowHome(); SelectNav(1); });
        NavButton(strip.transform, 2, 340,  UIKit.NavShop(),  () => { OpenShop(); SelectNav(2); });
    }

    void NavButton(Transform parent, int idx, float x, Sprite icon, System.Action onClick)
    {
        var holder = new GameObject("Nav", typeof(RectTransform));
        holder.transform.SetParent(parent, false);
        var hrt = holder.GetComponent<RectTransform>();
        hrt.anchorMin = hrt.anchorMax = new Vector2(0.5f, 0); hrt.pivot = new Vector2(0.5f, 0);
        hrt.anchoredPosition = new Vector2(x, 15); hrt.sizeDelta = new Vector2(170, 170);

        // Orange backing like the original look: ONLY visible while selected, so the
        // unselected icons sit clean on the blue strip (no tinted box behind them).
        var bg = Img(holder.transform, UIKit.NavBtnBg(), Orange);
        bg.color = Orange;
        Center(bg.rectTransform, new Vector2(160, 160));
        bg.rectTransform.anchoredPosition = new Vector2(0, 20);
        bg.raycastTarget = false;
        bg.gameObject.SetActive(false);

        var btn = Btn(holder.transform, icon, White, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(115, 115), onClick);
        btn.GetComponent<Image>().raycastTarget = true;

        navRt[idx] = hrt; navBg[idx] = bg;
    }

    void SelectNav(int idx)
    {
        for (int i = 0; i < 3; i++)
        {
            if (navRt[i] == null) continue;
            bool sel = i == idx;
            if (navBg[i]) navBg[i].gameObject.SetActive(sel); // orange backing only on the selected one
            float targetY = sel ? 48f : 15f;
            if (Application.isPlaying) StartCoroutine(NavMove(navRt[i], targetY));
            else navRt[i].anchoredPosition = new Vector2(navRt[i].anchoredPosition.x, targetY);
        }
    }

    IEnumerator NavMove(RectTransform rt, float targetY)
    {
        Vector2 start = rt.anchoredPosition;
        float e = 0f, dur = 0.15f;
        while (e < dur && rt != null)
        {
            e += Time.deltaTime;
            float k = Mathf.Clamp01(e / dur);
            rt.anchoredPosition = new Vector2(start.x, Mathf.Lerp(start.y, targetY, k));
            yield return null;
        }
        if (rt != null) rt.anchoredPosition = new Vector2(start.x, targetY);
    }

    // ---- Daily Rewards ----------------------------------------------------
    void BuildDaily()
    {
        dailyPanel = Panel("Daily", Dim);
        TapOutside(dailyPanel, GoHome);

        var card = Img(dailyPanel.transform, UIKit.PanelCyan(), new Color(0.55f, 0.82f, 0.95f));
        Center(card.rectTransform, new Vector2(900, 1300));

        Label(card.transform, "DAILY REWARDS", title, new Vector2(0, 520), new Vector2(820, 120), 70, White);
        Label(card.transform, "Come back every day to get great rewards", num, new Vector2(0, 420), new Vector2(760, 60), 30, White);

        int[] coins = { 100, 150, 200, 500, 750, 1000 };
        int claimedDays = 3; // demo: first 3 already claimed
        float[] cx = { -270, 0, 270 };
        for (int i = 0; i < 6; i++)
        {
            int day = i + 1;
            int col = i % 3, rowi = i / 3;
            var c = Img(card.transform, UIKit.CardCream(), new Color(0.95f, 0.96f, 0.85f));
            Center(c.rectTransform, new Vector2(240, 260));
            c.rectTransform.anchoredPosition = new Vector2(cx[col], 250 - rowi * 290);
            Label(c.transform, "Day " + day, title, new Vector2(0, 95), new Vector2(220, 50), 30, Dark);
            var rico = Img(c.transform, UIKit.DailyCoin(), Gold);
            rico.raycastTarget = false; Center(rico.rectTransform, new Vector2(110, 110));
            Label(c.transform, "+" + coins[i], num, new Vector2(0, -95), new Vector2(220, 50), 30, Dark);
            if (day <= claimedDays)
            {
                var chk = Img(c.transform, UIKit.CheckMark(), new Color(1f, 0.7f, 0.1f));
                chk.raycastTarget = false; Center(chk.rectTransform, new Vector2(120, 120));
            }
        }
        var d7 = Img(card.transform, UIKit.CardCream(), new Color(0.85f, 0.92f, 0.98f));
        Center(d7.rectTransform, new Vector2(770, 230));
        d7.rectTransform.anchoredPosition = new Vector2(0, -330);
        Label(d7.transform, "Day 7", title, new Vector2(0, 80), new Vector2(740, 50), 32, Dark);
        var heli = Img(d7.transform, UIKit.JokerHeli(), new Color(0.6f, 0.7f, 0.9f));
        heli.raycastTarget = false; Center(heli.rectTransform, new Vector2(110, 110));
        Label(d7.transform, "Helicopter  x2", num, new Vector2(0, -80), new Vector2(740, 50), 30, Dark);
    }

    // ---- Shop -------------------------------------------------------------
    void BuildShop()
    {
        shopPanel = Panel("Shop", new Color(0.10f, 0.12f, 0.22f, 0.98f));
        var bg = Img(shopPanel.transform, UIKit.PanelTall(), new Color(0.30f, 0.25f, 0.55f));
        Stretch(bg.rectTransform); bg.rectTransform.offsetMin = new Vector2(30, 30); bg.rectTransform.offsetMax = new Vector2(-30, -200);

        Label(shopPanel.transform, "SHOP", title, new Vector2(0, 760), new Vector2(700, 110), 74, White);
        RedClose(shopPanel.transform, GoHome);

        BuildShopRow(shopPanel.transform, 470, "SWAP",       UIKit.JokerSwap(),    UIKit.ShopBoxA(), UIKit.ShopIconBgA(), new Color(0.30f, 0.60f, 0.90f), 1000);
        BuildShopRow(shopPanel.transform, 300, "RECOLOR",    UIKit.JokerRecolor(), UIKit.ShopBoxB(), UIKit.ShopIconBgB(), new Color(0.85f, 0.40f, 0.80f), 1000);
        BuildShopRow(shopPanel.transform, 130, "HELICOPTER", UIKit.JokerHeli(),    UIKit.ShopBoxA(), UIKit.ShopIconBgA(), new Color(0.95f, 0.62f, 0.25f), 1000);

        var ads = Btn(shopPanel.transform, UIKit.ShopBoxB(), new Color(0.35f, 0.40f, 0.65f), new Vector2(0.5f, 0.5f), new Vector2(0, -60), new Vector2(820, 150), OpenRemoveAds);
        var adIco = Img(ads.transform, UIKit.NoAds(), new Color(0.85f, 0.3f, 0.3f)); adIco.raycastTarget = false;
        Place(adIco.rectTransform, new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(90, 0), new Vector2(110, 110));
        Label(ads.transform, "REMOVE ADS", title, new Vector2(40, 20), new Vector2(560, 50), 40, White);
        Label(ads.transform, "₺249,99", num, new Vector2(40, -35), new Vector2(560, 40), 32, Gold);

        int[] amounts = { 100, 500, 1000 };
        int[] prices  = { 100, 250, 500 };
        float[] px = { -280, 0, 280 };
        for (int i = 0; i < 3; i++)
        {
            int amt = amounts[i], pr = prices[i];
            var box = Btn(shopPanel.transform, i % 2 == 0 ? UIKit.ShopIconBgA() : UIKit.ShopIconBgB(), new Color(0.55f, 0.40f, 0.75f),
                new Vector2(0.5f, 0.5f), new Vector2(px[i], -270), new Vector2(250, 250),
                () => { SaveSystem.AddCoins(amt); });
            var bico = Img(box.transform, i == 2 ? UIKit.CoinPackBig() : UIKit.ShopCoinA(), Gold); bico.raycastTarget = false;
            Center(bico.rectTransform, new Vector2(130, 130)); bico.rectTransform.anchoredPosition = new Vector2(0, 20);
            Label(box.transform, "+" + amt, num, new Vector2(0, 75), new Vector2(230, 40), 30, White);
            Label(box.transform, "$ " + pr, num, new Vector2(0, -95), new Vector2(230, 50), 34, Gold);
        }
    }

    void BuildShopRow(Transform parent, float y, string name, Sprite joker, Sprite rowBg, Sprite buyBg, Color fallback, int price)
    {
        var row = Img(parent, rowBg, fallback);
        Center(row.rectTransform, new Vector2(860, 150)); row.rectTransform.anchoredPosition = new Vector2(0, y);
        var ico = Img(row.transform, joker, new Color(0.9f, 0.9f, 1f)); ico.raycastTarget = false;
        Place(ico.rectTransform, new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(85, 0), new Vector2(110, 110));
        Label(row.transform, name, title, new Vector2(40, 30), new Vector2(420, 50), 38, White, TextAnchor.MiddleLeft);
        var buy = Btn(row.transform, buyBg, new Color(0.3f, 0.75f, 0.35f), new Vector2(1, 0.5f), new Vector2(-130, 0), new Vector2(230, 105),
            () => { if (SaveSystem.TrySpend(price)) { /* grant joker (persist later) */ } });
        var bc = Img(buy.transform, UIKit.Coin(), Gold); bc.raycastTarget = false;
        Place(bc.rectTransform, new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(20, 0), new Vector2(48, 48));
        Label(buy.transform, price.ToString(), num, new Vector2(20, 0), new Vector2(150, 50), 32, White);
    }

    // ---- Profile ----------------------------------------------------------
    void BuildProfile()
    {
        profilePanel = Panel("Profile", Dim);
        TapOutside(profilePanel, GoHome);
        var card = Img(profilePanel.transform, UIKit.EmptyBoxBlue(), new Color(0.25f, 0.55f, 0.90f));
        Center(card.rectTransform, new Vector2(800, 1000));
        Label(card.transform, "PROFILE", title, new Vector2(0, 400), new Vector2(700, 100), 64, White);
        var av = Img(card.transform, UISprites.Circle(), new Color(0.55f, 0.65f, 0.85f));
        av.raycastTarget = false; Center(av.rectTransform, new Vector2(240, 240)); av.rectTransform.anchoredPosition = new Vector2(0, 250);
        Label(card.transform, "Player", num, new Vector2(0, 60), new Vector2(600, 60), 40, White);
        RedClose(card.transform, GoHome);
    }

    // ---- Settings (draggable on/off sliders) -------------------------------
    void BuildSettings()
    {
        settingsPanel = Panel("Settings", Dim);
        TapOutside(settingsPanel, GoHome);
        var card = Img(settingsPanel.transform, UIKit.EmptyBoxBlue(), new Color(0.25f, 0.55f, 0.90f));
        Center(card.rectTransform, new Vector2(800, 1000));
        Label(card.transform, "SETTINGS", title, new Vector2(0, 400), new Vector2(700, 100), 64, White);

        SliderRow(card.transform, 230,  "SOUND",     UIKit.IconSound(), SaveSystem.Sound,     v => SaveSystem.Sound = v);
        SliderRow(card.transform, 90,   "MUSIC",     UIKit.IconMusic(), SaveSystem.Music,     v => SaveSystem.Music = v);
        SliderRow(card.transform, -50,  "VIBRATION", UIKit.Gear(),      SaveSystem.Vibration, v => SaveSystem.Vibration = v);

        // Language row (active placeholder; localization later).
        var ico = Img(card.transform, UIKit.Gear(), new Color(0.8f, 0.85f, 0.95f)); ico.raycastTarget = false;
        Place(ico.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(-260, -190), new Vector2(80, 80));
        Label(card.transform, "LANGUAGE", num, new Vector2(-40, -190), new Vector2(340, 60), 38, White, TextAnchor.MiddleLeft);
        var lang = Btn(card.transform, UIKit.BtnOrange(), new Color(0.95f, 0.6f, 0.25f), new Vector2(0.5f, 0.5f), new Vector2(230, -190), new Vector2(160, 92), null);
        Label(lang.transform, "EN", title, Vector2.zero, new Vector2(160, 92), 36, White);

        RedClose(card.transform, GoHome);
    }

    void SliderRow(Transform parent, float y, string name, Sprite icon, bool initial, System.Action<bool> onChange)
    {
        var ico = Img(parent, icon, new Color(0.8f, 0.85f, 0.95f)); ico.raycastTarget = false;
        Place(ico.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(-260, y), new Vector2(80, 80));
        Label(parent, name, num, new Vector2(-40, y), new Vector2(340, 60), 38, White, TextAnchor.MiddleLeft);
        ToggleSlider(parent, new Vector2(230, y), initial, onChange);
    }

    // Draggable on/off slider built on the kit's slider-track sprite.
    void ToggleSlider(Transform parent, Vector2 pos, bool initial, System.Action<bool> onChange)
    {
        var track = Img(parent, UIKit.SliderTrack(), new Color(0.18f, 0.22f, 0.35f));
        Place(track.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), pos, new Vector2(220, 70));

        var area = new GameObject("Area", typeof(RectTransform));
        area.transform.SetParent(track.transform, false);
        var art = area.GetComponent<RectTransform>();
        art.anchorMin = Vector2.zero; art.anchorMax = Vector2.one;
        art.offsetMin = new Vector2(34, 0); art.offsetMax = new Vector2(-34, 0);

        var handle = Img(area.transform, UISprites.Circle(), initial ? OnCol : OffCol);
        handle.color = initial ? OnCol : OffCol;
        handle.rectTransform.sizeDelta = new Vector2(56, 56);

        var slider = track.gameObject.AddComponent<Slider>();
        slider.transition = Selectable.Transition.None;
        slider.handleRect = handle.rectTransform;
        slider.targetGraphic = handle;
        slider.minValue = 0f; slider.maxValue = 1f;
        slider.value = initial ? 1f : 0f;
        slider.onValueChanged.AddListener(v =>
        {
            bool on = v > 0.5f;
            handle.color = on ? OnCol : OffCol;
            onChange?.Invoke(on);
        });
    }

    // ---- Remove Ads (800x600) ----------------------------------------------
    void BuildRemoveAds()
    {
        removeAdsPanel = Panel("RemoveAds", Dim);
        TapOutside(removeAdsPanel, GoHome);
        var card = Img(removeAdsPanel.transform, UIKit.EmptyBoxBlue(), new Color(0.25f, 0.55f, 0.90f));
        Center(card.rectTransform, new Vector2(800, 600));
        Label(card.transform, "REMOVE ADS", title, new Vector2(0, 210), new Vector2(700, 90), 60, White);

        var watch = Btn(card.transform, UIKit.ShopBoxA(), new Color(0.55f, 0.45f, 0.75f), new Vector2(0.5f, 0.5f), new Vector2(0, 95), new Vector2(620, 110),
            () => { SaveSystem.AddCoins(10); });
        var wi = Img(watch.transform, UIKit.AdReward(), new Color(0.5f, 0.7f, 0.9f)); wi.raycastTarget = false;
        Place(wi.rectTransform, new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(70, 0), new Vector2(80, 80));
        Label(watch.transform, "Watch ad  +10", num, new Vector2(40, 0), new Vector2(440, 50), 30, White);

        var noads = Img(card.transform, UIKit.NoAds(), new Color(0.85f, 0.3f, 0.3f));
        noads.raycastTarget = false; Center(noads.rectTransform, new Vector2(120, 120)); noads.rectTransform.anchoredPosition = new Vector2(0, -30);
        Label(card.transform, "Remove all ads forever", num, new Vector2(0, -120), new Vector2(700, 40), 28, White);
        var buy = Btn(card.transform, UIKit.BtnGreen(), new Color(0.3f, 0.75f, 0.35f), new Vector2(0.5f, 0), new Vector2(0, 50), new Vector2(420, 130), null);
        Label(buy.transform, "₺249,99", title, Vector2.zero, new Vector2(420, 90), 44, White);
        RedClose(card.transform, GoHome);
    }

    // ---- Navigation -----------------------------------------------------
    void ShowOnly(GameObject screen)
    {
        // Home view (PLAY + no-ads) is visible ONLY when no pop-up screen is open,
        // so the main-menu elements never bleed through the panels.
        if (homeView) homeView.SetActive(screen == null);
        if (dailyPanel) dailyPanel.SetActive(screen == dailyPanel);
        if (shopPanel) shopPanel.SetActive(screen == shopPanel);
        if (profilePanel) profilePanel.SetActive(screen == profilePanel);
        if (settingsPanel) settingsPanel.SetActive(screen == settingsPanel);
        if (removeAdsPanel) removeAdsPanel.SetActive(screen == removeAdsPanel);
    }
    void ShowHome()      { ShowOnly(null); }
    void GoHome()        { ShowOnly(null); SelectNav(1); }
    void OpenDaily()     { ShowOnly(dailyPanel); }
    void OpenShop()      { ShowOnly(shopPanel); }
    void OpenProfile()   { ShowOnly(profilePanel); }
    void OpenSettings()  { ShowOnly(settingsPanel); }
    void OpenRemoveAds() { ShowOnly(removeAdsPanel); }
    void Play()          { if (Application.isPlaying) SceneManager.LoadScene(GameScene); }

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

    void TapOutside(GameObject panel, System.Action onClose)
    {
        var b = panel.GetComponent<Image>();
        if (b == null) return;
        b.raycastTarget = true;
        var btn = panel.AddComponent<Button>();
        btn.transition = Selectable.Transition.None;
        btn.onClick.AddListener(() => onClose());
    }

    void Place(RectTransform rt, Vector2 min, Vector2 max, Vector2 pos, Vector2 size)
    { rt.anchorMin = min; rt.anchorMax = max; rt.pivot = new Vector2(0.5f, 0.5f); rt.anchoredPosition = pos; rt.sizeDelta = size; }
    void Center(RectTransform rt, Vector2 size)
    { rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f); rt.anchoredPosition = Vector2.zero; rt.sizeDelta = size; }
    void Stretch(RectTransform rt)
    { rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one; rt.offsetMin = Vector2.zero; rt.offsetMax = Vector2.zero; }
}
