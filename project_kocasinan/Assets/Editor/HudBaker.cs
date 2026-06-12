using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEditor;
using UnityEditor.SceneManagement;
using BusJam;

/// <summary>
/// Editor tool that BAKES the in-game HUD (level badge, coin bar, settings gear, people
/// badge, combo text, and the 3 joker buttons) into the open scene as real, editable
/// GameObjects. At play time <see cref="GameUI"/> adopts it via the <see cref="InGameHud"/>
/// marker and keeps driving the dynamic values (coins/level/people/joker locks). Edit any
/// element's colour / size / sprite / font in the Inspector. Re-running clears the bake.
///
/// Menu:  Tools ▸ 300Mind UI ▸ Bake In-Game HUD (into open scene)
/// </summary>
public static class HudBaker
{
    static readonly Color White = Color.white;
    static readonly Color Gold = new Color(1f, 0.85f, 0.30f);
    static readonly Color Dark = new Color(0.16f, 0.20f, 0.30f);
    static readonly Vector2 TL = new Vector2(0, 1);

    static Font Title => UIKit.Title();
    static Font Num => UIKit.Num();

    [MenuItem("Tools/300Mind UI/Bake In-Game HUD (into open scene)")]
    static void Bake()
    {
        foreach (var go in UnityEngine.SceneManagement.SceneManager.GetActiveScene().GetRootGameObjects())
            if (go.name == "InGameHud_Baked") Object.DestroyImmediate(go);

        var rootGo = new GameObject("InGameHud_Baked");
        var canvas = rootGo.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 10; // above gameplay, below shop (50) / panels (60)
        var scaler = rootGo.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1080, 1920);
        scaler.matchWidthOrHeight = 0.5f;
        rootGo.AddComponent<GraphicRaycaster>();
        var hud = rootGo.AddComponent<InGameHud>();
        var root = rootGo.transform;

        if (Object.FindAnyObjectByType<EventSystem>() == null)
        {
            var es = new GameObject("EventSystem");
            es.transform.SetParent(root, false);
            es.AddComponent<EventSystem>();
            es.AddComponent<InputSystemUIInputModule>().AssignDefaultActions();
        }

        // Transparent full-screen HUD container (shown/hidden at runtime).
        var panel = Img(root, "Hud", null, new Color(0, 0, 0, 0)); panel.raycastTarget = false;
        Stretch(panel.rectTransform);
        hud.hudRoot = panel.gameObject;

        // LEVEL badge (top-left).
        var badge = Img(panel.transform, "Level_Badge", UIKit.A(25), new Color(0.45f, 0.40f, 0.85f));
        Place(badge.rectTransform, TL, TL, new Vector2(110, -110), new Vector2(170, 170)); badge.raycastTarget = false;
        Label(badge.transform, "Level_Label", "LEVEL", Num, new Vector2(0, 42), new Vector2(160, 36), 24, White);
        hud.levelText = Label(badge.transform, "Level_Num", "1", Title, new Vector2(0, -16), new Vector2(160, 90), 64, White);

        // Theme text (top-left, under the badge).
        var theme = Label(panel.transform, "Theme_Text", "", Num, new Vector2(110, -210), new Vector2(260, 36), 22, new Color(0.85f, 0.9f, 1f));
        theme.rectTransform.anchorMin = theme.rectTransform.anchorMax = TL;
        theme.rectTransform.anchoredPosition = new Vector2(110, -210);
        hud.themeText = theme;

        // COIN bar (top-center) -> opens the shop.
        var coin = Btn(panel.transform, "Coin_Bar", UIKit.CoinBar(), Dark, new Vector2(0.5f, 1), new Vector2(0, -100), new Vector2(300, 96));
        hud.coinButton = coin;
        var ci = Img(coin.transform, "Coin_Icon", UIKit.Coin(), Gold); ci.raycastTarget = false;
        Place(ci.rectTransform, new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(42, 0), new Vector2(74, 74));
        hud.coinText = Label(coin.transform, "Coin_Text", "0", Num, new Vector2(35, 0), new Vector2(180, 60), 44, White);

        // SETTINGS gear (top-right) -> opens settings.
        hud.gearButton = Btn(panel.transform, "Btn_Gear", UIKit.Gear(), new Color(0.7f, 0.72f, 0.78f), new Vector2(1, 1), new Vector2(-90, -100), new Vector2(120, 120));

        // (People-left badge removed — the people count is already shown in the 3D board.)

        // COMBO text (hidden until a combo happens).
        hud.comboText = Label(panel.transform, "Combo_Text", "", Title, new Vector2(0, 360), new Vector2(900, 100), 70, Gold);
        hud.comboText.gameObject.SetActive(false);

        // 3 joker buttons (bottom). Costs here are placeholders; GameUI sets the real ones.
        hud.recolor = JokerButton(panel.transform, "Joker_Recolor", -260, UIKit.JokerRecolor(), 5);
        hud.swap    = JokerButton(panel.transform, "Joker_Swap",    0,    UIKit.JokerSwap(),    10);
        hud.heli    = JokerButton(panel.transform, "Joker_Heli",    260,  UIKit.JokerHeli(),    15);

        EditorUtility.SetDirty(hud);
        EditorSceneManager.MarkSceneDirty(UnityEngine.SceneManagement.SceneManager.GetActiveScene());
        Selection.activeGameObject = rootGo;
        Debug.Log("[HudBaker] Baked in-game HUD. Edit it in the Inspector, then SAVE the scene (Ctrl+S).");
    }

    static HudJoker JokerButton(Transform parent, string name, float x, Sprite icon, int unlock)
    {
        // Nice rounded button (atlas1_25) with the joker icon centred + a small count badge.
        var btn = Btn(parent, name, UIKit.A(25), new Color(0.45f, 0.40f, 0.85f), new Vector2(0.5f, 0), new Vector2(x, 70), new Vector2(180, 180));
        var ico = Img(btn.transform, "Icon", icon, White); ico.raycastTarget = false;
        Center(ico.rectTransform, new Vector2(112, 112));
        var lk = Img(btn.transform, "Lock", null, new Color(0, 0, 0, 0.55f)); lk.raycastTarget = false;
        Center(lk.rectTransform, new Vector2(180, 180));
        Label(lk.transform, "LockLabel", "LV " + unlock, Num, Vector2.zero, new Vector2(170, 60), 34, White);
        // Owned-count badge (atlas1_34), top-right corner.
        var cb = Img(btn.transform, "Counter", UIKit.A(34), new Color(0.95f, 0.78f, 0.20f)); cb.raycastTarget = false;
        Place(cb.rectTransform, new Vector2(1, 1), new Vector2(1, 1), new Vector2(-4, -4), new Vector2(72, 72));
        var ct = Label(cb.transform, "CounterText", "0", Num, Vector2.zero, new Vector2(72, 50), 32, White);
        return new HudJoker { button = btn, background = btn.GetComponent<Image>(), icon = ico, lockGo = lk.gameObject, counterGo = cb.gameObject, counterText = ct };
    }

    // ---- builders ----
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
