using UnityEngine;

namespace BusJam
{
    /// <summary>A parking bay. Extra bays start locked; one opens by watching an ad, the rest cost coins.</summary>
    public class ParkingSlot : MonoBehaviour
    {
        public int index;
        public bool locked;
        public bool adUnlock;   // when locked: true = opens by watching a rewarded ad; false = opens with coins
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
