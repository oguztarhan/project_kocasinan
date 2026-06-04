using UnityEngine;

namespace BusJam.Core
{
    /// <summary>
    /// Manages the limited holding area — a fixed set of slot Transforms where passengers wait
    /// when they don't match the active bus.
    ///
    /// <para><b>Auto-flush:</b> subscribes to <see cref="GameEvents.ActiveBusChanged"/>. When a new
    /// bus arrives, any waiting passenger whose color matches is immediately boarded (respecting the
    /// bus's remaining capacity), freeing its slot.</para>
    ///
    /// <para><b>Deadlock:</b> when the area fills it raises <see cref="GameEvents.SlotsFull"/>. It does
    /// NOT decide game-over itself — GameController arbitrates, because a full holding area is only
    /// terminal if the active bus also can't be fed from the grid. See GameController.</para>
    /// </summary>
    public class SlotManager : MonoBehaviour
    {
        [Header("Slots")]
        [Tooltip("Waiting positions. The number of elements IS the holding-area size (e.g. 5–7).")]
        [SerializeField] private Transform[] slotPoints;

        [Header("Dependencies")]
        [SerializeField] private BusManager busManager;

        // Parallel array: occupant[i] is the passenger parked at slotPoints[i], or null.
        private Passenger[] _occupants;

        public int Capacity => slotPoints != null ? slotPoints.Length : 0;
        public bool HasFreeSlot => FreeCount > 0;

        public int FreeCount
        {
            get
            {
                int free = 0;
                for (int i = 0; i < _occupants.Length; i++)
                    if (_occupants[i] == null) free++;
                return free;
            }
        }

        // ---------------------------------------------------------------------
        // Lifecycle
        // ---------------------------------------------------------------------
        public void Init()
        {
            _occupants = new Passenger[Capacity];
        }

        /// <summary>Code-wiring hooks (used by the zero-setup Bootstrap). Call before Init().</summary>
        public void SetSlots(Transform[] points) => slotPoints = points;
        public void SetBusManager(BusManager bm) => busManager = bm;

        private void OnEnable()  => GameEvents.ActiveBusChanged += OnActiveBusChanged;
        private void OnDisable() => GameEvents.ActiveBusChanged -= OnActiveBusChanged;

        // ---------------------------------------------------------------------
        // Sending a passenger to a slot
        // ---------------------------------------------------------------------
        /// <summary>
        /// Parks <paramref name="p"/> in the first free slot and starts its walk. Returns false if
        /// the holding area is full (caller must handle that — typically it never gets here because
        /// InputManager checks <see cref="HasFreeSlot"/> first).
        /// </summary>
        public bool SendToSlot(Passenger p)
        {
            if (p == null) return false;
            int idx = FirstFreeIndex();
            if (idx < 0) return false;

            _occupants[idx] = p;
            p.MoveToSlot(slotPoints[idx].position);

            // If that was the last free slot, let the arbiter evaluate a possible deadlock.
            if (!HasFreeSlot) GameEvents.RaiseSlotsFull();
            return true;
        }

        // ---------------------------------------------------------------------
        // Auto-flush when the active bus changes
        // ---------------------------------------------------------------------
        private void OnActiveBusChanged(Bus bus)
        {
            if (bus == null || busManager == null) return;

            // Board matching waiters in slot order until the bus is full.
            for (int i = 0; i < _occupants.Length; i++)
            {
                Passenger p = _occupants[i];
                if (p == null || p.Color != bus.Color) continue;
                if (busManager.ActiveBusAccepts(p.Color) && busManager.TryBoard(p))
                {
                    _occupants[i] = null; // slot freed the instant boarding is committed (seat reserved)
                }
                if (!busManager.ActiveBusAccepts(bus.Color)) break; // bus full → stop scanning
            }
        }

        // ---------------------------------------------------------------------
        // Queries for the deadlock arbiter
        // ---------------------------------------------------------------------
        /// <summary>True if any waiting passenger matches <paramref name="color"/>.</summary>
        public bool AnyWaiterOfColor(ColorType color)
        {
            for (int i = 0; i < _occupants.Length; i++)
                if (_occupants[i] != null && _occupants[i].Color == color) return true;
            return false;
        }

        public bool IsEmpty
        {
            get
            {
                for (int i = 0; i < _occupants.Length; i++)
                    if (_occupants[i] != null) return false;
                return true;
            }
        }

        // ---------------------------------------------------------------------
        // Helpers
        // ---------------------------------------------------------------------
        private int FirstFreeIndex()
        {
            for (int i = 0; i < _occupants.Length; i++)
                if (_occupants[i] == null) return i;
            return -1;
        }
    }
}
