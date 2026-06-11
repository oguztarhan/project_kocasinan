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

    /// <summary>Per-level board shape. Biases ONLY the order BuildGrid tries anchor cells (richer
    /// look + more blocking); the BodyFree/SlideClear clearance rules are unchanged, so every level
    /// stays solvable-by-construction.</summary>
    public enum LayoutStyle { Scatter, Ring, Cross, Diamond }

    public static class LevelGenerator
    {
        public const int BaseSlots = 5;   // unlocked parking == BuildQueue servability window
        public const int ExtraSlots = 3;  // locked, unlock for coins -> ~8 total pads

        /// <summary>Procedural levels (used for 6+ and as the fallback). Gets harder
        /// as the level rises: more colors, more buses, more specials.</summary>
        public static LevelData Generate(int level)
        {
            var rng = new System.Random(level * 9176 + 4242);

            if (level % 10 == 0) return GenerateBonus(level, rng); // every 10th = 2-color core-boxed-by-ring bonus

            // MANY vehicles every level (easy-but-many at L1); difficulty rises via colors/specials/
            // diagonals/density, NOT count.
            int colorCount = Mathf.Clamp(2 + (level - 1) / 3, 2, Palette.Count); // L1-3 = 2 colors, +1 every 3
            int busCount   = Mathf.Clamp(20 + level / 5, 20, 28);               // ~20 at L1 -> 28
            float goldenP  = Mathf.Min(0.10f, level * 0.01f);
            float mysteryP = Mathf.Min(0.30f, Mathf.Max(0, level - 4) * 0.03f); // no mystery until L5

            // Special "<<" crawlers ramp in later (boards are denser now); none early.
            float specialP = level < 10 ? 0f : Mathf.Min(0.20f, (level - 9) * 0.03f);

            return Build(rng, level, colorCount, busCount, 7, 0, BaseSlots, ExtraSlots,
                         goldenP, mysteryP, MixForLevel(level), specialP, 4, /*minRun*/ 4);
        }

        // Car-heavy early (many small cap-4 cars = easy + many + few people); buses ramp in for difficulty.
        static VehicleMix MixForLevel(int level)
        {
            if (level <= 5) return VehicleMix.CarsOnly;
            return VehicleMix.CarsAndBuses;
        }

        // ---- BONUS levels (every 10th): a DENSELY-PACKED jam where EVERY vehicle is one color (fill) except
        // ONE contrasting vehicle trapped in the dead CENTER (extracted LAST). Mixed cars + buses. Reverse-
        // placed center-out with the SAME BodyFree+SlideClear, every vehicle exiting outward -> solvable
        // (clear from the outside in to free the middle one).
        static LevelData GenerateBonus(int level, System.Random rng)
        {
            const int busCount = 36; // MANY mixed vehicles, densely packed
            var fill = PieceColor.Yellow; // everything is this color...
            var core = PieceColor.Red;    // ...except ONE vehicle in the dead center.

            var buses = new List<BusDef>(busCount);
            for (int i = 0; i < busCount; i++)
            {
                bool isCore = (i == busCount - 1); // LAST index = center, extracted LAST (after all the fill clears)
                var type = isCore ? VehicleType.Bus                                       // trapped centre piece = a bus
                                  : (rng.Next(10) < 6 ? VehicleType.Car : VehicleType.Bus); // fill: ~60% cars, 40% buses
                buses.Add(new BusDef { color = isCore ? core : fill, type = type,
                                       capacity = Vehicles.DefaultCapacity(type), advanceN = 0 });
            }

            var groups = BuildQueue(buses, rng, BaseSlots, 4, 0f, 0f); // queue untouched: window==baseSlots, total==sum caps
            var gridBuses = BuildBonusGrid(buses, rng, out int gridW, out int gridH);

            return new LevelData
            {
                levelNumber = level, groups = groups, gridBuses = gridBuses,
                gridW = gridW, gridH = gridH, baseSlots = BaseSlots, extraSlots = ExtraSlots, colorCount = 2
            };
        }

        // Dense packed bonus jam: fill all cells center-out by DESCENDING index (outer = LOW index = extracted
        // first), every vehicle exiting OUTWARD; the single highest index lands dead centre and leaves last.
        // Reverse-placed with BodyFree+SlideClear -> solvable by construction (clear outside-in to free the middle).
        static List<GridBus> BuildBonusGrid(List<BusDef> buses, System.Random rng, out int W, out int H)
        {
            int n = buses.Count;
            int totalCells = 0;
            for (int i = 0; i < n; i++) totalCells += Vehicles.CellLength(buses[i].type);
            // Tight pack so it reads as a PACKED jam (vs the open early levels), with just enough slack for lanes.
            float budget = totalCells * 1.3f;
            W = Mathf.Clamp(Mathf.CeilToInt(Mathf.Sqrt(budget)), 6, 8);
            H = Mathf.Clamp(Mathf.CeilToInt(budget / W), 5, 12);

            var occupied = new HashSet<Vector2Int>();
            var result = new GridBus[n];

            for (int k = n - 1; k >= 0; k--)
            {
                int L = Vehicles.CellLength(buses[k].type);
                bool placed = false;
                int guard = 0;
                while (!placed)
                {
                    var cells = BoxOrderedCells(W, H, rng, centerFirst: true); // fill center-out by descending index
                    foreach (var anchor in cells)
                    {
                        var ds = OutwardDirs(anchor, W, H, rng); // exit toward the nearest edge (clear lanes on a packed board)
                        foreach (var d in ds)
                        {
                            if (BodyFree(anchor, d, L, occupied, W, H) && SlideClear(anchor, d, L, occupied.Contains, W, H))
                            {
                                result[k] = new GridBus { color = buses[k].color, type = buses[k].type, capacity = buses[k].capacity, cell = anchor, dir = d, advanceN = 0 };
                                foreach (var c in OccCells(anchor, d, L)) occupied.Add(c);
                                placed = true; break;
                            }
                        }
                        if (placed) break;
                    }
                    if (!placed)
                    {
                        if (H < 13) H++;
                        if (guard++ > 8) // extreme fallback (on-screen): deepest row <=12, never grow past 13
                        {
                            var d = new Vector2Int(-1, 0);
                            var anchor = new Vector2Int(0, Mathf.Min(H, 12));
                            result[k] = new GridBus { color = buses[k].color, type = buses[k].type, capacity = buses[k].capacity, cell = anchor, dir = d, advanceN = 0 };
                            foreach (var c in OccCells(anchor, d, L)) occupied.Add(c);
                            H = Mathf.Min(H + 2, 13); placed = true;
                        }
                    }
                }
            }
            return new List<GridBus>(result);
        }

        // Cells ordered by Manhattan distance from center: centerFirst -> innermost first (core block); else
        // outermost first (ring). Jitter randomizes within a tier (try-order only; legality unchanged).
        static List<Vector2Int> BoxOrderedCells(int W, int H, System.Random rng, bool centerFirst)
        {
            float cx = (W - 1) * 0.5f, cy = (H - 1) * 0.5f;
            var keyed = new List<(Vector2Int cell, float key)>(W * H);
            for (int y = 0; y < H; y++)
                for (int x = 0; x < W; x++)
                {
                    float dist = Mathf.Abs(x - cx) + Mathf.Abs(y - cy);
                    keyed.Add((new Vector2Int(x, y), (centerFirst ? dist : -dist) + (float)rng.NextDouble() * 0.5f));
                }
            keyed.Sort((a, b) => a.key.CompareTo(b.key));
            var ordered = new List<Vector2Int>(keyed.Count);
            foreach (var kv in keyed) ordered.Add(kv.cell);
            return ordered;
        }

        // The 4 cardinals ordered most-OUTWARD-first (dir best aligned with anchor-from-center).
        static List<Vector2Int> OutwardDirs(Vector2Int anchor, int W, int H, System.Random rng)
        {
            float ox = anchor.x - (W - 1) * 0.5f, oy = anchor.y - (H - 1) * 0.5f;
            var dirs = new[] { new Vector2Int(0, -1), new Vector2Int(0, 1), new Vector2Int(-1, 0), new Vector2Int(1, 0) };
            var keyed = new List<(Vector2Int d, float key)>(4);
            foreach (var d in dirs) keyed.Add((d, -(d.x * ox + d.y * oy) + (float)rng.NextDouble() * 0.3f)); // outward (high dot) first
            keyed.Sort((a, b) => a.key.CompareTo(b.key));
            var res = new List<Vector2Int>(4);
            foreach (var kv in keyed) res.Add(kv.d);
            return res;
        }

        /// <summary>Authored levels. Parameters are tunable in the Inspector but still
        /// flow through the same solvable-by-construction core.</summary>
        public static LevelData Generate(LevelDefinition def)
        {
            int seed = def.seed != 0 ? def.seed : def.levelNumber * 9176 + 4242;
            var rng = new System.Random(seed);

            int colorCount = Mathf.Clamp(def.colorCount, 2, Palette.Count); // allow 2-color easy/bonus levels
            int busCount   = Mathf.Max(4, def.busCount);
            int baseSlots  = Mathf.Max(1, def.baseSlots);
            int extraSlots = Mathf.Max(0, def.extraSlots);

            return Build(rng, def.levelNumber, colorCount, busCount, Mathf.Max(3, def.gridWidth), def.gridHeight,
                         baseSlots, extraSlots,
                         Mathf.Clamp01(def.goldenChance), Mathf.Clamp01(def.mysteryChance), def.vehicleMix,
                         Mathf.Clamp01(def.specialChance), def.specialMaxAdvance, Mathf.Max(1, def.minRunLength));
        }

        // Shared solvable-by-construction core for both procedural and authored levels.
        static LevelData Build(System.Random rng, int levelNumber, int colorCount, int busCount,
            int gridWidth, int gridHeightHint, int baseSlots, int extraSlots,
            float goldenP, float mysteryP, VehicleMix mix,
            float specialChance, int specialMaxAdvance, int minRun)
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
            var groups    = BuildQueue(buses, rng, baseSlots, minRun, goldenP, mysteryP);

            // Layout VARIETY + difficulty ramp (solvability unchanged): cycle a shape per level, pack
            // denser as levels rise, and let HARDER levels use diagonals (true 8-way) while easy levels
            // stay 4-way like the reference.
            var style = (LayoutStyle)((Mathf.Max(1, levelNumber) - 1) % 4);
            float pack = Mathf.Lerp(1.7f, 1.35f, Mathf.Clamp01((levelNumber - 1) / 20f)); // more slack early, denser later
            bool allowDiagonals = levelNumber >= 6; // early high-count boards stay 4-way/readable; 6+ = 8-way
            var gridBuses = BuildGrid(buses, rng, gridWidth, gridHeightHint, style, pack, allowDiagonals, out int gridW, out int gridH);

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
                    // Limos removed — bias toward buses with some cars (no limo type generated).
                    return rng.Next(100) < 65 ? VehicleType.Bus : VehicleType.Car;
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
        static List<LineGroup> BuildQueue(List<BusDef> buses, System.Random rng, int window, int minRun, float goldenP, float mysteryP)
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
                // STICKY RUN: emit a chunk of THIS bus's color before picking again, so colors come out in
                // long same-color stretches instead of choppy 1-1-2 alternation. The run is clamped to what
                // remains, so each bus still emits EXACTLY its capacity (total people / "N Left" unchanged),
                // and we only ever emit from a bus that is currently `open` (window invariant -> servable).
                int floor = Mathf.Min(Mathf.Max(1, minRun), remaining[pick]);
                int run = floor + rng.Next(remaining[pick] - floor + 1); // uniform in [floor, remaining]
                for (int r = 0; r < run; r++) { flat.Add(buses[pick].color); remaining[pick]--; }
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
        static List<GridBus> BuildGrid(List<BusDef> buses, System.Random rng, int gridWidth, int gridHeightHint, LayoutStyle style, float pack, bool allowDiagonals, out int W, out int H)
        {
            int n = buses.Count;
            int totalCells = 0;
            for (int i = 0; i < n; i++) totalCells += Vehicles.CellLength(buses[i].type);

            // Size for ~pack x total cells (room for exit lanes) while keeping the board within the camera
            // envelope: widen first (W up to 8 -> stays on-screen even on tall 9:20), then deepen (H up to
            // 12). Bigger than before to hold the ~20-28 vehicle design (paired with CellSize=0.9).
            W = Mathf.Clamp(Mathf.Max(gridWidth, Mathf.CeilToInt(totalCells * pack / 8f)), 5, 8);
            H = gridHeightHint > 0 ? Mathf.Clamp(gridHeightHint, 3, 12)
                                   : Mathf.Clamp(Mathf.CeilToInt(totalCells * pack / W), 3, 12);

            var cardinals = new[] { new Vector2Int(0, -1), new Vector2Int(0, 1), new Vector2Int(-1, 0), new Vector2Int(1, 0) };
            var eight = new[] { new Vector2Int(0, -1), new Vector2Int(0, 1), new Vector2Int(-1, 0), new Vector2Int(1, 0),
                                new Vector2Int(-1, -1), new Vector2Int(1, -1), new Vector2Int(-1, 1), new Vector2Int(1, 1) };
            var occupied = new HashSet<Vector2Int>();
            var result = new GridBus[n];

            for (int k = n - 1; k >= 0; k--)
            {
                int L = Vehicles.CellLength(buses[k].type);
                // Special "<<" crawlers stay CARDINAL for clean per-tap framing (the unified occ rewrite is
                // diagonal-safe via the shared OccCells; this is a design choice, not a geometry limit).
                var dirs = (allowDiagonals && buses[k].advanceN == 0) ? eight : cardinals;
                bool placed = false;
                int guard = 0;
                while (!placed)
                {
                    // Try cells in the layout-style's preferred order (ring/cross/diamond first), but ALL
                    // cells are still tried -> placement still succeeds whenever a valid spot exists.
                    var cells = StyleOrderedCells(W, H, style, rng);
                    foreach (var anchor in cells)
                    {
                        // L>=2 buses lose the random fit lottery for their thick (2x2) diagonal footprint, so
                        // bias the TRY-ORDER toward diagonals first (every dir is still tried -> solvability
                        // unchanged). Cars (L==1) and cardinal-only sets keep the flat shuffle.
                        List<Vector2Int> ds;
                        if (L >= 2 && dirs.Length == 8)
                        {
                            var diag = new List<Vector2Int>(4);
                            var card = new List<Vector2Int>(4);
                            foreach (var d in dirs) { if (d.x != 0 && d.y != 0) diag.Add(d); else card.Add(d); }
                            Shuffle(diag, rng); Shuffle(card, rng);
                            ds = new List<Vector2Int>(8);
                            ds.AddRange(diag); ds.AddRange(card); // diagonals first, then cardinals
                        }
                        else
                        {
                            ds = new List<Vector2Int>(dirs);
                            Shuffle(ds, rng);
                        }
                        foreach (var d in ds)
                        {
                            // BodyFree + SlideClear use the SAME OccCells footprint the runtime uses, so a
                            // diagonal vehicle's thick footprint + corner-sweep are placed solvably.
                            if (BodyFree(anchor, d, L, occupied, W, H) && SlideClear(anchor, d, L, occupied.Contains, W, H))
                            {
                                result[k] = new GridBus { color = buses[k].color, type = buses[k].type, capacity = buses[k].capacity, cell = anchor, dir = d, advanceN = buses[k].advanceN };
                                foreach (var c in OccCells(anchor, d, L)) occupied.Add(c);
                                placed = true;
                                break;
                            }
                        }
                        if (placed) break;
                    }
                    if (!placed)
                    {
                        // Cap growth so the deepest normally-placed row (c.y up to H-1) stays within the
                        // camera's bottom edge. With GridExitZ=3.6 / CellSize=0.9, H=13 -> deepest row
                        // z=-7.2 (on-screen, ~1.5u margin); taller would clip. 9x13=117 cells comfortably
                        // holds the densest set (~28 vehicles incl. diagonal footprints), so this rarely bites.
                        if (H < 13) H++;
                        if (guard++ > 8)
                        {
                            // extreme fallback (effectively never fires — stress-tested 0/11000): drop the
                            // vehicle exiting left, but on the DEEPEST ON-SCREEN row (<=12) and never grow H
                            // past 13, so no vehicle can ever land below the camera's bottom edge.
                            var d = new Vector2Int(-1, 0);
                            var anchor = new Vector2Int(0, Mathf.Min(H, 12));
                            result[k] = new GridBus { color = buses[k].color, type = buses[k].type, capacity = buses[k].capacity, cell = anchor, dir = d, advanceN = buses[k].advanceN };
                            foreach (var c in OccCells(anchor, d, L)) occupied.Add(c);
                            H = Mathf.Min(H + 2, 13); placed = true;
                        }
                    }
                }
            }

            return new List<GridBus>(result);
        }

        // The STATIC cells a vehicle occupies. Cardinal: L body cells (anchor - dir*i). Diagonal: those
        // L body cells PLUS the two corner cells swept between each consecutive pair (a thick diagonal
        // stripe), so a 45deg-rotated body can't overlap a neighbour. SINGLE source of truth, used by the
        // generator AND BusJamGame's occ bookkeeping + slide checks.
        public static List<Vector2Int> OccCells(Vector2Int cell, Vector2Int dir, int L)
        {
            var list = new List<Vector2Int>(L * 2);
            bool diag = dir.x != 0 && dir.y != 0;
            for (int i = 0; i < L; i++)
            {
                var b = cell - dir * i;
                list.Add(b);
                if (diag && i < L - 1)
                {
                    list.Add(new Vector2Int(b.x - dir.x, b.y)); // corners between b and the next body cell
                    list.Add(new Vector2Int(b.x, b.y - dir.y));
                }
            }
            return list;
        }

        // All occupied cells of the placed vehicle are in-grid and free.
        static bool BodyFree(Vector2Int anchor, Vector2Int dir, int L, HashSet<Vector2Int> occ, int W, int H)
        {
            foreach (var c in OccCells(anchor, dir, L))
            {
                if (c.x < 0 || c.x >= W || c.y < 0 || c.y >= H) return false;
                if (occ.Contains(c)) return false;
            }
            return true;
        }

        // Can the vehicle slide along `dir` fully off the board? Every NEW cell its footprint enters
        // (incl. the diagonal corners it sweeps) must be free. Cardinal reduces to "check cell+dir onward"
        // exactly as before. SHARED by the generator (placement) and TryTapBus (runtime) so they never
        // disagree -> solvable-by-construction holds for 8-way levels.
        public static bool SlideClear(Vector2Int cell, Vector2Int dir, int L, System.Func<Vector2Int, bool> occupied, int W, int H)
        {
            bool InG(Vector2Int p) => p.x >= 0 && p.x < W && p.y >= 0 && p.y < H;
            var own = new HashSet<Vector2Int>(OccCells(cell, dir, L));
            bool diag = dir.x != 0 && dir.y != 0;
            var p = cell;
            while (true)
            {
                var next = p + dir;
                if (diag) // corner-sweep (needed for L=1; harmless/redundant for L>1)
                {
                    var ca = new Vector2Int(next.x, p.y);
                    var cb = new Vector2Int(p.x, next.y);
                    if (InG(ca) && !own.Contains(ca) && occupied(ca)) return false;
                    if (InG(cb) && !own.Contains(cb) && occupied(cb)) return false;
                }
                bool anyInGrid = false;
                foreach (var c in OccCells(next, dir, L))
                {
                    if (!InG(c)) continue;
                    anyInGrid = true;
                    if (!own.Contains(c) && occupied(c)) return false;
                    own.Add(c);
                }
                if (!anyInGrid) return true; // whole body has cleared the board
                p = next;
            }
        }

        // How many forward steps along `dir` the footprint can take while staying fully IN-GRID, stopping at
        // the first step that is blocked OR would push any body cell off the board. Same OccCells + diagonal
        // corner-sweep geometry as SlideClear (cardinal reduces to the old single-cell walk), so the runtime
        // crawl reposition stays consistent. RUNTIME-only -> generator clearance functions are untouched.
        public static int MaxAdvanceSteps(Vector2Int cell, Vector2Int dir, int L, System.Func<Vector2Int, bool> occupied, int W, int H, int cap)
        {
            bool InG(Vector2Int p) => p.x >= 0 && p.x < W && p.y >= 0 && p.y < H;
            var own = new HashSet<Vector2Int>(OccCells(cell, dir, L));
            bool diag = dir.x != 0 && dir.y != 0;
            var p = cell;
            int steps = 0;
            while (steps < cap)
            {
                var next = p + dir;
                if (diag) // a swept corner that's occupied blocks the step (mirrors SlideClear)
                {
                    var ca = new Vector2Int(next.x, p.y);
                    var cb = new Vector2Int(p.x, next.y);
                    if (InG(ca) && !own.Contains(ca) && occupied(ca)) break;
                    if (InG(cb) && !own.Contains(cb) && occupied(cb)) break;
                }
                bool ok = true;
                foreach (var c in OccCells(next, dir, L))
                {
                    if (!InG(c)) { ok = false; break; }                          // would leave the board (no in-grid advance)
                    if (!own.Contains(c) && occupied(c)) { ok = false; break; }  // blocked
                }
                if (!ok) break;
                foreach (var c in OccCells(next, dir, L)) own.Add(c);
                p = next; steps++;
            }
            return steps;
        }

        static List<Vector2Int> AllCells(int W, int H)
        {
            var list = new List<Vector2Int>(W * H);
            for (int y = 0; y < H; y++)
                for (int x = 0; x < W; x++)
                    list.Add(new Vector2Int(x, y));
            return list;
        }

        // ALL cells, ordered so the layout style's preferred cells are tried first (lower key first).
        // Jitter (<1) keeps the score TIERS in order but randomizes within a tier for variety. This
        // only changes the try-order, never which placements are legal -> solvability is untouched.
        static List<Vector2Int> StyleOrderedCells(int W, int H, LayoutStyle style, System.Random rng)
        {
            float cx = (W - 1) * 0.5f, cy = (H - 1) * 0.5f;
            var keyed = new List<(Vector2Int cell, float key)>(W * H);
            for (int y = 0; y < H; y++)
                for (int x = 0; x < W; x++)
                {
                    float dx = Mathf.Abs(x - cx), dy = Mathf.Abs(y - cy);
                    float score;
                    switch (style)
                    {
                        case LayoutStyle.Ring:    score = -Mathf.Max(dx, dy); break;  // outer ring first
                        case LayoutStyle.Cross:   score = Mathf.Min(dx, dy);  break;  // central row/column first
                        case LayoutStyle.Diamond: score = -(dx + dy);         break;  // diamond tips/edges first
                        default:                  score = 0f;                 break;  // Scatter (fully random)
                    }
                    keyed.Add((new Vector2Int(x, y), score + (float)rng.NextDouble() * 0.9f));
                }
            keyed.Sort((a, b) => a.key.CompareTo(b.key));
            var ordered = new List<Vector2Int>(keyed.Count);
            foreach (var kv in keyed) ordered.Add(kv.cell);
            return ordered;
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
