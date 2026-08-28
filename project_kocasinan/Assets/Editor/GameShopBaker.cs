using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using Ridebury;

/// <summary>
/// Builds the one shared store prefab from the project-local cut-out artwork. ShopUI
/// keeps the IAP, restore and joker behaviour wired at runtime.
/// </summary>
public static class GameShopBaker
{
    const string Art = "Assets/kesilmis-ikonlar/";
    const string DesignMarker = "ShopDesign_20260828_V5";

    static readonly Color White = Color.white;
    static readonly Color Ink = new Color(0.24f, 0.055f, 0.10f);
    static readonly Color NavyBackdrop = new Color(0.015f, 0.055f, 0.13f, 0.94f);
    static readonly Color Gold = new Color(1f, 0.83f, 0.18f);

    static Font Title => UIKit.Title();
    static Font Num => UIKit.Num();

    // When this source revision lands while Unity is already open, rebuild once in that
    // same editor. The marker stored inside the prefab makes later reloads no-ops.
    [InitializeOnLoadMethod]
    static void BakeUpdatedDesignOnce()
    {
        EditorApplication.update -= TryAutoBake;
        EditorApplication.update += TryAutoBake;
    }

    static void TryAutoBake()
    {
        if (EditorApplication.isCompiling || EditorApplication.isUpdating) return;
        EditorApplication.update -= TryAutoBake;
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(ShopUnifier.PrefabPath);
        if (prefab != null && prefab.transform.Find(DesignMarker) != null) return;
        BakeShop();
        AssetDatabase.SaveAssets();
    }

    [MenuItem("Tools/300Mind UI/Bake Shop Prefab (the one shop)")]
    static void BakeShopMenu()
    {
        if (!EditorUtility.DisplayDialog("Bake the shop prefab?",
            "This rebuilds " + ShopUnifier.PrefabPath + " from the project shop artwork.",
            "Rebuild", "Cancel")) return;
        BakeShop();
    }

    /// <summary>Headless entry point used to regenerate the committed prefab.</summary>
    public static void BakeShopFromCommandLine()
    {
        BakeShop();
        AssetDatabase.SaveAssets();
        Debug.Log("[GameShopBaker] Command-line shop bake completed.");
    }

    static void BakeShop()
    {
        AssetDatabase.Refresh();

        var rootGo = new GameObject(ShopUnifier.PrefabName);
        var canvas = rootGo.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 200;
        var scaler = rootGo.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1080, 1920);
        scaler.matchWidthOrHeight = 0f;
        rootGo.AddComponent<GraphicRaycaster>();
        var marker = rootGo.AddComponent<ShopUI>();
        var version = new GameObject(DesignMarker);
        version.transform.SetParent(rootGo.transform, false);
        version.SetActive(false);

        var panel = Img(rootGo.transform, "Panel_GameShop", null, NavyBackdrop);
        Stretch(panel.rectTransform);
        panel.raycastTarget = true;
        var panelButton = panel.gameObject.AddComponent<Button>();
        panelButton.targetGraphic = panel;
        panelButton.transition = Selectable.Transition.None;
        var panelTag = panel.gameObject.AddComponent<InGameShopButton>();
        panelTag.action = InGameShopButton.Act.Close;
        marker.panel = panel.gameObject;

        var card = Img(panel.transform, "Card", Cut("lacivert-panel-uzun-temiz.png"), NavyBackdrop);
        Center(card.rectTransform, new Vector2(1010, 1840));
        card.raycastTarget = true;

        var titleBand = Img(card.transform, "TitleBand", Cut("bar_red.png"), new Color(0.60f, 0.05f, 0.15f));
        Place(titleBand.rectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0, -105), new Vector2(750, 150));
        titleBand.raycastTarget = false;
        Label(titleBand.transform, "Title", "SHOP", Title, Vector2.zero, new Vector2(620, 105), 68, White, true);

        var close = Btn(card.transform, "Close", Cut("icon_close.png"), Color.white,
            new Vector2(1f, 1f), new Vector2(-80, -86), new Vector2(104, 104));
        close.image.preserveAspect = true;
        var closeTag = close.gameObject.AddComponent<InGameShopButton>();
        closeTag.action = InGameShopButton.Act.Close;

        var scroll = BuildScroll(card.transform);
        var content = scroll.content;

        SectionHeader(content, "GOLD PACKS");
        CoinRow(content, "Pack_200",  Cut("coinpack_1.png"), "200",  "$0.99", 200);
        CoinRow(content, "Pack_500",  Cut("coinpack_2.png"), "500",  "$1.99", 500);
        CoinRow(content, "Pack_1300", Cut("coinpack_3.png"), "1 300", "$4.99", 1300);
        CoinRow(content, "Pack_2500", Cut("coinpack_4.png"), "2 500", "$8.99", 2500);
        CoinRow(content, "Pack_4000", Cut("coinpack_5.png"), "4 000", "$12.99", 4000);
        CoinRow(content, "Pack_5500", Cut("coinpack_6.png"), "5 500", "$17.99", 5500);

        SectionHeader(content, "SPECIAL OFFERS");
        PromoRow(content, "RemoveAds", Cut("ads_icon.png"), "REMOVE ADS", "$9.99");
        BonusPromoRow(content, "RemoveAds (1)", "$12.99");
        PromoRow(content, "RemoveBanner", Cut("icon_watch_ad.png"), "REMOVE BANNER", "$0.99");

        SectionHeader(content, "POWER-UPS");
        JokerRow(content, "Bar_Shuffle", Cut("joker_recolor.png"), "RECOLOR");
        JokerRow(content, "Bar_Swap", Cut("joker_shuffle.png"), "SWAP");
        JokerRow(content, "Bar_Heli", Cut("helikopter.png"), "HELICOPTER");
        RestoreButton(content);

        panel.gameObject.SetActive(false);
        ShopUnifier.SavePrefab(rootGo);
        Object.DestroyImmediate(rootGo);
        Debug.Log("[GameShopBaker] Rebuilt polished store -> " + ShopUnifier.PrefabPath);
    }

    static ScrollRect BuildScroll(Transform parent)
    {
        var scrollGo = new GameObject("ScrollView", typeof(RectTransform));
        scrollGo.transform.SetParent(parent, false);
        var scrollRt = scrollGo.GetComponent<RectTransform>();
        scrollRt.anchorMin = scrollRt.anchorMax = scrollRt.pivot = new Vector2(0.5f, 0.5f);
        scrollRt.anchoredPosition = new Vector2(0, -15);
        scrollRt.sizeDelta = new Vector2(900, 1450);

        var scroll = scrollGo.AddComponent<ScrollRect>();
        scroll.horizontal = false;
        scroll.vertical = true;
        scroll.movementType = ScrollRect.MovementType.Elastic;
        scroll.elasticity = 0.12f;
        scroll.decelerationRate = 0.12f;
        scroll.scrollSensitivity = 32f;

        var viewportGo = new GameObject("Viewport", typeof(RectTransform));
        viewportGo.transform.SetParent(scrollGo.transform, false);
        var viewportRt = viewportGo.GetComponent<RectTransform>();
        Stretch(viewportRt);
        var viewportImage = viewportGo.AddComponent<Image>();
        viewportImage.color = new Color(1, 1, 1, 0.004f);
        viewportImage.raycastTarget = true;
        viewportGo.AddComponent<RectMask2D>();

        var contentGo = new GameObject("Content", typeof(RectTransform));
        contentGo.transform.SetParent(viewportGo.transform, false);
        var contentRt = contentGo.GetComponent<RectTransform>();
        contentRt.anchorMin = new Vector2(0, 1);
        contentRt.anchorMax = new Vector2(1, 1);
        contentRt.pivot = new Vector2(0.5f, 1);
        contentRt.anchoredPosition = Vector2.zero;
        contentRt.sizeDelta = Vector2.zero;

        var layout = contentGo.AddComponent<VerticalLayoutGroup>();
        layout.spacing = 15;
        layout.padding = new RectOffset(10, 10, 8, 28);
        layout.childAlignment = TextAnchor.UpperCenter;
        layout.childControlWidth = true;
        layout.childControlHeight = true;
        layout.childForceExpandWidth = true;
        layout.childForceExpandHeight = false;
        var fitter = contentGo.AddComponent<ContentSizeFitter>();
        fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        fitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;

        scroll.viewport = viewportRt;
        scroll.content = contentRt;
        return scroll;
    }

    static void SectionHeader(Transform parent, string text)
    {
        var band = Img(parent, "Section_" + text.Replace(" ", "_"), Cut("bar_red.png"), new Color(0.65f, 0.06f, 0.16f));
        var le = band.gameObject.AddComponent<LayoutElement>();
        le.preferredHeight = le.minHeight = 122;
        Label(band.transform, "Label", text, Title, Vector2.zero, new Vector2(780, 80), 46, White, true);
    }

    static void CoinRow(Transform parent, string name, Sprite icon, string amount, string fallbackPrice, int coins)
    {
        var row = Row(parent, name, 182);
        var iconImage = Img(row.transform, "Icon", icon, Gold);
        iconImage.preserveAspect = true;
        iconImage.raycastTarget = false;
        Place(iconImage.rectTransform, new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(108, 1), new Vector2(154, 154));

        var amountText = Label(row.transform, "Amount", amount, Num, new Vector2(-68, 0), new Vector2(300, 100), 54, Ink, true);
        amountText.resizeTextForBestFit = true;
        amountText.resizeTextMinSize = 36;
        amountText.resizeTextMaxSize = 54;

        var buy = Btn(row.transform, "Buy", Cut("btn_action.png"), new Color(1f, 0.45f, 0.05f),
            new Vector2(1, 0.5f), new Vector2(-172, 0), new Vector2(302, 110));
        Label(buy.transform, "Price", fallbackPrice, Num, Vector2.zero, new Vector2(270, 72), 37, White, true);
        var tag = buy.gameObject.AddComponent<InGameShopButton>();
        tag.action = InGameShopButton.Act.GrantCoins;
        tag.amount = coins;
    }

    static void PromoRow(Transform parent, string name, Sprite icon, string title, string fallbackPrice)
    {
        var row = Row(parent, name, 168);
        var iconImage = Img(row.transform, "Icon", icon, White);
        iconImage.preserveAspect = true;
        iconImage.raycastTarget = false;
        Place(iconImage.rectTransform, new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(92, 0), new Vector2(124, 124));
        var titleText = Label(row.transform, "OfferTitle", title, Title, new Vector2(-78, 0), new Vector2(355, 90), 32, Ink, false);
        titleText.resizeTextForBestFit = true;
        titleText.resizeTextMinSize = 23;
        titleText.resizeTextMaxSize = 32;

        var priceBg = Btn(row.transform, "PriceBg", Cut("btn_action.png"), new Color(1f, 0.45f, 0.05f),
            new Vector2(1, 0.5f), new Vector2(-166, 0), new Vector2(292, 104));
        Label(priceBg.transform, "Price", fallbackPrice, Num, Vector2.zero, new Vector2(260, 68), 34, White, true);
    }

    static void BonusPromoRow(Transform parent, string name, string fallbackPrice)
    {
        var row = Row(parent, name, 210);
        var ads = Img(row.transform, "Icon", Cut("ads_icon.png"), White);
        ads.preserveAspect = true; ads.raycastTarget = false;
        Place(ads.rectTransform, new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(82, 0), new Vector2(112, 112));

        var title = Label(row.transform, "OfferTitle", "REMOVE ADS + BONUS", Title,
            new Vector2(-100, 42), new Vector2(370, 54), 30, Ink, false);
        title.resizeTextForBestFit = true; title.resizeTextMinSize = 22; title.resizeTextMaxSize = 30;

        var recolor = Img(row.transform, "Bonus_Recolor", Cut("joker_recolor.png"), White);
        recolor.preserveAspect = true; recolor.raycastTarget = false;
        Place(recolor.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(-195, -42), new Vector2(54, 54));
        Label(row.transform, "RecolorAmount", "×1", Num, new Vector2(-145, -42), new Vector2(58, 45), 26, Ink, true);

        var coin = Img(row.transform, "Bonus_Coin", Cut("icon_coin.png"), Gold);
        coin.preserveAspect = true; coin.raycastTarget = false;
        Place(coin.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(-72, -42), new Vector2(54, 54));
        Label(row.transform, "CoinAmount", "200", Num, new Vector2(-5, -42), new Vector2(88, 45), 26, Ink, true);

        var priceBg = Btn(row.transform, "PriceBg", Cut("btn_action.png"), new Color(1f, 0.45f, 0.05f),
            new Vector2(1, 0.5f), new Vector2(-166, 0), new Vector2(292, 104));
        Label(priceBg.transform, "Price", fallbackPrice, Num, Vector2.zero, new Vector2(260, 68), 34, White, true);
    }

    static void RestoreButton(Transform parent)
    {
        var button = Btn(parent, "RestorePurchases", Cut("btn_orange.png"), White,
            new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(460, 136));
        var le = button.gameObject.AddComponent<LayoutElement>();
        le.preferredHeight = le.minHeight = 146;
        Label(button.transform, "Label", "RESTORE PURCHASES", Title, Vector2.zero, new Vector2(420, 72), 31, White, true);
    }

    static void JokerRow(Transform parent, string name, Sprite icon, string title)
    {
        var row = Row(parent, name, 164);
        var iconImage = Img(row.transform, "Icon", icon, White);
        iconImage.preserveAspect = true;
        iconImage.raycastTarget = false;
        Place(iconImage.rectTransform, new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(94, 0), new Vector2(126, 126));
        Label(row.transform, "PowerupTitle", title, Title, new Vector2(-82, 0), new Vector2(345, 82), 34, Ink, true);

        var buy = Btn(row.transform, "Buy", Cut("btn_action.png"), new Color(1f, 0.45f, 0.05f),
            new Vector2(1, 0.5f), new Vector2(-166, 0), new Vector2(292, 104));
        var coin = Img(buy.transform, "Coin", Cut("icon_coin.png"), Gold);
        coin.preserveAspect = true;
        coin.raycastTarget = false;
        Place(coin.rectTransform, new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(52, 0), new Vector2(54, 54));
        Label(buy.transform, "Price", "100", Num, new Vector2(25, 0), new Vector2(200, 66), 36, White, true);
        var tag = buy.gameObject.AddComponent<InGameShopButton>();
        tag.action = InGameShopButton.Act.SpendJoker;
    }

    static Image Row(Transform parent, string name, float height)
    {
        var row = Img(parent, name, Cut("bar_cream.png"), new Color(1f, 0.92f, 0.78f));
        var le = row.gameObject.AddComponent<LayoutElement>();
        le.preferredHeight = le.minHeight = height;
        row.raycastTarget = true;
        return row;
    }

    static Sprite Cut(string file) => AssetDatabase.LoadAssetAtPath<Sprite>(Art + file);

    static Image Img(Transform parent, string name, Sprite sprite, Color fallback)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        var image = go.AddComponent<Image>();
        if (sprite != null) { image.sprite = sprite; image.color = White; }
        else image.color = fallback;
        return image;
    }

    static Text Label(Transform parent, string name, string text, Font font, Vector2 pos, Vector2 size,
        int fontSize, Color color, bool outline)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        var label = go.AddComponent<Text>();
        label.font = font;
        label.text = text;
        label.fontSize = fontSize;
        label.color = color;
        label.alignment = TextAnchor.MiddleCenter;
        label.horizontalOverflow = HorizontalWrapMode.Overflow;
        label.verticalOverflow = VerticalWrapMode.Overflow;
        label.raycastTarget = false;
        var shadow = go.AddComponent<Shadow>();
        shadow.effectColor = new Color(0, 0, 0, color == White ? 0.58f : 0.20f);
        shadow.effectDistance = new Vector2(2, -3);
        if (outline)
        {
            var ol = go.AddComponent<Outline>();
            ol.effectColor = color == White ? new Color(0.20f, 0.04f, 0.07f, 0.82f) : new Color(1f, 0.88f, 0.55f, 0.45f);
            ol.effectDistance = new Vector2(2, -2);
        }
        var rt = label.rectTransform;
        rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = pos;
        rt.sizeDelta = size;
        return label;
    }

    static Button Btn(Transform parent, string name, Sprite sprite, Color fallback, Vector2 anchor, Vector2 pos, Vector2 size)
    {
        var image = Img(parent, name, sprite, fallback);
        var rt = image.rectTransform;
        rt.anchorMin = rt.anchorMax = anchor;
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = pos;
        rt.sizeDelta = size;
        var button = image.gameObject.AddComponent<Button>();
        button.targetGraphic = image;
        button.transition = Selectable.Transition.ColorTint;
        var colors = button.colors;
        colors.highlightedColor = new Color(1f, 0.94f, 0.78f);
        colors.pressedColor = new Color(0.88f, 0.82f, 0.70f);
        colors.fadeDuration = 0.08f;
        button.colors = colors;
        return button;
    }

    static void Place(RectTransform rt, Vector2 min, Vector2 max, Vector2 pos, Vector2 size)
    {
        rt.anchorMin = min;
        rt.anchorMax = max;
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = pos;
        rt.sizeDelta = size;
    }

    static void Center(RectTransform rt, Vector2 size)
    {
        rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = Vector2.zero;
        rt.sizeDelta = size;
    }

    static void Stretch(RectTransform rt)
    {
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;
    }
}
