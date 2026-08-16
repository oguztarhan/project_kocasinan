using System.Collections;
using UnityEngine;
using UnityEngine.UI;

namespace BusJam
{
    /// <summary>
    /// Garage screen (partial of <see cref="GameUI"/>): browse vehicle skins (owned / locked), equip them, open the
    /// gold gacha chests, spend rare chest KEYS, and craft cars from shards. Built in code on the same UICanvas +
    /// kit sprites + fonts (title/num) as the rest of GameUI. Economy lives in ChestService / CraftService / SaveSystem.
    ///
    /// NOTE: skin cards use rarity-coloured placeholders, NOT live 3D thumbnails — the vehicle models are being
    /// re-done, so real thumbnails are deferred until the final models land; the preview rig drops in here later.
    /// </summary>
    public partial class GameUI
    {
        public System.Action OnReskin; // equip -> rebuild the live board so the newly-equipped MODEL is shown
        public static bool OpenGarageOnLoad; // set by the main-menu Garage button before it loads the game scene
        bool garageFromMenu;                  // opened straight from the menu -> closing the garage returns to the menu
        public bool GarageFromMenu => garageFromMenu; // BusJamGame reads this to SKIP building a level (garage-only screen)

        GameObject garagePanel, revealPanel, chestOddsPanel;
        Transform garageContent;
        ScrollRect garageScroll;              // kept so the tutorial can scroll each step's target into view

        // ---- garage tutorial (first open): highlight-until-tap on the HUD button, then a 4-step tap-through tour.
        const string GarageTutKey = "garage_tut_done";                          // PlayerPrefs flag (menu button pulse reads it too)
        Transform tutVehiclesRow, tutChestGrid, tutFreeChestRow, tutCraftHeader; // step targets (reassigned on every RefreshGarage)
        GameObject garageTutOverlay;
        Coroutine garageTutCo, garageBtnPulseCo;
        bool garageTutTapped;
        Text garageGoldT, garageShardT, revealName, revealSub, revealKeyText;
        Image revealFrame, revealGlow;
        RawImage revealPattern, revealRays;
        CanvasGroup revealChestGroup, revealItemGroup, revealKeyGroup;
        Button revealOk;
        System.Action revealThenDo; // when set, the reveal's OK runs this (bonus reward -> advance the level) instead of only closing
        Transform revealChestTf;    // the reveal's opening-chest holder (re-tinted per tier by SetRevealChestTier)
        Coroutine revealCo;

        // The ROPE/band colour — the chest BODY is the same wood on every tier (see UIKit.BuildChest).
        // The palette lives in UIKit so the menu's daily-reward chest matches the garage exactly.
        static Color ChestTint(ChestTier t) => UIKit.ChestTint(t.ToString());

        // ---- HUD entry button (top-right, under the gear) -------------------
        GameObject garageBtnGo; // the in-HUD GARAGE button — hidden by SetHudChromeVisible while a panel/garage is open
        void AddGarageButton(Transform hud)
        {
            if (hud == null) return;
            // Baked, Inspector-editable button ("BusJam ▸ Bake Garage HUD Button"): ADOPT it — sprite / colour /
            // shape / position are fully yours in the Inspector; code only wires the click, localizes the label
            // TEXT (font/size/colour stay as authored) and runs the same first-time pulse.
            var baked = InGameHud.Instance != null ? InGameHud.Instance.garageButton : null;
            if (baked != null)
            {
                baked.onClick.RemoveListener(ShowGarage); // Remove-then-Add: the baked button persists across HUD re-setups, so the listener must never stack
                baked.onClick.AddListener(ShowGarage);
                var lbl = baked.GetComponentInChildren<Text>(true);
                if (lbl != null) lbl.text = Loc.T("GARAGE");
                garageBtnGo = baked.gameObject;
                if (garageBtnPulseCo != null) { StopCoroutine(garageBtnPulseCo); garageBtnPulseCo = null; }
                if (PlayerPrefs.GetInt(GarageTutKey, 0) == 0)
                    garageBtnPulseCo = StartCoroutine(PulseGarageHudBtn((RectTransform)baked.transform));
                return;
            }
            // The kit's ORANGE button sprite at its natural colour (matches the menu buttons), bar-shaped and bigger
            // than the old purple square so it clearly reads as a button.
            var b = Btn(hud, UIKit.BtnOrange(), new Color(1f, 0.62f, 0.15f), new Vector2(1, 1), new Vector2(-105, -250), new Vector2(184, 92), ShowGarage);
            Label(b.transform, Loc.T("GARAGE"), num, Vector2.zero, new Vector2(176, 54), 28, White);
            garageBtnGo = b.gameObject;
            // First-time players: pulse the button (scale + soft halo) until they open the garage and finish its
            // tutorial. The HUD is rebuilt per level, so restart the pulse against the NEW button each time.
            if (garageBtnPulseCo != null) { StopCoroutine(garageBtnPulseCo); garageBtnPulseCo = null; }
            if (PlayerPrefs.GetInt(GarageTutKey, 0) == 0)
                garageBtnPulseCo = StartCoroutine(PulseGarageHudBtn((RectTransform)b.transform));
        }

        // Attention pulse on the HUD GARAGE button — runs until the garage tutorial has been completed (the flag is
        // written at the tutorial's end), then restores the button and removes the halo.
        IEnumerator PulseGarageHudBtn(RectTransform rt)
        {
            var halo = Img(rt, UIKit.CircleYellow(), White);
            halo.color = new Color(1f, 0.85f, 0.25f, 0f); halo.raycastTarget = false;
            Center(halo.rectTransform, new Vector2(240, 148)); // oval, hugging the bar-shaped button
            halo.transform.SetAsFirstSibling(); // over the button bg, under its label
            while (rt != null && PlayerPrefs.GetInt(GarageTutKey, 0) == 0)
            {
                float k = 0.5f + 0.5f * Mathf.Sin(Time.unscaledTime * 5f);
                rt.localScale = Vector3.one * (1f + 0.10f * k);
                halo.color = new Color(1f, 0.85f, 0.25f, 0.12f + 0.22f * k);
                yield return null;
            }
            if (rt != null) rt.localScale = Vector3.one;
            if (halo != null) Destroy(halo.gameObject);
            garageBtnPulseCo = null;
        }

        // Apply an optional Inspector override to a garage element's Image (from the InGameGarage marker). Assigning a
        // Sprite swaps the image (rendered at full colour); a Colour with alpha > 0 tints it. Leaving the Sprite empty
        // and the Colour at alpha 0 keeps the element's built-in look, so the panels are UNCHANGED until you assign one.
        void GOverride(Image img, System.Func<InGameGarage, Sprite> pickSprite, System.Func<InGameGarage, Color> pickColor)
        {
            if (img == null || garageCfg == null) return;
            var sp = pickSprite(garageCfg); if (sp != null) { img.sprite = sp; img.color = White; }
            var c  = pickColor(garageCfg);  if (c.a > 0f)    img.color = c;
        }

        // Per-tier chest CARD background override (Bronze / Silver / Gold), read from the marker.
        void ApplyChestCardOverride(Image img, ChestTier tier)
        {
            if (img == null || garageCfg == null) return;
            Sprite sp; Color c;
            switch (tier)
            {
                case ChestTier.Silver: sp = garageCfg.silverCardSprite; c = garageCfg.silverCardColor; break;
                case ChestTier.Gold:   sp = garageCfg.goldCardSprite;   c = garageCfg.goldCardColor;   break;
                default:               sp = garageCfg.bronzeCardSprite; c = garageCfg.bronzeCardColor; break;
            }
            if (sp != null) { img.sprite = sp; img.color = White; }
            if (c.a > 0f) img.color = c;
        }

        // ---- Build the (hidden) garage panel + reveal modal -----------------
        // Always build the window chrome in CODE so the garage reflects the LATEST code. (A stale baked panel used to
        // be adopted here and never picked up code changes — that was the "still shows the old version" bug.) The
        // InGameGarage marker is read only for per-element image/colour OVERRIDES (see GOverride); its baked chrome is
        // no longer adopted.
        void BuildGarage()
        {
            Button close = BuildGarageChrome();
            if (close) close.onClick.AddListener(HideGarage); // wired at runtime (button onClick refs don't serialize)
            BuildReveal();
            RefreshGarage();
            if (garagePanel) garagePanel.SetActive(false);
        }

        // Build ONLY the garage window chrome (window, title, close, gold counter, scroll area). Sets garagePanel /
        // garageContent / garageGoldT and returns the (unwired) close button. Shared by the runtime code path above
        // AND the editor baker (EditorBakeGarage) so the baked panel is identical to the code-built one.
        Button BuildGarageChrome()
        {
            garagePanel = Panel("Garage", new Color(0, 0, 0, 0.62f));

            var card = Img(garagePanel.transform, UIKit.PanelTall(), new Color(0.20f, 0.22f, 0.33f));
            GOverride(card, g => g.garageWindowSprite, g => g.garageWindowColor); // Inspector: swap the Garage window image / colour
            Center(card.rectTransform, new Vector2(980, PanelCardHeight()));     // clamped to the DEVICE height (short/16:9 phones)
            var titleT = Label(card.transform, "GARAGE", title, Vector2.zero, new Vector2(700, 120), 74, White);
            Place(titleT.rectTransform, new Vector2(0.5f, 1), new Vector2(0.5f, 1), new Vector2(0, -90), new Vector2(700, 120)); // pinned to the card TOP -> stays put at ANY card height
            var close = RedClose(card.transform, null);

            // gold + shard counters (shards are earned from duplicate cars and spent in the CRAFT section below)
            var goldChip = Img(card.transform, UIKit.CoinBar(), Dark); goldChip.raycastTarget = false;
            Place(goldChip.rectTransform, new Vector2(0.5f, 1), new Vector2(0.5f, 1), new Vector2(-175, -205), new Vector2(300, 88));
            var gci = Img(goldChip.transform, UIKit.Coin(), Gold); gci.raycastTarget = false;
            Place(gci.rectTransform, new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(40, 0), new Vector2(60, 60));
            garageGoldT = Label(goldChip.transform, "0", num, new Vector2(34, 0), new Vector2(190, 56), 40, White);

            var shardChip = Img(card.transform, UIKit.CoinBar(), Dark); shardChip.raycastTarget = false;
            Place(shardChip.rectTransform, new Vector2(0.5f, 1), new Vector2(0.5f, 1), new Vector2(175, -205), new Vector2(300, 88));
            var sci = Img(shardChip.transform, UIKit.Gem(), new Color(0.42f, 0.82f, 1f)); sci.raycastTarget = false;
            Place(sci.rectTransform, new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(42, 0), new Vector2(54, 54));
            garageShardT = Label(shardChip.transform, "0", num, new Vector2(34, 0), new Vector2(190, 56), 40, new Color(0.72f, 0.92f, 1f));

            // scroll view (chests + skins grid + craft rows) — same recipe as BuildShop.
            // VERTICALLY STRETCHED between a fixed top pad (below the title + counters) and bottom pad instead of a
            // fixed 1080 height, so it can NEVER poke out of the window on a shorter card (that was the
            // "scroll view goes outside the UI" bug on 16:9 devices).
            var svGo = new GameObject("ScrollView", typeof(RectTransform));
            svGo.transform.SetParent(card.transform, false);
            var svRt = svGo.GetComponent<RectTransform>();
            svRt.anchorMin = new Vector2(0.5f, 0f); svRt.anchorMax = new Vector2(0.5f, 1f); svRt.pivot = new Vector2(0.5f, 0.5f);
            // Pads MEASURED against the window sprite (Sprite_2_4, 965x780): the corner scallops cut ~114px deep and
            // extend 76px (top) / 96px (bottom) native = up to ~152/192px at full card height. Bottom pad 210 keeps
            // the lowest row clear of the bottom scallops; 820 wide clears the 28px straight-edge inset with margin.
            svRt.sizeDelta = new Vector2(820, -(350f + 210f));      // height = cardH - topPad(350) - bottomPad(210)
            svRt.anchoredPosition = new Vector2(0, (210f - 350f) * 0.5f); // centre shifted so the pads land 350/210
            var scroll = svGo.AddComponent<ScrollRect>();
            scroll.horizontal = false; scroll.vertical = true;
            scroll.movementType = ScrollRect.MovementType.Elastic; scroll.scrollSensitivity = 28;

            var vpGo = new GameObject("Viewport", typeof(RectTransform));
            vpGo.transform.SetParent(svGo.transform, false);
            var vpRt = vpGo.GetComponent<RectTransform>();
            vpRt.anchorMin = Vector2.zero; vpRt.anchorMax = Vector2.one; vpRt.offsetMin = Vector2.zero; vpRt.offsetMax = Vector2.zero;
            var vpImg = vpGo.AddComponent<Image>(); vpImg.color = new Color(1, 1, 1, 0.01f);
            vpGo.AddComponent<RectMask2D>();

            var ctGo = new GameObject("Content", typeof(RectTransform));
            ctGo.transform.SetParent(vpGo.transform, false);
            var ctRt = ctGo.GetComponent<RectTransform>();
            ctRt.anchorMin = new Vector2(0, 1); ctRt.anchorMax = new Vector2(1, 1); ctRt.pivot = new Vector2(0.5f, 1);
            ctRt.anchoredPosition = Vector2.zero; ctRt.sizeDelta = Vector2.zero;
            var vlg = ctGo.AddComponent<VerticalLayoutGroup>();
            vlg.spacing = 22; vlg.padding = new RectOffset(12, 12, 12, 12);
            vlg.childAlignment = TextAnchor.UpperCenter;
            vlg.childControlWidth = true; vlg.childControlHeight = true;
            vlg.childForceExpandWidth = true; vlg.childForceExpandHeight = false;
            var fit = ctGo.AddComponent<ContentSizeFitter>();
            fit.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            fit.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
            scroll.viewport = vpRt; scroll.content = ctRt;
            garageContent = ctGo.transform;
            garageScroll = scroll; // the tutorial scrolls each step's target into view with this
            return close;
        }

#if UNITY_EDITOR
        // Editor baker entry (BusJam ▸ Bake Garage Panels): build the garage chrome under `canvas` and hand back its
        // refs. No runtime wiring / no dynamic content — those happen at play time when GameUI adopts the marker.
        public (GameObject panel, Transform content, Text gold, Button close) EditorBakeGarage(Transform canvas)
        {
            title = UIKit.Title(); num = UIKit.Num(); root = canvas;
            var close = BuildGarageChrome();
            return (garagePanel, garageContent, garageGoldT, close);
        }
#endif

        // The GlobalFontApplier multiplies EVERY Text by GameFont.UiScale on its passes (a level rebuild — which an
        // equip triggers via OnReskin/RetryLevel — re-runs them), which SHRANK the garage/wardrobe text after equipping.
        // Our cards are authored at the final on-screen sizes, so pre-seed each Text's FontScaleTag to CANCEL that
        // multiply: baseSize = size/scale, so the applier's target (baseSize*scale) lands back exactly on the size we
        // built. Call right after (re)building content, before any applier pass sees the new text.
        static void PreserveAuthoredFontSizes(Transform root)
        {
            if (root == null) return;
            float scale = GameFont.UiScale; if (scale <= 0f) scale = 1f;
            foreach (var t in root.GetComponentsInChildren<Text>(true))
            {
                var tag = t.GetComponent<FontScaleTag>(); if (tag == null) tag = t.gameObject.AddComponent<FontScaleTag>();
                tag.baseSize = t.fontSize / scale; // applier: fontSize = baseSize*scale = the size we authored (unchanged)
                tag.captured = true;
            }
        }

        // ---- (Re)populate the scroll content + counters ---------------------
        void RefreshGarage()
        {
            SetCoins(SaveSystem.Coins);
            if (garageGoldT)  garageGoldT.text  = SaveSystem.Coins.ToString();
            if (garageShardT) garageShardT.text = SaveSystem.Shards.ToString();
            if (garageContent == null) return;

            for (int i = garageContent.childCount - 1; i >= 0; i--)
            {
                var ch = garageContent.GetChild(i);
                ch.SetParent(null, false);
                Destroy(ch.gameObject);
            }

            // 0) vehicle wardrobe ("dolap") entry — opens the 3-section Cars/Minivans/Buses panel
            AddVehiclesEntry(garageContent);

            // 1) chests — stay in the scroll list (a free-positioned chest box overlaps the scroll). Size / spacing /
            //    columns are Inspector-tunable on InGameGarage.
            var gg = garageCfg; // Inspector config (read even when the marker's canvas is inactive)
            var chestsHeader = SectionLabel(garageContent, Loc.T("CHESTS"),
                garageCfg != null ? garageCfg.chestsHeaderSprite : null,
                garageCfg != null ? garageCfg.chestsHeaderColor : default,
                garageCfg != null ? garageCfg.chestsHeaderHeight : 74f,
                garageCfg != null ? garageCfg.chestsHeaderFontSize : 40);
            // STORE-POLICY: loot-box odds must be disclosed BEFORE purchase (Apple 3.1.1 / Play Monetization).
            // A round ⓘ on the CHESTS header — directly above every buyable chest — opens the DROP RATES popup.
            var oddsInfo = Btn(chestsHeader, UIKit.CircleYellow(), new Color(1f, 0.85f, 0.25f), new Vector2(1, 0.5f), new Vector2(-52, 0), new Vector2(56, 56), ShowChestOdds);
            Label(oddsInfo.transform, "i", title, new Vector2(0, 2), new Vector2(50, 44), 36, new Color(0.35f, 0.22f, 0.05f));
            var chestGrid = GridRow(garageContent, gg != null ? gg.chestCellSize : new Vector2(275, 275), gg != null ? Mathf.Max(1, gg.chestColumns) : 3);
            if (gg != null) { var glg = chestGrid.GetComponent<GridLayoutGroup>(); if (glg) { glg.spacing = gg.chestSpacing; ClampGridToWidth(glg, 796f); } } // re-clamp with the Inspector spacing
            tutChestGrid = chestGrid; // tutorial step 2 target
            ChestCard(chestGrid, ChestTier.Bronze, Loc.T("BRONZE"));
            ChestCard(chestGrid, ChestTier.Silver, Loc.T("SILVER"));
            ChestCard(chestGrid, ChestTier.Gold,   Loc.T("GOLD"));
            LegendaryChestRow(garageContent);
            FreeChestRow(garageContent);

            // (Vehicle skins removed — the vehicle PACKAGES in the wardrobe, opened from the entry at the top, replace them.)

            // 2) CRAFT — spend shards (duplicate cars melt into shards when a chest is opened) on a GUARANTEED new car
            // of a chosen tier (never a dupe). CraftHeader shows the live shard balance — that is the visible shard
            // counter on the baked garage panel, whose top chrome bakes a gold counter only.
            CraftHeader(garageContent);
            CraftRow(garageContent, 0); // Common
            CraftRow(garageContent, 1); // Uncommon
            CraftRow(garageContent, 2); // Epic
            CraftRow(garageContent, 3); // Legendary

            PreserveAuthoredFontSizes(garageContent); // keep these authored sizes through the global font applier
        }

        // A section header row (LayoutElement gives the vertical group a fixed height). Optional overrides (used by the
        // CHESTS header) let the Inspector make it prominent: a background image, a tint, a taller bar, bigger text.
        // Defaults reproduce the original faint header, so the wardrobe's CARS/MINIVANS/BUSES headers are unchanged.
        Transform SectionLabel(Transform parent, string text, Sprite bgSprite = null, Color bgColor = default, float height = 74f, int fontSize = 44)
        {
            var go = bgSprite != null ? Img(parent, bgSprite, White) : Img(parent, null, new Color(1, 1, 1, 0.06f));
            if (bgColor.a > 0f) go.color = bgColor;
            go.raycastTarget = false;
            float h = height > 0f ? height : 74f;
            var le = go.gameObject.AddComponent<LayoutElement>(); le.preferredHeight = h; le.minHeight = h;
            Label(go.transform, text, num, Vector2.zero, new Vector2(860, 60), fontSize > 0 ? fontSize : 40, new Color(0.88f, 0.91f, 0.97f));
            return go.transform; // so callers can attach extras (e.g. the CHESTS drop-rates ⓘ button)
        }

        // Garage/wardrobe window height, clamped to the DEVICE so the card (and everything inside it) always fits:
        // the canvas is width-matched at 1080, so the visible height in canvas units is 1080 * H/W. On tall phones
        // this returns the full 1560; on short (16:9) screens the card shrinks instead of spilling off-screen.
        static float PanelCardHeight()
        {
            float uiH = 1080f * Screen.height / Mathf.Max(1, Screen.width);
            return Mathf.Min(1560f, uiH - 60f);
        }

        // A sub-object with a fixed-column grid; returns its transform so cards parent into it.
        Transform GridRow(Transform parent, Vector2 cell, int cols)
        {
            var go = new GameObject("Grid", typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var gl = go.AddComponent<GridLayoutGroup>();
            gl.cellSize = cell; gl.spacing = new Vector2(15, 18);
            gl.childAlignment = TextAnchor.UpperCenter;
            gl.constraint = GridLayoutGroup.Constraint.FixedColumnCount; gl.constraintCount = cols;
            ClampGridToWidth(gl, 796f); // scroll content width (820 viewport - 2x12 padding) — cells may never overflow it
            return go.transform;
        }

        // Shrink a grid's cells (proportionally) until a full row fits `availW` — guards against Inspector-tuned
        // chest cell sizes pushing the cards out of the scroll view sideways.
        static void ClampGridToWidth(GridLayoutGroup gl, float availW)
        {
            if (gl == null || gl.constraintCount < 1) return;
            float maxW = (availW - gl.spacing.x * (gl.constraintCount - 1)) / gl.constraintCount;
            if (gl.cellSize.x > maxW)
                gl.cellSize = new Vector2(maxW, gl.cellSize.y * (maxW / gl.cellSize.x));
        }

        // A positioned empty holder (so code-built art can be centred + sized inside a card).
        RectTransform Holder(Transform parent, Vector2 pos, Vector2 size)
        {
            var go = new GameObject("Holder", typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var rt = go.GetComponent<RectTransform>();
            Place(rt, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), pos, size);
            return rt;
        }

        // The chest art now lives in UIKit.BuildChest so the menu's daily-reward card can draw the
        // SAME chest as the garage cards and the reveal popup.
        void BuildChest(Transform parent, Color tint, float w) => UIKit.BuildChest(parent, tint, w);

        // One gold chest card: chest art + gold-cost OPEN button + (if you hold keys) a key badge to open free.
        void ChestCard(Transform parent, ChestTier tier, string name)
        {
            Color tint = ChestTint(tier);
            var card = Img(parent, UIKit.ShopIconBgA(), White); card.color = new Color(0.22f, 0.24f, 0.31f); // SAME neutral dark card on every tier (only the ropes differ) — no more vibrant-silver card
            ApplyChestCardOverride(card, tier); // Inspector: swap the Bronze/Silver/Gold card background image / colour
            Label(card.transform, name, num, new Vector2(0, 98), new Vector2(255, 48), 34, White);
            BuildChest(Holder(card.transform, new Vector2(0, 16), new Vector2(150, 110)), tint, 110);
            Vector2 buyOff = new Vector2(0, 16), buySize = new Vector2(250, 78);
            if (garageCfg != null && garageCfg.overrideChestButtons) { buyOff = garageCfg.chestButtonOffset; buySize = garageCfg.chestButtonSize; } // Inspector: size + position
            var buy = Btn(card.transform, UIKit.PriceBtnA(), new Color(0.30f, 0.72f, 0.36f), new Vector2(0.5f, 0), buyOff, buySize, () => OpenChest(tier));
            var bc = Img(buy.transform, UIKit.Coin(), Gold); bc.raycastTarget = false;
            Place(bc.rectTransform, new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(30, 0), new Vector2(46, 46));
            Label(buy.transform, ChestService.Cost(tier).ToString(), num, new Vector2(26, 0), new Vector2(250, 52), 38, White);

            int keys = SaveSystem.Keys(tier.ToString());
            if (keys > 0) KeyBadge(card.transform, tier, keys);
        }

        // Premium key-only Legendary chest (full-width row); openable only with a Legendary key.
        void LegendaryChestRow(Transform parent)
        {
            int keys = SaveSystem.Keys(ChestTier.Legendary.ToString());
            var row = Img(parent, UIKit.ShopBoxA(), White); // natural ORANGE kit bar (matches the game's orange UI theme)
            GOverride(row, g => g.legendaryChestSprite, g => g.legendaryChestColor); // Inspector: swap the key-only LEGENDARY chest image / colour
            var le = row.gameObject.AddComponent<LayoutElement>(); le.preferredHeight = 156; le.minHeight = 156;
            BuildChest(Holder(row.transform, new Vector2(-330, 0), new Vector2(150, 120)), ChestTint(ChestTier.Legendary), 116);
            Label(row.transform, Loc.T("LEGENDARY"), title, new Vector2(-40, 26), new Vector2(440, 54), 40, White, TextAnchor.MiddleLeft);
            Label(row.transform, Loc.T("key only"), num, new Vector2(-40, -24), new Vector2(440, 36), 26, new Color(0.92f, 0.86f, 1f), TextAnchor.MiddleLeft);
            if (keys > 0)
            {
                var open = Btn(row.transform, UIKit.PriceBtnA(), new Color(0.30f, 0.72f, 0.36f), new Vector2(1, 0.5f), new Vector2(-150, 0), new Vector2(300, 104), () => OpenChestWithKey(ChestTier.Legendary));
                var ki = Img(open.transform, UIKit.Gem(), new Color(1f, 0.95f, 0.5f)); ki.raycastTarget = false;
                Place(ki.rectTransform, new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(28, 0), new Vector2(46, 46));
                Label(open.transform, Loc.T("OPEN") + " " + keys, title, new Vector2(26, 0), new Vector2(300, 64), 36, White);
            }
            else
            {
                Label(row.transform, Loc.T("FIND A KEY"), num, new Vector2(250, 0), new Vector2(320, 60), 34, new Color(0.86f, 0.80f, 1f));
            }
        }

        // A round key badge on a chest card -> open that chest free with a key.
        void KeyBadge(Transform card, ChestTier tier, int count)
        {
            var b = Btn(card, UIKit.CircleYellow(), new Color(0.95f, 0.80f, 0.20f), new Vector2(1, 1), new Vector2(-6, -6), new Vector2(80, 80), () => OpenChestWithKey(tier));
            var ki = Img(b.transform, UIKit.Gem(), new Color(1f, 0.96f, 0.55f)); ki.raycastTarget = false;
            Place(ki.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0, 8), new Vector2(40, 40));
            Label(b.transform, count.ToString(), num, new Vector2(0, -22), new Vector2(80, 30), 22, White);
        }

        // Full-width free-chest row: OPEN when ready, else the remaining cooldown.
        void FreeChestRow(Transform parent)
        {
            var row = Img(parent, UIKit.ShopBoxA(), new Color(0.30f, 0.62f, 0.40f));
            tutFreeChestRow = row.transform; // tutorial step 3 target
            GOverride(row, g => g.freeChestSprite, g => g.freeChestColor); // Inspector: swap the FREE CHEST row image / colour
            var le = row.gameObject.AddComponent<LayoutElement>(); le.preferredHeight = 130; le.minHeight = 130;
            Label(row.transform, Loc.T("FREE CHEST"), title, new Vector2(-140, 0), new Vector2(420, 70), 44, White, TextAnchor.MiddleLeft);
            if (ChestService.FreeChestReady())
            {
                var open = Btn(row.transform, UIKit.PriceBtnA(), new Color(0.30f, 0.72f, 0.36f), new Vector2(1, 0.5f), new Vector2(-180, 0), new Vector2(300, 96), OpenFreeChest);
                Label(open.transform, Loc.T("OPEN"), title, Vector2.zero, new Vector2(300, 64), 42, White);
            }
            else
            {
                long s = ChestService.FreeChestSecondsLeft();
                string t = (s / 3600) + "h " + ((s % 3600) / 60) + "m";
                Label(row.transform, t, num, new Vector2(220, 0), new Vector2(360, 60), 42, new Color(0.92f, 0.96f, 1f));
            }
        }

        // ---- DROP RATES popup (store-policy loot-box odds disclosure) -------
        // Renders ChestService.CarTierOdds/PityCount LIVE, so the disclosure always matches the real roll tables.
        void ShowChestOdds()
        {
            if (chestOddsPanel == null) BuildChestOdds();
            chestOddsPanel.SetActive(true);
        }

        void BuildChestOdds()
        {
            chestOddsPanel = Panel("ChestOdds", Dim);
            var cv = chestOddsPanel.AddComponent<Canvas>(); cv.overrideSorting = true; cv.sortingOrder = 88; // above garage (30) + wardrobe (80), below nothing that matters
            chestOddsPanel.AddComponent<GraphicRaycaster>();
            var card = Img(chestOddsPanel.transform, UIKit.PanelTall(), new Color(0.22f, 0.24f, 0.36f));
            Center(card.rectTransform, new Vector2(880, 1100));
            Label(card.transform, Loc.T("DROP RATES"), title, new Vector2(0, 470), new Vector2(700, 80), 50, White);

            string[] tierKeys = { "COMMON", "UNCOMMON", "EPIC", "LEGENDARY" };
            (ChestTier t, string key)[] chests =
                { (ChestTier.Bronze, "BRONZE"), (ChestTier.Silver, "SILVER"), (ChestTier.Gold, "GOLD"), (ChestTier.Legendary, "LEGENDARY") };
            float y = 350f;
            foreach (var c in chests)
            {
                Label(card.transform, Loc.T(c.key), title, new Vector2(0, y), new Vector2(780, 50), 36, ChestTint(c.t));
                var odds = ChestService.CarTierOdds(c.t);
                var sb = new System.Text.StringBuilder();
                for (int i = 0; i < odds.Length; i++)
                {
                    if (odds[i] <= 0f) continue;                       // hide impossible outcomes (e.g. Legendary from Bronze)
                    if (sb.Length > 0) sb.Append("    ");
                    sb.Append(Loc.T(tierKeys[i])).Append(' ').Append((odds[i] * 100f).ToString("0.#")).Append('%');
                }
                Label(card.transform, sb.ToString(), num, new Vector2(0, y - 50), new Vector2(800, 40), 26, new Color(0.88f, 0.91f, 0.97f));
                Label(card.transform, string.Format(Loc.T("EPIC or better guaranteed every {0} opens."), ChestService.PityCount(c.t)),
                      num, new Vector2(0, y - 92), new Vector2(800, 34), 22, new Color(0.72f, 0.76f, 0.85f));
                y -= 200f;
            }

            RedClose(card.transform, () => chestOddsPanel.SetActive(false));
            chestOddsPanel.SetActive(false);
        }

        // (SkinCard removed — vehicle skins are deprecated; the garage shows car packages + chests + craft.)

        // CRAFT section header: the title + the player's LIVE shard balance on the right. On the baked garage panel
        // the top chrome bakes a gold counter only, so THIS is the visible shard counter; it is rebuilt on every
        // RefreshGarage so it always reads the current balance.
        void CraftHeader(Transform parent)
        {
            Sprite hSp = garageCfg != null ? garageCfg.craftHeaderSprite : null; // Inspector: banner image behind CRAFT
            var go = hSp != null ? Img(parent, hSp, White) : Img(parent, null, new Color(0.42f, 0.82f, 1f, 0.10f));
            if (garageCfg != null && garageCfg.craftHeaderColor.a > 0f) go.color = garageCfg.craftHeaderColor;
            go.raycastTarget = false;
            float h = garageCfg != null && garageCfg.craftHeaderHeight > 0f ? garageCfg.craftHeaderHeight : 72f;
            var le = go.gameObject.AddComponent<LayoutElement>(); le.preferredHeight = h; le.minHeight = h;
            int fs = garageCfg != null && garageCfg.craftHeaderFontSize > 0 ? garageCfg.craftHeaderFontSize : 40;
            tutCraftHeader = go.transform; // tutorial step 4 target
            // "CRAFT" pinned to the LEFT edge, shard balance pinned to the RIGHT edge -> stays inside ANY panel width.
            AnchorLeft(Label(go.transform, Loc.T("CRAFT"), title, Vector2.zero, new Vector2(300, 58), fs, White, TextAnchor.MiddleLeft).rectTransform, 36, 0);
            var gem = Img(go.transform, UIKit.Gem(), new Color(0.42f, 0.82f, 1f)); gem.raycastTarget = false;
            Place(gem.rectTransform, new Vector2(1, 0.5f), new Vector2(1, 0.5f), new Vector2(-210, 0), new Vector2(46, 46));
            AnchorRight(Label(go.transform, SaveSystem.Shards.ToString(), num, Vector2.zero, new Vector2(150, 52), 38, new Color(0.78f, 0.93f, 1f), TextAnchor.MiddleRight).rectTransform, 28, 0);
        }

        // One craft row: car TIER + remaining-locked count + a shard-cost CRAFT button (greyed when unaffordable or the
        // tier is fully owned). Crafting grants a GUARANTEED new car of that tier — never a duplicate.
        void CraftRow(Transform parent, int tier)
        {
            int locked = CraftService.Craftable(tier).Count;
            bool can = CraftService.CanCraft(tier);
            Color rc = TierColor(tier);
            var row = Img(parent, UIKit.ShopBoxA(), White); // natural ORANGE kit bar on every tier (orange UI theme; the tier reads from its coloured name)
            GOverride(row, g => g.CraftSprite(tier), g => g.CraftColor(tier)); // Inspector: per-tier CRAFT row image / colour (Common/Uncommon/Epic/Legendary)
            var le = row.gameObject.AddComponent<LayoutElement>(); le.preferredHeight = 120; le.minHeight = 120;
            // tier name + "N left" pinned LEFT, CRAFT button pinned RIGHT -> contained at any panel width (no overflow).
            AnchorLeft(Label(row.transform, Loc.T(TierName(tier)), num, Vector2.zero, new Vector2(320, 46), 34, Color.Lerp(rc, White, 0.35f), TextAnchor.MiddleLeft).rectTransform, 40, 24);
            AnchorLeft(Label(row.transform, string.Format(Loc.T("{0} left"), locked), num, Vector2.zero, new Vector2(320, 34), 26, new Color(0.86f, 0.88f, 0.94f), TextAnchor.MiddleLeft).rectTransform, 40, -24);
            Vector2 crOff = new Vector2(-160, 0), crSize = new Vector2(280, 92);
            if (garageCfg != null && garageCfg.overrideCraftButtons) { crOff = garageCfg.craftButtonOffset; crSize = garageCfg.craftButtonSize; } // Inspector: size + position
            var craft = Btn(row.transform, UIKit.PriceBtnA(), can ? new Color(0.30f, 0.72f, 0.36f) : new Color(0.45f, 0.45f, 0.50f),
                            new Vector2(1, 0.5f), crOff, crSize, () => { if (can) CraftTier(tier); });
            var sc = Img(craft.transform, UIKit.Gem(), new Color(0.42f, 0.82f, 1f)); sc.raycastTarget = false;
            Place(sc.rectTransform, new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(30, 0), new Vector2(44, 44));
            Label(craft.transform, CraftService.Cost(tier).ToString(), num, new Vector2(26, 0), new Vector2(280, 50), 34, White);
        }

        // Pin a rect to the LEFT / RIGHT edge of its parent so craft rows fit ANY panel width (baked or code-built),
        // never overflowing the sides. x = margin from that edge.
        static void AnchorLeft(RectTransform rt, float x, float y)  { rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0f, 0.5f); rt.anchoredPosition = new Vector2(x, y); }
        static void AnchorRight(RectTransform rt, float x, float y) { rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(1f, 0.5f); rt.anchoredPosition = new Vector2(-x, y); }

        // ---- actions --------------------------------------------------------
        void OpenChest(ChestTier tier)        { var res = ChestService.BuyAndOpen(tier);  if (res == null) return; ShowChestResult(res.Value); }
        void OpenChestWithKey(ChestTier tier) { var res = ChestService.OpenWithKey(tier);  if (res == null) return; ShowChestResult(res.Value); }
        void OpenFreeChest()                  { var res = ChestService.OpenFree();          if (res == null) return; ShowChestResult(res.Value); }
        void ShowChestResult(ChestResult r)
        {
            if (r.car == null) return;
            ShowRevealCar(r.car, r.wasDupe ? string.Format(Loc.T("DUPLICATE  +{0} shards"), r.shardsGained) : Loc.T("NEW!"), r.keyDropped, r.keyTier);
            RefreshGarage();
        }
        void CraftTier(int tier)
        {
            var car = CraftService.Craft(tier);
            if (car == null) return;
            ShowRevealCar(car, Loc.T("CRAFTED!"), false, ChestTier.Bronze);
            RefreshGarage();
        }
        // ---- reveal modal + opening animation -------------------------------
        void BuildReveal()
        {
            revealPanel = Panel("Reveal", Dim);
            var cv = revealPanel.AddComponent<Canvas>(); cv.overrideSorting = true; cv.sortingOrder = 85;
            revealPanel.AddComponent<GraphicRaycaster>();
            var card = Img(revealPanel.transform, UIKit.PanelTall(), new Color(0.22f, 0.24f, 0.36f));
            var gr = InGameGarage.Instance;
            if (gr != null && gr.revealCard != null) { Center(card.rectTransform, gr.revealCard.sizeDelta); card.rectTransform.anchoredPosition = gr.revealCard.anchoredPosition; }
            else { Center(card.rectTransform, gr != null ? gr.revealSize : new Vector2(820, 980)); if (gr != null) card.rectTransform.anchoredPosition = gr.revealPos; }

            // rarity glow (a circle that flashes out during the burst)
            revealGlow = Img(card.transform, UIKit.CircleYellow(), White); revealGlow.raycastTarget = false;
            Place(revealGlow.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0, 90), new Vector2(330, 330));
            revealGlow.color = new Color(1, 1, 1, 0);

            // rarity ray-burst behind the prize (spins; faded in + scaled by rarity in the anim)
            var raysGo = new GameObject("Rays", typeof(RectTransform));
            raysGo.transform.SetParent(card.transform, false);
            Place(raysGo.GetComponent<RectTransform>(), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0, 95), new Vector2(640, 640));
            revealRays = raysGo.AddComponent<RawImage>();
            revealRays.texture = SkinTextureFactory.Rays();
            revealRays.raycastTarget = false;
            revealRays.color = new Color(1, 1, 1, 0);
            raysGo.AddComponent<Spinner>().Set(Vector3.forward, 22f);

            // animated chest group (body + lid + lock)
            var chestGo = new GameObject("Chest", typeof(RectTransform));
            chestGo.transform.SetParent(card.transform, false);
            var chestRt = chestGo.GetComponent<RectTransform>();
            Place(chestRt, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0, 70), new Vector2(360, 320));
            revealChestGroup = chestGo.AddComponent<CanvasGroup>();
            BuildChest(chestGo.transform, new Color(0.98f, 0.80f, 0.28f), 240); // generic gold-roped chest for the opening animation
            revealChestTf = chestGo.transform;                                  // bonus rewards re-tint this to the won tier (SetRevealChestTier)

            // item group (frame + name + sub) — hidden until the chest pops open
            var itemGo = new GameObject("Item", typeof(RectTransform));
            itemGo.transform.SetParent(card.transform, false);
            Stretch(itemGo.GetComponent<RectTransform>());
            revealItemGroup = itemGo.AddComponent<CanvasGroup>(); revealItemGroup.alpha = 0f;
            Label(itemGo.transform, Loc.T("YOU GOT"), num, new Vector2(0, 330), new Vector2(600, 60), 34, new Color(0.85f, 0.90f, 1f));
            revealFrame = Img(itemGo.transform, null, White); revealFrame.raycastTarget = false;
            Place(revealFrame.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0, 90), new Vector2(300, 240));
            // dark tile + the won skin's pattern preview inside the rarity frame
            var revTile = Img(revealFrame.transform, null, new Color(0.13f, 0.14f, 0.19f)); revTile.raycastTarget = false;
            Center(revTile.rectTransform, new Vector2(278, 218));
            var revPatGo = new GameObject("Pat", typeof(RectTransform)); revPatGo.transform.SetParent(revTile.transform, false);
            revealPattern = revPatGo.AddComponent<RawImage>(); revealPattern.raycastTarget = false;
            var revPrt = revealPattern.rectTransform; revPrt.anchorMin = Vector2.zero; revPrt.anchorMax = Vector2.one; revPrt.offsetMin = Vector2.zero; revPrt.offsetMax = Vector2.zero;
            revealName = Label(itemGo.transform, "", title, new Vector2(0, -120), new Vector2(700, 90), 52, White);
            revealSub  = Label(itemGo.transform, "", num, new Vector2(0, -210), new Vector2(700, 70), 36, Gold);

            // key-drop group (only shown when a key dropped)
            var keyGo = new GameObject("KeyDrop", typeof(RectTransform));
            keyGo.transform.SetParent(card.transform, false);
            Place(keyGo.GetComponent<RectTransform>(), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0, -300), new Vector2(700, 80));
            revealKeyGroup = keyGo.AddComponent<CanvasGroup>(); revealKeyGroup.alpha = 0f;
            var ki = Img(keyGo.transform, UIKit.Gem(), new Color(1f, 0.85f, 0.3f)); ki.raycastTarget = false;
            Place(ki.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(-160, 0), new Vector2(64, 64));
            revealKeyText = Label(keyGo.transform, "", title, new Vector2(40, 0), new Vector2(540, 70), 38, new Color(1f, 0.90f, 0.40f));

            revealOk = Btn(card.transform, UIKit.PriceBtnA(), new Color(0.30f, 0.72f, 0.36f), new Vector2(0.5f, 0), new Vector2(0, 60), new Vector2(380, 120), () => { revealPanel.SetActive(false); var cb = revealThenDo; revealThenDo = null; cb?.Invoke(); });
            Label(revealOk.transform, Loc.T("OK"), title, Vector2.zero, new Vector2(380, 80), 46, White);

            revealPanel.SetActive(false);
        }

        // Re-tint the reveal's opening chest to a tier — bonus rewards call this so the chest that "opens" matches the
        // tier the player just won, rebuilding the chest art under the same animated group.
        void SetRevealChestTier(ChestTier tier)
        {
            if (revealChestTf == null) return;
            for (int i = revealChestTf.childCount - 1; i >= 0; i--) { var ch = revealChestTf.GetChild(i); ch.SetParent(null, false); Destroy(ch.gameObject); }
            BuildChest(revealChestTf, ChestTint(tier), 240);
        }

        // Car reveal (chest path) — the won car's 3D thumbnail + name in its rarity-tier colour.
        void ShowRevealCar(VehicleSetCatalog.VehicleSet car, string sub, bool keyDropped, ChestTier keyTier)
        {
            if (car == null || revealPanel == null) return;
            revealPanel.SetActive(true);
            if (revealCo != null) StopCoroutine(revealCo);
            // same per-type framing + yaw as the wardrobe cards (sedans flipped 180 to match the vans/buses)
            float fill = car.type == VehicleType.Car ? 0.6f : car.type == VehicleType.Minivan ? 0.72f : 0.85f;
            float yaw  = car.type == VehicleType.Car ? 215f : 35f;
            Texture preview = VehiclePreview.Get(car.PrefabFor(car.type), yaw, car.type != VehicleType.Car, fill);
            revealCo = StartCoroutine(RevealAnim(TierColor(car.rarity), Mathf.Clamp01(car.rarity / 3f), preview, car.displayName, sub, keyDropped, keyTier, car.rarity));
        }

        IEnumerator RevealAnim(Color rc, float inten, Texture preview, string nm, string sub, bool keyDropped, ChestTier keyTier, int rarity)
        {
            if (revealFrame) revealFrame.color = rc;
            if (revealName)  revealName.text = nm;
            if (revealSub)   revealSub.text  = sub;
            if (revealPattern) revealPattern.texture = preview;

            revealItemGroup.alpha = 0f; revealItemGroup.transform.localScale = Vector3.one * 0.6f;
            revealKeyGroup.alpha = 0f;
            revealGlow.color = new Color(rc.r, rc.g, rc.b, 0f); revealGlow.transform.localScale = Vector3.one * 0.2f;
            if (revealRays) { revealRays.color = new Color(rc.r, rc.g, rc.b, 0f); revealRays.transform.localScale = Vector3.one * (0.7f + 0.5f * inten); }
            revealChestGroup.gameObject.SetActive(true); revealChestGroup.alpha = 1f; revealChestGroup.transform.localScale = Vector3.one;
            if (revealOk) revealOk.gameObject.SetActive(false);

            // 1) chest shakes — longer + harder for higher rarity
            float t = 0f, shakeDur = 0.6f + 0.5f * inten;
            while (t < shakeDur)
            {
                t += Time.unscaledDeltaTime;
                float amp = Mathf.Clamp01(t / (shakeDur * 0.6f));
                revealChestGroup.transform.localRotation = Quaternion.Euler(0, 0, Mathf.Sin(t * 38f) * (8f + 6f * inten) * amp);
                revealChestGroup.transform.localScale = Vector3.one * (1f + (0.05f + 0.04f * inten) * Mathf.Sin(t * 22f));
                yield return null;
            }
            revealChestGroup.transform.localRotation = Quaternion.identity;

            // 2) burst: rarity glow + ray-burst flash out (bigger/brighter the rarer), chest pops + fades
            Sfx.Ensure().Chest(rarity); // rarity-specific chest fanfare hits the instant the chest pops open
            t = 0f;
            float glowMax = 2.4f + 2.0f * inten, raysA = 0.20f + 0.55f * inten;
            while (t < 0.28f)
            {
                t += Time.unscaledDeltaTime; float k = t / 0.28f;
                revealGlow.transform.localScale = Vector3.one * Mathf.Lerp(0.2f, glowMax, k);
                revealGlow.color = new Color(rc.r, rc.g, rc.b, (0.5f + 0.3f * inten) * (1f - k));
                if (revealRays) { revealRays.color = new Color(rc.r, rc.g, rc.b, raysA * k); revealRays.transform.localScale = Vector3.one * (0.7f + 0.5f * inten) * Mathf.Lerp(0.6f, 1f, k); }
                revealChestGroup.transform.localScale = Vector3.one * Mathf.Lerp(1f, 1.6f, k);
                revealChestGroup.alpha = 1f - k;
                yield return null;
            }
            revealChestGroup.gameObject.SetActive(false);

            // 3) item pops in with an overshoot
            t = 0f;
            while (t < 0.35f)
            {
                t += Time.unscaledDeltaTime; float k = t / 0.35f;
                revealItemGroup.transform.localScale = Vector3.one * Mathf.LerpUnclamped(0.6f, 1f, EaseOutBack(k));
                revealItemGroup.alpha = Mathf.Clamp01(k * 1.5f);
                yield return null;
            }
            revealItemGroup.transform.localScale = Vector3.one; revealItemGroup.alpha = 1f;

            // 4) bonus key drop (optional)
            if (keyDropped && revealKeyGroup)
            {
                if (revealKeyText) revealKeyText.text = string.Format(Loc.T("+1 {0} KEY!"), Loc.T(keyTier.ToString().ToUpper())); // tier name localizes via its own BRONZE/SILVER/GOLD/LEGENDARY key
                t = 0f;
                while (t < 0.35f)
                {
                    t += Time.unscaledDeltaTime; float k = t / 0.35f;
                    revealKeyGroup.alpha = k;
                    revealKeyGroup.transform.localScale = Vector3.one * Mathf.LerpUnclamped(0.5f, 1f, EaseOutBack(k));
                    yield return null;
                }
                revealKeyGroup.alpha = 1f; revealKeyGroup.transform.localScale = Vector3.one;
            }

            if (revealOk) revealOk.gameObject.SetActive(true);
            revealCo = null;
        }

        // Overshoot easing for the item "punch".
        static float EaseOutBack(float k)
        {
            const float c1 = 1.70158f, c3 = 2.70158f;
            float p = k - 1f;
            return 1f + c3 * p * p * p + c1 * p * p;
        }

        // ---- first-open tutorial -------------------------------------------
        // A 4-step tap-through tour of the garage: each step scrolls its target row into view, frames it in yellow
        // and explains it in a coach bubble; a tap anywhere advances. Runs ONCE (PlayerPrefs flag) — the HUD/menu
        // garage buttons pulse until this completes. Closing the garage mid-tour aborts WITHOUT setting the flag,
        // so it simply restarts on the next open.
        IEnumerator GarageTutorial()
        {
            yield return null;                 // let the freshly-built scroll content lay itself out first
            Canvas.ForceUpdateCanvases();
            if (garagePanel == null || !garagePanel.activeInHierarchy) { garageTutCo = null; yield break; }

            // full-screen overlay INSIDE the garage panel -> draws over the card, dies with it
            garageTutOverlay = new GameObject("GarageTut", typeof(RectTransform));
            garageTutOverlay.transform.SetParent(garagePanel.transform, false);
            Stretch((RectTransform)garageTutOverlay.transform);
            var dim = Img(garageTutOverlay.transform, null, new Color(0, 0, 0, 0.55f));
            Stretch(dim.rectTransform);
            var dimBtn = dim.gameObject.AddComponent<Button>();   // the tap-anywhere advance catcher
            dimBtn.transition = Selectable.Transition.None;
            dimBtn.onClick.AddListener(() => garageTutTapped = true);

            // yellow highlight frame (flat fill + 4 crisp edges — kit sprites are pre-shaded, a flat quad keeps the colour true)
            var frame = Img(garageTutOverlay.transform, null, new Color(1f, 0.85f, 0.25f, 0.22f));
            frame.raycastTarget = false;
            frame.rectTransform.sizeDelta = Vector2.zero; // invisible until the first step measures its target
            FrameEdge(frame.transform, new Vector2(0, 1), new Vector2(1, 1), new Vector2(0, 6), new Vector2(0, 3));   // top
            FrameEdge(frame.transform, new Vector2(0, 0), new Vector2(1, 0), new Vector2(0, 6), new Vector2(0, -3));  // bottom
            FrameEdge(frame.transform, new Vector2(0, 0), new Vector2(0, 1), new Vector2(6, 0), new Vector2(-3, 0));  // left
            FrameEdge(frame.transform, new Vector2(1, 0), new Vector2(1, 1), new Vector2(6, 0), new Vector2(3, 0));   // right

            // coach bubble (kept clear of the highlight: bottom half of the screen for top targets and vice versa)
            var bubble = Img(garageTutOverlay.transform, UIKit.ShopBoxA(), White);
            bubble.color = new Color(0.13f, 0.15f, 0.22f, 0.97f); bubble.raycastTarget = false;
            Center(bubble.rectTransform, new Vector2(880, 260));
            var bubbleText = Label(bubble.transform, "", num, new Vector2(0, 20), new Vector2(760, 170), 30, White);
            bubbleText.horizontalOverflow = HorizontalWrapMode.Wrap; // long localized lines WRAP inside the bubble instead of running off-screen
            Label(bubble.transform, Loc.T("TAP TO CONTINUE"), num, new Vector2(0, -98), new Vector2(700, 40), 22, new Color(1f, 0.85f, 0.35f));

            var steps = new (Transform target, string text, float scrollPos)[]
            {
                (tutVehiclesRow,  Loc.T("Welcome to your GARAGE! Tap VEHICLES to see and EQUIP the cars, minivans and buses you own."), 1f),
                (tutChestGrid,    Loc.T("Open chests with gold to win NEW vehicles — the better the chest, the rarer the prize!"),      1f),
                (tutFreeChestRow, Loc.T("The FREE CHEST refills over time. Come back and open it — it costs nothing!"),                 0.6f),
                (tutCraftHeader,  Loc.T("Duplicate vehicles turn into shards. Spend shards here to CRAFT a guaranteed NEW car!"),       0f),
            };

            foreach (var s in steps)
            {
                if (s.target == null) continue;
                if (garageScroll != null) garageScroll.verticalNormalizedPosition = s.scrollPos; // bring the target into view
                Canvas.ForceUpdateCanvases();
                yield return null; // one frame so the scroll/layout settles before measuring

                var trt = (RectTransform)s.target;
                frame.rectTransform.position = trt.TransformPoint(trt.rect.center); // same canvas -> world pos + rect size line up 1:1
                frame.rectTransform.sizeDelta = trt.rect.size + new Vector2(28, 28);
                Vector2 local = garageTutOverlay.transform.InverseTransformPoint(frame.rectTransform.position);
                bubble.rectTransform.anchoredPosition = new Vector2(0, local.y > 0f ? -430f : 430f);
                bubbleText.text = s.text;

                garageTutTapped = false;
                while (!garageTutTapped)
                {
                    frame.color = new Color(1f, 0.85f, 0.25f, 0.20f + 0.10f * Mathf.Sin(Time.unscaledTime * 4f)); // gentle pulse
                    yield return null;
                }
            }

            PlayerPrefs.SetInt(GarageTutKey, 1); PlayerPrefs.Save(); // done — the HUD/menu button pulses stop themselves on this flag
            if (garageScroll != null) garageScroll.verticalNormalizedPosition = 1f; // hand the garage back scrolled to the top
            Destroy(garageTutOverlay); garageTutOverlay = null;
            garageTutCo = null;
        }

        // One crisp edge of the tutorial highlight frame (anchor-stretched along its side so it follows any frame size).
        void FrameEdge(Transform frame, Vector2 aMin, Vector2 aMax, Vector2 size, Vector2 pos)
        {
            var e = Img(frame, null, new Color(1f, 0.92f, 0.45f, 0.95f)); e.raycastTarget = false;
            var rt = e.rectTransform; rt.anchorMin = aMin; rt.anchorMax = aMax; rt.sizeDelta = size; rt.anchoredPosition = pos;
        }

        // ---- show / hide ----------------------------------------------------
        public void ShowGarage()
        {
            SetHudChromeVisible(false);
            if (coinBarGo) coinBarGo.SetActive(false); // (#6) the garage panel shows its own gold — hide the HUD coin bar so it isn't duplicated
            hideBonusTimer = true; HideBonusCountdown(); // (#2) don't duplicate the bonus countdown over the garage (it keeps ticking underneath)
            RefreshGarage();
            Toggle(garagePanel, true);
            // From the MENU this is a garage-only SCREEN: BusJamGame skips building a level entirely (GarageFromMenu)
            // and the backdrop goes fully OPAQUE — there is no game behind it at all.
            var bg = garagePanel != null ? garagePanel.GetComponent<Image>() : null;
            if (bg) bg.color = garageFromMenu ? new Color(0.07f, 0.06f, 0.10f, 1f) : new Color(0, 0, 0, 0.62f);
            // The guided tour belongs to the IN-GAME garage (where the highlighted HUD button led the player).
            if (!garageFromMenu && PlayerPrefs.GetInt(GarageTutKey, 0) == 0 && garageTutCo == null)
                garageTutCo = StartCoroutine(GarageTutorial()); // first in-game open -> run the tour
        }
        public void HideGarage()
        {
            if (garageTutCo != null) { StopCoroutine(garageTutCo); garageTutCo = null; }          // closed mid-tour ->
            if (garageTutOverlay) { Destroy(garageTutOverlay); garageTutOverlay = null; }         // abort; restarts next open
            if (garageFromMenu)
            {
                // Menu-garage screen: DON'T tear the garage down first. Hiding it + re-showing the HUD revealed the
                // empty game scene for the frames it takes MainMenu to load — a weird full-screen flash on close.
                // Keep the garage (opaque backdrop) up as the transition cover; the scene load destroys it anyway.
                garageFromMenu = false;
                OnHome?.Invoke(); // -> GoToMainMenu -> LoadScene("MainMenu")
                return;
            }
            Toggle(garagePanel, false); SetHudChromeVisible(true);
            if (chestOddsPanel) chestOddsPanel.SetActive(false); // drop-rates popup dies with the garage
            if (coinBarGo) coinBarGo.SetActive(true);  // (#6) restore the HUD coin bar
            hideBonusTimer = false;                     // (#2) the bonus tick re-shows the timer next frame if the level is still running
        }
    }
}
