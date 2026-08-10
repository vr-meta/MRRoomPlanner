using System.Collections.Generic;

namespace RoomPlanner.Core
{
    /// <summary>
    /// The baked laminate catalog (design/22): which pattern × color variants exist and
    /// how their files are named. Pure data in Core so the composition is pin-testable;
    /// the Editor baker writes the files and SetupPaintTool wires them into the
    /// FinishLibrary. One normal map per PATTERN — relief does not depend on color.
    /// </summary>
    public static class LaminateCatalog
    {
        public const float PlankLengthMeters = 1.2f;
        public const float PlankWidthMeters = 0.2f;
        public const float Gloss = 0.35f;          // varnished laminate, matches parquet-*
        public const int SourcePlanks = 18;        // D:\Maps set (design/22)

        public struct Entry
        {
            public string Id;                 // catalog id, e.g. "lam-herringbone-grey"
            public LaminatePattern Pattern;
            public string ColorKey;           // "natural" / "grey" / "dark" / "bleached"
            public float TileMeters;          // square seamless period
        }

        public static readonly (LaminatePattern pattern, string key)[] Patterns =
        {
            (LaminatePattern.Deck, "deck"),
            (LaminatePattern.Herringbone, "herringbone"),
            (LaminatePattern.Basket, "basket"),
        };

        public static readonly string[] ColorKeys = { "natural", "grey", "dark", "bleached" };

        /// <summary>12 entries: every pattern × every color, catalog order (the paint
        /// tool's Floors swatch row order).</summary>
        public static IReadOnlyList<Entry> Entries => _entries ??= BuildEntries();
        private static List<Entry> _entries;

        private static List<Entry> BuildEntries()
        {
            var list = new List<Entry>();
            foreach (var (pattern, key) in Patterns)
                foreach (var color in ColorKeys)
                    list.Add(new Entry
                    {
                        Id = $"lam-{key}-{color}",
                        Pattern = pattern,
                        ColorKey = color,
                        TileMeters = LaminateLayout.TileMeters(
                            pattern, PlankLengthMeters, PlankWidthMeters),
                    });
            return list;
        }

        public static string DiffuseFileName(Entry e) => $"{e.Id}.png";

        public static string NormalFileName(LaminatePattern pattern)
        {
            foreach (var (p, key) in Patterns)
                if (p == pattern) return $"lam-{key}-normal.png";
            return null;
        }
    }
}
