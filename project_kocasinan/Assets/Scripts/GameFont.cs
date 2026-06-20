using UnityEngine;
using TMPro;

namespace BusJam
{
    /// <summary>
    /// Central access to the game-wide font (Matcha Cih). uGUI Text uses the TrueType <see cref="UGUI"/>;
    /// TMP text uses <see cref="TMP"/> — a DYNAMIC SDF font asset created from the same TTF at runtime
    /// (glyphs rasterise on demand, exactly like a normal dynamic TMP font). Both are loaded once from
    /// Resources/Fonts/Matcha Cih. If the asset is ever missing it falls back to the built-in font, so this
    /// can never null-crash any text.
    /// </summary>
    public static class GameFont
    {
        const string ResPath = "Fonts/Matcha Cih";

        static Font _ugui;
        public static Font UGUI =>
            _ugui != null ? _ugui
                          : (_ugui = Resources.Load<Font>(ResPath) ?? Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf"));

        static TMP_FontAsset _tmp;
        static bool _tmpTried;
        public static TMP_FontAsset TMP
        {
            get
            {
                if (_tmp == null && !_tmpTried)
                {
                    _tmpTried = true;
                    var src = Resources.Load<Font>(ResPath);
                    if (src != null)
                    {
                        try { _tmp = TMP_FontAsset.CreateFontAsset(src); }
                        catch (System.Exception e) { Debug.LogWarning("[GameFont] dynamic TMP font create failed: " + e.Message); _tmp = null; }
                        if (_tmp != null) _tmp.name = "Matcha Cih SDF (runtime)";
                    }
                }
                return _tmp;
            }
        }
    }
}
