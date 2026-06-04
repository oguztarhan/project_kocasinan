using System.Collections.Generic;
using UnityEngine;

namespace BusJam.Core
{
    /// <summary>
    /// Owns the passenger grid: where passengers stand, and — critically — whether a given
    /// passenger has a clear path out toward the bus stop (row 0, the "front").
    ///
    /// <para><b>"Free to move" rule:</b> a passenger is free if a path of EMPTY cells (4-neighbour)
    /// connects it to the front row. We compute this with a breadth-first flood fill. The buffers
    /// are pre-allocated once and reused every query, so the hot path does zero GC allocation —
    /// no LINQ, no temporary lists.</para>
    /// </summary>
    public class GridManager : MonoBehaviour
    {
        [Header("Layout")]
        [Tooltip("World position of grid cell (row 0, col 0) — the front-left corner.")]
        [SerializeField] private Transform gridOrigin;

        // Occupancy: cells[row, col] is the passenger standing there, or null.
        private Passenger[,] _cells;
        private int _rows, _cols;
        private float _cellSize = 1f;

        // Live, ordered list of all on-grid passengers (for cheap iteration without LINQ).
        private readonly List<Passenger> _passengers = new List<Passenger>(64);

        // --- Reused BFS buffers (sized to the grid on Build) ---------------------
        private int[] _bfsQueue;   // ring/linear queue of flattened cell indices
        private bool[] _visited;   // visited flag per flattened index

        /// <summary>Code-wiring hook (used by the zero-setup Bootstrap).</summary>
        public void SetOrigin(Transform origin) => gridOrigin = origin;

        public int Rows => _rows;
        public int Columns => _cols;
        public int PassengerCount => _passengers.Count;
        public bool IsEmpty => _passengers.Count == 0;

        // ---------------------------------------------------------------------
        // Build
        // ---------------------------------------------------------------------
        /// <summary>Allocates the grid and BFS buffers. Call once per level load.</summary>
        public void Build(int rows, int cols, float cellSize)
        {
            _rows = Mathf.Max(0, rows);
            _cols = Mathf.Max(0, cols);
            _cellSize = cellSize;
            _cells = new Passenger[_rows, _cols];
            _passengers.Clear();

            int n = _rows * _cols;
            _bfsQueue = new int[Mathf.Max(1, n)];
            _visited = new bool[Mathf.Max(1, n)];
        }

        /// <summary>Places an already-instantiated passenger at a coordinate.</summary>
        public void Register(Passenger p, GridCoord coord)
        {
            if (p == null || !InBounds(coord.Row, coord.Col)) return;
            _cells[coord.Row, coord.Col] = p;
            _passengers.Add(p);
        }

        // ---------------------------------------------------------------------
        // World <-> grid
        // ---------------------------------------------------------------------
        public Vector3 CellToWorld(GridCoord c) => CellToWorld(c.Row, c.Col);

        public Vector3 CellToWorld(int row, int col)
        {
            Vector3 o = gridOrigin != null ? gridOrigin.position : Vector3.zero;
            // Columns spread along +X; rows recede from the stop along +Z (row 0 = front).
            return o + new Vector3(col * _cellSize, 0f, row * _cellSize);
        }

        // ---------------------------------------------------------------------
        // The core query: is this passenger free to leave?
        // ---------------------------------------------------------------------
        /// <summary>
        /// True if <paramref name="p"/> can reach the front row (row 0) through empty cells.
        /// Front-row passengers are trivially free.
        /// </summary>
        public bool IsFree(Passenger p)
        {
            if (p == null || !p.IsOnGrid) return false;

            int sr = p.Coord.Row;
            int sc = p.Coord.Col;
            if (!InBounds(sr, sc)) return false;
            if (sr == 0) return true; // already at the exit row

            // BFS over EMPTY neighbours, starting from the passenger's own cell.
            System.Array.Clear(_visited, 0, _rows * _cols);
            int head = 0, tail = 0;
            int startIdx = Index(sr, sc);
            _visited[startIdx] = true;
            _bfsQueue[tail++] = startIdx;

            while (head < tail)
            {
                int idx = _bfsQueue[head++];
                int r = idx / _cols;
                int c = idx % _cols;

                // 4-neighbour expansion. We may only step INTO empty cells.
                // The moment any expansion reaches an empty front-row cell, the passenger is free.
                TryVisit(r - 1, c, ref tail, out bool reachedFront); if (reachedFront) return true;
                TryVisit(r + 1, c, ref tail, out reachedFront);      if (reachedFront) return true;
                TryVisit(r, c - 1, ref tail, out reachedFront);      if (reachedFront) return true;
                TryVisit(r, c + 1, ref tail, out reachedFront);      if (reachedFront) return true;
            }
            return false;
        }

        /// <summary>
        /// Visits a neighbour if it is in-bounds, empty and unvisited. Reports via
        /// <paramref name="reachedFront"/> when that neighbour is on the exit row.
        /// </summary>
        private bool TryVisit(int r, int c, ref int tail, out bool reachedFront)
        {
            reachedFront = false;
            if (!InBounds(r, c)) return false;
            int idx = Index(r, c);
            if (_visited[idx]) return false;
            if (_cells[r, c] != null) return false; // blocked by another passenger

            _visited[idx] = true;
            _bfsQueue[tail++] = idx;
            if (r == 0) reachedFront = true; // an empty front-row cell is a valid exit
            return true;
        }

        // ---------------------------------------------------------------------
        // Mutation
        // ---------------------------------------------------------------------
        /// <summary>Removes a passenger from the grid (it's now in transit to a slot/bus).</summary>
        public void Remove(Passenger p)
        {
            if (p == null || !p.Coord.IsValid) return;
            int r = p.Coord.Row, c = p.Coord.Col;
            if (InBounds(r, c) && _cells[r, c] == p) _cells[r, c] = null;
            _passengers.Remove(p);
            p.ClearGridCoord();
        }

        /// <summary>
        /// True if ANY currently-free grid passenger matches <paramref name="color"/>.
        /// Used by the deadlock arbiter. Plain for-loop — no LINQ, no allocation.
        /// </summary>
        public bool AnyFreePassengerOfColor(ColorType color)
        {
            for (int i = 0; i < _passengers.Count; i++)
            {
                Passenger p = _passengers[i];
                if (p != null && p.Color == color && IsFree(p)) return true;
            }
            return false;
        }

        // ---------------------------------------------------------------------
        // Helpers
        // ---------------------------------------------------------------------
        private bool InBounds(int r, int c) => r >= 0 && r < _rows && c >= 0 && c < _cols;
        private int Index(int r, int c) => r * _cols + c;
    }
}
