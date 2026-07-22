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
        public enum BonusKind { None, TrafficDodge, CoinRush, TimeAttack, MysteryRush }
        BonusKind bonusKind = BonusKind.None;                          // this level's bonus type (set in StartLevel)
        // Every 10th level = TrafficDodge (unchanged). 15,25,35... rotate the three new types. Remote flag off => None everywhere.
        static BonusKind LevelBonusKind(int lvl)
        {
            if (!GameConfig.FeatureBonusLevels) return BonusKind.None;
            if (lvl % 10 == 0) return BonusKind.TrafficDodge;
            if (lvl >= 15 && lvl % 10 == 5)
                switch (((lvl - 15) / 10) % 3) { case 0: return BonusKind.CoinRush; case 1: return BonusKind.TimeAttack; default: return BonusKind.MysteryRush; }
            return BonusKind.None;
        }
        public bool IsBonus => bonusKind == BonusKind.TrafficDodge;    // the night traffic-dodge round (ALL existing bonus logic stays gated on this)
        bool SpecialBonus => bonusKind == BonusKind.CoinRush || bonusKind == BonusKind.TimeAttack || bonusKind == BonusKind.MysteryRush;

        // Joker prices tuned to the flat 25-gold/level economy (Swap = ~2 levels, Recolor = ~3, Heli = ~4).
        static int RecolorCost => GameConfig.RecolorCost;
        static int SwapCost => GameConfig.SwapCost;
        static int HeliCost => GameConfig.HeliCost;
        static int SlotUnlockCost => GameConfig.SlotUnlockCost;
        bool heliCarrying;   // a helicopter joker is mid-lift; blocks a 2nd heli tap until the 1st starts leaving the screen
        static int ContinueBaseCost => GameConfig.ContinueBaseCost;   // 1st continue costs this; doubles each further continue in the level
        int continueCount;                  // gold continues used this level (resets on StartLevel)
        int CurrentContinueCost => ContinueBaseCost << continueCount; // 150, 300, 600, 1200, ...
        static int J1UnlockLevel => GameConfig.Joker1Unlock;
        static int J2UnlockLevel => GameConfig.Joker2Unlock;
        static int J3UnlockLevel => GameConfig.Joker3Unlock; // RECOLOR / SWAP / HELI

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
        LevelSelect levelSelect;              // opened from the in-game Settings → LEVELS button (debug/testing)
        PeopleCatalog peopleCatalog;
        VehicleCatalog vehicleCatalog;
        GameSettings gameSettings;            // editable tuning (speeds, sizes) — Resources/GameSettings.asset
        // Imported SimplePoly/Polygonal pack assets, loaded STRAIGHT from Resources/Fx (copied there, no build step).
        // Any null -> that piece falls back to procedural; the game never hard-fails on a missing asset.
        GameObject smokeFx, hitFx, busStopFx;
        GameObject[] cityBuildings, cityTrees, cityRoads, cityProps;
        Material toonOutlineFx;
        Material smokeMat, hitMat;            // cached runtime URP particle mats (so the built-in-shader VFX aren't magenta)
        static ParticleSystem.Particle[] particleBuf; // shared scratch for StopExhaust — avoids a per-call array alloc
        Font seatFont;
        Transform boardRoot;

        readonly Dictionary<PieceColor, Material> bodyMats = new Dictionary<PieceColor, Material>();
        Material glassMat, wheelMat, lightMat, skinMat, seatEmptyMat, mysteryMat, goldMat, arrowMat, lockMat, slotMat;
        Material heliBodyMat, heliAccentMat;  // helicopter-joker chopper: rescue-red shell + yellow accent (fin/hub/hook)
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
        TutorialCoach coach; bool tutorialActive; int tutorialStep; string[] tutPost; bool tutorialTapSkip;

        // ---- Bonus night-mode (every 10th level): countdown + cross-traffic + night headlights ----
        const float BonusTime = 60f;        // bonus-only countdown length (1 minute)
        const int BonusReward = 50;         // coins granted for finishing the bonus IN TIME
        const int CoinRushGold = 120;       // Coin Rush bonus: gold granted on clear (on top of the chest)
        const int PerfectBonus = 0;         // optional EXTRA for a no-crash run (opt-in: 0 = off by default)
        const int BonusComboTarget = 3;     // crash-free bus sends IN A ROW that earn the time reward
        const float BonusComboReward = 3f;  // seconds added each time the combo target is hit
        // Bonus TRAFFIC LIGHT: cars STOP on red (road clear, safe to send freely) then GO on green (must time the
        // crossings or crash). Cycles RED -> GREEN -> RED for the whole bonus. Makes the round actually beatable.
        const float BonusRedTime = 6f;      // red phase: cross-traffic frozen, crossings are SAFE
        const float BonusGreenTime = 8f;    // green phase: cross-traffic moving, crossings can CRASH
        bool trafficGo;                     // true = green (cars move, crash risk); false = red (frozen, safe)
        float trafficPhaseLeft;             // seconds left in the current red/green phase
        float bonusTimeLeft;
        bool bonusStarted;                  // bonus countdown waits for the player's FIRST tap, then ticks (no auto-start at load)
        float bonusElapsed;                 // TimeAttack: seconds since the first tap (faster clear -> better chest)
        int bonusCombo;                     // consecutive crash-free bonus sends; resets to 0 on any crash
        bool crashedThisBonus;              // set on a T3 crash -> disqualifies the perfect bonus
        bool nightMode;                     // cached in ApplyTheme (Night/Bonus) -> board + traffic headlights
        ColorAdjustments postCA;            // cached post-grade (deepened on dark themes, restored on bright)
        Vignette postVig;
        // Pooled cross-traffic (bonus only): plain position model, NO colliders, NO per-car Update.
        class TrafficCar { public Transform tf; public float x; public int dir; public int lane; public Transform headlights; }
        readonly List<TrafficCar> traffic = new List<TrafficCar>();
        float trafficHalfLoop, trafficLoop; // wrap bounds (even spacing) shared by both lanes
        int trafficSpawnIdx;                // deterministic variety counter (no Random in the hot path)
        float trafficCarSpeed;              // progressive car speed (eased by bonus index in BuildTraffic): slow at L10, faster later
        float trafficVis;                   // 0 = cars CLEARED off the road (red: road is genuinely clear, nothing to hit), 1 = present & flowing (green). Ramped for a smooth clear/return.
        // Real in-world traffic-light poles on the LEFT and RIGHT of the road (bonus only). The lit lamp tracks the
        // red/green phase; lamp renderers are swapped between on/off emissive materials (no per-frame work).
        readonly List<Renderer> trafficRedLamps = new List<Renderer>();
        readonly List<Renderer> trafficGreenLamps = new List<Renderer>();
        Material lampRedOn, lampRedOff, lampGreenOn, lampGreenOff;

        ParkingSlot[] slots;
        readonly Dictionary<Vector2Int, Bus> occ = new Dictionary<Vector2Int, Bus>();
        // Cells an IN-FLIGHT exiting bus's swept footprint is currently driving through (over the jam). Keeps a
        // moving vehicle VISIBLE to every later path/slide query so a second bus can't cross its live corridor.
        // Freed the moment the bus clears the jam (z >= RoadZ). Helicopter is exempt (flies over, never reserves).
        readonly Dictionary<Vector2Int, Bus> reservedByMoving = new Dictionary<Vector2Int, Bus>();
        Bus gridDriver; // the ONE bus currently driving ACROSS the jam — serialized so two exiting vehicles can never mesh
        int exitSeqCounter; // monotonic -> Bus.exitSeq: earlier exits get a lower number = right-of-way for the anti-overlap yield
        readonly List<Bus> gridBuses = new List<Bus>();
        readonly List<Bus> liveBuses = new List<Bus>(); // EVERY vehicle this level (jam + in-flight + parked) — scanned each frame to drive the engine sound
        // Per (pack material, color) instance with "Main Color 1" (_Color01) driven to the match color.
        // STATIC: keyed by shared ASSETS (pack material/texture + color), so the expensive first-time recolours
        // (full-texture repaints, GPU readbacks) are paid ONCE per app run — not again on every menu->game re-entry.
        static readonly Dictionary<(Material, PieceColor), Material> tintedVehicleMats = new Dictionary<(Material, PieceColor), Material>();

        List<LineGroup> groups;
        int nextGroupIndex;
        readonly List<LineUnit> visible = new List<LineUnit>();

        // ====================================================================
        void Start()
        {
            Screen.orientation = ScreenOrientation.Portrait;
            DeviceSetup.ApplyFrameRate();       // dynamic cap by device tier (high-end up to 120, others 60); re-assert after scene load
            lowEnd = DeviceSetup.DeviceTier == DeviceSetup.Tier.Low; // Low tier (~<3GB phone) → lightest paths (editor/desktop = High, never lowEnd)
            cam = Camera.main;
            BuildMaterials();
            peopleCatalog = Resources.Load<PeopleCatalog>("PeopleCatalog"); // null -> code-built people
            vehicleCatalog = Resources.Load<VehicleCatalog>("VehicleCatalog"); // null -> code-built vehicles
            smokeFx       = Resources.Load<GameObject>("Fx/smoke");            // imported pack prefabs copied into Resources/Fx (null -> procedural)
            hitFx         = Resources.Load<GameObject>("Fx/hit");
            busStopFx     = Resources.Load<GameObject>("Fx/busstop");
            // Gritline toon outline REMOVED — leave this null so OutlineAll() no-ops (no ink edge is drawn on
            // any vehicle/person/building). The Gritline Outline_BackFaceCull shadergraph is no longer loaded.
            toonOutlineFx = null;
            cityBuildings = Resources.LoadAll<GameObject>("Fx/Buildings");
            cityTrees     = Resources.LoadAll<GameObject>("Fx/Trees");
            cityRoads     = Resources.LoadAll<GameObject>("Fx/Roads");
            cityProps     = Resources.LoadAll<GameObject>("Fx/Props");
            gameSettings = Resources.Load<GameSettings>("GameSettings");       // tuning knobs (Inspector-editable)
            if (gameSettings == null) gameSettings = ScriptableObject.CreateInstance<GameSettings>(); // fall back to defaults
            seatFont = GameFont.UGUI; // roof seat-count number — now the global Matcha font
            // Pre-warm the mystery "?" glyph into the dynamic-font atlas NOW, so the FIRST level's already-in-line gray
            // passengers render it. "?" is otherwise a cold glyph (unlike digits, which the HUD already cached): the first
            // batch of "?" texts triggers an atlas rebuild and stays BLANK, while later spawns work once it's cached.
            GameFont.UGUI.RequestCharactersInTexture("?", 82, FontStyle.Bold);
            PlaceCamera();
            SetupPostFX();

            sfx = Sfx.Ensure(); // ONE persistent SFX voice for the whole game (no mixing); UiClickSound clicks every button
            ui = gameObject.AddComponent<GameUI>();
            var ad = AdManager.Ensure(this); // AdMob singleton (DontDestroyOnLoad); created from the gameplay scene
            ui.OnMenu = () => { PauseRequested?.Invoke(); }; // click handled globally by UiClickSound
            ui.OnRecolor = JokerRecolor;
            ui.OnSwap = JokerSwapPeople;
            ui.OnHeli = JokerHelicopter;
            ui.OnReskin = RetryLevel;            // Garage equip -> rebuild the board so the new skin MODEL is shown
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
                onReward: () => { ui.HideContinue(); ContinueLevel(true); },      // revive ONLY on a completed rewarded ad; open an AD pad (not a gold pad)
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
            ui.OnColorBlindToggle = ApplyColorBlindMode; // in-game Settings -> COLOR BLIND toggle (rebuilds colours live)

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
            yield return WarmVanRecolor(); // pre-warm the .glb recolour readbacks so level 1 (the tutorial) builds without GPU-readback stalls
            LoadLevel(SaveSystem.Level);
        }

        // Pre-warm the .glb van/bus recolour BEFORE the first level builds, so the tutorial doesn't pay the synchronous
        // GPU readback (RecoloredVanTex's Blit + ReadPixels) mid-board — the #1 cause of the level-1 frame drop. Uses the
        // EXACT runtime path (RecoloredVanMat -> PeopleColor), so the cached textures are colour-identical to what the
        // level build would compute (zero colour risk), and it fills the SAME instance cache the build reads. Bounded
        // (the base catalog is a handful of prefabs); one (material,colour) per frame keeps the warm itself from spiking.
        IEnumerator WarmVanRecolor()
        {
            if (vehicleCatalog == null) yield break;
            var colors = (PieceColor[])System.Enum.GetValues(typeof(PieceColor));
            var seen = new HashSet<Material>();
            foreach (VehicleType vt in (VehicleType[])System.Enum.GetValues(typeof(VehicleType)))
            {
                var prefab = vehicleCatalog.PrefabFor(vt);
                if (prefab == null) continue;
                foreach (var rend in prefab.GetComponentsInChildren<Renderer>(true))
                    foreach (var mat in rend.sharedMaterials)
                    {
                        if (mat == null || !seen.Add(mat)) continue;
                        // Mirror the runtime routing (~line 2702): a Mega Pack Car body takes the CPU-atlas path (no GPU
                        // readback) -> nothing to pre-warm there; only the .glb shells hit the readback path we care about.
                        bool atlasSedan = vt == VehicleType.Car && !mat.HasProperty("baseColorFactor") && !mat.HasProperty("baseColorTexture");
                        if (atlasSedan) continue;
                        foreach (var col in colors)
                        {
                            RecoloredVanMat(mat, col); // builds + caches the recoloured texture exactly as the level build will
                            yield return null;          // one readback per frame -> the warm itself never hitches
                        }
                    }
            }
        }

        // ---- Public control ------------------------------------------------
        public void LoadLevel(int levelNumber) { CancelInvoke(); StartLevel(levelNumber); }
        public void NextLevel() { LoadLevel(currentLevel + 1); } // the level AFTER the one just played (not SaveSystem.Level, which is the highest UNLOCKED -> replaying L7 used to jump to L10)
        public void RetryLevel() { LoadLevel(currentLevel); }
        public void ToggleSound() { SaveSystem.Sound = !SaveSystem.Sound; sfx.Click(); }

        // Called when the player flips the COLOR BLIND toggle. Palette.ToColor already returns the new palette (it reads
        // SaveSystem.ColorBlind), but the per-colour materials are CACHED (bodyMats + several recolor caches, some
        // static/persisting), so they must be invalidated and rebuilt or the vehicles keep their old colours. Then
        // RetryLevel rebuilds the board so every vehicle/person is recreated in the new palette.
        public void ApplyColorBlindMode()
        {
            ClearColorCaches();
            BuildMaterials();   // rebuild bodyMats (+ glass/wheel/etc.) from Palette.ToColor in the current mode
            RetryLevel();       // rebuild the board so live vehicles/people pick up the new materials
        }

        // Drop every cached (colour → material/texture) so the next build re-derives them from the current palette.
        // Covers the STATIC caches (survive scene reloads) AND the instance caches. bodyMats is repopulated by
        // BuildMaterials(); the recolor caches (imported sedan / glb bus/connect / van) rebuild on demand as the board
        // is created, all off the freshly-rebuilt bodyMats colours.
        void ClearColorCaches()
        {
            tintedVehicleMats.Clear();   // static
            texTintCache.Clear();        // static
            atlasRecolorCache.Clear();   // static
            atlasMatCache.Clear();       // static
            vanRecolorCache.Clear();     // static
            vanMatCache.Clear();         // instance
            skinTintCache.Clear();       // instance
            bodyMats.Clear();            // instance — refilled by BuildMaterials()
        }

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

        // (Economy rework) FLAT end-of-level reward — the ONLY gold source in gameplay. No level scaling, no star
        // bonus, no golden/passenger bonus (keeps the player from getting rich):
        //   normal level: 25 gold   |   bonus level: 50 gold.
        // CLAIM grants this; WATCH-AD grants 2x (see ClaimWinReward) -> 50 normal / 100 bonus.
        int LevelReward(int stars, bool bonus) => bonus ? GameConfig.BonusReward : GameConfig.LevelReward;

        public void ContinueLevel(bool preferAdSlot = false)
        {
            if (state != GameState.Lose || slots == null) return;
            CancelInvoke();
            // Revive by unlocking one locked slot (breaks the parking deadlock). When the revive was EARNED with a
            // rewarded ad, open an AD-unlock pad first so watching the ad opens the AD parking — not a coin (gold) pad.
            ParkingSlot target = null;
            if (preferAdSlot)
                foreach (var s in slots) if (s != null && s.locked && s.adUnlock) { target = s; break; }
            if (target == null)
                foreach (var s in slots) if (s != null && s.locked) { target = s; break; }
            if (target != null) target.Unlock();
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
            if (IsBonus && bonusStarted) { int lv0 = currentLevel; TickBonusTimer(); if (currentLevel != lv0 || state != GameState.Playing) return; } // bonus clock starts on the player's FIRST tap (set in TryTapBus), not at level load
            else if (bonusKind == BonusKind.TimeAttack && bonusStarted) { bonusElapsed += Time.deltaTime; ui.SetBonusStopwatch(bonusElapsed); } // count-UP stopwatch
#if UNITY_EDITOR
            AssertNoOccOverlap(); // invariant watchdog: logs to the Console if two vehicles ever share a cell
#endif
            RevealMystery();
            RevealReadyMysteryVehicles(); // gray vehicles turn their true color the moment their exit lane is clear

            if (TryGetPointerDown(out Vector2 sp))
            {
                if (EventSystem.current != null && EventSystem.current.IsPointerOverGameObject()) return;
                Ray ray = cam.ScreenPointToRay(sp);
                if (Physics.Raycast(ray, out RaycastHit hit, 400f))
                {
                    var bus = hit.collider.GetComponentInParent<Bus>();
                    if (bus != null) { if (tutorialActive) { tutorialTapSkip = true; if (tutorialStep == 1) AdvanceTutorialOnFirstMove(); } TryTapBus(bus); return; } // tapping the coached vehicle advances/dismisses the tutorial (not only on a successful park)
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

            System.Func<Vector2Int, bool> blocked    = c => Blocked(c, bus);                              // jam + in-flight corridors (used for the crawl below)
            System.Func<Vector2Int, bool> jamBlocked = c => occ.TryGetValue(c, out var ob) && ob != bus; // jam vehicles ONLY
            // Leave as soon as the JAM ahead is clear. A moving vehicle ahead is fine — we slide out RIGHT BEHIND it,
            // grounded, and the per-vehicle yield keeps us from ever touching it. No waiting for it to park; the exit
            // routine slides (never lifts/flies) because it tests the SAME jam-only clearance.
            bool jamClear = LevelGenerator.SlideClear(bus.cell, bus.dir, bus.length, jamBlocked, gridW, gridH);
            // For ANY diagonal vehicle, if the grid (which over-blocks tilted bodies) says no, ask the real body whether
            // its capsule can slide the EXACT 45° lane it will actually drive without touching anyone. The body check
            // and the real slide now follow the SAME preserve-45° path, so a "clear" verdict can't be driven into a mesh.
            bool isDiag = bus.dir.x != 0 && bus.dir.y != 0;
            bool canExit = jamClear || (isDiag && BodySlideClear(bus));
            var slot = canExit ? NearestFreeSlot(GridWorldCenter(bus.cell, bus.dir, bus.length).x) : null;

            if (canExit && slot != null)
            {
                if (bus.mystery && !bus.revealed) RevealVehicle(bus); // never drive off still gray (normally already revealed by Update)
                foreach (var c in LevelGenerator.OccCells(bus.cell, bus.dir, bus.length)) occ.Remove(c);
                gridBuses.Remove(bus);
                slot.occupant = bus; bus.slotIndex = slot.index; bus.state = BusState.MovingToSlot; // claimed synchronously
                if ((IsBonus || SpecialBonus) && !bonusStarted) { bonusStarted = true; if (coach != null) coach.Hide(); } // first vehicle sent -> start the clock (traffic + time-attack) + drop the intro text
                if (tutorialActive) AdvanceTutorialOnFirstMove(); // (#4) first successful park advances the coach
                StartCoroutine(ExitRoutine(bus, slot)); // engine (vroom) starts automatically while it drives — see UpdateEngineSfx
                return;
            }

            // A tap that didn't just park now NOSES FORWARD into the empty space ahead, as far as it can — not only the
            // special "«" crawler (user request: "front is clear, it should go as far as it can").
            // SAFETY LIMIT (keeps the old level-brick guard): if the lane is FULLY clear (canExit) but every parking
            // slot is full, STAY PUT. Sliding a fully-clear lane all the way to the board edge is exactly what used to
            // brick levels (it silently slid into another vehicle's still-needed exit lane, breaking index-order
            // solvability). Only a lane BLOCKED further ahead advances, and MaxAdvanceSteps stops AT that blocker while
            // staying in-grid — so the vehicle noses into the visible gap but never drives off the board or past it.
            if (canExit) { sfx.Crash(); StartCoroutine(Bump(bus.transform)); SpawnBlockedHit(bus); return; } // clear lane + no free slot -> no-op

            int cap = bus.advanceN > 0 ? bus.advanceN : gridW + gridH; // "«" crawler noses its fixed amount; any other vehicle goes up to the blocker
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
            bus.exitSeq = exitSeqCounter++; // right-of-way order: this vehicle yields to everyone who started before it

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
            // JAM-only clearance (a moving vehicle ahead is NOT a blocker — we slide out behind it, grounded, and yield).
            // So a follow-out vehicle SLIDES instead of taking the lift-over branch below, which is the "flies not drives" bug.
            bool laneClear = LevelGenerator.SlideClear(bus.cell, bus.dir, bus.length, c => occ.TryGetValue(c, out var ob) && ob != bus, gridW, gridH)
                             || ((bus.dir.x != 0 && bus.dir.y != 0) && BodySlideClear(bus)); // ANY diagonal whose REAL body has a clear 45° slide -> glide out grounded (not the lift-over)
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
                    float underZ = deepestZ - (halfLen + 0.9f);                  // FULLY below the deepest jammed vehicle (its cell bottom is ~half a cell under its centre) so the sideways move never clips the jam — esp. for long buses
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
                bool diagBus = bus.dir.x != 0 && bus.dir.y != 0;                          // ANY diagonal: keep the 45° angle (never bend) and leave via the side lane, so the tilted body can't sweep into a neighbour
                Vector3 clearPt = bus.transform.position + new Vector3(bus.dir.x, 0, -bus.dir.y) * (clearSteps * CellSize);
                if (diagBus)
                {
                    // Clamping clearPt.x would BEND the 45° slide steeper than the body faces, so the long body sweeps
                    // sideways into a neighbour (the "drives into the jam" bug). Instead SHORTEN the slide along the true
                    // 45° lane until it's on-screen — the body stays on its grid-clear diagonal lane and simply stops at
                    // the on-screen side edge, then rises out the side lane below.
                    Vector3 dn = new Vector3(bus.dir.x, 0, -bus.dir.y).normalized;
                    Vector3 sp0 = bus.transform.position;
                    float dist = Vector3.Distance(sp0, clearPt);
                    while (dist > CellSize && Mathf.Abs((sp0 + dn * dist).x) > VisHalfW((sp0 + dn * dist).z) - 1.0f) dist -= CellSize * 0.5f;
                    clearPt = sp0 + dn * dist;
                }
                else
                    clearPt = OnScreenX(clearPt, 1.0f);                                   // never slide the body off the SIDE of the screen (fixes deep sideways exits too)
                yield return MoveToYield(bus, clearPt, gameSettings.busDriveSpeed);       // STRAIGHT slide; HOLDS for a leader it's following out (no bow/rotation in the jam)

                float slotX = SlotX(slot.index);
                // ---- Phase 1: drive ONTO the road (no final bay commitment yet) ----
                var toRoad = new List<Vector3>();
                // A diagonal BUS that didn't fully clear the jam top (it stopped at the on-screen side edge) rises out
                // the SIDE lane on the side it's ALREADY on, so it drives to the stops FROM THE SIDE and never crosses
                // back over the jam. (If it did clear the top, it just comes onto the road like a normal toward exit.)
                bool sideRoute = away || (diagBus && clearPt.z < GridExitZ + 0.3f);
                if (sideRoute)
                {
                    // Rise hugging the on-screen side LANE up to the ROAD. Both side lanes sit OUTSIDE the jam (the
                    // ~25% zoom-out opened them), so either is grounded + collision-free. Away exits pick the side
                    // toward the bay; a diagonal bus rises on the side it slid out to (its own x) so it stays clear.
                    float side = diagBus ? (clearPt.x >= 0f ? 1f : -1f) : (slotX >= clearPt.x ? 1f : -1f);
                    const float M = 1.0f;                                        // body half-width + spline-bow + safety
                    toRoad.Add(new Vector3(side * (VisHalfW(clearPt.z) - M), 0, clearPt.z)); // out to the on-screen side lane
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
                    yield return DrivePathYield(bus, toRoad, gameSettings.busDriveSpeed, gameSettings.turnSmoothness);
                    float roadX = bus.transform.position.x;              // where it really is on the road
                    slot = NearestSlotToRoad(bus, slot, roadX);          // keeps its reserved bay only if it's still the closest
                    slotX = SlotX(slot.index);
                    var toBay = new List<Vector3>
                    {
                        OnScreenX(new Vector3(slotX, 0, RoadZ), 1.0f),     // along the open road to the bay's x
                        OnScreenX(new Vector3(slotX, 0, ParkingZ), 1.0f),  // pull up into the bay
                    };
                    yield return DrivePathYield(bus, toBay, gameSettings.busDriveSpeed, gameSettings.turnSmoothness);
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
            OnBonusBusSent();                                                            // bonus combo: a crash-free send -> may grant +time
            busy--;
            TryStartBoardingPump();
            CheckEnd();
        }

        // BONUS combo: a vehicle reached its stop WITHOUT crashing. Every BonusComboTarget sends in a row add
        // BonusComboReward seconds (a crash resets the streak in CrashAndReturn). No-op off bonus levels.
        void OnBonusBusSent()
        {
            if (!IsBonus || state != GameState.Playing) return;
            bonusCombo++;
            if (bonusCombo % BonusComboTarget == 0)
            {
                bonusTimeLeft += BonusComboReward;
                ui.SetBonusCountdown(bonusTimeLeft);
                ui.ShowTimeBonus(Mathf.RoundToInt(BonusComboReward));
                sfx.Coin();                                                              // a little positive ping for the reward
            }
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
            // ONE burst, in the vehicle's OWN colour. A second goldMat "spark flash" used to fire on top of this; it read
            // as a single effect only because EVERY bonus vehicle was yellow back when the bonus was 2-colour. Against the
            // 4-colour board it showed up as a clashing second colour on every non-yellow crash. Count raised from 18 to
            // absorb the removed 10 gold particles, so the crash still reads as unmistakable.
            Juice.Burst(this, boardRoot, hitPos, bodyMats[bus.color], 26, 5.5f);
            sfx.Crash();                                                           // impact sound
            sfx.Screech();                                                         // + tyre screech
            StopExhaust(exhaust, false);                           // stop the exit trail
            yield return Bump(bus.transform);                      // shake IN PLACE (yielded -> nothing else moves it -> no jitter/teleport)
            if (bus == null || state != GameState.Playing) { busy--; yield break; }

            // Penalty: TIME only. A resulting timeout funnels through the SAME single FinishBonus(false) below.
            bonusTimeLeft -= 3f;
            crashedThisBonus = true;                               // a crash this run drops a star on the success panel
            bonusCombo = 0;                                        // a crash breaks the crash-free combo streak
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
            foreach (var c in LevelGenerator.OccCells(bus.cell, bus.dir, bus.length))
            {
                occ[c] = bus;                                                                            // re-add the footprint (jam-occupied again)
                if (reservedByMoving.TryGetValue(c, out var rb) && rb != bus) reservedByMoving.Remove(c); // occ is authoritative: a cell this crashed bus reclaimed can't ALSO stay another mover's reserved corridor (that was the bonus [OccOverlap]); occ now protects it for everyone
            }
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
                    Vector3 target = s[idx + 1];
                    float d = Vector3.Distance(bus.transform.position, target);
                    Vector3 cand = (d <= move) ? target : Vector3.MoveTowards(bus.transform.position, target, move);
                    if (WouldOverlapPeer(bus, cand)) break;   // hold for a right-of-way peer (concurrent exits never overlap)
                    if (d <= move) { move -= d; bus.transform.position = target; idx++; }
                    else { bus.transform.position = cand; break; }
                }
                Vector3 look = s[Mathf.Min(idx + 1, s.Count - 1)] - bus.transform.position;
                if (look.sqrMagnitude > 1e-5f)
                    bus.transform.rotation = Quaternion.Slerp(bus.transform.rotation,
                        Quaternion.Euler(0, Mathf.Atan2(-look.x, -look.z) * Mathf.Rad2Deg, 0), 1f - Mathf.Exp(-turnLerp * Time.deltaTime));
                // Crash on a REAL mesh while CROSSING (moving perpendicular, mostly in z). Per-car x AND z overlap
                // test using the car's ACTUAL lane z, so it never false-fires below the lanes or while merging
                // ALONG the road's median (where the bus moves in x and its z-extent is just its narrow width).
                Vector3 vel = bus.transform.position - prev;
                // Crash only while the cars are ACTUALLY on the road (trafficVis past the halfway of its clear/return
                // ramp) — NOT off the instant light flip. So: steady RED (trafficVis~0) = road clear, crossing SAFE;
                // it stays live through the green->red scale-OUT until cars vanish (no driving through still-visible
                // frozen cars), and off through the red->green scale-IN until they're really there.
                if (trafficVis > 0.5f && Mathf.Abs(vel.z) > Mathf.Abs(vel.x))
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
                foreach (var fc in LevelGenerator.OccCells(c, bus.dir, bus.length)) // FULL footprint — matches the (thick) movement clearance, so a moving vehicle's reservation never disagrees with what it drives through
                {
                    if (CellWorld(fc).z >= RoadZ) continue; // only the contested jam corridor
                    // Never reserve a cell a DIFFERENT jam vehicle still occupies. A DIAGONAL that exited via the thin
                    // BodySlideClear check has a THICK footprint (incl. swept corners) that can clip a neighbour's
                    // corner cell — reserving it would put that cell in BOTH occ and reservedByMoving, which is the
                    // [OccOverlap] error (and a real clip). BodySlideClear already proved the REAL bodies don't touch,
                    // so that phantom corner needs no reservation; the jam vehicle's own occ keeps others out of it.
                    if (occ.TryGetValue(fc, out var jo) && jo != null && jo != bus) continue;
                    if (reservedByMoving.TryGetValue(fc, out var ex) && ex != null && ex != bus) continue; // FOLLOW-OUT: leave a cell the leader still holds; the per-vehicle yield keeps us apart
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
            if (u != null) { ModelPool.ReleaseAllUnder(u.transform); Destroy(u.gameObject); } // recycle the character model; the tiny root still dies
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

        // The WHOLE remaining queue in order: the on-screen window first, then the not-yet-streamed groups. Callers
        // (the heli's color pick) must see past `visible` because a single color's run can fill all 10 visible slots.
        IEnumerable<PieceColor> RemainingQueueColors()
        {
            foreach (var u in visible) if (u != null) yield return u.color;
            if (groups != null) for (int i = nextGroupIndex; i < groups.Count; i++) yield return groups[i].color;
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
            if (bus != null) { Juice.StopPunch(bus.transform); ModelPool.ReleaseAllUnder(bus.transform); Destroy(bus.gameObject); } // evict punch state, recycle the model, then destroy off-frame
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
            if (visible.Count == 0 && nextGroupIndex >= groups.Count) { if (bonusKind != BonusKind.None) BonusSuccess(); else Win(); return; }
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
            StartCoroutine(RecolorFx(new List<Bus>(gridBuses)));     // colour-burst wave across the freshly recoloured jam
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
            StartCoroutine(SwapFx(new List<LineUnit>(visible)));     // each shuffled person sparks in their OWN colour
            StartCoroutine(AfterJoker());
        }

        // J3 @ Lv15 HELICOPTER: airlift ONE jam vehicle straight onto a free slot, ignoring blockers.
        // Only relocates a vehicle that had to be parked eventually -> per-color balance unchanged.
        void JokerHelicopter()
        {
            if (heliCarrying) { sfx.Error(); return; } // a chopper is still lifting — wait until it starts leaving the screen
            if (state != GameState.Playing || gridBuses.Count == 0) { sfx.Error(); return; }
            if (SaveSystem.Level < J3UnlockLevel) { sfx.Error(); return; }
            var slot = FirstFreeSlot();
            if (slot == null) { sfx.Error(); return; } // no free slot -> nothing spent

            // Bring the NEXT color in line that ISN'T already being handled. A color counts as handled if a vehicle
            // of it is at/heading to a stop and still boarding (Parked or arriving, not full). Walk the WHOLE
            // remaining queue (past the visible window — runs can fill all 10 slots) and fetch the first queued
            // vehicle whose color isn't handled. So sending a correct vehicle then tapping heli grabs a DIFFERENT,
            // next-needed color instead of duplicating the one currently boarding.
            var handled = new HashSet<PieceColor>();
            foreach (var s in slots)
            {
                var b = s.occupant;
                if (b != null && (b.state == BusState.MovingToSlot || b.state == BusState.Parked) && !b.IsFull)
                    handled.Add(b.color);
            }
            Bus pick = null;
            foreach (var color in RemainingQueueColors())
            {
                if (handled.Contains(color)) continue;                                                              // a vehicle is already on this color
                foreach (var b in gridBuses) if (b.state == BusState.Queued && b.color == color) { pick = b; break; } // fetch this next-needed color
                if (pick != null) break;
            }
            if (pick == null) foreach (var b in gridBuses) if (b.state == BusState.Queued) { pick = b; break; }      // fallback: any queued vehicle
            if (pick == null) { sfx.Error(); return; }
            if (!SpendJoker(2, HeliCost)) { sfx.Error(); return; }

            foreach (var c in LevelGenerator.OccCells(pick.cell, pick.dir, pick.length)) occ.Remove(c); // free ALL body cells (no phantom)
            gridBuses.Remove(pick);
            slot.occupant = pick; pick.slotIndex = slot.index; pick.state = BusState.MovingToSlot;
            heliCarrying = true; // lock out a 2nd heli tap until this one starts leaving (cleared before the climb-away)
            StartCoroutine(HeliRoutine(pick, slot));
        }

        // A real rescue-chopper lift: a mini helicopter flies in from off-screen, drops a hook onto the picked
        // vehicle, winches it up, carries it over the jam to its bus stop, lowers it in, then climbs away
        // off-screen. The chopper + cable + hook are spawned here and torn down at the end (joker is one-shot).
        IEnumerator HeliRoutine(Bus bus, ParkingSlot slot)
        {
            busy++;
            sfx.Helicopter(true); // looping rotor for the whole flight (mutes the vroom while it owns the audio)

            float vehSize   = gameSettings.vehicleSize;
            float roofWorld = Mathf.Max(0.25f, bus.roofY * vehSize);          // height the cable grabs the roof at
            Vector3 vStart  = bus.transform.position;
            Quaternion vRot0 = bus.transform.rotation;
            Quaternion vPark = Quaternion.Euler(0, 180f, 0);                  // final parked facing (nose +Z) — matches drive-in parks
            Vector3 park    = ParkingWorld(slot.index);

            const float cruiseY  = 4.4f;   // cruise altitude — well in frame under the steep top-down camera
            const float carryGap = 1.5f;   // belly-to-roof gap while the vehicle dangles
            float hangY = cruiseY - carryGap - roofWorld;                     // vehicle base-Y while carried
            Vector3 bellyLocal = new Vector3(0f, -0.32f, 0.04f);             // cable anchor under the chopper

            Vector3 overCar  = new Vector3(vStart.x, cruiseY, vStart.z);
            Vector3 overPark = new Vector3(park.x,   cruiseY, park.z);
            float inSide  = vStart.x >= 0f ? 1f : -1f;
            float outSide = park.x  >= 0f ? 1f : -1f;
            Vector3 entry = new Vector3(inSide  * 14f, cruiseY + 1.2f, vStart.z);       // fly in from the near side
            Vector3 exit  = new Vector3(outSide * 15f, cruiseY + 3.5f, park.z + 2.0f);  // climb out + off-screen

            var heli = LowPolyBuilder.BuildHelicopter(boardRoot, heliBodyMat, glassMat, wheelMat, heliAccentMat);
            heli.transform.localScale = Vector3.one * 0.82f; // a touch smaller chopper
            heli.transform.position = entry;
            heli.transform.rotation = Quaternion.LookRotation((overCar - entry).normalized, Vector3.up);
            OutlineAll(heli);
            var cable = MakeCube(boardRoot, wheelMat,      new Vector3(0.05f, 0.3f, 0.05f)); cable.name = "HeliCable";
            var hook  = MakeCube(boardRoot, heliAccentMat, new Vector3(0.14f, 0.14f, 0.14f)); hook.name  = "HeliHook";

            Vector3 BellyPt() => heli.transform.TransformPoint(bellyLocal);
            Vector3 RoofPt()  => bus.transform.position + Vector3.up * roofWorld;
            Vector3 TuckPt()  => BellyPt() + Vector3.down * 0.3f;
            void DrawCable(Vector3 bot)
            {
                Vector3 top = BellyPt();
                Vector3 d = bot - top; float len = d.magnitude;
                cable.transform.position = (top + bot) * 0.5f;
                cable.transform.rotation = len > 1e-4f ? Quaternion.FromToRotation(Vector3.up, d / len) : Quaternion.identity;
                cable.transform.localScale = new Vector3(0.05f, Mathf.Max(len, 0.001f), 0.05f);
                hook.transform.position = bot;
            }

            DrawCable(TuckPt());

            // 1) fly in and hover over the picked vehicle
            yield return HeliFly(heli.transform, entry, overCar, 0.7f, true, () => DrawCable(TuckPt()));
            // 2) lower the hook onto the roof
            yield return HeliTween(0.4f, k => DrawCable(Vector3.Lerp(TuckPt(), RoofPt(), Mathf.SmoothStep(0, 1, k))));
            yield return new WaitForSeconds(0.06f);
            StartCoroutine(Juice.PunchScale(bus.transform, 0.08f)); // latch nudge
            // 3) winch the vehicle up to dangle below
            yield return HeliTween(0.5f, k =>
            {
                float s = Mathf.SmoothStep(0, 1, k);
                var p = bus.transform.position; p.y = Mathf.Lerp(0f, hangY, s); bus.transform.position = p;
                bus.transform.rotation = Quaternion.Slerp(vRot0, vPark, s);
                DrawCable(RoofPt());
            });
            // 4) carry it across to over the stop
            yield return HeliFly(heli.transform, overCar, overPark, 0.95f, true, () =>
            {
                var h = heli.transform.position;
                bus.transform.position = new Vector3(h.x, hangY, h.z);
                bus.transform.rotation = vPark;
                DrawCable(RoofPt());
            });
            // 5) lower it gently into the bay
            yield return HeliTween(0.5f, k =>
            {
                float s = Mathf.SmoothStep(0, 1, k);
                bus.transform.position = new Vector3(park.x, Mathf.Lerp(hangY, 0f, s), park.z);
                bus.transform.rotation = vPark;
                DrawCable(RoofPt());
            });
            bus.transform.position = park;
            bus.transform.rotation = vPark;
            bus.state = BusState.Parked;
            StartCoroutine(Juice.PunchScale(bus.transform, 0.16f));
            // 6) release the hook
            yield return HeliTween(0.28f, k => DrawCable(Vector3.Lerp(RoofPt(), TuckPt(), Mathf.SmoothStep(0, 1, k))));
            // 7) climb away off-screen — the chopper is now LEAVING, so free the heli joker for another tap
            heliCarrying = false;
            yield return HeliFly(heli.transform, overPark, exit, 0.8f, true, () => DrawCable(TuckPt()));

            if (heli  != null) Destroy(heli);
            if (cable != null) Destroy(cable);
            if (hook  != null) Destroy(hook);
            sfx.Helicopter(false); // rotor off -> the vroom may resume for normal moves

            busy--;
            TryStartBoardingPump();
            CheckEnd();
        }

        // Move the chopper from->to over dur (smoothstep), optionally banking its nose toward travel, and run
        // `tick` every frame AFTER the move so the cable + any dangling vehicle stay glued to the new pose.
        IEnumerator HeliFly(Transform heli, Vector3 from, Vector3 to, float dur, bool face, System.Action tick)
        {
            Quaternion r0 = heli != null ? heli.rotation : Quaternion.identity;
            Vector3 flat = to - from; flat.y = 0f;
            Quaternion r1 = (face && flat.sqrMagnitude > 0.04f)
                ? Quaternion.LookRotation(flat.normalized, Vector3.up) * Quaternion.Euler(8f, 0, 0) // slight nose-down lean
                : r0;
            float e = 0f;
            while (e < dur)
            {
                if (heli == null) yield break;
                e += Time.deltaTime;
                float k = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(e / dur));
                heli.position = Vector3.Lerp(from, to, k);
                heli.rotation = Quaternion.Slerp(r0, r1, Mathf.Clamp01(e / dur * 1.8f));
                tick?.Invoke();
                yield return null;
            }
            if (heli != null) { heli.position = to; heli.rotation = r1; }
            tick?.Invoke();
        }

        // Timed driver for the heli's hover phases: calls step(k) with k 0->1 over dur, then a final step(1).
        IEnumerator HeliTween(float dur, System.Action<float> step)
        {
            if (dur <= 0f) { step(1f); yield break; }
            float e = 0f;
            while (e < dur) { e += Time.deltaTime; step(Mathf.Clamp01(e / dur)); yield return null; }
            step(1f);
        }

        // Re-tint a jam bus to a new match-color (body + roof passengers) for RECOLOR / mystery reveal. Works for a
        // skin car-pack model (body tinted by material name), the imported gameplay pack (_Color01), or code-built.
        void RecolorBus(Bus bus, PieceColor newColor)
        {
            bus.color = newColor;
            var modelTf = bus.transform.Find("Model");
            if (modelTf != null)
            {
                if (bus.skinModelPrefab != null) // skin / glb model: recolor ONLY the body (texture × color / largest solid part)
                {
                    ColorSkinModel(modelTf, bus.skinModelPrefab, newColor, bus.type);
                }
                else if (vehicleCatalog != null) // imported gameplay pack: re-tint each slot's _Color01 from the prefab base
                {
                    var prefab = vehicleCatalog.PrefabFor(bus.type);
                    if (prefab != null && !ModelHasColor01(prefab)) // glb / no-_Color01 model: body-only recolor (matches build)
                    {
                        ColorSkinModel(modelTf, prefab, newColor, bus.type);
                    }
                    else if (prefab != null)
                    {
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

        // (DebugCycleSkin removed — skins are deprecated; the garage chests + craft drive vehicles now.)

        // GRAY a mystery vehicle's whole shell to the mystery material (mirror of RecolorBus, but to gray).
        // bus.color is left untouched — only the materials change, so reveal just re-tints from the prefab base.
        void GrayBus(Bus bus)
        {
            var modelTf = bus.transform.Find("Model");
            if (modelTf != null)
            {
                // Gray the whole model (mystery = fully hidden). Reveal re-derives the body color from the prefab.
                var modelRends = modelTf.GetComponentsInChildren<Renderer>(true);
                for (int r = 0; r < modelRends.Length; r++)
                {
                    var m = modelRends[r].sharedMaterials;
                    for (int i = 0; i < m.Length; i++) m[i] = mysteryMat;
                    modelRends[r].sharedMaterials = m;
                }
            }
            else // code-built fallback: gray the body cube
            {
                var bodyTf = bus.transform.Find("Body");
                if (bodyTf != null) { var br = bodyTf.GetComponent<Renderer>(); if (br != null) br.sharedMaterial = mysteryMat; }
            }
            // Gray the roof heads' caps too, so no color leaks through.
            if (bus.roofPeople != null)
                foreach (var pax in bus.roofPeople)
                {
                    if (pax == null) continue;
                    var hat = pax.transform.Find("Hat");
                    if (hat != null) { var hr = hat.GetComponent<Renderer>(); if (hr != null) hr.sharedMaterial = mysteryMat; }
                }
        }

        // Reveal a mystery vehicle's true color (its lane just became fully clear). Re-tints from the prefab
        // base via RecolorBus(bus, bus.color), drops the diamond marker, and pops for a beat of feedback.
        void RevealVehicle(Bus bus)
        {
            if (bus == null || bus.revealed) return;
            bus.revealed = true;
            RecolorBus(bus, bus.color); // bus.color already holds the TRUE color -> restores the real shell
            if (bus.mysteryMarker != null) { Destroy(bus.mysteryMarker); bus.mysteryMarker = null; }
            StartCoroutine(Juice.PunchScale(bus.transform, 0.16f));
        }

        // Auto-reveal any still-gray mystery vehicle whose exit lane is now fully clear. Same clearance test the
        // tap-to-exit path uses, so "revealed" == "tappable".
        // PERF: throttled to ~6x/sec instead of every frame — VehicleLaneClear → BodySlideClear is an O(vehicles ×
        // samples) geometry scan plus an `occ` walk per unrevealed diagonal mystery vehicle, and a clear lane is a
        // human-timescale event (a 0.15 s reveal delay is invisible). Tapping stays instant: TryTapBus runs its own
        // clearance test, independent of this.
        float mysteryRevealTimer;
        void RevealReadyMysteryVehicles()
        {
            if (gridBuses == null) return;
            mysteryRevealTimer += Time.deltaTime;
            if (mysteryRevealTimer < 0.15f) return;
            mysteryRevealTimer = 0f;
            for (int i = 0; i < gridBuses.Count; i++)
            {
                var b = gridBuses[i];
                if (b == null || !b.mystery || b.revealed) continue;
                if (b.state != BusState.Queued && b.state != BusState.Staging) continue; // only while still parked in the jam
                if (VehicleLaneClear(b)) RevealVehicle(b);
            }
        }

        // True when `bus` could drive its full length out of the jam right now — cardinal lane clear, OR a
        // diagonal whose real preserve-45° body path is clear. Mirrors the canExit test in TryTapBus exactly.
        bool VehicleLaneClear(Bus bus)
        {
            bool jamClear = LevelGenerator.SlideClear(bus.cell, bus.dir, bus.length,
                c => occ.TryGetValue(c, out var ob) && ob != bus, gridW, gridH);
            bool isDiag = bus.dir.x != 0 && bus.dir.y != 0;
            return jamClear || (isDiag && BodySlideClear(bus));
        }

        // ---- Joker "reward" FX (re-added after an origin-sync wipe — COMMIT this so it sticks) -----------
        // RECOLOR: a colour-burst that radiates OUT from the jam centre — each vehicle pops and sprays its
        // NEW colour, so the recolour visibly ripples across the whole jam (a happy splash of colour).
        IEnumerator RecolorFx(List<Bus> buses)
        {
            Vector3 center = Vector3.zero; int n = 0;
            foreach (var b in buses) if (b != null) { center += b.transform.position; n++; }
            if (n > 0) center /= n;
            buses.Sort((a, c) =>
            {
                float da = a != null ? (a.transform.position - center).sqrMagnitude : 1e9f;
                float dc = c != null ? (c.transform.position - center).sqrMagnitude : 1e9f;
                return da.CompareTo(dc);
            });
            foreach (var b in buses)
            {
                if (state != GameState.Playing) yield break;
                if (b == null) continue;
                StartCoroutine(Juice.PunchScale(b.transform, 0.18f, 0.26f));
                Juice.Burst(this, boardRoot, b.transform.position + Vector3.up * 0.9f, bodyMats[b.color], 8, 4.4f);  // spark in the vehicle's NEW colour
                yield return new WaitForSeconds(0.035f);
            }
        }

        // SWAP: a colour sparkle that sweeps left-to-right across the reshuffled queue — each waiting person
        // pops and sparks in THEIR OWN colour, so you see the colours rearranging (mystery folk stay hidden).
        IEnumerator SwapFx(List<LineUnit> people)
        {
            people.Sort((a, c) =>
            {
                float xa = a != null ? a.transform.position.x : 1e9f;
                float xc = c != null ? c.transform.position.x : 1e9f;
                return xa.CompareTo(xc);
            });
            foreach (var u in people)
            {
                if (state != GameState.Playing) yield break;
                if (u == null) continue;
                StartCoroutine(Juice.PunchScale(u.transform, 0.22f, 0.24f));
                Material m = (u.mystery && !u.revealed) ? goldMat : bodyMats[u.color];  // person's OWN colour; don't spoil a hidden mystery
                Juice.Burst(this, boardRoot, u.transform.position + Vector3.up * 0.9f, m, 7, 3.9f);
                yield return new WaitForSeconds(0.045f);
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
            bonusKind = LevelBonusKind(levelNumber);
            Teardown();

            // Load an authored level asset if one exists; otherwise generate procedurally.
            var def = Resources.Load<LevelDefinition>("Levels/Level" + levelNumber);
            // Bonus jams: Coin Rush = an easy jam shaped like a picture (heart/circle/…); Mystery Rush = every vehicle GRAY. Others authored-or-procedural.
            if (bonusKind == BonusKind.CoinRush)
            {
                // A DIFFERENT shape each Coin Rush level (15,45,75,105,135... every 30): circle -> triangle -> plus -> X -> heart, repeat.
                LayoutStyle shape;
                switch (Mathf.Max(0, (levelNumber - 15) / 30) % 5)
                {
                    case 0:  shape = LayoutStyle.Circle;   break;
                    case 1:  shape = LayoutStyle.Triangle; break;
                    case 2:  shape = LayoutStyle.Plus;     break;
                    case 3:  shape = LayoutStyle.XShape;   break;
                    default: shape = LayoutStyle.Heart;    break;
                }
                level = LevelGenerator.Generate(Mathf.Clamp(levelNumber / 4, 3, 5), shape, shapeFill: true);
            }
            else if (bonusKind == BonusKind.MysteryRush) level = LevelGenerator.Generate(levelNumber, forceMysteryP: 1f);
            else level = def != null ? LevelGenerator.Generate(def) : LevelGenerator.Generate(levelNumber);
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
                bonusCombo = 0;
                bonusTimeLeft = BonusTime;
                bonusStarted = false;                                       // frozen until the first tap; the bonus round shows NO coach text
                ui.SetBonusCountdown(bonusTimeLeft);
                trafficGo = false; trafficPhaseLeft = BonusRedTime;          // start on RED -> a free safe window to begin
                trafficVis = 0f;                                             // RED at start -> cars cleared off, the road is empty for the opening window
                BuildTraffic();
                BuildTrafficLights();                                        // real poles on both road sides; lit to match the phase
                StartCoroutine(TrafficLoop());
            }
            else if (SpecialBonus)
            {
                // New bonus types play as a normal jam with a twist; the reward is granted on clear (BonusSuccess).
                bonusElapsed = 0f; bonusStarted = false;
                if (bonusKind == BonusKind.TimeAttack) ui.SetBonusStopwatch(0f); // count-UP stopwatch, ticked in Update; starts on the first tap
                else ui.HideBonusCountdown();
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
            // Clear any leftover coach state (pulsing pointer ring + step flags) from the previous level first,
            // so a prior level's joker/diagonal coach can't bleed into this one (e.g. Lv5's RECOLOR ring lingering
            // into Lv6 when jumping levels). Each branch below re-arms its own banner/pointer fresh.
            tutorialActive = false; tutorialStep = 0;
            if (coach != null) coach.HidePointer();
            // NOTE: pass the RAW English key to the coach (NOT Loc.T(...)). TutorialCoach translates it AND re-translates
            // on a language change; pre-translating here would freeze the banner in the language it was created in.
            if (levelNumber == 1 && !SaveSystem.TutorialDone)
                StartTutorial("Tap a car to send it to a parking spot!"); // stays until ANY vehicle tap, then never shown again (TutorialDone)
            else if (levelNumber == 5 && !SaveSystem.FreeJokerGranted) // FIRST Lv5 visit only — skip on every replay (FreeJokerGranted persists once the RECOLOR unlock coach has run, mirroring level 1's TutorialDone gate)
                StartCoroutine(ShowBanner("Buses seat 10 people!", 3.5f, StartJokerTutorial)); // teach bus capacity, then the RECOLOR joker
            else if (levelNumber == 6)
                StartCoroutine(ShowBanner("New: vehicles can now move DIAGONALLY!", 5f));       // diagonals unlock at level 6
            else if (levelNumber == 10)
            {   // bonus: a small intro explaining the round; it vanishes on the first tap (TryTapBus), which also starts the clock
                if (coach == null) { coach = gameObject.AddComponent<TutorialCoach>(); coach.Build(); }
                coach.ShowText("Bonus round! A 4-colour jam — clear every vehicle before time runs out, and don't hit the cars crossing the road!");
            }
            else if (SpecialBonus)
            {
                if (coach == null) { coach = gameObject.AddComponent<TutorialCoach>(); coach.Build(); }
                coach.ShowText(bonusKind == BonusKind.CoinRush ? "Coin Rush! Clear the heart jam for a chest — then stop the bar on GOLD!"
                             : bonusKind == BonusKind.TimeAttack ? "Time Attack! Clear the jam FAST — a quicker time = a better chest!"
                             : "Mystery Rush! Every car is GRAY — send them out to reveal their colour, then grab a chest!");
            }
            else { tutorialActive = false; if (coach != null) coach.Hide(); }
        }

        void Teardown()
        {
            StopAllCoroutines();
            Juice.ClearAllPunches(); // drop punch state left by hard-stopped coroutines (no cross-level leak)
            busy = 0; pumpRunning = false; pumpDirty = false; heliCarrying = false;
            occ.Clear(); reservedByMoving.Clear(); gridDriver = null; gridBuses.Clear(); liveBuses.Clear(); visible.Clear(); slots = null;
            if (sfx != null) { sfx.SetEngine(false); sfx.StopAllHelicopter(); } // kill the engine + rotor loops across a level rebuild (a heli may be interrupted mid-flight)
            traffic.Clear(); // T2: drop pooled traffic refs (the cars themselves die with boardRoot below)
            trafficRedLamps.Clear(); trafficGreenLamps.Clear(); // drop traffic-light lamp refs (poles die with boardRoot)
            peopleLeftSign = null; // destroyed with boardRoot below; drop the stale ref (no cross-level leak)
            // Recycle EVERY pooled model (vehicles, characters, env decor, traffic, FX) BEFORE the board dies: the
            // next level's build then pops them from the pool instead of Instantiate'ing ~50 prefabs in one frame —
            // this was the level-transition freeze on weak phones.
            if (boardRoot != null) ModelPool.ReleaseAllUnder(boardRoot);
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
        void StartTutorial(string step1, params string[] postSteps)
        {
            if (SaveSystem.TutorialDone) { tutorialActive = false; if (coach != null) coach.Hide(); return; } // dismissed once -> never show again, whatever calls this (e.g. a reskin/RetryLevel rebuild)
            if (coach == null) { coach = gameObject.AddComponent<TutorialCoach>(); coach.Build(); }
            tutorialActive = true;
            tutorialStep = 1;
            tutPost = postSteps;
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
            SaveSystem.TutorialDone = true; // tapped a vehicle -> dismiss the coach for good; it never shows again
            EndTutorial();
        }

        IEnumerator PostStepSequence()
        {
            if (tutPost != null)
                foreach (var msg in tutPost)
                {
                    if (!tutorialActive || state != GameState.Playing) yield break;
                    coach.ShowText(msg);
                    tutorialTapSkip = false; // a fresh vehicle tap skips THIS info line (tap-to-advance)
                    float t = 0f;
                    while (t < 3f && tutorialActive && state == GameState.Playing && !tutorialTapSkip) { t += Time.unscaledDeltaTime; yield return null; }
                }
            EndTutorial();
        }

        // A non-interactive coach banner shown for `dur` seconds (info-only tutorials, e.g. capacity / diagonal notes).
        IEnumerator ShowBanner(string msg, float dur, System.Action then = null)
        {
            if (coach == null) { coach = gameObject.AddComponent<TutorialCoach>(); coach.Build(); }
            coach.ShowText(msg);
            float t = 0f;
            while (t < dur && state == GameState.Playing) { t += Time.unscaledDeltaTime; yield return null; }
            if (then != null && state == GameState.Playing) then(); else if (coach != null) coach.Hide(); // don't chain into the joker tutorial if the level already ended
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
            // RECOLOR coaching belongs ONLY to its unlock level (Lv5). The capacity banner defers this by 3.5s, so
            // if the player jumped to another level meanwhile, bail — never let it bleed onto Lv6/Lv10/etc.
            if (currentLevel != J1UnlockLevel) { if (coach != null) coach.Hide(); return; }
            if (coach == null) { coach = gameObject.AddComponent<TutorialCoach>(); coach.Build(); }
            // Mandatory free joker: grant ONE Recolor the first time it unlocks, so the player can try it for free.
            if (!SaveSystem.FreeJokerGranted) { SaveSystem.AddFreeJoker(0, 1); SaveSystem.FreeJokerGranted = true; ui.RefreshJokerLocks(); }
            tutorialActive = true;
            tutorialStep = 3; // distinct from the bus steps (1/2) so a bus tap can't advance it
            coach.ShowText("RECOLOR unlocked — here's 1 free! Tap it to reshuffle the buses' colours when stuck.");
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

            // Traffic light cycle: RED (cars frozen, safe) <-> GREEN (cars moving, risky).
            trafficPhaseLeft -= Time.deltaTime;
            if (trafficPhaseLeft <= 0f)
            {
                trafficGo = !trafficGo;
                trafficPhaseLeft = trafficGo ? BonusGreenTime : BonusRedTime;
                SetTrafficLightsVisual(trafficGo); // swap the lit lamp on the in-world poles (only on phase change)
            }

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

        // Unified SUCCESS path for ALL bonus kinds: lock progression + confetti, then hand off to the reward flow —
        // TimeAttack grants a chest by finish time; the others run the stop-the-bar mini-game to pick the chest.
        // (Bonus FAIL still goes through FinishBonus(false) / Lose. NextLevel advances once the reward is claimed.)
        void BonusSuccess()
        {
            if (state != GameState.Playing) return;
            state = GameState.Win;
            ui.HideBonusCountdown();
            EndTutorial();
            SaveSystem.Level = Mathf.Max(SaveSystem.Level, currentLevel + 1);
            SaveSystem.BestLevel = currentLevel;
            sfx.Win();
            ui.HideHud();
            ConfettiFromCorners();
            if (bonusKind == BonusKind.CoinRush) { SaveSystem.AddCoins(CoinRushGold); CoinsChanged?.Invoke(SaveSystem.Coins); } // the "rush" gold on top of the chest
            LevelCompleted?.Invoke(0, 3); // win signal for the ad cadence (the reward itself is a chest, not coins)
            if (bonusKind == BonusKind.TimeAttack)
            {
                ChestTier tier = bonusElapsed < 25f ? ChestTier.Gold : bonusElapsed < 45f ? ChestTier.Silver : ChestTier.Bronze;
                ui.ShowBonusReward(false, tier, NextLevel);          // time picked the tier -> straight to the chest reveal
            }
            else
                ui.ShowBonusReward(true, ChestTier.Bronze, NextLevel); // stop-the-bar decides Bronze/Silver/Gold
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
                    // Billboard indicator so the player sees HOW to open the pad: an animated yellow COST NUMBER on the
                    // gold (coin) pads, a video-ad icon on the rewarded-ad pads. Parented to the marker -> removed on Unlock.
                    if (slot.adUnlock) BuildSlotIcon(marker.transform, UIKit.WatchAd());
                    else BuildSlotCostNumber(marker.transform, SlotUnlockCost.ToString()); // the cost number shown INSIDE a gold coin
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

        // The coin pad's unlock indicator: the cost number (e.g. "75") sitting INSIDE a gold COIN, so it clearly reads
        // as a COIN cost instead of a bare, ambiguous number (the old version showed just the number). Floats above the
        // pad, billboards to the camera, bobs (its own IdleBob) and pulses (the marker's IdleBob).
        void BuildSlotCostNumber(Transform parent, string text)
        {
            var go = new GameObject("SlotCost", typeof(RectTransform), typeof(Canvas));
            go.transform.SetParent(parent, false);
            go.GetComponent<Canvas>().renderMode = RenderMode.WorldSpace;
            ((RectTransform)go.transform).sizeDelta = new Vector2(120, 120);
            go.transform.localPosition = new Vector3(0, 0.5f, 0); // centered on the marker, where the coin icon used to sit
            go.transform.localScale = new Vector3(-1f, 1f, 1f) * (0.92f / 120f); // -X cancels the BillboardUp flip; a touch bigger so coin + number both read clearly
            go.AddComponent<BillboardUp>();

            // Gold COIN backing (added FIRST -> renders behind the number). Same coin sprite as the HUD, gold-tinted.
            var coinGo = new GameObject("Coin", typeof(RectTransform));
            coinGo.transform.SetParent(go.transform, false);
            var crt = coinGo.GetComponent<RectTransform>();
            crt.anchorMin = Vector2.zero; crt.anchorMax = Vector2.one; crt.offsetMin = Vector2.zero; crt.offsetMax = Vector2.zero;
            var cimg = coinGo.AddComponent<UnityEngine.UI.Image>();
            cimg.sprite = UIKit.Coin(); cimg.color = new Color(1f, 0.84f, 0.28f); cimg.raycastTarget = false; cimg.preserveAspect = true;

            // Cost number INSIDE the coin: white + black outline (AddSignText) so it stays legible on gold at any angle.
            AddSignText(go.transform, text, 56, new Vector2(0.14f, 0.20f), new Vector2(0.86f, 0.80f), Color.white);
            var bob = go.AddComponent<IdleBob>(); bob.amp = 0.08f; bob.speed = 3f; bob.phase = 1.5f;
        }

        void BuildGrid()
        {
            gridW = level.gridW; gridH = level.gridH;
            occ.Clear(); gridBuses.Clear();
            foreach (var gb in level.gridBuses)
            {
                var bus = CreateBus(gb.color, gb.type, gb.capacity, gb.advanceN, DirYaw(gb.dir), gb.mystery);
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
            var model = ModelPool.Get(prefab, root); // pooled: skinned-character Instantiate+Animator init was a spawn hitch
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

            var model = ModelPool.Get(prefab, root); // pooled: this runs MID-PLAY for every streamed-in person (was the recurring spawn hitch)
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
                u.mysteryCover = LowPolyBuilder.BuildMysteryMark(root, mysteryMat, mh * 0.9f, mh * 0.4f); // "?" on top of the head, like a hat
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

        Bus CreateBus(PieceColor color, VehicleType type, int capacity, int advanceN, float yaw, bool mystery = false)
        {
            var root = new GameObject(type + "_" + color);
            root.transform.SetParent(boardRoot, false);
            root.transform.rotation = Quaternion.Euler(0, yaw, 0);
            var bus = root.AddComponent<Bus>();
            bus.color = color; bus.type = type; bus.capacity = capacity; bus.advanceN = advanceN; bus.mystery = mystery;
            liveBuses.Add(bus); // track for the engine-sound movement scan (cleared on level teardown)

            // Skins removed: a vehicle ALWAYS uses the player's EQUIPPED wardrobe model for its type (from the unlocked
            // set / "dolap"), falling back to the VehicleCatalog default, then to the code-built vehicle. No skin-model
            // override + no livery/accessory extras -> the garage vehicles read clean (just the gameplay colour). This
            // also drops the random-per-car skin model path, which built+recoloured a DIFFERENT model for every car and
            // could storm the texture recolour on load (the level freeze / "yüklenmiyor").
            GameObject prefab = VehicleWardrobe.EquippedModel(type);
            if (prefab == null && vehicleCatalog != null) prefab = vehicleCatalog.PrefabFor(type);
            if (prefab != null)
            {
                // Remember the ACTUAL model so RecolorBus re-tints from IT (not the catalog default), else a
                // wardrobe-equipped sedan recoloured via the Royal default -> slot mismatch -> raw atlas colour.
                bus.skinModelPrefab = prefab;
                BuildImportedVehicle(bus, root.transform, prefab, color, capacity, type); // builds seat-number + "<<" badge
            }
            else
            {
                LowPolyBuilder.BuildVehicle(root.transform, type, CellSize,
                    bodyMats[color], glassMat, wheelMat, lightMat, arrowMat);
                // Cute heads pop onto the roof as people board (replaces the empty-seat NUMBER).
                float cbTop = CellSize * 0.6f, cbLen = LowPolyBuilder.VehicleLength(type, CellSize);
                bus.roofPeople = BuildRoofHeads(root.transform, capacity, color, cbTop, CellSize * 0.26f, cbLen);
                bus.roofY = cbTop; // for the mystery "?" placement (same roof level as the arrow)
            }
            // MYSTERY: gray the whole shell and add the "?" roof badge BEFORE OutlineAll, so the toon outline
            // is added fresh on top (stays black ink) and reveal — RecolorBus from the prefab base — restores it.
            if (mystery) { GrayBus(bus); BuildMysteryMarker(bus, root.transform); }
            root.transform.localScale = Vector3.one * gameSettings.vehicleSize; // editable vehicle-size multiplier (both render paths)
            OutlineAll(root); // toon ink edge on the vehicle body + roof markers (before headlights so the glowing lenses stay clean)
            // Night headlights on the JAM vehicles (lens + glow + beam decals, no real light). HIGH-END only: a bonus
            // board is night-mode with up to ~32 vehicles, so this is ~32× transparent overdraw on exactly the heaviest
            // levels. Mid/low devices skip it (cleaner, much faster night/bonus board); the scene is still lit by the sun.
            if (nightMode && DeviceSetup.HighEndDevice())
                AttachHeadlights(root.transform, LowPolyBuilder.VehicleLength(type, CellSize) * 0.5f, false);
            return bus;
        }

        // A small "?" badge sitting on a mystery vehicle's roof, at the SAME height + centerline as the direction
        // arrow (bus.roofY) so the two read as one set of roof markings — sized to stay within the body. It's a
        // screen-aligned world-space label (BillboardUp keeps it upright + readable at any vehicle yaw), parented
        // under the root so it follows crawls and inherits the vehicle's size scale. Destroyed on reveal. (Jam
        // vehicles carry no roof passengers until they've parked — reveal always fires first — so it never clashes.)
        void BuildMysteryMarker(Bus bus, Transform root)
        {
            var go = new GameObject("MysteryMark", typeof(RectTransform), typeof(Canvas));
            go.transform.SetParent(root, false);
            go.GetComponent<Canvas>().renderMode = RenderMode.WorldSpace;
            ((RectTransform)go.transform).sizeDelta = new Vector2(100, 100);
            go.transform.localPosition = new Vector3(0f, bus.roofY + CellSize * 0.15f, 0f); // rest it on the roof, by the arrow
            go.transform.localScale = new Vector3(-1f, 1f, 1f) * (CellSize * 0.30f / 100f); // -X cancels the BillboardUp mirror; ~0.33u = small enough for tiny car roofs
            go.AddComponent<BillboardUp>();
            AddSignText(go.transform, "?", 100, Vector2.zero, Vector2.one, new Color(0.97f, 0.97f, 0.97f)); // white "?" w/ black outline reads on gray
            bus.mysteryMarker = go;
        }

        // Imported-vehicle wrapper: the catalog/wardrobe model, using its PER-TYPE catalog yaw (glTF minivan/bus face
        // opposite to the FBX car) + fit/offset.
        void BuildImportedVehicle(Bus bus, Transform root, GameObject prefab, PieceColor color, int capacity, VehicleType type)
            => BuildModelVehicle(bus, root, prefab, color, capacity, type, vehicleCatalog.YawFor(type), vehicleCatalog.fitFactor, vehicleCatalog.yOffset, false, TintedVehicleMat);

        // Skin-model wrapper: an imported car/SUV/bus model — color ONLY its body (largest part) to the match color.
        void BuildSkinVehicle(Bus bus, Transform root, GameObject prefab, PieceColor color, int capacity, VehicleType type)
            => BuildModelVehicle(bus, root, prefab, color, capacity, type, 0f, 1f, 0f, true, null);

        // A code-built BUS themed to a skin: the standard low-poly bus (body = match color) + two roof-edge accent
        // rails in the theme color, so it clearly reads as a BUS (not a stretched car) while matching the car theme.
        readonly Dictionary<string, Material> busAccentMats = new Dictionary<string, Material>();
        void BuildThemedBus(Bus bus, Transform root, PieceColor color, int capacity, VehicleType type, SkinDef skin)
        {
            LowPolyBuilder.BuildVehicle(root, type, CellSize, bodyMats[color], glassMat, wheelMat, lightMat, arrowMat);
            float cbTop = CellSize * 0.6f, cbLen = LowPolyBuilder.VehicleLength(type, CellSize);
            bus.roofPeople = BuildRoofHeads(root, capacity, color, cbTop, CellSize * 0.26f, cbLen);
            bus.roofY = cbTop;

            Material accent = (busAccentMats.TryGetValue(skin.id, out var am) && am != null)
                ? am : (busAccentMats[skin.id] = MaterialLibrary.MakeRuntime(skin.busAccent, 0.5f, 0.15f));
            float w = CellSize * 0.52f;
            for (int s = -1; s <= 1; s += 2) // a rail down each roof edge, clear of the centre arrow
            {
                var rail = MakeCube(root, accent, new Vector3(w * 0.12f, 0.06f * CellSize, cbLen * 0.86f));
                rail.transform.localPosition = new Vector3(s * w * 0.42f, cbTop + 0.04f * CellSize, 0);
            }
        }

        // ---- Code-built vehicle SKINS (v2) ----------------------------------------------------------------
        // A skin layers accent-coloured ACCESSORIES + a LIVERY onto the standard low-poly vehicle. Purely additive:
        // the body stays the gameplay match-colour, so recolor / reveal / colour-matching are completely untouched.

        readonly Dictionary<string, Material> skinAccentMats = new Dictionary<string, Material>();
        Material SkinAccent(SkinDef skin)
        {
            if (skin == null) return null;
            if (skinAccentMats.TryGetValue(skin.id, out var m) && m != null) return m;
            m = MaterialLibrary.MakeRuntime(skin.accent, 0.45f, skin.glow ? 0.75f : 0.12f); // emissive for neon/galaxy
            skinAccentMats[skin.id] = m;
            return m;
        }

        GameObject SkinCube(Transform root, Material mat, Vector3 pos, Vector3 scale)
        {
            var c = MakeCube(root, mat, scale);
            c.name = "SkinExtra"; // tagged so it's easy to find/strip; OutlineAll inks it like the rest
            c.transform.localPosition = pos;
            return c;
        }

        // The built vehicle's extent in ROOT-LOCAL space (before the vehicleSize scale), from its mesh renderers — so
        // skin extras fit whatever base was built (a code-built body OR an imported model). Skips our own extras + the
        // roof direction-arrow (which would inflate the measured top).
        bool TryVehicleBounds(Transform root, out Bounds b)
        {
            b = new Bounds(); bool has = false;
            var toLocal = root.worldToLocalMatrix;
            foreach (var r in root.GetComponentsInChildren<Renderer>())
            {
                if (r.name == "SkinExtra" || r.sharedMaterial == arrowMat) continue; // skip our extras + the roof arrow (head + shaft)
                Mesh mesh = null;
                var mf = r.GetComponent<MeshFilter>(); if (mf != null) mesh = mf.sharedMesh;
                if (mesh == null && r is SkinnedMeshRenderer smr) mesh = smr.sharedMesh;
                if (mesh == null) continue;
                var mb = mesh.bounds; Vector3 c = mb.center, e = mb.extents;
                Matrix4x4 m = toLocal * r.transform.localToWorldMatrix;
                for (int i = 0; i < 8; i++)
                {
                    Vector3 corner = c + new Vector3((i & 1) == 0 ? -e.x : e.x, (i & 2) == 0 ? -e.y : e.y, (i & 4) == 0 ? -e.z : e.z);
                    Vector3 pt = m.MultiplyPoint3x4(corner);
                    if (!has) { b = new Bounds(pt, Vector3.zero); has = true; } else b.Encapsulate(pt);
                }
            }
            return has;
        }

        // Lay the equipped skin's GENERATED pattern texture over the vehicle — a roof DECAL (small patch) or a full
        // BODY WRAP (covers the roof). The texture is transparent except for the accent pattern, so the body's
        // gameplay match-colour always shows through and colour-matching stays clear. Sized to the measured vehicle.
        void BuildSkinExtras(Transform root, SkinDef skin, VehicleType type)
        {
            if (skin == null || !skin.HasPattern) return;
            if (!TryVehicleBounds(root, out Bounds vb)) return;
            var tex = SkinTextureFactory.Get(skin.pattern, skin.accent);
            float halfW = Mathf.Max(0.06f, vb.extents.x), halfLen = Mathf.Max(0.06f, vb.extents.z);
            float cover = (skin.apply == SkinApply.BodyWrap) ? 0.98f : 0.58f; // wrap = whole roof, decal = small patch

            var quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
            var col = quad.GetComponent<Collider>(); if (col != null) Destroy(col);
            quad.name = "SkinExtra";
            quad.transform.SetParent(root, false);
            quad.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);                       // lie flat on the roof, facing up
            quad.transform.localPosition = new Vector3(vb.center.x, vb.max.y + CellSize * 0.01f, vb.center.z);
            quad.transform.localScale = new Vector3(halfW * 2f * cover, halfLen * 2f * cover, 1f);
            quad.GetComponent<MeshRenderer>().sharedMaterial = SkinDecalMat(tex, skin.glow);
        }

        // Transparent unlit material that shows a skin pattern texture (alpha-blended). Cached per texture.
        readonly Dictionary<Texture, Material> skinDecalMats = new Dictionary<Texture, Material>();
        Material SkinDecalMat(Texture tex, bool glow)
        {
            if (skinDecalMats.TryGetValue(tex, out var cached) && cached != null) return cached;
            Shader sh = Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Unlit/Transparent");
            var m = new Material(sh);
            if (m.HasProperty("_BaseMap")) m.SetTexture("_BaseMap", tex);
            if (m.HasProperty("_MainTex")) m.SetTexture("_MainTex", tex);
            if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", glow ? new Color(1.7f, 1.7f, 1.7f, 1f) : Color.white);
            if (m.HasProperty("_Surface")) m.SetFloat("_Surface", 1f); // URP: transparent
            if (m.HasProperty("_Blend")) m.SetFloat("_Blend", 0f);     // alpha blend
            if (m.HasProperty("_SrcBlend")) m.SetFloat("_SrcBlend", 5f);   // SrcAlpha
            if (m.HasProperty("_DstBlend")) m.SetFloat("_DstBlend", 10f);  // OneMinusSrcAlpha
            if (m.HasProperty("_ZWrite")) m.SetFloat("_ZWrite", 0f);
            if (m.HasProperty("_Cull")) m.SetFloat("_Cull", 0f); // double-sided -> visible whichever way the quad faces
            m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            m.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
            skinDecalMats[tex] = m;
            return m;
        }

        Transform FindArrow(Transform root)
        {
            foreach (var t in root.GetComponentsInChildren<Transform>(true))
                if (t != root && t.name.StartsWith("Arrow")) return t;
            return null;
        }

        Material policeRed, policeBlue;
        Material PoliceLightMat(bool red)
        {
            if (red)  return policeRed  != null ? policeRed  : (policeRed  = MaterialLibrary.MakeRuntime(new Color(1f, 0.16f, 0.13f), 0.3f, 0.95f));
            return         policeBlue != null ? policeBlue : (policeBlue = MaterialLibrary.MakeRuntime(new Color(0.16f, 0.32f, 1f), 0.3f, 0.95f));
        }

        // Instantiate a vehicle MODEL, drive its body to the match color (via `tintMat`), auto-face + auto-scale it
        // into the cell footprint, and add the white arrow + roof passenger heads + a tap box. Shared by the
        // imported-gameplay path and the cosmetic skin path (they differ only in config + which material is the body).
        void BuildModelVehicle(Bus bus, Transform root, GameObject prefab, PieceColor color, int capacity, VehicleType type,
                               float yaw, float fitFactor, float yOffset, bool bodyOnly, System.Func<Material, PieceColor, Material> tintMat)
        {
            var model = ModelPool.Get(prefab, root); // pooled: the high-poly vehicle Instantiate was THE level-build spike
            model.name = "Model";
            model.transform.localPosition = Vector3.zero;
            model.transform.localRotation = Quaternion.Euler(0, yaw, 0);
            model.transform.localScale = Vector3.one;

            // Strip physics from the pack prefab — its root Rigidbody+gravity would make the
            // model fall through the floor at Play (leaving only our roof decals visible).
            foreach (var rb in model.GetComponentsInChildren<Rigidbody>(true)) Destroy(rb);
            foreach (var c in model.GetComponentsInChildren<Collider>(true)) Destroy(c);

            // Color the body. Skin/glb models — and ANY imported model WITHOUT the pack's _Color01 slot (e.g. a raw
            // .glb bus from othercars) — color ONLY their largest part (the body) to the gameplay color, leaving
            // glass/wheels/lights as-is; only the LowPolyRoadVehicles pack (which exposes _Color01) tints per-slot.
            if (bodyOnly || !ModelHasColor01(prefab))
            {
                // .glb vans/buses ship a flat baked "showroom" floor under the model; it's part of the body mesh, so the
                // body recolour paints it too -> a coloured ground slab under the vehicle. Strip those bottom horizontal
                // triangles (same trim the garage preview uses) BEFORE recolouring so no slab remains to be painted.
                foreach (var mf in model.GetComponentsInChildren<MeshFilter>(true))
                    if (mf.sharedMesh != null) mf.sharedMesh = VehiclePreview.TrimBase(mf.sharedMesh);
                ColorSkinModel(model.transform, prefab, color, type);
            }
            else
                foreach (var r in model.GetComponentsInChildren<Renderer>(true))
                {
                    var mats = r.sharedMaterials;
                    for (int i = 0; i < mats.Length; i++)
                        if (mats[i] != null) mats[i] = tintMat(mats[i], color);
                    r.sharedMaterials = mats;
                }

            // Auto-face forward: rotate the model so its LONGEST horizontal axis runs along the root's
            // local Z (the exit direction), regardless of the pack's native orientation. Measured in
            // ROOT-LOCAL space (NOT a world AABB) so the decision is INDEPENDENT of the root's world yaw —
            // a diagonal (±45°-yawed) vehicle decides exactly like a cardinal one. (A world AABB is square-ish
            // at 45°, so the old test was an unstable tie that flipped some diagonal bodies crosswise to their arrow.)
            Bounds faceB = ModelBoundsIn(root, model);
            if (faceB.size.x > faceB.size.z)
                model.transform.localRotation = Quaternion.Euler(0, yaw + 90f, 0);

            // Span the vehicle's grid footprint: CellLength cells (Car 1 / Bus 2).
            float target = Vehicles.CellLength(type) * CellSize * fitFactor;

            var rends = model.GetComponentsInChildren<Renderer>(true); // include inactive for first-frame consistency (matches the mesh queries)
            // PERF: vehicles don't CAST real-time shadows. On dense/bonus boards (up to ~32 high-poly bodies) the
            // directional soft-shadow pass would re-render every vehicle into the shadow map — roughly doubling the
            // vertex load on exactly the heaviest levels. The ground + buildings still cast; vehicles only lose their
            // own cast shadow (barely visible top-down). They still RECEIVE shadows. Single biggest GPU win here.
            for (int ri = 0; ri < rends.Length; ri++)
                if (rends[ri] != null) rends[ri].shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
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
                // UNIFORM scale keeps the model's true PROPORTIONS (no width stretching); fit length to the L-cell
                // span. PER-TYPE fill leaves a GAP so neighbours don't look joined ("bitişik"); the minivan fills a
                // touch more so it reads as a bigger vehicle than a car. (Tune these two numbers freely.)
                float fill = type == VehicleType.Minivan ? 0.97f : 0.88f;
                float scl = Mathf.Min(target / len, (CellSize * 1.1f) / widRaw) * fill;
                model.transform.localScale = Vector3.one * scl;
                float bottom = localFrame ? lb.min.y : (lb.min.y - root.position.y);
                // Re-center the body on the root origin in X/Z (a pack pivot is often NOT the mesh center),
                // so the roof arrow + heads + tap box sit symmetric on the ACTUAL body, not the pivot.
                Vector3 ctr = localFrame ? model.transform.localRotation * (lb.center * scl) : Vector3.zero;
                model.transform.localPosition = new Vector3(-ctr.x, -bottom * scl + yOffset, -ctr.z);
                roofY = lb.size.y * scl + yOffset;
                span = len * scl;
                wid = widRaw * scl;
            }

            // Roof-marker height. Use the MESH-derived roofY (frame-independent), NOT Renderer.bounds: on the
            // FIRST frame of Play, Renderer.bounds is still zero/stale (Unity hasn't run a cull pass yet), which
            // put the arrow + heads + badge + tap-box at the wrong height on level 1 until a Replay rebuilt them
            // on a later frame ("buses look funny on Play, Replay fixes it"). roofY already == the scaled model
            // height with the base sitting at y=0, so it IS the true top — and matches Renderer.bounds when valid.
            float topY = roofY;
            bus.roofY = topY; // for the mystery "?" placement (same roof level as the arrow)

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

        // True if the prefab exposes the LowPolyRoadVehicles pack's "_Color01" body slot. glTF/.glb imports (the
        // othercars Bus/Connect/Sedan) lack it -> they tint via the body-only path (ColorSkinModel) instead, so no
        // catalog flag is needed: drop a glb into busPrefab/carPrefab and it recolors correctly on its own.
        static readonly Dictionary<GameObject, bool> hasColor01Cache = new Dictionary<GameObject, bool>(); // static: survives scene re-entry
        bool ModelHasColor01(GameObject prefab)
        {
            if (prefab == null) return false;
            // Memoized per prefab: this is a full renderer+material scan and it's called once per spawned vehicle —
            // on a 32-vehicle board that's 32 identical scans of the SAME prefab. Cache -> one scan per prefab, ever.
            if (hasColor01Cache.TryGetValue(prefab, out var cached)) return cached;
            bool has = false;
            foreach (var r in prefab.GetComponentsInChildren<Renderer>(true))
            {
                foreach (var m in r.sharedMaterials)
                    if (m != null && m.HasProperty("_Color01")) { has = true; break; }
                if (has) break;
            }
            hasColor01Cache[prefab] = has;
            return has;
        }

        // Car-pack skin tinting: drive the BODY (the paint material — its name doesn't match a "detail" part) to the
        // match color so the car shows the gameplay color, keeping glass/wheels/lights/trim as-is. Cached per
        // (material, color) so it never allocates per-vehicle and never mutates the shared pack material.
        // The vehicle BODY = the renderer-submesh with the MOST geometry (windows/wheels/lights are smaller parts).
        // This lets us color ONLY the body of an imported skin/glb model — no reliance on how its materials are named.
        (Renderer rend, int slot) FindSkinBody(GameObject model)
        {
            Renderer best = null; int bestSlot = 0; long bestCount = -1;
            foreach (var r in model.GetComponentsInChildren<Renderer>(true))
            {
                Mesh mesh = null;
                var mf = r.GetComponent<MeshFilter>(); if (mf != null) mesh = mf.sharedMesh;
                if (mesh == null && r is SkinnedMeshRenderer smr) mesh = smr.sharedMesh;
                if (mesh == null) continue;
                for (int s = 0; s < mesh.subMeshCount; s++)
                {
                    long cnt = mesh.GetIndexCount(s);
                    if (cnt > bestCount) { bestCount = cnt; best = r; bestSlot = s; }
                }
            }
            return (best, bestSlot);
        }

        Material LargestMaterial(GameObject model)
        {
            var (rend, slot) = FindSkinBody(model);
            if (rend == null) return null;
            var mats = rend.sharedMaterials;
            return slot < mats.Length ? mats[slot] : null;
        }

        // The model's albedo texture (try the common shader property names so it works for URP Lit AND glTFast).
        static Texture GetAlbedo(Material m)
        {
            if (m == null) return null;
            if (m.mainTexture != null) return m.mainTexture;
            string[] props = { "_BaseMap", "_BaseColorMap", "baseColorTexture", "_MainTex" };
            foreach (var p in props) if (m.HasProperty(p)) { var t = m.GetTexture(p); if (t != null) return t; }
            return null;
        }

        // A URP/Lit material that keeps the model's texture but MULTIPLIES it by the match color. The texture's dark
        // windows/wheels stay dark (dark × color ≈ dark); only the light body takes the color. Cached per texture+color.
        static readonly Dictionary<(Texture, PieceColor), Material> texTintCache = new Dictionary<(Texture, PieceColor), Material>(); // static: survives scene re-entry
        Material TexturedTint(Texture tex, PieceColor color)
        {
            var key = (tex, color);
            if (texTintCache.TryGetValue(key, out var cached) && cached != null) return cached;
            Shader sh = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
            var m = new Material(sh);
            Color c = PeopleColor(color);
            if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", c);
            if (m.HasProperty("_Color")) m.SetColor("_Color", c);
            if (m.HasProperty("_BaseMap")) m.SetTexture("_BaseMap", tex);
            if (m.HasProperty("_MainTex")) m.SetTexture("_MainTex", tex);
            if (m.HasProperty("_Smoothness")) m.SetFloat("_Smoothness", 0.5f);
            texTintCache[key] = m;
            return m;
        }

        // Color a skin/imported model to the match color — BODY ONLY. Re-derives each slot from `srcPrefab` so it
        // works at build AND after a recolor. A TEXTURED material (single-mesh .glb like the SUV) keeps its texture
        // and is multiplied by the color, so the baked-dark windows/wheels stay dark. A SOLID material (FBX paint)
        // gets the gameplay-color material on the body part only; its glass/wheels/lights are left untouched.
        // Distinct shared materials on a model — used to tell a SEPARATE-body model (Mega Pack: body + lights) from a
        // SINGLE-material one (the .glb van/bus, whose one texture bakes in the windows).
        int DistinctMaterialCount(GameObject prefab)
        {
            if (prefab == null) return 0;
            var set = new HashSet<Material>();
            foreach (var r in prefab.GetComponentsInChildren<Renderer>(true))
                foreach (var m in r.sharedMaterials) if (m != null) set.Add(m);
            return set.Count;
        }

        // Recolour is best-effort: a texture read / Blit hiccup must NEVER abort the synchronous level build (that
        // would freeze / half-load the level — "bazısında yüklenmiyor"); on any exception we just leave the vehicle
        // its default texture and carry on.
        void ColorSkinModel(Transform modelTf, GameObject srcPrefab, PieceColor color, VehicleType type)
        {
            try { ColorSkinModelCore(modelTf, srcPrefab, color, type); }
            catch (System.Exception e) { Debug.LogWarning("[ColorSkinModel] recolour skipped: " + e.Message); }
        }
        void ColorSkinModelCore(Transform modelTf, GameObject srcPrefab, PieceColor color, VehicleType type)
        {
            GameObject matSrc = srcPrefab != null ? srcPrefab : modelTf.gameObject;
            var modelRends = modelTf.GetComponentsInChildren<Renderer>(true);
            var srcRends = matSrc.GetComponentsInChildren<Renderer>(true);

            // Tint ONLY the BODY = the single LARGEST mesh submesh (the painted shell). Everything ELSE is left exactly
            // as shipped: the separate wheel renderers (TYRES), and the body's OTHER submesh (lights / brake lights /
            // glass / mirrors / bumpers). A Mega Pack car is one shell submesh + a details submesh + 4 wheel renderers
            // that ALL share one colour-atlas material, so painting every atlas slot used to colour the tyres & trim
            // too — this paints just the shell. (renderer index maps prefab->instance 1:1, same hierarchy order.)
            var (bodyRend, bodySlot) = FindSkinBody(matSrc);
            int bodyIdx = bodyRend != null ? System.Array.IndexOf(srcRends, bodyRend) : -1;
            if (bodyIdx < 0 || bodyIdx >= modelRends.Length) return;
            var rend = modelRends[bodyIdx];

            // Route by VEHICLE TYPE, not a material heuristic (the old heuristic misrouted some .glb vans into the atlas
            // path, leaving the shell its native colour with only the band painted = "colours mixed up"). SEDANS (Car)
            // are Mega Pack FBX with a shared colour-ATLAS body -> recolour the swatch band (keeps glass + bumpers).
            // MINIVANS + BUSES are .glb single-texture shells -> recolour BY VALUE (RecoloredVanTex): repaint the body
            // pixels to the palette colour, keep the dark windows/wheels. So vans/buses now recolour exactly like the
            // sedans look — clean body, windows intact. (A Car that is actually a .glb falls through to the value path.)
            var origMats = bodyRend.sharedMaterials;
            Material origBody = bodySlot < origMats.Length ? origMats[bodySlot] : null;
            bool atlasSedan = type == VehicleType.Car
                              && origBody != null
                              && !origBody.HasProperty("baseColorFactor")
                              && !origBody.HasProperty("baseColorTexture");
            var m = rend.sharedMaterials;
            if (bodySlot < m.Length && origBody != null)
            {
                m[bodySlot] = atlasSedan ? RecoloredAtlasMat(origBody, color) : RecoloredVanMat(origBody, color);
                rend.sharedMaterials = m; // cached per (material, colour) on the SHARED slot — same colour reuses, different colours don't bleed
            }
        }

        // --- Mega Pack atlas recolour ---------------------------------------------------------------------
        // A Mega Pack car's whole paint is ONE shared COLOUR ATLAS: a horizontal BAND of body-colour swatches, plus
        // glass / chrome / black regions for windows / bumpers / tyres. To get a clean gameplay-colour body WITHOUT
        // touching the windows + bumpers, recolour ONLY that body band to the match colour and leave the rest. V0..V1
        // = the UV-y range of the swatch band (tune to the atlas; verified live). Cached per (texture, colour).
        const float AtlasBodyV0 = 0.22f, AtlasBodyV1 = 0.76f;
        static readonly Dictionary<(Texture, PieceColor), Texture2D> atlasRecolorCache = new Dictionary<(Texture, PieceColor), Texture2D>(); // static: a full-texture repaint is paid once per app run
        Texture2D RecoloredAtlas(Texture2D src, PieceColor color)
        {
            if (src == null) return null;
            var key = ((Texture)src, color);
            if (atlasRecolorCache.TryGetValue(key, out var cached) && cached != null) return cached;
            int w = src.width, h = src.height;
            Color32[] px;
            try { px = src.GetPixels32(); }              // needs "Read/Write Enabled" on the texture import
            catch (UnityException) { atlasRecolorCache[key] = null; return null; }
            Color32 c = PeopleColor(color);
            int y0 = Mathf.Clamp(Mathf.RoundToInt(AtlasBodyV0 * h), 0, h);
            int y1 = Mathf.Clamp(Mathf.RoundToInt(AtlasBodyV1 * h), 0, h);
            for (int y = y0; y < y1; y++) { int row = y * w; for (int x = 0; x < w; x++) px[row + x] = c; }
            var dst = new Texture2D(w, h, TextureFormat.RGBA32, false) { name = src.name + "_" + color };
            dst.SetPixels32(px); dst.Apply(false);
            atlasRecolorCache[key] = dst;
            return dst;
        }

        // A clone of the atlas body material whose texture has the body band recoloured to the match colour. If the
        // source texture isn't readable, falls back to the flat palette colour (paints the whole shell, but clean).
        static readonly Dictionary<(Material, PieceColor), Material> atlasMatCache = new Dictionary<(Material, PieceColor), Material>(); // static: survives scene re-entry
        Material RecoloredAtlasMat(Material bodyMat, PieceColor color)
        {
            var key = (bodyMat, color);
            if (atlasMatCache.TryGetValue(key, out var m) && m != null) return m;
            var srcTex = (bodyMat.HasProperty("_BaseMap") ? bodyMat.GetTexture("_BaseMap") : null) as Texture2D
                      ?? (bodyMat.HasProperty("_MainTex") ? bodyMat.GetTexture("_MainTex") : null) as Texture2D;
            var rt = RecoloredAtlas(srcTex, color);
            if (rt == null) { var fb = bodyMats.TryGetValue(color, out var bm) ? bm : bodyMat; atlasMatCache[key] = fb; return fb; }
            m = new Material(bodyMat);
            if (m.HasProperty("_BaseMap")) m.SetTexture("_BaseMap", rt);
            if (m.HasProperty("_MainTex")) m.SetTexture("_MainTex", rt);
            if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", Color.white);
            if (m.HasProperty("_Color")) m.SetColor("_Color", Color.white);
            atlasMatCache[key] = m;
            return m;
        }

        // --- .glb van/bus recolour (single-material model: one texture bakes in body + windows + wheels) -------------
        // There's no swatch BAND to recolour like the Mega Pack atlas — the van is one textured shell, so find the body
        // BY COLOUR: a model with a distinct paint HUE (azure/red/yellow) repaints just that hue; a neutral white/silver/
        // grey model repaints its LIGHT shell. Either way windows, tyres and lights are KEPT at their original pixel so
        // they survive untouched, and the body becomes the flat gameplay colour (sedan body + boarding people match it).
        // Reads via a GPU copy (glb textures aren't CPU-readable).
        static readonly Dictionary<(Texture, PieceColor), Texture2D> vanRecolorCache = new Dictionary<(Texture, PieceColor), Texture2D>(); // static: the GPU-readback recolour is paid once per app run
        Texture2D RecoloredVanTex(Texture src, PieceColor color)
        {
            if (src == null) return null;
            var key = (src, color);
            if (vanRecolorCache.TryGetValue(key, out var cached)) return cached; // cached value may be null (un-recolourable)
            int w = src.width, h = src.height;
            // Cap the working resolution (256) so the recolour stays cheap on the synchronous level build AND on the
            // mystery reveal — the body becomes a FLAT colour anyway, so the cap only softens the kept windows/wheels a touch.
            const int Max = 256;
            if (w > Max || h > Max) { float s = (float)Max / Mathf.Max(w, h); w = Mathf.Max(1, Mathf.RoundToInt(w * s)); h = Mathf.Max(1, Mathf.RoundToInt(h * s)); }
            Color32[] px = ReadPixels32(src, w, h);
            if (px == null) { vanRecolorCache[key] = null; return null; }
            Color32 c = PeopleColor(color);

            // These chaotic single-mesh .glb vehicles bake the WHOLE model (body + windows + tyres + lights) into ONE
            // texture with no separable submesh, so "body only" is decided PER-PIXEL — and windows + tyres are always
            // LEFT AT THEIR ORIGINAL pixel (never recoloured, never flattened to a fake grey). Two body cases:
            //   • COLOURED body (azure / red / yellow bus): the body is a distinct HUE -> repaint ONLY that hue.
            //   • NEUTRAL white / silver / grey body: no body hue -> the body is the LIGHT shell, split from the darker
            //     windows/tyres by an Otsu threshold over the NON-near-black luminance (so the unused black UV-atlas
            //     background + deep shadow can't drag the split onto the body). Adapts across white..silver.
            // A truly BLACK-bodied van can't be told apart from its own black glass — it keeps its dark shell (the one
            // case a single baked texture genuinely can't separate) rather than smearing colour across the windows.

            // body paint hue = peak of a saturation-weighted hue histogram over the BRIGHT saturated pixels (value >= 0.35
            // so DARK tinted glass — e.g. the Classic bus's navy windows — isn't mistaken for a coloured body and painted
            // over); also tally the bright-WHITE pixels so a coloured bus with a big white roof can have that roof painted.
            var hueHist = new float[36];
            int satCount = 0, whiteCount = 0;
            for (int i = 0; i < px.Length; i++)
            {
                Color.RGBToHSV(new Color(px[i].r / 255f, px[i].g / 255f, px[i].b / 255f), out float ph, out float ps, out float pv);
                if (ps >= 0.25f && pv >= 0.35f) { hueHist[Mathf.Clamp((int)(ph * 36f), 0, 35)] += ps; satCount++; }
                if (pv >= 0.82f && ps <= 0.12f) whiteCount++;
            }
            bool coloredBody = (float)satCount / px.Length >= 0.10f;
            // A coloured bus often has a WHITE roof (Fleet, Classic) the hue test would leave white. If white is a BIG
            // area (a roof — not just a few light bits / headlights) paint it the body colour too so the roof matches.
            bool paintRoofWhite = coloredBody && (float)whiteCount / px.Length >= 0.04f;
            float bodyHue = 0f;
            if (coloredBody)
            {
                int pk = 0; float pkv = -1f;
                for (int k = 0; k < 36; k++) if (hueHist[k] > pkv) { pkv = hueHist[k]; pk = k; }
                bodyHue = (pk + 0.5f) / 36f;
            }

            // neutral fallback: Otsu threshold over the luminance of the non-near-black pixels (skip < ~0.063)
            float lumThr = 0.5f;
            if (!coloredBody)
            {
                var lhist = new int[256];
                int lcount = 0;
                for (int i = 0; i < px.Length; i++)
                {
                    float l = 0.299f * px[i].r / 255f + 0.587f * px[i].g / 255f + 0.114f * px[i].b / 255f;
                    int bI = Mathf.Clamp((int)(l * 255f), 0, 255);
                    if (bI >= 16) { lhist[bI]++; lcount++; }
                }
                if (lcount > 0)
                {
                    float sum = 0f; for (int t = 16; t < 256; t++) sum += t * (float)lhist[t];
                    float sumB = 0f, wB = 0f, maxVar = -1f; int best = 128;
                    for (int t = 16; t < 256; t++)
                    {
                        wB += lhist[t]; if (wB == 0f) continue;
                        float wF = lcount - wB; if (wF <= 0f) break;
                        sumB += t * (float)lhist[t];
                        float mB = sumB / wB, mF = (sum - sumB) / wF, diff = mB - mF;
                        float between = wB * wF * diff * diff;
                        if (between > maxVar) { maxVar = between; best = t; }
                    }
                    lumThr = best / 255f;
                }
            }

            // paint ONLY the body; every other pixel (windows, tyres, lights, trim) is left ORIGINAL
            for (int i = 0; i < px.Length; i++)
            {
                float r = px[i].r / 255f, g = px[i].g / 255f, b = px[i].b / 255f;
                bool isBody;
                if (coloredBody)
                {
                    Color.RGBToHSV(new Color(r, g, b), out float ph, out float ps, out float pv);
                    float hd = Mathf.Abs(ph - bodyHue); if (hd > 0.5f) hd = 1f - hd;       // circular hue distance
                    isBody = (ps >= 0.12f && pv >= 0.10f && hd < 0.075f)                    // same paint hue, any brightness
                          || (paintRoofWhite && pv >= 0.82f && ps <= 0.12f);                // ...or the big white roof
                }
                else
                {
                    float l = 0.299f * r + 0.587f * g + 0.114f * b;
                    isBody = l >= lumThr;                                            // light shell = body
                }
                if (isBody) px[i] = c;
            }
            var dst = new Texture2D(w, h, TextureFormat.RGBA32, false) { name = src.name + "_van_" + color };
            dst.SetPixels32(px); dst.Apply(false);
            vanRecolorCache[key] = dst;
            return dst;
        }

        // Read a texture's pixels at the target w x h: GetPixels32 if it's already that size + CPU-readable, else a GPU
        // blit (which downscales) -> ReadPixels copy (works for non-readable + glb, and bounds the CPU pixel work).
        static Color32[] ReadPixels32(Texture src, int w, int h)
        {
            if (src is Texture2D t2 && t2.width == w && t2.height == h) { try { return t2.GetPixels32(); } catch (UnityException) { } }
            var rt = RenderTexture.GetTemporary(w, h, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            Graphics.Blit(src, rt);
            var prev = RenderTexture.active; RenderTexture.active = rt;
            var tmp = new Texture2D(w, h, TextureFormat.RGBA32, false);
            tmp.ReadPixels(new Rect(0, 0, w, h), 0, 0); tmp.Apply(false);
            RenderTexture.active = prev; RenderTexture.ReleaseTemporary(rt);
            var px = tmp.GetPixels32();
            Object.Destroy(tmp);
            return px;
        }

        // A clone of the .glb body material whose texture has the light shell repainted to the match colour (windows +
        // wheels kept). Falls back to the flat palette material (solid colour, no window detail) if the texture can't be
        // read or the model isn't light-bodied. Cached per (material, colour).
        readonly Dictionary<(Material, PieceColor), Material> vanMatCache = new Dictionary<(Material, PieceColor), Material>();
        Material RecoloredVanMat(Material bodyMat, PieceColor color)
        {
            var key = (bodyMat, color);
            if (vanMatCache.TryGetValue(key, out var m) && m != null) return m;
            var tex = RecoloredVanTex(GetAlbedo(bodyMat), color);
            if (tex == null) { var fb = bodyMats.TryGetValue(color, out var bm) ? bm : bodyMat; vanMatCache[key] = fb; return fb; }
            m = new Material(bodyMat);
            if (m.HasProperty("_BaseMap")) m.SetTexture("_BaseMap", tex);
            if (m.HasProperty("_MainTex")) m.SetTexture("_MainTex", tex);
            if (m.HasProperty("baseColorTexture")) m.SetTexture("baseColorTexture", tex);
            if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", Color.white); // texture now carries the colour -> don't multiply again
            if (m.HasProperty("_Color")) m.SetColor("_Color", Color.white);
            if (m.HasProperty("baseColorFactor")) m.SetColor("baseColorFactor", Color.white);
            vanMatCache[key] = m;
            return m;
        }

        // Clone a model's material and drive its base colour to the match colour (keeps the texture + shader, so a
        // textured palette model tints toward the gameplay colour instead of being flattened). Cached per (material, colour).
        readonly Dictionary<(Material, PieceColor), Material> skinTintCache = new Dictionary<(Material, PieceColor), Material>();
        Material TintExistingMat(Material baseM, PieceColor color)
        {
            var key = (baseM, color);
            if (skinTintCache.TryGetValue(key, out var m) && m != null) return m;
            m = new Material(baseM);
            Color c = PeopleColor(color);
            if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", c);
            if (m.HasProperty("_Color")) m.SetColor("_Color", c);
            // glTF-imported bodies (the othercars .glb vehicles) expose their base colour as the glTF-spec
            // "baseColorFactor" (NOT _BaseColor), so set that too. The body texture is then MULTIPLIED by the
            // match colour: the light body shell turns the gameplay colour, while the baked-dark windows, tyres
            // and headlights stay dark and untouched. Without this the .glb kept its raw grey texture (never
            // took its colour), which is why the new bus rendered grey instead of red/yellow/etc.
            if (m.HasProperty("baseColorFactor")) m.SetColor("baseColorFactor", c);
            skinTintCache[key] = m;
            return m;
        }

        // World-space "PEOPLE LEFT" sign on a post beside the road. Billboards to the camera (world-space
        // canvas + flip-cancel, like the crawler badge). Wired to the LOGICAL pool via UpdatePeopleLeft.
        void BuildPeopleLeftSign()
        {
            float signW = 1.05f, signH = 1.56f;   // people-count sign size — bumped again (~1.28x of the previous 0.82x1.22); board, neon frame + text all derive from these, and the on-screen clamp uses frameHalf so it stays in view
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

            // Neon count + caption. Board/frame stay STRAIGHT (axis-aligned, facing -Z) so the sign never looks skewed.
            // The text is FIXED to that same -Z facing (NOT billboarded) so it stays parallel to the board and just in
            // FRONT of it -> the count can't tilt behind the board (which clipped its lower half) AND nothing rotates.
            var go = new GameObject("PeopleLeftSign", typeof(RectTransform), typeof(Canvas));
            go.transform.SetParent(boardRoot, false);
            go.GetComponent<Canvas>().renderMode = RenderMode.WorldSpace;
            ((RectTransform)go.transform).sizeDelta = new Vector2(120, 120);
            go.transform.position = new Vector3(sx, topY + 0.4f, sz - 0.06f);
            go.transform.localRotation = Quaternion.Euler(0f, 180f, 0f);          // face the camera (-Z) upright — no billboard tilt/roll
            go.transform.localScale = new Vector3(-1f, 1f, 1f) * (signW / 120f);  // -X un-mirrors the 180° flip; width matches the board

            Color neon = new Color(0.3f, 1f, 0.8f); // bright neon cyan-green (blooms on capable devices)
            AddSignText(go.transform, "LEFT", 22, new Vector2(0, 0.80f), new Vector2(1, 1f), neon);    // small caption pinned to the very top
            peopleLeftSign = AddSignText(go.transform, PeopleLeft().ToString(), 64, new Vector2(0, 0f), new Vector2(1, 1f), neon); // number fills the WHOLE board -> centered + clearly visible
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
        // ---- Anti-overlap for concurrent on-road exits ("move together, never touch") -------------------------
        // True if moving THIS exiting vehicle to `pos` would bring its body within touching distance of another
        // IN-FLIGHT vehicle that has the right-of-way (a LOWER exitSeq = started earlier). Each vehicle is a
        // CAPSULE — its true body SEGMENT (centre +/- forward*(halfLen-halfW)) plus a halfW radius — and we test
        // the real segment-to-segment distance. (The old centre-lozenge over-covered a long bus's DIAGONAL corners,
        // which falsely pinned a car sitting BESIDE a perpendicular bus's END until the bus moved — the "parallel
        // cross" bug.) Yielding ONLY to lower-seq peers means the earliest mover is never blocked -> never deadlocks.
        bool WouldOverlapPeer(Bus bus, Vector3 pos)
        {
            // Capsule width EXACTLY equals the rendered body: BuildVehicle makes it w = CellSize*0.52, so the half-width
            // is CellSize*0.26. buffer = 0 => a vehicle is blocked ONLY when the real bodies actually overlap, never on a
            // visible gap. (Was 0.42 + 0.18 padding, a ~1.5x-too-fat phantom that pinned a diagonal car whenever ANOTHER
            // vehicle merely sat beside it, even with a clearly-open lane ahead. "If the mesh doesn't collide, let it pass.")
            const float halfW = CellSize * 0.26f, buffer = 0f;
            Vector3 mf = bus.transform.forward; mf.y = 0f;
            mf = mf.sqrMagnitude < 1e-6f ? Vector3.forward : mf.normalized;
            float myHalf = Mathf.Max(0f, LowPolyBuilder.VehicleLength(bus.type, CellSize) * 0.5f - halfW); // capsule core half-length
            Vector2 myC = new Vector2(pos.x, pos.z), myD = new Vector2(mf.x, mf.z) * myHalf;
            Vector2 myA = myC - myD, myB = myC + myD;
            float rad = halfW + halfW + buffer;
            for (int i = 0; i < liveBuses.Count; i++)
            {
                var o = liveBuses[i];
                if (o == null || o == bus || o.state != BusState.MovingToSlot) continue; // only OTHER in-flight exiters
                if (o.exitSeq >= bus.exitSeq) continue;                                   // yield only to earlier (right-of-way) peers
                Vector3 of = o.transform.forward; of.y = 0f;
                of = of.sqrMagnitude < 1e-6f ? Vector3.forward : of.normalized;
                float oHalf = Mathf.Max(0f, LowPolyBuilder.VehicleLength(o.type, CellSize) * 0.5f - halfW);
                Vector3 op = o.transform.position;
                Vector2 oC = new Vector2(op.x, op.z), oD = new Vector2(of.x, of.z) * oHalf;
                if (Seg2SegSqXZ(myA, myB, oC - oD, oC + oD) < rad * rad) return true;
            }
            return false;
        }

        // Squared closest distance between 2D segments [p1,q1] and [p2,q2] (Ericson, Real-Time Collision Detection).
        static float Seg2SegSqXZ(Vector2 p1, Vector2 q1, Vector2 p2, Vector2 q2)
        {
            Vector2 d1 = q1 - p1, d2 = q2 - p2, r = p1 - p2;
            float a = Vector2.Dot(d1, d1), e = Vector2.Dot(d2, d2), f = Vector2.Dot(d2, r);
            float s, t;
            if (a <= 1e-7f && e <= 1e-7f) return Vector2.Dot(r, r);   // both degenerate to points
            if (a <= 1e-7f) { s = 0f; t = Mathf.Clamp01(f / e); }     // first is a point
            else
            {
                float c = Vector2.Dot(d1, r);
                if (e <= 1e-7f) { t = 0f; s = Mathf.Clamp01(-c / a); } // second is a point
                else
                {
                    float b = Vector2.Dot(d1, d2);
                    float denom = a * e - b * b;
                    s = denom > 1e-7f ? Mathf.Clamp01((b * f - c * e) / denom) : 0f;
                    t = (b * s + f) / e;
                    if (t < 0f) { t = 0f; s = Mathf.Clamp01(-c / a); }
                    else if (t > 1f) { t = 1f; s = Mathf.Clamp01((b - c) / a); }
                }
            }
            Vector2 c1 = p1 + d1 * s, c2 = p2 + d2 * t;
            return (c1 - c2).sqrMagnitude;
        }

        // REAL tilted-body clearance for DIAGONAL vehicles. The square grid can't represent a 45° body: its corner
        // cells either over-cover (false "won't move") or under-cover (drives THROUGH a neighbour = meshing). So for a
        // diagonal vehicle we ignore the grid and ask the actual question: can its capsule body slide along its arrow,
        // all the way off-board, WITHOUT ever overlapping a STATIC jam vehicle's capsule? True => a genuine gap exists
        // (let it out); false => something is really in the way (don't). In-flight peers are handled separately by
        // WouldOverlapPeer while driving. halfW/buffer are the same body size used there, and are the tuning knobs.
        bool BodySlideClear(Bus bus)
        {
            // Rebuild the SAME clearSteps the exit routine uses, so the body check and the real slide agree exactly.
            int maxSteps = ExitDistance(bus.cell, bus.dir) + bus.length;
            int clearSteps = maxSteps;
            float halfLen = bus.length * 0.55f;
            if (bus.dir.y > 0) // away from the stops
            {
                float deepestZ = ParkingZ;
                foreach (var kv in occ) { float z = CellWorld(kv.Key).z; if (z < deepestZ) deepestZ = z; }
                float underZ = Mathf.Max(deepestZ - (halfLen + 0.9f), ScreenFloorZ + halfLen + 0.25f);
                clearSteps = Mathf.Clamp(Mathf.CeilToInt((bus.transform.position.z - underZ) / CellSize), 1, maxSteps);
            }
            else if (bus.dir.y < 0) // toward the stops
                clearSteps = Mathf.Clamp(Mathf.CeilToInt((GridExitZ + halfLen + 0.5f - bus.transform.position.z) / CellSize), 1, maxSteps);
            Vector3 sp = bus.transform.position;
            // Validate the EXACT preserve-45° slide the exit takes: the full 45° lane, SHORTENED to stay on-screen
            // (NOT clamped — clamping bends the path). Checking the real path is what makes the body verdict trustworthy.
            Vector3 dn3 = new Vector3(bus.dir.x, 0f, -bus.dir.y);
            float mag = dn3.magnitude;
            if (mag < 1e-4f) return true;
            float slideDist = mag * (clearSteps * CellSize);
            dn3 /= mag;
            while (slideDist > CellSize && Mathf.Abs((sp + dn3 * slideDist).x) > VisHalfW((sp + dn3 * slideDist).z) - 1.0f) slideDist -= CellSize * 0.5f;
            if (slideDist < 1e-3f) return true;
            Vector2 wd = new Vector2(dn3.x, dn3.z); // the real 45° slide direction
            const float halfW = CellSize * 0.26f, buffer = 0f; // capsule == the real rendered body (w=CellSize*0.52); zero padding so a diagonal vehicle is blocked ONLY by an actual mesh overlap, never by a neighbour beside a clear lane (see WouldOverlapPeer)
            float rad2 = (halfW + halfW + buffer) * (halfW + halfW + buffer);
            Vector3 mf = bus.transform.forward; mf.y = 0f;
            mf = mf.sqrMagnitude < 1e-6f ? new Vector3(wd.x, 0f, wd.y) : mf.normalized;
            float myHalf = Mathf.Max(0f, LowPolyBuilder.VehicleLength(bus.type, CellSize) * 0.5f - halfW);
            Vector2 mfd = new Vector2(mf.x, mf.z) * myHalf;
            Vector2 start = new Vector2(sp.x, sp.z);
            float maxDist = slideDist + bus.length * CellSize; // a little past the endpoint so the whole body clears
            float stepLen = CellSize * 0.2f; // fine enough that a glancing pass can't slip between samples
            for (int i = 0; i < liveBuses.Count; i++)
            {
                var o = liveBuses[i];
                if (o == null || o == bus) continue;
                if (o.state != BusState.Queued && o.state != BusState.Staging) continue; // only STATIC jam vehicles
                Vector3 op = o.transform.position; Vector2 oC = new Vector2(op.x, op.z);
                Vector3 of = o.transform.forward; of.y = 0f;
                of = of.sqrMagnitude < 1e-6f ? Vector3.forward : of.normalized;
                float oHalf = Mathf.Max(0f, LowPolyBuilder.VehicleLength(o.type, CellSize) * 0.5f - halfW);
                Vector2 oD = new Vector2(of.x, of.z) * oHalf;
                Vector2 oA = oC - oD, oB = oC + oD;
                // DRIVING INTO o == the body overlaps o AND is CLOSER to o than it was at the start. A beside/behind
                // neighbour (even a packed one we're touching) only gets FARTHER as we slide away, so it never trips
                // this -> ignored correctly, with NO first-step shortcut that could skip a vehicle we pass close to.
                float dist0sq = Seg2SegSqXZ(start - mfd, start + mfd, oA, oB);
                for (float d = stepLen; d <= maxDist; d += stepLen)
                {
                    Vector2 myC = start + wd * d;
                    float dsq = Seg2SegSqXZ(myC - mfd, myC + mfd, oA, oB);
                    if (dsq < rad2 && dsq < dist0sq - 1e-4f) return false;  // overlapping AND closer than at the start = meshing into it
                    if (Vector2.Dot(oC - myC, wd) < -CellSize * 2f) break;  // slid well past it; stop checking this one
                }
            }
            return true;
        }

        // Same spline follower as DrivePath, but it HOLDS position any frame its next step would touch a
        // right-of-way peer. Used for the on-road drive, where there's no grid corridor to reserve.
        IEnumerator DrivePathYield(Bus bus, List<Vector3> pts, float speed, float turnLerp)
        {
            if (bus == null || pts == null || pts.Count == 0) yield break;
            var t = bus.transform;
            var c = new List<Vector3>(pts.Count + 1) { t.position };
            c.AddRange(pts);
            if (c.Count < 2) yield break;
            Vector3 C(int i) => c[Mathf.Clamp(i, 0, c.Count - 1)];
            var s = new List<Vector3> { c[0] };
            for (int i = 0; i < c.Count - 1; i++)
            {
                int n = Mathf.Max(2, Mathf.CeilToInt(Vector3.Distance(C(i), C(i + 1)) / 0.1f));
                for (int k = 1; k <= n; k++) s.Add(CatmullRom(C(i - 1), C(i), C(i + 1), C(i + 2), k / (float)n));
            }
            int idx = 0;
            while (idx < s.Count - 1)
            {
                if (bus == null) yield break;
                float move = Mathf.Max(speed, 0.01f) * Time.deltaTime;
                while (idx < s.Count - 1 && move > 0f)
                {
                    Vector3 target = s[idx + 1];
                    float d = Vector3.Distance(t.position, target);
                    Vector3 cand = (d <= move) ? target : Vector3.MoveTowards(t.position, target, move);
                    if (WouldOverlapPeer(bus, cand)) break;          // a right-of-way peer is in the way -> hold this frame
                    if (d <= move) { move -= d; t.position = target; idx++; }
                    else { t.position = cand; break; }
                }
                Vector3 look = s[Mathf.Min(idx + 1, s.Count - 1)] - t.position;
                if (look.sqrMagnitude > 1e-5f)
                    t.rotation = Quaternion.Slerp(t.rotation, Quaternion.Euler(0, Mathf.Atan2(-look.x, -look.z) * Mathf.Rad2Deg, 0),
                                                  1f - Mathf.Exp(-turnLerp * Time.deltaTime));
                yield return null;
            }
            if (bus != null) t.position = s[s.Count - 1];
        }

        // Straight slide (no rotation, no spline) that HOLDS when its next step would touch a right-of-way peer — so a
        // follower can trail the vehicle ahead out of the jam without rear-ending it.
        IEnumerator MoveToYield(Bus bus, Vector3 target, float speed)
        {
            if (bus == null) yield break;
            var t = bus.transform;
            while (bus != null && (t.position - target).sqrMagnitude > 1e-4f)
            {
                Vector3 cand = Vector3.MoveTowards(t.position, target, Mathf.Max(speed, 0.01f) * Time.deltaTime);
                if (!WouldOverlapPeer(bus, cand)) t.position = cand;   // else hold this frame
                yield return null;
            }
            if (bus != null) t.position = target;
        }

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
            if (heliBodyMat == null)   heliBodyMat   = MaterialLibrary.MakeRuntime(new Color(0.16f, 0.46f, 0.82f), 0.4f);  // sky-blue shell (built once, reused by every heli joker)
            if (heliAccentMat == null) heliAccentMat = MaterialLibrary.MakeRuntime(new Color(0.97f, 0.78f, 0.16f), 0.35f); // warm yellow accent (fin/hub/hook)
            roadMat      = MaterialLibrary.MakeRuntime(new Color(0.16f, 0.17f, 0.19f), 0.18f);       // STANDARD dark asphalt — CLEAN flat surface: removed the faint facet TEXTURE that read as old thin lines on the road; the dashed centre line is the only marking now
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

        // A tileable low-poly GROUND texture: a grid of flat-shaded triangle facets with a subtle per-facet brightness
        // variation. GREYSCALE, so the ground material's _BaseColor still supplies the THEME hue (texture x colour) ->
        // the ground reads as low-poly terrain in each theme's own colour. Built once + cached (one small texture).
        // Shared LOW-POLY FACET texture factory: greyscale triangle facets (multiplies the material's _BaseColor
        // tint), cached per look. cells = facets per tile edge; bMin..1 = facet brightness range (lower = bolder).
        // The grid hash wraps -> tiles seamlessly. One 128² texture per distinct look, cached for the app run.
        static readonly Dictionary<int, Texture2D> _facetTexCache = new Dictionary<int, Texture2D>();
        static Texture2D FacetTex(int cells, float bMin)
        {
            int key = cells * 1000 + Mathf.RoundToInt(bMin * 100f);
            if (_facetTexCache.TryGetValue(key, out var cached) && cached != null) return cached;
            const int N = 128;
            float cs = (float)N / cells, range = 1f - bMin;
            var px = new Color32[N * N];
            for (int y = 0; y < N; y++)
                for (int x = 0; x < N; x++)
                {
                    int cx = (int)(x / cs), cy = (int)(y / cs);
                    float fx = x / cs - cx, fy = y / cs - cy;
                    bool flip = (((cx * 49157) ^ (cy * 98317)) & 1) == 1; // alternate the SPLIT diagonal per cell -> varied triangle orientations (not a uniform stripe)
                    bool upper = flip ? (fx + fy > 1f) : (fx > fy);        // which of the cell's two triangles this pixel is in
                    int h = (cx * 73856093) ^ (cy * 19349663) ^ (upper ? 83492791 : 26949127); // per-facet hash (wraps mod cells -> seamless)
                    byte v = (byte)((bMin + range * ((h & 1023) / 1023f)) * 255f);
                    px[y * N + x] = new Color32(v, v, v, 255);
                }
            var tex = new Texture2D(N, N, TextureFormat.RGBA32, true) { wrapMode = TextureWrapMode.Repeat, filterMode = FilterMode.Point };
            tex.SetPixels32(px);
            tex.Apply(true);
            _facetTexCache[key] = tex;
            return tex;
        }

        static Texture2D LowPolyGroundTex() => FacetTex(8, 0.90f); // ground: SUBTLE grain (bolder read as confusing)

        // A COPY of `src` with the facet grain applied — never mutates src (theme materials can be shared assets;
        // a mutated asset would persist in the editor). tile = repeats across the mesh's 0..1 UVs (per cube face).
        static Material Faceted(Material src, int cells, float bMin, float tile)
        {
            if (src == null) return null;
            var m = new Material(src);
            m.mainTexture = FacetTex(cells, bMin);
            m.mainTextureScale = new Vector2(tile, tile);
            return m;
        }

        // Paint the facet texture onto a ground-band material, tiled so each facet is ~0.6 world units (square facets
        // regardless of the band's depth). The material keeps its theme _BaseColor as the tint.
        static void ApplyLowPolyGround(Material m, Vector3 size)
        {
            if (m == null) return;
            const float repeat = 5f; // world units per texture tile (8 facets across a tile -> ~0.6-unit facets)
            m.mainTexture = LowPolyGroundTex();
            m.mainTextureScale = new Vector2(size.x / repeat, size.z / repeat);
        }

        // Pack prefabs whose name contains ANY key (lowercase match), e.g. ("fir") -> the fir tree. Null when the
        // pool is missing or nothing matches, so callers fall back explicitly (whole pool / procedural props).
        static GameObject[] FilterFx(GameObject[] pool, params string[] keys)
        {
            if (pool == null || pool.Length == 0) return null;
            var list = new List<GameObject>();
            foreach (var g in pool)
            {
                if (g == null) continue;
                string n = g.name.ToLowerInvariant();
                foreach (var k in keys)
                    if (n.Contains(k)) { list.Add(g); break; }
            }
            return list.Count > 0 ? list.ToArray() : null;
        }

        // THEME ENVIRONMENT — VISIBLE-ONLY dressing. The 2960x1440 portrait frustum (steep camera, FOV 52) shows
        // ground only for z ≈ -7.3..22 and |x| < VisHalfW(z) (≈4.6 at the jam front, ≈7 at the people band). The
        // old far rows (z 24/28.5) and the jam-side scatter (x ±6.8 at z < 8) were pure OFF-SCREEN cost — REMOVED.
        // The theme's cast (firs/palms/cacti/lamps/trees) now stands in the two side strips between the road and
        // the people band (z 9.6..13.8, x ±6.3..6.9) plus one accent in the open top-right corner — all ON screen,
        // all clear of gameplay lanes (road z7.4, stops |x|<=4.2 + bus-stop decor ±5.5, exits stay inside
        // VisHalfW-1). Fewer objects than the old scatter AND every one of them visible.
        void BuildThemeEnvironment(Theme th, Material main, Material alt, Material foliage, Material trunk, Material window)
        {
            // ---- cast the theme (what stands in the side strips) ---------------------------------------------
            GameObject[] sideTrees = null;
            bool sideProcedural = false, sideLamps = false, sideBench = false;
            PropKind procKind = th.prop;
            float sideSize = 1.5f;
            switch (th.name)
            {
                case "City": case "Night": case "Bonus":
                    sideTrees = FilterFx(cityTrees, "cube", "big");
                    sideLamps = true;                                   // avenue: a street light in the middle slot
                    break;
                case "Park":
                    sideTrees = FilterFx(cityTrees, "big tree", "cube");
                    sideBench = true;                                   // a park bench in the middle slot
                    break;
                case "Forest":
                    sideTrees = FilterFx(cityTrees, "fir", "big tree"); sideSize = 1.8f; // taller forest edge
                    break;
                case "Snow":
                    sideTrees = FilterFx(cityTrees, "fir");
                    break;
                case "Candy":
                    sideTrees = FilterFx(cityTrees, "cube");
                    break;
                case "Autumn":
                    sideTrees = FilterFx(cityTrees, "big tree", "cube");
                    break;
                case "Beach": case "Sunset":
                    sideProcedural = true; procKind = PropKind.Palm;    // no palms in the pack
                    break;
                case "Desert":
                    sideProcedural = true; procKind = PropKind.Cactus;  // no cacti in the pack
                    break;
                default:
                    sideTrees = cityTrees;
                    break;
            }
            if (sideTrees == null) sideProcedural = true;               // pack missing -> procedural props

            // ---- the two visible side strips (between road and people band) ---------------------------------
            var lamps   = FilterFx(cityProps, "light");
            var benches = FilterFx(cityProps, "bench");
            int slots = lowEnd ? 2 : (th.name == "Forest" ? 4 : 3);     // forest reads denser; low-end stays light
            float step = th.name == "Forest" ? 1.4f : 1.6f;
            for (int i = 0; i < slots; i++)
            {
                float z = 9.6f + i * step;                              // 9.6..13.8 — the strip the camera actually shows
                float x = 6.3f + 0.2f * i;                              // follow the widening frustum (deeper = wider)
                for (int s = -1; s <= 1; s += 2)
                {
                    if (sideLamps && i == 1 && lamps != null)
                    { FitDecor(lamps[0], new Vector3(x * s, 0, z), 1.7f, Quaternion.Euler(0, s > 0 ? -90f : 90f, 0)); continue; }
                    if (sideBench && i == 1 && benches != null)
                    { FitDecor(benches[0], new Vector3(x * s, 0, z), 1.0f, Quaternion.Euler(0, s > 0 ? -90f : 90f, 0)); continue; }
                    if (sideProcedural)
                        LowPolyBuilder.BuildProp(boardRoot, (i % 2 == 0) ? procKind : th.prop2, new Vector3(x * s, 0, z), main, alt, foliage, trunk, window, 1f);
                    else
                        FitDecor(sideTrees[(i + (s > 0 ? 1 : 0)) % sideTrees.Length], new Vector3(x * s, 0, z), sideSize, Quaternion.Euler(0, i * 47f + (s > 0 ? 90f : 0f), 0));
                }
            }

            // ---- one themed accent in the OPEN top-right corner (right of the terminal, on-screen at z=15) ---
            if (!lowEnd)
            {
                var spot = new Vector3(6.9f, 0, 15.0f);
                switch (th.name)
                {
                    case "City": case "Night": case "Bonus":
                        var towers = FilterFx(cityBuildings, "sky");
                        if (towers != null) FitDecor(towers[ThemePick(th, towers.Length, 5)], spot, 4.5f, Quaternion.Euler(0, 180f, 0));
                        break;
                    case "Beach": case "Sunset":
                        LowPolyBuilder.BuildProp(boardRoot, PropKind.Palm, spot, main, alt, foliage, trunk, window, 1.8f);
                        break;
                    case "Desert":
                        LowPolyBuilder.BuildProp(boardRoot, PropKind.Cactus, spot, main, alt, foliage, trunk, window, 1.7f);
                        break;
                    default:
                        if (!sideProcedural) FitDecor(sideTrees[0], spot, 2.2f, Quaternion.Euler(0, 130f, 0));
                        else LowPolyBuilder.BuildProp(boardRoot, procKind, spot, main, alt, foliage, trunk, window, 1.6f);
                        break;
                }
            }
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
                // Tiered shadows: Low skips the shadowmap pass entirely; Mid gets cheap HARD shadows; High gets soft.
                sun.shadows = DeviceSetup.DeviceTier == DeviceSetup.Tier.Low ? LightShadows.None
                            : DeviceSetup.DeviceTier == DeviceSetup.Tier.Mid ? LightShadows.Hard
                            : LightShadows.Soft;
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
            if (th.name == "Forest")
            {
                // Forest floor is DIRT/MUD, not lawn: earthy people band + darker packed-mud jam band. (The low-poly
                // ground texture below applies on top, so it reads as forest soil.)
                ground      = MaterialLibrary.MakeRuntimeMuted(new Color(0.52f, 0.40f, 0.27f), 0.30f, 0f);
                vehicleZone = MaterialLibrary.MakeRuntimeMuted(new Color(0.43f, 0.34f, 0.24f), 0.28f, 0f);
            }
            // Every env material below carries the low-poly FACET grain (a textured COPY per purpose — GetTheme can
            // return shared assets, never mutate those). Chunkier facets on foliage (leafy), finer on built stuff.
            // Windows/clouds stay CLEAN (glass + sky read better untextured). Same material count -> no extra batches.
            Material accent = Faceted(MaterialLibrary.GetTheme(th.name, "Accent", th.accent, 0.45f, 0.06f), 5, 0.87f, 2f);
            Material main   = Faceted(MaterialLibrary.GetTheme(th.name, "PropMain", th.propMain, 0.45f, 0.05f), 5, 0.87f, 2f);
            Material alt    = Faceted(MaterialLibrary.GetTheme(th.name, "PropAlt", th.propAlt, 0.45f, 0.05f), 5, 0.87f, 2f);
            Material foliage= Faceted(MaterialLibrary.GetTheme(th.name, "Foliage", th.foliage, 0.35f, 0.06f), 4, 0.82f, 2f);
            Material trunk  = Faceted(MaterialLibrary.GetTheme(th.name, "Trunk", th.trunk, 0.25f), 4, 0.84f, 1f);
            Material grass  = Faceted(MaterialLibrary.GetTheme(th.name, "Grass", th.grass, 0.30f, 0.06f), 4, 0.84f, 1f);
            Material window = MaterialLibrary.GetTheme(th.name, "Window", new Color(th.sky.r * 0.9f + 0.1f, th.sky.g * 0.9f + 0.1f, th.sky.b, 1f), 0.7f, 0.25f);
            Material cloud  = MaterialLibrary.GetTheme(th.name, "Cloud", new Color(1f, 1f, 1f), 0f, 0.18f);
            // slotMat is now a stable, editable asset set in BuildMaterials (no theme override).

            // The ground is TWO colors split at the ROAD, spanning the FULL width (sides included): the VEHICLE area
            // below the road (toward the camera) is gray; the PEOPLE area above the road is the themed ground. The
            // road slab at RoadZ hides the seam on-screen (it is off-screen at the far sides).
            const float gFront = -32f, gBack = 38f; // full ground z-extent (replaces the old field + central plaza)
            var vzSize = new Vector3(46f, 0.2f, RoadZ - gFront);
            var gdSize = new Vector3(46f, 0.2f, gBack - RoadZ);
            ApplyLowPolyGround(vehicleZone, vzSize); // low-poly faceted texture on BOTH ground bands (each keeps its own theme tint)
            ApplyLowPolyGround(ground, gdSize);
            LowPolyBuilder.Slab(boardRoot, new Vector3(0, -0.12f, (gFront + RoadZ) * 0.5f), vzSize, vehicleZone);
            LowPolyBuilder.Slab(boardRoot, new Vector3(0, -0.12f, (RoadZ + gBack) * 0.5f), gdSize, ground);
            // Distinct ROAD lane BELOW the parking stops (own band at RoadZ, between jam and stops) — full
            // buses drive off-screen sideways ALONG it. STANDARD asphalt every level (slimmed to a 1.0 lane).
            LowPolyBuilder.Slab(boardRoot, new Vector3(0, -0.10f, RoadZ), new Vector3(28f, 0.2f, 1.0f), roadMat);

            // (#8a) Dashed white CENTRE line down the road so the lane reads clearly (was plain asphalt). Flat paint
            // dashes sit right on the road surface (slab top = y 0.0), evenly spaced along the driving (X) axis.
            if (stripeMat != null)
            {
                const float dashLen = 0.72f, gap = 0.62f, dashH = 0.012f, dashY = dashH * 0.5f + 0.001f; // FLAT painted dashes: near-zero height so no raised 3D side face shows as a thin line under each dash
                for (float x = -12.6f; x <= 12.6f; x += dashLen + gap)
                {
                    var dash = LowPolyBuilder.Slab(boardRoot, new Vector3(x, dashY, RoadZ), new Vector3(dashLen, dashH, 0.15f), stripeMat);
                    var dr = dash.GetComponent<Renderer>();                       // paint never casts/receives shadows -> no thin
                    dr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; // shadow slivers can appear around the dashes
                    dr.receiveShadows = false;
                }
            }

            for (int i = -4; i <= 4; i++)
            {
                var post = MakeCube(boardRoot, accent, new Vector3(0.1f, 0.5f, 0.1f));
                post.transform.position = new Vector3(i * 1.1f, 0.25f, FenceZ);
                var bar = MakeCube(boardRoot, accent, new Vector3(1.1f, 0.06f, 0.05f));
                bar.transform.position = new Vector3(i * 1.1f + 0.55f, 0.34f, FenceZ);
            }

            // Per-theme world dressing: side scatter + far backdrop become what the theme IS (city avenue / park /
            // forest / beach / desert...). Gameplay chrome (portal, people, bus stops, road, vehicles) untouched.
            BuildThemeEnvironment(th, main, alt, foliage, trunk, window);

            // Behind the people band: a closed mall/terminal FACADE (people emerge from its doors), else
            // the legacy house centerpiece / prop row.
            doorXs = null;
            float backZ = PeopleZ + 4f;
            bool cityOk = cityBuildings != null && cityBuildings.Length > 0;
            if (th.hasFacade)
            {
                if (cityOk) BuildCityFacade(th, main, alt, foliage, trunk, window); // a real SimplePoly building people walk OUT of (sets doorXs/exitDoorX); flankers themed
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
            if (doorXs != null) BuildBoardingDoor(th, accent); // the glowing arched portal the queue steps out of

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
            var go = ModelPool.Get(prefab, boardRoot); // pooled: ~15 env prefabs per level rebuild -> recycled across levels
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
        void BuildCityFacade(Theme th, Material main, Material alt, Material foliage, Material trunk, Material window)
        {
            float doorX = 3.5f;
            doorXs = new[] { doorX };
            exitDoorX = doorX;
            FitDecor(cityBuildings[ThemePick(th, cityBuildings.Length, 0)], new Vector3(doorX, 0, FacadeZ + 2.2f), 4.5f, Quaternion.Euler(0, 180f, 0)); // the TERMINAL people exit — stays on every theme (portal untouched)
            if (lowEnd) return;
            // The two FLANKERS follow the theme, so the back block reads as the theme's world instead of always
            // being random city shops: cottages in Park/Candy/Autumn, firs at the Forest/Snow edge, palms on
            // Beach/Sunset, cacti in the Desert. City/Night/Bonus keep the shop block.
            Vector3 fa = new Vector3(doorX - 5.0f, 0, FacadeZ + 2.5f), fb = new Vector3(doorX - 9.0f, 0, FacadeZ + 2.3f);
            switch (th.name)
            {
                case "Park": case "Candy": case "Autumn":
                    var houses = FilterFx(cityBuildings, "house");
                    if (houses != null)
                    {
                        FitDecor(houses[0], fa, 3.2f, Quaternion.Euler(0, 180f, 0));
                        FitDecor(houses[houses.Length > 1 ? 1 : 0], fb, 3.0f, Quaternion.Euler(0, 180f, 0));
                        return;
                    }
                    break; // no houses in the pack -> shop block fallback below
                case "Forest": case "Snow":
                    var firs = FilterFx(cityTrees, "fir", "big tree");
                    if (firs != null)
                    {
                        FitDecor(firs[0], fa, 3.0f, Quaternion.Euler(0, 25f, 0));
                        FitDecor(firs[firs.Length > 1 ? 1 : 0], fb, 3.4f, Quaternion.Euler(0, 190f, 0));
                        return;
                    }
                    break;
                case "Beach": case "Sunset":
                    LowPolyBuilder.BuildProp(boardRoot, PropKind.Palm, fa, main, alt, foliage, trunk, window, 2.0f);
                    LowPolyBuilder.BuildProp(boardRoot, PropKind.Palm, fb, main, alt, foliage, trunk, window, 2.3f);
                    return;
                case "Desert":
                    LowPolyBuilder.BuildProp(boardRoot, PropKind.Cactus, fa, main, alt, foliage, trunk, window, 1.9f);
                    LowPolyBuilder.BuildProp(boardRoot, PropKind.Cactus, fb, main, alt, foliage, trunk, window, 2.2f);
                    return;
            }
            FitDecor(cityBuildings[ThemePick(th, cityBuildings.Length, 1)], fa, 3.6f, Quaternion.Euler(0, 180f, 0));
            FitDecor(cityBuildings[ThemePick(th, cityBuildings.Length, 2)], fb, 3.4f, Quaternion.Euler(0, 180f, 0));
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
            // City-ish themes + Park keep real street furniture; nature themes swap it for bushes (world-matching).
            GameObject[] streetPool = cityProps;
            if (th.name != "City" && th.name != "Night" && th.name != "Bonus" && th.name != "Park")
            { var bushes = FilterFx(cityTrees, "bush"); if (bushes != null) streetPool = bushes; }
            if (!lowEnd && streetPool != null && streetPool.Length > 0)
                for (int i = 0; i < 3; i++)
                {
                    float z = PeopleZ - 0.8f + i * 0.6f; // #3: kept FORWARD of the back buildings (no clipping, e.g. the fire hydrant) but still in the people zone
                    FitDecor(streetPool[ThemePick(th, streetPool.Length, i)],     new Vector3(-5.5f, 0, z), 1.0f, Quaternion.Euler(0, 90f, 0));
                    FitDecor(streetPool[ThemePick(th, streetPool.Length, i + 1)], new Vector3(5.5f, 0, z), 1.0f, Quaternion.Euler(0, -90f, 0));
                }

            BuildJamProps(th); // little theme props framing the FRONT of the jam (small; foreground, out of the slots)
        }

        // The GLOWING ARCHED PORTAL the boarding queue steps out of (exitDoorX, DoorSpawnZ), facing the buses (-Z):
        // a bright warm emissive panel with a rounded (arched) top set into a themed building-wall slab, framed by
        // side posts, wrapped in a soft warm halo that blooms onto the wall, with a flat awning above and a doorstep
        // below. Built every level that has a door, at the one exit door where DoorSpawn births the queue.
        void BuildBoardingDoor(Theme th, Material frameMat)
        {
            float x = exitDoorX, z = DoorSpawnZ;
            frameMat = Faceted(MaterialLibrary.MakeRuntimeMuted(new Color(0.58f, 0.55f, 0.52f), 0.3f), 5, 0.88f, 2f); // OUTSIDE frame/posts: FIXED neutral stone + facet grain — only the INSIDE glow carries the theme colour
            var wallMat = Faceted(MaterialLibrary.GetTheme(th.name, "DoorWall", th.propMain, 0.40f, 0.05f), 6, 0.88f, 3f); // themed building face with panel facets
            // The INSIDE light carries the theme colour. Theme accents are pale pastels, and pale x Vibrant() x 2.6
            // emission x bloom blew out to pure WHITE on screen — so force the hue SATURATED and keep the emission
            // moderate, so the glow visibly reads in the theme's colour (bloom only whitens the very core).
            Color.RGBToHSV(th.accent, out float ph, out float ps, out float pv);
            Color portalCol = Color.HSVToRGB(ph, Mathf.Max(ps, 0.65f), 1f);
            var glowMat = MaterialLibrary.MakeRuntime(portalCol, 0.5f, 1.5f);                        // theme-COLOURED portal light
            var haloMat = MaterialLibrary.MakeRuntime(portalCol, 0.6f, 0.85f);                       // matching halo bleeding onto the wall

            // Building-wall slab the portal is set into.
            MakeCube(boardRoot, wallMat, new Vector3(2.3f, 2.8f, 0.18f)).transform.position = new Vector3(x, 1.40f, z + 0.16f);

            // Soft warm halo AROUND the portal (between the wall and the bright panel) -> a glowy bloom edge on the wall.
            MakePrim(boardRoot, haloMat, PrimitiveType.Sphere, new Vector3(1.95f, 2.70f, 0.05f)).transform.position = new Vector3(x, 1.18f, z + 0.12f);

            // The bright glowing portal: a rectangular emissive body capped by a rounded (arched) top.
            MakeCube(boardRoot, glowMat, new Vector3(1.10f, 1.78f, 0.06f)).transform.position = new Vector3(x, 0.95f, z + 0.05f);
            MakePrim(boardRoot, glowMat, PrimitiveType.Sphere, new Vector3(1.10f, 1.10f, 0.06f)).transform.position = new Vector3(x, 1.84f, z + 0.05f);

            // Two side posts framing the opening (proud of the wall).
            MakeCube(boardRoot, frameMat, new Vector3(0.16f, 1.95f, 0.24f)).transform.position = new Vector3(x - 0.64f, 1.02f, z);
            MakeCube(boardRoot, frameMat, new Vector3(0.16f, 1.95f, 0.24f)).transform.position = new Vector3(x + 0.64f, 1.02f, z);

            // Flat awning over the entrance + a doorstep underfoot.
            MakeCube(boardRoot, frameMat, new Vector3(2.0f, 0.10f, 0.55f)).transform.position = new Vector3(x, 2.64f, z - 0.22f);
            MakeCube(boardRoot, frameMat, new Vector3(1.6f, 0.07f, 0.50f)).transform.position = new Vector3(x, 0.04f, z - 0.20f);
        }

        // Little theme props (small, fit-to-size) tucked into the FRONT CORNERS of the jam's foreground — out of the
        // slots/parking and mostly clear of the central exit lanes. Different per theme. No-op without the pack.
        void BuildJamProps(Theme th)
        {
            if (lowEnd) return;
            // City-ish themes (and the Park, which has real street furniture) keep hydrants/dustbins at the jam
            // corners; every other theme swaps them for BUSHES so the corners match the world (no hydrants in a forest).
            GameObject[] pool = cityProps;
            if (th.name != "City" && th.name != "Night" && th.name != "Bonus" && th.name != "Park")
            { var bushes = FilterFx(cityTrees, "bush"); if (bushes != null) pool = bushes; }
            if (pool == null || pool.Length == 0) return;
            Vector3[] spots = {
                new Vector3(-4.0f, 0, -5.2f), new Vector3(4.0f, 0, -5.2f),
                new Vector3(-3.6f, 0, -6.0f), new Vector3(3.6f, 0, -6.0f), // pulled in + shallower -> on-screen, clear of reversing away-exit buses
            };
            for (int i = 0; i < spots.Length; i++)
                FitDecor(pool[ThemePick(th, pool.Length, i + 3)], spots[i], 0.7f, Quaternion.Euler(0, i * 73f, 0));
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
            trafficCarSpeed = Mathf.Lerp(1.6f, 2.4f, diff);          // gentler top speed (was up to 3.0 — too fast to read)
            float period = Mathf.Lerp(22.0f, 8.0f, diff);          // FEWER cars + bigger gaps: L10 = 2 cars, L60+ = ~6 (was up to 8, too dense to win)
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
                    // cache the night headlights group so TrafficLoop can switch its real Spot Light OFF when the car is
                    // cleared on red (transform scale hides meshes but NOT a Light -> would otherwise leave light pools).
                    traffic.Add(new TrafficCar { tf = pivot.transform, x = x, dir = dir, lane = lane, headlights = pivot.transform.Find("Headlights") });
                }
            }
        }

        // ====================================================================
        // Real in-world traffic lights — one pole on the LEFT and one on the RIGHT of the road (bonus only).
        // The lit lamp tracks the red/green phase so the player reads the rules straight off the road, no HUD blob.
        // ====================================================================
        void BuildTrafficLights()
        {
            trafficRedLamps.Clear(); trafficGreenLamps.Clear();
            lampRedOn    = MaterialLibrary.MakeRuntime(new Color(1f, 0.16f, 0.13f), 0.35f, 2.4f);   // bright red glow (STOP)
            lampRedOff   = MaterialLibrary.MakeRuntime(new Color(0.22f, 0.04f, 0.04f), 0.25f, 0f);  // dark red lens
            lampGreenOn  = MaterialLibrary.MakeRuntime(new Color(0.28f, 1f, 0.40f), 0.35f, 2.4f);   // bright green glow (GO)
            lampGreenOff = MaterialLibrary.MakeRuntime(new Color(0.05f, 0.20f, 0.09f), 0.25f, 0f);  // dark green lens
            var amber    = MaterialLibrary.MakeRuntime(new Color(0.34f, 0.25f, 0.05f), 0.25f, 0f);  // dark amber (decorative middle lens)
            var bodyMat  = MaterialLibrary.MakeRuntime(new Color(0.10f, 0.11f, 0.13f), 0.30f, 0f);  // matte dark pole + housing

            var group = new GameObject("TrafficLights");
            group.transform.SetParent(boardRoot, false);

            float poleZ = RoadZ - 0.9f;                  // near curb: between the jam and the traffic lane (won't clip cars)
            float edgeX = VisHalfW(poleZ) - 0.9f;        // just inside the visible road edges -> clearly LEFT & RIGHT
            BuildTrafficLightPole(group.transform, -edgeX, poleZ, bodyMat, amber);
            BuildTrafficLightPole(group.transform,  edgeX, poleZ, bodyMat, amber);

            StripPhysics(group);                         // belt-and-braces: no collider near the road ever eats a tap
            SetTrafficLightsVisual(trafficGo);           // light the correct lamp for the starting phase (RED)
        }

        // One traffic-light pole: post + housing + three stacked lamps (red/amber/green) on the camera-facing (-Z) face.
        void BuildTrafficLightPole(Transform parent, float x, float z, Material bodyMat, Material amberMat)
        {
            float fz = z - 0.17f;                         // lamps sit on the housing's front (-Z) face -> visible top-down
            var pole = MakeCube(parent, bodyMat, new Vector3(0.16f, 2.4f, 0.16f));
            pole.transform.position = new Vector3(x, 1.20f, z);
            var housing = MakeCube(parent, bodyMat, new Vector3(0.42f, 1.10f, 0.30f));
            housing.transform.position = new Vector3(x, 2.55f, z);
            var red = MakeCube(parent, lampRedOff, new Vector3(0.26f, 0.26f, 0.10f));
            red.transform.position = new Vector3(x, 2.85f, fz);
            var amb = MakeCube(parent, amberMat, new Vector3(0.26f, 0.26f, 0.10f));
            amb.transform.position = new Vector3(x, 2.55f, fz);
            var green = MakeCube(parent, lampGreenOff, new Vector3(0.26f, 0.26f, 0.10f));
            green.transform.position = new Vector3(x, 2.25f, fz);
            trafficRedLamps.Add(red.GetComponent<Renderer>());
            trafficGreenLamps.Add(green.GetComponent<Renderer>());
        }

        // Swap the lit lamp on every pole: green glows on GO, red glows on STOP. Called only when the phase flips.
        void SetTrafficLightsVisual(bool go)
        {
            for (int i = 0; i < trafficRedLamps.Count; i++)
                if (trafficRedLamps[i]) trafficRedLamps[i].sharedMaterial = go ? lampRedOff : lampRedOn;
            for (int i = 0; i < trafficGreenLamps.Count; i++)
                if (trafficGreenLamps[i]) trafficGreenLamps[i].sharedMaterial = go ? lampGreenOn : lampGreenOff;
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
                var model = ModelPool.Get(src, pivot.transform); // pooled: a bonus board spawns a whole lane of these at once
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
            pivot.transform.localScale = Vector3.one * trafficVis; // start matching the light (0 on the opening RED -> hidden until green)
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
                // RED -> the cars CLEAR OFF the road (scale out) so it reads as genuinely empty and a crossing vehicle
                // can never drive into a stopped car; GREEN -> they return (scale in) and flow. Smoothly ramped.
                trafficVis = Mathf.MoveTowards(trafficVis, trafficGo ? 1f : 0f, Time.deltaTime / 0.28f);
                float step = trafficCarSpeed * Time.deltaTime;
                // Keep DRIVING while still visible (so cars roll off as they shrink instead of freezing full-size in
                // place for the ramp); only fully frozen once cleared (invisible) on a steady red.
                bool flow = trafficGo || trafficVis > 0.01f;
                bool lightsOn = trafficVis > 0.02f;
                for (int i = 0; i < traffic.Count; i++)
                {
                    var c = traffic[i];
                    if (c.tf == null) continue;
                    if (flow)
                    {
                        c.x += c.dir * step;
                        if (c.x >= trafficHalfLoop) c.x -= trafficLoop;
                        else if (c.x < -trafficHalfLoop) c.x += trafficLoop;
                    }
                    var p = c.tf.position; p.x = c.x; c.tf.position = p;
                    c.tf.localScale = Vector3.one * trafficVis;   // shrink out on red, grow back in on green
                    // also kill the real headlight Spot when cleared (scale-0 hides meshes but not a Light component)
                    if (c.headlights != null && c.headlights.gameObject.activeSelf != lightsOn)
                        c.headlights.gameObject.SetActive(lightsOn);
                }
                yield return null;
            }
        }

        // T3 crossing checkpoint (pure arithmetic against the x the loop just wrote — NO colliders). Always true
        // on non-bonus levels, so the normal dispatch path is untouched.
        bool RoadClearAt(float crossX, float clearance)
        {
            if (!IsBonus || traffic.Count == 0) return true;
            if (!trafficGo && trafficVis <= 0.5f) return true;   // red AND cars have actually cleared off -> road clear
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

            if (withSpot && DeviceSetup.HighEndDevice()) // ONE real shadowless spot — HIGH-END only; mid/low keep just the
            {                                            // cheap beam mesh above (real-time lights are costly on mobile,
                                                         // and bonus/night boards already stack many vehicles + overdraw)
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
            var go = ModelPool.Get(smokeFx, bus.transform); // pooled one-shot FX: no Instantiate/Destroy churn per dispatch
            StripPhysics(go);
            float halfLen = LowPolyBuilder.VehicleLength(bus.type, CellSize) * 0.5f;
            go.transform.localPosition = new Vector3(0f, 0.15f, halfLen); // right behind the rear
            go.transform.localRotation = Quaternion.identity;
            float smokeMul = bus.type == VehicleType.Bus ? 0.28f : 0.5f;  // buses are 2 cells long, so use a smaller multiplier -> their puff isn't ~2x the car's
            go.transform.localScale = Vector3.one * (halfLen * smokeMul); // ~0.25 for cars/minivans, ~0.28 for buses (tune the two factors)
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
            foreach (var ps in go.GetComponentsInChildren<ParticleSystem>(true))
            {
                var main = ps.main;
                main.scalingMode = ParticleSystemScalingMode.Hierarchy; // so the localScale above actually scales the puff (sizes + speeds)
                main.startLifetime = 0.8f;                              // shorter-lived puffs -> the smoke doesn't linger. SET (not *=) so pool reuse can't compound it away
                main.startColor = Color.Lerp(PeopleColor(bus.color), Color.white, 0.25f); // tint the puff toward the vehicle's colour (lightened a touch so it stays smoke-like)
                ps.Play();
            }
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
            const float fade = 0.6f;
            foreach (var ps in smoke.GetComponentsInChildren<ParticleSystem>(true))
            {
                ps.Stop(true, ParticleSystemStopBehavior.StopEmitting);
                int max = ps.particleCount;
                if (max <= 0) continue;
                if (particleBuf == null || particleBuf.Length < max) particleBuf = new ParticleSystem.Particle[Mathf.Max(max, 64)]; // shared scratch — no per-call alloc
                int n = ps.GetParticles(particleBuf);
                for (int i = 0; i < n; i++)
                    if (particleBuf[i].remainingLifetime > fade) particleBuf[i].remainingLifetime = fade;
                ps.SetParticles(particleBuf, n);
            }
            ModelPool.ReleaseAfter(smoke, fade + 0.25f); // back to the pool once the trail has thinned out (was Destroy)
        }

        // T6: one-shot impact burst at a world pos. Returns false on lowEnd / missing prefab so the caller can use
        // the cheap Juice.Burst fallback. Self-destructs after its lifetime; parented under boardRoot.
        bool SpawnHit(Vector3 pos)
        {
            if (lowEnd || hitFx == null) return false;
            var go = ModelPool.Get(hitFx, boardRoot); // pooled one-shot FX: no Instantiate/Destroy churn per blocked tap
            StripPhysics(go);
            FixParticleMaterials(go, ref hitMat, new Color(0.82f, 0.72f, 0.55f, 1f));
            go.transform.position = pos;
            var ps = go.GetComponentInChildren<ParticleSystem>();
            if (ps != null) ps.Play();                                                    // ensure the one-shot fires
            float life = ps != null ? ps.main.duration + ps.main.startLifetime.constantMax + 0.2f : 0.4f;
            ModelPool.ReleaseAfter(go, life); // back to the pool after the burst (was Destroy)
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
            // Toon outlines draw every model a SECOND time (inverted hull), so they're the single biggest gameplay-board
            // cost on weak/mid phones. HIGH tier only — Low AND Mid skip them (they still read fine without the ink edge).
            if (DeviceSetup.DeviceTier != DeviceSetup.Tier.High || go == null || toonOutlineFx == null) return;
            foreach (var mr in go.GetComponentsInChildren<MeshRenderer>(true))
            {
                if (mr.name == "SkinExtra") continue; // flat skin decal/wrap quad — no toon edge
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

        // All submesh slots point at the single shared outline material. Cached per slot-count: OutlineAll runs for
        // every model part on every build (and pooled models re-outline on reuse), so don't allocate a fresh array each time.
        readonly Dictionary<int, Material[]> outlineSlotCache = new Dictionary<int, Material[]>();
        Material[] OutlineSlots(int subMeshCount)
        {
            int n = Mathf.Max(1, subMeshCount);
            if (outlineSlotCache.TryGetValue(n, out var a) && a.Length == n && a[0] == toonOutlineFx) return a;
            a = new Material[n];
            for (int i = 0; i < a.Length; i++) a[i] = toonOutlineFx;
            outlineSlotCache[n] = a;
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
