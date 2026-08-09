using NUnit.Framework;
using UnityEngine;
using RoomPlanner.Core;

namespace RoomPlanner.Tests
{
    public class MeasureMathTests
    {
        [Test]
        public void SnapToAxis_PicksVertical_WhenYDominant()
        {
            var a = Vector3.zero;
            var b = new Vector3(0.1f, 1f, 0.2f);
            var r = MeasureMath.SnapToAxis(a, b);
            Assert.AreEqual(0f, r.x, 1e-5f);
            Assert.AreEqual(1f, r.y, 1e-5f);
            Assert.AreEqual(0f, r.z, 1e-5f);
        }

        [Test]
        public void SnapToAxis_PicksHorizontalX_WhenXDominant()
        {
            var a = Vector3.zero;
            var b = new Vector3(2f, 0.1f, 0.3f);
            var r = MeasureMath.SnapToAxis(a, b);
            Assert.AreEqual(2f, r.x, 1e-5f);
            Assert.AreEqual(0f, r.y, 1e-5f);
            Assert.AreEqual(0f, r.z, 1e-5f);
        }

        [Test]
        public void SnapToAxis_PicksHorizontalZ_WhenZDominant()
        {
            var a = new Vector3(1f, 1f, 1f);
            var b = new Vector3(1.2f, 1.1f, 4f);
            var r = MeasureMath.SnapToAxis(a, b);
            Assert.AreEqual(1f, r.x, 1e-5f);
            Assert.AreEqual(1f, r.y, 1e-5f);
            Assert.AreEqual(4f, r.z, 1e-5f);
        }

        [Test]
        public void SnapToAngleXZ_SnapsNearZeroToAxis()
        {
            var a = Vector3.zero;
            var b = new Vector3(1f, 0f, 0.1f); // ~5.7°
            var r = MeasureMath.SnapToAngleXZ(a, b, 15f);
            float len = new Vector2(1f, 0.1f).magnitude;
            Assert.AreEqual(len, r.x, 1e-4f);
            Assert.AreEqual(0f, r.z, 1e-4f);
        }

        [Test]
        public void SnapToAngleXZ_KeepsExactMultiples()
        {
            var a = Vector3.zero;
            var b = new Vector3(1f, 0f, 1f); // 45° is a multiple of 15°
            var r = MeasureMath.SnapToAngleXZ(a, b, 15f);
            Assert.AreEqual(1f, r.x, 1e-4f);
            Assert.AreEqual(1f, r.z, 1e-4f);
        }

        [Test]
        public void ClosestPointOnSegment_ProjectsOntoMiddle()
        {
            var r = MeasureMath.ClosestPointOnSegment(new Vector3(0, 0, 0), new Vector3(2, 0, 0), new Vector3(1, 0, 1));
            Assert.AreEqual(1f, r.x, 1e-5f);
            Assert.AreEqual(0f, r.y, 1e-5f);
            Assert.AreEqual(0f, r.z, 1e-5f);
        }

        [Test]
        public void ClosestPointOnSegment_ClampsToEnds()
        {
            var a = new Vector3(0, 0, 0);
            var b = new Vector3(2, 0, 0);
            Assert.AreEqual(a, MeasureMath.ClosestPointOnSegment(a, b, new Vector3(-5, 0, 0)));
            Assert.AreEqual(b, MeasureMath.ClosestPointOnSegment(a, b, new Vector3(9, 0, 3)));
        }

        [Test]
        public void FormatDistanceCm_RoundsToCentimeters()
        {
            Assert.AreEqual("123 cm", MeasureMath.FormatDistanceCm(1.234f));
            Assert.AreEqual("0 cm", MeasureMath.FormatDistanceCm(0f));
            Assert.AreEqual("100 cm", MeasureMath.FormatDistanceCm(1f));
        }

        [Test]
        public void SnapToGridXZ_RoundsXZ_KeepsY()
        {
            var r = MeasureMath.SnapToGridXZ(new Vector3(0.11f, 1.37f, 0.24f), 0.05f);
            Assert.AreEqual(0.10f, r.x, 1e-5f);
            Assert.AreEqual(0.25f, r.z, 1e-5f);
            Assert.AreEqual(1.37f, r.y, 1e-5f, "grid snap is XZ only; height is untouched");
        }

        [Test]
        public void RayPlaneY_HitsHorizontalPlane()
        {
            // ray from above pointing down crosses y = 0 at (2,0,3)
            var ray = new Ray(new Vector3(2f, 5f, 3f), Vector3.down);
            Assert.IsTrue(MeasureMath.RayPlaneY(ray, 0f, out var hit));
            Assert.AreEqual(2f, hit.x, 1e-4f);
            Assert.AreEqual(0f, hit.y, 1e-4f);
            Assert.AreEqual(3f, hit.z, 1e-4f);
        }

        [Test]
        public void RayPlaneY_ParallelRay_ReturnsFalse()
        {
            var ray = new Ray(new Vector3(0f, 1f, 0f), Vector3.forward); // horizontal, never meets y=0
            Assert.IsFalse(MeasureMath.RayPlaneY(ray, 0f, out _));
        }
    }
}
