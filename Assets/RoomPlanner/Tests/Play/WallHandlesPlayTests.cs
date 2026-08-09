using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using RoomPlanner.Editing;
using RoomPlanner.Walls;

namespace RoomPlanner.Tests.Play
{
    /// <summary>
    /// Phase B / step B5 — dragging a wall vertex (docs/design/13-phase-b-wallgraph.md).
    ///
    /// This is the payoff of the phase: the handle IS the shared graph node, so pulling a
    /// corner moves both walls and pulling a T moves all three — and the whole gesture is one
    /// undo entry, not one per frame.
    /// </summary>
    public class WallHandlesPlayTests
    {
        private readonly List<GameObject> _spawned = new();

        private static Vector3 P(float x, float z) => new Vector3(x, 0f, z);

        private (WallGraphRenderer r, SceneModel model) MakeRig()
        {
            var prefabGo = new GameObject("WallPrefab");
            prefabGo.AddComponent<MeshFilter>();
            prefabGo.AddComponent<MeshRenderer>();
            prefabGo.AddComponent<Wall>();
            prefabGo.AddComponent<WallHandles>();
            prefabGo.AddComponent<Selectable>();
            prefabGo.SetActive(false);
            _spawned.Add(prefabGo);

            var rigGo = new GameObject("Rig");
            _spawned.Add(rigGo);
            var model = rigGo.AddComponent<SceneModel>();
            var r = rigGo.AddComponent<WallGraphRenderer>();
            r.Configure(prefabGo.GetComponent<Wall>(), model);
            return (r, model);
        }

        private static WallSegment Draw(WallGraphRenderer r, Vector3 a, Vector3 b)
        {
            var g = r.Graph;
            var s = g.AddSegment(g.SnapOrCreateNode(a), g.SnapOrCreateNode(b));
            if (s != null) { s.Thickness = 0.2f; s.Height = 2.7f; s.SideSign = 1f; }
            r.Sync();
            return s;
        }

        private static WallHandles HandlesOf(WallGraphRenderer r, WallSegment s) =>
            r.ViewOf(s).GetComponent<WallHandles>();

        [TearDown]
        public void Cleanup()
        {
            foreach (var go in _spawned) if (go != null) Object.DestroyImmediate(go);
            _spawned.Clear();
        }

        [UnityTest]
        public IEnumerator AWallExposesItsTwoEnds()
        {
            var (r, _) = MakeRig();
            var s = Draw(r, P(0, 0), P(3, 0));
            yield return null;

            var h = HandlesOf(r, s);
            Assert.AreEqual(2, h.HandleCount);
            Assert.AreEqual(P(0, 0), h.GetHandlePosition(0));
            Assert.AreEqual(P(3, 0), h.GetHandlePosition(1));
        }

        [UnityTest]
        public IEnumerator DraggingACorner_MovesBothWalls()
        {
            var (r, _) = MakeRig();
            var ab = Draw(r, P(0, 0), P(2, 0));
            var bc = Draw(r, P(2, 0), P(2, 2));
            var corner = ab.B;
            Assert.AreSame(corner, bc.A, "precondition: shared corner");
            yield return null;

            var h = HandlesOf(r, ab);
            h.PreviewHandle(1, P(3, 1));          // index 1 = node B

            Assert.AreEqual(P(3, 1), corner.Position, "the shared node followed the handle");
            Assert.AreEqual(P(3, 1), bc.A.Position, "so did the neighbour's end — it IS the same node");
        }

        [UnityTest]
        public IEnumerator DraggingATJunction_MovesAllThreeWalls()
        {
            var (r, _) = MakeRig();
            var through = Draw(r, P(0, 0), P(4, 0));
            var mid = r.Graph.SplitSegmentAt(through, P(2, 0));
            var stem = r.Graph.AddSegment(mid, r.Graph.SnapOrCreateNode(P(2, 3)));
            stem.Thickness = 0.2f; stem.Height = 2.7f; stem.SideSign = 1f;
            r.Sync();
            yield return null;

            Assert.AreEqual(3, mid.Degree);
            var bounds = new Dictionary<int, Vector3>();
            foreach (var s in r.Graph.Segments)
                bounds[s.Id] = r.ViewOf(s).GetComponent<MeshFilter>().sharedMesh.bounds.center;

            HandlesOf(r, stem).PreviewHandle(0, P(2.5f, 0.5f));   // stem's A is the junction node

            foreach (var s in r.Graph.Segments)
            {
                var now = r.ViewOf(s).GetComponent<MeshFilter>().sharedMesh.bounds.center;
                Assert.AreNotEqual(bounds[s.Id], now, $"segment {s.Id} was not rebuilt");
            }
        }

        [UnityTest]
        public IEnumerator DragDefersThePhysicsMesh_AndSettlesOnCommit()
        {
            // rule 4.2: no MeshCollider re-cook every frame; one at the end
            var (r, _) = MakeRig();
            var s = Draw(r, P(0, 0), P(4, 0));
            var view = r.ViewOf(s);
            yield return new WaitForFixedUpdate();

            var h = HandlesOf(r, s);
            h.PreviewHandle(1, P(4, 2));
            Assert.IsTrue(view.DeferCollider, "physics is left alone mid-drag");

            h.CommitHandle(1, P(4, 0), P(4, 2));
            Assert.IsFalse(view.DeferCollider, "and settled when the drag ends");
            Assert.IsNotNull(view.GetComponent<MeshCollider>().sharedMesh);
        }

        [UnityTest]
        public IEnumerator WholeDragIsOneUndoEntry()
        {
            var (r, model) = MakeRig();
            var s = Draw(r, P(0, 0), P(4, 0));
            yield return null;

            var h = HandlesOf(r, s);
            Vector3 from = h.GetHandlePosition(1);

            // many frames of dragging...
            h.PreviewHandle(1, P(4, 1));
            h.PreviewHandle(1, P(4, 2));
            h.PreviewHandle(1, P(4, 3));
            var cmd = h.CommitHandle(1, from, P(4, 3));
            model.History.Record(cmd);

            Assert.AreEqual(1, model.History.UndoCount, "one entry for the gesture, not one per frame");
            Assert.AreEqual(P(4, 3), s.B.Position);

            model.History.Undo();
            Assert.AreEqual(from, s.B.Position, "undo returns the vertex where it started");

            model.History.Redo();
            Assert.AreEqual(P(4, 3), s.B.Position);
        }

        [UnityTest]
        public IEnumerator CommitWithoutMovement_RecordsNothing()
        {
            var (r, _) = MakeRig();
            var s = Draw(r, P(0, 0), P(4, 0));
            yield return null;

            var h = HandlesOf(r, s);
            Vector3 from = h.GetHandlePosition(1);
            Assert.IsNull(h.CommitHandle(1, from, from), "a click without a drag is not an edit");
        }

        [UnityTest]
        public IEnumerator DraggingKeepsTheNodeOnItsLevel()
        {
            // dragging is a plan-view operation; a stray Y must not lift the wall off the floor
            var (r, _) = MakeRig();
            var s = Draw(r, new Vector3(0, 1.5f, 0), new Vector3(4, 1.5f, 0));
            yield return null;

            HandlesOf(r, s).PreviewHandle(1, new Vector3(4f, 9f, 2f));

            Assert.AreEqual(1.5f, s.B.Position.y, 1e-4f, "the storey level is preserved");
            Assert.AreEqual(2f, s.B.Position.z, 1e-4f);
        }
    }
}
