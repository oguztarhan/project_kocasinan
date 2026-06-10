using System.Collections.Generic;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace BusJam
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
            if (found == null) found = Resources.Load<Sprite>("UIKit/" + name); // build fallback
            if (found == null) Debug.LogWarning($"[UIKit] sprite not found: {name}");
            _cache[name] = found;
            return found;
        }

        // ---- Fonts ----
        public static Font Title() { if (_title == null) _title = LoadFont("GROBOLD"); return _title; }   // chunky cartoon titles
        public static Font Num()   { if (_num == null)   _num   = LoadFont("Oswald-Bold"); return _num; } // numbers / body

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
        public static Sprite NavShop()    => A(0);
        public static Sprite NavHome()    => A(2);
        public static Sprite NavDaily()   => A(3);   // calendar
        public static Sprite Gear()       => A(4);   // settings
        public static Sprite NavBtnBg()   => A(15);  // ORANGE backing: behind the SELECTED nav icon
        public static Sprite NavBtnOff()  => A(14);  // BLUE backing: behind unselected nav icons
        public static Sprite NavStrip()   => A(35);  // bottom blue nav strip
        public static Sprite CheckMark()  => A(5);   // claimed check (daily)

        // Top bar / counters:
        public static Sprite CoinBar()    => A(20);  // gold counter bar (menu + in-game)
        public static Sprite Coin()       => A(16);  // coin icon
        public static Sprite PlusGreen()  => A(17);  // green "+" on the counter
        public static Sprite SliderTrack()=> A(9);   // draggable on/off slider track
        public static Sprite CircleGreen()=> A(18);  // round green badge (people-left)
        public static Sprite CircleYellow()=> A(19); // round yellow badge (level)
        public static Sprite Gem()        => A(22);  // (not used for now)

        // Home:
        public static Sprite PlayBtn()    => A(21);  // PLAY button

        // Shop:
        public static Sprite ShopCoinA()  => A(11);  // coin-pack icons
        public static Sprite ShopCoinB()  => A(12);
        public static Sprite ShopCoinC()  => A(13);
        public static Sprite ShopGold()   => A(29);
        public static Sprite CoinPackSmall() => A(30);
        public static Sprite CoinPackBig()   => A(31); // most expensive
        public static Sprite QtyPlus()    => A(23);  // buy-quantity +
        public static Sprite QtyMinus()   => A(32);  // buy-quantity -
        public static Sprite PriceBtnA()  => A(36);  // price buttons
        public static Sprite PriceBtnB()  => A(37);
        public static Sprite ShopBoxA()   => A(44);  // shop item card backgrounds
        public static Sprite ShopBoxB()   => A(55);
        public static Sprite ShopIconBgA()=> A(56);  // backing behind shop coin icons
        public static Sprite ShopIconBgB()=> A(57);
        public static Sprite AdReward()   => A(27);  // optional "watch ad for gold" icon
        public static Sprite NoAds()      => A(39);

        // Titles / panels:
        public static Sprite TitleBarA()  => A(45);
        public static Sprite TitleBarB()  => A(50);
        public static Sprite TitleBarC()  => A(53);

        // Daily rewards:
        public static Sprite DailyCoin()  => A(38);
        public static Sprite DailyIconA() => A(58);
        public static Sprite DailyIconB() => A(59);
        public static Sprite CardCream()  => A(66);  // cream daily-reward card background

        public static Sprite WatchAd()    => A(61);  // video-ad button
        public static Sprite CloseX()     => A(79);  // red close
        public static Sprite Back()       => A(80);
        public static Sprite IconSound()  => A(71);
        public static Sprite IconMusic()  => A(73);

        // ---- Atlas 2 ----
        public static Sprite EmptyBoxBlue() => B(0);
        public static Sprite PanelTall()    => B(2);   // big popup background
        public static Sprite PanelCyan()    => B(4);   // light popup background
        public static Sprite BtnOrange()    => B(9);
        public static Sprite BtnDark()      => B(10);
        public static Sprite BtnGreen()     => B(16);
        public static Sprite BtnRed()       => B(17);
        public static Sprite JokerRecolor() => B(8);   // swirl arrows (recolor)
        public static Sprite JokerSwap()    => B(15);  // crossed arrows (swap people)
        public static Sprite JokerHeli()    => B(14);  // HAND placeholder (no heli icon in kit)
        public static Sprite JokerShield()  => B(13);
        public static Sprite JokerDestroy() => B(7);
    }
}
