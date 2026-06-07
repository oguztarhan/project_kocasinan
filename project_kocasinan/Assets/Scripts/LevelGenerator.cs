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

    /// <summary>One queue slot: a single waiting person.</summary>
    public class LineGroup
    {
        public PieceColor color;
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

        /// <summary>Procedural levels (used for 6+ and as the fallback). Gets harder
        /// as the level rises: more colors, more buses, less time per person.</summary>
        public static LevelData Generate(int level)
        {
            var rng = new System.Random(level * 9176 + 4242);

            int colorCount = Mathf.Clamp(3 + (level - 1) / 4, 3, Palette.Count); // +1 color every 4 levels
            int busCount   = Mathf.Clamp(4 + level, 6, 16);
            float rate     = Mathf.Clamp(2.2f - level * 0.04f, 1.4f, 2.2f);       // seconds per person
            float timeLimit = Mathf.Max(18f, busCount * 3 * rate);
            float goldenP  = Mathf.Min(0.10f, level * 0.01f);
            float mysteryP = Mathf.Min(0.30f, level * 0.025f);

            return Build(rng, level, colorCount, busCount, 5, 0, BaseSlots, ExtraSlots, timeLimit, goldenP, mysteryP);
        }

        /// <summary>Authored levels. Parameters are tunable in the Inspector but still
        /// flow through the same solvable-by-construction core.</summary>
        public static LevelData Generate(LevelDefinition def)
        {
            int seed = def.seed != 0 ? def.seed : def.levelNumber * 9176 + 4242;
            var rng = new System.Random(seed);

            int colorCount = Mathf.Clamp(def.colorCount, 3, Palette.Count);
            int busCount   = Mathf.Max(4, def.busCount);
            int baseSlots  = Mathf.Max(1, def.baseSlots);
            int extraSlots = Mathf.Max(0, def.extraSlots);
            float timeLimit = Mathf.Max(10f, def.timeLimit);

            return Build(rng, def.levelNumber, colorCount, busCount, Mathf.Max(3, def.gridWidth), def.gridHeight,
                         baseSlots, extraSlots, timeLimit,
                         Mathf.Clamp01(def.goldenChance), Mathf.Clamp01(def.mysteryChance));
        }

        // Shared solvable-by-construction core for both procedural and authored levels.
        static LevelData Build(System.Random rng, int levelNumber, int colorCount, int busCount,
            int gridWidth, int gridHeightHint, int baseSlots, int extraSlots, float timeLimit,
            float goldenP, float mysteryP)
        {
            const int capacity = 3;
            var buses = new List<BusDef>(busCount);
            for (int i = 0; i < busCount; i++)
                buses.Add(new BusDef { color = (PieceColor)rng.Next(colorCount), capacity = capacity });

            var groups    = BuildQueue(buses, rng, baseSlots, goldenP, mysteryP);
            var gridBuses = BuildGrid(buses, rng, gridWidth, gridHeightHint, out int gridW, out int gridH);

            return new LevelData
            {
                levelNumber = levelNumber,
                groups = groups,
                gridBuses = gridBuses,
                gridW = gridW, gridH = gridH,
                baseSlots = baseSlots,
                extraSlots = extraSlots,
                timeLimit = timeLimit,
                colorCount = colorCount
            };
        }

        // ---- Queue (window emission -> one single person per slot) -----------
        // window MUST equal the unlocked parking slots (baseSlots): at most `window`
        // buses are ever "open", which keeps the queue servable -> solvable.
        static List<LineGroup> BuildQueue(List<BusDef> buses, System.Random rng, int window, float goldenP, float mysteryP)
        {
            int n = buses.Count;
            window = Mathf.Clamp(window, 1, n);
            var remaining = new int[n];
            for (int i = 0; i < n; i++) remaining[i] = buses[i].capacity;

            var flat = new List<PieceColor>();
            var open = new List<int>();
            int nextToOpen = Mathf.Min(window, n);
            for (int i = 0; i < nextToOpen; i++) open.Add(i);
            while (open.Count > 0)
            {
                int pick = open[rng.Next(open.Count)];
                flat.Add(buses[pick].color);
                remaining[pick]--;
                if (remaining[pick] == 0)
                {
                    open.Remove(pick);
                    if (nextToOpen < n) { open.Add(nextToOpen); nextToOpen++; }
                }
            }

            // One single person per emitted color.
            var groups = new List<LineGroup>();
            foreach (var c in flat) groups.Add(new LineGroup { color = c });

            for (int i = 0; i < groups.Count; i++)
            {
                if (rng.NextDouble() < goldenP) groups[i].golden = true;
                if (i >= 2 && rng.NextDouble() < mysteryP) groups[i].mystery = true;
            }
            return groups;
        }

        // ---- Grid (reverse generation: always solvable) ----------------------
        static List<GridBus> BuildGrid(List<BusDef> buses, System.Random rng, int gridWidth, int gridHeightHint, out int W, out int H)
        {
            int n = buses.Count;
            W = Mathf.Max(3, gridWidth);
            H = gridHeightHint > 0 ? Mathf.Max(2, gridHeightHint) : Mathf.Clamp(Mathf.CeilToInt(n * 1.7f / W), 2, 6);

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
