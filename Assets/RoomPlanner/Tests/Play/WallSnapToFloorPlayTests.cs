using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using RoomPlanner.Core;
using RoomPlanner.Editing;
using RoomPlanner.Floors;
using RoomPlanner.Walls;

namespace RoomPlanner.Tests.Play
{
    /// <summary>
    /// Phase C / step C5 — walls snap to the floor outline
    /// (docs/design/17-floor-outline.md). This is why the phase was pulled forward: you lay a
    /// slab, then run walls along its edge, and the wall lands exactly on the boundary.
    ///
    /// Floors are discovered through SceneModel, so the wall tool needs no wiring to the floor
    /// tool — these tests cover that discovery as well as the snapping itself.
    /// </summary>
    public class WallSnapToFloorPlayTests
    {
        private readonly List<GameObject> _spawned = new();

        private static Vector3 P(float x, float z) => new Vector3(x, 0f, z);

        private WallController MakeWallTool()
        {
            var rigGo = new GameObject("Rig");
            _spawned.Add(rigGo);
            rigGo.AddComponent<SceneModel>();
            return rigGo.AddComponent<WallController>();
        }

        private Floor MakeSlab(List<Vector3> outline)
        {
            var go = new GameObject("Floor");
            _spawned.Add(go);
            go.AddComponent<MeshFilter>();
            go.AddComponent<MeshRenderer>();
            var slab = go.AddComponent<Floor>();
            var sel = go.AddComponent<Selectable>();
            slab.BuildOutline(outline, 0f, 0.2f, 5f, 0f, 0f, 0f);

            var model = SceneModel.Instance;
            if (model != null) model.Register(sel);
            return slab;
        }

        private static List<Vector3> Rect() => new()
        {
            P(0, 0), P(4, 0), P(4, 3), P(0, 3)
        };

        [TearDown]
        public void Cleanup()
        {
            foreach (var go in _spawned) if (go != null) Object.DestroyImmediate(go);
            _spawned.Clear();
        }

        [UnityTest]
        public IEnumerator SnapsToAFloorCorner()
        {
            var tool = MakeWallTool();
            MakeSlab(Rect());
            yield return null;

            var finder = SnapFinder.WithRadius(0.2f);
            tool.AddFloorSnapCandidates(new Vector3(4.03f, 0f, 0.02f), ref finder, true, true);

            Assert.IsTrue(finder.Found, "a wall point near the slab corner must magnetise");
            Assert.AreEqual(SnapKind.Corner, finder.Kind);
            Assert.AreEqual(P(4, 0), finder.Point);
        }

        [UnityTest]
        public IEnumerator SnapsOntoAFloorEdge()
        {
            var tool = MakeWallTool();
            MakeSlab(Rect());
            yield return null;

            var finder = SnapFinder.WithRadius(0.2f);
            tool.AddFloorSnapCandidates(new Vector3(2f, 0f, 0.05f), ref finder, true, true);

            Assert.IsTrue(finder.Found);
            Assert.AreEqual(SnapKind.Edge, finder.Kind, "mid-edge is an edge snap, not a corner");
            Assert.AreEqual(2f, finder.Point.x, 1e-3f);
            Assert.AreEqual(0f, finder.Point.z, 1e-3f, "the wall lands ON the slab boundary");
        }

        [UnityTest]
        public IEnumerator RespectsTheSnapToggles()
        {
            var tool = MakeWallTool();
            MakeSlab(Rect());
            yield return null;

            var noEdges = SnapFinder.WithRadius(0.2f);
            tool.AddFloorSnapCandidates(new Vector3(2f, 0f, 0.05f), ref noEdges, true, false);
            Assert.IsFalse(noEdges.Found, "Edge toggle off — a mid-edge point must not magnetise");

            var noneAtAll = SnapFinder.WithRadius(0.2f);
            tool.AddFloorSnapCandidates(new Vector3(4.02f, 0f, 0f), ref noneAtAll, false, false);
            Assert.IsFalse(noneAtAll.Found);
        }

        [UnityTest]
        public IEnumerator IgnoresPointsOutOfRange()
        {
            var tool = MakeWallTool();
            MakeSlab(Rect());
            yield return null;

            var finder = SnapFinder.WithRadius(0.1f);
            tool.AddFloorSnapCandidates(new Vector3(2f, 0f, 1.5f), ref finder, true, true);

            Assert.IsFalse(finder.Found, "the middle of the room is not near any boundary");
        }

        [UnityTest]
        public IEnumerator HiddenSlabDoesNotMagnetise()
        {
            // a deleted slab is hidden, not destroyed — it must stop attracting the cursor
            // (coding rule 2.4)
            var tool = MakeWallTool();
            var slab = MakeSlab(Rect());
            yield return null;

            slab.GetComponent<Selectable>().SetHidden(true);

            var finder = SnapFinder.WithRadius(0.2f);
            tool.AddFloorSnapCandidates(new Vector3(4.02f, 0f, 0f), ref finder, true, true);

            Assert.IsFalse(finder.Found, "invisible geometry must not magnetise");
        }

        [UnityTest]
        public IEnumerator FollowsANonRectangularOutline()
        {
            var tool = MakeWallTool();
            MakeSlab(new List<Vector3> { P(0, 0), P(4, 0), P(4, 2), P(2, 2), P(2, 5), P(0, 5) });
            yield return null;

            // the inner corner of the L — the point a rectangle could never offer
            var finder = SnapFinder.WithRadius(0.2f);
            tool.AddFloorSnapCandidates(new Vector3(2.03f, 0f, 2.03f), ref finder, true, true);

            Assert.IsTrue(finder.Found);
            Assert.AreEqual(P(2, 2), finder.Point);
        }
    }
}
