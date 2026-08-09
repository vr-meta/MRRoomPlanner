using NUnit.Framework;
using UnityEngine;
using RoomPlanner.Core;

namespace RoomPlanner.Tests
{
    /// <summary>Degenerate-input coverage for MeasureMath (the happy paths live in MeasureMathTests).</summary>
    public class MeasureMathEdgeTests
    {
        [Test]
        public void RayPlaneY_RayStartingOnThePlane_Hits()
        {
            // A tool raycasting from a point ON the floor must not be told it missed.
            var ray = new Ray(new Vector3(1f, 0f, 2f), Vector3.down);
            Assert.IsTrue(MeasureMath.RayPlaneY(ray, 0f, out var hit));
            Assert.AreEqual(ray.origin, hit);
        }

        [Test]
        public void RayPlaneY_PlaneBehindRay_Misses()
        {
            var ray = new Ray(new Vector3(0f, 1f, 0f), Vector3.up);
            Assert.IsFalse(MeasureMath.RayPlaneY(ray, 0f, out _));
        }

        [Test]
        public void SnapToGridXZ_NonPositiveSize_IsNoOp()
        {
            var p = new Vector3(1.234f, 5f, -0.678f);
            Assert.AreEqual(p, MeasureMath.SnapToGridXZ(p, 0f));
            Assert.AreEqual(p, MeasureMath.SnapToGridXZ(p, -1f));
        }

        [Test]
        public void SnapToAngleXZ_DegenerateDirection_ReturnsB()
        {
            var a = new Vector3(1f, 0f, 1f);
            var b = new Vector3(1f, 2f, 1f);   // no horizontal component
            Assert.AreEqual(b, MeasureMath.SnapToAngleXZ(a, b, 15f));
        }

        [Test]
        public void SnapToAngleXZ_NonPositiveStep_ClampsToOneDegree()
        {
            var a = Vector3.zero;
            var b = new Vector3(1f, 0f, 0.004f);   // ~0.23° off the X axis
            var snapped = MeasureMath.SnapToAngleXZ(a, b, 0f);
            Assert.AreEqual(0f, snapped.z, 1e-4f, "step clamps to 1°, direction snaps onto the axis");
        }

        [Test]
        public void ClosestPointOnSegment_DegenerateSegment_ReturnsA()
        {
            var a = new Vector3(2f, 0f, 3f);
            Assert.AreEqual(a, MeasureMath.ClosestPointOnSegment(a, a, new Vector3(9f, 9f, 9f)));
        }

        [Test]
        public void FormatDistanceCm_Negative_KeepsSign()
        {
            Assert.AreEqual("-50 cm", MeasureMath.FormatDistanceCm(-0.5f));
        }
    }
}
