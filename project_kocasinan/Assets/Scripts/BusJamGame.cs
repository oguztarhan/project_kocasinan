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
        public bool IsBonus => currentLevel % 10 == 0; // every 10th level = the night traffic-dodge bonus round

        const int RecolorCost = 80, SwapCost = 40, HeliCost = 100, SlotUnlockCost = 80;
        const int ContinueBaseCost = 150;   // 1st continue costs this; doubles each further continue in the level
        int continueCount;                  // gold continues used this level (resets on StartLevel)
        int CurrentContinueCost => ContinueBaseCost << continueCount; // 150, 300, 600, 1200, ...
        const int J1UnlockLevel = 5, J2UnlockLevel = 10, J3UnlockLevel = 15; // RECOLOR / SWAP / HELI

        // World Z grows AWAY from the camera (up the portrait screen). Bottom→top:
        // big bus grid (low Z) -> parking row -> thin people band (high Z).
        const float CellSize = 1.1f;          // BIG cells: a 6-wide jam fills the portrait at the zoomed camera; vehicles scale with this
        const float GridExitZ = 5.5f;         // grid row y=0 (exit edge); the H9 jam fills the lower screen (deepest row stays on)
        const float ScreenFloorZ = -7.3f;     // lowest on-screen ground z (tied to PlaceCamera FOV 52) — away-exits keep their near edge above this
        const float RoadZ = 7.4f;             // road/drive-in lane — pushed back +0.5 so the jam front no longer touches the road band
                                              // vehicle can drive ALONG it to its slot without clipping the jam OR the parked cars
        const float ParkingZ = 9.7f;          // bus stop (parking row); nudged back +0.4 (clears the moved-back road on the front side)
        const float SlotSpacing = 1.4f;       // wide enough for the widest vehicle (bus) side-by-side; 7 pads fit (~±4.2 < visible)
        const float PeopleZ = 11.8f;          // mid of the people area — pushed back +1.4 so parked buses no longer clip the queue
        const float PeopleSpacing = 0.85f;    // (queue is an L from the top-right door)
        const float FenceZ = 11.4f;           // fence IN FRONT of the people line (toward the buses) — moved back +1.4 with the people
        const float FacadeZ = 13.1f;          // mall/terminal wall center, TOP-RIGHT; the L-queue (vertical 2 + horizontal) feeds its door
        const float DoorSpawnZ = 12.4f;       // people are born at the door (top of the L) and the line runs down 2 then left across
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
        // Imported SimplePoly/Polygonal pack assets, loaded STRAIGHT from Resources/Fx (copied there, no build step).
        // Any null -> that piece falls back to procedural; the game never hard-fails on a missing asset.
        GameObject smokeFx, hitFx, busStopFx;
        GameObject[] cityBuildings, cityTrees, cityRoads, cityProps;
        Material toonOutlineFx;
        Material smokeMat, hitMat;            // cached runtime URP particle mats (so the built-in-shader VFX aren't magenta)
        Font seatFont;
        Transform boardRoot;

        readonly Dictionary<PieceColor, Material> bodyMats = new Dictionary<PieceColor, Material>();
        Material glassMat, wheelMat, lightMat, skinMat, seatEmptyMat, mysteryMat, goldMat, arrowMat, lockMat, slotMat;
        Material roadMat, neonMat, stripeMat; // asphalt road + emissive neon (people-left sign) + white parking-bay lane stripes
        Material headlightMat, beamMat, beamMatDim, lampGlowMat; // T4: warm emissive lens + night beam (bright for moving traffic / dim for the packed jam) + soft lamp halo
        Mesh headlightBeamMesh;               // procedural tapered ground cone (built once, shared by every headlight)
        Material[] confettiMats;

        LevelData level;
        int currentLevel = 1;
        int totalSlots, gridW, gridH;
        float[] doorXs;       // facade door world-X positions (set in BuildFacade); openings the interior line shows through
        float exitDoorX;      // the ONE door the boarding queue comes out of (set in BuildFacade)
        UnityEngine.UI.Text peopleLeftSign; // world-space "people left" sign by the road (rebuilt each level)
        int earnedThisLevel, combo, maxCombo, goldenThisLevel, pendingReward; // pendingReward = the win reward, granted on CLAIM
        float lastBoardTime = -10f;
        int busy;
        bool pumpRunning, pumpDirty;

        // (#4) Level-1 tutorial coach (self-contained overlay; created lazily; reused by #5/#6).
        TutorialCoach coach; bool tutorialActive; int tutorialStep; string tutStep2;

        // ---- Bonus night-mode (every 10th level): countdown + cross-traffic + night headlights ----
        const float BonusTime = 120f;       // bonus-only countdown length (2 minutes)
        const int BonusReward = 50;         // coins granted for finishing the bonus IN TIME
        const int PerfectBonus = 0;         // optional EXTRA for a no-crash run (opt-in: 0 = off by default)
        float bonusTimeLeft;
        bool crashedThisBonus;              // set on a T3 crash -> disqualifies the perfect bonus
        bool nightMode;                     // cached in ApplyTheme (Night/Bonus) -> board + traffic headlights
        ColorAdjustments postCA;            // cached post-grade (deepened on dark themes, restored on bright)
        Vignette postVig;
        // Pooled cross-traffic (bonus only): plain position model, NO colliders, NO per-car Update.
        class TrafficCar { public Transform tf; public float x; public int dir; public int lane; }
        readonly List<TrafficCar> traffic = new List<TrafficCar>();
        float trafficHalfLoop, trafficLoop; // wrap bounds (even spacing) shared by both lanes
        int trafficSpawnIdx;                // deterministic variety counter (no Random in the hot path)
        float trafficCarSpeed;              // progressive car speed (eased by bonus index in BuildTraffic): slow at L10, faster later

        ParkingSlot[] slots;
        readonly Dictionary<Vector2Int, Bus> occ = new Dictionary<Vector2Int, Bus>();
        // Cells an IN-FLIGHT exiting bus's swept footprint is currently driving through (over the jam). Keeps a
        // moving vehicle VISIBLE to every later path/slide query so a second bus can't cross its live corridor.
        // Freed the moment the bus clears the jam (z >= RoadZ). Helicopter is exempt (flies over, never reserves).
        readonly Dictionary<Vector2Int, Bus> reservedByMoving = new Dictionary<Vector2Int, Bus>();
        Bus gridDriver; // the ONE bus currently driving ACROSS the jam — serialized so two exiting vehicles can never mesh
        readonly List<Bus> gridBuses = new List<Bus>();
        readonly List<Bus> liveBuses = new List<Bus>(); // EVERY vehicle this level (jam + in-flight + parked) — scanned each frame to drive the engine sound
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
            smokeFx       = Resources.Load<GameObject>("Fx/smoke");            // imported pack prefabs copied into Resources/Fx (null -> procedural)
            hitFx         = Resources.Load<GameObject>("Fx/hit");
            busStopFx     = Resources.Load<GameObject>("Fx/busstop");
            toonOutlineFx = Resources.Load<Material>("Fx/toon_outline");
            cityBuildings = Resources.LoadAll<GameObject>("Fx/Buildings");
            cityTrees     = Resources.LoadAll<GameObject>("Fx/Trees");
            cityRoads     = Resources.LoadAll<GameObject>("Fx/Roads");
            cityProps     = Resources.LoadAll<GameObject>("Fx/Props");
            gameSettings = Resources.Load<GameSettings>("GameSettings");       // tuning knobs (Inspector-editable)
            if (gameSettings == null) gameSettings = ScriptableObject.CreateInstance<GameSettings>(); // fall back to defaults
            seatFont = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf"); // roof seat-count number
            PlaceCamera();
            SetupPostFX();

            sfx = Sfx.Ensure(); // ONE persistent SFX voice for the whole game (no mixing); UiClickSound clicks every button
            ui = gameObject.AddComponent<GameUI>();
            var ad = AdManager.Ensure(this); // AdMob singleton (DontDestroyOnLoad); created from the gameplay scene
            ui.OnMenu = () => { PauseRequested?.Invoke(); }; // click handled globally by UiClickSound
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
            ui.OnContinueAd = () => ad.ShowRewarded("continue",
                onReward: () => { ui.HideContinue(); ContinueLevel(); },          // revive ONLY on a completed rewarded ad
                onClosedNoReward: () => sfx.Error());                              // skip / no-ad -> stay on the continue panel
            ui.OnContinueDeclined = () =>                                          // leaving the loss flow: loss-interstitial (if eligible) THEN Failed
            {
                ui.HideContinue();
                Time.timeScale = 0f;
                ad.ShowInterstitialIfEligible(() => { Time.timeScale = 1f; ui.ShowFailed(); });
            };
            ui.OnFreeCoins = n => { SaveSystem.AddCoins(n); ui.SetCoins(SaveSystem.Coins); CoinsChanged?.Invoke(SaveSystem.Coins); }; // +coins rewarded button
            ui.Build(RecolorCost, SwapCost, HeliCost, J1UnlockLevel, J2UnlockLevel, J3UnlockLevel);

            // AdMob cadence signals + banner — subscribe with += so any existing handlers are preserved.
            LevelCompleted += (earned, stars) => ad.AddInterstitialWin();   // WIN signal (Win); increment only — success panel is up
            LevelFailed    += reason => ad.AddInterstitialLoss();           // LOSS signal (Lose); increment only — continue panel is up
            LevelStarted   += _ => ad.ShowBanner();                         // banner shown during gameplay

            levelSelect = gameObject.AddComponent<LevelSelect>();
            levelSelect.Build(this);
            ui.OnLevels = () => levelSelect.Open(); // in-game Settings -> LEVELS map (wired after the field is built)

            if (autoStart) StartCoroutine(AutoStartFirstLevel());
            else { state = GameState.Menu; ui.HideHud(); }
        }

        // Build the FIRST level on the NEXT frame rather than inside Start(): on frame 1 some freshly-instantiated
        // imported-vehicle meshes aren't active/registered yet, so the body re-centering came out a touch off (roof
        // arrows sat slightly LEFT) until the player reloaded. One frame later the engine is warmed up exactly like
        // a reload, so the first level matches every subsequent load. (Pairs with the include-inactive mesh queries.)
        IEnumerator AutoStartFirstLevel()
        {
            yield return null;
            LoadLevel(SaveSystem.Level);
        }

        // ---- Public control ------------------------------------------------
        public void LoadLevel(int levelNumber) { CancelInvoke(); StartLevel(levelNumber); }
        public void NextLevel() { LoadLevel(SaveSystem.Level); }
        public void RetryLevel() { LoadLevel(currentLevel); }
        public void ToggleSound() { SaveSystem.Sound = !SaveSystem.Sound; sfx.Click(); }

        // Settings panel: HOME button -> back to the main menu scene. (click handled globally by UiClickSound)
        public void GoToMainMenu() { AdManager.Instance?.HideBanner(); SceneManager.LoadScene("MainMenu"); }

        // Success panel: grant the win reward (pendingReward, or 2x when claimed via the rewarded ad) then advance.
        void ClaimWinReward(int amount)
        {
            if (state != GameState.Win) return;
            int grant = (amount >= 40) ? pendingReward * 2 : pendingReward; // GameUI passes 40 for the AD x2 button, 20 otherwise
            AddCoins(grant);
            sfx.Coin();
            // Win-interstitial fires AFTER the claim, with time FROZEN so the next level can't build under the ad;
            // the ad close (or the immediate no-ad fallback) un-pauses and advances. (SaveSystem.Level already advanced in Win.)
            var ad = AdManager.Instance;
            if (ad != null) { Time.timeScale = 0f; ad.ShowInterstitialIfEligible(() => { Time.timeScale = 1f; NextLevel(); }); }
            else NextLevel();
        }

        // (Economy rework) The single end-of-level coin reward. Bounded so the balance can't balloon with level size:
        //   normal:  25 + 5*min(level,20)   |   bonus level: flat 100
        //   + star bonus (0 / 10 / 25 for 1 / 2 / 3 stars)   + golden bonus (+5 each, capped at +20).
        // CLAIM grants this; WATCH-AD grants 2x (see ClaimWinReward).
        int LevelReward(int stars, bool bonus)
        {
            int basePart  = bonus ? 100 : (25 + 5 * Mathf.Min(currentLevel, 20));
            int starBonus = stars >= 3 ? 25 : (stars >= 2 ? 10 : 0);
            int goldBonus = Mathf.Min(goldenThisLevel * 5, 20);
            return basePart + starBonus + goldBonus;
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
            TryStartBoardingPump();
        }

        // ====================================================================
        void Update()
        {
#if UNITY_EDITOR
            // TEMP AD DEBUG (editor only): press I to force an interstitial. Gated by AdManager.SHOW_AD_DEBUG.
            if (AdManager.SHOW_AD_DEBUG && Keyboard.current != null && Keyboard.current.iKey.wasPressedThisFrame)
                AdManager.Instance?.ForceInterstitial(null);
#endif
            UpdateEngineSfx(); // engine loops while ANY vehicle moves, stops the instant they all stop (runs even when paused -> off)
            if (state != GameState.Playing) return;
            // The bonus timer may end the level THIS frame (FinishBonus -> NextLevel rebuilds + re-sets Playing),
            // so bail if the level changed or we left Playing — never run taps against a half-swapped board.
            if (IsBonus) { int lv0 = currentLevel; TickBonusTimer(); if (currentLevel != lv0 || state != GameState.Playing) return; }
#if UNITY_EDITOR
            AssertNoOccOverlap(); // invariant watchdog: logs to the Console if two vehicles ever share a cell
#endif
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

        const float EngineMoveEpsSq = 0.000004f; // ~0.002 units/frame: a vehicle that moved more than this is "driving"

        // Drive the looping engine purely from ACTUAL motion: if any tracked vehicle's position changed since last
        // frame, the engine plays; the frame they all stop, it stops. Robust to every move path (exit/crawl/drive-
        // away/heli/crash-return) without touching those coroutines. Cross-traffic cars aren't Bus, so they're excluded.
        void UpdateEngineSfx()
        {
            if (sfx == null) return;
            bool moving = false;
            if (state == GameState.Playing)
            {
                for (int i = 0; i < liveBuses.Count; i++)
                {
                    var b = liveBuses[i];
                    if (b == null) continue;
                    Vector3 p = b.transform.position;
                    if (b.sfxPosInit && (p - b.sfxLastPos).sqrMagnitude > EngineMoveEpsSq) moving = true;
                    b.sfxLastPos = p; b.sfxPosInit = true;
                }
            }
            sfx.SetEngine(moving);
        }

        void OnDisable() { if (sfx != null) sfx.SetEngine(false); } // never leave the engine humming if the game scene unloads

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

            // occ (jammed vehicles) PLUS the live corridors of in-flight exiting buses — so a tap never starts an
            // exit whose lane crosses a bus that's still driving across the jam.
            System.Func<Vector2Int, bool> blocked = c => Blocked(c, bus);
            bool laneClear = LevelGenerator.SlideClear(bus.cell, bus.dir, bus.length, blocked, gridW, gridH);
            var slot = laneClear ? NearestFreeSlot(GridWorldCenter(bus.cell, bus.dir, bus.length).x) : null;

            if (laneClear && slot != null)
            {
                foreach (var c in LevelGenerator.OccCells(bus.cell, bus.dir, bus.length)) occ.Remove(c);
                gridBuses.Remove(bus);
                slot.occupant = bus; bus.slotIndex = slot.index; bus.state = BusState.MovingToSlot; // claimed synchronously
                if (tutorialActive) AdvanceTutorialOnFirstMove(); // (#4) first successful park advances the coach
                StartCoroutine(ExitRoutine(bus, slot)); // engine (vroom) starts automatically while it drives — see UpdateEngineSfx
                return;
            }

            // Partial advance: crawlers cap at advanceN cells/tap; normals advance until the blocker.
            int cap = bus.advanceN > 0 ? bus.advanceN : gridW + gridH;
            int step = LevelGenerator.MaxAdvanceSteps(bus.cell, bus.dir, bus.length, blocked, gridW, gridH, cap);
            if (step == 0) { sfx.Crash(); StartCoroutine(Bump(bus.transform)); SpawnBlockedHit(bus); return; } // blocked: crash + shake + debris poof (no forward progress, no exit)

            foreach (var c in LevelGenerator.OccCells(bus.cell, bus.dir, bus.length)) occ.Remove(c); // free old, THEN
            bus.cell += bus.dir * step;
            foreach (var c in LevelGenerator.OccCells(bus.cell, bus.dir, bus.length)) occ[c] = bus;  // add new -- atomic, no leak
            bus.state = BusState.Staging; // not tappable until the crawl animation finishes
            StartCoroutine(CrawlMove(bus, step)); // engine (vroom) plays automatically while it crawls — see UpdateEngineSfx
        }

        IEnumerator ExitRoutine(Bus bus, ParkingSlot slot)
        {
            busy++;
            // Hold our own footprint immediately: occ was freed at the tap (237) but we still physically sit here
            // while planning/waiting below — keep us visible to other buses' paths until we actually move off.
            ReserveCorridor(bus, new List<Vector2Int> { bus.cell });

            // CONCURRENT exits on ALL levels (normal AND bonus): do NOT wait for another vehicle to arrive. TryTapBus
            // only starts an exit when this vehicle's slide lane is already clear of jammed cars AND every in-flight
            // corridor, so vehicles in non-conflicting columns slide out TOGETHER — tap several (with free stops) and
            // they all move at once. A vehicle whose lane conflicts with one in flight just doesn't start an exit yet.
            // (reservedByMoving keeps the jam slides collision-free; the bonus crash/cross logic is all per-vehicle.)
            var freeCorr = StartCoroutine(FreeCorridorWhenClear(bus)); // handle kept so a T3 crash can stop it (else it outlives the retreat)
            // Release each reserved lane cell the moment this bus has driven PAST it, so a vehicle queued directly
            // BEHIND it can follow into the vacated space immediately instead of waiting for the whole exit to finish.
            // NON-BONUS only: on bonus a mistimed crossing reverses the bus back DOWN this very lane (CrashAndReturn),
            // so the lane must stay fully reserved for the round trip there.
            if (!IsBonus) StartCoroutine(ReleaseCorridorBehind(bus));
            var exhaust = SpawnExhaust(bus); // T5: rear exhaust trail while it drives off (null on lowEnd / unbuilt catalog)

            // Exit GROUNDED, never hitting a vehicle. The bus already FACES `dir`; if its straight `dir` lane is
            // STILL clear (re-checked — a crawl may have moved during our serialize wait) we SLIDE STRAIGHT out
            // along it (linear move — NO spline bow, NO rotation inside the jam, so the body can't enter/sweep an
            // occupied cell), then drive to the slot through OPEN space only (above the jam, or around its side).
            bool laneClear = LevelGenerator.SlideClear(bus.cell, bus.dir, bus.length, c => Blocked(c, bus), gridW, gridH);
            if (laneClear)
            {
                int maxSteps = ExitDistance(bus.cell, bus.dir) + bus.length;              // full slide that clears the board (upper bound)
                int clearSteps = maxSteps;
                bool away = bus.dir.y > 0;                                                // arrow points AWAY from the stops
                if (away)
                {
                    // Slide down only until the body is JUST BELOW the deepest jammed vehicle (so we can cross
                    // under the jam), but keep our NEAR edge ON-SCREEN — never drop off the bottom. Adaptive to
                    // the jam's real depth and the vehicle's own length.
                    float halfLen = bus.length * 0.55f;                          // body half-length (world)
                    float deepestZ = ParkingZ;
                    foreach (var kv in occ) { float z = CellWorld(kv.Key).z; if (z < deepestZ) deepestZ = z; }
                    float underZ = deepestZ - (halfLen + 0.45f);                 // just below the deepest jammed vehicle
                    underZ = Mathf.Max(underZ, ScreenFloorZ + halfLen + 0.25f);  // ...but the near edge stays on-screen
                    int need = Mathf.CeilToInt((bus.transform.position.z - underZ) / CellSize);
                    clearSteps = Mathf.Clamp(need, 1, maxSteps);
                }
                else if (bus.dir.y < 0)
                {
                    // Toward-parking: slide up only until the body just clears the jam top, then take the road lane to
                    // the slot — don't overshoot far past the stops and bounce back down to the road.
                    float halfLen = bus.length * 0.55f;
                    int need = Mathf.CeilToInt((GridExitZ + halfLen + 0.5f - bus.transform.position.z) / CellSize);
                    clearSteps = Mathf.Clamp(need, 1, maxSteps);
                }
                var lane = new List<Vector2Int>();
                for (int s = 0; s <= clearSteps; s++) lane.Add(bus.cell + bus.dir * s);
                ReserveCorridor(bus, lane);                                               // hold the straight lane (a crawling vehicle can't enter it)
                Vector3 clearPt = bus.transform.position + new Vector3(bus.dir.x, 0, -bus.dir.y) * (clearSteps * CellSize);
                clearPt = OnScreenX(clearPt, 1.0f);                                       // never slide the body off the SIDE of the screen (fixes deep sideways exits too)
                float slideDur = Vector3.Distance(bus.transform.position, clearPt) / Mathf.Max(gameSettings.busDriveSpeed, 1f);
                yield return MoveTo(bus.transform, clearPt, slideDur);                    // STRAIGHT slide along the clear lane (no bow/rotation in the jam)

                float slotX = SlotX(slot.index);
                // ---- Phase 1: drive ONTO the road (no final bay commitment yet) ----
                var toRoad = new List<Vector3>();
                if (away)
                {
                    // Rise hugging the on-screen side LANE up to the ROAD. Both side lanes sit OUTSIDE the jam (the
                    // ~25% zoom-out opened them), so either is grounded + collision-free — rise on the side TOWARD the
                    // (provisional) bay so it heads to its closest stop instead of crossing the whole road back.
                    float side = (slotX >= clearPt.x) ? 1f : -1f;
                    const float M = 1.0f;                                        // body half-width + spline-bow + safety
                    toRoad.Add(new Vector3(side * (VisHalfW(clearPt.z) - M), 0, clearPt.z)); // out to the on-screen side lane (below the jam)
                    for (float z = clearPt.z + 1.6f; z < RoadZ; z += 1.6f)
                        toRoad.Add(new Vector3(side * (VisHalfW(z) - M), 0, z));  // rise hugging the side lane (gentle, slow turn)
                    toRoad.Add(new Vector3(side * (VisHalfW(RoadZ) - M), 0, RoadZ));
                }
                else
                    toRoad.Add(new Vector3(clearPt.x, 0, RoadZ));     // toward-parking / sideways exit: come onto the road at our current x
                for (int i = 0; i < toRoad.Count; i++) toRoad[i] = OnScreenX(toRoad[i], 1.0f);

                if (IsBonus)
                {
                    // BONUS keeps ONE continuous mesh-checked approach to the tap-time bay — CrashAndReturn reverses
                    // the WHOLE route, so it must stay a single committed path. (T3: continuous mesh check, so a car
                    // arriving mid-cross still crashes it; the old single pre-pull-up snapshot missed those.)
                    var rest = new List<Vector3>(toRoad)
                    {
                        OnScreenX(new Vector3(slotX, 0, RoadZ), 1.0f),     // along the open road to the bay's x
                        OnScreenX(new Vector3(slotX, 0, ParkingZ), 1.0f),  // pull up into the bay
                    };
                    yield return DriveBonusApproach(bus, rest, gameSettings.busDriveSpeed, gameSettings.turnSmoothness);
                    if (bus.crossMeshed)
                    {
                        yield return CrashAndReturn(bus, slot, exhaust, freeCorr, rest, clearPt); // penalty + grounded reverse-path return + re-claim
                        yield break;                                     // never fall into the park tail below
                    }
                }
                else
                {
                    // ---- Phase 2 (NON-BONUS): now that it's ON the road, pick the bay nearest to where it ACTUALLY
                    // came onto the road (its real x on the road — NOT its old jam cell), then pull straight in. So a
                    // vehicle that emerges on the LEFT (e.g. an away exit that rose up the left lane, or a leftward
                    // sideways slide) stops at the closest LEFT bay instead of crossing the whole road to a right one.
                    yield return DrivePath(bus.transform, toRoad, gameSettings.busDriveSpeed, gameSettings.turnSmoothness);
                    float roadX = bus.transform.position.x;              // where it really is on the road
                    slot = NearestSlotToRoad(bus, slot, roadX);          // keeps its reserved bay only if it's still the closest
                    slotX = SlotX(slot.index);
                    var toBay = new List<Vector3>
                    {
                        OnScreenX(new Vector3(slotX, 0, RoadZ), 1.0f),     // along the open road to the bay's x
                        OnScreenX(new Vector3(slotX, 0, ParkingZ), 1.0f),  // pull up into the bay
                    };
                    yield return DrivePath(bus.transform, toBay, gameSettings.busDriveSpeed, gameSettings.turnSmoothness);
                }
            }
            else
            {
                // RARE: a crawl blocked the lane after the tap so there's no grounded route out — lift over (the
                // only remaining no-collision option). Almost never happens.
                yield return MoveAndRotateArc(bus.transform, ParkingWorld(slot.index), Quaternion.Euler(0, 180f, 0), 0.6f, 2.0f);
            }
            bus.transform.rotation = Quaternion.Euler(0, 180f, 0);                        // settle to exact parked facing (nose +Z)
            FreeCorridor(bus);
            if (gridDriver == bus) gridDriver = null;
            StopExhaust(exhaust, false); // T5: stop the trail as it parks (stays under the parked bus, fades, self-destructs)
            bus.state = BusState.Parked;
            sfx.Honk();                                                                  // ONE honk as it pulls into the stop
            StartCoroutine(Juice.PunchScale(bus.transform, 0.16f));
            busy--;
            TryStartBoardingPump();
            CheckEnd();
        }

        // T3 (BONUS): a mistimed crossing — the tapped bus reached the road but cross-traffic is in the way. Crash
        // FX + a 3s time penalty, then DRIVE BACK GROUNDED to its jam cell and re-claim it (re-tappable). The bus is
        // NEVER lost and NEVER lifts/flies; the penalty only costs TIME (and the perfect bonus), never solvability.
        IEnumerator CrashAndReturn(Bus bus, ParkingSlot slot, GameObject exhaust, Coroutine freeCorr, List<Vector3> rest, Vector3 clearPt)
        {
            if (bus == null || slot == null) { busy--; yield break; }  // teardown insurance (keep busy balanced)
            // Kill THIS exit's in-flight FreeCorridorWhenClear: a multi-cell bus that crashed before its tail crossed
            // RoadZ leaves that coroutine still waiting, and it would otherwise survive the retreat and later free a
            // RE-TAPPED exit's live jam corridor at the wrong moment (desync). CrashAndReturn frees this exit's own
            // corridor below (FreeCorridor), so stopping it here loses nothing.
            if (freeCorr != null) StopCoroutine(freeCorr);

            // FX + feedback IN PLACE at the road: shake HERE first (NOT concurrent with any move) so it clearly reads
            // as "THIS vehicle crashed here", then it drives back. (The old code shook while MoveTo'ing -> jitter.)
            Vector3 hitPos = bus.transform.position + Vector3.up * 0.5f;
            SpawnHit(hitPos);                                                       // HitRock debris poof (no-op on lowEnd)
            Juice.Burst(this, boardRoot, hitPos, bodyMats[bus.color], 18, 5.5f);    // ALWAYS a big colored spark burst so the crash is unmistakable
            Juice.Burst(this, boardRoot, hitPos, goldMat, 10, 4f);                  // + a bright spark flash
            sfx.Crash();                                                           // impact sound
            sfx.Screech();                                                         // + tyre screech
            StopExhaust(exhaust, false);                           // stop the exit trail
            yield return Bump(bus.transform);                      // shake IN PLACE (yielded -> nothing else moves it -> no jitter/teleport)
            if (bus == null || state != GameState.Playing) { busy--; yield break; }

            // Penalty: TIME only. A resulting timeout funnels through the SAME single FinishBonus(false) below.
            bonusTimeLeft -= 3f;
            crashedThisBonus = true;                               // a crash this run drops a star on the success panel
            ui.SetBonusCountdown(bonusTimeLeft);
            SpawnPenaltyText(bus.transform.position, "-3", new Color(1f, 0.28f, 0.24f)); // red floating "-3" at the crash

            // GROUNDED return: DRIVE BACK retracing the (clear) exit lane IN REVERSE — never cuts straight across the
            // jam, the NOSE follows the path (reads as driving, not sliding/flying), y stays 0, NO MoveAndRotateArc.
            Vector3 home = GridWorldCenter(bus.cell, bus.dir, bus.length); // bus.cell is UNCHANGED on the ExitRoutine path
            ReserveCorridor(bus, new List<Vector2Int> { bus.cell });        // re-hold the landing cell across the retreat
            var back = new List<Vector3>();
            for (int i = rest.Count - 3; i >= 0; i--) back.Add(rest[i]);    // road/side-lane waypoints reversed (we're at rest[len-2]=(slotX,RoadZ); the ParkingZ pull-up we never reached is skipped)
            back.Add(clearPt);                                              // back to the slide start (just outside the jam)
            back.Add(home);                                                // ...then slide straight into the jam cell
            yield return DrivePath(bus.transform, back, gameSettings.busDriveSpeed, gameSettings.turnSmoothness);
            if (bus == null || state != GameState.Playing) { busy--; yield break; } // mid-frame teardown insurance
            bus.transform.rotation = Quaternion.Euler(0, DirYaw(bus.dir), 0);       // restore the arrow facing in the cell

            // RE-CLAIM (mirror-reverse of TryTapBus's claim). The occ re-add + corridor-free happen in the SAME frame
            // (no yield), so there is no window where the home cells are unheld. AssertNoOccOverlap catches a slip in-editor.
            foreach (var c in LevelGenerator.OccCells(bus.cell, bus.dir, bus.length)) occ[c] = bus; // re-add the footprint
            if (!gridBuses.Contains(bus)) gridBuses.Add(bus);     // undo the gridBuses.Remove
            slot.occupant = null; bus.slotIndex = -1;             // release the stop it had claimed
            bus.state = BusState.Queued;                          // re-tappable
            FreeCorridor(bus);                                    // drop reservedByMoving (occ holds the cells now)
            if (gridDriver == bus) gridDriver = null;             // release the serialize lock -> next tap isn't deadlocked
            busy--;                                               // exactly ONE busy-- (the busy++ was taken in ExitRoutine)

            if (bonusTimeLeft <= 0f) { FinishBonus(false); yield break; } // the crash ran the clock out -> single soft end
            TryStartBoardingPump();
            CheckEnd();
        }

        // T3 (BONUS): drive the crossing approach like DrivePath, but CONTINUOUSLY check for a REAL traffic mesh and
        // bail (-> bonusCrossMeshed) the instant the body overlaps a car WHILE crossing a lane. Driving ALONG the road
        // (moving in x) sits in the median BETWEEN the two lanes and never meshes, so we only test when moving
        // PERPENDICULAR (mostly in z) inside the lane band — which is exactly when the body sweeps a lane. This
        // replaces the old single pre-pull-up snapshot that missed cars arriving mid-cross.
        IEnumerator DriveBonusApproach(Bus bus, List<Vector3> pts, float speed, float turnLerp)
        {
            if (bus == null || pts == null || pts.Count == 0) yield break;
            bus.crossMeshed = false; // per-VEHICLE flag (not a shared field) so concurrent bonus crossings each track their own mesh
            float halfLen = LowPolyBuilder.VehicleLength(bus.type, CellSize) * 0.5f; // body half-length = its z-extent while crossing
            var c = new List<Vector3>(pts.Count + 1) { bus.transform.position };
            c.AddRange(pts);
            Vector3 C(int i) => c[Mathf.Clamp(i, 0, c.Count - 1)];
            var s = new List<Vector3> { c[0] };                                   // dense arc-length spline samples (same as DrivePath)
            for (int i = 0; i < c.Count - 1; i++)
            {
                int n = Mathf.Max(2, Mathf.CeilToInt(Vector3.Distance(C(i), C(i + 1)) / 0.1f));
                for (int k = 1; k <= n; k++) s.Add(CatmullRom(C(i - 1), C(i), C(i + 1), C(i + 2), k / (float)n));
            }
            int idx = 0;
            while (idx < s.Count - 1)
            {
                if (bus == null) yield break;
                Vector3 prev = bus.transform.position;
                float move = Mathf.Max(speed, 0.01f) * Time.deltaTime;
                while (idx < s.Count - 1 && move > 0f)
                {
                    float d = Vector3.Distance(bus.transform.position, s[idx + 1]);
                    if (d <= move) { move -= d; bus.transform.position = s[idx + 1]; idx++; }
                    else { bus.transform.position = Vector3.MoveTowards(bus.transform.position, s[idx + 1], move); break; }
                }
                Vector3 look = s[Mathf.Min(idx + 1, s.Count - 1)] - bus.transform.position;
                if (look.sqrMagnitude > 1e-5f)
                    bus.transform.rotation = Quaternion.Slerp(bus.transform.rotation,
                        Quaternion.Euler(0, Mathf.Atan2(-look.x, -look.z) * Mathf.Rad2Deg, 0), 1f - Mathf.Exp(-turnLerp * Time.deltaTime));
                // Crash on a REAL mesh while CROSSING (moving perpendicular, mostly in z). Per-car x AND z overlap
                // test using the car's ACTUAL lane z, so it never false-fires below the lanes or while merging
                // ALONG the road's median (where the bus moves in x and its z-extent is just its narrow width).
                Vector3 vel = bus.transform.position - prev;
                if (Mathf.Abs(vel.z) > Mathf.Abs(vel.x))
                {
                    Vector3 bp = bus.transform.position;
                    for (int ti = 0; ti < traffic.Count; ti++)
                    {
                        var car = traffic[ti];
                        if (car.tf != null && Mathf.Abs(car.x - bp.x) < gameSettings.trafficClearance
                            && Mathf.Abs(car.tf.position.z - bp.z) < halfLen + 0.4f)
                        { bus.crossMeshed = true; break; }
                    }
                    if (bus.crossMeshed) yield break;
                }
                yield return null;
            }
            if (bus != null) bus.transform.position = s[s.Count - 1];
        }

        // occ (jammed) OR a live in-flight corridor holds this cell for a DIFFERENT bus.
        bool Blocked(Vector2Int c, Bus self) =>
            (occ.TryGetValue(c, out var ob) && ob != self) || (reservedByMoving.TryGetValue(c, out var rb) && rb != self);

        // Reserve the swept FOOTPRINT of the path's jam-side cells (z < RoadZ) for this moving bus.
        void ReserveCorridor(Bus bus, List<Vector2Int> cells)
        {
            foreach (var c in cells)
                foreach (var fc in LevelGenerator.OccCells(c, bus.dir, bus.length))
                {
                    if (CellWorld(fc).z >= RoadZ) continue; // only the contested jam corridor
#if UNITY_EDITOR
                    if (reservedByMoving.TryGetValue(fc, out var ex) && ex != bus)
                        Debug.LogError($"[OccOverlap] reserving {fc} for {bus.name} but already held by {(ex ? ex.name : "?")}");
#endif
                    reservedByMoving[fc] = bus;
                }
        }

        void FreeCorridor(Bus bus)
        {
            if (reservedByMoving.Count == 0) return;
            var rm = new List<Vector2Int>();
            foreach (var kv in reservedByMoving) if (kv.Value == bus) rm.Add(kv.Key);
            foreach (var c in rm) reservedByMoving.Remove(c);
        }

        // Progressively free the reserved corridor cells this bus has already driven PAST (measured along its travel
        // direction, with a body-length + buffer margin so its current footprint stays reserved). This lets a vehicle
        // directly behind it FOLLOW into the vacated lane right away. Started from ExitRoutine on NON-bonus levels only.
        IEnumerator ReleaseCorridorBehind(Bus bus)
        {
            if (bus == null) yield break;
            Vector3 fwd = new Vector3(bus.dir.x, 0f, -bus.dir.y);
            if (fwd.sqrMagnitude < 1e-4f) yield break;
            fwd.Normalize();
            float behind = (bus.length * 0.5f + 0.6f) * CellSize; // free a cell only once it's well behind the tail
            var passed = new List<Vector2Int>();
            while (bus != null && bus.state != BusState.Parked)
            {
                passed.Clear();
                Vector3 pos = bus.transform.position;
                foreach (var kv in reservedByMoving)
                    if (kv.Value == bus && Vector3.Dot(CellWorld(kv.Key) - pos, fwd) < -behind)
                        passed.Add(kv.Key);
                for (int i = 0; i < passed.Count; i++) reservedByMoving.Remove(passed[i]);
                yield return null;
            }
        }

        IEnumerator FreeCorridorWhenClear(Bus bus)
        {
            // Free only once the TAIL (not just the center) has cleared the jam: for a length-L body the center
            // must sit a half-body PAST RoadZ so the rear cells are out. length-1 Car -> margin 0 (identical to before).
            yield return new WaitUntil(() => bus == null ||
                bus.transform.position.z >= RoadZ + (bus.length - 1) * 0.5f * CellSize - 0.05f);
            FreeCorridor(bus); // free the JAM lane for crawls once the tail is out (normal levels don't use gridDriver
                               // at all now; on BONUS the serialize lock is released at PARK / on crash, not here)
        }

#if UNITY_EDITOR
        // Invariant watchdog (Editor only, every frame): NO grid cell may be held by two DIFFERENT vehicles across
        // occ (jammed) + reservedByMoving (in-flight corridors). Logs a red Console error naming the cell + both
        // buses if it ever happens — so a regression in the lock-step shows up immediately while you play.
        void AssertNoOccOverlap()
        {
            foreach (var kv in reservedByMoving)
                if (occ.TryGetValue(kv.Key, out var ob) && ob != null && ob != kv.Value)
                    Debug.LogError($"[OccOverlap] cell {kv.Key} held by '{ob.name}' (jam) AND '{(kv.Value ? kv.Value.name : "?")}' (moving)");
        }
#endif

        // ---- A* shortest CLEAR, on-screen exit path (FOOTPRINT-AWARE) ------------------------------------------
        // Returns the CELL CHAIN (start..goal) or null. Each node is vetted with the bus's FULL OccCells footprint
        // (body + diagonal corner cells — the SAME geometry the generator placed with) against jammed vehicles
        // (occ) AND other in-flight corridors (reservedByMoving), and every footprint cell must stay on-screen. So
        // the animated drive can never corner its body through a neighbour. ignoreReserved=true tests against
        // jammed vehicles ONLY (used to tell a transient corridor-block from a genuinely dense board).
        // For a length-1 Car, OccCells(c,dir,1) == [c], so this is byte-identical to the old point check.
        List<Vector2Int> FindClearPath(Bus bus, ParkingSlot slot, bool ignoreReserved)
        {
            Vector2Int start = bus.cell;
            Vector2Int goal = WorldToCell(ParkingWorld(slot.index));
            int xMin = Mathf.Min(0, Mathf.Min(start.x, goal.x)) - bus.length - 1;
            int xMax = Mathf.Max(gridW - 1, Mathf.Max(start.x, goal.x)) + bus.length + 1;
            int yMin = Mathf.Min(goal.y, start.y) - bus.length - 1;
            int yMax = Mathf.Max(gridH - 1, start.y) + bus.length + 1;

            bool Walk(Vector2Int c)
            {
                if (c == start || c == goal) return true;            // endpoints (own start footprint + clear apron)
                foreach (var fc in LevelGenerator.OccCells(c, bus.dir, bus.length))
                {
                    if (occ.TryGetValue(fc, out var ob) && ob != bus) return false;                          // jammed vehicle
                    if (!ignoreReserved && reservedByMoving.TryGetValue(fc, out var rb) && rb != bus) return false; // another live corridor
                    Vector3 w = CellWorld(fc);
                    if (Mathf.Abs(w.x) > VisHalfW(w.z) - 0.35f) return false; // every footprint cell on-screen
                }
                return true;
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
                if (cur == goal)
                {
                    var cells = new List<Vector2Int> { cur };
                    while (came.ContainsKey(cur)) { cur = came[cur]; cells.Add(cur); }
                    cells.Reverse(); // start ... goal
                    return cells;
                }
                closed.Add(cur);
                foreach (var d in steps)
                {
                    var n = cur + d;
                    if (n.x < xMin || n.x > xMax || n.y < yMin || n.y > yMax) continue;
                    if (closed.Contains(n) || !Walk(n)) continue;
                    if (d.x != 0 && d.y != 0 &&                       // footprint-aware corner-sweep (ca/cb = SlideClear convention)
                        (!Walk(new Vector2Int(cur.x + d.x, cur.y)) || !Walk(new Vector2Int(cur.x, cur.y + d.y)))) continue;
                    float tg = gScore[cur] + ((d.x != 0 && d.y != 0) ? 1.41421356f : 1f);
                    if (!gScore.TryGetValue(n, out float gn) || tg < gn)
                    {
                        came[n] = cur; gScore[n] = tg; fScore[n] = tg + Heur(n, goal);
                        if (!open.Contains(n)) open.Add(n);
                    }
                }
            }
            return null; // no clear on-screen path -> caller waits or falls back
        }

        static float Heur(Vector2Int a, Vector2Int b) { float dx = a.x - b.x, dy = a.y - b.y; return Mathf.Sqrt(dx * dx + dy * dy); }
        Vector2Int WorldToCell(Vector3 w) => new Vector2Int(
            Mathf.RoundToInt(w.x / CellSize + (gridW - 1) * 0.5f), Mathf.RoundToInt((GridExitZ - w.z) / CellSize));
        Vector3 CellWorld(Vector2Int c) => new Vector3((c.x - (gridW - 1) * 0.5f) * CellSize, 0, GridExitZ - c.y * CellSize);

        // Visible half-width (world units) at ground depth z, for a tall portrait (aspect 0.462).
        // Tied to PlaceCamera (pos 0,21.2,-8.99 / target 0,0,3.2 / FOV 54). The +6.0 vs the old 13.867 is the
        // camera pulled back 6.0u along the view axis (the ~25% zoom-out). Keep in sync if the camera changes.
        static float VisHalfW(float z) => (19.867f + 0.4983f * (z + 6f)) * 0.2255f; // 0.2255 = aspect*tan(FOV/2) at FOV 52 (was 0.2356 at FOV 54)

        // Clamp a waypoint's X so the whole body (+ spline bow, ~margin) stays inside the camera frustum at depth z.
        static Vector3 OnScreenX(Vector3 p, float margin)
        {
            float h = Mathf.Max(0.2f, VisHalfW(p.z) - margin);
            return new Vector3(Mathf.Clamp(p.x, -h, h), p.y, p.z);
        }

        // Cell chain -> world waypoints, dropping the start cell (DrivePath prepends the bus's pos) and collinear runs.
        List<Vector3> CellsToWorld(List<Vector2Int> cells)
        {
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

            // (Economy rework) No per-passenger coin trickle anymore — it was tied to passenger count and ballooned
            // the balance (~15k by L20). Coins are granted ONCE at level end (see LevelReward). Golden still counts
            // toward a small capped end-of-level bonus and keeps its juicy burst.
            if (golden)
            {
                goldenThisLevel++;
                sfx.Coin();
                Juice.Burst(this, boardRoot, pos + Vector3.up * 0.6f, goldMat, 14, 4.2f);
            }
            else sfx.Board();
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
            var exhaust = SpawnExhaust(bus); // T5: exhaust trail as the full bus pulls away

            // Drive the FULL-SIZE bus FLAT (grounded). Parked buses face +Z (nose toward the people band), so
            // leaving is a real maneuver: BACK UP a little out of the stop, then sweep onto the road and cruise
            // off-screen as ONE smooth, rounded drive (the spline steers the nose gradually, no 90° snap). The
            // road lane sits above the jam, so the bus never drives through the jam.
            float side = start.x >= 0f ? 1f : -1f;                                  // exit the closer side
            Vector3 backUp = new Vector3(start.x, start.y, ParkingZ - 1.2f);        // reverse a little (faces +Z, so a -Z move reads as backing up)
            yield return MoveTo(bus.transform, backUp, 0.35f);                       // back up a little out of the stop
            // T3 (BONUS only): a FULL bus leaving is automatic, NOT a player skill check — so it WAITS for a clear
            // gap in the cross-traffic before merging (it never crashes). Generous timeout so it can't ever hang.
            if (IsBonus)
            {
                float gapWait = 0f;
                yield return new WaitUntil(() => bus == null || state != GameState.Playing ||
                    RoadClearAt(start.x, gameSettings.trafficClearance) || (gapWait += Time.deltaTime) > 3f);
                if (bus == null || state != GameState.Playing) { busy--; yield break; }
            }
            yield return DrivePath(bus.transform, new List<Vector3> {
                new Vector3(start.x + side * 1.4f, start.y, RoadZ),                  // sweep onto the road toward the exit side
                new Vector3(side * 14f, start.y, RoadZ),                            // cruise off-screen along the road
            }, gameSettings.busLeaveSpeed, gameSettings.turnSmoothness);

            StopExhaust(exhaust, true); // T5: bus is about to be Destroyed -> detach the trail to boardRoot + self-destruct
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
            if (visible.Count == 0 && nextGroupIndex >= groups.Count) { if (IsBonus) FinishBonus(true); else Win(); return; }
            if (visible.Count == 0) return;

            // The front passenger can board one of the parked buses -> keep playing.
            if (FindParkedBus(visible[0].color) != null) return;

            // There is still an OPEN (unlocked & empty) parking slot, so the player can place
            // another bus that might match -> parking is NOT full yet, so this is not a deadlock.
            if (FirstFreeSlot() != null) return;

            // On a BONUS level a deadlock (stuck: parking full, front passenger matches nothing) = FAILED.
            if (IsBonus) { FinishBonus(false); return; }

            // Otherwise (normal level): the front passenger matches NO parked bus AND the parking is full.
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
            if (slot.adUnlock) { WatchAdToUnlock(slot); return; }   // ad pad: open by watching a rewarded ad
            if (!Spend(SlotUnlockCost)) { sfx.Error(); StartCoroutine(Bump(slot.transform)); return; }
            slot.Unlock();
            sfx.Coin();
            TryStartBoardingPump();
        }

        // AD-unlock pad. No rewarded-ad SDK is wired, so this grants the open immediately as a placeholder
        // "ad reward". To ship a REAL ad: show the rewarded ad here and call DoAdUnlock(slot) ONLY from its
        // reward callback (and an Error()/Bump on dismiss-without-reward).
        // TODO: integrate a rewarded-ad SDK (Unity Ads / AdMob) and gate DoAdUnlock behind the reward.
        void WatchAdToUnlock(ParkingSlot slot)
        {
            var ad = AdManager.Instance;
            if (ad != null)
                ad.ShowRewarded("padunlock",
                    onReward: () => DoAdUnlock(slot),                                       // unlock ONLY on a completed rewarded ad
                    onClosedNoReward: () => { sfx.Error(); StartCoroutine(Bump(slot.transform)); });
            else DoAdUnlock(slot); // no AdManager (degenerate) -> grant so the pad isn't permanently stuck
        }

        void DoAdUnlock(ParkingSlot slot)
        {
            if (slot == null || !slot.locked) return;
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
            if (tutorialActive && tutorialStep == 3) EndTutorial(); // (#6) used the free joker -> drop the coach

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
            // success click handled globally by UiClickSound (button press)
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
            MusicManager.PlayTheme(theme.name); // per-theme background music (night themes -> night track)
            BuildSlots();
            BuildGrid();
            BuildPeopleLeftSign();
            BuildLine();

            earnedThisLevel = 0; combo = 0; maxCombo = 0; goldenThisLevel = 0; pendingReward = 0; lastBoardTime = -10f;
            continueCount = 0; // reset escalating continue price each level

            state = GameState.Playing;
            StartCoroutine(LineLayoutLoop()); // continuous queue re-spacing for the duration of the level
            ui.ShowHud();
            if (IsBonus)
            {
                // Night traffic-dodge bonus: start the 60s countdown + spawn the pooled cross-traffic (T2).
                crashedThisBonus = false;
                bonusTimeLeft = BonusTime;
                ui.SetBonusCountdown(bonusTimeLeft);
                BuildTraffic();
                StartCoroutine(TrafficLoop());
            }
            else ui.HideBonusCountdown();
            ui.SetLevel(levelNumber);
            ui.SetTheme(theme.name);
            ui.SetCoins(SaveSystem.Coins);
            ui.RefreshJokerLocks();  // unlock RECOLOR/SWAP/HELI as SaveSystem.Level rises
            UpdatePeopleLeft(); // initial total (this level's real people count, not the visible window)
            LevelStarted?.Invoke(levelNumber);
            CheckEnd(); // detect an immediately-stuck board (no-op normally: free slots exist at start)

            // (#4/#5) First-time coaches: level 1 teaches the core loop; level 10 introduces the bonus round.
            if (levelNumber == 1)
                StartTutorial(Loc.T("Tap a bus to send it to a parking spot!"),
                              Loc.T("Same-color passengers board automatically. Clear them all to win!"));
            else if (levelNumber == 5)
                StartJokerTutorial();   // (#6) RECOLOR unlocks at level 5 -> grant 1 free + coach how to use it
            else if (levelNumber == 10)
                StartTutorial(Loc.T("Bonus round! Clear every bus before the timer runs out!"),
                              Loc.T("Watch out — crossing cars can crash a moving bus!"));
            else { tutorialActive = false; if (coach != null) coach.Hide(); }
        }

        void Teardown()
        {
            StopAllCoroutines();
            Juice.ClearAllPunches(); // drop punch state left by hard-stopped coroutines (no cross-level leak)
            busy = 0; pumpRunning = false; pumpDirty = false;
            occ.Clear(); reservedByMoving.Clear(); gridDriver = null; gridBuses.Clear(); liveBuses.Clear(); visible.Clear(); slots = null;
            if (sfx != null) sfx.SetEngine(false); // kill the engine loop across a level rebuild
            traffic.Clear(); // T2: drop pooled traffic refs (the cars themselves die with boardRoot below)
            peopleLeftSign = null; // destroyed with boardRoot below; drop the stale ref (no cross-level leak)
            if (boardRoot != null) Destroy(boardRoot.gameObject);
            boardRoot = null;
        }

        void Win()
        {
            state = GameState.Win;
            EndTutorial(); // (#4) drop the level-1 coach before the success panel
            // No timer anymore — stars reward boarding flow (best combo streak this level).
            int stars = maxCombo >= 8 ? 3 : (maxCombo >= 4 ? 2 : 1);
            // Level progression is locked in now; the actual coin reward is granted
            // by the success panel (CLAIM = 20, WATCH AD x2 = 40) via ClaimWinReward.
            SaveSystem.Level = Mathf.Max(SaveSystem.Level, currentLevel + 1);
            SaveSystem.BestLevel = currentLevel;
            sfx.Win();
            ui.HideHud();
            ConfettiFromCorners(); // confetti shoots UP from the bottom-left & bottom-right corners
            pendingReward = LevelReward(stars, false);
            LevelCompleted?.Invoke(pendingReward, stars);
            ui.ShowSuccess(stars, pendingReward); // white box + ★s + claim (= reward) / watch-ad (= 2x reward)
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
            EndTutorial(); // (#4) drop the level-1 coach before the continue/fail panel
            sfx.Lose();
            ui.HideHud();
            ui.SetContinuePrice(CurrentContinueCost); // 150, then doubles each continue
            ui.ShowContinue(); // runtime Continue panel (decline -> Failed). GameManager is neutralized to avoid a 2nd panel.
            LevelFailed?.Invoke(reason);
            OnGameOver?.Invoke(reason);
            // The Continue panel (ui.ShowContinue) now owns the loss flow — no auto-retry.
        }

        // ---- (#4) Level-1 tutorial: coach the tap -> park -> board loop -----------------------------------
        void StartTutorial(string step1, string step2)
        {
            if (coach == null) { coach = gameObject.AddComponent<TutorialCoach>(); coach.Build(); }
            tutorialActive = true;
            tutorialStep = 1;
            tutStep2 = step2;
            coach.ShowText(step1);
            StartCoroutine(TutorialLoop());
        }

        IEnumerator TutorialLoop()
        {
            // Step 1: keep the pulsing ring parked over a bus that CAN actually exit (clear lane + free slot),
            // re-evaluated each frame as the board changes, so the player's first tap is guaranteed to succeed.
            while (tutorialActive && tutorialStep == 1 && state == GameState.Playing)
            {
                Bus target = FindMovableBus();
                if (target != null && cam != null)
                {
                    Vector3 sp = cam.WorldToScreenPoint(target.transform.position);
                    if (sp.z > 0f) coach.PointAt(new Vector2(sp.x, sp.y)); else coach.HidePointer();
                }
                else coach.HidePointer();
                yield return null;
            }
        }

        void AdvanceTutorialOnFirstMove()
        {
            if (!tutorialActive || tutorialStep != 1) return;
            tutorialStep = 2;
            coach.HidePointer();
            coach.ShowText(tutStep2);
            StartCoroutine(EndTutorialAfter(5f));
        }

        IEnumerator EndTutorialAfter(float delay)
        {
            float t = 0f;
            while (t < delay && tutorialActive && state == GameState.Playing) { t += Time.unscaledDeltaTime; yield return null; }
            EndTutorial();
        }

        void EndTutorial()
        {
            tutorialActive = false;
            if (coach != null) coach.Hide();
        }

        // First jam bus (if any) whose straight lane is clear AND has a free slot — i.e. a tap that will PARK it.
        // Mirrors the success branch of TryTapBus so the coached tap can never be a dud.
        Bus FindMovableBus()
        {
            foreach (var bus in gridBuses)
            {
                if (bus == null || bus.state != BusState.Queued) continue;
                System.Func<Vector2Int, bool> blocked = c => Blocked(c, bus);
                if (LevelGenerator.SlideClear(bus.cell, bus.dir, bus.length, blocked, gridW, gridH)
                    && NearestFreeSlot(GridWorldCenter(bus.cell, bus.dir, bus.length).x) != null)
                    return bus;
            }
            return null;
        }

        // ---- (#6) Joker-unlock coach + the one-time mandatory free joker --------------------------------
        void StartJokerTutorial()
        {
            if (coach == null) { coach = gameObject.AddComponent<TutorialCoach>(); coach.Build(); }
            // Mandatory free joker: grant ONE Recolor the first time it unlocks, so the player can try it for free.
            if (!SaveSystem.FreeJokerGranted) { SaveSystem.AddFreeJoker(0, 1); SaveSystem.FreeJokerGranted = true; ui.RefreshJokerLocks(); }
            tutorialActive = true;
            tutorialStep = 3; // distinct from the bus steps (1/2) so a bus tap can't advance it
            coach.ShowText(Loc.T("RECOLOR unlocked — here's 1 free! Tap it to reshuffle the buses' colours when stuck."));
            StartCoroutine(JokerTutorialLoop());
        }

        IEnumerator JokerTutorialLoop()
        {
            // The joker button is fixed on the HUD: keep the pulsing ring on it until the player has had time to try it.
            float t = 0f;
            while (tutorialActive && tutorialStep == 3 && state == GameState.Playing && t < 12f)
            {
                Vector2 jp = ui.JokerScreenPos(0);
                coach.PointAt(new Vector2(jp.x, jp.y + 130f)); // hover the ring just ABOVE the joker so it doesn't blend with the icon
                t += Time.unscaledDeltaTime;
                yield return null;
            }
            if (tutorialStep == 3) EndTutorial();
        }

        // ---- Bonus night-mode timer + soft end (every 10th level) -----------------------------------------
        void TickBonusTimer()
        {
            bonusTimeLeft -= Time.deltaTime;
            ui.SetBonusCountdown(bonusTimeLeft);
            if (bonusTimeLeft <= 0f) FinishBonus(false); // ran out of time -> the FAILED panel
        }

        // The ONE bonus completion path (guarded so it fires exactly once). inTime = solved before 0:00 -> SUCCESS
        // panel (advances on CLAIM); !inTime = timed out OR stuck -> FAILED panel (RETRY replays the bonus, no advance).
        void FinishBonus(bool inTime)
        {
            if (state != GameState.Playing) return;
            ui.HideBonusCountdown();
            EndTutorial(); // (#5) drop the bonus coach before the success/fail panel
            if (inTime)
            {
                // SUCCESS — lock progression + the win panel (CLAIM/AD grants the reward, then NextLevel).
                state = GameState.Win;
                SaveSystem.Level = Mathf.Max(SaveSystem.Level, currentLevel + 1);
                SaveSystem.BestLevel = currentLevel;
                sfx.Win();
                ui.HideHud();
                ConfettiFromCorners();
                int stars = crashedThisBonus ? 2 : 3;             // a clean (no-crash) run earns the full 3 stars
                pendingReward = LevelReward(stars, true);
                LevelCompleted?.Invoke(pendingReward, stars);
                ui.ShowSuccess(stars, pendingReward);
            }
            else
            {
                // FAILED (time up / stuck) — the Failed panel: RETRY replays the bonus, HOME -> menu. No progression.
                state = GameState.Lose;
                sfx.Lose();
                ui.HideHud();
                LevelFailed?.Invoke("Bonus failed");
                OnGameOver?.Invoke("Bonus failed");
                ui.ShowFailed();
            }
        }

        // ====================================================================
        // Build
        // ====================================================================
        void BuildSlots()
        {
            slots = new ParkingSlot[totalSlots];
            // Unlock EXACTLY baseSlots pads (== the BuildQueue servability window); lock the rest at the
            // edges so the open pads are central and the player unlocks outward. Of the locked pads, the
            // FIRST opens by watching an AD; the rest open with COINS (e.g. 4 open + 1 ad + 2 coin = 7).
            int lockCount = Mathf.Max(0, totalSlots - level.baseSlots);
            int leftLocks = lockCount / 2;
            int rightStart = totalSlots - (lockCount - leftLocks);
            for (int i = 0; i < totalSlots; i++)
            {
                var pad = GameObject.CreatePrimitive(PrimitiveType.Cube);
                pad.name = "Slot" + i;
                pad.transform.SetParent(boardRoot, false);
                pad.transform.position = new Vector3(SlotX(i), -0.05f, ParkingZ);
                pad.transform.localScale = new Vector3(SlotSpacing * 0.84f, 0.1f, 2.4f); // longer bay (fits the bus length); centred on ParkingZ → spans ~8.5–10.9, still clears the road band (7.9) and fence (11.4)
                var padRend = pad.GetComponent<Renderer>();
                padRend.sharedMaterial = slotMat;
                padRend.enabled = false; // hide the box — the bay is shown ONLY by painted lane lines (BuildParkingStripes); the collider + ParkingSlot stay for taps/parking

                var slot = pad.AddComponent<ParkingSlot>();
                slot.index = i;
                slot.locked = (i < leftLocks) || (i >= rightStart); // central pads unlocked
                slot.adUnlock = slot.locked && (i >= totalSlots - 2); // the 2 RIGHTMOST locked pads open by rewarded ad; the rest by coins
                slots[i] = slot;

                if (slot.locked)
                {
                    // (#3) The "+" cross-bar marker was removed — the animated coin / video-ad ICON (below) is the
                    // indicator now. The marker GameObject stays (empty) to host the IdleBob pulse + the icon.
                    var marker = new GameObject(slot.adUnlock ? "AdLock" : "CoinLock");
                    marker.transform.SetParent(pad.transform, false);
                    marker.transform.localPosition = new Vector3(0, 0.7f, 0);
                    var pulse = marker.AddComponent<IdleBob>();
                    pulse.scalePulse = true; pulse.scaleAmp = 0.12f; pulse.speed = 3f; pulse.amp = 0f;
                    slot.lockMarker = marker;
                    // Billboard ICON so the player sees HOW to open it: a coin on the gold pads, a video-ad icon on
                    // the rewarded-ad pads. Coin pads ALSO show the price number ("80") underneath. Parented to the
                    // marker -> removed on Unlock.
                    BuildSlotIcon(marker.transform, slot.adUnlock ? UIKit.WatchAd() : UIKit.Coin());
                    if (!slot.adUnlock) BuildSlotCostNumber(marker.transform, SlotUnlockCost.ToString()); // (#2) "80" under the coin
                }
            }
            BuildParkingStripes(); // paint lane-marking stripes between the parking bays
        }

        // Painted parking-bay markings (no raised boxes): a thin white line down each bay boundary (left edge,
        // between bays, right edge) running the bay's depth, plus one line across the head of the row. Rebuilt with
        // the slots each level; the (now hidden) slot cubes still carry the ParkingSlot + tap collider.
        void BuildParkingStripes()
        {
            if (stripeMat == null) return;
            const float y = 0.01f, h = 0.04f, w = 0.10f, depth = 2.3f;
            float half = SlotSpacing * 0.5f;
            for (int i = 0; i <= totalSlots; i++)                              // side lines: a divider at every bay edge
            {
                float x = SlotX(0) - half + i * SlotSpacing;
                LowPolyBuilder.Slab(boardRoot, new Vector3(x, y, ParkingZ), new Vector3(w, h, depth), stripeMat);
            }
            float rowW = totalSlots * SlotSpacing;
            LowPolyBuilder.Slab(boardRoot, new Vector3(0f, y, ParkingZ + depth * 0.5f), new Vector3(rowW + w, h, w), stripeMat); // head line across the back
            for (int i = 0; i < totalSlots; i++)                              // a "P" per bay — HIDDEN until the bay is unlocked
            {
                var p = BuildBayLetterP(SlotX(i));
                if (slots[i] != null) { p.SetActive(!slots[i].locked); slots[i].letterP = p; }
            }
        }

        // A "P" marker in one parking bay. It billboards to the (fixed, steep top-down) camera — the SAME proven
        // orientation the slot icons use (-X cancels the billboard mirror), so it always reads correctly and looks
        // nearly flat at this camera angle. Created HIDDEN on locked bays and revealed by ParkingSlot.Unlock() when
        // the player opens the bay (ad / gold); also naturally hidden under a bus while one is parked there.
        GameObject BuildBayLetterP(float x)
        {
            var go = new GameObject("BayP", typeof(RectTransform), typeof(Canvas));
            go.transform.SetParent(boardRoot, false);
            go.GetComponent<Canvas>().renderMode = RenderMode.WorldSpace;
            ((RectTransform)go.transform).sizeDelta = new Vector2(100, 100);
            go.transform.position = new Vector3(x, 0.15f, ParkingZ);
            go.transform.localScale = new Vector3(-1f, 1f, 1f) * (0.85f / 100f); // -X cancels the BillboardUp mirror
            go.AddComponent<BillboardUp>();
            AddSignText(go.transform, "P", 90, Vector2.zero, Vector2.one, new Color(0.95f, 0.95f, 0.9f));
            return go;
        }

        // Small camera-facing ICON (no text) above a locked pad's marker: a coin (gold pads) or a video-ad icon (ad pads).
        void BuildSlotIcon(Transform parent, Sprite sprite)
        {
            if (sprite == null) return;
            var go = new GameObject("SlotIcon", typeof(RectTransform), typeof(Canvas));
            go.transform.SetParent(parent, false);
            go.GetComponent<Canvas>().renderMode = RenderMode.WorldSpace;
            ((RectTransform)go.transform).sizeDelta = new Vector2(120, 120);
            go.transform.localPosition = new Vector3(0, 0.55f, 0);
            go.transform.localScale = new Vector3(-1f, 1f, 1f) * (0.62f / 120f); // -X cancels the BillboardUp flip
            go.AddComponent<BillboardUp>();
            var iconGo = new GameObject("Icon", typeof(RectTransform), typeof(UnityEngine.UI.Image));
            iconGo.transform.SetParent(go.transform, false);
            var rt = (RectTransform)iconGo.transform;
            rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one; rt.offsetMin = Vector2.zero; rt.offsetMax = Vector2.zero;
            var img = iconGo.GetComponent<UnityEngine.UI.Image>();
            img.sprite = sprite; img.raycastTarget = false; img.preserveAspect = true;
        }

        // (#2) Small camera-facing cost number under a coin pad's icon (e.g. "80"), so the price is clear/visible.
        void BuildSlotCostNumber(Transform parent, string text)
        {
            var go = new GameObject("SlotCost", typeof(RectTransform), typeof(Canvas));
            go.transform.SetParent(parent, false);
            go.GetComponent<Canvas>().renderMode = RenderMode.WorldSpace;
            ((RectTransform)go.transform).sizeDelta = new Vector2(120, 70);
            go.transform.localPosition = new Vector3(0, 0.05f, 0); // just below the coin icon (icon sits at y 0.55)
            go.transform.localScale = new Vector3(-1f, 1f, 1f) * (0.62f / 120f); // match the icon's billboard scale
            go.AddComponent<BillboardUp>();
            AddSignText(go.transform, text, 72, Vector2.zero, Vector2.one, new Color(1f, 0.85f, 0.25f));
            var bob = go.AddComponent<IdleBob>(); bob.amp = 0.08f; bob.speed = 3f; bob.phase = 1.5f; // (#10) the gold price bobs on its own rhythm so it reads as "tap to unlock"
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
                OutlineAll(person); // toon ink edge on the background crowd figure
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
            OutlineAll(go); // toon ink edge on the boarding person (imported skinned OR code fallback)
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
            liveBuses.Add(bus); // track for the engine-sound movement scan (cleared on level teardown)

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
                // (Removed) the "«N" advance badge that floated on the roof — advanceN still works in gameplay, it's just no longer shown.
            }
            root.transform.localScale = Vector3.one * gameSettings.vehicleSize; // editable vehicle-size multiplier (both render paths)
            OutlineAll(root); // toon ink edge on the vehicle body + roof markers (before headlights so the glowing lenses stay clean)
            if (nightMode) AttachHeadlights(root.transform, LowPolyBuilder.VehicleLength(type, CellSize) * 0.5f, false); // T4: night headlights (no real spot on the jam vehicles)
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

            // Span the vehicle's grid footprint: CellLength cells (Car 1 / Bus 2).
            float target = Vehicles.CellLength(type) * CellSize * vehicleCatalog.fitFactor;

            var rends = model.GetComponentsInChildren<Renderer>(true); // include inactive for first-frame consistency (matches the mesh queries)
            float span = target, wid = target * 0.5f, roofY = CellSize * 0.5f;

            // Measure the model in its OWN LOCAL frame (from mesh bounds), NOT a world AABB: a world AABB
            // inflates for a 45deg-yawed (diagonal) body, which would make diagonal vehicles a DIFFERENT size
            // than straight ones. Local measurement -> every bus is identical regardless of direction.
            Bounds lb = default; bool localFrame = false;
            // include INACTIVE meshes: on the FIRST frame (this build runs in Start) the model's mesh children
            // aren't active/registered yet, so an active-only query finds NONE -> localFrame stays false -> the
            // body is never re-centered onto the root -> the centered arrow sits a bit LEFT until a level reload
            // rebuilds it on a later frame. Including inactive makes it identical first-build and reload.
            foreach (var mf in model.GetComponentsInChildren<MeshFilter>(true))
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

            // Roof-marker height. Use the MESH-derived roofY (frame-independent), NOT Renderer.bounds: on the
            // FIRST frame of Play, Renderer.bounds is still zero/stale (Unity hasn't run a cull pass yet), which
            // put the arrow + heads + badge + tap-box at the wrong height on level 1 until a Replay rebuilt them
            // on a later frame ("buses look funny on Play, Replay fixes it"). roofY already == the scaled model
            // height with the base sitting at y=0, so it IS the true top — and matches Renderer.bounds when valid.
            float topY = roofY;

            // Tappable box (the prefab's colliders were stripped).
            var box = root.gameObject.AddComponent<BoxCollider>();
            box.center = new Vector3(0, topY * 0.5f, 0);
            box.size = new Vector3(Mathf.Max(wid, CellSize * 0.4f), Mathf.Max(topY, 0.5f), span);

            // Clean, symmetric arrow at the nose; cute heads pop in behind it as people board.
            BuildRoofArrow(root, topY, wid * 0.5f, span);
            bus.roofPeople = BuildRoofHeads(root, capacity, color, topY, wid * 0.5f, span);

            // (Removed) the "«N" advance/crawler badge that floated on the roof — advanceN still works in gameplay, it's just no longer drawn.
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
            float signW = 0.82f, signH = 1.22f;   // (#8) ~1.5x bigger people-count sign (board, neon frame + text all derive from these)
            float frameHalf = (signW + 0.14f) * 0.5f;
            // Just LEFT of the first (leftmost) bus stop, but CLAMPED so the whole sign stays on-screen
            // even when a level has many parking slots (SlotX(0) can run off the left edge otherwise).
            float sz = ParkingZ - 1.4f;   // pulled FORWARD of the bus-stop props (z≈9) so it isn't occluded behind one
            float sx = Mathf.Max(SlotX(0) - 0.85f, -(VisHalfW(sz) - frameHalf - 0.08f)); // clamp on-screen at the new depth
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

        // A floating damage-style number (e.g. a red "-3" at a bonus crash): world-space canvas that BillboardUps to
        // the camera, then RISES + FADES and self-destructs, so the player clearly sees the time penalty land.
        void SpawnPenaltyText(Vector3 worldPos, string text, Color color)
        {
            var go = new GameObject("PenaltyText", typeof(RectTransform), typeof(Canvas), typeof(CanvasGroup));
            go.transform.SetParent(boardRoot, false);
            go.GetComponent<Canvas>().renderMode = RenderMode.WorldSpace;
            ((RectTransform)go.transform).sizeDelta = new Vector2(120, 80);
            go.transform.position = worldPos + Vector3.up * 1.2f;
            go.transform.localScale = new Vector3(-1f, 1f, 1f) * (0.9f / 120f); // -X cancels the BillboardUp flip
            go.AddComponent<BillboardUp>();
            AddSignText(go.transform, text, 80, Vector2.zero, Vector2.one, color);
            StartCoroutine(FloatAndFade(go.transform, go.GetComponent<CanvasGroup>(), 1.1f));
        }

        static IEnumerator FloatAndFade(Transform t, CanvasGroup cg, float dur)
        {
            if (t == null) yield break;
            Vector3 from = t.position;
            float e = 0f;
            while (e < dur)
            {
                if (t == null) yield break;
                e += Time.deltaTime;
                float k = Mathf.Clamp01(e / dur);
                t.position = from + Vector3.up * (k * 1.6f);   // rise
                if (cg != null) cg.alpha = 1f - k;             // fade (whole canvas, incl. outline)
                yield return null;
            }
            if (t != null) Destroy(t.gameObject);
        }

        // Clean, SYMMETRIC roof arrow (a triangular head + shaft) centered on x=0, pointing local -Z (the exit
        // dir), flat on the roof front. Used on the imported path; the code-built path has its own.
        void BuildRoofArrow(Transform root, float topY, float halfWidth, float span)
        {
            // Clear arrow pointing -Z: a SOLID TRIANGULAR head + a shaft (reads as an arrow from the top-down
            // camera, unlike the old ambiguous 45° "diamond"). Lives in the front ~third of the roof.
            float y = topY + 0.06f;
            float frontZ = -span * 0.5f;
            float headW = Mathf.Clamp(halfWidth * 1.6f, 0.18f, 0.5f);   // wide head -> unmistakably an arrowhead
            float headL = Mathf.Clamp(span * 0.20f, 0.14f, 0.4f);
            float shaftLen = Mathf.Clamp(span * 0.18f, 0.12f, 0.36f);
            float tipZ = frontZ + 0.05f;                                // tip just inside the nose

            var head = new GameObject("ArrowHead");
            head.transform.SetParent(root, false);
            head.transform.localPosition = new Vector3(0, y, tipZ + headL * 0.5f);  // tip at tipZ (-Z)
            head.transform.localScale = new Vector3(headW, 1f, headL);
            head.AddComponent<MeshFilter>().sharedMesh = LowPolyBuilder.ArrowHeadMesh();
            head.AddComponent<MeshRenderer>().sharedMaterial = arrowMat;

            var shaft = MakeCube(root, arrowMat, new Vector3(headW * 0.32f, 0.05f, shaftLen));
            shaft.transform.localPosition = new Vector3(0, y, tipZ + headL + shaftLen * 0.5f);
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
            // Head diameter keyed off BOTH spacings so heads never overlap (dense Bus = 10 seats).
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
            foreach (var mf in model.GetComponentsInChildren<MeshFilter>(true)) // include inactive (first-frame meshes may be inactive) so the auto-face decides identically first-build and reload
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
            const float doorX = 3.5f, horizZ = PeopleZ, vGap = 0.7f, hSpacing = 0.9f; // queue sits at the people line (BEHIND the fence), tracks PeopleZ
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

        // Re-pick the bay nearest to a bus's ON-ROAD x. Considers every FREE bay PLUS the one it already reserved
        // (`held`), so it never loses its guaranteed spot, and swaps the reservation only if a free bay is strictly
        // closer. Atomic (no yield before the swap), so concurrent exits can never double-book a bay.
        ParkingSlot NearestSlotToRoad(Bus bus, ParkingSlot held, float x)
        {
            ParkingSlot best = held;
            float bd = Mathf.Abs(SlotX(held.index) - x);
            foreach (var s in slots)
                if (s.IsFree) { float d = Mathf.Abs(SlotX(s.index) - x); if (d < bd) { bd = d; best = s; } }
            if (best != held)
            {
                held.occupant = null;                            // release the bay we reserved at tap time
                best.occupant = bus; bus.slotIndex = best.index; // claim the closer one
            }
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
            stripeMat    = MaterialLibrary.MakeRuntime(new Color(0.93f, 0.93f, 0.86f), 0.10f);       // white-cream paint for the parking-bay lane markings
            neonMat      = MaterialLibrary.MakeRuntime(new Color(0.12f, 1f, 0.70f), 0.5f, 1.7f);      // emissive neon (glows under bloom) for the people-left sign
            headlightMat = MaterialLibrary.MakeRuntime(new Color(1f, 0.97f, 0.86f), 0.5f, 1.6f);       // #5: warm emissive headlight lens — SOFTER glow (was 3.0, looked harsh/blown-out on bonus levels)
            var beamShader = Shader.Find("Sprites/Default");                                          // URP-safe translucent (never magenta), like the smoke fix
            if (beamShader != null)
            {
                // #6: beam tint at FULL alpha — the actual fade comes from per-vertex alpha on the cone mesh
                // (bright at the lamp, transparent at the far end), so the spill reads soft + realistic, not a flat box.
                beamMat = new Material(beamShader) { color = new Color(1f, 0.96f, 0.84f, 1f) };    // full spill — MOVING traffic on the dark road
                beamMatDim = new Material(beamShader) { color = new Color(1f, 0.96f, 0.84f, 0.45f) }; // faint spill — the bumper-to-bumper JAM (otherwise 30 fans = gray clutter)
                // Soft warm halo around each lens (additive-looking via Sprites/Default alpha) so a lamp reads as a
                // glowing core with falloff, not a hard dot. Subtle on purpose; bloom does the rest.
                lampGlowMat = new Material(beamShader) { color = new Color(1f, 0.95f, 0.82f, 0.22f) };
            }
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
            if (cam != null)
            {
                RenderSettings.skybox = null;
                cam.clearFlags = CameraClearFlags.SolidColor;
                cam.backgroundColor = MaterialLibrary.Mute(th.sky);   // T2: quieter sky so gameplay pops (separate from any post-FX grade)
            }
            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
            RenderSettings.ambientLight = MaterialLibrary.Mute(th.ambient);   // T2: muted (desaturated) env fill, separate from any post-FX grade
            var sun = Object.FindAnyObjectByType<Light>();
            if (sun != null && sun.type == LightType.Directional)
            {
                sun.color = th.lightColor;                                  // warm/cool key per theme
                sun.intensity = th.lightIntensity * 1.2f;                   // stronger key = crisper, less-flat shading
                sun.transform.rotation = Quaternion.Euler(52f, -34f, 0f);   // pleasant diagonal so shadows read on the top-down framing
                sun.shadows = lowEnd ? LightShadows.None : LightShadows.Soft; // budget phones skip the shadowmap pass (objects still read grounded on the ground slab)
                sun.shadowStrength = 0.55f;                                 // soft, not pitch-black
            }

            // T4: distance fog on DARK themes only (Night/Bonus); EXPLICITLY off on every bright theme so the
            // global RenderSettings.fog state can't linger after switching away from a dark level. (URP honors
            // legacy RenderSettings.fog — no Volume override needed; the Cody Dreams SS-fog pack shipped broken.)
            bool darkTheme = th.name == "Night" || th.name == "Bonus";
            nightMode = darkTheme; // T4: gate board + traffic headlights on the dark themes
            if (darkTheme)
            {
                // Fog runs on EVERY device now (legacy Linear fog is ~free); night-tinted to match the dark sky.
                RenderSettings.fog = true;
                RenderSettings.fogMode = FogMode.Linear;
                RenderSettings.fogColor = new Color(0.10f, 0.12f, 0.22f);
                RenderSettings.fogStartDistance = 12f;
                RenderSettings.fogEndDistance = 42f;
            }
            else RenderSettings.fog = false;

            // T1: deepen the global grade on dark themes (lower exposure + stronger vignette) so the gold accent
            // and headlights pop at night; restore the bright-theme values otherwise. postCA/postVig are cached
            // once in SetupPostFX — the single Volume is NEVER rebuilt per level.
            if (postCA != null) postCA.postExposure.Override(darkTheme ? -0.55f : 0.12f);
            if (postVig != null) postVig.intensity.Override(darkTheme ? 0.42f : 0.24f);

            // Editable per-theme env material assets (Resources/Materials/<Theme>_<Type>), else runtime fallback.
            // smoothness/emission here MATCH MaterialLibrary.ThemeTypes so the fallback looks like the asset.
            // (#3) BOTH ground bands now carry the theme so levels look distinct (was: dark-forced ground + a GRAY
            // jam every theme → "minor variations"). Dark themes (Night/Bonus) stay moody; bright themes show their
            // hue. Kept muted + mid-value so the colourful, ink-outlined buses still pop against the jam.
            Color.RGBToHSV(th.ground, out float gh, out float gs, out _);                                     // PEOPLE area (backdrop)
            Material ground      = MaterialLibrary.MakeRuntimeMuted(Color.HSVToRGB(gh, gs, darkTheme ? 0.34f : 0.60f), 0.35f, 0f);
            Color.RGBToHSV(th.field, out float vh, out float vs, out _);                                      // VEHICLE area (the jam)
            Material vehicleZone = MaterialLibrary.MakeRuntimeMuted(Color.HSVToRGB(vh, vs * (darkTheme ? 0.8f : 0.55f), darkTheme ? 0.30f : 0.52f), 0.32f, 0f);
            Material accent = MaterialLibrary.GetTheme(th.name, "Accent", th.accent, 0.45f, 0.06f);
            Material main   = MaterialLibrary.GetTheme(th.name, "PropMain", th.propMain, 0.45f, 0.05f);
            Material alt    = MaterialLibrary.GetTheme(th.name, "PropAlt", th.propAlt, 0.45f, 0.05f);
            Material foliage= MaterialLibrary.GetTheme(th.name, "Foliage", th.foliage, 0.35f, 0.06f);
            Material trunk  = MaterialLibrary.GetTheme(th.name, "Trunk", th.trunk, 0.25f);
            Material grass  = MaterialLibrary.GetTheme(th.name, "Grass", th.grass, 0.30f, 0.06f);
            Material window = MaterialLibrary.GetTheme(th.name, "Window", new Color(th.sky.r * 0.9f + 0.1f, th.sky.g * 0.9f + 0.1f, th.sky.b, 1f), 0.7f, 0.25f);
            Material cloud  = MaterialLibrary.GetTheme(th.name, "Cloud", new Color(1f, 1f, 1f), 0f, 0.18f);
            // slotMat is now a stable, editable asset set in BuildMaterials (no theme override).

            // The ground is TWO colors split at the ROAD, spanning the FULL width (sides included): the VEHICLE area
            // below the road (toward the camera) is gray; the PEOPLE area above the road is the themed ground. The
            // road slab at RoadZ hides the seam on-screen (it is off-screen at the far sides).
            const float gFront = -32f, gBack = 38f; // full ground z-extent (replaces the old field + central plaza)
            LowPolyBuilder.Slab(boardRoot, new Vector3(0, -0.12f, (gFront + RoadZ) * 0.5f), new Vector3(46f, 0.2f, RoadZ - gFront), vehicleZone);
            LowPolyBuilder.Slab(boardRoot, new Vector3(0, -0.12f, (RoadZ + gBack) * 0.5f), new Vector3(46f, 0.2f, gBack - RoadZ), ground);
            // Distinct ROAD lane BELOW the parking stops (own band at RoadZ, between jam and stops) — full
            // buses drive off-screen sideways ALONG it. STANDARD asphalt every level (slimmed to a 1.0 lane).
            LowPolyBuilder.Slab(boardRoot, new Vector3(0, -0.10f, RoadZ), new Vector3(28f, 0.2f, 1.0f), roadMat);

            for (int i = -4; i <= 4; i++)
            {
                var post = MakeCube(boardRoot, accent, new Vector3(0.1f, 0.5f, 0.1f));
                post.transform.position = new Vector3(i * 1.1f, 0.25f, FenceZ);
                var bar = MakeCube(boardRoot, accent, new Vector3(1.1f, 0.06f, 0.05f));
                bar.transform.position = new Vector3(i * 1.1f + 0.55f, 0.34f, FenceZ);
            }

            // Side scatter — real SimplePoly trees/bushes down each side (fit-to-size); procedural props if the pack
            // is missing. Halved on low-end.
            int sideN = lowEnd ? 3 : 6;
            bool useTrees = cityTrees != null && cityTrees.Length > 0;
            for (int i = 0; i < sideN; i++)
            {
                float z = -1f + i * 2.6f;
                if (Mathf.Abs(z - RoadZ) < 1.6f) continue; // #1: skip side decor that would land ON the drive-in road lane (a tree sitting in the road at its start/end)
                if (useTrees)
                {
                    FitDecor(cityTrees[i % cityTrees.Length], new Vector3(-6.8f, 0, z), 1.5f, Quaternion.Euler(0, i * 47f, 0));
                    FitDecor(cityTrees[(i + 2) % cityTrees.Length], new Vector3(6.8f, 0, z), 1.5f, Quaternion.Euler(0, i * 53f + 90f, 0));
                }
                else
                {
                    PropKind k = (i % 2 == 0) ? th.prop : th.prop2;
                    LowPolyBuilder.BuildProp(boardRoot, k, new Vector3(-6.8f, 0, z), main, alt, foliage, trunk, window, 1f);
                    LowPolyBuilder.BuildProp(boardRoot, k, new Vector3(6.8f, 0, z), main, alt, foliage, trunk, window, 1f);
                }
            }

            // Behind the people band: a closed mall/terminal FACADE (people emerge from its doors), else
            // the legacy house centerpiece / prop row.
            doorXs = null;
            float backZ = PeopleZ + 4f;
            bool cityOk = cityBuildings != null && cityBuildings.Length > 0;
            if (th.hasFacade)
            {
                if (cityOk) BuildCityFacade(th);        // a real SimplePoly building people walk OUT of (sets doorXs/exitDoorX)
                else BuildFacade(th, accent, window);   // procedural fallback only if the pack is missing
            }
            else if (cityOk)
            {
                BuildCityBackRow(th, backZ);            // SimplePoly building row (no boarding door) — different per theme
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

            BuildCityDecor(th); // T3: road assets ON the road lane + side bus stops + per-theme street props (cosmetic)
            if (doorXs != null) BuildDoorPortal(th, accent); // themed door portal at the boarding door — people emerge from it

            // Grass tufts dressing the back lawn — skipped behind the closed facade (paved terminal plaza,
            // and they would otherwise poke through the wall front).
            if (!th.hasFacade && !lowEnd)
                for (int i = 0; i < 12; i++)
                {
                    float gx = -7.5f + (i * 1.45f) % 15f;
                    float gz = (PeopleZ + 1.8f) + (i % 3) * 1.1f;
                    LowPolyBuilder.GrassTuft(boardRoot, new Vector3(gx, 0, gz), 1.0f, grass);
                }

            int cloudN = lowEnd ? 2 : 4;
            for (int k = 0; k < cloudN; k++)
            {
                Vector3 cp = new Vector3(-5.5f + k * 3.5f, 9f + (k % 2) * 1.2f, 10f + (k % 3) * 2.5f);
                MakeCloud(cp, cloud, k);
            }
        }

        // Cosmetic-only: strip physics so nothing intercepts the tap raycast. LODGroup is left intact.
        static void StripPhysics(GameObject go)
        {
            // Disable BEFORE Destroy: Destroy is deferred to end-of-frame, but a DISABLED collider is ignored by
            // raycasts immediately — so taps are never blocked, and the synchronous "no enabled collider" check
            // below can't false-positive (which was tripping Error Pause and freezing every bonus level).
            foreach (var c in go.GetComponentsInChildren<Collider>(true))  { c.enabled = false; Destroy(c); }
            foreach (var r in go.GetComponentsInChildren<Rigidbody>(true)) { r.isKinematic = true; Destroy(r); }
        }

        // Combined world-space renderer bounds of an instantiated object (for fit-to-size placement).
        static Bounds RendererBounds(GameObject go)
        {
            var rs = go.GetComponentsInChildren<Renderer>();
            if (rs.Length == 0) return new Bounds(go.transform.position, Vector3.one);
            var b = rs[0].bounds;
            for (int i = 1; i < rs.Length; i++) b.Encapsulate(rs[i].bounds);
            return b;
        }

        // T3: instantiate a city-pack prefab, strip physics, SCALE so its XZ footprint ~= targetWidth (the pack is
        // authored far bigger than our 1.1-cell board) and sit its BASE on the ground at pos. Null prefab -> no-op.
        GameObject FitDecor(GameObject prefab, Vector3 pos, float targetWidth, Quaternion rot)
        {
            if (prefab == null) return null;
            if (prefab.name.Contains("Hydrant")) targetWidth *= 0.45f; // #3: the red fire hydrant was too big — scale it down
            var go = Instantiate(prefab, boardRoot, false);
            StripPhysics(go);
            MuteRenderers(go); // T2: quiet the city-pack prefab's OWN bright materials (per-INSTANCE; never the shared .mat)
            OutlineAll(go);    // toon ink edge on every env prefab (buildings, trees, road, stops, props) — AFTER mute so the outline stays black
            go.transform.SetPositionAndRotation(Vector3.zero, rot);
            go.transform.localScale = Vector3.one;
            var b = RendererBounds(go);
            float maxXZ = Mathf.Max(b.size.x, b.size.z, 0.01f);
            go.transform.localScale = Vector3.one * (targetWidth / maxXZ);
            b = RendererBounds(go);                                                       // recompute after scaling
            go.transform.position += new Vector3(pos.x - b.center.x, pos.y - b.min.y, pos.z - b.center.z); // center XZ, base on ground
            return go;
        }

        // T2 env mute: which shader color slots to pull toward grey. Covers URP/standard (_BaseColor/_Color) AND the
        // SimplePoly/POLYGON pack shaders (_Color01.._Color08), so every pack prefab is handled regardless of shader.
        static readonly string[] EnvColorProps =
            { "_BaseColor", "_Color", "_Color01", "_Color02", "_Color03", "_Color04", "_Color05", "_Color06", "_Color07", "_Color08" };
        static readonly Color EnvMuteGrey = new Color(0.5f, 0.5f, 0.5f);

        // T2: desaturate a city-pack prefab's OWN materials so the environment recedes behind the popped gameplay.
        // The pack prefabs carry their own BRIGHT .mat (untouched by Theme.cs/Vibrant) and are the LOUDEST env. Pull
        // every color slot HALFWAY to mid-grey. Per-INSTANCE via MaterialPropertyBlock so the shared on-disk .mat is
        // NEVER mutated (can't bleed into other instances or gameplay). Gameplay vehicles colour their body via the
        // bodyMats/_Color01 path (separate from FitDecor), so this can never leak onto gameplay.
        void MuteRenderers(GameObject go)
        {
            if (go == null) return;
            var mpb = new MaterialPropertyBlock();
            foreach (var r in go.GetComponentsInChildren<MeshRenderer>(true))
            {
                var sm = r.sharedMaterial;
                if (sm == null) continue;
                r.GetPropertyBlock(mpb);
                bool any = false;
                foreach (var p in EnvColorProps)
                {
                    if (!sm.HasProperty(p)) continue;
                    Color c = sm.GetColor(p);
                    if (c.maxColorComponent <= 0.02f) continue;          // skip black/unused slots
                    mpb.SetColor(p, Color.Lerp(c, EnvMuteGrey, 0.5f));   // halfway to mid-grey -> desaturate + recede
                    any = true;
                }
                if (any) r.SetPropertyBlock(mpb);
            }
        }

        // Per-theme, per-slot DISTINCT index into a pack array, so DIFFERENT themes show different buildings/props.
        static int ThemePick(Theme th, int count, int slot)
        {
            if (count <= 0) return 0;
            int t = Mathf.Abs((th.name ?? "").GetHashCode() % 997);
            return (t * 3 + slot * 7) % count;
        }

        // T3: a real SimplePoly building as the TERMINAL people walk out of. The boarding queue spawns at exitDoorX
        // (kept ~3.5), so the door building is centered on that x and faces the buses; flanked by MORE distinct
        // buildings for a per-theme city block. Sets doorXs/exitDoorX (the one gameplay-adjacent detail).
        void BuildCityFacade(Theme th)
        {
            float doorX = 3.5f;
            doorXs = new[] { doorX };
            exitDoorX = doorX;
            FitDecor(cityBuildings[ThemePick(th, cityBuildings.Length, 0)], new Vector3(doorX, 0, FacadeZ + 2.2f), 4.5f, Quaternion.Euler(0, 180f, 0)); // pushed back (+1.0) so it sits higher in frame; FitDecor outlines it
            if (lowEnd) return;
            FitDecor(cityBuildings[ThemePick(th, cityBuildings.Length, 1)], new Vector3(doorX - 5.0f, 0, FacadeZ + 2.5f), 3.6f, Quaternion.Euler(0, 180f, 0));
            FitDecor(cityBuildings[ThemePick(th, cityBuildings.Length, 2)], new Vector3(doorX - 9.0f, 0, FacadeZ + 2.3f), 3.4f, Quaternion.Euler(0, 180f, 0));
        }

        // T3: SimplePoly building row across the back for non-facade themes (no boarding door). Different per theme.
        void BuildCityBackRow(Theme th, float backZ)
        {
            int n = lowEnd ? 3 : 5;
            for (int i = 0; i < n; i++)
                FitDecor(cityBuildings[ThemePick(th, cityBuildings.Length, i)], new Vector3(-6f + i * 3f, 0, backZ + 1.5f + (i % 2) * 0.6f), 3.4f, Quaternion.Euler(0, 180f, 0)); // +1.0 back -> sits higher in frame
        }

        // T3: dress the ACTUAL road lane (RoadZ — where full buses drive off) with real SimplePoly road tiles, plus
        // side bus stops and per-theme street props. Cosmetic; fit-to-size + ground-placed; never touches the play grid.
        void BuildCityDecor(Theme th)
        {
            // Road tiles laid along RoadZ across the FULL width — extended PAST both screen edges so the drive-in lane
            // runs OFF-screen left AND right (a continuous road, not one that visibly ends on-screen).
            if (cityRoads != null && cityRoads.Length > 0)
            {
                float reach = VisHalfW(RoadZ) + 1.0f;                   // reach ~1u past the screen edge on each side
                const float step = 2.2f, tileHalf = 1.3f;              // 2.6-wide tiles (half 1.3), 2.2 step -> overlap, no gaps
                int half = Mathf.CeilToInt((reach - tileHalf) / step); // tiles each side of center -> lane crosses both edges
                for (int i = -half; i <= half; i++)
                    FitDecor(cityRoads[ThemePick(th, cityRoads.Length, i + half)], new Vector3(i * step, 0.01f, RoadZ), 2.6f, Quaternion.Euler(0, 90f, 0));
            }

            // Side bus stops, OUTSIDE the outer slot (~±4.2 at SlotSpacing 1.4, 7 pads) so they never cover a pad/lane.
            if (busStopFx != null)
            {
                FitDecor(busStopFx, new Vector3(-5.5f, 0, ParkingZ - 0.3f), 1.5f, Quaternion.Euler(0, 90f, 0));
                FitDecor(busStopFx, new Vector3(5.5f, 0, ParkingZ - 0.3f), 1.5f, Quaternion.Euler(0, -90f, 0));
            }

            // Per-theme street props down the sides of the PEOPLE area (the boarding zone) — NOT on the road/jam.
            if (!lowEnd && cityProps != null && cityProps.Length > 0)
                for (int i = 0; i < 3; i++)
                {
                    float z = PeopleZ - 0.8f + i * 0.6f; // #3: kept FORWARD of the back buildings (no clipping, e.g. the fire hydrant) but still in the people zone
                    FitDecor(cityProps[ThemePick(th, cityProps.Length, i)],     new Vector3(-5.5f, 0, z), 1.0f, Quaternion.Euler(0, 90f, 0));
                    FitDecor(cityProps[ThemePick(th, cityProps.Length, i + 1)], new Vector3(5.5f, 0, z), 1.0f, Quaternion.Euler(0, -90f, 0));
                }

            BuildJamProps(th); // little theme props framing the FRONT of the jam (small; foreground, out of the slots)
        }

        // A little BLACK door PORTAL right at the boarding door (exitDoorX, DoorSpawnZ) the people queue walks out
        // of — a near-black opening in a theme-accent frame, facing the buses (-Z). Built every level that has a door.
        void BuildDoorPortal(Theme th, Material frameMat)
        {
            float x = exitDoorX, z = DoorSpawnZ;

            // (#7) Brighter, softly-PULSING glow the queue steps out of (stronger emission than before -> a lit gateway).
            var glow = MaterialLibrary.MakeRuntime(th.accent, 0.05f, 1.1f);
            var op = MakeCube(boardRoot, glow, new Vector3(1.05f, 1.72f, 0.08f));          // the themed glowing opening people step out of
            op.transform.position = new Vector3(x, 0.9f, z + 0.06f);
            var bob = op.AddComponent<IdleBob>(); bob.scalePulse = true; bob.scaleAmp = 0.05f; bob.speed = 2.2f; bob.amp = 0f; // gentle "alive" shimmer

            // Side posts (a touch chunkier).
            var lp = MakeCube(boardRoot, frameMat, new Vector3(0.18f, 2.05f, 0.24f)); lp.transform.position = new Vector3(x - 0.64f, 1.02f, z);
            var rp = MakeCube(boardRoot, frameMat, new Vector3(0.18f, 2.05f, 0.24f)); rp.transform.position = new Vector3(x + 0.64f, 1.02f, z);

            // (#7) Stepped ARCH top instead of one flat lintel -> a friendlier, gateway-like silhouette.
            MakeCube(boardRoot, frameMat, new Vector3(1.52f, 0.18f, 0.24f)).transform.position = new Vector3(x, 2.02f, z);
            MakeCube(boardRoot, frameMat, new Vector3(1.04f, 0.16f, 0.24f)).transform.position = new Vector3(x, 2.17f, z);
            MakeCube(boardRoot, frameMat, new Vector3(0.56f, 0.14f, 0.24f)).transform.position = new Vector3(x, 2.30f, z);

            // (#7) Glowing finial caps on the posts + a keystone at the arch peak, plus a lit threshold underfoot.
            MakeCube(boardRoot, glow, new Vector3(0.24f, 0.24f, 0.28f)).transform.position = new Vector3(x - 0.64f, 2.12f, z);
            MakeCube(boardRoot, glow, new Vector3(0.24f, 0.24f, 0.28f)).transform.position = new Vector3(x + 0.64f, 2.12f, z);
            MakeCube(boardRoot, glow, new Vector3(0.22f, 0.22f, 0.30f)).transform.position = new Vector3(x, 2.42f, z);
            MakeCube(boardRoot, glow, new Vector3(1.25f, 0.05f, 0.5f)).transform.position  = new Vector3(x, 0.03f, z + 0.22f);
        }

        // Little theme props (small, fit-to-size) tucked into the FRONT CORNERS of the jam's foreground — out of the
        // slots/parking and mostly clear of the central exit lanes. Different per theme. No-op without the pack.
        void BuildJamProps(Theme th)
        {
            if (lowEnd || cityProps == null || cityProps.Length == 0) return;
            Vector3[] spots = {
                new Vector3(-4.0f, 0, -5.2f), new Vector3(4.0f, 0, -5.2f),
                new Vector3(-3.6f, 0, -6.0f), new Vector3(3.6f, 0, -6.0f), // pulled in + shallower -> on-screen, clear of reversing away-exit buses
            };
            for (int i = 0; i < spots.Length; i++)
                FitDecor(cityProps[ThemePick(th, cityProps.Length, i + 3)], spots[i], 0.7f, Quaternion.Euler(0, i * 73f, 0));
        }

        // ---- T5/T6 imported VFX (COSMETIC; lowEnd-gated; missing prefab -> no-op / cheap procedural fallback) ----

        // Built-in (legacy) particle materials render MAGENTA under URP. Swap BILLBOARD particles to a cached
        // runtime URP particle (alpha-blend) material, keeping the texture, so the imported VFX show WITHOUT a
        // manual material-conversion step. Mesh particles / already-URP materials are left alone.
        void FixParticleMaterials(GameObject go, ref Material cache, Color tint)
        {
            foreach (var psr in go.GetComponentsInChildren<ParticleSystemRenderer>(true))
            {
                if (psr.renderMode == ParticleSystemRenderMode.Mesh) continue;            // mesh debris -> rely on the editor URP conversion
                var src = psr.sharedMaterial;
                if (src != null && src.shader != null && src.shader.name.StartsWith("Universal")) continue; // already URP
                if (cache == null)
                {
                    var urp = Shader.Find("Universal Render Pipeline/Particles/Unlit");
                    if (urp == null) return;                                              // not URP / stripped -> leave original (user converts)
                    var m = new Material(urp);
                    if (src != null && src.mainTexture != null) m.mainTexture = src.mainTexture;
                    m.SetFloat("_Surface", 1f); m.SetFloat("_Blend", 0f);                 // transparent, alpha blend
                    m.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
                    m.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
                    m.SetFloat("_ZWrite", 0f);
                    m.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
                    m.color = tint;
                    cache = m;
                }
                psr.sharedMaterial = cache;
            }
        }

        // ====================================================================
        // T2: Bonus cross-traffic — pooled, collider-free, phase-locked, fair
        // ====================================================================
        // Two opposing lanes straddling the RoadZ band. Every car in a lane is rigidly phase-locked at a fixed
        // `period` (world units) apart, so gaps are CONSTANT and can never close into a wall — always dodgeable.
        // No colliders (taps never blocked), no per-car Update: ONE TrafficLoop advances all cars by writing x.
        void BuildTraffic()
        {
            traffic.Clear();
            trafficSpawnIdx = 0;
            var prefabs = Resources.LoadAll<GameObject>("Fx/Traffic"); // may be empty -> code-built fallback
            // PROGRESSIVE difficulty: the FIRST bonus (level 10) is the gentlest so players LEARN the dodge — few,
            // slow cars with big gaps — ramping to fast/denser by ~level 60. lowEnd thins it further. (This is the
            // fix for "too confusing / too much traffic": L10 has ~4 slow cars with huge gaps -> tap during an obvious opening.)
            int bonusIdx = Mathf.Max(1, currentLevel / 10);            // 1 at L10, 2 at L20, ...
            float diff = Mathf.Clamp01((bonusIdx - 1) / 5f);          // 0 at L10 -> 1 at L60+
            trafficCarSpeed = Mathf.Lerp(1.8f, 3.0f, diff);          // slow & readable at L10, faster later
            float period = Mathf.Lerp(18.0f, 4.5f, diff);          // L10: 1 car/lane (2 total, HUGE gaps) -> L60+: 4/lane (8 cars)
            if (lowEnd) period += 1.5f;                              // even fewer cars on budget phones
            const float SPAN = 18f;                                       // off-screen -9..+9 across the road
            int perLane = Mathf.Max(1, Mathf.CeilToInt(SPAN / period));
            trafficLoop = perLane * period;                              // wrap length -> EVEN spacing (no seam gap)
            trafficHalfLoop = trafficLoop * 0.5f;
            float width = CellSize * 0.95f;                             // ~a single road car
            for (int lane = 0; lane < 2; lane++)
            {
                int dir = lane == 0 ? +1 : -1;                         // lane 0 -> +X, lane 1 -> -X
                float laneZ = RoadZ + (lane == 0 ? -0.30f : 0.30f);
                float phase = lane == 1 ? period * 0.5f : 0f;          // offset the lanes so their gaps never align
                for (int i = 0; i < perLane; i++)
                {
                    float x = -trafficHalfLoop + Mathf.Repeat(i * period + phase, trafficLoop);
                    var pivot = BuildTrafficCar(prefabs, width, dir);
                    pivot.transform.position = new Vector3(x, 0f, laneZ);
                    traffic.Add(new TrafficCar { tf = pivot.transform, x = x, dir = dir, lane = lane });
                }
            }
        }

        // ONE car: a pivot at the lane spot holding a fit/recentered model whose NOSE (local -Z) faces travel.
        GameObject BuildTrafficCar(GameObject[] prefabs, float width, int dir)
        {
            var pivot = new GameObject("TrafficCar");
            pivot.transform.SetParent(boardRoot, false); // fit at the origin (boardRoot sits at world 0)
            float halfLen = width * 0.5f;
            if (prefabs != null && prefabs.Length > 0)
            {
                var src = prefabs[trafficSpawnIdx % prefabs.Length];
                var model = Instantiate(src, pivot.transform, false);
                model.name = "Model";
                StripPhysics(model);
                FitModelLocal(model, width);
                var b = RendererBounds(model);                          // measured at the origin -> world == local
                if (b.size.x > b.size.z) model.transform.localRotation = Quaternion.Euler(0, 90f, 0); // long axis -> Z
                b = RendererBounds(model);
                halfLen = Mathf.Max(b.size.z, b.size.x) * 0.5f;         // real span -> nose lamps land on the bumper
            }
            else
            {
                // Fallback: code-built car (nose = local -Z), tinted for variety. Never hard-fails.
                var palette = (PieceColor[])System.Enum.GetValues(typeof(PieceColor));
                var color = palette[trafficSpawnIdx % palette.Length];
                LowPolyBuilder.BuildVehicle(pivot.transform, VehicleType.Car, CellSize,
                    bodyMats[color], glassMat, wheelMat, lightMat, arrowMat);
                halfLen = LowPolyBuilder.VehicleLength(VehicleType.Car, CellSize) * 0.5f;
            }
            // Pivot faces travel: nose (local -Z) points the drive dir (+X -> yaw -90, -X -> yaw +90).
            pivot.transform.localRotation = Quaternion.Euler(0, dir > 0 ? -90f : 90f, 0);
            StripPhysics(pivot); // CRITICAL: the code-built body keeps a collider -> strip so traffic never eats a tap
            if (nightMode) AttachHeadlights(pivot.transform, halfLen, true); // T4: moving cars get the real spot
            trafficSpawnIdx++;
#if UNITY_EDITOR
            bool blocksTap = false;
            foreach (var col in pivot.GetComponentsInChildren<Collider>(true)) if (col.enabled) { blocksTap = true; break; }
            if (blocksTap)
                Debug.LogError("[Traffic] a traffic car still has an ENABLED collider — it would block taps!");
#endif
            return pivot;
        }

        // Fit a model to `targetWidth` and recenter it at its parent's LOCAL origin (XZ centered, base on y=0).
        // Parent must be at the world origin when called (boardRoot is), so world bounds == local offsets.
        void FitModelLocal(GameObject model, float targetWidth)
        {
            model.transform.localPosition = Vector3.zero;
            model.transform.localRotation = Quaternion.identity;
            model.transform.localScale = Vector3.one;
            var b = RendererBounds(model);
            float maxXZ = Mathf.Max(b.size.x, b.size.z, 0.01f);
            model.transform.localScale = Vector3.one * (targetWidth / maxXZ);
            b = RendererBounds(model);
            model.transform.localPosition += new Vector3(-b.center.x, -b.min.y, -b.center.z);
        }

        // ONE driver for every car: advance x, wrap within the loop, write position.x only (keeps the FitModelLocal
        // y/z). No allocation, no per-car component. Exits when the level leaves Playing (StopAllCoroutines also kills it).
        IEnumerator TrafficLoop()
        {
            while (state == GameState.Playing)
            {
                float step = trafficCarSpeed * Time.deltaTime;
                for (int i = 0; i < traffic.Count; i++)
                {
                    var c = traffic[i];
                    if (c.tf == null) continue;
                    c.x += c.dir * step;
                    if (c.x >= trafficHalfLoop) c.x -= trafficLoop;
                    else if (c.x < -trafficHalfLoop) c.x += trafficLoop;
                    var p = c.tf.position; p.x = c.x; c.tf.position = p;
                }
                yield return null;
            }
        }

        // T3 crossing checkpoint (pure arithmetic against the x the loop just wrote — NO colliders). Always true
        // on non-bonus levels, so the normal dispatch path is untouched.
        bool RoadClearAt(float crossX, float clearance)
        {
            if (!IsBonus || traffic.Count == 0) return true;
            for (int i = 0; i < traffic.Count; i++)
                if (Mathf.Abs(traffic[i].x - crossX) < clearance) return false;
            return true;
        }

        // T4: cheap forward headlights at a vehicle's NOSE (local -Z): emissive lamp lenses + a soft translucent
        // beam on the road (URP-safe, never magenta). withSpot adds ONE shadowless real Spot on !lowEnd (moving
        // traffic only — the 32-car bonus JAM with a real light each would be far too many). Parented to the
        // vehicle, so it is reaped with boardRoot. No-op if the night materials didn't build.
        void AttachHeadlights(Transform root, float halfLen, bool withSpot)
        {
            if (headlightMat == null) return;
            var grp = new GameObject("Headlights");
            grp.transform.SetParent(root, false);
            grp.transform.localPosition = new Vector3(0f, 0.12f, -halfLen - 0.05f); // nose = local -Z

            if (beamMat != null && headlightBeamMesh == null) headlightBeamMesh = BuildHeadlightBeam();

            for (int s = -1; s <= 1; s += 2) // two headlights at ±X — each: lens + glow + its OWN ground pool
            {
                // #6: flattened oval lens (thin in Z) reads like a real headlight, not a blobby sphere.
                var lamp = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                Destroy(lamp.GetComponent<Collider>());
                lamp.transform.SetParent(grp.transform, false);
                lamp.transform.localPosition = new Vector3(s * 0.18f, 0f, 0f);
                lamp.transform.localScale = new Vector3(0.13f, 0.10f, 0.05f); // wide+short oval, shallow lens
                lamp.GetComponent<Renderer>().sharedMaterial = headlightMat;

                if (lampGlowMat != null) // soft warm halo behind the lens → bright core with falloff
                {
                    var glow = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                    Destroy(glow.GetComponent<Collider>());
                    glow.transform.SetParent(grp.transform, false);
                    glow.transform.localPosition = new Vector3(s * 0.18f, 0f, 0.01f);
                    glow.transform.localScale = Vector3.one * 0.2f;
                    glow.GetComponent<Renderer>().sharedMaterial = lampGlowMat;
                }

                // #6: each lamp throws its OWN teardrop pool onto the road, toed slightly inward so the two pools
                // overlap down the centre — that overlap (real headlights do this) reads far more believable than
                // one symmetric box-fan. Per-vertex alpha fades it into the dark asphalt. The packed JAM gets a
                // SHORT, faint pool (bumper-to-bumper cars don't throw long beams, and 30 long fans = gray clutter);
                // MOVING traffic on the open dark road gets the full bright spill.
                var beamFor = withSpot ? beamMat : beamMatDim;
                if (beamFor != null)
                {
                    var beam = new GameObject("Beam");
                    beam.transform.SetParent(grp.transform, false);
                    beam.transform.localPosition = new Vector3(s * 0.16f, -0.06f, -0.18f); // start at the lamp, near the ground
                    beam.transform.localRotation = Quaternion.Euler(6f, -s * 5f, 0f);      // tilt down + toe inward
                    if (!withSpot) beam.transform.localScale = new Vector3(0.8f, 1f, 0.5f); // jam: narrower + much shorter throw
                    beam.AddComponent<MeshFilter>().sharedMesh = headlightBeamMesh;
                    beam.AddComponent<MeshRenderer>().sharedMaterial = beamFor;
                }
            }

            if (withSpot && !lowEnd) // ONE real shadowless spot (capable devices, moving cars only)
            {
                var lgo = new GameObject("Spot");
                lgo.transform.SetParent(grp.transform, false);
                lgo.transform.localRotation = Quaternion.Euler(10f, 180f, 0f); // face the car's -Z nose, tilt down
                var l = lgo.AddComponent<Light>();
                // #6: longer throw + softer inner cone so the pool fades gradually instead of a hard disc.
                l.type = LightType.Spot; l.range = 6f; l.spotAngle = 64f; l.innerSpotAngle = 16f;
                l.color = new Color(1f, 0.95f, 0.82f); l.intensity = 1.1f; l.shadows = LightShadows.None; // gentle warm key
            }
            // CreatePrimitive() gives the lamps/beam ENABLED colliders; disable+destroy them so they never block a
            // tap (and never trip the editor "enabled collider" check, which was pausing every bonus level).
            StripPhysics(grp);
        }

        // #6: a single lamp's ground spill — a TEARDROP pool: narrow + bright at the lens, bulging to its widest a
        // third of the way out, then tapering to a soft point and fading to fully transparent (per-vertex alpha).
        // Two of these (one per lamp, toed inward) overlap down the centre like real headlights. Lies in the XZ
        // plane pointing -Z (forward) so the beam object just tilts it onto the asphalt. Built once, SHARED.
        static Mesh BuildHeadlightBeam()
        {
            const float wNear = 0.09f, wWide = 0.30f; // half-widths: lens → widest bulge
            const float zWide = -0.7f, zTip = -1.9f;  // bulge distance, then the tip
            var bright = new Color(1f, 1f, 1f, 0.30f); // hot pool right at the lens
            var wide   = new Color(1f, 1f, 1f, 0.15f); // the bulge
            var fade   = new Color(1f, 1f, 1f, 0f);    // melts into the dark road
            var verts = new Vector3[]
            {
                new Vector3(-wNear, 0f, 0f),    new Vector3(wNear, 0f, 0f),    // 0,1 lens
                new Vector3(-wWide, 0f, zWide), new Vector3(wWide, 0f, zWide), // 2,3 widest bulge
                new Vector3(0f,     0f, zTip),                                 // 4   soft tip
            };
            var cols = new[] { bright, bright, wide, wide, fade };
            var tris = new[]
            {
                0, 2, 1,  1, 2, 3, // lens → bulge band
                2, 4, 3,           // bulge → tip
            };
            var m = new Mesh { name = "HeadlightBeam" };
            m.vertices = verts;
            m.colors = cols;
            m.triangles = tris;
            m.RecalculateBounds();
            return m;
        }

        // T5: take the imported Smoke03 prefab AS-IS, parent it BEHIND the vehicle, and let it PLAY as it drives.
        // No material/size overrides (those broke it before). NOT lowEnd-gated, so it always shows. Physics stripped
        // so no collider blocks the tap. null only if the prefab somehow didn't load.
        GameObject SpawnExhaust(Bus bus)
        {
            if (bus == null || smokeFx == null) return null;
            var go = Instantiate(smokeFx, bus.transform, false);
            StripPhysics(go);
            float halfLen = LowPolyBuilder.VehicleLength(bus.type, CellSize) * 0.5f;
            go.transform.localPosition = new Vector3(0f, 0.15f, halfLen); // right behind the rear
            go.transform.localRotation = Quaternion.identity;
            // The Polygonal pack material is a built-in shader -> magenta/invisible under URP. Keep Smoke03's OWN
            // texture but on the URP-safe Sprites/Default shader so it's guaranteed visible (white, alpha-blended).
            if (smokeMat == null)
            {
                var sp = Shader.Find("Sprites/Default");
                if (sp != null)
                {
                    var src = go.GetComponentInChildren<ParticleSystemRenderer>();
                    smokeMat = new Material(sp) { color = Color.white };
                    if (src != null && src.sharedMaterial != null && src.sharedMaterial.mainTexture != null)
                        smokeMat.mainTexture = src.sharedMaterial.mainTexture;
                }
            }
            if (smokeMat != null)
                foreach (var psr in go.GetComponentsInChildren<ParticleSystemRenderer>(true)) psr.sharedMaterial = smokeMat;
            foreach (var ps in go.GetComponentsInChildren<ParticleSystem>(true)) ps.Play(); // play it
            return go;
        }

        // Stop emitting + self-destruct after the puff fades. detach=true reparents to boardRoot first (dispatch
        // case: the bus is about to be Destroyed, so the trail must outlive it without being yanked away).
        void StopExhaust(GameObject smoke, bool detach)
        {
            if (smoke == null) return;
            if (detach && boardRoot != null) smoke.transform.SetParent(boardRoot, true);
            // (#7) Fade GRADUALLY, not suddenly. The old code destroyed the whole GameObject at ~0.45x of the
            // particles' lifetime, cutting the live puff off mid-air -> a visible "pop". Instead: stop emitting and
            // cap each live particle's REMAINING life to `fade`s, so the trail thins out and dies on its own (its
            // alpha-over-lifetime fade plays out) within a short, fixed window. Destroy only after they're gone.
            const float fade = 1.2f;
            foreach (var ps in smoke.GetComponentsInChildren<ParticleSystem>(true))
            {
                ps.Stop(true, ParticleSystemStopBehavior.StopEmitting);
                int max = ps.particleCount;
                if (max <= 0) continue;
                var buf = new ParticleSystem.Particle[max];
                int n = ps.GetParticles(buf);
                for (int i = 0; i < n; i++)
                    if (buf[i].remainingLifetime > fade) buf[i].remainingLifetime = fade;
                ps.SetParticles(buf, n);
            }
            Destroy(smoke, fade + 0.25f);
        }

        // T6: one-shot impact burst at a world pos. Returns false on lowEnd / missing prefab so the caller can use
        // the cheap Juice.Burst fallback. Self-destructs after its lifetime; parented under boardRoot.
        bool SpawnHit(Vector3 pos)
        {
            if (lowEnd || hitFx == null) return false;
            var go = Instantiate(hitFx, boardRoot, false);
            StripPhysics(go);
            FixParticleMaterials(go, ref hitMat, new Color(0.82f, 0.72f, 0.55f, 1f));
            go.transform.position = pos;
            var ps = go.GetComponentInChildren<ParticleSystem>();
            if (ps != null) ps.Play();                                                    // ensure the one-shot fires
            float life = ps != null ? ps.main.duration + ps.main.startLifetime.constantMax + 0.2f : 0.4f;
            Destroy(go, life);
            return true;
        }

        // T6: blocked-tap impact at the bus NOSE (NOSE is local -Z -> world -forward). Real debris poof, else
        // the cheap procedural burst on lowEnd / missing prefab.
        void SpawnBlockedHit(Bus bus)
        {
            if (bus == null) return;
            float halfLen = LowPolyBuilder.VehicleLength(bus.type, CellSize) * 0.5f;
            Vector3 pos = bus.transform.position + (-bus.transform.forward) * halfLen + Vector3.up * 0.4f;
            if (!SpawnHit(pos)) Juice.Burst(this, boardRoot, pos, bodyMats[bus.color], 8, 3f);
        }

        // TOON (game-wide): give every model a cohesive Gritline ink edge by drawing a DUPLICATE of its mesh with the
        // back-face outline material (inverted hull) on EVERY submesh. Unlike the old single-slot append, this works
        // for multi-submesh meshes AND skinned meshes (people) by sharing the original bones, so the outline covers
        // the whole silhouette and never drops submeshes. lowEnd skips it (it renders each mesh twice). Call ONCE on a
        // freshly-built model root; the duplicate has NO collider (taps unaffected) and is a child so Teardown reaps it.
        void OutlineAll(GameObject go)
        {
            if (lowEnd || go == null || toonOutlineFx == null) return;
            foreach (var mr in go.GetComponentsInChildren<MeshRenderer>(true))
            {
                var mf = mr.GetComponent<MeshFilter>();
                if (mf == null || mf.sharedMesh == null) continue;
                var dup = new GameObject("ToonEdge") { layer = mr.gameObject.layer };
                dup.transform.SetParent(mr.transform, false);          // child at identity -> overlaps the source mesh exactly
                dup.AddComponent<MeshFilter>().sharedMesh = mf.sharedMesh;
                var dmr = dup.AddComponent<MeshRenderer>();
                dmr.sharedMaterials = OutlineSlots(mf.sharedMesh.subMeshCount);
                dmr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                dmr.receiveShadows = false;
                dmr.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
            }
            foreach (var smr in go.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                if (smr.sharedMesh == null) continue;
                var dup = new GameObject("ToonEdge") { layer = smr.gameObject.layer };
                dup.transform.SetParent(smr.transform.parent, false);  // sibling in the same skeleton space
                dup.transform.localPosition = smr.transform.localPosition;
                dup.transform.localRotation = smr.transform.localRotation;
                dup.transform.localScale = smr.transform.localScale;
                var dsmr = dup.AddComponent<SkinnedMeshRenderer>();
                dsmr.sharedMesh = smr.sharedMesh;
                dsmr.bones = smr.bones;                                 // share the ORIGINAL skeleton -> deforms identically with the animation
                dsmr.rootBone = smr.rootBone;
                dsmr.localBounds = smr.localBounds;
                dsmr.sharedMaterials = OutlineSlots(smr.sharedMesh.subMeshCount);
                dsmr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                dsmr.receiveShadows = false;
                dsmr.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
            }
        }

        // All submesh slots point at the single shared outline material.
        Material[] OutlineSlots(int subMeshCount)
        {
            var a = new Material[Mathf.Max(1, subMeshCount)];
            for (int i = 0; i < a.Length; i++) a[i] = toonOutlineFx;
            return a;
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
            // Steep top-down, pulled BACK 6.0u along the view axis vs the original (0,16,-6): the jam now fills
            // ~74% of the portrait width, leaving a clear lane down each side WIDE ENOUGH (with comfortable margin
            // even at the deepest row) for AWAY-arrow vehicles to drive around to the stops on-screen without
            // touching the jam. VisHalfW + ScreenFloorZ are tied to this pos — change all three together.
            // (Dial 6.0 down toward 4.5 for a tighter frame if you accept a small graze at the two deepest rows.)
            Vector3 pos = new Vector3(0f, 21.2f, -8.99f);
            Vector3 target = new Vector3(0f, 0f, 3.2f);
            cam.transform.position = pos;
            cam.transform.rotation = Quaternion.LookRotation(target - pos, Vector3.up);
            cam.fieldOfView = 52f; // subtle zoom-in (was 54); camera pos/angle UNCHANGED so no new specular sheen (white-safe). VisHalfW + ScreenFloorZ re-fit to this.
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
            postCA = p.Add<ColorAdjustments>();
            postCA.postExposure.Override(0.12f); // a hair brighter (compensates the trimmed ambient)
            postCA.contrast.Override(12f);       // deeper shadows = more pop
            postCA.saturation.Override(18f);     // vivid, not faded

            var wb = p.Add<WhiteBalance>();
            wb.temperature.Override(6f);     // a touch warmer — friendlier, toy-like

            postVig = p.Add<Vignette>();     // subtle focus on the play area
            postVig.intensity.Override(0.24f);
            postVig.smoothness.Override(0.45f);
            postVig.rounded.Override(true);

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
