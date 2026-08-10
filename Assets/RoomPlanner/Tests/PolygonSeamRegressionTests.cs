using NUnit.Framework;
using RoomPlanner.Core;
using System.Collections.Generic;
using UnityEngine;

namespace RoomPlanner.Tests
{
    /// <summary>
    /// Regressions found 2026-08-10 while validating the icon set (design/20 §6.1), but the
    /// bugs live in Polygon and threaten floor slabs with 2+ holes just the same:
    /// 1) a second hole's bridge landing on the first seam's vertex made a triple point —
    ///    the ring stopped being weakly simple and ear clipping deadlocked;
    /// 2) an ear whose diagonal crossed a hole edge without containing any vertex emitted
    ///    a triangle overlapping the hole.
    /// </summary>
    public class PolygonSeamRegressionTests
    {
        private static List<Vector3> Rect(float x0, float z0, float x1, float z1) => new()
        {
            new Vector3(x0, 0, z0), new Vector3(x1, 0, z0),
            new Vector3(x1, 0, z1), new Vector3(x0, 0, z1),
        };

        [Test]
        public void SeamThroughAHoleVertex_IsRejected_AndBridgesElsewhere()
        {
            // Hole (2,2)-(3,3) in a 6×6 square: the nearest visible bridge target from
            // (3,3) is outline corner (0,0), but that seam runs exactly through hole
            // corner (2,2) — SegmentsCross (strict) never saw it, the spliced ring
            // pinched itself and ear clipping deadlocked (audit 2026-08-10, 04 §Б1).
            var tris = Polygon.TriangulateWithHoles(
                Rect(0, 0, 6, 6), new List<List<Vector3>> { Rect(2, 2, 3, 3) }, out var merged);
            Assert.Greater(tris.Count, 0, "must triangulate via a different seam");
            Assert.AreEqual(35f, TriArea(merged, tris), 1e-2f, "area = 36 − 1");
        }

        private static float TriArea(List<Vector3> pts, List<int> tris)
        {
            float sum = 0f;
            for (int t = 0; t + 2 < tris.Count; t += 3)
            {
                Vector3 a = pts[tris[t]], b = pts[tris[t + 1]], c = pts[tris[t + 2]];
                sum += Mathf.Abs((b.x - a.x) * (c.z - a.z) - (b.z - a.z) * (c.x - a.x)) * 0.5f;
            }
            return sum;
        }

        [Test]
        public void TwoHoles_WhereSecondSeamWouldLandOnFirstSeamVertex_Triangulates()
        {
            // The exact shape that deadlocked: outer sheet, a large hole whose corner is the
            // nearest visible vertex for the second, smaller hole (blueprint-icon geometry).
            var outer = Rect(4, 3, 20, 21);
            var holes = new List<IReadOnlyList<Vector3>>
            {
                Rect(7, 7, 17, 17),          // large hole — bridged first (rightmost)
                Rect(7, 18.5f, 13, 19.8f),   // small hole — nearest vertex is (17,17), a seam vertex
            };

            var tris = Polygon.TriangulateWithHoles(outer, holes, out var merged);
            Assert.Greater(tris.Count, 0, "two-hole shape must triangulate");
            // 16×18 − 10×10 − 6×1.3
            Assert.AreEqual(288f - 100f - 7.8f, TriArea(merged, tris), 0.01f);
        }

        [Test]
        public void TwoHoles_NoTriangleOverlapsEitherHole()
        {
            var outer = Rect(4, 3, 20, 21);
            var h1 = Rect(7, 7, 17, 17);
            var h2 = Rect(7, 18.5f, 13, 19.8f);
            var tris = Polygon.TriangulateWithHoles(outer,
                new List<IReadOnlyList<Vector3>> { h1, h2 }, out var merged);
            Assert.Greater(tris.Count, 0);

            for (int t = 0; t + 2 < tris.Count; t += 3)
            {
                Vector3 centroid = (merged[tris[t]] + merged[tris[t + 1]] + merged[tris[t + 2]]) / 3f;
                Assert.IsFalse(Polygon.Contains(h1, centroid),
                    $"triangle {t / 3} centroid falls inside hole 1");
                Assert.IsFalse(Polygon.Contains(h2, centroid),
                    $"triangle {t / 3} centroid falls inside hole 2");
            }
        }

        [Test]
        public void FiveHoles_StillTriangulates()
        {
            // seams for several holes competing for nearby targets — the duplicate-avoiding
            // target pick must keep every seam on its own vertex. (Known limit: a DENSE grid
            // of 8+ holes can still chain seams into a deadlock — the clipper then refuses
            // with an empty result per its contract rather than emitting garbage.)
            var outer = Rect(0, 0, 30, 30);
            var holes = new List<IReadOnlyList<Vector3>>
            {
                Rect(4, 4, 10, 10), Rect(20, 4, 26, 10),
                Rect(4, 20, 10, 26), Rect(20, 20, 26, 26),
                Rect(13, 13, 17, 17),
            };
            var tris = Polygon.TriangulateWithHoles(outer, holes, out var merged);
            Assert.Greater(tris.Count, 0);
            Assert.AreEqual(900f - 4f * 36f - 16f, TriArea(merged, tris), 0.01f);
        }
    }
}
