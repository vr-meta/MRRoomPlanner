using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using RoomPlanner.Core;
using RoomPlanner.Editing;

namespace RoomPlanner.Tests.Play
{
    /// <summary>
    /// PlayMode coverage for the selection model that the EditMode (Core-only) suite can't
    /// reach: SceneModel ray-picking against real colliders, and the Move/Delete commands
    /// driven through EditHistory on a live ISelectable. Uses a test double (FakeSelectable)
    /// so no Meta/TMP runtime is needed.
    /// </summary>
    public class SelectionModelPlayTests
    {
        // ---- test double ----
        private class FakeSelectable : MonoBehaviour, ISelectable
        {
            public string Id { get; set; }
            public SelectableKind Kind { get; set; } = SelectableKind.Wall;
            public Transform Transform => transform;
            public Bounds WorldBounds => new Bounds(transform.position, Vector3.one);
            public bool IsHidden => !gameObject.activeSelf;
            public bool IsAlive => this != null;   // Unity fake-null via the typed `this`

            public Vector3 Moved;           // accumulated MoveBy deltas
            public int HighlightChanges;
            public HighlightState LastHighlight = HighlightState.None;

            public void SetHighlight(HighlightState state) { LastHighlight = state; HighlightChanges++; }
            public void SetHidden(bool hidden) => gameObject.SetActive(!hidden);
            public void MoveBy(Vector3 delta) { Moved += delta; transform.position += delta; }
            public string Describe() => "fake";
            public RoomPlanner.Core.SettingsSchema GetSettings() => null;   // no per-instance rows
        }

        private readonly List<GameObject> _spawned = new();

        private FakeSelectable MakePickable(string name, Vector3 pos, bool withSelectable = true)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);  // brings a BoxCollider
            go.name = name;
            go.transform.position = pos;
            _spawned.Add(go);
            return withSelectable ? go.AddComponent<FakeSelectable>() : null;
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

        // ---- SceneModel.TryPick (needs physics → PlayMode) ----

        [UnityTest]
        public IEnumerator TryPick_PicksNearestSelectable()
        {
            var model = MakeModel();
            var near = MakePickable("Near", new Vector3(0f, 0f, 2f));
            var far = MakePickable("Far", new Vector3(0f, 0f, 6f));
            model.Register(near);
            model.Register(far);
            Physics.SyncTransforms();
            yield return new WaitForFixedUpdate();

            var ray = new Ray(new Vector3(0f, 0f, -1f), Vector3.forward);
            Assert.IsTrue(model.TryPick(ray, out var hit, out _));
            Assert.AreSame(near, hit);
        }

        [UnityTest]
        public IEnumerator TryPick_SkipsHiddenObject()
        {
            var model = MakeModel();
            var near = MakePickable("Near", new Vector3(0f, 0f, 2f));
            var far = MakePickable("Far", new Vector3(0f, 0f, 6f));
            model.Register(near);
            model.Register(far);
            near.SetHidden(true);                 // deleted → inactive, must not be picked
            Physics.SyncTransforms();
            yield return new WaitForFixedUpdate();

            var ray = new Ray(new Vector3(0f, 0f, -1f), Vector3.forward);
            Assert.IsTrue(model.TryPick(ray, out var hit, out _));
            Assert.AreSame(far, hit);
        }

        [UnityTest]
        public IEnumerator TryPick_IgnoresColliderWithoutSelectable()
        {
            var model = MakeModel();
            MakePickable("PlainWall", new Vector3(0f, 0f, 2f), withSelectable: false); // e.g. scan mesh
            var behind = MakePickable("Selectable", new Vector3(0f, 0f, 6f));
            model.Register(behind);
            Physics.SyncTransforms();
            yield return new WaitForFixedUpdate();

            var ray = new Ray(new Vector3(0f, 0f, -1f), Vector3.forward);
            Assert.IsTrue(model.TryPick(ray, out var hit, out _));
            Assert.AreSame(behind, hit);
        }

        [UnityTest]
        public IEnumerator TryPick_ReturnsFalse_WhenNothingHit()
        {
            var model = MakeModel();
            MakePickable("Off", new Vector3(100f, 0f, 0f));
            Physics.SyncTransforms();
            yield return new WaitForFixedUpdate();

            var ray = new Ray(Vector3.zero, Vector3.up);   // points at nothing
            Assert.IsFalse(model.TryPick(ray, out _, out _));
        }

        // ---- commands via EditHistory (no physics needed) ----

        [Test]
        public void MoveCommand_Execute_MovesTarget_Undo_Reverts()
        {
            var target = MakePickable("T", Vector3.zero);
            var history = new EditHistory();
            var delta = new Vector3(1f, 0f, 2f);

            history.Execute(new MoveCommand(target, delta));
            Assert.AreEqual(delta, target.Moved);

            history.Undo();
            Assert.AreEqual(Vector3.zero, target.Moved);   // delta + (-delta)

            history.Redo();
            Assert.AreEqual(delta, target.Moved);
        }

        [Test]
        public void DeleteCommand_Execute_Hides_Undo_Restores()
        {
            var target = MakePickable("T", Vector3.zero);
            var history = new EditHistory();

            history.Execute(new DeleteCommand(target));
            Assert.IsTrue(target.IsHidden);

            history.Undo();
            Assert.IsFalse(target.IsHidden);
        }

        [Test]
        public void DeleteCommand_Redo_HidesAgain()
        {
            var target = MakePickable("T", Vector3.zero);
            var history = new EditHistory();

            history.Execute(new DeleteCommand(target));
            history.Undo();
            Assert.IsFalse(target.IsHidden);

            history.Redo();
            Assert.IsTrue(target.IsHidden, "redo must re-hide the object");
        }

        // ---- lifetime: destroyed targets must never crash the history ----

        [UnityTest]
        public IEnumerator Undo_AfterTargetDestroyed_IsSafeNoOp()
        {
            var model = MakeModel();
            var target = MakePickable("T", Vector3.zero);
            model.Register(target);
            model.History.Execute(new MoveCommand(target, Vector3.right));

            // Hard-destroy WITHOUT unregistering — the worst case (a missed pairing).
            Object.Destroy(target.gameObject);
            yield return null;   // destroy applies at end of frame

            Assert.DoesNotThrow(() => model.History.Undo(), "IsAlive guard must absorb the dead target");
            Assert.DoesNotThrow(() => model.History.Redo());
        }

        [Test]
        public void Unregister_PurgesTargetCommandsFromHistory()
        {
            var model = MakeModel();
            var a = MakePickable("A", Vector3.zero);
            var b = MakePickable("B", Vector3.one);
            model.Register(a);
            model.Register(b);
            model.History.Execute(new MoveCommand(a, Vector3.right));
            model.History.Execute(new MoveCommand(b, Vector3.forward));
            model.History.Undo();   // b's move sits on the redo stack
            Assert.AreEqual(1, model.History.UndoCount);
            Assert.AreEqual(1, model.History.RedoCount);

            model.Unregister(a);

            Assert.AreEqual(0, model.History.UndoCount, "a's command purged from undo");
            Assert.AreEqual(1, model.History.RedoCount, "b's command survives on redo");
            model.History.Redo();
            Assert.AreEqual(Vector3.forward, b.Moved, "surviving command still replays");
        }

        [Test]
        public void Register_AssignsUniqueIds_AndTracksItems()
        {
            var model = MakeModel();
            var a = MakePickable("A", Vector3.zero);
            var b = MakePickable("B", Vector3.zero);
            model.Register(a);
            model.Register(b);
            model.Register(a);   // duplicate ignored

            Assert.AreEqual(2, model.Items.Count);
            Assert.IsNotEmpty(a.Id);
            Assert.AreNotEqual(a.Id, b.Id);

            model.Unregister(a);
            Assert.AreEqual(1, model.Items.Count);
        }
    }
}
