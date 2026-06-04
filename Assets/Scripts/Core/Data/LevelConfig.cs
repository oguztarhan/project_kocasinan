using System.Collections.Generic;
using UnityEngine;

namespace BusJam.Core
{
    /// <summary>
    /// A complete, data-driven level definition. Designers build levels as assets — no code,
    /// no scene surgery — which is what makes the game <i>scalable</i> to hundreds of levels.
    /// Create via: Assets ▸ Create ▸ BusJam ▸ Level Config.
    /// </summary>
    [CreateAssetMenu(fileName = "Level_", menuName = "BusJam/Level Config", order = 2)]
    public class LevelConfig : ScriptableObject
    {
        /// <summary>
        /// One row of the passenger grid. Unity cannot serialize a 2D array in the inspector,
        /// so we wrap each row — the standard, designer-friendly workaround.
        /// </summary>
        [System.Serializable]
        public class GridRow
        {
            [Tooltip("Cells left→right. ColorType.None = empty cell.")]
            public ColorType[] cells;
        }

        [Header("Grid")]
        [Min(0.1f)] public float cellSize = 1f;

        [Tooltip("Row 0 is the FRONT row, nearest the bus stop. Passengers exit toward row 0.")]
        public GridRow[] grid;

        [Header("Holding Area")]
        [Tooltip("Number of waiting slots available this level (e.g. 5–7).")]
        [Min(1)] public int slotCount = 5;

        [Header("Buses")]
        [Tooltip("The order buses arrive in. Front of the list arrives first.")]
        public List<BusData> busSequence = new List<BusData>();

        // --- Convenience accessors (no allocation) -------------------------------
        public int Rows => grid != null ? grid.Length : 0;

        public int Columns
        {
            get
            {
                if (grid == null || grid.Length == 0 || grid[0].cells == null) return 0;
                return grid[0].cells.Length;
            }
        }

        /// <summary>Color at a grid coordinate; <see cref="ColorType.None"/> if empty/out-of-range.</summary>
        public ColorType ColorAt(int row, int col)
        {
            if (grid == null || row < 0 || row >= grid.Length) return ColorType.None;
            var cells = grid[row].cells;
            if (cells == null || col < 0 || col >= cells.Length) return ColorType.None;
            return cells[col];
        }
    }
}
