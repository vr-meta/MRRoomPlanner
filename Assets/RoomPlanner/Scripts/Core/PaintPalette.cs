using UnityEngine;

namespace RoomPlanner.Core
{
    /// <summary>
    /// Preset interior paint colors for the Paint tool (design/04, v1). A fixed curated
    /// list, not a free color picker: cycling a named row beats RGB sliders in VR, and
    /// the names read like a paint catalog. Data hues only — the system hover/selected
    /// cyan/mint are deliberately absent (UiColors rule: states ≠ data).
    /// </summary>
    public static class PaintPalette
    {
        public static readonly string[] Names =
        {
            "White", "Ivory", "Sand", "Terracotta", "Rose",
            "Sky", "Teal", "Mint", "Olive", "Lavender", "Graphite", "Navy",
        };

        public static readonly Color[] Colors =
        {
            new(0.95f, 0.95f, 0.93f),   // White
            new(0.93f, 0.90f, 0.82f),   // Ivory
            new(0.85f, 0.78f, 0.64f),   // Sand
            new(0.77f, 0.44f, 0.31f),   // Terracotta
            new(0.85f, 0.65f, 0.63f),   // Rose
            new(0.66f, 0.78f, 0.88f),   // Sky
            new(0.31f, 0.56f, 0.53f),   // Teal
            new(0.75f, 0.88f, 0.79f),   // Mint
            new(0.54f, 0.56f, 0.36f),   // Olive
            new(0.76f, 0.73f, 0.86f),   // Lavender
            new(0.29f, 0.31f, 0.33f),   // Graphite
            new(0.20f, 0.31f, 0.43f),   // Navy
        };

        public static int Count => Names.Length;

        public static string NameAt(int index) => Names[Wrap(index)];
        public static Color ColorAt(int index) => Colors[Wrap(index)];

        private static int Wrap(int index) => ((index % Count) + Count) % Count;
    }
}
