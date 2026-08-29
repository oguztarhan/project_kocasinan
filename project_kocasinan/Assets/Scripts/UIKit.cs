using System.Collections.Generic;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace Ridebury
{
    /// <summary>
    /// Loader + semantic map for the "300Mind / 2D Game UI Kit" sprite atlases and
    /// fonts. The atlases are multi-sprite (sliced) PNGs named UI-pack_Sprite_1/2;
    /// sub-sprites are loaded by name via AssetDatabase in the editor (cached). For a
    /// standalone build, copy the used sprites/fonts into Assets/Resources/UIKit/ and
    /// the Resources fallback below picks them up. A missing sprite logs one warning
    /// and returns null so callers draw a solid-color fallback instead of throwing.
    ///
    /// The semantic accessors (Coin, Gem, NavHome, BtnGreen, JokerSwap, …) were mapped
    /// by cropping each atlas sub-sprite and identifying it visually.
    /// </summary>
    public static class UIKit
    {
        const string A1 = "Assets/300Mind/2D Game UI Kit/Sprites/UI-pack_Sprite_1.png";
        const string A2 = "Assets/300Mind/2D Game UI Kit/Sprites/UI-pack_Sprite_2.png";
        const string FontDir = "Assets/300Mind/2D Game UI Kit/Fonts/";

        static readonly Dictionary<string, Sprite> _cache = new Dictionary<string, Sprite>();
        static Font _title, _num;

        // Build-safe sprite source: in a player build AssetDatabase is compiled out, so sprites come from this baked
        // ScriptableObject (Resources/UIKitAtlas.asset, produced by "Ridebury ▸ Bake UIKit Resources"). Loaded once.
        static UIKitAtlas _atlas; static bool _atlasTried;
        static UIKitAtlas Atlas
        {
            get { if (!_atlasTried) { _atlasTried = true; _atlas = Resources.Load<UIKitAtlas>("UIKitAtlas"); } return _atlas; }
        }

        // Raw atlas access by index.
        public static Sprite A(int i) => Get(A1, "UI-pack_Sprite_1_" + i);
        public static Sprite B(int i) => Get(A2, "UI-pack_Sprite_2_" + i);

        static Sprite Get(string atlasPath, string name)
        {
            if (_cache.TryGetValue(name, out var cached)) return cached;
            Sprite found = null;
#if UNITY_EDITOR
            foreach (var o in AssetDatabase.LoadAllAssetsAtPath(atlasPath))
                if (o is Sprite sp && sp.name == name) { found = sp; break; }
#endif
            if (found == null && Atlas != null) found = Atlas.Find(name);       // build-safe (baked registry)
            if (found == null) found = Resources.Load<Sprite>("UIKit/" + name); // legacy per-file fallback
            if (found == null) Debug.LogWarning($"[UIKit] sprite not found: {name}");
            _cache[name] = found;
            return found;
        }

        // ---- Fonts ----
        // Whole game uses one font now (Matcha Cih, via GameFont). Title()/Num() both return it so baked AND
        // procedural UI render in Matcha from the first frame — GlobalFontApplier also enforces this at runtime.
        public static Font Title() { if (_title == null) _title = GameFont.UGUI; return _title; }
        public static Font Num()   { if (_num == null)   _num   = GameFont.UGUI; return _num; }

        static Font LoadFont(string n)
        {
            Font f = null;
#if UNITY_EDITOR
            f = AssetDatabase.LoadAssetAtPath<Font>(FontDir + n + ".ttf");
#endif
            if (f == null) f = Resources.Load<Font>("UIKit/" + n);
            if (f == null) f = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            return f;
        }

        // ---- Semantic map (verified WITH the user against the atlas) ----
        // Nav icons + their backing:
        public static Sprite NavShop()    => NewUI("navigasyon-magaza", Res("nav_shop", A(0)));
        public static Sprite NavHome()    => NewUI("navigasyon-home", Res("nav_home", A(2)));
        public static Sprite NavDaily()   => NewUI("navigasyon-daily-reward", Res("nav_daily", A(3)));
        public static Sprite Gear()       => NewUI("ayarlar", Res("icon_gear", A(4)));
        public static Sprite NavBtnBg()   => A(15);  // ORANGE backing: behind the SELECTED nav icon
        public static Sprite NavBtnOff()  => A(14);  // BLUE backing: behind unselected nav icons
        public static Sprite NavStrip()   => A(35);  // bottom blue nav strip
        public static Sprite CheckMark()  => A(5);   // claimed check (daily)

        // Top bar / counters:
        public static Sprite CoinBar()    => A(20);  // gold counter bar (menu + in-game)
        public static Sprite Coin()       => Res("icon_coin", A(16));  // coin icon
        public static Sprite PlusGreen()  => A(17);  // green "+" on the counter
        public static Sprite SliderTrack()=> A(9);   // draggable on/off slider track
        public static Sprite CircleGreen()=> A(18);  // round green badge (people-left)
        public static Sprite CircleYellow()=> A(19); // round yellow badge (level)
        public static Sprite Gem()        => Res("icon_gem", A(22));   // gem / shard

        // Home:
        public static Sprite PlayBtn()    => NewUI("ana-aksiyon-butonu-buyuk-turuncu", Res("btn_play", A(21)));

        // Shop:
        public static Sprite ShopCoinA()  => CoinPack(1);  // coin-pack icons, smallest -> biggest
        public static Sprite ShopCoinB()  => CoinPack(2);
        public static Sprite ShopCoinC()  => CoinPack(3);
        public static Sprite ShopGold()   => CoinPack(4);
        public static Sprite CoinPackSmall() => CoinPack(5);
        public static Sprite CoinPackBig()   => CoinPack(6); // most expensive
        public static Sprite QtyPlus()    => A(23);  // buy-quantity +
        public static Sprite QtyMinus()   => A(32);  // buy-quantity -
        public static Sprite PriceBtnA()  => NewUI("magaza-fiyat-butonu", Res("btn_action", A(36)));
        public static Sprite PriceBtnB()  => NewUI("ana-aksiyon-butonu-buyuk-turuncu", Res("btn_orange", A(37)));
        public static Sprite ShopBoxA()   => A(44);  // shop item card backgrounds
        public static Sprite ShopBoxB()   => A(55);
        public static Sprite ShopIconBgA()=> A(56);  // backing behind shop coin icons
        public static Sprite ShopIconBgB()=> A(57);
        public static Sprite AdReward()   => Res("icon_watch_ad", A(27));  // "watch ad for gold" icon
        public static Sprite NoAds()      => A(39);

        // Titles / panels:
        public static Sprite TitleBarA()  => A(45);
        public static Sprite TitleBarB()  => A(50);
        public static Sprite TitleBarC()  => A(53);

        // Daily rewards:
        public static Sprite DailyCoin()  => A(38);
        public static Sprite DailyIconA() => A(58);
        public static Sprite DailyIconB() => A(59);
        public static Sprite CardCream()  => NewUI("daily-reward-kart-normal", Res("card_daily", A(66)));

        public static Sprite WatchAd()    => A(61);  // video-ad button
        public static Sprite CloseX()     => NewUI("navigasyon-kapat", Res("icon_close", A(79)));
        public static Sprite Back()       => A(80);
        public static Sprite IconSound()  => NewUI("ses-butonu", Res("icon_sound", A(71)));
        public static Sprite IconMusic()  => NewUI("muzik-butonu", Res("icon_music", A(73)));

        // Crisp custom audio icons (external PNGs in Assets/MenuManager/Icons, not in the atlas).
        public static Sprite IconSpeaker() => Res("icon_sound", GetExternal("Assets/MenuManager/Icons/Icon_Sound.png", "Icon_Sound"));
        public static Sprite IconNote()    => Res("icon_music", GetExternal("Assets/MenuManager/Icons/Icon_Music.png", "Icon_Music"));

        // ---- The CUT UI kit (Assets/kesilmis-ikonlar/*.png) -----------------------------
        // Hand-cut art that REPLACES the 300Mind atlas piece by piece. ONE copy on disk: the same
        // folder the editor bakers (GameShopBaker / GarageVisualPolisher / StorefrontMenuPolisher)
        // already pull from, so authored prefabs and code-built UI can never drift apart.
        //
        // It is NOT under Resources, so a player build reads it back out of the baked registry
        // (Resources/UIKitAtlas.asset — "Ridebury ▸ Bake UIKit Resources" writes both the atlas
        // sub-sprites and this folder into it). Every accessor passes the old atlas sprite as
        // `fallback`: a missing or renamed PNG degrades to the previous look instead of nothing.
        //
        // These are the DEFAULTS, deliberately: the Inspector overrides on InGameGarage still win
        // where they are set, but nothing has to be assigned by hand for the new art to show up.
        const string NewKitDir = "Assets/kesilmis-ikonlar/";
        const string NewUIDir = "Assets/Resources/UI/NewDesigns/";

        // Final UI art supplied as individual sprites. Keeping these in Resources makes the same
        // semantic mapping work in both the editor and an Android player without a separate bake.
        static Sprite NewUI(string file, Sprite fallback = null)
        {
            string key = "newui:" + file;
            if (_cache.TryGetValue(key, out var cached)) return cached != null ? cached : fallback;
            Sprite found = null;
#if UNITY_EDITOR
            found = AssetDatabase.LoadAssetAtPath<Sprite>(NewUIDir + file + ".png");
#endif
            if (found == null) found = Resources.Load<Sprite>("UI/NewDesigns/" + file);
            if (found == null) Debug.LogWarning($"[UIKit] new UI sprite not found: {file}");
            _cache[key] = found;
            return found != null ? found : fallback;
        }

        public static Sprite MainPanel()       => NewUI("trafik-temali-ana-panel-v1", PanelTall());
        public static Sprite LevelBadgeNew()   => NewUI("trafik-temali-kare-level-gostergesi-v2", CircleYellow());
        public static Sprite GoldCounterNew()  => NewUI("kaynak-gostergesi-altin", CoinBar());
        public static Sprite GemCounterNew()   => NewUI("kaynak-gostergesi-elmas-pembe", BtnCream());
        public static Sprite LockedState()     => NewUI("durum-kilitli", BtnGrey());
        public static Sprite PassiveState()    => NewUI("durum-pasif", BtnGrey());
        public static Sprite AdUnlockState()   => NewUI("durum-reklamla-ac", BtnGrey());
        public static Sprite NoticeCount()     => NewUI("bildirim-sayi-rozeti", CircleGreen());
        public static Sprite NoticeAlert()     => NewUI("bildirim-unlem", CircleYellow());
        public static Sprite NoticeTimer()     => NewUI("bildirim-zamanlayici", CircleYellow());
        public static Sprite NoticeReward()    => NewUI("bildirim-ucretsiz-odul", AdReward());
        public static Sprite DailyGift()       => NewUI("daily-reward", DailyIconA());
        public static Sprite DailyCardNormal() => NewUI("daily-reward-kart-normal", CardDay());
        public static Sprite DailyCardChosen() => NewUI("daily-reward-kart-secili", DailyCardNormal());
        public static Sprite DailyCardDone()   => NewUI("daily-reward-kart-tamamlandi", DailyCardNormal());
        public static Sprite DailyClaim()      => NewUI("daily-reward-al-butonu", BtnAction());
        public static Sprite ShopCardSingle()  => NewUI("magaza-tek-urun-karti", BarCream());
        public static Sprite ShopCardDouble()  => NewUI("magaza-iki-kutulu-yatay-kart", BarCream());
        public static Sprite ShopCardOffer()   => NewUI("magaza-ozel-teklif-karti", BarCream());
        public static Sprite ShopPrice()       => NewUI("magaza-fiyat-butonu", BtnAction());
        public static Sprite ChestLocked()     => NewUI("sandik-durum-kilitli", LockedState());

        static Sprite Res(string file, Sprite fallback)
        {
            string key = "res:" + file;
            if (_cache.TryGetValue(key, out var cached)) return cached != null ? cached : fallback;
            Sprite found = null;
#if UNITY_EDITOR
            found = AssetDatabase.LoadAssetAtPath<Sprite>(NewKitDir + file + ".png");
#endif
            if (found == null && Atlas != null) found = Atlas.Find(file);        // build-safe (baked registry)
            if (found == null) found = Resources.Load<Sprite>("UIKit/" + file);  // legacy per-file fallback
            if (found == null) Debug.LogWarning($"[UIKit] cut-kit sprite not found: {file} (using the atlas fallback)");
            _cache[key] = found;
            return found != null ? found : fallback;
        }

        // Surfaces. All 9-sliced — draw them through GameUI.Sliced() so the border scales to the rect.
        public static Sprite BarCream()  => Res("bar_cream",  A(44));  // cream counter / row bar
        public static Sprite BarRed()    => Res("bar_red",    A(45));  // red section-header ribbon
        public static Sprite CardDaily() => Res("card_daily", B(2));   // deep-blue card, orange frame (pop-ups)
        public static Sprite CardDay()   => NewUI("daily-reward-kart-normal", Res("sade-daily-reward-karti", CardDaily()));
        public static Sprite BtnGrey()   => NewUI("ana-aksiyon-butonu-kompakt-gri", Res("gri-joker-butonu", A(25)));
        public static Sprite BtnAction() => NewUI("ana-aksiyon-butonu-buyuk-turuncu", Res("btn_action", B(9)));
        public static Sprite BtnPill()   => NewUI("ana-aksiyon-butonu-buyuk-turuncu", Res("btn_orange", B(9)));
        public static Sprite BtnCream()  => NewUI("ana-aksiyon-butonu-orta-krem", Res("btn_cream", B(9)));
        public static Sprite RowGold()   => Res("panel_row_gold",  A(44));
        public static Sprite RowCream()  => Res("panel_row_cream", A(44));

        // 9-slice `img` for the rect it will occupy. The cut kit is authored ~1024px wide, so its
        // borders dwarf a 90px-tall chip and a raw Sliced image renders as mush; pixelsPerUnitMultiplier
        // shrinks the border until a border pair takes at most 80% of `approxSize`. No border authored
        // (every icon) -> left alone, still Simple.
        public static void Slice(UnityEngine.UI.Image img, Vector2 approxSize)
        {
            if (img == null || img.sprite == null) return;
            var b = img.sprite.border;
            if (b == Vector4.zero) return;
            img.type = UnityEngine.UI.Image.Type.Sliced;
            float mul = 1f, hb = b.x + b.z, vb = b.y + b.w;
            if (approxSize.x > 1f && hb > approxSize.x * 0.8f) mul = Mathf.Max(mul, hb / (approxSize.x * 0.8f));
            if (approxSize.y > 1f && vb > approxSize.y * 0.8f) mul = Mathf.Max(mul, vb / (approxSize.y * 0.8f));
            img.pixelsPerUnitMultiplier = mul;
        }

        // Coin piles 1..6 (1 = smallest, 6 = vault).
        public static Sprite CoinPack(int i) => Res("coinpack_" + Mathf.Clamp(i, 1, 6), A(11));

        // Drawn treasure chests, one per tier. Null tier -> Bronze. Callers that get null fall back to
        // the code-built chest (UIKit.BuildChest), so this is safe before the PNGs are imported.
        public static Sprite Chest(string tier)
        {
            switch (tier)
            {
                case "Silver":    return NewUI("sandik-gumus-yeni", Res("chest_silver", null));
                case "Gold":      return NewUI("sandik-altin-yeni", Res("chest_gold", null));
                case "Legendary": return NewUI("sandik-efsanevi-yeni", Res("chest_legendary", null));
                default:          return NewUI("sandik-bronz-yeni", Res("chest_bronze", null));
            }
        }

        static Sprite GetExternal(string path, string resName)
        {
            if (_cache.TryGetValue(resName, out var cached)) return cached;
            Sprite found = null;
#if UNITY_EDITOR
            found = AssetDatabase.LoadAssetAtPath<Sprite>(path);
#endif
            if (found == null && Atlas != null) found = Atlas.Find(resName);     // build-safe (baked registry)
            if (found == null) found = Resources.Load<Sprite>("UIKit/" + resName);
            if (found == null) Debug.LogWarning($"[UIKit] sprite not found: {resName}");
            _cache[resName] = found;
            return found;
        }

        // ---- Atlas 2 ----
        public static Sprite EmptyBoxBlue() => B(0);
        public static Sprite PanelTall()    => B(2);   // big popup background
        public static Sprite PanelCyan()    => B(4);   // light popup background
        public static Sprite BtnOrange()    => NewUI("ana-aksiyon-butonu-buyuk-turuncu", Res("btn_orange", B(9)));
        public static Sprite BtnDark()      => B(10);
        public static Sprite BtnGreen()     => B(16);
        public static Sprite BtnRed()       => B(17);
        public static Sprite JokerRecolor() => NewUI("joker-butonu-recolor", Res("joker_recolor", B(8)));
        public static Sprite JokerSwap()    => NewUI("joker-butonu-shuffle", Res("joker_shuffle", B(15)));
        public static Sprite JokerHeli()    => NewUI("joker-butonu-helikopter", B(14));
        public static Sprite JokerShield()  => B(13);
        public static Sprite JokerDestroy() => B(7);

        // ---- Code-built treasure chest -------------------------------------------------
        // Shared by the garage chest cards, the chest-reveal popup and the daily-reward
        // cards, so a "Bronze chest" looks the same everywhere. The BODY is the same wood
        // on every tier; only the ropes + lock carry the tier colour.
        public static Color ChestTint(string tier)
        {
            switch (tier)
            {
                case "Silver":    return new Color(0.82f, 0.84f, 0.88f); // bright silver
                case "Gold":      return new Color(0.98f, 0.80f, 0.28f); // gold
                case "Legendary": return new Color(1.00f, 0.45f, 0.08f); // flaming orange
                default:          return new Color(0.74f, 0.45f, 0.17f); // Bronze — copper-brown
            }
        }

        // A CUTE chest — chunky ROUNDED body + dome lid, tier-coloured straps, a big round
        // lock with a keyhole, two little feet and a shine. Centred in `parent`, scaled by
        // width `w`. Uses rounded kit sprites so nothing is a bare rectangle.
        public static void BuildChest(Transform parent, Color tint, float w)
        {
            const float V = 0.5f, V2 = 0.5f; // anchor shorthand (centre)
            Color bodyCol = new Color(0.38f, 0.31f, 0.24f); // DARK wood on every tier so the coloured ropes read clearly
            Color lidCol  = new Color(0.30f, 0.24f, 0.19f); // darker
            Color footCol = new Color(0.23f, 0.18f, 0.14f); // darkest
            Color lockCol = Color.Lerp(tint, Color.white, 0.28f); // the tier colour, lightened for a metallic lock

            // two little rounded feet at the bottom
            for (int s = -1; s <= 1; s += 2)
            {
                var foot = ChestImg(parent, ShopIconBgA(), Color.white); foot.color = footCol;
                ChestPlace(foot.rectTransform, new Vector2(s * w * 0.27f, -w * 0.42f), new Vector2(w * 0.24f, w * 0.18f));
            }
            // dome lid (rounded), poking up above the body
            var lid = ChestImg(parent, ShopIconBgA(), Color.white); lid.color = lidCol;
            ChestPlace(lid.rectTransform, new Vector2(0, w * 0.20f), new Vector2(w * 1.00f, w * 0.40f));
            // rounded body — drawn after the lid so its front covers the lid's lower edge (reads as lid-on-box)
            var body = ChestImg(parent, ShopIconBgA(), Color.white); body.color = bodyCol;
            ChestPlace(body.rectTransform, new Vector2(0, -w * 0.12f), new Vector2(w * 0.92f, w * 0.56f));
            // the ROPES — horizontal rim at the lid/body seam + a vertical strap down the front — carry the TIER colour
            var rim = ChestImg(parent, null, tint);
            ChestPlace(rim.rectTransform, new Vector2(0, w * 0.04f), new Vector2(w * 1.00f, w * 0.13f));
            var strap = ChestImg(parent, null, tint);
            ChestPlace(strap.rectTransform, new Vector2(0, -w * 0.14f), new Vector2(w * 0.16f, w * 0.44f));
            // big round lock (tier colour) + dark keyhole
            var lok = ChestImg(parent, CircleYellow(), Color.white); lok.color = lockCol;
            ChestPlace(lok.rectTransform, new Vector2(0, w * 0.02f), new Vector2(w * 0.26f, w * 0.26f));
            var hole = ChestImg(parent, null, new Color(0.30f, 0.20f, 0.10f));
            ChestPlace(hole.rectTransform, new Vector2(0, w * 0.02f), new Vector2(w * 0.07f, w * 0.11f));
            // soft shine on the lid
            var shine = ChestImg(parent, null, new Color(1f, 1f, 1f, 0.35f));
            ChestPlace(shine.rectTransform, new Vector2(-w * 0.24f, w * 0.28f), new Vector2(w * 0.22f, w * 0.09f));
        }

        static UnityEngine.UI.Image ChestImg(Transform parent, Sprite sprite, Color fallback)
        {
            var go = new GameObject("Img", typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var img = go.AddComponent<UnityEngine.UI.Image>();
            if (sprite != null) { img.sprite = sprite; img.color = Color.white; }
            else img.color = fallback;
            img.raycastTarget = false;
            return img;
        }

        static void ChestPlace(RectTransform rt, Vector2 pos, Vector2 size)
        {
            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = pos; rt.sizeDelta = size;
        }
    }
}
