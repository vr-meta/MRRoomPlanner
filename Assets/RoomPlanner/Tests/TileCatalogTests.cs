using NUnit.Framework;
using RoomPlanner.Core;

namespace RoomPlanner.Tests
{
    /// <summary>Pin on the procedural ceramic composition (design/23) — baker files and
    /// FinishLibrary wiring follow this table, and the ids live in saved projects.</summary>
    public class TileCatalogTests
    {
        [Test]
        public void EighteenEntries_EveryPatternTimesEveryColor()
        {
            Assert.AreEqual(3, TileCatalog.Patterns.Length);
            Assert.AreEqual(6, TileCatalog.Colors.Length);
            Assert.AreEqual(18, TileCatalog.Entries.Count);
        }

        [Test]
        public void Ids_AreStable_AndFilesMatch()
        {
            string[] patterns = { "subway", "grid", "herringbone" };
            string[] colors = { "white", "cream", "sage", "sky", "graphite", "terracotta" };
            int i = 0;
            foreach (var p in patterns)
                foreach (var c in colors)
                {
                    Assert.AreEqual($"tile-{p}-{c}", TileCatalog.Entries[i].Id);
                    Assert.AreEqual($"tile-{p}-{c}.png",
                        TileCatalog.DiffuseFileName(TileCatalog.Entries[i]));
                    i++;
                }
            foreach (var p in TileCatalog.Patterns)
                Assert.AreEqual($"tile-{p.Key}-normal.png", TileCatalog.NormalFileName(p));
        }

        [Test]
        public void Periods_MatchLayouts()
        {
            foreach (var e in TileCatalog.Entries)
                Assert.AreEqual(
                    e.Pattern.Layout == LaminatePattern.Deck ? 0.2f : 0.4f,
                    e.TileMeters, 1e-5f, e.Id);
        }

        [Test]
        public void Subway_HasTheWideKabanchikBevel()
        {
            foreach (var p in TileCatalog.Patterns)
                if (p.Key == "subway")
                {
                    Assert.AreEqual(0.5f, p.DeckOffset, "subway bond is half-offset");
                    Assert.Greater(p.BevelMeters, 0.01f, "кабанчик = wide chamfer");
                    return;
                }
            Assert.Fail("no subway pattern");
        }
    }
}
