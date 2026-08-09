using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using RoomPlanner.Core;

namespace RoomPlanner.Tests
{
    /// <summary>
    /// Phase C / step C1 — plan-view polygon maths behind non-rectangular floors
    /// (docs/design/17-floor-outline.md). Everything is XZ: a floor plan seen from above.
    /// </summary>
    public class PolygonTests
    {
        private static Vector3 P(float x, float z) => new Vector3(x, 0f, z);

        /// <summary>4 x 3 rectangle, counter-clockwise seen from above.</summary>
        private static List<Vector3> Rect() => new()
        {
            P(0, 0), P(4, 0), P(4, 3), P(0, 3)
        };

        /// <summary>An L-shaped room — the case a rectangle cannot express.</summary>
        private static List<Vector3> LShape() => new()
        {
            P(0, 0), P(4, 0), P(4, 2), P(2, 2), P(2, 5), P(0, 5)
        };

        // ---- area / orientation ----

        [Test]
        public void Area_OfARectangle()
        {
            Assert.AreEqual(12f, Polygon.Area(Rect()), 1e-4f);
        }

        [Test]
        public void Area_OfAnLShape()
        {
            // 4x2 plus 2x3 = 8 + 6
            Assert.AreEqual(14f, Polygon.Area(LShape()), 1e-4f);
        }

        [Test]
        public void Orientation_IsDetectedBothWays()
        {
            var ccw = Rect();
            var cw = new List<Vector3>(ccw);
            cw.Reverse();

            Assert.IsFalse(Polygon.IsClockwise(ccw));
            Assert.IsTrue(Polygon.IsClockwise(cw));
        }

        [Test]
        public void ToCounterClockwise_NormalisesEitherInput()
        {
            var cw = Rect();
            cw.Reverse();

            var fixedUp = Polygon.ToCounterClockwise(cw);

            Assert.IsFalse(Polygon.IsClockwise(fixedUp), "a floor drawn clockwise must not end up facing down");
            Assert.AreEqual(4, fixedUp.Count);
            Assert.AreEqual(Polygon.Area(cw), Polygon.Area(fixedUp), 1e-4f, "shape unchanged, only order");
        }

        [Test]
        public void Area_IsSignAgnostic()
        {
            var cw = Rect();
            cw.Reverse();
            Assert.AreEqual(12f, Polygon.Area(cw), 1e-4f);
        }

        // ---- cleaning dirty MR input ----

        [Test]
        public void Clean_DropsDoubleClickedPoints()
        {
            var pts = new List<Vector3> { P(0, 0), P(0.001f, 0f), P(4, 0), P(4, 3), P(0, 3) };
            var cleaned = Polygon.Clean(pts);

            Assert.AreEqual(4, cleaned.Count, "a point 1 mm from the last one is a double click");
        }

        [Test]
        public void Clean_DropsAClosingDuplicate()
        {
            // the user closed the outline by clicking the first point again
            var pts = new List<Vector3> { P(0, 0), P(4, 0), P(4, 3), P(0, 3), P(0, 0) };
            var cleaned = Polygon.Clean(pts);

            Assert.AreEqual(4, cleaned.Count, "the outline is implicitly closed, no repeated first point");
        }

        // ---- containment ----

        [Test]
        public void Contains_InsideAndOutside()
        {
            var r = Rect();
            Assert.IsTrue(Polygon.Contains(r, P(2, 1)));
            Assert.IsFalse(Polygon.Contains(r, P(5, 1)));
            Assert.IsFalse(Polygon.Contains(r, P(2, 4)));
        }

        [Test]
        public void Contains_RespectsTheNotchOfAnLShape()
        {
            var l = LShape();
            Assert.IsTrue(Polygon.Contains(l, P(1, 4)), "inside the tall leg");
            Assert.IsFalse(Polygon.Contains(l, P(3, 4)), "the notch is OUTSIDE the room");
        }

        // ---- self-intersection guard ----

        [Test]
        public void IsSimple_AcceptsNormalOutlines()
        {
            Assert.IsTrue(Polygon.IsSimple(Rect()));
            Assert.IsTrue(Polygon.IsSimple(LShape()));
        }

        [Test]
        public void IsSimple_RejectsAFigureOfEight()
        {
            var bowtie = new List<Vector3> { P(0, 0), P(4, 3), P(4, 0), P(0, 3) };
            Assert.IsFalse(Polygon.IsSimple(bowtie), "a crossed outline has no sane inside");
        }

        // ---- triangulation ----

        [Test]
        public void Triangulate_RectangleGivesTwoTriangles()
        {
            var tris = Polygon.Triangulate(Rect());
            Assert.AreEqual(6, tris.Count, "n-2 triangles for n points");
        }

        [Test]
        public void Triangulate_LShapeGivesFourTriangles()
        {
            var tris = Polygon.Triangulate(LShape());
            Assert.AreEqual(12, tris.Count, "6 points → 4 triangles");
        }

        [Test]
        public void Triangulate_CoversTheWholeArea()
        {
            // the strongest check that no ear was mis-clipped: the pieces must add up
            var poly = LShape();
            var tris = Polygon.Triangulate(poly);

            float sum = 0f;
            for (int i = 0; i < tris.Count; i += 3)
                sum += Polygon.Area(new List<Vector3> { poly[tris[i]], poly[tris[i + 1]], poly[tris[i + 2]] });

            Assert.AreEqual(Polygon.Area(poly), sum, 1e-3f, "triangles must tile the polygon exactly");
        }

        [Test]
        public void Triangulate_KeepsCounterClockwiseWinding()
        {
            var poly = LShape();
            var tris = Polygon.Triangulate(poly);

            for (int i = 0; i < tris.Count; i += 3)
            {
                var tri = new List<Vector3> { poly[tris[i]], poly[tris[i + 1]], poly[tris[i + 2]] };
                Assert.IsFalse(Polygon.IsClockwise(tri),
                    "every triangle must wind the same way, or the slab gets holes facing down");
            }
        }

        [Test]
        public void Triangulate_NormalisesAClockwiseOutline()
        {
            var cw = LShape();
            cw.Reverse();
            var tris = Polygon.Triangulate(cw);

            Assert.AreEqual(12, tris.Count);
            for (int i = 0; i < tris.Count; i += 3)
            {
                var tri = new List<Vector3> { cw[tris[i]], cw[tris[i + 1]], cw[tris[i + 2]] };
                Assert.IsFalse(Polygon.IsClockwise(tri), "drawing direction must not flip the floor");
            }
        }

        [Test]
        public void Triangulate_RefusesACrossedOutline()
        {
            var bowtie = new List<Vector3> { P(0, 0), P(4, 3), P(4, 0), P(0, 3) };
            Assert.AreEqual(0, Polygon.Triangulate(bowtie).Count,
                "better no slab than a garbage one the user cannot see is wrong");
        }

        [Test]
        public void Triangulate_RefusesTooFewPoints()
        {
            Assert.AreEqual(0, Polygon.Triangulate(new List<Vector3> { P(0, 0), P(1, 0) }).Count);
            Assert.AreEqual(0, Polygon.Triangulate(null).Count);
        }
    }
}
