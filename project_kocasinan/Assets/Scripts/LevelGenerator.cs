using System.Collections.Generic;
using UnityEngine;

namespace BusJam
{
    public struct BusDef { public PieceColor color; public VehicleType type; public int capacity; public int advanceN; }

    /// <summary>A vehicle placed in the jam grid. `cell` = the LEADING cell (nearest the
    /// edge it slides off toward parking); the body extends backward as cell - i*dir for
    /// i in 0..CellLength(type)-1. `dir` = arrow/exit direction: Down (0,-1), Up (0,1),
    /// Left (-1,0), Right (1,0). `advanceN` &gt; 0 = special "&lt;&lt;" crawler (cells/tap).</summary>
    public struct GridBus
    {
        public PieceColor color;
        public VehicleType type;
        public int capacity;
        public Vector2Int cell;
        public Vector2Int dir;
        public int advanceN;
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
        public int colorCount;
    }

    public static class LevelGenerator
    {
        public const int BaseSlots = 5;   // unlocked parking == BuildQueue servability window
        public const int ExtraSlots = 3;  // locked, unlock for coins -> ~8 total pads

        /// <summary>Procedural levels (used for 6+ and as the fallback). Gets harder
        /// as the level rises: more colors, more buses, more specials.</summary>
        public static LevelData Generate(int level)
        {
            var rng = new System.Random(level * 9176 + 4242);

            int colorCount = Mathf.Clamp(3 + (level - 1) / 4, 3, Palette.Count); // +1 color every 4 levels
            int busCount   = Mathf.Clamp(4 + level, 6, 16);
            float goldenP  = Mathf.Min(0.10f, level * 0.01f);
            float mysteryP = Mathf.Min(0.30f, level * 0.025f);

            // Special "<<" crawlers ramp in from level 8 (none earlier so the mechanic introduces cleanly).
            float specialP = level < 8 ? 0f : Mathf.Min(0.22f, (level - 7) * 0.035f);

            return Build(rng, level, colorCount, busCount, 5, 0, BaseSlots, ExtraSlots,
                         goldenP, mysteryP, MixForLevel(level), specialP, 4);
        }

        // Vehicle variety ramps in: bus → cars+buses → limos.
        static VehicleMix MixForLevel(int level)
        {
            if (level <= 5) return VehicleMix.BusOnly;
            if (level <= 9) return VehicleMix.CarsAndBuses;
            return VehicleMix.WithLimo;
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

            return Build(rng, def.levelNumber, colorCount, busCount, Mathf.Max(3, def.gridWidth), def.gridHeight,
                         baseSlots, extraSlots,
                         Mathf.Clamp01(def.goldenChance), Mathf.Clamp01(def.mysteryChance), def.vehicleMix,
                         Mathf.Clamp01(def.specialChance), def.specialMaxAdvance);
        }

        // Shared solvable-by-construction core for both procedural and authored levels.
        static LevelData Build(System.Random rng, int levelNumber, int colorCount, int busCount,
            int gridWidth, int gridHeightHint, int baseSlots, int extraSlots,
            float goldenP, float mysteryP, VehicleMix mix,
            float specialChance, int specialMaxAdvance)
        {
            int maxAdvance = Mathf.Max(2, specialMaxAdvance); // N >= 2 so a special always makes progress on a clear lane
            var buses = new List<BusDef>(busCount);
            for (int i = 0; i < busCount; i++)
            {
                var type = PickType(mix, rng);
                int cap = CapacityFor(type, mix, rng);
                // advanceN is ORTHOGONAL to placement/capacity, so it never affects solvability.
                int advanceN = (specialChance > 0f && rng.NextDouble() < specialChance) ? rng.Next(2, maxAdvance + 1) : 0;
                buses.Add(new BusDef { color = (PieceColor)rng.Next(colorCount), type = type, capacity = cap, advanceN = advanceN });
            }

            // BuildQueue emits exactly `capacity` people per vehicle, so total people ==
            // total seats per color -> every vehicle fills exactly -> always winnable.
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
                colorCount = colorCount
            };
        }

        static VehicleType PickType(VehicleMix mix, System.Random rng)
        {
            switch (mix)
            {
                case VehicleMix.CarsOnly: return VehicleType.Car;
                case VehicleMix.CarsAndBuses: return rng.Next(2) == 0 ? VehicleType.Car : VehicleType.Bus;
                case VehicleMix.WithLimo:
                    // Bias toward FEWER-but-BIGGER vehicles (bigger people totals, not vehicle spam).
                    int r = rng.Next(100);
                    if (r < 35) return VehicleType.Limo;   // 35% big bus (16)
                    if (r < 85) return VehicleType.Bus;     // 50% bus (10)
                    return VehicleType.Car;                 // 15% car (4)
                default: return VehicleType.Bus;           // BusOnly / BusesVaried
            }
        }

        static int CapacityFor(VehicleType type, VehicleMix mix, System.Random rng)
        {
            if (type == VehicleType.Bus && mix == VehicleMix.BusesVaried)
                return 6 + rng.Next(7); // 6..12 — varied bus sizes
            return Vehicles.DefaultCapacity(type);
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
        // Multi-cell: each vehicle occupies CellLength(type) cells in a line along its exit
        // direction. Placing in reverse so that, for every k, the body cells are free AND the
        // exit lane ahead is clear, guarantees forward extraction (0..n-1) is always solvable:
        // when vehicle k leaves, all later-placed vehicles are still clear of its lane.
        static List<GridBus> BuildGrid(List<BusDef> buses, System.Random rng, int gridWidth, int gridHeightHint, out int W, out int H)
        {
            int n = buses.Count;
            int totalCells = 0;
            for (int i = 0; i < n; i++) totalCells += Vehicles.CellLength(buses[i].type);

            // Size for ~1.5x total cells (room for exit lanes) while keeping the board within the
            // camera envelope: widen first (W up to 7), then deepen (H up to 8). 1.5 (was 1.7) lets the
            // bigger multi-cell vehicles (now up to 16-seat 3-cell limos) still fit W7xH8.
            W = Mathf.Clamp(Mathf.Max(gridWidth, Mathf.CeilToInt(totalCells * 1.5f / 8f)), 4, 7);
            H = gridHeightHint > 0 ? Mathf.Clamp(gridHeightHint, 3, 8)
                                   : Mathf.Clamp(Mathf.CeilToInt(totalCells * 1.5f / W), 3, 8);

            var dirs = new[] { new Vector2Int(0, -1), new Vector2Int(0, 1), new Vector2Int(-1, 0), new Vector2Int(1, 0) };
            var occupied = new HashSet<Vector2Int>();
            var result = new GridBus[n];

            for (int k = n - 1; k >= 0; k--)
            {
                int L = Vehicles.CellLength(buses[k].type);
                bool placed = false;
                int guard = 0;
                while (!placed)
                {
                    var cells = AllCells(W, H);
                    Shuffle(cells, rng);
                    foreach (var anchor in cells)
                    {
                        var ds = new List<Vector2Int>(dirs);
                        Shuffle(ds, rng);
                        foreach (var d in ds)
                        {
                            if (BodyFree(anchor, d, L, occupied, W, H) && PathClear(anchor, d, occupied, W, H))
                            {
                                result[k] = new GridBus { color = buses[k].color, type = buses[k].type, capacity = buses[k].capacity, cell = anchor, dir = d, advanceN = buses[k].advanceN };
                                for (int i = 0; i < L; i++) occupied.Add(anchor - d * i);
                                placed = true;
                                break;
                            }
                        }
                        if (placed) break;
                    }
                    if (!placed)
                    {
                        H++;
                        if (guard++ > 8)
                        {
                            // extreme fallback: drop the whole vehicle into a fresh row, exiting left.
                            var d = new Vector2Int(-1, 0);
                            var anchor = new Vector2Int(0, H);
                            result[k] = new GridBus { color = buses[k].color, type = buses[k].type, capacity = buses[k].capacity, cell = anchor, dir = d, advanceN = buses[k].advanceN };
                            for (int i = 0; i < L; i++) occupied.Add(anchor - d * i); // (0,H),(1,H),(2,H)
                            H += 2; placed = true;
                        }
                    }
                }
            }

            return new List<GridBus>(result);
        }

        // All L body cells (anchor, anchor-dir, ... anchor-(L-1)*dir) are in-grid and unoccupied.
        static bool BodyFree(Vector2Int anchor, Vector2Int dir, int L, HashSet<Vector2Int> occ, int W, int H)
        {
            for (int i = 0; i < L; i++)
            {
                var c = anchor - dir * i;
                if (c.x < 0 || c.x >= W || c.y < 0 || c.y >= H) return false;
                if (occ.Contains(c)) return false;
            }
            return true;
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
