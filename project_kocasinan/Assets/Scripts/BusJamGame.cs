using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;

namespace BusJam
{
    /// <summary>
    /// Portrait bus-jam manager. Buses sit in a 2D JAM GRID; each has an arrow
    /// (down / left / right). Tap a bus and it slides out to a parking slot ONLY if
    /// its path to that edge is clear — otherwise it's blocked. The queue STREAMS:
    /// only the first units are shown, more walk in from off-screen, and the total
    /// is hidden. Drives the HUD; exposes gameplay events for external UI.
    /// </summary>
    public class BusJamGame : MonoBehaviour
    {
        enum GameState { Boot, Menu, Playing, Win, Lose }

        [Header("Standalone testing")]
        public bool autoStart = true;
        public bool autoAdvance = true;

        [Header("Confetti (win celebration)")]
        public ConfettiSettings confetti = new ConfettiSettings();

        public System.Action<int> CoinsChanged;
        public System.Action<int> LevelStarted;
        public System.Action<int, int> LevelCompleted;
        public System.Action<string> LevelFailed;
        public System.Action<string> OnGameOver;
        public System.Action PauseRequested;

        public int CurrentLevel => currentLevel;
        public int Coins => SaveSystem.Coins;

        const int RecolorCost = 80, SwapCost = 40, HeliCost = 100, SlotUnlockCost = 80;
        const int ContinueBaseCost = 150;   // 1st continue costs this; doubles each further continue in the level
        int continueCount;                  // gold continues used this level (resets on StartLevel)
        int CurrentContinueCost => ContinueBaseCost << continueCount; // 150, 300, 600, 1200, ...
        const int J1UnlockLevel = 5, J2UnlockLevel = 10, J3UnlockLevel = 15; // RECOLOR / SWAP / HELI

        // World Z grows AWAY from the camera (up the portrait screen). Bottom→top:
        // big bus grid (low Z) -> parking row -> thin people band (high Z).
        const float CellSize = 1.1f;          // BIG cells: a 6-wide jam fills the portrait at the zoomed camera; vehicles scale with this
        const float GridExitZ = 5.5f;         // grid row y=0 (exit edge); the H9 jam fills the lower screen (deepest row stays on)
        const float RoadZ = 6.4f;             // road lane, right above the jam
        const float ParkingZ = 7.9f;          // bus stop (parking row), right above the road
        const float SlotSpacing = 1.05f;      // all 8 pads fit the portrait width (outer pad edge ~4.1 < visible)
        const float PeopleZ = 9.0f;           // mid of the people area (used for confetti / grass / no-facade spawn)
        const float PeopleSpacing = 0.85f;    // (queue is an L from the top-right door)
        const float FenceZ = 9.5f;            // fence, right under the people line (above the parked buses' noses)
        const float FacadeZ = 11.7f;          // mall/terminal wall center, TOP-RIGHT; the L-queue (vertical 2 + horizontal) feeds its door
        const float DoorSpawnZ = 11.0f;       // people are born at the door (top of the L) and the line runs down 2 then left across
        const int VISIBLE = 10;
        // Boarding pacing (T2): the pump DISPATCHES one front passenger every BoardGap (their walks
        // overlap), so throughput is BoardGap/person — far below the old ~0.32s serial cost.
        // boarding cadence + per-passenger walk duration now live in GameSettings (boardCadence / boardWalkDuration)

        GameState state = GameState.Boot;
        Camera cam;
        bool lowEnd;                          // budget/old mobile → lighter render path (set in Start)
        GameUI ui;
        Sfx sfx;
        LevelSelect levelSelect;              // opened from the in-game Settings → LEVELS (no on-screen button)
        PeopleCatalog peopleCatalog;
        VehicleCatalog vehicleCatalog;
        GameSettings gameSettings;            // editable tuning (speeds, sizes) — Resources/GameSettings.asset
        Font seatFont;
        Transform boardRoot;

        readonly Dictionary<PieceColor, Material> bodyMats = new Dictionary<PieceColor, Material>();
        Material glassMat, wheelMat, lightMat, skinMat, seatEmptyMat, mysteryMat, goldMat, arrowMat, lockMat, slotMat;
        Material roadMat, neonMat;            // fixed asphalt road (same every level) + emissive neon for the people-left sign
        Material[] confettiMats;

        LevelData level;
        int currentLevel = 1;
        int totalSlots, gridW, gridH;
        float[] doorXs;       // facade door world-X positions (set in BuildFacade); openings the interior line shows through
        float exitDoorX;      // the ONE door the boarding queue comes out of (set in BuildFacade)
        UnityEngine.UI.Text peopleLeftSign; // world-space "people left" sign by the road (rebuilt each level)
        int earnedThisLevel, combo, maxCombo;
        float lastBoardTime = -10f;
        int busy;
        bool pumpRunning, pumpDirty;

        ParkingSlot[] slots;
        readonly Dictionary<Vector2Int, Bus> occ = new Dictionary<Vector2Int, Bus>();
        readonly List<Bus> gridBuses = new List<Bus>();
        // Per (pack material, color) instance with "Main Color 1" (_Color01) driven to the match color.
        readonly Dictionary<(Material, PieceColor), Material> tintedVehicleMats = new Dictionary<(Material, PieceColor), Material>();

        List<LineGroup> groups;
        int nextGroupIndex;
        readonly List<LineUnit> visible = new List<LineUnit>();

        // ====================================================================
        void Start()
        {
            Screen.orientation = ScreenOrientation.Portrait;
            QualitySettings.vSyncCount = 0;     // so targetFrameRate is honored (mobile otherwise caps at 30)
            Application.targetFrameRate = 60;   // smooth 60 fps on capable devices
            lowEnd = Application.isMobilePlatform && SystemInfo.systemMemorySize < 3072; // <3GB phone → lighter paths (editor/desktop never lowEnd)
            cam = Camera.main;
            BuildMaterials();
            peopleCatalog = Resources.Load<PeopleCatalog>("PeopleCatalog"); // null -> code-built people
            vehicleCatalog = Resources.Load<VehicleCatalog>("VehicleCatalog"); // null -> code-built vehicles
            gameSettings = Resources.Load<GameSettings>("GameSettings");       // tuning knobs (Inspector-editable)
            if (gameSettings == null) gameSettings = ScriptableObject.CreateInstance<GameSettings>(); // fall back to defaults
            seatFont = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf"); // roof seat-count number
            PlaceCamera();
            SetupPostFX();

            sfx = gameObject.AddComponent<Sfx>();
            ui = gameObject.AddComponent<GameUI>();
            ui.OnMenu = () => { sfx.Click(); PauseRequested?.Invoke(); };
            ui.OnRecolor = JokerRecolor;
            ui.OnSwap = JokerSwapPeople;
            ui.OnHeli = JokerHelicopter;
            ui.OnHome = GoToMainMenu;            // settings -> HOME
            ui.OnReplay = RetryLevel;            // settings -> REPLAY
            ui.OnClaimReward = ClaimWinReward;   // success panel -> claim / ad
            ui.OnContinuePay = () =>             // Continue panel: pay 150, then doubles each time
            {
                if (SaveSystem.TrySpend(CurrentContinueCost))
                {
                    continueCount++;
                    ui.SetCoins(SaveSystem.Coins);
                    CoinsChanged?.Invoke(SaveSystem.Coins);
                    ui.HideContinue();
                    ContinueLevel();
                }
                else sfx.Error();
            };
            ui.OnContinueAd = () => { ui.HideContinue(); ContinueLevel(); };   // TODO: gate behind a real rewarded ad
            ui.OnContinueDeclined = () => { ui.HideContinue(); ui.ShowFailed(); };
            ui.Build(RecolorCost, SwapCost, HeliCost, J1UnlockLevel, J2UnlockLevel, J3UnlockLevel);

            levelSelect = gameObject.AddComponent<LevelSelect>();
            levelSelect.Build(this);
            ui.OnLevels = () => levelSelect.Open(); // in-game Settings -> LEVELS map (wired after the field is built)

            if (autoStart) LoadLevel(SaveSystem.Level);
            else { state = GameState.Menu; ui.HideHud(); }
        }

        // ---- Public control ------------------------------------------------
        public void LoadLevel(int levelNumber) { CancelInvoke(); StartLevel(levelNumber); }
        public void NextLevel() { LoadLevel(SaveSystem.Level); }
        public void RetryLevel() { LoadLevel(currentLevel); }
        public void ToggleSound() { SaveSystem.Sound = !SaveSystem.Sound; sfx.Click(); }

        // Settings panel: HOME button -> back to the main menu scene.
        public void GoToMainMenu() { sfx.Click(); SceneManager.LoadScene("MainMenu"); }

        // Success panel: grant the reward (base 20 / ad 40) then advance a level.
        void ClaimWinReward(int amount)
        {
            if (state != GameState.Win) return;
            AddCoins(amount);
            sfx.Coin();
            NextLevel(); // SaveSystem.Level was already advanced in Win()
        }

        public void ContinueLevel()
        {
            if (state != GameState.Lose || slots == null) return;
            CancelInvoke();
            // Revive by unlocking one locked slot (breaks the parking deadlock).
            foreach (var s in slots)
                if (s != null && s.locked) { s.Unlock(); break; }
            state = GameState.Playing;
            StartCoroutine(LineLayoutLoop()); // restart queue re-spacing (it exited when state left Playing)
            ui.ShowHud();
            sfx.Click();
            TryStartBoardingPump();
        }

        // ====================================================================
        void Update()
        {
            if (state != GameState.Playing) return;

            RevealMystery();

            if (TryGetPointerDown(out Vector2 sp))
            {
                if (EventSystem.current != null && EventSystem.current.IsPointerOverGameObject()) return;
                Ray ray = cam.ScreenPointToRay(sp);
                if (Physics.Raycast(ray, out RaycastHit hit, 400f))
                {
                    var bus = hit.collider.GetComponentInParent<Bus>();
                    if (bus != null) { TryTapBus(bus); return; }
                    var slot = hit.collider.GetComponentInParent<ParkingSlot>();
                    if (slot != null && slot.locked) TryUnlockSlot(slot);
                }
            }
        }

        static bool TryGetPointerDown(out Vector2 pos)
        {
            pos = default;
            if (Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame)
            { pos = Mouse.current.position.ReadValue(); return true; }
            if (Touchscreen.current != null && Touchscreen.current.primaryTouch.press.wasPressedThisFrame)
            { pos = Touchscreen.current.primaryTouch.position.ReadValue(); return true; }
            return false;
        }

        void RevealMystery()
        {
            int n = Mathf.Min(visible.Count, 4);
            for (int i = 0; i < n; i++)
                if (visible[i] != null && visible[i].mystery && !visible[i].revealed) visible[i].Reveal(bodyMats[visible[i].color]);
        }

        // ====================================================================
        // Jam grid: tap a bus -> slide out if its path is clear
        // ====================================================================
        // Forgiving UNIFIED tap (normal + crawler, cardinal + diagonal): a fully-clear lane + free slot drives
        // grounded to the stop; otherwise the footprint advances forward to the blocker; only a tap with no
        // forward progress AND no exit is rejected. SlideClear/OccCells are the SAME shared geometry the
        // generator placed with, so solvable-by-construction holds.
        void TryTapBus(Bus bus)
        {
            if (bus.state != BusState.Queued) return; // already leaving / parked / mid-crawl

            bool laneClear = LevelGenerator.SlideClear(bus.cell, bus.dir, bus.length, occ.ContainsKey, gridW, gridH);
            var slot = laneClear ? NearestFreeSlot(GridWorldCenter(bus.cell, bus.dir, bus.length).x) : null;

            if (laneClear && slot != null)
            {
                foreach (var c in LevelGenerator.OccCells(bus.cell, bus.dir, bus.length)) occ.Remove(c);
                gridBuses.Remove(bus);
                slot.occupant = bus; bus.slotIndex = slot.index; bus.state = BusState.MovingToSlot; // claimed synchronously
                sfx.Deploy();
                StartCoroutine(ExitRoutine(bus, slot));
                return;
            }

            // Partial advance: crawlers cap at advanceN cells/tap; normals advance until the blocker.
            int cap = bus.advanceN > 0 ? bus.advanceN : gridW + gridH;
            int step = LevelGenerator.MaxAdvanceSteps(bus.cell, bus.dir, bus.length, occ.ContainsKey, gridW, gridH, cap);
            if (step == 0) { sfx.Crash(); StartCoroutine(Bump(bus.transform)); return; } // blocked: crash + shake (no forward progress, no exit)

            foreach (var c in LevelGenerator.OccCells(bus.cell, bus.dir, bus.length)) occ.Remove(c); // free old, THEN
            bus.cell += bus.dir * step;
            foreach (var c in LevelGenerator.OccCells(bus.cell, bus.dir, bus.length)) occ[c] = bus;  // add new -- atomic, no leak
            bus.state = BusState.Staging; // not tappable until the crawl animation finishes
            sfx.Deploy();
            StartCoroutine(CrawlMove(bus, step));
        }

        IEnumerator ExitRoutine(Bus bus, ParkingSlot slot)
        {
            busy++;
            // Find the SHORTEST CLEAR, on-screen path from the bus's cell to the parking slot — A* through the
            // EMPTY cells (occ = the vehicles still in the jam) that never leaves the screen — driven as a smooth
            // spline. Falls back to a direct up-to-the-road route only when the jam is too dense to thread.
            var path = FindClearPath(bus.cell, slot);
            if (path == null || path.Count == 0)
                path = new List<Vector3> { new Vector3(SlotX(slot.index), 0, RoadZ) }; // dense-jam fallback (still on-screen)
            path.Add(ParkingWorld(slot.index));                                        // end exactly at the slot
            yield return DrivePath(bus.transform, path, gameSettings.busDriveSpeed, gameSettings.turnSmoothness);
            bus.transform.rotation = Quaternion.Euler(0, 180f, 0);                   // settle to exact parked facing (nose +Z)
            bus.state = BusState.Parked;
            sfx.Honk();                                                              // ONE honk as it pulls into the stop
            StartCoroutine(Juice.PunchScale(bus.transform, 0.16f));
            busy--;
            TryStartBoardingPump();
            CheckEnd();
        }

        // ---- A* shortest CLEAR, on-screen exit path ------------------------------------------------------
        // World waypoints (ground y=0) from the bus's cell to the parking slot, threading only EMPTY cells
        // (occ = vehicles still jammed) and staying on-screen; null if the jam is too dense to thread. The bus
        // is treated as a point on the jam cell grid, which is extended with a clear apron up past the parking.
        List<Vector3> FindClearPath(Vector2Int start, ParkingSlot slot)
        {
            Vector2Int goal = WorldToCell(ParkingWorld(slot.index));
            int xMin = Mathf.Min(0, Mathf.Min(start.x, goal.x)) - 1;
            int xMax = Mathf.Max(gridW - 1, Mathf.Max(start.x, goal.x)) + 1;
            int yMin = Mathf.Min(goal.y, start.y) - 1;
            int yMax = Mathf.Max(gridH - 1, start.y) + 1;

            bool Walk(Vector2Int c)
            {
                if (c == start || c == goal) return true;            // endpoints always allowed
                if (occ.ContainsKey(c)) return false;                // a vehicle is parked there
                Vector3 w = CellWorld(c);
                return Mathf.Abs(w.x) <= VisHalfW(w.z) - 0.35f;      // must stay on-screen (body + perspective margin)
            }

            var open = new List<Vector2Int> { start };
            var came = new Dictionary<Vector2Int, Vector2Int>();
            var gScore = new Dictionary<Vector2Int, float> { [start] = 0f };
            var fScore = new Dictionary<Vector2Int, float> { [start] = Heur(start, goal) };
            var closed = new HashSet<Vector2Int>();
            var steps = new[] { new Vector2Int(1, 0), new Vector2Int(-1, 0), new Vector2Int(0, 1), new Vector2Int(0, -1),
                                new Vector2Int(1, 1), new Vector2Int(1, -1), new Vector2Int(-1, 1), new Vector2Int(-1, -1) };
            int guard = 0;
            while (open.Count > 0 && guard++ < 5000)
            {
                int bi = 0;
                for (int i = 1; i < open.Count; i++) if (fScore[open[i]] < fScore[open[bi]]) bi = i;
                var cur = open[bi]; open.RemoveAt(bi);
                if (cur == goal) return CellsToWorld(came, cur);
                closed.Add(cur);
                foreach (var d in steps)
                {
                    var n = cur + d;
                    if (n.x < xMin || n.x > xMax || n.y < yMin || n.y > yMax) continue;
                    if (closed.Contains(n) || !Walk(n)) continue;
                    if (d.x != 0 && d.y != 0 &&                       // no diagonal squeeze past a blocked corner
                        (!Walk(new Vector2Int(cur.x + d.x, cur.y)) || !Walk(new Vector2Int(cur.x, cur.y + d.y)))) continue;
                    float tg = gScore[cur] + ((d.x != 0 && d.y != 0) ? 1.41421356f : 1f);
                    if (!gScore.TryGetValue(n, out float gn) || tg < gn)
                    {
                        came[n] = cur; gScore[n] = tg; fScore[n] = tg + Heur(n, goal);
                        if (!open.Contains(n)) open.Add(n);
                    }
                }
            }
            return null; // no clear on-screen path -> caller falls back
        }

        static float Heur(Vector2Int a, Vector2Int b) { float dx = a.x - b.x, dy = a.y - b.y; return Mathf.Sqrt(dx * dx + dy * dy); }
        Vector2Int WorldToCell(Vector3 w) => new Vector2Int(
            Mathf.RoundToInt(w.x / CellSize + (gridW - 1) * 0.5f), Mathf.RoundToInt((GridExitZ - w.z) / CellSize));
        Vector3 CellWorld(Vector2Int c) => new Vector3((c.x - (gridW - 1) * 0.5f) * CellSize, 0, GridExitZ - c.y * CellSize);

        // Visible half-width (world units) at ground depth z, conservative for a tall portrait (aspect 0.462).
        // Tied to PlaceCamera (pos 0,16,-6 / target 0,0,3.2 / FOV 54) — keep in sync if the camera changes.
        static float VisHalfW(float z) => (13.867f + 0.4983f * (z + 6f)) * 0.2356f;

        // A* cell chain -> world waypoints, dropping the start cell (DrivePath prepends the bus's pos) and collinear runs.
        List<Vector3> CellsToWorld(Dictionary<Vector2Int, Vector2Int> came, Vector2Int cur)
        {
            var cells = new List<Vector2Int> { cur };
            while (came.ContainsKey(cur)) { cur = came[cur]; cells.Add(cur); }
            cells.Reverse(); // start ... goal
            var pts = new List<Vector3>();
            for (int i = 1; i < cells.Count; i++)
            {
                if (i < cells.Count - 1)
                {
                    Vector2Int a = cells[i - 1], b = cells[i], c2 = cells[i + 1];
                    if ((b.x - a.x) == (c2.x - b.x) && (b.y - a.y) == (c2.y - b.y)) continue; // drop collinear
                }
                pts.Add(CellWorld(cells[i]));
            }
            return pts;
        }

        IEnumerator CrawlMove(Bus bus, int step)
        {
            busy++;
            float dur = Mathf.Clamp(0.16f + 0.09f * step, 0.18f, 0.75f); // smooth grounded slide, scaled by crawl distance
            yield return MoveTo(bus.transform, GridWorldCenter(bus.cell, bus.dir, bus.length), dur);
            bus.state = BusState.Queued; // tappable again
            busy--;
            CheckEnd();
        }

        int ExitDistance(Vector2Int cell, Vector2Int dir)
        {
            int d = 0; var p = cell;
            while (InGrid(p)) { p += dir; d++; }
            return d;
        }

        bool InGrid(Vector2Int p) => p.x >= 0 && p.x < gridW && p.y >= 0 && p.y < gridH;

        // ====================================================================
        // Boarding (streaming queue)
        // ====================================================================
        void TryStartBoardingPump()
        {
            if (pumpRunning) pumpDirty = true;
            else StartCoroutine(BoardingPump());
        }

        IEnumerator BoardingPump()
        {
            pumpRunning = true;
            busy++;
            bool progressed = true;
            while (progressed || pumpDirty)
            {
                pumpDirty = false;
                progressed = false;

                // Drive off buses that are full AND whose reserved passengers have ALL arrived (so a bus
                // never leaves while someone is still walking to it).
                foreach (var slot in slots)
                {
                    var b = slot.occupant;
                    if (b != null && b.state == BusState.Parked && b.ReadyToLeave)
                    {
                        b.state = BusState.Leaving;
                        StartCoroutine(DispatchRoutine(b, slot));
                        progressed = true;
                    }
                }

                // Dispatch the FRONT passenger: reserve their seat NOW (capacity can't be over-assigned),
                // run the walk ASYNC, and advance after just BoardGap so successive walks overlap.
                if (visible.Count > 0)
                {
                    var u = visible[0];
                    Bus bus = FindParkedBus(u.color);
                    if (bus != null)
                    {
                        visible.RemoveAt(0);
                        int seat = bus.ReserveSeat();
                        OnBoarded(u.golden, BusDoorWorld(bus)); // combo/coins in dispatch order, once each
                        StartCoroutine(BoardWalk(u, bus, seat));
                        StreamNext();
                        progressed = true;
                        yield return new WaitForSeconds(gameSettings.boardCadence); // cadence, NOT the full walk
                    }
                }
            }
            busy--;
            pumpRunning = false;
            CheckEnd();
        }

        // One passenger walks to their reserved seat independently of the pump, so many can be in flight
        // at once. busy is bracketed so CheckEnd/Win can't fire while anyone is still walking.
        IEnumerator BoardWalk(LineUnit u, Bus bus, int seat)
        {
            busy++;
            if (u != null) yield return MoveTo(u.transform, BusDoorWorld(bus), gameSettings.boardWalkDuration, ease: true);
            if (bus != null)
            {
                bus.LightSeat(seat);
                StartCoroutine(Juice.PunchScale(bus.transform, 0.12f));
            }
            if (u != null) Destroy(u.gameObject);
            busy--;
            TryStartBoardingPump(); // this arrival may have made the bus ReadyToLeave
            CheckEnd();
        }

        void StreamNext()
        {
            if (nextGroupIndex < groups.Count)
            {
                var u = CreateUnit(groups[nextGroupIndex++]);
                u.transform.position = DoorSpawn(LinePos(visible.Count).x); // emerge from the one exit door
                visible.Add(u);
            }
            UpdatePeopleLeft(); // a person was just served (or skipped) -> refresh the counter
        }

        // People still to serve = unspawned (groups - cursor) + on-screen window. Reads the LOGICAL
        // pool, NOT visible.Count alone; equals 0 exactly when visible==0 && cursor>=groups.Count (Win).
        int PeopleLeft() => Mathf.Max(0, (groups != null ? groups.Count - nextGroupIndex : 0) + visible.Count);
        void UpdatePeopleLeft()
        {
            int n = PeopleLeft();
            if (peopleLeftSign != null) peopleLeftSign.text = n.ToString(); // neon world-space sign by the first bus stop (HUD chip removed)
        }

        void OnBoarded(bool golden, Vector3 pos)
        {
            combo = (Time.time - lastBoardTime < 1.6f) ? combo + 1 : 1;
            if (combo > maxCombo) maxCombo = combo; // drives the win star rating (no timer)
            lastBoardTime = Time.time;

            int coins = Mathf.Clamp(combo, 1, 5);
            if (golden)
            {
                coins += 15;
                sfx.Coin();
                Juice.Burst(this, boardRoot, pos + Vector3.up * 0.6f, goldMat, 14, 4.2f);
            }
            else sfx.Board();

            AddCoins(coins);
            if (combo >= 2) ui.ShowCombo(combo);
        }

        Bus FindParkedBus(PieceColor color)
        {
            foreach (var slot in slots)
            {
                var b = slot.occupant;
                if (b != null && b.state == BusState.Parked && !b.IsFull && b.color == color) return b;
            }
            return null;
        }

        IEnumerator DispatchRoutine(Bus bus, ParkingSlot slot)
        {
            if (bus == null || slot == null) yield break; // cheap insurance vs a mid-frame teardown/level-change
            busy++;
            slot.occupant = null; // free the slot immediately so BoardingPump can refill it
            Vector3 start = bus.transform.position;
            sfx.Screech();                                                          // full bus pulls away = tyre screech
            Juice.Burst(this, boardRoot, start + Vector3.up * 0.4f, bodyMats[bus.color], 16, 4.5f); // celebrate as it pulls away

            // Drive the FULL-SIZE bus FLAT (grounded). Parked buses face +Z (nose toward the people band), so
            // leaving is a real maneuver: BACK UP a little out of the stop, then sweep onto the road and cruise
            // off-screen as ONE smooth, rounded drive (the spline steers the nose gradually, no 90° snap). The
            // road lane sits above the jam, so the bus never drives through the jam.
            float side = start.x >= 0f ? 1f : -1f;                                  // exit the closer side
            Vector3 backUp = new Vector3(start.x, start.y, ParkingZ - 1.2f);        // reverse a little (faces +Z, so a -Z move reads as backing up)
            yield return MoveTo(bus.transform, backUp, 0.35f);                       // back up a little out of the stop
            yield return DrivePath(bus.transform, new List<Vector3> {
                new Vector3(start.x + side * 1.4f, start.y, RoadZ),                  // sweep onto the road toward the exit side
                new Vector3(side * 14f, start.y, RoadZ),                            // cruise off-screen along the road
            }, gameSettings.busLeaveSpeed, gameSettings.turnSmoothness);

            if (bus != null) { Juice.StopPunch(bus.transform); Destroy(bus.gameObject); } // evict punch state, then destroy off-frame
            busy--;
            CheckEnd();
        }

        // Continuously eases the visible queue to its slot positions, so people streaming in and boarders
        // leaving re-space SMOOTHLY without an awaited per-board reposition (replaces RepositionLine on the
        // boarding path). One owner of each person's position -> no overlapping MoveTo coroutines fighting.
        IEnumerator LineLayoutLoop()
        {
            while (state == GameState.Playing)
            {
                float k = 1f - Mathf.Exp(-14f * Time.deltaTime); // frame-rate-independent ease
                for (int i = 0; i < visible.Count; i++)
                {
                    var t = visible[i] != null ? visible[i].transform : null;
                    if (t != null) t.position = Vector3.Lerp(t.position, LinePos(i), k);
                }
                yield return null;
            }
        }

        void CheckEnd()
        {
            if (state != GameState.Playing) return;
            // Defer ALL end-decisions until in-flight walks/drive-offs settle (busy brackets every async
            // boarder), so Win can't pop while the last passengers are still walking to their bus.
            if (busy > 0) return;
            if (visible.Count == 0 && nextGroupIndex >= groups.Count) { Win(); return; }
            if (visible.Count == 0) return;

            // The front passenger can board one of the parked buses -> keep playing.
            if (FindParkedBus(visible[0].color) != null) return;

            // There is still an OPEN (unlocked & empty) parking slot, so the player can place
            // another bus that might match -> parking is NOT full yet, so this is not a deadlock.
            if (FirstFreeSlot() != null) return;

            // Otherwise: the front passenger matches NO parked bus AND the parking is full.
            // This is a genuine deadlock -> lose. Locked slots, the number of remaining grid buses
            // and joker coins are intentionally NOT treated as an escape (per design: the front
            // passenger being unable to board with a full parking == loss -> Continue panel).
            Lose("No matching bus - parking full.");
        }

        // ====================================================================
        // Player actions
        // ====================================================================
        void TryUnlockSlot(ParkingSlot slot)
        {
            if (!slot.locked) return;
            if (!Spend(SlotUnlockCost)) { sfx.Error(); StartCoroutine(Bump(slot.transform)); return; }
            slot.Unlock();
            sfx.Coin();
            TryStartBoardingPump();
        }

        // ---- Jokers: level-gated (locked buttons are greyed + non-interactable; the early guards
        // here are a safety net) and coin-costed. All three keep the level winnable. ----

        // J1 @ Lv5 RECOLOR: re-tint EVERY jam vehicle. Permutes colors WITHIN each capacity group, so
        // each color's total jam seats is unchanged -> remaining_people[c]==remaining_seats[c] stays
        // balanced -> still winnable; an accessible vehicle can take on a needed color.
        void JokerRecolor()
        {
            if (state != GameState.Playing || gridBuses.Count == 0) { sfx.Error(); return; }
            if (SaveSystem.Level < J1UnlockLevel) { sfx.Error(); return; }
            if (!SpendJoker(0, RecolorCost)) { sfx.Error(); return; }
            sfx.Coin();

            var byCap = new Dictionary<int, List<Bus>>();
            foreach (var b in gridBuses)
            {
                if (!byCap.TryGetValue(b.capacity, out var l)) { l = new List<Bus>(); byCap[b.capacity] = l; }
                l.Add(b);
            }
            foreach (var kv in byCap)
            {
                var list = kv.Value;
                var colors = new PieceColor[list.Count];
                for (int i = 0; i < list.Count; i++) colors[i] = list[i].color;
                for (int i = list.Count - 1; i > 0; i--) { int j = Random.Range(0, i + 1); (colors[i], colors[j]) = (colors[j], colors[i]); }
                for (int i = 0; i < list.Count; i++) RecolorBus(list[i], colors[i]);
            }
            StartCoroutine(AfterJoker());
        }

        // J2 @ Lv10 SWAP: shuffle the visible queue. Any permutation keeps the color multiset and the
        // people total -> solvability-safe; brings a servable color to the front.
        void JokerSwapPeople()
        {
            if (state != GameState.Playing || visible.Count < 2) { sfx.Error(); return; }
            if (SaveSystem.Level < J2UnlockLevel) { sfx.Error(); return; }
            if (!SpendJoker(1, SwapCost)) { sfx.Error(); return; }
            sfx.Click();
            for (int i = visible.Count - 1; i > 0; i--) { int j = Random.Range(0, i + 1); (visible[i], visible[j]) = (visible[j], visible[i]); }
            StartCoroutine(AfterJoker());
        }

        // J3 @ Lv15 HELICOPTER: airlift ONE jam vehicle straight onto a free slot, ignoring blockers.
        // Only relocates a vehicle that had to be parked eventually -> per-color balance unchanged.
        void JokerHelicopter()
        {
            if (state != GameState.Playing || gridBuses.Count == 0) { sfx.Error(); return; }
            if (SaveSystem.Level < J3UnlockLevel) { sfx.Error(); return; }
            var slot = FirstFreeSlot();
            if (slot == null) { sfx.Error(); return; } // no free slot -> nothing spent

            // Prefer a vehicle matching the front passenger (directly unsticks), else any queued one.
            Bus pick = null;
            if (visible.Count > 0)
            {
                var want = visible[0].color;
                foreach (var b in gridBuses) if (b.state == BusState.Queued && b.color == want) { pick = b; break; }
            }
            if (pick == null) foreach (var b in gridBuses) if (b.state == BusState.Queued) { pick = b; break; }
            if (pick == null) { sfx.Error(); return; }
            if (!SpendJoker(2, HeliCost)) { sfx.Error(); return; }
            sfx.Deploy();

            foreach (var c in LevelGenerator.OccCells(pick.cell, pick.dir, pick.length)) occ.Remove(c); // free ALL body cells (no phantom)
            gridBuses.Remove(pick);
            slot.occupant = pick; pick.slotIndex = slot.index; pick.state = BusState.MovingToSlot;
            StartCoroutine(HeliRoutine(pick, slot));
        }

        IEnumerator HeliRoutine(Bus bus, ParkingSlot slot)
        {
            busy++;
            // big arc -> the vehicle lifts and flies OVER the jam to its slot, ignoring obstacles.
            yield return MoveAndRotateArc(bus.transform, ParkingWorld(slot.index), Quaternion.Euler(0, 180f, 0), 0.55f, 2.6f);
            bus.state = BusState.Parked;
            StartCoroutine(Juice.PunchScale(bus.transform, 0.16f));
            busy--;
            TryStartBoardingPump();
            CheckEnd();
        }

        // Re-tint a jam bus to a new match-color (body + roof passengers) for RECOLOR.
        void RecolorBus(Bus bus, PieceColor newColor)
        {
            bus.color = newColor;
            var modelTf = bus.transform.Find("Model");
            var prefab = vehicleCatalog != null ? vehicleCatalog.PrefabFor(bus.type) : null;
            if (modelTf != null && prefab != null)
            {
                // Tint each model slot against its ORIGINAL prefab material (same as the build path) so
                // multi-material vehicles keep windows/wheels, and the cache key stays bounded.
                var modelRends = modelTf.GetComponentsInChildren<Renderer>(true);
                var prefabRends = prefab.GetComponentsInChildren<Renderer>(true);
                for (int r = 0; r < modelRends.Length; r++)
                {
                    var m = modelRends[r].sharedMaterials;
                    var baseMats = r < prefabRends.Length ? prefabRends[r].sharedMaterials : null;
                    for (int i = 0; i < m.Length; i++)
                    {
                        Material baseM = (baseMats != null && i < baseMats.Length) ? baseMats[i] : null;
                        if (baseM != null) m[i] = TintedVehicleMat(baseM, newColor);
                    }
                    modelRends[r].sharedMaterials = m;
                }
            }
            else // code-built fallback: re-tint the body cube
            {
                var bodyTf = bus.transform.Find("Body");
                if (bodyTf != null) { var br = bodyTf.GetComponent<Renderer>(); if (br != null) br.sharedMaterial = bodyMats[newColor]; }
            }
            // Re-tint the roof heads' caps (revealed AND not-yet-revealed) to the new color.
            if (bus.roofPeople != null)
                foreach (var pax in bus.roofPeople)
                {
                    if (pax == null) continue;
                    var hat = pax.transform.Find("Hat");
                    if (hat != null) { var hr = hat.GetComponent<Renderer>(); if (hr != null) hr.sharedMaterial = bodyMats[newColor]; }
                }
        }

        IEnumerator AfterJoker()
        {
            busy++;
            yield return new WaitForSeconds(0.12f); // let the layout loop re-space the shuffled queue
            busy--;
            UpdatePeopleLeft();
            TryStartBoardingPump();
            CheckEnd();
        }

        // ====================================================================
        // Economy
        // ====================================================================
        void AddCoins(int delta)
        {
            SaveSystem.AddCoins(delta);
            earnedThisLevel += delta;
            ui.SetCoins(SaveSystem.Coins);
            CoinsChanged?.Invoke(SaveSystem.Coins);
        }

        bool Spend(int cost)
        {
            if (!SaveSystem.TrySpend(cost)) return false;
            ui.SetCoins(SaveSystem.Coins);
            CoinsChanged?.Invoke(SaveSystem.Coins);
            return true;
        }

        // Use a free daily-reward joker charge (kind 0/1/2) if available, else pay gold.
        bool SpendJoker(int kind, int cost)
        {
            if (SaveSystem.TryUseFreeJoker(kind))
            {
                if (ui != null) ui.RefreshJokerLocks(); // refresh the free-charge badge
                return true;
            }
            return Spend(cost);
        }

        // ====================================================================
        // Level lifecycle
        // ====================================================================
        void StartLevel(int levelNumber)
        {
            currentLevel = levelNumber;
            Teardown();

            // Load an authored level asset if one exists; otherwise generate procedurally.
            var def = Resources.Load<LevelDefinition>("Levels/Level" + levelNumber);
            level = def != null ? LevelGenerator.Generate(def) : LevelGenerator.Generate(levelNumber);
            totalSlots = level.baseSlots + level.extraSlots;
            boardRoot = new GameObject("Board").transform;

            Theme theme = Themes.For(levelNumber);
            ApplyTheme(theme);
            BuildSlots();
            BuildGrid();
            BuildBoardBackground(theme);
            BuildPeopleLeftSign();
            BuildLine();

            earnedThisLevel = 0; combo = 0; maxCombo = 0; lastBoardTime = -10f;
            continueCount = 0; // reset escalating continue price each level

            state = GameState.Playing;
            StartCoroutine(LineLayoutLoop()); // continuous queue re-spacing for the duration of the level
            ui.ShowHud();
            ui.SetLevel(levelNumber);
            ui.SetTheme(theme.name);
            ui.SetCoins(SaveSystem.Coins);
            ui.RefreshJokerLocks();  // unlock RECOLOR/SWAP/HELI as SaveSystem.Level rises
            UpdatePeopleLeft(); // initial total (this level's real people count, not the visible window)
            LevelStarted?.Invoke(levelNumber);
            CheckEnd(); // detect an immediately-stuck board (no-op normally: free slots exist at start)
        }

        void Teardown()
        {
            StopAllCoroutines();
            Juice.ClearAllPunches(); // drop punch state left by hard-stopped coroutines (no cross-level leak)
            busy = 0; pumpRunning = false; pumpDirty = false;
            occ.Clear(); gridBuses.Clear(); visible.Clear(); slots = null;
            peopleLeftSign = null; // destroyed with boardRoot below; drop the stale ref (no cross-level leak)
            if (boardRoot != null) Destroy(boardRoot.gameObject);
            boardRoot = null;
        }

        void Win()
        {
            state = GameState.Win;
            // No timer anymore — stars reward boarding flow (best combo streak this level).
            int stars = maxCombo >= 8 ? 3 : (maxCombo >= 4 ? 2 : 1);
            // Level progression is locked in now; the actual coin reward is granted
            // by the success panel (CLAIM = 20, WATCH AD x2 = 40) via ClaimWinReward.
            SaveSystem.Level = Mathf.Max(SaveSystem.Level, currentLevel + 1);
            SaveSystem.BestLevel = currentLevel;
            sfx.Win();
            ui.HideHud();
            ConfettiFromCorners(); // confetti shoots UP from the bottom-left & bottom-right corners
            LevelCompleted?.Invoke(earnedThisLevel, stars);
            ui.ShowSuccess(stars); // white box + blue stripe + animated 1-3 stars + claim/ad
        }

        // Two upward confetti bursts from the bottom-left and bottom-right screen corners.
        void ConfettiFromCorners()
        {
            if (cam == null)
            {
                Juice.Confetti(this, boardRoot, new Vector3(0, 6, PeopleZ), confettiMats, confetti);
                return;
            }
            float depth = Mathf.Abs(cam.transform.position.z - PeopleZ);
            // Exact bottom-LEFT and bottom-RIGHT corners, bursting DIAGONALLY upward
            // toward the middle of the screen (dirX +1 from the left, -1 from the right).
            Vector3 bl = cam.ViewportToWorldPoint(new Vector3(0.02f, 0.02f, depth));
            Vector3 br = cam.ViewportToWorldPoint(new Vector3(0.98f, 0.02f, depth));
            Juice.Confetti(this, boardRoot, bl, confettiMats, confetti, +1f);
            Juice.Confetti(this, boardRoot, br, confettiMats, confetti, -1f);
        }

        void Lose(string reason)
        {
            if (state != GameState.Playing) return;
            state = GameState.Lose;
            sfx.Lose();
            ui.HideHud();
            ui.SetContinuePrice(CurrentContinueCost); // 150, then doubles each continue
            ui.ShowContinue(); // runtime Continue panel (decline -> Failed). GameManager is neutralized to avoid a 2nd panel.
            LevelFailed?.Invoke(reason);
            OnGameOver?.Invoke(reason);
            // The Continue panel (ui.ShowContinue) now owns the loss flow — no auto-retry.
        }

        // ====================================================================
        // Build
        // ====================================================================
        void BuildSlots()
        {
            slots = new ParkingSlot[totalSlots];
            // Unlock EXACTLY baseSlots pads (== the BuildQueue servability window); lock the rest at the
            // edges so the open pads are central and the player unlocks outward.
            int lockCount = Mathf.Max(0, totalSlots - level.baseSlots);
            int leftLocks = lockCount / 2;
            int rightStart = totalSlots - (lockCount - leftLocks);
            for (int i = 0; i < totalSlots; i++)
            {
                var pad = GameObject.CreatePrimitive(PrimitiveType.Cube);
                pad.name = "Slot" + i;
                pad.transform.SetParent(boardRoot, false);
                pad.transform.position = new Vector3(SlotX(i), -0.05f, ParkingZ);
                pad.transform.localScale = new Vector3(SlotSpacing * 0.84f, 0.1f, 1.6f); // shallower so the stop clears the road band (no overlap)
                pad.GetComponent<Renderer>().sharedMaterial = slotMat;

                var slot = pad.AddComponent<ParkingSlot>();
                slot.index = i;
                slot.locked = (i < leftLocks) || (i >= rightStart); // central pads unlocked
                slots[i] = slot;

                if (slot.locked)
                {
                    var marker = new GameObject("Lock");
                    marker.transform.SetParent(pad.transform, false);
                    marker.transform.localPosition = new Vector3(0, 0.7f, 0);
                    MakeCube(marker.transform, lockMat, new Vector3(0.55f, 0.12f, 0.14f));
                    MakeCube(marker.transform, lockMat, new Vector3(0.14f, 0.12f, 0.55f));
                    var pulse = marker.AddComponent<IdleBob>();
                    pulse.scalePulse = true; pulse.scaleAmp = 0.12f; pulse.speed = 3f; pulse.amp = 0f;
                    slot.lockMarker = marker;
                }
            }
        }

        void BuildGrid()
        {
            gridW = level.gridW; gridH = level.gridH;
            occ.Clear(); gridBuses.Clear();
            foreach (var gb in level.gridBuses)
            {
                var bus = CreateBus(gb.color, gb.type, gb.capacity, gb.advanceN, DirYaw(gb.dir));
                bus.cell = gb.cell; bus.dir = gb.dir; bus.length = Vehicles.CellLength(gb.type);
                bus.state = BusState.Queued;
                bus.transform.position = GridWorldCenter(gb.cell, gb.dir, bus.length);
                foreach (var c in LevelGenerator.OccCells(gb.cell, gb.dir, bus.length)) occ[c] = bus;
                gridBuses.Add(bus);
            }
        }

        // Seamless packed-lot ground under the jam grid (no cell lattice) so vehicles sit on a
        // surface that blends with the theme. Pure visual — all grid logic is unchanged.
        void BuildBoardBackground(Theme th)
        {
            float w = gridW * CellSize;
            float d = gridH * CellSize;
            float cz = GridExitZ - (gridH - 1) * CellSize * 0.5f;

            // One ground/parking slab in the theme ground color (fallback args MATCH ApplyTheme's "Ground").
            Material lot = MaterialLibrary.GetTheme(th.name, "Ground", th.ground, 0.35f, 0.05f);
            LowPolyBuilder.Slab(boardRoot, new Vector3(0, -0.07f, cz), new Vector3(w + 0.6f, 0.12f, d + 0.6f), lot);
        }

        // Wide mall/terminal facade behind the people band. Themed (Facade/Trim/Door materials),
        // deterministic, parented to boardRoot (torn down each level). The wall has REAL door OPENINGS
        // (header beam + pillars). A single bent QUEUE of little people lines up INSIDE, visible through the
        // openings, and HOOKS out the one exit door (rightmost) — so it reads as one line coming out of the
        // building; the moving boarding queue emerges from that same door (exitDoorX) via DoorSpawn. Sign +
        // door-glass reuse the accent/window mats.
        void BuildFacade(Theme th, Material sign, Material window)
        {
            Material body = MaterialLibrary.GetTheme(th.name, "Facade", th.propMain, 0.40f, 0.05f);
            Material trim = MaterialLibrary.GetTheme(th.name, "FacadeTrim", th.propAlt, 0.45f, 0.06f);
            Material door = MaterialLibrary.GetTheme(th.name, "FacadeDoor",
                new Color(th.accent.r * 0.35f, th.accent.g * 0.35f, th.accent.b * 0.35f, 1f), 0.55f, 0.12f);

            // Thin wall so the steep top-down camera sees the lit interior crowd THROUGH the openings (a
            // thick wall's reveal would occlude anyone standing behind it). 10.5 keeps the ends on-screen.
            const float wallW = 10.5f, wallH = 3.0f, wallD = 0.6f;
            float frontZ = FacadeZ - wallD * 0.5f; // wall face toward the camera
            float backZ  = FacadeZ + wallD * 0.5f; // interior side of the wall

            // Opening layout: ONE wide doorway on the RIGHT of the top wall (TOP-RIGHT of the screen). openH tall
            // (2.35 of the 3.0 wall) so the party characters inside read head-to-toe under the header.
            const int doorCount = 1;
            const float doorSpread = 8.0f, openW = 2.2f, openH = 2.35f;
            var xs = new float[doorCount];
            xs[0] = 3.5f;                 // door on the RIGHT (top-right); the L-queue emerges here, runs down then left
            doorXs = xs;
            exitDoorX = xs[0];

            // Header beam (lintel) spanning the full width above every opening, + roof cornice.
            float headH = wallH - openH;
            LowPolyBuilder.Slab(boardRoot, new Vector3(0, openH + headH * 0.5f, FacadeZ), new Vector3(wallW, headH, wallD), body);
            LowPolyBuilder.Slab(boardRoot, new Vector3(0, wallH + 0.12f, FacadeZ), new Vector3(wallW + 0.7f, 0.32f, wallD + 0.5f), trim);

            // Solid wall pillars filling every span BETWEEN/around the openings (floor -> header). Build the
            // ordered boundary x's (wall ends + each opening edge) and slab each solid gap in pairs.
            float half = openW * 0.5f;
            var edges = new List<float> { -wallW * 0.5f };
            for (int j = 0; j < doorCount; j++) { edges.Add(xs[j] - half); edges.Add(xs[j] + half); }
            edges.Add(wallW * 0.5f);
            for (int e = 0; e < edges.Count; e += 2)
            {
                float x0 = edges[e], x1 = edges[e + 1], w = x1 - x0;
                if (w <= 0.01f) continue;
                LowPolyBuilder.Slab(boardRoot, new Vector3((x0 + x1) * 0.5f, openH * 0.5f, FacadeZ), new Vector3(w, openH, wallD), body);
            }

            // Per-opening framing: a trim lintel strip + a glass transom on the header front.
            for (int j = 0; j < doorCount; j++)
            {
                LowPolyBuilder.Slab(boardRoot, new Vector3(xs[j], openH + 0.08f, frontZ - 0.04f), new Vector3(openW + 0.18f, 0.16f, 0.12f), trim); // flush to opening top — never dips into the head sightline
                LowPolyBuilder.Slab(boardRoot, new Vector3(xs[j], openH + 0.36f, frontZ - 0.05f), new Vector3(openW * 1.05f, 0.42f, 0.08f), window);
            }

            // Interior shell behind the wall so the openings read as INSIDE a building (not a hole to the
            // field): dark floor + back/side walls. Open-topped — the steep camera looks down into it.
            float inBackZ = FacadeZ + 2.2f, inMidZ = (backZ + inBackZ) * 0.5f, inDepth = inBackZ - backZ;
            LowPolyBuilder.Slab(boardRoot, new Vector3(0, -0.03f, inMidZ), new Vector3(wallW, 0.08f, inDepth), door);                          // dark interior floor (top ~+0.01, clears the ground plane — no coplanar z-fight)
            LowPolyBuilder.Slab(boardRoot, new Vector3(0, wallH * 0.45f, inBackZ), new Vector3(wallW, wallH * 0.9f, 0.30f), body);             // back wall
            LowPolyBuilder.Slab(boardRoot, new Vector3(-wallW * 0.5f + 0.15f, wallH * 0.45f, inMidZ), new Vector3(0.30f, wallH * 0.9f, inDepth), body); // L side
            LowPolyBuilder.Slab(boardRoot, new Vector3( wallW * 0.5f - 0.15f, wallH * 0.45f, inMidZ), new Vector3(0.30f, wallH * 0.9f, inDepth), body); // R side

            // The line inside the terminal: party-character people forming ONE bent queue that feeds the single
            // centre door. A back row sits just behind the wall (z 11.35, heads under the header), a front row
            // winds forward INSIDE the doorway opening (|x|<=1.0 so it clears the flank pillars), and one figure
            // steps out the door — reading as a line coming out of the building. The off-centre back-row ends
            // trail behind the flank walls, implying the line continues. (x,z) hand-tuned: every pair >=~0.42
            // apart (bodies ~0.44 wide); only the door-opening figures sit at z<11.2. Purely cosmetic.
            PieceColor[] crowdColors = { PieceColor.Red, PieceColor.Yellow, PieceColor.Blue, PieceColor.Green,
                                         PieceColor.Orange, PieceColor.Pink, PieceColor.Teal, PieceColor.Purple };
            var lineXZ = new[]
            {
                // back row behind the RIGHT door (centred on the door x=3.3); centre shows through the opening
                new Vector2(1.70f,12.22f), new Vector2(2.30f,12.22f), new Vector2(2.90f,12.22f),
                new Vector2(3.50f,12.22f), new Vector2(4.10f,12.22f), new Vector2(4.70f,12.22f),
                // front row, INSIDE the door opening (x 3.5 +/- 0.65 clears the flank pillars)
                new Vector2(2.85f,11.72f), new Vector2(3.28f,11.72f), new Vector2(3.72f,11.72f), new Vector2(4.15f,11.72f),
                // the BEND: front figure stepping out the right door, meeting the boarding queue (back at z~11.0)
                new Vector2(3.50f,11.35f),
            };
            for (int i = 0; i < lineXZ.Length; i++)
            {
                var person = new GameObject("Crowd");
                person.transform.SetParent(boardRoot, false);
                person.transform.position = new Vector3(lineXZ[i].x, 0, lineXZ[i].y);
                BuildCrowdMember(person.transform, crowdColors[i % crowdColors.Length]);
            }

            // sign band over the entrance
            LowPolyBuilder.Slab(boardRoot, new Vector3(0, wallH - 0.35f, frontZ - 0.06f), new Vector3(doorSpread * 0.8f, 0.55f, 0.14f), sign);
        }

        // One STATIC interior-crowd figure: a party-character model (root motion off, idles in place) tinted to
        // `color`, mirroring BuildPersonVisual. Falls back to the code person ONLY if the catalog is empty.
        void BuildCrowdMember(Transform root, PieceColor color)
        {
            GameObject prefab = peopleCatalog != null ? peopleCatalog.RandomPrefab() : null;
            if (prefab == null)
            {
                LowPolyBuilder.BuildPerson(root, bodyMats[color], skinMat, false, false, mysteryMat, goldMat, out _);
                return;
            }
            var model = Instantiate(prefab, root, false);
            model.name = "Model";
            float s = peopleCatalog.modelScale * gameSettings.peopleSize;
            model.transform.localScale = new Vector3(s, s, s);
            model.transform.localPosition = new Vector3(0, peopleCatalog.yOffset, 0);
            model.transform.localRotation = Quaternion.Euler(0, peopleCatalog.yaw, 0);
            var anim = model.GetComponent<Animator>();
            if (anim != null)
            {
                anim.applyRootMotion = false; // stay put — no root-motion walk
                // On budget phones, freeze this static background figure in a standing pose and STOP its
                // per-frame skinning (a dozen of these idling is the biggest steady-state CPU cost on mobile).
                if (lowEnd) { anim.Rebind(); anim.Update(0f); anim.enabled = false; }
            }

            // Tint every non-face material slot to the crowd color (same rule as BuildPersonVisual).
            Material colorMat = bodyMats[color];
            var smr = model.GetComponentInChildren<SkinnedMeshRenderer>();
            if (smr != null)
            {
                var mats = smr.sharedMaterials;
                for (int i = 0; i < mats.Length; i++)
                    if (mats[i] == null || !mats[i].name.ToLowerInvariant().Contains("face")) mats[i] = colorMat;
                smr.sharedMaterials = mats;
            }
        }

        // Where a new person is born so they appear to step OUT of the building: just in front of the ONE exit
        // door (exitDoorX), continuing the bent interior line. They then ease to their queue slot, fanning out
        // from that single door. Falls back to the old off-screen-right spawn when there is no facade.
        Vector3 DoorSpawn(float targetX)
        {
            if (doorXs == null || doorXs.Length == 0) return new Vector3(targetX + 3f * PeopleSpacing, 0, PeopleZ);
            return new Vector3(exitDoorX, 0, DoorSpawnZ); // everyone emerges from THE one exit door
        }

        void BuildLine()
        {
            groups = level.groups;
            nextGroupIndex = 0;
            visible.Clear();
            int init = Mathf.Min(VISIBLE, groups.Count);
            for (int i = 0; i < init; i++)
            {
                var u = CreateUnit(groups[i]);
                u.transform.position = DoorSpawn(LinePos(i).x); // at level start, people pour out of the one exit door
                visible.Add(u);
            }
            nextGroupIndex = init;
        }

        LineUnit CreateUnit(LineGroup g)
        {
            var go = new GameObject("Person");
            go.transform.SetParent(boardRoot, false);
            var u = go.AddComponent<LineUnit>();
            u.color = g.color; u.golden = g.golden; u.mystery = g.mystery;
            BuildPersonVisual(u, go.transform, g.color, g.golden, g.mystery);
            return u;
        }

        // Random party-character model with its BODY tinted to the boarding color (face/hat kept),
        // so the whole body reads as the color. Falls back to the code person if no catalog/model.
        void BuildPersonVisual(LineUnit u, Transform root, PieceColor color, bool golden, bool mystery)
        {
            GameObject prefab = peopleCatalog != null ? peopleCatalog.RandomPrefab() : null;
            if (prefab == null)
            {
                u.body = LowPolyBuilder.BuildPerson(root, bodyMats[color], skinMat, golden, mystery, mysteryMat, goldMat, out GameObject cover);
                u.bodyMaterialIndex = -1;
                u.mysteryCover = cover;
                return;
            }

            var model = Instantiate(prefab, root, false);
            model.name = "Model";
            float s = peopleCatalog.modelScale * gameSettings.peopleSize;
            model.transform.localScale = new Vector3(s, s, s);
            model.transform.localPosition = new Vector3(0, peopleCatalog.yOffset, 0);
            model.transform.localRotation = Quaternion.Euler(0, peopleCatalog.yaw, 0);
            var anim = model.GetComponent<Animator>();
            if (anim != null) anim.applyRootMotion = false; // never let a clip walk the model away

            // Tint the body (every non-face material slot) to the color; grey first if mystery.
            Material colorMat = mystery ? mysteryMat : bodyMats[color];
            var smr = model.GetComponentInChildren<SkinnedMeshRenderer>();
            if (smr != null)
            {
                var mats = smr.sharedMaterials;
                int bodyIndex = -1;
                for (int i = 0; i < mats.Length; i++)
                {
                    if (mats[i] != null && mats[i].name.ToLowerInvariant().Contains("face")) continue;
                    mats[i] = colorMat;
                    if (bodyIndex < 0) bodyIndex = i;
                }
                smr.sharedMaterials = mats;
                u.body = smr;
                u.bodyMaterialIndex = bodyIndex;
            }

            float mh = peopleCatalog.markerHeight;
            if (mystery)
            {
                var q = GameObject.CreatePrimitive(PrimitiveType.Cube);
                Destroy(q.GetComponent<Collider>());
                q.name = "Mystery";
                q.transform.SetParent(root, false);
                q.transform.localPosition = new Vector3(0, mh, 0);
                q.transform.localScale = Vector3.one * 0.18f;
                q.transform.localRotation = Quaternion.Euler(0, 45, 0);
                q.GetComponent<Renderer>().sharedMaterial = mysteryMat;
                u.mysteryCover = q;
            }
            if (golden)
            {
                var crown = GameObject.CreatePrimitive(PrimitiveType.Cube);
                Destroy(crown.GetComponent<Collider>());
                crown.name = "Crown";
                crown.transform.SetParent(root, false);
                crown.transform.localPosition = new Vector3(0, mh, 0);
                crown.transform.localScale = new Vector3(0.3f, 0.1f, 0.3f);
                crown.transform.localRotation = Quaternion.Euler(0, 45, 0);
                crown.GetComponent<Renderer>().sharedMaterial = goldMat;
            }
        }

        Bus CreateBus(PieceColor color, VehicleType type, int capacity, int advanceN, float yaw)
        {
            var root = new GameObject(type + "_" + color);
            root.transform.SetParent(boardRoot, false);
            root.transform.rotation = Quaternion.Euler(0, yaw, 0);
            var bus = root.AddComponent<Bus>();
            bus.color = color; bus.type = type; bus.capacity = capacity; bus.advanceN = advanceN;

            GameObject prefab = vehicleCatalog != null ? vehicleCatalog.PrefabFor(type) : null;
            if (prefab != null)
            {
                BuildImportedVehicle(bus, root.transform, prefab, color, capacity, type); // builds seat-number + "<<" badge
            }
            else
            {
                LowPolyBuilder.BuildVehicle(root.transform, type, CellSize,
                    bodyMats[color], glassMat, wheelMat, lightMat, arrowMat);
                // Cute heads pop onto the roof as people board (replaces the empty-seat NUMBER).
                float cbTop = CellSize * 0.6f, cbLen = LowPolyBuilder.VehicleLength(type, CellSize);
                bus.roofPeople = BuildRoofHeads(root.transform, capacity, color, cbTop, CellSize * 0.26f, cbLen);
                if (advanceN > 0)
                    BuildSpecialBadge(root.transform, advanceN, new Vector3(0, cbTop + 0.12f, -cbLen * 0.42f), Mathf.Clamp(CellSize * 0.42f, 0.3f, 0.6f));
            }
            root.transform.localScale = Vector3.one * gameSettings.vehicleSize; // editable vehicle-size multiplier (both render paths)
            return bus;
        }

        // Instantiate an imported vehicle, drive its BODY ("Main Color 1" = _Color01) to the match
        // color so the body IS the boarding color, keep a white direction arrow, and float a
        // people-colored empty-seat NUMBER above it. No roof tint.
        void BuildImportedVehicle(Bus bus, Transform root, GameObject prefab, PieceColor color, int capacity, VehicleType type)
        {
            var model = Instantiate(prefab, root, false);
            model.name = "Model";
            model.transform.localPosition = Vector3.zero;
            model.transform.localRotation = Quaternion.Euler(0, vehicleCatalog.yaw, 0);
            model.transform.localScale = Vector3.one;

            // Strip physics from the pack prefab — its root Rigidbody+gravity would make the
            // model fall through the floor at Play (leaving only our roof decals visible).
            foreach (var rb in model.GetComponentsInChildren<Rigidbody>(true)) Destroy(rb);
            foreach (var c in model.GetComponentsInChildren<Collider>(true)) Destroy(c);

            // Drive the body — "Main Color 1" (_Color01) — to the match color; keep windows/wheels (_Color02..08).
            foreach (var r in model.GetComponentsInChildren<Renderer>(true))
            {
                var mats = r.sharedMaterials;
                for (int i = 0; i < mats.Length; i++)
                    if (mats[i] != null) mats[i] = TintedVehicleMat(mats[i], color);
                r.sharedMaterials = mats;
            }

            // Auto-face forward: rotate the model so its LONGEST horizontal axis runs along the root's
            // local Z (the exit direction), regardless of the pack's native orientation. Measured in
            // ROOT-LOCAL space (NOT a world AABB) so the decision is INDEPENDENT of the root's world yaw —
            // a diagonal (±45°-yawed) vehicle decides exactly like a cardinal one. (A world AABB is square-ish
            // at 45°, so the old test was an unstable tie that flipped some diagonal bodies crosswise to their arrow.)
            Bounds faceB = ModelBoundsIn(root, model);
            if (faceB.size.x > faceB.size.z)
                model.transform.localRotation = Quaternion.Euler(0, vehicleCatalog.yaw + 90f, 0);

            // Span the vehicle's grid footprint: CellLength cells (Car 1 / Bus 2 / Limo 3).
            float target = Vehicles.CellLength(type) * CellSize * vehicleCatalog.fitFactor;

            var rends = model.GetComponentsInChildren<Renderer>();
            float span = target, wid = target * 0.5f, roofY = CellSize * 0.5f;

            // Measure the model in its OWN LOCAL frame (from mesh bounds), NOT a world AABB: a world AABB
            // inflates for a 45deg-yawed (diagonal) body, which would make diagonal vehicles a DIFFERENT size
            // than straight ones. Local measurement -> every bus is identical regardless of direction.
            Bounds lb = default; bool localFrame = false;
            foreach (var mf in model.GetComponentsInChildren<MeshFilter>())
            {
                if (mf.sharedMesh == null) continue;
                Matrix4x4 toModel = model.transform.worldToLocalMatrix * mf.transform.localToWorldMatrix;
                var mb = mf.sharedMesh.bounds; Vector3 c = mb.center, e = mb.extents;
                for (int sx = -1; sx <= 1; sx += 2) for (int sy = -1; sy <= 1; sy += 2) for (int sz = -1; sz <= 1; sz += 2)
                {
                    Vector3 p = toModel.MultiplyPoint3x4(c + new Vector3(sx * e.x, sy * e.y, sz * e.z));
                    if (!localFrame) { lb = new Bounds(p, Vector3.zero); localFrame = true; } else lb.Encapsulate(p);
                }
            }
            if (!localFrame && rends.Length > 0) // fallback: world bounds (no MeshFilters)
            { lb = rends[0].bounds; for (int i = 1; i < rends.Length; i++) lb.Encapsulate(rends[i].bounds); }
            if (localFrame || rends.Length > 0)
            {
                float len = Mathf.Max(lb.size.x, lb.size.z, 0.01f);
                float widRaw = Mathf.Max(Mathf.Min(lb.size.x, lb.size.z), 0.01f);
                // UNIFORM scale keeps the model's true PROPORTIONS (no width stretching); fit length to the
                // L-cell span, lightly boosted (1.05) so vehicles fill their cells WITHOUT overlapping/meshing.
                float scl = Mathf.Min(target / len, (CellSize * 1.1f) / widRaw) * 1.05f;
                model.transform.localScale = Vector3.one * scl;
                float bottom = localFrame ? lb.min.y : (lb.min.y - root.position.y);
                // Re-center the body on the root origin in X/Z (a pack pivot is often NOT the mesh center),
                // so the roof arrow + heads + tap box sit symmetric on the ACTUAL body, not the pivot.
                Vector3 ctr = localFrame ? model.transform.localRotation * (lb.center * scl) : Vector3.zero;
                model.transform.localPosition = new Vector3(-ctr.x, -bottom * scl + vehicleCatalog.yOffset, -ctr.z);
                roofY = lb.size.y * scl + vehicleCatalog.yOffset;
                span = len * scl;
                wid = widRaw * scl;
            }

            // True top of the model (recompute AFTER scaling/positioning) so the roof markers
            // always sit ABOVE the body and never sink into it.
            float topY = roofY;
            {
                var rr = model.GetComponentsInChildren<Renderer>();
                if (rr.Length > 0)
                {
                    Bounds tb = rr[0].bounds;
                    for (int i = 1; i < rr.Length; i++) tb.Encapsulate(rr[i].bounds);
                    topY = tb.max.y - root.position.y; // root unscaled & only Y-rotated, so world maxY == local top
                }
            }

            // Tappable box (the prefab's colliders were stripped).
            var box = root.gameObject.AddComponent<BoxCollider>();
            box.center = new Vector3(0, topY * 0.5f, 0);
            box.size = new Vector3(Mathf.Max(wid, CellSize * 0.4f), Mathf.Max(topY, 0.5f), span);

            // Clean, symmetric arrow at the nose; cute heads pop in behind it as people board.
            BuildRoofArrow(root, topY, wid * 0.5f, span);
            bus.roofPeople = BuildRoofHeads(root, capacity, color, topY, wid * 0.5f, span);

            // Special "<<" crawler badge at the FRONT (distinct Y/Z from the passengers so they never overlap).
            if (bus.advanceN > 0)
                BuildSpecialBadge(root, bus.advanceN, new Vector3(0, topY + 0.12f, -span * 0.42f), Mathf.Clamp(wid * 0.6f, 0.3f, 0.7f));
        }

        // The boarding/match color as a Color (from the Bus_<color> palette material's base color).
        Color PeopleColor(PieceColor color) =>
            bodyMats[color].HasProperty("_BaseColor") ? bodyMats[color].GetColor("_BaseColor") : bodyMats[color].color;

        // A per-(material,color) instance of a pack vehicle material with "Main Color 1" (_Color01)
        // set to the match color, so the BODY shows the boarding color while windows/wheels stay.
        Material TintedVehicleMat(Material baseMat, PieceColor color)
        {
            var key = (baseMat, color);
            if (!tintedVehicleMats.TryGetValue(key, out var m))
            {
                m = new Material(baseMat);
                if (m.HasProperty("_Color01")) m.SetColor("_Color01", PeopleColor(color));
                // Match the code-vehicle/people candy finish so imported vehicles don't read as a different gloss/metal.
                if (m.HasProperty("_Smoothness")) m.SetFloat("_Smoothness", 0.65f);
                if (m.HasProperty("_Glossiness")) m.SetFloat("_Glossiness", 0.65f);
                if (m.HasProperty("_Metallic"))   m.SetFloat("_Metallic", 0f);
                tintedVehicleMats[key] = m;
            }
            return m;
        }

        // World-space "PEOPLE LEFT" sign on a post beside the road. Billboards to the camera (world-space
        // canvas + flip-cancel, like the crawler badge). Wired to the LOGICAL pool via UpdatePeopleLeft.
        void BuildPeopleLeftSign()
        {
            float signW = 0.54f, signH = 0.84f;
            float frameHalf = (signW + 0.14f) * 0.5f;
            // Just LEFT of the first (leftmost) bus stop, but CLAMPED so the whole sign stays on-screen
            // even when a level has many parking slots (SlotX(0) can run off the left edge otherwise).
            float sx = Mathf.Max(SlotX(0) - 0.85f, -(VisHalfW(ParkingZ) - frameHalf - 0.08f));
            float sz = ParkingZ;          // at the bus-stop row
            float topY = 1.5f;

            // Post + a NEON emissive frame (glows under bloom) with a dark board in front for contrast.
            var post = MakeCube(boardRoot, seatEmptyMat, new Vector3(0.1f, topY, 0.1f));
            post.transform.position = new Vector3(sx, topY * 0.5f, sz);
            var frame = MakeCube(boardRoot, neonMat, new Vector3(signW + 0.14f, signH + 0.14f, 0.05f));
            frame.transform.position = new Vector3(sx, topY + 0.4f, sz + 0.02f);   // behind (camera is at -Z) → neon halo edge
            var board = MakeCube(boardRoot, seatEmptyMat, new Vector3(signW, signH, 0.06f));
            board.transform.position = new Vector3(sx, topY + 0.4f, sz);

            // Camera-facing neon count + caption.
            var go = new GameObject("PeopleLeftSign", typeof(RectTransform), typeof(Canvas));
            go.transform.SetParent(boardRoot, false);
            go.GetComponent<Canvas>().renderMode = RenderMode.WorldSpace;
            ((RectTransform)go.transform).sizeDelta = new Vector2(120, 120);
            go.transform.position = new Vector3(sx, topY + 0.4f, sz - 0.05f);
            go.transform.localScale = new Vector3(-1f, 1f, 1f) * (signW / 120f); // -X cancels the billboard flip; matches the board width
            go.AddComponent<BillboardUp>();

            Color neon = new Color(0.3f, 1f, 0.8f); // bright neon cyan-green (blooms on capable devices)
            AddSignText(go.transform, "LEFT", 26, new Vector2(0, 0.62f), new Vector2(1, 1f), neon);
            peopleLeftSign = AddSignText(go.transform, PeopleLeft().ToString(), 64, new Vector2(0, 0f), new Vector2(1, 0.62f), neon);
        }

        // A bold, outlined, camera-facing UI.Text child filling [anchorMin..anchorMax] of a sign canvas.
        UnityEngine.UI.Text AddSignText(Transform parent, string text, int fontSize, Vector2 anchorMin, Vector2 anchorMax, Color color)
        {
            var go = new GameObject("Text", typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = anchorMin; rt.anchorMax = anchorMax;
            rt.offsetMin = Vector2.zero; rt.offsetMax = Vector2.zero;
            var txt = go.AddComponent<UnityEngine.UI.Text>();
            txt.font = seatFont; txt.text = text; txt.fontSize = fontSize;
            txt.fontStyle = FontStyle.Bold; txt.alignment = TextAnchor.MiddleCenter; txt.color = color;
            txt.horizontalOverflow = HorizontalWrapMode.Overflow; txt.verticalOverflow = VerticalWrapMode.Overflow;
            var outline = go.AddComponent<UnityEngine.UI.Outline>();
            outline.effectColor = Color.black; outline.effectDistance = new Vector2(3, 3);
            return txt;
        }

        // Clean, SYMMETRIC roof arrow (a diamond head + shaft) centered on x=0, pointing local -Z (the exit
        // dir), flat on the roof front. Used on the imported path; the code-built path has its own.
        void BuildRoofArrow(Transform root, float topY, float halfWidth, float span)
        {
            float y = topY + 0.05f;
            float frontZ = -span * 0.5f;
            float zoneLen = span * 0.32f;                                           // arrow lives in the front ~third
            float aw = Mathf.Clamp(Mathf.Min(halfWidth * 0.85f, zoneLen * 0.45f), 0.12f, 0.4f);
            float headZ = frontZ + aw + 0.03f;                                      // diamond head just inside the nose
            float shaftLen = Mathf.Max(zoneLen - aw * 1.6f, aw * 0.7f);

            var head = MakeCube(root, arrowMat, new Vector3(aw * 1.3f, 0.05f, aw * 1.3f));
            head.transform.localPosition = new Vector3(0, y, headZ);
            head.transform.localRotation = Quaternion.Euler(0, 45f, 0);             // 45° cube = diamond, tip toward -Z

            var shaft = MakeCube(root, arrowMat, new Vector3(aw * 0.42f, 0.05f, shaftLen));
            shaft.transform.localPosition = new Vector3(0, y, headZ + aw * 0.7f + shaftLen * 0.5f);
        }

        // Cute heads on the roof — one per seat, HIDDEN until that passenger boards (Bus.LightSeat pops it in).
        // No empty seats: the body color says WHICH color; the filling heads show people getting on. Laid out
        // in 1–2 centered columns BEHIND the arrow. Parented to root (yaws with the vehicle, diagonals included).
        // Returns the head GameObjects (index = seat) so LightSeat(i) reveals head i.
        GameObject[] BuildRoofHeads(Transform root, int capacity, PieceColor color, float topY, float halfWidth, float span)
        {
            var heads = new GameObject[Mathf.Max(capacity, 0)];
            if (capacity <= 0) return heads;

            Material capMat = bodyMats[color];                            // people-color cap = a cute pop of the boarding color
            int cols = capacity >= 4 ? 2 : 1;
            int rows = Mathf.CeilToInt(capacity / (float)cols);
            float zFront = -span * 0.05f, zBack = span * 0.46f;           // start clear of the front arrow zone
            float rowPitch = rows > 1 ? (zBack - zFront) / (rows - 1) : 0f;
            float colX = cols > 1 ? Mathf.Clamp(halfWidth * 0.55f, 0.1f, 0.4f) : 0f;
            // Head diameter keyed off BOTH spacings so heads never overlap (dense Limo = 16 seats).
            float rowSpace = rows > 1 ? rowPitch : span * 0.5f;
            float colSpace = cols > 1 ? colX * 1.7f : halfWidth * 1.5f;
            float d = Mathf.Clamp(Mathf.Min(rowSpace, colSpace) * 0.85f, 0.11f, 0.28f);
            float baseY = topY + 0.02f;

            for (int i = 0; i < capacity; i++)
            {
                int r = i / cols, c = i % cols;
                float x = cols == 1 ? 0f : (c == 0 ? -colX : colX);
                float z = zFront + r * rowPitch;

                var pax = new GameObject("Pax" + i);
                pax.transform.SetParent(root, false);
                pax.transform.localPosition = new Vector3(x, baseY, z);

                var dome = MakePrim(pax.transform, skinMat, PrimitiveType.Sphere, new Vector3(d, d * 0.92f, d));
                dome.name = "Head";
                dome.transform.localPosition = new Vector3(0, d * 0.46f, 0);

                var cap = MakePrim(pax.transform, capMat, PrimitiveType.Sphere, new Vector3(d * 0.8f, d * 0.42f, d * 0.8f));
                cap.name = "Hat";
                cap.transform.localPosition = new Vector3(0, d * 0.78f, 0);

                pax.SetActive(false);
                heads[i] = pax;
            }
            return heads;
        }

        // The model's bounding box expressed in ROOT-LOCAL axes (yaw-independent), from mesh bounds.
        // Used by the imported auto-face so a diagonal (±45°-yawed) body decides like a cardinal one.
        static Bounds ModelBoundsIn(Transform root, GameObject model)
        {
            Bounds b = default; bool init = false;
            foreach (var mf in model.GetComponentsInChildren<MeshFilter>())
            {
                if (mf.sharedMesh == null) continue;
                Matrix4x4 toRoot = root.worldToLocalMatrix * mf.transform.localToWorldMatrix;
                var mb = mf.sharedMesh.bounds; Vector3 c = mb.center, e = mb.extents;
                for (int sx = -1; sx <= 1; sx += 2) for (int sy = -1; sy <= 1; sy += 2) for (int sz = -1; sz <= 1; sz += 2)
                {
                    Vector3 p = toRoot.MultiplyPoint3x4(c + new Vector3(sx * e.x, sy * e.y, sz * e.z));
                    if (!init) { b = new Bounds(p, Vector3.zero); init = true; } else b.Encapsulate(p);
                }
            }
            return b;
        }

        // Collider-free primitive of any type (sphere/capsule for roof passengers), parented + scaled + tinted.
        GameObject MakePrim(Transform parent, Material mat, PrimitiveType type, Vector3 scale)
        {
            var go = GameObject.CreatePrimitive(type);
            Destroy(go.GetComponent<Collider>());
            go.transform.SetParent(parent, false);
            go.transform.localScale = scale;
            go.GetComponent<Renderer>().sharedMaterial = mat;
            return go;
        }

        // Camera-facing "<<N" badge marking a special crawler (N cells advanced per tap).
        void BuildSpecialBadge(Transform root, int advanceN, Vector3 localPos, float worldSize)
        {
            var go = new GameObject("SpecialBadge", typeof(RectTransform), typeof(Canvas));
            go.transform.SetParent(root, false);
            go.GetComponent<Canvas>().renderMode = RenderMode.WorldSpace;
            ((RectTransform)go.transform).sizeDelta = new Vector2(100, 100);
            go.transform.localPosition = localPos;
            go.transform.localScale = new Vector3(-1f, 1f, 1f) * (Mathf.Max(worldSize, 0.2f) / 100f); // -X cancels billboard flip
            go.AddComponent<BillboardUp>();

            // Distinct amber panel so it never reads as the people-color seat number.
            var bgGo = new GameObject("BG", typeof(RectTransform));
            bgGo.transform.SetParent(go.transform, false);
            var bg = bgGo.AddComponent<UnityEngine.UI.Image>();
            bg.color = new Color(0.96f, 0.55f, 0.15f, 1f);
            Stretch(bg.rectTransform);

            var txtGo = new GameObject("Text", typeof(RectTransform));
            txtGo.transform.SetParent(go.transform, false);
            var txt = txtGo.AddComponent<UnityEngine.UI.Text>();
            txt.font = seatFont;
            txt.text = "«" + advanceN; // « = "<<" double-chevron + step count
            txt.fontSize = 52;
            txt.fontStyle = FontStyle.Bold;
            txt.alignment = TextAnchor.MiddleCenter;
            txt.color = Color.white;
            Stretch(txt.rectTransform);
            var outline = txtGo.AddComponent<UnityEngine.UI.Outline>();
            outline.effectColor = Color.black;
            outline.effectDistance = new Vector2(2, 2);
        }

        static void Stretch(RectTransform rt)
        {
            rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero; rt.offsetMax = Vector2.zero;
        }

        // ====================================================================
        // Positions
        // ====================================================================
        float SlotX(int i) => (i - (totalSlots - 1) / 2f) * SlotSpacing;
        Vector3 ParkingWorld(int i) => new Vector3(SlotX(i), 0, ParkingZ);
        // Grid y=0 sits at GridExitZ (top of grid, nearest parking); deeper rows extend toward the camera.
        Vector3 GridWorld(Vector2Int c) => new Vector3((c.x - (gridW - 1) / 2f) * CellSize, 0, GridExitZ - c.y * CellSize);
        // Center of an L-cell vehicle whose leading cell is `anchor`, body extending back along -dir.
        Vector3 GridWorldCenter(Vector2Int anchor, Vector2Int dir, int length) =>
            GridWorld(anchor) - new Vector3(dir.x, 0, -dir.y) * ((length - 1) * 0.5f * CellSize);
        // People: an L-shaped queue "__ı" fed by the TOP-RIGHT door. index 0 = front (boards), index 9 = back (at
        // the door). From the door it runs DOWN the right edge (x=doorX), turns at the bottom-right corner, then
        // LEFT along the bottom (z=bottomZ) toward the buses. Hugs the right + bottom, leaving the centre/left for
        // the big jam. bottomZ=8.0 clears the parked vehicles' noses (parking 6.2 + up to ~1.5 half-length).
        Vector3 LinePos(int index)
        {
            const float doorX = 3.5f, horizZ = 10.3f, vGap = 0.7f, hSpacing = 0.9f;
            const int cornerIdx = 8;     // index 9,8 = the 2-person VERTICAL stub at the top-right (8 = corner);
            if (index >= cornerIdx)      // 7..0 = the HORIZONTAL run left across the full width to the front
                return new Vector3(doorX, 0, horizZ + (index - cornerIdx) * vGap);   // up the right edge (corner -> door)
            return new Vector3(doorX - (cornerIdx - index) * hSpacing, 0, horizZ);   // left along the horizontal (z=10.3)
        }

        Vector3 BusDoorWorld(Bus bus)
        {
            float len = LowPolyBuilder.VehicleLength(bus.type, CellSize);
            return bus.transform.position + new Vector3(0, 0.25f, len * 0.4f); // +Z = toward the people band
        }

        // Arrow yaw so a bus visually points the way it will exit (toward parking / sides).
        // World exit dir = (d.x,0,-d.y); model nose points local -Z. Atan2 handles all 8 dirs (cardinals
        // verify: (0,-1)->180, (0,1)->0, (-1,0)->90, (1,0)->-90; diagonals (1,1)->-45, (-1,1)->45, etc.).
        float DirYaw(Vector2Int d) => Mathf.Atan2(-d.x, d.y) * Mathf.Rad2Deg;

        ParkingSlot FirstFreeSlot() { foreach (var s in slots) if (s.IsFree) return s; return null; }
        ParkingSlot NearestFreeSlot(float x)
        {
            ParkingSlot best = null; float bd = float.MaxValue;
            foreach (var s in slots)
                if (s.IsFree) { float d = Mathf.Abs(SlotX(s.index) - x); if (d < bd) { bd = d; best = s; } }
            return best;
        }
        bool HasLockedSlot() { foreach (var s in slots) if (s.locked) return true; return false; }

        // ====================================================================
        // Coroutine helpers
        // ====================================================================
        static IEnumerator MoveTo(Transform t, Vector3 target, float dur, bool ease = false)
        {
            if (t == null) yield break;
            Vector3 from = t.position;
            float e = 0f;
            while (e < dur)
            {
                if (t == null) yield break;
                e += Time.deltaTime;
                float k = Mathf.Clamp01(e / dur);
                if (ease) k = EaseOutBack(k);
                t.position = Vector3.LerpUnclamped(from, target, k);
                yield return null;
            }
            if (t != null) t.position = target;
        }

        static IEnumerator MoveAndRotateArc(Transform t, Vector3 target, Quaternion rot, float dur, float arc)
        {
            if (t == null) yield break;
            Vector3 from = t.position; Quaternion fr = t.rotation;
            float e = 0f;
            while (e < dur)
            {
                if (t == null) yield break;
                e += Time.deltaTime;
                float k = Mathf.Clamp01(e / dur);
                Vector3 p = Vector3.Lerp(from, target, k);
                p.y += Mathf.Sin(k * Mathf.PI) * arc;
                t.position = p;
                t.rotation = Quaternion.Slerp(fr, rot, Mathf.Clamp01(k * 1.6f));
                yield return null;
            }
            if (t != null) { t.position = target; t.rotation = rot; }
        }

        // Smoothly drive a grounded transform along a Catmull-Rom spline through `pts` at ~constant `speed`, easing
        // the nose toward the travel tangent (`turnLerp` = how lazily it steers; lower is more gradual). ONE
        // continuous motion: corners are ROUNDED, so there is no per-waypoint stop and turns are car-like sweeps,
        // not 90° snaps. Model nose = local -Z, so the yaw points -Z along the tangent.
        static IEnumerator DrivePath(Transform t, List<Vector3> pts, float speed, float turnLerp = 6f)
        {
            if (t == null || pts == null || pts.Count == 0) yield break;
            var c = new List<Vector3>(pts.Count + 1) { t.position };
            c.AddRange(pts);
            if (c.Count < 2) yield break;
            Vector3 C(int i) => c[Mathf.Clamp(i, 0, c.Count - 1)];
            var s = new List<Vector3> { c[0] };                                  // dense arc-length spline samples
            for (int i = 0; i < c.Count - 1; i++)
            {
                int n = Mathf.Max(2, Mathf.CeilToInt(Vector3.Distance(C(i), C(i + 1)) / 0.1f));
                for (int k = 1; k <= n; k++) s.Add(CatmullRom(C(i - 1), C(i), C(i + 1), C(i + 2), k / (float)n));
            }
            int idx = 0;
            while (idx < s.Count - 1)
            {
                if (t == null) yield break;
                float move = Mathf.Max(speed, 0.01f) * Time.deltaTime;
                while (idx < s.Count - 1 && move > 0f)
                {
                    float d = Vector3.Distance(t.position, s[idx + 1]);
                    if (d <= move) { move -= d; t.position = s[idx + 1]; idx++; }
                    else { t.position = Vector3.MoveTowards(t.position, s[idx + 1], move); break; }
                }
                Vector3 look = s[Mathf.Min(idx + 1, s.Count - 1)] - t.position;
                if (look.sqrMagnitude > 1e-5f)
                    t.rotation = Quaternion.Slerp(t.rotation, Quaternion.Euler(0, Mathf.Atan2(-look.x, -look.z) * Mathf.Rad2Deg, 0),
                                                  1f - Mathf.Exp(-turnLerp * Time.deltaTime));
                yield return null;
            }
            if (t != null) t.position = s[s.Count - 1];
        }

        static Vector3 CatmullRom(Vector3 p0, Vector3 p1, Vector3 p2, Vector3 p3, float u)
        {
            float u2 = u * u, u3 = u2 * u;
            return 0.5f * (2f * p1 + (-p0 + p2) * u + (2f * p0 - 5f * p1 + 4f * p2 - p3) * u2 + (-p0 + 3f * p1 - 3f * p2 + p3) * u3);
        }

        static float EaseOutBack(float x)
        {
            const float c1 = 1.70158f, c3 = 2.70158f;
            float xm = x - 1f;
            return 1f + c3 * xm * xm * xm + c1 * xm * xm;
        }

        static IEnumerator Bump(Transform t)
        {
            if (t == null) yield break;
            Vector3 p = t.position;
            for (int i = 0; i < 6; i++)
            {
                if (t == null) yield break;
                t.position = p + new Vector3(Mathf.Sin(i * 1.6f) * 0.08f, 0, 0);
                yield return null;
            }
            if (t != null) t.position = p;
        }

        // ====================================================================
        // Materials & theme environment
        // ====================================================================
        void BuildMaterials()
        {
            // Editable .mat assets from Resources/Materials if present, else runtime fallbacks.
            var lib = MaterialLibrary.BuildAll();
            var list = new List<Material>();
            foreach (PieceColor c in System.Enum.GetValues(typeof(PieceColor)))
            {
                bodyMats[c] = lib[MaterialLibrary.BusKey(c)];
                list.Add(bodyMats[c]);
            }
            confettiMats = list.ToArray();

            glassMat     = lib["Glass"];
            wheelMat     = lib["Wheel"];
            lightMat     = lib["Headlight"];
            skinMat      = lib["Skin"];
            seatEmptyMat = lib["SeatEmpty"];
            mysteryMat   = lib["Mystery"];
            goldMat      = lib["Gold"];
            arrowMat     = lib["Arrow"];
            lockMat      = lib["Lock"];
            slotMat      = lib["SlotPad"];   // stable + editable (was theme accent)
            roadMat      = MaterialLibrary.MakeRuntime(new Color(0.16f, 0.17f, 0.19f), 0.18f);       // STANDARD dark asphalt — same on every theme/level
            neonMat      = MaterialLibrary.MakeRuntime(new Color(0.12f, 1f, 0.70f), 0.5f, 1.7f);      // emissive neon (glows under bloom) for the people-left sign
        }

        GameObject MakeCube(Transform parent, Material mat, Vector3 scale)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            Destroy(go.GetComponent<Collider>());
            go.transform.SetParent(parent, false);
            go.transform.localScale = scale;
            go.GetComponent<Renderer>().sharedMaterial = mat;
            return go;
        }

        void ApplyTheme(Theme th)
        {
            if (cam != null) { cam.clearFlags = CameraClearFlags.SolidColor; cam.backgroundColor = th.sky; }
            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
            RenderSettings.ambientLight = th.ambient * 1.0f;   // a touch less fill = more contrast/pop (post-exposure adds brightness back)
            var sun = Object.FindAnyObjectByType<Light>();
            if (sun != null && sun.type == LightType.Directional)
            {
                sun.color = th.lightColor;                                  // warm/cool key per theme
                sun.intensity = th.lightIntensity * 1.2f;                   // stronger key = crisper, less-flat shading
                sun.transform.rotation = Quaternion.Euler(52f, -34f, 0f);   // pleasant diagonal so shadows read on the top-down framing
                sun.shadows = lowEnd ? LightShadows.None : LightShadows.Soft; // budget phones skip the shadowmap pass (objects still read grounded on the ground slab)
                sun.shadowStrength = 0.55f;                                 // soft, not pitch-black
            }

            // Editable per-theme env material assets (Resources/Materials/<Theme>_<Type>), else runtime fallback.
            // smoothness/emission here MATCH MaterialLibrary.ThemeTypes so the fallback looks like the asset.
            Material ground = MaterialLibrary.GetTheme(th.name, "Ground", th.ground, 0.35f, 0.05f);
            Material field  = MaterialLibrary.GetTheme(th.name, "Field", th.field, 0.28f, 0.03f);
            Material accent = MaterialLibrary.GetTheme(th.name, "Accent", th.accent, 0.45f, 0.06f);
            Material main   = MaterialLibrary.GetTheme(th.name, "PropMain", th.propMain, 0.45f, 0.05f);
            Material alt    = MaterialLibrary.GetTheme(th.name, "PropAlt", th.propAlt, 0.45f, 0.05f);
            Material foliage= MaterialLibrary.GetTheme(th.name, "Foliage", th.foliage, 0.35f, 0.06f);
            Material trunk  = MaterialLibrary.GetTheme(th.name, "Trunk", th.trunk, 0.25f);
            Material grass  = MaterialLibrary.GetTheme(th.name, "Grass", th.grass, 0.30f, 0.06f);
            Material window = MaterialLibrary.GetTheme(th.name, "Window", new Color(th.sky.r * 0.9f + 0.1f, th.sky.g * 0.9f + 0.1f, th.sky.b, 1f), 0.7f, 0.25f);
            Material cloud  = MaterialLibrary.GetTheme(th.name, "Cloud", new Color(1f, 1f, 1f), 0f, 0.18f);
            // slotMat is now a stable, editable asset set in BuildMaterials (no theme override).

            LowPolyBuilder.Slab(boardRoot, new Vector3(0, -0.32f, 3f), new Vector3(46, 0.3f, 70), field);
            LowPolyBuilder.Slab(boardRoot, new Vector3(0, -0.12f, 3f), new Vector3(12f, 0.2f, 30), ground);
            // Distinct ROAD lane BELOW the parking stops (own band at RoadZ, between jam and stops) — full
            // buses drive off-screen sideways ALONG it. Raised to y=-0.10 ABOVE the ground slab (y=-0.12) so
            // it reads as a real road, and slimmed to a 1.0 lane (clears the stops). Spans past both screen edges.
            LowPolyBuilder.Slab(boardRoot, new Vector3(0, -0.10f, RoadZ), new Vector3(28f, 0.2f, 1.0f), roadMat); // STANDARD asphalt every level; slimmer so it clears the stops

            for (int i = -4; i <= 4; i++)
            {
                var post = MakeCube(boardRoot, accent, new Vector3(0.1f, 0.5f, 0.1f));
                post.transform.position = new Vector3(i * 1.1f, 0.25f, FenceZ);
                var bar = MakeCube(boardRoot, accent, new Vector3(1.1f, 0.06f, 0.05f));
                bar.transform.position = new Vector3(i * 1.1f + 0.55f, 0.34f, FenceZ);
            }

            // Side scatter — alternate the theme's two prop kinds for variety (halved on low-end).
            for (int i = 0; i < (lowEnd ? 3 : 6); i++)
            {
                float z = -1f + i * 2.6f;
                PropKind k = (i % 2 == 0) ? th.prop : th.prop2;
                LowPolyBuilder.BuildProp(boardRoot, k, new Vector3(-6.8f, 0, z), main, alt, foliage, trunk, window, 1f);
                LowPolyBuilder.BuildProp(boardRoot, k, new Vector3(6.8f, 0, z), main, alt, foliage, trunk, window, 1f);
            }

            // Behind the people band: a closed mall/terminal FACADE (people emerge from its doors), else
            // the legacy house centerpiece / prop row.
            doorXs = null;
            float backZ = PeopleZ + 4f;
            if (th.hasFacade)
            {
                BuildFacade(th, accent, window);
            }
            else if (th.hasHouse)
            {
                LowPolyBuilder.BuildProp(boardRoot, PropKind.House, new Vector3(0, 0, backZ + 0.6f), main, alt, foliage, trunk, window, 1.8f);
                LowPolyBuilder.BuildProp(boardRoot, PropKind.RoundTree, new Vector3(-4.4f, 0, backZ), main, alt, foliage, trunk, window, 1.8f);
                LowPolyBuilder.BuildProp(boardRoot, PropKind.RoundTree, new Vector3(4.4f, 0, backZ), main, alt, foliage, trunk, window, 1.8f);
                LowPolyBuilder.BuildProp(boardRoot, PropKind.Bush, new Vector3(-2.1f, 0, backZ - 1.3f), main, alt, foliage, trunk, window, 1.4f);
                LowPolyBuilder.BuildProp(boardRoot, PropKind.Bush, new Vector3(2.1f, 0, backZ - 1.3f), main, alt, foliage, trunk, window, 1.4f);
            }
            else
            {
                for (int i = 0; i < 5; i++)
                {
                    PropKind k = (i % 2 == 0) ? th.prop : th.prop2;
                    LowPolyBuilder.BuildProp(boardRoot, k, new Vector3(-5f + i * 2.5f, 0, backZ), main, alt, foliage, trunk, window, 1.7f);
                }
            }

            // Grass tufts dressing the back lawn — skipped behind the closed facade (paved terminal plaza,
            // and they would otherwise poke through the wall front).
            if (!th.hasFacade && !lowEnd)
                for (int i = 0; i < 12; i++)
                {
                    float gx = -7.5f + (i * 1.45f) % 15f;
                    float gz = (PeopleZ + 1.8f) + (i % 3) * 1.1f;
                    LowPolyBuilder.GrassTuft(boardRoot, new Vector3(gx, 0, gz), 1.0f, grass);
                }

            for (int k = 0; k < (lowEnd ? 2 : 4); k++)
                MakeCloud(new Vector3(-5.5f + k * 3.5f, 9f + (k % 2) * 1.2f, 10f + (k % 3) * 2.5f), cloud, k);
        }

        void MakeCloud(Vector3 pos, Material mat, int seed)
        {
            var cloud = new GameObject("Cloud");
            cloud.transform.SetParent(boardRoot, false);
            cloud.transform.position = pos;
            float[] dx = { -0.6f, 0.2f, 0.9f };
            float[] sc = { 1.0f, 1.3f, 0.9f };
            for (int i = 0; i < 3; i++)
            {
                var s = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                Destroy(s.GetComponent<Collider>());
                s.transform.SetParent(cloud.transform, false);
                s.transform.localPosition = new Vector3(dx[i], 0, 0);
                s.transform.localScale = new Vector3(1.4f, 0.8f, 1.2f) * sc[i];
                s.GetComponent<Renderer>().sharedMaterial = mat;
            }
            var drift = cloud.AddComponent<IdleBob>();
            drift.axis = Vector3.right; drift.amp = 1.6f; drift.speed = 0.22f; drift.phase = seed * 1.3f;
        }

        void PlaceCamera()
        {
            if (cam == null) return;
            // Zoomed-in steep top-down (T3): the big-cell jam + the people line both fill the portrait frame.
            // FOV 54 keeps the 6-wide jam's deepest corner on-screen on a tall (0.462) phone. Tune in-editor.
            Vector3 pos = new Vector3(0f, 16f, -6f);
            Vector3 target = new Vector3(0f, 0f, 3.2f);
            cam.transform.position = pos;
            cam.transform.rotation = Quaternion.LookRotation(target - pos, Vector3.up);
            cam.fieldOfView = 54f;
        }

        // Builds the whole "fantastic look" in code (matches the build-on-Start philosophy):
        // enables post on the camera + a single global Volume that grades the entire frame.
        // The global saturation/exposure here is what lifts EVERY material — including the
        // baked theme env materials — so the per-material Vibrant() lift and this stack stack up.
        void SetupPostFX()
        {
            if (cam == null) return;

            // DEVICE TIER (lowEnd set in Start) — so it runs on EVERY phone. Budget mobiles drop the GPU-heavy
            // effects (no AA, no Bloom, no HDR) and keep ONLY the cheap single-pass grade, which still carries
            // the vibrant look. Capable mobile gets FXAA + Bloom; desktop/editor keep SMAA (the authored look).
            cam.allowHDR = !lowEnd; // HDR bandwidth only pays off with Bloom; off on low-end

            var camData = cam.GetUniversalAdditionalCameraData();
            if (camData != null)
            {
                camData.renderPostProcessing = true;
                // AA tier: low-end none; capable mobile FXAA (cheap); desktop/editor SMAA (crisp, as authored).
                if (lowEnd) camData.antialiasing = AntialiasingMode.None;
                else if (Application.isMobilePlatform) { camData.antialiasing = AntialiasingMode.FastApproximateAntialiasing; camData.antialiasingQuality = AntialiasingQuality.Low; }
                else { camData.antialiasing = AntialiasingMode.SubpixelMorphologicalAntiAliasing; camData.antialiasingQuality = AntialiasingQuality.High; }
                camData.dithering = !lowEnd; // banding-kill, skip on low-end
            }

            // One global volume, priority above the project's default profile, drives the look.
            var go = new GameObject("PostFX");
            go.transform.SetParent(transform, false);
            var vol = go.AddComponent<Volume>();
            vol.isGlobal = true;
            vol.priority = 100f;
            var p = ScriptableObject.CreateInstance<VolumeProfile>();
            vol.sharedProfile = p;

            // ---- CHEAP grade (single uber-post pass) — ALWAYS on, keeps the vibrant pop everywhere. ----
            // Filmic tonemap — Neutral preserves hue/saturation (ACES would mute the candy pop).
            var tm = p.Add<Tonemapping>();
            tm.mode.Override(TonemappingMode.Neutral);

            // Global color grade — THE main "un-fade" lift; touches every pixel of every material.
            var ca = p.Add<ColorAdjustments>();
            ca.postExposure.Override(0.12f); // a hair brighter (compensates the trimmed ambient)
            ca.contrast.Override(12f);       // deeper shadows = more pop
            ca.saturation.Override(18f);     // vivid, not faded

            var wb = p.Add<WhiteBalance>();
            wb.temperature.Override(6f);     // a touch warmer — friendlier, toy-like

            var vig = p.Add<Vignette>();     // subtle focus on the play area
            vig.intensity.Override(0.24f);
            vig.smoothness.Override(0.45f);
            vig.rounded.Override(true);

            // ---- Bloom: the priciest mobile post effect (HDR + extra blur passes). Capable devices only. ----
            if (!lowEnd)
            {
                var bloom = p.Add<Bloom>();
                bloom.threshold.Override(0.9f);
                bloom.intensity.Override(0.8f);
                bloom.scatter.Override(0.7f);
                bloom.tint.Override(Color.white);
            }
        }
    }
}
