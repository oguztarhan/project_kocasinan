using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEditor;
using UnityEditor.SceneManagement;
using BusJam;

/// <summary>
/// Editor tool that BAKES the in-game shop (the pop-up opened by tapping the coin during
/// gameplay) into the open scene as real, fully editable GameObjects — exactly like the
/// main-menu shop (Remove-Ads bar → gold packs grid → joker bars, scrollable).
///
/// At play time <see cref="GameUI"/> finds this baked shop via the <see cref="InGameShop"/>
/// marker and uses it instead of building one in code, wiring each tagged button's action
/// (<see cref="InGameShopButton"/>). Select any element in the Hierarchy and change its
/// colour / size / position / font in the Inspector. Re-running clears the previous bake.
///
/// Menu:  Tools ▸ 300Mind UI ▸ Bake In-Game Shop (into open scene)
/// </summary>
public static class GameShopBaker
{
    static readonly Color White = Color.white;
    static readonly Color Gold = new Color(1f, 0.85f, 0.30f);

    static Font Title => UIKit.Title();
    static Font Num => UIKit.Num();

    [MenuItem("Tools/300Mind UI/Bake In-Game Shop (into open scene)")]
    static void BakeShop()
    {
        // Clear any previous bake.
        var old = GameObject.Find("InGameShop_Baked");
        if (old) Object.DestroyImmediate(old);

        var rootGo = new GameObject("InGameShop_Baked");
        var canvas = rootGo.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 50; // above the code-built HUD canvas (0)
        var scaler = rootGo.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1080, 1920);
        scaler.matchWidthOrHeight = 0.5f;
        rootGo.AddComponent<GraphicRaycaster>();
        var marker = rootGo.AddComponent<InGameShop>();
        var root = rootGo.transform;

        if (Object.FindAnyObjectByType<EventSystem>() == null)
        {
            var es = new GameObject("EventSystem");
            es.transform.SetParent(root, false);
            es.AddComponent<EventSystem>();
            es.AddComponent<InputSystemUIInputModule>().AssignDefaultActions();
        }

        // ---- Dim backdrop (tap to close) = the panel GameUI shows/hides ----
        var panel = Img(root, "Panel_GameShop", null, new Color(0, 0, 0, 0.6f));
        Stretch(panel.rectTransform); panel.raycastTarget = true;
        var pbtn = panel.gameObject.AddComponent<Button>();
        pbtn.targetGraphic = panel; pbtn.transition = Selectable.Transition.None;
        var pTag = panel.gameObject.AddComponent<InGameShopButton>(); pTag.action = InGameShopButton.Act.Close;
        marker.panel = panel.gameObject;

        // ---- Tall card + title + red close ----
        var card = Img(panel.transform, "Card", UIKit.PanelTall(), new Color(0.30f, 0.25f, 0.55f));
        Center(card.rectTransform, new Vector2(960, 1500));
        Label(card.transform, "Title", "SHOP", Title, new Vector2(0, 680), new Vector2(700, 120), 74, White);
        var close = Btn(card.transform, "Close", UIKit.CloseX(), new Color(0.85f, 0.2f, 0.2f), new Vector2(1, 1), new Vector2(-40, -40), new Vector2(96, 96));
        var cTag = close.gameObject.AddComponent<InGameShopButton>(); cTag.action = InGameShopButton.Act.Close;

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

        // 1) Remove-ads bar (atlas1_44 bg): no-ads icon + price (display only).
        var adsRow = Img(ctGo.transform, "RemoveAds", UIKit.ShopBoxA(), new Color(0.95f, 0.55f, 0.20f));
        var adsLe = adsRow.gameObject.AddComponent<LayoutElement>(); adsLe.preferredHeight = 160; adsLe.minHeight = 160;
        var adsIco = Img(adsRow.transform, "Icon", UIKit.NoAds(), new Color(0.85f, 0.3f, 0.3f)); adsIco.raycastTarget = false;
        Place(adsIco.rectTransform, new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(95, 0), new Vector2(110, 110));
        var adsPrice = Img(adsRow.transform, "PriceBg", UIKit.PriceBtnA(), new Color(0.3f, 0.75f, 0.35f)); adsPrice.raycastTarget = false;
        Place(adsPrice.rectTransform, new Vector2(1, 0.5f), new Vector2(1, 0.5f), new Vector2(-210, 0), new Vector2(360, 110));
        Label(adsPrice.transform, "Price", "TRY 249,99", Num, Vector2.zero, new Vector2(360, 60), 36, White);

        // 2) Gold purchases (3-column grid, icons 11,12,13,29,30,31).
        var gridGo = new GameObject("CoinGrid", typeof(RectTransform));
        gridGo.transform.SetParent(ctGo.transform, false);
        var gl = gridGo.AddComponent<GridLayoutGroup>();
        gl.cellSize = new Vector2(275, 360); gl.spacing = new Vector2(15, 20);
        gl.childAlignment = TextAnchor.UpperCenter;
        gl.constraint = GridLayoutGroup.Constraint.FixedColumnCount; gl.constraintCount = 3;
        CoinCard(gridGo.transform, "Pack_100",   UIKit.ShopCoinA(),     "100",   "$ 100",   100);
        CoinCard(gridGo.transform, "Pack_500",   UIKit.ShopCoinB(),     "500",   "$ 250",   500);
        CoinCard(gridGo.transform, "Pack_1000",  UIKit.ShopCoinC(),     "1000",  "$ 500",   1000);
        CoinCard(gridGo.transform, "Pack_2000",  UIKit.ShopGold(),      "2000",  "$ 800",   2000);
        CoinCard(gridGo.transform, "Pack_5000",  UIKit.CoinPackSmall(), "5000",  "$ 1200",  5000);
        CoinCard(gridGo.transform, "Pack_10000", UIKit.CoinPackBig(),   "10000", "$ 2100",  10000);

        // 3) Joker bars LAST (atlas1_44 style): icon left, buy for 100 gold.
        JokerBar(ctGo.transform, "Bar_Shuffle", UIKit.JokerRecolor());
        JokerBar(ctGo.transform, "Bar_Swap",    UIKit.JokerSwap());
        JokerBar(ctGo.transform, "Bar_Heli",    UIKit.JokerHeli());

        EditorUtility.SetDirty(marker);
        EditorSceneManager.MarkSceneDirty(UnityEngine.SceneManagement.SceneManager.GetActiveScene());
        Selection.activeGameObject = card.gameObject;
        Debug.Log("[GameShopBaker] Baked in-game shop into the open scene. Edit it in the Inspector, then SAVE (Ctrl+S). It hides itself on Play and opens when you tap the coin.");
    }

    // One purple coin-pack card (atlas1_56): coin icon + amount + green price button.
    static void CoinCard(Transform parent, string name, Sprite icon, string amount, string price, int coins)
    {
        var card = Img(parent, name, UIKit.ShopIconBgA(), new Color(0.55f, 0.40f, 0.78f));
        var ico = Img(card.transform, "Icon", icon, Gold); ico.raycastTarget = false;
        Place(ico.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0, 40), new Vector2(150, 150));
        Label(card.transform, "Amount", amount, Num, new Vector2(0, 132), new Vector2(255, 50), 34, White);
        var buy = Btn(card.transform, "Buy", UIKit.PriceBtnA(), new Color(0.3f, 0.75f, 0.35f), new Vector2(0.5f, 0), new Vector2(0, 22), new Vector2(245, 92));
        Label(buy.transform, "Price", price, Num, Vector2.zero, new Vector2(245, 56), 32, White);
        var tag = buy.gameObject.AddComponent<InGameShopButton>(); tag.action = InGameShopButton.Act.GrantCoins; tag.amount = coins;
    }

    // A full-width joker bar (atlas1_44 bg): icon on the dark-orange left + a 100-gold buy button.
    static void JokerBar(Transform parent, string name, Sprite icon)
    {
        var row = Img(parent, name, UIKit.ShopBoxA(), new Color(0.95f, 0.55f, 0.20f));
        var le = row.gameObject.AddComponent<LayoutElement>(); le.preferredHeight = 160; le.minHeight = 160;
        var ico = Img(row.transform, "Icon", icon, White); ico.raycastTarget = false;
        Place(ico.rectTransform, new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(110, 0), new Vector2(120, 120));
        var buy = Btn(row.transform, "Buy", UIKit.PriceBtnA(), new Color(0.3f, 0.75f, 0.35f), new Vector2(1, 0.5f), new Vector2(-210, 0), new Vector2(360, 110));
        var bc = Img(buy.transform, "Coin", UIKit.Coin(), Gold); bc.raycastTarget = false;
        Place(bc.rectTransform, new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(45, 0), new Vector2(56, 56));
        Label(buy.transform, "Price", "100", Num, new Vector2(30, 0), new Vector2(360, 60), 36, White);
        var tag = buy.gameObject.AddComponent<InGameShopButton>(); tag.action = InGameShopButton.Act.SpendJoker;
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
