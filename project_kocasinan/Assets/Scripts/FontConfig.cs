using UnityEngine;
using TMPro;

namespace Ridebury
{
    /// <summary>
    /// The ONE place that decides the game-wide font. Lives as an editable asset at Resources/FontConfig.asset:
    /// drag ANY font into <see cref="uiFont"/> in the Inspector and the entire project switches to it (every uGUI
    /// Text + TMP text), thanks to <see cref="GameFont"/> + GlobalFontApplier. No code edits, no per-text changes.
    ///
    /// Leave <see cref="tmpFont"/> empty to auto-generate a matching TMP (SDF) font from uiFont at runtime, or assign
    /// a hand-made TMP Font Asset for crisper TMP text.
    /// </summary>
    [CreateAssetMenu(fileName = "FontConfig", menuName = "Ridebury/Font Config")]
    public class FontConfig : ScriptableObject
    {
        [Tooltip("Game-wide uGUI (legacy Text) font. Drag any .ttf/.otf here to change EVERY text in the project.")]
        public Font uiFont;

        [Tooltip("Optional TMP font asset for TextMeshPro text. Empty = generated from uiFont at runtime.")]
        public TMP_FontAsset tmpFont;

        [Tooltip("Global text-size multiplier for EVERY text. 1 = original sizes; e.g. 1.15 makes all text 15% bigger, " +
                 "0.9 makes it smaller. Useful when a font renders small. Scales each text from its own authored size.")]
        [Range(0.5f, 2.5f)] public float fontScale = 1f;
    }
}
