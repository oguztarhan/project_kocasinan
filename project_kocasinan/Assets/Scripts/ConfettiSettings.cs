using UnityEngine;

namespace BusJam
{
    /// <summary>
    /// Inspector-editable tuning for the win-celebration confetti. Lives as a serialized
    /// field on <see cref="BusJamGame"/>, so you can tweak amount / size / speed / gravity /
    /// lifetime straight from the BusJamGame component in the gameplay scene.
    /// </summary>
    [System.Serializable]
    public class ConfettiSettings
    {
        [Tooltip("How many confetti pieces each corner burst spawns.")]
        [Min(0)] public int countPerCorner = 35;

        [Tooltip("Size of one confetti piece (cube scale).")]
        public Vector3 size = new Vector3(0.16f, 0.16f, 0.04f);

        [Tooltip("Upward launch speed range (higher = flies higher).")]
        public float upSpeedMin = 4.5f;
        public float upSpeedMax = 7.5f;

        [Tooltip("Sideways speed range for the diagonal corner bursts.")]
        public float sideSpeedMin = 2.0f;
        public float sideSpeedMax = 5.0f;

        [Tooltip("How long (seconds) a piece lives before disappearing.")]
        public float lifeMin = 1.2f;
        public float lifeMax = 2.0f;

        [Tooltip("Gravity pulling the pieces back down (higher = falls faster).")]
        public float gravity = 9.8f;

        [Tooltip("Random horizontal spread of the spawn point.")]
        [Min(0f)] public float spawnSpreadX = 0.4f;
    }
}
