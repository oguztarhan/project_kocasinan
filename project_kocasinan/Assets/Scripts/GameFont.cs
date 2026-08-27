using UnityEngine;
using TMPro;

namespace Ridebury
{
    /// <summary>
    /// Central access to the game-wide font. The font is chosen in ONE editable asset — Resources/FontConfig.asset:
    /// drag any font into its "Ui Font" slot and the whole project switches (uGUI Text via <see cref="UGUI"/>, TMP
    /// text via <see cref="TMP"/>), applied everywhere by GlobalFontApplier. No code edits needed to change the font.
    ///
    /// TMP: if FontConfig.tmpFont is empty, a DYNAMIC SDF asset is created from the uiFont at runtime. Everything
    /// falls back safely (config missing -> a Resources font -> the built-in font), so text can never null-crash.
    /// </summary>
    public static class GameFont
    {
        const string FallbackResPath = "Fonts/atomicage-regular"; // used only if FontConfig.asset / its uiFont is missing

        static FontConfig _cfg;
        static bool _cfgTried;
        static FontConfig Cfg
        {
            get { if (!_cfgTried) { _cfgTried = true; _cfg = Resources.Load<FontConfig>("FontConfig"); } return _cfg; }
        }

        /// <summary>Global text-size multiplier from FontConfig (1 = original). GlobalFontApplier scales every text by this.</summary>
        public static float UiScale { get { var c = Cfg; return c != null && c.fontScale > 0f ? c.fontScale : 1f; } }

        static Font _ugui;
        public static Font UGUI
        {
            get
            {
                if (_ugui != null) return _ugui;
                var c = Cfg;
                _ugui = (c != null && c.uiFont != null) ? c.uiFont
                      : Resources.Load<Font>(FallbackResPath)
                      ?? Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
                return _ugui;
            }
        }

        static TMP_FontAsset _tmp;
        static bool _tmpTried;
        public static TMP_FontAsset TMP
        {
            get
            {
                if (_tmp != null) return _tmp;
                if (_tmpTried) return _tmp;
                _tmpTried = true;

                var c = Cfg;
                if (c != null && c.tmpFont != null) { _tmp = c.tmpFont; return _tmp; } // hand-made TMP asset, if provided

                var src = UGUI;                                                         // else generate a dynamic SDF from the UI font
                if (src != null)
                {
                    try { _tmp = TMP_FontAsset.CreateFontAsset(src); }
                    catch (System.Exception e) { Debug.LogWarning("[GameFont] dynamic TMP font create failed: " + e.Message); _tmp = null; }
                    if (_tmp != null) _tmp.name = src.name + " SDF (runtime)";
                }
                return _tmp;
            }
        }
    }
}
