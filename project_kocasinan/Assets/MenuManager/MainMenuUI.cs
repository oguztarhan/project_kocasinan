using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.SceneManagement;
using BusJam; // SaveSystem lives here

/// <summary>
/// Runtime-built portrait uGUI overlay for the Main Menu, mirroring the
/// builder style of <see cref="BusJam.GameUI"/>. It is attached by
/// <see cref="MenuManager"/> at runtime (AddComponent), so no extra scene
/// wiring is required and the existing scene canvas/buttons stay untouched.
///
/// Adds:
///   * Bottom navigation bar: STORE (left) / HOME (center) / SKIN (right).
///   * Top-left PROFILE button -> 800x1000 profile panel (preset avatars + name).
///   * Top-center COIN + DIAMOND indicators; tapping either opens the Store.
///   * Store and Skin screens (only one screen visible at a time).
/// </summary>
public class MainMenuUI : MonoBehaviour
{
    // Preset avatar colors (no device-gallery upload; selectable swatches only).
    static readonly Color[] AvatarColors =
    {
        new Color(0.96f, 0.43f, 0.43f), new Color(0.98f, 0.64f, 0.33f),
        new Color(0.99f, 0.83f, 0.39f), new Color(0.48f, 0.81f, 0.52f),
        new Color(0.44f, 0.64f, 0.95f), new Color(0.68f, 0.52f, 0.92f),
    };
    static readonly Color Gold    = new Color(1f, 0.82f, 0.30f);
    static readonly Color Diamond = new Color(0.46f, 0.85f, 0.95f);

    Font font;
    Sprite knob;
    Transform root;

    // ---- Auto-bootstrap --------------------------------------------------
    // Build the overlay as soon as the MainMenu scene loads — before the player
    // presses Play — without depending on any other component's lifecycle. This
    // runs in the editor and in players, and also re-builds when the player
    // returns to the menu from gameplay.
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Bootstrap()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
        SceneManager.sceneLoaded += OnSceneLoaded;
        EnsureForActiveScene(); // handle the very first (boot) scene
    }

    static void OnSceneLoaded(Scene scene, LoadSceneMode mode) => EnsureForActiveScene();

    static void EnsureForActiveScene()
    {
        if (SceneManager.GetActiveScene().name != "MainMenu") return;
        if (FindAnyObjectByType<MainMenuUI>() != null) return; // already present
        var go = new GameObject("MainMenuUI");
        go.AddComponent<MainMenuUI>().Build();
    }

    GameObject storeScreen, skinScreen, profilePanel;
    Text coinText, diamondText;
    Image profileAvatar;
    InputField nameField;

    public void Build()
    {
        font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        // Runtime-generated circle (no built-in "UI/Skin/Knob.psd" dependency,
        // which is unavailable in Unity 6 players and logs a sprite error). Keeps
        // the round profile/coin/diamond/avatar dots without any console errors.
        knob = UISprites.Circle();

        var canvasGo = new GameObject("MainMenuUICanvas");
        var canvas = canvasGo.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        // Render strictly ABOVE any pre-existing scene canvas (the menu's own UI),
        // whatever order it uses — compute the current top and sit above it.
        int topOrder = 0;
        foreach (var c in Object.FindObjectsByType<Canvas>(FindObjectsSortMode.None))
            if (c != canvas) topOrder = Mathf.Max(topOrder, c.sortingOrder);
        canvas.sortingOrder = Mathf.Max(1000, topOrder + 100);
        var scaler = canvasGo.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1080, 1920);
        scaler.matchWidthOrHeight = 0.5f;
        canvasGo.AddComponent<GraphicRaycaster>();
        // Keep it at scene root so it can't be hidden/deactivated by a parent object.
        canvasGo.transform.SetParent(null, false);
        canvasGo.SetActive(true);
        root = canvasGo.transform;

        if (EventSystem.current == null)
        {
            var es = new GameObject("EventSystem");
            es.transform.SetParent(transform, false);
            es.AddComponent<EventSystem>();
            es.AddComponent<InputSystemUIInputModule>().AssignDefaultActions();
        }

        // Screens first, then the top bar and nav LAST so they stay on top and
        // the HOME button is always clickable above any open screen.
        BuildStoreScreen();
        BuildSkinScreen();
        BuildProfilePanel();
        BuildTopBar();
        BuildBottomNav();

        ShowHome(); // default view
    }

    // ---- Top bar: profile button + currency indicators -------------------
    void BuildTopBar()
    {
        // Profile button, TOP-LEFT. Pressing it again closes the panel (toggle).
        var pbtn = Button(root, "", new Vector2(0, 1), new Vector2(110, -110), new Vector2(150, 150),
            new Color(0.22f, 0.26f, 0.36f), ToggleProfile, 0);
        profileAvatar = Image(pbtn.transform, AvatarColors[Mathf.Clamp(SaveSystem.AvatarIndex, 0, AvatarColors.Length - 1)]);
        profileAvatar.sprite = knob;
        profileAvatar.raycastTarget = false;
        Center(profileAvatar.rectTransform, new Vector2(118, 118));

        // Coin indicator, TOP-CENTER (left of center). Clickable -> Store.
        var coinBtn = Button(root, "", new Vector2(0.5f, 1), new Vector2(-150, -110), new Vector2(250, 96),
            new Color(0.16f, 0.18f, 0.24f, 0.95f), OpenStore, 0);
        var coinDot = Image(coinBtn.transform, Gold); coinDot.sprite = knob; coinDot.raycastTarget = false;
        Anchor(coinDot.rectTransform, new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(48, 0), new Vector2(60, 60));
        coinText = Label(coinBtn.transform, "0", new Vector2(20, 0), new Vector2(170, 60), 40, Gold, TextAnchor.MiddleCenter);

        // Diamond indicator, TOP-CENTER (right of center). Clickable -> Store.
        var diaBtn = Button(root, "", new Vector2(0.5f, 1), new Vector2(150, -110), new Vector2(250, 96),
            new Color(0.16f, 0.18f, 0.24f, 0.95f), OpenStore, 0);
        var diaDot = Image(diaBtn.transform, Diamond); diaDot.sprite = knob; diaDot.raycastTarget = false;
        Anchor(diaDot.rectTransform, new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(48, 0), new Vector2(60, 60));
        diamondText = Label(diaBtn.transform, "0", new Vector2(20, 0), new Vector2(170, 60), 40, Diamond, TextAnchor.MiddleCenter);

        RefreshCurrencies();
    }

    void BuildBottomNav()
    {
        var bar = Image(root, new Color(0.10f, 0.12f, 0.18f, 0.96f));
        bar.rectTransform.anchorMin = new Vector2(0, 0); bar.rectTransform.anchorMax = new Vector2(1, 0);
        bar.rectTransform.pivot = new Vector2(0.5f, 0);
        bar.rectTransform.offsetMin = new Vector2(0, 0); bar.rectTransform.offsetMax = new Vector2(0, 190);

        Button(bar.transform, "STORE", new Vector2(0.5f, 0), new Vector2(-340, 95), new Vector2(300, 150),
            new Color(0.35f, 0.56f, 0.88f), OpenStore, 40);
        Button(bar.transform, "HOME", new Vector2(0.5f, 0), new Vector2(0, 95), new Vector2(300, 150),
            new Color(0.42f, 0.72f, 0.42f), ShowHome, 40);
        Button(bar.transform, "SKIN", new Vector2(0.5f, 0), new Vector2(340, 95), new Vector2(300, 150),
            new Color(0.88f, 0.52f, 0.40f), OpenSkin, 40);
    }

    // ---- Screens ---------------------------------------------------------
    void BuildStoreScreen()
    {
        storeScreen = FullScreen("StoreScreen", new Color(0.10f, 0.12f, 0.18f, 0.98f));
        Label(storeScreen.transform, "STORE", new Vector2(0, 720), new Vector2(900, 100), 70, Color.white);
        Label(storeScreen.transform, "Spend coins and diamonds here.", new Vector2(0, 600), new Vector2(900, 60), 34,
            new Color(0.8f, 0.84f, 0.92f));

        // A couple of placeholder offers (purely cosmetic for now).
        Button(storeScreen.transform, "100 Coins  -  10 Diamonds", new Vector2(0.5f, 0.5f), new Vector2(0, 360), new Vector2(760, 150),
            new Color(0.35f, 0.56f, 0.88f), () => { if (SaveSystem.Diamonds >= 10) { SaveSystem.AddDiamonds(-10); SaveSystem.AddCoins(100); RefreshCurrencies(); } }, 36);
        Button(storeScreen.transform, "Free Daily Diamond  (+1)", new Vector2(0.5f, 0.5f), new Vector2(0, 160), new Vector2(760, 150),
            new Color(0.46f, 0.85f, 0.95f), () => { SaveSystem.AddDiamonds(1); RefreshCurrencies(); }, 36);
    }

    void BuildSkinScreen()
    {
        skinScreen = FullScreen("SkinScreen", new Color(0.12f, 0.10f, 0.16f, 0.98f));
        Label(skinScreen.transform, "SKINS", new Vector2(0, 720), new Vector2(900, 100), 70, Color.white);
        Label(skinScreen.transform, "Pick a look (more coming soon).", new Vector2(0, 600), new Vector2(900, 60), 34,
            new Color(0.85f, 0.82f, 0.92f));

        for (int i = 0; i < AvatarColors.Length; i++)
        {
            int col = i % 3, rowi = i / 3;
            var sw = Image(skinScreen.transform, AvatarColors[i]);
            sw.sprite = knob; sw.raycastTarget = false;
            Center(sw.rectTransform, new Vector2(190, 190));
            sw.rectTransform.anchoredPosition = new Vector2(-240 + col * 240, 320 - rowi * 240);
        }
    }

    void BuildProfilePanel()
    {
        // Dimmer + centered 800x1000 panel.
        profilePanel = FullScreen("ProfileScreen", new Color(0, 0, 0, 0.6f));

        // Tapping the dimmed background (anywhere outside the card) closes the panel.
        // The full-screen dimmer Image is a raycast target; the card and its controls
        // sit on top with their own raycast targets, so taps on the card don't close it.
        var dim = profilePanel.AddComponent<Button>();
        dim.transition = Selectable.Transition.None;
        dim.onClick.AddListener(ShowHome);

        var card = Image(profilePanel.transform, new Color(0.14f, 0.16f, 0.22f, 1f));
        Center(card.rectTransform, new Vector2(800, 1000));
        // Absorb taps on the card itself so they don't bubble to the dimmer and close it.
        var block = card.gameObject.AddComponent<Button>();
        block.transition = Selectable.Transition.None;

        Label(card.transform, "PROFILE", new Vector2(0, 410), new Vector2(700, 90), 60, Color.white);

        // Big preview of the currently selected avatar.
        var preview = Image(card.transform, AvatarColors[Mathf.Clamp(SaveSystem.AvatarIndex, 0, AvatarColors.Length - 1)]);
        preview.sprite = knob; preview.raycastTarget = false;
        Center(preview.rectTransform, new Vector2(220, 220));
        preview.rectTransform.anchoredPosition = new Vector2(0, 280);

        Label(card.transform, "Choose avatar", new Vector2(0, 130), new Vector2(700, 50), 34,
            new Color(0.8f, 0.84f, 0.92f));

        // Selectable preset avatar swatches.
        for (int i = 0; i < AvatarColors.Length; i++)
        {
            int idx = i;
            int col = i % 3, rowi = i / 3;
            var btn = Button(card.transform, "", Vector2.zero, new Vector2(-200 + col * 200, 0 - rowi * 200),
                new Vector2(150, 150), AvatarColors[i], () => SelectAvatar(idx, preview), 0);
            var dot = Image(btn.transform, AvatarColors[i]);
            dot.sprite = knob; dot.raycastTarget = false;
            Center(dot.rectTransform, new Vector2(140, 140));
        }

        // Editable name field.
        Label(card.transform, "Name", new Vector2(-250, -320), new Vector2(250, 50), 34,
            new Color(0.8f, 0.84f, 0.92f), TextAnchor.MiddleLeft);
        nameField = InputBox(card.transform, SaveSystem.PlayerName, new Vector2(40, -320), new Vector2(560, 90));

        Button(card.transform, "SAVE", new Vector2(0.5f, 0), new Vector2(-150, 80), new Vector2(280, 120),
            new Color(0.42f, 0.72f, 0.42f), SaveProfile, 40);
        Button(card.transform, "CLOSE", new Vector2(0.5f, 0), new Vector2(150, 80), new Vector2(280, 120),
            new Color(0.55f, 0.40f, 0.40f), ShowHome, 40);
    }

    // ---- Navigation / actions -------------------------------------------
    void ShowHome()    { ShowOnly(null); }
    void OpenStore()   { ShowOnly(storeScreen); }
    void OpenSkin()    { ShowOnly(skinScreen); }
    void OpenProfile() { RefreshProfileFields(); ShowOnly(profilePanel); }

    // Profile button toggles the panel: open if closed, close (back to home) if open.
    void ToggleProfile()
    {
        if (profilePanel != null && profilePanel.activeSelf) ShowHome();
        else OpenProfile();
    }

    void ShowOnly(GameObject screen)
    {
        if (storeScreen) storeScreen.SetActive(screen == storeScreen);
        if (skinScreen) skinScreen.SetActive(screen == skinScreen);
        if (profilePanel) profilePanel.SetActive(screen == profilePanel);
    }

    void SelectAvatar(int idx, Image preview)
    {
        SaveSystem.AvatarIndex = idx;
        Color c = AvatarColors[Mathf.Clamp(idx, 0, AvatarColors.Length - 1)];
        if (preview) preview.color = c;
        if (profileAvatar) profileAvatar.color = c;
    }

    void SaveProfile()
    {
        if (nameField != null) SaveSystem.PlayerName = nameField.text;
        RefreshProfileFields();
        ShowHome();
    }

    void RefreshProfileFields()
    {
        if (nameField != null) nameField.text = SaveSystem.PlayerName;
        Color c = AvatarColors[Mathf.Clamp(SaveSystem.AvatarIndex, 0, AvatarColors.Length - 1)];
        if (profileAvatar) profileAvatar.color = c;
    }

    void RefreshCurrencies()
    {
        if (coinText) coinText.text = SaveSystem.Coins.ToString();
        if (diamondText) diamondText.text = SaveSystem.Diamonds.ToString();
    }

    // ---- Builders (legacy uGUI, self-contained) --------------------------
    GameObject FullScreen(string name, Color bg)
    {
        var img = Image(root, bg);
        img.gameObject.name = name;
        Stretch(img.rectTransform);
        return img.gameObject;
    }

    Image Image(Transform parent, Color color)
    {
        var go = new GameObject("Img", typeof(RectTransform));
        go.transform.SetParent(parent, false);
        var img = go.AddComponent<Image>();
        img.color = color;
        return img;
    }

    Text Label(Transform parent, string text, Vector2 pos, Vector2 size, int fontSize, Color color,
        TextAnchor align = TextAnchor.MiddleCenter)
    {
        var t = MakeText(parent, text, fontSize, color, align);
        var rt = t.rectTransform;
        rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = pos; rt.sizeDelta = size;
        return t;
    }

    Text MakeText(Transform parent, string text, int fontSize, Color color, TextAnchor align)
    {
        var go = new GameObject("Text", typeof(RectTransform));
        go.transform.SetParent(parent, false);
        var t = go.AddComponent<Text>();
        t.font = font; t.text = text; t.fontSize = fontSize; t.color = color; t.alignment = align;
        t.horizontalOverflow = HorizontalWrapMode.Overflow; t.verticalOverflow = VerticalWrapMode.Overflow;
        t.raycastTarget = false;
        var sh = go.AddComponent<Shadow>(); sh.effectColor = new Color(0, 0, 0, 0.6f); sh.effectDistance = new Vector2(2, -2);
        return t;
    }

    Button Button(Transform parent, string text, Vector2 anchor, Vector2 pos, Vector2 size, Color color,
        System.Action onClick, int fontSize = 32)
    {
        var img = Image(parent, color);
        var rt = img.rectTransform;
        rt.anchorMin = rt.anchorMax = anchor == Vector2.zero ? new Vector2(0.5f, 0.5f) : anchor;
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = pos; rt.sizeDelta = size;
        var btn = img.gameObject.AddComponent<Button>();
        btn.targetGraphic = img;
        var colors = btn.colors; colors.highlightedColor = color * 1.1f; colors.pressedColor = color * 0.85f; btn.colors = colors;
        if (onClick != null) btn.onClick.AddListener(() => onClick());
        if (fontSize > 0)
        {
            var label = Label(img.transform, text, Vector2.zero, size, fontSize, Color.white);
            Stretch(label.rectTransform); label.rectTransform.sizeDelta = Vector2.zero;
        }
        return btn;
    }

    InputField InputBox(Transform parent, string value, Vector2 pos, Vector2 size)
    {
        var bg = Image(parent, new Color(0.20f, 0.22f, 0.30f, 1f));
        var rt = bg.rectTransform;
        rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = pos; rt.sizeDelta = size;

        var field = bg.gameObject.AddComponent<InputField>();
        field.targetGraphic = bg;

        var textComp = MakeText(bg.transform, value, 40, Color.white, TextAnchor.MiddleLeft);
        textComp.supportRichText = false; textComp.raycastTarget = true;
        Stretch(textComp.rectTransform);
        textComp.rectTransform.offsetMin = new Vector2(20, 6); textComp.rectTransform.offsetMax = new Vector2(-20, -6);

        var placeholder = MakeText(bg.transform, "Enter name...", 40, new Color(1, 1, 1, 0.4f), TextAnchor.MiddleLeft);
        Stretch(placeholder.rectTransform);
        placeholder.rectTransform.offsetMin = new Vector2(20, 6); placeholder.rectTransform.offsetMax = new Vector2(-20, -6);

        field.textComponent = textComp;
        field.placeholder = placeholder;
        field.text = value;
        field.characterLimit = 16;
        field.lineType = InputField.LineType.SingleLine;
        return field;
    }

    void Center(RectTransform rt, Vector2 size)
    {
        rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = Vector2.zero; rt.sizeDelta = size;
    }

    void Anchor(RectTransform rt, Vector2 min, Vector2 max, Vector2 pos, Vector2 size)
    { rt.anchorMin = min; rt.anchorMax = max; rt.pivot = new Vector2(0.5f, 0.5f); rt.anchoredPosition = pos; rt.sizeDelta = size; }

    void Stretch(RectTransform rt)
    { rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one; rt.offsetMin = Vector2.zero; rt.offsetMax = Vector2.zero; }
}
