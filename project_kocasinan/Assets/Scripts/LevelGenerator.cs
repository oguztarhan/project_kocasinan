using System.Collections.Generic;
using UnityEngine;

namespace BusJam
{
    public struct BusDef
    {
        public PieceColor color;
        public int capacity;
    }

    public class LevelData
    {
        public int levelNumber;
        public List<PieceColor> line;            // index 0 = front
        public List<List<BusDef>> busColumns;    // columns of buses; column[0] = front (nearest exit)
        public int columns;
        public HashSet<int> goldenSet;
        public HashSet<int> mysterySet;
        public int baseSlots;
        public int extraSlots;
        public float timeLimit;
        public int colorCount;
    }

    /// <summary>
    /// Solvable-by-construction generator. A sliding window of at most
    /// <c>baseSlots</c> buses is ever "open", and buses are dealt into
    /// <c>columns</c> (>= baseSlots) in FIFO order, so each bus reaches the front
    /// of its column exactly when the line needs it.
    /// </summary>
    public static class LevelGenerator
    {
        public const int BaseSlots = 3;
        public const int ExtraSlots = 2;
        public const int MaxColumns = 5;

        public static LevelData Generate(int level)
        {
            var rng = new System.Random(level * 9176 + 4242);

            int colorCount = Mathf.Clamp(3 + (level - 1) / 5, 3, Palette.Count); // +1 color every 5 levels
            int busCount   = Mathf.Clamp(4 + level, 4, 28);
            int capacity   = 3;
            int columns    = Mathf.Clamp(busCount, BaseSlots, MaxColumns);

            var buses = new List<BusDef>(busCount);
            for (int i = 0; i < busCount; i++)
                buses.Add(new BusDef { color = (PieceColor)rng.Next(colorCount), capacity = capacity });

            // Window emission -> the line.
            int window = BaseSlots;
            var remaining = new int[busCount];
            for (int i = 0; i < busCount; i++) remaining[i] = buses[i].capacity;

            var line = new List<PieceColor>();
            var open = new List<int>();
            int nextToOpen = Mathf.Min(window, busCount);
            for (int i = 0; i < nextToOpen; i++) open.Add(i);
            while (open.Count > 0)
            {
                int pick = open[rng.Next(open.Count)];
                line.Add(buses[pick].color);
                remaining[pick]--;
                if (remaining[pick] == 0)
                {
                    open.Remove(pick);
                    if (nextToOpen < busCount) { open.Add(nextToOpen); nextToOpen++; }
                }
            }

            // Deal buses into columns in FIFO order (each column ascending).
            var busColumns = new List<List<BusDef>>();
            for (int j = 0; j < columns; j++) busColumns.Add(new List<BusDef>());
            for (int i = 0; i < busCount; i++) busColumns[i % columns].Add(buses[i]);

            int people = line.Count;

            var golden = new HashSet<int>();
            int goldenCount = Mathf.Min(level / 2, people / 6);
            int guard = 0;
            while (golden.Count < goldenCount && guard++ < 2000) golden.Add(rng.Next(people));

            var mystery = new HashSet<int>();
            int mysteryCount = Mathf.Min(level, people / 4);
            guard = 0;
            while (mystery.Count < mysteryCount && guard++ < 2000)
            {
                int idx = rng.Next(people);
                if (idx >= 3) mystery.Add(idx);
            }

            float timeLimit = Mathf.Max(20f, people * 1.9f - level * 1.4f);

            return new LevelData
            {
                levelNumber = level,
                line = line,
                busColumns = busColumns,
                columns = columns,
                goldenSet = golden,
                mysterySet = mystery,
                baseSlots = BaseSlots,
                extraSlots = ExtraSlots,
                timeLimit = timeLimit,
                colorCount = colorCount
            };
        }
    }
}
