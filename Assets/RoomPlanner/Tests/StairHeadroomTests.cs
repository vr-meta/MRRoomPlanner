using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using RoomPlanner.Core;
using RoomPlanner.Floors;
using RoomPlanner.Stairs;

namespace RoomPlanner.Tests
{
    /// <summary>
    /// Headroom rule (audit 2026-08-10, 05 §Б1): the slab above a flight must be open
    /// wherever its underside comes closer than 2.0 m to the walking line. Covers the
    /// imported first-floor stairwell whose IFC hole left the user head-butting the slab.
    /// </summary>
    public class StairHeadroomTests
    {
        // 15 × 0.2 m risers = 3.0 m total, treads 0.25 m → run 3.5 m.
        private const int Risers = 15;
        private const float RiserH = 0.2f;
        private const float Tread = 0.25f;
        private const float Total = Risers * RiserH;          // 3.0
        private const float Run = (Risers - 1) * Tread;       // 3.5

        private bool _savedAO;

        [SetUp]
        public void DisableVertexAO()
        {
            _savedAO = MeshShading.VertexAO;
            MeshShading.VertexAO = false;
        }

        [TearDown]
        public void RestoreVertexAO() => MeshShading.VertexAO = _savedAO;

        private static Vector3 P(float x, float z) => new Vector3(x, 0f, z);

        private static Floor MakeSlab(out GameObject go, float level, float thickness = 0.25f)
        {
            go = new GameObject("SlabTest");
            var f = go.AddComponent<Floor>();
            f.Build(P(0, 0), new Vector3(6f, 0f, 6f), level, thickness, 5f, 0f, 0f);
            return f;
        }

        private static Stair MakeStair(out GameObject go, Vector3 basePoint, float yaw = 0f)
        {
            go = new GameObject("StairTest");
            var s = go.AddComponent<Stair>();
            s.Build(basePoint, yaw, 1.0f, Risers, RiserH, Tread, StairKind.Waist);
            return s;
        }

        private static bool IsOpenAt(Floor slab, Vector3 p)
        {
            if (!Polygon.Contains(slab.Outline, p)) return true;
            foreach (var h in slab.Holes)
                if (Polygon.Contains(h, p)) return true;
            return false;
        }

        // ---- pure math ----

        [Test]
        public void CutRange_SlabHighAbove_NeedsNoCut()
        {
            // Underside 5.0 m above base = 2.0 m over the flight top — exactly clears.
            Assert.IsFalse(StairMath.CutRange(Risers, RiserH, Tread, Total + 2.0f, out _, out _));
        }

        [Test]
        public void CutRange_LandingSlab_CutsFromWhereHeadroomDies()
        {
            // Slab whose top is the landing: underside at 3.0 - 0.25 = 2.75 above base.
            Assert.IsTrue(StairMath.CutRange(Risers, RiserH, Tread, 2.75f, out float d0, out float d1));
            // StepLine(d0) == 2.75 - 2.0 = 0.75 → d0 = (0.75-0.2)/(3.0-0.2) * 3.5
            Assert.AreEqual((0.75f - RiserH) / (Total - RiserH) * Run, d0, 1e-4f);
            Assert.AreEqual(Run, d1, 1e-4f, "the opening always reaches the flight top");
        }

        [Test]
        public void CutRange_SlabLowOverFirstStep_CutsTheWholeRun()
        {
            Assert.IsTrue(StairMath.CutRange(Risers, RiserH, Tread, 1.0f, out float d0, out _));
            Assert.AreEqual(0f, d0, 1e-4f, "violated from the first tread");
        }

        [Test]
        public void StepLineY_InterpolatesFirstTreadToTotal()
        {
            Assert.AreEqual(RiserH, StairMath.StepLineY(0f, Risers, RiserH, Tread), 1e-4f);
            Assert.AreEqual(Total, StairMath.StepLineY(Run, Risers, RiserH, Tread), 1e-4f);
        }

        // ---- scene-side cut ----

        [Test]
        public void CutHeadroomIn_SlabWithNoHole_GetsAStairwellOpening()
        {
            var slab = MakeSlab(out var slabGo, 3.0f);
            var stair = MakeStair(out var stairGo, new Vector3(1f, 0f, 1f));
            try
            {
                Assert.IsTrue(stair.CutHeadroomIn(slab));
                Assert.AreEqual(1, slab.Holes.Count);
                Assert.IsTrue(IsOpenAt(slab, new Vector3(1f, 0f, 3.0f)), "mid-flight must be open");
                Assert.IsTrue(IsOpenAt(slab, new Vector3(1f, 0f, 4.4f)), "top of the flight must be open");
                Assert.IsFalse(IsOpenAt(slab, new Vector3(5f, 0f, 5f)), "far corner stays solid");
                Assert.IsFalse(IsOpenAt(slab, new Vector3(1f, 0f, 1.1f)),
                    "the low steps still have headroom — slab stays over them");
            }
            finally { Object.DestroyImmediate(slabGo); Object.DestroyImmediate(stairGo); }
        }

        [Test]
        public void CutHeadroomIn_TooShortExistingHole_IsAbsorbedAndWidened()
        {
            // The reported bug: the file's stairwell hole covers only the very top of the
            // flight; walking up you hit the slab with your head.
            var slab = MakeSlab(out var slabGo, 3.0f);
            var stair = MakeStair(out var stairGo, new Vector3(1f, 0f, 1f));
            try
            {
                Assert.IsTrue(slab.AddHole(new List<Vector3>
                    { P(0.5f, 3.8f), P(1.5f, 3.8f), P(1.5f, 4.5f), P(0.5f, 4.5f) }));
                Assert.IsTrue(IsOpenAt(slab, new Vector3(1f, 0f, 4.0f)));
                Assert.IsFalse(IsOpenAt(slab, new Vector3(1f, 0f, 2.5f)), "mid-flight blocked before the fix");

                Assert.IsTrue(stair.CutHeadroomIn(slab));
                Assert.AreEqual(1, slab.Holes.Count, "the short hole is absorbed, not duplicated");
                Assert.IsTrue(IsOpenAt(slab, new Vector3(1f, 0f, 2.5f)), "mid-flight open after the fix");
                Assert.IsTrue(IsOpenAt(slab, new Vector3(1f, 0f, 4.0f)), "the old opening stays open");
            }
            finally { Object.DestroyImmediate(slabGo); Object.DestroyImmediate(stairGo); }
        }

        [Test]
        public void CutHeadroomIn_HoleAlreadyCoversTheFlight_DoesNothing()
        {
            var slab = MakeSlab(out var slabGo, 3.0f);
            var stair = MakeStair(out var stairGo, new Vector3(1f, 0f, 1f));
            try
            {
                Assert.IsTrue(slab.AddHole(new List<Vector3>
                    { P(0.3f, 1.4f), P(1.7f, 1.4f), P(1.7f, 4.6f), P(0.3f, 4.6f) }));
                Assert.IsFalse(stair.CutHeadroomIn(slab), "a good stairwell is left alone");
                Assert.AreEqual(1, slab.Holes.Count);
            }
            finally { Object.DestroyImmediate(slabGo); Object.DestroyImmediate(stairGo); }
        }

        [Test]
        public void CutHeadroomIn_SlabTheFlightStandsOn_IsIgnored()
        {
            var slab = MakeSlab(out var slabGo, 0f);   // ground slab under the stair base
            var stair = MakeStair(out var stairGo, new Vector3(1f, 0f, 1f));
            try
            {
                Assert.IsFalse(stair.CutHeadroomIn(slab));
                Assert.AreEqual(0, slab.Holes.Count);
            }
            finally { Object.DestroyImmediate(slabGo); Object.DestroyImmediate(stairGo); }
        }

        [Test]
        public void CutHeadroomIn_FlightElsewhere_LeavesTheSlabAlone()
        {
            var slab = MakeSlab(out var slabGo, 3.0f);
            var stair = MakeStair(out var stairGo, new Vector3(20f, 0f, 20f));
            try
            {
                Assert.IsFalse(stair.CutHeadroomIn(slab));
                Assert.AreEqual(0, slab.Holes.Count);
            }
            finally { Object.DestroyImmediate(slabGo); Object.DestroyImmediate(stairGo); }
        }

        [Test]
        public void CutHeadroomIn_RotatedFlight_CutsInItsOwnFrame()
        {
            var slab = MakeSlab(out var slabGo, 3.0f);
            // Yaw 90° → run points along +X, starting at (1, 0, 3).
            var stair = MakeStair(out var stairGo, new Vector3(1f, 0f, 3f), 90f);
            try
            {
                Assert.IsTrue(stair.CutHeadroomIn(slab));
                Assert.IsTrue(IsOpenAt(slab, new Vector3(3.5f, 0f, 3f)), "mid-flight along +X is open");
                Assert.IsFalse(IsOpenAt(slab, new Vector3(3.5f, 0f, 1f)), "off to the side stays solid");
            }
            finally { Object.DestroyImmediate(slabGo); Object.DestroyImmediate(stairGo); }
        }
    }
}
