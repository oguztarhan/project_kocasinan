using UnityEngine;

namespace BusJam.Core
{
    public enum BusState { Arriving, AtStop, Departing, Done }

    /// <summary>
    /// Runtime bus. Holds its color, capacity and seat bookkeeping.
    ///
    /// <para><b>Reservation pattern:</b> a seat is <see cref="TryReserve"/>d the instant a passenger
    /// is committed to boarding — BEFORE its walk animation finishes. <see cref="ConfirmSeat"/> is
    /// called on arrival. This prevents a fast double-tap from committing more passengers than there
    /// are seats (a real race in any animate-then-count design).</para>
    /// </summary>
    [DisallowMultipleComponent]
    public class Bus : MonoBehaviour
    {
        [SerializeField] private Renderer bodyRenderer; // tinted to the bus color; optional

        public ColorType Color { get; private set; }
        public int Capacity { get; private set; }
        public BusState State { get; set; } = BusState.Arriving;

        private int _seated;    // passengers who have physically arrived
        private int _reserved;  // committed but still walking

        /// <summary>True when every seat is spoken for (seated + in-transit).</summary>
        public bool IsFull => _seated + _reserved >= Capacity;
        /// <summary>True when every committed passenger has physically arrived.</summary>
        public bool AllSeated => _seated >= Capacity;
        public int SeatsRemaining => Capacity - (_seated + _reserved);
        /// <summary>True while one or more reserved passengers are still walking to this bus.</summary>
        public bool HasPendingArrivals => _reserved > 0;

        private MaterialPropertyBlock _mpb;

        public void Init(BusData data)
        {
            Color = data.color;
            Capacity = Mathf.Max(1, data.capacity);
            _seated = 0;
            _reserved = 0;
            State = BusState.Arriving;
            ApplyTint();
        }

        /// <summary>Reserve a seat. Returns false if none free (caller must not board).</summary>
        public bool TryReserve()
        {
            if (IsFull) return false;
            _reserved++;
            return true;
        }

        /// <summary>Convert a reservation into a filled seat (passenger has arrived).</summary>
        public void ConfirmSeat()
        {
            if (_reserved > 0) _reserved--;
            _seated++;
        }

        private void ApplyTint()
        {
            if (bodyRenderer == null) bodyRenderer = GetComponentInChildren<Renderer>();
            if (bodyRenderer == null) return;
            _mpb ??= new MaterialPropertyBlock();
            bodyRenderer.GetPropertyBlock(_mpb);
            var c = ColorPalette.ToColor(Color);
            _mpb.SetColor("_BaseColor", c);
            _mpb.SetColor("_Color", c);
            bodyRenderer.SetPropertyBlock(_mpb);
        }
    }
}
