using System.Collections;
using UnityEngine;
using UnityEngine.UI;

namespace BusJam
{
    /// <summary>
    /// Garage screen (partial of <see cref="GameUI"/>): browse vehicle skins (owned / locked), equip them, open the
    /// gold gacha chests, spend rare chest KEYS, and craft skins from shards. Built in code on the same UICanvas +
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

        GameObject garagePanel, revealPanel;
        Transform garageContent;
        Text garageGoldT, garageShardT, revealName, revealSub, revealKeyText;
        Image revealFrame, revealGlow;
        RawImage revealPattern, revealRays;
        CanvasGroup revealChestGroup, revealItemGroup, revealKeyGroup;
        Button revealOk;
        Coroutine revealCo;

        // Rarity -> frame colour (plan palette).
        static Color RarityColor(SkinRarity r)
        {
            switch (r)
            {
                case SkinRarity.Uncommon:  return new Color(0.30f, 0.77f, 0.38f); // #4CC461
                case SkinRarity.Rare:      return new Color(0.24f, 0.55f, 0.94f); // #3E8BF0
                case SkinRarity.Epic:      return new Color(0.69f, 0.26f, 0.94f); // #B043F0
                case SkinRarity.Legendary: return new Color(0.94f, 0.63f, 0.19f); // #F0A030
                default:                   return new Color(0.69f, 0.72f, 0.75f); // Common #B0B7C0
            }
        }
        static Color ChestTint(ChestTier t)
        {
            switch (t)
            {
                case ChestTier.Silver:    return new Color(0.70f, 0.74f, 0.82f);
                case ChestTier.Gold:      return new Color(0.95f, 0.78f, 0.30f);
                case ChestTier.Legendary: return new Color(0.72f, 0.42f, 0.95f);
                default:                  return new Color(0.78f, 0.53f, 0.30f); // Bronze
            }
        }

        // ---- HUD entry button (top-right, under the gear) -------------------
        void AddGarageButton(Transform hud)
        {
            if (hud == null) return;
            var b = Btn(hud, UIKit.A(25), new Color(0.55f, 0.42f, 0.86f), new Vector2(1, 1), new Vector2(-95, -250), new Vector2(132, 132), ShowGarage);
            Label(b.transform, "GARAGE", num, Vector2.zero, new Vector2(130, 50), 24, White);
        }

        // ---- Build the (hidden) garage panel + reveal modal -----------------
        void BuildGarage()
        {
            garagePanel = Panel("Garage", new Color(0, 0, 0, 0.62f));

            var card = Img(garagePanel.transform, UIKit.PanelTall(), new Color(0.20f, 0.22f, 0.33f));
            Center(card.rectTransform, new Vector2(980, 1560));
            Label(card.transform, "GARAGE", title, new Vector2(0, 690), new Vector2(700, 120), 74, White);
            RedClose(card.transform, HideGarage);

            // gold + shard counters
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

            // scroll view (chests + skins grid + craft rows) — same recipe as BuildShop
            var svGo = new GameObject("ScrollView", typeof(RectTransform));
            svGo.transform.SetParent(card.transform, false);
            var svRt = svGo.GetComponent<RectTransform>();
            svRt.anchorMin = svRt.anchorMax = svRt.pivot = new Vector2(0.5f, 0.5f);
            svRt.anchoredPosition = new Vector2(0, -110); svRt.sizeDelta = new Vector2(900, 1080);
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

            BuildReveal();
            RefreshGarage();
            garagePanel.SetActive(false);
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

            // 1) chests
            SectionLabel(garageContent, "CHESTS");
            var chestGrid = GridRow(garageContent, new Vector2(275, 275), 3);
            ChestCard(chestGrid, ChestTier.Bronze, "BRONZE");
            ChestCard(chestGrid, ChestTier.Silver, "SILVER");
            ChestCard(chestGrid, ChestTier.Gold,   "GOLD");
            LegendaryChestRow(garageContent);
            FreeChestRow(garageContent);

            // 2) skins
            SectionLabel(garageContent, "VEHICLE SKINS");
            var skinGrid = GridRow(garageContent, new Vector2(275, 330), 3);
            foreach (var def in SkinCatalog.Vehicles) SkinCard(skinGrid, def);

            // 3) craft (shards)
            SectionLabel(garageContent, "CRAFT  (shards)");
            CraftRow(garageContent, SkinRarity.Uncommon);
            CraftRow(garageContent, SkinRarity.Rare);
            CraftRow(garageContent, SkinRarity.Epic);
            CraftRow(garageContent, SkinRarity.Legendary);
        }

        // A faint section header row (LayoutElement gives the vertical group a fixed height).
        void SectionLabel(Transform parent, string text)
        {
            var go = Img(parent, null, new Color(1, 1, 1, 0.06f)); go.raycastTarget = false;
            var le = go.gameObject.AddComponent<LayoutElement>(); le.preferredHeight = 64; le.minHeight = 64;
            Label(go.transform, text, num, Vector2.zero, new Vector2(840, 50), 34, new Color(0.85f, 0.88f, 0.95f));
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
            return go.transform;
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

        // A simple code-built treasure chest (body + lid + gold lock), centred in `parent`, scaled by width `w`.
        void BuildChest(Transform parent, Color tint, float w)
        {
            var body = Img(parent, null, tint); body.raycastTarget = false;
            Place(body.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0, -w * 0.12f), new Vector2(w, w * 0.52f));
            var lid = Img(parent, null, new Color(tint.r * 0.78f, tint.g * 0.78f, tint.b * 0.78f)); lid.raycastTarget = false;
            Place(lid.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0, w * 0.22f), new Vector2(w * 1.05f, w * 0.30f));
            var lck = Img(parent, null, new Color(1f, 0.85f, 0.3f)); lck.raycastTarget = false;
            Place(lck.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0, 0), new Vector2(w * 0.17f, w * 0.17f));
        }

        // One gold chest card: chest art + gold-cost OPEN button + (if you hold keys) a key badge to open free.
        void ChestCard(Transform parent, ChestTier tier, string name)
        {
            Color tint = ChestTint(tier);
            var card = Img(parent, UIKit.ShopIconBgA(), White); card.color = new Color(tint.r * 0.55f, tint.g * 0.55f, tint.b * 0.55f);
            Label(card.transform, name, num, new Vector2(0, 96), new Vector2(255, 44), 26, White);
            BuildChest(Holder(card.transform, new Vector2(0, 16), new Vector2(150, 110)), tint, 110);
            var buy = Btn(card.transform, UIKit.PriceBtnA(), new Color(0.30f, 0.72f, 0.36f), new Vector2(0.5f, 0), new Vector2(0, 16), new Vector2(250, 78), () => OpenChest(tier));
            var bc = Img(buy.transform, UIKit.Coin(), Gold); bc.raycastTarget = false;
            Place(bc.rectTransform, new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(30, 0), new Vector2(46, 46));
            Label(buy.transform, ChestService.Cost(tier).ToString(), num, new Vector2(26, 0), new Vector2(250, 48), 30, White);

            int keys = SaveSystem.Keys(tier.ToString());
            if (keys > 0) KeyBadge(card.transform, tier, keys);
        }

        // Premium key-only Legendary chest (full-width row); openable only with a Legendary key.
        void LegendaryChestRow(Transform parent)
        {
            int keys = SaveSystem.Keys(ChestTier.Legendary.ToString());
            var row = Img(parent, UIKit.ShopBoxA(), White); row.color = new Color(0.50f, 0.30f, 0.70f);
            var le = row.gameObject.AddComponent<LayoutElement>(); le.preferredHeight = 156; le.minHeight = 156;
            BuildChest(Holder(row.transform, new Vector2(-330, 0), new Vector2(150, 120)), ChestTint(ChestTier.Legendary), 116);
            Label(row.transform, "LEGENDARY", title, new Vector2(-40, 26), new Vector2(440, 54), 36, White, TextAnchor.MiddleLeft);
            Label(row.transform, "key only", num, new Vector2(-40, -24), new Vector2(440, 36), 22, new Color(0.92f, 0.86f, 1f), TextAnchor.MiddleLeft);
            if (keys > 0)
            {
                var open = Btn(row.transform, UIKit.PriceBtnA(), new Color(0.30f, 0.72f, 0.36f), new Vector2(1, 0.5f), new Vector2(-150, 0), new Vector2(300, 104), () => OpenChestWithKey(ChestTier.Legendary));
                var ki = Img(open.transform, UIKit.Gem(), new Color(1f, 0.95f, 0.5f)); ki.raycastTarget = false;
                Place(ki.rectTransform, new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(28, 0), new Vector2(46, 46));
                Label(open.transform, "OPEN " + keys, title, new Vector2(26, 0), new Vector2(300, 64), 32, White);
            }
            else
            {
                Label(row.transform, "FIND A KEY", num, new Vector2(250, 0), new Vector2(320, 60), 30, new Color(0.86f, 0.80f, 1f));
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
            var le = row.gameObject.AddComponent<LayoutElement>(); le.preferredHeight = 130; le.minHeight = 130;
            Label(row.transform, "FREE CHEST", title, new Vector2(-140, 0), new Vector2(420, 70), 40, White, TextAnchor.MiddleLeft);
            if (ChestService.FreeChestReady())
            {
                var open = Btn(row.transform, UIKit.PriceBtnA(), new Color(0.30f, 0.72f, 0.36f), new Vector2(1, 0.5f), new Vector2(-180, 0), new Vector2(300, 96), OpenFreeChest);
                Label(open.transform, "OPEN", title, Vector2.zero, new Vector2(300, 64), 38, White);
            }
            else
            {
                long s = ChestService.FreeChestSecondsLeft();
                string t = (s / 3600) + "h " + ((s % 3600) / 60) + "m";
                Label(row.transform, t, num, new Vector2(220, 0), new Vector2(360, 60), 38, new Color(0.92f, 0.96f, 1f));
            }
        }

        // One skin card in the grid: rarity tag + thumbnail placeholder + name; tap to equip (owned) or LOCKED overlay.
        void SkinCard(Transform parent, SkinDef def)
        {
            bool owned = SaveSystem.OwnsSkin(def.id);
            bool equipped = owned && SaveSystem.EquippedVehicleSkin == def.id;
            Color rc = RarityColor(def.rarity);
            var card = Img(parent, UIKit.ShopIconBgA(), White); card.color = new Color(rc.r * 0.42f, rc.g * 0.42f, rc.b * 0.42f);

            // rarity PILL (top) — white on the rarity colour so it always reads, on any card
            var pill = Img(card.transform, null, rc); pill.raycastTarget = false;
            Place(pill.rectTransform, new Vector2(0.5f, 1), new Vector2(0.5f, 1), new Vector2(0, -34), new Vector2(212, 44));
            Label(pill.transform, def.rarity.ToString().ToUpper(), num, Vector2.zero, new Vector2(206, 40), 22, White);

            var tile = Img(card.transform, null, new Color(0.16f, 0.17f, 0.22f)); tile.raycastTarget = false;
            Place(tile.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0, 6), new Vector2(182, 120));
            if (def.HasPattern) // preview the actual generated pattern texture on the card
            {
                var patGo = new GameObject("Pat", typeof(RectTransform));
                patGo.transform.SetParent(tile.transform, false);
                var raw = patGo.AddComponent<RawImage>();
                raw.texture = SkinTextureFactory.Get(def.pattern, def.accent);
                raw.raycastTarget = false;
                var prt = raw.rectTransform; prt.anchorMin = Vector2.zero; prt.anchorMax = Vector2.one; prt.offsetMin = Vector2.zero; prt.offsetMax = Vector2.zero;
            }

            Label(card.transform, def.displayName, num, new Vector2(0, -126), new Vector2(255, 50), 28, White);

            if (owned)
            {
                var b = card.gameObject.AddComponent<Button>(); b.targetGraphic = card;
                string id = def.id;
                b.onClick.AddListener(() => EquipSkin(id));
                if (equipped)
                {
                    var badge = Img(card.transform, null, new Color(0.20f, 0.72f, 0.32f)); badge.raycastTarget = false;
                    Place(badge.rectTransform, new Vector2(0.5f, 0), new Vector2(0.5f, 0), new Vector2(0, 14), new Vector2(190, 44));
                    Label(badge.transform, "EQUIPPED", num, Vector2.zero, new Vector2(190, 40), 22, White);
                }
            }
            else
            {
                // lock overlay ONLY over the thumbnail, so the rarity pill + name stay readable
                var lk = Img(card.transform, null, new Color(0, 0, 0, 0.60f)); lk.raycastTarget = false;
                Place(lk.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0, 6), new Vector2(186, 124));
                Label(lk.transform, "LOCKED", num, Vector2.zero, new Vector2(180, 44), 24, new Color(0.90f, 0.90f, 0.96f));
            }
        }

        // One craft row: rarity + remaining-locked count + a shard-cost CRAFT button (greyed when unaffordable/empty).
        void CraftRow(Transform parent, SkinRarity rar)
        {
            int locked = CraftService.Craftable(rar).Count;
            bool can = CraftService.CanCraft(rar);
            Color rc = RarityColor(rar);
            var row = Img(parent, UIKit.ShopBoxA(), White); row.color = new Color(rc.r * 0.45f, rc.g * 0.45f, rc.b * 0.45f);
            var le = row.gameObject.AddComponent<LayoutElement>(); le.preferredHeight = 120; le.minHeight = 120;
            Label(row.transform, rar.ToString().ToUpper(), num, new Vector2(-262, 24), new Vector2(320, 46), 30, White, TextAnchor.MiddleLeft);
            Label(row.transform, locked + " left", num, new Vector2(-262, -24), new Vector2(320, 34), 22, new Color(0.86f, 0.88f, 0.94f), TextAnchor.MiddleLeft);
            var craft = Btn(row.transform, UIKit.PriceBtnA(), can ? new Color(0.30f, 0.72f, 0.36f) : new Color(0.45f, 0.45f, 0.50f),
                            new Vector2(1, 0.5f), new Vector2(-165, 0), new Vector2(290, 92), () => { if (can) CraftRarity(rar); });
            var sc = Img(craft.transform, UIKit.Gem(), new Color(0.42f, 0.82f, 1f)); sc.raycastTarget = false;
            Place(sc.rectTransform, new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(30, 0), new Vector2(44, 44));
            Label(craft.transform, CraftService.Cost(rar).ToString(), num, new Vector2(26, 0), new Vector2(290, 50), 30, White);
        }

        // ---- actions --------------------------------------------------------
        void OpenChest(ChestTier tier)        { var res = ChestService.BuyAndOpen(tier);  if (res == null) return; ShowChestResult(res.Value); }
        void OpenChestWithKey(ChestTier tier) { var res = ChestService.OpenWithKey(tier);  if (res == null) return; ShowChestResult(res.Value); }
        void OpenFreeChest()                  { var res = ChestService.OpenFree();          if (res == null) return; ShowChestResult(res.Value); }
        void ShowChestResult(ChestResult r)
        {
            if (r.skin == null) return;
            ShowReveal(r.skin, r.wasDupe ? ("DUPLICATE  +" + r.shardsGained + " shards") : "NEW!", r.keyDropped, r.keyTier);
            RefreshGarage();
        }
        void CraftRarity(SkinRarity rar)
        {
            var skin = CraftService.Craft(rar);
            if (skin == null) return;
            ShowReveal(skin, "CRAFTED!", false, ChestTier.Bronze);
            RefreshGarage();
        }
        void EquipSkin(string id)
        {
            SaveSystem.EquippedVehicleSkin = id;
            RefreshGarage();
            OnReskin?.Invoke(); // rebuild the live board so the equipped MODEL shows
        }

        // ---- reveal modal + opening animation -------------------------------
        void BuildReveal()
        {
            revealPanel = Panel("Reveal", Dim);
            var cv = revealPanel.AddComponent<Canvas>(); cv.overrideSorting = true; cv.sortingOrder = 85;
            revealPanel.AddComponent<GraphicRaycaster>();
            var card = Img(revealPanel.transform, UIKit.PanelTall(), new Color(0.22f, 0.24f, 0.36f));
            Center(card.rectTransform, new Vector2(820, 980));

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
            BuildChest(chestGo.transform, new Color(0.78f, 0.53f, 0.30f), 240);

            // item group (frame + name + sub) — hidden until the chest pops open
            var itemGo = new GameObject("Item", typeof(RectTransform));
            itemGo.transform.SetParent(card.transform, false);
            Stretch(itemGo.GetComponent<RectTransform>());
            revealItemGroup = itemGo.AddComponent<CanvasGroup>(); revealItemGroup.alpha = 0f;
            Label(itemGo.transform, "YOU GOT", num, new Vector2(0, 330), new Vector2(600, 60), 34, new Color(0.85f, 0.90f, 1f));
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

            revealOk = Btn(card.transform, UIKit.PriceBtnA(), new Color(0.30f, 0.72f, 0.36f), new Vector2(0.5f, 0), new Vector2(0, 60), new Vector2(380, 120), () => revealPanel.SetActive(false));
            Label(revealOk.transform, "OK", title, Vector2.zero, new Vector2(380, 80), 46, White);

            revealPanel.SetActive(false);
        }

        void ShowReveal(SkinDef skin, string sub, bool keyDropped, ChestTier keyTier)
        {
            if (skin == null || revealPanel == null) return;
            revealPanel.SetActive(true);
            if (revealCo != null) StopCoroutine(revealCo);
            revealCo = StartCoroutine(RevealAnim(skin, sub, keyDropped, keyTier));
        }

        IEnumerator RevealAnim(SkinDef skin, string sub, bool keyDropped, ChestTier keyTier)
        {
            Color rc = RarityColor(skin.rarity);
            float inten = (int)skin.rarity / 4f; // 0 (Common) .. 1 (Legendary) -> bigger, longer, brighter reveal
            if (revealFrame) revealFrame.color = rc;
            if (revealName)  revealName.text = skin.displayName;
            if (revealSub)   revealSub.text  = sub;
            if (revealPattern) revealPattern.texture = skin.HasPattern ? SkinTextureFactory.Get(skin.pattern, skin.accent) : null;

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
                if (revealKeyText) revealKeyText.text = "+1 " + keyTier.ToString().ToUpper() + " KEY!";
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

        // ---- show / hide ----------------------------------------------------
        public void ShowGarage() { SetHudChromeVisible(false); RefreshGarage(); Toggle(garagePanel, true); }
        public void HideGarage()
        {
            Toggle(garagePanel, false); SetHudChromeVisible(true);
            if (garageFromMenu) { garageFromMenu = false; OnHome?.Invoke(); } // opened from the menu -> go back to it
        }
    }
}
