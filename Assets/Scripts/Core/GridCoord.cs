namespace BusJam.Core
{
    /// <summary>Immutable (row, col) grid address. A readonly struct → no heap allocation, no GC.</summary>
    public readonly struct GridCoord
    {
        public readonly int Row;
        public readonly int Col;

        public GridCoord(int row, int col)
        {
            Row = row;
            Col = col;
        }

        public static readonly GridCoord Invalid = new GridCoord(-1, -1);
        public bool IsValid => Row >= 0 && Col >= 0;

        public override string ToString() => $"({Row},{Col})";
    }
}
