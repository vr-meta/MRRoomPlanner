using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using RoomPlanner.Core;
using RoomPlanner.Editing;
using RoomPlanner.Measure;
using RoomPlanner.Tools;
using RoomPlanner.Walls;

namespace RoomPlanner.Tests.Play
{
    /// <summary>
    /// Navigation gestures through the REAL ToolManager.Update (#61): the A-tap
    /// teleports the model (one undoable TeleportCommand), and the X/Y buttons run
    /// global undo/redo. Scripted frames via the virtual MeasureInput seams.
    /// </summary>
    public class NavGesturePlayTests
    {
        private class FakeInput : MeasureInput
        {
            public bool TapA, HoldA, Undo, Redo;
            public override bool ConfirmPressed() => false;
            public override bool ConfirmHeld() => false;
            public override bool ClearPressed() => false;
            public override bool TeleportPressed() => TapA;
            public override bool TeleportHeld() => HoldA;
            public override bool UndoPressed() => Undo;
            public override bool RedoPressed() => Redo;
            public override void Pulse(float amplitude = 0.5f, float duration = 0.06f) { }
            public override void PulseLeft(float amplitude = 0.5f, float duration = 0.02f) { }
        }

        private class FakePointer : PointerProvider
        {
            public Ray Ray;
            public override Ray GetRay() => Ray;
        }

        private readonly List<GameObject> _spawned = new();
        private MethodInfo _update;

        private GameObject Track(GameObject go)
        {
            _spawned.Add(go);
            return go;
        }

        [TearDown]
        public void Cleanup()
        {
            foreach (var go in _spawned) if (go != null) Object.DestroyImmediate(go);
            _spawned.Clear();
        }

        private static void SetField(object target, string field, object value) =>
            target.GetType()
                .GetField(field, BindingFlags.Instance | BindingFlags.NonPublic)
                .SetValue(target, value);

        private (ToolManager manager, FakeInput input, FakePointer pointer,
                 SceneModel model, RoomPlanner.Floors.Floor slab) MakeRig()
        {
            var rig = Track(new GameObject("Rig"));
            var model = rig.AddComponent<SceneModel>();

            var template = Track(new GameObject("WallTemplate"));
            template.SetActive(false);
            template.AddComponent<MeshFilter>();
            template.AddComponent<MeshRenderer>();
            var prefab = template.AddComponent<Wall>();
            template.AddComponent<Selectable>();
            var walls = rig.AddComponent<WallGraphRenderer>();
            walls.Configure(prefab, model);

            var manager = rig.AddComponent<ToolManager>();
            manager.enabled = false;   // frames are driven manually
            var input = rig.AddComponent<FakeInput>();
            var pointer = rig.AddComponent<FakePointer>();
            SetField(manager, "input", input);
            SetField(manager, "pointer", pointer);
            SetField(manager, "sceneModel", model);
            _update = typeof(ToolManager).GetMethod("Update",
                BindingFlags.Instance | BindingFlags.NonPublic);

            var slabGo = Track(new GameObject("Slab"));
            slabGo.AddComponent<MeshFilter>();
            slabGo.AddComponent<MeshRenderer>();
            var slab = slabGo.AddComponent<RoomPlanner.Floors.Floor>();
            slab.BuildOutline(new List<Vector3>
            {
                new(0f, 0f, 0f), new(2f, 0f, 0f), new(2f, 0f, 2f), new(0f, 0f, 2f),
            }, 0f, 0.2f, 5f, 0f, 0f, 0f);
            var sel = slabGo.AddComponent<Selectable>();
            model.Register(sel);

            // the head the teleport brings the aimed point under
            var camGo = Track(new GameObject("Head") { tag = "MainCamera" });
            camGo.AddComponent<Camera>();
            camGo.transform.position = new Vector3(5f, 1.7f, 5f);

            return (manager, input, pointer, model, slab);
        }

        private void Frame(ToolManager manager) => _update.Invoke(manager, null);

        [UnityTest]
        public IEnumerator ATap_TeleportsTheModel_XUndoes_YRedoes()
        {
            var (manager, input, pointer, model, slab) = MakeRig();
            yield return null;
            Physics.SyncTransforms();

            pointer.Ray = new Ray(new Vector3(1f, 2f, 1f), Vector3.down);
            Vector3 before = slab.Outline[0];      // slabs teleport by outline data, not transform

            input.TapA = true; input.HoldA = false;
            Frame(manager);                        // press+release inside the tap window
            input.TapA = false;

            Assert.AreEqual(1, model.History.UndoCount, "the tap recorded one TeleportCommand");
            Vector3 after = slab.Outline[0];
            Assert.Greater((after - before).magnitude, 1f,
                "the model moved toward the head (aimed point goes under the feet)");

            input.Undo = true;
            Frame(manager);                        // X — global undo
            input.Undo = false;
            Assert.AreEqual(before, slab.Outline[0], "X undid the teleport");

            input.Redo = true;
            Frame(manager);                        // Y — global redo
            input.Redo = false;
            Assert.AreEqual(after, slab.Outline[0], "Y re-applied the teleport");
        }

        [UnityTest]
        public IEnumerator ATapOnEmptySpace_DoesNothing()
        {
            var (manager, input, pointer, model, slab) = MakeRig();
            yield return null;
            Physics.SyncTransforms();

            pointer.Ray = new Ray(new Vector3(10f, 2f, 10f), Vector3.down);   // no slab there
            input.TapA = true; input.HoldA = false;
            Frame(manager);
            input.TapA = false;

            Assert.AreEqual(0, model.History.UndoCount, "no target — no teleport");
        }
    }
}
