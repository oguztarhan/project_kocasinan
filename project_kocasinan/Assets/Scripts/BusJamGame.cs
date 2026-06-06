using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;

namespace BusJam
{
    /// <summary>
    /// Top-level manager / bootstrapper (the only component in the scene).
    /// Portrait mobile bus-jam: tap an unblocked bus (front of its column) to send
    /// it to a parking slot; the single line boards in order; full buses leave.
    /// Builds everything procedurally on Start.
    /// </summary>
    public class BusJamGame : MonoBehaviour
    {
        enum GameState { Boot, Playing, Win, Lose }

        // Raised when a level is cleared / failed so the project's own Level Complete
        // and Game Over canvases can react. Scoring + progression are applied before
        // these fire, so subscribers only need to present the result.
        public System.Action<int, int> OnLevelComplete; // (coinsEarnedThisLevel, stars 1-3)
        public System.Action<string>   OnGameOver;      // (reason)
        public System.Action           OnExitToMenu;    // HUD pause button pressed

        const int SkipCost = 60, SwapCost = 40, TimeCost = 50, SlotUnlockCost = 80, AddTimeAmount = 15;

        // When the player chooses to continue after a loss, refill the clock to at least
        // this many seconds so a time-out failure can actually keep playing.
        const float ContinueTimeBonus = 30f;

        // Portrait layout
        const float SlotSpacing = 1.35f, ColumnSpacing = 1.35f;
        const float ParkingZ = 5.0f, ColumnFrontZ = 7.6f, ColumnDepthSpacing = 2.3f;
        const float QueueHeadZ = 2.7f, FenceZ = 3.9f;
        const int PerRow = 6;

        GameState state = GameState.Boot;
        Camera cam;
        GameUI ui;
        Sfx sfx;
        Transform boardRoot;

        readonly Dictionary<PieceColor, Material> bodyMats = new Dictionary<PieceColor, Material>();
        Material glassMat, wheelMat, lightMat, skinMat, seatEmptyMat, mysteryMat, goldMat, arrowMat, lockMat;
        Material slotMat;
        Material[] confettiMats;

        LevelData level;
        int currentLevel = 1;
        int totalSlots, columnCount;
        float timeLeft;
        int earnedThisLevel, combo;
        float lastBoardTime = -10f;
        int busy;
        bool pumpRunning, pumpDirty;

        ParkingSlot[] slots;
        readonly List<List<Bus>> columns = new List<List<Bus>>();
        readonly List<Passenger> line = new List<Passenger>();

        // ====================================================================
        void Start()
        {
            Screen.orientation = ScreenOrientation.Portrait;
            cam = Camera.main;
            BuildMaterials();
            PlaceCamera();

            sfx = gameObject.AddComponent<Sfx>();
            ui = gameObject.AddComponent<GameUI>();
            WireUI();
            ui.Build(SkipCost, SwapCost, TimeCost);

            // The Main Menu is now a separate scene/canvas, so the gameplay scene
            // starts the saved level immediately on load.
            StartLevel(SaveSystem.Level);
        }

        void WireUI()
        {
            // Only in-game HUD controls are wired here. The pause button is forwarded
            // to OnExitToMenu so the project's own navigation decides where to go.
            ui.OnMenu    = () => { sfx.Click(); OnExitToMenu?.Invoke(); };
            ui.OnSkip    = JokerSkip;
            ui.OnSwap    = JokerSwap;
            ui.OnAddTime = JokerTime;
        }

        void Update()
        {
            if (state != GameState.Playing) return;

            timeLeft -= Time.deltaTime;
            ui.SetTimer(timeLeft);
            if (timeLeft <= 0f) { sfx.Lose(); Lose("Time's up!"); return; }

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
            int n = Mathf.Min(line.Count, 5);
            for (int i = 0; i < n; i++)
                if (line[i] != null && line[i].mystery && !line[i].revealed) line[i].Reveal(bodyMats[line[i].color]);
        }

        // ====================================================================
        // Tap a bus -> move it out if it is the unblocked front of its column
        // ====================================================================
        void TryTapBus(Bus bus)
        {
            for (int j = 0; j < columns.Count; j++)
            {
                int d = columns[j].IndexOf(bus);
                if (d < 0) continue;

                if (d != 0) { sfx.Error(); StartCoroutine(Bump(bus.transform)); return; }      // blocked
                var slot = FirstFreeSlot();
                if (slot == null) { sfx.Error(); StartCoroutine(Bump(bus.transform)); return; } // no room

                columns[j].RemoveAt(0);
                slot.occupant = bus; bus.slotIndex = slot.index; bus.state = BusState.MovingToSlot;
                sfx.Deploy();
                StartCoroutine(DeployRoutine(bus, slot));
                RepositionColumn(j);
                return;
            }
            // not a column bus (already parked/leaving) -> ignore
        }

        IEnumerator DeployRoutine(Bus bus, ParkingSlot slot)
        {
            busy++;
            yield return MoveTo(bus.transform, ParkingWorld(slot.index), 0.3f);
            bus.state = BusState.Parked;
            StartCoroutine(Juice.PunchScale(bus.transform, 0.18f));
            busy--;
            TryStartBoardingPump();
            CheckEnd();
        }

        void RepositionColumn(int j)
        {
            for (int d = 0; d < columns[j].Count; d++)
                StartCoroutine(MoveTo(columns[j][d].transform, ColumnPos(j, d), 0.2f));
        }

        // ====================================================================
        // Boarding (single ordered line)
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

                if (line.Count > 0)
                {
                    var front = line[0];
                    Bus bus = FindParkedBus(front.color);
                    if (bus != null)
                    {
                        line.RemoveAt(0);
                        yield return MoveTo(front.transform, BusDoorWorld(bus), 0.22f);
                        bus.FillNextSeat();
                        StartCoroutine(Juice.PunchScale(bus.transform, 0.14f));
                        OnBoarded(front);
                        if (front != null) Destroy(front.gameObject);
                        yield return RepositionLine();
                        progressed = true;
                    }
                }
            }
            busy--;
            pumpRunning = false;
            CheckEnd();
        }

        void OnBoarded(Passenger p)
        {
            combo = (Time.time - lastBoardTime < 1.6f) ? combo + 1 : 1;
            lastBoardTime = Time.time;

            int coins = Mathf.Clamp(combo, 1, 5);
            if (p.golden)
            {
                coins += 15;
                sfx.Coin();
                Juice.Burst(this, boardRoot, p.transform.position + Vector3.up * 0.5f, goldMat, 12, 4f);
            }
            else sfx.Board();

            SaveSystem.AddCoins(coins);
            earnedThisLevel += coins;
            ui.SetCoins(SaveSystem.Coins);
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
            Vector3 away = bus.transform.position + new Vector3(0, 0, -16f);
            yield return MoveTo(bus.transform, away, 0.4f);
            bus.state = BusState.Done;
            if (bus != null) Destroy(bus.gameObject);
            busy--;
            CheckEnd();
        }

        IEnumerator RepositionLine()
        {
            for (int i = 0; i < line.Count; i++)
                if (line[i] != null) StartCoroutine(MoveTo(line[i].transform, LinePos(i), 0.18f));
            yield return new WaitForSeconds(0.1f);
        }

        void CheckEnd()
        {
            // Only evaluate the board while a level is actively being played.
            if (state != GameState.Playing) return;

            // Every passenger has boarded -> level cleared.
            if (line.Count == 0) { Win(); return; }

            // Wait for any in-flight bus/passenger animations to settle before judging
            // whether the board is genuinely stuck (avoids false positives mid-move).
            if (busy > 0) return;

            // A move still exists if the next passenger can board a parked bus.
            if (FindParkedBus(line[0].color) != null) return;

            bool parkingFull = FirstFreeSlot() == null; // no free (unlocked & empty) bay
            bool busesLeft   = AnyColumnHasBus();        // buses still queued in columns

            // A move also exists if there is an open bay to deploy another column bus.
            if (!parkingFull && busesLeft) return;

            // DEADLOCK: the parking bays are full (or no buses remain to deploy) AND the
            // next passenger cannot board any parked bus. The grid is locked, so trigger
            // the lose condition immediately. Setting state to Lose (inside Lose) halts
            // Update()'s timer and tap handling, so no further grid moves are processed
            // while the ContinuePanel UI flow takes over.
            sfx.Lose();
            Lose("Stuck! Parking full and no one can board.");
        }

        // ====================================================================
        // Player actions
        // ====================================================================
        void TryUnlockSlot(ParkingSlot slot)
        {
            if (!slot.locked) return;
            if (!SaveSystem.TrySpend(SlotUnlockCost)) { sfx.Error(); StartCoroutine(Bump(slot.transform)); return; }
            slot.Unlock();
            sfx.Coin();
            ui.SetCoins(SaveSystem.Coins);
            TryStartBoardingPump();
        }

        void JokerSkip()
        {
            if (state != GameState.Playing || line.Count == 0) { sfx.Error(); return; }
            if (!SaveSystem.TrySpend(SkipCost)) { sfx.Error(); return; }
            sfx.Click(); ui.SetCoins(SaveSystem.Coins);
            var p = line[0]; line.RemoveAt(0);
            if (p != null) StartCoroutine(LeaveAndDestroy(p.transform));
            StartCoroutine(AfterJoker());
        }

        void JokerSwap()
        {
            if (state != GameState.Playing || line.Count < 2) { sfx.Error(); return; }
            if (!SaveSystem.TrySpend(SwapCost)) { sfx.Error(); return; }
            sfx.Click(); ui.SetCoins(SaveSystem.Coins);
            (line[0], line[1]) = (line[1], line[0]);
            StartCoroutine(AfterJoker());
        }

        void JokerTime()
        {
            if (state != GameState.Playing) { sfx.Error(); return; }
            if (!SaveSystem.TrySpend(TimeCost)) { sfx.Error(); return; }
            sfx.Click(); ui.SetCoins(SaveSystem.Coins);
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
        // Level lifecycle
        // ====================================================================
        // ---- Public progression API (called by the project's own UI) --------
        /// <summary>Advance to the next saved level (Level Complete -> Next).</summary>
        public void PlayNextLevel() { sfx.Click(); StartLevel(SaveSystem.Level); }

        /// <summary>Replay the current level (Game Over -> Retry).</summary>
        public void RetryLevel() { sfx.Click(); StartLevel(currentLevel); }

        /// <summary>
        /// Resume a failed level after the player chooses to continue (pay gold / watch ad).
        /// Grants one extra unlocked parking bay and tops the clock back up so play can go on.
        /// Safe no-op (returns false) unless the game is currently in the Lose state, so it can
        /// never be fired from the menu or mid-play without throwing.
        /// </summary>
        public bool ContinueLevel()
        {
            if (state != GameState.Lose) return false;

            AddParkingSlot();

            // Refill the clock so a time-out loss can actually keep playing.
            timeLeft = Mathf.Max(timeLeft, ContinueTimeBonus);
            ui.SetTimer(timeLeft);

            state = GameState.Playing;
            ui.ShowHud();
            sfx.Click();

            // Nudge the boarding loop in case the new bay immediately unblocks a move.
            TryStartBoardingPump();
            return true;
        }

        /// <summary>Append one already-unlocked parking bay and re-center the row.</summary>
        void AddParkingSlot()
        {
            int newIndex = totalSlots;
            totalSlots++;

            // Grow the slots array by one.
            var grown = new ParkingSlot[totalSlots];
            System.Array.Copy(slots, grown, slots.Length);

            var pad = GameObject.CreatePrimitive(PrimitiveType.Cube);
            pad.name = "Slot" + newIndex;
            pad.transform.SetParent(boardRoot, false);
            pad.transform.localScale = new Vector3(SlotSpacing * 0.84f, 0.1f, 2.4f);
            pad.GetComponent<Renderer>().sharedMaterial = slotMat;

            var slot = pad.AddComponent<ParkingSlot>();
            slot.index = newIndex;
            slot.locked = false; // continue-bonus bays open immediately
            grown[newIndex] = slot;
            slots = grown;

            // SlotX is centered on the slot count, so re-place every pad (and any parked
            // bus) now that the count changed, keeping the row aligned.
            for (int i = 0; i < totalSlots; i++)
            {
                slots[i].transform.position = new Vector3(SlotX(i), -0.05f, ParkingZ);
                if (slots[i].occupant != null)
                    slots[i].occupant.transform.position = ParkingWorld(slots[i].index);
            }
        }

        void StartLevel(int levelNumber)
        {
            currentLevel = levelNumber;
            Teardown();

            level = LevelGenerator.Generate(levelNumber);
            totalSlots = level.baseSlots + level.extraSlots;
            columnCount = level.columns;
            boardRoot = new GameObject("Board").transform;

            Theme theme = Themes.For(levelNumber);
            ApplyTheme(theme);
            BuildSlots();
            BuildColumns();
            BuildLine();

            timeLeft = level.timeLimit;
            earnedThisLevel = 0; combo = 0; lastBoardTime = -10f;

            state = GameState.Playing;
            ui.SetLevel(levelNumber);
            ui.SetTheme(theme.name);
            ui.SetCoins(SaveSystem.Coins);
            ui.SetTimer(timeLeft);
            ui.ShowHud();
        }

        void Teardown()
        {
            StopAllCoroutines();
            busy = 0; pumpRunning = false; pumpDirty = false;
            columns.Clear(); line.Clear(); slots = null;
            if (boardRoot != null) Destroy(boardRoot.gameObject);
            boardRoot = null;
        }

        void Win()
        {
            state = GameState.Win;
            int stars = timeLeft > level.timeLimit * 0.5f ? 3 : (timeLeft > level.timeLimit * 0.2f ? 2 : 1);
            int bonus = 25 + currentLevel * 5 + stars * 10;
            SaveSystem.AddCoins(bonus); earnedThisLevel += bonus;
            SaveSystem.Level = Mathf.Max(SaveSystem.Level, currentLevel + 1);
            SaveSystem.BestLevel = currentLevel;
            ui.SetCoins(SaveSystem.Coins);
            sfx.Win();
            Juice.Confetti(this, boardRoot, new Vector3(0, 6, QueueHeadZ), confettiMats, 46);

            // Scoring + progression are saved above; the custom Level Complete canvas
            // presents the result via this event.
            OnLevelComplete?.Invoke(earnedThisLevel, stars);
        }

        void Lose(string reason)
        {
            if (state != GameState.Playing) return;
            state = GameState.Lose;

            // The custom Game Over canvas presents the failure via this event.
            OnGameOver?.Invoke(reason);
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
                pad.transform.localScale = new Vector3(SlotSpacing * 0.84f, 0.1f, 2.4f);
                pad.GetComponent<Renderer>().sharedMaterial = slotMat;

                var slot = pad.AddComponent<ParkingSlot>();
                slot.index = i; slot.locked = i >= level.baseSlots;
                slots[i] = slot;

                if (slot.locked)
                {
                    var marker = new GameObject("Lock");
                    marker.transform.SetParent(pad.transform, false);
                    marker.transform.localPosition = new Vector3(0, 0.7f, 0);
                    MakeCube(marker.transform, lockMat, new Vector3(0.55f, 0.12f, 0.14f));
                    MakeCube(marker.transform, lockMat, new Vector3(0.14f, 0.12f, 0.55f));
                    slot.lockMarker = marker;
                }
            }
        }

        void BuildColumns()
        {
            columns.Clear();
            for (int j = 0; j < columnCount; j++)
            {
                var col = new List<Bus>();
                var defs = level.busColumns[j];
                for (int d = 0; d < defs.Count; d++)
                {
                    var bus = CreateBus(defs[d]);
                    bus.transform.position = ColumnPos(j, d);
                    bus.state = BusState.Queued;
                    col.Add(bus);
                }
                columns.Add(col);
            }
        }

        void BuildLine()
        {
            for (int i = 0; i < level.line.Count; i++)
            {
                PieceColor color = level.line[i];
                bool golden = level.goldenSet.Contains(i);
                bool mystery = level.mysterySet.Contains(i);

                var go = new GameObject("Person");
                go.transform.SetParent(boardRoot, false);
                go.transform.position = LinePos(i);
                var body = LowPolyBuilder.BuildPerson(go.transform, bodyMats[color], skinMat,
                    golden, mystery, mysteryMat, goldMat, out GameObject cover);

                var p = go.AddComponent<Passenger>();
                p.color = color; p.golden = golden; p.mystery = mystery;
                p.body = body; p.mysteryCover = cover;
                line.Add(p);
            }
        }

        Bus CreateBus(BusDef def)
        {
            var root = new GameObject("Bus_" + def.color);
            root.transform.SetParent(boardRoot, false);
            var bus = root.AddComponent<Bus>();
            bus.color = def.color; bus.capacity = def.capacity; bus.filledMat = bodyMats[def.color];
            bus.seatWindows = LowPolyBuilder.BuildBus(root.transform, def.capacity,
                bodyMats[def.color], glassMat, wheelMat, lightMat, seatEmptyMat, arrowMat);
            return bus;
        }

        // ====================================================================
        // Positions
        // ====================================================================
        float SlotX(int i) => (i - (totalSlots - 1) / 2f) * SlotSpacing;
        float ColumnX(int j) => (j - (columnCount - 1) / 2f) * ColumnSpacing;
        Vector3 ColumnPos(int j, int depth) => new Vector3(ColumnX(j), 0, ColumnFrontZ + depth * ColumnDepthSpacing);
        Vector3 ParkingWorld(int i) => new Vector3(SlotX(i), 0, ParkingZ);

        Vector3 LinePos(int index)
        {
            int row = index / PerRow;
            int col = index % PerRow;
            if (row % 2 == 1) col = PerRow - 1 - col;
            float x = (col - (PerRow - 1) / 2f) * 0.95f;
            return new Vector3(x, 0, QueueHeadZ - row * 0.95f);
        }

        Vector3 BusDoorWorld(Bus bus)
        {
            float len = LowPolyBuilder.BusLength(bus.capacity);
            return bus.transform.position + new Vector3(0, 0.25f, -len * 0.4f);
        }

        ParkingSlot FirstFreeSlot()
        {
            foreach (var s in slots) if (s.IsFree) return s;
            return null;
        }
        bool AnyColumnHasBus() { foreach (var c in columns) if (c.Count > 0) return true; return false; }

        // ====================================================================
        // Coroutine helpers
        // ====================================================================
        static IEnumerator MoveTo(Transform t, Vector3 target, float dur)
        {
            if (t == null) yield break;
            Vector3 from = t.position;
            float e = 0f;
            while (e < dur)
            {
                if (t == null) yield break;
                e += Time.deltaTime;
                t.position = Vector3.Lerp(from, target, Mathf.Clamp01(e / dur));
                yield return null;
            }
            if (t != null) t.position = target;
        }

        static IEnumerator Bump(Transform t)
        {
            if (t == null) yield break;
            Vector3 p = t.position;
            for (int i = 0; i < 6; i++)
            {
                if (t == null) yield break;
                t.position = p + new Vector3(Mathf.Sin(i * 1.6f) * 0.1f, 0, 0);
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
            var mats = new List<Material>();
            foreach (PieceColor c in System.Enum.GetValues(typeof(PieceColor)))
            {
                bodyMats[c] = Mat(sh, Palette.ToColor(c), 0.15f);
                mats.Add(bodyMats[c]);
            }
            confettiMats = mats.ToArray();

            glassMat     = Mat(sh, new Color(0.16f, 0.22f, 0.33f), 0.8f);
            wheelMat     = Mat(sh, new Color(0.12f, 0.12f, 0.14f), 0.2f);
            lightMat     = Mat(sh, new Color(1f, 0.95f, 0.7f), 0.6f);
            skinMat      = Mat(sh, Palette.Skin, 0.1f);
            seatEmptyMat = Mat(sh, Palette.SeatEmpty, 0.2f);
            mysteryMat   = Mat(sh, Palette.Mystery, 0.2f);
            goldMat      = Mat(sh, Palette.Gold, 0.7f);
            arrowMat     = Mat(sh, new Color(0.98f, 0.98f, 0.98f), 0.3f);
            lockMat      = Mat(sh, new Color(0.4f, 0.88f, 0.45f), 0.2f);
        }

        static Material Mat(Shader sh, Color col, float smooth)
        {
            var m = new Material(sh);
            if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", col);
            if (m.HasProperty("_Color")) m.SetColor("_Color", col);
            if (m.HasProperty("_Smoothness")) m.SetFloat("_Smoothness", smooth);
            if (m.HasProperty("_Glossiness")) m.SetFloat("_Glossiness", smooth);
            if (m.HasProperty("_Metallic")) m.SetFloat("_Metallic", 0f);
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
            RenderSettings.ambientLight = th.ambient;
            var sun = Object.FindFirstObjectByType<Light>();
            if (sun != null && sun.type == LightType.Directional) sun.intensity = 1.5f;

            Material ground = Mat(sh, th.ground, 0.05f);
            Material field  = Mat(sh, th.field, 0.05f);
            Material road   = Mat(sh, th.road, 0.05f);
            Material accent = Mat(sh, th.accent, 0.2f);
            Material main   = Mat(sh, th.propMain, 0.1f);
            Material alt    = Mat(sh, th.propAlt, 0.1f);
            Material foliage= Mat(sh, th.foliage, 0.05f);
            Material trunk  = Mat(sh, th.trunk, 0.1f);
            Material window = Mat(sh, new Color(th.sky.r * 0.9f + 0.1f, th.sky.g * 0.9f + 0.1f, th.sky.b, 1f), 0.6f);
            slotMat = accent;

            // Surfaces
            LowPolyBuilder.Slab(boardRoot, new Vector3(0, -0.32f, 4f), new Vector3(40, 0.3f, 60), field);
            LowPolyBuilder.Slab(boardRoot, new Vector3(0, -0.12f, 5f), new Vector3(9.5f, 0.2f, 22), ground);
            LowPolyBuilder.Slab(boardRoot, new Vector3(0, -0.14f, -2.5f), new Vector3(9.5f, 0.2f, 6), road);

            // Fence between queue and parking
            for (int i = -4; i <= 4; i++)
            {
                var post = MakeCube(boardRoot, accent, new Vector3(0.1f, 0.5f, 0.1f));
                post.transform.position = new Vector3(i * 1.1f, 0.25f, FenceZ);
                var bar = MakeCube(boardRoot, accent, new Vector3(1.1f, 0.06f, 0.05f));
                bar.transform.position = new Vector3(i * 1.1f + 0.55f, 0.34f, FenceZ);
            }

            // Side props
            for (int i = 0; i < 6; i++)
            {
                float z = -1f + i * 2.6f;
                LowPolyBuilder.BuildProp(boardRoot, th.prop, new Vector3(-6.2f, 0, z), main, alt, foliage, trunk, window, 1f);
                LowPolyBuilder.BuildProp(boardRoot, th.prop, new Vector3(6.2f, 0, z), main, alt, foliage, trunk, window, 1f);
            }
            // Back skyline
            for (int i = 0; i < 5; i++)
                LowPolyBuilder.BuildProp(boardRoot, th.prop, new Vector3(-5f + i * 2.5f, 0, ColumnFrontZ + columnCount + 6f),
                    main, alt, foliage, trunk, window, 1.6f);
        }

        void PlaceCamera()
        {
            if (cam == null) return;
            Vector3 pos = new Vector3(0f, 17.5f, -10.5f);
            Vector3 target = new Vector3(0f, 0f, 5.0f);
            cam.transform.position = pos;
            cam.transform.rotation = Quaternion.LookRotation(target - pos, Vector3.up);
            cam.fieldOfView = 50f;
        }
    }
}
