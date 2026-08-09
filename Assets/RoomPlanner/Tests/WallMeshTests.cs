using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using RoomPlanner.Walls;

namespace RoomPlanner.Tests
{
    /// <summary>
    /// Phase B / step B2 — per-segment footprints whose ends are shaped by the graph
    /// (docs/design/13-phase-b-wallgraph.md).
    ///
    /// The headline test is NoRegression_LCorner_MatchesPolylineMiter: the whole risk of this
    /// phase is that moving from one polyline mesh to independent segments quietly ruins
    /// corners, so the new joint is pinned against the geometry the shipping builder produces.
    /// </summary>
    public class WallMeshTests
    {
        private const float Thick = 0.2f;
        private const float Height = 2.7f;

        private static Vector3 P(float x, float z) => new Vector3(x, 0f, z);

        /// <summary>Graph segment with the current defaults, thickened to a chosen side.</summary>
        private static WallSegment Seg(WallGraph g, Vector3 a, Vector3 b, float sideSign)
        {
            var s = g.AddSegment(g.SnapOrCreateNode(a), g.SnapOrCreateNode(b));
            s.Thickness = Thick;
            s.Height = Height;
            s.Offset = WallOffsetMode.Outer;
            s.SideSign = sideSign;
            return s;
        }

        // ---- the regression guard ----

        [Test]
        public void NoRegression_LCorner_MatchesPolylineMiter()
        {
            // Same L the shipping Wall builds: east along X, then north along Z.
            Vector3 a = P(0, 0), b = P(1, 0), c = P(1, 1);
            Vector3 interior = new Vector3(0.5f, 0f, -1f);   // we stand south of the first leg

            // --- reference: today's polyline mesh ---
            var go = new GameObject("WallRef");
            Vector3 refInner, refOuter;
            try
            {
                var wall = go.AddComponent<Wall>();
                wall.Build(new List<Vector3> { a, b, c }, Thick, Height, WallOffsetMode.Outer, WallJoin.Miter, interior);
                var v = go.GetComponent<MeshFilter>().sharedMesh.vertices;
                // cross-section 1 is the corner; layout per section is [Inner, Outer, Inner+up, Outer+up]
                refInner = v[4];
                refOuter = v[5];
            }
            finally { Object.DestroyImmediate(go); }

            // --- new: two graph segments sharing the corner node ---
            // The polyline picks the side once and keeps it for every leg, so both segments
            // take the same sign (-1 = thicken against the right normal = left of travel).
            var g = new WallGraph();
            var ab = Seg(g, a, b, -1f);
            var bc = Seg(g, b, c, -1f);
            var corner = ab.B;
            Assert.AreSame(corner, bc.A, "the two legs must share the corner node");

            var fab = WallMesh.BuildFootprint(ab);
            var fbc = WallMesh.BuildFootprint(bc);

            // With SideSign -1 the body grows against the right normal, so the drawn line is the
            // "Right" face and the mitred outer face is "Left".
            AssertSameXZ(refInner, fab.BRight, "inner corner (on the drawn line)");
            AssertSameXZ(refOuter, fab.BLeft, "outer corner (mitred)");

            // the neighbour leg must report the very same corner points — one shared corner
            AssertSameXZ(fab.BRight, fbc.ARight, "both legs agree on the inner corner");
            AssertSameXZ(fab.BLeft, fbc.ALeft, "both legs agree on the mitred outer corner");
        }

        // ---- straight, isolated segment ----

        [Test]
        public void LoneSegment_IsAFlatSlab_OfExactThickness()
        {
            var g = new WallGraph();
            var s = Seg(g, P(0, 0), P(4, 0), +1f);
            var f = WallMesh.BuildFootprint(s);

            // Outer mode: the drawn line IS one face and the body grows to the side SideSign
            // picks. Direction is +X so the right normal is -Z; with SideSign +1 the body
            // extends along it, making "Right" the thick face and "Left" the drawn line.
            Assert.AreEqual(-Thick, f.ARight.z, 1e-4f, "thick face, one thickness off the line");
            Assert.AreEqual(0f, f.ALeft.z, 1e-4f, "the drawn line is the other face");
        }

        [Test]
        public void LoneSegment_FacesAreOneThicknessApart()
        {
            var g = new WallGraph();
            var s = Seg(g, P(0, 0), P(4, 0), +1f);
            var f = WallMesh.BuildFootprint(s);

            Assert.AreEqual(Thick, Vector3.Distance(f.ARight, f.ALeft), 1e-4f);
            Assert.AreEqual(Thick, Vector3.Distance(f.BRight, f.BLeft), 1e-4f);
            Assert.AreEqual(4f, Vector3.Distance(f.ARight, f.BRight), 1e-4f, "length is untouched by the joint");
        }

        [Test]
        public void CenterMode_IsSymmetricAboutTheLine()
        {
            var g = new WallGraph();
            var s = Seg(g, P(0, 0), P(4, 0), +1f);
            s.Offset = WallOffsetMode.Center;
            var f = WallMesh.BuildFootprint(s);

            Assert.AreEqual(Thick * 0.5f, Mathf.Abs(f.ARight.z), 1e-4f);
            Assert.AreEqual(Thick * 0.5f, Mathf.Abs(f.ALeft.z), 1e-4f);
            Assert.AreEqual(-f.ARight.z, f.ALeft.z, 1e-4f, "faces sit on opposite sides");
        }

        [Test]
        public void SideSign_FlipsWhichFaceIsThick()
        {
            var g1 = new WallGraph();
            var plus = WallMesh.BuildFootprint(Seg(g1, P(0, 0), P(4, 0), +1f));
            var g2 = new WallGraph();
            var minus = WallMesh.BuildFootprint(Seg(g2, P(0, 0), P(4, 0), -1f));

            Assert.AreNotEqual(plus.ARight.z, minus.ARight.z, "the thick side must actually swap");
            Assert.AreEqual(-plus.ALeft.z, minus.ARight.z, 1e-4f);
        }

        // ---- straight run through a node (the split case) ----

        [Test]
        public void SplitWall_StaysStraight_AcrossTheNewNode()
        {
            // A wall split for a T-junction must not develop a kink at the split point.
            var g = new WallGraph();
            var s = Seg(g, P(0, 0), P(4, 0), +1f);
            var mid = g.SplitSegmentAt(s, P(2, 0));
            var tail = mid.Segments[0] == s ? mid.Segments[1] : mid.Segments[0];

            var f1 = WallMesh.BuildFootprint(s);
            var f2 = WallMesh.BuildFootprint(tail);

            AssertSameXZ(f1.BRight, f2.ARight, "faces line up across the split");
            AssertSameXZ(f1.BLeft, f2.ALeft);
            Assert.AreEqual(-Thick, f1.BRight.z, 1e-4f, "still straight — no kink at the node");
            Assert.AreEqual(0f, f1.BLeft.z, 1e-4f);
        }

        // ---- T-junction ----

        [Test]
        public void TJunction_StemIsCutAgainstTheThroughWall()
        {
            //   through wall along X at z = 0, stem going north from its middle
            var g = new WallGraph();
            var through = Seg(g, P(0, 0), P(4, 0), +1f);
            var mid = g.SplitSegmentAt(through, P(2, 0));
            var stem = g.AddSegment(mid, g.SnapOrCreateNode(P(2, 3)));
            stem.Thickness = Thick;
            stem.Height = Height;
            stem.Offset = WallOffsetMode.Outer;
            stem.SideSign = +1f;

            Assert.AreEqual(3, mid.Degree, "precondition: a real T");

            var fs = WallMesh.BuildFootprint(stem);

            // The stem starts at the through wall, so its two corners sit on that wall's line
            // (z = 0) — not floating in the middle of it and not short of it.
            Assert.AreEqual(0f, fs.ARight.z, 1e-3f, "stem meets the through wall");
            Assert.AreEqual(0f, fs.ALeft.z, 1e-3f);
            Assert.AreEqual(Thick, Vector3.Distance(fs.ARight, fs.ALeft), 1e-3f,
                "the stem keeps its thickness at the junction");
        }

        [Test]
        public void TJunction_ThroughWallKeepsItsFaces()
        {
            var g = new WallGraph();
            var through = Seg(g, P(0, 0), P(4, 0), +1f);
            var mid = g.SplitSegmentAt(through, P(2, 0));
            var stem = g.AddSegment(mid, g.SnapOrCreateNode(P(2, 3)));
            stem.Thickness = Thick;
            stem.Offset = WallOffsetMode.Outer;
            stem.SideSign = +1f;

            var f = WallMesh.BuildFootprint(through);

            // A wall someone tee'd into must not bend: the far end is untouched and the
            // junction end still sits on the same two lines.
            Assert.AreEqual(-Thick, f.ARight.z, 1e-3f);
            Assert.AreEqual(-Thick, f.BRight.z, 1e-3f, "the through wall stays straight at the T");
            Assert.AreEqual(0f, f.BLeft.z, 1e-3f);
        }

        // ---- degenerate input (coding rule 1.3) ----

        [Test]
        public void DoublingBack_DoesNotSpike()
        {
            // ~180° turn: an unclamped miter runs off to infinity.
            var g = new WallGraph();
            var ab = Seg(g, P(0, 0), P(2, 0), +1f);
            var bc = Seg(g, P(2, 0), P(0.02f, 0.001f), +1f);   // folds back on itself

            var f = WallMesh.BuildFootprint(ab);
            float reach = Vector3.Distance(f.BRight, ab.B.Position);

            Assert.Less(reach, WallMesh.MiterLimit * Thick + 1e-3f,
                "the miter is clamped instead of spiking out");
            Assert.IsFalse(float.IsNaN(f.BRight.x) || float.IsNaN(f.BRight.z), "no NaN in the footprint");
        }

        [Test]
        public void NegativeThickness_IsNormalised()
        {
            var g = new WallGraph();
            var s = Seg(g, P(0, 0), P(4, 0), +1f);
            s.Thickness = -Thick;
            var f = WallMesh.BuildFootprint(s);

            Assert.AreEqual(Thick, Vector3.Distance(f.ARight, f.ALeft), 1e-4f,
                "a negative thickness must not invert the wall");
        }

        [Test]
        public void NeighboursWithDifferentThickness_StillShareTheCorner()
        {
            var g = new WallGraph();
            var ab = Seg(g, P(0, 0), P(2, 0), -1f);
            var bc = Seg(g, P(2, 0), P(2, 2), -1f);
            bc.Thickness = 0.4f;                      // thicker neighbour

            var fab = WallMesh.BuildFootprint(ab);
            var fbc = WallMesh.BuildFootprint(bc);

            AssertSameXZ(fab.BRight, fbc.ARight, "inner corner is still a single point");
            AssertSameXZ(fab.BLeft, fbc.ALeft, "outer corner is still a single point");
        }

        private static void AssertSameXZ(Vector3 expected, Vector3 actual, string what = "")
        {
            Assert.AreEqual(expected.x, actual.x, 1e-3f, $"{what} (x)");
            Assert.AreEqual(expected.z, actual.z, 1e-3f, $"{what} (z)");
        }
    }
}
