using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Events;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEditor;
using UnityEditor.Events;
using UnityEditor.SceneManagement;
using BusJam;

/// <summary>
/// Editor tool that BAKES the main menu into the open scene as real, fully editable
/// GameObjects (Image/Text/Button), wires their behaviour to a <see cref="MenuController"/>
/// via persistent OnClick events, and assigns the controller's references. After running
/// it you can select any element in the Hierarchy and change its colour / size / position /
/// font in the Inspector — exactly like hand-built UI. Re-running clears the previous bake.
///
/// Menu:  Tools ▸ 300Mind UI ▸ Bake Main Menu (into open scene)
/// </summary>
public static class MenuUIBaker
{
    static readonly Color White = Color.white;
    static readonly Color Orange = new Color(1f, 0.62f, 0.15f);
    static readonly Color NavBlue = new Color(0.20f, 0.45f, 0.90f);
    static readonly Color Gold = new Color(1f, 0.85f, 0.30f);
    static readonly Color Dark = new Color(0.16f, 0.20f, 0.30f);
    static readonly Color Dim = new Color(0, 0, 0, 0.6f);

    static Font Title => UIKit.Title();
    static Font Num => UIKit.Num();

    [MenuItem("Tools/300Mind UI/Bake Main Menu (into open scene)")]
    static void BakeMenu()
    {
        // Clear any previous bake.
        var old = GameObject.Find("MenuUI_Baked");
        if (old) Object.DestroyImmediate(old);

        var rootGo = new GameObject("MenuUI_Baked");
        var canvas = rootGo.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 100;
        var scaler = rootGo.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1080, 1920);
        scaler.matchWidthOrHeight = 0.5f;
        rootGo.AddComponent<GraphicRaycaster>();
        var ctrl = rootGo.AddComponent<MenuController>();
        var root = rootGo.transform;

        if (Object.FindAnyObjectByType<EventSystem>() == null)
        {
            var es = new GameObject("EventSystem");
            es.transform.SetParent(root, false);
            es.AddComponent<EventSystem>();
            es.AddComponent<InputSystemUIInputModule>().AssignDefaultActions();
        }

        // ---- Panels (built first so the bar/nav sit on top) ----
        ctrl.dailyPanel     = BuildPanel(root, "Panel_Daily", "DAILY REWARDS", ctrl);
        ctrl.shopPanel      = BuildPanel(root, "Panel_Shop", "SHOP", ctrl);
        ctrl.profilePanel   = BuildPanel(root, "Panel_Profile", "PROFILE", ctrl);
        ctrl.settingsPanel  = BuildPanel(root, "Panel_Settings", "SETTINGS", ctrl);
        ctrl.removeAdsPanel = BuildPanel(root, "Panel_RemoveAds", "REMOVE ADS", ctrl);

        // ---- Home (PLAY + No-Ads) ----
        var play = Btn(root, "Btn_Play", UIKit.PlayBtn(), new Color(0.30f, 0.75f, 0.35f), new Vector2(0.5f, 0.5f), new Vector2(0, -120), new Vector2(460, 180));
        Label(play.transform, "Txt_Play", "PLAY", Title, Vector2.zero, new Vector2(460, 120), 70, White);
        Wire(play, ctrl.Play);

        var noads = Btn(root, "Btn_NoAds", UIKit.NoAds(), new Color(0.85f, 0.30f, 0.30f), new Vector2(1, 1), new Vector2(-110, -360), new Vector2(150, 150));
        Wire(noads, ctrl.OpenRemoveAds);

        // ---- Top bar ----
        var coin = Btn(root, "Coin_Bar", UIKit.CoinBar(), Dark, new Vector2(0, 1), new Vector2(200, -95), new Vector2(300, 96));
        var ci = Img(coin.transform, "Coin_Icon", UIKit.Coin(), Gold); ci.raycastTarget = false;
        Place(ci.rectTransform, new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(42, 0), new Vector2(74, 74));
        var cp = Img(coin.transform, "Coin_Plus", UIKit.PlusGreen(), new Color(0.3f, 0.8f, 0.35f)); cp.raycastTarget = false;
        Place(cp.rectTransform, new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(88, -22), new Vector2(46, 46));
        ctrl.coinText = Label(coin.transform, "Txt_Coin", "0", Num, new Vector2(45, 0), new Vector2(170, 60), 44, White);
        Wire(coin, ctrl.OpenShop);

        var gear = Btn(root, "Btn_Settings", UIKit.Gear(), new Color(0.7f, 0.72f, 0.78f), new Vector2(1, 1), new Vector2(-90, -100), new Vector2(120, 120));
        Wire(gear, ctrl.OpenSettings);

        // ---- Bottom nav ----
        var strip = Img(root, "Nav_Strip", UIKit.NavStrip(), new Color(0.18f, 0.42f, 0.85f));
        strip.rectTransform.anchorMin = new Vector2(0, 0); strip.rectTransform.anchorMax = new Vector2(1, 0);
        strip.rectTransform.pivot = new Vector2(0.5f, 0);
        strip.rectTransform.offsetMin = Vector2.zero; strip.rectTransform.offsetMax = new Vector2(0, 200);

        ctrl.navDailySel = NavButton(strip.transform, "Nav_Daily", -340, UIKit.NavDaily(), "DAILY", ctrl.OpenDaily);
        ctrl.navHomeSel  = NavButton(strip.transform, "Nav_Home", 0,    UIKit.NavHome(),  "HOME",  ctrl.ShowHome);
        ctrl.navShopSel  = NavButton(strip.transform, "Nav_Shop", 340,  UIKit.NavShop(),  "SHOP",  ctrl.OpenShop);

        // Persist.
        EditorUtility.SetDirty(ctrl);
        EditorSceneManager.MarkSceneDirty(UnityEngine.SceneManagement.SceneManager.GetActiveScene());
        Selection.activeGameObject = rootGo;
        Debug.Log("[MenuUIBaker] Baked main menu into the scene. Edit any element in the Inspector, then SAVE the scene (Ctrl+S).");
    }

    // ============================================================================
    // Rebuild ONLY the Daily Rewards panel (transparent black backdrop like the
    // reference). Touches nothing else in the scene.
    // ============================================================================
    [MenuItem("Tools/300Mind UI/Rebuild Daily Rewards (transparent)")]
    static void RebuildDaily()
    {
        var rootGo = GameObject.Find("MenuUI_Baked");
        if (!rootGo) { Debug.LogError("[MenuUIBaker] Run 'Bake Main Menu' first."); return; }
        var ctrl = rootGo.GetComponent<MenuController>();
        var panelT = FindChild(rootGo.transform, "Panel_Daily");
        if (!panelT) { Debug.LogError("[MenuUIBaker] Panel_Daily not found - re-bake the menu."); return; }
        var panel = panelT.gameObject;

        // Clear old content.
        for (int i = panel.transform.childCount - 1; i >= 0; i--)
            Object.DestroyImmediate(panel.transform.GetChild(i).gameObject);

        // Transparent black backdrop; tap anywhere closes it.
        var bg = panel.GetComponent<Image>();
        bg.sprite = null; bg.color = new Color(0, 0, 0, 0.6f); bg.raycastTarget = true;
        var pbtn = panel.GetComponent<Button>(); if (!pbtn) pbtn = panel.AddComponent<Button>();
        pbtn.transition = Selectable.Transition.None;
        pbtn.onClick = new Button.ButtonClickedEvent();
        UnityEventTools.AddPersistentListener(pbtn.onClick, ctrl.CloseAll);

        // Title + subtitle (top), like the reference.
        // Title box centered at the top.
        Label(panel.transform, "Title", "Daily Rewards", Title, new Vector2(0, 630), new Vector2(880, 150), 84, Gold);
        Label(panel.transform, "Subtitle", "COME BACK EVERY DAY TO GET\nGREAT REWARDS", Num, new Vector2(0, 500), new Vector2(760, 90), 30, White);

        // Days 1-7 (1-3 claimed). Base card = atlas1_58, coin = atlas1_11. Grid centered on screen.
        var cardSize = new Vector2(230, 280);
        DayCard(panel.transform, -255, 250, cardSize, 1, UIKit.ShopCoinA(), "+100", 100);
        DayCard(panel.transform, 0,    250, cardSize, 2, UIKit.ShopCoinA(), "+150", 150);
        DayCard(panel.transform, 255,  250, cardSize, 3, UIKit.ShopCoinA(), "+200", 200);
        DayCard(panel.transform, -255, -50, cardSize, 4, UIKit.ShopCoinA(), "+500", 500);
        DayCard(panel.transform, 0,    -50, cardSize, 5, UIKit.JokerSwap(),    "Swap",    0);
        DayCard(panel.transform, 255,  -50, cardSize, 6, UIKit.JokerRecolor(), "Shuffle", 0);
        // Day 7: wide banner with the helicopter reward (atlas1_59), centered.
        DayCard(panel.transform, 0, -350, new Vector2(770, 250), 7, UIKit.DailyIconB(), "x2", 0);

        // Claim manager (1 reward/day, in order, with checkmark pop animation).
        if (panel.GetComponent<DailyRewards>() == null) panel.AddComponent<DailyRewards>();

        panel.SetActive(true);
        EditorUtility.SetDirty(ctrl);
        EditorSceneManager.MarkSceneDirty(UnityEngine.SceneManagement.SceneManager.GetActiveScene());
        Selection.activeGameObject = panel;
        Debug.Log("[MenuUIBaker] Rebuilt Daily Rewards (transparent). Edit cards in the Inspector, then SAVE (Ctrl+S).");
    }

    // One day card: atlas1_58 base + reward icon (atlas1_11 coin / joker / atlas1_59),
    // a "Day N" label, an amount, and a claimed check (atlas1_5) when already taken.
    static void DayCard(Transform parent, float x, float y, Vector2 size, int day, Sprite icon, string amount, int coins)
    {
        var card = Img(parent, "Day" + day, UIKit.DailyIconA(), new Color(0.85f, 0.90f, 0.98f));
        card.raycastTarget = true; // the whole card is the claim button
        Place(card.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(x, y), size);
        // "Day N" centered on the card's blue top strip.
        var dl = Label(card.transform, "DayLabel", "Day " + day, Title, Vector2.zero, new Vector2(size.x, 52), 28, White);
        dl.rectTransform.anchorMin = dl.rectTransform.anchorMax = new Vector2(0.5f, 1f);
        dl.rectTransform.pivot = new Vector2(0.5f, 1f);
        dl.rectTransform.anchoredPosition = new Vector2(0, -30);
        var ico = Img(card.transform, "Reward", icon, Gold); ico.raycastTarget = false;
        Center(ico.rectTransform, new Vector2(110, 110));
        if (!string.IsNullOrEmpty(amount))
            Label(card.transform, "Amount", amount, Num, new Vector2(0, -size.y * 0.5f + 34), new Vector2(size.x - 16, 46), 28, Dark);

        // Checkmark overlay (hidden until claimed) + claim button + data tag.
        var chk = Img(card.transform, "Check", UIKit.CheckMark(), new Color(1f, 0.7f, 0.1f));
        chk.raycastTarget = false; Center(chk.rectTransform, new Vector2(130, 130));
        chk.gameObject.SetActive(false);

        var btn = card.gameObject.AddComponent<Button>();
        btn.targetGraphic = card;
        var dc = card.gameObject.AddComponent<DailyCard>();
        dc.day = day; dc.coins = coins; dc.check = chk.gameObject; dc.button = btn;
    }

    static Transform FindChild(Transform root, string name)
    {
        foreach (var t in root.GetComponentsInChildren<Transform>(true))
            if (t.name == name) return t;
        return null;
    }

    // ============================================================================
    // Rebuild the Shop panel as a SCROLLABLE list (drag up/down). Add your own
    // products under the "Content" object — the list grows and scrolls automatically.
    // ============================================================================
    [MenuItem("Tools/300Mind UI/Rebuild Shop (scrollable)")]
    static void RebuildShop()
    {
        var rootGo = GameObject.Find("MenuUI_Baked");
        if (!rootGo) { Debug.LogError("[MenuUIBaker] Run 'Bake Main Menu' first."); return; }
        var ctrl = rootGo.GetComponent<MenuController>();
        var panelT = FindChild(rootGo.transform, "Panel_Shop");
        if (!panelT) { Debug.LogError("[MenuUIBaker] Panel_Shop not found - re-bake the menu."); return; }
        var panel = panelT.gameObject;
        for (int i = panel.transform.childCount - 1; i >= 0; i--)
            Object.DestroyImmediate(panel.transform.GetChild(i).gameObject);

        var bg = panel.GetComponent<Image>();
        bg.sprite = null; bg.color = new Color(0, 0, 0, 0.6f); bg.raycastTarget = true;
        var pbtn = panel.GetComponent<Button>(); if (!pbtn) pbtn = panel.AddComponent<Button>();
        pbtn.transition = Selectable.Transition.None; pbtn.onClick = new Button.ButtonClickedEvent();
        UnityEventTools.AddPersistentListener(pbtn.onClick, ctrl.CloseAll);

        var card = Img(panel.transform, "Card", UIKit.PanelTall(), new Color(0.30f, 0.25f, 0.55f));
        Center(card.rectTransform, new Vector2(960, 1500));
        Label(card.transform, "Title", "SHOP", Title, new Vector2(0, 680), new Vector2(700, 120), 74, White);
        var close = Btn(card.transform, "Close", UIKit.CloseX(), new Color(0.85f, 0.2f, 0.2f), new Vector2(1, 1), new Vector2(-40, -40), new Vector2(96, 96));
        UnityEventTools.AddPersistentListener(close.onClick, ctrl.CloseAll);

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
        ctRt.anchorMin = new Vector2(0, 1); ctRt.anchorMax = new Vector2(1, 1); ctRt.pivot = new Vector2(0.5f, 1); ctRt.anchoredPosition = Vector2.zero; ctRt.sizeDelta = Vector2.zero;
        var vlg = ctGo.AddComponent<VerticalLayoutGroup>();
        vlg.spacing = 24; vlg.padding = new RectOffset(15, 15, 15, 15);
        vlg.childAlignment = TextAnchor.UpperCenter;
        vlg.childControlWidth = true; vlg.childControlHeight = true;
        vlg.childForceExpandWidth = true; vlg.childForceExpandHeight = false;
        var fit = ctGo.AddComponent<ContentSizeFitter>();
        fit.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        fit.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;

        scroll.viewport = vpRt; scroll.content = ctRt;

        // 1) Remove-ads bar FIRST (atlas1_44 background): no-ads icon on the dark-orange
        //    left, the price (on an atlas1_36 button) in the big space on the right.
        var adsRow = Img(ctGo.transform, "RemoveAds", UIKit.ShopBoxA(), new Color(0.95f, 0.55f, 0.20f));
        var adsLe = adsRow.gameObject.AddComponent<LayoutElement>(); adsLe.preferredHeight = 160; adsLe.minHeight = 160;
        var adsBtn = adsRow.gameObject.AddComponent<Button>(); adsBtn.targetGraphic = adsRow;
        UnityEventTools.AddPersistentListener(adsBtn.onClick, ctrl.OpenRemoveAds);
        var adsIco = Img(adsRow.transform, "Icon", UIKit.NoAds(), new Color(0.85f, 0.3f, 0.3f)); adsIco.raycastTarget = false;
        Place(adsIco.rectTransform, new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(95, 0), new Vector2(110, 110));
        var adsPrice = Img(adsRow.transform, "PriceBg", UIKit.PriceBtnA(), new Color(0.3f, 0.75f, 0.35f)); adsPrice.raycastTarget = false;
        Place(adsPrice.rectTransform, new Vector2(1, 0.5f), new Vector2(1, 0.5f), new Vector2(-210, 0), new Vector2(360, 110));
        Label(adsPrice.transform, "Price", "TRY 249,99", Num, Vector2.zero, new Vector2(360, 60), 36, White);

        // 2) Gold purchases (3-column grid, icons in order: atlas1 11,12,13,29,30,31).
        var gridGo = new GameObject("CoinGrid", typeof(RectTransform));
        gridGo.transform.SetParent(ctGo.transform, false);
        var gl = gridGo.AddComponent<GridLayoutGroup>();
        gl.cellSize = new Vector2(275, 360);
        gl.spacing = new Vector2(15, 20);
        gl.childAlignment = TextAnchor.UpperCenter;
        gl.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
        gl.constraintCount = 3;
        CoinCard(gridGo.transform, "Pack_100",   UIKit.ShopCoinA(),     "100",   "$ 100");
        CoinCard(gridGo.transform, "Pack_500",   UIKit.ShopCoinB(),     "500",   "$ 250");
        CoinCard(gridGo.transform, "Pack_1000",  UIKit.ShopCoinC(),     "1000",  "$ 500");
        CoinCard(gridGo.transform, "Pack_2000",  UIKit.ShopGold(),      "2000",  "$ 800");
        CoinCard(gridGo.transform, "Pack_5000",  UIKit.CoinPackSmall(), "5000",  "$ 1200");
        CoinCard(gridGo.transform, "Pack_10000", UIKit.CoinPackBig(),   "10000", "$ 2100");

        // 3) Joker bars LAST (atlas1_44 style): icon on the dark-orange left, buy for 100 gold.
        JokerBar(ctGo.transform, "Bar_Shuffle", UIKit.JokerRecolor(), ctrl); // shuffle
        JokerBar(ctGo.transform, "Bar_Swap",    UIKit.JokerSwap(),    ctrl); // swap
        JokerBar(ctGo.transform, "Bar_Heli",    UIKit.JokerHeli(),    ctrl); // helicopter (atlas2_14 placeholder)

        EditorUtility.SetDirty(ctrl);
        EditorSceneManager.MarkSceneDirty(UnityEngine.SceneManagement.SceneManager.GetActiveScene());
        Selection.activeGameObject = ctGo;
        Debug.Log("[MenuUIBaker] Rebuilt Shop: 6 coin packs (icons 11,12,13,29,30,31), scrollable grid. SAVE the scene (Ctrl+S).");
    }

    // One purple coin-pack card (atlas1_56) holding a coin icon, an amount and a green
    // price button. Placed inside the scroll grid.
    static void CoinCard(Transform parent, string name, Sprite icon, string amount, string price)
    {
        var card = Img(parent, name, UIKit.ShopIconBgA(), new Color(0.55f, 0.40f, 0.78f)); // purple card
        var ico = Img(card.transform, "Icon", icon, Gold); ico.raycastTarget = false;
        Place(ico.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0, 40), new Vector2(150, 150));
        Label(card.transform, "Amount", amount, Num, new Vector2(0, 132), new Vector2(255, 50), 34, White);
        var buy = Btn(card.transform, "Buy", UIKit.PriceBtnA(), new Color(0.3f, 0.75f, 0.35f), new Vector2(0.5f, 0), new Vector2(0, 22), new Vector2(245, 92));
        Label(buy.transform, "Price", price, Num, Vector2.zero, new Vector2(245, 56), 32, White);
    }

    // A full-width joker bar (atlas1_44 bg): icon centered on the dark-orange left + a
    // "100 gold" buy button (atlas1_36) on the right, just like the Remove-Ads bar.
    static void JokerBar(Transform parent, string name, Sprite icon, MenuController ctrl)
    {
        var row = Img(parent, name, UIKit.ShopBoxA(), new Color(0.95f, 0.55f, 0.20f));
        var le = row.gameObject.AddComponent<LayoutElement>(); le.preferredHeight = 160; le.minHeight = 160;
        var ico = Img(row.transform, "Icon", icon, White); ico.raycastTarget = false;
        Place(ico.rectTransform, new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(110, 0), new Vector2(120, 120));
        var buy = Btn(row.transform, "Buy", UIKit.PriceBtnA(), new Color(0.3f, 0.75f, 0.35f), new Vector2(1, 0.5f), new Vector2(-210, 0), new Vector2(360, 110));
        UnityEventTools.AddPersistentListener(buy.onClick, ctrl.BuyFor100);
        var bc = Img(buy.transform, "Coin", UIKit.Coin(), Gold); bc.raycastTarget = false;
        Place(bc.rectTransform, new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(45, 0), new Vector2(56, 56));
        Label(buy.transform, "Price", "100", Num, new Vector2(30, 0), new Vector2(360, 60), 36, White);
    }

    // ---- A pop-up panel: dim backdrop + atlas2_0 card + blue title tile + red close ----
    static GameObject BuildPanel(Transform root, string name, string titleText, MenuController ctrl)
    {
        var panel = Img(root, name, null, Dim);
        Stretch(panel.rectTransform);

        var card = Img(panel.transform, "Card", UIKit.EmptyBoxBlue(), White);
        card.color = White;
        Center(card.rectTransform, new Vector2(820, 1100));

        var tile = Img(card.transform, "TitleTile", UIKit.TitleBarA(), new Color(0.25f, 0.55f, 0.90f));
        tile.color = new Color(0.25f, 0.55f, 0.90f); tile.raycastTarget = false;
        Place(tile.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0, 460), new Vector2(620, 130));
        Label(card.transform, "Title", titleText, Title, new Vector2(0, 460), new Vector2(600, 100), 56, White);

        var close = Btn(card.transform, "Close", UIKit.CloseX(), new Color(0.85f, 0.2f, 0.2f), new Vector2(1, 1), new Vector2(-40, -40), new Vector2(96, 96));
        Wire(close, ctrl.CloseAll);

        panel.gameObject.SetActive(false);
        return panel.gameObject;
    }

    // ---- A bottom-nav button: blue backing (14) + orange selected backing (15) + icon ----
    static GameObject NavButton(Transform parent, string name, float x, Sprite icon, string label, UnityAction onClick)
    {
        var holder = new GameObject(name, typeof(RectTransform));
        holder.transform.SetParent(parent, false);
        var hrt = holder.GetComponent<RectTransform>();
        hrt.anchorMin = hrt.anchorMax = new Vector2(0.5f, 0); hrt.pivot = new Vector2(0.5f, 0);
        hrt.anchoredPosition = new Vector2(x, 22); hrt.sizeDelta = new Vector2(170, 170);

        var off = Img(holder.transform, "Bg", UIKit.NavBtnOff(), NavBlue);
        Center(off.rectTransform, new Vector2(160, 160)); off.raycastTarget = false;

        var sel = Img(holder.transform, "Sel", UIKit.NavBtnBg(), Orange);
        Center(sel.rectTransform, new Vector2(160, 160)); sel.raycastTarget = false;
        var lbl = Label(sel.transform, "Label", label, Title, new Vector2(0, -58), new Vector2(160, 40), 26, White);
        sel.gameObject.SetActive(false); // shown only when this nav is selected

        var btn = Btn(holder.transform, "Icon", icon, White, new Vector2(0.5f, 0.5f), new Vector2(0, 12), new Vector2(110, 110));
        Wire(btn, onClick);

        return sel.gameObject;
    }

    // ---- Persistent OnClick wiring ----
    static void Wire(Button b, UnityAction action)
    {
        UnityEventTools.AddPersistentListener(b.onClick, action);
    }

    // ---- Object builders (persistent, named) ----
    static Image Img(Transform parent, string name, Sprite sprite, Color fallback)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        var img = go.AddComponent<Image>();
        if (sprite != null) { img.sprite = sprite; img.color = White; } else img.color = fallback;
        return img;
    }

    static Text Label(Transform parent, string name, string text, Font font, Vector2 pos, Vector2 size, int fontSize, Color color)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        var t = go.AddComponent<Text>();
        t.font = font; t.text = text; t.fontSize = fontSize; t.color = color; t.alignment = TextAnchor.MiddleCenter;
        t.horizontalOverflow = HorizontalWrapMode.Overflow; t.verticalOverflow = VerticalWrapMode.Overflow;
        t.raycastTarget = false;
        var sh = go.AddComponent<Shadow>(); sh.effectColor = new Color(0, 0, 0, 0.4f); sh.effectDistance = new Vector2(2, -2);
        var rt = t.rectTransform;
        rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = pos; rt.sizeDelta = size;
        return t;
    }

    static Button Btn(Transform parent, string name, Sprite sprite, Color fallback, Vector2 anchor, Vector2 pos, Vector2 size)
    {
        var img = Img(parent, name, sprite, fallback);
        var rt = img.rectTransform;
        rt.anchorMin = rt.anchorMax = anchor; rt.pivot = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = pos; rt.sizeDelta = size;
        var btn = img.gameObject.AddComponent<Button>();
        btn.targetGraphic = img;
        return btn;
    }

    static void Place(RectTransform rt, Vector2 min, Vector2 max, Vector2 pos, Vector2 size)
    { rt.anchorMin = min; rt.anchorMax = max; rt.pivot = new Vector2(0.5f, 0.5f); rt.anchoredPosition = pos; rt.sizeDelta = size; }
    static void Center(RectTransform rt, Vector2 size)
    { rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f); rt.anchoredPosition = Vector2.zero; rt.sizeDelta = size; }
    static void Stretch(RectTransform rt)
    { rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one; rt.offsetMin = Vector2.zero; rt.offsetMax = Vector2.zero; }
}
