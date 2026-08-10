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
    /// Phase B / step B3b — the renderer that owns the graph and one Wall view per segment
    /// (docs/design/13-phase-b-wallgraph.md).
    ///
    /// PlayMode because this is where lifetime, SceneModel registration and real MeshColliders
    /// meet: a view has to appear for every segment, disappear with it, and stay pickable.
    /// </summary>
    public class WallGraphRendererPlayTests
    {
        private readonly List<GameObject> _spawned = new();

        private static Vector3 P(float x, float z) => new Vector3(x, 0f, z);

        private (WallGraphRenderer renderer, SceneModel model) MakeRig()
        {
            var prefabGo = new GameObject("WallPrefab");
            prefabGo.AddComponent<MeshFilter>();
            prefabGo.AddComponent<MeshRenderer>();
            prefabGo.AddComponent<Wall>();
            prefabGo.AddComponent<Selectable>();
            prefabGo.SetActive(false);              // template, not a live wall
            _spawned.Add(prefabGo);

            var rigGo = new GameObject("Rig");
            _spawned.Add(rigGo);
            var model = rigGo.AddComponent<SceneModel>();
            var renderer = rigGo.AddComponent<WallGraphRenderer>();

            renderer.Configure(prefabGo.GetComponent<Wall>(), model);

            return (renderer, model);
        }

        private static WallSegment Draw(WallGraphRenderer r, Vector3 a, Vector3 b)
        {
            var g = r.Graph;
            var s = g.AddSegment(g.SnapOrCreateNode(a), g.SnapOrCreateNode(b));
            if (s != null) { s.Thickness = 0.2f; s.Height = 2.7f; s.SideSign = 1f; }
            r.Sync();
            return s;
        }

        [TearDown]
        public void Cleanup()
        {
            foreach (var go in _spawned) if (go != null) Object.DestroyImmediate(go);
            _spawned.Clear();
        }

        [UnityTest]
        public IEnumerator HiddenWall_LeavesItsNeighboursJoint_AndReturnsOnUndo()
        {
            // Audit 02 §Б3: a deleted (hidden) wall stayed in the graph and kept mitring
            // its neighbour's corner — the joint outlived the wall.
            var (r, model) = MakeRig();
            var s1 = Draw(r, P(0, 0), P(4, 0));
            var s2 = Draw(r, P(4, 0), P(4, 3));    // L-corner at (4,0)
            r.RebuildNeighbourhood(s2);            // the controller does this after each commit
            yield return null;

            var v1 = r.ViewOf(s1);
            var mitred = new List<Vector3>(v1.GetComponent<MeshFilter>().sharedMesh.vertices);

            var sel2 = r.ViewOf(s2).GetComponent<Selectable>();
            model.History.Execute(new DeleteCommand(sel2));
            yield return null;

            Assert.IsTrue(s2.Suppressed, "hide marks the segment suppressed in the graph");
            var capped = new List<Vector3>(v1.GetComponent<MeshFilter>().sharedMesh.vertices);
            CollectionAssert.AreNotEqual(mitred, capped,
                "the survivor's end must become a flat cap, not keep the ghost miter");

            model.History.Undo();
            yield return null;

            Assert.IsFalse(s2.Suppressed, "undo un-suppresses");
            CollectionAssert.AreEqual(mitred,
                new List<Vector3>(v1.GetComponent<MeshFilter>().sharedMesh.vertices),
                "the miter is back exactly as it was");
        }

        [UnityTest]
        public IEnumerator Sync_CreatesOneViewPerSegment_AndRegistersIt()
        {
            var (r, model) = MakeRig();
            var s = Draw(r, P(0, 0), P(3, 0));
            yield return null;

            var view = r.ViewOf(s);
            Assert.IsNotNull(view, "every segment gets a view");
            Assert.AreSame(s, view.Segment);
            Assert.IsTrue(view.gameObject.activeSelf, "the view is live, unlike the prefab template");
            Assert.AreEqual(1, model.Items.Count, "the view is registered for selection");
        }

        [UnityTest]
        public IEnumerator Sync_IsIdempotent()
        {
            var (r, model) = MakeRig();
            Draw(r, P(0, 0), P(3, 0));
            r.Sync();
            r.Sync();
            yield return null;

            Assert.AreEqual(1, model.Items.Count, "syncing again must not duplicate views");
        }

        [UnityTest]
        public IEnumerator SplitForTJunction_ProducesThreeViews()
        {
            var (r, model) = MakeRig();
            var through = Draw(r, P(0, 0), P(4, 0));

            // tee into the middle of it, exactly as the tool does on an edge snap
            var mid = r.Graph.SplitSegmentAt(through, P(2, 0));
            var stem = r.Graph.AddSegment(mid, r.Graph.SnapOrCreateNode(P(2, 3)));
            stem.Thickness = 0.2f; stem.Height = 2.7f; stem.SideSign = 1f;
            r.Sync();
            yield return null;

            Assert.AreEqual(3, mid.Degree, "a real T-junction");
            Assert.AreEqual(3, r.Graph.Segments.Count);
            Assert.AreEqual(3, model.Items.Count, "two halves plus the stem, all selectable");
            foreach (var s in r.Graph.Segments)
                Assert.IsNotNull(r.ViewOf(s), $"segment {s.Id} has no view");
        }

        [UnityTest]
        public IEnumerator RemoveSegment_DropsTheViewAndUnregisters()
        {
            var (r, model) = MakeRig();
            var s = Draw(r, P(0, 0), P(3, 0));
            var view = r.ViewOf(s);
            yield return null;

            r.RemoveSegment(s);
            yield return null;

            Assert.IsNull(r.ViewOf(s), "the view is gone with its segment");
            Assert.AreEqual(0, model.Items.Count, "and it is no longer selectable");
            Assert.AreEqual(0, r.Graph.Segments.Count);
        }

        [UnityTest]
        public IEnumerator MovingAWall_RebuildsItsNeighbour()
        {
            // the shared-node promise: drag one wall, the wall attached to it follows
            var (r, _) = MakeRig();
            var ab = Draw(r, P(0, 0), P(2, 0));
            var bc = Draw(r, P(2, 0), P(2, 2));
            var corner = ab.B;
            Assert.AreSame(corner, bc.A, "precondition: they share the corner node");

            var viewBc = r.ViewOf(bc);
            Vector3 beforeBc = viewBc.GetComponent<MeshFilter>().sharedMesh.bounds.center;
            yield return null;

            r.ViewOf(ab).MoveBy(new Vector3(0f, 0f, 1f));
            yield return null;

            Assert.AreEqual(P(2, 1), corner.Position, "the shared node moved");
            Vector3 afterBc = viewBc.GetComponent<MeshFilter>().sharedMesh.bounds.center;
            Assert.AreNotEqual(beforeBc, afterBc, "the neighbour's mesh was rebuilt, not left stale");
        }

        [UnityTest]
        public IEnumerator ViewStaysPickable_AfterRebuild()
        {
            var (r, model) = MakeRig();
            var s = Draw(r, P(0, 0), P(4, 0));
            var view = r.ViewOf(s);
            view.gameObject.layer = 6;                       // Selectable layer, as the prefab has
            yield return new WaitForFixedUpdate();

            r.RebuildSegment(s);
            Physics.SyncTransforms();
            yield return new WaitForFixedUpdate();

            var ray = new Ray(new Vector3(2f, 1f, -5f), Vector3.forward);
            Assert.IsTrue(model.TryPick(ray, out var hit, out _), "a rebuilt wall must stay pickable");
            Assert.AreSame(view.GetComponent<Selectable>(), hit);
        }
    }
}
