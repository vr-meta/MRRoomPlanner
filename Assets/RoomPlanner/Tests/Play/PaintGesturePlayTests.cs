using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using RoomPlanner.Core;
using RoomPlanner.Editing;
using RoomPlanner.Measure;
using RoomPlanner.Tools;

namespace RoomPlanner.Tests.Play
{
    /// <summary>
    /// Paint gesture through the REAL PaintController.Tick (#61): aim at a slab, pull
    /// the trigger — one undoable PaintCommand; a miss paints nothing. Scripted input
    /// frame by frame via the virtual MeasureInput/PointerProvider seams.
    /// </summary>
    public class PaintGesturePlayTests
    {
        private class FakeInput : MeasureInput
        {
            public bool Pressed, Clear;
            public override bool ConfirmPressed() => Pressed;
            public override bool ConfirmHeld() => false;
            public override bool ClearPressed() => Clear;
            public override void Pulse(float amplitude = 0.5f, float duration = 0.06f) { }
            public override void PulseLeft(float amplitude = 0.5f, float duration = 0.02f) { }
        }

        private class FakePointer : PointerProvider
        {
            public Ray Ray;
            public override Ray GetRay() => Ray;
        }

        private readonly List<GameObject> _spawned = new();

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
                .GetField(field, System.Reflection.BindingFlags.Instance
                                 | System.Reflection.BindingFlags.NonPublic)
                .SetValue(target, value);

        private (PaintController paint, FakeInput input, FakePointer pointer,
                 SceneModel model, Selectable slabSel) MakeRig()
        {
            var rig = Track(new GameObject("Rig"));
            var model = rig.AddComponent<SceneModel>();

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

            var host = Track(new GameObject("Paint"));
            var input = host.AddComponent<FakeInput>();
            var pointer = host.AddComponent<FakePointer>();
            var paint = host.AddComponent<PaintController>();
            SetField(paint, "input", input);
            SetField(paint, "pointer", pointer);
            SetField(paint, "sceneModel", model);
            return (paint, input, pointer, model, sel);
        }

        [UnityTest]
        public IEnumerator TriggerOnSlab_PaintsOnce_UndoRestores()
        {
            var (paint, input, pointer, model, sel) = MakeRig();
            yield return null;
            Physics.SyncTransforms();

            pointer.Ray = new Ray(new Vector3(1f, 2f, 1f), Vector3.down);
            input.Pressed = true;
            paint.Tick(false);
            input.Pressed = false;
            paint.Tick(false);

            Assert.IsTrue(sel.IsPainted, "the trigger painted the slab");
            Assert.AreEqual(1, model.History.UndoCount, "one click = one PaintCommand");

            model.History.Undo();
            Assert.IsFalse(sel.IsPainted, "undo returns the original look");
        }

        [UnityTest]
        public IEnumerator TriggerOnEmptySpace_PaintsNothing()
        {
            var (paint, input, pointer, model, sel) = MakeRig();
            yield return null;
            Physics.SyncTransforms();

            pointer.Ray = new Ray(new Vector3(10f, 2f, 10f), Vector3.down);   // misses the slab
            input.Pressed = true;
            paint.Tick(false);
            input.Pressed = false;

            Assert.IsFalse(sel.IsPainted, "a miss must not paint");
            Assert.AreEqual(0, model.History.UndoCount, "no command recorded on a miss");
        }

        [UnityTest]
        public IEnumerator BlockedTick_IgnoresTheTrigger()
        {
            // pointer over the menu (blocked=true) — the trigger belongs to the UI then
            var (paint, input, pointer, model, sel) = MakeRig();
            yield return null;
            Physics.SyncTransforms();

            pointer.Ray = new Ray(new Vector3(1f, 2f, 1f), Vector3.down);
            input.Pressed = true;
            paint.Tick(true);
            input.Pressed = false;

            Assert.IsFalse(sel.IsPainted, "a blocked tick must not paint");
            Assert.AreEqual(0, model.History.UndoCount);
        }
    }
}
