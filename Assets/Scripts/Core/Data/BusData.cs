using UnityEngine;

namespace BusJam.Core
{
    /// <summary>
    /// Designer-authored definition of a single bus in a level's sequence:
    /// what color it accepts, how many it seats, and how it drives.
    /// Create via: Assets ▸ Create ▸ BusJam ▸ Bus Data.
    /// </summary>
    [CreateAssetMenu(fileName = "BusData", menuName = "BusJam/Bus Data", order = 1)]
    public class BusData : ScriptableObject
    {
        [Header("Identity")]
        public ColorType color = ColorType.Red;

        [Tooltip("How many passengers this bus seats.")]
        [Min(1)] public int capacity = 3;

        [Header("Timing (seconds)")]
        [Tooltip("Time to drive from off-screen into the active stop.")]
        [Min(0.05f)] public float arriveDuration = 0.45f;

        [Tooltip("Time to drive away once full.")]
        [Min(0.05f)] public float departDuration = 0.5f;
    }
}
