using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;

namespace BusJam.Core
{
    /// <summary>
    /// Detects mobile touch / mouse taps, identifies the tapped passenger, validates that it is
    /// free (via <see cref="GridManager"/>), then routes it:
    ///   • matches the active bus &amp; bus has room → board the bus,
    ///   • otherwise, if a slot is free → go to the holding area,
    ///   • otherwise → blocked (no destructive state change; arbiter may flag a deadlock).
    ///
    /// Uses the new Input System (this project ships com.unity.inputsystem). Input can be disabled
    /// by the GameController during win/lose/transition states via <see cref="SetEnabled"/>.
    /// </summary>
    public class InputManager : MonoBehaviour
    {
        [Header("Dependencies")]
        [SerializeField] private Camera gameCamera;
        [SerializeField] private GridManager gridManager;
        [SerializeField] private BusManager busManager;
        [SerializeField] private SlotManager slotManager;

        [Header("Raycast")]
        // ~(1 << 2) = every layer EXCEPT "Ignore Raycast", so ground/buses (placed on layer 2)
        // never intercept a tap meant for a passenger.
        [SerializeField] private LayerMask passengerMask = ~(1 << 2);
        [SerializeField] private float rayDistance = 400f;

        private bool _inputEnabled = true;

        public void SetEnabled(bool enabled) => _inputEnabled = enabled;

        /// <summary>Code-wiring hook (used by the zero-setup Bootstrap).</summary>
        public void SetDependencies(Camera cam, GridManager grid, BusManager bus, SlotManager slot)
        {
            gameCamera = cam;
            gridManager = grid;
            busManager = bus;
            slotManager = slot;
        }

        private void Awake()
        {
            if (gameCamera == null) gameCamera = Camera.main;
        }

        private void Update()
        {
            if (!_inputEnabled) return;
            if (!TryGetTapPosition(out Vector2 screenPos)) return;

            // Ignore taps that land on UI (buttons, popups).
            if (EventSystem.current != null && EventSystem.current.IsPointerOverGameObject()) return;

            Ray ray = gameCamera.ScreenPointToRay(screenPos);
            if (!Physics.Raycast(ray, out RaycastHit hit, rayDistance, passengerMask)) return;

            Passenger p = hit.collider.GetComponentInParent<Passenger>();
            if (p != null) HandlePassengerTap(p);
        }

        // ---------------------------------------------------------------------
        // Routing
        // ---------------------------------------------------------------------
        private void HandlePassengerTap(Passenger p)
        {
            // 1. Only idle, on-grid passengers are tappable.
            if (p.State != PassengerState.Idle) return;

            // 2. Blocked passengers can't move at all.
            if (!gridManager.IsFree(p))
            {
                // Hook point for "shake/error" feedback.
                return;
            }

            // 3. Decide the destination BEFORE removing from the grid, so a passenger is never
            //    pulled off the board with nowhere to go.
            if (busManager.ActiveBusAccepts(p.Color))
            {
                gridManager.Remove(p);
                busManager.TryBoard(p);
            }
            else if (slotManager.HasFreeSlot)
            {
                gridManager.Remove(p);
                slotManager.SendToSlot(p);
            }
            else
            {
                // Free to move, but no matching bus and no free slot: not a legal move.
                // Leave it on the grid. GameController.EvaluateDeadlock() determines if this is terminal.
            }
        }

        // ---------------------------------------------------------------------
        // Cross-platform pointer-down (mouse in editor, touch on device)
        // ---------------------------------------------------------------------
        private static bool TryGetTapPosition(out Vector2 pos)
        {
            pos = default;
            var touch = Touchscreen.current;
            if (touch != null && touch.primaryTouch.press.wasPressedThisFrame)
            {
                pos = touch.primaryTouch.position.ReadValue();
                return true;
            }
            var mouse = Mouse.current;
            if (mouse != null && mouse.leftButton.wasPressedThisFrame)
            {
                pos = mouse.position.ReadValue();
                return true;
            }
            return false;
        }
    }
}
