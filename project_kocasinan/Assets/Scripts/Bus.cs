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

        public Renderer[] seatWindows;       // code-built vehicles: seat pips that light up
        public Material filledMat;
        public UnityEngine.UI.Text seatLabel; // imported vehicles: roof number = EMPTY seats left

        public bool IsFull => seatsFilled >= capacity;

        public void FillNextSeat()
        {
            int i = seatsFilled;
            if (seatWindows != null && i >= 0 && i < seatWindows.Length && filledMat != null)
                seatWindows[i].sharedMaterial = filledMat;
            seatsFilled++;
            RefreshSeatLabel();
        }

        public void RefreshSeatLabel()
        {
            if (seatLabel != null) seatLabel.text = Mathf.Max(0, capacity - seatsFilled).ToString();
        }
    }
}
