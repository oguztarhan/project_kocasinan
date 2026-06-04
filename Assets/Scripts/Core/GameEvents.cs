using System;

namespace BusJam.Core
{
    /// <summary>
    /// Static, type-safe event bus. This is the ONLY channel managers use to notify
    /// one another of state changes, keeping them decoupled (Dependency Inversion:
    /// publishers depend on an abstraction — the event — not on concrete subscribers).
    ///
    /// <para><b>Lifetime contract:</b> every subscriber MUST unsubscribe in OnDisable/OnDestroy.
    /// Because this is static, a forgotten subscription survives scene reloads and will
    /// fire into destroyed objects. <see cref="Reset"/> is a hard safety net for that.</para>
    /// </summary>
    public static class GameEvents
    {
        // --- Passenger lifecycle -------------------------------------------------
        /// <summary>A passenger has finished its travel animation and is seated on a bus.</summary>
        public static event Action<Passenger> PassengerBoarded;
        /// <summary>A passenger has settled into a holding slot.</summary>
        public static event Action<Passenger> PassengerEnteredSlot;

        // --- Bus lifecycle -------------------------------------------------------
        /// <summary>A new bus has pulled into the active stop. Carries the new active bus.</summary>
        public static event Action<Bus> ActiveBusChanged;
        /// <summary>The active bus filled up and is driving away.</summary>
        public static event Action<Bus> BusDeparted;

        // --- Board / flow state --------------------------------------------------
        /// <summary>Every holding slot is occupied (raised by SlotManager; arbitrated by GameController).</summary>
        public static event Action SlotsFull;
        /// <summary>All passengers cleared — the level is won.</summary>
        public static event Action LevelCompleted;
        /// <summary>No legal move remains. Carries a human-readable reason.</summary>
        public static event Action<string> GameOver;

        // --- Raisers (invoked only by the owning manager) ------------------------
        public static void RaisePassengerBoarded(Passenger p) => PassengerBoarded?.Invoke(p);
        public static void RaisePassengerEnteredSlot(Passenger p) => PassengerEnteredSlot?.Invoke(p);
        public static void RaiseActiveBusChanged(Bus b) => ActiveBusChanged?.Invoke(b);
        public static void RaiseBusDeparted(Bus b) => BusDeparted?.Invoke(b);
        public static void RaiseSlotsFull() => SlotsFull?.Invoke();
        public static void RaiseLevelCompleted() => LevelCompleted?.Invoke();
        public static void RaiseGameOver(string reason) => GameOver?.Invoke(reason);

        /// <summary>
        /// Clears every subscription. Call when (re)loading a level so stale listeners
        /// from a torn-down scene can never be invoked. Cheap insurance against the
        /// classic "static event memory leak" bug.
        /// </summary>
        public static void Reset()
        {
            PassengerBoarded = null;
            PassengerEnteredSlot = null;
            ActiveBusChanged = null;
            BusDeparted = null;
            SlotsFull = null;
            LevelCompleted = null;
            GameOver = null;
        }
    }
}
