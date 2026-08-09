using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using RoomPlanner.Core;

namespace RoomPlanner.Tests
{
    /// <summary>
    /// Phase C / step C6 — outlines with holes (stairwells, shafts).
    /// Ear clipping cannot see a hole, so each hole is bridged into the outer ring first; these
    /// tests pin that the bridged result still tiles the correct area and refuses nonsense.
    /// </summary>
    public class PolygonHolesTests
    {
        private static Vector3 P(float x, float z) => new Vector3(x, 0f, z);

        private static List<Vector3> Rect(float x0, float z0, float x1, float z1) => new()
        {
            P(x0, z0), P(x1, z0), P(x1, z1), P(x0, z1)
        };

        private static List<IReadOnlyList<Vector3>> Holes(params List<Vector3>[] rings)
        {
            var list = new List<IReadOnlyList<Vector3>>();
            foreach (var r in rings) list.Add(r);
            return list;
        }

        private static float TriangleAreaSum(List<Vector3> verts, List<int> tris)
        {
            float sum = 0f;
            for (int i = 0; i < tris.Count; i += 3)
                sum += Polygon.Area(new List<Vector3> { verts[tris[i]], verts[tris[i + 1]], verts[tris[i + 2]] });
            return sum;
        }

        // ---- area / containment ----

        [Test]
        public void AreaWithHoles_SubtractsTheHole()
        {
            var outer = Rect(0, 0, 6, 4);            // 24
            var hole = Rect(2, 1, 4, 3);             // 4
            Assert.AreEqual(20f, Polygon.AreaWithHoles(outer, Holes(hole)), 1e-3f);
        }

        [Test]
        public void ContainsWithHoles_TreatsTheHoleAsOutside()
        {
            var outer = Rect(0, 0, 6, 4);
            var hole = Rect(2, 1, 4, 3);

            Assert.IsTrue(Polygon.ContainsWithHoles(outer, Holes(hole), P(1, 2)), "solid part");
            Assert.IsFalse(Polygon.ContainsWithHoles(outer, Holes(hole), P(3, 2)), "you would fall through");
            Assert.IsFalse(Polygon.ContainsWithHoles(outer, Holes(hole), P(9, 2)), "outside entirely");
        }

        // ---- triangulation with holes ----

        [Test]
        public void TriangulateWithHoles_TilesTheSolidArea()
        {
            var outer = Rect(0, 0, 6, 4);
            var hole = Rect(2, 1, 4, 3);

            var tris = Polygon.TriangulateWithHoles(outer, Holes(hole), out var merged);

            Assert.Greater(tris.Count, 0, "a slab with a stairwell must still triangulate");
            Assert.AreEqual(20f, TriangleAreaSum(merged, tris), 1e-2f,
                "the triangles must cover the solid area — no more, no less");
        }

        [Test]
        public void TriangulateWithHoles_KeepsOneWinding()
        {
            var tris = Polygon.TriangulateWithHoles(Rect(0, 0, 6, 4), Holes(Rect(2, 1, 4, 3)), out var merged);

            for (int i = 0; i < tris.Count; i += 3)
            {
                var tri = new List<Vector3> { merged[tris[i]], merged[tris[i + 1]], merged[tris[i + 2]] };
                Assert.IsFalse(Polygon.IsClockwise(tri),
                    "a flipped triangle would leave a face looking the wrong way");
            }
        }

        [Test]
        public void TriangulateWithHoles_HandlesTwoHoles()
        {
            var outer = Rect(0, 0, 10, 4);                        // 40
            var tris = Polygon.TriangulateWithHoles(outer,
                Holes(Rect(1, 1, 3, 3), Rect(6, 1, 8, 3)), out var merged);   // 4 + 4

            Assert.Greater(tris.Count, 0);
            Assert.AreEqual(32f, TriangleAreaSum(merged, tris), 1e-2f);
        }

        [Test]
        public void TriangulateWithHoles_HandlesAHoleInAnLShape()
        {
            // 6x2 bottom strip + 3x4 upper leg = 24, minus a 1x2 shaft = 22
            var l = new List<Vector3> { P(0, 0), P(6, 0), P(6, 2), P(3, 2), P(3, 6), P(0, 6) };
            var tris = Polygon.TriangulateWithHoles(l, Holes(Rect(1, 3, 2, 5)), out var merged);

            Assert.Greater(tris.Count, 0);
            Assert.AreEqual(22f, TriangleAreaSum(merged, tris), 1e-2f);
        }

        [Test]
        public void TriangulateWithHoles_DrawingDirectionOfTheHoleDoesNotMatter()
        {
            var cwHole = Rect(2, 1, 4, 3);
            var ccwHole = new List<Vector3>(cwHole);
            ccwHole.Reverse();

            var a = Polygon.TriangulateWithHoles(Rect(0, 0, 6, 4), Holes(cwHole), out var mergedA);
            var b = Polygon.TriangulateWithHoles(Rect(0, 0, 6, 4), Holes(ccwHole), out var mergedB);

            Assert.AreEqual(TriangleAreaSum(mergedA, a), TriangleAreaSum(mergedB, b), 1e-2f);
        }

        [Test]
        public void TriangulateWithHoles_NoHoles_MatchesThePlainCase()
        {
            var outer = Rect(0, 0, 6, 4);
            var tris = Polygon.TriangulateWithHoles(outer, null, out var merged);
            Assert.AreEqual(24f, TriangleAreaSum(merged, tris), 1e-3f);
        }

        [Test]
        public void TriangulateWithHoles_IgnoresAJunkHole()
        {
            // a two-point "hole" is not a shape; the slab should still build
            var outer = Rect(0, 0, 6, 4);
            var junk = new List<Vector3> { P(2, 1), P(4, 1) };
            var tris = Polygon.TriangulateWithHoles(outer, Holes(junk), out var merged);

            Assert.AreEqual(24f, TriangleAreaSum(merged, tris), 1e-2f, "the junk ring is skipped, not fatal");
        }

        [Test]
        public void TriangulateWithHoles_RefusesACrossedOutline()
        {
            var bowtie = new List<Vector3> { P(0, 0), P(4, 3), P(4, 0), P(0, 3) };
            Assert.AreEqual(0, Polygon.TriangulateWithHoles(bowtie, Holes(Rect(1, 1, 2, 2)), out _).Count);
        }
    }
}
