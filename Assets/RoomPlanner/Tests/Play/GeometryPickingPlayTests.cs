using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using RoomPlanner.Core;
using RoomPlanner.Editing;
using RoomPlanner.Walls;
using RoomPlanner.Floors;

namespace RoomPlanner.Tests.Play
{
    /// <summary>
    /// End-to-end PlayMode coverage on REAL procedural geometry: a wall/floor builds its own
    /// runtime MeshCollider, SceneModel ray-picks it, and Move/Delete commands drive the
    /// geometry so the collider tracks the change. This is the loop the Select tool relies on
    /// (pick -> drag -> undo) and the class of bug (collider not following geometry) unit tests
    /// on pure math can't catch.
    /// </summary>
    public class GeometryPickingPlayTests
    {
        // ISelectable adapter mirroring the real Selectable's delegation, but reachable from the
        // test assembly (the production Selectable lives in Assembly-CSharp).
        private class GeoProbe : MonoBehaviour, ISelectable
        {
            public Wall Wall;
            public Floor Floor;

            public string Id { get; set; }
            public SelectableKind Kind => Wall != null ? SelectableKind.Wall : SelectableKind.Floor;
            public Transform Transform => transform;
            public Bounds WorldBounds
            {
                get { var r = GetComponent<Renderer>(); return r != null ? r.bounds : new Bounds(transform.position, Vector3.one); }
            }
            public bool IsHidden => !gameObject.activeSelf;

            public void SetHighlight(HighlightState state) { }
            public void SetHidden(bool hidden) => gameObject.SetActive(!hidden);
            public void MoveBy(Vector3 delta)
            {
                if (Wall != null) Wall.MoveBy(delta);
                else if (Floor != null) Floor.MoveBy(delta);
            }
            public string Describe() => "geo";
        }

        private readonly List<GameObject> _spawned = new();
        private static readonly Ray ThroughX1 = new Ray(new Vector3(1f, 1f, -5f), Vector3.forward);

        private GeoProbe SpawnWall(Vector3 a, Vector3 b)
        {
            var go = new GameObject("Wall");
            _spawned.Add(go);
            var w = go.AddComponent<Wall>();
            var interior = new Vector3((a.x + b.x) * 0.5f, 0f, Mathf.Min(a.z, b.z) - 1f); // interior on -Z
            w.Build(new List<Vector3> { a, b }, 0.2f, 2.7f, WallOffsetMode.Outer, WallJoin.Miter, interior);
            var p = go.AddComponent<GeoProbe>();
            p.Wall = w;
            return p;
        }

        private GeoProbe SpawnFloor(Vector3 a, Vector3 b, float level)
        {
            var go = new GameObject("Floor");
            _spawned.Add(go);
            var f = go.AddComponent<Floor>();
            f.Build(a, b, level, 0.2f, 5f, 0f, 0f);
            var p = go.AddComponent<GeoProbe>();
            p.Floor = f;
            return p;
        }

        private SceneModel MakeModel()
        {
            var go = new GameObject("SceneModel");
            _spawned.Add(go);
            return go.AddComponent<SceneModel>();
        }

        [TearDown]
        public void Cleanup()
        {
            foreach (var go in _spawned) if (go != null) Object.Destroy(go);
            _spawned.Clear();
        }

        [UnityTest]
        public IEnumerator TryPick_HitsRealWallCollider()
        {
            var model = MakeModel();
            var wall = SpawnWall(new Vector3(0, 0, 2), new Vector3(2, 0, 2)); // spans x[0..2], z[2..2.2]
            model.Register(wall);
            Physics.SyncTransforms();
            yield return new WaitForFixedUpdate();

            Assert.IsTrue(model.TryPick(ThroughX1, out var hit, out var pt), "ray should hit the wall's runtime MeshCollider");
            Assert.AreSame(wall, hit);
            Assert.AreEqual(2f, pt.z, 0.05f, "hit point sits on the wall's front face");
        }

        [UnityTest]
        public IEnumerator TryPick_HitsRealFloorFromAbove()
        {
            var model = MakeModel();
            var floor = SpawnFloor(new Vector3(0, 0, 0), new Vector3(4, 0, 3), 0f);
            model.Register(floor);
            Physics.SyncTransforms();
            yield return new WaitForFixedUpdate();

            var down = new Ray(new Vector3(1f, 5f, 1f), Vector3.down);
            Assert.IsTrue(model.TryPick(down, out var hit, out var pt));
            Assert.AreSame(floor, hit);
            Assert.AreEqual(0f, pt.y, 0.05f, "hit lands on the slab top at the level");
        }

        [UnityTest]
        public IEnumerator TryPick_PicksNearerRealWall()
        {
            var model = MakeModel();
            var near = SpawnWall(new Vector3(0, 0, 2), new Vector3(2, 0, 2));
            var far = SpawnWall(new Vector3(0, 0, 6), new Vector3(2, 0, 6));
            model.Register(near);
            model.Register(far);
            Physics.SyncTransforms();
            yield return new WaitForFixedUpdate();

            Assert.IsTrue(model.TryPick(ThroughX1, out var hit, out _));
            Assert.AreSame(near, hit);
        }

        [UnityTest]
        public IEnumerator WallCollider_FollowsMoveBy()
        {
            var model = MakeModel();
            var wall = SpawnWall(new Vector3(0, 0, 2), new Vector3(2, 0, 2));
            model.Register(wall);
            Physics.SyncTransforms();
            yield return new WaitForFixedUpdate();
            Assert.IsTrue(model.TryPick(ThroughX1, out _, out _), "hits before the move");

            wall.MoveBy(new Vector3(20f, 0f, 0f));   // slide far along +X
            Physics.SyncTransforms();
            yield return new WaitForFixedUpdate();

            Assert.IsFalse(model.TryPick(ThroughX1, out _, out _), "old ray misses: collider moved with the mesh");
            var shifted = new Ray(new Vector3(21f, 1f, -5f), Vector3.forward);
            Assert.IsTrue(model.TryPick(shifted, out _, out _), "ray at the new location hits");
        }

        [UnityTest]
        public IEnumerator MoveCommand_OnWall_MovesCollider_AndUndoRestores()
        {
            var model = MakeModel();
            var wall = SpawnWall(new Vector3(0, 0, 2), new Vector3(2, 0, 2));
            model.Register(wall);
            var history = new EditHistory();
            Physics.SyncTransforms();
            yield return new WaitForFixedUpdate();

            history.Execute(new MoveCommand(wall, new Vector3(20f, 0f, 0f)));
            Physics.SyncTransforms();
            yield return new WaitForFixedUpdate();
            Assert.IsFalse(model.TryPick(ThroughX1, out _, out _), "moved away, original ray misses");

            history.Undo();
            Physics.SyncTransforms();
            yield return new WaitForFixedUpdate();
            Assert.IsTrue(model.TryPick(ThroughX1, out var hit, out _), "undo brings it back onto the original ray");
            Assert.AreSame(wall, hit);
        }

        [UnityTest]
        public IEnumerator DeleteCommand_OnWall_RemovesFromPicking_AndUndoRestores()
        {
            var model = MakeModel();
            var wall = SpawnWall(new Vector3(0, 0, 2), new Vector3(2, 0, 2));
            model.Register(wall);
            var history = new EditHistory();
            Physics.SyncTransforms();
            yield return new WaitForFixedUpdate();
            Assert.IsTrue(model.TryPick(ThroughX1, out _, out _));

            history.Execute(new DeleteCommand(wall));
            Physics.SyncTransforms();
            yield return new WaitForFixedUpdate();
            Assert.IsFalse(model.TryPick(ThroughX1, out _, out _), "deleted (inactive) wall is not pickable");

            history.Undo();
            Physics.SyncTransforms();
            yield return new WaitForFixedUpdate();
            Assert.IsTrue(model.TryPick(ThroughX1, out _, out _), "undo restores it");
        }
    }
}
