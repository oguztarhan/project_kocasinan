using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.InputSystem;

namespace BusJam.Core
{
    /// <summary>
    /// ZERO-SETUP LAUNCHER. Press Play and the whole game builds itself — camera, lighting, ground,
    /// the passenger crowd, the holding slots, the bus queue, and a minimal HUD. No scene wiring, no
    /// prefabs, no ScriptableObject assets to author.
    ///
    /// It hooks in via <see cref="RuntimeInitializeOnLoadMethod"/>, so it runs no matter which scene
    /// is open. The procedurally-built game lives under one "BusJamGame" root; this launcher persists
    /// and rebuilds that root on demand (win / lose → tap to play again).
    ///
    /// This is intentionally a "composition root": the one place allowed to know about every manager
    /// and wire them together. The managers themselves stay decoupled (they talk via GameEvents).
    /// </summary>
    public class Bootstrap : MonoBehaviour
    {
        // Auto-spawn the launcher after the scene loads. Runs once per play session.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void AutoStart()
        {
            if (FindFirstObjectByType<Bootstrap>() != null) return;
            var go = new GameObject("BusJamBootstrap");
            go.AddComponent<Bootstrap>();
        }

        // --- Level shape (tweak freely) -----------------------------------------
        private const int Cols = 5;
        private const int BusCapacity = 3;
        private const int BusesPerColor = 2;            // 2 buses × 3 seats = 6 passengers / color
        private const float CellSize = 1.1f;
        private const int SlotCount = 7;
        private static readonly ColorType[] LevelColors = { ColorType.Red, ColorType.Blue, ColorType.Green };

        // --- Runtime refs --------------------------------------------------------
        private GameObject _root;
        private GameController _controller;
        private PassengerData _passengerData;
        private readonly List<ScriptableObject> _ownedAssets = new List<ScriptableObject>();

        // --- UI ------------------------------------------------------------------
        private Canvas _canvas;
        private Text _hud;
        private Text _banner;
        private Font _font;
        private bool _gameEnded;

        // ---------------------------------------------------------------------
        private void Awake()
        {
            DontDestroyOnLoad(gameObject);
            _font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf")
                 ?? Resources.GetBuiltinResource<Font>("Arial.ttf");
            BuildUI();
            Subscribe();
            BuildGame();
        }

        private void OnDestroy()
        {
            Unsubscribe();
            DestroyOwnedAssets();
        }

        private void Update()
        {
            if (_gameEnded && TapPressed()) Restart();
        }

        // ---------------------------------------------------------------------
        // Event glue → drives the HUD
        // ---------------------------------------------------------------------
        private void Subscribe()
        {
            GameEvents.ActiveBusChanged += OnBusChanged;
            GameEvents.PassengerBoarded += OnCountChanged;
            GameEvents.PassengerEnteredSlot += OnCountChanged;
            GameEvents.LevelCompleted += OnWin;
            GameEvents.GameOver += OnLose;
        }

        private void Unsubscribe()
        {
            GameEvents.ActiveBusChanged -= OnBusChanged;
            GameEvents.PassengerBoarded -= OnCountChanged;
            GameEvents.PassengerEnteredSlot -= OnCountChanged;
            GameEvents.LevelCompleted -= OnWin;
            GameEvents.GameOver -= OnLose;
        }

        private void OnBusChanged(Bus _) => UpdateHud();
        private void OnCountChanged(Passenger _) => UpdateHud();
        private void OnWin() { _gameEnded = true; _banner.text = "YOU WIN! 🎉\n\nTap to play again"; }
        private void OnLose(string reason) { _gameEnded = true; _banner.text = "GAME OVER\n" + reason + "\n\nTap to retry"; }

        // ---------------------------------------------------------------------
        // Build / rebuild the playable game
        // ---------------------------------------------------------------------
        private void Restart()
        {
            if (_root != null)
            {
                _root.SetActive(false); // fires managers' OnDisable → clean unsubscribe BEFORE the rebuild
                Destroy(_root);
            }
            DestroyOwnedAssets();
            BuildGame();
        }

        private void BuildGame()
        {
            _gameEnded = false;
            _banner.text = "";
            _root = new GameObject("BusJamGame");

            Camera cam = SetupCamera();
            SetupLight();
            BuildGround();

            // Scene anchors.
            Transform gridOrigin = Anchor("GridOrigin", new Vector3(-((Cols - 1) / 2f) * CellSize, 0.6f, 1.5f));
            Transform stop       = Anchor("Stop",       new Vector3(0f, 0.4f, -2f));
            Transform arriveFrom = Anchor("ArriveFrom", new Vector3(-12f, 0.4f, -2f));
            Transform departTo   = Anchor("DepartTo",   new Vector3(12f, 0.4f, -2f));

            Transform[] slots = new Transform[SlotCount];
            for (int i = 0; i < SlotCount; i++)
                slots[i] = Anchor("Slot" + i, new Vector3((i - (SlotCount - 1) / 2f) * 1.0f, 0.6f, 0f));

            // Managers (Awake/OnEnable fire here — subscriptions go live immediately).
            var grid  = _root.AddComponent<GridManager>();
            var bus   = _root.AddComponent<BusManager>();
            var slot  = _root.AddComponent<SlotManager>();
            var input = _root.AddComponent<InputManager>();
            _controller = _root.AddComponent<GameController>();

            // Data (created in code; tracked so we can destroy them on rebuild — no asset leak).
            _passengerData = Track(ScriptableObject.CreateInstance<PassengerData>());
            LevelConfig level = BuildLevel();

            // Wire dependencies, then start. Order matters: slots set BEFORE the controller calls Init().
            grid.SetOrigin(gridOrigin);
            bus.SetAnchors(stop, arriveFrom, departTo);
            slot.SetSlots(slots);
            slot.SetBusManager(bus);
            input.SetDependencies(cam, grid, bus, slot);
            _controller.Configure(grid, bus, slot, input, level, _passengerData);

            _controller.StartLevel(level);
            UpdateHud();
        }

        /// <summary>Builds a balanced, fully-consumable level: bus capacities exactly match the crowd.</summary>
        private LevelConfig BuildLevel()
        {
            // 1. Fill a bag with the exact number of each color the buses can take, then shuffle.
            var bag = new List<ColorType>();
            int perColor = BusCapacity * BusesPerColor;
            for (int ci = 0; ci < LevelColors.Length; ci++)
                for (int k = 0; k < perColor; k++)
                    bag.Add(LevelColors[ci]);

            for (int i = bag.Count - 1; i > 0; i--) // Fisher–Yates
            {
                int j = Random.Range(0, i + 1);
                (bag[i], bag[j]) = (bag[j], bag[i]);
            }

            // 2. Lay the bag into a grid (row 0 = front, nearest the bus).
            int total = bag.Count;
            int rows = Mathf.CeilToInt(total / (float)Cols);

            var level = Track(ScriptableObject.CreateInstance<LevelConfig>());
            level.cellSize = CellSize;
            level.slotCount = SlotCount;
            level.grid = new LevelConfig.GridRow[rows];

            int idx = 0;
            for (int r = 0; r < rows; r++)
            {
                var row = new LevelConfig.GridRow { cells = new ColorType[Cols] };
                for (int c = 0; c < Cols; c++)
                    row.cells[c] = idx < total ? bag[idx++] : ColorType.None;
                level.grid[r] = row;
            }

            // 3. Bus order: interleave colors so the player keeps switching targets.
            level.busSequence = new List<BusData>();
            for (int k = 0; k < BusesPerColor; k++)
                for (int ci = 0; ci < LevelColors.Length; ci++)
                {
                    var bd = Track(ScriptableObject.CreateInstance<BusData>());
                    bd.color = LevelColors[ci];
                    bd.capacity = BusCapacity;
                    level.busSequence.Add(bd);
                }

            return level;
        }

        // ---------------------------------------------------------------------
        // Scene scaffolding helpers
        // ---------------------------------------------------------------------
        private Camera SetupCamera()
        {
            Camera cam = Camera.main;
            if (cam == null)
            {
                var cgo = new GameObject("Main Camera") { tag = "MainCamera" };
                cam = cgo.AddComponent<Camera>();
                cgo.AddComponent<AudioListener>();
            }
            cam.transform.position = new Vector3(0f, 9f, -8.5f);
            cam.transform.rotation = Quaternion.LookRotation(new Vector3(0f, 0f, 2f) - cam.transform.position, Vector3.up);
            cam.fieldOfView = 55f;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.46f, 0.74f, 0.92f); // sky blue
            return cam;
        }

        private void SetupLight()
        {
            var lgo = new GameObject("Sun");
            lgo.transform.SetParent(_root.transform);
            var light = lgo.AddComponent<Light>();
            light.type = LightType.Directional;
            light.intensity = 1.15f;
            light.transform.rotation = Quaternion.Euler(50f, -30f, 0f);
        }

        private void BuildGround()
        {
            var g = GameObject.CreatePrimitive(PrimitiveType.Plane);
            g.name = "Ground";
            g.transform.SetParent(_root.transform);
            g.transform.position = new Vector3(0f, 0f, 1f);
            g.transform.localScale = new Vector3(3f, 1f, 3f);
            g.layer = 2; // Ignore Raycast → never intercepts taps
            Tint(g, new Color(0.34f, 0.56f, 0.34f));
        }

        private Transform Anchor(string name, Vector3 pos)
        {
            var a = new GameObject(name);
            a.transform.SetParent(_root.transform);
            a.transform.position = pos;
            return a.transform;
        }

        private static void Tint(GameObject go, Color c)
        {
            var r = go.GetComponent<Renderer>();
            if (r == null) return;
            var mpb = new MaterialPropertyBlock();
            r.GetPropertyBlock(mpb);
            mpb.SetColor("_BaseColor", c);
            mpb.SetColor("_Color", c);
            r.SetPropertyBlock(mpb);
        }

        // ---------------------------------------------------------------------
        // UI (legacy uGUI Text — no TMP import prompt, no EventSystem needed)
        // ---------------------------------------------------------------------
        private void BuildUI()
        {
            var cgo = new GameObject("Canvas");
            cgo.transform.SetParent(transform);
            _canvas = cgo.AddComponent<Canvas>();
            _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            var scaler = cgo.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1080f, 1920f);
            cgo.AddComponent<GraphicRaycaster>();

            _hud = MakeText("HUD", new Vector2(0.5f, 1f), new Vector2(0f, -40f),
                            new Vector2(1000f, 90f), 40, TextAnchor.MiddleCenter);
            _banner = MakeText("Banner", new Vector2(0.5f, 0.5f), Vector2.zero,
                               new Vector2(1000f, 600f), 64, TextAnchor.MiddleCenter);
        }

        private Text MakeText(string name, Vector2 anchor, Vector2 pos, Vector2 size, int fontSize, TextAnchor align)
        {
            var go = new GameObject(name);
            go.transform.SetParent(_canvas.transform, false);
            var t = go.AddComponent<Text>();
            t.font = _font;
            t.fontSize = fontSize;
            t.alignment = align;
            t.color = Color.white;
            t.horizontalOverflow = HorizontalWrapMode.Overflow;
            t.verticalOverflow = VerticalWrapMode.Overflow;
            t.raycastTarget = false;

            var shadow = go.AddComponent<Shadow>(); // readability over the sky
            shadow.effectColor = new Color(0f, 0f, 0f, 0.6f);
            shadow.effectDistance = new Vector2(2f, -2f);

            var rt = t.rectTransform;
            rt.anchorMin = rt.anchorMax = rt.pivot = anchor;
            rt.anchoredPosition = pos;
            rt.sizeDelta = size;
            return t;
        }

        private void UpdateHud()
        {
            if (_hud == null || _controller == null) return;
            ColorType active = _controller.ActiveBusColor;
            string busLabel = active == ColorType.None ? "—" : active.ToString();
            _hud.text = $"Bus: {busLabel}    Crowd: {_controller.CrowdRemaining}";
        }

        // ---------------------------------------------------------------------
        // Misc helpers
        // ---------------------------------------------------------------------
        private T Track<T>(T asset) where T : ScriptableObject { _ownedAssets.Add(asset); return asset; }

        private void DestroyOwnedAssets()
        {
            for (int i = 0; i < _ownedAssets.Count; i++)
                if (_ownedAssets[i] != null) Destroy(_ownedAssets[i]);
            _ownedAssets.Clear();
        }

        private static bool TapPressed()
        {
            var mouse = Mouse.current;
            if (mouse != null && mouse.leftButton.wasPressedThisFrame) return true;
            var touch = Touchscreen.current;
            if (touch != null && touch.primaryTouch.press.wasPressedThisFrame) return true;
            return false;
        }
    }
}
