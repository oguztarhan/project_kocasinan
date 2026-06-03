using UnityEngine;

namespace BusJam
{
    public enum BusState { Queued, Staging, MovingToSlot, Parked, Leaving, Done }

    /// <summary>Runtime bus. Visual seat windows light up as people board.</summary>
    public class Bus : MonoBehaviour
    {
        public PieceColor color;
        public int capacity = 3;
        public int seatsFilled;
        public BusState state = BusState.Queued;
        public int slotIndex = -1;

        public Renderer[] seatWindows;
        public Material filledMat;

        public bool IsFull => seatsFilled >= capacity;

        public void FillNextSeat()
        {
            int i = seatsFilled;
            if (seatWindows != null && i >= 0 && i < seatWindows.Length && filledMat != null)
                seatWindows[i].sharedMaterial = filledMat;
            seatsFilled++;
        }
    }
}
