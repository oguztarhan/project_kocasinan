using System;
using System.Collections;
using UnityEngine;

namespace BusJam.Core
{
    /// <summary>The four lifecycle states of a passenger.</summary>
    public enum PassengerState
    {
        Idle,         // waiting on the grid, tappable
        MovingToSlot, // walking to a holding slot
        MovingToBus,  // walking to board a bus
        Boarded,      // seated; about to be destroyed
    }

    /// <summary>
    /// A single passenger actor. Owns ONLY its own concerns (SRP):
    /// its color, its state, its position in the grid, and its movement animation.
    /// It does not know about board rules — managers drive it via <see cref="MoveToSlot"/>
    /// and <see cref="MoveToBus"/>, and react to its completion callbacks.
    /// </summary>
    [DisallowMultipleComponent]
    public class Passenger : MonoBehaviour
    {
        [SerializeField] private Renderer bodyRenderer; // tinted to the passenger color; optional

        // --- Encapsulated state: external code reads, only this class writes ----
        public ColorType Color { get; private set; }
        public PassengerState State { get; private set; } = PassengerState.Idle;
        public GridCoord Coord { get; private set; } = GridCoord.Invalid;
        public bool IsOnGrid => Coord.IsValid && State == PassengerState.Idle;

        private PassengerData _data;
        private MaterialPropertyBlock _mpb; // tint without instantiating a material (no leaks, batches well)
        private Coroutine _moveRoutine;

        /// <summary>Fires (this passenger) when a slot-bound walk completes.</summary>
        public event Action<Passenger> ReachedSlot;
        /// <summary>Fires (this passenger) when a bus-bound walk completes (i.e. seated).</summary>
        public event Action<Passenger> ReachedBus;

        // ---------------------------------------------------------------------
        // Setup
        // ---------------------------------------------------------------------
        public void Init(ColorType color, PassengerData data, GridCoord coord)
        {
            Color = color;
            _data = data;
            Coord = coord;
            State = PassengerState.Idle;
            ApplyTint();
        }

        /// <summary>Called by GridManager the moment the passenger leaves the grid.</summary>
        public void ClearGridCoord() => Coord = GridCoord.Invalid;

        private void ApplyTint()
        {
            // Auto-grab a renderer (covers the primitive-fallback case where none is assigned).
            if (bodyRenderer == null) bodyRenderer = GetComponentInChildren<Renderer>();
            if (bodyRenderer == null) return;
            _mpb ??= new MaterialPropertyBlock();
            bodyRenderer.GetPropertyBlock(_mpb);
            // Support both URP/Lit (_BaseColor) and Standard (_Color).
            var c = ColorPalette.ToColor(Color);
            _mpb.SetColor("_BaseColor", c);
            _mpb.SetColor("_Color", c);
            bodyRenderer.SetPropertyBlock(_mpb);
        }

        // ---------------------------------------------------------------------
        // Commands (issued by managers)
        // ---------------------------------------------------------------------
        public void MoveToSlot(Vector3 worldTarget)
        {
            State = PassengerState.MovingToSlot;
            StartMove(worldTarget, onArrive: () =>
            {
                State = PassengerState.MovingToSlot; // settled in slot, still "parked" there
                ReachedSlot?.Invoke(this);
                GameEvents.RaisePassengerEnteredSlot(this);
            });
        }

        public void MoveToBus(Vector3 worldTarget)
        {
            State = PassengerState.MovingToBus;
            StartMove(worldTarget, onArrive: () =>
            {
                State = PassengerState.Boarded;
                ReachedBus?.Invoke(this);
                GameEvents.RaisePassengerBoarded(this);
            });
        }

        // ---------------------------------------------------------------------
        // Movement implementation
        //
        // NOTE: This is the dependency-free tween. DOTween is NOT installed in this
        // project, so we use an eased coroutine. To switch to DOTween later, replace
        // the body of MoveRoutine with:
        //     yield return transform.DOMove(target, dur).SetEase(Ease.OutQuad).WaitForCompletion();
        // ...and keep the onArrive callback. Nothing else in the codebase changes.
        // ---------------------------------------------------------------------
        private void StartMove(Vector3 target, Action onArrive)
        {
            if (_moveRoutine != null) StopCoroutine(_moveRoutine);
            _moveRoutine = StartCoroutine(MoveRoutine(target, onArrive));
        }

        private IEnumerator MoveRoutine(Vector3 target, Action onArrive)
        {
            Vector3 start = transform.position;
            float dist = Vector3.Distance(start, target);
            float speed = _data != null ? _data.moveSpeed : 6f;
            float dur = Mathf.Max(0.0001f, dist / speed);
            float hop = _data != null ? _data.hopHeight : 0f;

            float t = 0f;
            while (t < dur)
            {
                t += Time.deltaTime;
                float n = Mathf.Clamp01(t / dur);
                float eased = n * n * (3f - 2f * n); // smoothstep, no GC, cheap
                Vector3 p = Vector3.LerpUnclamped(start, target, eased);
                if (hop > 0f) p.y += Mathf.Sin(n * Mathf.PI) * hop; // single-arc hop
                transform.position = p;
                yield return null;
            }
            transform.position = target;

            // Small arrival "settle" (scale punch) for game feel.
            yield return PunchScale();

            _moveRoutine = null;
            onArrive?.Invoke();
        }

        private IEnumerator PunchScale()
        {
            float settle = _data != null ? _data.arriveSettleTime : 0f;
            float punch = _data != null ? _data.arrivePunch : 0f;
            if (settle <= 0f || punch <= 0f) yield break;

            Vector3 baseScale = transform.localScale;
            float t = 0f;
            while (t < settle)
            {
                t += Time.deltaTime;
                float n = Mathf.Clamp01(t / settle);
                float s = 1f + Mathf.Sin(n * Mathf.PI) * punch; // out-and-back
                transform.localScale = baseScale * s;
                yield return null;
            }
            transform.localScale = baseScale;
        }
    }
}
