using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;

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

        const float CellSize = 1.3f, GridBaseZ = 7.0f, ParkingZ = 4.6f;
        const float SlotSpacing = 1.3f, QueueFrontZ = 3.2f, FenceZ = 3.9f, QueueSpacing = 0.85f;
        const int VISIBLE = 10;

        GameState state = GameState.Boot;
        Camera cam;
        GameUI ui;
        Sfx sfx;
        Transform boardRoot;

        readonly Dictionary<PieceColor, Material> bodyMats = new Dictionary<PieceColor, Material>();
        Material glassMat, wheelMat, lightMat, skinMat, seatEmptyMat, mysteryMat, goldMat, arrowMat, lockMat, slotMat;
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
            PlaceCamera();

            sfx = gameObject.AddComponent<Sfx>();
            ui = gameObject.AddComponent<GameUI>();
            ui.OnMenu = () => { sfx.Click(); PauseRequested?.Invoke(); };
            ui.OnSkip = JokerSkip;
            ui.OnSwap = JokerSwap;
            ui.OnAddTime = JokerTime;
            ui.Build(SkipCost, SwapCost, TimeCost);

            if (autoStart) LoadLevel(SaveSystem.Level);
            else { state = GameState.Menu; ui.HideHud(); }
        }

        // ---- Public control ------------------------------------------------
        public void LoadLevel(int levelNumber) { CancelInvoke(); StartLevel(levelNumber); }
        public void NextLevel() { LoadLevel(SaveSystem.Level); }
        public void RetryLevel() { LoadLevel(currentLevel); }
        public void ToggleSound() { SaveSystem.Sound = !SaveSystem.Sound; sfx.Click(); }

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

            var slot = NearestFreeSlot(GridWorld(bus.cell).x);
            if (slot == null) { sfx.Error(); StartCoroutine(Bump(bus.transform)); return; }

            occ.Remove(bus.cell);
            gridBuses.Remove(bus);
            slot.occupant = bus; bus.slotIndex = slot.index; bus.state = BusState.MovingToSlot;
            sfx.Deploy();
            StartCoroutine(ExitRoutine(bus, slot));
        }

        IEnumerator ExitRoutine(Bus bus, ParkingSlot slot)
        {
            busy++;
            int dist = ExitDistance(bus.cell, bus.dir);
            Vector3 exitPt = GridWorld(bus.cell) + new Vector3(bus.dir.x, 0, bus.dir.y) * (dist * CellSize);
            yield return MoveTo(bus.transform, exitPt, 0.16f);
            yield return MoveAndRotateArc(bus.transform, ParkingWorld(slot.index), Quaternion.identity, 0.32f, 0.9f);
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
                bus.transform.position = start + new Vector3(0, 1.6f, -1.5f) * k;
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

            if (FindParkedBus(visible[0].color) != null) return;
            bool freeSlot = FirstFreeSlot() != null;
            bool busesLeft = gridBuses.Count > 0;
            if (freeSlot && busesLeft) return;
            if (HasLockedSlot() && SaveSystem.Coins >= SlotUnlockCost) return;
            if (visible.Count > 0 && SaveSystem.Coins >= SkipCost) return;
            if (visible.Count >= 2 && SaveSystem.Coins >= SwapCost) return;
            Lose("Stuck! No moves left.");
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
            int bonus = 25 + currentLevel * 5 + stars * 10;
            AddCoins(bonus);
            SaveSystem.Level = Mathf.Max(SaveSystem.Level, currentLevel + 1);
            SaveSystem.BestLevel = currentLevel;
            sfx.Win();
            Juice.Confetti(this, boardRoot, new Vector3(0, 6, QueueFrontZ), confettiMats, 50);
            ui.HideHud();
            LevelCompleted?.Invoke(earnedThisLevel, stars);
            if (autoAdvance && LevelCompleted == null) Invoke(nameof(NextLevel), 2.2f);
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
                var bus = CreateBus(gb.color, gb.capacity, DirYaw(gb.dir));
                bus.cell = gb.cell; bus.dir = gb.dir; bus.state = BusState.Queued;
                bus.transform.position = GridWorld(gb.cell);
                occ[gb.cell] = bus;
                gridBuses.Add(bus);
            }
        }

        // Visible board + faint cell-grid lines so the jam layout reads clearly.
        void BuildBoardBackground()
        {
            Shader sh = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
            Material boardMat = Mat(sh, new Color(0.20f, 0.22f, 0.28f), 0.08f);
            Material lineMat = Mat(sh, new Color(0.36f, 0.40f, 0.50f), 0.1f, 0.12f);

            float w = gridW * CellSize;
            float d = gridH * CellSize;
            float cz = GridBaseZ + (gridH - 1) * CellSize * 0.5f;

            LowPolyBuilder.Slab(boardRoot, new Vector3(0, -0.07f, cz), new Vector3(w + 0.3f, 0.12f, d + 0.3f), boardMat);

            for (int x = 0; x <= gridW; x++)
            {
                var ln = MakeCube(boardRoot, lineMat, new Vector3(0.04f, 0.03f, d));
                ln.transform.position = new Vector3((x - gridW / 2f) * CellSize, 0f, cz);
            }
            for (int y = 0; y <= gridH; y++)
            {
                var ln = MakeCube(boardRoot, lineMat, new Vector3(w, 0.03f, 0.04f));
                ln.transform.position = new Vector3(0, 0f, GridBaseZ - CellSize * 0.5f + y * CellSize);
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
            u.body = LowPolyBuilder.BuildPerson(go.transform, bodyMats[g.color], skinMat,
                g.golden, g.mystery, mysteryMat, goldMat, out GameObject cover);
            u.mysteryCover = cover;
            return u;
        }

        Bus CreateBus(PieceColor color, int capacity, float yaw)
        {
            var root = new GameObject("Bus_" + color);
            root.transform.SetParent(boardRoot, false);
            root.transform.rotation = Quaternion.Euler(0, yaw, 0);
            var bus = root.AddComponent<Bus>();
            bus.color = color; bus.capacity = capacity; bus.filledMat = bodyMats[color];
            bus.seatWindows = LowPolyBuilder.BuildBus(root.transform, capacity, CellSize,
                bodyMats[color], glassMat, wheelMat, lightMat, seatEmptyMat, arrowMat);
            return bus;
        }

        // ====================================================================
        // Positions
        // ====================================================================
        float SlotX(int i) => (i - (totalSlots - 1) / 2f) * SlotSpacing;
        Vector3 ParkingWorld(int i) => new Vector3(SlotX(i), 0, ParkingZ);
        Vector3 GridWorld(Vector2Int c) => new Vector3((c.x - (gridW - 1) / 2f) * CellSize, 0, GridBaseZ + c.y * CellSize);
        Vector3 LinePos(int index) => new Vector3(0, 0, QueueFrontZ - index * QueueSpacing);

        Vector3 BusDoorWorld(Bus bus)
        {
            float len = LowPolyBuilder.BusLength(CellSize);
            return bus.transform.position + new Vector3(0, 0.25f, -len * 0.4f);
        }

        float DirYaw(Vector2Int d) { if (d.x == -1) return 90f; if (d.x == 1) return -90f; return 0f; }

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
            Shader sh = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
            var list = new List<Material>();
            foreach (PieceColor c in System.Enum.GetValues(typeof(PieceColor)))
            {
                bodyMats[c] = Mat(sh, Palette.ToColor(c), 0.2f, 0.18f);
                list.Add(bodyMats[c]);
            }
            confettiMats = list.ToArray();

            glassMat     = Mat(sh, new Color(0.18f, 0.26f, 0.40f), 0.85f);
            wheelMat     = Mat(sh, new Color(0.12f, 0.12f, 0.14f), 0.2f);
            lightMat     = Mat(sh, new Color(1f, 0.96f, 0.72f), 0.6f, 0.5f);
            skinMat      = Mat(sh, Palette.Skin, 0.1f);
            seatEmptyMat = Mat(sh, Palette.SeatEmpty, 0.2f);
            mysteryMat   = Mat(sh, Palette.Mystery, 0.2f);
            goldMat      = Mat(sh, Palette.Gold, 0.7f, 0.4f);
            arrowMat     = Mat(sh, new Color(0.99f, 0.99f, 0.99f), 0.3f, 0.25f);
            lockMat      = Mat(sh, new Color(0.42f, 0.9f, 0.48f), 0.2f, 0.25f);
        }

        static Material Mat(Shader sh, Color col, float smooth, float emission = 0f)
        {
            var m = new Material(sh);
            if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", col);
            if (m.HasProperty("_Color")) m.SetColor("_Color", col);
            if (m.HasProperty("_Smoothness")) m.SetFloat("_Smoothness", smooth);
            if (m.HasProperty("_Glossiness")) m.SetFloat("_Glossiness", smooth);
            if (m.HasProperty("_Metallic")) m.SetFloat("_Metallic", 0f);
            if (emission > 0f && m.HasProperty("_EmissionColor"))
            {
                m.EnableKeyword("_EMISSION");
                m.SetColor("_EmissionColor", col * emission);
            }
            return m;
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
            Shader sh = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");

            if (cam != null) { cam.clearFlags = CameraClearFlags.SolidColor; cam.backgroundColor = th.sky; }
            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
            RenderSettings.ambientLight = th.ambient * 1.1f;
            var sun = Object.FindAnyObjectByType<Light>();
            if (sun != null && sun.type == LightType.Directional) sun.intensity = 1.75f;

            Material ground = Mat(sh, th.ground, 0.05f);
            Material field  = Mat(sh, th.field, 0.05f);
            Material road   = Mat(sh, th.road, 0.05f);
            Material accent = Mat(sh, th.accent, 0.2f);
            Material main   = Mat(sh, th.propMain, 0.1f);
            Material alt    = Mat(sh, th.propAlt, 0.1f);
            Material foliage= Mat(sh, th.foliage, 0.05f);
            Material trunk  = Mat(sh, th.trunk, 0.1f);
            Material window = Mat(sh, new Color(th.sky.r * 0.9f + 0.1f, th.sky.g * 0.9f + 0.1f, th.sky.b, 1f), 0.6f, 0.2f);
            Material cloud  = Mat(sh, new Color(1f, 1f, 1f), 0f, 0.15f);
            slotMat = accent;

            LowPolyBuilder.Slab(boardRoot, new Vector3(0, -0.32f, 6f), new Vector3(46, 0.3f, 70), field);
            LowPolyBuilder.Slab(boardRoot, new Vector3(0, -0.12f, 6f), new Vector3(11f, 0.2f, 26), ground);
            LowPolyBuilder.Slab(boardRoot, new Vector3(0, -0.14f, -2.4f), new Vector3(11f, 0.2f, 6), road);

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
            float backZ = GridBaseZ + level.gridH * CellSize + 4f;
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
            Vector3 pos = new Vector3(0f, 18f, -11f);
            Vector3 target = new Vector3(0f, 0f, 5.5f);
            cam.transform.position = pos;
            cam.transform.rotation = Quaternion.LookRotation(target - pos, Vector3.up);
            cam.fieldOfView = 56f;
        }
    }
}
