using UnityEngine;

namespace BusJam
{
    /// <summary>Team colors. The first N entries are used for an N-color level, so they are ORDERED for maximum
    /// distinctness as colors are added (Red+Blue at 2, +Yellow at 3, +Green at 4, ...) — early levels never pair
    /// two similar hues. ToColor maps each NAME to its color, so reordering here only changes the add-order.</summary>
    public enum PieceColor
    {
        Red, Blue, Yellow, Green, Purple, Orange, Teal, Pink
    }

    public static class Palette
    {
        public static readonly Color Mystery  = new Color(0.62f, 0.64f, 0.70f);
        public static readonly Color SeatEmpty = new Color(0.20f, 0.22f, 0.28f);
        public static readonly Color Skin      = new Color(0.99f, 0.83f, 0.69f);
        public static readonly Color Gold      = new Color(1f, 0.82f, 0.30f);

        public static int Count => 8;

        // Single color source. In COLOR-BLIND mode (SaveSystem.ColorBlind) it returns a palette that stays
        // distinguishable for red-green / blue-yellow deficiency; otherwise the normal vibrant palette.
        public static Color ToColor(PieceColor c) => SaveSystem.ColorBlind ? ToColorBlind(c) : ToColorNormal(c);

        // DISTINCT, saturated team colors — spread evenly around the hue wheel so adjacent colors never read
        // alike (the old soft pastels blended: red/orange/pink, green/teal/blue). Vibrant() lifts these further.
        static Color ToColorNormal(PieceColor c)
        {
            switch (c)
            {
                case PieceColor.Red:    return new Color(0.95f, 0.16f, 0.18f);  // hue ~358 pure red (not pink/orange)
                case PieceColor.Orange: return new Color(1.00f, 0.52f, 0.06f);  // hue ~30 pushed off red AND yellow
                case PieceColor.Yellow: return new Color(1.00f, 0.88f, 0.10f);  // hue ~52 golden, lighter/greener than orange
                case PieceColor.Green:  return new Color(0.20f, 0.78f, 0.26f);  // hue ~130 pure green, well off teal
                case PieceColor.Teal:   return new Color(0.00f, 0.74f, 0.74f);  // hue ~180 cyan, unmistakably not green/blue
                case PieceColor.Blue:   return new Color(0.13f, 0.45f, 1.00f);  // hue ~218 strong blue
                case PieceColor.Purple: return new Color(0.62f, 0.24f, 0.95f);  // hue ~270 violet, off both blue and pink
                case PieceColor.Pink:   return new Color(1.00f, 0.28f, 0.72f);  // hue ~325 magenta, clearly not red
                default:                return Color.gray;
            }
        }

        // COLOR-BLIND-SAFE palette (based on the Okabe-Ito set, tuned for a game). Colors differ in BOTH hue and
        // LUMINANCE, so red-green / blue-yellow deficient players can still tell them apart. The enum add-order is
        // Red,Blue,Yellow,Green,... so the FIRST colors a level uses are the most separated (vermillion / blue /
        // bright-yellow / bluish-green). NOTE: a true fix also needs SHAPES per colour — this is the palette half.
        static Color ToColorBlind(PieceColor c)
        {
            switch (c)
            {
                case PieceColor.Red:    return new Color(0.84f, 0.37f, 0.00f);  // vermillion (warm, mid luminance)
                case PieceColor.Blue:   return new Color(0.00f, 0.45f, 0.70f);  // blue (cool, low-mid luminance)
                case PieceColor.Yellow: return new Color(0.96f, 0.90f, 0.28f);  // bright yellow (high luminance)
                case PieceColor.Green:  return new Color(0.00f, 0.62f, 0.45f);  // bluish-green (reads apart from vermillion)
                case PieceColor.Purple: return new Color(0.80f, 0.47f, 0.65f);  // reddish-purple
                case PieceColor.Orange: return new Color(0.96f, 0.64f, 0.22f);  // orange (lighter than vermillion)
                case PieceColor.Teal:   return new Color(0.35f, 0.71f, 0.91f);  // sky-blue (light, apart from Blue)
                case PieceColor.Pink:   return new Color(0.16f, 0.16f, 0.20f);  // near-black (max luminance separation for the 8th)
                default:                return Color.gray;
            }
        }
    }
}
