using System.Collections.Generic;
using UnityEngine;

namespace BusJam
{
    public struct BusDef { public PieceColor color; public int capacity; }

    /// <summary>A bus placed in the jam grid: cell + the direction its arrow points
    /// (the edge it slides off toward parking). Down = (0,-1), Left = (-1,0), Right = (1,0).</summary>
    public struct GridBus
    {
        public PieceColor color;
        public int capacity;
        public Vector2Int cell;
        public Vector2Int dir;
    }

    /// <summary>One queue slot: a single person (count 1) or a cabin emitting N
    /// same-color people in order.</summary>
    public class LineGroup
    {
        public PieceColor color;
        public int count;
        public bool golden;
        public bool mystery;
    }

    public class LevelData
    {
        public int levelNumber;
        public List<LineGroup> groups;       // full queue (streamed in; total hidden from player)
        public List<GridBus> gridBuses;      // jam grid, index = a guaranteed-solvable extraction order
        public int gridW, gridH;
        public int baseSlots;
        public int extraSlots;
        public float timeLimit;
        public int colorCount;
    }

    public static class LevelGenerator
    {
        public const int BaseSlots = 3;
        public const int ExtraSlots = 2;

        public static LevelData Generate(int level)
        {
            var rng = new System.Random(level * 9176 + 4242);

            int colorCount = Mathf.Clamp(3 + (level - 1) / 5, 3, Palette.Count); // +1 color / 5 levels
            int busCount   = Mathf.Clamp(4 + level, 4, 15);
            int capacity   = 3;

            var buses = new List<BusDef>(busCount);
            for (int i = 0; i < busCount; i++)
                buses.Add(new BusDef { color = (PieceColor)rng.Next(colorCount), capacity = capacity });

            var groups   = BuildQueue(buses, rng, level);
            var gridBuses = BuildGrid(buses, rng, out int gridW, out int gridH);

            int people = 0; foreach (var g in groups) people += g.count;
            float timeLimit = Mathf.Max(20f, people * 2.0f - level * 1.4f);

            return new LevelData
            {
                levelNumber = level,
                groups = groups,
                gridBuses = gridBuses,
                gridW = gridW, gridH = gridH,
                baseSlots = BaseSlots,
                extraSlots = ExtraSlots,
                timeLimit = timeLimit,
                colorCount = colorCount
            };
        }

        // ---- Queue (window emission -> mostly singles, occasional cabins) ----
        static List<LineGroup> BuildQueue(List<BusDef> buses, System.Random rng, int level)
        {
            int n = buses.Count;
            int window = BaseSlots;
            var remaining = new int[n];
            for (int i = 0; i < n; i++) remaining[i] = buses[i].capacity;

            var flat = new List<PieceColor>();
            var open = new List<int>();
            int nextToOpen = Mathf.Min(window, n);
            for (int i = 0; i < nextToOpen; i++) open.Add(i);
            int lastPick = -1;

            while (open.Count > 0)
            {
                int pick;
                if (lastPick >= 0 && remaining[lastPick] > 0 && rng.NextDouble() < 0.30) // mild bias
                    pick = lastPick;
                else
                    pick = open[rng.Next(open.Count)];

                flat.Add(buses[pick].color);
                remaining[pick]--;
                lastPick = remaining[pick] > 0 ? pick : -1;
                if (remaining[pick] == 0)
                {
                    open.Remove(pick);
                    if (nextToOpen < n) { open.Add(nextToOpen); nextToOpen++; }
                }
            }

            // Merge same-color runs, then keep only runs >= 3 as cabins (else singles).
            var merged = new List<LineGroup>();
            foreach (var c in flat)
            {
                if (merged.Count > 0 && merged[merged.Count - 1].color == c && merged[merged.Count - 1].count < 9)
                    merged[merged.Count - 1].count++;
                else
                    merged.Add(new LineGroup { color = c, count = 1 });
            }

            var groups = new List<LineGroup>();
            foreach (var g in merged)
            {
                if (g.count >= 3) groups.Add(g);                       // cabin
                else for (int i = 0; i < g.count; i++) groups.Add(new LineGroup { color = g.color, count = 1 });
            }

            float goldenP = Mathf.Min(0.10f, level * 0.01f);
            float mysteryP = Mathf.Min(0.26f, level * 0.022f);
            for (int i = 0; i < groups.Count; i++)
            {
                if (rng.NextDouble() < goldenP) groups[i].golden = true;
                if (groups[i].count == 1 && i >= 2 && rng.NextDouble() < mysteryP) groups[i].mystery = true;
            }
            return groups;
        }

        // ---- Grid (reverse generation: always solvable) ----------------------
        static List<GridBus> BuildGrid(List<BusDef> buses, System.Random rng, out int W, out int H)
        {
            int n = buses.Count;
            W = 5;
            H = Mathf.Clamp(Mathf.CeilToInt(n * 1.7f / W), 2, 6);

            var dirs = new[] { new Vector2Int(0, -1), new Vector2Int(-1, 0), new Vector2Int(1, 0) };
            var occupied = new HashSet<Vector2Int>();
            var result = new GridBus[n];

            for (int k = n - 1; k >= 0; k--)
            {
                bool placed = false;
                int guard = 0;
                while (!placed)
                {
                    var cells = AllCells(W, H);
                    Shuffle(cells, rng);
                    foreach (var cell in cells)
                    {
                        if (occupied.Contains(cell)) continue;
                        var ds = new List<Vector2Int>(dirs);
                        Shuffle(ds, rng);
                        foreach (var d in ds)
                        {
                            if (PathClear(cell, d, occupied, W, H))
                            {
                                result[k] = new GridBus { color = buses[k].color, capacity = buses[k].capacity, cell = cell, dir = d };
                                occupied.Add(cell);
                                placed = true;
                                break;
                            }
                        }
                        if (placed) break;
                    }
                    if (!placed) { H++; if (guard++ > 8) { /* extreme fallback */ result[k] = new GridBus { color = buses[k].color, capacity = buses[k].capacity, cell = new Vector2Int(0, H), dir = new Vector2Int(-1, 0) }; occupied.Add(new Vector2Int(0, H)); H++; placed = true; } }
                }
            }

            var list = new List<GridBus>(result);
            return list;
        }

        static List<Vector2Int> AllCells(int W, int H)
        {
            var list = new List<Vector2Int>(W * H);
            for (int y = 0; y < H; y++)
                for (int x = 0; x < W; x++)
                    list.Add(new Vector2Int(x, y));
            return list;
        }

        static bool PathClear(Vector2Int cell, Vector2Int dir, HashSet<Vector2Int> occ, int W, int H)
        {
            var p = cell + dir;
            while (p.x >= 0 && p.x < W && p.y >= 0 && p.y < H)
            {
                if (occ.Contains(p)) return false;
                p += dir;
            }
            return true;
        }

        static void Shuffle<T>(List<T> list, System.Random rng)
        {
            for (int i = list.Count - 1; i > 0; i--)
            {
                int j = rng.Next(i + 1);
                (list[i], list[j]) = (list[j], list[i]);
            }
        }
    }
}
