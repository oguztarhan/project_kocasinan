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

        // DISTINCT, saturated team colors — spread evenly around the hue wheel so adjacent colors never read
        // alike (the old soft pastels blended: red/orange/pink, green/teal/blue). Vibrant() lifts these further.
        public static Color ToColor(PieceColor c)
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
    }
}
