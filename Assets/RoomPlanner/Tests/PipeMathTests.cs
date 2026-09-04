using System.Collections.Generic;
using NUnit.Framework;
using RoomPlanner.Plumbing;
using UnityEngine;

namespace RoomPlanner.Tests
{
    public class PipeMathTests
    {
        private readonly List<Vector3> _elbow = new();

        // ---- OrthoElbowLow: drainage travels at the LOWER of the two heights ----

        [Test]
        public void OrthoElbowLow_GoingDown_DropsFirstThenTravels()
        {
            PipeMath.OrthoElbowLow(new Vector3(0f, 1f, 0f), new Vector3(2f, 0f, 0f), _elbow);
            Assert.AreEqual(1, _elbow.Count);
            Assert.AreEqual(new Vector3(0f, 0f, 0f), _elbow[0],
                "the vertical drop comes first, the main lies along the bottom");
        }

        [Test]
        public void OrthoElbowLow_GoingUp_TravelsAlongTheBottomThenRises()
        {
            PipeMath.OrthoElbowLow(new Vector3(0f, 0f, 0f), new Vector3(2f, 1f, 3f), _elbow);
            Assert.AreEqual(1, _elbow.Count);
            Assert.AreEqual(new Vector3(2f, 0f, 3f), _elbow[0]);
        }

        [Test]
        public void OrthoElbowLow_LevelPair_IsAStraightSegment()
        {
            PipeMath.OrthoElbowLow(new Vector3(0f, 0.5f, 0f), new Vector3(2f, 0.5f, 1f), _elbow);
            Assert.AreEqual(0, _elbow.Count);
        }

        [Test]
        public void OrthoElbowLow_VerticalPair_NeedsNoElbow()
        {
            PipeMath.OrthoElbowLow(new Vector3(1f, 0f, 1f), new Vector3(1f, 2f, 1f), _elbow);
            Assert.AreEqual(0, _elbow.Count);
        }

        // ---- ClosestOnSegment: the riser-axis snap ----

        [Test]
        public void ClosestOnSegment_ProjectsOntoTheAxis()
        {
            var a = new Vector3(1f, 0f, 1f);
            var b = new Vector3(1f, 3f, 1f);
            var p = PipeMath.ClosestOnSegment(a, b, new Vector3(2f, 1.7f, 1f));
            Assert.AreEqual(0f, Vector3.Distance(new Vector3(1f, 1.7f, 1f), p), 1e-5f,
                "a tee lands at the click height");
        }

        [Test]
        public void ClosestOnSegment_ClampsToTheEnds()
        {
            var a = new Vector3(0f, 0f, 0f);
            var b = new Vector3(0f, 2f, 0f);
            Assert.AreEqual(b, PipeMath.ClosestOnSegment(a, b, new Vector3(0.5f, 5f, 0f)));
            Assert.AreEqual(a, PipeMath.ClosestOnSegment(a, b, new Vector3(0f, -3f, 0.2f)));
        }

        [Test]
        public void ClosestOnSegment_DegenerateSegment_ReturnsThePoint()
        {
            var a = new Vector3(1f, 1f, 1f);
            Assert.AreEqual(a, PipeMath.ClosestOnSegment(a, a, new Vector3(9f, 9f, 9f)));
        }

        // ---- CountElbows: fitting classification for the BOM ----

        [Test]
        public void CountElbows_RightAngle_Counts90()
        {
            var pts = new List<Vector3> { new(0f, 0f, 0f), new(2f, 0f, 0f), new(2f, 0f, 3f) };
            PipeMath.CountElbows(pts, out int d90, out int d45);
            Assert.AreEqual(1, d90);
            Assert.AreEqual(0, d45);
        }

        [Test]
        public void CountElbows_FortyFive_Counts45()
        {
            var pts = new List<Vector3>
            {
                new(0f, 0f, 0f), new(2f, 0f, 0f), new(4f, 0f, 2f),   // 45° turn in plan
            };
            PipeMath.CountElbows(pts, out int d90, out int d45);
            Assert.AreEqual(0, d90);
            Assert.AreEqual(1, d45);
        }

        [Test]
        public void CountElbows_CollinearAndShortPolylines_CountNothing()
        {
            var straight = new List<Vector3> { new(0f, 0f, 0f), new(1f, 0f, 0f), new(5f, 0f, 0f) };
            PipeMath.CountElbows(straight, out int d90, out int d45);
            Assert.AreEqual(0, d90 + d45);

            PipeMath.CountElbows(new List<Vector3> { Vector3.zero, Vector3.one }, out d90, out d45);
            Assert.AreEqual(0, d90 + d45);
            PipeMath.CountElbows(null, out d90, out d45);
            Assert.AreEqual(0, d90 + d45);
        }

        [Test]
        public void CountElbows_MixedRun_CountsEachBendOnce()
        {
            // floor run: 90 up the wall, 45 across, then 90 again
            var pts = new List<Vector3>
            {
                new(0f, 0f, 0f), new(3f, 0f, 0f),        // along the floor
                new(3f, 1f, 0f),                          // 90: up
                new(3f, 2f, 1f),                          // 45: diagonal
                new(3f, 2f, 4f),                          // 45 back to straight
            };
            PipeMath.CountElbows(pts, out int d90, out int d45);
            Assert.AreEqual(1, d90);
            Assert.AreEqual(2, d45);
        }
    }
}
