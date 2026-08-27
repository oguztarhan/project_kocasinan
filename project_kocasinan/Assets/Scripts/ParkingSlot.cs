using UnityEngine;

namespace Ridebury
{
    /// <summary>A parking bay. Extra bays start locked; one opens by watching an ad, the rest cost coins.</summary>
    public class ParkingSlot : MonoBehaviour
    {
        public int index;
        public bool locked;
        public bool adUnlock;   // when locked: true = opens by watching a rewarded ad; false = opens with coins
        public Bus occupant;
        public GameObject lockMarker;
        public GameObject letterP;  // the bay's "P" marking — created hidden while locked, shown on Unlock()

        public bool IsFree => !locked && occupant == null;

        public void Unlock()
        {
            locked = false;
            if (lockMarker != null) Destroy(lockMarker);
            if (letterP != null) letterP.SetActive(true); // bay is now usable -> reveal its "P"
        }
    }
}
