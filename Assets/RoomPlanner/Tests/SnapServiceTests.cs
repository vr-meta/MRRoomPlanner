using NUnit.Framework;
using UnityEngine;
using RoomPlanner.Core;

namespace RoomPlanner.Tests
{
    /// <summary>
    /// Phase B / step B6 — the shared snapping policy (docs/design/13-phase-b-wallgraph.md).
    /// Each tool used to carry its own copy of this and they drifted; these tests pin the
    /// behaviour every tool now gets.
    /// </summary>
    public class SnapServiceTests
    {
        private static Vector3 P(float x, float z) => new Vector3(x, 0f, z);

        [Test]
        public void NothingWithinRadius_IsNotFound()
        {
            var f = SnapFinder.WithRadius(0.1f);
            f.TryCorner(P(0, 0), P(1, 0));
            Assert.IsFalse(f.Found);
            Assert.AreEqual(SnapKind.None, f.Kind);
        }

        [Test]
        public void NearestCornerWins()
        {
            var f = SnapFinder.WithRadius(0.5f);
            f.TryCorner(P(0, 0), P(0.3f, 0), 1);
            f.TryCorner(P(0, 0), P(0.1f, 0), 2);

            Assert.IsTrue(f.Found);
            Assert.AreEqual(P(0.1f, 0), f.Point);
            Assert.AreEqual(2, f.Index, "the index of the winning candidate comes back");
        }

        [Test]
        public void EdgeSnapsToTheClosestPointOnIt()
        {
            var f = SnapFinder.WithRadius(0.5f);
            f.TryEdge(new Vector3(2f, 0f, 0.3f), P(0, 0), P(4, 0));

            Assert.IsTrue(f.Found);
            Assert.AreEqual(SnapKind.Edge, f.Kind);
            Assert.AreEqual(2f, f.Point.x, 1e-4f);
            Assert.AreEqual(0f, f.Point.z, 1e-4f);
        }

        [Test]
        public void CornerBeatsEdge_WhenBothAreCloseByTheCorner()
        {
            // aiming just past a wall's end: the edge is marginally nearer, but the endpoint
            // is what the user means
            var cursor = new Vector3(0.01f, 0f, 0.012f);
            var f = SnapFinder.WithRadius(0.2f);
            f.TryEdge(cursor, P(0, 0), P(4, 0));     // ~1.2 cm away
            f.TryCorner(cursor, P(0, 0));            // ~1.6 cm away

            Assert.AreEqual(SnapKind.Corner, f.Kind, "endpoints win near a corner");
            Assert.AreEqual(P(0, 0), f.Point);
        }

        [Test]
        public void ClearlyNearerEdge_StillWins()
        {
            // ...but the bias must not let a distant corner hijack a wall face
            var cursor = new Vector3(2f, 0f, 0.01f);
            var f = SnapFinder.WithRadius(0.5f);
            f.TryEdge(cursor, P(0, 0), P(4, 0));     // 1 cm away
            f.TryCorner(cursor, P(0, 0));            // 2 m away

            Assert.AreEqual(SnapKind.Edge, f.Kind);
        }

        [Test]
        public void Fallbacks_AngleThenGrid()
        {
            // no magnet hit: angle snap needs the previous point, grid rounds afterwards
            var r = SnapFinder.ApplyFallbacks(new Vector3(1f, 0f, 0.08f),
                hasPrevious: true, previous: Vector3.zero,
                angleOn: true, angleStepDeg: 15f, gridOn: true, gridSize: 0.05f);

            Assert.AreEqual(0f, r.z, 1e-4f, "snapped onto the axis, then onto the grid");
            Assert.AreEqual(Mathf.Round(r.x / 0.05f) * 0.05f, r.x, 1e-4f);
        }

        [Test]
        public void Fallbacks_WithoutPrevious_SkipAngle()
        {
            var cursor = new Vector3(1.11f, 0f, 2.22f);
            var r = SnapFinder.ApplyFallbacks(cursor,
                hasPrevious: false, previous: Vector3.zero,
                angleOn: true, angleStepDeg: 15f, gridOn: false, gridSize: 0.05f);

            Assert.AreEqual(cursor, r, "the very first point has no direction to snap");
        }

        [Test]
        public void Fallbacks_AllOff_LeaveTheCursorAlone()
        {
            var cursor = new Vector3(1.11f, 0.5f, 2.22f);
            var r = SnapFinder.ApplyFallbacks(cursor, true, Vector3.zero, false, 15f, false, 0.05f);
            Assert.AreEqual(cursor, r);
        }
    }
}
