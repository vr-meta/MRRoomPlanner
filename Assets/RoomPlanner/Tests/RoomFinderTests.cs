using System.Collections.Generic;
using NUnit.Framework;
using RoomPlanner.Walls;
using UnityEngine;

namespace RoomPlanner.Tests
{
    /// <summary>Rooms from the wall graph (design/24, issue #52): bounded faces of
    /// the planar graph are rooms; the T-heal closes rings broken by mid-span
    /// touches (imported corridor walls).</summary>
    public class RoomFinderTests
    {
        private static WallGraph Grid(params (Vector3 a, Vector3 b)[] walls)
        {
            var g = new WallGraph();
            foreach (var (a, b) in walls)
                g.AddSegment(g.SnapOrCreateNode(a), g.SnapOrCreateNode(b));
            return g;
        }

        private static (Vector3, Vector3) W(float ax, float az, float bx, float bz, float y = 0f) =>
            (new Vector3(ax, y, az), new Vector3(bx, y, bz));

        [Test]
        public void SingleSquare_IsOneRoom_OuterFaceDropped()
        {
            var g = Grid(W(0, 0, 4, 0), W(4, 0, 4, 3), W(4, 3, 0, 3), W(0, 3, 0, 0));
            var rooms = RoomFinder.FindRooms(g);
            Assert.AreEqual(1, rooms.Count, "one bounded face; the outer face is dropped");
            Assert.AreEqual(12f, rooms[0].Area, 1e-3f, "4×3 m");
            Assert.IsTrue(rooms[0].ContainsXZ(new Vector3(2f, 0f, 1.5f)));
            Assert.IsFalse(rooms[0].ContainsXZ(new Vector3(5f, 0f, 1.5f)));
        }

        [Test]
        public void SharedWall_MakesTwoRooms()
        {
            // 8×3 box with a divider at x = 4 (all endpoints are shared nodes)
            var g = Grid(
                W(0, 0, 4, 0), W(4, 0, 8, 0),
                W(8, 0, 8, 3),
                W(8, 3, 4, 3), W(4, 3, 0, 3),
                W(0, 3, 0, 0),
                W(4, 0, 4, 3));
            var rooms = RoomFinder.FindRooms(g);
            Assert.AreEqual(2, rooms.Count, "the divider splits the box into two rooms");
            rooms.Sort((x, y) => x.Area.CompareTo(y.Area));
            Assert.AreEqual(12f, rooms[0].Area, 1e-3f);
            Assert.AreEqual(12f, rooms[1].Area, 1e-3f);
            var left = new Vector3(2f, 0f, 1.5f);
            var right = new Vector3(6f, 0f, 1.5f);
            int leftHits = (rooms[0].ContainsXZ(left) ? 1 : 0) + (rooms[1].ContainsXZ(left) ? 1 : 0);
            int rightHits = (rooms[0].ContainsXZ(right) ? 1 : 0) + (rooms[1].ContainsXZ(right) ? 1 : 0);
            Assert.AreEqual(1, leftHits, "the left point belongs to exactly one room");
            Assert.AreEqual(1, rightHits, "the right point belongs to exactly one room");
            Assert.AreNotEqual(rooms[0].ContainsXZ(left), rooms[0].ContainsXZ(right),
                "the two points are in different rooms");
        }

        [Test]
        public void SuppressedDivider_MergesTheRooms()
        {
            var g = Grid(
                W(0, 0, 4, 0), W(4, 0, 8, 0),
                W(8, 0, 8, 3),
                W(8, 3, 4, 3), W(4, 3, 0, 3),
                W(0, 3, 0, 0),
                W(4, 0, 4, 3));
            foreach (var s in g.Segments)
                if (Mathf.Approximately(s.A.Position.x, 4f) && Mathf.Approximately(s.B.Position.x, 4f))
                    s.Suppressed = true;   // deleted wall
            var rooms = RoomFinder.FindRooms(g);
            Assert.AreEqual(1, rooms.Count, "a deleted divider merges the two rooms");
            Assert.AreEqual(24f, rooms[0].Area, 1e-3f);
        }

        [Test]
        public void DanglingSpur_DoesNotBreakTheRoom()
        {
            var g = Grid(
                W(0, 0, 4, 0), W(4, 0, 4, 3), W(4, 3, 0, 3), W(0, 3, 0, 0),
                W(2, 3, 2, 5));   // spur sticking out of the top wall's midpoint node? no — separate nodes
            // heal first so the spur attaches, as the real pipeline does
            RoomFinder.SplitTJunctions(g);
            var rooms = RoomFinder.FindRooms(g);
            Assert.AreEqual(1, rooms.Count, "a dead-end spur adds no rooms and breaks nothing");
            Assert.AreEqual(12f, rooms[0].Area, 1e-2f);
        }

        [Test]
        public void OpenContour_HasNoRooms()
        {
            var g = Grid(W(0, 0, 4, 0), W(4, 0, 4, 3), W(4, 3, 0, 3));
            Assert.AreEqual(0, RoomFinder.FindRooms(g).Count, "three walls make no room");
        }

        [Test]
        public void Storeys_AreSeparated()
        {
            var g = Grid(
                W(0, 0, 4, 0), W(4, 0, 4, 3), W(4, 3, 0, 3), W(0, 3, 0, 0),
                W(0, 0, 4, 0, 3f), W(4, 0, 4, 3, 3f), W(4, 3, 0, 3, 3f), W(0, 3, 0, 0, 3f));
            var rooms = RoomFinder.FindRooms(g);
            Assert.AreEqual(2, rooms.Count, "one room per storey");
            rooms.Sort((x, y) => x.Level.CompareTo(y.Level));
            Assert.AreEqual(0f, rooms[0].Level, 0.6f);
            Assert.AreEqual(3f, rooms[1].Level, 0.6f);
        }

        [Test]
        public void THeal_SplitsTheLongWall_AndClosesTheRooms()
        {
            // Imported look: an 8 m wall drawn as ONE segment, a divider merely
            // TOUCHING its midpoint (own node at (4,0), not shared) — without the
            // heal the ring never closes and painting the long wall spans two rooms.
            var g = Grid(
                W(0, 0, 8, 0),                  // long south wall — one segment
                W(8, 0, 8, 3),
                W(8, 3, 0, 3),                  // long north wall — one segment
                W(0, 3, 0, 0),
                W(4, 0, 4, 3));                 // divider touching both long walls
            var unhealed = RoomFinder.FindRooms(g);
            Assert.AreEqual(1, unhealed.Count,
                "unhealed: the divider floats — the box reads as ONE room");
            Assert.AreEqual(24f, unhealed[0].Area, 1e-2f);

            int splits = RoomFinder.SplitTJunctions(g);
            Assert.AreEqual(2, splits, "both long walls get split at the divider");
            Assert.AreEqual(7, g.Segments.Count, "5 walls became 7 segments");

            var rooms = RoomFinder.FindRooms(g);
            Assert.AreEqual(2, rooms.Count, "healed graph closes two rooms");

            Assert.AreEqual(0, RoomFinder.SplitTJunctions(g), "the heal is idempotent");
        }

        [Test]
        public void THeal_MovesOpeningsWithTheSplit()
        {
            var g = Grid(W(0, 0, 8, 0), W(4, 0, 4, 3));
            var longWall = g.Segments[0];
            longWall.Openings.Add(new WallOpening
            {
                Id = 1, AlongFraction = 0.75f, Width = 0.9f, Height = 2.1f,   // at x = 6
                Kind = OpeningKind.Door,
            });
            RoomFinder.SplitTJunctions(g);
            // the door at x=6 must live on the 4..8 half at fraction 0.5
            WallSegment half = null;
            foreach (var s in g.Segments)
                if (s.Openings.Count > 0) half = s;
            Assert.IsNotNull(half, "the opening survived the split");
            Assert.AreEqual(4f, Mathf.Min(half.A.Position.x, half.B.Position.x), 1e-3f);
            Assert.AreEqual(0.5f, half.Openings[0].AlongFraction, 1e-3f,
                "world position preserved across the split");
        }

        [Test]
        public void THeal_WeldsAPartitionEndingOnTheWallFace()
        {
            // The real-scene case (headset feedback): the partition's axis STOPS at the
            // long wall's face — 10 cm off a 20 cm wall's centreline. The heal must
            // reach through the wall body, snap the node onto the axis and split.
            var g = new WallGraph();
            var longWall = g.AddSegment(
                g.SnapOrCreateNode(new Vector3(0f, 0f, 0f)),
                g.SnapOrCreateNode(new Vector3(8f, 0f, 0f)));
            longWall.Thickness = 0.2f;
            var faceNode = g.SnapOrCreateNode(new Vector3(4f, 0f, 0.1f));   // on the face
            var partition = g.AddSegment(faceNode, g.SnapOrCreateNode(new Vector3(4f, 0f, 3f)));
            partition.Thickness = 0.1f;

            Assert.AreEqual(1, RoomFinder.SplitTJunctions(g), "one weld through the body");
            Assert.AreEqual(0f, faceNode.Position.z, 1e-3f, "the node snapped onto the centreline");
            Assert.AreEqual(3, g.Segments.Count, "the long wall split in two");
            Assert.AreEqual(3, faceNode.Degree, "two halves + the partition share the junction");
        }

        [Test]
        public void THeal_SnapRescalesTheParitionsOpenings()
        {
            // A door in the partition must keep its WORLD position when the endpoint
            // stretches onto the target's centreline.
            var g = new WallGraph();
            var longWall = g.AddSegment(
                g.SnapOrCreateNode(new Vector3(0f, 0f, 0f)),
                g.SnapOrCreateNode(new Vector3(8f, 0f, 0f)));
            longWall.Thickness = 0.2f;
            var faceNode = g.SnapOrCreateNode(new Vector3(4f, 0f, 0.1f));
            var partition = g.AddSegment(faceNode, g.SnapOrCreateNode(new Vector3(4f, 0f, 3.1f)));
            partition.Openings.Add(new WallOpening
            {
                Id = 1, AlongFraction = 0.5f, Width = 0.9f, Height = 2.1f,   // door at z = 1.6
                Kind = OpeningKind.Door,
            });

            RoomFinder.SplitTJunctions(g);
            float doorZ = Vector3.Lerp(partition.A.Position, partition.B.Position,
                partition.Openings[0].AlongFraction).z;
            Assert.AreEqual(1.6f, doorZ, 1e-3f, "the door kept its world position");
        }

        [Test]
        public void THeal_LeavesParallelDoubleWallsAlone()
        {
            // A double wall 15 cm away must NOT be welded even though it sits inside
            // the neighbour's body reach — parallel is not a T (angle guard).
            var g = new WallGraph();
            var a = g.AddSegment(
                g.SnapOrCreateNode(new Vector3(0f, 0f, 0f)),
                g.SnapOrCreateNode(new Vector3(4f, 0f, 0f)));
            a.Thickness = 0.3f;   // reach 0.17
            var b = g.AddSegment(
                g.SnapOrCreateNode(new Vector3(1f, 0f, 0.15f)),
                g.SnapOrCreateNode(new Vector3(3f, 0f, 0.15f)));
            b.Thickness = 0.3f;

            Assert.AreEqual(0, RoomFinder.SplitTJunctions(g), "parallel walls stay separate");
            Assert.AreEqual(2, g.Segments.Count);
        }

        [Test]
        public void Inset_ShrinksTheRing_BothWindings()
        {
            var ccw = new List<Vector3>
            {
                new(0f, 0f, 0f), new(4f, 0f, 0f), new(4f, 0f, 4f), new(0f, 0f, 4f),
            };
            var cw = new List<Vector3> { ccw[3], ccw[2], ccw[1], ccw[0] };

            foreach (var ring in new[] { ccw, cw })
            {
                var inset = RoomFinder.Inset(ring, 0.02f);
                Assert.AreEqual(4, inset.Count);
                foreach (var p in inset)
                {
                    Assert.Greater(p.x, -1e-4f); Assert.Less(p.x, 4.0001f);
                    Assert.Greater(p.z, -1e-4f); Assert.Less(p.z, 4.0001f);
                }
                // corners moved inward by ~2 cm on both axes
                float minX = float.MaxValue;
                foreach (var p in inset) minX = Mathf.Min(minX, p.x);
                Assert.AreEqual(0.02f, minX, 1e-3f, "ring pulled inward");
            }
        }
    }
}
