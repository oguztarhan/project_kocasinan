using UnityEngine;

namespace BusJam.Core
{
    /// <summary>
    /// Designer-authored tuning for passenger behaviour/visuals. One asset can be shared by
    /// thousands of passengers (flyweight) — they hold a reference, not a copy, so changing the
    /// asset retunes every passenger with zero code changes.
    /// Create via: Assets ▸ Create ▸ BusJam ▸ Passenger Data.
    /// </summary>
    [CreateAssetMenu(fileName = "PassengerData", menuName = "BusJam/Passenger Data", order = 0)]
    public class PassengerData : ScriptableObject
    {
        [Header("Movement")]
        [Tooltip("World units per second while walking to a slot or bus.")]
        [Min(0.1f)] public float moveSpeed = 6f;

        [Tooltip("Extra seconds to 'settle' (squash/scale punch) on arrival.")]
        [Min(0f)] public float arriveSettleTime = 0.12f;

        [Header("Feel")]
        [Tooltip("Vertical hop height during a walk (0 = none). Pure juice, no gameplay effect.")]
        [Min(0f)] public float hopHeight = 0.15f;

        [Tooltip("Scale punch magnitude applied on arrival.")]
        [Range(0f, 0.5f)] public float arrivePunch = 0.15f;
    }
}
