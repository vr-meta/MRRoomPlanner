using NUnit.Framework;
using UnityEngine;
using RoomPlanner.Walls;

namespace RoomPlanner.Tests
{
    /// <summary>
    /// Phase B / step B1 — the wall graph itself (docs/design/13-phase-b-wallgraph.md).
    /// Pure data, so everything here is EditMode. What matters is that walls which meet
    /// really SHARE a node: that is what makes corners, T-junctions and "drag the corner and
    /// both walls follow" work without any CSG.
    /// </summary>
    public class WallGraphTests
    {
        private static Vector3 P(float x, float z) => new Vector3(x, 0f, z);

        // ---- nodes ----

        [Test]
        public void SnapOrCreateNode_ReusesNodeWithinTolerance()
        {
            var g = new WallGraph();
            var a = g.SnapOrCreateNode(P(0, 0));
            var b = g.SnapOrCreateNode(P(0.02f, 0f));   // 2 cm away, tolerance 5 cm

            Assert.AreSame(a, b, "a point inside the tolerance must reuse the node, not add one");
            Assert.AreEqual(1, g.Nodes.Count);
        }

        [Test]
        public void SnapOrCreateNode_CreatesNodeOutsideTolerance()
        {
            var g = new WallGraph();
            g.SnapOrCreateNode(P(0, 0));
            g.SnapOrCreateNode(P(1, 0));

            Assert.AreEqual(2, g.Nodes.Count);
        }

        [Test]
        public void SnapOrCreateNode_DoesNotMergeAcrossLevels()
        {
            // same XZ, different storey height — these must stay separate nodes
            var g = new WallGraph();
            g.SnapOrCreateNode(new Vector3(0, 0f, 0));
            g.SnapOrCreateNode(new Vector3(0, 2.8f, 0));

            Assert.AreEqual(2, g.Nodes.Count);
        }

        [Test]
        public void NodeIds_AreUniqueAndStable()
        {
            var g = new WallGraph();
            var a = g.SnapOrCreateNode(P(0, 0));
            var b = g.SnapOrCreateNode(P(2, 0));
            int idA = a.Id;

            g.AddSegment(a, b);
            Assert.AreNotEqual(a.Id, b.Id);
            Assert.AreEqual(idA, a.Id, "ids must not shift as the graph grows (save/load, openings)");
        }

        // ---- segments ----

        [Test]
        public void AddSegment_LinksBothNodes()
        {
            var g = new WallGraph();
            var a = g.SnapOrCreateNode(P(0, 0));
            var b = g.SnapOrCreateNode(P(3, 0));
            var s = g.AddSegment(a, b);

            Assert.IsNotNull(s);
            Assert.AreEqual(1, g.Segments.Count);
            Assert.AreEqual(1, a.Degree);
            Assert.AreEqual(1, b.Degree);
            Assert.AreSame(b, s.Other(a));
            Assert.AreSame(a, s.Other(b));
            Assert.AreEqual(3f, s.Length, 1e-4f);
        }

        [Test]
        public void AddSegment_SameNodeTwice_IsRejected()
        {
            var g = new WallGraph();
            var a = g.SnapOrCreateNode(P(0, 0));

            Assert.IsNull(g.AddSegment(a, a));
            Assert.AreEqual(0, g.Segments.Count);
        }

        [Test]
        public void AddSegment_ZeroLength_IsRejected()
        {
            // двойной клик триггера: two distinct nodes on top of each other
            var g = new WallGraph();
            var a = g.SnapOrCreateNode(P(0, 0));
            var b = g.SnapOrCreateNode(P(1, 0));
            g.MoveNode(b, a.Position);                  // collapsed onto a

            Assert.IsNull(g.AddSegment(a, b), "a zero-length wall is controller noise, not intent");
            Assert.AreEqual(0, g.Segments.Count);
        }

        [Test]
        public void AddSegment_Duplicate_ReturnsExisting_NoDoubleWall()
        {
            var g = new WallGraph();
            var a = g.SnapOrCreateNode(P(0, 0));
            var b = g.SnapOrCreateNode(P(3, 0));
            var first = g.AddSegment(a, b);
            var second = g.AddSegment(b, a);            // drawn again, opposite direction

            Assert.AreSame(first, second);
            Assert.AreEqual(1, g.Segments.Count);
            Assert.AreEqual(1, a.Degree, "the node must not accumulate duplicate references");
        }

        [Test]
        public void SharedNode_CornerOfTwoWalls()
        {
            var g = new WallGraph();
            var corner = g.SnapOrCreateNode(P(0, 0));
            var west = g.SnapOrCreateNode(P(-3, 0));
            var north = g.SnapOrCreateNode(P(0, 3));
            g.AddSegment(west, corner);
            g.AddSegment(corner, north);

            Assert.AreEqual(2, corner.Degree, "a corner is one node with two walls on it");

            // moving the shared node implicitly moves the end of BOTH walls
            g.MoveNode(corner, P(1, 1));
            foreach (var s in corner.Segments)
                Assert.AreEqual(P(1, 1), s.Has(corner) ? corner.Position : Vector3.zero);
        }

        // ---- split / T-junction ----

        [Test]
        public void SplitSegmentAt_MakesTJunction()
        {
            var g = new WallGraph();
            var a = g.SnapOrCreateNode(P(0, 0));
            var b = g.SnapOrCreateNode(P(4, 0));
            var wall = g.AddSegment(a, b);

            var mid = g.SplitSegmentAt(wall, P(2, 0));   // snapped onto the wall's side

            Assert.AreEqual(2, g.Segments.Count, "the split wall becomes two");
            Assert.AreEqual(3, g.Nodes.Count);
            Assert.AreEqual(2, mid.Degree, "still just a straight run until a third wall arrives");

            var stem = g.SnapOrCreateNode(P(2, 3));
            g.AddSegment(mid, stem);
            Assert.AreEqual(3, mid.Degree, "three walls meeting = T-junction");
            Assert.AreEqual(3, g.Segments.Count);
        }

        [Test]
        public void SplitSegmentAt_ProjectsPointOntoTheWall()
        {
            var g = new WallGraph();
            var a = g.SnapOrCreateNode(P(0, 0));
            var b = g.SnapOrCreateNode(P(4, 0));
            var wall = g.AddSegment(a, b);

            var mid = g.SplitSegmentAt(wall, new Vector3(2f, 0f, 0.4f));  // aimed slightly off the wall

            Assert.AreEqual(2f, mid.Position.x, 1e-4f);
            Assert.AreEqual(0f, mid.Position.z, 1e-4f, "the node lands ON the wall, not where the ray was");
        }

        [Test]
        public void SplitSegmentAt_NearEndpoint_ReusesEndpoint()
        {
            var g = new WallGraph();
            var a = g.SnapOrCreateNode(P(0, 0));
            var b = g.SnapOrCreateNode(P(4, 0));
            var wall = g.AddSegment(a, b);

            var n = g.SplitSegmentAt(wall, P(0.01f, 0));   // 1 cm from the end, inside tolerance

            Assert.AreSame(a, n, "snapping near a corner must attach to it, not slice a 1 cm stub");
            Assert.AreEqual(1, g.Segments.Count);
            Assert.AreEqual(2, g.Nodes.Count);
        }

        [Test]
        public void SplitSegmentAt_KeepsSegmentParameters()
        {
            var g = new WallGraph();
            var a = g.SnapOrCreateNode(P(0, 0));
            var b = g.SnapOrCreateNode(P(4, 0));
            var wall = g.AddSegment(a, b);
            wall.Thickness = 0.35f;
            wall.Height = 3.1f;
            wall.Offset = WallOffsetMode.Center;
            wall.Join = WallJoin.Bevel;

            var mid = g.SplitSegmentAt(wall, P(2, 0));
            var tail = mid.Segments[1] == wall ? mid.Segments[0] : mid.Segments[1];

            Assert.AreEqual(0.35f, tail.Thickness, 1e-5f, "both halves keep the wall's parameters");
            Assert.AreEqual(3.1f, tail.Height, 1e-5f);
            Assert.AreEqual(WallOffsetMode.Center, tail.Offset);
            Assert.AreEqual(WallJoin.Bevel, tail.Join);
        }

        [Test]
        public void SplitSegmentAt_PreservesTotalLength()
        {
            var g = new WallGraph();
            var a = g.SnapOrCreateNode(P(0, 0));
            var b = g.SnapOrCreateNode(P(4, 0));
            var wall = g.AddSegment(a, b);

            var mid = g.SplitSegmentAt(wall, P(1.5f, 0));
            float total = 0f;
            foreach (var s in g.Segments) total += s.Length;

            Assert.AreEqual(4f, total, 1e-4f);
            Assert.AreEqual(1.5f, wall.Length, 1e-4f, "the original segment keeps its identity as the A-side half");
        }

        // ---- removal ----

        [Test]
        public void RemoveSegment_DropsOrphanNodes()
        {
            var g = new WallGraph();
            var a = g.SnapOrCreateNode(P(0, 0));
            var b = g.SnapOrCreateNode(P(3, 0));
            var s = g.AddSegment(a, b);

            Assert.IsTrue(g.RemoveSegment(s));
            Assert.AreEqual(0, g.Segments.Count);
            Assert.AreEqual(0, g.Nodes.Count, "nodes with nothing attached must not linger");
        }

        [Test]
        public void RemoveSegment_KeepsNodeStillInUse()
        {
            var g = new WallGraph();
            var corner = g.SnapOrCreateNode(P(0, 0));
            var west = g.SnapOrCreateNode(P(-3, 0));
            var north = g.SnapOrCreateNode(P(0, 3));
            var w1 = g.AddSegment(west, corner);
            g.AddSegment(corner, north);

            g.RemoveSegment(w1);

            Assert.AreEqual(1, g.Segments.Count);
            Assert.AreEqual(2, g.Nodes.Count, "the corner survives — the other wall still uses it");
            Assert.AreEqual(1, corner.Degree);
        }

        [Test]
        public void RemoveSegment_Twice_IsSafe()
        {
            var g = new WallGraph();
            var a = g.SnapOrCreateNode(P(0, 0));
            var b = g.SnapOrCreateNode(P(3, 0));
            var s = g.AddSegment(a, b);

            Assert.IsTrue(g.RemoveSegment(s));
            Assert.IsFalse(g.RemoveSegment(s), "removing an already-removed segment is a no-op, not a crash");
            Assert.IsFalse(g.RemoveSegment(null));
        }

        // ---- queries used by the mesh builder ----

        [Test]
        public void NeighborsOf_ReturnsOtherWallsAtTheNode()
        {
            var g = new WallGraph();
            var hub = g.SnapOrCreateNode(P(0, 0));
            var s1 = g.AddSegment(g.SnapOrCreateNode(P(-2, 0)), hub);
            var s2 = g.AddSegment(hub, g.SnapOrCreateNode(P(2, 0)));
            var s3 = g.AddSegment(hub, g.SnapOrCreateNode(P(0, 2)));

            var n = g.NeighborsOf(s1, hub);
            CollectionAssert.AreEquivalent(new[] { s2, s3 }, n);
            Assert.AreEqual(0, g.NeighborsOf(s1, s1.A).Count, "dead end has no neighbours");
        }

        [Test]
        public void NeighborsOf_NonAllocOverload_ReusesBuffer()
        {
            var g = new WallGraph();
            var hub = g.SnapOrCreateNode(P(0, 0));
            var s1 = g.AddSegment(g.SnapOrCreateNode(P(-2, 0)), hub);
            g.AddSegment(hub, g.SnapOrCreateNode(P(2, 0)));

            var buf = new System.Collections.Generic.List<WallSegment> { null, null };  // dirty buffer
            g.NeighborsOf(s1, hub, buf);

            Assert.AreEqual(1, buf.Count, "the buffer is cleared before filling");
        }

        // ---- B7 groundwork: openings hook and room loops ----

        [Test]
        public void Segment_CarriesAnOpeningsList_PreservedAcrossSplit()
        {
            var g = new WallGraph();
            var wall = g.AddSegment(g.SnapOrCreateNode(P(0, 0)), g.SnapOrCreateNode(P(4, 0)));
            Assert.IsNotNull(wall.Openings, "segments must be able to host doors/windows (Phase D)");
            Assert.AreEqual(0, wall.Openings.Count);

            var mid = g.SplitSegmentAt(wall, P(2, 0));
            var tail = mid.Segments[0] == wall ? mid.Segments[1] : mid.Segments[0];
            Assert.IsNotNull(tail.Openings, "both halves of a split wall can host openings");
        }

        [Test]
        public void SplitSegmentAt_RedistributesOpenings_KeepingWorldPositions()
        {
            // Audit 02 §Б1: a T-junction through a walled window used to leave every
            // opening on the A half at a stale fraction — the window jumped.
            var g = new WallGraph();
            var wall = g.AddSegment(g.SnapOrCreateNode(P(0, 0)), g.SnapOrCreateNode(P(4, 0)));
            var door = new WallOpening { AlongFraction = 0.25f, Width = 0.9f };   // world x = 1.0
            var window = new WallOpening { AlongFraction = 0.7f, Width = 1.2f };  // world x = 2.8
            wall.Openings.Add(door);
            wall.Openings.Add(window);

            var mid = g.SplitSegmentAt(wall, P(2, 0));                            // t = 0.5
            var tail = mid.Segments[0] == wall ? mid.Segments[1] : mid.Segments[0];

            Assert.AreEqual(1, wall.Openings.Count, "the door stays on the A half");
            Assert.AreSame(door, wall.Openings[0]);
            Assert.AreEqual(0.5f, door.AlongFraction, 1e-4f, "0.25 of 4 m = 0.5 of 2 m — still x = 1.0");

            Assert.AreEqual(1, tail.Openings.Count, "the window moves to the far half");
            Assert.AreSame(window, tail.Openings[0]);
            Assert.AreEqual(0.4f, window.AlongFraction, 1e-4f, "0.7 of 4 m = 0.4 of the 2..4 m half — still x = 2.8");
        }

        [Test]
        public void FindClosedLoops_FindsARectangularRoom()
        {
            var g = new WallGraph();
            var a = g.SnapOrCreateNode(P(0, 0));
            var b = g.SnapOrCreateNode(P(4, 0));
            var c = g.SnapOrCreateNode(P(4, 3));
            var d = g.SnapOrCreateNode(P(0, 3));
            g.AddSegment(a, b); g.AddSegment(b, c); g.AddSegment(c, d); g.AddSegment(d, a);

            var loops = g.FindClosedLoops();

            Assert.AreEqual(1, loops.Count, "one room, reported once — not once per corner");
            Assert.AreEqual(4, loops[0].Count);
        }

        [Test]
        public void FindClosedLoops_IgnoresAnOpenRun()
        {
            var g = new WallGraph();
            var a = g.SnapOrCreateNode(P(0, 0));
            var b = g.SnapOrCreateNode(P(4, 0));
            var c = g.SnapOrCreateNode(P(4, 3));
            g.AddSegment(a, b); g.AddSegment(b, c);   // an L, not a room

            Assert.AreEqual(0, g.FindClosedLoops().Count);
        }

        [Test]
        public void FindClosedLoops_SkipsRingsThroughAJunction()
        {
            // a room with a wall tee-ing off it: the ring itself is no longer plain degree-2,
            // and deciding how a room continues through a T belongs to the room detector
            var g = new WallGraph();
            var a = g.SnapOrCreateNode(P(0, 0));
            var b = g.SnapOrCreateNode(P(4, 0));
            var c = g.SnapOrCreateNode(P(4, 3));
            var d = g.SnapOrCreateNode(P(0, 3));
            g.AddSegment(a, b); g.AddSegment(b, c); g.AddSegment(c, d); g.AddSegment(d, a);
            g.AddSegment(b, g.SnapOrCreateNode(P(7, 0)));   // stub off the corner

            var loops = g.FindClosedLoops();
            Assert.AreEqual(0, loops.Count, "not a plain ring any more");
        }

        [Test]
        public void DirectionFrom_PointsAwayFromTheNode()
        {
            var g = new WallGraph();
            var a = g.SnapOrCreateNode(P(0, 0));
            var b = g.SnapOrCreateNode(P(0, 5));
            var s = g.AddSegment(a, b);

            Assert.AreEqual(Vector3.forward, s.DirectionFrom(a));
            Assert.AreEqual(Vector3.back, s.DirectionFrom(b));
        }
    }
}
