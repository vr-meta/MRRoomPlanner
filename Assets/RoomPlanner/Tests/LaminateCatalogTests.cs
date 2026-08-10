using NUnit.Framework;
using RoomPlanner.Core;

namespace RoomPlanner.Tests
{
    /// <summary>Pin on the baked laminate composition (design/22): the baker's output
    /// files and the FinishLibrary wiring both follow this table — a silent change here
    /// would orphan baked files or break catalog ids saved in projects.</summary>
    public class LaminateCatalogTests
    {
        [Test]
        public void TwelveEntries_EveryPatternTimesEveryColor()
        {
            Assert.AreEqual(3, LaminateCatalog.Patterns.Length);
            Assert.AreEqual(4, LaminateCatalog.ColorKeys.Length);
            Assert.AreEqual(12, LaminateCatalog.Entries.Count);
        }

        [Test]
        public void Ids_AreStable()
        {
            var expected = new[]
            {
                "lam-deck-natural", "lam-deck-grey", "lam-deck-dark", "lam-deck-bleached",
                "lam-herringbone-natural", "lam-herringbone-grey",
                "lam-herringbone-dark", "lam-herringbone-bleached",
                "lam-basket-natural", "lam-basket-grey", "lam-basket-dark", "lam-basket-bleached",
            };
            for (int i = 0; i < expected.Length; i++)
                Assert.AreEqual(expected[i], LaminateCatalog.Entries[i].Id);
        }

        [Test]
        public void FileNames_MatchIds_NormalsPerPattern()
        {
            foreach (var e in LaminateCatalog.Entries)
                Assert.AreEqual($"{e.Id}.png", LaminateCatalog.DiffuseFileName(e));
            Assert.AreEqual("lam-deck-normal.png",
                LaminateCatalog.NormalFileName(LaminatePattern.Deck));
            Assert.AreEqual("lam-herringbone-normal.png",
                LaminateCatalog.NormalFileName(LaminatePattern.Herringbone));
            Assert.AreEqual("lam-basket-normal.png",
                LaminateCatalog.NormalFileName(LaminatePattern.Basket));
        }

        [Test]
        public void TileMeters_MatchLayoutPeriods()
        {
            foreach (var e in LaminateCatalog.Entries)
                Assert.AreEqual(
                    e.Pattern == LaminatePattern.Herringbone ? 2.4f : 1.2f,
                    e.TileMeters, 1e-5f, e.Id);
        }
    }
}
