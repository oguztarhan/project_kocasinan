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

        const int SkipCost = 60, SwapCost = 40, TimeCost = 50, SlotUnlockCost = 80, AddTimeAmount = 15;
        const float ContinueTimeBonus = 20f;

        // World Z grows AWAY from the camera (up the portrait screen). Bottom→top:
        // big bus grid (low Z) -> parking row -> thin people band (high Z).
        const float CellSize = 1.2f;          // multi-cell vehicles (Car1/Bus2/Limo3) -> smaller cells keep the board on-screen
        const float GridExitZ = 4.0f;         // grid row y=0 (exit edge, nearest parking); deeper rows go DOWN (toward camera)
        const float ParkingZ = 6.2f;          // parking row, just above the grid
        const float SlotSpacing = 1.7f;
        const float PeopleZ = 9.0f;           // thin people band across the top
        const float PeopleStartX = -3.8f, PeopleSpacing = 0.85f;
        const float FenceZ = 7.9f;            // divider between people (top) and bus area
        const int VISIBLE = 10;

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
        Material glassMat, wheelMat, lightMat, skinMat, seatEmptyMat, mysteryMat, goldMat, arrowMat, lockMat, slotMat, boardMat, lineMat;
        Material[] confettiMats;

        LevelData level;
        int currentLevel = 1;
        int totalSlots, gridW, gridH;
        float timeLeft;
        int earnedThisLevel, combo;
        float lastBoardTime = -10f;
        int busy;
        bool pumpRunning, pumpDirty;

        ParkingSlot[] slots;
        readonly Dictionary<Vector2Int, Bus> occ = new Dictionary<Vector2Int, Bus>();
        readonly List<Bus> gridBuses = new List<Bus>();

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
            ui.OnSkip = JokerSkip;
            ui.OnSwap = JokerSwap;
            ui.OnAddTime = JokerTime;
            ui.OnHome = GoToMainMenu;            // settings -> HOME
            ui.OnReplay = RetryLevel;            // settings -> REPLAY
            ui.OnClaimReward = ClaimWinReward;   // success panel -> claim / ad
            ui.Build(SkipCost, SwapCost, TimeCost);

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
            foreach (var s in slots)
                if (s != null && s.locked) { s.Unlock(); break; }
            timeLeft = Mathf.Max(timeLeft, 0f) + ContinueTimeBonus;
            state = GameState.Playing;
            ui.ShowHud();
            ui.SetTimer(timeLeft);
            sfx.Click();
            TryStartBoardingPump();
        }

        // ====================================================================
        void Update()
        {
            if (state != GameState.Playing) return;

            timeLeft -= Time.deltaTime;
            ui.SetTimer(timeLeft);
            if (timeLeft <= 0f) { Lose("Time's up!"); return; }

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
        void TryTapBus(Bus bus)
        {
            if (bus.state != BusState.Queued) return; // already leaving / parked

            var p = bus.cell + bus.dir;
            while (InGrid(p))
            {
                if (occ.ContainsKey(p)) { sfx.Error(); StartCoroutine(Bump(bus.transform)); return; } // blocked
                p += bus.dir;
            }

            var slot = NearestFreeSlot(GridWorldCenter(bus.cell, bus.dir, bus.length).x);
            if (slot == null) { sfx.Error(); StartCoroutine(Bump(bus.transform)); return; }

            for (int i = 0; i < bus.length; i++) occ.Remove(bus.cell - bus.dir * i);
            gridBuses.Remove(bus);
            slot.occupant = bus; bus.slotIndex = slot.index; bus.state = BusState.MovingToSlot;
            sfx.Deploy();
            StartCoroutine(ExitRoutine(bus, slot));
        }

        IEnumerator ExitRoutine(Bus bus, ParkingSlot slot)
        {
            busy++;
            int dist = ExitDistance(bus.cell, bus.dir) + bus.length; // +length so the tail fully clears
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

                foreach (var slot in slots)
                {
                    var b = slot.occupant;
                    if (b != null && b.state == BusState.Parked && b.IsFull)
                    {
                        b.state = BusState.Leaving;
                        StartCoroutine(DispatchRoutine(b, slot));
                        progressed = true;
                    }
                }

                if (visible.Count > 0)
                {
                    var u = visible[0];
                    Bus bus = FindParkedBus(u.color);
                    if (bus != null)
                    {
                        visible.RemoveAt(0);
                        yield return MoveTo(u.transform, BusDoorWorld(bus), 0.22f, ease: true);
                        bus.FillNextSeat();
                        StartCoroutine(Juice.PunchScale(bus.transform, 0.12f));
                        OnBoarded(u.golden, u.transform.position);
                        Destroy(u.gameObject);
                        StreamNext();
                        yield return RepositionLine();
                        progressed = true;
                    }
                }
            }
            busy--;
            pumpRunning = false;
            CheckEnd();
        }

        void StreamNext()
        {
            if (nextGroupIndex < groups.Count)
            {
                var u = CreateUnit(groups[nextGroupIndex++]);
                u.transform.position = LinePos(visible.Count + 3); // walk in from off-screen
                visible.Add(u);
            }
        }

        void OnBoarded(bool golden, Vector3 pos)
        {
            combo = (Time.time - lastBoardTime < 1.6f) ? combo + 1 : 1;
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
            slot.occupant = null;
            Material poof = bodyMats[bus.color];
            Vector3 start = bus.transform.position;
            Vector3 startScale = bus.transform.localScale;
            sfx.Deploy();
            float e = 0f, dur = 0.4f;
            while (e < dur && bus != null)
            {
                e += Time.deltaTime;
                float k = e / dur;
                bus.transform.position = start + new Vector3(0, 1.6f, 1.5f) * k; // drive away (toward the top) + lift
                bus.transform.localScale = startScale * Mathf.Lerp(1f, 0.1f, k);
                yield return null;
            }
            Juice.Burst(this, boardRoot, start + Vector3.up * 0.4f, poof, 16, 4.5f);
            if (bus != null) Destroy(bus.gameObject);
            busy--;
            CheckEnd();
        }

        IEnumerator RepositionLine()
        {
            for (int i = 0; i < visible.Count; i++)
                if (visible[i] != null) StartCoroutine(MoveTo(visible[i].transform, LinePos(i), 0.18f, ease: true));
            yield return new WaitForSeconds(0.1f);
        }

        void CheckEnd()
        {
            if (state != GameState.Playing) return;
            if (visible.Count == 0 && nextGroupIndex >= groups.Count) { Win(); return; }
            if (busy > 0) return;
            if (visible.Count == 0) return;

            // The front passenger can board one of the parked buses -> keep playing.
            if (FindParkedBus(visible[0].color) != null) return;

            // There is still an OPEN (unlocked & empty) parking slot, so the player can
            // place another bus that might match the front passenger -> not stuck yet.
            if (FirstFreeSlot() != null) return;

            // Front passenger matches NO parked bus AND the parking is full -> deadlock.
            // Only "can the front passenger board right now?" matters here: locked slots,
            // the number of remaining grid buses, and joker coins are intentionally NOT
            // treated as an escape. Lose immediately so the Continue panel appears.
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

        void JokerSkip()
        {
            if (state != GameState.Playing || visible.Count == 0) { sfx.Error(); return; }
            if (!Spend(SkipCost)) { sfx.Error(); return; }
            sfx.Click();
            var u = visible[0]; visible.RemoveAt(0);
            if (u != null) StartCoroutine(LeaveAndDestroy(u.transform));
            StreamNext();
            StartCoroutine(AfterJoker());
        }

        void JokerSwap()
        {
            if (state != GameState.Playing || visible.Count < 2) { sfx.Error(); return; }
            if (!Spend(SwapCost)) { sfx.Error(); return; }
            sfx.Click();
            (visible[0], visible[1]) = (visible[1], visible[0]);
            StartCoroutine(AfterJoker());
        }

        void JokerTime()
        {
            if (state != GameState.Playing) { sfx.Error(); return; }
            if (!Spend(TimeCost)) { sfx.Error(); return; }
            sfx.Click();
            timeLeft += AddTimeAmount; ui.SetTimer(timeLeft);
        }

        IEnumerator AfterJoker()
        {
            busy++;
            yield return RepositionLine();
            busy--;
            TryStartBoardingPump();
            CheckEnd();
        }

        IEnumerator LeaveAndDestroy(Transform t)
        {
            if (t == null) yield break;
            yield return MoveTo(t, t.position + new Vector3(0, 0, -4f), 0.3f);
            if (t != null) Destroy(t.gameObject);
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
            BuildBoardBackground();
            BuildLine();

            timeLeft = level.timeLimit;
            earnedThisLevel = 0; combo = 0; lastBoardTime = -10f;

            state = GameState.Playing;
            ui.ShowHud();
            ui.SetLevel(levelNumber);
            ui.SetTheme(theme.name);
            ui.SetCoins(SaveSystem.Coins);
            ui.SetTimer(timeLeft);
            LevelStarted?.Invoke(levelNumber);
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
            int stars = timeLeft > level.timeLimit * 0.5f ? 3 : (timeLeft > level.timeLimit * 0.2f ? 2 : 1);
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
                slot.locked = (i % 2 == 1); // unlock 0,2,4 — lock 1,3
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
                var bus = CreateBus(gb.color, gb.type, gb.capacity, DirYaw(gb.dir));
                bus.cell = gb.cell; bus.dir = gb.dir; bus.length = Vehicles.CellLength(gb.type);
                bus.state = BusState.Queued;
                bus.transform.position = GridWorldCenter(gb.cell, gb.dir, bus.length);
                for (int i = 0; i < bus.length; i++) occ[gb.cell - gb.dir * i] = bus;
                gridBuses.Add(bus);
            }
        }

        // Visible board + faint cell-grid lines so the jam layout reads clearly.
        void BuildBoardBackground()
        {
            float w = gridW * CellSize;
            float d = gridH * CellSize;
            float cz = GridExitZ - (gridH - 1) * CellSize * 0.5f;

            LowPolyBuilder.Slab(boardRoot, new Vector3(0, -0.07f, cz), new Vector3(w + 0.3f, 0.12f, d + 0.3f), boardMat);

            for (int x = 0; x <= gridW; x++)
            {
                var ln = MakeCube(boardRoot, lineMat, new Vector3(0.04f, 0.03f, d));
                ln.transform.position = new Vector3((x - gridW / 2f) * CellSize, 0f, cz);
            }
            for (int y = 0; y <= gridH; y++)
            {
                var ln = MakeCube(boardRoot, lineMat, new Vector3(w, 0.03f, 0.04f));
                ln.transform.position = new Vector3(0, 0f, GridExitZ + CellSize * 0.5f - y * CellSize);
            }
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
                u.transform.position = LinePos(i);
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

        Bus CreateBus(PieceColor color, VehicleType type, int capacity, float yaw)
        {
            var root = new GameObject(type + "_" + color);
            root.transform.SetParent(boardRoot, false);
            root.transform.rotation = Quaternion.Euler(0, yaw, 0);
            var bus = root.AddComponent<Bus>();
            bus.color = color; bus.type = type; bus.capacity = capacity;

            GameObject prefab = vehicleCatalog != null ? vehicleCatalog.PrefabFor(type) : null;
            if (prefab != null)
            {
                BuildImportedVehicle(bus, root.transform, prefab, color, capacity, type);
            }
            else
            {
                bus.filledMat = bodyMats[color];
                bus.seatWindows = LowPolyBuilder.BuildVehicle(root.transform, type, capacity, CellSize,
                    bodyMats[color], glassMat, wheelMat, lightMat, seatEmptyMat, arrowMat);
            }
            return bus;
        }

        // Instantiate an imported vehicle KEEPING its real look (URP shadergraph atlas), auto-face
        // it forward, size it per type, and lay the boarding color on the ROOF (a big colored slab)
        // with a white arrow + seat pips on top — so the model still reads as a real vehicle while
        // the match color + direction + fill stay clear from the top-down camera.
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
            // NOTE: materials are intentionally left as-is (keep the real vehicle look).

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

            // Floating COLOR + NUMBER tag, camera-facing, ABOVE the vehicle: a colored panel
            // (match color) with the empty-seat number on it. Always readable, never mirrored,
            // and clearly above the body (can't sink into the mesh).
            Color tagColor = bodyMats[color].HasProperty("_BaseColor")
                ? bodyMats[color].GetColor("_BaseColor") : bodyMats[color].color;
            float tagSize = Mathf.Clamp(wid * 0.95f, 0.42f, 0.95f);
            float tagY = topY + tagSize * 0.55f + 0.08f;
            bus.seatLabel = BuildSeatTag(root, tagColor, capacity, new Vector3(0, tagY, 0), tagSize);
            bus.RefreshSeatLabel();
        }

        // Camera-facing world-space tag floating above a vehicle: colored panel + empty-seat number.
        UnityEngine.UI.Text BuildSeatTag(Transform root, Color tagColor, int capacity, Vector3 localPos, float worldSize)
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

            // Colored panel = the match color.
            var bgGo = new GameObject("BG", typeof(RectTransform));
            bgGo.transform.SetParent(go.transform, false);
            var bg = bgGo.AddComponent<UnityEngine.UI.Image>();
            bg.color = tagColor;
            Stretch(bg.rectTransform);

            // Empty-seat number, colored to contrast the panel.
            float lum = tagColor.r * 0.299f + tagColor.g * 0.587f + tagColor.b * 0.114f;
            bool dark = lum < 0.55f;
            var txtGo = new GameObject("Text", typeof(RectTransform));
            txtGo.transform.SetParent(go.transform, false);
            var txt = txtGo.AddComponent<UnityEngine.UI.Text>();
            txt.font = seatFont;
            txt.text = capacity.ToString();
            txt.fontSize = 64;
            txt.alignment = TextAnchor.MiddleCenter;
            txt.color = dark ? Color.white : Color.black;
            Stretch(txt.rectTransform);
            var outline = txtGo.AddComponent<UnityEngine.UI.Outline>();
            outline.effectColor = dark ? Color.black : Color.white;
            outline.effectDistance = new Vector2(2, 2);
            return txt;
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
        float DirYaw(Vector2Int d) { if (d.x == -1) return 90f; if (d.x == 1) return -90f; return 180f; }

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
            boardMat     = lib["Board"];
            lineMat      = lib["GridLine"];
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
            RenderSettings.ambientLight = th.ambient * 1.1f;
            var sun = Object.FindAnyObjectByType<Light>();
            if (sun != null && sun.type == LightType.Directional) sun.intensity = 1.75f;

            // Editable per-theme env material assets (Resources/Materials/<Theme>_<Type>), else runtime fallback.
            Material ground = MaterialLibrary.GetTheme(th.name, "Ground", th.ground, 0.05f);
            Material field  = MaterialLibrary.GetTheme(th.name, "Field", th.field, 0.05f);
            Material road   = MaterialLibrary.GetTheme(th.name, "Road", th.road, 0.05f);
            Material accent = MaterialLibrary.GetTheme(th.name, "Accent", th.accent, 0.2f);
            Material main   = MaterialLibrary.GetTheme(th.name, "PropMain", th.propMain, 0.1f);
            Material alt    = MaterialLibrary.GetTheme(th.name, "PropAlt", th.propAlt, 0.1f);
            Material foliage= MaterialLibrary.GetTheme(th.name, "Foliage", th.foliage, 0.05f);
            Material trunk  = MaterialLibrary.GetTheme(th.name, "Trunk", th.trunk, 0.1f);
            Material window = MaterialLibrary.GetTheme(th.name, "Window", new Color(th.sky.r * 0.9f + 0.1f, th.sky.g * 0.9f + 0.1f, th.sky.b, 1f), 0.6f, 0.2f);
            Material cloud  = MaterialLibrary.GetTheme(th.name, "Cloud", new Color(1f, 1f, 1f), 0f, 0.15f);
            // slotMat is now a stable, editable asset set in BuildMaterials (no theme override).

            LowPolyBuilder.Slab(boardRoot, new Vector3(0, -0.32f, 3f), new Vector3(46, 0.3f, 70), field);
            LowPolyBuilder.Slab(boardRoot, new Vector3(0, -0.12f, 3f), new Vector3(12f, 0.2f, 30), ground);
            LowPolyBuilder.Slab(boardRoot, new Vector3(0, -0.14f, -6f), new Vector3(12f, 0.2f, 8), road);

            for (int i = -4; i <= 4; i++)
            {
                var post = MakeCube(boardRoot, accent, new Vector3(0.1f, 0.5f, 0.1f));
                post.transform.position = new Vector3(i * 1.1f, 0.25f, FenceZ);
                var bar = MakeCube(boardRoot, accent, new Vector3(1.1f, 0.06f, 0.05f));
                bar.transform.position = new Vector3(i * 1.1f + 0.55f, 0.34f, FenceZ);
            }

            for (int i = 0; i < 6; i++)
            {
                float z = -1f + i * 2.6f;
                LowPolyBuilder.BuildProp(boardRoot, th.prop, new Vector3(-6.8f, 0, z), main, alt, foliage, trunk, window, 1f);
                LowPolyBuilder.BuildProp(boardRoot, th.prop, new Vector3(6.8f, 0, z), main, alt, foliage, trunk, window, 1f);
            }
            float backZ = PeopleZ + 4f;
            for (int i = 0; i < 5; i++)
                LowPolyBuilder.BuildProp(boardRoot, th.prop, new Vector3(-5f + i * 2.5f, 0, backZ), main, alt, foliage, trunk, window, 1.7f);

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
