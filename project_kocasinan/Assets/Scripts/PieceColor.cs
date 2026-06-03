using UnityEngine;

namespace BusJam
{
    /// <summary>Team colors. First N entries are used for an N-color level.</summary>
    public enum PieceColor
    {
        Red, Orange, Yellow, Green, Teal, Blue, Purple, Pink
    }

    public static class Palette
    {
        public static readonly Color Mystery  = new Color(0.62f, 0.64f, 0.70f);
        public static readonly Color SeatEmpty = new Color(0.20f, 0.22f, 0.28f);
        public static readonly Color Skin      = new Color(0.99f, 0.83f, 0.69f);
        public static readonly Color Gold      = new Color(1f, 0.82f, 0.30f);

        public static int Count => 8;

        // Soft, candy-bright colors — vivid but easy on the eyes.
        public static Color ToColor(PieceColor c)
        {
            switch (c)
            {
                case PieceColor.Red:    return new Color(0.96f, 0.43f, 0.43f);
                case PieceColor.Orange: return new Color(0.98f, 0.64f, 0.33f);
                case PieceColor.Yellow: return new Color(0.99f, 0.83f, 0.39f);
                case PieceColor.Green:  return new Color(0.48f, 0.81f, 0.52f);
                case PieceColor.Teal:   return new Color(0.36f, 0.79f, 0.75f);
                case PieceColor.Blue:   return new Color(0.44f, 0.64f, 0.95f);
                case PieceColor.Purple: return new Color(0.68f, 0.52f, 0.92f);
                case PieceColor.Pink:   return new Color(0.97f, 0.57f, 0.76f);
                default:                return Color.gray;
            }
        }
    }
}
