using System.Collections.Generic;
using NUnit.Framework;
using RoomPlanner.Electrical;
using UnityEngine;

namespace RoomPlanner.Tests
{
    public class WireMathTests
    {
        // ---- PolylineLength ----

        [Test]
        public void PolylineLength_SumsSegments()
        {
            var pts = new List<Vector3> { Vector3.zero, new(3f, 0f, 0f), new(3f, 4f, 0f) };
            Assert.AreEqual(7f, WireMath.PolylineLength(pts), 1e-5);
        }

        [Test]
        public void PolylineLength_EmptyOrSingle_IsZero()
        {
            Assert.AreEqual(0f, WireMath.PolylineLength(null), 1e-6);
            Assert.AreEqual(0f, WireMath.PolylineLength(new List<Vector3>()), 1e-6);
            Assert.AreEqual(0f, WireMath.PolylineLength(new List<Vector3> { Vector3.one }), 1e-6);
        }

        // ---- OrthoElbow (v2 manual routing: no automatic detour to the ceiling) ----

        [Test]
        public void OrthoElbow_ClimbingPair_ElbowAboveTheStart()
        {
            var mid = new List<Vector3>();
            // outlet up to a wall/ceiling junction click: rise first, travel on top
            WireMath.OrthoElbow(new Vector3(0f, 0.3f, 0f), new Vector3(2f, 2.7f, 0f), mid);
            Assert.AreEqual(1, mid.Count);
            Assert.AreEqual(new Vector3(0f, 2.7f, 0f), mid[0]);
        }

        [Test]
        public void OrthoElbow_DroppingPair_ElbowAboveTheTarget()
        {
            var mid = new List<Vector3>();
            // ceiling run down to a switch: travel on top, then a vertical drop
            WireMath.OrthoElbow(new Vector3(0f, 2.7f, 0f), new Vector3(2f, 0.9f, 0f), mid);
            Assert.AreEqual(1, mid.Count);
            Assert.AreEqual(new Vector3(2f, 2.7f, 0f), mid[0]);
        }

        [Test]
        public void OrthoElbow_OutletToOutlet_StraightRun_NeverViaCeiling()
        {
            var mid = new List<Vector3>();
            // the v2 headline: two outlets at the same height connect directly
            WireMath.OrthoElbow(new Vector3(0f, 0.3f, 0f), new Vector3(3f, 0.3f, 0f), mid);
            Assert.AreEqual(0, mid.Count);
        }

        [Test]
        public void OrthoElbow_VerticalPair_NoElbow()
        {
            var mid = new List<Vector3>();
            WireMath.OrthoElbow(new Vector3(1f, 0.3f, 0f), new Vector3(1f, 2.0f, 0f), mid);
            Assert.AreEqual(0, mid.Count, "no horizontal travel — plain vertical drop");
        }

        [Test]
        public void OrthoElbow_NearLevelPair_NoNoiseElbow()
        {
            var mid = new List<Vector3>();
            // 5 mm of height difference is hand tremor, not an elbow
            WireMath.OrthoElbow(new Vector3(0f, 2.295f, 0f), new Vector3(2f, 2.3f, 0f), mid);
            Assert.AreEqual(0, mid.Count);
        }

        // ---- CollapsePoints ----

        [Test]
        public void CollapsePoints_DropsDoubleClicks_KeepsEndpoints()
        {
            var pts = new List<Vector3>
            {
                Vector3.zero,
                new(0.004f, 0f, 0f),      // double click near the start
                new(1f, 0f, 0f),
                new(1.001f, 0f, 0f),      // double click near the end — endpoint must survive
            };
            WireMath.CollapsePoints(pts, 0.01f);
            Assert.AreEqual(2, pts.Count);
            Assert.AreEqual(Vector3.zero, pts[0]);
            Assert.AreEqual(new Vector3(1.001f, 0f, 0f), pts[1], "the last endpoint is kept");
        }

        // ---- BuildTube ----

        private static void AssertOutward(List<Vector3> v, List<int> t, Vector3 axisA, Vector3 axisB)
        {
            Vector3 axisDir = (axisB - axisA).normalized;
            for (int i = 0; i + 2 < t.Count; i += 3)
            {
                Vector3 a = v[t[i]], b = v[t[i + 1]], c = v[t[i + 2]];
                Vector3 n = Vector3.Cross(b - a, c - a);
                Vector3 centroid = (a + b + c) / 3f;
                float axial = Mathf.Abs(Vector3.Dot(n.normalized, axisDir));
                if (axial > 0.9f)
                {
                    // cap: must face away from the segment midpoint
                    Vector3 mid = (axisA + axisB) * 0.5f;
                    Assert.Greater(Vector3.Dot(n, centroid - mid), 0f, $"cap {i / 3} faces inward");
                }
                else
                {
                    // side: must face away from the axis
                    Vector3 onAxis = axisA + axisDir * Vector3.Dot(centroid - axisA, axisDir);
                    Assert.Greater(Vector3.Dot(n, centroid - onAxis), 0f, $"side face {i / 3} faces inward");
                }
            }
        }

        [Test]
        public void BuildTube_StraightSegment_AllFacesOutward()
        {
            var v = new List<Vector3>(); var t = new List<int>(); var uv = new List<Vector2>();
            var a = Vector3.zero; var b = new Vector3(1f, 0f, 0f);
            WireMath.BuildTube(new List<Vector3> { a, b }, 0.0075f, 8, v, t, uv);
            Assert.Greater(t.Count, 0);
            AssertOutward(v, t, a, b);
        }

        [Test]
        public void BuildTube_VerticalSegment_AllFacesOutward()
        {
            var v = new List<Vector3>(); var t = new List<int>(); var uv = new List<Vector2>();
            var a = new Vector3(1f, 0.3f, 2f); var b = new Vector3(1f, 2.3f, 2f);
            WireMath.BuildTube(new List<Vector3> { a, b }, 0.0075f, 8, v, t, uv);
            Assert.Greater(t.Count, 0);
            AssertOutward(v, t, a, b);
        }

        [Test]
        public void BuildTube_EnclosedVolume_MatchesPrism()
        {
            var v = new List<Vector3>(); var t = new List<int>(); var uv = new List<Vector2>();
            const float r = 0.01f; const float len = 2f; const int sides = 8;
            WireMath.BuildTube(new List<Vector3> { Vector3.zero, new(len, 0f, 0f) }, r, sides, v, t, uv);

            // signed volume via divergence theorem: positive iff winding is outward
            float vol = 0f;
            for (int i = 0; i + 2 < t.Count; i += 3)
                vol += Vector3.Dot(v[t[i]], Vector3.Cross(v[t[i + 1]], v[t[i + 2]])) / 6f;
            float expected = 0.5f * sides * r * r * Mathf.Sin(2f * Mathf.PI / sides) * len;
            Assert.AreEqual(expected, vol, expected * 0.01f, "closed outward-wound prism volume");
        }

        [Test]
        public void BuildTube_VertexCount_TwoRingsPlusTwoCentersPerSegment()
        {
            var v = new List<Vector3>(); var t = new List<int>(); var uv = new List<Vector2>();
            var pts = new List<Vector3> { Vector3.zero, new(1f, 0f, 0f), new(1f, 1f, 0f) };
            WireMath.BuildTube(pts, 0.0075f, 8, v, t, uv);
            Assert.AreEqual(2 * (2 * 8 + 2), v.Count);
            Assert.AreEqual(v.Count, uv.Count, "one uv per vertex");
        }

        [Test]
        public void BuildTube_DegenerateInput_ProducesNothing()
        {
            var v = new List<Vector3>(); var t = new List<int>(); var uv = new List<Vector2>();
            WireMath.BuildTube(new List<Vector3> { Vector3.one }, 0.0075f, 8, v, t, uv);
            Assert.AreEqual(0, v.Count);
            WireMath.BuildTube(new List<Vector3> { Vector3.zero, new(0.001f, 0f, 0f) }, 0.0075f, 8, v, t, uv);
            Assert.AreEqual(0, v.Count, "sub-merge segment is skipped");
            WireMath.BuildTube(null, 0.0075f, 8, v, t, uv);
            Assert.AreEqual(0, v.Count);
        }
    }
}
