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
        void BuildVehicles()
        {
            vehiclesPanel = Panel("Vehicles", new Color(0, 0, 0, 0.62f));
            var cv = vehiclesPanel.AddComponent<Canvas>(); cv.overrideSorting = true; cv.sortingOrder = 80; // above the garage
            vehiclesPanel.AddComponent<GraphicRaycaster>();

            var card = Img(vehiclesPanel.transform, UIKit.PanelTall(), new Color(0.20f, 0.22f, 0.33f));
            Center(card.rectTransform, new Vector2(980, 1560));
            Label(card.transform, Loc.T("VEHICLES"), title, new Vector2(0, 690), new Vector2(760, 120), 70, White);
            RedClose(card.transform, HideVehicles);

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

            RefreshVehicles();
            vehiclesPanel.SetActive(false);
        }

        // ---- (re)populate the three sections + coin counter --------------------------------
        void RefreshVehicles()
        {
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
        }

        // One section: a localized header + a card per DISTINCT model of this type across the sets (dedup by prefab,
        // so the shared Connect/Bus appear once even though every set bundles them).
        void VehicleSectionCards(VehicleType t, string headerKey)
        {
            SectionLabel(vehiclesContent, Loc.T(headerKey));
            var grid = GridRow(vehiclesContent, new Vector2(275, 320), 3);
            var seen = new HashSet<GameObject>();
            foreach (var s in VehicleWardrobe.Catalog.sets)
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
            string label = t == VehicleType.Car ? set.displayName : (t == VehicleType.Minivan ? "Connect" : "Bus");

            var card = Img(parent, UIKit.ShopIconBgA(), White); card.color = new Color(0.22f, 0.24f, 0.31f);

            var tile = Img(card.transform, null, new Color(0.16f, 0.17f, 0.22f)); tile.raycastTarget = false;
            Place(tile.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0, 18), new Vector2(210, 150));
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
                var lk = Img(card.transform, null, new Color(0, 0, 0, 0.58f)); lk.raycastTarget = false;
                Place(lk.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0, 18), new Vector2(214, 154));
                Label(lk.transform, Loc.T("LOCKED"), num, new Vector2(0, 34), new Vector2(200, 40), 22, new Color(0.9f, 0.9f, 0.96f));
                bool afford = SaveSystem.Coins >= set.price;
                var buy = Btn(card.transform, UIKit.PriceBtnA(), afford ? new Color(0.30f, 0.72f, 0.36f) : new Color(0.45f, 0.45f, 0.50f),
                              new Vector2(0.5f, 0), new Vector2(0, 12), new Vector2(232, 64), () => UnlockVehicle(set.id));
                var bc = Img(buy.transform, UIKit.Coin(), Gold); bc.raycastTarget = false;
                Place(bc.rectTransform, new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(20, 0), new Vector2(40, 40));
                Label(buy.transform, set.price.ToString(), num, new Vector2(24, 0), new Vector2(232, 44), 26, White);
            }
        }

        // ---- actions ----------------------------------------------------------------------
        void EquipVehicle(VehicleType t, string setId)
        {
            SaveSystem.SetEquippedSet(t, setId);
            RefreshVehicles();
            OnReskin?.Invoke(); // rebuild the live board so the newly-equipped model shows
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
