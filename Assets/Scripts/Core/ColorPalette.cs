using UnityEngine;

namespace BusJam.Core
{
    /// <summary>
    /// Maps a <see cref="ColorType"/> to a renderable Unity <see cref="Color"/>.
    /// Centralised so designers/artists tweak the look in exactly one place
    /// (Single Source of Truth) instead of scattering literal colors across actors.
    /// </summary>
    public static class ColorPalette
    {
        // A flat lookup keyed by (int)ColorType. Kept as a static array (not a
        // Dictionary) to avoid any hashing/boxing on the hot path.
        private static readonly Color[] Colors =
        {
            new Color(0.55f, 0.55f, 0.58f), // None  (neutral grey, e.g. "mystery")
            new Color(0.90f, 0.22f, 0.22f), // Red
            new Color(0.20f, 0.45f, 0.92f), // Blue
            new Color(0.27f, 0.78f, 0.35f), // Green
            new Color(0.97f, 0.83f, 0.20f), // Yellow
            new Color(0.62f, 0.32f, 0.83f), // Purple
            new Color(0.96f, 0.55f, 0.16f), // Orange
        };

        /// <summary>Returns the display color for a <see cref="ColorType"/>.</summary>
        public static Color ToColor(ColorType type)
        {
            int i = (int)type;
            return (i >= 0 && i < Colors.Length) ? Colors[i] : Color.magenta; // magenta = "missing", obvious in-editor
        }
    }
}
