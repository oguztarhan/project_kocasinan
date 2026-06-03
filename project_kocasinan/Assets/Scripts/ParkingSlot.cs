using UnityEngine;

namespace BusJam
{
    /// <summary>A parking bay. Extra bays start locked and cost coins to open.</summary>
    public class ParkingSlot : MonoBehaviour
    {
        public int index;
        public bool locked;
        public Bus occupant;
        public GameObject lockMarker;

        public bool IsFree => !locked && occupant == null;

        public void Unlock()
        {
            locked = false;
            if (lockMarker != null) Destroy(lockMarker);
        }
    }
}
