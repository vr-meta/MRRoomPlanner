using System.Collections.Generic;
using NUnit.Framework;
using RoomPlanner.Electrical;

namespace RoomPlanner.Tests
{
    public class ElectricalBomTests
    {
        [Test]
        public void MetersByType_SumsByCable_WithConnectionAllowance()
        {
            var entries = new List<RouteBomEntry>
            {
                new(CableType.C3x15, 10f, 2),   // 10 + 2×0.15 = 10.3
                new(CableType.C3x15, 5f, 0),    // 5
                new(CableType.C3x25, 7f, 1),    // 7 + 0.15 = 7.15
            };
            var m = ElectricalBom.MetersByType(entries, 0);
            Assert.AreEqual(15.3f, m[(int)CableType.C3x15], 1e-4);
            Assert.AreEqual(7.15f, m[(int)CableType.C3x25], 1e-4);
        }

        [Test]
        public void Reserve_MultipliesEveryType()
        {
            var entries = new List<RouteBomEntry> { new(CableType.C3x25, 10f, 0) };
            var m = ElectricalBom.MetersByType(entries, 10);
            Assert.AreEqual(11f, m[(int)CableType.C3x25], 1e-4);
        }

        [Test]
        public void ZeroAndNegativeLengths_Ignored()
        {
            var entries = new List<RouteBomEntry>
            {
                new(CableType.C3x15, 0f, 2),
                new(CableType.C3x15, -3f, 0),
            };
            var m = ElectricalBom.MetersByType(entries, 10);
            Assert.AreEqual(0f, m[(int)CableType.C3x15], 1e-6, "empty routes add no allowance either");
            Assert.AreEqual(0f, ElectricalBom.Total(m), 1e-6);
        }

        [Test]
        public void NullEntries_GiveEmptyBom()
        {
            var m = ElectricalBom.MetersByType(null, 10);
            Assert.AreEqual(Cable.TypeCount, m.Length);
            Assert.AreEqual(0f, ElectricalBom.Total(m), 1e-6);
        }

        [Test]
        public void Describe_ListsTypesTotalAndReserve()
        {
            var entries = new List<RouteBomEntry>
            {
                new(CableType.C3x15, 10f, 2),
                new(CableType.C3x25, 7f, 1),
            };
            string s = ElectricalBom.Describe(entries, 10, unrouted: 0);
            StringAssert.Contains("3x1.5 — ", s);
            StringAssert.Contains("3x2.5 — ", s);
            StringAssert.Contains("Total — ", s);
            StringAssert.Contains("(+10%)", s);
            StringAssert.DoesNotContain("unrouted", s);
        }

        [Test]
        public void Describe_OmitsZeroTypes_ShowsUnrouted()
        {
            var entries = new List<RouteBomEntry> { new(CableType.C3x25, 4f, 0) };
            string s = ElectricalBom.Describe(entries, 0, unrouted: 2);
            StringAssert.DoesNotContain("3x1.5", s);
            StringAssert.Contains("unrouted: 2", s);
        }

        [Test]
        public void Describe_EmptyScene_StillShowsTotal()
        {
            string s = ElectricalBom.Describe(new List<RouteBomEntry>(), 10, 0);
            StringAssert.Contains("Total — 0.0 m", s);
        }

        [Test]
        public void FormatMeters_OneDecimal_InvariantCulture()
        {
            Assert.AreEqual("12.3 m", ElectricalBom.FormatMeters(12.34f));
            Assert.AreEqual("0.0 m", ElectricalBom.FormatMeters(0f));
        }

        [Test]
        public void Cable_LabelsAndDefaults()
        {
            Assert.AreEqual("3x1.5", Cable.Label(CableType.C3x15));
            Assert.AreEqual("3x2.5", Cable.Label(CableType.C3x25));
            Assert.AreEqual(CableType.C3x25, Cable.Next(CableType.C3x15));
            Assert.AreEqual(CableType.C3x15, Cable.Next(CableType.C3x25));
            Assert.AreEqual(CableType.C3x15, Cable.DefaultFor(FixtureKind.Switch), "switch feeds lighting");
            Assert.AreEqual(CableType.C3x25, Cable.DefaultFor(FixtureKind.Outlet));
        }
    }
}
