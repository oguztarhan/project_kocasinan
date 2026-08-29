using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace Ridebury
{
    /// <summary>
    /// Vehicle wardrobe ("dolap") panel (partial of <see cref="GameUI"/>): three localized sections
    /// (Cars / Minivans / Buses) showing the player's UNLOCKED vehicles. Tap an owned vehicle to EQUIP it for
    /// that type INDEPENDENTLY (changing the car keeps the same minivan/bus). Locked vehicles show a coin price
    /// that unlocks the whole set. Opened from a button on the Garage screen. Models come from the
    /// VehicleSetCatalog (built once via "Ridebury ▸ Build Vehicle Sets").
    /// </summary>
    public partial class GameUI
    {
        GameObject vehiclesPanel;
        Transform vehiclesContent;
        Text vehiclesGoldT;

        // Lazy preview generation — cards are created empty, then filled ONE RENDER per frame so opening the panel
        // doesn't render all ~30 vehicles in one frame (that caused the open-delay + FPS drop).
        readonly List<(RawImage img, GameObject prefab, bool crop, float fill, float yaw, Color tint)> pendingPreviews = new List<(RawImage, GameObject, bool, float, float, Color)>();
        Coroutine previewCo;

        // Full-width entry row added at the top of the Garage scroll content (tap to open the wardrobe).
        void AddVehiclesEntry(Transform parent)
        {
            // The wardrobe entry is the garage's primary action, so it uses the cut kit's ORANGE action
            // button (dark outline). It used to share the flat kit bar with the plain rows, which read as
            // decoration rather than as something tappable.
            var row = Img(parent, UIKit.BtnOrange(), new Color(1f, 0.55f, 0.12f));
            tutVehiclesRow = row.transform; // garage-tutorial step 1 target
            GOverride(row, g => g.vehiclesButtonSprite, g => g.vehiclesButtonColor); // Inspector: swap the "ARAÇLAR" button image / colour
            var le = row.gameObject.AddComponent<LayoutElement>(); le.preferredHeight = 134; le.minHeight = 134;
            Sliced(row, new Vector2(796, 134));
            var b = row.gameObject.AddComponent<Button>(); b.targetGraphic = row;
            b.onClick.AddListener(ShowVehicles);
            var vlabel = Label(row.transform, Loc.T("VEHICLES"), title, Vector2.zero, new Vector2(760, 80), 52, Ink);
            var vout = vlabel.gameObject.AddComponent<Outline>();
            vout.effectColor = new Color(1f, 0.86f, 0.58f, 0.55f); vout.effectDistance = new Vector2(1.5f, -1.5f);
        }

        // ---- build the (hidden) wardrobe panel — same scroll recipe as BuildGarage --------
        // Always build the wardrobe chrome in code (so it reflects the latest changes); the InGameGarage marker is read
        // only for colours. Content (+ thumbnails) is built lazily on first ShowVehicles.
        void BuildVehicles()
        {
            Button close = BuildVehiclesChrome();
            if (close) close.onClick.AddListener(HideVehicles); // wired at runtime (onClick refs don't serialize)
            if (vehiclesPanel) vehiclesPanel.SetActive(false);
        }

        // Build ONLY the wardrobe window chrome; sets vehiclesPanel / vehiclesContent / vehiclesGoldT and returns the
        // (unwired) close button. Shared by the runtime path above AND the editor baker (EditorBakeVehicles).
        Button BuildVehiclesChrome()
        {
            vehiclesPanel = Panel("Vehicles", new Color(0, 0, 0, 0.62f));
            var cv = vehiclesPanel.AddComponent<Canvas>(); cv.overrideSorting = true; cv.sortingOrder = 80; // above the garage
            vehiclesPanel.AddComponent<GraphicRaycaster>();

            var card = Img(vehiclesPanel.transform, UIKit.PanelTall(), new Color(0.20f, 0.22f, 0.33f));
            GOverride(card, g => g.vehiclesWindowSprite, g => g.vehiclesWindowColor); // Inspector: swap the Vehicles ("Araçlar") window image / colour
            Center(card.rectTransform, new Vector2(980, PanelCardHeight()));          // clamped to the DEVICE height (short/16:9 phones)
            var titleT = Label(card.transform, Loc.T("VEHICLES"), title, Vector2.zero, new Vector2(760, 120), 70, White);
            Place(titleT.rectTransform, new Vector2(0.5f, 1), new Vector2(0.5f, 1), new Vector2(0, -90), new Vector2(760, 120)); // pinned to the card TOP
            var close = RedClose(card.transform, null);

            // Same deal as the garage: the gold pill is authored in GaragePanel.prefab (Counter_Gold_Vehicles) and
            // cloned from there, so the Inspector is where you edit it. See CounterPill.
            var counters = new GameObject("Counters", typeof(RectTransform)).GetComponent<RectTransform>();
            counters.SetParent(card.transform, false);
            counters.anchorMin = Vector2.zero; counters.anchorMax = Vector2.one;
            counters.offsetMin = Vector2.zero; counters.offsetMax = Vector2.zero;
            vehiclesGoldT = CounterPill(counters, "Counter_Gold_Vehicles", new Vector2(0, -205),
                                        garageCfg != null ? garageCfg.coinCounterIcon : null, UIKit.Coin(), 60f);

            // Stretched between fixed top/bottom pads (not a fixed 1080 height) so it always stays INSIDE the card —
            // same overflow fix as BuildGarageChrome.
            var svGo = new GameObject("ScrollView", typeof(RectTransform));
            svGo.transform.SetParent(card.transform, false);
            var svRt = svGo.GetComponent<RectTransform>();
            svRt.anchorMin = new Vector2(0.5f, 0f); svRt.anchorMax = new Vector2(0.5f, 1f); svRt.pivot = new Vector2(0.5f, 0.5f);
            svRt.sizeDelta = new Vector2(820, -(350f + 210f));      // measured against the notched window sprite — see BuildGarageChrome
            svRt.anchoredPosition = new Vector2(0, (210f - 350f) * 0.5f);
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
        // Editor baker entry (Ridebury ▸ Bake Garage Panels): build the wardrobe chrome under `canvas`, return its refs.
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
                SectionLabel(vehiclesContent, Loc.T("Run Ridebury > Build Vehicle Sets"));
                return;
            }

            VehicleSectionCards(VehicleType.Car,     "CARS");
            VehicleSectionCards(VehicleType.Minivan, "MINIVANS");
            VehicleSectionCards(VehicleType.Bus,     "BUSES");
            previewCo = StartCoroutine(FillPreviewsLazy(pendingPreviews.ToArray()));
            PreserveAuthoredFontSizes(vehiclesContent); // keep these authored sizes through the global font applier (else an equip's rebuild shrinks them)
        }

        // Fill the queued preview thumbnails one RENDER per frame (cached ones fill instantly) so the panel never
        // stalls when opened. Snapshot the queue so a re-open (which rebuilds it) can't mutate a running pass.
        System.Collections.IEnumerator FillPreviewsLazy((RawImage img, GameObject prefab, bool crop, float fill, float yaw, Color tint)[] items)
        {
            foreach (var p in items)
            {
                if (p.img == null) continue;
                bool wasCached = VehiclePreview.IsCached(p.prefab);
                var rt = VehiclePreview.Get(p.prefab, p.yaw, p.crop, p.fill);
                if (p.img != null && rt != null) { p.img.texture = rt; p.img.color = p.tint; }
                if (!wasCached) yield return null; // only spread the EXPENSIVE first-time renders across frames
            }
            previewCo = null;
        }

        // One section: a localized header + a card per DISTINCT model of this type across the sets (dedup by prefab,
        // so the shared Connect/Bus appear once even though every set bundles them).
        void VehicleSectionCards(VehicleType t, string headerKey)
        {
            SectionLabel(vehiclesContent, Loc.T(headerKey));
            var grid = GridRow(vehiclesContent, new Vector2(280, 400), 3);
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
                Place(pill.rectTransform, new Vector2(0.5f, 1), new Vector2(0.5f, 1), new Vector2(0, -28), new Vector2(196, 44));
                Label(pill.transform, Loc.T(TierName(set.rarity)), num, Vector2.zero, new Vector2(192, 40), 26, White);
            }

            var tile = Img(card.transform, null, new Color(0.16f, 0.17f, 0.22f)); tile.raycastTarget = false;
            Place(tile.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0, 34), new Vector2(258, 212));

            // live 3D thumbnail — created empty now, FILLED lazily (FillPreviewsLazy) so the panel opens without a hitch.
            // Locked vehicles render GREY (previewTint) so they clearly read as not-yet-collected; owned = full colour.
            // fill < 1 = camera closer = bigger vehicle (cards were too small — pulled the camera in more per type).
            var pv = new GameObject("Preview", typeof(RectTransform)).AddComponent<RawImage>();
            pv.transform.SetParent(tile.transform, false);
            pv.raycastTarget = false; pv.color = new Color(1, 1, 1, 0); // invisible until its texture is ready
            var pr = pv.rectTransform; pr.anchorMin = Vector2.zero; pr.anchorMax = Vector2.one; pr.offsetMin = Vector2.zero; pr.offsetMax = Vector2.zero;
            // Every model is now an in-house .glb normalised to the same length and nose direction, so ONE fill and
            // ONE yaw give all three types an identical pose and apparent size. (Cars needed +180 only as FBX.)
            const float fill = 0.60f; // <1 = camera closer = bigger
            const float yaw  = 35f;
            Color previewTint = owned ? Color.white : new Color(0.40f, 0.40f, 0.46f, 1f); // locked -> greyed out
            pendingPreviews.Add((pv, set.PrefabFor(t), t != VehicleType.Car, fill, yaw, previewTint));

            Label(card.transform, label, num, new Vector2(0, -104), new Vector2(272, 54), 36, White);

            if (owned)
            {
                var b = card.gameObject.AddComponent<Button>(); b.targetGraphic = card;
                string id = set.id; var tt = t;
                b.onClick.AddListener(() => EquipVehicle(tt, id));
                if (equipped)
                {
                    var badge = Img(card.transform, null, new Color(0.20f, 0.72f, 0.32f)); badge.raycastTarget = false;
                    Place(badge.rectTransform, new Vector2(0.5f, 0), new Vector2(0.5f, 0), new Vector2(0, 38), new Vector2(224, 50));
                    Label(badge.transform, Loc.T("EQUIPPED"), num, Vector2.zero, new Vector2(222, 46), 28, White);
                }
            }
            else
            {
                // Locked = not collected. The 3D preview is greyed (previewTint above) + a PADLOCK badge over the tile;
                // a "from chests" tag along the bottom marks it locked without hiding the shape.
                BuildLockBadge(tile.transform, Vector2.zero, 50f);
                var banner = Img(card.transform, null, new Color(0.05f, 0.05f, 0.08f, 0.85f)); banner.raycastTarget = false;
                Place(banner.rectTransform, new Vector2(0.5f, 0), new Vector2(0.5f, 0), new Vector2(0, 38), new Vector2(232, 50));
                Label(banner.transform, Loc.T("From chests"), num, Vector2.zero, new Vector2(226, 44), 24, new Color(0.96f, 0.86f, 0.5f));
            }
        }

        // Car rarity-tier -> badge colour / label (0 Common, 1 Medium, 2 Legendary). Shared with the reveal modal.
        public static Color TierColor(int rarity) =>
            rarity >= 3 ? new Color(0.95f, 0.66f, 0.20f)   // Legendary = gold
          : rarity == 2 ? new Color(0.69f, 0.30f, 0.95f)   // Epic = purple
          : rarity == 1 ? new Color(0.32f, 0.78f, 0.42f)   // Uncommon = green
          :               new Color(0.62f, 0.66f, 0.72f);  // Common = grey
        public static string TierName(int rarity) => rarity >= 3 ? "LEGENDARY" : rarity == 2 ? "EPIC" : rarity == 1 ? "UNCOMMON" : "COMMON";

        // A small code-built PADLOCK on a round dark badge — marks a LOCKED vehicle, centred at `pos` in `parent`,
        // body `w` wide. The gold shackle is a ring (gold disc + a hole matching the badge) whose lower half the body
        // hides, so it reads as an upside-down U. Gold (not steel) because the kit's circle sprite is yellow and only
        // tints warm; a steel tint would come out muddy.
        void BuildLockBadge(Transform parent, Vector2 pos, float w)
        {
            Color gold   = new Color(1f, 0.80f, 0.28f);
            Color goldHi = new Color(1f, 0.88f, 0.44f);
            Color navy   = new Color(0.07f, 0.08f, 0.12f);

            var bg = Img(parent, UIKit.CircleYellow(), White); bg.color = new Color(navy.r, navy.g, navy.b, 0.82f); bg.raycastTarget = false;
            Place(bg.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), pos, new Vector2(w * 2.0f, w * 2.0f));

            // gold shackle ring: outer disc + a navy hole; the body (below) hides the lower half -> upside-down U
            var shackle = Img(parent, UIKit.CircleYellow(), White); shackle.color = gold; shackle.raycastTarget = false;
            Place(shackle.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), pos + new Vector2(0, w * 0.30f), new Vector2(w * 0.66f, w * 0.66f));
            var shackleHole = Img(parent, UIKit.CircleYellow(), White); shackleHole.color = navy; shackleHole.raycastTarget = false;
            Place(shackleHole.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), pos + new Vector2(0, w * 0.33f), new Vector2(w * 0.38f, w * 0.38f));

            // rounded body (covers the shackle's lower half) + a dark keyhole
            var body = Img(parent, UIKit.ShopIconBgA(), White); body.color = goldHi; body.raycastTarget = false;
            Place(body.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), pos + new Vector2(0, -w * 0.16f), new Vector2(w, w * 0.74f));
            var hole = Img(parent, null, new Color(0.30f, 0.20f, 0.08f)); hole.raycastTarget = false;
            Place(hole.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), pos + new Vector2(0, -w * 0.14f), new Vector2(w * 0.16f, w * 0.26f));
        }

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
