using UnityEngine;

namespace BusJam
{
    public enum BusState { Queued, Staging, MovingToSlot, Parked, Leaving, Done }

    /// <summary>Runtime vehicle (Car/Bus/Limo). Visual seat windows light up as people board.</summary>
    public class Bus : MonoBehaviour
    {
        public PieceColor color;
        public VehicleType type = VehicleType.Bus;
        public int capacity = 3;
        public int seatsFilled;
        public BusState state = BusState.Queued;
        public int slotIndex = -1;

        // Jam-grid placement. `cell` = leading cell (nearest the exit edge); the body
        // extends backward as cell - i*dir for i in 0..length-1. occ holds every body cell.
        public Vector2Int cell;
        public Vector2Int dir;
        public int length = 1;

        // Special "<<" vehicle: 0 = normal (full exit in one tap); >0 = advances this many
        // grid cells along its arrow per tap (crawls out over multiple taps).
        public int advanceN;

        public Renderer[] seatWindows;       // code-built vehicles: seat pips that light up
        public Material filledMat;
        public UnityEngine.UI.Text seatLabel; // imported vehicles: roof number = EMPTY seats left

        public bool IsFull => seatsFilled >= capacity;

        // In-flight passengers who reserved a seat but haven't visually arrived yet. The bus may only
        // drive off once it is full AND everyone has arrived, so no walker is ever left heading to nothing.
        public int arrivalsPending;
        public bool ReadyToLeave => IsFull && arrivalsPending <= 0;

        // Reserve the next seat LOGICALLY at dispatch time (so overlapping boards can't over-assign past
        // capacity) and tick the roof number down immediately. The seat WINDOW lights later, on arrival.
        public int ReserveSeat()
        {
            int i = seatsFilled;
            seatsFilled++;
            arrivalsPending++;
            RefreshSeatLabel();
            return i;
        }

        // Visual catch-up when the passenger reaches the door: light their seat pip, clear the reservation.
        public void LightSeat(int i)
        {
            if (seatWindows != null && i >= 0 && i < seatWindows.Length && filledMat != null)
                seatWindows[i].sharedMaterial = filledMat;
            arrivalsPending = Mathf.Max(0, arrivalsPending - 1);
        }

        public void RefreshSeatLabel()
        {
            if (seatLabel != null) seatLabel.text = Mathf.Max(0, capacity - seatsFilled).ToString();
        }
    }
}
