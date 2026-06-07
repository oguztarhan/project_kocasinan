using UnityEngine;

namespace BusJam
{
    /// <summary>
    /// Hand-authorable level data. These are DIFFICULTY PARAMETERS fed into the same
    /// solvable-by-construction generator (see <see cref="LevelGenerator.Generate(LevelDefinition)"/>),
    /// so any values you set still produce a solvable level. Put assets under
    /// Assets/Resources/Levels/Level{n} so BusJamGame loads them automatically.
    /// </summary>
    [CreateAssetMenu(fileName = "Level", menuName = "BusJam/Level Definition")]
    public class LevelDefinition : ScriptableObject
    {
        [Min(1)] public int levelNumber = 1;

        [Header("Difficulty")]
        [Range(3, 8)] public int colorCount = 3;
        [Min(4)] public int busCount = 4;
        [Min(10)] public float timeLimit = 60f;

        [Header("Grid")]
        [Min(3)] public int gridWidth = 5;
        [Tooltip("0 = auto-size from bus count.")]
        [Min(0)] public int gridHeight = 0;

        [Header("Parking")]
        [Min(1)] public int baseSlots = 3;
        [Min(0)] public int extraSlots = 2;

        [Header("Specials (chance per queue person)")]
        [Range(0f, 1f)] public float goldenChance = 0f;
        [Range(0f, 1f)] public float mysteryChance = 0f;

        [Header("Layout")]
        [Tooltip("0 = derive a stable seed from levelNumber. Same seed = same layout every Play.")]
        public int seed = 0;
    }
}
