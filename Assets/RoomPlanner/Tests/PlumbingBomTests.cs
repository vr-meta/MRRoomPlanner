using System.Collections.Generic;
using NUnit.Framework;
using RoomPlanner.Plumbing;

namespace RoomPlanner.Tests
{
    public class PlumbingBomTests
    {
        [Test]
        public void MetersByDiameter_SumsPerDiameter_WithConnectionAllowance()
        {
            var entries = new List<PipeBomEntry>
            {
                new(PipeDiameter.D110, 3.0f, 1, 0, 0),   // riser teed once
                new(PipeDiameter.D50, 4.0f, 2, 1, 0),
                new(PipeDiameter.D50, 1.0f, 0, 0, 0),
            };
            var m = PlumbingBom.MetersByDiameter(entries, 0);
            Assert.AreEqual(3.15f, m[(int)PipeDiameter.D110], 1e-4);
            Assert.AreEqual(5.30f, m[(int)PipeDiameter.D50], 1e-4);
            Assert.AreEqual(0f, m[(int)PipeDiameter.D40], 1e-4);
        }

        [Test]
        public void Reserve_ScalesEverything()
        {
            var entries = new List<PipeBomEntry> { new(PipeDiameter.D40, 10.0f, 0, 0, 0) };
            var m = PlumbingBom.MetersByDiameter(entries, 10);
            Assert.AreEqual(11.0f, m[(int)PipeDiameter.D40], 1e-4);
            Assert.AreEqual(11.0f, PlumbingBom.Total(m), 1e-4);
        }

        [Test]
        public void ZeroAndNegativeLengths_AreIgnored()
        {
            var entries = new List<PipeBomEntry>
            {
                new(PipeDiameter.D50, 0f, 2, 0, 0),
                new(PipeDiameter.D50, -1f, 0, 0, 0),
            };
            Assert.AreEqual(0f, PlumbingBom.Total(PlumbingBom.MetersByDiameter(entries, 20)), 1e-4);
        }

        [Test]
        public void Describe_ListsDiametersTotalAndElbows()
        {
            var entries = new List<PipeBomEntry>
            {
                new(PipeDiameter.D110, 3.0f, 0, 2, 0),
                new(PipeDiameter.D50, 5.0f, 0, 1, 1),
            };
            string s = PlumbingBom.Describe(entries, 10);
            StringAssert.Contains("D110 — 3.3 m", s);
            StringAssert.Contains("D50 — 5.5 m", s);
            StringAssert.Contains("Total — 8.8 m", s);
            StringAssert.Contains("(+10%)", s);
            StringAssert.Contains("90°×3", s);
            StringAssert.Contains("45°×1", s);
        }

        [Test]
        public void Describe_NoElbows_OmitsTheFittingTail()
        {
            var entries = new List<PipeBomEntry> { new(PipeDiameter.D50, 2.0f, 0, 0, 0) };
            string s = PlumbingBom.Describe(entries, 0);
            StringAssert.DoesNotContain("elbows", s);
        }

        [Test]
        public void Describe_EmptyScene_StillReportsTotal()
        {
            string s = PlumbingBom.Describe(new List<PipeBomEntry>(), 10);
            StringAssert.Contains("Total — 0.0 m", s);
        }

        [Test]
        public void PipeSpec_RadiiAndLabels()
        {
            Assert.AreEqual(0.055f, PipeSpec.Radius(PipeDiameter.D110), 1e-5);
            Assert.AreEqual(0.025f, PipeSpec.Radius(PipeDiameter.D50), 1e-5);
            Assert.AreEqual(0.020f, PipeSpec.Radius(PipeDiameter.D40), 1e-5);
            Assert.AreEqual("110", PipeSpec.Label(PipeDiameter.D110));
            Assert.AreEqual("50", PipeSpec.Label(PipeDiameter.D50));
            Assert.AreEqual("40", PipeSpec.Label(PipeDiameter.D40));
        }
    }
}
