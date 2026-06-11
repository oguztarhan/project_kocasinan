using System.Collections;
using UnityEngine;

namespace BusJam
{
    public enum BusState { Queued, Staging, MovingToSlot, Parked, Leaving, Done }

    /// <summary>Runtime vehicle (Car/Bus/Limo). Little roof passengers pop in as people board.</summary>
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

        // Tiny passengers on the roof — one per seat, hidden until that seat fills. Replaces the old
        // floating empty-seat NUMBER: the player COUNTS empty seats instead of reading a digit.
        public GameObject[] roofPeople;

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
            return i;
        }

        // Visual catch-up when the passenger reaches the door: pop their roof seat's little person in,
        // clear the reservation. The shrinking count of EMPTY seats is the readable "how many left".
        public void LightSeat(int i)
        {
            if (roofPeople != null && i >= 0 && i < roofPeople.Length && roofPeople[i] != null)
            {
                roofPeople[i].SetActive(true);
                StartCoroutine(PopIn(roofPeople[i].transform));
            }
            arrivalsPending = Mathf.Max(0, arrivalsPending - 1);
        }

        // Quick scale-pop so a newly seated passenger catches the eye.
        static IEnumerator PopIn(Transform t)
        {
            if (t == null) yield break;
            Vector3 baseScale = t.localScale;
            float dur = 0.2f, e = 0f;
            while (e < dur)
            {
                if (t == null) yield break;
                e += Time.deltaTime;
                float k = Mathf.Clamp01(e / dur);
                t.localScale = baseScale * (1f + 0.4f * Mathf.Sin(k * Mathf.PI));
                yield return null;
            }
            if (t != null) t.localScale = baseScale;
        }
    }
}
