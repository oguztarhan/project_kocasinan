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
