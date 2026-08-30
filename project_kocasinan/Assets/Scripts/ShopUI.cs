using UnityEngine;
using UnityEngine.UI;

namespace Ridebury
{
    /// <summary>
    /// THE shop — one hierarchy, one wiring path, used by BOTH entry points:
    /// the main-menu bottom nav (SHOP) and the in-game coin tap.
    ///
    /// The visuals live in ONE asset, the prefab <c>Assets/Resources/UI/ShopPanel.prefab</c>
    /// (bake/refresh it with "Tools ▸ 300Mind UI ▸ Bake Shop Prefab"). Both scenes spawn
    /// that prefab at runtime — edit the prefab and both places change together. Everything
    /// dynamic (real IAP products, localized prices, joker costs, restore, tap handling)
    /// is wired here, so the menu and the game can never drift apart again.
    ///
    /// Hosts (<see cref="GameUI"/> / MenuController) only call <see cref="Ensure"/> once and
    /// then <see cref="Open"/>/<see cref="Close"/>; they hook <see cref="onOpened"/>,
    /// <see cref="onClosed"/> and <see cref="onCoinsChanged"/> for their own chrome.
    /// </summary>
    public class ShopUI : MonoBehaviour
    {
        /// <summary>Resources path of the single shop prefab (Assets/Resources/UI/ShopPanel.prefab).</summary>
        public const string ResourcePath = "UI/ShopPanel";

        // Lazily re-found: a domain reload (editing a script while in Play) clears statics, and
        // the scene's shop then has to be discoverable again without a re-Ensure.
        static ShopUI instance;
        public static ShopUI Instance
        {
            get
            {
                if (instance == null) instance = Object.FindFirstObjectByType<ShopUI>(FindObjectsInactive.Include);
                return instance;
            }
            private set { instance = value; }
        }

        [Tooltip("Dim backdrop + card shown while the shop is open.")]
        public GameObject panel;

        [Tooltip("Sorting order of the shop's own canvas — above every other UI canvas (menu 100, in-game panels 60).")]
        public int sortingOrder = 200;

        // Host hooks: the menu hides its bottom nav, the game hides its HUD chrome, and both
        // repaint their gold counter after a joker purchase.
        [System.NonSerialized] public System.Action onOpened;
        [System.NonSerialized] public System.Action onClosed;
        [System.NonSerialized] public System.Action onCoinsChanged;

        // False when the shop was adopted from a canvas it SHARES with other UI (a legacy
        // menu bake): its sorting order then belongs to that UI and must not be touched.
        [System.NonSerialized] public bool ownsCanvas = true;

        bool wired;

        // The Restore button's own label — reset back to "RESTORE PURCHASES" on every open so a previous
        // "NOTHING TO RESTORE" / "STORE NOT READY" answer doesn't stay on the button forever.
        Text restoreLabel;

        public bool IsOpen => panel != null && panel.activeInHierarchy;

        void Awake() { if (instance == null) instance = this; }

        void OnDestroy()
        {
            IAPManager.OnChanged -= RefreshPrices;
            if (instance == this) instance = null;
        }

        // ---- Creation --------------------------------------------------------

        /// <summary>The scene's shop, spawned from the prefab on first use and wired once.
        /// Returns null only when neither the prefab nor a legacy baked shop exists.</summary>
        public static ShopUI Ensure()
        {
            if (Instance != null) { Instance.Init(); return Instance; }

            var shop = Object.FindFirstObjectByType<ShopUI>(FindObjectsInactive.Include);
            if (shop == null)
            {
                var prefab = Resources.Load<GameObject>(ResourcePath);
                if (prefab != null)
                {
                    HideLegacyShops();                       // pre-unify scene copies must never show alongside it
                    var go = Object.Instantiate(prefab);
                    go.name = prefab.name;
                    CanvasAspectFit.Apply(go);
                    shop = go.GetComponent<ShopUI>();
                }
                else shop = AdoptLegacy();                   // prefab not baked yet -> drive the baked scene shop
            }
            if (shop == null) { Debug.LogWarning("[Shop] no shop prefab (Resources/" + ResourcePath + ") and no baked shop in the scene."); return null; }

            Instance = shop;
            shop.Init();
            return shop;
        }

        // Deactivate shops left over from the old two-shop setup (a scene-baked InGameShop
        // canvas, or a "Panel_Shop"/"Panel_GameShop" under some other canvas). Only the panel
        // objects are touched — never a canvas that also carries unrelated UI.
        static void HideLegacyShops()
        {
            foreach (var m in Object.FindObjectsByType<InGameShop>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                if (m != null && m.GetComponent<ShopUI>() == null) m.gameObject.SetActive(false);
            var t = FindInScene("Panel_Shop");
            if (t != null) t.gameObject.SetActive(false);
        }

        // Legacy path: no prefab yet, so drive whatever shop the scene was baked with. The menu's
        // own "Panel_Shop" wins over a "Panel_GameShop": the menu scene can still hold a stray
        // baked in-game shop canvas from the old two-shop setup, and that one is not the menu's.
        static ShopUI AdoptLegacy()
        {
            var t = FindInScene("Panel_Shop") ?? FindInScene("Panel_GameShop");
            if (t == null) return null;

            var pnl = t.gameObject;
            var canvas = t.GetComponentInParent<Canvas>(true);                   // e.g. the menu's MenuUI_Baked canvas
            var root = canvas != null ? canvas.gameObject : pnl;
            // A canvas SHARED with other UI (the menu bake) — its sorting order isn't ours to set.
            bool shared = root != pnl && root.GetComponent<InGameShop>() == null;

            if (!root.activeSelf) root.SetActive(true);
            var shop = root.GetComponent<ShopUI>();
            if (shop == null) shop = root.AddComponent<ShopUI>();
            shop.panel = pnl;
            shop.ownsCanvas = !shared;
            return shop;
        }

        void Init()
        {
            IAPManager.OnChanged -= RefreshPrices;
            IAPManager.OnChanged += RefreshPrices;   // repaint amounts + localized prices when a purchase resolves
            if (wired) return;
            wired = true;

            if (!gameObject.activeSelf) gameObject.SetActive(true);
            if (panel == null)
            {
                var t = FindDeep(transform, "Panel_GameShop") ?? FindDeep(transform, "Panel_Shop");
                if (t != null) panel = t.gameObject;
            }
            if (panel == null) { Debug.LogWarning("[Shop] shop root has no panel."); return; }

            var canvas = GetComponent<Canvas>();
            if (ownsCanvas && canvas != null) canvas.sortingOrder = sortingOrder; // always on top of the menu / HUD

            Wire();
            panel.SetActive(false);
        }

        // ---- Open / close ----------------------------------------------------

        public void Open()
        {
            if (panel == null) return;
            if (!gameObject.activeSelf) gameObject.SetActive(true);
            panel.SetActive(true);
            panel.transform.SetAsLastSibling();
            if (restoreLabel != null) restoreLabel.text = Loc.T("RESTORE PURCHASES"); // clear last restore's answer
            RefreshPrices();            // IAP is ready by open-time even when it wasn't at Start
            Localizer.LocalizeScene();  // the panel is active now -> its text is found and translated
            onOpened?.Invoke();
        }

        public void Close()
        {
            if (panel != null) panel.SetActive(false);
            onClosed?.Invoke();
        }

        // ---- Wiring ----------------------------------------------------------

        void Wire()
        {
            var root = panel.transform;

            // The dim backdrop closes the shop; every card/row inside it must NOT (see BlockBackgroundTaps).
            var bg = panel.GetComponent<Image>();
            if (bg != null) bg.raycastTarget = true;
            var pbtn = panel.GetComponent<Button>();
            if (pbtn == null)
            {
                pbtn = panel.AddComponent<Button>();
                pbtn.transition = Selectable.Transition.None;
                if (bg != null) pbtn.targetGraphic = bg;
            }
            pbtn.onClick = new Button.ButtonClickedEvent();   // drop any baked persistent listener (e.g. MenuController.CloseAll)
            pbtn.onClick.AddListener(Close);

            // Baker tags. GrantCoins cards are mapped to the real products in MapCoinButtons and
            // joker bars are wired by row name, so only Close needs handling here.
            foreach (var b in root.GetComponentsInChildren<InGameShopButton>(true))
            {
                var btn = b.GetComponent<Button>();
                if (btn == null || b.gameObject == panel) continue;
                if (b.action == InGameShopButton.Act.Close)
                {
                    btn.onClick = new Button.ButtonClickedEvent();
                    btn.onClick.AddListener(Close);
                }
            }

            EnsureCloseButton(root);
            WireJokerBars(root);
            MapCoinButtons(root, true);
            RefreshPromoBars(root);
            AddRestoreRow(root);
            HideExtraCoinCards(root);
            BlockBackgroundTaps(root);
            PolishStore(root);
        }

        // The red ✕. Prefers a button authored in the prefab ("Close" / "ShopClose", name-tolerant);
        // only if none exists is one built at runtime, anchored to the PANEL's top-right (the Card's
        // corner can sit off-screen on tall phones).
        void EnsureCloseButton(Transform root)
        {
            Transform x = null;
            foreach (var t in root.GetComponentsInChildren<Transform>(true))
            {
                if (t == root) continue;
                string n = t.name.Replace(" ", "").Replace("_", "").ToLowerInvariant();
                if (n == "shopclose") { x = t; break; }
                if (n == "close" && x == null) x = t;
            }
            if (x != null)
            {
                var img = x.GetComponent<Image>();
                if (img != null) img.raycastTarget = true;
                var btn = x.GetComponent<Button>();
                if (btn == null) btn = x.gameObject.AddComponent<Button>();
                if (img != null) btn.targetGraphic = img;
                btn.interactable = true;
                btn.onClick = new Button.ButtonClickedEvent();
                btn.onClick.AddListener(Close);
                return;
            }

            var go = new GameObject("ShopCloseBtn_Runtime", typeof(RectTransform), typeof(Image), typeof(Button));
            go.transform.SetParent(panel.transform, false);
            go.transform.SetAsLastSibling();
            var rt = (RectTransform)go.transform;
            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(1f, 1f);
            rt.anchoredPosition = new Vector2(-45f, -90f);
            rt.sizeDelta = new Vector2(96f, 96f);
            var cimg = go.GetComponent<Image>();
            cimg.sprite = UIKit.CloseX(); cimg.color = Color.white; cimg.preserveAspect = true;
            go.GetComponent<Button>().onClick.AddListener(Close);
        }

        // Joker bars: charge the real per-joker price (Recolor 75 / Swap 50 / Heli 100 from
        // GameConfig), GRANT the joker, and relabel the baked flat "100".
        void WireJokerBars(Transform root)
        {
            foreach (var t in root.GetComponentsInChildren<Transform>(true))
            {
                int kind = JokerBarKind(t.name);
                if (kind < 0) continue;
                var buyT = FindDeep(t, "Buy");
                var buy = buyT != null ? buyT.GetComponent<Button>() : null;
                if (buy == null) continue;
                int cost = JokerCost(kind);
                var priceT = FindDeep(buy.transform, "Price");
                var priceTxt = priceT != null ? priceT.GetComponent<Text>() : null;
                if (priceTxt != null) priceTxt.text = cost.ToString();
                buy.onClick = new Button.ButtonClickedEvent();   // drop any baked listener (BuyFor100 / GrantCoins)
                buy.onClick.AddListener(() =>
                {
                    if (!SaveSystem.TrySpend(cost)) return;
                    SaveSystem.AddFreeJoker(kind, 1);
                    onCoinsChanged?.Invoke();
                });
            }
        }

        /// <summary>Map the shop's "Pack_&lt;amount&gt;" cards onto the REAL IAP CoinPacks,
        /// smallest→smallest, so the displayed amount, the price and the product actually
        /// purchased always agree with <see cref="IAPManager"/> — whatever the prefab was baked
        /// with, and with no re-bake. Card layout: "Pack_N" → "Amount" + "Buy" → "Price".
        /// <paramref name="wireClicks"/> adds the purchase listener (once, on setup); false only
        /// refreshes the labels when IAP finishes initialising.</summary>
        public static void MapCoinButtons(Transform shopRoot, bool wireClicks)
        {
            if (shopRoot == null) return;
            var cards = new System.Collections.Generic.List<(int baked, Transform card, Button buy)>();
            foreach (var t in shopRoot.GetComponentsInChildren<Transform>(true))
            {
                if (t == null || !t.name.StartsWith("Pack_")) continue;
                if (!int.TryParse(t.name.Substring(5), out int baked)) continue;
                var buyT = t.Find("Buy");
                var buy = buyT != null ? buyT.GetComponent<Button>() : null;
                if (buy != null) cards.Add((baked, t, buy));
            }
            if (cards.Count == 0) return;
            cards.Sort((a, b) => a.baked.CompareTo(b.baked));
            var packs = new System.Collections.Generic.List<(string id, int coins)>(IAPManager.CoinPacks);
            packs.Sort((a, b) => a.coins.CompareTo(b.coins));
            for (int i = 0; i < cards.Count && i < packs.Count; i++)
            {
                var card = cards[i].card; var buy = cards[i].buy; var pack = packs[i];
                var amtT = card.Find("Amount");
                if (amtT != null) { var lt = amtT.GetComponent<Text>(); if (lt != null) lt.text = pack.coins.ToString(); }
                var priceTf = buy.transform.Find("Price");
                if (priceTf != null)
                {
                    var lt = priceTf.GetComponent<Text>();
                    string pr = null;
#if !UNITY_EDITOR
                    pr = IAPManager.Instance != null ? IAPManager.Instance.Price(pack.id) : null; // real localized store price (device only)
#endif
                    // The editor uses Unity's FAKE store ("$0.01" for everything) — show the fixed placeholder instead.
                    if (string.IsNullOrEmpty(pr) && i < FallbackPrices.Length) pr = FallbackPrices[i];
                    if (lt != null && !string.IsNullOrEmpty(pr)) lt.text = pr;
                }
                if (wireClicks) { var id = pack.id; buy.onClick.AddListener(() => IAPManager.Instance?.Buy(id)); }
            }
        }

        // Placeholder prices until the REAL localized store price loads (editor / before IAP
        // initialises / before the products go Active). Index = CoinPacks sorted ascending.
        static readonly string[] FallbackPrices = { "$0.99", "$1.99", "$4.99", "$8.99", "$12.99", "$17.99" };

        // A prefab baked with more coin cards than there are products (or with duplicates) would
        // leave dead buttons on screen — hide the extras instead of shipping cards nobody can buy.
        static void HideExtraCoinCards(Transform root)
        {
            var seen = new System.Collections.Generic.List<int>();
            var cards = new System.Collections.Generic.List<(int coins, Transform card)>();
            foreach (var t in root.GetComponentsInChildren<Transform>(true))
            {
                if (t == null || !t.name.StartsWith("Pack_")) continue;
                if (!int.TryParse(t.name.Substring(5), out int coins)) continue;
                cards.Add((coins, t));
            }
            cards.Sort((a, b) => a.coins.CompareTo(b.coins));
            for (int i = 0; i < cards.Count; i++)
            {
                bool show = i < IAPManager.CoinPacks.Length && !seen.Contains(cards[i].coins);
                if (show) seen.Add(cards[i].coins);
                if (cards[i].card.gameObject.activeSelf != show) cards[i].card.gameObject.SetActive(show);
            }
        }

        // ---- Promo bars (no-ads / no-banner) ---------------------------------

        void RefreshPromoBars(Transform root)
        {
            WirePromoBar(root, "RemoveAds", IAPManager.RemoveAds, "$9.99", () => IAPManager.Instance?.Buy(IAPManager.RemoveAds));
            WirePromoBar(root, "RemoveAds (1)", IAPManager.RemoveAdsPlus, "$12.99", () => IAPManager.Instance?.Buy(IAPManager.RemoveAdsPlus));
            WirePromoBar(root, "RemoveBanner", IAPManager.RemoveBanner, "$0.99", () => IAPManager.Instance?.Buy(IAPManager.RemoveBanner));
        }

        // Wire a promo row to a real purchase, but make ONLY the green price button ("PriceBg")
        // buy. The orange bar background stays a plain tap-blocker: tapping it must not purchase
        // and must not fall through to the close-backdrop behind the shop.
        //
        // ONE-TIME OFFERS: the three no-ads tiers are non-consumables. Once one is owned the row is
        // LOCKED — no click listener, a dead button and an "OWNED" label instead of a price — so it
        // can never be bought twice. Re-evaluated on every RefreshPrices (every open + every
        // IAPManager.OnChanged), so the row locks itself the moment the purchase resolves.
        static void WirePromoBar(Transform shopRoot, string rowName, string productId, string fallbackPrice, System.Action onBuy)
        {
            var row = FindDeep(shopRoot, rowName);
            if (row == null) return;

            bool owned = IAPManager.Owned(productId);

            var rowImg = row.GetComponent<Image>();
            if (rowImg != null) rowImg.raycastTarget = true;
            var rowBtn = row.GetComponent<Button>();
            if (rowBtn != null) rowBtn.onClick = new Button.ButtonClickedEvent();   // the whole bar must never buy

            var price = FindDeep(row, "PriceBg");
            var target = price != null ? price : row;                              // fallback: keep buying working
            var pImg = target.GetComponent<Image>();
            if (pImg != null) pImg.raycastTarget = true;
            var pBtn = target.GetComponent<Button>();
            if (pBtn == null) pBtn = target.gameObject.AddComponent<Button>();
            if (pImg != null) pBtn.targetGraphic = pImg;
            pBtn.onClick = new Button.ButtonClickedEvent();
            if (!owned) pBtn.onClick.AddListener(() => onBuy());
            pBtn.interactable = !owned;

            // Dim the whole row while it's owned, so "already yours" reads at a glance. A CanvasGroup
            // is used (not tinting the sprites) so nothing about the authored art has to be restored.
            var cg = row.GetComponent<CanvasGroup>();
            if (cg == null && owned) cg = row.gameObject.AddComponent<CanvasGroup>();
            if (cg != null) cg.alpha = owned ? 0.55f : 1f;

            // Real localized store price on the row's "Price" label (Text or TMP), with the
            // placeholder as a fallback so the label is never blank.
            string real = null;
#if !UNITY_EDITOR
            real = IAPManager.Instance != null ? IAPManager.Instance.Price(productId) : null;
#endif
            string shown = owned ? Loc.T("OWNED") : (string.IsNullOrEmpty(real) ? fallbackPrice : real);
            if (string.IsNullOrEmpty(shown)) return;
            var pl = FindDeep(row, "Price") ?? price;
            if (pl == null) return;
            var t = pl.GetComponentInChildren<Text>(true);
            if (t != null) t.text = shown;
            var tmp = pl.GetComponentInChildren<TMPro.TMP_Text>(true);
            if (tmp != null) tmp.text = shown;
        }

        /// <summary>Repaint amounts + localized prices (IAPManager.OnChanged, and on every open).</summary>
        public void RefreshPrices()
        {
            if (panel == null) return;
            MapCoinButtons(panel.transform, false);
            RefreshPromoBars(panel.transform);
        }

        // ---- Restore Purchases row -------------------------------------------

        // Storefront requirement (Google Play + App Store): a Restore entry point. Appended to
        // the bottom of the shop's scroll list, and the scroll view is extended DOWN to fit it.
        void AddRestoreRow(Transform root)
        {
            var authored = FindDeep(root, "RestorePurchases");
            if (authored != null)
            {
                var authoredButton = authored.GetComponent<Button>();
                if (authoredButton == null) authoredButton = authored.gameObject.AddComponent<Button>();
                var authoredImage = authored.GetComponent<Image>();
                if (authoredImage != null) authoredButton.targetGraphic = authoredImage;
                var authoredLabel = authored.GetComponentInChildren<Text>(true);
                restoreLabel = authoredLabel;
                authoredButton.onClick = new Button.ButtonClickedEvent();
                authoredButton.onClick.AddListener(() =>
                {
                    // Report the REAL outcome: claiming "RESTORED" when the store never answered (or when
                    // the account owns nothing) is exactly the behaviour store review flags.
                    if (IAPManager.Instance == null)
                    {
                        if (authoredLabel != null) authoredLabel.text = Loc.T("STORE NOT READY");
                        return;
                    }
                    IAPManager.Instance.Restore(r =>
                    {
                        if (authoredLabel != null) authoredLabel.text = IAPManager.RestoreLabel(r);
                    });
                });
                return;
            }

            var scroll = root.GetComponentInChildren<ScrollRect>(true);
            var content = scroll != null && scroll.content != null ? scroll.content : (RectTransform)root;

            var rowGo = new GameObject("RestorePurchases", typeof(RectTransform));
            rowGo.transform.SetParent(content, false);
            var row = rowGo.AddComponent<Image>();
            row.sprite = UIKit.ShopBoxA(); row.color = new Color(0.30f, 0.55f, 0.85f);
            var le = rowGo.AddComponent<LayoutElement>(); le.preferredHeight = 120; le.minHeight = 120;

            var lblGo = new GameObject("Label", typeof(RectTransform));
            lblGo.transform.SetParent(rowGo.transform, false);
            var lbl = lblGo.AddComponent<Text>();
            lbl.font = UIKit.Title(); lbl.text = Loc.T("RESTORE PURCHASES"); lbl.fontSize = 36;
            lbl.color = new Color(0.12f, 0.09f, 0.06f, 1f); lbl.alignment = TextAnchor.MiddleCenter; lbl.raycastTarget = false;
            lbl.horizontalOverflow = HorizontalWrapMode.Overflow; lbl.verticalOverflow = VerticalWrapMode.Overflow;
            var lrt = lbl.rectTransform;
            lrt.anchorMin = lrt.anchorMax = lrt.pivot = new Vector2(0.5f, 0.5f);
            lrt.anchoredPosition = Vector2.zero; lrt.sizeDelta = new Vector2(640, 70);

            restoreLabel = lbl;
            var btn = rowGo.AddComponent<Button>(); btn.targetGraphic = row;
            btn.onClick.AddListener(() =>
            {
                if (IAPManager.Instance == null) { lbl.text = Loc.T("STORE NOT READY"); return; }
                IAPManager.Instance.Restore(r => lbl.text = IAPManager.RestoreLabel(r));
            });

            if (scroll != null)   // make room: extend the scroll view's BOTTOM edge down (away from the title)
            {
                var srt = (RectTransform)scroll.transform;
                srt.offsetMin = new Vector2(srt.offsetMin.x, srt.offsetMin.y - 90f);
            }
        }

        // ---- Tap handling ----------------------------------------------------

        // Only the empty black backdrop (and the red ✕) close the shop. Make every background /
        // card / row image catch taps so tapping a package can't fall through to the backdrop.
        // A button's own graphic — and an icon/label parented DIRECTLY under a button — is left
        // alone so its taps still reach the button.
        void BlockBackgroundTaps(Transform shopRoot)
        {
            foreach (var img in shopRoot.GetComponentsInChildren<Image>(true))
            {
                if (img.transform == shopRoot) continue;                        // the backdrop itself closes the shop
                var p = img.transform.parent;
                if (p != null && p != shopRoot && p.GetComponent<Button>() != null && img.GetComponent<Button>() == null)
                    continue;
                img.raycastTarget = true;
            }

            // The backdrop is an ANCESTOR of every card AND is the tap-to-close Button: a click on
            // a card with no handler BUBBLES UP to it (raycastTarget on the card does not stop the
            // bubble). Put a no-op click consumer on each scroll Viewport and on the Card so taps
            // inside the shop are swallowed there. Drags still scroll (a separate event path).
            foreach (var sr in shopRoot.GetComponentsInChildren<ScrollRect>(true))
            {
                var vp = sr.viewport != null ? sr.viewport : sr.transform.Find("Viewport") as RectTransform;
                if (vp != null) AddClickConsumer(vp.gameObject);
            }
            // The card, whatever it is called in the prefab: every DIRECT child of the backdrop.
            // (Looking only for a child literally named "Card" missed hand-renamed cards, and a tap
            // on the card's margin — around the title or beside the list — then closed the shop.)
            for (int i = 0; i < shopRoot.childCount; i++)
                AddClickConsumer(shopRoot.GetChild(i).gameObject);
        }

        // A Button with no onClick listeners consumes the click and does nothing else.
        static void AddClickConsumer(GameObject go)
        {
            if (go == null || go.GetComponent<Selectable>() != null) return;
            var g = go.GetComponent<Graphic>();
            if (g == null) { var img = go.AddComponent<Image>(); img.color = new Color(1f, 1f, 1f, 0.004f); g = img; }
            g.raycastTarget = true;
            var btn = go.AddComponent<Button>();
            btn.transition = Selectable.Transition.None;
            btn.targetGraphic = g;
        }

        // ---- Small helpers ---------------------------------------------------

        /// <summary>First descendant (inactive included) named <paramref name="name"/>, else null.</summary>
        // Two presentation fixes applied to the AUTHORED store card on adoption, so they survive a
        // re-bake of the prefab without anyone repeating them by hand. Both are idempotent.
        static void PolishStore(Transform root)
        {
            // The new cards and price plates are cream/cyan. A single dark ink colour keeps every
            // authored and runtime-localized label readable, including prices and section headers.
            Color ink = new Color(0.12f, 0.09f, 0.06f, 1f);
            foreach (var label in root.GetComponentsInChildren<Text>(true))
                label.color = ink;
            foreach (var label in root.GetComponentsInChildren<TMPro.TMP_Text>(true))
                label.color = ink;

            // The title sat on a red slab. Drop the slab, keep the word.
            var band = FindDeep(root, "TitleBand");
            if (band != null)
            {
                var bandImage = band.GetComponent<Image>();
                if (bandImage != null) bandImage.enabled = false;
            }

            // The rows read as oversized cards. Scale each one down a notch — and shrink its
            // LayoutElement by the same factor, because THAT is what reserves the vertical space
            // (scaling alone would leave the old gaps behind).
            var content = FindDeep(root, "Content");
            if (content == null) return;
            const float k = 0.86f;
            foreach (RectTransform row in content)
            {
                if (row == null || Mathf.Abs(row.localScale.x - 1f) > 0.001f) continue; // already scaled
                row.localScale = new Vector3(k, k, 1f);
                var le = row.GetComponent<LayoutElement>();
                if (le == null) continue;
                if (le.preferredHeight > 0f) le.preferredHeight *= k;
                if (le.minHeight > 0f) le.minHeight *= k;
            }
        }

        public static Transform FindDeep(Transform root, string name)
        {
            if (root == null) return null;
            foreach (var t in root.GetComponentsInChildren<Transform>(true))
                if (t.name == name) return t;
            return null;
        }

        // Same search across every root object of the active scene (inactive included).
        static Transform FindInScene(string name)
        {
            var scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
            foreach (var go in scene.GetRootGameObjects())
            {
                var t = FindDeep(go.transform, name);
                if (t != null) return t;
            }
            return null;
        }

        /// <summary>A shop joker bar → its joker kind ("Bar_Shuffle"=Recolor 0, "Bar_Swap"=Swap 1, "Bar_Heli"=Heli 2).</summary>
        public static int JokerBarKind(string name)
        {
            if (string.IsNullOrEmpty(name) || !name.StartsWith("Bar_")) return -1;
            var n = name.ToLowerInvariant();
            if (n.Contains("heli")) return 2;
            if (n.Contains("swap")) return 1;
            if (n.Contains("shuffle") || n.Contains("recolor")) return 0;
            return -1;
        }

        /// <summary>Per-joker shop price from GameConfig — the same source as the HUD joker buy panel.</summary>
        public static int JokerCost(int kind) => kind == 1 ? GameConfig.SwapCost : kind == 2 ? GameConfig.HeliCost : GameConfig.RecolorCost;
    }
}
