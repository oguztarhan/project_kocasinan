using UnityEditor;
using UnityEditor.Events;
using UnityEngine;
using UnityEngine.UI;
using Ridebury;

/// <summary>Focused, non-destructive patch for the dedicated no-ads popup in MenuUI.</summary>
public static class StorefrontMenuPolisher
{
    const string PrefabPath = "Assets/Resources/UI/MenuUI.prefab";
    const string Art = "Assets/kesilmis-ikonlar/";
    const string Marker = "RemoveAdsDesign_20260828_V4";

    static readonly Color White = Color.white;
    static readonly Color Ink = new Color(0.24f, 0.055f, 0.10f);
    static readonly Color Gold = new Color(1f, 0.83f, 0.18f);

    [InitializeOnLoadMethod]
    static void Schedule()
    {
        EditorApplication.update -= TryPatch;
        EditorApplication.update += TryPatch;
    }

    [MenuItem("Tools/300Mind UI/Polish Remove Ads Panel")]
    public static void PatchFromMenu() => Patch();

    static void TryPatch()
    {
        if (EditorApplication.isCompiling || EditorApplication.isUpdating) return;
        EditorApplication.update -= TryPatch;
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
        var panel = prefab != null ? FindDeep(prefab.transform, "Panel_RemoveAds") : null;
        if (panel != null && FindDeep(panel, Marker) != null) return;
        Patch();
    }

    static void Patch()
    {
        var root = PrefabUtility.LoadPrefabContents(PrefabPath);
        try
        {
            var ctrl = root.GetComponent<MenuController>();
            var panel = FindDeep(root.transform, "Panel_RemoveAds");
            if (ctrl == null || panel == null)
            {
                Debug.LogError("[StorefrontMenuPolisher] MenuController or Panel_RemoveAds is missing.");
                return;
            }

            for (int i = panel.childCount - 1; i >= 0; i--)
                Object.DestroyImmediate(panel.GetChild(i).gameObject);

            var backdrop = panel.GetComponent<Image>();
            if (backdrop == null) backdrop = panel.gameObject.AddComponent<Image>();
            backdrop.sprite = null;
            backdrop.color = new Color(0, 0, 0, 0.62f);
            backdrop.raycastTarget = true;
            var backdropButton = panel.GetComponent<Button>();
            if (backdropButton == null) backdropButton = panel.gameObject.AddComponent<Button>();
            backdropButton.transition = Selectable.Transition.None;
            backdropButton.targetGraphic = backdrop;
            backdropButton.onClick = new Button.ButtonClickedEvent();
            UnityEventTools.AddPersistentListener(backdropButton.onClick, ctrl.CloseAll);

            var card = Img(panel, "Card", Cut("lacivert-panel-standart-temiz.png"), new Color(0.02f, 0.09f, 0.25f));
            Center(card.rectTransform, new Vector2(900, 1160));
            card.raycastTarget = true;

            Label(card.transform, "Title", "REMOVE ADS", UIKit.Title(), new Vector2(0, 470), new Vector2(650, 88), 55, White, true);
            var close = Btn(card.transform, "Close", Cut("icon_close.png"), White,
                new Vector2(1, 1), new Vector2(-54, -52), new Vector2(98, 98));
            close.image.preserveAspect = true;
            UnityEventTools.AddPersistentListener(close.onClick, ctrl.CloseAll);

            // Keep the offer stack as one centred unit. This prevents the rows drifting toward an edge when the
            // surrounding panel is resized for a different phone aspect ratio.
            var offers = new GameObject("OffersGroup", typeof(RectTransform)).GetComponent<RectTransform>();
            offers.SetParent(card.transform, false);
            Center(offers, new Vector2(790, 650));
            offers.anchoredPosition = new Vector2(0, 20);

            Offer(offers, "Offer_RemoveBanner", Cut("ads_icon.png"), "REMOVE BANNER", "$0.99", new Vector2(0, 205), 158);
            Offer(offers, "Offer_RemoveAds", Cut("ads_icon.png"), "REMOVE ADS", "$9.99", new Vector2(0, 15), 158);
            BonusOffer(offers, new Vector2(0, -205));

            var restore = Btn(card.transform, "RestorePurchases", Cut("btn_orange.png"), White,
                new Vector2(0.5f, 0.5f), new Vector2(0, -455), new Vector2(440, 132));
            Label(restore.transform, "Label", "RESTORE PURCHASES", UIKit.Title(), Vector2.zero, new Vector2(400, 66), 28, White, true);
            UnityEventTools.AddPersistentListener(restore.onClick, ctrl.RestorePurchases);

            var marker = new GameObject(Marker);
            marker.transform.SetParent(panel, false);
            marker.SetActive(false);

            ctrl.removeAdsPanel = panel.gameObject;
            EditorUtility.SetDirty(ctrl);
            PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
            AssetDatabase.SaveAssets();
            Debug.Log("[StorefrontMenuPolisher] Rebuilt Remove Ads panel -> " + PrefabPath);
        }
        finally { PrefabUtility.UnloadPrefabContents(root); }
    }

    static void Offer(Transform parent, string name, Sprite icon, string title, string price, Vector2 pos, float height)
    {
        var row = Img(parent, name, Cut("bar_cream.png"), new Color(1f, 0.92f, 0.78f));
        Place(row.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), pos, new Vector2(790, height));
        var ico = Img(row.transform, "Icon", icon, White);
        ico.preserveAspect = true; ico.raycastTarget = false;
        Place(ico.rectTransform, new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(80, 0), new Vector2(108, 108));
        var titleText = Label(row.transform, "OfferTitle", title, UIKit.Title(), new Vector2(-70, 0), new Vector2(355, 72), 33, Ink, false);
        titleText.resizeTextForBestFit = true; titleText.resizeTextMinSize = 23; titleText.resizeTextMaxSize = 33;
        var buy = Btn(row.transform, "PriceBg", Cut("btn_action.png"), new Color(1f, 0.45f, 0.05f),
            new Vector2(1, 0.5f), new Vector2(-142, 0), new Vector2(255, 94));
        Label(buy.transform, "Price", price, UIKit.Num(), Vector2.zero, new Vector2(225, 60), 32, White, true);
    }

    static void BonusOffer(Transform parent, Vector2 pos)
    {
        var row = Img(parent, "Offer_RemoveAdsPlus", Cut("bar_cream.png"), new Color(1f, 0.92f, 0.78f));
        Place(row.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), pos, new Vector2(790, 210));
        var ads = Img(row.transform, "Icon", Cut("ads_icon.png"), White);
        ads.preserveAspect = true; ads.raycastTarget = false;
        Place(ads.rectTransform, new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(76, 0), new Vector2(104, 104));

        var title = Label(row.transform, "OfferTitle", "REMOVE ADS + BONUS", UIKit.Title(),
            new Vector2(-82, 42), new Vector2(330, 52), 28, Ink, false);
        title.resizeTextForBestFit = true; title.resizeTextMinSize = 21; title.resizeTextMaxSize = 28;
        var joker = Img(row.transform, "Bonus_Recolor", Cut("joker_recolor.png"), White);
        joker.preserveAspect = true; joker.raycastTarget = false;
        Place(joker.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(-175, -42), new Vector2(52, 52));
        Label(row.transform, "RecolorAmount", "×1", UIKit.Num(), new Vector2(-126, -42), new Vector2(55, 42), 25, Ink, true);
        var coin = Img(row.transform, "Bonus_Coin", Cut("icon_coin.png"), Gold);
        coin.preserveAspect = true; coin.raycastTarget = false;
        Place(coin.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(-55, -42), new Vector2(52, 52));
        Label(row.transform, "CoinAmount", "200", UIKit.Num(), new Vector2(8, -42), new Vector2(82, 42), 25, Ink, true);

        var buy = Btn(row.transform, "PriceBg", Cut("btn_action.png"), new Color(1f, 0.45f, 0.05f),
            new Vector2(1, 0.5f), new Vector2(-142, 0), new Vector2(255, 94));
        Label(buy.transform, "Price", "$12.99", UIKit.Num(), Vector2.zero, new Vector2(225, 60), 31, White, true);
    }

    static Sprite Cut(string file) => AssetDatabase.LoadAssetAtPath<Sprite>(Art + file);

    static Transform FindDeep(Transform root, string name)
    {
        foreach (var t in root.GetComponentsInChildren<Transform>(true)) if (t.name == name) return t;
        return null;
    }

    static Image Img(Transform parent, string name, Sprite sprite, Color fallback)
    {
        var go = new GameObject(name, typeof(RectTransform)); go.transform.SetParent(parent, false);
        var image = go.AddComponent<Image>();
        if (sprite != null) { image.sprite = sprite; image.color = White; } else image.color = fallback;
        return image;
    }

    static Button Btn(Transform parent, string name, Sprite sprite, Color fallback, Vector2 anchor, Vector2 pos, Vector2 size)
    {
        var image = Img(parent, name, sprite, fallback);
        Place(image.rectTransform, anchor, anchor, pos, size);
        var button = image.gameObject.AddComponent<Button>(); button.targetGraphic = image;
        return button;
    }

    static Text Label(Transform parent, string name, string value, Font font, Vector2 pos, Vector2 size, int fontSize, Color color, bool outline)
    {
        var go = new GameObject(name, typeof(RectTransform)); go.transform.SetParent(parent, false);
        var text = go.AddComponent<Text>(); text.font = font; text.text = value; text.fontSize = fontSize;
        text.color = color; text.alignment = TextAnchor.MiddleCenter; text.raycastTarget = false;
        text.horizontalOverflow = HorizontalWrapMode.Overflow; text.verticalOverflow = VerticalWrapMode.Overflow;
        var shadow = go.AddComponent<Shadow>(); shadow.effectColor = new Color(0, 0, 0, 0.45f); shadow.effectDistance = new Vector2(2, -2);
        if (outline) { var ol = go.AddComponent<Outline>(); ol.effectColor = color == White ? new Color(0.2f, 0.04f, 0.08f, 0.8f) : new Color(1f, 0.85f, 0.4f, 0.4f); ol.effectDistance = new Vector2(2, -2); }
        Place(text.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), pos, size);
        return text;
    }

    static void Place(RectTransform rt, Vector2 min, Vector2 max, Vector2 pos, Vector2 size)
    {
        rt.anchorMin = min; rt.anchorMax = max; rt.pivot = new Vector2(0.5f, 0.5f); rt.anchoredPosition = pos; rt.sizeDelta = size;
    }

    static void Center(RectTransform rt, Vector2 size)
    {
        rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f); rt.anchoredPosition = Vector2.zero; rt.sizeDelta = size;
    }
}
