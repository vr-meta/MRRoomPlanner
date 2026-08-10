using System.Collections.Generic;
using UnityEngine;

namespace RoomPlanner.Core
{
    /// <summary>
    /// The baked ceramic tile catalog (design/23): subway ("кабанчик"), square grid and
    /// tile herringbone in preset glaze colors. Unlike laminate (design/22) there are no
    /// photo sources — faces are synthesised (glaze + grout + bevel), so the whole set
    /// reproduces from nothing. Pure data in Core so the composition is pin-testable.
    /// One normal map per PATTERN — grout and bevel do not depend on the glaze color.
    /// </summary>
    public static class TileCatalog
    {
        public const float Gloss = 0.75f;          // glazed ceramic
        public const int SourceSlots = 32;         // per-tile variation hash space

        public struct Pattern
        {
            public LaminatePattern Layout;
            public string Key;          // id fragment: "subway" / "grid" / "herringbone"
            public float TileL, TileW;  // one ceramic tile, metres
            public float DeckOffset;    // Deck only: row offset fraction
            public float BevelMeters;   // chamfer width — subway's WIDE bevel is its look
        }

        public static readonly Pattern[] Patterns =
        {
            new() { Layout = LaminatePattern.Deck, Key = "subway",
                    TileL = 0.2f, TileW = 0.1f, DeckOffset = 0.5f, BevelMeters = 0.015f },
            new() { Layout = LaminatePattern.Grid, Key = "grid",
                    TileL = 0.2f, TileW = 0.2f, DeckOffset = 0f, BevelMeters = 0.003f },
            new() { Layout = LaminatePattern.Herringbone, Key = "herringbone",
                    TileL = 0.2f, TileW = 0.1f, DeckOffset = 0f, BevelMeters = 0.003f },
        };

        /// <summary>(key, glaze color) — sRGB presets picked for bathroom/kitchen looks.</summary>
        public static readonly (string key, Color glaze)[] Colors =
        {
            ("white", new Color(0.93f, 0.92f, 0.90f)),
            ("cream", new Color(0.90f, 0.85f, 0.74f)),
            ("sage", new Color(0.62f, 0.70f, 0.60f)),
            ("sky", new Color(0.55f, 0.68f, 0.78f)),
            ("graphite", new Color(0.20f, 0.21f, 0.23f)),
            ("terracotta", new Color(0.72f, 0.40f, 0.28f)),
        };

        public struct Entry
        {
            public string Id;           // "tile-subway-white"
            public Pattern Pattern;
            public string ColorKey;
            public Color Glaze;
            public float TileMeters;    // square seamless period
        }

        /// <summary>18 entries: every pattern × every color, catalog order (the paint
        /// tool's Tiles swatch row order).</summary>
        public static IReadOnlyList<Entry> Entries => _entries ??= BuildEntries();
        private static List<Entry> _entries;

        private static List<Entry> BuildEntries()
        {
            var list = new List<Entry>();
            foreach (var p in Patterns)
                foreach (var (key, glaze) in Colors)
                    list.Add(new Entry
                    {
                        Id = $"tile-{p.Key}-{key}",
                        Pattern = p,
                        ColorKey = key,
                        Glaze = glaze,
                        TileMeters = LaminateLayout.TileMeters(p.Layout, p.TileL, p.TileW),
                    });
            return list;
        }

        public static string DiffuseFileName(Entry e) => $"{e.Id}.png";
        public static string NormalFileName(Pattern p) => $"tile-{p.Key}-normal.png";
    }
}
