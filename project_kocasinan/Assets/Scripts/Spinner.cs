using UnityEngine;

namespace Ridebury
{
    /// <summary>Continuously spins a transform about a local axis — used for the helicopter joker's
    /// main + tail rotors. Self-contained (no wiring); add it and call Set(axis, degPerSec).</summary>
    public class Spinner : MonoBehaviour
    {
        public Vector3 axis = Vector3.up;
        public float degPerSec = 720f;

        public void Set(Vector3 a, float dps) { axis = a.sqrMagnitude > 1e-6f ? a.normalized : Vector3.up; degPerSec = dps; }

        void Update() { transform.Rotate(axis, degPerSec * Time.deltaTime, Space.Self); }
    }
}
