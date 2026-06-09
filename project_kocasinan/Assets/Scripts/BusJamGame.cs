using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
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

        public System.Action<int> CoinsChanged;
        public System.Action<int> LevelStarted;
        public System.Action<int, int> LevelCompleted;
        public System.Action<string> LevelFailed;
        public System.Action<string> OnGameOver;
        public System.Action PauseRequested;

        public int CurrentLevel => currentLevel;
        public int Coins => SaveSystem.Coins;

        const int RecolorCost = 80, SwapCost = 40, HeliCost = 100, SlotUnlockCost = 80;
        const int J1UnlockLevel = 5, J2UnlockLevel = 10, J3UnlockLevel = 15; // RECOLOR / SWAP / HELI

        // World Z grows AWAY from the camera (up the portrait screen). Bottom→top:
        // big bus grid (low Z) -> parking row -> thin people band (high Z).
        const float CellSize = 1.2f;          // multi-cell vehicles (Car1/Bus2/Limo3) -> smaller cells keep the board on-screen
        const float GridExitZ = 4.0f;         // grid row y=0 (exit edge, nearest parking); deeper rows go DOWN (toward camera)
        const float ParkingZ = 6.2f;          // parking row, just above the grid
        const float SlotSpacing = 1.55f;      // tighter so ~8 pads fit the portrait width
        const float PeopleZ = 9.0f;           // thin people band across the top
        const float PeopleStartX = -3.8f, PeopleSpacing = 0.85f;
        const float FenceZ = 7.9f;            // divider between people (top) and bus area
        const float FacadeZ = 10.9f;          // closed mall/terminal wall center (behind the people band at PeopleZ)
        const float DoorSpawnZ = 10.2f;       // people are born just in front of the facade doors, then walk into the queue
        const int VISIBLE = 10;
        // Boarding pacing (T2): the pump DISPATCHES one front passenger every BoardGap (their walks
        // overlap), so throughput is BoardGap/person — far below the old ~0.32s serial cost.
        const float BoardGap = 0.07f;         // dispatch cadence between successive boarders
        const float BoardWalkDur = 0.20f;     // each passenger's async walk to the door

        GameState state = GameState.Boot;
        Camera cam;
        GameUI ui;
        Sfx sfx;
        LevelSelect levelSelect;
        PeopleCatalog peopleCatalog;
        VehicleCatalog vehicleCatalog;
        Font seatFont;
        Transform boardRoot;

        readonly Dictionary<PieceColor, Material> bodyMats = new Dictionary<PieceColor, Material>();
        Material glassMat, wheelMat, lightMat, skinMat, seatEmptyMat, mysteryMat, goldMat, arrowMat, lockMat, slotMat;
        Material[] confettiMats;

        LevelData level;
        int currentLevel = 1;
        int totalSlots, gridW, gridH;
        float[] doorXs;       // facade door world-X positions (set in BuildFacade); people emerge from the nearest one
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
            cam = Camera.main;
            BuildMaterials();
            peopleCatalog = Resources.Load<PeopleCatalog>("PeopleCatalog"); // null -> code-built people
            vehicleCatalog = Resources.Load<VehicleCatalog>("VehicleCatalog"); // null -> code-built vehicles
            seatFont = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf"); // roof seat-count number
            PlaceCamera();

            sfx = gameObject.AddComponent<Sfx>();
            ui = gameObject.AddComponent<GameUI>();
            ui.OnMenu = () => { sfx.Click(); PauseRequested?.Invoke(); };
            ui.OnRecolor = JokerRecolor;
            ui.OnSwap = JokerSwapPeople;
            ui.OnHeli = JokerHelicopter;
            ui.OnHome = GoToMainMenu;            // settings -> HOME
            ui.OnReplay = RetryLevel;            // settings -> REPLAY
            ui.OnClaimReward = ClaimWinReward;   // success panel -> claim / ad
            ui.Build(RecolorCost, SwapCost, HeliCost, J1UnlockLevel, J2UnlockLevel, J3UnlockLevel);

            levelSelect = gameObject.AddComponent<LevelSelect>();
            levelSelect.Build(this);

            if (autoStart) LoadLevel(SaveSystem.Level);
            else { state = GameState.Menu; ui.HideHud(); }
        }

        // ---- Public control ------------------------------------------------
        public void LoadLevel(int levelNumber) { CancelInvoke(); StartLevel(levelNumber); }
        public void NextLevel() { LoadLevel(SaveSystem.Level); }
        public void RetryLevel() { LoadLevel(currentLevel); }
        public void ToggleSound() { SaveSystem.Sound = !SaveSystem.Sound; sfx.Click(); }
        public void OpenLevelSelect() { if (levelSelect != null) levelSelect.Open(); }

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
                    if (bus != null) { if (bus.advanceN > 0) TapSpecial(bus); else TryTapBus(bus); return; }
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
        void TryTapBus(Bus bus)
        {
            if (bus.state != BusState.Queued) return; // already leaving / parked

            // Same footprint + diagonal corner-sweep the generator used to PLACE this vehicle, so a
            // solvable-by-construction lane is always tappable (4-way and 8-way alike).
            if (!LevelGenerator.SlideClear(bus.cell, bus.dir, bus.length, occ.ContainsKey, gridW, gridH))
            { sfx.Error(); StartCoroutine(Bump(bus.transform)); return; } // blocked

            var slot = NearestFreeSlot(GridWorldCenter(bus.cell, bus.dir, bus.length).x);
            if (slot == null) { sfx.Error(); StartCoroutine(Bump(bus.transform)); return; }

            foreach (var c in LevelGenerator.OccCells(bus.cell, bus.dir, bus.length)) occ.Remove(c);
            gridBuses.Remove(bus);
            slot.occupant = bus; bus.slotIndex = slot.index; bus.state = BusState.MovingToSlot;
            sfx.Deploy();
            StartCoroutine(ExitRoutine(bus, slot));
        }

        IEnumerator ExitRoutine(Bus bus, ParkingSlot slot)
        {
            busy++;
            int dist = ExitDistance(bus.cell, bus.dir) + bus.length; // +length so the tail fully clears
            // Up (0,1) exits the FAR edge (away from parking); just pop out a little, then arc to the slot
            // instead of sliding the whole way off-screen and flying back over the board.
            if (bus.dir.y == 1) dist = Mathf.Min(dist, 2);
            // grid +y maps to world -z, so negate z when converting the grid dir to a world slide.
            Vector3 exitPt = bus.transform.position + new Vector3(bus.dir.x, 0, -bus.dir.y) * (dist * CellSize);
            yield return MoveTo(bus.transform, exitPt, 0.18f);
            yield return MoveAndRotateArc(bus.transform, ParkingWorld(slot.index), Quaternion.Euler(0, 180f, 0), 0.32f, 0.9f);
            bus.state = BusState.Parked;
            StartCoroutine(Juice.PunchScale(bus.transform, 0.16f));
            busy--;
            TryStartBoardingPump();
            CheckEnd();
        }

        // Special "<<" crawler: each tap advances up to advanceN cells along its arrow, stopping at
        // the first occupied cell or the grid edge. It exits (normal tail) only when the lane reaches
        // the edge within N; otherwise it repositions and stays tappable. occ is rewritten atomically.
        void TapSpecial(Bus bus)
        {
            if (bus.state != BusState.Queued) return; // ignore a mid-crawl re-tap (state is Staging then)

            int step = 0;
            Vector2Int lead = bus.cell;
            bool reachedEdge = false;
            while (step < bus.advanceN)
            {
                Vector2Int next = lead + bus.dir;
                if (!InGrid(next)) { reachedEdge = true; break; } // lane is clear off the edge within N -> can exit
                if (occ.ContainsKey(next)) break;                  // blocked ahead
                lead = next; step++;
            }

            if (reachedEdge)
            {
                var slot = NearestFreeSlot(GridWorldCenter(bus.cell, bus.dir, bus.length).x);
                if (slot != null)
                {
                    // Full exit — identical to the normal tail (free ALL body cells, then ExitRoutine).
                    foreach (var c in LevelGenerator.OccCells(bus.cell, bus.dir, bus.length)) occ.Remove(c);
                    gridBuses.Remove(bus);
                    slot.occupant = bus; bus.slotIndex = slot.index; bus.state = BusState.MovingToSlot;
                    sfx.Deploy();
                    StartCoroutine(ExitRoutine(bus, slot));
                    return;
                }
                if (step == 0) { sfx.Error(); StartCoroutine(Bump(bus.transform)); return; } // at edge but no slot
                // else: no slot but we can still crawl forward 'step' cells -> fall through to reposition.
            }
            else if (step == 0) { sfx.Error(); StartCoroutine(Bump(bus.transform)); return; } // immediately blocked

            // Reposition (partial crawl): remove ALL old body cells, THEN add ALL new — synchronously, no leak.
            foreach (var c in LevelGenerator.OccCells(bus.cell, bus.dir, bus.length)) occ.Remove(c);
            bus.cell = lead;
            foreach (var c in LevelGenerator.OccCells(bus.cell, bus.dir, bus.length)) occ[c] = bus;
            bus.state = BusState.Staging; // not tappable until the crawl animation finishes
            sfx.Deploy();
            StartCoroutine(CrawlMove(bus));
        }

        IEnumerator CrawlMove(Bus bus)
        {
            busy++;
            yield return MoveTo(bus.transform, GridWorldCenter(bus.cell, bus.dir, bus.length), 0.16f);
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
                        yield return new WaitForSeconds(BoardGap); // cadence, NOT the full walk
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
            if (u != null) yield return MoveTo(u.transform, BusDoorWorld(bus), BoardWalkDur, ease: true);
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
                u.transform.position = DoorSpawn(LinePos(visible.Count).x); // emerge from the nearest facade door
                visible.Add(u);
            }
            UpdatePeopleLeft(); // a person was just served (or skipped) -> refresh the counter
        }

        // People still to serve = unspawned (groups - cursor) + on-screen window. Reads the LOGICAL
        // pool, NOT visible.Count alone; equals 0 exactly when visible==0 && cursor>=groups.Count (Win).
        int PeopleLeft() => Mathf.Max(0, (groups != null ? groups.Count - nextGroupIndex : 0) + visible.Count);
        void UpdatePeopleLeft() { if (ui != null) ui.SetPeopleLeft(PeopleLeft()); }

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
            busy++;
            slot.occupant = null; // free the slot immediately so BoardingPump can refill it
            Vector3 start = bus.transform.position;
            sfx.Deploy();
            Juice.Burst(this, boardRoot, start + Vector3.up * 0.4f, bodyMats[bus.color], 16, 4.5f); // celebrate as it pulls away

            // Drive the FULL-SIZE bus FLAT along the road (stays on the ground, no flying/shrink) to the
            // nearer screen edge and OFF-SCREEN.
            float side = start.x >= 0f ? 1f : -1f;                                 // exit the closer side
            Vector3 target = new Vector3(side * 20f, start.y, start.z);            // 20 = well past the camera frustum at ParkingZ
            Quaternion faceOut = Quaternion.Euler(0, side > 0f ? -90f : 90f, 0);   // turn its nose toward the exit side
            yield return MoveAndRotateArc(bus.transform, target, faceOut, 0.5f, 0f); // arc 0 = grounded drive, no lift

            if (bus != null) Destroy(bus.gameObject); // destroyed only after it has driven off-frame
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

            // There is still an OPEN (unlocked & empty) parking slot, so the player can
            // extract a matching grid bus (one always exists by construction while that color
            // still has people) -> not stuck.
            if (FirstFreeSlot() != null) return;

            // A locked slot the player can still AFFORD to unlock -> they can open it and bring a
            // matching bus -> not stuck. (Loosened so we don't declare a FALSE deadlock while a
            // winning move exists — levels are solvable-by-construction.)
            if (HasLockedSlot() && SaveSystem.Coins >= SlotUnlockCost) return;

            // Genuinely wedged: front passenger matches no parked bus, every unlocked slot is full of
            // non-matching non-full buses, and no affordable locked slot remains. By construction this
            // is only reachable by player mistakes (parking the wrong buses); the design is otherwise
            // effectively no-lose (jokers in T8 are the deliberate get-unstuck path). Show Continue.
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
            if (!Spend(RecolorCost)) { sfx.Error(); return; }
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
            if (!Spend(SwapCost)) { sfx.Error(); return; }
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
            if (!Spend(HeliCost)) { sfx.Error(); return; }
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

        // Re-tint a jam bus to a new match-color (body + roof number) for RECOLOR.
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
            else if (bus.seatWindows != null) // code-built fallback
            {
                var bodyTf = bus.transform.Find("Body");
                if (bodyTf != null) { var br = bodyTf.GetComponent<Renderer>(); if (br != null) br.sharedMaterial = bodyMats[newColor]; }
                bus.filledMat = bodyMats[newColor];
                for (int i = 0; i < bus.seatsFilled && i < bus.seatWindows.Length; i++)
                    if (bus.seatWindows[i] != null) bus.seatWindows[i].sharedMaterial = bodyMats[newColor];
            }
            if (bus.seatLabel != null) bus.seatLabel.color = PeopleColor(newColor);
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
            BuildLine();

            earnedThisLevel = 0; combo = 0; maxCombo = 0; lastBoardTime = -10f;

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
            busy = 0; pumpRunning = false; pumpDirty = false;
            occ.Clear(); gridBuses.Clear(); visible.Clear(); slots = null;
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
            Juice.Confetti(this, boardRoot, new Vector3(0, 6, PeopleZ), confettiMats, 50);
            ui.HideHud();
            LevelCompleted?.Invoke(earnedThisLevel, stars);
            ui.ShowSuccess(); // 800x1000 "GOOD" panel with CLAIM / WATCH AD x2
        }

        void Lose(string reason)
        {
            if (state != GameState.Playing) return;
            state = GameState.Lose;
            sfx.Lose();
            ui.HideHud();
            LevelFailed?.Invoke(reason);
            OnGameOver?.Invoke(reason);
            bool handledExternally = OnGameOver != null || LevelFailed != null;
            if (autoAdvance && !handledExternally) Invoke(nameof(RetryLevel), 1.8f);
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
                pad.transform.localScale = new Vector3(SlotSpacing * 0.84f, 0.1f, 2.2f);
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

        // Wide closed mall/terminal facade behind the people band. Themed (Facade/Trim/Door materials),
        // deterministic, parented to boardRoot (torn down each level). Records door world-X's in doorXs so
        // people emerge from the nearest door. Sign + door-glass reuse the accent/window mats.
        void BuildFacade(Theme th, Material sign, Material window)
        {
            Material body = MaterialLibrary.GetTheme(th.name, "Facade", th.propMain, 0.40f, 0.05f);
            Material trim = MaterialLibrary.GetTheme(th.name, "FacadeTrim", th.propAlt, 0.45f, 0.06f);
            Material door = MaterialLibrary.GetTheme(th.name, "FacadeDoor",
                new Color(th.accent.r * 0.35f, th.accent.g * 0.35f, th.accent.b * 0.35f, 1f), 0.55f, 0.12f);

            const float wallW = 10.5f, wallH = 3.0f, wallD = 1.0f; // 10.5 keeps the ends on-screen on tall 20:9 phones
            float frontZ = FacadeZ - wallD * 0.5f; // wall face toward the camera

            // wall + roof cornice (no front plinth — it would clip the door spawn/walk path)
            LowPolyBuilder.Slab(boardRoot, new Vector3(0, wallH * 0.5f, FacadeZ), new Vector3(wallW, wallH, wallD), body);
            LowPolyBuilder.Slab(boardRoot, new Vector3(0, wallH + 0.12f, FacadeZ), new Vector3(wallW + 0.7f, 0.32f, wallD + 0.5f), trim);

            // doors across the band + a glass transom above each
            const int doorCount = 4;
            const float doorSpread = 8.0f, doorW = 1.0f, doorH = 1.9f; // doors at x ~ {-3,-1,1,3}, under the band ends
            var xs = new float[doorCount];
            for (int j = 0; j < doorCount; j++)
            {
                float dx = -doorSpread * 0.5f + (doorSpread / doorCount) * (j + 0.5f);
                xs[j] = dx;
                LowPolyBuilder.Slab(boardRoot, new Vector3(dx, doorH * 0.5f, frontZ - 0.05f), new Vector3(doorW, doorH, 0.16f), door);          // dark doorway
                LowPolyBuilder.Slab(boardRoot, new Vector3(dx, doorH + 0.42f, frontZ - 0.04f), new Vector3(doorW * 1.12f, 0.55f, 0.10f), window); // glass transom
            }
            doorXs = xs;

            // sign band over the entrance
            LowPolyBuilder.Slab(boardRoot, new Vector3(0, wallH - 0.35f, frontZ - 0.06f), new Vector3(doorSpread * 0.8f, 0.55f, 0.14f), sign);
        }

        // Where a new person is born so they appear to step OUT of a facade door: the nearest door to their
        // target queue x, just in front of the wall. Falls back to the old off-screen-right spawn (no facade).
        Vector3 DoorSpawn(float targetX)
        {
            if (doorXs == null || doorXs.Length == 0) return new Vector3(targetX + 3f * PeopleSpacing, 0, PeopleZ);
            float bestX = doorXs[0], bestD = Mathf.Abs(targetX - doorXs[0]);
            for (int i = 1; i < doorXs.Length; i++)
            {
                float d = Mathf.Abs(targetX - doorXs[i]);
                if (d < bestD) { bestD = d; bestX = doorXs[i]; }
            }
            return new Vector3(bestX, 0, DoorSpawnZ);
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
                u.transform.position = DoorSpawn(LinePos(i).x); // at level start, people pour out of the doors
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
            float s = peopleCatalog.modelScale;
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
                bus.filledMat = bodyMats[color];
                bus.seatWindows = LowPolyBuilder.BuildVehicle(root.transform, type, capacity, CellSize,
                    bodyMats[color], glassMat, wheelMat, lightMat, seatEmptyMat, arrowMat);
                // Unify the people-colored roof seat-NUMBER onto the code-built path (imported already has it).
                float cbTop = CellSize * 0.55f, cbLen = LowPolyBuilder.VehicleLength(type, CellSize);
                float cbSize = Mathf.Clamp(CellSize * 0.5f, 0.42f, 0.95f);
                bus.seatLabel = BuildSeatTag(root.transform, PeopleColor(color), capacity, new Vector3(0, cbTop + cbSize * 0.55f + 0.08f, 0), cbSize);
                bus.RefreshSeatLabel();
                if (advanceN > 0)
                    BuildSpecialBadge(root.transform, advanceN, new Vector3(0, cbTop + 0.12f, -cbLen * 0.42f), Mathf.Clamp(CellSize * 0.42f, 0.3f, 0.6f));
            }
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

            // Auto-face forward: rotate the model so its LONGEST horizontal axis runs along the
            // root's local Z (the exit direction), regardless of the pack's native orientation.
            var rends = model.GetComponentsInChildren<Renderer>();
            if (rends.Length > 0)
            {
                Bounds wb = rends[0].bounds;
                for (int i = 1; i < rends.Length; i++) wb.Encapsulate(rends[i].bounds);
                Vector3 f = root.forward;
                bool rootZalongWorldZ = Mathf.Abs(f.z) >= Mathf.Abs(f.x);
                float alongRootZ = rootZalongWorldZ ? wb.size.z : wb.size.x;
                float alongRootX = rootZalongWorldZ ? wb.size.x : wb.size.z;
                if (alongRootX > alongRootZ)
                    model.transform.localRotation = Quaternion.Euler(0, vehicleCatalog.yaw + 90f, 0);
            }

            // Span the vehicle's grid footprint: CellLength cells (Car 1 / Bus 2 / Limo 3).
            float target = Vehicles.CellLength(type) * CellSize * vehicleCatalog.fitFactor;

            rends = model.GetComponentsInChildren<Renderer>();
            float span = target, wid = target * 0.5f, roofY = CellSize * 0.5f;
            if (rends.Length > 0)
            {
                Bounds b = rends[0].bounds;
                for (int i = 1; i < rends.Length; i++) b.Encapsulate(rends[i].bounds);
                float len = Mathf.Max(b.size.x, b.size.z, 0.01f);
                float widRaw = Mathf.Max(Mathf.Min(b.size.x, b.size.z), 0.01f);
                // Fit length to the L-cell span; cap width so a wide vehicle can't grossly overflow lanes.
                float scl = Mathf.Min(target / len, (CellSize * 1.1f) / widRaw);
                model.transform.localScale = Vector3.one * scl;
                model.transform.localPosition = new Vector3(0, -(b.min.y - root.position.y) * scl + vehicleCatalog.yOffset, 0);
                roofY = b.size.y * scl + vehicleCatalog.yOffset;
                span = len * scl;
                wid = Mathf.Min(b.size.x, b.size.z) * scl;
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

            // Layout: a clean chevron in the FRONT zone, then a reasonably-sized colored plate
            // + number in the area behind it. Everything floats clearly above the body.
            float markY = topY + 0.06f;
            float frontZ = -span * 0.5f;
            float arrowZoneEnd = frontZ + Mathf.Min(span * 0.34f, wid * 1.1f);

            // White chevron arrow (two angled bars meeting at a forward tip), local -Z = exit dir.
            float tipZ = frontZ + Mathf.Min(span * 0.05f, 0.08f);
            float baseZ = arrowZoneEnd;
            float ax = wid * 0.34f;
            for (int s = -1; s <= 1; s += 2)
            {
                float dx = s * ax, dz = baseZ - tipZ;
                float barLen = Mathf.Sqrt(dx * dx + dz * dz);
                float ang = Mathf.Atan2(dx, dz) * Mathf.Rad2Deg;
                var bar = MakeCube(root, arrowMat, new Vector3(wid * 0.12f, 0.05f, barLen));
                bar.transform.localPosition = new Vector3(dx * 0.5f, markY, (tipZ + baseZ) * 0.5f);
                bar.transform.localRotation = Quaternion.Euler(0, ang, 0);
            }

            // Floating empty-seat NUMBER, camera-facing, ABOVE the vehicle, in the people-color
            // (no colored panel). Always readable (contrasting outline) and never sinks into the mesh.
            float tagSize = Mathf.Clamp(wid * 0.95f, 0.42f, 0.95f);
            float tagY = topY + tagSize * 0.55f + 0.08f;
            bus.seatLabel = BuildSeatTag(root, PeopleColor(color), capacity, new Vector3(0, tagY, 0), tagSize);
            bus.RefreshSeatLabel();

            // Special "<<" crawler badge at the FRONT (distinct Y/Z from the seat-number so they never overlap).
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
                tintedVehicleMats[key] = m;
            }
            return m;
        }

        // Camera-facing world-space empty-seat number floating above a vehicle, in the people-color.
        UnityEngine.UI.Text BuildSeatTag(Transform root, Color peopleColor, int capacity, Vector3 localPos, float worldSize)
        {
            var go = new GameObject("SeatTag", typeof(RectTransform), typeof(Canvas));
            go.transform.SetParent(root, false);
            var canvas = go.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            ((RectTransform)go.transform).sizeDelta = new Vector2(100, 100);
            go.transform.localPosition = localPos;
            // Negative X cancels the horizontal flip the camera-facing rotation introduces, so the number reads correctly.
            go.transform.localScale = new Vector3(-1f, 1f, 1f) * (Mathf.Max(worldSize, 0.2f) / 100f);
            go.AddComponent<BillboardUp>(); // faces the camera

            // Empty-seat number in the people-color, with a contrasting outline so it reads on any background.
            float lum = peopleColor.r * 0.299f + peopleColor.g * 0.587f + peopleColor.b * 0.114f;
            var txtGo = new GameObject("Text", typeof(RectTransform));
            txtGo.transform.SetParent(go.transform, false);
            var txt = txtGo.AddComponent<UnityEngine.UI.Text>();
            txt.font = seatFont;
            txt.text = capacity.ToString();
            txt.fontSize = 64;
            txt.fontStyle = FontStyle.Bold;
            txt.alignment = TextAnchor.MiddleCenter;
            txt.color = peopleColor;
            Stretch(txt.rectTransform);
            var outline = txtGo.AddComponent<UnityEngine.UI.Outline>();
            outline.effectColor = lum < 0.5f ? Color.white : Color.black;
            outline.effectDistance = new Vector2(3, 3);
            return txt;
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
        // People: a single horizontal row across the top (index 0 = front, at the left).
        Vector3 LinePos(int index) => new Vector3(PeopleStartX + index * PeopleSpacing, 0, PeopleZ);

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
            RenderSettings.ambientLight = th.ambient * 1.15f;
            var sun = Object.FindAnyObjectByType<Light>();
            if (sun != null && sun.type == LightType.Directional)
            {
                sun.color = th.lightColor;          // warm/cool key per theme
                sun.intensity = th.lightIntensity;
                sun.shadows = LightShadows.Soft;    // soft shadows so vehicles read grounded (Mobile RP supports it)
            }

            // Editable per-theme env material assets (Resources/Materials/<Theme>_<Type>), else runtime fallback.
            // smoothness/emission here MATCH MaterialLibrary.ThemeTypes so the fallback looks like the asset.
            Material ground = MaterialLibrary.GetTheme(th.name, "Ground", th.ground, 0.35f, 0.05f);
            Material field  = MaterialLibrary.GetTheme(th.name, "Field", th.field, 0.28f, 0.03f);
            Material road   = MaterialLibrary.GetTheme(th.name, "Road", th.road, 0.30f);
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
            // Real ROAD band UNDER the parking row — full buses drive off-screen sideways ALONG it.
            // Spans well past both screen edges (x +/-14) and fits the grid(top ~4.6)<->fence(7.9) gap.
            LowPolyBuilder.Slab(boardRoot, new Vector3(0, -0.13f, ParkingZ), new Vector3(28f, 0.2f, 2.6f), road);

            for (int i = -4; i <= 4; i++)
            {
                var post = MakeCube(boardRoot, accent, new Vector3(0.1f, 0.5f, 0.1f));
                post.transform.position = new Vector3(i * 1.1f, 0.25f, FenceZ);
                var bar = MakeCube(boardRoot, accent, new Vector3(1.1f, 0.06f, 0.05f));
                bar.transform.position = new Vector3(i * 1.1f + 0.55f, 0.34f, FenceZ);
            }

            // Side scatter — alternate the theme's two prop kinds for variety.
            for (int i = 0; i < 6; i++)
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
            if (!th.hasFacade)
                for (int i = 0; i < 12; i++)
                {
                    float gx = -7.5f + (i * 1.45f) % 15f;
                    float gz = (PeopleZ + 1.8f) + (i % 3) * 1.1f;
                    LowPolyBuilder.GrassTuft(boardRoot, new Vector3(gx, 0, gz), 1.0f, grass);
                }

            for (int k = 0; k < 4; k++)
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
            // Fairly steep top-down so the grid reads as a flat tappable board. Tune in-editor.
            Vector3 pos = new Vector3(0f, 21f, -7f);
            Vector3 target = new Vector3(0f, 0f, 2.5f);
            cam.transform.position = pos;
            cam.transform.rotation = Quaternion.LookRotation(target - pos, Vector3.up);
            cam.fieldOfView = 58f;
        }
    }
}
