using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace BusJam
{
    /// <summary>
    /// Vehicle wardrobe ("dolap") panel (partial of <see cref="GameUI"/>): three localized sections
    /// (Cars / Minivans / Buses) showing the player's UNLOCKED vehicles. Tap an owned vehicle to EQUIP it for
    /// that type INDEPENDENTLY (changing the car keeps the same minivan/bus). Locked vehicles show a coin price
    /// that unlocks the whole set. Opened from a button on the Garage screen. Models come from the
    /// VehicleSetCatalog (built once via "BusJam ▸ Build Vehicle Sets").
    /// </summary>
    public partial class GameUI
    {
        GameObject vehiclesPanel;
        Transform vehiclesContent;
        Text vehiclesGoldT;

        // Lazy preview generation — cards are created empty, then filled ONE RENDER per frame so opening the panel
        // doesn't render all ~30 vehicles in one frame (that caused the open-delay + FPS drop).
        readonly List<(RawImage img, GameObject prefab, bool crop, float fill)> pendingPreviews = new List<(RawImage, GameObject, bool, float)>();
        Coroutine previewCo;

        // Full-width entry row added at the top of the Garage scroll content (tap to open the wardrobe).
        void AddVehiclesEntry(Transform parent)
        {
            var row = Img(parent, UIKit.ShopBoxA(), new Color(0.45f, 0.38f, 0.72f));
            var le = row.gameObject.AddComponent<LayoutElement>(); le.preferredHeight = 130; le.minHeight = 130;
            var b = row.gameObject.AddComponent<Button>(); b.targetGraphic = row;
            b.onClick.AddListener(ShowVehicles);
            Label(row.transform, Loc.T("VEHICLES"), title, Vector2.zero, new Vector2(760, 80), 44, White);
        }

        // ---- build the (hidden) wardrobe panel — same scroll recipe as BuildGarage --------
        // Adopt the baked panel (InGameGarage marker) if present; otherwise build the chrome in code. Content is built
        // lazily on first ShowVehicles either way, so the game is unchanged until you bake.
        void BuildVehicles()
        {
            var g = InGameGarage.Instance;
            Button close;
            if (g != null && g.vehiclesRoot != null && g.vehiclesContent != null && g.vehiclesGold != null)
            {
                vehiclesPanel = g.vehiclesRoot; vehiclesContent = g.vehiclesContent; vehiclesGoldT = g.vehiclesGold; close = g.vehiclesClose;
            }
            else close = BuildVehiclesChrome();

            if (close) close.onClick.AddListener(HideVehicles); // wired at runtime (onClick refs don't serialize)
            if (vehiclesPanel) vehiclesPanel.SetActive(false);  // content (+ thumbnails) built lazily on first ShowVehicles
        }

        // Build ONLY the wardrobe window chrome; sets vehiclesPanel / vehiclesContent / vehiclesGoldT and returns the
        // (unwired) close button. Shared by the runtime path above AND the editor baker (EditorBakeVehicles).
        Button BuildVehiclesChrome()
        {
            vehiclesPanel = Panel("Vehicles", new Color(0, 0, 0, 0.62f));
            var cv = vehiclesPanel.AddComponent<Canvas>(); cv.overrideSorting = true; cv.sortingOrder = 80; // above the garage
            vehiclesPanel.AddComponent<GraphicRaycaster>();

            var card = Img(vehiclesPanel.transform, UIKit.PanelTall(), new Color(0.20f, 0.22f, 0.33f));
            Center(card.rectTransform, new Vector2(980, 1560));
            Label(card.transform, Loc.T("VEHICLES"), title, new Vector2(0, 690), new Vector2(760, 120), 70, White);
            var close = RedClose(card.transform, null);

            var goldChip = Img(card.transform, UIKit.CoinBar(), Dark); goldChip.raycastTarget = false;
            Place(goldChip.rectTransform, new Vector2(0.5f, 1), new Vector2(0.5f, 1), new Vector2(0, -205), new Vector2(300, 88));
            var gci = Img(goldChip.transform, UIKit.Coin(), Gold); gci.raycastTarget = false;
            Place(gci.rectTransform, new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(40, 0), new Vector2(60, 60));
            vehiclesGoldT = Label(goldChip.transform, "0", num, new Vector2(34, 0), new Vector2(190, 56), 40, White);

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
            vehiclesContent = ctGo.transform;
            return close;
        }

#if UNITY_EDITOR
        // Editor baker entry (BusJam ▸ Bake Garage Panels): build the wardrobe chrome under `canvas`, return its refs.
        public (GameObject panel, Transform content, Text gold, Button close) EditorBakeVehicles(Transform canvas)
        {
            title = UIKit.Title(); num = UIKit.Num(); root = canvas;
            var close = BuildVehiclesChrome();
            return (vehiclesPanel, vehiclesContent, vehiclesGoldT, close);
        }
#endif

        // ---- (re)populate the three sections + coin counter --------------------------------
        void RefreshVehicles()
        {
            if (previewCo != null) { StopCoroutine(previewCo); previewCo = null; } // cancel a half-finished preview pass
            pendingPreviews.Clear();
            if (vehiclesGoldT) vehiclesGoldT.text = SaveSystem.Coins.ToString();
            if (vehiclesContent == null) return;
            for (int i = vehiclesContent.childCount - 1; i >= 0; i--)
            {
                var ch = vehiclesContent.GetChild(i); ch.SetParent(null, false); Destroy(ch.gameObject);
            }

            if (!VehicleWardrobe.HasCatalog)
            {
                SectionLabel(vehiclesContent, Loc.T("Run BusJam > Build Vehicle Sets"));
                return;
            }

            VehicleSectionCards(VehicleType.Car,     "CARS");
            VehicleSectionCards(VehicleType.Minivan, "MINIVANS");
            VehicleSectionCards(VehicleType.Bus,     "BUSES");
            previewCo = StartCoroutine(FillPreviewsLazy(pendingPreviews.ToArray()));
        }

        // Fill the queued preview thumbnails one RENDER per frame (cached ones fill instantly) so the panel never
        // stalls when opened. Snapshot the queue so a re-open (which rebuilds it) can't mutate a running pass.
        System.Collections.IEnumerator FillPreviewsLazy((RawImage img, GameObject prefab, bool crop, float fill)[] items)
        {
            foreach (var p in items)
            {
                if (p.img == null) continue;
                bool wasCached = VehiclePreview.IsCached(p.prefab);
                var rt = VehiclePreview.Get(p.prefab, 35f, p.crop, p.fill);
                if (p.img != null && rt != null) { p.img.texture = rt; p.img.color = Color.white; }
                if (!wasCached) yield return null; // only spread the EXPENSIVE first-time renders across frames
            }
            previewCo = null;
        }

        // One section: a localized header + a card per DISTINCT model of this type across the sets (dedup by prefab,
        // so the shared Connect/Bus appear once even though every set bundles them).
        void VehicleSectionCards(VehicleType t, string headerKey)
        {
            SectionLabel(vehiclesContent, Loc.T(headerKey));
            var grid = GridRow(vehiclesContent, new Vector2(275, 320), 3);
            // rarest first (every section); clone so we never reorder the catalog asset itself
            var sets = (VehicleSetCatalog.VehicleSet[])VehicleWardrobe.Catalog.sets.Clone();
            System.Array.Sort(sets, (a, b) => (b?.rarity ?? -1).CompareTo(a?.rarity ?? -1));
            var seen = new HashSet<GameObject>();
            foreach (var s in sets)
            {
                if (s == null) continue;
                var pf = s.PrefabFor(t);
                if (pf == null || !seen.Add(pf)) continue; // first providing set represents this model
                VehicleCard(grid, t, s);
            }
        }

        // One card: name + placeholder tile. Owned -> tap to equip (EQUIPPED badge if current). Locked -> dark
        // overlay + a coin-price button that unlocks the whole set.
        void VehicleCard(Transform parent, VehicleType t, VehicleSetCatalog.VehicleSet set)
        {
            bool owned = SaveSystem.OwnsSet(set.id);
            bool equipped = owned && SaveSystem.EquippedSet(t) == set.id;
            string label = set.displayName; // each item has its own name now (car / minivan / bus, incl. "Classic")

            var card = Img(parent, UIKit.ShopIconBgA(), White); card.color = new Color(0.22f, 0.24f, 0.31f);

            // rarity badge (all types) — a tier-coloured pill at the top
            {
                var pill = Img(card.transform, null, TierColor(set.rarity)); pill.raycastTarget = false;
                Place(pill.rectTransform, new Vector2(0.5f, 1), new Vector2(0.5f, 1), new Vector2(0, -26), new Vector2(170, 36));
                Label(pill.transform, TierName(set.rarity), num, Vector2.zero, new Vector2(166, 32), 19, White);
            }

            var tile = Img(card.transform, null, new Color(0.16f, 0.17f, 0.22f)); tile.raycastTarget = false;
            Place(tile.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0, 4), new Vector2(210, 140));

            // live 3D thumbnail — created empty now, FILLED lazily (FillPreviewsLazy) so the panel opens without a hitch.
            // fill < 1 = camera closer = bigger vehicle: cars were too small, minivans a touch bigger, bus unchanged.
            var pv = new GameObject("Preview", typeof(RectTransform)).AddComponent<RawImage>();
            pv.transform.SetParent(tile.transform, false);
            pv.raycastTarget = false; pv.color = new Color(1, 1, 1, 0); // invisible until its texture is ready
            var pr = pv.rectTransform; pr.anchorMin = Vector2.zero; pr.anchorMax = Vector2.one; pr.offsetMin = Vector2.zero; pr.offsetMax = Vector2.zero;
            float fill = t == VehicleType.Car ? 0.7f : t == VehicleType.Minivan ? 0.85f : 1.0f; // <1 = bigger; tweak per type
            pendingPreviews.Add((pv, set.PrefabFor(t), t != VehicleType.Car, fill));

            Label(card.transform, label, num, new Vector2(0, -126), new Vector2(255, 50), 28, White);

            if (owned)
            {
                var b = card.gameObject.AddComponent<Button>(); b.targetGraphic = card;
                string id = set.id; var tt = t;
                b.onClick.AddListener(() => EquipVehicle(tt, id));
                if (equipped)
                {
                    var badge = Img(card.transform, null, new Color(0.20f, 0.72f, 0.32f)); badge.raycastTarget = false;
                    Place(badge.rectTransform, new Vector2(0.5f, 0), new Vector2(0.5f, 0), new Vector2(0, 12), new Vector2(200, 44));
                    Label(badge.transform, Loc.T("EQUIPPED"), num, Vector2.zero, new Vector2(200, 40), 22, White);
                }
            }
            else
            {
                // Locked = not collected. Keep the car image at FULL brightness so its real colours read ("olduğu
                // gibi", and locked cars don't all blur into the same dark blob) — only a small "from chests" banner
                // along the bottom of the tile marks it locked. No heavy overlay.
                var banner = Img(card.transform, null, new Color(0.04f, 0.04f, 0.06f, 0.80f)); banner.raycastTarget = false;
                Place(banner.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0, -52), new Vector2(212, 34));
                Label(banner.transform, Loc.T("From chests"), num, Vector2.zero, new Vector2(206, 30), 18, new Color(0.96f, 0.86f, 0.5f));
            }
        }

        // Car rarity-tier -> badge colour / label (0 Common, 1 Medium, 2 Legendary). Shared with the reveal modal.
        public static Color TierColor(int rarity) =>
            rarity >= 3 ? new Color(0.95f, 0.66f, 0.20f)   // Legendary = gold
          : rarity == 2 ? new Color(0.69f, 0.30f, 0.95f)   // Epic = purple
          : rarity == 1 ? new Color(0.32f, 0.78f, 0.42f)   // Uncommon = green
          :               new Color(0.62f, 0.66f, 0.72f);  // Common = grey
        public static string TierName(int rarity) => rarity >= 3 ? "LEGENDARY" : rarity == 2 ? "EPIC" : rarity == 1 ? "UNCOMMON" : "COMMON";

        // ---- actions ----------------------------------------------------------------------
        void EquipVehicle(VehicleType t, string setId)
        {
            SaveSystem.SetEquippedSet(t, setId);
            RefreshVehicles();
            OnReskin?.Invoke();         // rebuild the live board so the newly-equipped model shows
            SetHudChromeVisible(false); // that rebuild re-shows the HUD (StartLevel -> ShowHud); re-hide it — a panel is open
        }

        void UnlockVehicle(string setId)
        {
            var cat = VehicleWardrobe.Catalog;
            var set = cat != null ? cat.ById(setId) : null;
            if (set == null) return;
            if (!VehicleWardrobe.TryUnlock(set)) return;     // not enough coins / already owned
            SaveSystem.SetEquippedSet(VehicleType.Car, setId); // auto-equip the car you just bought
            SetCoins(SaveSystem.Coins);
            RefreshVehicles();
            OnReskin?.Invoke();
        }

        public void ShowVehicles() { RefreshVehicles(); Toggle(vehiclesPanel, true); }
        public void HideVehicles()
        {
            Toggle(vehiclesPanel, false);
            if (garageGoldT) garageGoldT.text = SaveSystem.Coins.ToString(); // sync the garage coin counter after unlocks
        }
    }
}
