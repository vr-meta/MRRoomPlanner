using System.Collections.Generic;
using NUnit.Framework;
using RoomPlanner.Core;
using UnityEngine;

namespace RoomPlanner.Tests
{
    /// <summary>SvgPath parser: the exact command subset the icon guide allows (20 §5).</summary>
    public class SvgPathTests
    {
        [Test]
        public void AbsoluteSquare_ParsesAsOneRing()
        {
            var rings = SvgPath.Parse("M2 2 L22 2 L22 22 L2 22 Z");
            Assert.AreEqual(1, rings.Count);
            Assert.AreEqual(4, rings[0].Count);
            Assert.AreEqual(new Vector2(2, 2), rings[0][0]);
            Assert.AreEqual(new Vector2(2, 22), rings[0][3]);
        }

        [Test]
        public void RelativeCommands_AccumulateFromPen()
        {
            var rings = SvgPath.Parse("m2 2 l20 0 l0 20 l-20 0 z");
            Assert.AreEqual(1, rings.Count);
            Assert.AreEqual(new Vector2(22, 22), rings[0][2]);
        }

        [Test]
        public void HorizontalAndVertical_MoveOneAxis()
        {
            var rings = SvgPath.Parse("M2 2 H22 V22 H2 Z");
            Assert.AreEqual(4, rings[0].Count);
            Assert.AreEqual(new Vector2(22, 2), rings[0][1]);
            Assert.AreEqual(new Vector2(22, 22), rings[0][2]);
        }

        [Test]
        public void RelativeHV_OffsetFromPen()
        {
            var rings = SvgPath.Parse("M2 2 h20 v20 h-20 Z");
            Assert.AreEqual(new Vector2(22, 22), rings[0][2]);
        }

        [Test]
        public void ImplicitLineto_AfterMove()
        {
            // per SVG spec, numbers after M continue as L
            var rings = SvgPath.Parse("M2 2 22 2 22 22 2 22 Z");
            Assert.AreEqual(1, rings.Count);
            Assert.AreEqual(4, rings[0].Count);
        }

        [Test]
        public void ImplicitRepetition_OfLineto()
        {
            var rings = SvgPath.Parse("M0 0 L4 0 4 4 0 4 Z");
            Assert.AreEqual(4, rings[0].Count);
            Assert.AreEqual(new Vector2(0, 4), rings[0][3]);
        }

        [Test]
        public void CommasAndNegativeDecimals_Tokenize()
        {
            var rings = SvgPath.Parse("M-1.5,-2.25 L3.5,-2.25 L3.5,4 L-1.5,4 Z");
            Assert.AreEqual(new Vector2(-1.5f, -2.25f), rings[0][0]);
            Assert.AreEqual(new Vector2(3.5f, 4f), rings[0][2]);
        }

        [Test]
        public void Cubic_FlattensToSegments_EndingExactly()
        {
            var rings = SvgPath.Parse("M0 12 C0 5 5 0 12 0 L12 12 Z");
            // 1 move point + CurveSegments curve points + 1 line point
            Assert.AreEqual(2 + SvgPath.CurveSegments, rings[0].Count);
            var curveEnd = rings[0][SvgPath.CurveSegments];
            Assert.Less(Vector2.Distance(curveEnd, new Vector2(12, 0)), 1e-4f);
        }

        [Test]
        public void Quadratic_FlattensToSegments_EndingExactly()
        {
            var rings = SvgPath.Parse("M0 0 Q12 0 12 12 L0 12 Z");
            var curveEnd = rings[0][SvgPath.CurveSegments];
            Assert.Less(Vector2.Distance(curveEnd, new Vector2(12, 12)), 1e-4f);
        }

        [Test]
        public void RelativeCubic_OffsetsFromPen()
        {
            var abs = SvgPath.Parse("M10 10 C10 3 15 0 20 0 L20 10 Z");
            var rel = SvgPath.Parse("M10 10 c0 -7 5 -10 10 -10 l0 10 z");
            Assert.AreEqual(abs[0].Count, rel[0].Count);
            for (int i = 0; i < abs[0].Count; i++)
                Assert.Less(Vector2.Distance(abs[0][i], rel[0][i]), 1e-4f);
        }

        [Test]
        public void TwoSubpaths_BothReturned()
        {
            var rings = SvgPath.Parse("M0 0 L4 0 L4 4 L0 4 Z M8 8 L12 8 L12 12 L8 12 Z");
            Assert.AreEqual(2, rings.Count);
        }

        [Test]
        public void OpenSubpath_ClosesImplicitly()
        {
            // fill semantics: an unclosed triangle is still a triangle
            var rings = SvgPath.Parse("M0 0 L4 0 L2 4");
            Assert.AreEqual(1, rings.Count);
            Assert.AreEqual(3, rings[0].Count);
        }

        [Test]
        public void ExplicitClosingPoint_Deduplicated()
        {
            var rings = SvgPath.Parse("M0 0 L4 0 L4 4 L0 4 L0 0 Z");
            Assert.AreEqual(4, rings[0].Count);
        }

        [Test]
        public void DegenerateSubpath_Dropped()
        {
            var rings = SvgPath.Parse("M0 0 L4 0 Z M8 8 L12 8 L12 12 L8 12 Z");
            Assert.AreEqual(1, rings.Count);   // the 2-point sliver is not a ring
        }

        [Test]
        public void UnsupportedArcCommand_RefusesWholePath()
        {
            var rings = SvgPath.Parse("M0 0 A5 5 0 0 1 10 10 Z");
            Assert.AreEqual(0, rings.Count);
        }

        [Test]
        public void MalformedNumber_RefusesWholePath()
        {
            Assert.AreEqual(0, SvgPath.Parse("M0 0 L4 # Z").Count);
            Assert.AreEqual(0, SvgPath.Parse("M0 0 L4").Count);   // odd coordinate count
        }

        [Test]
        public void EmptyInput_ReturnsEmpty()
        {
            Assert.AreEqual(0, SvgPath.Parse("").Count);
            Assert.AreEqual(0, SvgPath.Parse(null).Count);
        }
    }

    /// <summary>SvgTessellator: even-odd fill → mesh facing −Z, normalized to [−0.5, 0.5]².</summary>
    public class SvgTessellatorTests
    {
        private const string FullSquare = "M0 0 L24 0 L24 24 L0 24 Z";

        [Test]
        public void FullSquare_AreaIsOne()
        {
            var mesh = SvgTessellator.Build(FullSquare);
            Assert.IsFalse(mesh.IsEmpty);
            Assert.AreEqual(1f, SvgTessellator.Area(mesh), 1e-4f);
        }

        [Test]
        public void Vertices_NormalizedAndFlat()
        {
            var mesh = SvgTessellator.Build(FullSquare);
            foreach (var v in mesh.Vertices)
            {
                Assert.LessOrEqual(Mathf.Abs(v.x), 0.5f + 1e-5f);
                Assert.LessOrEqual(Mathf.Abs(v.y), 0.5f + 1e-5f);
                Assert.AreEqual(0f, v.z, 1e-6f);
            }
        }

        [Test]
        public void SvgYDown_MapsToMeshYUp()
        {
            // a bar across the TOP of the viewBox (small SVG y) must land at POSITIVE mesh y
            var mesh = SvgTessellator.Build("M0 0 L24 0 L24 6 L0 6 Z");
            float minY = float.MaxValue, maxY = float.MinValue;
            foreach (var v in mesh.Vertices) { minY = Mathf.Min(minY, v.y); maxY = Mathf.Max(maxY, v.y); }
            Assert.AreEqual(0.5f, maxY, 1e-4f);
            Assert.AreEqual(0.25f, minY, 1e-4f);
        }

        [Test]
        public void Triangles_FaceViewer_MinusZ()
        {
            // Unity front face: Cross(b−a, c−a) is the render normal — must be −Z here,
            // same convention as panel text children sitting at negative z offsets
            var mesh = SvgTessellator.Build("M2 2 L22 2 L22 22 L2 22 Z M8 8 L16 8 L16 16 L8 16 Z");
            for (int t = 0; t + 2 < mesh.Triangles.Length; t += 3)
            {
                Vector3 a = mesh.Vertices[mesh.Triangles[t]];
                Vector3 b = mesh.Vertices[mesh.Triangles[t + 1]];
                Vector3 c = mesh.Vertices[mesh.Triangles[t + 2]];
                Vector3 n = Vector3.Cross(b - a, c - a);
                Assert.Less(n.z, 0f, $"triangle {t / 3} faces away from the viewer");
            }
        }

        [Test]
        public void EvenOdd_InnerRingIsHole()
        {
            var mesh = SvgTessellator.Build("M0 0 L24 0 L24 24 L0 24 Z M6 6 L18 6 L18 18 L6 18 Z");
            // 24² − 12² = 432 of 576
            Assert.AreEqual(432f / 576f, SvgTessellator.Area(mesh), 1e-3f);
        }

        [Test]
        public void EvenOdd_IslandInsideHoleIsSolid()
        {
            var mesh = SvgTessellator.Build(
                "M0 0 L24 0 L24 24 L0 24 Z M4 4 L20 4 L20 20 L4 20 Z M9 9 L15 9 L15 15 L9 15 Z");
            // 576 − 256 + 36
            Assert.AreEqual((576f - 256f + 36f) / 576f, SvgTessellator.Area(mesh), 1e-3f);
        }

        [Test]
        public void DisjointRings_BothSolid()
        {
            var mesh = SvgTessellator.Build("M0 0 L10 0 L10 10 L0 10 Z M14 14 L24 14 L24 24 L14 24 Z");
            Assert.AreEqual(200f / 576f, SvgTessellator.Area(mesh), 1e-3f);
        }

        [Test]
        public void AuthorWinding_DoesNotMatter()
        {
            // same hole drawn CW and CCW — Polygon normalizes winding internally
            var a = SvgTessellator.Build("M0 0 L24 0 L24 24 L0 24 Z M6 6 L18 6 L18 18 L6 18 Z");
            var b = SvgTessellator.Build("M0 0 L24 0 L24 24 L0 24 Z M6 6 L6 18 L18 18 L18 6 Z");
            Assert.AreEqual(SvgTessellator.Area(a), SvgTessellator.Area(b), 1e-3f);
        }

        [Test]
        public void CurvedShape_Tessellates()
        {
            // quarter-round "D" — curve flattening feeds the triangulator directly
            var mesh = SvgTessellator.Build("M4 2 L14 2 C19 2 22 6 22 12 C22 18 19 22 14 22 L4 22 Z");
            Assert.IsFalse(mesh.IsEmpty);
            Assert.Greater(SvgTessellator.Area(mesh), 0.4f);
            Assert.Less(SvgTessellator.Area(mesh), 0.8f);
        }

        [Test]
        public void SelfIntersectingRing_RefusedWhole()
        {
            var mesh = SvgTessellator.Build("M0 0 L24 24 L24 0 L0 24 Z");   // bow-tie
            Assert.IsTrue(mesh.IsEmpty);
        }

        [Test]
        public void EmptyInput_EmptyMesh()
        {
            Assert.IsTrue(SvgTessellator.Build("").IsEmpty);
            Assert.IsTrue(SvgTessellator.Tessellate(null).IsEmpty);
            Assert.AreEqual(0f, SvgTessellator.Area(new SvgMesh()));
        }
    }
}
