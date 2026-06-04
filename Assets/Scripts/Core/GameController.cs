using UnityEngine;

namespace BusJam.Core
{
    /// <summary>
    /// Top-level orchestrator (mediator). It owns level lifecycle and arbitrates the two outcomes
    /// the individual managers deliberately do NOT decide on their own:
    ///   • <b>Win</b>  — grid empty AND holding area empty AND no buses pending.
    ///   • <b>Game over</b> — a true deadlock (see <see cref="EvaluateDeadlock"/>).
    ///
    /// Wiring is done in the inspector (constructor-injection-by-serialization). Managers talk to
    /// each other only through <see cref="GameEvents"/>; this class subscribes to those events to
    /// re-evaluate the board after every state change. That single choke-point is why win/lose logic
    /// lives here instead of being smeared across managers.
    /// </summary>
    public class GameController : MonoBehaviour
    {
        [Header("Managers")]
        [SerializeField] private GridManager gridManager;
        [SerializeField] private BusManager busManager;
        [SerializeField] private SlotManager slotManager;
        [SerializeField] private InputManager inputManager;

        [Header("Content")]
        [SerializeField] private LevelConfig level;
        [SerializeField] private PassengerData passengerData;
        [Tooltip("Passenger prefab with a Passenger + Collider. If null, a primitive capsule is generated.")]
        [SerializeField] private Passenger passengerPrefab;

        private bool _levelOver;
        private bool _started;

        // HUD accessors (read-only) for a launcher/UI.
        public ColorType ActiveBusColor => busManager != null ? busManager.ActiveColor : ColorType.None;
        public int CrowdRemaining => gridManager != null ? gridManager.PassengerCount : 0;

        /// <summary>Code-wiring hook (used by the zero-setup Bootstrap).</summary>
        public void Configure(GridManager grid, BusManager bus, SlotManager slot, InputManager input,
                              LevelConfig levelConfig, PassengerData pData)
        {
            gridManager = grid;
            busManager = bus;
            slotManager = slot;
            inputManager = input;
            level = levelConfig;
            passengerData = pData;
        }

        // ---------------------------------------------------------------------
        // Lifecycle
        // ---------------------------------------------------------------------
        private void OnEnable()
        {
            GameEvents.PassengerBoarded += OnBoardChanged;
            GameEvents.PassengerEnteredSlot += OnBoardChanged;
            GameEvents.BusDeparted += OnBusDeparted;
            GameEvents.SlotsFull += OnSlotsFull;
        }

        private void OnDisable()
        {
            GameEvents.PassengerBoarded -= OnBoardChanged;
            GameEvents.PassengerEnteredSlot -= OnBoardChanged;
            GameEvents.BusDeparted -= OnBusDeparted;
            GameEvents.SlotsFull -= OnSlotsFull;
        }

        // Auto-start for the inspector-driven path. Guarded so a programmatic launcher (Bootstrap)
        // that already called StartLevel doesn't trigger a second build.
        private void Start()
        {
            if (!_started && level != null) StartLevel(level);
        }

        // ---------------------------------------------------------------------
        // Build
        // ---------------------------------------------------------------------
        public void StartLevel(LevelConfig config)
        {
            // NOTE: we deliberately do NOT call GameEvents.Reset() here. Subscriptions are owned by
            // each manager's OnEnable/OnDisable, so an in-place restart never double-subscribes, and a
            // scene reload destroys every manager (firing OnDisable) which cleans up automatically.
            // Reset() exists only for a hard, manual teardown — calling it here would clobber other
            // managers' live subscriptions (e.g. SlotManager's auto-flush).
            level = config;
            _levelOver = false;
            _started = true;

            gridManager.Build(config.Rows, config.Columns, config.cellSize);
            slotManager.Init();
            busManager.Load(config.busSequence);

            SpawnPassengers(config);

            inputManager.SetEnabled(true);
            busManager.SpawnFirstBus();
        }

        private void SpawnPassengers(LevelConfig config)
        {
            for (int r = 0; r < config.Rows; r++)
            {
                for (int c = 0; c < config.Columns; c++)
                {
                    ColorType color = config.ColorAt(r, c);
                    if (color == ColorType.None) continue;

                    var coord = new GridCoord(r, c);
                    Passenger p = CreatePassenger();
                    p.transform.SetParent(transform, false); // parent under controller → cleaned on teardown
                    p.transform.position = gridManager.CellToWorld(coord);
                    p.Init(color, passengerData, coord);
                    gridManager.Register(p, coord);
                }
            }
        }

        private Passenger CreatePassenger()
        {
            if (passengerPrefab != null) return Instantiate(passengerPrefab);

            // Fallback so the system runs without art set up: a capsule with a collider.
            var go = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            go.name = "Passenger";
            go.transform.localScale = new Vector3(0.6f, 0.6f, 0.6f);
            return go.AddComponent<Passenger>();
        }

        // ---------------------------------------------------------------------
        // Re-evaluation after every meaningful state change
        // ---------------------------------------------------------------------
        private void OnBoardChanged(Passenger _) => Evaluate();
        private void OnBusDeparted(Bus _) => Evaluate();
        private void OnSlotsFull() => Evaluate();

        private void Evaluate()
        {
            if (_levelOver) return;

            if (IsLevelComplete())
            {
                _levelOver = true;
                inputManager.SetEnabled(false);
                GameEvents.RaiseLevelCompleted();
                return;
            }

            if (EvaluateDeadlock())
            {
                _levelOver = true;
                inputManager.SetEnabled(false);
                GameEvents.RaiseGameOver("No moves left.");
            }
        }

        /// <summary>
        /// Win condition: every passenger has boarded a bus. That means the grid is empty, the
        /// holding area is empty, AND no passenger is still walking toward a bus (the active bus
        /// holds no outstanding reservations). The last clause defends against a premature win when
        /// two passengers are mid-walk and the first one arrives.
        /// Partially-filled buses are irrelevant — clearing all passengers is the goal.
        /// </summary>
        private bool IsLevelComplete()
        {
            return gridManager.IsEmpty && slotManager.IsEmpty && !busManager.ActiveBusHasPendingArrivals;
        }

        /// <summary>
        /// True only for a genuine deadlock. A full holding area is NOT enough on its own:
        /// the player may still tap a grid passenger that matches the active bus, which after the
        /// bus fills brings in a new bus that can free a slot.
        ///
        /// Deadlock = holding area full
        ///            AND no waiting passenger matches the active bus
        ///            AND no FREE grid passenger matches the active bus
        ///            (i.e. nothing can feed the current bus, and nowhere to stash anyone).
        ///
        /// This is the single place to tune difficulty/forgiveness (e.g. add joker checks here).
        /// </summary>
        private bool EvaluateDeadlock()
        {
            if (slotManager.HasFreeSlot) return false;          // can still stash someone
            if (!busManager.HasActiveBus) return false;         // a bus is in transit; wait for it

            ColorType active = busManager.ActiveColor;
            if (slotManager.AnyWaiterOfColor(active)) return false;       // a waiter can board now
            if (gridManager.AnyFreePassengerOfColor(active)) return false; // a grid passenger can feed the bus

            return true; // truly stuck
        }
    }
}
