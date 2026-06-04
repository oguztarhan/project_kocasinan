using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace BusJam.Core
{
    /// <summary>
    /// Owns the bus queue and the single active stop. Responsibilities (SRP):
    ///  • spawn buses from the level's <see cref="BusData"/> sequence,
    ///  • expose the active bus & whether it accepts a color,
    ///  • commit passengers to seats (via Bus reservations),
    ///  • drive a full bus away and bring the next one in.
    ///
    /// It NEVER reaches into SlotManager/GridManager. It only raises events
    /// (<see cref="GameEvents.ActiveBusChanged"/>, <see cref="GameEvents.BusDeparted"/>);
    /// other systems react. This keeps the dependency graph acyclic.
    /// </summary>
    public class BusManager : MonoBehaviour
    {
        [Header("Scene anchors")]
        [SerializeField] private Transform stopPoint;       // where the active bus waits
        [SerializeField] private Transform arriveFromPoint; // off-screen spawn point
        [SerializeField] private Transform departToPoint;   // off-screen exit point

        [Header("Prefab (optional)")]
        [Tooltip("Bus prefab with a Bus component. If null, a primitive cube is generated as a fallback.")]
        [SerializeField] private Bus busPrefab;

        private readonly Queue<BusData> _pending = new Queue<BusData>();
        private Bus _active;

        public Bus ActiveBus => _active;
        public bool HasActiveBus => _active != null && _active.State == BusState.AtStop;
        public bool HasPendingBuses => _pending.Count > 0;
        /// <summary>True while a passenger is still walking to the active bus (used by the win check).</summary>
        public bool ActiveBusHasPendingArrivals => _active != null && _active.HasPendingArrivals;

        /// <summary>Code-wiring hook (used by the zero-setup Bootstrap).</summary>
        public void SetAnchors(Transform stop, Transform arriveFrom, Transform departTo)
        {
            stopPoint = stop;
            arriveFromPoint = arriveFrom;
            departToPoint = departTo;
        }

        // ---------------------------------------------------------------------
        // Setup
        // ---------------------------------------------------------------------
        public void Load(IReadOnlyList<BusData> sequence)
        {
            _pending.Clear();
            if (sequence != null)
                for (int i = 0; i < sequence.Count; i++)
                    if (sequence[i] != null) _pending.Enqueue(sequence[i]);

            _active = null;
        }

        /// <summary>Brings the first bus in immediately (call after Load, when the level starts).</summary>
        public void SpawnFirstBus()
        {
            if (_active == null && _pending.Count > 0)
                StartCoroutine(BringNextBus());
        }

        // ---------------------------------------------------------------------
        // Queries used by InputManager / GameController
        // ---------------------------------------------------------------------
        /// <summary>True if the active bus is at the stop, matches the color, and has a free seat.</summary>
        public bool ActiveBusAccepts(ColorType color)
        {
            return HasActiveBus && _active.Color == color && !_active.IsFull;
        }

        public ColorType ActiveColor => HasActiveBus ? _active.Color : ColorType.None;

        // ---------------------------------------------------------------------
        // Boarding
        // ---------------------------------------------------------------------
        /// <summary>
        /// Commit <paramref name="p"/> to the active bus and start its walk. Returns false if the
        /// bus can't take it (caller keeps the passenger where it is). Seat is reserved up front.
        /// </summary>
        public bool TryBoard(Passenger p)
        {
            if (p == null || !ActiveBusAccepts(p.Color)) return false;
            if (!_active.TryReserve()) return false;

            // Finalise the seat (and possibly depart) only when the passenger physically arrives.
            Bus boardingBus = _active;
            void OnArrived(Passenger arrived)
            {
                arrived.ReachedBus -= OnArrived;
                boardingBus.ConfirmSeat();
                Destroy(arrived.gameObject);
                if (boardingBus.AllSeated && boardingBus.State == BusState.AtStop)
                    StartCoroutine(Depart(boardingBus));
            }
            p.ReachedBus += OnArrived;
            p.MoveToBus(BusDoorWorld(boardingBus));
            return true;
        }

        // ---------------------------------------------------------------------
        // Bus traffic
        // ---------------------------------------------------------------------
        private IEnumerator Depart(Bus bus)
        {
            bus.State = BusState.Departing;
            if (_active == bus) _active = null;
            GameEvents.RaiseBusDeparted(bus);

            Vector3 to = departToPoint != null ? departToPoint.position : bus.transform.position + Vector3.back * 16f;
            yield return MoveTo(bus.transform, to, 0.5f);
            bus.State = BusState.Done;
            Destroy(bus.gameObject);

            if (_pending.Count > 0) yield return BringNextBus();
            // If no buses remain, GameController's LevelCompleted check (grid+slots empty) handles the win.
        }

        private IEnumerator BringNextBus()
        {
            if (_pending.Count == 0) yield break;

            BusData data = _pending.Dequeue();
            Bus bus = CreateBus(data);
            Vector3 from = arriveFromPoint != null ? arriveFromPoint.position
                                                   : StopWorld() + Vector3.forward * 16f;
            bus.transform.position = from;
            bus.State = BusState.Arriving;

            yield return MoveTo(bus.transform, StopWorld(), 0.45f);
            bus.State = BusState.AtStop;
            _active = bus;

            // Announce the new active bus → SlotManager flushes any matching waiters.
            GameEvents.RaiseActiveBusChanged(bus);
        }

        private Bus CreateBus(BusData data)
        {
            Bus bus;
            if (busPrefab != null)
            {
                bus = Instantiate(busPrefab, transform);
            }
            else
            {
                // Fallback so the system is runnable without art set up.
                var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
                go.name = "Bus_" + data.color;
                go.transform.SetParent(transform, false);
                go.transform.localScale = new Vector3(1.2f, 0.8f, 2.2f);
                go.layer = 2; // Ignore Raycast — buses must not intercept passenger taps
                bus = go.AddComponent<Bus>();
            }
            bus.Init(data);
            return bus;
        }

        // ---------------------------------------------------------------------
        // Positions / helpers
        // ---------------------------------------------------------------------
        private Vector3 StopWorld() => stopPoint != null ? stopPoint.position : Vector3.zero;
        private Vector3 BusDoorWorld(Bus bus) => bus.transform.position + new Vector3(0f, 0.25f, -1.1f);

        private static IEnumerator MoveTo(Transform t, Vector3 target, float dur)
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
    }
}
