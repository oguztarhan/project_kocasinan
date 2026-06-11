using UnityEngine;

namespace BusJam
{
    /// <summary>
    /// Editable runtime tuning knobs (people/bus speed, sizes, turn feel). Loaded at runtime from
    /// Resources by BusJamGame — tweak the GameSettings.asset in the Inspector any time. If the asset
    /// is missing, BusJamGame falls back to these same defaults, so nothing breaks.
    ///
    /// Layout-critical values — CellSize, grid size, band Z positions, camera, and the vehicle COUNT —
    /// are intentionally NOT here: they are fitted + stress-tested in code, and changing them would
    /// silently break the framing / solvability. Ask to expose any of those and we re-verify.
    /// </summary>
    [CreateAssetMenu(fileName = "GameSettings", menuName = "BusJam/Game Settings")]
    public class GameSettings : ScriptableObject
    {
        [Header("People")]
        [Tooltip("Seconds each passenger takes to walk to the bus. Higher = slower boarding.")]
        [Range(0.1f, 1.5f)] public float boardWalkDuration = 0.35f;

        [Tooltip("Seconds between successive boarders starting their walk. Higher = calmer, less rushed.")]
        [Range(0.02f, 0.6f)] public float boardCadence = 0.13f;

        [Tooltip("People (queue + crowd) size multiplier. 1 = default.")]
        [Range(0.4f, 2f)] public float peopleSize = 1f;

        [Header("Vehicles")]
        [Tooltip("Bus drive speed from the jam to its parking pad (world units / sec). Higher = faster.")]
        [Range(3f, 30f)] public float busDriveSpeed = 11f;

        [Tooltip("Bus speed leaving off-screen once it is full (world units / sec).")]
        [Range(3f, 30f)] public float busLeaveSpeed = 14f;

        [Tooltip("How lazily a vehicle eases into turns. Lower = wider, lazier sweeps; higher = snappier.")]
        [Range(2f, 14f)] public float turnSmoothness = 6f;

        [Tooltip("Vehicle size multiplier. 1 = default. Above ~1.1 vehicles may visually overlap neighbours.")]
        [Range(0.6f, 1.4f)] public float vehicleSize = 1f;
    }
}
