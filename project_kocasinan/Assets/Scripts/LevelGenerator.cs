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
        public bool mystery;   // spawns GRAY (color hidden) like a mystery person; reveals when it could fully drive out
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
    public enum LayoutStyle { Scatter, Ring, Cross, Diamond, Heart, Circle, Triangle, Plus, XShape }

    public static class LevelGenerator
    {
        public const int BaseSlots = 4;   // unlocked parking == BuildQueue servability window (4 OPEN stops)
        public const int ExtraSlots = 3;  // locked: 1 opens by AD, 2 by COINS -> 7 total pads

        /// <summary>Procedural levels (used for 6+ and as the fallback). Gets harder
        /// as the level rises: more colors, more buses, more specials.</summary>
        public static LevelData Generate(int level, LayoutStyle? forceStyle = null, float forceMysteryP = -1f, bool shapeFill = false)
        {
            var rng = new System.Random(level * 9176 + 4242);

            if (level % 10 == 0) return GenerateBonus(level, rng); // every 10th = 4-colour core-boxed-by-ring bonus

            if (shapeFill && forceStyle.HasValue)
            {
                // Coin Rush: fill the EXACT shape silhouette with uniform cap-2 cars on the MAX grid (W6xH9) so it reads
                // crisply. Car count == the shape's cell count, and the shape style fills those cells first.
                int carCount = Mathf.Clamp(ShapeCount(6, 9, forceStyle.Value), 12, 26);
                return Build(rng, level, 2, carCount, 6, 9, BaseSlots, ExtraSlots,
                             0.10f, 0f, MixForLevel(level), 0f, 4, 4, forceStyle, forceMysteryP, forceAllCars: true);
            }

            // MANY vehicles every level (easy-but-many at L1); difficulty rises via colors/specials/
            // diagonals/density, NOT count. Every ramp below keeps climbing DEEP into the game (the old caps
            // all plateaued by ~L25, which made L30+ feel identical) — each knob is solvability-safe.
            int colorCount = Mathf.Clamp(2 + (level - 1) / 3, 2, Palette.Count); // L1-3 = 2 colors, +1 every 3 -> all 8 by L19
            // Vehicle count: 16 at L1 -> 19 by L25 (the stress-tested big-jam band), then keeps growing +1 every
            // 12 levels to 24 by L85. Still WELL under the board's proven capacity: the bonus generator packs 32
            // vehicles (~42 cells) onto the same W6/H9 board with the same BodyFree+SlideClear rules; 24 normal
            // vehicles is ~31 of 54 cells, and placement retries grow H whenever a lane can't be cleared.
            int busCount   = level <= 25 ? Mathf.Clamp(14 + level / 5, 16, 19)
                                         : Mathf.Min(24, 19 + (level - 25) / 12); // 20 at L37, 21 at L49 ... 24 at L85
            float goldenP  = Mathf.Min(0.10f, level * 0.01f);
            float mysteryP = Mathf.Min(0.45f, Mathf.Max(0, level - 4) * 0.03f); // gray queue people: none until L5, up to 45% by L20 (was capped 30%). Cosmetic reveal-timing only -> solvability unchanged.

            // Special "<<" crawlers ramp in later (boards are denser now); none early. Cap raised 20% -> 30%.
            float specialP = level < 10 ? 0f : Mathf.Min(0.30f, (level - 9) * 0.03f);

            // QUEUE CHOPPINESS — the biggest late-game lever. minRun is the guaranteed same-color chunk length
            // in the queue: 4 = long friendly stretches (easy to plan), 1 = fully choppy alternation (must juggle
            // several colors' buses at once). Purely an emission-order knob: BuildQueue's window invariant and
            // distinct-open-colours servability fix hold for ANY run length, so solvability is untouched.
            int minRun = level < 12 ? 4 : level < 35 ? 3 : level < 70 ? 2 : 1;

            return Build(rng, level, colorCount, busCount, 6, 0, BaseSlots, ExtraSlots,
                         goldenP, mysteryP, MixForLevel(level), specialP, 4, minRun, forceStyle, forceMysteryP);
        }

        // Gentle ramp across the THREE vehicle types: cap-4 cars only at first (easy + many + few people),
        // then 6-seat minivans join, then 10-seat buses complete the set. (Tune the level thresholds freely.)
        static VehicleMix MixForLevel(int level)
        {
            if (level <= 3) return VehicleMix.CarsOnly;        // L1-3: small 4-seat cars
            if (level <= 6) return VehicleMix.CarsAndMinivans; // L4-6: add 6-seat minivans
            return VehicleMix.AllThree;                        // L7+: cars + minivans + buses
        }

        // ---- BONUS levels (every 10th): a DENSELY-PACKED jam of mixed cars/minivans/buses in TWO colours:
        // ONE core-colour vehicle trapped in the dead CENTER (extracted LAST), every other vehicle the fill
        // colour. Reverse-placed center-out with the SAME BodyFree+SlideClear, every vehicle exiting outward
        // -> solvable (clear from the outside in to free the middle one).
        static LevelData GenerateBonus(int level, System.Random rng)
        {
            const int busCount = 32; // MANY mixed vehicles, densely packed (T3 big-jam: fits the W6/H9 board; stress-tested 0/0)
            // TWO colours (the classic bonus look): one fill colour everywhere, one different core colour in the
            // middle. Servability with only 2 colours cannot use BuildQueue's distinct-open-colours trick
            // (window=4 > 2 colours), so it is guaranteed structurally instead:
            //  - every FILL bus is the SAME colour, so a fill person can board ANY open fill bus. The buses are
            //    interchangeable and total capacity == total fill people, so forced front-of-queue boarding can
            //    never fill the "wrong" one — there is no wrong one — and can never strand a person.
            //  - every CORE-colour person is stable-moved to the END of the queue (below), matching the core being
            //    extracted LAST (it is boxed in the dead center): its people arrive exactly when it can park.
            // This is the previously stress-verified configuration (0 unsolvable across all bonus levels 10..1500).
            // The PAIR varies per bonus (red+yellow, blue+red, green+blue, ...): drawn from a SEPARATE rng seeded by
            // the level so the main rng stream — and therefore the verified board geometry — is untouched.
            var colorRng = new System.Random(level * 733 + 91);
            int fillIdx = colorRng.Next(Palette.Count);
            int coreIdx = (fillIdx + 1 + colorRng.Next(Palette.Count - 1)) % Palette.Count; // any colour EXCEPT the fill
            PieceColor FillColor = (PieceColor)fillIdx;
            PieceColor CoreColor = (PieceColor)coreIdx;

            var buses = new List<BusDef>(busCount);
            for (int i = 0; i < busCount; i++)
            {
                bool isCore = (i == busCount - 1); // LAST index = center, extracted LAST (after everything else clears)
                int rt = rng.Next(10);                                                    // spread across all 3 types
                var type = isCore ? VehicleType.Bus                                       // trapped centre piece = a bus
                                  : (rt < 4 ? VehicleType.Car : (rt < 7 ? VehicleType.Minivan : VehicleType.Bus)); // ~40% car, 30% minivan, 30% bus
                buses.Add(new BusDef { color = isCore ? CoreColor : FillColor, type = type,
                                       capacity = Vehicles.DefaultCapacity(type), advanceN = 0 });
            }

            // colorCount <= 0 -> BuildQueue KEEPS the authored colours above (no distinct-open reassignment). The
            // rng consumption inside BuildQueue is colour-independent, so the grid built afterwards is byte-identical
            // to the verified geometry.
            var groups = BuildQueue(buses, rng, BaseSlots, 4, 0f, 0f, 0);
            // Stable partition: every core-colour person goes to the END of the queue (relative order otherwise
            // preserved). Safe ONLY because the fill buses are all one interchangeable colour — see the note above.
            var reordered = new List<LineGroup>(groups.Count);
            foreach (var g in groups) if (g.color != CoreColor) reordered.Add(g);
            foreach (var g in groups) if (g.color == CoreColor) reordered.Add(g);
            groups = reordered;

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
            W = Mathf.Clamp(Mathf.CeilToInt(Mathf.Sqrt(budget)), 5, 6);   // zoomed-camera caps (CellSize 1.1, big jam)
            H = Mathf.Clamp(Mathf.CeilToInt(budget / W), 5, 9);

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
                        if (H < 9) H++;
                        if (guard++ > 8) // extreme fallback (on-screen): deepest row <=8, never grow past 9
                        {
                            var d = new Vector2Int(-1, 0);
                            var anchor = new Vector2Int(0, Mathf.Min(H, 8));
                            result[k] = new GridBus { color = buses[k].color, type = buses[k].type, capacity = buses[k].capacity, cell = anchor, dir = d, advanceN = 0 };
                            foreach (var c in OccCells(anchor, d, L)) occupied.Add(c);
                            H = Mathf.Min(H + 2, 9); placed = true;
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
            float specialChance, int specialMaxAdvance, int minRun,
            LayoutStyle? forceStyle = null, float forceMysteryP = -1f, bool forceAllCars = false)
        {
            // FIXED slot layout for EVERY level (procedural + authored): exactly BaseSlots OPEN stops +
            // ExtraSlots locked = 7 pads. Overriding here (not trusting def.baseSlots) keeps it uniform; the
            // queue/grid below are then built solvable-by-construction with this 4-slot servability window.
            baseSlots = BaseSlots; extraSlots = ExtraSlots;
            int maxAdvance = Mathf.Max(2, specialMaxAdvance); // N >= 2 so a special always makes progress on a clear lane
            var buses = new List<BusDef>(busCount);
            for (int i = 0; i < busCount; i++)
            {
                var type = forceAllCars ? VehicleType.Car : PickType(mix, rng); // Coin Rush shapes: uniform 1-cell cars read crisply
                int cap = forceAllCars ? 2 : CapacityFor(type, mix, rng);       // small capacity keeps the dense shape quick to clear
                // advanceN is ORTHOGONAL to placement/capacity, so it never affects solvability.
                int advanceN = (specialChance > 0f && rng.NextDouble() < specialChance) ? rng.Next(2, maxAdvance + 1) : 0;
                buses.Add(new BusDef { color = (PieceColor)rng.Next(colorCount), type = type, capacity = cap, advanceN = advanceN });
            }

            // BuildQueue emits exactly `capacity` people per vehicle, so total people ==
            // total seats per color -> every vehicle fills exactly -> always winnable.
            // colorCount<=0 keeps authored colours untouched: Coin Rush shape-fill (forceAllCars) is a uniform 2-colour
            // silhouette that never had the aliasing deadlock, so leave its colours exactly as authored.
            var groups    = BuildQueue(buses, rng, baseSlots, minRun, goldenP, GameConfig.FeatureMystery ? mysteryP : 0f, forceAllCars ? 0 : colorCount);

            // Layout VARIETY + difficulty ramp (solvability unchanged): cycle a shape per level, pack
            // denser as levels rise, and let HARDER levels use diagonals (true 8-way) while easy levels
            // stay 4-way like the reference.
            // Early levels cycle the 4 gentle styles; from L24 the FULL 9-style set joins (Heart/Circle/Triangle/
            // Plus/X make more interlocked, blockier jams). Style biases anchor try-order ONLY -> solvability safe.
            var style = forceStyle ?? (LayoutStyle)((Mathf.Max(1, levelNumber) - 1) % (levelNumber < 24 ? 4 : 9));
            // Density keeps climbing past the old L21 plateau: 1.7 -> 1.35 by L21, then on to 1.2 by ~L81.
            // pack only sets the INITIAL board size; placement retries still grow H whenever BodyFree/SlideClear
            // can't be satisfied, so a tighter pack means a tighter jam, never a broken one.
            float pack = levelNumber <= 21
                ? Mathf.Lerp(1.7f, 1.35f, Mathf.Clamp01((levelNumber - 1) / 20f))   // more slack early, denser later
                : Mathf.Lerp(1.35f, 1.2f, Mathf.Clamp01((levelNumber - 21) / 60f)); // late game: denser still
            bool allowDiagonals = levelNumber >= 6 && GameConfig.FeatureDiagonals; // early high-count boards stay 4-way/readable; 6+ = 8-way (remote flag off => 4-way everywhere)
            // MYSTERY vehicles (spawn GRAY, color hidden until they could fully drive out): start at level 11 and
            // grow with difficulty, capped at 30% of the board. 0 before L11 -> short-circuits the rng so early
            // level layouts are byte-for-byte unchanged.
            float vehicleMysteryP = forceMysteryP >= 0f ? forceMysteryP : (GameConfig.FeatureMystery ? Mathf.Min(0.40f, Mathf.Max(0, levelNumber - 10) * 0.03f) : 0f); // cap raised 30% -> 40% (hits at L24)
            var gridBuses = BuildGrid(buses, rng, gridWidth, gridHeightHint, style, pack, allowDiagonals, vehicleMysteryP, out int gridW, out int gridH);

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
                case VehicleMix.CarsAndMinivans: return rng.Next(2) == 0 ? VehicleType.Car : VehicleType.Minivan;
                case VehicleMix.AllThree:
                    // Even-ish spread over the three types.
                    { int r = rng.Next(3); return r == 0 ? VehicleType.Car : (r == 1 ? VehicleType.Minivan : VehicleType.Bus); }
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
        static List<LineGroup> BuildQueue(List<BusDef> buses, System.Random rng, int window, int minRun, float goldenP, float mysteryP, int colorCount)
        {
            int n = buses.Count;
            window = Mathf.Clamp(window, 1, n);
            var remaining = new int[n];
            for (int i = 0; i < n; i++) remaining[i] = buses[i].capacity;

            // SERVABILITY: (re)assign each bus a color AS IT OPENS, distinct from every currently-open bus's color.
            // The intended solution parks buses in index order and boards the FRONT queue person onto the matching
            // PARKED bus (FindParkedBus, by color). The window guarantees the front color always belongs to some open
            // bus — but if two open buses shared a color, forced boarding could fill the "wrong" one and strand a later
            // color, deadlocking a "guaranteed-solvable" level (this was the real ~19%-of-levels bug). Making the open
            // set colour-distinct means each queue colour maps to exactly ONE parked bus, so boarding can never
            // mis-route, for ANY runtime tie-break. Deterministic (consumes NO rng), so the grid built afterwards is
            // byte-identical. Colours are written back to `buses` so the grid carries the same assignment. When
            // colorCount <= 0 (bonus fill/core), the authored colours are kept; when colorCount < the concurrently-
            // open count, distinctness is impossible but ALSO unnecessary (few-colour boards can't strand a colour).
            var color = new PieceColor[n];
            var open = new List<int>();
            void OpenBus(int idx)
            {
                if (colorCount <= 0) { color[idx] = buses[idx].color; open.Add(idx); return; }
                var used = new HashSet<int>();
                foreach (var o in open) used.Add((int)color[o]);
                int c = 0; while (c < colorCount && used.Contains(c)) c++;
                color[idx] = (PieceColor)(c < colorCount ? c : idx % colorCount);
                open.Add(idx);
            }

            var flat = new List<PieceColor>();
            int nextToOpen = Mathf.Min(window, n);
            for (int i = 0; i < nextToOpen; i++) OpenBus(i);
            while (open.Count > 0)
            {
                int pick = open[rng.Next(open.Count)];
                // STICKY RUN: emit a chunk of THIS bus's color before picking again, so colors come out in
                // long same-color stretches instead of choppy 1-1-2 alternation. The run is clamped to what
                // remains, so each bus still emits EXACTLY its capacity (total people / "N Left" unchanged),
                // and we only ever emit from a bus that is currently `open` (window invariant -> servable).
                int floor = Mathf.Min(Mathf.Max(1, minRun), remaining[pick]);
                int run = floor + rng.Next(remaining[pick] - floor + 1); // uniform in [floor, remaining]
                for (int r = 0; r < run; r++) { flat.Add(color[pick]); remaining[pick]--; }
                if (remaining[pick] == 0)
                {
                    open.Remove(pick);
                    if (nextToOpen < n) { OpenBus(nextToOpen); nextToOpen++; }
                }
            }
            // Carry the servable colour assignment into the grid (placement is colour-independent, so geometry is
            // unchanged — only which colour rides on each vehicle).
            for (int i = 0; i < n; i++) { var b = buses[i]; b.color = color[i]; buses[i] = b; }

            // One single person per emitted color.
            var groups = new List<LineGroup>();
            foreach (var c in flat) groups.Add(new LineGroup { color = c });

            for (int i = 0; i < groups.Count; i++)
            {
                // Golden passengers removed (economy rework): gold comes ONLY from the flat per-level reward, never from
                // people/vehicles. goldenP is intentionally ignored so no "golden" person ever spawns.
                if (i >= 2 && rng.NextDouble() < mysteryP) groups[i].mystery = true;
            }
            return groups;
        }

        // ---- Grid (reverse generation: always solvable) ----------------------
        // Multi-cell: each vehicle occupies CellLength(type) cells in a line along its exit
        // direction. Placing in reverse so that, for every k, the body cells are free AND the
        // exit lane ahead is clear, guarantees forward extraction (0..n-1) is always solvable:
        // when vehicle k leaves, all later-placed vehicles are still clear of its lane.
        static List<GridBus> BuildGrid(List<BusDef> buses, System.Random rng, int gridWidth, int gridHeightHint, LayoutStyle style, float pack, bool allowDiagonals, float vehicleMysteryP, out int W, out int H)
        {
            int n = buses.Count;
            int totalCells = 0;
            for (int i = 0; i < n; i++) totalCells += Vehicles.CellLength(buses[i].type);

            // Size for ~pack x total cells (room for exit lanes) within the ZOOMED camera envelope: W up to 6
            // (big CellSize=1.1 -> a 6-wide jam fills the portrait width at FOV54), H up to 9 (raised GridExitZ
            // keeps the deepest row on-screen). Holds the big-jam set (busCount<=19) in W6xH9=54 cells with slack.
            W = Mathf.Clamp(Mathf.Max(gridWidth, Mathf.CeilToInt(totalCells * pack / 8f)), 5, 6);
            H = gridHeightHint > 0 ? Mathf.Clamp(gridHeightHint, 3, 9)
                                   : Mathf.Clamp(Mathf.CeilToInt(totalCells * pack / W), 3, 9);

            var cardinals = new[] { new Vector2Int(0, -1), new Vector2Int(0, 1), new Vector2Int(-1, 0), new Vector2Int(1, 0) };
            var eight = new[] { new Vector2Int(0, -1), new Vector2Int(0, 1), new Vector2Int(-1, 0), new Vector2Int(1, 0),
                                new Vector2Int(-1, -1), new Vector2Int(1, -1), new Vector2Int(-1, 1), new Vector2Int(1, 1) };
            var occupied = new HashSet<Vector2Int>();
            var result = new GridBus[n];

            // WHOLE-BOARD RETRY: if some vehicle can't be placed legally even at H=9, RESTART the whole board with
            // fresh rng jitter instead of force-dropping it. The old force-drop skipped BodyFree, so on a board that
            // was genuinely too tight it could OVERLAP two vehicles (headless verification found 4 such levels in
            // 1..1000: 101/235/578/866 after the difficulty ramp densified late boards). A restart re-rolls every
            // try-order, which is virtually always enough slack; levels that never needed the fallback consume the
            // SAME rng draws as before -> their layouts are byte-identical.
            int Hinit = H;
            for (int attempt = 0; ; attempt++)
            {
                bool lastResort = attempt >= 23; // 24th attempt: accept the legacy fallback rather than loop forever
                // ESCALATION: a board that keeps failing is over-packed by DIAGONAL vehicles (their thick swept
                // footprint eats ~2x the cells of a straight one). From attempt 12 on, place straight-only; from
                // attempt 18 on, use the BONUS packer's strategy (center-out fill, exits toward the nearest edge)
                // which provably packs 32 vehicles (~42 cells) on this same 6x9 board — denser than any normal level.
                bool diagAllowedThisAttempt = allowDiagonals && attempt < 12;
                bool bonusStylePacking = attempt >= 18;
                occupied.Clear();
                H = Hinit;
                bool restart = false;

                for (int k = n - 1; k >= 0 && !restart; k--)
                {
                    int L = Vehicles.CellLength(buses[k].type);
                    // Roll mystery ONCE per vehicle (short-circuits so P==0 levels never touch rng -> identical layout).
                    bool isMystery = vehicleMysteryP > 0f && rng.NextDouble() < vehicleMysteryP;
                    // Special "<<" crawlers stay CARDINAL for clean per-tap framing (the unified occ rewrite is
                    // diagonal-safe via the shared OccCells; this is a design choice, not a geometry limit).
                    var dirs = (diagAllowedThisAttempt && buses[k].advanceN == 0) ? eight : cardinals;
                    bool placed = false;
                    int guard = 0;
                    while (!placed)
                    {
                        // Try cells in the layout-style's preferred order (ring/cross/diamond first), but ALL
                        // cells are still tried -> placement still succeeds whenever a valid spot exists.
                        // (Bonus-style escalation: center-out order + outward exits — the proven dense packer.)
                        var cells = bonusStylePacking ? BoxOrderedCells(W, H, rng, centerFirst: true)
                                                      : StyleOrderedCells(W, H, style, rng);
                        foreach (var anchor in cells)
                        {
                            // BALANCE straight vs diagonal: prefer cardinals MOST of the time, diagonals only ~35%,
                            // so the board is a healthy mix (not all-diagonal) for cars AND buses. Every dir is still
                            // tried, so solvability is unchanged. Cardinal-only sets (crawlers / levels <6) stay flat.
                            List<Vector2Int> ds;
                            if (bonusStylePacking)
                                ds = OutwardDirs(anchor, W, H, rng); // exit toward the nearest edge (clear lanes on a packed board)
                            else if (dirs.Length == 8)
                            {
                                var diag = new List<Vector2Int>(4);
                                var card = new List<Vector2Int>(4);
                                foreach (var d in dirs) { if (d.x != 0 && d.y != 0) diag.Add(d); else card.Add(d); }
                                Shuffle(diag, rng); Shuffle(card, rng);
                                ds = new List<Vector2Int>(8);
                                if (rng.NextDouble() < 0.35) { ds.AddRange(diag); ds.AddRange(card); } // ~35% prefer diagonal
                                else { ds.AddRange(card); ds.AddRange(diag); }                          // ~65% prefer straight
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
                                    result[k] = new GridBus { color = buses[k].color, type = buses[k].type, capacity = buses[k].capacity, cell = anchor, dir = d, advanceN = buses[k].advanceN, mystery = isMystery };
                                    foreach (var c in OccCells(anchor, d, L)) occupied.Add(c);
                                    placed = true;
                                    break;
                                }
                            }
                            if (placed) break;
                        }
                        if (!placed)
                        {
                            // Cap growth so the deepest row (c.y up to H-1) stays within the camera's bottom edge.
                            // With GridExitZ=5.5 / CellSize=1.1 and the camera (FOV54), H=9 -> deepest row edge
                            // z=-3.85 (on-screen, ndc 0.94).
                            if (H < 9) H++;
                            if (guard++ > 8)
                            {
                                if (!lastResort) { restart = true; break; } // re-roll the WHOLE board (see note above)
                                // absolute last resort (never reached in 1..2000 headless verification): place at the
                                // first BODY-FREE spot so at least nothing overlaps; blind edge-drop only if even
                                // that fails.
                                bool dropped = false;
                                foreach (var anchor in StyleOrderedCells(W, H, style, rng))
                                {
                                    foreach (var d in cardinals)
                                        if (BodyFree(anchor, d, L, occupied, W, H))
                                        {
                                            result[k] = new GridBus { color = buses[k].color, type = buses[k].type, capacity = buses[k].capacity, cell = anchor, dir = d, advanceN = buses[k].advanceN, mystery = isMystery };
                                            foreach (var c in OccCells(anchor, d, L)) occupied.Add(c);
                                            dropped = true; break;
                                        }
                                    if (dropped) break;
                                }
                                if (!dropped)
                                {
                                    var d = new Vector2Int(-1, 0);
                                    var anchor = new Vector2Int(0, Mathf.Min(H, 8));
                                    result[k] = new GridBus { color = buses[k].color, type = buses[k].type, capacity = buses[k].capacity, cell = anchor, dir = d, advanceN = buses[k].advanceN, mystery = isMystery };
                                    foreach (var c in OccCells(anchor, d, L)) occupied.Add(c);
                                }
                                H = Mathf.Min(H + 2, 9); placed = true;
                            }
                        }
                    }
                }

                if (!restart) break; // whole board placed legally
            }

            // GUARANTEE at least one mystery vehicle on every level past 10. The per-vehicle roll can come up
            // empty at the low early chances (3% at L11), so if none landed, force a random vehicle gray. Bonus
            // levels use BuildBonusGrid (not this), so they stay mystery-free. Runs AFTER placement (no layout
            // impact) and only when P>0, so levels <=10 never touch rng here -> identical to before.
            if (vehicleMysteryP > 0f)
            {
                bool any = false;
                for (int k = 0; k < n; k++) if (result[k].mystery) { any = true; break; }
                if (!any && n > 0) result[rng.Next(n)].mystery = true;
            }

            return new List<GridBus>(result);
        }

        // The STATIC cells a vehicle occupies. Cardinal: L body cells (anchor - dir*i). Diagonal: those L body
        // cells PLUS the two corner cells the 45deg-rotated body covers between each consecutive pair, so a
        // diagonal vehicle reserves what it visually overlaps (no corner mesh) and can't slide THROUGH a
        // corner-blocked neighbour. SINGLE source of truth: generator placement AND runtime occ/slide checks.
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
                    list.Add(new Vector2Int(b.x - dir.x, b.y)); // corners covered between b and the next body cell
                    list.Add(new Vector2Int(b.x, b.y - dir.y));
                }
            }
            return list;
        }

        // The vehicle's L BODY cells ONLY (the thin line along its axis) — NO swept corner cells. A tilted
        // (diagonal) vehicle's real body is a thin 45° strip that does NOT fill the staircase corners, so MOVEMENT
        // clearance uses this (a diagonal car only needs its own lane, not the corner a neighbour merely touches in
        // the grid). Placement still uses the thick OccCells so nothing ever SPAWNS overlapping. Cardinal: identical.
        public static List<Vector2Int> OccBodyCells(Vector2Int cell, Vector2Int dir, int L)
        {
            var list = new List<Vector2Int>(L);
            for (int i = 0; i < L; i++) list.Add(cell - dir * i);
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

        // Can the vehicle slide along `dir` fully off the board? Every NEW cell its footprint enters (incl. the
        // diagonal corners it sweeps) must be free, so a diagonal vehicle can't squeeze THROUGH a corner-blocked
        // gap. Cardinal reduces to "check cell+dir onward". SHARED by the generator (placement) and TryTapBus
        // (runtime) so they never disagree -> solvable-by-construction holds.
        public static bool SlideClear(Vector2Int cell, Vector2Int dir, int L, System.Func<Vector2Int, bool> occupied, int W, int H)
        {
            bool InG(Vector2Int p) => p.x >= 0 && p.x < W && p.y >= 0 && p.y < H;
            var own = new HashSet<Vector2Int>(OccCells(cell, dir, L));
            bool diag = dir.x != 0 && dir.y != 0;
            var p = cell;
            while (true)
            {
                var next = p + dir;
                if (diag) // corner-sweep: the rotated body clips a diagonally-adjacent occupied cell, so it can't drive THROUGH it
                {
                    var ca = new Vector2Int(next.x, p.y);
                    var cb = new Vector2Int(p.x, next.y);
                    if (InG(ca) && !own.Contains(ca) && occupied(ca)) return false;
                    if (InG(cb) && !own.Contains(cb) && occupied(cb)) return false;
                }
                bool anyInGrid = false;
                foreach (var c in OccCells(next, dir, L)) // FULL footprint (incl. swept corners) must be clear -> a diagonal body never meshes into a neighbour while it drives
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
        // corner-sweep geometry as SlideClear, so the runtime crawl reposition stays consistent.
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
                        case LayoutStyle.Scatter: score = 0f;                 break;  // fully random
                        // filled SILHOUETTE shapes (Coin Rush): INSIDE cells fill first, centre-out (constructor-friendly), outside far back
                        default:                  score = InShape(x, y, cx, cy, W, H, style) ? (dx + dy) : 40f + dx + dy; break;
                    }
                    keyed.Add((new Vector2Int(x, y), score + (float)rng.NextDouble() * 0.9f));
                }
            keyed.Sort((a, b) => a.key.CompareTo(b.key));
            var ordered = new List<Vector2Int>(keyed.Count);
            foreach (var kv in keyed) ordered.Add(kv.cell);
            return ordered;
        }

        // Hand-authored 6-wide silhouettes for the Coin Rush shape levels. Row 0 = FRONT (lowest y, nearest the
        // camera / bottom of screen); '#' = a car cell, '.' = empty. Kept compact (~16-20 cells) so the whole shape
        // fits W6xH9 centred, WITH exit lanes, and stays solvable-by-construction. Only these 5 non-analytic styles
        // reach InShape's lookup; the normal-level styles (Scatter/Ring/Cross/Diamond) are handled before it.
        static readonly Dictionary<LayoutStyle, string[]> ShapeArt = new Dictionary<LayoutStyle, string[]>
        {
            { LayoutStyle.Heart,    new[] { "..##..", ".####.", "######", "##..##" } }, // point at front, lobes+cleft at back
            { LayoutStyle.Circle,   new[] { ".####.", "######", "######", ".####." } },
            { LayoutStyle.Plus,     new[] { "..##..", "..##..", "######", "######", "..##..", "..##.." } },
            { LayoutStyle.XShape,   new[] { "..##..", ".####.", "######", ".####.", "..##.." } }, // diamond
            { LayoutStyle.Triangle, new[] { "######", "######", ".####.", "..##.." } }, // wide base at front, apex at back
        };

        // Is cell (x,y) inside the Coin Rush silhouette for this style? Centred in the grid. Non-shape styles => every cell.
        static bool InShape(int x, int y, float cx, float cy, int W, int H, LayoutStyle style)
        {
            if (!ShapeArt.TryGetValue(style, out var art)) return true;
            int bh = art.Length, bw = art[0].Length;
            int ox = (W - bw) / 2, oy = (H - bh) / 2;                          // centre the silhouette in the grid
            int rx = x - ox, ry = y - oy;
            if (rx < 0 || rx >= bw || ry < 0 || ry >= bh) return false;
            return art[ry][rx] == '#';                                         // art row 0 == lowest y (front)
        }

        // How many cells the shape silhouette covers on a WxH grid (Coin Rush sets its car count to this).
        static int ShapeCount(int W, int H, LayoutStyle style)
        {
            float cx = (W - 1) * 0.5f, cy = (H - 1) * 0.5f;
            int c = 0;
            for (int y = 0; y < H; y++) for (int x = 0; x < W; x++) if (InShape(x, y, cx, cy, W, H, style)) c++;
            return c;
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
