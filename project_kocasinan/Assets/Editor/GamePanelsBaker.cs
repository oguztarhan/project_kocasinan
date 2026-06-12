using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEditor;
using UnityEditor.SceneManagement;
using BusJam;

/// <summary>
/// Editor tool that BAKES the in-game Settings / Continue / Failed pop-ups into the open
/// scene as real, fully editable GameObjects (mirrors the code-built panels). At play time
/// <see cref="GameUI"/> finds them via the <see cref="InGamePanels"/> marker and uses them
/// instead of building them in code, wiring each tagged button (<see cref="InGamePanelButton"/>).
///
/// Panels are baked inactive (root canvas stays active) so they don't cover the editor; to
/// edit one, tick it active in the Hierarchy. Re-running clears the previous bake.
///
/// Menu:  Tools ▸ 300Mind UI ▸ Bake In-Game Panels (settings/continue/failed)
/// </summary>
public static class GamePanelsBaker
{
    static readonly Color White = Color.white;
    static readonly Color Gold = new Color(1f, 0.85f, 0.30f);
    static readonly Color Dim = new Color(0, 0, 0, 0.6f);
    static readonly Vector2 C = new Vector2(0.5f, 0.5f);

    static Font Title => UIKit.Title();
    static Font Num => UIKit.Num();

    const string IconSoundPath = "Assets/MenuManager/Icons/Icon_Sound.png";
    const string IconMusicPath = "Assets/MenuManager/Icons/Icon_Music.png";

    [MenuItem("Tools/300Mind UI/Bake In-Game Panels (settings/continue/failed/success)")]
    static void Bake()
    {
        foreach (var go in UnityEngine.SceneManagement.SceneManager.GetActiveScene().GetRootGameObjects())
            if (go.name == "InGamePanels_Baked") Object.DestroyImmediate(go);

        var rootGo = new GameObject("InGamePanels_Baked");
        var canvas = rootGo.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 60; // above the HUD (0) and the shop (50)
        var scaler = rootGo.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1080, 1920);
        scaler.matchWidthOrHeight = 0.5f;
        rootGo.AddComponent<GraphicRaycaster>();
        var marker = rootGo.AddComponent<InGamePanels>();
        var root = rootGo.transform;

        if (Object.FindAnyObjectByType<EventSystem>() == null)
        {
            var es = new GameObject("EventSystem");
            es.transform.SetParent(root, false);
            es.AddComponent<EventSystem>();
            es.AddComponent<InputSystemUIInputModule>().AssignDefaultActions();
        }

        marker.settings      = BuildSettings(root);
        marker.continuePanel = BuildContinue(root);
        marker.failed        = BuildFailed(root);
        marker.success       = BuildSuccess(root);
        BuildJokerBuy(root, marker);

        EditorUtility.SetDirty(marker);
        EditorSceneManager.MarkSceneDirty(UnityEngine.SceneManagement.SceneManager.GetActiveScene());
        Selection.activeGameObject = rootGo;
        Debug.Log("[GamePanelsBaker] Baked Settings / Continue / Failed / Success (hidden in editor). SAVE the scene (Ctrl+S). Tick a panel active to edit it.");
    }

    // ---- Settings: #A12929 card, blue title tile, SOUND/MUSIC toggles, empty + HOME/REPLAY ----
    static GameObject BuildSettings(Transform root)
    {
        var panel = Img(root, "Panel_Settings", null, Dim);
        Stretch(panel.rectTransform);

        var card = Img(panel.transform, "Card", UIKit.EmptyBoxBlue(), White);
        card.color = new Color(0.631f, 0.161f, 0.161f); // #A12929
        Center(card.rectTransform, new Vector2(820, 1050));

        var tile = Img(card.transform, "TitleTile", UIKit.TitleBarA(), new Color(0.25f, 0.55f, 0.90f));
        tile.color = new Color(0.25f, 0.55f, 0.90f); tile.raycastTarget = false;
        Place(tile.rectTransform, C, C, new Vector2(0, 430), new Vector2(580, 130));
        Label(card.transform, "Title", "SETTINGS", Title, new Vector2(0, 430), new Vector2(560, 100), 56, White);

        AudioToggle(card.transform, "Toggle_Sound", -160, LoadIcon(IconSoundPath), InGamePanelButton.Act.ToggleSound);
        AudioToggle(card.transform, "Toggle_Music",  160, LoadIcon(IconMusicPath), InGamePanelButton.Act.ToggleMusic);

        Btn(card.transform, "Btn_Empty", UIKit.PriceBtnB(), new Color(0.95f, 0.78f, 0.20f), C, new Vector2(0, 40), new Vector2(430, 120));

        var home = Btn(card.transform, "Home", UIKit.PriceBtnB(), new Color(0.95f, 0.78f, 0.20f), C, new Vector2(-180, -120), new Vector2(320, 120));
        Label(home.transform, "Label", "HOME", Title, Vector2.zero, new Vector2(320, 80), 40, White);
        Tag(home, InGamePanelButton.Act.Home);
        var replay = Btn(card.transform, "Replay", UIKit.PriceBtnB(), new Color(0.95f, 0.78f, 0.20f), C, new Vector2(180, -120), new Vector2(320, 120));
        Label(replay.transform, "Label", "REPLAY", Title, Vector2.zero, new Vector2(320, 80), 38, White);
        Tag(replay, InGamePanelButton.Act.Replay);

        var close = Btn(card.transform, "Close", UIKit.CloseX(), new Color(0.85f, 0.2f, 0.2f), new Vector2(1, 1), new Vector2(-40, -40), new Vector2(96, 96));
        Tag(close, InGamePanelButton.Act.Close);

        panel.gameObject.SetActive(false);
        return panel.gameObject;
    }

    // ---- Continue: WATCH AD / PAY 100 / red close (decline) ----
    static GameObject BuildContinue(Transform root)
    {
        var panel = Img(root, "Panel_Continue", null, Dim);
        Stretch(panel.rectTransform);

        var card = Img(panel.transform, "Card", UIKit.EmptyBoxBlue(), new Color(0.25f, 0.55f, 0.90f));
        Center(card.rectTransform, new Vector2(820, 1000));
        Label(card.transform, "Title", "CONTINUE?", Title, new Vector2(0, 360), new Vector2(700, 100), 62, White);

        var ad = Btn(card.transform, "WatchAd", UIKit.ShopIconBgA(), new Color(0.3f, 0.75f, 0.35f), C, new Vector2(0, 60), new Vector2(580, 160));
        var adi = Img(ad.transform, "Icon", UIKit.WatchAd(), new Color(0.5f, 0.7f, 0.9f)); adi.raycastTarget = false;
        Place(adi.rectTransform, new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(85, 0), new Vector2(95, 95));
        Label(ad.transform, "Label", "WATCH AD", Title, new Vector2(45, 0), new Vector2(420, 70), 40, White);
        Tag(ad, InGamePanelButton.Act.ContinueAd);

        var pay = Btn(card.transform, "Pay", UIKit.ShopIconBgB(), new Color(0.95f, 0.6f, 0.25f), C, new Vector2(0, -150), new Vector2(580, 160));
        var payc = Img(pay.transform, "Coin", UIKit.Coin(), Gold); payc.raycastTarget = false;
        Place(payc.rectTransform, new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(110, 0), new Vector2(70, 70));
        Label(pay.transform, "Label", "150", Title, new Vector2(40, 0), new Vector2(440, 70), 44, White);
        Tag(pay, InGamePanelButton.Act.ContinuePay);

        var close = Btn(card.transform, "Close", UIKit.CloseX(), new Color(0.85f, 0.2f, 0.2f), new Vector2(1, 1), new Vector2(-40, -40), new Vector2(96, 96));
        Tag(close, InGamePanelButton.Act.Close); // decline

        panel.gameObject.SetActive(false);
        return panel.gameObject;
    }

    // ---- Failed: red title tile, HOME + REPLAY ----
    static GameObject BuildFailed(Transform root)
    {
        var panel = Img(root, "Panel_Failed", null, Dim);
        Stretch(panel.rectTransform);

        var card = Img(panel.transform, "Card", UIKit.EmptyBoxBlue(), new Color(0.25f, 0.55f, 0.90f));
        Center(card.rectTransform, new Vector2(820, 1000));

        var tile = Img(card.transform, "TitleTile", UIKit.TitleBarB(), new Color(0.85f, 0.2f, 0.2f)); tile.raycastTarget = false;
        Place(tile.rectTransform, C, C, new Vector2(0, 360), new Vector2(560, 150));
        Label(card.transform, "Title", "FAIL", Title, new Vector2(0, 360), new Vector2(540, 110), 72, White);

        var home = Btn(card.transform, "Home", UIKit.ShopIconBgA(), new Color(0.4f, 0.8f, 0.45f), C, new Vector2(-170, -100), new Vector2(300, 170));
        Label(home.transform, "Label", "HOME", Title, Vector2.zero, new Vector2(300, 90), 40, White);
        Tag(home, InGamePanelButton.Act.Home);
        var replay = Btn(card.transform, "Replay", UIKit.ShopIconBgB(), new Color(0.95f, 0.6f, 0.25f), C, new Vector2(170, -100), new Vector2(300, 170));
        Label(replay.transform, "Label", "REPLAY", Title, Vector2.zero, new Vector2(300, 90), 38, White);
        Tag(replay, InGamePanelButton.Act.Replay);

        panel.gameObject.SetActive(false);
        return panel.gameObject;
    }

    // ---- Joker buy pop-ups: one per joker, each with its icon + gold price baked in ----
    static void BuildJokerBuy(Transform root, InGamePanels marker)
    {
        marker.jokerBuyRecolor = BuildJokerBuyOne(root, "Panel_JokerBuy_Recolor", UIKit.JokerRecolor(), "80",  out marker.jokerBuyRecolorBtn);
        marker.jokerBuySwap    = BuildJokerBuyOne(root, "Panel_JokerBuy_Swap",    UIKit.JokerSwap(),    "40",  out marker.jokerBuySwapBtn);
        marker.jokerBuyHeli    = BuildJokerBuyOne(root, "Panel_JokerBuy_Heli",    UIKit.JokerHeli(),    "100", out marker.jokerBuyHeliBtn);
    }

    static GameObject BuildJokerBuyOne(Transform root, string name, Sprite icon, string gold, out Button buyBtn)
    {
        var panel = Img(root, name, null, Dim);
        Stretch(panel.rectTransform);

        var card = Img(panel.transform, "Card", UIKit.EmptyBoxBlue(), White); card.color = White;
        Center(card.rectTransform, new Vector2(620, 700));

        var ic = Img(card.transform, "Icon", icon, White); ic.raycastTarget = false;
        Place(ic.rectTransform, C, C, new Vector2(0, 130), new Vector2(230, 230));

        buyBtn = Btn(card.transform, "Buy", UIKit.PriceBtnA(), new Color(0.30f, 0.75f, 0.35f), C, new Vector2(0, -150), new Vector2(380, 130));
        var bc = Img(buyBtn.transform, "Coin", UIKit.Coin(), Gold); bc.raycastTarget = false;
        Place(bc.rectTransform, new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(46, 0), new Vector2(60, 60));
        Label(buyBtn.transform, "Price", gold, Title, new Vector2(30, 0), new Vector2(380, 80), 44, White);

        var close = Btn(card.transform, "Close", UIKit.CloseX(), new Color(0.85f, 0.2f, 0.2f), new Vector2(1, 1), new Vector2(-40, -40), new Vector2(96, 96));
        Tag(close, InGamePanelButton.Act.Close);

        panel.gameObject.SetActive(false);
        return panel.gameObject;
    }

    // A sound/music toggle button (atlas1_37) with the crisp logo on top; GameUI drives the
    // fade colour + on/off state at runtime.
    static void AudioToggle(Transform card, string name, float x, Sprite logo, InGamePanelButton.Act act)
    {
        var btn = Btn(card, name, UIKit.PriceBtnB(), new Color(0.95f, 0.78f, 0.20f), C, new Vector2(x, 230), new Vector2(220, 150));
        var ico = Img(btn.transform, "Logo", logo, White); ico.raycastTarget = false;
        Center(ico.rectTransform, new Vector2(110, 110));
        Tag(btn, act);
    }

    // ---- Success / achievement: reward coin + amount, NEXT (claim 20) + AD x2 (claim 40) ----
    static GameObject BuildSuccess(Transform root)
    {
        var panel = Img(root, "Panel_Success", null, new Color(0, 0, 0, 0.65f));
        Stretch(panel.rectTransform);

        var card = Img(panel.transform, "Card", UIKit.EmptyBoxBlue(), new Color(0.25f, 0.55f, 0.90f));
        Center(card.rectTransform, new Vector2(820, 1000));

        var tile = Img(card.transform, "TitleTile", UIKit.TitleBarC(), new Color(0.30f, 0.65f, 0.95f)); tile.raycastTarget = false;
        Place(tile.rectTransform, C, C, new Vector2(0, 380), new Vector2(660, 150));
        Label(card.transform, "Title", "ACHIEVEMENT", Title, new Vector2(0, 380), new Vector2(640, 110), 56, White);

        var rc = Img(card.transform, "Coin", UIKit.Coin(), Gold); rc.raycastTarget = false;
        Center(rc.rectTransform, new Vector2(180, 180)); rc.rectTransform.anchoredPosition = new Vector2(0, 130);
        Label(card.transform, "Reward", "+20", Title, new Vector2(0, -10), new Vector2(600, 90), 64, Gold);

        var next = Btn(card.transform, "Next", UIKit.ShopIconBgA(), new Color(0.3f, 0.75f, 0.35f), C, new Vector2(0, -180), new Vector2(580, 150));
        Label(next.transform, "Label", "NEXT", Title, Vector2.zero, new Vector2(580, 90), 46, White);
        TagClaim(next, 20);

        var ad = Btn(card.transform, "AdReward", UIKit.ShopIconBgB(), new Color(0.95f, 0.6f, 0.25f), C, new Vector2(0, -345), new Vector2(580, 145));
        var adi = Img(ad.transform, "Icon", UIKit.WatchAd(), new Color(0.5f, 0.7f, 0.9f)); adi.raycastTarget = false;
        Place(adi.rectTransform, new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(80, 0), new Vector2(85, 85));
        Label(ad.transform, "Label", "AD  x2", Title, new Vector2(40, 0), new Vector2(440, 70), 40, White);
        TagClaim(ad, 40);

        panel.gameObject.SetActive(false);
        return panel.gameObject;
    }

    static void Tag(Button b, InGamePanelButton.Act act) { b.gameObject.AddComponent<InGamePanelButton>().action = act; }
    static void TagClaim(Button b, int amount) { var t = b.gameObject.AddComponent<InGamePanelButton>(); t.action = InGamePanelButton.Act.Claim; t.amount = amount; }

    // Load a PNG as a Sprite, forcing Sprite import (project defaults to 3D = Texture).
    static Sprite LoadIcon(string path)
    {
        var ti = AssetImporter.GetAtPath(path) as TextureImporter;
        if (ti != null && (ti.textureType != TextureImporterType.Sprite || ti.spriteImportMode != SpriteImportMode.Single))
        {
            ti.textureType = TextureImporterType.Sprite;
            ti.spriteImportMode = SpriteImportMode.Single;
            ti.SaveAndReimport();
        }
        return AssetDatabase.LoadAssetAtPath<Sprite>(path);
    }

    // ============================================================================
    // Add the in-game LANGUAGE pop-up (additive: only adds Panel_Language). GameUI wires
    // the empty settings button to open it.
    // ============================================================================
    [MenuItem("Tools/300Mind UI/Add Language (in-game)")]
    static void AddLanguageInGame()
    {
        var rootGo = GameObject.Find("InGamePanels_Baked");
        if (!rootGo) { Debug.LogError("[GamePanelsBaker] Run 'Bake In-Game Panels' first."); return; }
        var marker = rootGo.GetComponent<InGamePanels>();
        var old = rootGo.transform.Find("Panel_Language");
        if (old) Object.DestroyImmediate(old.gameObject);
        marker.language = BuildLanguagePanel(rootGo.transform);
        EditorUtility.SetDirty(marker);
        EditorSceneManager.MarkSceneDirty(UnityEngine.SceneManagement.SceneManager.GetActiveScene());
        Selection.activeGameObject = marker.language;
        Debug.Log("[GamePanelsBaker] Added in-game Language pop-up. SAVE the scene (Ctrl+S).");
    }

    static GameObject BuildLanguagePanel(Transform root)
    {
        var panel = Img(root, "Panel_Language", null, Dim);
        Stretch(panel.rectTransform);
        var pbtn = panel.gameObject.AddComponent<Button>(); pbtn.transition = Selectable.Transition.None;

        var card = Img(panel.transform, "Card", UIKit.EmptyBoxBlue(), White); card.color = White;
        Center(card.rectTransform, new Vector2(720, 1180));
        Label(card.transform, "Title", "LANGUAGE", Title, new Vector2(0, 500), new Vector2(600, 100), 52, new Color(0.16f, 0.20f, 0.30f));

        var sel = panel.gameObject.AddComponent<LanguageSelector>();
        sel.panelRoot = panel.gameObject;
        sel.backdropButton = pbtn;

        float top = 390f, step = 92f;
        for (int i = 0; i < LanguageSelector.Names.Length; i++)
            LangOption(card.transform, "Opt_" + i, i, LanguageSelector.Names[i], new Vector2(0, top - i * step));

        var close = Btn(card.transform, "Close", UIKit.CloseX(), new Color(0.85f, 0.2f, 0.2f), new Vector2(1, 1), new Vector2(-34, -34), new Vector2(90, 90));
        sel.closeButton = close;

        panel.gameObject.SetActive(false);
        return panel.gameObject;
    }

    static void LangOption(Transform card, string name, int index, string text, Vector2 pos)
    {
        var btn = Btn(card, name, UIKit.PriceBtnA(), new Color(0.30f, 0.75f, 0.35f), C, pos, new Vector2(440, 80));
        Label(btn.transform, "Label", text, Num, new Vector2(-28, 0), new Vector2(340, 56), 34, White);
        var chk = Img(btn.transform, "Check", UIKit.CheckMark(), new Color(1f, 0.8f, 0.1f)); chk.raycastTarget = false;
        Place(chk.rectTransform, new Vector2(1, 0.5f), new Vector2(1, 0.5f), new Vector2(-40, 0), new Vector2(54, 54));
        chk.gameObject.SetActive(false);
        var lo = btn.gameObject.AddComponent<LanguageOption>();
        lo.index = index; lo.check = chk.gameObject; lo.button = btn;
    }

    // ============================================================================
    // Make all 3 joker-buy backdrops match (copies Panel_JokerBuy_Recolor's backdrop
    // colour to Swap + Heli). Only touches the backdrop Image colour, nothing else.
    // ============================================================================
    [MenuItem("Tools/300Mind UI/Fix Joker Buy Backdrops")]
    static void FixJokerBuyBackdrops()
    {
        var rootGo = GameObject.Find("InGamePanels_Baked");
        if (!rootGo) { Debug.LogError("[GamePanelsBaker] InGamePanels_Baked not found - bake first."); return; }
        var refT = rootGo.transform.Find("Panel_JokerBuy_Recolor");
        var refImg = refT != null ? refT.GetComponent<Image>() : null;
        var refColor = refImg != null ? refImg.color : Dim;
        int n = 0;
        foreach (var name in new[] { "Panel_JokerBuy_Swap", "Panel_JokerBuy_Heli" })
        {
            var t = rootGo.transform.Find(name);
            var img = t != null ? t.GetComponent<Image>() : null;
            if (img != null) { img.color = refColor; n++; }
        }
        EditorSceneManager.MarkSceneDirty(UnityEngine.SceneManagement.SceneManager.GetActiveScene());
        Debug.Log($"[GamePanelsBaker] Matched {n} joker-buy backdrop(s) to Recolor's. SAVE the scene (Ctrl+S).");
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
